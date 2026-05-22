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
            FtaDllFileName = FtaDllPressureReader.DefaultFtaDllFileName
        };
        var reader = new FtaDllPressureReader(configuration, new FakeNativeDllLoader(DllLoadResult.Success()));

        var status = await reader.InitializeAsync();

        Assert.True(status.IsInitialized);
        Assert.True(status.IsConnected);
        Assert.Contains("FTAStatus:", status.StatusMessage);
        Assert.Contains("bit 1 new firmness:", status.StatusMessage);
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
            FtaDllFileName = FtaDllPressureReader.DefaultFtaDllFileName
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
            FtaDllFileName = FtaDllPressureReader.DefaultFtaDllFileName
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
            FtaDllFileName = FtaDllPressureReader.DefaultFtaDllFileName
        };

    private sealed class FakeNativeDllLoader(DllLoadResult result) : INativeDllLoader
    {
        private static readonly IntPtr FakeHandle = new(123);

        public bool NewFirmnessAvailable { get; set; } = true;
        public float MaxFirmness { get; set; } = 12.5f;
        public int DoFirmnessReadingCalls { get; private set; }
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
        }

        private void FTASetup()
        {
        }

        private int FTAStatus() => 0;

        private int FTABitStatus(int bit) => bit switch
        {
            1 => NewFirmnessAvailable ? 1 : 0,
            3 => 1,
            5 => 1,
            7 => 1,
            _ => 0
        };

        private void FTADoFirmnessReading() => DoFirmnessReadingCalls++;

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
}
