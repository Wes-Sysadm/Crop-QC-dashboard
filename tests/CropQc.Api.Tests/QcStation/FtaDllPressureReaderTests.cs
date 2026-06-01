using CropQc.QcStation.Fta;
using System.Reflection;

namespace CropQc.Api.Tests.QcStation;

public sealed class FtaDllPressureReaderTests
{
    [Fact]
    public async Task Missing_dll_path_returns_clear_error_without_throwing()
    {
        var configuration = new StationConfiguration
        {
            StationName = "Real DLL Test",
            FtaMode = FtaMode.RealDll,
            FtaDllPath = Path.Combine(Path.GetTempPath(), "crop-qc-missing-fta-dll")
        };
        var reader = new FtaDllPressureReader(configuration, new FakeNativeDllLoader(DllLoadResult.Success()));

        var status = await reader.InitializeAsync();

        Assert.False(status.IsInitialized);
        Assert.False(status.IsConnected);
        Assert.Contains(FtaDllPressureReader.DefaultFtaDllFileName, status.ErrorMessage);
    }

    [Fact]
    public async Task Missing_borland_memory_manager_is_warning_only_when_main_dll_loads()
    {
        var tempFolder = CreateTempDllFolder(FtaDllPressureReader.DefaultFtaDllFileName);
        var configuration = new StationConfiguration
        {
            StationName = "Real DLL Test",
            FtaMode = FtaMode.RealDll,
            FtaDllPath = tempFolder,
            FtaDllFileName = FtaDllPressureReader.DefaultFtaDllFileName,
            FtaFirmnessUnit = FtaFirmnessUnit.Pounds
        };
        var reader = new FtaDllPressureReader(configuration, new FakeNativeDllLoader(DllLoadResult.Success()));

        var status = await reader.InitializeAsync();

        Assert.True(status.IsInitialized);
        Assert.True(status.IsConnected);
        Assert.Contains("FTAStatus raw value:", status.StatusMessage);
        Assert.Contains("FTABitStatus(1) new firmness: raw", status.StatusMessage);
        Assert.Contains("borlndmm.dll found: No", status.StatusMessage);
        Assert.Contains("warning-only", status.ErrorMessage);
    }

    [Fact]
    public async Task Alternate_main_dll_name_is_supported_when_configured_file_is_missing()
    {
        var tempFolder = CreateTempDllFolder(FtaDllPressureReader.AlternateFtaDllFileName);
        var configuration = new StationConfiguration
        {
            StationName = "Real DLL Test",
            FtaMode = FtaMode.RealDll,
            FtaDllPath = tempFolder,
            FtaDllFileName = "missing-configured-name.dll"
        };
        var reader = new FtaDllPressureReader(configuration, new FakeNativeDllLoader(DllLoadResult.Success()));

        var status = await reader.InitializeAsync();

        Assert.True(status.IsInitialized);
        Assert.Contains("Main DLL:", status.StatusMessage);
        Assert.Contains("Main DLL found: Yes", status.StatusMessage);
    }

    [Fact]
    public async Task Initialize_uses_fta_init2_when_configured()
    {
        var tempFolder = CreateTempDllFolder(FtaDllPressureReader.DefaultFtaDllFileName);
        var configuration = CreateRealDllConfiguration(tempFolder);
        configuration.FtaInitializationMode = FtaInitializationMode.FTAInit2;
        configuration.FtaConfigPath = @"C:\Program Files\FTADLL\FTA_DLL.CFG";
        var fakeLoader = new FakeNativeDllLoader(DllLoadResult.Success());
        var reader = new FtaDllPressureReader(configuration, fakeLoader);

        var status = await reader.InitializeAsync();

        Assert.True(status.IsInitialized);
        Assert.Equal(0, fakeLoader.InitCalls);
        Assert.Equal(1, fakeLoader.Init2Calls);
        Assert.Equal(configuration.FtaConfigPath, fakeLoader.LastInit2Path);
        Assert.Contains("FTAInit2", status.StatusMessage);
    }

