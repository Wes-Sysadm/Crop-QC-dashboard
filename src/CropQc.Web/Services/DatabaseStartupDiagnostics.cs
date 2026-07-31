using System.Data;
using System.Reflection;
using CropQc.Data;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public static class DatabaseStartupDiagnostics
{
    public const string ExpectedSchemaMigration = "20260731014107_SeparatePlanningProjectionsFromActualRuns";

    private static readonly SchemaExpectation[] RequiredSchemaExpectations =
    [
        new("CanonicalOrchards", "CanonicalOrchards", null),
        new("OrchardReportRecipients", "OrchardReportRecipients", null),
        new("Receipts.CanonicalOrchardBlockId", "Receipts", "CanonicalOrchardBlockId"),
        new("CanonicalOrchardBlocks.CanonicalOrchardId", "CanonicalOrchardBlocks", "CanonicalOrchardId"),
        new("PackCodeDefinitions", "PackCodeDefinitions", null),
        new("PackoutAnalysisConfigurations", "PackoutAnalysisConfigurations", null),
        new("PackoutRuns", "PackoutRuns", null),
        new("PackoutEmailAttempts", "PackoutEmailAttempts", null),
        new("PackoutReportSources", "PackoutReportSources", null),
        new("PackoutReportLines", "PackoutReportLines", null),
        new("RunProjectionSources.TotalDefectPercentageSnapshot", "RunProjectionSources", "TotalDefectPercentageSnapshot"),
        new("RunProjections.IsLocked", "RunProjections", "IsLocked"),
        new("RunProjections.LockedAt", "RunProjections", "LockedAt"),
        new("RunProjections.LockedByUserId", "RunProjections", "LockedByUserId"),
        new("QcSamples.DefectInspectionStatus", "QcSamples", "DefectInspectionStatus"),
        new("BinsRunEntries.IsReconciled", "BinsRunEntries", "IsReconciled"),
        new("BinsRunEntries.ReconciledAt", "BinsRunEntries", "ReconciledAt"),
        new("BinsRunEntries.ReconciledByUserId", "BinsRunEntries", "ReconciledByUserId"),
        new("ActualRuns", "ActualRuns", null),
        new("ActualRunRevisions", "ActualRunRevisions", null),
        new("ActualRunOverrideRequests", "ActualRunOverrideRequests", null),
        new("ActualRunOverrideRequestLines", "ActualRunOverrideRequestLines", null),
        new("RoomInventoryAdjustments.ActualRunId", "RoomInventoryAdjustments", "ActualRunId"),
        new("RoomInventoryAdjustments.ActualRunRevisionId", "RoomInventoryAdjustments", "ActualRunRevisionId"),
        new("BinsRunEntries.ActualRunId", "BinsRunEntries", "ActualRunId"),
        new("BinsRunEntries.ActualRunRevisionId", "BinsRunEntries", "ActualRunRevisionId"),
        new("BinsRunEntries.TransactionType", "BinsRunEntries", "TransactionType"),
        new("BinsRunEntries.ReversesBinsRunEntryId", "BinsRunEntries", "ReversesBinsRunEntryId"),
        new("RoomTransfers", "RoomTransfers", null),
        new("RoomInventoryAdjustments.InventoryInvariantVersion", "RoomInventoryAdjustments", "InventoryInvariantVersion"),
        new("RoomInventoryAdjustments.InventoryOperationKey", "RoomInventoryAdjustments", "InventoryOperationKey"),
        new("RoomInventoryAdjustments.RoomTransferId", "RoomInventoryAdjustments", "RoomTransferId"),
        new("RunExpectations", "RunExpectations", null),
        new("RunExpectationSources", "RunExpectationSources", null),
        new("PackoutSourceAllocations", "PackoutSourceAllocations", null),
        new("PackoutRuns.ActualRunId", "PackoutRuns", "ActualRunId"),
        new("PackoutRuns.RunExpectationId", "PackoutRuns", "RunExpectationId"),
        new("PackoutRuns.RunProjectionId nullable", "PackoutRuns", "RunProjectionId", RequireNullable: true)
    ];

    private static readonly SchemaNamedObjectExpectation[] RequiredIndexExpectations =
    [
        new("IX_PackoutRuns_RunExpectationId", "PackoutRuns", "IX_PackoutRuns_RunExpectationId"),
        new("UX_PackoutRuns_ActualRunId", "PackoutRuns", "UX_PackoutRuns_ActualRunId"),
        new("IX_PackoutSourceAllocations_PackoutRunId_RunExpectationSourceId", "PackoutSourceAllocations", "IX_PackoutSourceAllocations_PackoutRunId_RunExpectationSourceId"),
        new("IX_PackoutSourceAllocations_RunExpectationSourceId", "PackoutSourceAllocations", "IX_PackoutSourceAllocations_RunExpectationSourceId"),
        new("IX_RunExpectations_ActualRunId_RevisionNumber", "RunExpectations", "IX_RunExpectations_ActualRunId_RevisionNumber"),
        new("IX_RunExpectations_ActualRunRevisionId", "RunExpectations", "IX_RunExpectations_ActualRunRevisionId"),
        new("IX_RunExpectations_CreatedByUserId", "RunExpectations", "IX_RunExpectations_CreatedByUserId"),
        new("IX_RunExpectationSources_BinsRunEntryId", "RunExpectationSources", "IX_RunExpectationSources_BinsRunEntryId"),
        new("IX_RunExpectationSources_QcSampleId", "RunExpectationSources", "IX_RunExpectationSources_QcSampleId"),
        new("IX_RunExpectationSources_RunExpectationId_BinsRunEntryId", "RunExpectationSources", "IX_RunExpectationSources_RunExpectationId_BinsRunEntryId"),
        new("IX_RunExpectationSources_WarehouseId_RoomId_CropYearSnapshot_LotSnapshot_VarietySnapshot", "RunExpectationSources", "IX_RunExpectationSources_WarehouseId_RoomId_CropYearSnapshot_LotSnapshot_VarietySnapshot")
    ];

    private static readonly SchemaNamedObjectExpectation[] RequiredForeignKeyExpectations =
    [
        new("FK_PackoutRuns_ActualRuns_ActualRunId", "PackoutRuns", "FK_PackoutRuns_ActualRuns_ActualRunId"),
        new("FK_PackoutRuns_RunExpectations_RunExpectationId", "PackoutRuns", "FK_PackoutRuns_RunExpectations_RunExpectationId"),
        new("FK_RunExpectations_ActualRunRevisions_ActualRunRevisionId", "RunExpectations", "FK_RunExpectations_ActualRunRevisions_ActualRunRevisionId"),
        new("FK_RunExpectations_ActualRuns_ActualRunId", "RunExpectations", "FK_RunExpectations_ActualRuns_ActualRunId"),
        new("FK_RunExpectations_Users_CreatedByUserId", "RunExpectations", "FK_RunExpectations_Users_CreatedByUserId"),
        new("FK_RunExpectationSources_BinsRunEntries_BinsRunEntryId", "RunExpectationSources", "FK_RunExpectationSources_BinsRunEntries_BinsRunEntryId"),
        new("FK_RunExpectationSources_QcSamples_QcSampleId", "RunExpectationSources", "FK_RunExpectationSources_QcSamples_QcSampleId"),
        new("FK_RunExpectationSources_RunExpectations_RunExpectationId", "RunExpectationSources", "FK_RunExpectationSources_RunExpectations_RunExpectationId"),
        new("FK_PackoutSourceAllocations_PackoutRuns_PackoutRunId", "PackoutSourceAllocations", "FK_PackoutSourceAllocations_PackoutRuns_PackoutRunId"),
        new("FK_PackoutSourceAllocations_RunExpectationSources_RunExpectationSourceId", "PackoutSourceAllocations", "FK_PackoutSourceAllocations_RunExpectationSources_RunExpectationSourceId")
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
        var applicationVersion = GetApplicationVersion();
        var compiledMigrations = db.Database.GetMigrations().ToArray();
        var latestCompiledMigration = compiledMigrations.LastOrDefault() ?? "None";

        logger.LogInformation(
            "Database startup check. Environment {Environment}; provider {Provider}; application version {ApplicationVersion}; deployed commit {DeployedCommit}; latest compiled migration {LatestCompiledMigration}.",
            environment.EnvironmentName,
            provider,
            applicationVersion,
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
                logger.LogInformation(
                    "Application schema check succeeded. Expected migration {ExpectedMigration}; checked object count {CheckedObjectCount}.",
                    ExpectedSchemaMigration,
                    RequiredSchemaExpectations.Length
                    + RequiredIndexExpectations.Length
                    + RequiredForeignKeyExpectations.Length);
            }
            else
            {
                var referenceId = Guid.NewGuid().ToString("N")[..8];
                var partiallyUpdated = missing.Count < RequiredObjectCount;
                logger.LogError(
                    "Database schema mismatch detected. Reference {ReferenceId}; category {Category}; provider {Provider}; application version {ApplicationVersion}; deployed commit {DeployedCommit}; expected migration {ExpectedMigration}; partially updated {PartiallyUpdated}; missing objects {MissingObjects}; operator action {OperatorAction}. Production data was not modified.",
                    referenceId,
                    DatabaseFailureCategory.SchemaMismatch,
                    provider,
                    applicationVersion,
                    deployedCommit,
                    ExpectedSchemaMigration,
                    partiallyUpdated,
                    string.Join(", ", missing),
                    "Keep the prior compatible deployment active, run the reviewed PostgreSQL preflight, obtain backup and production authorization, apply the approved compatibility script, then verify before redeploying.");
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

    public static async Task<bool> VerifyRequiredSchemaAsync(
        IServiceProvider services,
        IConfiguration configuration,
        IHostEnvironment environment,
        string expectedMigration,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseSchemaDeploymentGate");
        var provider = db.Database.ProviderName ?? "Unknown";
        var deployedCommit = configuration["RENDER_GIT_COMMIT"] ?? configuration["SourceVersion"] ?? "Unknown";
        var applicationVersion = GetApplicationVersion();
        var referenceId = Guid.NewGuid().ToString("N")[..8];

        if (!string.Equals(expectedMigration, ExpectedSchemaMigration, StringComparison.Ordinal))
        {
            logger.LogError(
                "Database deployment gate rejected an unknown expected migration. Reference {ReferenceId}; requested migration {RequestedMigration}; supported migration {ExpectedMigration}.",
                referenceId,
                expectedMigration,
                ExpectedSchemaMigration);
            return false;
        }

        try
        {
            if (!await db.Database.CanConnectAsync(cancellationToken))
            {
                logger.LogError(
                    "Database deployment gate failed. Reference {ReferenceId}; category {Category}; provider {Provider}; environment {Environment}; application version {ApplicationVersion}; deployed commit {DeployedCommit}; expected migration {ExpectedMigration}; operator action {OperatorAction}.",
                    referenceId,
                    DatabaseFailureCategory.ConnectionUnavailable,
                    provider,
                    environment.EnvironmentName,
                    applicationVersion,
                    deployedCommit,
                    expectedMigration,
                    "Restore database connectivity before retrying the deployment. No schema changes were attempted.");
                return false;
            }

            var missing = await FindMissingSchemaObjectsAsync(db, provider, cancellationToken);
            if (missing.Count == 0)
            {
                logger.LogInformation(
                    "Database deployment gate passed. Reference {ReferenceId}; provider {Provider}; environment {Environment}; application version {ApplicationVersion}; deployed commit {DeployedCommit}; expected migration {ExpectedMigration}; checked object count {CheckedObjectCount}.",
                    referenceId,
                    provider,
                    environment.EnvironmentName,
                    applicationVersion,
                    deployedCommit,
                    expectedMigration,
                    RequiredObjectCount);
                return true;
            }

            logger.LogError(
                "Database deployment gate blocked activation. Reference {ReferenceId}; category {Category}; provider {Provider}; environment {Environment}; application version {ApplicationVersion}; deployed commit {DeployedCommit}; expected migration {ExpectedMigration}; partially updated {PartiallyUpdated}; missing objects {MissingObjects}; operator action {OperatorAction}. No schema changes were attempted.",
                referenceId,
                DatabaseFailureCategory.SchemaMismatch,
                provider,
                environment.EnvironmentName,
                applicationVersion,
                deployedCommit,
                expectedMigration,
                missing.Count < RequiredObjectCount,
                string.Join(", ", missing),
                "Keep the prior compatible deployment active. Run the reviewed preflight and apply scripts only after a verified backup and explicit production authorization, then run verification and retry the deployment.");
            return false;
        }
        catch (Exception ex)
        {
            var diagnostic = DatabaseFailureDiagnostics.Classify(ex);
            logger.LogError(
                ex,
                "Database deployment gate failed. Reference {ReferenceId}; category {Category}; provider {Provider}; provider code {ProviderCode}; environment {Environment}; application version {ApplicationVersion}; deployed commit {DeployedCommit}; expected migration {ExpectedMigration}; operator action {OperatorAction}.",
                referenceId,
                diagnostic.Category,
                provider,
                diagnostic.ProviderCode ?? "None",
                environment.EnvironmentName,
                applicationVersion,
                deployedCommit,
                expectedMigration,
                "Review the safe server log and correct connectivity or schema state before retrying. No schema changes were attempted.");
            return false;
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
            foreach (var expectation in RequiredSchemaExpectations)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = SchemaExistsSql(
                    provider,
                    expectation.ColumnName is not null,
                    expectation.RequireNullable);

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

            foreach (var expectation in RequiredIndexExpectations)
            {
                if (!await NamedObjectExistsAsync(
                    connection,
                    provider,
                    expectation,
                    foreignKey: false,
                    cancellationToken))
                {
                    missing.Add(expectation.DisplayName);
                }
            }

            foreach (var expectation in RequiredForeignKeyExpectations)
            {
                if (!await NamedObjectExistsAsync(
                    connection,
                    provider,
                    expectation,
                    foreignKey: true,
                    cancellationToken))
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

    private static string GetApplicationVersion() =>
        typeof(DatabaseStartupDiagnostics).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion
        ?? typeof(DatabaseStartupDiagnostics).Assembly.GetName().Version?.ToString()
        ?? "Unknown";

    private static int RequiredObjectCount =>
        RequiredSchemaExpectations.Length
        + RequiredIndexExpectations.Length
        + RequiredForeignKeyExpectations.Length;

    private static async Task<bool> NamedObjectExistsAsync(
        System.Data.Common.DbConnection connection,
        string provider,
        SchemaNamedObjectExpectation expectation,
        bool foreignKey,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = NamedObjectExistsSql(provider, foreignKey);

        var tableParameter = command.CreateParameter();
        tableParameter.ParameterName = "tableName";
        tableParameter.Value = expectation.TableName;
        command.Parameters.Add(tableParameter);

        var objectParameter = command.CreateParameter();
        objectParameter.ParameterName = "objectName";
        objectParameter.Value = expectation.ObjectName;
        command.Parameters.Add(objectParameter);

        return Convert.ToBoolean(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static string SchemaExistsSql(string provider, bool column, bool requireNullable)
    {
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            if (column && requireNullable)
            {
                return "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = @tableName AND column_name = @columnName AND is_nullable = 'YES');";
            }

            return column
                ? "SELECT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = @tableName AND column_name = @columnName);"
                : "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = current_schema() AND table_name = @tableName);";
        }

        if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            if (column && requireNullable)
            {
                return "SELECT CONVERT(bit, CASE WHEN EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID(@tableName) AND name = @columnName AND is_nullable = 1) THEN 1 ELSE 0 END);";
            }

            return column
                ? "SELECT CONVERT(bit, CASE WHEN COL_LENGTH(@tableName, @columnName) IS NULL THEN 0 ELSE 1 END);"
                : "SELECT CONVERT(bit, CASE WHEN OBJECT_ID(@tableName, 'U') IS NULL THEN 0 ELSE 1 END);";
        }

        throw new InvalidOperationException($"Unsupported database provider '{provider}' for schema diagnostics.");
    }

    private static string NamedObjectExistsSql(string provider, bool foreignKey)
    {
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            return foreignKey
                ? "SELECT EXISTS (SELECT 1 FROM pg_constraint c JOIN pg_class t ON t.oid = c.conrelid JOIN pg_namespace n ON n.oid = t.relnamespace WHERE n.nspname = current_schema() AND t.relname = @tableName AND c.conname = left(@objectName, 63) AND c.contype = 'f');"
                : "SELECT EXISTS (SELECT 1 FROM pg_indexes WHERE schemaname = current_schema() AND tablename = @tableName AND indexname = left(@objectName, 63));";
        }

        if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            return foreignKey
                ? "SELECT CONVERT(bit, CASE WHEN EXISTS (SELECT 1 FROM sys.foreign_keys WHERE parent_object_id = OBJECT_ID(@tableName) AND name = @objectName) THEN 1 ELSE 0 END);"
                : "SELECT CONVERT(bit, CASE WHEN EXISTS (SELECT 1 FROM sys.indexes WHERE object_id = OBJECT_ID(@tableName) AND name = @objectName) THEN 1 ELSE 0 END);";
        }

        throw new InvalidOperationException($"Unsupported database provider '{provider}' for schema diagnostics.");
    }

    private sealed record SchemaExpectation(
        string DisplayName,
        string TableName,
        string? ColumnName,
        bool RequireNullable = false);

    private sealed record SchemaNamedObjectExpectation(
        string DisplayName,
        string TableName,
        string ObjectName);
}
