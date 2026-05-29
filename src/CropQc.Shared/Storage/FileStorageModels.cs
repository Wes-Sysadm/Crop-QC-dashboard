namespace CropQc.Shared.Storage;

public sealed record FileStorageSaveRequest(
    Stream Content,
    string TargetPath,
    string FileName,
    string ContentType,
    long? FileSizeBytes = null);

public sealed record FileStorageReference(
    string StorageProvider,
    string StorageKey,
    string TargetPath,
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string? DriveId = null,
    string? FileId = null,
    string? FolderId = null,
    string? WebUrl = null);

public sealed record FileStorageTargetContext(
    int CropYear,
    string WarehouseCode,
    string ReceiptId,
    string PhotoType,
    DateTimeOffset CapturedAt);
