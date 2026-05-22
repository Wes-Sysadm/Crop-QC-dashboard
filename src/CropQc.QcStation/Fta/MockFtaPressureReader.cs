namespace CropQc.QcStation.Fta;

public sealed class MockFtaPressureReader(string stationName) : IFtaDevice, IFtaPressureReader
{
    private readonly Random random = new();
    private bool isInitialized;
    private bool isConnected = true;
    private bool isReading;
    private PressureReading? latestReading;

    public string DeviceName => "Mock FTA";
    public string? LastStatusMessage { get; private set; }

    public void SetConnected(bool connected) => isConnected = connected;

    public PressureReading SetManualReading(decimal readingValueLbs)
    {
        latestReading = PressureReading.Success(readingValueLbs, PressureReadingSource.Manual, stationName);
        isReading = false;
        return latestReading;
    }

    public Task<FtaDeviceStatus> InitializeAsync(CancellationToken cancellationToken = default)
    {
        isInitialized = isConnected;
        LastStatusMessage = isConnected ? "Mock FTA initialized." : "Mock FTA is disconnected.";
        return Task.FromResult(CurrentStatus(LastStatusMessage));
    }

    public Task<FtaDeviceStatus> CheckStatusAsync(CancellationToken cancellationToken = default)
    {
        LastStatusMessage = isConnected ? "Mock FTA connected." : "Mock FTA disconnected.";
        return Task.FromResult(CurrentStatus(LastStatusMessage));
    }

    public Task<FtaDeviceStatus> DiagnosticStatusAsync(CancellationToken cancellationToken = default)
    {
        LastStatusMessage = string.Join(" | ",
            "Mock diagnostic status.",
            "FTAStatus raw value: 0",
            "FTABitStatus(1) new firmness: No",
            $"FTABitStatus(3) interface connected: {YesNo(isConnected)}",
            "FTABitStatus(5) probe at top: Yes",
            "FTABitStatus(6) probe at bottom: No",
            $"FTABitStatus(7) FTA responded: {YesNo(isConnected)}",
            "FTABitStatus(8) new mass reading: No",
            "FTABitStatus(9) scale attached/can measure mass: No");
        return Task.FromResult(CurrentStatus(LastStatusMessage));
    }

    public Task<FtaDeviceStatus> StartPressureReadingAsync(CancellationToken cancellationToken = default)
    {
        if (!isInitialized || !isConnected)
        {
            LastStatusMessage = "Mock FTA is not ready.";
            return Task.FromResult(CurrentStatus(LastStatusMessage, "Initialize the mock FTA before reading."));
        }

        isReading = true;
        var value = Math.Round((decimal)(random.NextDouble() * 10d + 8d), 2);
        latestReading = PressureReading.Success(value, PressureReadingSource.Mock, stationName);
        isReading = false;
        LastStatusMessage = $"Mock pressure reading captured: {value:0.00} lbs.";
        return Task.FromResult(CurrentStatus(LastStatusMessage));
    }

    public async Task<PressureReading?> StartAutoFirmnessReadingAsync(CancellationToken cancellationToken = default)
    {
        await StartPressureReadingAsync(cancellationToken);
        LastStatusMessage = latestReading is null
            ? LastStatusMessage
            : $"Mock auto firmness reading captured: {latestReading.ReadingValueLbs:0.00} lbs.";
        return latestReading;
    }

    public async Task<PressureReading?> StartAndWaitManualFirmnessReadingAsync(CancellationToken cancellationToken = default)
    {
        await StartPressureReadingAsync(cancellationToken);
        LastStatusMessage = latestReading is null
            ? LastStatusMessage
            : $"Mock manual/button firmness reading captured: {latestReading.ReadingValueLbs:0.00} lbs.";
        return latestReading;
    }

    public Task<PressureReading?> GetLatestPressureReadingAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(latestReading);

    public Task<FtaDeviceStatus> CancelReadingAsync(CancellationToken cancellationToken = default)
    {
        isReading = false;
        LastStatusMessage = "Mock reading cancelled.";
        return Task.FromResult(CurrentStatus(LastStatusMessage));
    }

    public Task<FtaDeviceStatus> OpenSetupAsync(CancellationToken cancellationToken = default)
    {
        LastStatusMessage = "Mock setup dialog placeholder opened.";
        return Task.FromResult(CurrentStatus(LastStatusMessage));
    }

    public Task<FtaDeviceStatus> ReturnProbeHomeAsync(CancellationToken cancellationToken = default)
    {
        LastStatusMessage = "Mock probe returned home.";
        return Task.FromResult(CurrentStatus(LastStatusMessage));
    }

    public Task<FtaDeviceStatus> QuitAsync(CancellationToken cancellationToken = default)
    {
        isInitialized = false;
        isReading = false;
        LastStatusMessage = "Mock FTA disconnected.";
        return Task.FromResult(CurrentStatus(LastStatusMessage));
    }

    private FtaDeviceStatus CurrentStatus(string message, string? errorMessage = null) =>
        new(isInitialized, isConnected, isReading, message, errorMessage);

    private static string YesNo(bool value) => value ? "Yes" : "No";
}
