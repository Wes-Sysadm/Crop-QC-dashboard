using CropQc.QcStation.Fta;

namespace CropQc.Api.Tests.QcStation;

public sealed class MockFtaPressureReaderTests
{
    [Fact]
    public async Task Mock_reader_generates_pressure_reading_without_hardware()
    {
        var reader = new MockFtaPressureReader("Station A");

        var initializeStatus = await reader.InitializeAsync();
        var readStatus = await reader.StartPressureReadingAsync();
        var reading = await reader.GetLatestPressureReadingAsync();

        Assert.True(initializeStatus.IsConnected);
        Assert.True(readStatus.IsConnected);
        Assert.NotNull(reading);
        Assert.Equal(PressureReadingSource.Mock, reading.Source);
        Assert.True(reading.ReadingValueLbs > 0m);
        Assert.Equal("Station A", reading.StationName);
    }

    [Fact]
    public void Manual_mock_reading_uses_manual_source()
    {
        var reader = new MockFtaPressureReader("Station B");

        var reading = reader.SetManualReading(14.25m);

        Assert.Equal(14.25m, reading.ReadingValueLbs);
        Assert.Equal(PressureReadingSource.Manual, reading.Source);
        Assert.Equal("Captured", reading.Status);
    }
}
