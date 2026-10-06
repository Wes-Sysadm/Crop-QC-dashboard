using System.Text.Json;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Models;

namespace CropQc.Web.Services;

public sealed partial class RoomInventoryLossService
{
    private async Task<string?> CreateCanonicalLossAsync(RoomInventoryLossForm form, int actor, CancellationToken ct)
    {
        if (canonicalCommands == null) return "Canonical inventory command execution is not configured.";
        if (form.BinCount <= 0 || form.Notes?.Length > 1000) return "Enter a positive dropped-bin quantity and notes under 1000 characters.";
        var submission = JsonSerializer.Serialize(form);
        var replay = await CanonicalApplicationReplay.TryAsync(dbContext, canonicalCommands, form.OperationKey, actor, InventoryCommandKind.Loss, submission, ct);
        if (replay != null) return CanonicalInventoryMessages.Result(replay);
        var snapshot = (await ledgerQuery.GetSnapshotsAsync(null, [form.RoomId], ct)).SingleOrDefault(x => x.LatestAdjustmentId == form.InventoryAdjustmentId);
        if (snapshot == null) return "Inventory changed; reload and try again.";
        var batch = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(dbContext)).ResolveAsync(new(snapshot.WarehouseId, [snapshot.RoomId]),
            new(TreatmentSignature: form.TreatmentSignature), businessTime.UtcNow, ct);
        var p = batch.Positions.SingleOrDefault(x => x.Identity.Key == CanonicalTreatmentSelections.Identity(snapshot).Key);
        if (p == null || p.AvailableQuantity != form.ExpectedCurrentBins || p.Watermark.Fingerprint != form.CanonicalFingerprint)
            return "Inventory changed; reload and try again.";
        return CanonicalInventoryMessages.Result(await canonicalCommands.ExecuteAsync(new(form.OperationKey, InventoryCommandKind.Loss, actor,
            form.OccurredAt ?? businessTime.UtcNow, "Bins became unavailable for packing because they were dropped.",
            [new(new(p.Identity, p.Location, form.CanonicalFingerprint, []), form.BinCount, form.TreatmentSignature)],
            ApplicationIntent: submission, Dispatch: new(null, form.Notes)), ct));
    }

    private async Task<string?> ReverseCanonicalLossAsync(ReverseRoomInventoryLossForm form, int actor, CancellationToken ct)
    {
        if (canonicalCommands == null) return "Canonical inventory command execution is not configured.";
        var submission = JsonSerializer.Serialize(form);
        var replay = await CanonicalApplicationReplay.TryAsync(dbContext, canonicalCommands, form.OperationKey, actor, InventoryCommandKind.ReverseLoss, submission, ct);
        if (replay != null) return CanonicalInventoryMessages.Result(replay);
        return CanonicalInventoryMessages.Result(await canonicalCommands.ExecuteAsync(new(form.OperationKey, InventoryCommandKind.ReverseLoss,
            actor, businessTime.UtcNow, form.Reason, [], ApplicationIntent: submission, PhysicalParentId: form.Id), ct));
    }
}
