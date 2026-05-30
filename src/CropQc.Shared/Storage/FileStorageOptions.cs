namespace CropQc.Shared.Storage;

public sealed class FileStorageOptions
{
    public string Provider { get; set; } = FileStorageProviders.Local;
    public string LocalRootPath { get; set; } = Path.Combine("App_Data", "CropQcFiles");
    public string BasePath { get; set; } = "Crop QC Photos";
}

public sealed class GoogleDriveStorageOptions
{
    public bool UseSharedDrive { get; set; }
    public string RootFolderId { get; set; } = "";
    public string SharedDriveId { get; set; } = "";
    public string? ServiceAccountJson { get; set; }
    public string? ServiceAccountJsonPath { get; set; }
    public string ApplicationName { get; set; } = "Crop QC Dashboard";
    public string BaseFolderName { get; set; } = "Photos";
}

public static class FileStorageProviders
{
    public const string Local = "Local";
    public const string GoogleDrive = "GoogleDrive";
    public const string Placeholder = "Placeholder";
}
