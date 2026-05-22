namespace CropQc.QcStation.Fta;

public interface IFtaStationService
{
    StationConfiguration Configuration { get; }
    PressureReading? LatestReading { get; }
    IReadOnlyList<string> LogEntries { get; }

    Task<FtaDeviceStatus> InitializeAsync(CancellationToken cancellationToken = default);
    Task<FtaDeviceStatus> CheckStatusAsync(CancellationToken cancellationToken = default);
    Task<FtaDeviceStatus> StartPressureReadingAsync(CancellationToken cancellationToken = default);
    Task<PressureReading?> GetLatestPressureReadingAsync(CancellationToken cancellationToken = default);
    Task<FtaDeviceStatus> CancelReadingAsync(CancellationToken cancellationToken = default);
    Task<FtaDeviceStatus> ReturnProbeHomeAsync(CancellationToken cancellationToken = default);
    PressureReading UseMockReading(decimal? manualValueLbs = null);
    void ClearLog();
}
