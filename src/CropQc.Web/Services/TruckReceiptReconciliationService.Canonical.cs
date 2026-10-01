using System.Collections.Immutable;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public sealed partial class TruckReceiptReconciliationService
{
    private Task<string?> EditTransferCanonicalAsync(TransitEditForm form, CancellationToken ct) => CanonicalTruckWriteAsync(async actor =>
    {
        await RequireAccessAsync(ApplicationAreas.Transfers, PageAccessLevel.Edit, ct);
        Require(form.Bins > 0 && !string.IsNullOrWhiteSpace(form.Reason), "A positive quantity and edit reason are required.");
        var kind = form.DispatchMovementId.HasValue ? InventoryCommandKind.ReturnTransitAllocation : InventoryCommandKind.TransferEdit;
        var key = $"transit-edit:{form.TransferId}:v{form.TransferVersion}";
        var submission = JsonSerializer.Serialize(form);
        var replay = await CanonicalApplicationReplay.TryAsync(db, canonicalCommands!, key, actor.Id, kind, submission, ct);
        if (replay != null) return CanonicalInventoryMessages.Result(replay);
        var transfer = await db.InterCrewTransfers.AsNoTracking().Include(x => x.SourceWarehouse).SingleOrDefaultAsync(x => x.Id == form.TransferId, ct);
        Require(transfer != null && transfer.Status == InterCrewTransferStatuses.InTransit && transfer.ConcurrencyVersion == form.TransferVersion,
            "In-transit transfer changed; reload before editing.");
        Require(TruckReceiptRoutes.RequiresReceiptForGroup(transfer!.SourceWarehouse.Code, transfer.DestinationCustodyGroup), "This load uses the internal workflow.");
        await RequireCrewAsync(actor, TruckReceiptRoutes.Group(transfer.SourceWarehouse.Code) ?? "", ct);
        InventoryCommandLine line;
        InventoryReceivingEvidence? receiptEvidence = null;
        if (form.DispatchMovementId is long movementId)
        {
            var movement = await db.TreatmentLineageMovements.AsNoTracking().SingleOrDefaultAsync(x => x.Id == movementId && x.InterCrewTransferId == transfer.Id, ct);
            Require(movement != null, "Dispatch allocation was not found.");
            var batch = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(
                new(transfer.SourceWarehouseId, [], InventoryCustody.InTransit, transfer.Id), new(AllowedCustody: InventoryCustody.InTransit), time.UtcNow, ct);
            var p = batch.Positions.SingleOrDefault(x => x.Identity.Key == InventoryStatusIdentity.NormalizeLineageKey(movement!.IdentityKey));
            Require(p != null && p.IsOperable, "Transit allocation cannot be proven.");
            line = new(new(p!.Identity, p.Location, p.Watermark.Fingerprint, p.Watermark.Versions), form.Bins, movement!.TreatmentSignatureSnapshot);
            if (transfer.ReceivingReceiptId is long matched)
                receiptEvidence = new(matched, await db.Receipts.Where(x => x.Id == matched).Select(x => x.ConcurrencyVersion).SingleAsync(ct));
        }
        else
        {
            var option = (await inventory.GetInventoryAsync(ct)).SingleOrDefault(x => x.SourceKey == form.SourceKey
                && x.RoomId == transfer.SourceRoomId && x.WarehouseId == transfer.SourceWarehouseId);
            Require(option is { IsAvailable: true } && option.AvailableBins == form.ExpectedAvailableBins && form.Bins <= option.AvailableBins,
                "Source inventory changed or is unavailable. Reload before adding bins.");
            var identity = new InventoryIdentity(option!.CropYear, option.GrowerLotId, option.FruitProfileId, option.LotNumber, option.GrowerNumber,
                option.VarietyCode, option.ProductionType, option.IsOrganic, option.InventoryStatus);
            line = new(new(identity, new(InventoryCustody.Room, option.WarehouseId, option.RoomId, option.Facility, option.Room),
                option.SourceKey[(option.SourceKey.LastIndexOf(':') + 1)..], []), form.Bins, option.TreatmentSignature);
        }
        return CanonicalInventoryMessages.Result(await canonicalCommands!.ExecuteAsync(new(key, kind, actor.Id, time.UtcNow, form.Reason,
            [line], ExpectedTransferVersion: form.TransferVersion, ApplicationIntent: submission, PhysicalParentId: transfer.Id,
            ReceivingEvidence: receiptEvidence, DispatchMovementId: form.DispatchMovementId), ct));
    }, ct);

    private static string CanonicalTruckKey(string operation, TruckReceiptActionForm form) => "truck:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"{operation}:{form.TransferId}:{form.ReceiptId}:{form.TransferVersion}:{form.ReceiptVersion}")))[..48];

    private async Task<string?> CanonicalTruckWriteAsync(Func<User, Task<string?>> write, CancellationToken ct)
    {
        if (!Enabled) return TruckReceiptOptions.DisabledMessage;
        if (canonicalCommands == null) return "Canonical inventory command execution is not configured.";
        try
        {
            var email = Principal.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();
            var actor = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Email == email && x.IsActive, ct);
            Require(actor != null, "An active signed-in user is required.");
            return await write(actor!);
        }
        catch (InvalidOperationException ex) { return ex.Message; }
    }

    private Task<string?> CompleteCanonicalAsync(TruckReceiptActionForm form, CancellationToken ct) => CanonicalTruckWriteAsync(async actor =>
    {
        await RequireAccessAsync(ApplicationAreas.Receipts, PageAccessLevel.Edit, ct);
        var transfer = await db.InterCrewTransfers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == form.TransferId, ct);
        var receipt = await db.Receipts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == form.ReceiptId && !x.IsDeleted, ct);
        Require(transfer != null && receipt is { IsTransferReceipt: true } && transfer.RequiresTruckReceipt, "A matched Truck Receipt transfer is required.");
        await RequireCrewAsync(actor, transfer!.DestinationCustodyGroup, ct);
        var key = CanonicalTruckKey("complete", form);
        var submission = JsonSerializer.Serialize(form);
        var replay = await CanonicalApplicationReplay.TryAsync(db, canonicalCommands!, key, actor.Id, InventoryCommandKind.ReceiveTransfer, submission, ct);
        if (replay != null) return CanonicalInventoryMessages.Result(replay);
        Require(transfer.ReceivingReceiptId == receipt!.Id && transfer.ConcurrencyVersion == form.TransferVersion && receipt.ConcurrencyVersion == form.ReceiptVersion,
            "The matched receipt or transfer changed; reload reconciliation.");
        var batch = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(
            new(transfer.SourceWarehouseId, [], InventoryCustody.InTransit, transfer.Id), new(AllowedCustody: InventoryCustody.InTransit), time.UtcNow, ct);
        Require(batch.Positions.Length > 0 && batch.Positions.All(x => x.IsOperable), "Transit inventory cannot be proven.");
        var lines = batch.Positions.SelectMany(p => CanonicalTreatmentSelections.MovementSlices(p).Select(s => new InventoryCommandLine(
            new(p.Identity, p.Location, p.Watermark.Fingerprint, p.Watermark.Versions), s.Quantity, s.Signature, new(receipt.WarehouseId, receipt.RoomId)))).ToImmutableArray();
        return CanonicalInventoryMessages.Result(await canonicalCommands!.ExecuteAsync(new(key, InventoryCommandKind.ReceiveTransfer, actor.Id, time.UtcNow,
            "Complete reconciled Truck Receipt", lines, ReceivingEvidence: new(receipt.Id, form.ReceiptVersion), ExpectedTransferVersion: form.TransferVersion,
            ApplicationIntent: submission, PhysicalParentId: transfer.Id), ct));
    }, ct);

    private Task<string?> ReopenCanonicalAsync(TruckReceiptActionForm form, CancellationToken ct) => CanonicalTruckWriteAsync(async actor =>
    {
        await RequireAccessAsync(ApplicationAreas.Transfers, PageAccessLevel.Admin, ct);
        Require(!string.IsNullOrWhiteSpace(form.Reason), "A reopen reason is required.");
        var key = CanonicalTruckKey("reopen", form);
        var submission = JsonSerializer.Serialize(form);
        var replay = await CanonicalApplicationReplay.TryAsync(db, canonicalCommands!, key, actor.Id, InventoryCommandKind.ReopenTransfer, submission, ct);
        if (replay != null) return CanonicalInventoryMessages.Result(replay);
        var transfer = await db.InterCrewTransfers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == form.TransferId, ct);
        var receipt = await db.Receipts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == form.ReceiptId && !x.IsDeleted, ct);
        Require(transfer != null && receipt != null && transfer.RequiresTruckReceipt && transfer.ReceivingReceiptId == receipt.Id
            && transfer.ConcurrencyVersion == form.TransferVersion && receipt.ConcurrencyVersion == form.ReceiptVersion,
            "The matched receipt or transfer changed; reload reconciliation.");
        // Unlinking an open match is metadata-only and keeps its existing audited path.
        if (transfer!.Status == InterCrewTransferStatuses.InTransit) return await ReopenLegacyAsync(form, ct);
        Require(transfer.Status == InterCrewTransferStatuses.Received && transfer.DestinationRoomId != null, "The transfer is not completed.");
        var allMoves = await db.TreatmentLineageMovements.AsNoTracking().Where(x => x.InterCrewTransferId == transfer.Id).ToListAsync(ct);
        var receives = allMoves.Where(x => x.MovementType == "InterCrewReceive" && !allMoves.Any(r => r.ReversesTreatmentLineageMovementId == x.Id)).ToArray();
        var batch = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(transfer.DestinationWarehouseId, [transfer.DestinationRoomId!.Value]), new(), time.UtcNow, ct);
        var lines = ImmutableArray.CreateBuilder<InventoryCommandLine>();
        foreach (var group in receives.GroupBy(x => new { Identity = InventoryStatusIdentity.NormalizeLineageKey(x.IdentityKey), x.TreatmentSignatureSnapshot }))
        {
            var p = batch.Positions.SingleOrDefault(x => x.Identity.Key == group.Key.Identity);
            Require(p != null && p.IsOperable, "Received inventory cannot be proven for reopening.");
            lines.Add(new(new(p!.Identity, p.Location, p.Watermark.Fingerprint, p.Watermark.Versions), group.Sum(x => x.BinCount), group.Key.TreatmentSignatureSnapshot));
        }
        return CanonicalInventoryMessages.Result(await canonicalCommands!.ExecuteAsync(new(key, InventoryCommandKind.ReopenTransfer, actor.Id, time.UtcNow,
            form.Reason, lines.ToImmutable(), ReceivingEvidence: new(receipt!.Id, form.ReceiptVersion), ExpectedTransferVersion: form.TransferVersion,
            ApplicationIntent: submission, PhysicalParentId: transfer.Id), ct));
    }, ct);
}
