using CropQc.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public sealed record InventoryIdentityKey(int CropYear, int GrowerLotId, int FruitProfileId)
{
    public override string ToString() => $"{CropYear}/{GrowerLotId}/{FruitProfileId}";
}

public sealed record InventoryIdentityResolution(
    InventoryIdentityKey Source,
    InventoryIdentityKey Canonical,
    GrowerLot GrowerLot,
    FruitProfile FruitProfile,
    IReadOnlyList<Guid> CorrectionChain)
{
    public bool IsSuperseded => Source != Canonical;
}

public interface IInventoryIdentityService
{
    Task<InventoryIdentityResolution> ResolveAsync(InventoryIdentityKey source, CancellationToken cancellationToken);
    Task<RoomInventoryLedgerSnapshot> ResolveSnapshotAsync(RoomInventoryLedgerSnapshot snapshot, CancellationToken cancellationToken);
    Task<string?> ValidateCorrectionAsync(InventoryIdentityKey source, InventoryIdentityKey target, CancellationToken cancellationToken);
    Task<string?> RejectSupersededWriteAsync(InventoryIdentityKey source, string operationLabel, CancellationToken cancellationToken);
}

public sealed class InventoryIdentityService(CropQcDbContext dbContext) : IInventoryIdentityService
{
    private const int MaximumCorrectionDepth = 32;

    public async Task<InventoryIdentityResolution> ResolveAsync(
        InventoryIdentityKey source,
        CancellationToken cancellationToken)
    {
        var mappings = await dbContext.InventoryIdentityCorrections.AsNoTracking()
            .Where(x => x.IsActive && x.IsComplete)
            .ToListAsync(cancellationToken);
        mappings = mappings.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToList();
        var bySource = mappings.ToDictionary(
            x => new InventoryIdentityKey(x.SourceCropYear, x.SourceGrowerLotId, x.SourceFruitProfileId));
        var visited = new HashSet<InventoryIdentityKey>();
        var chain = new List<Guid>();
        var current = source;
        for (var depth = 0; depth < MaximumCorrectionDepth; depth++)
        {
            if (!visited.Add(current))
            {
                throw new InvalidOperationException($"Inventory identity correction cycle detected at {current}.");
            }
            if (!bySource.TryGetValue(current, out var mapping))
            {
                var growerLot = await dbContext.GrowerLots.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == current.GrowerLotId, cancellationToken)
                    ?? throw new InvalidOperationException($"Inventory identity {current} references missing Grower Lot {current.GrowerLotId}.");
                var fruitProfile = await dbContext.FruitProfiles.AsNoTracking()
                    .SingleOrDefaultAsync(x => x.Id == current.FruitProfileId, cancellationToken)
                    ?? throw new InvalidOperationException($"Inventory identity {current} references missing Fruit Profile {current.FruitProfileId}.");
                return new(source, current, growerLot, fruitProfile, chain);
            }
            chain.Add(mapping.Id);
            current = new InventoryIdentityKey(
                mapping.TargetCropYear,
                mapping.TargetGrowerLotId,
                mapping.TargetFruitProfileId);
        }
        throw new InvalidOperationException($"Inventory identity correction chain exceeded {MaximumCorrectionDepth} mappings from {source}.");
    }

    public async Task<RoomInventoryLedgerSnapshot> ResolveSnapshotAsync(
        RoomInventoryLedgerSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (snapshot.CropYear is null || snapshot.GrowerLotId is null || snapshot.FruitProfileId is null)
        {
            throw new InvalidOperationException("Current inventory identity is incomplete and cannot be canonicalized.");
        }
        var resolved = await ResolveAsync(
            new InventoryIdentityKey(snapshot.CropYear.Value, snapshot.GrowerLotId.Value, snapshot.FruitProfileId.Value),
            cancellationToken);
        if (!resolved.IsSuperseded) return snapshot;
        return snapshot with
        {
            CropYear = resolved.Canonical.CropYear,
            GrowerLotId = resolved.Canonical.GrowerLotId,
            FruitProfileId = resolved.Canonical.FruitProfileId,
            Grower = resolved.GrowerLot.Grower,
            GrowerNumber = resolved.GrowerLot.LotNumber,
            Lot = resolved.GrowerLot.LotNumber,
            PoolStart = resolved.GrowerLot.PoolStart,
            StoredVarietyCode = resolved.FruitProfile.VarietyCode,
            Variety = resolved.FruitProfile.VarietyCode,
            VarietyName = resolved.FruitProfile.Name,
            FruitType = resolved.FruitProfile.FruitType,
            ProductionType = resolved.FruitProfile.ProductionType,
            IsOrganic = resolved.FruitProfile.IsOrganic,
            InventoryStatus = resolved.FruitProfile.ProductionType
        };
    }

    public async Task<string?> ValidateCorrectionAsync(
        InventoryIdentityKey source,
        InventoryIdentityKey target,
        CancellationToken cancellationToken)
    {
        if (source == target) return "Source and target inventory identities must be different.";
        if (source.CropYear <= 0 || target.CropYear <= 0)
            return "Inventory identity corrections require valid crop years.";
        var existing = await dbContext.InventoryIdentityCorrections.AsNoTracking()
            .SingleOrDefaultAsync(x => x.SourceCropYear == source.CropYear
                && x.SourceGrowerLotId == source.GrowerLotId
                && x.SourceFruitProfileId == source.FruitProfileId, cancellationToken);
        if (existing is not null)
        {
            var existingTarget = new InventoryIdentityKey(existing.TargetCropYear, existing.TargetGrowerLotId, existing.TargetFruitProfileId);
            return existingTarget == target
                ? null
                : $"Inventory identity {source} already has a conflicting correction to {existingTarget}.";
        }
        if (!await dbContext.GrowerLots.AsNoTracking().AnyAsync(x => x.Id == source.GrowerLotId, cancellationToken)
            || !await dbContext.FruitProfiles.AsNoTracking().AnyAsync(x => x.Id == source.FruitProfileId, cancellationToken)
            || !await dbContext.GrowerLots.AsNoTracking().AnyAsync(x => x.Id == target.GrowerLotId && x.IsActive, cancellationToken)
            || !await dbContext.FruitProfiles.AsNoTracking().AnyAsync(x => x.Id == target.FruitProfileId && x.IsActive, cancellationToken))
        {
            return "The source or target Grower Lot/Fruit Profile is unavailable.";
        }
        var targetResolution = await ResolveAsync(target, cancellationToken);
        if (targetResolution.Canonical == source)
            return $"Inventory identity correction {source} -> {target} would create a cycle.";
        if (targetResolution.IsSuperseded)
            return $"Selected target inventory identity {target} has been superseded by {targetResolution.Canonical}. Select the final canonical identity.";
        return null;
    }

    public async Task<string?> RejectSupersededWriteAsync(
        InventoryIdentityKey source,
        string operationLabel,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveAsync(source, cancellationToken);
        return resolved.IsSuperseded
            ? $"{operationLabel} uses superseded inventory identity {source}; the canonical identity is {resolved.Canonical}. Refresh and use the corrected identity."
            : null;
    }
}

