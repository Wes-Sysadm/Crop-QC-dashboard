using System.Collections.Immutable;
using System.Text.Json;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public sealed partial class OutsideWarehouseTransferService
{
    private async Task<OutsideWarehouseTransferWriteResult> CreateCanonicalAsync(OutsideWarehouseTransferForm form, CancellationToken ct)
    {
        if (canonicalCommands == null) return new(false, false, null, "Canonical inventory command execution is not configured.");
        var actor = await GetActorAsync(ct);
        if (actor == null) return new(false, false, null, "The current active user could not be resolved.");
        var submission = JsonSerializer.Serialize(form);
        static OutsideWarehouseTransferWriteResult Map(InventoryCommandResult r) => new(
            r.Status is InventoryCommandStatus.Committed or InventoryCommandStatus.Replayed,
            r.Status == InventoryCommandStatus.Replayed, r.Effects.FirstOrDefault()?.ParentId, CanonicalInventoryMessages.Result(r));
        var replay = await CanonicalApplicationReplay.TryAsync(dbContext, canonicalCommands, form.OperationKey, actor.Id,
            InventoryCommandKind.OutsideWarehouseTransfer, submission, ct);
        if (replay != null) return Map(replay);
        var option = (await GetInventoryOptionsAsync(ct)).SingleOrDefault(x => x.SourceKey == form.SourceKey);
        if (option == null || !option.IsAvailable || option.AvailableBins != form.ExpectedAvailableBins)
            return new(false, false, null, "Inventory changed; reload and try again.");
        var identity = new InventoryIdentity(option.CropYear, option.GrowerLotId, option.FruitProfileId, option.LotNumber,
            option.GrowerNumber, option.VarietyCode, option.ProductionType, option.IsOrganic, option.InventoryStatus);
        var command = new InventoryCommand(form.OperationKey, InventoryCommandKind.OutsideWarehouseTransfer, actor.Id,
            businessTime.PacificLocalToUtc(form.TransferredAt), "Outside Warehouse transfer",
            [new(new(identity, new(InventoryCustody.Room, option.WarehouseId, option.RoomId, option.Facility, option.Room),
                form.SourceKey[(form.SourceKey.LastIndexOf(':') + 1)..], []), form.BinCount, option.TreatmentSignature)],
            CounterpartyId: form.OutsideWarehouseId, ApplicationIntent: submission, Dispatch: new(Normalize(form.TruckLoadBolNumber), Normalize(form.Notes)));
        return Map(await canonicalCommands.ExecuteAsync(command, ct));
    }

    private async Task<string?> ReverseCanonicalAsync(OutsideWarehouseTransferReversalForm form, int actor, CancellationToken ct)
    {
        if (canonicalCommands == null) return "Canonical inventory command execution is not configured.";
        var submission = JsonSerializer.Serialize(form);
        var replay = await CanonicalApplicationReplay.TryAsync(dbContext, canonicalCommands, form.OperationKey, actor, InventoryCommandKind.Return, submission, ct);
        if (replay != null) return CanonicalInventoryMessages.Result(replay);
        var parent = await dbContext.OutsideWarehouseTransfers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == form.TransferId, ct);
        if (parent == null || parent.IsReversed) return "Transfer is missing or already reversed.";
        var separator = parent.OperationKey.LastIndexOf(':');
        string? originalKey = separator > 0 ? parent.OperationKey[..separator] : null;
        if (!await dbContext.InventoryCommands.AnyAsync(x => x.OperationKey == originalKey, ct)) originalKey = null;
        var batch = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(dbContext)).ResolveAsync(
            new(parent.SourceWarehouseId, [], InventoryCustody.OutsideWarehouse, parent.Id), new(AllowedCustody: InventoryCustody.OutsideWarehouse), businessTime.UtcNow, ct);
        if (batch.Positions.Length == 0 || batch.Positions.Any(x => !x.IsOperable)) return "Outside custody cannot be proven.";
        var lines = batch.Positions.SelectMany(r => CanonicalTreatmentSelections.MovementSlices(r).Select(s =>
            new InventoryCommandLine(new(r.Identity, r.Location, r.Watermark.Fingerprint, r.Watermark.Versions), s.Quantity, s.Signature))).ToImmutableArray();
        var command = new InventoryCommand(form.OperationKey, InventoryCommandKind.Return, actor, businessTime.UtcNow, form.Reason!, lines,
            OriginalOperationKey: originalKey, ApplicationIntent: submission, PhysicalParentId: parent.Id);
        return CanonicalInventoryMessages.Result(await canonicalCommands.ExecuteAsync(command, ct));
    }
}
