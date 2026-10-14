using System.Data.Common;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class InventoryCommitFailureTests
{
    [InventoryPostgresTheory]
    [InlineData("40001")]
    [InlineData("23514")]
    public async Task Server_aborted_commit_preserves_original_error_and_rolls_back_every_row(string sqlState)
    {
        await using var f = await Fixture.Create();
        var interceptor = new AbortedCommit(sqlState);
        var instrumented = new Fixture(f.Connection, interceptor);
        var c = Receipt();
        var before = await f.Snapshot();
        if (sqlState == "40001")
        {
            var retrySawOriginalState = false;
            var result = await instrumented.Execute(c, new Observer(async (stage, attempt) =>
            {
                if (stage != "Resolved" || attempt != 2) return;
                Assert.Equal(before, await f.Snapshot());
                retrySawOriginalState = true;
            }));
            Assert.True(retrySawOriginalState);
            Assert.Equal(InventoryCommandStatus.Committed, result.Status);
            Assert.Equal(2, result.Attempts);
            Assert.Equal(26, await f.Physical());
            await using var db = f.CreateDbContext();
            Assert.Single(await db.Receipts.Where(x => x.CompuTechReceiptId == "LOCAL-COMMIT").ToArrayAsync());
            Assert.Single(await db.InventoryCommands.ToArrayAsync());
        }
        else
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => instrumented.Execute(c));
            Assert.Equal(sqlState, error.SqlState);
            Assert.Equal(before, await f.Snapshot());
        }
    }

    [InventoryPostgresFact]
    public async Task Lost_commit_response_replays_durable_receipt_without_duplicating_inventory()
    {
        await using var f = await Fixture.Create();
        var instrumented = new Fixture(f.Connection, new LostCommitResponse());
        var c = Receipt();
        await Assert.ThrowsAsync<IOException>(() => instrumented.Execute(c));
        Assert.Equal(26, await f.Physical());
        var committed = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Replayed, (await f.Execute(c)).Status);
        Assert.Equal(committed, await f.Snapshot());
    }

    private static InventoryCommand Receipt() => new(Guid.NewGuid().ToString("N"), InventoryCommandKind.ReceiveStock,
        8000, DateTimeOffset.UtcNow, "Local commit failure regression", [],
        Receipt: new(2026, 9001, 9002, 100000, 9004, "LOCAL-COMMIT", 7));

    private sealed class AbortedCommit(string sqlState) : DbTransactionInterceptor
    {
        private int commits;
        public override async ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref commits) != 1) return result;
            // Reproduce the provider state after PostgreSQL rejects COMMIT: the
            // native transaction has ended before the original error is observed.
            await transaction.RollbackAsync(cancellationToken);
            throw new PostgresException("Local aborted commit", "ERROR", "ERROR", sqlState);
        }
    }

    private sealed class LostCommitResponse : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData,
            CancellationToken cancellationToken = default) => Task.FromException(new IOException("Local lost commit response"));
    }
}
