using System.Reflection;
using CropQc.Data.Migrations;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class InventoryMovementMigrationTests
{
    [InventoryPostgresFact]
    public async Task Cohort_schema_roundtrip_preserves_old_data_and_down_refuses_after_use()
    {
        await using var f = await Fixture.Create();
        await using var db = f.CreateDbContext();
        var migration = db.GetService<IMigrationsAssembly>().CreateMigration(
            typeof(IsolatedInventoryMovementCohorts).GetTypeInfo(), db.Database.ProviderName!);
        var generator = db.GetService<IMigrationsSqlGenerator>();
        var down = string.Join('\n', generator.Generate(migration.DownOperations).Select(x => x.CommandText));
        var up = string.Join('\n', generator.Generate(migration.UpOperations).Select(x => x.CommandText));
        var original = await f.Snapshot();
        await Execute(down); await Execute(up);
        Assert.Equal(original, await f.Snapshot());
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(await f.Command(InventoryCommandKind.RoomMove, 7))).Status);
        var used = await f.Snapshot();
        var refusal = await Assert.ThrowsAsync<PostgresException>(() => Execute(down));
        Assert.Equal("P0001", refusal.SqlState);
        Assert.Contains("Cannot remove cohort evidence after use", refusal.MessageText);
        Assert.Equal(used, await f.Snapshot());

        async Task Execute(string sql)
        {
            await using var connection = new NpgsqlConnection(f.Connection);
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            await command.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        }
    }
}
