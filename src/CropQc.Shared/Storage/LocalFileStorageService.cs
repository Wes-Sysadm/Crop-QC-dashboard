namespace CropQc.Shared.Storage;

public sealed class LocalFileStorageService(FileStorageOptions options) : IFileStorageService
{
    public string GenerateTargetPath(FileStorageTargetContext context)
    {
        var receiptFolder = $"Receipt-{SanitizeSegment(context.ReceiptId)}";
        return Path.Combine(
            options.BasePath,
            context.CropYear.ToString(),
            SanitizeSegment(context.WarehouseCode),
            receiptFolder,
            SanitizeSegment(context.PhotoType));
    }

    public async Task<FileStorageReference> SaveAsync(FileStorageSaveRequest request, CancellationToken cancellationToken = default)
    {
        var targetPath = NormalizeRelativePath(request.TargetPath);
        var fileName = SanitizeFileName(request.FileName);
        var relativePath = Path.Combine(targetPath, fileName);
        var fullPath = GetSafeFullPath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        await using (var file = File.Create(fullPath))
        {
            await request.Content.CopyToAsync(file, cancellationToken);
        }

        var fileInfo = new FileInfo(fullPath);
        return new FileStorageReference(
            FileStorageProviders.Local,
            relativePath,
            targetPath,
            fileName,
            request.ContentType,
            request.FileSizeBytes ?? fileInfo.Length,
            WebUrl: fullPath);
    }

    public Task<FileStorageReference?> GetMetadataAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var fullPath = GetSafeFullPath(storageKey);
        if (!File.Exists(fullPath))
        {
            return Task.FromResult<FileStorageReference?>(null);
        }

        var fileInfo = new FileInfo(fullPath);
        var targetPath = Path.GetDirectoryName(storageKey) ?? string.Empty;
        return Task.FromResult<FileStorageReference?>(new FileStorageReference(
            FileStorageProviders.Local,
            storageKey,
            targetPath,
            fileInfo.Name,
            "application/octet-stream",
            fileInfo.Length,
            WebUrl: fullPath));
    }

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var fullPath = GetSafeFullPath(storageKey);
        return Task.FromResult<Stream?>(File.Exists(fullPath) ? File.OpenRead(fullPath) : null);
    }

    public Task DeleteOrVoidAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var fullPath = GetSafeFullPath(storageKey);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    private string GetSafeFullPath(string relativePath)
    {
        var root = Path.GetFullPath(options.LocalRootPath);
        var fullPath = Path.GetFullPath(Path.Combine(root, NormalizeRelativePath(relativePath)));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;
        if (!fullPath.Equals(root, StringComparison.OrdinalIgnoreCase)
            && !fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("File storage path resolved outside the configured local root.");
        }

        return fullPath;
    }

    private static string NormalizeRelativePath(string path) =>
        path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar).TrimStart(Path.DirectorySeparatorChar);

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch));
    }

    private static string SanitizeSegment(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch));
    }
}
