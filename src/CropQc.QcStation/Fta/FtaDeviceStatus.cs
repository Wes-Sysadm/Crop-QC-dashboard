namespace CropQc.QcStation.Fta;

public sealed record FtaDeviceStatus(
    bool IsInitialized,
    bool IsConnected,
    bool IsReading,
    string StatusMessage,
    string? ErrorMessage = null);