    [Fact]
    public async Task Start_pressure_reading_calls_documented_firmness_function()
    {
        var tempFolder = CreateTempDllFolder(FtaDllPressureReader.DefaultFtaDllFileName);
        var configuration = CreateRealDllConfiguration(tempFolder);
        var fakeLoader = new FakeNativeDllLoader(DllLoadResult.Success());
        var reader = new FtaDllPressureReader(configuration, fakeLoader);

        await reader.InitializeAsync();
        var status = await reader.StartPressureReadingAsync();

        Assert.True(status.IsReading);
        Assert.Equal(1, fakeLoader.DoFirmnessReadingCalls);
        Assert.Contains("FTADoFirmnessReading completed", status.StatusMessage);
        Assert.Contains("Before FTADoFirmnessReading", status.StatusMessage);
        Assert.Contains("After FTADoFirmnessReading", status.StatusMessage);
    }

    [Fact]
    public async Task Start_pressure_reading_logs_setup_guidance_when_no_new_reading_is_detected()
    {
        var tempFolder = CreateTempDllFolder(FtaDllPressureReader.DefaultFtaDllFileName);
        var configuration = CreateRealDllConfiguration(tempFolder);
        var fakeLoader = new FakeNativeDllLoader(DllLoadResult.Success())
        {
            NewFirmnessAvailable = false
        };
        var reader = new FtaDllPressureReader(configuration, fakeLoader);

        await reader.InitializeAsync();
        var status = await reader.StartPressureReadingAsync();

        Assert.Contains("FTADoFirmnessReading call returned, but no new reading detected yet. Confirm FTA setup COM port and probe state.", status.StatusMessage);
    }

    [Fact]
    public async Task Auto_firmness_reading_calls_documented_auto_function_and_reads_max_firmness()
    {
        var tempFolder = CreateTempDllFolder(FtaDllPressureReader.DefaultFtaDllFileName);
        var configuration = CreateRealDllConfiguration(tempFolder);
        var fakeLoader = new FakeNativeDllLoader(DllLoadResult.Success())
        {
            MaxFirmness = 15.25f
        };
        var reader = new FtaDllPressureReader(configuration, fakeLoader);

        await reader.InitializeAsync();
        var reading = await reader.StartAutoFirmnessReadingAsync();

        Assert.NotNull(reading);
        Assert.Equal(15.25m, reading.ReadingValueLbs);
        Assert.Equal(1, fakeLoader.DoAutoFirmnessReadingCalls);
        Assert.Equal(1, fakeLoader.ReadMaxFirmnessCalls);
        Assert.Contains("FTADoAutoFirmnessReading completed", reader.LastStatusMessage);
        Assert.Contains("Before FTADoAutoFirmnessReading", reader.LastStatusMessage);
        Assert.Contains("After FTADoAutoFirmnessReading", reader.LastStatusMessage);
    }

    [Fact]
    public async Task Demo_style_poll_reads_max_firmness_only_when_status_is_positive_and_bit_one_is_set()
    {
        var tempFolder = CreateTempDllFolder(FtaDllPressureReader.DefaultFtaDllFileName);
        var configuration = CreateRealDllConfiguration(tempFolder);
        var fakeLoader = new FakeNativeDllLoader(DllLoadResult.Success())
        {
            StatusSequence = new Queue<int>([0, -1, 0, 1]),
            MaxFirmness = 16.25f
        };
        var reader = new FtaDllPressureReader(configuration, fakeLoader);

        await reader.InitializeAsync();
        var reading = await reader.DemoStylePollReadingAsync();

        Assert.NotNull(reading);
        Assert.Equal(16.25m, reading.ReadingValueLbs);
        Assert.Equal(1, fakeLoader.ReadMaxFirmnessCalls);
        Assert.Contains("Demo-style raw FTAStatus samples:", reader.LastStatusMessage);
    }

