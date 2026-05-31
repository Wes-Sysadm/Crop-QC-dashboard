using CropQc.QcStation.Fta;

namespace CropQc.Api.Tests.QcStation;

public sealed class TestFruitPressureCaptureTests
{
    [Fact]
    public void Capture_AutoAdvance_AssignsFirstReadingToPressure1AndSecondToPressure2()
    {
        var capture = new TestFruitPressureCapture { FruitNumber = 7 };
        var first = PressureReading.Success(14.25m, PressureReadingSource.FTA, "Station A");
        var second = PressureReading.Success(15.75m, PressureReadingSource.FTA, "Station A");

        var firstSlot = capture.Capture(first, PressureCaptureTarget.AutoAdvance);
        var secondSlot = capture.Capture(second, PressureCaptureTarget.AutoAdvance);

        var row = capture.Rows.Single(x => x.FruitNumber == 7);
        Assert.Equal("Pressure 1", firstSlot);
        Assert.Equal("Pressure 2", secondSlot);
        Assert.Equal(14.25m, row.Pressure1Lbs);
        Assert.Equal(15.75m, row.Pressure2Lbs);
        Assert.Equal("Complete", row.Status);
        Assert.Equal(8, capture.FruitNumber);
        Assert.Equal("Pressure 1", capture.CurrentTargetSlot);
        Assert.Equal(2, capture.History.Count);
        Assert.All(capture.History, entry => Assert.Equal(7, entry.FruitNumber));
    }

    [Fact]
    public void AveragePressureLbs_ReturnsAverageWhenBothPressuresAreCaptured()
    {
        var capture = new TestFruitPressureCapture();

        capture.Capture(PressureReading.Success(14.25m, PressureReadingSource.FTA, "Station A"), PressureCaptureTarget.Pressure1);
        capture.Capture(PressureReading.Success(15.75m, PressureReadingSource.FTA, "Station A"), PressureCaptureTarget.Pressure2);

        Assert.Equal(15.00m, capture.AveragePressureLbs);
    }

    [Fact]
    public void Capture_DirectTarget_MapsManualReadingToRequestedSlot()
    {
        var capture = new TestFruitPressureCapture { FruitNumber = 2 };
        var reading = PressureReading.Success(16.5m, PressureReadingSource.FTA, "Station A");

        var slot = capture.Capture(reading, PressureCaptureTarget.Pressure2);

        Assert.Equal("Pressure 2", slot);
        Assert.Null(capture.Pressure1Lbs);
        Assert.Equal(16.5m, capture.Pressure2Lbs);
        Assert.Equal(reading, capture.LastCapturedReading);
        var history = Assert.Single(capture.History);
        Assert.Equal(16.5m, history.PressureValueLbs);
        Assert.Equal("Pressure 2", history.TargetSlot);
    }

    [Fact]
    public void Capture_AutoAdvance_CompletingPressure2MovesToNextFruitPressure1()
    {
        var capture = new TestFruitPressureCapture();

        capture.Capture(PressureReading.Success(10m, PressureReadingSource.FTA, "Station A"), PressureCaptureTarget.AutoAdvance);
        capture.Capture(PressureReading.Success(12m, PressureReadingSource.FTA, "Station A"), PressureCaptureTarget.AutoAdvance);

        Assert.Equal(2, capture.FruitNumber);
        Assert.Equal("Pressure 1", capture.CurrentTargetSlot);
        var firstFruit = capture.Rows.Single(x => x.FruitNumber == 1);
        Assert.Equal(11m, firstFruit.AveragePressureLbs);
        Assert.Equal("Complete", firstFruit.Status);
    }

    [Fact]
    public void Capture_AutoAdvance_StopsAtFruit25Pressure2()
    {
        var capture = new TestFruitPressureCapture();

        for (var i = 0; i < TestFruitPressureCapture.MaxFruitCount * 2; i++)
        {
            capture.Capture(PressureReading.Success(10m + i, PressureReadingSource.FTA, "Station A"), PressureCaptureTarget.AutoAdvance);
        }

        Assert.Equal(25, capture.FruitNumber);
        Assert.Equal("Sample Complete", capture.CurrentTargetSlot);
        Assert.True(capture.IsSampleComplete);
        Assert.All(capture.Rows, row => Assert.Equal("Complete", row.Status));
    }

    [Fact]
    public void LoadRows_StartsAtFirstMissingPressureSlot()
    {
        var capture = new TestFruitPressureCapture();

        capture.LoadRows(
        [
            new FruitPressureCaptureRow(1, 10m, 11m, 10.5m, "Complete"),
            new FruitPressureCaptureRow(2, 12m, null, null, "Missing P2"),
            new FruitPressureCaptureRow(3, null, null, null, "Missing P1")
        ]);

        Assert.Equal(2, capture.FruitNumber);
        Assert.Equal("Pressure 2", capture.CurrentTargetSlot);
    }

    [Fact]
    public void ShouldCaptureReading_PreventsDuplicateReadingId()
    {
        var capture = new TestFruitPressureCapture();
        var reading = PressureReading.Success(12.5m, PressureReadingSource.FTA, "Station A");

        Assert.True(capture.ShouldCaptureReading(reading));
        capture.Capture(reading, PressureCaptureTarget.AutoAdvance);

        Assert.False(capture.ShouldCaptureReading(reading));
    }

    [Fact]
    public void StationConfiguration_DefaultsToSmoothSafeManualRearm()
    {
        var configuration = new StationConfiguration();

        Assert.True(configuration.FtaManualCaptureSafeMode);
        Assert.Equal(750, configuration.FtaManualRearmDelayMs);
        Assert.Equal(150, configuration.FtaHomePollIntervalMs);
        Assert.Equal(5000, configuration.FtaMaxHomeWaitMs);
    }
}
