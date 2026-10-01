using CropQc.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

// Only clones an explicitly disposable, local, independently restored template.
// Backup freshness/checksums are a separate release gate, not asserted by this helper.
internal static class CanonicalRestoreFixture
{
    internal static async Task<Fixture> Clone()
    {
        var original = Environment.GetEnvironmentVariable("CANONICAL_INVENTORY_RESTORE_POSTGRES")!;
        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(original);
        var source = new NpgsqlConnectionStringBuilder(original);
        Assert.True(source.Host is "127.0.0.1" or "localhost");
        var name = $"phase3_restore_{Guid.NewGuid():N}_test";
        await using (var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(original) { Database = "postgres" }.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\" TEMPLATE \"{source.Database!.Replace("\"", "\"\"")}\"", admin);
            await create.ExecuteNonQueryAsync();
        }
        var fixture = new Fixture(new NpgsqlConnectionStringBuilder(original) { Database = name }.ConnectionString);
        try
        {
            var options = new DbContextOptionsBuilder<CropQcDbContext>();
            CropQcDatabase.Configure(options, DatabaseProviders.PostgreSql, fixture.Connection);
            await using var db = new CropQcDbContext(options.Options); // Same provider configuration as the host; flag OFF.
            var filters = new Dictionary<string, string> { ["__EFMigrationsHistory"] = "false" };
            var before = await fixture.Snapshot(filters);
            // Production's older provider history is intentionally released using
            // reviewed bounded scripts; replaying all old migrations is not safe.
            var applied = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
            const string phase2 = "20261001144023_CanonicalInventoryCommands";
            const string phase3 = "20261001203105_CanonicalIdentityRestorationSides";
            const string truck = "20260923202144_AddTruckReceiptReconciliation";
            Assert.Contains(truck, applied);
            if (!applied.Contains(phase3))
            {
                var script = db.GetService<IMigrator>().GenerateScript(applied.Contains(phase2) ? phase2 : truck, phase3);
                await db.Database.ExecuteSqlRawAsync(script);
            }
            using var oldTables = JsonDocument.Parse(before);
            using var newTables = JsonDocument.Parse(await fixture.Snapshot(filters));
            var oldRows = oldTables.RootElement.EnumerateArray().ToDictionary(x => x.GetProperty("table").GetString()!, x => x.GetProperty("rows").GetString());
            foreach (var table in newTables.RootElement.EnumerateArray())
            {
                var tableName = table.GetProperty("table").GetString()!;
                Assert.Equal(oldRows.GetValueOrDefault(tableName, ""), table.GetProperty("rows").GetString());
                oldRows.Remove(tableName);
            }
            Assert.Empty(oldRows);
            Assert.False(db.CanonicalInventoryEnabled);
            return fixture;
        }
        catch { await fixture.DisposeAsync(); throw; }
    }

    internal static async Task<Dictionary<string, string>> ExistingRows(Fixture fixture)
    {
        await using var db = fixture.CreateDbContext();
        var filters = new Dictionary<string, string>();
        await using var connection = new NpgsqlConnection(fixture.Connection);
        await connection.OpenAsync();
        foreach (var entity in db.Model.GetEntityTypes().Where(x => x.GetTableName() != null))
        {
            var table = entity.GetTableName()!;
            var store = StoreObjectIdentifier.Table(table, entity.GetSchema());
            var first = entity.FindPrimaryKey()?.Properties.FirstOrDefault();
            if (first == null) continue;
            var column = first.GetColumnName(store)!;
            var type = Nullable.GetUnderlyingType(first.ClrType) ?? first.ClrType;
            // Numeric monotonic keys cover operational tables and join-table ownership.
            if (type == typeof(int) || type == typeof(long))
            {
                await using var max = new NpgsqlCommand($"SELECT COALESCE(MAX(\"{column}\"),0)::bigint FROM \"{table}\"", connection);
                filters[table] = $"\"{column}\" <= {(long)(await max.ExecuteScalarAsync())!}";
            }
            else
            {
                var keys = new List<string>();
                await using var read = new NpgsqlCommand($"SELECT DISTINCT \"{column}\"::text FROM \"{table}\"", connection);
                await using var reader = await read.ExecuteReaderAsync();
                while (await reader.ReadAsync()) keys.Add("'" + reader.GetString(0).Replace("'", "''") + "'");
                filters[table] = keys.Count == 0 ? "false" : $"\"{column}\"::text IN ({string.Join(',', keys)})";
            }
        }
        return filters;
    }

    internal static async Task SeedIsolatedRooms(Fixture fixture, int positions = 0)
    {
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.Warehouses.AnyAsync(x => x.Id >= 9001));
        Assert.False(await db.Users.AnyAsync(x => x.Id == 8000));
        Assert.False(await db.GrowerLots.AnyAsync(x => x.Id >= 100000));
        await InventoryAvailabilityDatabaseTests.SeedAsync(db, positions);
        (await db.Warehouses.SingleAsync(x => x.Code == "WP")).Code = "BASE-WP";
        await db.SaveChangesAsync();
        (await db.Warehouses.SingleAsync(x => x.Id == 9001)).Code = "WP";
        db.Users.Add(new() { Id = 8000, Email = "canonical-test@example.invalid", DisplayName = "Disposable restore operator" });
        db.Rooms.Add(new() { Id = 9003, WarehouseId = 9001, Code = "DEST", Name = "Disposable destination" });
        await db.SaveChangesAsync();
    }
}
