namespace CropQc.QcStation.Fta;

public sealed class MockFtaPressureReader(string stationName) : IFtaDevice, IFtaPressureReader
{
    private readonly Random random = new();
    private bool isInitialized;
    private bool isConnected = true;
    private bool isReading;
    private PressureReading? latestReading;

    public string DeviceName => "Mock FTA";

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
        return Task.FromResult(CurrentStatus(isConnected ? "Mock FTA initialized." : "Mock FTA is disconnected."));
    }

    public Task<FtaDeviceStatus> CheckStatusAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CurrentStatus(isConnected ? "Mock FTA connected." : "Mock FTA disconnected."));

    public Task<FtaDeviceStatus> StartPressureReadingAsync(CancellationToken cancellationToken = default)
    {
        if (!isInitialized || !isConnected)
        {
            return Task.FromResult(CurrentStatus("Mock FTA is not ready.", "Initialize the mock FTA before reading."));
        }

        isReading = true;
        var value = Math.Round((decimal)(random.NextDouble() * 10d + 8d), 2);
        latestReading = PressureReading.Success(value, PressureReadingSource.Mock, stationName);
        isReading = false;
        return Task.FromResult(CurrentStatus($"Mock pressure reading captured: {value:0.00} lbs."));
    }

    public Task<PressureReading?> GetLatestPressureReadingAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(latestReading);

    public Task<FtaDeviceStatus> CancelReadingAsync(CancellationToken cancellationToken = default)
    {
        isReading = false;
        return Task.FromResult(CurrentStatus("Mock reading cancelled."));
    }

    public Task<FtaDeviceStatus> ReturnProbeHomeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CurrentStatus("Mock probe returned home."));

    private FtaDeviceStatus CurrentStatus(string message, string? errorMessage = null) =>
        new(isInitialized, isConnected, isReading, message, errorMessage);
}
