namespace CropQc.Shared.Storage;

public interface IFileStorageService
{
    string GenerateTargetPath(FileStorageTargetContext context);
    Task<FileStorageReference> SaveAsync(FileStorageSaveRequest request, CancellationToken cancellationToken = default);
    Task<FileStorageReference?> GetMetadataAsync(string storageKey, CancellationToken cancellationToken = default);
    Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);
    Task DeleteOrVoidAsync(string storageKey, CancellationToken cancellationToken = default);
}
