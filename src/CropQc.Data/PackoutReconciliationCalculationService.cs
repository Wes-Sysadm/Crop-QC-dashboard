using CropQc.Data.Entities;

namespace CropQc.Data;

public sealed record PackoutActualLine(
    decimal Quantity,
    decimal NetWeightPounds,
    string ProductCategory,
    int? SizeCategory = null,
    string? GradeCode = null);

public sealed record PackoutDistributionPoint(string Key, decimal Pounds, decimal Percentage);

public sealed record PackoutAccuracyWeights(
    decimal Size = 35m,
    decimal Grade = 35m,
    decimal Packout = 21m,
    decimal Juice = 3m,
    decimal PeelerSlicer = 3m,
    decimal Waste = 3m)
{
    public decimal Total => Size + Grade + Packout + Juice + PeelerSlicer + Waste;
}

public sealed record PackoutReconciliationCalculation(
    decimal DumpedPounds,
    decimal PackedProductPounds,
    decimal JuicePounds,
    decimal PeelerSlicerPounds,
    decimal WastePounds,
    decimal? PackoutPercent,
    decimal? JuicePercent,
    decimal? PeelerSlicerPercent,
    decimal? WastePercent,
    IReadOnlyList<PackoutDistributionPoint> SizeDistribution,
    IReadOnlyList<PackoutDistributionPoint> GradeDistribution,
    decimal? SizeAccuracy,
    decimal? GradeAccuracy,
    decimal PackoutAccuracy,
    decimal JuiceAccuracy,
    decimal PeelerSlicerAccuracy,
    decimal WasteAccuracy,
    decimal OverallAccuracy,
    decimal ReconciliationDifferencePounds,
    bool HasReconciliationWarning);

public static class PackoutReconciliationCalculationService
{
    public const string CurrentCalculationVersion = "1.0-weighted-overlap";
    public const decimal ReconciliationWarningThresholdPercent = 10m;

    public static PackoutReconciliationCalculation Calculate(
        decimal dumpedBins,
        decimal poundsPerBin,
        IEnumerable<PackoutActualLine> actualLines,
        IReadOnlyDictionary<string, decimal> projectedSizePercentages,
        IReadOnlyDictionary<string, decimal> projectedGradePercentages,
        decimal? projectedPackoutPercent,
        decimal? projectedJuicePercent,
        decimal? projectedPeelerSlicerPercent,
        decimal? projectedWastePercent,
        PackoutAccuracyWeights? weights = null)
    {
        if (dumpedBins < 0m) throw new ArgumentOutOfRangeException(nameof(dumpedBins));
        if (poundsPerBin <= 0m) throw new ArgumentOutOfRangeException(nameof(poundsPerBin));
        weights ??= new PackoutAccuracyWeights();
        if (weights.Total <= 0m) throw new ArgumentOutOfRangeException(nameof(weights));

        var lines = actualLines
            .Where(x => x.Quantity != 0m && x.NetWeightPounds > 0m)
            .Select(x => new WeightedLine(
                x,
                x.Quantity * x.NetWeightPounds))
            .ToList();
        var dumpedPounds = dumpedBins * poundsPerBin;
        var packed = lines.Where(x => IsCategory(x.Line.ProductCategory, PackoutProductCategories.Packed)).Sum(x => x.Pounds);
        var juice = lines.Where(x => IsCategory(x.Line.ProductCategory, PackoutProductCategories.Juice)).Sum(x => x.Pounds);
        var peeler = lines.Where(x => IsCategory(x.Line.ProductCategory, PackoutProductCategories.PeelerSlicer)).Sum(x => x.Pounds);
        var waste = lines.Where(x => IsCategory(x.Line.ProductCategory, PackoutProductCategories.Waste)).Sum(x => x.Pounds);

        var size = Distribution(
            lines.Where(x => IsCategory(x.Line.ProductCategory, PackoutProductCategories.Packed) && x.Line.SizeCategory is not null)
                .Select(x => (x.Line.SizeCategory!.Value.ToString(), x.Pounds)));
        var grade = Distribution(
            lines.Where(x => IsCategory(x.Line.ProductCategory, PackoutProductCategories.Packed) && !string.IsNullOrWhiteSpace(x.Line.GradeCode))
                .Select(x => (x.Line.GradeCode!.Trim(), x.Pounds)));

        var packoutPercent = Percent(packed, dumpedPounds);
        var juicePercent = Percent(juice, dumpedPounds);
        var peelerPercent = Percent(peeler, dumpedPounds);
        var wastePercent = Percent(waste, dumpedPounds);
        var sizeScore = DistributionOverlap(projectedSizePercentages, size);
        var gradeScore = DistributionOverlap(projectedGradePercentages, grade);
        var packoutScore = ComponentScore(projectedPackoutPercent, packoutPercent);
        var juiceScore = ComponentScore(projectedJuicePercent, juicePercent);
        var peelerScore = ComponentScore(projectedPeelerSlicerPercent, peelerPercent);
        var wasteScore = ComponentScore(projectedWastePercent, wastePercent);
        var overall = (
            (sizeScore ?? 0m) * weights.Size
            + (gradeScore ?? 0m) * weights.Grade
            + packoutScore * weights.Packout
            + juiceScore * weights.Juice
            + peelerScore * weights.PeelerSlicer
            + wasteScore * weights.Waste) / weights.Total;
        var difference = packed + juice + peeler + waste - dumpedPounds;
        var warning = dumpedPounds > 0m
            && Math.Abs(difference) / dumpedPounds * 100m > ReconciliationWarningThresholdPercent;

        return new(
            dumpedPounds,
            packed,
            juice,
            peeler,
            waste,
            packoutPercent,
            juicePercent,
            peelerPercent,
            wastePercent,
            size,
            grade,
            sizeScore,
            gradeScore,
            packoutScore,
            juiceScore,
            peelerScore,
            wasteScore,
            decimal.Round(overall, 4),
            decimal.Round(difference, 4),
            warning);
    }

