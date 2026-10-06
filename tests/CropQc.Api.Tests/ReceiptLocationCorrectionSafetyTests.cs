using System.Data.Common;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed partial class ReceiptLocationCorrectionTests
{
    [InventoryPostgresFact]
    public async Task Ambiguous_receipt_pool_blocks_without_substituting_same_lot_inventory()
    {
        await using var f = await Hundred();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, executor);
        var form = await Form(db, service);
        // Seed an ambiguous legacy pool before the first canonical command.
        await using (var seed = f.CreateDbContext())
        {
            var original = await seed.Receipts.AsNoTracking().SingleAsync(x => x.Id == 100000);
            original.Id = 100001; original.CompuTechReceiptId = "AMBIGUOUS-SECOND"; original.BinCount = 7;
            seed.Receipts.Add(original);
            var ledger = await seed.RoomInventoryAdjustments.AsNoTracking().SingleAsync(x => x.Id == 100000);
            ledger.Id = 100001; ledger.ReceiptId = 100001; ledger.ChangeAmount = 7; ledger.NewBinCount = 107;
            seed.RoomInventoryAdjustments.Add(ledger);
            var row = await seed.TreatmentLineageSegments.SingleAsync(x => x.Id == 100000);
            row.ReceiptId = null; row.CurrentBins = 107;
            await seed.SaveChangesAsync();
        }
        var preview = (await service.GetPreviewAsync(100000, default))!;
        Assert.Contains("cannot be proven", preview.CanonicalBlocker);
        form.ExpectedInventoryStateToken = preview.InventoryStateToken;
        var before = await f.Snapshot();
        Assert.Contains("cannot be proven", (await service.ApplyEditAsync(form, Actor(), default)).Error);
        Assert.Equal(before, await f.Snapshot());
    }

    [InventoryPostgresFact]
    public async Task Inventory_change_during_transaction_rolls_back_correction_then_rejects_stale_review()
    {
        await using var f = await Hundred();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = new InventoryCommandExecutor(factory, new Observer(async (stage, attempt) =>
        {
            if (stage == "Movement" && attempt == 1) { reached.SetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(45)); }
        }));
        var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, executor);
        var form = await Form(db, service); form.CorrectionSourceRoomId = 9002;
        var correction = service.ApplyEditAsync(form, Actor(), default);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(45));
        string before;
        try
        {
            await using var second = factory.CreateDbContext();
            await Transfer(CanonicalReceivingWorkflowTests.Dashboard(second, new InventoryCommandExecutor(factory)), 9002, 9003, 10);
            before = await f.Snapshot();
        }
        finally { release.TrySetResult(); }
        Assert.True((await correction).IsConflict);
        Assert.Equal(before, await f.Snapshot());
        Assert.Equal(90, await f.Physical()); Assert.Equal(10, await f.Physical(9003)); Assert.Equal(0, await f.Physical(9008));
    }

    [InventoryPostgresFact]
    public async Task Completed_truck_link_and_receiving_receipt_remain_historical_when_original_receipt_stock_is_relocated()
    {
        await using var f = await Fixture.Create();
        var receiving = await f.ReceiveCommand();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        var transfer = await db.InterCrewTransfers.AsNoTracking().SingleAsync(x => x.Id == receiving.Lines[0].Source.Location.CustodyRecordId);
        var truck = CanonicalTruckReceiptWorkflowTests.Service(db, executor);
        Assert.Null(await truck.CompleteAsync(new()
        {
            TransferId = transfer.Id,
            TransferVersion = transfer.ConcurrencyVersion,
            ReceiptId = receiving.ReceivingEvidence!.ReceiptId,
            ReceiptVersion = receiving.ReceivingEvidence.ExpectedVersion
        }, default));
        async Task<string> TruckHistory() => JsonSerializer.Serialize(new
        {
            Parent = await db.InterCrewTransfers.AsNoTracking().SingleAsync(x => x.Id == transfer.Id),
            Receiving = await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == receiving.ReceivingEvidence.ReceiptId),
            Lines = await db.ReceiptVarietyLines.AsNoTracking().Where(x => x.ReceiptId == receiving.ReceivingEvidence.ReceiptId).ToArrayAsync()
        });
        var before = await TruckHistory();
        var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, executor);
        var form = await CanonicalReceiptCorrectionWorkflowTests.Form(db, service, 100000, 19);
        form.RoomId = 9003;
        Assert.Null((await service.ApplyEditAsync(form, Actor(), default)).Error);
        Assert.Equal(before, await TruckHistory());
        Assert.Equal(19, await f.Physical(9003)); Assert.Equal(0, await f.Physical());
        Assert.Equal(0, (await new CropQc.Data.Inventory.RoomInventoryLedgerQueryService(db).GetSnapshotsAsync(9006, [9007], default)).Sum(x => x.CurrentBins));
    }

    [InventoryPostgresFact]
    public async Task Large_receipt_keeps_query_count_constant_and_does_not_query_per_bin()
    {
        var counts = new List<int>();
        foreach (var quantity in new[] { 100, 100000 })
        {
            await using var f = await Hundred();
            await using (var seed = f.CreateDbContext())
            {
                (await seed.Receipts.SingleAsync(x => x.Id == 100000)).BinCount = quantity;
                var ledger = await seed.RoomInventoryAdjustments.SingleAsync(x => x.Id == 100000);
                ledger.ChangeAmount = quantity; ledger.NewBinCount = quantity;
                (await seed.TreatmentLineageSegments.SingleAsync(x => x.Id == 100000)).CurrentBins = quantity;
                await seed.SaveChangesAsync();
            }
            var counter = new LocationQueryCounter();
            var factory = new CountedFactory(f.Connection, counter);
            await using var db = factory.CreateDbContext();
            var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, new InventoryCommandExecutor(factory));
            var form = await CanonicalReceiptCorrectionWorkflowTests.Form(db, service, 100000, quantity);
            form.RoomId = 9008; counter.Reads = 0;
            Assert.Null((await service.ApplyEditAsync(form, Actor(), default)).Error);
            counts.Add(counter.Reads);
            Assert.Equal(quantity, await f.Physical(9008));
        }
        Assert.Equal(counts[0], counts[1]);
        Assert.InRange(counts[1], 1, 180);
    }

    [InventoryPostgresFact]
    public async Task Post_commit_readback_failure_does_not_claim_rollback_and_retry_remains_idempotent()
    {
        await using var f = await Hundred();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, new MissingReadback(executor));
        var form = await Form(db, service);
        var result = await service.ApplyEditAsync(form, Actor(), default);
        Assert.Contains("could not be confirmed", result.Error);
        Assert.DoesNotContain("No changes were saved", result.Error);
        Assert.Equal(100, await f.Physical(9008)); Assert.Equal(0, await f.Physical());
        var before = await f.Snapshot();
        var retry = CanonicalReceiptCorrectionWorkflowTests.Service(db, executor);
        Assert.True((await retry.ApplyEditAsync(form, Actor(), default)).WasIdempotent);
        Assert.Equal(before, await f.Snapshot());
    }

    // Simulate an unavailable post-commit read-back without injecting a failed write.
    private sealed class MissingReadback(IInventoryCommandExecutor inner) : IInventoryCommandExecutor
    {
        public async Task<InventoryCommandResult> ExecuteAsync(InventoryCommand command, CancellationToken cancellationToken = default) =>
            (await inner.ExecuteAsync(command, cancellationToken)) with { OperationKey = "missing-readback" };
    }

    private sealed class LocationQueryCounter : DbCommandInterceptor
    {
        public int Reads { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { Reads++; return ValueTask.FromResult(result); }
    }
    private sealed class CountedFactory(string connection, LocationQueryCounter counter) : IDbContextFactory<CropQcDbContext>
    {
        public CropQcDbContext CreateDbContext() => new(new DbContextOptionsBuilder<CropQcDbContext>().UseNpgsql(connection).AddInterceptors(counter).Options, new(true));
    }
}
