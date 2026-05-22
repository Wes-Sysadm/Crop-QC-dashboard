namespace CropQc.QcStation.Fta;

public sealed class FtaStationService(
    StationConfiguration configuration,
    IFtaDevice device,
    IFtaPressureReader pressureReader,
    MockFtaPressureReader? mockPressureReader = null) : IFtaStationService
{
    private readonly List<string> logEntries = [];

    public StationConfiguration Configuration { get; } = configuration;
    public PressureReading? LatestReading { get; private set; }
    public IReadOnlyList<string> LogEntries => logEntries;

    public async Task<FtaDeviceStatus> InitializeAsync(CancellationToken cancellationToken = default)
    {
        Log("Initialize FTA started.");
        return LogStatus("Initialize FTA", await device.InitializeAsync(cancellationToken));
    }

    public async Task<FtaDeviceStatus> CheckStatusAsync(CancellationToken cancellationToken = default)
    {
        Log("Check Status started.");
        return LogStatus("Check Status", await device.CheckStatusAsync(cancellationToken));
    }

    public async Task<FtaDeviceStatus> OpenSetupAsync(CancellationToken cancellationToken = default)
    {
        Log("Open FTA Setup started.");
        return LogStatus("Open FTA Setup", await device.OpenSetupAsync(cancellationToken));
    }

    public async Task<FtaDeviceStatus> StartPressureReadingAsync(CancellationToken cancellationToken = default)
    {
        Log("Start Pressure Reading started.");
        return LogStatus("Start Pressure Reading", await pressureReader.StartPressureReadingAsync(cancellationToken));
    }

    public async Task<PressureReading?> GetLatestPressureReadingAsync(CancellationToken cancellationToken = default)
    {
        Log("Get Latest Reading started.");
        LatestReading = await pressureReader.GetLatestPressureReadingAsync(cancellationToken);
        if (LatestReading is null)
        {
            Log(pressureReader.LastStatusMessage ?? "No pressure reading is available.");
        }
        else
        {
            Log($"Latest pressure reading: {LatestReading.ReadingValueLbs:0.00} lbs ({LatestReading.Source}).");
        }
        return LatestReading;
    }

    public async Task<FtaDeviceStatus> CancelReadingAsync(CancellationToken cancellationToken = default)
    {
        Log("Cancel started.");
        return LogStatus("Cancel", await pressureReader.CancelReadingAsync(cancellationToken));
    }

    public async Task<FtaDeviceStatus> ReturnProbeHomeAsync(CancellationToken cancellationToken = default)
    {
        Log("Return Probe Home started.");
        return LogStatus("Return Probe Home", await device.ReturnProbeHomeAsync(cancellationToken));
    }

    public async Task<FtaDeviceStatus> QuitAsync(CancellationToken cancellationToken = default)
    {
        Log("Quit FTA started.");
        return LogStatus("Quit FTA", await device.QuitAsync(cancellationToken));
    }

    public PressureReading UseMockReading(decimal? manualValueLbs = null)
    {
        if (mockPressureReader is null)
        {
            LatestReading = PressureReading.Failed(PressureReadingSource.Mock, Configuration.StationName, "Mock readings are only available when mock mode is configured.");
            Log(LatestReading.ErrorMessage!);
            return LatestReading;
        }

        LatestReading = manualValueLbs is null
            ? PressureReading.Success(12.5m, PressureReadingSource.Mock, Configuration.StationName)
            : mockPressureReader.SetManualReading(manualValueLbs.Value);
        Log($"Mock reading selected: {LatestReading.ReadingValueLbs:0.00} lbs ({LatestReading.Source}).");
        return LatestReading;
    }

    public void ClearLog() => logEntries.Clear();

    private FtaDeviceStatus LogStatus(string action, FtaDeviceStatus status)
    {
        Log($"{action}: {status.StatusMessage}{(string.IsNullOrWhiteSpace(status.ErrorMessage) ? "" : $" Error: {status.ErrorMessage}")}");
        return status;
    }

    private void Log(string message) =>
        logEntries.Add($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {message}");
}
