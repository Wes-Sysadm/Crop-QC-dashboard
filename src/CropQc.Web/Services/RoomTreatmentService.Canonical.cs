using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CropQc.Data.Inventory;
using CropQc.Data;
using CropQc.Shared.Inventory;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public sealed partial class RoomTreatmentService
{
    private async Task<Dictionary<string, List<CurrentTreatmentSegmentViewModel>>> ProjectCanonicalTreatmentsAsync(
        IReadOnlyList<RoomInventoryLedgerSnapshot> snapshots, CancellationToken ct)
    {
        if (snapshots.Count == 0) return [];
        var batch = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(dbContext)).ResolveAsync(
            new(null, snapshots.Select(x => x.RoomId).Distinct().ToImmutableArray()), new(), businessTime.UtcNow, ct);
        var ids = batch.Positions.SelectMany(p => p.TreatmentSlices).SelectMany(s => s.ApplicationIds).Distinct().ToArray();
        var applications = await dbContext.RoomTreatmentApplications.AsNoTracking().Where(x => ids.Contains(x.Id))
            .Select(x => new TreatmentApplicationSummaryViewModel(x.Id, x.AppliedAt, x.ProductNameSnapshot, x.CommonNameSnapshot, x.ReversedAt != null)).ToDictionaryAsync(x => x.Id, ct);
        var result = new Dictionary<string, List<CurrentTreatmentSegmentViewModel>>();
        foreach (var s in snapshots.DistinctBy(SelectionLookupKey))
        {
            var p = batch.Positions.SingleOrDefault(x => x.Location.RoomId == s.RoomId && x.Identity.Key == CanonicalTreatmentSelections.Identity(s).Key);
            var rows = new List<CurrentTreatmentSegmentViewModel>();
            if (p != null && !p.IsOperable)
                rows.Add(new(null, p.Identity.Key, s.GrowerNumber ?? s.Lot, s.Grower, s.VarietyName, s.ProductionType, s.IsOrganic,
                    p.AuthoritativeQuantity, "NeedsReview", "needs-review", [], null, false, CanonicalInventoryMessages.Blocker(p), p.RawProjectionQuantity));
            else if (p != null)
                foreach (var slice in p.TreatmentSlices)
                    rows.Add(new(slice.ProjectionIds.Length == 1 ? slice.ProjectionIds[0] : null, p.Identity.Key, s.GrowerNumber ?? s.Lot,
                        s.Grower, s.VarietyName, s.ProductionType, s.IsOrganic, slice.Quantity, slice.State, slice.Signature,
                        slice.ApplicationIds.Where(applications.ContainsKey).Select(id => applications[id]).ToArray(),
                        slice.ReceiptEvidenceIds.Length == 1 ? slice.ReceiptEvidenceIds[0] : null));
            result[SelectionLookupKey(s)] = rows;
        }
        return result;
    }
    private async Task<InventoryAvailabilityBatch> CanonicalRoomAsync(int room, CancellationToken ct) =>
        await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(dbContext)).ResolveAsync(new(null, [room]), new(), businessTime.UtcNow, ct);

    private static string TreatmentWatermark(InventoryAvailabilityBatch batch) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        string.Join('\n', batch.Positions.OrderBy(x => x.PositionKey).Select(x => x.Watermark.Fingerprint)))));

    private static ImmutableArray<InventoryCommandLine> TreatmentLines(InventoryAvailabilityBatch batch, long? application = null) =>
        batch.Positions.SelectMany(p => p.TreatmentSlices.Where(s => s.Quantity > 0 && (application == null || s.ApplicationIds.Contains(application.Value)))
            .GroupBy(s => s.Signature).Select(g => new InventoryCommandLine(new(p.Identity, p.Location, p.Watermark.Fingerprint, p.Watermark.Versions),
                g.Sum(s => s.Quantity), g.Key))).ToImmutableArray();

    private async Task<(string? Error, long? ApplicationId)> ApplyCanonicalRoomAsync(RoomTreatmentApplyForm form, int actor, CancellationToken ct)
    {
        if (canonicalCommands == null) return ("Canonical inventory command execution is not configured.", null);
        var submission = JsonSerializer.Serialize(form);
        var replay = await CanonicalApplicationReplay.TryAsync(dbContext, canonicalCommands, form.OperationKey, actor, InventoryCommandKind.TreatmentAssignment, submission, ct);
        if (replay != null) return (CanonicalInventoryMessages.Result(replay), replay.Effects.FirstOrDefault()?.ParentId);
        var batch = await CanonicalRoomAsync(form.RoomId, ct);
        if (form.CanonicalSnapshot != TreatmentWatermark(batch)) return ("Inventory changed; reload and review treatment again.", null);
        if (batch.Positions.Any(p => !p.IsOperable)) return ("Treatment identity cannot be proven for every occupied position.", null);
        var result = await canonicalCommands.ExecuteAsync(new(form.OperationKey, InventoryCommandKind.TreatmentAssignment,
            actor, form.AppliedAt, "Room treatment", TreatmentLines(batch), TreatmentChemicalId: form.TreatmentChemicalId,
            ApplicationIntent: submission, Dispatch: new(null, form.Notes)), ct);
        return (CanonicalInventoryMessages.Result(result), result.Effects.FirstOrDefault()?.ParentId);
    }

    private async Task<string?> ReverseCanonicalTreatmentAsync(ReverseRoomTreatmentApplicationForm form, string level, int actor, CancellationToken ct)
    {
        if (canonicalCommands == null) return "Canonical inventory command execution is not configured.";
        var submission = JsonSerializer.Serialize(form);
        var replay = await CanonicalApplicationReplay.TryAsync(dbContext, canonicalCommands, form.OperationKey, actor, InventoryCommandKind.TreatmentReversal, submission, ct);
        if (replay != null) return CanonicalInventoryMessages.Result(replay);
        var app = await dbContext.RoomTreatmentApplications.AsNoTracking().SingleOrDefaultAsync(x => x.Id == form.Id, ct);
        if (app == null || app.ApplicationLevel != level || app.ReversedAt != null) return "The active treatment application was not found.";
        var batch = await CanonicalRoomAsync(app.RoomId, ct);
        return CanonicalInventoryMessages.Result(await canonicalCommands.ExecuteAsync(new(form.OperationKey, InventoryCommandKind.TreatmentReversal,
            actor, businessTime.UtcNow, form.Reason, TreatmentLines(batch, app.Id), TreatmentApplicationId: app.Id, ApplicationIntent: submission), ct));
    }
}
