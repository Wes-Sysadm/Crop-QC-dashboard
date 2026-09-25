using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CropQc.Api.Tests;

public sealed class TruckReceiptPostgresTests
{
    internal static string? Connection => Environment.GetEnvironmentVariable("CROPQC_TRUCK_RECEIPT_TEST_CONNECTION");

    [TruckPostgresTheory]
    [InlineData("EBS", "WP")]
    [InlineData("EBS", "DH")]
    [InlineData("EBS", "McDougall")]
    [InlineData("WP", "EBS")]
    [InlineData("DH", "EBS")]
    [InlineData("McDougall", "EBS")]
    public async Task Exact_multi_variety_receiving_and_reopen_conserve_every_identity(string source, string destination)
    {
        await using var f = await TruckReceiptReconciliationTests.Fixture.CreateAsync(Connection, source, destination);
        var transfer = await f.DispatchAsync(70);
        await f.AddSecondVarietyAsync(transfer.Id, 30);
        var receipt = await f.CreateReceiptAsync(100);
        var edit = await f.FormAsync(receipt.Id, transfer.Id);
        edit.Lines = [new() { FruitProfileId = f.First.Id, BinCount = 70 }, new() { FruitProfileId = f.Second.Id, BinCount = 30 }];
        Assert.Null(await f.Service.EditReceiptAsync(edit, default));
        Assert.Null(await f.Service.MatchAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        Assert.Null(await f.Service.CompleteAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        Assert.Equal(100, (await f.Ledger.GetSnapshotsAsync(null, [f.Destination.Id], default)).Sum(x => x.CurrentBins));
        Assert.Equal(300, (await f.Ledger.GetSnapshotsAsync(null, [f.Source.Id], default)).Sum(x => x.CurrentBins));
        var form = await f.FormAsync(receipt.Id, transfer.Id); form.Reason = "Rehearsal reopen";
        Assert.Null(await f.Service.ReopenAsync(form, default));
        Assert.Equal(0, (await f.Ledger.GetSnapshotsAsync(null, [f.Destination.Id], default)).Sum(x => x.CurrentBins));
        Assert.Equal(100, (await f.Service.ActiveAllocationsAsync(transfer.Id, default)).Sum(x => x.Bins));
        Assert.Null(await f.Service.MatchAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        Assert.Null(await f.Service.CompleteAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        Assert.Equal(100, (await f.Ledger.GetSnapshotsAsync(null, [f.Destination.Id], default)).Sum(x => x.CurrentBins));
    }

    [TruckPostgresFact]
    public async Task Failure_after_destination_writes_rolls_back_all_inventory_history_status_and_audit()
    {
        await using var f = await TruckReceiptReconciliationTests.Fixture.CreateAsync(Connection);
        var transfer = await f.DispatchAsync(70);
        var receipt = await f.CreateReceiptAsync(70);
        Assert.Null(await f.Service.MatchAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        var before = await CountsAsync(f.Db);
        var faulty = f.ServiceWithInvariant(new FailAfterWrites());
        Assert.Contains("injected", await faulty.CompleteAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
        Assert.Equal(before, await CountsAsync(f.Db));
        Assert.Equal(0, await f.BalanceAsync(f.Destination.Id));
        Assert.Null(await f.Db.Receipts.Where(x => x.Id == receipt.Id).Select(x => x.TransferCompletedAt).SingleAsync());
        Assert.Equal(InterCrewTransferStatuses.InTransit, await f.Db.InterCrewTransfers.Where(x => x.Id == transfer.Id).Select(x => x.Status).SingleAsync());
        Assert.Null(await f.Service.CompleteAsync(await f.FormAsync(receipt.Id, transfer.Id), default));
    }

    [TruckPostgresFact]
    public async Task Independent_context_rejects_stale_row_version()
    {
        await using var f = await TruckReceiptReconciliationTests.Fixture.CreateAsync(Connection);
        var receipt = await f.CreateReceiptAsync(70);
        await using var second = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseNpgsql(f.Db.Database.GetDbConnection()).Options);
        await second.Database.UseTransactionAsync(f.Transaction!.GetDbTransaction());
        var stale = await second.Receipts.SingleAsync(x => x.Id == receipt.Id);
        var form = new CropQc.Web.Models.TruckReceiptActionForm
        {
            ReceiptId = receipt.Id,
            ReceiptVersion = receipt.ConcurrencyVersion,
            Lines = [new() { FruitProfileId = f.First.Id, BinCount = 71 }]
        };
        Assert.Null(await f.Service.EditReceiptAsync(form, default));
        stale.BinCount = 72; stale.ConcurrencyVersion++;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
        Assert.Equal(71, await f.Db.Receipts.AsNoTracking().Where(x => x.Id == receipt.Id).Select(x => x.BinCount).SingleAsync());
    }

    private static async Task<string> CountsAsync(CropQcDbContext db) => $"{await db.RoomInventoryAdjustments.CountAsync()}/{await db.TreatmentLineageSegments.CountAsync()}/{await db.TreatmentLineageMovements.CountAsync()}/{await db.AuditLogs.CountAsync()}";
    private sealed class FailAfterWrites : IInventoryDeductionInvariantService
    {
        public Task ValidateBeforeCommitAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("injected final validation failure");
        public Task<InventoryDeductionReadinessResult> VerifyReadinessAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

public sealed class TruckPostgresFactAttribute : FactAttribute
{
    public TruckPostgresFactAttribute() { if (string.IsNullOrWhiteSpace(TruckReceiptPostgresTests.Connection)) Skip = "Set CROPQC_TRUCK_RECEIPT_TEST_CONNECTION to a migrated disposable PostgreSQL restore."; }
}
public sealed class TruckPostgresTheoryAttribute : TheoryAttribute
{
    public TruckPostgresTheoryAttribute() { if (string.IsNullOrWhiteSpace(TruckReceiptPostgresTests.Connection)) Skip = "Set CROPQC_TRUCK_RECEIPT_TEST_CONNECTION to a migrated disposable PostgreSQL restore."; }
}
