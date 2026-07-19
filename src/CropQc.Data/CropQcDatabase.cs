using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace CropQc.Data;

public static class CropQcDatabase
{
    public const string DefaultConnectionStringName = "CropQc";
    public const string DefaultProvider = "SqlServer";
    public const string DefaultSqlServerConnectionString =
        "Server=(localdb)\\mssqllocaldb;Database=CropQcDashboard;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";

    public static void Configure(
        DbContextOptionsBuilder options,
        string? provider,
        string? connectionString,
        Action<SqlServerDbContextOptionsBuilder>? sqlServerOptions = null)
    {
        var resolvedProvider = NormalizeProvider(provider);

        switch (resolvedProvider)
        {
            case DatabaseProviders.SqlServer:
                var resolvedConnectionString = string.IsNullOrWhiteSpace(connectionString)
                    ? DefaultSqlServerConnectionString
                    : connectionString;
                options.UseSqlServer(resolvedConnectionString, sqlServerOptions ?? (_ => { }));
                break;
            case DatabaseProviders.PostgreSql:
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new InvalidOperationException("PostgreSql provider requires a configured connection string.");
                }

                options
                    .UseNpgsql(connectionString)
                    // The model snapshot is generated from the SQL Server provider, while
                    // production also supports PostgreSQL. EF Core 9 raises provider-specific
                    // snapshot differences as a pending-model warning before migrations run.
                    .ConfigureWarnings(warnings => warnings.Ignore(RelationalEventId.PendingModelChangesWarning));
                break;
            default:
                throw new InvalidOperationException($"Unsupported database provider '{provider}'. Supported providers: SqlServer, PostgreSql.");
        }
    }

    public static string NormalizeProvider(string? provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            return DatabaseProviders.SqlServer;
        }

        return provider.Trim().ToLowerInvariant() switch
        {
            "sqlserver" or "sql-server" or "mssql" or "azure-sql" or "azuresql" => DatabaseProviders.SqlServer,
            "postgresql" or "postgres" or "npgsql" => DatabaseProviders.PostgreSql,
            _ => provider.Trim()
        };
    }
}

public static class DatabaseProviders
{
    public const string SqlServer = "SqlServer";
    public const string PostgreSql = "PostgreSql";
}