public static class InventoryIdentityWriteGuard
{
    public const string AdjustmentType = "InventoryIdentityCorrection";
    public static async Task<string?> RejectSupersededAsync(
        CropQcDbContext dbContext,
        int? cropYear,
        int? growerLotId,
        int? fruitProfileId,
        string operationLabel,
        CancellationToken cancellationToken)
    {
        var corrections = dbContext.InventoryIdentityCorrections.AsNoTracking()
            .Where(x => x.IsActive && x.IsComplete);
        if (cropYear is not null) corrections = corrections.Where(x => x.SourceCropYear == cropYear.Value);
        if (growerLotId is not null) corrections = corrections.Where(x => x.SourceGrowerLotId == growerLotId.Value);
        if (fruitProfileId is not null) corrections = corrections.Where(x => x.SourceFruitProfileId == fruitProfileId.Value);

        // Older, otherwise valid ledger history can predate one or more identity snapshots.
        // It remains reversible unless the available identity fields actually intersect a
        // durable correction. A matching partial identity must still fail closed because
        // replaying it cannot prove whether the obsolete or canonical identity is intended.
        if (cropYear is null && growerLotId is null && fruitProfileId is null) return null;
        if (cropYear is null || growerLotId is null || fruitProfileId is null)
        {
            return await corrections.AnyAsync(cancellationToken)
                ? $"{operationLabel} has incomplete historical inventory identity that intersects a superseded identity and cannot be safely replayed."
                : null;
        }
        var correction = await corrections
            .Select(x => new { x.TargetCropYear, x.TargetGrowerLotId, x.TargetFruitProfileId })
            .SingleOrDefaultAsync(cancellationToken);
        return correction is null
            ? null
            : $"{operationLabel} uses a superseded inventory identity. Its canonical identity is {correction.TargetCropYear}/{correction.TargetGrowerLotId}/{correction.TargetFruitProfileId}; refresh or use a reviewed compensating correction.";
    }

    public static InventoryIdentityKey ResolveCanonical(
        InventoryIdentityKey source,
        IReadOnlyCollection<InventoryIdentityCorrection> corrections)
    {
        var bySource = corrections
            .Where(x => x.IsActive && x.IsComplete)
            .GroupBy(x => new InventoryIdentityKey(x.SourceCropYear, x.SourceGrowerLotId, x.SourceFruitProfileId))
            .ToDictionary(x => x.Key, x => x.OrderBy(y => y.CreatedAt).ThenBy(y => y.Id).Single());
        var visited = new HashSet<InventoryIdentityKey>();
        var current = source;
        for (var depth = 0; depth < 32; depth++)
        {
            if (!visited.Add(current))
                throw new InvalidOperationException($"Inventory identity correction cycle detected at {current}.");
            if (!bySource.TryGetValue(current, out var correction)) return current;
            current = new InventoryIdentityKey(
                correction.TargetCropYear,
                correction.TargetGrowerLotId,
                correction.TargetFruitProfileId);
        }
        throw new InvalidOperationException($"Inventory identity correction chain exceeded 32 mappings from {source}.");
    }
}
