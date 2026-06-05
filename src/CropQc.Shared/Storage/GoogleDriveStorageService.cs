using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Upload;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using File = Google.Apis.Drive.v3.Data.File;

namespace CropQc.Shared.Storage;

public interface IGoogleDriveClient
{
    Task<GoogleDriveFolder?> FindFolderAsync(string parentFolderId, string name, CancellationToken cancellationToken);
    Task<GoogleDriveFolder> CreateFolderAsync(string parentFolderId, string name, CancellationToken cancellationToken);
    Task<GoogleDriveFile> UploadFileAsync(string folderId, string fileName, string contentType, Stream content, CancellationToken cancellationToken);
    Task<Stream?> DownloadFileAsync(string fileId, CancellationToken cancellationToken);
}

public sealed record GoogleDriveFolder(string Id, string Name, string? DriveId, string? WebViewLink);
public sealed record GoogleDriveFile(string Id, string Name, string? DriveId, string? WebViewLink, long? Size);

public sealed class GoogleDriveStorageService(
    GoogleDriveStorageOptions options,
    IGoogleDriveClient? client = null,
    ILogger<GoogleDriveStorageService>? logger = null) : IFileStorageService
{
    internal const string SharedDriveQuotaGuidance = "The configured Google Drive folder is not being treated as a Shared Drive upload target. Confirm GoogleDrive__UseSharedDrive=true, GoogleDrive__RootFolderId is a folder inside the Shared Drive, GoogleDrive__SharedDriveId is set, and the service account has Content Manager access.";
    private readonly Lazy<IGoogleDriveClient> client = new(() => client ?? CreateClient(options));
    private readonly ILogger<GoogleDriveStorageService> logger = logger ?? NullLogger<GoogleDriveStorageService>.Instance;

    public string GenerateTargetPath(FileStorageTargetContext context) =>
        string.Join('/',
            SanitizeSegment(options.BaseFolderName),
            context.CropYear.ToString(),
            SanitizeSegment(context.WarehouseCode),
            $"Receipt-{SanitizeSegment(context.ReceiptId)}",
            SanitizeSegment(context.PhotoType));

    public async Task<FileStorageReference> SaveAsync(FileStorageSaveRequest request, CancellationToken cancellationToken = default)
    {
        ValidateSharedDriveConfiguration();

        if (string.IsNullOrWhiteSpace(options.RootFolderId))
        {
            throw new InvalidOperationException("GoogleDrive:RootFolderId is required when FileStorage:Provider is GoogleDrive.");
        }

        var targetPath = NormalizeTargetPath(request.TargetPath);
        logger.LogInformation(
            "Google Drive storage save started. TargetPath: {TargetPath}. FileName: {FileName}. ContentType: {ContentType}. Size: {Size}. UseSharedDrive: {UseSharedDrive}. RootFolderConfigured: {RootFolderConfigured}. SharedDriveConfigured: {SharedDriveConfigured}.",
            targetPath,
            request.FileName,
            request.ContentType,
            request.FileSizeBytes ?? 0,
            options.UseSharedDrive,
            !string.IsNullOrWhiteSpace(options.RootFolderId),
            !string.IsNullOrWhiteSpace(options.SharedDriveId));
        var folderId = await EnsureFolderPathAsync(targetPath, cancellationToken);
        var fileName = SanitizeFileName(request.FileName);
        var uploaded = await client.Value.UploadFileAsync(folderId, fileName, request.ContentType, request.Content, cancellationToken);
        logger.LogInformation("Google Drive storage save succeeded. FileId: {FileId}. FolderId: {FolderId}. DriveId: {DriveId}. WebUrlPresent: {WebUrlPresent}.", uploaded.Id, folderId, uploaded.DriveId ?? "(none)", !string.IsNullOrWhiteSpace(uploaded.WebViewLink));

        return new FileStorageReference(
            FileStorageProviders.GoogleDrive,
            uploaded.Id,
            targetPath,
            uploaded.Name,
            request.ContentType,
            request.FileSizeBytes ?? uploaded.Size ?? 0,
            DriveId: uploaded.DriveId,
            FileId: uploaded.Id,
            FolderId: folderId,
            WebUrl: uploaded.WebViewLink);
    }

    public Task<FileStorageReference?> GetMetadataAsync(string storageKey, CancellationToken cancellationToken = default) =>
        Task.FromResult<FileStorageReference?>(null);

    public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) =>
        client.Value.DownloadFileAsync(storageKey, cancellationToken);

    public Task DeleteOrVoidAsync(string storageKey, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public async Task<string> EnsureFolderPathAsync(string targetPath, CancellationToken cancellationToken = default)
    {
        ValidateSharedDriveConfiguration();

        var parentId = options.RootFolderId.Trim();
        foreach (var segment in SplitPath(targetPath))
        {
            logger.LogInformation("Google Drive folder lookup started. ParentFolderId: {ParentFolderId}. FolderName: {FolderName}.", parentId, segment);
            var existing = await client.Value.FindFolderAsync(parentId, segment, cancellationToken);
            var folder = existing ?? await client.Value.CreateFolderAsync(parentId, segment, cancellationToken);
            logger.LogInformation(
                "Google Drive folder {FolderAction}. ParentFolderId: {ParentFolderId}. FolderName: {FolderName}. FolderId: {FolderId}.",
                existing is null ? "created" : "reused",
                parentId,
                segment,
                folder.Id);
            parentId = folder.Id;
        }

        return parentId;
    }

    private void ValidateSharedDriveConfiguration()
    {
        if (!options.UseSharedDrive)
        {
            throw new InvalidOperationException("GoogleDrive:UseSharedDrive=true is required for Google Drive uploads. Service accounts do not have My Drive storage quota.");
        }

        if (string.IsNullOrWhiteSpace(options.RootFolderId))
        {
            throw new InvalidOperationException("GoogleDrive:RootFolderId is required when GoogleDrive:UseSharedDrive is true.");
        }

        if (string.IsNullOrWhiteSpace(options.SharedDriveId))
        {
            throw new InvalidOperationException("GoogleDrive:SharedDriveId is required when GoogleDrive:UseSharedDrive is true.");
        }
    }

    private static IGoogleDriveClient CreateClient(GoogleDriveStorageOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ServiceAccountJson) && string.IsNullOrWhiteSpace(options.ServiceAccountJsonPath))
        {
            throw new InvalidOperationException("Google Drive service account credentials are required. Set GoogleDrive:ServiceAccountJson or GoogleDrive:ServiceAccountJsonPath.");
        }

        GoogleCredential credential;
        try
        {
            credential = !string.IsNullOrWhiteSpace(options.ServiceAccountJson)
                ? GoogleCredential.FromJson(options.ServiceAccountJson)
                : GoogleCredential.FromFile(options.ServiceAccountJsonPath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Invalid Google Drive service account credentials. Confirm the JSON is valid and the Drive API is enabled.", ex);
        }

        var service = new DriveService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential.CreateScoped(DriveService.Scope.Drive),
            ApplicationName = string.IsNullOrWhiteSpace(options.ApplicationName) ? "Crop QC Dashboard" : options.ApplicationName
        });

        return new GoogleDriveApiClient(service, options);
    }

    private static string NormalizeTargetPath(string path) =>
        string.Join('/', SplitPath(path));

    private static IReadOnlyList<string> SplitPath(string path) =>
        path.Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(SanitizeSegment)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToList();

    private static string SanitizeFileName(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(ch => invalidChars.Contains(ch) ? '_' : ch));
    }

    private static string SanitizeSegment(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Concat(value.Select(ch => invalidChars.Contains(ch) || ch is '/' or '\\' ? '_' : ch));
        return string.IsNullOrWhiteSpace(sanitized) ? "Unknown" : sanitized;
    }
}

