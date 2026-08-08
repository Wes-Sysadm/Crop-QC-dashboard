using CropQc.Web.Services;
using Npgsql;

namespace CropQc.Api.Tests;

public sealed class DatabaseRegressionDiagnosticsTests
{
    [Theory]
    [InlineData("42P01")]
    [InlineData("42703")]
    [InlineData("42704")]
    public void PostgreSqlMissingSchemaErrors_AreClassifiedAsSchemaMismatch(string sqlState)
    {
        var exception = new PostgresException(
            "relation or column is missing",
            "ERROR",
            "ERROR",
            sqlState);

        var result = DatabaseFailureDiagnostics.Classify(exception);

        Assert.Equal(DatabaseFailureCategory.SchemaMismatch, result.Category);
        Assert.Equal(sqlState, result.ProviderCode);
        Assert.Contains("schema is behind", result.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("relation", result.SafeMessage, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("column", result.SafeMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PostgreSqlAuthenticationError_IsClassifiedSeparately()
    {
        var exception = new PostgresException("authentication failed", "FATAL", "FATAL", "28P01");

        var result = DatabaseFailureDiagnostics.Classify(exception);

        Assert.Equal(DatabaseFailureCategory.AuthenticationFailure, result.Category);
        Assert.Equal("28P01", result.ProviderCode);
    }

    [Fact]
    public void PostgreSqlConnectionError_IsClassifiedSeparately()
    {
        var result = DatabaseFailureDiagnostics.Classify(new NpgsqlException("Connection refused."));

        Assert.Equal(DatabaseFailureCategory.ConnectionUnavailable, result.Category);
    }

    [Fact]
    public void PostgreSqlNonSchemaError_RemainsAQueryFailure()
    {
        var exception = new PostgresException("duplicate value", "ERROR", "ERROR", "23505");

        var result = DatabaseFailureDiagnostics.Classify(exception);

        Assert.Equal(DatabaseFailureCategory.QueryFailure, result.Category);
        Assert.Equal("23505", result.ProviderCode);
    }

    [Fact]
    public void OrchardRecipientMigration_UsesProviderSpecificEmailSnapshotTypes()
    {
        var migration = File.ReadAllText(FindRepositoryFile(
            "src", "CropQc.Data", "Migrations", "20260721233604_AddOrchardReportRecipients.cs"));

        Assert.Contains("character varying(2000)", migration);
        Assert.Contains("nvarchar(2000)", migration);
        Assert.Contains("character varying(320)", migration);
        Assert.Contains("nvarchar(320)", migration);
    }

    [Fact]
    public void ProductionCompatibilityScript_IsTransactionalNonDestructiveAndDoesNotForgeMigrationHistory()
    {
        var script = File.ReadAllText(FindRepositoryFile(
            "scripts", "postgresql", "apply-orchard-report-recipients-schema.sql"));

        Assert.Contains("begin;", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("commit;", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CanonicalOrchards", script);
        Assert.Contains("OrchardReportRecipients", script);
        Assert.Contains("CanonicalOrchardBlockId", script);
        Assert.DoesNotContain("delete from", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("drop table", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("truncate", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("insert into \"__EFMigrationsHistory\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("update \"__EFMigrationsHistory\"", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PreflightScript_IsReadOnly()
    {
        var script = File.ReadAllText(FindRepositoryFile(
            "scripts", "postgresql", "preflight-orchard-report-recipients.sql"));

        Assert.Contains("20260721233604_AddOrchardReportRecipients", script);
        Assert.DoesNotContain("alter table", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("create table", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("insert into", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("update ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("delete from", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PackoutProductionPreflight_IsReadOnlyAndReportsCompatibilityState()
    {
        var script = File.ReadAllText(FindRepositoryFile(
            "scripts", "postgresql", "preflight-packout-projection-reconciliation.sql"));

        Assert.Contains("begin transaction read only;", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("20260729165910_AddPackoutProjectionReconciliation", script);
        Assert.Contains("__EFMigrationsHistory", script);
        Assert.Contains("PARTIALLY APPLIED", script);
        Assert.Contains("samples_with_defects", script);
        Assert.Contains("Orphaned PackoutRuns", script);
        Assert.DoesNotContain("alter table", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("create table", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("delete from", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("truncate ", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PackoutProductionApply_IsTransactionalIdempotentAndDoesNotForgeHistory()
    {
        var script = File.ReadAllText(FindRepositoryFile(
            "scripts", "postgresql", "apply-packout-projection-reconciliation-schema.sql"));

        Assert.Contains(@"\set ON_ERROR_STOP on", script);
        Assert.Contains("begin;", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("commit;", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pg_advisory_xact_lock", script);
        Assert.Contains("add column if not exists", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("create table if not exists", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("create unique index if not exists", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No defects found", script);
        Assert.Contains("Defects found", script);
        Assert.Contains("\"PackoutAnalysisConfigurations\"", script);
        Assert.DoesNotContain("delete from", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("drop table", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("truncate ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("insert into \"__EFMigrationsHistory\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("update \"__EFMigrationsHistory\"", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PackoutProductionVerification_IsReadOnlyAndChecksApplicationQueries()
    {
        var script = File.ReadAllText(FindRepositoryFile(
            "scripts", "postgresql", "verify-packout-projection-reconciliation.sql"));

        Assert.Contains("begin transaction read only;", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DefectInspectionStatus", script);
        Assert.Contains("PackoutAnalysisConfigurations", script);
        Assert.Contains("where false;", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Orphaned packout reconciliation relationships", script);
        Assert.DoesNotContain("alter table", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("create table", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\ninsert into ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\nupdate ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\ndelete from ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("truncate ", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ActualRunProductionPreflight_IsReadOnlyAndReportsPartialObjectState()
    {
        var script = File.ReadAllText(FindRepositoryFile(
            "scripts", "postgresql", "preflight-projection-actual-run-separation.sql"));

        Assert.Contains("begin transaction read only;", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("RunExpectations", script);
        Assert.Contains("PackoutRuns.ActualRunId", script);
        Assert.Contains("pg_index", script);
        Assert.Contains("pg_constraint", script);
        Assert.Contains("expected_nullable", script);
        Assert.Contains("expected_unique", script);
        Assert.DoesNotContain("alter table", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("create table", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\ninsert into ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\nupdate ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\ndelete from ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("truncate ", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ActualRunProductionApply_IsTransactionalIdempotentAndDoesNotForgeHistory()
    {
        var path = FindRepositoryFile(
            "scripts", "postgresql", "apply-projection-actual-run-separation-schema.sql");
        var bytes = File.ReadAllBytes(path);
        var script = File.ReadAllText(path);

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf,
            "The psql production apply script must be UTF-8 without a byte-order mark.");

        Assert.Contains(@"\set ON_ERROR_STOP on", script);
        Assert.Contains("START TRANSACTION", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("COMMIT", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pg_advisory_xact_lock", script);
        Assert.Contains("ADD COLUMN IF NOT EXISTS", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CREATE TABLE IF NOT EXISTS", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("INDEX IF NOT EXISTS", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("POSTCHECK_NAMED_OBJECTS", script);
        Assert.Contains("pg_constraint", script);
        Assert.Contains("pg_index", script);
        Assert.Contains("PRIMARY_KEYS", script);
        Assert.Contains("ALTER COLUMN \"CalculatedAt\" SET NOT NULL", script);
        Assert.Contains("Transaction rolled back", script);
        Assert.DoesNotContain("DELETE FROM", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO \"__EFMigrationsHistory\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE \"__EFMigrationsHistory\"", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ActualRunProductionVerification_IsReadOnlyAndExercisesApplicationQueries()
    {
        var script = File.ReadAllText(FindRepositoryFile(
            "scripts", "postgresql", "verify-projection-actual-run-separation.sql"));

        Assert.Contains("begin transaction read only;", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("application_object_state_ready", script);
        Assert.Contains("where false;", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("required foreign key", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alter table", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("create table", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\ninsert into ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\nupdate ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\ndelete from ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("truncate ", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ActualRunController_ReturnsSafeSchemaBehindMessageWithReference()
    {
        var controller = File.ReadAllText(FindRepositoryFile(
            "src", "CropQc.Web", "Controllers", "BinsRunController.cs"));

        Assert.Contains("IsSchemaMismatch", controller);
        Assert.Contains("database update required by this release has not been completed", controller);
        Assert.Contains("No inventory was changed", controller);
        Assert.Contains("Reference {referenceId}", controller);
        Assert.Contains("Transaction was not committed", controller);
        Assert.DoesNotContain("RunExpectations does not exist", controller);
    }

    [Fact]
    public void RenderUsesFailClosedLatestSchemaGateBeforeBothWebDeployments()
    {
        var blueprint = File.ReadAllText(FindRepositoryFile("render.yaml"));
        var command = "preDeployCommand: dotnet CropQc.Web.dll --verify-schema=20260807044836_AddEndOfDayFillReporting";

        Assert.Equal(2, blueprint.Split(command, StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("dotnet ef database update", blueprint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FacilityRunReportingProductionPreflight_IsReadOnlyAndDetectsPartialObjectState()
    {
        var script = File.ReadAllText(FindRepositoryFile(
            "scripts", "postgresql", "preflight-facility-run-reporting-schema.sql"));

        Assert.Contains("begin transaction read only;", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("20260804052104_AddFacilityRunReporting", script);
        Assert.Contains("UserEmploymentHistory", script);
        Assert.Contains("GrowerNumberSnapshot", script);
        Assert.Contains("Unexpected partial facility-reporting schema", script);
        Assert.DoesNotContain("alter table", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("create table", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\ninsert into ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\nupdate ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\ndelete from ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("truncate ", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FacilityRunReportingProductionApply_IsTransactionalFailClosedAndOnlyRecordsItsOwnMigration()
    {
        var script = File.ReadAllText(FindRepositoryFile(
            "scripts", "postgresql", "apply-facility-run-reporting-schema.sql"));

        Assert.Contains(@"\set ON_ERROR_STOP on", script);
        Assert.Contains("start transaction;", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("commit;", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pg_advisory_xact_lock", script);
        Assert.Contains("Unexpected partial facility-reporting schema", script);
        Assert.Contains("20260804052104_AddFacilityRunReporting", script);
        Assert.Contains("INSERT INTO \"__EFMigrationsHistory\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("20260731014107_SeparatePlanningProjectionsFromActualRuns', '8.0.11", script);
        Assert.DoesNotContain("delete from", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("drop table", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("truncate ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("update \"__EFMigrationsHistory\"", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FacilityRunReportingProductionVerification_IsReadOnlyAndChecksRequiredRelationships()
    {
        var script = File.ReadAllText(FindRepositoryFile(
            "scripts", "postgresql", "verify-facility-run-reporting.sql"));

        Assert.Contains("begin transaction read only;", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UserEmploymentHistory", script);
        Assert.Contains("IX_UserEmploymentHistory_UserId_ChangedAt", script);
        Assert.Contains("FK_BinsRunEntries_Warehouses_ReportingFacilityWarehouseId", script);
        Assert.DoesNotContain("alter table", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("create table", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\ninsert into ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\nupdate ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\ndelete from ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("truncate ", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EndOfDayFillProductionPreflight_IsReadOnlyAndReportsExactCandidatesSafely()
    {
        var script = File.ReadAllText(FindRepositoryFile("scripts", "postgresql", "preflight-end-of-day-fill-reporting.sql"));
        Assert.Contains("begin transaction read only;", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("candidate_room_count", script);
        Assert.Contains("gmail_credential_present", script);
        Assert.Contains("gmail_send_scope_present", script);
        Assert.Contains("generate_series(1, 22)", script);
        Assert.Contains("generate_series(3, 16)", script);
        Assert.Contains("generate_series(13, 17)", script);
        Assert.Contains("generate_series(1, 12)", script);
        Assert.Contains("generate_series(1, 6)", script);
        Assert.Contains("expected_room_count <> 63", script);
        Assert.Contains("wp_candidate_count <> 36", script);
        Assert.Contains("ebs_candidate_count <> 27", script);
        Assert.Contains("'mcd-01'", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("excluded_not_seeded", script);
        Assert.DoesNotContain("AccessTokenEncrypted\" AS", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("RefreshTokenEncrypted\" AS", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("alter table", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("create table", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\ninsert into ", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EndOfDayFillProductionApply_IsTransactionalIdempotentAndLeavesMigrationHistoryUntouched()
    {
        var script = File.ReadAllText(FindRepositoryFile("scripts", "postgresql", "apply-end-of-day-fill-reporting-schema.sql"));
        Assert.Contains(@"\set ON_ERROR_STOP on", script);
        Assert.Contains("pg_advisory_xact_lock", script);
        Assert.Contains("ON CONFLICT", script);
        Assert.Contains("Unsupported partial End of Day Fill schema", script);
        Assert.Contains("ALTER TABLE \"Rooms\" ADD COLUMN \"EndOfDayFillReportGroupId\"", script);
        Assert.Contains("Preserving authoritative Room master-data assignments on repeat apply", script);
        Assert.Contains("Room capacity fingerprint changed", script);
        Assert.Contains("generate_series(1, 22)", script);
        Assert.Contains("generate_series(3, 16)", script);
        Assert.Contains("wp_candidate_count <> 36", script);
        Assert.Contains("ebs_candidate_count <> 27", script);
        Assert.Contains("lower(btrim(r.\"Code\"))=lower(e.room_code)", script);
        Assert.DoesNotContain("lower(btrim(w.\"Code\")) IN ('dh','mcdougall','ebs')\n  AND g.\"Name\"", script);
        Assert.DoesNotContain("CREATE TABLE \"EndOfDayFillReportGroupRooms\"", script);
        Assert.Contains("migration history is intentionally untouched", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO \"__EFMigrationsHistory\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("delete from", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("truncate ", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EndOfDayFillProductionVerification_IsReadOnlyAndChecksSchemaConfigurationAndEmptyHistory()
    {
        var script = File.ReadAllText(FindRepositoryFile("scripts", "postgresql", "verify-end-of-day-fill-reporting.sql"));
        Assert.Contains("begin transaction read only;", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CK_EndOfDayFillReportGroups_Facility", script);
        Assert.Contains("IX_EndOfDayFillReportSends_SuccessRevisionKey", script);
        Assert.Contains("FK_EndOfDayFillSendReservations_EndOfDayFillReportSends_SendAttemptId", script);
        Assert.Contains("FK_Rooms_EndOfDayFillReportGroups_EndOfDayFillReportGroupId", script);
        Assert.Contains("Obsolete room-membership join table must not exist", script);
        Assert.Contains("wp_assignment_count <> 36", script);
        Assert.Contains("ebs_assignment_count <> 27", script);
        Assert.Contains("MCD-01 must remain excluded", script);
        Assert.Contains("excluded_not_seeded", script);
        Assert.Contains("initial_send_count", script);
        Assert.Contains("initial_reservation_count", script);
        Assert.DoesNotContain("alter table", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("create table", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\ninsert into ", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EndOfDayFillMigration_SeedsOnlyTheReviewedRoomAllowlist()
    {
        var migration = File.ReadAllText(FindRepositoryFile(
            "src", "CropQc.Data", "Migrations", "20260807044836_AddEndOfDayFillReporting.cs"));

        Assert.Contains("'dh-1','dh-2','dh-3'", migration);
        Assert.Contains("'dh-20','dh-21','dh-22'", migration);
        Assert.Contains("'mcd-3','mcd-4','mcd-5'", migration);
        Assert.Contains("'mcd-14','mcd-15','mcd-16'", migration);
        Assert.Contains("'lamb-13','lamb-14','lamb-15','lamb-16','lamb-17'", migration);
        Assert.Contains("'evans-backside','evans-bkt','evans-hallway1','evans-hallway2'", migration);
        Assert.Contains("'bm-1','bm-2','bm-3','bm-4','bm-5','bm-6'", migration);
        Assert.DoesNotContain("'mcd-01'", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lower(btrim(w.\"Code\")) IN ('dh', 'mcdougall')", migration);
        Assert.DoesNotContain("LOWER(LTRIM(RTRIM(w.[Code]))) IN ('dh', 'mcdougall')", migration);
    }

    [Fact]
    public void StartupDiagnosticsLogSafeLatestSchemaDetailsAndOperatorAction()
    {
        var diagnostics = File.ReadAllText(FindRepositoryFile(
            "src", "CropQc.Web", "Services", "DatabaseStartupDiagnostics.cs"));
        var program = File.ReadAllText(FindRepositoryFile(
            "src", "CropQc.Web", "Program.cs"));

        Assert.Contains("ExpectedSchemaMigration", diagnostics);
        Assert.Contains("\"ActualRuns\"", diagnostics);
        Assert.Contains("\"ActualRunRevisions\"", diagnostics);
        Assert.Contains("\"ActualRunOverrideRequests\"", diagnostics);
        Assert.Contains("\"ActualRunOverrideRequestLines\"", diagnostics);
        Assert.Contains("\"RoomTransfers\"", diagnostics);
        Assert.Contains("\"RoomInventoryAdjustments\", \"InventoryInvariantVersion\"", diagnostics);
        Assert.Contains("\"RoomInventoryAdjustments\", \"InventoryOperationKey\"", diagnostics);
        Assert.Contains("\"RoomInventoryAdjustments\", \"RoomTransferId\"", diagnostics);
        Assert.Contains("\"RunExpectations\"", diagnostics);
        Assert.Contains("\"RunExpectationSources\"", diagnostics);
        Assert.Contains("\"PackoutSourceAllocations\"", diagnostics);
        Assert.Contains("\"PackoutRuns\", \"ActualRunId\"", diagnostics);
        Assert.Contains("\"PackoutRuns\", \"RunExpectationId\"", diagnostics);
        Assert.Contains("\"UserEmploymentHistory\"", diagnostics);
        Assert.Contains("\"Users\", \"EmploymentFacility\"", diagnostics);
        Assert.Contains("\"ActualRuns\", \"RunFacilityWarehouseId\"", diagnostics);
        Assert.Contains("\"ActualRunOverrideRequests\", \"RunFacilityWarehouseId\"", diagnostics);
        Assert.Contains("\"BinsRunEntries\", \"ReportingFacilityWarehouseId\"", diagnostics);
        Assert.Contains("IX_UserEmploymentHistory_UserId_ChangedAt", diagnostics);
        Assert.Contains("FK_BinsRunEntries_Warehouses_ReportingFacilityWarehouseId", diagnostics);
        Assert.Contains("\"ReceiptInventoryOverrides\"", diagnostics);
        Assert.Contains("\"Receipts\", \"ConcurrencyVersion\"", diagnostics);
        Assert.Contains("\"RoomInventoryAdjustments\", \"ReceiptInventoryOverrideId\"", diagnostics);
        Assert.Contains("IX_ReceiptInventoryOverrides_OperationKey", diagnostics);
        Assert.Contains("FK_RoomInventoryAdjustments_ReceiptOverrides_OverrideId", diagnostics);
        Assert.Contains("RequireNullable: true", diagnostics);
        Assert.Contains("RequiredIndexExpectations", diagnostics);
        Assert.Contains("RequiredForeignKeyExpectations", diagnostics);
        Assert.Contains("Reference {ReferenceId}", diagnostics);
        Assert.Contains("application version {ApplicationVersion}", diagnostics);
        Assert.Contains("expected migration {ExpectedMigration}", diagnostics);
        Assert.Contains("partially updated {PartiallyUpdated}", diagnostics);
        Assert.Contains("missing objects {MissingObjects}", diagnostics);
        Assert.Contains("operator action {OperatorAction}", diagnostics);
        Assert.Contains("No schema changes were attempted", diagnostics);
        Assert.Contains("--verify-schema=", program);
        Assert.Contains("VerifyRequiredSchemaAsync", program);
        Assert.Contains("VerifyInventoryDeductionReadinessAsync", program);
        Assert.Contains("schemaIsReady && deductionsAreReady", program);
        Assert.DoesNotContain("Database.Migrate", program, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DashboardFallback_LogsClassifiedFailuresAndNoLongerCallsEveryFailureUnavailable()
    {
        var service = File.ReadAllText(FindRepositoryFile(
            "src", "CropQc.Web", "Services", "DashboardDataService.cs"));

        Assert.Contains("DatabaseFailureDiagnostics.Classify", service);
        Assert.Contains("provider code {ProviderCode}", service);
        Assert.DoesNotContain("Database is not available yet. The dashboard shell is running with empty data.", service);
    }

    private static string FindRepositoryFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file {Path.Combine(pathParts)}.");
    }
}
