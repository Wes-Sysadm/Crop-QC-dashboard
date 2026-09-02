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
        var command = "preDeployCommand: dotnet CropQc.Web.dll --verify-schema=20260902140938_AddInventoryIdentityCorrections";

        Assert.Equal(2, blueprint.Split(command, StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("dotnet ef database update", blueprint, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EndOfDayFillWarehouseCompatibilityPackage_IsBoundedAndLeavesMigrationHistoryUntouched()
    {
        var preflight = File.ReadAllText(FindRepositoryFile("scripts", "postgresql", "preflight-end-of-day-fill-warehouse-scope.sql"));
        var apply = File.ReadAllText(FindRepositoryFile("scripts", "postgresql", "apply-end-of-day-fill-warehouse-scope.sql"));
        var verify = File.ReadAllText(FindRepositoryFile("scripts", "postgresql", "verify-end-of-day-fill-warehouse-scope.sql"));

        Assert.Contains("BEGIN TRANSACTION READ ONLY", preflight, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("State A", preflight);
        Assert.Contains("State B", preflight);
        Assert.Contains("State C", preflight);
        Assert.DoesNotContain("CREATE TABLE", preflight, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("pg_advisory_xact_lock", apply);
        Assert.Contains("ADD COLUMN \"WarehouseId\" integer", apply);
        Assert.Contains("SET \"WarehouseId\"=4", apply);
        Assert.Contains("SET \"WarehouseId\"=1", apply);
        Assert.Contains("IX_EndOfDayFillReportGroups_WarehouseId", apply);
        Assert.Contains("FK_EndOfDayFillReportGroups_Warehouses_WarehouseId", apply);
        Assert.Contains("cropqc.test_force_eod_fill_warehouse_failure", apply);
        Assert.Contains("transaction will roll back", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO \"__EFMigrationsHistory\"", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE \"__EFMigrationsHistory\"", apply, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("migration_history_intentionally_unchanged", verify);
        var adminView = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "EndOfDayFillAdmin", "Index.cshtml"));
        Assert.Contains("@warehouse.Label", adminView);
        Assert.DoesNotContain("stored warehouse", adminView, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EndOfDayFillWarehousePreviewVerification_IsExplicitlyReadOnly()
    {
        var program = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Program.cs"));
        var commandBlock = program[program.IndexOf("--verify-end-of-day-fill-warehouse-previews", StringComparison.Ordinal)..];
        commandBlock = commandBlock[..commandBlock.IndexOf("Tr108859DroppedBinsCorrectionConstants.CommandName", StringComparison.Ordinal)];

        Assert.Contains("GetPreviewAsync", commandBlock);
        Assert.Contains("GetCurrentLotsAsync", commandBlock);
        Assert.Contains("includedAuthoritativeTotal", commandBlock);
        Assert.Contains("allRoomAuthoritativeTotal", commandBlock);
        Assert.DoesNotContain("SaveChanges", commandBlock, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SendAsync", commandBlock, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RoomInventoryLossPreflight_IsReadOnlyAndFailsClosedOnPartialState()
    {
        var path = FindRepositoryFile("scripts", "postgresql", "preflight-room-inventory-losses.sql");
        var bytes = File.ReadAllBytes(path);
        var script = File.ReadAllText(path);

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf);
        Assert.Contains("BEGIN TRANSACTION READ ONLY", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("STATE A", script);
        Assert.Contains("STATE B", script);
        Assert.Contains("STATE C", script);
        Assert.Contains("exactly PK plus six FKs", script);
        Assert.Contains("PK index plus seven secondary indexes", script);
        Assert.Contains("RoomInventoryAdjustments.RoomInventoryLossId", script);
        Assert.DoesNotContain("CREATE TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\nINSERT INTO ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\nUPDATE ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\nDELETE FROM ", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RoomInventoryLossApply_IsTransactionalIdempotentAndDoesNotForgeHistory()
    {
        var path = FindRepositoryFile("scripts", "postgresql", "apply-room-inventory-losses-schema.sql");
        var bytes = File.ReadAllBytes(path);
        var script = File.ReadAllText(path);

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf);
        Assert.Contains(@"\set ON_ERROR_STOP on", script);
        Assert.Contains("pg_advisory_xact_lock", script);
        Assert.Contains("CREATE TABLE \"RoomInventoryLosses\"", script);
        Assert.Contains("ALTER TABLE \"RoomInventoryAdjustments\" ADD COLUMN \"RoomInventoryLossId\"", script);
        Assert.Contains("GENERATED BY DEFAULT AS IDENTITY", script);
        Assert.Contains("Forced compatibility-apply failure", script);
        Assert.Contains("COMMIT", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO \"__EFMigrationsHistory\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE \"__EFMigrationsHistory\"", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RoomInventoryLossPostgreSqlHarness_CoversParityFailuresIdempotencyAndRollback()
    {
        var path = FindRepositoryFile("scripts", "test-room-inventory-losses-production-schema.ps1");
        var bytes = File.ReadAllBytes(path);
        var script = File.ReadAllText(path);

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf);
        Assert.Contains("postgres:18", script);
        Assert.Contains("20260809151943_AddInventoryDiagnosticAcknowledgments", script);
        Assert.Contains("20260812061125_AddRoomInventoryLosses", script);
        Assert.Contains("wrong_column", script);
        Assert.Contains("missing_index", script);
        Assert.Contains("wrong_fk", script);
        Assert.Contains("test_force_room_inventory_loss_failure", script);
        Assert.Contains("Get-ObjectSignature", script);
        Assert.Contains("Migration history unchanged", script);
        Assert.Contains("311-object gate", script);
    }

    [Fact]
    public void InventoryDiagnosticAcknowledgmentPreflight_IsReadOnlyAndChecksExactObjectState()
    {
        var path = FindRepositoryFile("scripts", "postgresql", "preflight-inventory-diagnostic-acknowledgments.sql");
        var bytes = File.ReadAllBytes(path);
        var script = File.ReadAllText(path);

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf);
        Assert.Contains("BEGIN TRANSACTION READ ONLY", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("STATE A", script);
        Assert.Contains("STATE B", script);
        Assert.Contains("STATE C", script);
        Assert.Contains("20260809151943_AddInventoryDiagnosticAcknowledgments", script);
        Assert.Contains("expected exactly the PK index and five secondary indexes", script);
        Assert.Contains("confdeltype='r'", script);
        Assert.Contains("confdeltype='n'", script);
        Assert.Contains("identity_generation", script);
        Assert.Contains("PK_RoomInventoryAdjustments", script);
        Assert.Contains("PK_Users", script);
        Assert.Contains("RolePageAccesses", script);
        Assert.Contains("every active user must have exactly one role", script);
        Assert.DoesNotContain("CREATE TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ALTER TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\nINSERT INTO ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\nUPDATE ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\nDELETE FROM ", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InventoryDiagnosticAcknowledgmentApply_IsTransactionalIdempotentAndDoesNotForgeHistory()
    {
        var path = FindRepositoryFile("scripts", "postgresql", "apply-inventory-diagnostic-acknowledgments-schema.sql");
        var bytes = File.ReadAllBytes(path);
        var script = File.ReadAllText(path);

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf);
        Assert.Contains(@"\set ON_ERROR_STOP on", script);
        Assert.Contains("pg_advisory_xact_lock", script);
        Assert.Contains("CREATE TABLE \"InventoryDiagnosticAcknowledgments\"", script);
        Assert.Contains("GENERATED BY DEFAULT AS IDENTITY", script);
        Assert.Contains("ON DELETE RESTRICT", script);
        Assert.Equal(2, script.Split("ON DELETE SET NULL", StringSplitOptions.None).Length - 1);
        Assert.Contains("Forced compatibility-apply failure", script);
        Assert.Contains("COMMIT", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DROP TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TRUNCATE ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO \"__EFMigrationsHistory\"", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE \"__EFMigrationsHistory\"", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InventoryDiagnosticAcknowledgmentVerification_IsReadOnlyAndAcceptsExistingRows()
    {
        var path = FindRepositoryFile("scripts", "postgresql", "verify-inventory-diagnostic-acknowledgments.sql");
        var bytes = File.ReadAllBytes(path);
        var script = File.ReadAllText(path);

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf);
        Assert.Contains("BEGIN TRANSACTION READ ONLY", script, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("acknowledgment_row_count", script);
        Assert.Contains("exact_16_columns_5_indexes_pk_3_fks", script);
        Assert.DoesNotContain("= 0", script);
        Assert.DoesNotContain("ALTER TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CREATE TABLE", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\nINSERT INTO ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\nUPDATE ", script, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\nDELETE FROM ", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InventoryDiagnosticCompatibilityRunbook_RecordsExactMigrationGateAndProtectedScope()
    {
        var document = File.ReadAllText(FindRepositoryFile(
            "docs", "production-inventory-diagnostic-acknowledgments.md"));

        Assert.Contains("20260809151943_AddInventoryDiagnosticAcknowledgments", document);
        Assert.Contains("267", document);
        Assert.Contains("State A", document);
        Assert.Contains("State B", document);
        Assert.Contains("State C", document);
        Assert.Contains("run 57", document, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("f21daa865a5ff225b857033c196f7c933fc1ad64c3a8d5be88c62405c3c71974", document);
        Assert.Contains("__EFMigrationsHistory", document);
        Assert.Contains("inventory readiness", document, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InventoryDiagnosticPostgreSqlHarness_CoversParityFailuresIdempotencyAndRollback()
    {
        var path = FindRepositoryFile(
            "scripts", "test-inventory-diagnostic-acknowledgments-production-schema.ps1");
        var bytes = File.ReadAllBytes(path);
        var script = File.ReadAllText(path);

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf);
        Assert.Contains("postgres:18", script);
        Assert.Contains("20260807210820_AddRoleBasedUserAccess", script);
        Assert.Contains("20260809151943_AddInventoryDiagnosticAcknowledgments", script);
        Assert.Contains("wrong_column", script);
        Assert.Contains("missing_index", script);
        Assert.Contains("wrong_fk", script);
        Assert.Contains("test_force_inventory_diagnostic_ack_failure", script);
        Assert.Contains("Get-ObjectSignature", script);
        Assert.Contains("Migration history unchanged", script);
        Assert.Contains("267-object gate", script);
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
        Assert.Contains("expected_room_count <> 69", script);
        Assert.Contains("wp_candidate_count <> 42", script);
        Assert.Contains("ebs_candidate_count <> 27", script);
        Assert.Contains("'WP', 'mcdougall', 'MCD-01'", script);
        Assert.Contains("generate_series(4, 8)", script);
        Assert.Contains("included_approved_scope", script);
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
        Assert.Contains("'WP', 'mcdougall', 'MCD-01'", script);
        Assert.Contains("'WP', 'wp', 'WP-' || n FROM generate_series(4, 8)", script);
        Assert.Contains("wp_candidate_count <> 42", script);
        Assert.Contains("ebs_candidate_count <> 27", script);
        Assert.Contains("lower(btrim(r.\"Code\"))=lower(e.room_code)", script);
        Assert.DoesNotContain("lower(btrim(w.\"Code\")) IN ('dh','mcdougall','wp','ebs')\n  AND g.\"Name\"", script);
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
        Assert.Contains("wp_assignment_count <> 42", script);
        Assert.Contains("ebs_assignment_count <> 27", script);
        Assert.Contains("MCD-01 must be included", script);
        Assert.Contains("included_approved_scope", script);
        Assert.Contains("IN ('dh','mcdougall','wp') THEN 'WP'", script);
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
        Assert.Contains("'mcd-01','mcd-3'", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'wp-4','wp-5','wp-6','wp-7','wp-8'", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("'wp-9'", migration, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("'mcd-2'", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("'lamb-13','lamb-14','lamb-15','lamb-16','lamb-17'", migration);
        Assert.Contains("'evans-backside','evans-bkt','evans-hallway1','evans-hallway2'", migration);
        Assert.Contains("'bm-1','bm-2','bm-3','bm-4','bm-5','bm-6'", migration);
        Assert.DoesNotContain("lower(btrim(w.\"Code\")) IN ('dh', 'mcdougall')", migration);
        Assert.DoesNotContain("LOWER(LTRIM(RTRIM(w.[Code]))) IN ('dh', 'mcdougall')", migration);
    }

    [Fact]
    public void RoleCompatibilityPackage_FingerprintsAndPreservesDeployedEndOfDayFillState()
    {
        var preflight = File.ReadAllText(FindRepositoryFile(
            "scripts", "postgresql", "preflight-role-based-user-access.sql"));
        var apply = File.ReadAllText(FindRepositoryFile(
            "scripts", "postgresql", "apply-role-based-user-access-schema.sql"));
        var verify = File.ReadAllText(FindRepositoryFile(
            "scripts", "postgresql", "verify-role-based-user-access.sql"));

        foreach (var objectName in new[]
                 {
                     "EndOfDayFillReportGroups",
                     "EndOfDayFillReportRecipients",
                     "EndOfDayFillUserGroupAssignments",
                     "EndOfDayFillReportSends",
                     "EndOfDayFillSendReservations"
                 })
        {
            Assert.Contains(objectName, preflight);
            Assert.Contains(objectName, apply);
            Assert.Contains(objectName, verify);
        }

        Assert.Contains("EndOfDayFillReportGroupId", preflight);
        Assert.Contains("RoomEndOfDayFillAssignmentsAndCapacities", apply);
        Assert.Contains("_protected_state_after", apply);
        Assert.Contains("Role conversion changed legacy evidence or protected End-of-Day Fill state", apply);
        Assert.Contains("preserved_room_assignment_capacity_fingerprint", verify);
        Assert.Contains("Imported Access A", preflight);
        Assert.Contains("Imported Access E", apply);
        Assert.Contains("38d291003ef3287eaf098fc161c4496d", preflight);
        Assert.Contains("4c094069afe868d0f2be67fd41965528", apply);
        Assert.Contains("rob@earlbrownandsons.com", verify);
        Assert.Contains("crop-year-review", verify);
        Assert.Contains("data-cleanup", verify);
        Assert.DoesNotContain("UPDATE \"EndOfDayFill", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE FROM \"EndOfDayFill", apply, StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("\"RoomInventoryLosses\"", diagnostics);
        Assert.Contains("\"RoomInventoryAdjustments\", \"RoomInventoryLossId\"", diagnostics);
        Assert.Contains("IX_RoomInventoryLosses_OperationKey", diagnostics);
        Assert.Contains("FK_RoomInventoryAdjustments_RoomInventoryLosses_RoomInventoryLossId", diagnostics);
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
