using System.Data.Common;

namespace CropQc.Data;

public static class ProductionDatabaseSafety
{
    private static readonly string[] DisposableDatabaseMarkers =
        ["test", "testing", "ci", "local", "dev", "scratch", "temp", "disposable"];

    public static void RejectProductionStartupMutation(
        bool isProduction,
        bool ensureCreatedOnStartup,
        bool seedMasterDataOnStartup)
    {
        if (!isProduction)
        {
            return;
        }

        if (ensureCreatedOnStartup)
        {
            throw new InvalidOperationException("Database EnsureCreated is prohibited in production.");
        }

        if (seedMasterDataOnStartup)
        {
            throw new InvalidOperationException("Automatic master-data seeding is prohibited in production.");
        }
    }

    public static void RequireClearlyDisposableTestDatabase(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("A disposable test database connection is required.");
        }

        var builder = new DbConnectionStringBuilder { ConnectionString = connectionString };
        var databaseName = Value(builder, "Database") ?? Value(builder, "Initial Catalog") ?? "";
        if (!DisposableDatabaseMarkers.Any(marker => databaseName.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "Automated PostgreSQL tests may run only against a database whose name clearly identifies it as disposable.");
        }
    }

    private static string? Value(DbConnectionStringBuilder builder, string key) =>
        builder.TryGetValue(key, out var value) ? value?.ToString() : null;
}
