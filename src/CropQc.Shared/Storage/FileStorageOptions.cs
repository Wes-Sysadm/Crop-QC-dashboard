namespace CropQc.Shared.Storage;

public sealed class FileStorageOptions
{
    public string Provider { get; set; } = FileStorageProviders.Local;
    public string LocalRootPath { get; set; } = Path.Combine("App_Data", "CropQcFiles");
    public string BasePath { get; set; } = "Crop QC Photos";
}

public static class FileStorageProviders
{
    public const string Local = "Local";
    public const string GoogleDrive = "GoogleDrive";
}
