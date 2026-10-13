using CropQc.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public sealed record QcSizeCount(int Size, int Count);
public sealed record QcSizeDistribution(IReadOnlyList<QcSizeCount> Counts, int UnclassifiedCount);
public sealed record QcEmailSizeSnapshot(DateTimeOffset GeneratedAt, string AverageBoxSize,
    QcSizeDistribution CurrentSample, QcSizeDistribution EntireLot, int LotSampleCount, bool LotIdentityResolved);

public interface IQcEmailSizeSnapshotService
{
    Task<QcEmailSizeSnapshot> CaptureAsync(QcSample sample, CancellationToken cancellationToken);
}

/// <summary>Read-only, generation-time observations. No inventory inference or observation reclassification.</summary>
public sealed class QcEmailSizeSnapshotService(CropQcDbContext dbContext) : IQcEmailSizeSnapshotService
{
    public async Task<QcEmailSizeSnapshot> CaptureAsync(QcSample sample, CancellationToken cancellationToken)
    {
        var generatedAt = DateTimeOffset.UtcNow;
        var receipt = sample.Receipt ?? throw new InvalidOperationException("QC email requires a receipt.");
        var categories = await dbContext.FruitSizeConversionThresholds.AsNoTracking()
            .Where(x => x.IsActive && x.FruitType == receipt.FruitProfile.FruitType && x.SizeCategory > 0)
            .Select(x => x.SizeCategory).Distinct().OrderBy(x => x).ToArrayAsync(cancellationToken);
        var identity = CanonicalQcFruitIdentity.FromReceipt(receipt);
        // Include historical receipts predating GrowerLot linkage, then apply the canonical
        // resolver to the complete candidate set. Do not truncate to a latest-sample window.
        var legacyIdentity = CanonicalQcFruitIdentity.Create(receipt.CropYear, null,
            receipt.GrowerNumber ?? receipt.LotCode, receipt.LotCode, receipt.FruitProfileId,
            receipt.FruitProfile.VarietyCode, receipt.FruitProfile.ProductionType, receipt.FruitProfile.IsOrganic);
        var targets = new[] { identity, legacyIdentity }.OfType<CanonicalQcFruitIdentity>().Distinct().ToArray();
        var candidates = identity is null ? [] : await CanonicalQcFruitIdentity.FilterReceiptSamples(
                dbContext.QcSamples.AsNoTracking(), targets)
            .Where(x => x.Id != sample.Id && !x.Receipt!.IsDeleted
                && x.IsTestData == sample.IsTestData && x.Receipt.IsTestData == receipt.IsTestData)
            .Include(x => x.Receipt).ThenInclude(x => x!.FruitProfile)
            .Include(x => x.FruitReadings).ThenInclude(x => x.Defects)
            .AsSingleQuery().ToListAsync(cancellationToken);
        // No candidate limit: this is the entire qualifying lot, not a latest-sample lookup.
        var available = candidates.Where(x => x.CreatedAt <= generatedAt && x.SampleTakenAt <= generatedAt
            && x.Receipt!.CreatedAt <= generatedAt).ToArray();
        var matching = identity is null ? [] : CanonicalQcFruitIdentity.ResolveUnambiguous(identity,
            available, x => CanonicalQcFruitIdentity.FromReceipt(x.Receipt!));
        var resolved = identity is not null && (matching.Count > 0 || !available.Any(x =>
            CanonicalQcFruitIdentity.FromReceipt(x.Receipt!) is { } candidate && identity.Matches(candidate)));
        var observations = matching.SelectMany(x => x.FruitReadings)
            .Where(x => x.CreatedAt <= generatedAt).Concat(sample.FruitReadings);
        return Create(generatedAt, categories, sample.FruitReadings, observations,
            matching.Count + 1, resolved);
    }

    public static QcEmailSizeSnapshot Create(DateTimeOffset generatedAt, IEnumerable<int> categories,
        IEnumerable<QcFruitReading> current, IEnumerable<QcFruitReading> lot, int lotSampleCount, bool identityResolved)
    {
        var sizes = categories.Where(x => x > 0).Distinct().Order().ToArray();
        var currentRows = Unique(current).ToArray();
        return new(generatedAt, AverageBoxSize(currentRows.Select(x => x.WeightGrams), sizes),
            Distribution(currentRows, sizes), Distribution(Unique(lot), sizes), lotSampleCount, identityResolved);
    }

    private static IEnumerable<QcFruitReading> Unique(IEnumerable<QcFruitReading> rows) =>
        rows.DistinctBy(x => x.Id > 0 ? (x.Id, 0L, 0) : (0L, x.QcSampleId, x.RowNumber));

    public static string AverageBoxSize(IEnumerable<decimal?> weights, IReadOnlyList<int> categories)
    {
        var valid = weights.Where(x => x > 0).Select(x => x!.Value).ToArray();
        if (valid.Length == 0 || categories.Count == 0) return SizeCalculationService.NotCalculated;
        // Compare without dividing by a potentially tiny weight. This is ceiling to a category,
        // not rounding to an integer or averaging recorded categories.
        var average = valid.Average();
        foreach (var size in categories.Where(x => x > 0).Distinct().Order())
        {
            if (average >= 18143.6948m / size) return size.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        return SizeCalculationService.Undersized;
    }

    private static QcSizeDistribution Distribution(IEnumerable<QcFruitReading> rows, int[] categories)
    {
        var entered = rows.Where(x => x.WeightGrams is not null || x.SizeCategory is not null
            || x.Pressure1Lbs is not null || x.Pressure2Lbs is not null || x.GradeId is not null
            || x.StarchScaleValueId is not null || x.DefectsInspected || x.Defects.Count > 0).ToArray();
        var counts = entered.Where(x => x.SizeCategory is int size && categories.Contains(size))
            .GroupBy(x => x.SizeCategory!.Value).ToDictionary(x => x.Key, x => x.Count());
        var range = counts.Count == 0 ? [] : categories.Where(x => x >= counts.Keys.Min() && x <= counts.Keys.Max());
        return new(range.Select(x => new QcSizeCount(x, counts.GetValueOrDefault(x))).ToArray(),
            entered.Length - counts.Values.Sum());
    }
}
