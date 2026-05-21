namespace CropQc.QcStation.Fta;

public interface IFtaPressureReader
{
    Task<FtaDeviceStatus> StartPressureReadingAsync(CancellationToken cancellationToken = default);
    Task<PressureReading?> GetLatestPressureReadingAsync(CancellationToken cancellationToken = default);
    Task<FtaDeviceStatus> CancelReadingAsync(CancellationToken cancellationToken = default);
}