    public static decimal? DistributionOverlap(
        IReadOnlyDictionary<string, decimal> projectedPercentages,
        IReadOnlyList<PackoutDistributionPoint> actual)
    {
        if (projectedPercentages.Count == 0 || actual.Count == 0) return null;
        var actualByKey = actual.ToDictionary(x => x.Key, x => x.Percentage, StringComparer.OrdinalIgnoreCase);
        var keys = projectedPercentages.Keys
            .Concat(actualByKey.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var absoluteDifference = keys.Sum(key =>
            Math.Abs(projectedPercentages.GetValueOrDefault(key) - actualByKey.GetValueOrDefault(key)));
        return decimal.Round(Math.Max(0m, 100m - 0.5m * absoluteDifference), 4);
    }

    public static decimal ComponentScore(decimal? projectedPercent, decimal? actualPercent) =>
        projectedPercent is null || actualPercent is null
            ? 0m
            : decimal.Round(Math.Max(0m, 100m - Math.Abs(projectedPercent.Value - actualPercent.Value)), 4);

    private static IReadOnlyList<PackoutDistributionPoint> Distribution(
        IEnumerable<(string Key, decimal Pounds)> values)
    {
        var grouped = values
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(x => new { x.Key, Pounds = x.Sum(y => y.Pounds) })
            .Where(x => x.Pounds > 0m)
            .ToList();
        var total = grouped.Sum(x => x.Pounds);
        if (total <= 0m) return [];
        return grouped
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => new PackoutDistributionPoint(
                x.Key,
                decimal.Round(x.Pounds, 4),
                decimal.Round(x.Pounds / total * 100m, 4)))
            .ToList();
    }

    private static decimal? Percent(decimal value, decimal denominator) =>
        denominator <= 0m ? null : decimal.Round(value / denominator * 100m, 4);

    private static bool IsCategory(string value, string expected) =>
        string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);

    private sealed record WeightedLine(PackoutActualLine Line, decimal Pounds);
}
