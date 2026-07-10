using CropQc.Data.Entities;
using CropQc.Web.Models;

namespace CropQc.Web.Services;

public static class ProjectionDistributionMath
{
    public static readonly int[] SizeDisplayOrder = [32, 36, 40, 48, 56, 64, 72, 80, 88, 100, 113, 125, 138, 150, 163, 175, 198, 216];

    public static SizeSampleDistribution BuildSizePercentages(IEnumerable<QcFruitReading> readings)
    {
        var enteredRows = readings.Where(HasEnteredFruitData).ToList();
        var denominator = enteredRows.Count;
        if (denominator == 0)
        {
            return new SizeSampleDistribution(new Dictionary<int, decimal>(), 0, 0, 0);
        }

        var classifiedCounts = enteredRows
            .Where(x => x.SizeCategory is not null)
            .GroupBy(x => x.SizeCategory!.Value)
            .ToDictionary(x => x.Key, x => x.Count());
        var classifiedCount = classifiedCounts.Values.Sum();

        return new SizeSampleDistribution(
            classifiedCounts.ToDictionary(x => x.Key, x => x.Value / (decimal)denominator),
            denominator,
            classifiedCount,
            Math.Max(0, denominator - classifiedCount));
    }

    public static IReadOnlyList<BinsRunSizeDistributionPoint> CombineWeightedSizePercentages<TLot>(
        IReadOnlyList<TLot> lots,
        IReadOnlyDictionary<string, SizeSampleDistribution> sampleData,
        Func<TLot, string> lotKey,
        Func<TLot, int> currentBins)
    {
        var representedBins = lots
            .Where(x => sampleData.TryGetValue(lotKey(x), out var data) && data.Percentages.Count > 0)
            .Sum(currentBins);
        if (representedBins <= 0)
        {
            return [];
        }

        var points = SizeDisplayOrder
            .Select(size => new BinsRunSizeDistributionPoint(
                size,
                decimal.Round(
                    lots.Sum(lot => sampleData.TryGetValue(lotKey(lot), out var data)
                        && data.Percentages.TryGetValue(size, out var percentage)
                            ? currentBins(lot) * percentage
                            : 0m) / representedBins * 100m,
                    2)))
            .ToList();
        return points.Any(x => x.Percentage > 0) ? points : [];
    }

    public static decimal CombineWeightedUnclassifiedPercent<TLot>(
        IReadOnlyList<TLot> lots,
        IReadOnlyDictionary<string, SizeSampleDistribution> sampleData,
        Func<TLot, string> lotKey,
        Func<TLot, int> currentBins)
    {
        var representedBins = lots
            .Where(x => sampleData.TryGetValue(lotKey(x), out var data) && data.Percentages.Count > 0)
            .Sum(currentBins);
        if (representedBins <= 0)
        {
            return 0m;
        }

        var unclassified = lots.Sum(lot => sampleData.TryGetValue(lotKey(lot), out var data) && data.Percentages.Count > 0
            ? currentBins(lot) * data.UnclassifiedPercentage
            : 0m);
        return decimal.Round(unclassified / representedBins * 100m, 2);
    }

    private static bool HasEnteredFruitData(QcFruitReading row) =>
        row.Pressure1Lbs is not null ||
        row.Pressure2Lbs is not null ||
        row.WeightGrams is not null ||
        row.GradeId is not null ||
        row.StarchScaleValueId is not null ||
        row.SizeCategory is not null ||
        row.Defects.Count > 0;
}

public sealed record SizeSampleDistribution(
    IReadOnlyDictionary<int, decimal> Percentages,
    int DenominatorFruitCount,
    int ClassifiedFruitCount,
    int UnclassifiedFruitCount)
{
    public decimal UnclassifiedPercentage => DenominatorFruitCount <= 0
        ? 0m
        : UnclassifiedFruitCount / (decimal)DenominatorFruitCount;
}
