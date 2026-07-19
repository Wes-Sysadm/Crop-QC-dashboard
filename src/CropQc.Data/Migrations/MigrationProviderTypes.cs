using Microsoft.EntityFrameworkCore.Migrations;

namespace CropQc.Data.Migrations;

internal static class MigrationProviderTypes
{
    public static string StoreType(MigrationBuilder migrationBuilder, string sqlServerType, string postgreSqlType) =>
        (migrationBuilder.ActiveProvider ?? "").Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
            ? postgreSqlType
            : sqlServerType;

    public static string Sql(MigrationBuilder migrationBuilder, string sqlServerSql, string postgreSqlSql) =>
        (migrationBuilder.ActiveProvider ?? "").Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
            ? postgreSqlSql
            : sqlServerSql;
}
