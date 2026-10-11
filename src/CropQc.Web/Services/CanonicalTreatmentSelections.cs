using System.Collections.Immutable;
using CropQc.Data;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;

namespace CropQc.Web.Services;

internal static class CanonicalTreatmentSelections
{
    internal static IReadOnlyList<InventoryTreatmentSlice> MovementSlices(InventoryAvailabilityResult position) =>
        position.TreatmentSlices.GroupBy(x => new { x.Signature, x.State }).Select(g => new InventoryTreatmentSlice(g.Key.Signature,
            g.Key.State, g.Sum(x => x.Quantity), g.All(x => x.Confidence == InventoryConfidence.Proven) ? InventoryConfidence.Proven : InventoryConfidence.Unknown,
            g.SelectMany(x => x.ProjectionIds).Distinct().ToImmutableArray(), g.SelectMany(x => x.ApplicationIds).Distinct().ToImmutableArray(),
            g.SelectMany(x => x.ReceiptEvidenceIds).Distinct().ToImmutableArray())).ToArray();
    public static InventoryIdentity Identity(RoomInventoryLedgerSnapshot x) => new(x.CropYear, x.GrowerLotId, x.FruitProfileId,
        x.Lot, x.GrowerNumber, x.Variety, x.ProductionType, x.IsOrganic, x.InventoryStatus);

    public static async Task<IReadOnlyDictionary<string, IReadOnlyList<TreatmentSegmentSelection>>> LoadAsync(
        CropQcDbContext db, IReadOnlyList<RoomInventoryLedgerSnapshot> snapshots, CancellationToken ct)
    {
        if (snapshots.Count == 0) return new Dictionary<string, IReadOnlyList<TreatmentSegmentSelection>>();
        var batch = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(
            new(null, snapshots.Select(x => x.RoomId).Distinct().ToImmutableArray()), new() { AllowIndependentCohorts = true }, DateTimeOffset.UtcNow, ct);
        var byPosition = batch.Positions.ToDictionary(x => (x.Location.RoomId, x.Identity.Key));
        return snapshots.GroupBy(RoomTreatmentService.SelectionLookupKey).ToDictionary(x => x.Key, group =>
        {
            var s = group.First();
            if (!byPosition.TryGetValue((s.RoomId, Identity(s).Key), out var r)) return (IReadOnlyList<TreatmentSegmentSelection>)[];
            if (!r.IsOperable) return [new(r.Identity.Key, "", "Unknown", 0, "Needs Review", IsAvailable: false,
                UnavailableReason: CanonicalInventoryMessages.Blocker(r), CanonicalFingerprint: r.Watermark.Fingerprint)];
            return (IReadOnlyList<TreatmentSegmentSelection>)MovementSlices(r).Select(slice => new TreatmentSegmentSelection(
                r.Identity.Key, slice.Signature, slice.State, slice.Quantity,
                slice.State == "Untreated" ? "Untreated" : "Confirmed treatment",
                r.ReceiptProvenance.Confidence == InventoryConfidence.Proven && r.ReceiptProvenance.ReceiptIds.Length == 1 ? r.ReceiptProvenance.ReceiptIds[0] : null,
                null, slice.Confidence == InventoryConfidence.Proven, CanonicalFingerprint: r.Watermark.Fingerprint)).ToArray();
        });
    }
}
