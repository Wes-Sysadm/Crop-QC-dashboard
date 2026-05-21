using CropQc.QcStation.Fta;

namespace CropQc.Api.Tests.QcStation;

public sealed class FtaStationServiceTests
{
    [Fact]
    public async Task Station_service_logs_status_and_latest_reading()
    {
        var configuration = new StationConfiguration { StationName = "Station Service Test", FtaMode = FtaMode.Mock };
        var reader = new MockFtaPressureReader(configuration.StationName);
        var service = new FtaStationService(configuration, reader, reader, reader);

        await service.InitializeAsync();
        await service.StartPressureReadingAsync();
        var reading = await service.GetLatestPressureReadingAsync();

        Assert.NotNull(reading);
        Assert.Equal(reading, service.LatestReading);
        Assert.Contains(service.LogEntries, x => x.Contains("Initialize FTA", StringComparison.Ordinal));
        Assert.Contains(service.LogEntries, x => x.Contains("Latest pressure reading", StringComparison.Ordinal));
    }

    [Fact]
    public void Station_service_can_use_manual_mock_reading()
    {
        var configuration = new StationConfiguration { StationName = "Manual Station", FtaMode = FtaMode.Mock };
        var reader = new MockFtaPressureReader(configuration.StationName);
        var service = new FtaStationService(configuration, reader, reader, reader);

        var reading = service.UseMockReading(16.75m);

        Assert.Equal(16.75m, reading.ReadingValueLbs);
        Assert.Equal(PressureReadingSource.Manual, reading.Source);
        Assert.Equal(reading, service.LatestReading);
    }
}
