using System.Collections.Immutable;
using System.Text.Json;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public sealed partial class InterCrewTransferService
{
    private static InterCrewWriteResult MapCanonical(InventoryCommandResult r) => new(r.Status is InventoryCommandStatus.Committed or InventoryCommandStatus.Replayed,
        r.Status == InventoryCommandStatus.Replayed, r.Effects.FirstOrDefault()?.ParentId, CanonicalInventoryMessages.Result(r));

    private async Task<InterCrewWriteResult> DispatchCanonicalAsync(InterCrewDispatchForm form, CancellationToken ct)
    {
        if (canonicalCommands == null) return Fail("Canonical inventory command execution is not configured.");
        var actor = await GetActorAsync(ct);
        if (actor == null) return Fail("The active operator could not be resolved.");
        var submission = JsonSerializer.Serialize(form);
        var replay = await CanonicalApplicationReplay.TryAsync(dbContext, canonicalCommands, form.OperationKey, actor.Id, InventoryCommandKind.InterCompanyDispatch, submission, ct);
        if (replay != null) return MapCanonical(replay);
        var option = (await inventoryProvider.GetInventoryAsync(ct)).SingleOrDefault(x => x.SourceKey == form.SourceKey
            && x.WarehouseId == form.SourceWarehouseId && x.RoomId == form.SourceRoomId);
        if (option == null || !option.IsAvailable || option.AvailableBins != form.ExpectedAvailableBins) return Fail("Inventory changed; reload and try again.");
        if (!AllowedDestinationGroups(option.Facility).Contains(form.DestinationCustodyGroup, StringComparer.Ordinal)) return Fail("Use an internal room transfer for this destination.");
        var identity = new InventoryIdentity(option.CropYear, option.GrowerLotId, option.FruitProfileId, option.LotNumber, option.GrowerNumber,
            option.VarietyCode, option.ProductionType, option.IsOrganic, option.InventoryStatus);
        return MapCanonical(await canonicalCommands.ExecuteAsync(new(form.OperationKey, InventoryCommandKind.InterCompanyDispatch, actor.Id,
            businessTime.PacificLocalToUtc(form.LoadedAt), "Inter-crew dispatch", [new(new(identity,
                new(InventoryCustody.Room, option.WarehouseId, option.RoomId, option.Facility, option.Room), form.SourceKey[(form.SourceKey.LastIndexOf(':') + 1)..], []),
                form.BinsLoaded, option.TreatmentSignature)], CustodyGroup: form.DestinationCustodyGroup, ApplicationIntent: submission,
            Dispatch: new(Normalize(form.TruckLoadBolNumber), Normalize(form.Notes), truckReceiptOptions?.Enabled == true)), ct));
    }

    private async Task<ImmutableArray<InventoryCommandLine>> TransitLinesAsync(InterCrewTransfer transfer, InventoryCommandDestination? destination, CancellationToken ct)
    {
        var batch = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(dbContext)).ResolveAsync(
            new(transfer.SourceWarehouseId, [], InventoryCustody.InTransit, transfer.Id), new(AllowedCustody: InventoryCustody.InTransit), businessTime.UtcNow, ct);
        if (batch.Positions.Any(x => !x.IsOperable)) return [];
        return batch.Positions.SelectMany(p => CanonicalTreatmentSelections.MovementSlices(p).Select(s => new InventoryCommandLine(
            new(p.Identity, p.Location, p.Watermark.Fingerprint, p.Watermark.Versions), s.Quantity, s.Signature, destination))).ToImmutableArray();
    }

    private async Task<InterCrewWriteResult> ReceiveCanonicalAsync(InterCrewReceiveForm form, User actor, bool admin, CancellationToken ct)
    {
        if (canonicalCommands == null) return Fail("Canonical inventory command execution is not configured.");
        var submission = JsonSerializer.Serialize(form);
        var replay = await CanonicalApplicationReplay.TryAsync(dbContext, canonicalCommands, form.OperationKey, actor.Id, InventoryCommandKind.ReceiveTransfer, submission, ct);
        if (replay != null) return MapCanonical(replay);
        var transfer = await dbContext.InterCrewTransfers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == form.TransferId, ct);
        if (transfer == null || transfer.Status != InterCrewTransferStatuses.InTransit) return Fail("The in-transit transfer was not found.");
        if (transfer.RequiresTruckReceipt || transfer.ReceivingReceiptId != null) return Fail("Match and complete the Truck Receipt for this transfer.");
        if (!CanAccessGroup(CustodyGroupForUser(actor), admin, transfer.DestinationCustodyGroup)) return Fail("This load belongs to another receiving crew.");
        if (form.BinsReceived != transfer.BinsLoaded) return Fail("Received quantity must match proven transit stock; reconcile any variance before receiving.");
        var room = await dbContext.Rooms.AsNoTracking().Include(x => x.Warehouse).SingleOrDefaultAsync(x => x.Id == form.DestinationRoomId && x.IsActive, ct);
        if (room == null || !TransferCustodyGroups.ContainsWarehouse(transfer.DestinationCustodyGroup, room.Warehouse.Code) || room.Warehouse.Code == "McDougall")
            return Fail("Select a destination room belonging to the receiving crew.");
        return MapCanonical(await canonicalCommands.ExecuteAsync(new(form.OperationKey, InventoryCommandKind.ReceiveTransfer, actor.Id,
            businessTime.PacificLocalToUtc(form.ReceivedAt), form.Note ?? "Receive legacy inter-crew transfer",
            await TransitLinesAsync(transfer, new(room.WarehouseId, room.Id), ct), ExpectedTransferVersion: transfer.ConcurrencyVersion,
            ApplicationIntent: submission, PhysicalParentId: transfer.Id), ct));
    }

    private async Task<string?> ReverseCanonicalAsync(InterCrewReversalForm form, int actor, CancellationToken ct)
    {
        if (canonicalCommands == null) return "Canonical inventory command execution is not configured.";
        var submission = JsonSerializer.Serialize(form);
        var replay = await CanonicalApplicationReplay.TryAsync(dbContext, canonicalCommands, form.OperationKey, actor, InventoryCommandKind.Return, submission, ct);
        if (replay != null) return CanonicalInventoryMessages.Result(replay);
        var transfer = await dbContext.InterCrewTransfers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == form.TransferId, ct);
        if (transfer == null || transfer.Status == InterCrewTransferStatuses.Reversed) return "Transfer is missing or already reversed.";
        if (transfer.RequiresTruckReceipt || transfer.ReceivingReceiptId != null) return "Use Truck Receipt reopen or pending allocation return.";
        ImmutableArray<InventoryCommandLine> lines;
        if (transfer.Status == InterCrewTransferStatuses.InTransit) lines = await TransitLinesAsync(transfer, null, ct);
        else
        {
            var movements = await dbContext.TreatmentLineageMovements.AsNoTracking().Where(x => x.InterCrewTransferId == transfer.Id).ToListAsync(ct);
            var receives = movements.Where(x => x.MovementType == "InterCrewReceive" && !movements.Any(r => r.ReversesTreatmentLineageMovementId == x.Id));
            var batch = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(dbContext)).ResolveAsync(
                new(transfer.DestinationWarehouseId, [transfer.DestinationRoomId!.Value]), new(), businessTime.UtcNow, ct);
            var builder = ImmutableArray.CreateBuilder<InventoryCommandLine>();
            foreach (var g in receives.GroupBy(x => new { Identity = InventoryStatusIdentity.NormalizeLineageKey(x.IdentityKey), x.TreatmentSignatureSnapshot }))
            {
                var p = batch.Positions.SingleOrDefault(x => x.Identity.Key == g.Key.Identity);
                if (p == null || !p.IsOperable) return "Destination inventory cannot be proven for return.";
                builder.Add(new(new(p.Identity, p.Location, p.Watermark.Fingerprint, p.Watermark.Versions), g.Sum(x => x.BinCount), g.Key.TreatmentSignatureSnapshot));
            }
            lines = builder.ToImmutable();
        }
        return CanonicalInventoryMessages.Result(await canonicalCommands.ExecuteAsync(new(form.OperationKey, InventoryCommandKind.Return, actor,
            businessTime.UtcNow, form.Reason!, lines, ExpectedTransferVersion: transfer.ConcurrencyVersion, ApplicationIntent: submission, PhysicalParentId: transfer.Id), ct));
    }
}
