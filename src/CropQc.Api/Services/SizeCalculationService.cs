using CropQc.Data.Entities;

namespace CropQc.Api.Services;

public sealed record SizeCalculationResult(int? SizeCategory, string SizeStatus);

public static class SizeCalculationService
{
    public const string NotCalculated = "NotCalculated";
    public const string Undersized = "Undersized";
    public const string Sized = "Sized";

    public static SizeCalculationResult Calculate(decimal? weightGrams, IEnumerable<FruitSizeConversionThreshold> thresholds)
    {
        if (weightGrams is null)
        {
            return new SizeCalculationResult(null, NotCalculated);
        }

        var orderedThresholds = thresholds
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.MinimumWeightGrams)
            .ToList();

        var match = orderedThresholds.FirstOrDefault(x => weightGrams.Value >= x.MinimumWeightGrams);
        return match is null
            ? new SizeCalculationResult(null, Undersized)
            : new SizeCalculationResult(match.SizeCategory, Sized);
    }

    public static decimal? CalculatePressureAverage(decimal? pressure1Lbs, decimal? pressure2Lbs)
    {
        if (pressure1Lbs is null || pressure2Lbs is null)
        {
            return null;
        }

        return decimal.Round((pressure1Lbs.Value + pressure2Lbs.Value) / 2m, 2);
    }
}
