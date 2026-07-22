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