public sealed class GoogleDriveApiClient(DriveService service, GoogleDriveStorageOptions options) : IGoogleDriveClient
{
    private readonly bool useSharedDrive = options.UseSharedDrive;
    private readonly string? sharedDriveId = string.IsNullOrWhiteSpace(options.SharedDriveId) ? null : options.SharedDriveId.Trim();

    public async Task<GoogleDriveFolder?> FindFolderAsync(string parentFolderId, string name, CancellationToken cancellationToken)
    {
        var request = service.Files.List();
        request.Q = $"'{EscapeQueryValue(parentFolderId)}' in parents and name = '{EscapeQueryValue(name)}' and mimeType = 'application/vnd.google-apps.folder' and trashed = false";
        request.Fields = "files(id,name,driveId,webViewLink)";
        request.PageSize = 1;
        request.SupportsAllDrives = true;
        request.IncludeItemsFromAllDrives = true;
        ApplySharedDriveSearchOptions(request);

        var result = await ExecuteDriveRequestAsync(() => request.ExecuteAsync(cancellationToken), "find Google Drive folder");
        var folder = result.Files.FirstOrDefault();
        return folder is null ? null : new GoogleDriveFolder(folder.Id, folder.Name, folder.DriveId, folder.WebViewLink);
    }

    public async Task<GoogleDriveFolder> CreateFolderAsync(string parentFolderId, string name, CancellationToken cancellationToken)
    {
        var metadata = new File
        {
            Name = name,
            MimeType = "application/vnd.google-apps.folder",
            Parents = [parentFolderId]
        };
        var request = service.Files.Create(metadata);
        request.Fields = "id,name,driveId,webViewLink";
        request.SupportsAllDrives = true;

        var folder = await ExecuteDriveRequestAsync(() => request.ExecuteAsync(cancellationToken), "create Google Drive folder");
        return new GoogleDriveFolder(folder.Id, folder.Name, folder.DriveId, folder.WebViewLink);
    }

