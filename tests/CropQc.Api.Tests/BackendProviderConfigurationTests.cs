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
    public void PostgreSql_provider_requires_connection_string()
    {
        var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder();

        var error = Assert.Throws<InvalidOperationException>(() =>
            CropQcDatabase.Configure(options, DatabaseProviders.PostgreSql, null));

        Assert.Contains("connection string", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
