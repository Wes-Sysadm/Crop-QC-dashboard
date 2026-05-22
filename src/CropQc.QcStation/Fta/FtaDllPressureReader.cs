using System.Runtime.InteropServices;

namespace CropQc.QcStation.Fta;

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
    private FtaNativeBindings? bindings;
    private PressureReading? latestReading;
    private readonly INativeDllLoader nativeDllLoader = nativeDllLoader ?? new NativeDllLoader();

    public string DeviceName => "FTA DLL";
    public string? LastStatusMessage { get; private set; }

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

        var bindError = EnsureBindings();
        if (bindError is not null)
        {
            isInitialized = false;
            isConnected = false;
            errorMessage = bindError;
            return Task.FromResult(Status("FTA DLL binding failed.", errorMessage));
        }

        try
        {
            bindings!.FTAInit();
            isInitialized = true;
            var statusSnapshot = ReadStatusSnapshot("FTAInit completed.");
            isConnected = statusSnapshot.InterfaceConnected || statusSnapshot.FtaResponded;
            errorMessage = lastProbe.BorlandMemoryManagerFound
                ? null
                : $"{BorlandMemoryManagerFileName} was not found in {lastProbe.DllFolder}. This is warning-only.";
            LastStatusMessage = statusSnapshot.Message;
            return Task.FromResult(Status(statusSnapshot.Message, errorMessage));
        }
        catch (Exception ex)
        {
            isInitialized = false;
            isConnected = false;
            errorMessage = $"FTAInit failed: {ex.Message}";
            LastStatusMessage = errorMessage;
            return Task.FromResult(Status("FTA DLL initialization failed.", errorMessage));
        }
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

        var bindError = EnsureBindings();
        if (bindError is not null)
        {
            isConnected = false;
            errorMessage = bindError;
            return Task.FromResult(Status("FTA DLL binding failed.", errorMessage));
        }

        var statusSnapshot = ReadStatusSnapshot("FTA status check completed.");
        isConnected = statusSnapshot.InterfaceConnected || statusSnapshot.FtaResponded;
        errorMessage = lastProbe.BorlandMemoryManagerFound
            ? null
            : $"{BorlandMemoryManagerFileName} missing; warning only.";
        LastStatusMessage = statusSnapshot.Message;
        return Task.FromResult(Status(statusSnapshot.Message, errorMessage));
    }

    public Task<FtaDeviceStatus> DiagnosticStatusAsync(CancellationToken cancellationToken = default)
    {
        var readyStatus = EnsureReadyForFunctionCall();
        if (readyStatus is not null)
        {
            return Task.FromResult(readyStatus);
        }

        var statusSnapshot = ReadStatusSnapshot("FTA Diagnostic Status.");
        isConnected = statusSnapshot.InterfaceConnected || statusSnapshot.FtaResponded;
        LastStatusMessage = statusSnapshot.Message;
        return Task.FromResult(Status(statusSnapshot.Message, errorMessage));
    }

    public Task<FtaDeviceStatus> OpenSetupAsync(CancellationToken cancellationToken = default)
    {
        var readyStatus = EnsureReadyForFunctionCall();
        if (readyStatus is not null)
        {
            return Task.FromResult(readyStatus);
        }

        try
        {
            bindings!.FTASetup();
            LastStatusMessage = "FTASetup completed. The FTA setup dialog should be available for serial/USB configuration.";
            return Task.FromResult(Status(LastStatusMessage, errorMessage));
        }
        catch (Exception ex)
        {
            errorMessage = $"FTASetup failed: {ex.Message}";
            LastStatusMessage = errorMessage;
            return Task.FromResult(Status("FTA setup failed.", errorMessage));
        }
    }

    public Task<FtaDeviceStatus> StartPressureReadingAsync(CancellationToken cancellationToken = default)
    {
        var readyStatus = EnsureReadyForFunctionCall();
        if (readyStatus is not null)
        {
            return Task.FromResult(readyStatus);
        }

        try
        {
            var beforeSnapshot = ReadStatusSnapshot("Before FTADoFirmnessReading.");
            bindings!.FTADoFirmnessReading();
            var afterSnapshot = ReadStatusSnapshot("After FTADoFirmnessReading.");
            isReading = true;
            var waitingMessage = afterSnapshot.NewFirmnessAvailable
                ? null
                : "FTADoFirmnessReading call returned, but no new reading detected yet. Confirm FTA setup COM port and probe state.";
            LastStatusMessage = string.Join(" | ",
                "FTADoFirmnessReading completed.",
                beforeSnapshot.Message,
                afterSnapshot.Message,
                waitingMessage).TrimEnd(' ', '|');
            return Task.FromResult(Status(LastStatusMessage, errorMessage));
        }
        catch (Exception ex)
        {
            errorMessage = $"FTADoFirmnessReading failed: {ex.Message}";
            LastStatusMessage = errorMessage;
            return Task.FromResult(Status("FTA pressure reading start failed.", errorMessage));
        }
    }

    public Task<PressureReading?> GetLatestPressureReadingAsync(CancellationToken cancellationToken = default)
    {
        var readyStatus = EnsureReadyForFunctionCall();
        if (readyStatus is not null)
        {
            LastStatusMessage = readyStatus.ErrorMessage ?? readyStatus.StatusMessage;
            return Task.FromResult<PressureReading?>(null);
        }

        try
        {
            var hasNewFirmness = bindings!.FTABitStatus(1) != 0;
            if (!hasNewFirmness)
            {
                LastStatusMessage = "No new firmness reading is available. FTABitStatus(1) returned false.";
                return Task.FromResult<PressureReading?>(null);
            }

            var maxFirmness = bindings.FTAReadMaxFirmness();
            if (maxFirmness == -1f)
            {
                LastStatusMessage = "FTAReadMaxFirmness returned -1; no valid firmness reading is available.";
                return Task.FromResult<PressureReading?>(null);
            }

            latestReading = PressureReading.Success((decimal)maxFirmness, PressureReadingSource.FTA, configuration.StationName);
            isReading = false;
            LastStatusMessage = $"FTAReadMaxFirmness returned {maxFirmness:0.00} lbs.";
            return Task.FromResult<PressureReading?>(latestReading);
        }
        catch (Exception ex)
        {
            errorMessage = $"FTAReadMaxFirmness failed: {ex.Message}";
            LastStatusMessage = errorMessage;
            return Task.FromResult<PressureReading?>(null);
        }
    }

    public Task<FtaDeviceStatus> CancelReadingAsync(CancellationToken cancellationToken = default)
    {
        var readyStatus = EnsureReadyForFunctionCall();
        if (readyStatus is not null)
        {
            return Task.FromResult(readyStatus);
        }

        try
        {
            bindings!.FTACancel();
            isReading = false;
            LastStatusMessage = "FTACancel completed.";
            return Task.FromResult(Status(LastStatusMessage, errorMessage));
        }
        catch (Exception ex)
        {
            errorMessage = $"FTACancel failed: {ex.Message}";
            LastStatusMessage = errorMessage;
            return Task.FromResult(Status("FTA cancel failed.", errorMessage));
        }
    }

    public Task<FtaDeviceStatus> ReturnProbeHomeAsync(CancellationToken cancellationToken = default)
    {
        var readyStatus = EnsureReadyForFunctionCall();
        if (readyStatus is not null)
        {
            return Task.FromResult(readyStatus);
        }

        try
        {
            bindings!.FTABack();
            LastStatusMessage = "FTABack completed.";
            return Task.FromResult(Status(LastStatusMessage, errorMessage));
        }
        catch (Exception ex)
        {
            errorMessage = $"FTABack failed: {ex.Message}";
            LastStatusMessage = errorMessage;
            return Task.FromResult(Status("FTA return probe home failed.", errorMessage));
        }
    }

    public Task<FtaDeviceStatus> QuitAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            bindings?.FTAQuit();
            LastStatusMessage = bindings is null ? "FTAQuit skipped; native functions are not bound." : "FTAQuit completed.";
        }
        catch (Exception ex)
        {
            errorMessage = $"FTAQuit failed: {ex.Message}";
            LastStatusMessage = errorMessage;
        }
        finally
        {
            isInitialized = false;
            isConnected = false;
            isReading = false;
            latestReading = null;
            bindings = null;
            if (lastProbe?.NativeLibraryHandle is { } handle && handle != IntPtr.Zero)
            {
                nativeDllLoader.Free(handle);
                lastProbe = lastProbe with { NativeLibraryHandle = IntPtr.Zero };
            }
        }

        return Task.FromResult(Status(LastStatusMessage ?? "FTA interface disconnected.", errorMessage));
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
            return new DllProbeResult(dllFolder, mainDllFileName, mainDllPath, false, false, borlandMemoryManagerFound, null, false, IntPtr.Zero);
        }

        if (lastProbe?.NativeLibraryHandle is { } existingHandle && existingHandle != IntPtr.Zero && string.Equals(lastProbe.MainDllPath, mainDllPath, StringComparison.OrdinalIgnoreCase))
        {
            return lastProbe with
            {
                MainDllFound = true,
                MainDllLoaded = true,
                BorlandMemoryManagerFound = borlandMemoryManagerFound,
                LoadErrorMessage = null,
                IsArchitectureMismatch = false
            };
        }

        var loadResult = nativeDllLoader.TryLoad(mainDllPath);
        return new DllProbeResult(dllFolder, mainDllFileName, mainDllPath, true, loadResult.Loaded, borlandMemoryManagerFound, loadResult.ErrorMessage, loadResult.IsArchitectureMismatch, loadResult.NativeLibraryHandle);
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
                $"Ready for actual function calls: {YesNo(isInitialized && bindings is not null)}");
        return new(isInitialized, isConnected, isReading, detail, error);
    }

    private FtaDeviceStatus? EnsureReadyForFunctionCall()
    {
        if (!isInitialized || bindings is null)
        {
            var message = errorMessage ?? "Initialize the FTA DLL before calling FTA functions.";
            LastStatusMessage = message;
            return Status("FTA DLL is not initialized.", message);
        }

        return null;
    }

    private string? EnsureBindings()
    {
        if (bindings is not null)
        {
            return null;
        }

        var handle = lastProbe?.NativeLibraryHandle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
        {
            return "FTA DLL was loaded without a native library handle; cannot bind exported functions.";
        }

        return TryBindNativeFunctions(handle, out bindings);
    }

    private string? TryBindNativeFunctions(IntPtr nativeLibraryHandle, out FtaNativeBindings? nativeBindings)
    {
        nativeBindings = null;
        var init = Bind<FTAInit>(nativeLibraryHandle, nameof(FTAInit), out var error);
        if (error is not null) return error;
        var setup = Bind<FTASetup>(nativeLibraryHandle, nameof(FTASetup), out error);
        if (error is not null) return error;
        var status = Bind<FTAStatus>(nativeLibraryHandle, nameof(FTAStatus), out error);
        if (error is not null) return error;
        var bitStatus = Bind<FTABitStatus>(nativeLibraryHandle, nameof(FTABitStatus), out error);
        if (error is not null) return error;
        var doFirmnessReading = Bind<FTADoFirmnessReading>(nativeLibraryHandle, nameof(FTADoFirmnessReading), out error);
        if (error is not null) return error;
        var readMaxFirmness = Bind<FTAReadMaxFirmness>(nativeLibraryHandle, nameof(FTAReadMaxFirmness), out error);
        if (error is not null) return error;
        var readLastFirmness = Bind<FTAReadLastFirmness>(nativeLibraryHandle, nameof(FTAReadLastFirmness), out error);
        if (error is not null) return error;
        var cancel = Bind<FTACancel>(nativeLibraryHandle, nameof(FTACancel), out error);
        if (error is not null) return error;
        var back = Bind<FTABack>(nativeLibraryHandle, nameof(FTABack), out error);
        if (error is not null) return error;
        var quit = Bind<FTAQuit>(nativeLibraryHandle, nameof(FTAQuit), out error);
        if (error is not null) return error;

        nativeBindings = new FtaNativeBindings(init!, setup!, status!, bitStatus!, doFirmnessReading!, readMaxFirmness!, readLastFirmness!, cancel!, back!, quit!);
        return null;
    }

    private TDelegate? Bind<TDelegate>(IntPtr nativeLibraryHandle, string exportName, out string? error)
        where TDelegate : Delegate
    {
        if (!nativeDllLoader.TryGetExport(nativeLibraryHandle, exportName, typeof(TDelegate), out var nativeDelegate, out var exportError))
        {
            error = $"Failed to bind {exportName}: {exportError}";
            return null;
        }

        error = null;
        return (TDelegate)nativeDelegate!;
    }

    private FtaStatusSnapshot ReadStatusSnapshot(string prefix)
    {
        try
        {
            var statusWord = bindings!.FTAStatus();
            var bit1 = bindings.FTABitStatus(1) != 0;
            var bit3 = bindings.FTABitStatus(3) != 0;
            var bit5 = bindings.FTABitStatus(5) != 0;
            var bit6 = bindings.FTABitStatus(6) != 0;
            var bit7 = bindings.FTABitStatus(7) != 0;
            var bit8 = bindings.FTABitStatus(8) != 0;
            var bit9 = bindings.FTABitStatus(9) != 0;
            return new FtaStatusSnapshot(statusWord, bit1, bit3, bit5, bit6, bit7, bit8, bit9, string.Join(" | ",
                prefix,
                $"FTAStatus raw value: {statusWord}",
                $"FTABitStatus(1) new firmness: {YesNo(bit1)}",
                $"FTABitStatus(3) interface connected: {YesNo(bit3)}",
                $"FTABitStatus(5) probe at top: {YesNo(bit5)}",
                $"FTABitStatus(6) probe at bottom: {YesNo(bit6)}",
                $"FTABitStatus(7) FTA responded: {YesNo(bit7)}",
                $"FTABitStatus(8) new mass reading: {YesNo(bit8)}",
                $"FTABitStatus(9) scale attached/can measure mass: {YesNo(bit9)}"));
        }
        catch (Exception ex)
        {
            return new FtaStatusSnapshot(null, false, false, false, false, false, false, false, $"{prefix} Status read failed: {ex.Message}");
        }
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
        bool IsArchitectureMismatch,
        IntPtr NativeLibraryHandle);

    private sealed record FtaStatusSnapshot(
        int? StatusWord,
        bool NewFirmnessAvailable,
        bool InterfaceConnected,
        bool ProbeAtTop,
        bool ProbeAtBottom,
        bool FtaResponded,
        bool NewMassReading,
        bool ScaleAttachedCanMeasureMass,
        string Message);

    private sealed record FtaNativeBindings(
        FTAInit FTAInit,
        FTASetup FTASetup,
        FTAStatus FTAStatus,
        FTABitStatus FTABitStatus,
        FTADoFirmnessReading FTADoFirmnessReading,
        FTAReadMaxFirmness FTAReadMaxFirmness,
        FTAReadLastFirmness FTAReadLastFirmness,
        FTACancel FTACancel,
        FTABack FTABack,
        FTAQuit FTAQuit);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FTAInit();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FTASetup();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int FTAStatus();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int FTABitStatus(int bit);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FTADoFirmnessReading();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float FTAReadMaxFirmness();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate float FTAReadLastFirmness();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FTACancel();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FTABack();

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void FTAQuit();
}
