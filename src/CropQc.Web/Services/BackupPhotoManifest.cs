using CropQc.Data.Entities;
using CropQc.Shared.Storage;

namespace CropQc.Web.Services;

public static class BackupPhotoManifest
{
    public static async Task<IReadOnlyList<object>> BuildAsync(IReadOnlyList<QcPhoto> frozenPhotos,
        IFileStorageService storage, Action<int, long> progress, CancellationToken ct,
        TimeSpan? attemptTimeout = null, TimeSpan? retryDelay = null)
    {
        var result = new List<object>(frozenPhotos.Count);
        long bytes = 0;
        foreach (var photo in frozenPhotos)
        {
            if (!string.Equals(photo.StorageProvider, FileStorageProviders.GoogleDrive, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(photo.FileId))
                throw new InvalidDataException($"Frozen photo {photo.Id} has no supported object reference.");
            var remote = await RequiredMetadataAsync(storage, photo.FileId, photo.Id, ct, attemptTimeout, retryDelay);
            if (photo.FileSizeBytes is { } expected && remote.FileSizeBytes != expected)
                throw new InvalidDataException($"Frozen photo {photo.Id} size differs from its captured reference.");
            FileStorageReference? presentation = null;
            if (!string.IsNullOrWhiteSpace(photo.PresentationStorageKey) && photo.PresentationStorageKey != photo.FileId)
                presentation = await RequiredMetadataAsync(storage, photo.PresentationStorageKey, photo.Id, ct, attemptTimeout, retryDelay);
            if (presentation is not null && photo.PresentationFileSizeBytes is { } presentationSize && presentation.FileSizeBytes != presentationSize)
                throw new InvalidDataException($"Frozen photo {photo.Id} presentation size differs from its captured reference.");
            result.Add(new
            {
                photoId = photo.Id,
                photo.QcSampleId,
                photo.ReceiptId,
                photo.PhotoType,
                photo.StorageProvider,
                photo.DriveId,
                photo.FileId,
                photo.FolderId,
                photo.FileName,
                photo.ContentType,
                photo.FileSizeBytes,
                photo.CapturedAt,
                photo.UploadedAt,
                objectAccessible = true,
                remoteSizeBytes = remote.FileSizeBytes,
                remoteChecksum = remote.Checksum,
                remoteCreatedAt = remote.CreatedAt,
                remoteModifiedAt = remote.ModifiedAt,
                photo.PresentationStorageKey,
                photo.PresentationRevision,
                presentationSizeBytes = presentation?.FileSizeBytes,
                presentationChecksum = presentation?.Checksum
            });
            bytes += remote.FileSizeBytes + (presentation?.FileSizeBytes ?? 0);
            progress(result.Count, bytes);
        }
        return result;
    }

    private static async Task<FileStorageReference> RequiredMetadataAsync(IFileStorageService storage,
        string key, long photoId, CancellationToken ct, TimeSpan? timeout, TimeSpan? delay)
    {
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            using var bounded = CancellationTokenSource.CreateLinkedTokenSource(ct);
            bounded.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));
            try
            {
                var value = await storage.GetMetadataAsync(key, bounded.Token).WaitAsync(bounded.Token);
                if (value is not null) return value;
            }
            catch (Exception) when (!ct.IsCancellationRequested) { /* bounded object retry; no discovery */ }
            if (attempt < 3) await Task.Delay(delay ?? TimeSpan.FromSeconds(attempt), ct);
        }
        throw new InvalidDataException($"Frozen photo {photoId} object unavailable after 3 bounded attempts; backup is incomplete.");
    }
}