    public async Task<GoogleDriveFile> UploadFileAsync(string folderId, string fileName, string contentType, Stream content, CancellationToken cancellationToken)
    {
        var metadata = new File
        {
            Name = fileName,
            Parents = [folderId]
        };
        var request = service.Files.Create(metadata, content, contentType);
        request.Fields = "id,name,driveId,webViewLink,size";
        request.SupportsAllDrives = true;

        var result = await ExecuteDriveRequestAsync(() => request.UploadAsync(cancellationToken), "upload Google Drive file");
        if (result.Status != UploadStatus.Completed)
        {
            throw new InvalidOperationException($"Google Drive upload failed: {ToSafeGoogleDriveMessage(result.Exception?.Message ?? result.Status.ToString())}", result.Exception);
        }

        var file = request.ResponseBody;
        return new GoogleDriveFile(file.Id, file.Name, file.DriveId, file.WebViewLink, file.Size);
    }

    public async Task<Stream?> DownloadFileAsync(string fileId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(fileId))
        {
            return null;
        }

        var request = service.Files.Get(fileId);
        request.SupportsAllDrives = true;
        var stream = new MemoryStream();
        try
        {
            await request.DownloadAsync(stream, cancellationToken);
            stream.Position = 0;
            return stream;
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode is System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }

    private void ApplySharedDriveSearchOptions(FilesResource.ListRequest request)
    {
        if (!useSharedDrive || string.IsNullOrWhiteSpace(sharedDriveId))
        {
            return;
        }

        request.Corpora = "drive";
        request.DriveId = sharedDriveId;
    }

    private static async Task<T> ExecuteDriveRequestAsync<T>(Func<Task<T>> action, string operation)
    {
        try
        {
            return await action();
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode is System.Net.HttpStatusCode.NotFound)
        {
            throw new InvalidOperationException($"Google Drive {operation} failed. Root folder ID was not found or the service account does not have access.", ex);
        }
        catch (Google.GoogleApiException ex) when (ex.HttpStatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.Unauthorized)
        {
            if (ex.Message.Contains("Service Accounts do not have storage quota", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(GoogleDriveStorageService.SharedDriveQuotaGuidance, ex);
            }

            throw new InvalidOperationException($"Google Drive {operation} failed. Confirm the Drive API is enabled and the service account has Editor or Content Manager access to the root folder.", ex);
        }
        catch (Google.GoogleApiException ex)
        {
            throw new InvalidOperationException($"Google Drive {operation} failed: {ToSafeGoogleDriveMessage(ex.Message)}", ex);
        }
    }

    private static string ToSafeGoogleDriveMessage(string message) =>
        message.Contains("Service Accounts do not have storage quota", StringComparison.OrdinalIgnoreCase)
            ? GoogleDriveStorageService.SharedDriveQuotaGuidance
            : message;

    private static string EscapeQueryValue(string value) =>
        value.Replace("\\", "\\\\").Replace("'", "\\'");
}
