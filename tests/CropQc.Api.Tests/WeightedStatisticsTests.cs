using CropQc.Shared;

namespace CropQc.Api.Tests;

public sealed class WeightedStatisticsTests
{
    [Fact]
    public void WeightedMean_WeightsValuesByCurrentBins()
    {
        var value = WeightedStatistics.WeightedMean([(10m, 300m), (4m, 100m)]);

        Assert.Equal(8.5m, value);
    }

    [Fact]
    public void SampleStandardDeviation_UsesSampleFormula()
    {
        var value = WeightedStatistics.SampleStandardDeviation([10m, 12m, 14m]);

        Assert.Equal(2m, decimal.Round(value!.Value, 2));
    }

    [Fact]
    public void WeightedSampleStandardDeviation_DoesNotAverageStandardDeviations()
    {
        var combined = WeightedStatistics.WeightedSampleStandardDeviation([
            (10m, 50m),
            (12m, 50m),
            (18m, 25m),
            (20m, 25m)
        ]);

        Assert.Equal(4.59m, decimal.Round(combined!.Value, 2));
        Assert.NotEqual(1.41m, decimal.Round(combined.Value, 2));
    }

    [Fact]
    public void NormalizeChangeToThirtyDays_UsesElapsedDays()
    {
        var change = WeightedStatistics.NormalizeChangeToThirtyDays(10m, 11.5m, 45);

        Assert.Equal(-1m, decimal.Round(change, 2));
    }
}
