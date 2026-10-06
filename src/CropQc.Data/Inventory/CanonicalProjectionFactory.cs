using CropQc.Data.Entities;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

// Only the executor calls this factory. It never derives physical quantity from a gap.
internal sealed class CanonicalProjectionFactory(CropQcDbContext db)
{
    private readonly Dictionary<(int Room, long Receipt), List<TreatmentLineageSegment>> prepared = [];
    private readonly Dictionary<int, string> growers = [];

    // Receipt location correction writes many treatment slices in one operation.
    // Load its bounded destination once, rather than querying for every slice.
    public async Task PrepareReceiptDestinationAsync(InventoryIdentity identity, int room, long receipt, CancellationToken ct)
    {
        var rows = await db.TreatmentLineageSegments.Include(x => x.Applications)
            .Where(x => x.RoomId == room && x.ReceiptId == receipt && x.Disposition == "Current")
            .Take(InventoryEvidenceLoader.MaximumEvidenceRowsPerTable + 1).ToListAsync(ct);
        if (rows.Count > InventoryEvidenceLoader.MaximumEvidenceRowsPerTable)
            throw new InvalidOperationException("Destination receipt evidence exceeds the safe limit.");
        prepared[(room, receipt)] = rows;
        growers[identity.GrowerLotId!.Value] = await db.GrowerLots.Where(x => x.Id == identity.GrowerLotId).Select(x => x.Grower).SingleAsync(ct);
    }

    public async Task<TreatmentLineageSegment> CurrentAsync(InventoryIdentity identity, int warehouse, int room,
        string signature, string state, long? receiptId, IEnumerable<long> applications, DateTimeOffset now, CancellationToken ct)
    {
        var applicationIds = applications.Distinct().Order().ToArray();
        var rows = receiptId is long sourceReceiptId && prepared.TryGetValue((room, sourceReceiptId), out var cached) ? cached
            : await db.TreatmentLineageSegments.Include(x => x.Applications)
            .Where(x => x.RoomId == room && x.Disposition == "Current" && x.TreatmentSignature == signature && x.ReceiptId == receiptId)
            .ToListAsync(ct);
        var matches = rows.Concat(db.TreatmentLineageSegments.Local)
            .Distinct().Where(x => x.RoomId == room && x.Disposition == "Current" && x.TreatmentSignature == signature
                && x.ReceiptId == receiptId && InventoryStatusIdentity.NormalizeLineageKey(x.IdentityKey) == identity.Key).ToArray();
        if (matches.Length > 1 || matches.Any(x => x.IdentityKey != identity.Key || x.WarehouseId != warehouse
            || x.TreatmentState != state || !x.Applications.Select(a => a.RoomTreatmentApplicationId).Order().SequenceEqual(applicationIds)))
            throw new InvalidOperationException("Conflicting current projection identity or treatment links.");
        if (matches.Length == 1) return matches[0];
        var segment = new TreatmentLineageSegment
        {
            IdentityKey = identity.Key,
            WarehouseId = warehouse,
            RoomId = room,
            CropYear = identity.CropYear,
            GrowerLotId = identity.GrowerLotId,
            FruitProfileId = identity.FruitProfileId,
            GrowerNameSnapshot = growers.TryGetValue(identity.GrowerLotId!.Value, out var grower) ? grower
                : await db.GrowerLots.Where(x => x.Id == identity.GrowerLotId).Select(x => x.Grower).SingleAsync(ct),
            GrowerNumberSnapshot = identity.GrowerNumber,
            LotNumberSnapshot = identity.Lot,
            VarietyCodeSnapshot = identity.Variety,
            ProductionTypeSnapshot = identity.ProductionType,
            IsOrganicSnapshot = identity.IsOrganic,
            InventoryStatusSnapshot = InventoryStatusIdentity.Normalize(identity.Status, identity.ProductionType),
            TreatmentState = state,
            TreatmentSignature = signature,
            ReceiptId = receiptId,
            CreatedAt = now,
            UpdatedAt = now,
            Disposition = "Current"
        };
        foreach (var id in applicationIds) segment.Applications.Add(new() { RoomTreatmentApplicationId = id, Sequence = segment.Applications.Count });
        db.TreatmentLineageSegments.Add(segment);
        return segment;
    }

    public static void Retire(TreatmentLineageSegment segment, string key, DateTimeOffset now)
    {
        if (segment.Disposition != "Current") throw new InvalidOperationException("Historical projection cannot be revived or retired twice.");
        segment.RetiredQuantity = segment.CurrentBins;
        segment.CurrentBins = 0;
        segment.Disposition = "Historical";
        segment.RetiredAt = now;
        segment.RetiredByCommandKey = key;
        segment.UpdatedAt = now;
        segment.ConcurrencyVersion++;
    }
}
