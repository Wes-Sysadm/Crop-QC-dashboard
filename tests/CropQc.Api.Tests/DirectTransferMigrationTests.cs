using CropQc.Data.Migrations;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class DirectTransferMigrationTests
{
    internal static async Task InstallWriterFence(Fixture f)
    {
        Assert.Equal(42, (int)InventoryCommandKind.ConvertLegacyTransfer);
        await using var db = f.CreateDbContext();
        var migration = new DirectLocationTransfers { ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL" };
        foreach (var operation in migration.UpOperations.OfType<SqlOperation>())
            await db.Database.ExecuteSqlRawAsync(operation.Sql);
    }

    internal static async Task AssertOldWriterRejected(Fixture f)
    {
        await using var connection = new NpgsqlConnection(f.Connection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("UPDATE \"RoomInventoryAdjustments\" SET \"ChangeAmount\"=\"ChangeAmount\" WHERE \"Id\"=100000", connection);
        var refusal = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Contains("assignment-aware", refusal.MessageText);
    }

    [InventoryPostgresFact]
    public async Task Additive_schema_preserves_legacy_stock_and_used_history_refuses_destructive_or_incompatible_rollback()
    {
        await using var f = await Fixture.Create();
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(await f.Command(InventoryCommandKind.RoomMove, 7))).Status);
        await using var db = f.CreateDbContext();
        var migration = new DirectLocationTransfers { ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL" };
        var generator = db.GetService<IMigrationsSqlGenerator>();
        async Task Apply(IReadOnlyList<MigrationOperation> operations)
        {
            foreach (var sql in generator.Generate(operations, db.Model)) await db.Database.ExecuteSqlRawAsync(sql.CommandText);
        }
        // EnsureCreated supplies the final model but no migration triggers.
        // Establish them, then exercise a real unused-schema down/up transition.
        await Apply(migration.UpOperations.OfType<SqlOperation>().Cast<MigrationOperation>().ToArray());
        var current = await f.Snapshot();
        await Apply(migration.DownOperations);
        await Apply(migration.UpOperations);
        Assert.Equal(current, await f.Snapshot());
        Assert.Equal(12, await f.Physical());
        Assert.Equal(7, await f.Physical(9003));
        Assert.False((await db.RoomTransfers.SingleAsync()).IsDirectTransfer);
        var sent = await f.Execute(await f.Command(InventoryCommandKind.DirectTransfer, 5));
        Assert.True(sent.Status == InventoryCommandStatus.Committed, sent.Detail);
        var committed = await f.Snapshot();
        await Assert.ThrowsAsync<PostgresException>(() => Apply(migration.DownOperations));
        Assert.Equal(committed, await f.Snapshot());
        await using var oldWriter = new NpgsqlConnection(f.Connection);
        await oldWriter.OpenAsync();
        await using var invalid = new NpgsqlCommand("UPDATE \"RoomInventoryAdjustments\" SET \"ChangeAmount\"=\"ChangeAmount\" WHERE \"Id\"=100000", oldWriter);
        var refusal = await Assert.ThrowsAsync<PostgresException>(() => invalid.ExecuteNonQueryAsync());
        Assert.Contains("assignment-aware", refusal.MessageText);
        Assert.Equal(committed, await f.Snapshot());
        // Compatible canonical writes continue; this is not startup-only proof.
        var onward = await f.Command(InventoryCommandKind.DirectTransfer, 3, room: 9003);
        onward = onward with { Lines = [onward.Lines[0] with { Destination = new(9001, 9002) }] };
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(onward)).Status);
        Assert.Equal(19, await f.Physical() + await f.Physical(9003));
        Assert.Equal(3, await db.RoomTransfers.CountAsync());
    }
}
