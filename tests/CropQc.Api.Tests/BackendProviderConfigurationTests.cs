using CropQc.Data;
using CropQc.Shared.Storage;

namespace CropQc.Api.Tests;

public sealed class BackendProviderConfigurationTests
{
    [Theory]
    [InlineData(null, DatabaseProviders.SqlServer)]
    [InlineData("", DatabaseProviders.SqlServer)]
    [InlineData("SqlServer", DatabaseProviders.SqlServer)]
    [InlineData("azure-sql", DatabaseProviders.SqlServer)]
    [InlineData("PostgreSql", DatabaseProviders.PostgreSql)]
    [InlineData("postgres", DatabaseProviders.PostgreSql)]
    [InlineData("npgsql", DatabaseProviders.PostgreSql)]
    public void Database_provider_normalizes_supported_values(string? provider, string expected)
    {
        Assert.Equal(expected, CropQcDatabase.NormalizeProvider(provider));
    }

    [Fact]
    public void Local_file_storage_generates_crop_warehouse_receipt_photo_path()
    {
        var storage = new LocalFileStorageService(new FileStorageOptions
        {
            BasePath = "Crop QC Photos",
            LocalRootPath = Path.Combine(Path.GetTempPath(), "CropQcStorageTests")
        });

        var path = storage.GenerateTargetPath(new FileStorageTargetContext(
            2026,
            "WP",
            "12345",
            "BinTruck",
            new DateTimeOffset(2026, 9, 14, 9, 15, 22, TimeSpan.Zero)));

        Assert.EndsWith(Path.Combine("Crop QC Photos", "2026", "WP", "Receipt-12345", "BinTruck"), path);
    }

    [Fact]
    public async Task Local_file_storage_saves_uploaded_file()
    {
        var root = Path.Combine(Path.GetTempPath(), "CropQcStorageTests", Guid.NewGuid().ToString("N"));
        var storage = new LocalFileStorageService(new FileStorageOptions
        {
            BasePath = "Crop QC Photos",
            LocalRootPath = root
        });

        var target = storage.GenerateTargetPath(new FileStorageTargetContext(
            2026,
            "WP",
            "12345",
            "BinTruck",
            new DateTimeOffset(2026, 9, 14, 9, 15, 22, TimeSpan.Zero)));
        await using var content = new MemoryStream([1, 2, 3]);

        var reference = await storage.SaveAsync(new FileStorageSaveRequest(content, target, "test.jpg", "image/jpeg", 3));

        Assert.Equal(FileStorageProviders.Local, reference.StorageProvider);
        Assert.True(File.Exists(Path.Combine(root, reference.StorageKey)));
    }

    [Fact]
    public void Google_drive_storage_generates_required_photo_folder_path()
    {
        var storage = new GoogleDriveStorageService(new GoogleDriveStorageOptions
        {
            RootFolderId = "root",
            ServiceAccountJson = "{}"
        }, new FakeGoogleDriveClient());

        var path = storage.GenerateTargetPath(new FileStorageTargetContext(
            2026,
            "WP",
            "12345",
            "BinTruck",
            new DateTimeOffset(2026, 9, 14, 9, 15, 22, TimeSpan.Zero)));

        Assert.Equal("Photos/2026/WP/Receipt-12345/BinTruck", path);
    }

    [Fact]
    public async Task Google_drive_storage_reuses_existing_folders()
    {
        var client = new FakeGoogleDriveClient(existingFolders: ["root|Photos", "folder-Photos|2026"]);
        var storage = new GoogleDriveStorageService(new GoogleDriveStorageOptions
        {
            RootFolderId = "root",
            ServiceAccountJson = "{}"
        }, client);

        var folderId = await storage.EnsureFolderPathAsync("Photos/2026/WP/Receipt-12345/BinTruck");

        Assert.Equal("folder-BinTruck", folderId);
        Assert.DoesNotContain(client.CreatedFolders, x => x == "root|Photos");
        Assert.DoesNotContain(client.CreatedFolders, x => x == "folder-Photos|2026");
        Assert.Contains("folder-2026|WP", client.CreatedFolders);
        Assert.Contains("folder-WP|Receipt-12345", client.CreatedFolders);
        Assert.Contains("folder-Receipt-12345|BinTruck", client.CreatedFolders);
    }

    [Fact]
    public async Task Google_drive_storage_fails_clearly_without_credentials()
    {
        var storage = new GoogleDriveStorageService(new GoogleDriveStorageOptions
        {
            RootFolderId = "root"
        });

        await using var content = new MemoryStream([1]);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            storage.SaveAsync(new FileStorageSaveRequest(content, "Photos/2026/WP/Receipt-12345/BinTruck", "test.jpg", "image/jpeg", 1)));

        Assert.Contains("service account credentials", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PostgreSql_provider_requires_connection_string()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder();

        var error = Assert.Throws<InvalidOperationException>(() =>
            CropQcDatabase.Configure(options, DatabaseProviders.PostgreSql, null));

        Assert.Contains("connection string", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class FakeGoogleDriveClient(IEnumerable<string>? existingFolders = null) : IGoogleDriveClient
    {
        private readonly HashSet<string> existingFolders = existingFolders?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
        public List<string> CreatedFolders { get; } = [];

        public Task<GoogleDriveFolder?> FindFolderAsync(string parentFolderId, string name, CancellationToken cancellationToken)
        {
            var key = $"{parentFolderId}|{name}";
            return Task.FromResult(existingFolders.Contains(key)
                ? new GoogleDriveFolder($"folder-{name}", name, null, null)
                : null);
        }

        public Task<GoogleDriveFolder> CreateFolderAsync(string parentFolderId, string name, CancellationToken cancellationToken)
        {
            var key = $"{parentFolderId}|{name}";
            CreatedFolders.Add(key);
            existingFolders.Add(key);
            return Task.FromResult(new GoogleDriveFolder($"folder-{name}", name, null, null));
        }

        public Task<GoogleDriveFile> UploadFileAsync(string folderId, string fileName, string contentType, Stream content, CancellationToken cancellationToken) =>
            Task.FromResult(new GoogleDriveFile($"file-{fileName}", fileName, null, $"https://drive.example/{fileName}", content.Length));
    }
}
