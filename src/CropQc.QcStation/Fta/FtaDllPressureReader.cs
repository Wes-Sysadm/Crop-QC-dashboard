namespace CropQc.QcStation.Fta;

using System.Runtime.InteropServices;

public sealed class FtaDllPressureReader(StationConfiguration configuration, INativeDllLoader? nativeDllLoader = null) : IFtaDevice, IFtaPressureReader
{
    public const string DefaultFtaDllFileName = "FTA_dll.dll";
    public const string AlternateFtaDllFileName = "FTA_DLL.dll";
    public const string BorlandMemoryManagerFileName = "borlndmm.dll";

    private bool isInitialized;
    private bool isConnected;
    private bool isReading;
    private string? errorMessage;
    private DllProbeResult? lastProbe;
    private readonly INativeDllLoader nativeDllLoader = nativeDllLoader ?? new NativeDllLoader();

    public string DeviceName => "FTA DLL";

    public Task<FtaDeviceStatus> InitializeAsync(CancellationToken cancellationToken = default)
    {
        lastProbe = ProbeDllFiles();
        if (!lastProbe.MainDllFound)
        {
            isInitialized = false;
            isConnected = false;
            errorMessage = $"{lastProbe.MainDllFileName} was not found in {lastProbe.DllFolder}.";
            return Task.FromResult(Status("FTA DLL initialization failed.", errorMessage));
        }

        if (!lastProbe.MainDllLoaded)
        {
            isInitialized = false;
            isConnected = false;
            errorMessage = BuildLoadErrorMessage(lastProbe);
            return Task.FromResult(Status("FTA DLL load failed.", errorMessage));
        }

        // TODO: Add vendor FTA_DLL.dll P/Invoke declarations and initialization call here.
        // This class is intentionally the only place that should load or call the vendor DLL.
        isInitialized = true;
        isConnected = true;
        errorMessage = lastProbe.BorlandMemoryManagerFound
            ? null
            : $"{BorlandMemoryManagerFileName} was not found in {lastProbe.DllFolder}. This is warning-only until hardware DLL calls are implemented.";
        return Task.FromResult(Status("FTA DLL loaded; vendor function bindings are not implemented yet.", errorMessage));
    }

    public Task<FtaDeviceStatus> CheckStatusAsync(CancellationToken cancellationToken = default)
    {
        lastProbe = ProbeDllFiles();
        if (!lastProbe.MainDllFound)
        {
            isConnected = false;
            errorMessage = $"{lastProbe.MainDllFileName} was not found in {lastProbe.DllFolder}.";
            return Task.FromResult(Status("FTA DLL unavailable.", errorMessage));
        }

        if (!lastProbe.MainDllLoaded)
        {
            isConnected = false;
            errorMessage = BuildLoadErrorMessage(lastProbe);
            return Task.FromResult(Status("FTA DLL load failed.", errorMessage));
        }

        isConnected = true;
        errorMessage = lastProbe.BorlandMemoryManagerFound
            ? null
            : $"{BorlandMemoryManagerFileName} missing; warning only.";
        return Task.FromResult(Status("FTA DLL load check passed; hardware status call is not implemented yet.", errorMessage));
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

    private DllProbeResult ProbeDllFiles()
    {
        var dllFolder = string.IsNullOrWhiteSpace(configuration.FtaDllPath)
            ? AppContext.BaseDirectory
            : Path.GetFullPath(configuration.FtaDllPath);
        var mainDllFileName = ResolveMainDllFileName(dllFolder);
        var mainDllPath = Path.Combine(dllFolder, mainDllFileName);
        var memoryManagerDllPath = Path.Combine(dllFolder, BorlandMemoryManagerFileName);
        var mainDllFound = File.Exists(mainDllPath);
        var borlandMemoryManagerFound = File.Exists(memoryManagerDllPath);

        if (!mainDllFound)
        {
            return new DllProbeResult(dllFolder, mainDllFileName, mainDllPath, false, false, borlandMemoryManagerFound, null, false);
        }

        var loadResult = nativeDllLoader.TryLoad(mainDllPath);
        return new DllProbeResult(dllFolder, mainDllFileName, mainDllPath, true, loadResult.Loaded, borlandMemoryManagerFound, loadResult.ErrorMessage, loadResult.IsArchitectureMismatch);
    }

    private string ResolveMainDllFileName(string dllFolder)
    {
        var configuredFileName = string.IsNullOrWhiteSpace(configuration.FtaDllFileName)
            ? DefaultFtaDllFileName
            : configuration.FtaDllFileName.Trim();

        if (File.Exists(Path.Combine(dllFolder, configuredFileName)))
        {
            return configuredFileName;
        }

        if (!string.Equals(configuredFileName, DefaultFtaDllFileName, StringComparison.OrdinalIgnoreCase)
            && File.Exists(Path.Combine(dllFolder, DefaultFtaDllFileName)))
        {
            return DefaultFtaDllFileName;
        }

        if (!string.Equals(configuredFileName, AlternateFtaDllFileName, StringComparison.OrdinalIgnoreCase)
            && File.Exists(Path.Combine(dllFolder, AlternateFtaDllFileName)))
        {
            return AlternateFtaDllFileName;
        }

        return configuredFileName;
    }

    private FtaDeviceStatus Status(string message, string? error = null)
    {
        var probe = lastProbe;
        var detail = probe is null
            ? message
            : string.Join(" | ",
                message,
                $"DLL folder: {probe.DllFolder}",
                $"Main DLL: {probe.MainDllFileName}",
                $"Main DLL found: {YesNo(probe.MainDllFound)}",
                $"Main DLL load check: {YesNo(probe.MainDllLoaded)}",
                $"{BorlandMemoryManagerFileName} found: {YesNo(probe.BorlandMemoryManagerFound)}",
                $"Process architecture: {RuntimeInformation.ProcessArchitecture}",
                $"OS architecture: {RuntimeInformation.OSArchitecture}",
                "Ready for actual function calls: No; vendor P/Invoke bindings are not implemented yet");
        return new(isInitialized, isConnected, isReading, detail, error);
    }

    private static string? BuildLoadErrorMessage(DllProbeResult probe)
    {
        if (string.IsNullOrWhiteSpace(probe.LoadErrorMessage))
        {
            return null;
        }

        if (!probe.IsArchitectureMismatch)
        {
            return probe.LoadErrorMessage;
        }

        return string.Join(" ",
            probe.LoadErrorMessage,
            "This usually means a 32-bit/64-bit mismatch.",
            $"Current process architecture: {RuntimeInformation.ProcessArchitecture}.",
            $"OS architecture: {RuntimeInformation.OSArchitecture}.",
            "The FTA_dll.dll is likely 32-bit; run the QC Station as x86 for RealDll testing.");
    }

    private static string YesNo(bool value) => value ? "Yes" : "No";

    private sealed record DllProbeResult(
        string DllFolder,
        string MainDllFileName,
        string MainDllPath,
        bool MainDllFound,
        bool MainDllLoaded,
        bool BorlandMemoryManagerFound,
        string? LoadErrorMessage,
        bool IsArchitectureMismatch);
}
