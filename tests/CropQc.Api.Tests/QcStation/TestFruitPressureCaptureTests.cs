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

        Assert.Equal("Pressure 1", firstSlot);
        Assert.Equal("Pressure 2", secondSlot);
        Assert.Equal(14.25m, capture.Pressure1Lbs);
        Assert.Equal(15.75m, capture.Pressure2Lbs);
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
}
