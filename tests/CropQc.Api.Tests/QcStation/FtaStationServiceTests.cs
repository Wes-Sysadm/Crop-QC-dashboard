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

    [Fact]
    public async Task Quit_calls_cancel_before_quit_and_logs_each_step()
    {
        var configuration = new StationConfiguration { StationName = "Quit Station", FtaMode = FtaMode.RealDll };
        var calls = new List<string>();
        var device = new RecordingFtaDevice(calls);
        var reader = new RecordingFtaPressureReader(calls);
        var service = new FtaStationService(configuration, device, reader);

        var status = await service.QuitAsync();

        Assert.Equal(["Cancel", "Quit"], calls);
        Assert.False(status.IsInitialized);
        Assert.Contains(service.LogEntries, x => x.Contains("FTACancel", StringComparison.Ordinal));
        Assert.Contains(service.LogEntries, x => x.Contains("FTAQuit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Quit_continues_to_quit_when_cancel_throws()
    {
        var configuration = new StationConfiguration { StationName = "Quit Station", FtaMode = FtaMode.RealDll };
        var calls = new List<string>();
        var device = new RecordingFtaDevice(calls);
        var reader = new RecordingFtaPressureReader(calls) { ThrowOnCancel = true };
        var service = new FtaStationService(configuration, device, reader);

        var status = await service.QuitAsync();

        Assert.Equal(["Cancel", "Quit"], calls);
        Assert.False(status.IsInitialized);
        Assert.Contains(service.LogEntries, x => x.Contains("continuing to FTAQuit", StringComparison.Ordinal));
    }

    private sealed class RecordingFtaDevice(List<string> calls) : IFtaDevice
    {
        public string DeviceName => "Recording FTA";

        public Task<FtaDeviceStatus> InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Ready("Initialized."));

        public Task<FtaDeviceStatus> InitializeWithConfigPathAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Ready("Initialized with config."));

        public Task<FtaDeviceStatus> CheckStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Ready("Ready."));

        public Task<FtaDeviceStatus> DiagnosticStatusAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Ready("Diagnostic ready."));

        public Task<FtaDeviceStatus> OpenSetupAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Ready("Setup opened."));

        public Task<FtaDeviceStatus> ReturnProbeHomeAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Ready("Returned home."));

        public Task<FtaDeviceStatus> QuitAsync(CancellationToken cancellationToken = default)
        {
            calls.Add("Quit");
            return Task.FromResult(new FtaDeviceStatus(false, false, false, "FTAQuit completed."));
        }

        private static FtaDeviceStatus Ready(string message) => new(true, true, false, message);
    }

    private sealed class RecordingFtaPressureReader(List<string> calls) : IFtaPressureReader
    {
        public bool ThrowOnCancel { get; init; }
        public string? LastStatusMessage { get; private set; }

        public Task<FtaDeviceStatus> StartPressureReadingAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Ready("Started."));

        public Task<PressureReading?> StartAutoFirmnessReadingAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<PressureReading?>(null);

        public Task<PressureReading?> StartAndWaitManualFirmnessReadingAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<PressureReading?>(null);

        public Task<PressureReading?> DemoStylePollReadingAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<PressureReading?>(null);

        public Task<PressureReading?> DemoStyleAutoReadingAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<PressureReading?>(null);

        public Task<PressureReading?> DemoStyleManualButtonReadingAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<PressureReading?>(null);

        public Task<PressureReading?> GetLatestPressureReadingAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<PressureReading?>(null);

        public Task<FtaDeviceStatus> CancelReadingAsync(CancellationToken cancellationToken = default)
        {
            calls.Add("Cancel");
            if (ThrowOnCancel)
            {
                throw new InvalidOperationException("Cancel failed.");
            }

            LastStatusMessage = "FTACancel completed.";
            return Task.FromResult(Ready(LastStatusMessage));
        }

        private static FtaDeviceStatus Ready(string message) => new(true, true, false, message);
    }
}
