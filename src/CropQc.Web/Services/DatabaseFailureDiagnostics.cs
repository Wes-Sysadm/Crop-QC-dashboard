using Microsoft.Data.SqlClient;
using Npgsql;

namespace CropQc.Web.Services;

public enum DatabaseFailureCategory
{
    ConnectionUnavailable,
    AuthenticationFailure,
    SchemaMismatch,
    QueryFailure
}

public sealed record DatabaseFailureDiagnostic(
    DatabaseFailureCategory Category,
    string? ProviderCode,
    string SafeMessage);

public static class DatabaseFailureDiagnostics
{
    public static DatabaseFailureDiagnostic Classify(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        var postgres = Find<PostgresException>(exception);
        if (postgres is not null)
        {
            if (postgres.SqlState.StartsWith("28", StringComparison.Ordinal))
            {
                return Authentication(postgres.SqlState);
            }

            if (postgres.SqlState.StartsWith("08", StringComparison.Ordinal)
                || postgres.SqlState is "3D000" or "57P01" or "57P02" or "57P03")
            {
                return Connection(postgres.SqlState);
            }

            if (postgres.SqlState is "42P01" or "42703" or "42704" or "42883")
            {
                return Schema(postgres.SqlState);
            }

            return Query(postgres.SqlState);
        }

        var sqlServer = Find<SqlException>(exception);
        if (sqlServer is not null)
        {
            if (sqlServer.Number == 18456)
            {
                return Authentication(sqlServer.Number.ToString());
            }

            if (sqlServer.Number is 207 or 208 or 2812)
            {
                return Schema(sqlServer.Number.ToString());
            }

            if (sqlServer.Number is -2 or 53 or 64 or 233 or 10053 or 10054 or 10060)
            {
                return Connection(sqlServer.Number.ToString());
            }

            return Query(sqlServer.Number.ToString());
        }

        if (Find<NpgsqlException>(exception) is not null
            || exception is TimeoutException
            || exception.InnerException is TimeoutException)
        {
            return Connection(null);
        }

        return Query(null);
    }

    private static DatabaseFailureDiagnostic Authentication(string? code) => new(
        DatabaseFailureCategory.AuthenticationFailure,
        code,
        "The database could not authenticate the application. An administrator has been notified.");

    private static DatabaseFailureDiagnostic Connection(string? code) => new(
        DatabaseFailureCategory.ConnectionUnavailable,
        code,
        "The database connection is temporarily unavailable. An administrator has been notified.");

    private static DatabaseFailureDiagnostic Schema(string? code) => new(
        DatabaseFailureCategory.SchemaMismatch,
        code,
        "The database schema is behind the application version. An administrator must complete the approved database update.");

    private static DatabaseFailureDiagnostic Query(string? code) => new(
        DatabaseFailureCategory.QueryFailure,
        code,
        "Dashboard data could not be loaded. An administrator has been notified.");

    private static TException? Find<TException>(Exception? exception)
        where TException : Exception
    {
        while (exception is not null)
        {
            if (exception is TException match)
            {
                return match;
            }

            exception = exception.InnerException;
        }

        return null;
    }
}
