using System.Runtime.InteropServices;

namespace CropQc.QcStation.Fta;

public sealed class FtaDllPressureReader(
    StationConfiguration configuration,
    INativeDllLoader? nativeDllLoader = null,
    IFtaEnvironmentDiagnostics? environmentDiagnostics = null,
    IFtaMessagePump? messagePump = null) : IFtaDevice, IFtaPressureReader
{
    public const string DefaultFtaDllFileName = "FTA_dll.dll";
    public const string AlternateFtaDllFileName = "FTA_DLL.dll";
    public const string BorlandMemoryManagerFileName = "borlndmm.dll";
    private static readonly TimeSpan FirmnessReadingPollInterval = TimeSpan.FromMilliseconds(250);

    private bool isInitialized;
    private bool isConnected;
    private bool isReading;
    private string? errorMessage;
    private DllProbeResult? lastProbe;
    private FtaNativeBindings? bindings;
    private PressureReading? latestReading;
    private readonly INativeDllLoader nativeDllLoader = nativeDllLoader ?? new NativeDllLoader();
    private readonly IFtaEnvironmentDiagnostics environmentDiagnostics = environmentDiagnostics ?? new FtaEnvironmentDiagnostics();
    private readonly IFtaMessagePump messagePump = messagePump ?? NoOpFtaMessagePump.Instance;

    public string DeviceName => "FTA DLL";
    public string? LastStatusMessage { get; private set; }

    public Task<FtaDeviceStatus> InitializeAsync(CancellationToken cancellationToken = default) =>
        InitializeCoreAsync(configuration.FtaInitializationMode == FtaInitializationMode.FTAInit2);

    public Task<FtaDeviceStatus> InitializeWithConfigPathAsync(CancellationToken cancellationToken = default) =>
        InitializeCoreAsync(useConfigPath: true);

    private Task<FtaDeviceStatus> InitializeCoreAsync(bool useConfigPath)
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
            var initMessage = useConfigPath
                ? CallFtaInit2()
                : CallFtaInit();
            isInitialized = true;
            var statusSnapshot = ReadStatusSnapshot($"{initMessage} completed.");
            isConnected = statusSnapshot.IsInterfaceConnected || statusSnapshot.HasFtaResponded;
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
            errorMessage = $"{(useConfigPath ? "FTAInit2" : "FTAInit")} failed: {ex.Message}";
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
        isConnected = statusSnapshot.IsInterfaceConnected || statusSnapshot.HasFtaResponded;
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
        isConnected = statusSnapshot.IsInterfaceConnected || statusSnapshot.HasFtaResponded;
        var environmentSnapshot = ReadEnvironmentSnapshot();
        LastStatusMessage = string.Join(" | ", statusSnapshot.Message, environmentSnapshot.Message);
        return Task.FromResult(Status(LastStatusMessage, errorMessage));
    }

    public Task<FtaDeviceStatus> OpenSetupAsync(CancellationToken cancellationToken = default)
    {
        var readyStatus = EnsureReadyForFunctionCall();
        if (readyStatus is not null)
        {
            if (lastProbe is { MainDllFound: true, MainDllLoaded: false })
            {
                errorMessage = $"Cannot open FTA Setup because FTA_DLL.dll failed to load. {BuildLoadErrorMessage(lastProbe)}";
                LastStatusMessage = errorMessage;
                return Task.FromResult(Status("FTA setup/calibration unavailable.", errorMessage));
            }

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
            errorMessage = $"FTA setup/calibration could not be opened. Use FTAWin for calibration until this function is supported. FTASetup failed: {ex.Message}";
            LastStatusMessage = errorMessage;
            return Task.FromResult(Status("FTA setup/calibration failed.", errorMessage));
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
            var waitingMessage = afterSnapshot.HasNewFirmness
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

    public async Task<PressureReading?> StartAutoFirmnessReadingAsync(CancellationToken cancellationToken = default)
    {
        var readyStatus = EnsureReadyForFunctionCall();
        if (readyStatus is not null)
        {
            LastStatusMessage = readyStatus.ErrorMessage ?? readyStatus.StatusMessage;
            return null;
        }

        try
        {
            var beforeSnapshot = ReadStatusSnapshot("Before FTADoAutoFirmnessReading.");
            bindings!.FTADoAutoFirmnessReading();
            var afterSnapshot = ReadStatusSnapshot("After FTADoAutoFirmnessReading.");
            isReading = true;
            var reading = await WaitForMaxFirmnessReadingAsync("auto firmness", cancellationToken);
            LastStatusMessage = string.Join(" | ",
                "FTADoAutoFirmnessReading completed.",
                beforeSnapshot.Message,
                afterSnapshot.Message,
                LastStatusMessage).TrimEnd(' ', '|');
            return reading;
        }
        catch (OperationCanceledException)
        {
            LastStatusMessage = "Auto firmness reading wait was cancelled.";
            return null;
        }
        catch (Exception ex)
        {
            errorMessage = $"FTADoAutoFirmnessReading failed: {ex.Message}";
            LastStatusMessage = errorMessage;
            return null;
        }
    }

    public async Task<PressureReading?> StartAndWaitManualFirmnessReadingAsync(CancellationToken cancellationToken = default)
    {
        var readyStatus = EnsureReadyForFunctionCall();
        if (readyStatus is not null)
        {
            LastStatusMessage = readyStatus.ErrorMessage ?? readyStatus.StatusMessage;
            return null;
        }

        try
        {
            var beforeSnapshot = ReadStatusSnapshot("Before FTADoFirmnessReading.");
            bindings!.FTADoFirmnessReading();
            var afterSnapshot = ReadStatusSnapshot("After FTADoFirmnessReading.");
            isReading = true;
            var reading = await WaitForMaxFirmnessReadingAsync("manual/button firmness", cancellationToken);
            LastStatusMessage = string.Join(" | ",
                "FTADoFirmnessReading completed. Press the FTA front/init button or run the physical firmness test.",
                beforeSnapshot.Message,
                afterSnapshot.Message,
                LastStatusMessage).TrimEnd(' ', '|');
            return reading;
        }
        catch (OperationCanceledException)
        {
            LastStatusMessage = "Manual/button firmness reading wait was cancelled.";
            return null;
        }
        catch (Exception ex)
        {
            errorMessage = $"FTADoFirmnessReading failed: {ex.Message}";
            LastStatusMessage = errorMessage;
            return null;
        }
    }

    public async Task<PressureReading?> DemoStylePollReadingAsync(CancellationToken cancellationToken = default)
    {
        var readyStatus = EnsureReadyForFunctionCall();
        if (readyStatus is not null)
        {
            LastStatusMessage = readyStatus.ErrorMessage ?? readyStatus.StatusMessage;
            return null;
        }

        try
        {
            return await DemoStylePollReadingCoreAsync("demo-style poll", cancellationToken);
        }
        catch (OperationCanceledException)
        {
            LastStatusMessage = "Demo-style poll reading wait was cancelled.";
            return null;
        }
        catch (Exception ex)
        {
            errorMessage = $"Demo-style poll reading failed: {ex.Message}";
            LastStatusMessage = errorMessage;
            return null;
        }
    }

    public async Task<PressureReading?> DemoStyleAutoReadingAsync(CancellationToken cancellationToken = default)
    {
        var readyStatus = EnsureReadyForFunctionCall();
        if (readyStatus is not null)
        {
            LastStatusMessage = readyStatus.ErrorMessage ?? readyStatus.StatusMessage;
            return null;
        }

        try
        {
            var beforeSnapshot = ReadStatusSnapshot("Before demo-style FTADoAutoFirmnessReading.");
            bindings!.FTADoAutoFirmnessReading();
            var afterSnapshot = ReadStatusSnapshot("After demo-style FTADoAutoFirmnessReading.");
            isReading = true;
            var reading = await DemoStylePollReadingCoreAsync("demo-style auto firmness", cancellationToken);
            LastStatusMessage = string.Join(" | ",
                "FTADoAutoFirmnessReading completed.",
                beforeSnapshot.Message,
                afterSnapshot.Message,
                LastStatusMessage).TrimEnd(' ', '|');
            return reading;
        }
        catch (OperationCanceledException)
        {
            LastStatusMessage = "Demo-style auto firmness reading wait was cancelled.";
            return null;
        }
        catch (Exception ex)
        {
            errorMessage = $"Demo-style auto firmness reading failed: {ex.Message}";
            LastStatusMessage = errorMessage;
            return null;
        }
    }

    public async Task<PressureReading?> DemoStyleManualButtonReadingAsync(CancellationToken cancellationToken = default)
    {
        var readyStatus = EnsureReadyForFunctionCall();
        if (readyStatus is not null)
        {
            LastStatusMessage = readyStatus.ErrorMessage ?? readyStatus.StatusMessage;
            return null;
        }

        try
        {
            var beforeSnapshot = ReadStatusSnapshot("Before demo-style FTADoFirmnessReading.");
            bindings!.FTADoFirmnessReading();
            var afterSnapshot = ReadStatusSnapshot("After demo-style FTADoFirmnessReading.");
            isReading = true;
            var reading = await DemoStylePollReadingCoreAsync("demo-style manual/button firmness", cancellationToken);
            LastStatusMessage = string.Join(" | ",
                "FTADoFirmnessReading completed. Press the physical FTA front/init button.",
                beforeSnapshot.Message,
                afterSnapshot.Message,
                LastStatusMessage).TrimEnd(' ', '|');
            return reading;
        }
        catch (OperationCanceledException)
        {
            LastStatusMessage = "Demo-style manual/button firmness reading wait was cancelled.";
            return null;
        }
        catch (Exception ex)
        {
            errorMessage = $"Demo-style manual/button firmness reading failed: {ex.Message}";
            LastStatusMessage = errorMessage;
            return null;
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

            latestReading = CreateFtaPressureReading(maxFirmness);
            isReading = false;
            LastStatusMessage = FormatFirmnessStatus("FTAReadMaxFirmness returned", maxFirmness, latestReading);
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
        string? quitError = null;
        var messages = new List<string>();

        try
        {
            if (bindings is null)
            {
                messages.Add("FTAQuit skipped; native functions are not bound.");
            }
            else
            {
                messages.Add("FTAQuit call started.");
                bindings.FTAQuit();
                messages.Add("FTAQuit completed.");
            }
        }
        catch (Exception ex)
        {
            quitError = $"FTAQuit failed: {ex.Message}";
            messages.Add(quitError);
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

        messages.Add("FTA interface marked disconnected/not initialized.");
        errorMessage = quitError;
        LastStatusMessage = string.Join(" ", messages);
        return Task.FromResult(new FtaDeviceStatus(false, false, false, LastStatusMessage, quitError));
    }

    private DllProbeResult ProbeDllFiles()
    {
        ApplyWorkingDirectory();
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
            return new DllProbeResult(dllFolder, mainDllFileName, mainDllPath, false, false, borlandMemoryManagerFound, null, false, IntPtr.Zero, null, null, null, Environment.CurrentDirectory, null, mainDllPath);
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
        return new DllProbeResult(
            dllFolder,
            mainDllFileName,
            mainDllPath,
            true,
            loadResult.Loaded,
            borlandMemoryManagerFound,
            loadResult.ErrorMessage,
            loadResult.IsArchitectureMismatch,
            loadResult.NativeLibraryHandle,
            loadResult.ExceptionType,
            loadResult.HResult,
            loadResult.CurrentDirectoryBeforeLoad,
            loadResult.CurrentDirectoryAtLoad,
            loadResult.DllSearchDirectory,
            loadResult.LoadedPath);
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
                $"Current directory before DLL load: {FormatOptional(probe.CurrentDirectoryBeforeLoad)}",
                $"Current directory at DLL load: {FormatOptional(probe.CurrentDirectoryAtLoad)}",
                $"DLL search folder: {FormatOptional(probe.DllSearchDirectory)}",
                $"Full DLL path loaded: {FormatOptional(probe.LoadedPath)}",
                $"DLL load exception type: {FormatOptional(probe.LoadExceptionType)}",
                $"DLL load HResult: {FormatHResult(probe.LoadHResult)}",
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
        var init2 = Bind<FTAInit2>(nativeLibraryHandle, nameof(FTAInit2), out error);
        if (error is not null) return error;
        var setup = Bind<FTASetup>(nativeLibraryHandle, nameof(FTASetup), out error);
        if (error is not null) return error;
        var status = Bind<FTAStatus>(nativeLibraryHandle, nameof(FTAStatus), out error);
        if (error is not null) return error;
        var bitStatus = Bind<FTABitStatus>(nativeLibraryHandle, nameof(FTABitStatus), out error);
        if (error is not null) return error;
        var doFirmnessReading = Bind<FTADoFirmnessReading>(nativeLibraryHandle, nameof(FTADoFirmnessReading), out error);
        if (error is not null) return error;
        var doAutoFirmnessReading = Bind<FTADoAutoFirmnessReading>(nativeLibraryHandle, nameof(FTADoAutoFirmnessReading), out error);
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

        nativeBindings = new FtaNativeBindings(init!, init2!, setup!, status!, bitStatus!, doFirmnessReading!, doAutoFirmnessReading!, readMaxFirmness!, readLastFirmness!, cancel!, back!, quit!);
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
            var bit1 = bindings.FTABitStatus(1);
            var bit2 = bindings.FTABitStatus(2);
            var bit3 = bindings.FTABitStatus(3);
            var bit5 = bindings.FTABitStatus(5);
            var bit6 = bindings.FTABitStatus(6);
            var bit7 = bindings.FTABitStatus(7);
            var bit8 = bindings.FTABitStatus(8);
            var bit9 = bindings.FTABitStatus(9);
            return new FtaStatusSnapshot(statusWord, bit1, bit2, bit3, bit5, bit6, bit7, bit8, bit9, string.Join(" | ",
                prefix,
                FormatStatusWord(statusWord),
                FormatBitStatus(1, "new firmness", bit1),
                FormatBitStatus(2, "new size", bit2),
                FormatBitStatus(3, "interface connected", bit3),
                FormatBitStatus(5, "probe at top", bit5),
                FormatBitStatus(6, "probe at bottom", bit6),
                FormatBitStatus(7, "FTA responded", bit7),
                FormatBitStatus(8, "new mass", bit8),
                FormatBitStatus(9, "can measure mass", bit9)));
        }
        catch (Exception ex)
        {
            return new FtaStatusSnapshot(null, 0, 0, 0, 0, 0, 0, 0, 0, $"{prefix} Status read failed: {ex.Message}");
        }
    }

    private string CallFtaInit()
    {
        bindings!.FTAInit();
        return "FTAInit";
    }

    private string CallFtaInit2()
    {
        var configPath = ResolveFtaConfigPath();
        bindings!.FTAInit2(configPath);
        return $"FTAInit2({configPath})";
    }

    private FtaEnvironmentSnapshot ReadEnvironmentSnapshot()
    {
        var dllFolder = lastProbe?.DllFolder ?? (string.IsNullOrWhiteSpace(configuration.FtaDllPath)
            ? AppContext.BaseDirectory
            : Path.GetFullPath(configuration.FtaDllPath));
        var config = environmentDiagnostics.ReadConfigFile(ResolveFtaConfigPath(dllFolder));
        var availableComPorts = environmentDiagnostics.GetAvailableComPorts();
        var hidDevices = environmentDiagnostics.GetHidDeviceIdsByVendorId("VID_6017");
        var warnings = BuildEnvironmentWarnings(config, availableComPorts, hidDevices);

        return new FtaEnvironmentSnapshot(string.Join(" | ",
            $"FtaInitializationMode: {configuration.FtaInitializationMode}",
            $"FtaConfigPath: {ResolveFtaConfigPath(dllFolder)}",
            $"FtaWorkingDirectory: {FormatOptional(configuration.FtaWorkingDirectory)}",
            $"Current working directory: {Environment.CurrentDirectory}",
            $"DLL folder: {dllFolder}",
            $"Process architecture: {RuntimeInformation.ProcessArchitecture}",
            $"FTA_DLL.CFG path: {config.Path}",
            $"FTA_DLL.CFG exists: {YesNo(config.Exists)}",
            $"FTA_DLL.CFG LastWriteTime: {(config.LastWriteTime is null ? "(missing)" : config.LastWriteTime.Value.ToString("yyyy-MM-dd HH:mm:ss zzz"))}",
            $"FTA_DLL.CFG length: {(config.Length is null ? "(missing)" : config.Length.Value)}",
            $"FTA_DLL.CFG visible COM strings: {FormatList(config.VisibleComPorts)}",
            $"Windows available COM ports: {FormatList(availableComPorts)}",
            $"Windows HID devices matching VID_6017: {FormatList(hidDevices)}",
            $"FTA config/device warnings: {FormatList(warnings)}"));
    }

    private async Task<PressureReading?> DemoStylePollReadingCoreAsync(string readingMode, CancellationToken cancellationToken)
    {
        var timeoutAt = DateTimeOffset.UtcNow.Add(GetFirmnessReadingTimeout());
        var nextLogAt = DateTimeOffset.MinValue;
        var sampledStatuses = new List<int>();
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            cancellationToken.ThrowIfCancellationRequested();
            messagePump.ProcessPendingMessages();
            var status = bindings!.FTAStatus();
            if (DateTimeOffset.UtcNow >= nextLogAt)
            {
                sampledStatuses.Add(status);
                nextLogAt = DateTimeOffset.UtcNow.AddSeconds(1);
            }

            if (status > 0 && (status & 1) == 1)
            {
                var maxFirmness = bindings.FTAReadMaxFirmness();
                if (maxFirmness == -1f)
                {
                    LastStatusMessage = $"FTAReadMaxFirmness returned -1 after {readingMode} FTAStatus was {status}; no valid firmness reading is available. Demo-style raw FTAStatus samples: {FormatStatusSamples(sampledStatuses)}.";
                    isReading = false;
                    return null;
                }

                latestReading = CreateFtaPressureReading(maxFirmness);
                isReading = false;
                LastStatusMessage = $"{readingMode} reading detected from demo-style polling. FTAStatus: {status}. {FormatFirmnessStatus("FTAReadMaxFirmness returned", maxFirmness, latestReading)} Demo-style raw FTAStatus samples: {FormatStatusSamples(sampledStatuses)}.";
                return latestReading;
            }

            await Task.Delay(FirmnessReadingPollInterval, cancellationToken);
        }

        isReading = false;
        LastStatusMessage = $"No {readingMode} reading detected after {GetFirmnessReadingTimeout().TotalSeconds:0} seconds using demo-style polling. Demo-style raw FTAStatus samples: {FormatStatusSamples(sampledStatuses)}.";
        return null;
    }

    private static IReadOnlyList<string> BuildEnvironmentWarnings(
        FtaConfigFileDiagnostics config,
        IReadOnlyList<string> availableComPorts,
        IReadOnlyList<string> hidDevices)
    {
        var configSaysCom1 = config.VisibleComPorts.Any(port => string.Equals(port, "COM1", StringComparison.OrdinalIgnoreCase));
        var onlyCom1IsAvailable = availableComPorts.Count == 1 && string.Equals(availableComPorts[0], "COM1", StringComparison.OrdinalIgnoreCase);
        var ftaAppearsAsHid = hidDevices.Any(device => device.Contains("VID_6017", StringComparison.OrdinalIgnoreCase));

        if (configSaysCom1 && onlyCom1IsAvailable && ftaAppearsAsHid)
        {
            return [
                "FTA_DLL.CFG says COM1, Windows only reports COM1, and the FTA appears as HID USB VID_6017 instead of a COM port. Original vendor software may be using additional configuration beyond FTA_DLL.CFG."
            ];
        }

        return [];
    }

    private async Task<PressureReading?> WaitForMaxFirmnessReadingAsync(string readingMode, CancellationToken cancellationToken)
    {
        var timeoutAt = DateTimeOffset.UtcNow.Add(GetFirmnessReadingTimeout());
        while (DateTimeOffset.UtcNow < timeoutAt)
        {
            cancellationToken.ThrowIfCancellationRequested();
            messagePump.ProcessPendingMessages();
            var bit1Raw = bindings!.FTABitStatus(1);
            if (bit1Raw != 0)
            {
                var maxFirmness = bindings.FTAReadMaxFirmness();
                if (maxFirmness == -1f)
                {
                    LastStatusMessage = $"FTAReadMaxFirmness returned -1 after {readingMode} bit 1 became available; no valid firmness reading is available.";
                    isReading = false;
                    return null;
                }

                latestReading = CreateFtaPressureReading(maxFirmness);
                isReading = false;
                LastStatusMessage = $"{readingMode} reading detected. {FormatFirmnessStatus("FTAReadMaxFirmness returned", maxFirmness, latestReading)}";
                return latestReading;
            }

            await Task.Delay(FirmnessReadingPollInterval, cancellationToken);
        }

        isReading = false;
        LastStatusMessage = $"No {readingMode} reading detected after {GetFirmnessReadingTimeout().TotalSeconds:0} seconds.";
        return null;
    }

    private string ResolveFtaConfigPath(string? dllFolder = null)
    {
        if (!string.IsNullOrWhiteSpace(configuration.FtaConfigPath))
        {
            return Path.GetFullPath(configuration.FtaConfigPath);
        }

        var folder = dllFolder ?? lastProbe?.DllFolder ?? (string.IsNullOrWhiteSpace(configuration.FtaDllPath)
            ? AppContext.BaseDirectory
            : Path.GetFullPath(configuration.FtaDllPath));
        return Path.Combine(folder, FtaEnvironmentDiagnostics.FtaConfigFileName);
    }

    private PressureReading CreateFtaPressureReading(float rawFirmness)
    {
        var rawValue = (decimal)rawFirmness;
        var pounds = ConvertRawFirmnessToPounds(rawValue, configuration.FtaFirmnessUnit);
        return PressureReading.Success(pounds, PressureReadingSource.FTA, configuration.StationName, rawValue, FormatFirmnessUnit(configuration.FtaFirmnessUnit));
    }

    public static decimal ConvertRawFirmnessToPounds(decimal rawFirmness, FtaFirmnessUnit unit) =>
        unit == FtaFirmnessUnit.Kilograms
            ? Math.Round(rawFirmness * 2.20462262185m, 2, MidpointRounding.AwayFromZero)
            : rawFirmness;

    private static string FormatFirmnessStatus(string prefix, float rawFirmness, PressureReading reading)
    {
        var rawUnit = reading.RawReadingUnit ?? "lbs";
        return rawUnit.Equals("lbs", StringComparison.OrdinalIgnoreCase)
            ? $"{prefix} {rawFirmness:0.00} lbs."
            : $"{prefix} {rawFirmness:0.00} {rawUnit}; stored {reading.ReadingValueLbs:0.00} lbs.";
    }

    private static string FormatFirmnessUnit(FtaFirmnessUnit unit) =>
        unit == FtaFirmnessUnit.Kilograms ? "kg" : "lbs";

    private TimeSpan GetFirmnessReadingTimeout() =>
        TimeSpan.FromSeconds(configuration.FtaReadingTimeoutSeconds > 0 ? configuration.FtaReadingTimeoutSeconds : 60);

    private void ApplyWorkingDirectory()
    {
        if (string.IsNullOrWhiteSpace(configuration.FtaWorkingDirectory))
        {
            return;
        }

        var workingDirectory = Path.GetFullPath(configuration.FtaWorkingDirectory);
        if (Directory.Exists(workingDirectory))
        {
            Environment.CurrentDirectory = workingDirectory;
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

        if (RuntimeInformation.ProcessArchitecture == Architecture.X86)
        {
            return string.Join(" ",
                probe.LoadErrorMessage,
                "QC Station is already running x86. The DLL load failure is likely caused by the FTA DLL file or one of its dependencies being the wrong architecture, invalid, or loaded from the wrong folder.",
                "Confirm FTA_DLL.dll PE architecture, confirm borlndmm.dll PE architecture, confirm the DLL search path, and confirm whether the C:\\Windows\\SysWOW64 copy or C:\\Program Files\\FTADLL copy is the correct vendor DLL.");
        }

        return string.Join(" ",
            probe.LoadErrorMessage,
            "This usually means a 32-bit/64-bit mismatch.",
            $"Current process architecture: {RuntimeInformation.ProcessArchitecture}.",
            $"OS architecture: {RuntimeInformation.OSArchitecture}.",
            "The FTA_dll.dll is likely 32-bit; run the QC Station as x86 for RealDll testing.");
    }

    private static string YesNo(bool value) => value ? "Yes" : "No";

    private static string FormatStatusWord(int statusWord) =>
        statusWord < 0
            ? $"FTAStatus raw value: {statusWord} (negative/suspicious; raw status word was not decoded)"
            : $"FTAStatus raw value: {statusWord}";

    private static string FormatBitStatus(int bit, string label, int rawValue) =>
        $"FTABitStatus({bit}) {label}: raw {rawValue}, {(rawValue != 0 ? "Yes" : "No")}";

    private static string FormatList(IReadOnlyList<string> values) =>
        values.Count == 0 ? "(none)" : string.Join(", ", values);

    private static string FormatStatusSamples(IReadOnlyList<int> values) =>
        values.Count == 0 ? "(none)" : string.Join(", ", values);

    private static string FormatOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "(not configured)" : value;

    private static string FormatHResult(int? hResult) =>
        hResult is null ? "(none)" : $"0x{hResult.Value:X8}";

    private sealed record DllProbeResult(
        string DllFolder,
        string MainDllFileName,
        string MainDllPath,
        bool MainDllFound,
        bool MainDllLoaded,
        bool BorlandMemoryManagerFound,
        string? LoadErrorMessage,
        bool IsArchitectureMismatch,
        IntPtr NativeLibraryHandle,
        string? LoadExceptionType,
        int? LoadHResult,
        string? CurrentDirectoryBeforeLoad,
        string? CurrentDirectoryAtLoad,
        string? DllSearchDirectory,
        string? LoadedPath);

    private sealed record FtaStatusSnapshot(
        int? StatusWord,
        int NewFirmnessRaw,
        int NewSizeRaw,
        int InterfaceConnectedRaw,
        int ProbeAtTopRaw,
        int ProbeAtBottomRaw,
        int FtaRespondedRaw,
        int NewMassReadingRaw,
        int ScaleAttachedCanMeasureMassRaw,
        string Message)
    {
        public bool HasNewFirmness => NewFirmnessRaw != 0;
        public bool IsInterfaceConnected => InterfaceConnectedRaw != 0;
        public bool HasFtaResponded => FtaRespondedRaw != 0;
    }

    private sealed record FtaEnvironmentSnapshot(string Message);

    private sealed record FtaNativeBindings(
        FTAInit FTAInit,
        FTAInit2 FTAInit2,
        FTASetup FTASetup,
        FTAStatus FTAStatus,
        FTABitStatus FTABitStatus,
        FTADoFirmnessReading FTADoFirmnessReading,
        FTADoAutoFirmnessReading FTADoAutoFirmnessReading,
        FTAReadMaxFirmness FTAReadMaxFirmness,
        FTAReadLastFirmness FTAReadLastFirmness,
        FTACancel FTACancel,
        FTABack FTABack,
        FTAQuit FTAQuit);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void FTAInit();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void FTAInit2([MarshalAs(UnmanagedType.LPStr)] string sPath);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void FTASetup();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int FTAStatus();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int FTABitStatus(int bit);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void FTADoFirmnessReading();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void FTADoAutoFirmnessReading();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate float FTAReadMaxFirmness();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate float FTAReadLastFirmness();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void FTACancel();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void FTABack();

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void FTAQuit();
}
