using CropQc.Data;

namespace CropQc.Api.Tests;

public sealed class PressureCalculationServiceTests
{
    [Fact]
    public void OverallAverage_UsesEveryValidSideReadingAndIgnoresBlanks()
    {
        var average = PressureCalculationService.CalculateOverallAverage(
            [(12m, 13m), (11m, null), (null, null)]);

        Assert.Equal(12m, average);
    }

    [Fact]
    public void OverallAverage_ReturnsNullWhenNoSidesWereRead()
    {
        Assert.Null(PressureCalculationService.CalculateOverallAverage([(null, null)]));
    }

    [Fact]
    public void ValidSideReadings_PreservesRawSidesWithoutAddingZeroes()
    {
        var readings = PressureCalculationService.ValidSideReadings([(10m, null), (null, 14m)]);

        Assert.Equal([10m, 14m], readings);
    }
}