    [Fact]
    public async Task Demo_style_auto_reading_calls_auto_command_then_demo_poll()
    {
        var tempFolder = CreateTempDllFolder(FtaDllPressureReader.DefaultFtaDllFileName);
        var configuration = CreateRealDllConfiguration(tempFolder);
        var fakeLoader = new FakeNativeDllLoader(DllLoadResult.Success())
        {
            StatusWord = 1,
            MaxFirmness = 17.25f
        };
        var reader = new FtaDllPressureReader(configuration, fakeLoader);

        await reader.InitializeAsync();
        var reading = await reader.DemoStyleAutoReadingAsync();

        Assert.NotNull(reading);
        Assert.Equal(17.25m, reading.ReadingValueLbs);
        Assert.Equal(1, fakeLoader.DoAutoFirmnessReadingCalls);
    }

    [Fact]
    public async Task Demo_style_manual_button_reading_calls_manual_command_then_demo_poll()
    {
        var tempFolder = CreateTempDllFolder(FtaDllPressureReader.DefaultFtaDllFileName);
        var configuration = CreateRealDllConfiguration(tempFolder);
        var fakeLoader = new FakeNativeDllLoader(DllLoadResult.Success())
        {
            StatusWord = 1,
            MaxFirmness = 18.25f
        };
        var reader = new FtaDllPressureReader(configuration, fakeLoader);

        await reader.InitializeAsync();
        var reading = await reader.DemoStyleManualButtonReadingAsync();

        Assert.NotNull(reading);
        Assert.Equal(18.25m, reading.ReadingValueLbs);
        Assert.Equal(1, fakeLoader.DoFirmnessReadingCalls);
        Assert.Contains("physical FTA front/init button", reader.LastStatusMessage);
    }

    [Fact]
    public async Task Start_and_wait_manual_reading_reads_max_firmness_when_bit_is_available()
    {
        var tempFolder = CreateTempDllFolder(FtaDllPressureReader.DefaultFtaDllFileName);
        var configuration = CreateRealDllConfiguration(tempFolder);
        var fakeLoader = new FakeNativeDllLoader(DllLoadResult.Success())
        {
            MaxFirmness = 13.5f
        };
        var reader = new FtaDllPressureReader(configuration, fakeLoader);

        await reader.InitializeAsync();
        var reading = await reader.StartAndWaitManualFirmnessReadingAsync();

        Assert.NotNull(reading);
        Assert.Equal(13.5m, reading.ReadingValueLbs);
        Assert.Equal(1, fakeLoader.DoFirmnessReadingCalls);
        Assert.Equal(1, fakeLoader.ReadMaxFirmnessCalls);
        Assert.Contains("Press the FTA front/init button", reader.LastStatusMessage);
    }

    [Fact]
    public async Task Fta_reading_converts_kg_to_lbs_when_configured()
    {
        var tempFolder = CreateTempDllFolder(FtaDllPressureReader.DefaultFtaDllFileName);
        var configuration = CreateRealDllConfiguration(tempFolder);
        configuration.FtaFirmnessUnit = FtaFirmnessUnit.Kilograms;
        var fakeLoader = new FakeNativeDllLoader(DllLoadResult.Success())
        {
            MaxFirmness = 2.05f
        };
        var reader = new FtaDllPressureReader(configuration, fakeLoader);

        await reader.InitializeAsync();
        var reading = await reader.StartAndWaitManualFirmnessReadingAsync();

        Assert.NotNull(reading);
        Assert.Equal(4.52m, reading.ReadingValueLbs);
        Assert.Equal(2.05m, reading.RawReadingValue);
        Assert.Equal("kg", reading.RawReadingUnit);
        Assert.Contains("FTAReadMaxFirmness returned 2.05 kg; stored 4.52 lbs", reader.LastStatusMessage);
    }

