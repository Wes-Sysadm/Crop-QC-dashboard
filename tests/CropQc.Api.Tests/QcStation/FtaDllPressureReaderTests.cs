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
        var reader = new FtaDllPressureReader(configuration);

        var status = await reader.InitializeAsync();

        Assert.False(status.IsInitialized);
        Assert.False(status.IsConnected);
        Assert.Contains(FtaDllPressureReader.FtaDllFileName, status.ErrorMessage);
    }
}
