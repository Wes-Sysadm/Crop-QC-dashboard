using CropQc.Api.Services;

namespace CropQc.Api.Tests;

public sealed class ReadinessEvaluatorTests
{
    [Fact]
    public void Evaluate_ReturnsReadyWhenRowsAndPhotosAreComplete()
    {
        var input = new ReadinessEvaluationInput(
            true,
            "Receiving Sample",
            [new ReadinessFruitRow(true, true, true, true, true, true)],
            true,
            true,
            true,
            true,
            true,
            true);

        var result = ReadinessEvaluator.Evaluate(input);

        Assert.True(result.IsReady);
        Assert.Empty(result.MissingItems);
        Assert.Equal(1, result.CompletedFruitCount);
        Assert.Equal(0, result.StarchMissingCount);
    }

    [Fact]
    public void Evaluate_DoesNotRequireHectreForTransferSamples()
    {
        var input = new ReadinessEvaluationInput(
            true,
            "Transfer Sample",
            [new ReadinessFruitRow(true, true, true, true, true, true)],
            true,
            true,
            false,
            true,
            true,
            true);

        var result = ReadinessEvaluator.Evaluate(input);

        Assert.True(result.IsReady);
        Assert.DoesNotContain(result.MissingItems, x => x.Contains("Hectre", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Evaluate_ReportsMissingStarchAndPhotos()
    {
        var input = new ReadinessEvaluationInput(
            true,
            "Receiving Sample",
            [new ReadinessFruitRow(true, true, true, true, true, false)],
            false,
            false,
            false,
            true,
            false,
            false);

        var result = ReadinessEvaluator.Evaluate(input);

        Assert.False(result.IsReady);
        Assert.Equal(1, result.CompletedFruitCount);
        Assert.Equal(1, result.StarchMissingCount);
        Assert.Contains(result.MissingItems, x => x.Contains("Starch", StringComparison.OrdinalIgnoreCase));
        Assert.False(result.PhotoStatus.HasBinTruck);
        Assert.False(result.PhotoStatus.HasTopOfTruck);
        Assert.False(result.PhotoStatus.HasHectre);
        Assert.False(result.PhotoStatus.HasCutFruit);
        Assert.False(result.PhotoStatus.HasFruitAfterStarch);
    }

    [Theory]
    [InlineData("Receiving Sample")]
    [InlineData("Truck Sample")]
    public void Evaluate_RequiresStarchForTruckSamples(string sampleType)
    {
        var input = ReadyInput(sampleType, hasStarch: false);

        var result = ReadinessEvaluator.Evaluate(input);

        Assert.False(result.IsReady);
        Assert.Equal(1, result.StarchMissingCount);
        Assert.Contains(result.MissingItems, x => x.Contains("Starch", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Lot Sample")]
    [InlineData("Door Sample")]
    public void Evaluate_DoesNotRequireStarchForLotOrDoorSamples(string sampleType)
    {
        var input = ReadyInput(sampleType, hasStarch: false);

        var result = ReadinessEvaluator.Evaluate(input);

        Assert.True(result.IsReady);
        Assert.Equal(1, result.StarchMissingCount);
        Assert.DoesNotContain(result.MissingItems, x => x.Contains("Starch", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("Receiving Sample")]
    [InlineData("Door Sample")]
    [InlineData("Lot Sample")]
    [InlineData("Field Sample")]
    public void Evaluate_PearRequiresStarchPhotoButNumericReadingIsOptional(string sampleType)
    {
        var missingPhoto = ReadinessEvaluator.Evaluate(new ReadinessEvaluationInput(
            true,
            sampleType,
            [new ReadinessFruitRow(true, true, true, true, true, false)],
            false,
            true,
            true,
            true,
            true,
            false,
            "Pear"));
        var complete = ReadinessEvaluator.Evaluate(new ReadinessEvaluationInput(
            true,
            sampleType,
            [new ReadinessFruitRow(true, true, true, true, true, false)],
            false,
            true,
            true,
            true,
            true,
            true,
            "Pear"));

        Assert.False(missingPhoto.IsReady);
        Assert.Contains(missingPhoto.MissingItems, x => x.Contains("Starch pears", StringComparison.OrdinalIgnoreCase));
        Assert.True(complete.IsReady);
        Assert.Equal(1, complete.StarchMissingCount);
    }

    [Fact]
    public void Evaluate_TruckPhotoNeverBlocksReadiness()
    {
        var result = ReadinessEvaluator.Evaluate(ReadyInput("Receiving Sample", hasStarch: true) with
        {
            HasBinTruckPhoto = false
        });

        Assert.True(result.IsReady);
        Assert.DoesNotContain(result.MissingItems, x => x.Contains("truck photo", StringComparison.OrdinalIgnoreCase));
    }

    private static ReadinessEvaluationInput ReadyInput(string sampleType, bool hasStarch) =>
        new(
            true,
            sampleType,
            [new ReadinessFruitRow(true, true, true, true, true, hasStarch)],
            true,
            true,
            true,
            true,
            true,
            true);
}
