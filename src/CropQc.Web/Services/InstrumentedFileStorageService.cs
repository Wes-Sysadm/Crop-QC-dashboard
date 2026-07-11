using CropQc.Shared.Storage;

namespace CropQc.Web.Services;

public sealed class InstrumentedFileStorageService(
    IFileStorageService inner,
    IPerformanceExternalCallCounter externalCallCounter) : IFileStorageService
{
    public string GenerateTargetPath(FileStorageTargetContext context) =>
        inner.GenerateTargetPath(context);

    public Task<FileStorageReference> SaveAsync(FileStorageSaveRequest request, CancellationToken cancellationToken = default)
    {
        IncrementIfExternal();
        return inner.SaveAsync(request, cancellationToken);
    }

    public Task<FileStorageReference?> GetMetadataAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        IncrementIfExternal();
        return inner.GetMetadataAsync(storageKey, cancellationToken);
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        IncrementIfExternal();
        return inner.OpenReadAsync(storageKey, cancellationToken);
    }

    public Task DeleteOrVoidAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        IncrementIfExternal();
        return inner.DeleteOrVoidAsync(storageKey, cancellationToken);
    }

    private void IncrementIfExternal()
    {
        if (inner is GoogleDriveStorageService)
        {
            externalCallCounter.Increment("GoogleDrive");
        }
    }
}
