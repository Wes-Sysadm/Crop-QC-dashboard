namespace CropQc.QcStation.Fta;

public sealed class FtaDllPressureReader(StationConfiguration configuration) : IFtaDevice, IFtaPressureReader
{
    public const string FtaDllFileName = "FTA_DLL.dll";
    public const string BorlandMemoryManagerFileName = "borlndmm.dll";

    private bool isInitialized;
    private bool isReading;
    private string? errorMessage;

    public string DeviceName => "FTA DLL";

    public Task<FtaDeviceStatus> InitializeAsync(CancellationToken cancellationToken = default)
    {
        var validationError = ValidateDllFiles();
        if (validationError is not null)
        {
            isInitialized = false;
            errorMessage = validationError;
            return Task.FromResult(Status("FTA DLL initialization failed.", errorMessage));
        }

        // TODO: Add vendor FTA_DLL.dll P/Invoke declarations and initialization call here.
        // This class is intentionally the only place that should load or call the vendor DLL.
        isInitialized = false;
        errorMessage = "FTA DLL files were found, but vendor function bindings are not implemented yet.";
        return Task.FromResult(Status("FTA DLL placeholder reached.", errorMessage));
    }

    public Task<FtaDeviceStatus> CheckStatusAsync(CancellationToken cancellationToken = default)
    {
        var validationError = ValidateDllFiles();
        if (validationError is not null)
        {
            errorMessage = validationError;
            return Task.FromResult(Status("FTA DLL unavailable.", errorMessage));
        }

        return Task.FromResult(Status("FTA DLL files found; hardware status call is not implemented yet.", errorMessage));
    }

    public Task<FtaDeviceStatus> StartPressureReadingAsync(CancellationToken cancellationToken = default)
    {
        if (!isInitialized)
        {
            return Task.FromResult(Status("FTA DLL is not initialized.", errorMessage ?? "Initialize the FTA DLL before reading."));
        }

        // TODO: Call the vendor start-read function here.
        isReading = true;
        return Task.FromResult(Status("FTA pressure reading start placeholder invoked."));
    }

    public Task<PressureReading?> GetLatestPressureReadingAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Call the vendor latest-reading function and convert the value to pounds.
        return Task.FromResult<PressureReading?>(null);
    }

    public Task<FtaDeviceStatus> CancelReadingAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Call the vendor cancel-read function here.
        isReading = false;
        return Task.FromResult(Status("FTA cancel placeholder invoked."));
    }

    public Task<FtaDeviceStatus> ReturnProbeHomeAsync(CancellationToken cancellationToken = default)
    {
        // TODO: Call the vendor probe-home/back function here if supported by the FTA DLL.
        return Task.FromResult(Status("FTA probe-home placeholder invoked."));
    }

    private string? ValidateDllFiles()
    {
        var dllFolder = string.IsNullOrWhiteSpace(configuration.FtaDllPath)
            ? AppContext.BaseDirectory
            : Path.GetFullPath(configuration.FtaDllPath);
        var ftaDll = Path.Combine(dllFolder, FtaDllFileName);
        var memoryManagerDll = Path.Combine(dllFolder, BorlandMemoryManagerFileName);

        if (!File.Exists(ftaDll))
        {
            return $"{FtaDllFileName} was not found in {dllFolder}.";
        }

        if (!File.Exists(memoryManagerDll))
        {
            return $"{BorlandMemoryManagerFileName} was not found in {dllFolder}.";
        }

        return null;
    }

    private FtaDeviceStatus Status(string message, string? error = null) =>
        new(isInitialized, isInitialized, isReading, message, error);
}