    [Fact]
    public async Task Diagnostic_status_reports_raw_value_and_required_bits()
    {
        var tempFolder = CreateTempDllFolder(FtaDllPressureReader.DefaultFtaDllFileName);
        var configuration = CreateRealDllConfiguration(tempFolder);
        var reader = new FtaDllPressureReader(configuration, new FakeNativeDllLoader(DllLoadResult.Success()));

        await reader.InitializeAsync();
        var status = await reader.DiagnosticStatusAsync();

        Assert.Contains("FTAStatus raw value:", status.StatusMessage);
        Assert.Contains("FTABitStatus(1) new firmness: raw", status.StatusMessage);
        Assert.Contains("FTABitStatus(2) new size: raw", status.StatusMessage);
        Assert.Contains("FTABitStatus(3) interface connected: raw", status.StatusMessage);
        Assert.Contains("FTABitStatus(5) probe at top: raw", status.StatusMessage);
        Assert.Contains("FTABitStatus(6) probe at bottom: raw", status.StatusMessage);
        Assert.Contains("FTABitStatus(7) FTA responded: raw", status.StatusMessage);
        Assert.Contains("FTABitStatus(8) new mass: raw", status.StatusMessage);
        Assert.Contains("FTABitStatus(9) can measure mass: raw", status.StatusMessage);
    }

    [Fact]
    public async Task Diagnostic_status_labels_negative_status_as_suspicious()
    {
        var tempFolder = CreateTempDllFolder(FtaDllPressureReader.DefaultFtaDllFileName);
        var configuration = CreateRealDllConfiguration(tempFolder);
        var fakeLoader = new FakeNativeDllLoader(DllLoadResult.Success())
        {
            StatusWord = -1
        };
        var reader = new FtaDllPressureReader(configuration, fakeLoader);

        await reader.InitializeAsync();
        var status = await reader.DiagnosticStatusAsync();

        Assert.Contains("FTAStatus raw value: -1 (negative/suspicious; raw status word was not decoded)", status.StatusMessage);
        Assert.Contains("FTABitStatus(1) new firmness: raw", status.StatusMessage);
    }

    [Fact]
    public async Task Diagnostic_status_reports_config_com_and_hid_warning()
    {
        var tempFolder = CreateTempDllFolder(FtaDllPressureReader.DefaultFtaDllFileName);
        var configuration = CreateRealDllConfiguration(tempFolder);
        var environmentDiagnostics = new FakeFtaEnvironmentDiagnostics(
            new FtaConfigFileDiagnostics(@"C:\Program Files\FTADLL\FTA_DLL.CFG", true, new DateTimeOffset(2026, 5, 22, 9, 30, 0, TimeSpan.Zero), 128, ["COM1"]),
            ["COM1"],
            [@"VID_6017&PID_3430"]);
        var reader = new FtaDllPressureReader(configuration, new FakeNativeDllLoader(DllLoadResult.Success()), environmentDiagnostics);

        await reader.InitializeAsync();
        var status = await reader.DiagnosticStatusAsync();

        Assert.Contains(@"FTA_DLL.CFG path: C:\Program Files\FTADLL\FTA_DLL.CFG", status.StatusMessage);
        Assert.Contains("FTA_DLL.CFG exists: Yes", status.StatusMessage);
        Assert.Contains("FTA_DLL.CFG visible COM strings: COM1", status.StatusMessage);
        Assert.Contains("Windows available COM ports: COM1", status.StatusMessage);
        Assert.Contains("Windows HID devices matching VID_6017: VID_6017&PID_3430", status.StatusMessage);
        Assert.Contains("FTA_DLL.CFG says COM1, Windows only reports COM1, and the FTA appears as HID USB VID_6017 instead of a COM port.", status.StatusMessage);
    }

