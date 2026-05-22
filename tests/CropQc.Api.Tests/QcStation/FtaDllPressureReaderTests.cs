using CropQc.QcStation.Fta;

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

    private sealed class FakeNativeDllLoader(DllLoadResult result) : INativeDllLoader
    {
        public DllLoadResult TryLoad(string dllPath) => result;
    }
}
