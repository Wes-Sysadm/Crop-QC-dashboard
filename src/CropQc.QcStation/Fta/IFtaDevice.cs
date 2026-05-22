namespace CropQc.QcStation.Fta;

public interface IFtaDevice
{
    string DeviceName { get; }
    Task<FtaDeviceStatus> InitializeAsync(CancellationToken cancellationToken = default);
    Task<FtaDeviceStatus> CheckStatusAsync(CancellationToken cancellationToken = default);
    Task<FtaDeviceStatus> OpenSetupAsync(CancellationToken cancellationToken = default);
    Task<FtaDeviceStatus> ReturnProbeHomeAsync(CancellationToken cancellationToken = default);
    Task<FtaDeviceStatus> QuitAsync(CancellationToken cancellationToken = default);
}