    [Fact]
    public void Environment_diagnostics_extracts_visible_com_port_strings_from_config_file()
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), $"crop-qc-fta-cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempFolder);
        File.WriteAllText(Path.Combine(tempFolder, FtaEnvironmentDiagnostics.FtaConfigFileName), "Port=COM1\r\nBackup=COM2");
        var diagnostics = new FtaEnvironmentDiagnostics();

        var config = diagnostics.ReadConfigFile(tempFolder);

        Assert.True(config.Exists);
        Assert.True(config.Length > 0);
        Assert.Contains("COM1", config.VisibleComPorts);
        Assert.Contains("COM2", config.VisibleComPorts);
    }

    [Fact]
    public async Task Latest_reading_returns_null_when_new_firmness_bit_is_false()
    {
        var tempFolder = CreateTempDllFolder(FtaDllPressureReader.DefaultFtaDllFileName);
        var configuration = CreateRealDllConfiguration(tempFolder);
        var fakeLoader = new FakeNativeDllLoader(DllLoadResult.Success())
        {
            NewFirmnessAvailable = false
        };
        var reader = new FtaDllPressureReader(configuration, fakeLoader);

        await reader.InitializeAsync();
        var reading = await reader.GetLatestPressureReadingAsync();

        Assert.Null(reading);
        Assert.Contains("No new firmness reading", reader.LastStatusMessage);
        Assert.Equal(0, fakeLoader.ReadMaxFirmnessCalls);
    }

    [Fact]
    public async Task Latest_reading_reads_max_firmness_when_new_firmness_bit_is_true()
    {
        var tempFolder = CreateTempDllFolder(FtaDllPressureReader.DefaultFtaDllFileName);
        var configuration = CreateRealDllConfiguration(tempFolder);
        var fakeLoader = new FakeNativeDllLoader(DllLoadResult.Success())
        {
            MaxFirmness = 14.75f
        };
        var reader = new FtaDllPressureReader(configuration, fakeLoader);

        await reader.InitializeAsync();
        var reading = await reader.GetLatestPressureReadingAsync();

        Assert.NotNull(reading);
        Assert.Equal(PressureReadingSource.FTA, reading.Source);
        Assert.Equal(14.75m, reading.ReadingValueLbs);
        Assert.Equal(1, fakeLoader.ReadMaxFirmnessCalls);
    }

    [Fact]
    public async Task Latest_reading_treats_negative_one_as_no_valid_reading()
    {
        var tempFolder = CreateTempDllFolder(FtaDllPressureReader.DefaultFtaDllFileName);
        var configuration = CreateRealDllConfiguration(tempFolder);
        var fakeLoader = new FakeNativeDllLoader(DllLoadResult.Success())
        {
            MaxFirmness = -1f
        };
        var reader = new FtaDllPressureReader(configuration, fakeLoader);

        await reader.InitializeAsync();
        var reading = await reader.GetLatestPressureReadingAsync();

        Assert.Null(reading);
        Assert.Contains("returned -1", reader.LastStatusMessage);
    }

    [Fact]
    public async Task Main_dll_load_error_is_reported()
    {
        var tempFolder = CreateTempDllFolder(FtaDllPressureReader.DefaultFtaDllFileName);
        var configuration = new StationConfiguration
        {
            StationName = "Real DLL Test",
            FtaMode = FtaMode.RealDll,
            FtaDllPath = tempFolder,
            FtaDllFileName = FtaDllPressureReader.DefaultFtaDllFileName,
            FtaFirmnessUnit = FtaFirmnessUnit.Pounds
        };
        var reader = new FtaDllPressureReader(configuration, new FakeNativeDllLoader(DllLoadResult.Failed("Missing dependency: example.dll")));

        var status = await reader.InitializeAsync();

        Assert.False(status.IsInitialized);
        Assert.False(status.IsConnected);
        Assert.Contains("Missing dependency: example.dll", status.ErrorMessage);
    }

    [Fact]
    public async Task Architecture_mismatch_load_error_suggests_x86()
    {
        var tempFolder = CreateTempDllFolder(FtaDllPressureReader.DefaultFtaDllFileName);
        var configuration = new StationConfiguration
        {
            StationName = "Real DLL Test",
            FtaMode = FtaMode.RealDll,
            FtaDllPath = tempFolder,
            FtaDllFileName = FtaDllPressureReader.DefaultFtaDllFileName,
            FtaFirmnessUnit = FtaFirmnessUnit.Pounds
        };
        var reader = new FtaDllPressureReader(configuration, new FakeNativeDllLoader(DllLoadResult.ArchitectureMismatch("An attempt was made to load a program with an incorrect format. (0x8007000B)")));

        var status = await reader.InitializeAsync();

        Assert.False(status.IsInitialized);
        Assert.Contains("32-bit/64-bit mismatch", status.ErrorMessage);
        Assert.Contains("run the QC Station as x86", status.ErrorMessage);
        Assert.Contains("Process architecture:", status.StatusMessage);
        Assert.Contains("OS architecture:", status.StatusMessage);
    }

    private static string CreateTempDllFolder(string ftaDllFileName)
    {
        var tempFolder = Path.Combine(Path.GetTempPath(), $"crop-qc-fta-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempFolder);
        File.WriteAllText(Path.Combine(tempFolder, ftaDllFileName), "fake test dll");
        return tempFolder;
    }

    private static StationConfiguration CreateRealDllConfiguration(string tempFolder) =>
        new()
        {
            StationName = "Real DLL Test",
            FtaMode = FtaMode.RealDll,
            FtaDllPath = tempFolder,
            FtaDllFileName = FtaDllPressureReader.DefaultFtaDllFileName,
            FtaFirmnessUnit = FtaFirmnessUnit.Pounds
        };

    private sealed class FakeNativeDllLoader(DllLoadResult result) : INativeDllLoader
    {
        private static readonly IntPtr FakeHandle = new(123);

        public bool NewFirmnessAvailable { get; set; } = true;
        public int StatusWord { get; set; }
        public Queue<int>? StatusSequence { get; set; }
        public float MaxFirmness { get; set; } = 12.5f;
        public int InitCalls { get; private set; }
        public int Init2Calls { get; private set; }
        public string? LastInit2Path { get; private set; }
        public int DoFirmnessReadingCalls { get; private set; }
        public int DoAutoFirmnessReadingCalls { get; private set; }
        public int ReadMaxFirmnessCalls { get; private set; }

        public DllLoadResult TryLoad(string dllPath) =>
            result.Loaded
                ? result with { NativeLibraryHandle = FakeHandle }
                : result;

        public bool TryGetExport(IntPtr nativeLibraryHandle, string exportName, Type delegateType, out Delegate? nativeDelegate, out string? errorMessage)
        {
            var method = GetType().GetMethod(exportName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (method is null)
            {
                nativeDelegate = null;
                errorMessage = $"Fake export {exportName} is not configured.";
                return false;
            }

            nativeDelegate = method.CreateDelegate(delegateType, this);
            errorMessage = null;
            return true;
        }

        public void Free(IntPtr nativeLibraryHandle)
        {
        }

        private void FTAInit()
        {
            InitCalls++;
        }

        private void FTAInit2(string sPath)
        {
            Init2Calls++;
            LastInit2Path = sPath;
        }

        private void FTASetup()
        {
        }

        private int FTAStatus() =>
            StatusSequence is { Count: > 0 }
                ? StatusSequence.Dequeue()
                : StatusWord;

        private int FTABitStatus(int bit) => bit switch
        {
            1 => NewFirmnessAvailable ? 1 : 0,
            2 => 0,
            3 => 1,
            5 => 1,
            6 => 0,
            7 => 1,
            8 => 0,
            9 => 0,
            _ => 0
        };

        private void FTADoFirmnessReading() => DoFirmnessReadingCalls++;

        private void FTADoAutoFirmnessReading() => DoAutoFirmnessReadingCalls++;

        private float FTAReadMaxFirmness()
        {
            ReadMaxFirmnessCalls++;
            return MaxFirmness;
        }

        private float FTAReadLastFirmness() => MaxFirmness;

        private void FTACancel()
        {
        }

        private void FTABack()
        {
        }

        private void FTAQuit()
        {
        }
    }

    private sealed class FakeFtaEnvironmentDiagnostics(
        FtaConfigFileDiagnostics config,
        IReadOnlyList<string> availableComPorts,
        IReadOnlyList<string> hidDeviceIds) : IFtaEnvironmentDiagnostics
    {
        public FtaConfigFileDiagnostics ReadConfigFile(string dllFolder) => config;

        public IReadOnlyList<string> GetAvailableComPorts() => availableComPorts;

        public IReadOnlyList<string> GetHidDeviceIdsByVendorId(string vendorId) => hidDeviceIds;
    }
}
