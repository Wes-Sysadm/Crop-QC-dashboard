using System.Data;
using CropQc.Data;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public static class DatabaseStartupDiagnostics
{
    private static readonly SchemaExpectation[] OrchardRecipientExpectations =
    [
        new("Migration history", "__EFMigrationsHistory", null),
        new("Canonical orchard table", "CanonicalOrchards", null),
        new("Orchard recipient table", "OrchardReportRecipients", null),
        new("Receipt canonical block column", "Receipts", "CanonicalOrchardBlockId"),
        new("Canonical block orchard column", "CanonicalOrchardBlocks", "CanonicalOrchardId")
    ];

    public static async Task InspectAsync(
        IServiceProvider services,
        IConfiguration configuration,
        IHostEnvironment environment,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseStartupDiagnostics");
        var provider = db.Database.ProviderName ?? "Unknown";
        var deployedCommit = configuration["RENDER_GIT_COMMIT"] ?? configuration["SourceVersion"] ?? "Unknown";
        var compiledMigrations = db.Database.GetMigrations().ToArray();
        var latestCompiledMigration = compiledMigrations.LastOrDefault() ?? "None";

        logger.LogInformation(
            "Database startup check. Environment {Environment}; provider {Provider}; deployed commit {DeployedCommit}; latest compiled migration {LatestCompiledMigration}.",
            environment.EnvironmentName,
            provider,
            deployedCommit,
            latestCompiledMigration);

        try
        {
            if (!await db.Database.CanConnectAsync(cancellationToken))
            {
                logger.LogError(
                    "Database startup check failed. Category {Category}; provider {Provider}; the configured database did not accept a connection.",
                    DatabaseFailureCategory.ConnectionUnavailable,
                    provider);
                return;
            }
        }
        catch (Exception ex)
        {
            var diagnostic = DatabaseFailureDiagnostics.Classify(ex);
            logger.LogError(
                ex,
                "Database startup check failed. Category {Category}; provider {Provider}; provider code {ProviderCode}.",
                diagnostic.Category,
                provider,
                diagnostic.ProviderCode ?? "None");
            return;
        }

        logger.LogInformation("Database startup connection check succeeded for provider {Provider}.", provider);

        try
        {
            var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToArray();
            var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
            logger.LogInformation(
                "Database migration status. Applied count {AppliedCount}; pending count {PendingCount}; latest applied {LatestApplied}; latest compiled {LatestCompiled}.",
                applied.Length,
                pending.Length,
                applied.LastOrDefault() ?? "None",
                latestCompiledMigration);

            if (pending.Length > 0)
            {
                logger.LogWarning(
                    "Database schema tracking is behind the application. Pending migrations: {PendingMigrations}.",
                    string.Join(", ", pending));
            }
        }
        catch (Exception ex)
        {
            var diagnostic = DatabaseFailureDiagnostics.Classify(ex);
            logger.LogError(
                ex,
                "Database migration status check failed. Category {Category}; provider {Provider}; provider code {ProviderCode}.",
                diagnostic.Category,
                provider,
                diagnostic.ProviderCode ?? "None");
        }

        try
        {
            var missing = await FindMissingSchemaObjectsAsync(db, provider, cancellationToken);
            if (missing.Count == 0)
            {
                logger.LogInformation("Orchard recipient schema check succeeded.");
            }
            else
            {
                logger.LogError(
                    "Database schema mismatch detected. Category {Category}; provider {Provider}; missing objects: {MissingObjects}. Production data was not modified.",
                    DatabaseFailureCategory.SchemaMismatch,
                    provider,
                    string.Join(", ", missing));
            }
        }
        catch (Exception ex)
        {
            var diagnostic = DatabaseFailureDiagnostics.Classify(ex);
            logger.LogError(
                ex,
                "Database schema inspection failed. Category {Category}; provider {Provider}; provider code {ProviderCode}.",
                diagnostic.Category,
                provider,
                diagnostic.ProviderCode ?? "None");
        }
    }

    private static async Task<IReadOnlyList<string>> FindMissingSchemaObjectsAsync(
        CropQcDbContext db,
        string provider,
        CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var openedHere = connection.State != ConnectionState.Open;
        if (openedHere)
        {
            await connection.OpenAsync(cancellationToken);
        }

        try
        {
            var missing = new List<string>();
            foreach (var expectation in OrchardRecipientExpectations)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = SchemaExistsSql(provider, expectation.ColumnName is not null);

                var tableParameter = command.CreateParameter();
                tableParameter.ParameterName = "tableName";
                tableParameter.Value = expectation.TableName;
                command.Parameters.Add(tableParameter);

                if (expectation.ColumnName is not null)
                {
                    var columnParameter = command.CreateParameter();
                    columnParameter.ParameterName = "columnName";
                    columnParameter.Value = expectation.ColumnName;
                    command.Parameters.Add(columnParameter);
                }

                var result = await command.ExecuteScalarAsync(cancellationToken);
                if (!Convert.ToBoolean(result))
                {
                    missing.Add(expectation.DisplayName);
                }
            }

            return missing;
        }
        finally
        {
            if (openedHere)
            {
                await connection.CloseAsync();
            }
        }
    }

    private static string SchemaExistsSql(string provider, bool column)
    {
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            return column
                ? "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = @tableName AND column_name = @columnName);"
                : "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = @tableName);";
        }

        if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return column
                ? "SELECT CONVERT(bit, CASE WHEN COL_LENGTH(@tableName, @columnName) IS NULL THEN 0 ELSE 1 END);"
                : "SELECT CONVERT(bit, CASE WHEN OBJECT_ID(@tableName, 'U') IS NULL THEN 0 ELSE 1 END);";
        }

        throw new InvalidOperationException($"Unsupported database provider '{provider}' for schema diagnostics.");
    }

    private sealed record SchemaExpectation(string DisplayName, string TableName, string? ColumnName);
}
