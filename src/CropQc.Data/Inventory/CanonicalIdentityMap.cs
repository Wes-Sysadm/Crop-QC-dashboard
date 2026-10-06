using System.Collections.Immutable;
using CropQc.Data.Entities;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

internal sealed record CanonicalIdentityResolution(InventoryIdentity Original, InventoryIdentity Current, ImmutableArray<Guid> Corrections);

/// <summary>Audited identity overlays for immutable allocations. Never rewrites a
/// source segment, movement or receipt snapshot to make historical evidence fit.</summary>
internal sealed class CanonicalIdentityMap(IReadOnlyList<InventoryIdentityCorrection> corrections,
    IReadOnlyDictionary<int, GrowerLot> growers, IReadOnlyDictionary<int, FruitProfile> profiles)
{
    internal IReadOnlyList<InventoryIdentityCorrection> Corrections => corrections;

    internal static async Task<CanonicalIdentityMap> LoadAsync(CropQcDbContext db, IReadOnlyCollection<long> receipts, CancellationToken ct)
    {
        var rows = await db.InventoryIdentityCorrections.AsNoTracking().Where(x => x.IsActive && x.IsComplete
            && (x.CorrectedReceiptId == null || receipts.Contains(x.CorrectedReceiptId.Value)))
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Take(InventoryEvidenceLoader.MaximumEvidenceRowsPerTable + 1).ToListAsync(ct);
        if (rows.Count > InventoryEvidenceLoader.MaximumEvidenceRowsPerTable) throw new InvalidOperationException("Identity correction evidence exceeds the safe bound.");
        if (rows.Count == 0) return new(rows, new Dictionary<int, GrowerLot>(), new Dictionary<int, FruitProfile>());
        var growerIds = rows.Select(x => x.TargetGrowerLotId).Distinct().ToArray();
        var profileIds = rows.Select(x => x.TargetFruitProfileId).Distinct().ToArray();
        return new(rows,
            await db.GrowerLots.AsNoTracking().Where(x => growerIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct),
            await db.FruitProfiles.AsNoTracking().Where(x => profileIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct));
    }

    internal CanonicalIdentityResolution Resolve(InventoryIdentity source, long? receiptId)
    {
        var current = source;
        var visited = new HashSet<(int?, int?, int?)>();
        var chain = ImmutableArray.CreateBuilder<Guid>();
        for (var depth = 0; depth < 32; depth++)
        {
            if (!visited.Add((current.CropYear, current.GrowerLotId, current.FruitProfileId)))
                throw new InvalidOperationException("Identity correction history contains a cycle; review the recorded mappings.");
            var candidates = corrections.Where(x => x.SourceCropYear == current.CropYear && x.SourceGrowerLotId == current.GrowerLotId
                && x.SourceFruitProfileId == current.FruitProfileId && (x.CorrectedReceiptId == null || x.CorrectedReceiptId == receiptId)).ToArray();
            if (candidates.Any(x => x.CorrectedReceiptId == receiptId && receiptId != null))
                candidates = candidates.Where(x => x.CorrectedReceiptId == receiptId).ToArray();
            if (candidates.Length == 0) return new(source, current, chain.ToImmutable());
            if (candidates.Select(x => (x.TargetCropYear, x.TargetGrowerLotId, x.TargetFruitProfileId)).Distinct().Count() != 1)
                throw new InvalidOperationException("Identity correction history has conflicting targets; review it before changing inventory.");
            var next = candidates[0];
            if (!growers.TryGetValue(next.TargetGrowerLotId, out var grower) || !profiles.TryGetValue(next.TargetFruitProfileId, out var profile))
                throw new InvalidOperationException("Identity correction references missing Master Data.");
            chain.AddRange(candidates.Select(x => x.Id));
            current = new(next.TargetCropYear, next.TargetGrowerLotId, next.TargetFruitProfileId, grower.LotNumber, grower.LotNumber,
                profile.VarietyCode, profile.ProductionType, profile.IsOrganic, InventoryStatusIdentity.Normalize(current.Status, current.ProductionType));
        }
        throw new InvalidOperationException("Identity correction history exceeds the safe depth.");
    }

    internal static InventoryIdentity Historical(TreatmentLineageSegment source) => new(source.CropYear, source.GrowerLotId, source.FruitProfileId,
        source.LotNumberSnapshot, source.GrowerNumberSnapshot, source.VarietyCodeSnapshot, source.ProductionTypeSnapshot,
        source.IsOrganicSnapshot, source.InventoryStatusSnapshot ?? "");
}
