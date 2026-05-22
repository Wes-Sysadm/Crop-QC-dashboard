namespace CropQc.QcStation.Fta;

public interface IFtaPressureReader
{
    string? LastStatusMessage { get; }
    Task<FtaDeviceStatus> StartPressureReadingAsync(CancellationToken cancellationToken = default);
    Task<PressureReading?> StartAutoFirmnessReadingAsync(CancellationToken cancellationToken = default);
    Task<PressureReading?> StartAndWaitManualFirmnessReadingAsync(CancellationToken cancellationToken = default);
    Task<PressureReading?> DemoStylePollReadingAsync(CancellationToken cancellationToken = default);
    Task<PressureReading?> DemoStyleAutoReadingAsync(CancellationToken cancellationToken = default);
    Task<PressureReading?> DemoStyleManualButtonReadingAsync(CancellationToken cancellationToken = default);
    Task<PressureReading?> GetLatestPressureReadingAsync(CancellationToken cancellationToken = default);
    Task<FtaDeviceStatus> CancelReadingAsync(CancellationToken cancellationToken = default);
}
