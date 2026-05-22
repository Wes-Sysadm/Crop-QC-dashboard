namespace CropQc.QcStation.Fta;

public interface IFtaStationService
{
    StationConfiguration Configuration { get; }
    PressureReading? LatestReading { get; }
    IReadOnlyList<string> LogEntries { get; }

    Task<FtaDeviceStatus> InitializeAsync(CancellationToken cancellationToken = default);
    Task<FtaDeviceStatus> InitializeWithConfigPathAsync(CancellationToken cancellationToken = default);
    Task<FtaDeviceStatus> CheckStatusAsync(CancellationToken cancellationToken = default);
    Task<FtaDeviceStatus> DiagnosticStatusAsync(CancellationToken cancellationToken = default);
    Task<FtaDeviceStatus> OpenSetupAsync(CancellationToken cancellationToken = default);
    Task<FtaDeviceStatus> StartPressureReadingAsync(CancellationToken cancellationToken = default);
    Task<PressureReading?> StartAutoFirmnessReadingAsync(CancellationToken cancellationToken = default);
    Task<PressureReading?> StartAndWaitManualFirmnessReadingAsync(CancellationToken cancellationToken = default);
    Task<PressureReading?> DemoStylePollReadingAsync(CancellationToken cancellationToken = default);
    Task<PressureReading?> DemoStyleAutoReadingAsync(CancellationToken cancellationToken = default);
    Task<PressureReading?> DemoStyleManualButtonReadingAsync(CancellationToken cancellationToken = default);
    Task<PressureReading?> GetLatestPressureReadingAsync(CancellationToken cancellationToken = default);
    Task<PressureReading?> PollLatestPressureReadingAsync(CancellationToken cancellationToken = default);
    Task<FtaDeviceStatus> CancelReadingAsync(CancellationToken cancellationToken = default);
    Task<FtaDeviceStatus> ReturnProbeHomeAsync(CancellationToken cancellationToken = default);
    Task<FtaDeviceStatus> QuitAsync(CancellationToken cancellationToken = default);
    PressureReading UseMockReading(decimal? manualValueLbs = null);
    void ClearLog();
}
