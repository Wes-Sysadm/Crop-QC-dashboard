using CropQc.Data;
using CropQc.Data.Entities;

namespace CropQc.Api.Tests;

public sealed class SizeCalculationServiceTests
{
    [Fact]
    public void Calculate_AssignsLargestThresholdFruitQualifiesFor()
    {
        var thresholds = new[]
        {
            new FruitSizeConversionThreshold { FruitType = "Apple", SizeCategory = 48, MinimumWeightGrams = 405m },
            new FruitSizeConversionThreshold { FruitType = "Apple", SizeCategory = 56, MinimumWeightGrams = 354m },
            new FruitSizeConversionThreshold { FruitType = "Apple", SizeCategory = 64, MinimumWeightGrams = 298m }
        };

        var result = SizeCalculationService.Calculate(360m, thresholds);

        Assert.Equal(56, result.SizeCategory);
        Assert.Equal(SizeCalculationService.Sized, result.SizeStatus);
    }

    [Fact]
    public void Calculate_MarksUndersizedWhenBelowSmallestThreshold()
    {
        var thresholds = new[]
        {
            new FruitSizeConversionThreshold { FruitType = "Pear", SizeCategory = 210, MinimumWeightGrams = 87m },
            new FruitSizeConversionThreshold { FruitType = "Pear", SizeCategory = 225, MinimumWeightGrams = 81m }
        };

        var result = SizeCalculationService.Calculate(80m, thresholds);

        Assert.Null(result.SizeCategory);
        Assert.Equal(SizeCalculationService.Undersized, result.SizeStatus);
    }

    [Fact]
    public void CalculatePressureAverage_ReturnsAverageWhenBothPressuresExist()
    {
        var result = SizeCalculationService.CalculatePressureAverage(14.25m, 15.75m);

        Assert.Equal(15.00m, result);
    }
}
