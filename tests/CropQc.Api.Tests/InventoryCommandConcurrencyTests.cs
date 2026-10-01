using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class InventoryCommandConcurrencyTests
{
    [InventoryPostgresFact]
    public async Task Dump_versus_dump() => await Race(InventoryCommandKind.Dump, InventoryCommandKind.Dump);
    [InventoryPostgresFact]
    public async Task Transfer_versus_dump() => await Race(InventoryCommandKind.RoomMove, InventoryCommandKind.Dump);
    [InventoryPostgresFact]
    public async Task Treatment_versus_transfer() => await Race(InventoryCommandKind.TreatmentAssignment, InventoryCommandKind.RoomMove);
    [InventoryPostgresFact]
    public async Task Baseline_adjustment_versus_movement() => await Race(InventoryCommandKind.BaselineAdjustment, InventoryCommandKind.RoomMove);

    private static async Task Race(InventoryCommandKind left, InventoryCommandKind right)
    {
        await using var f = await Fixture.Create();
        var a = await f.Command(left, 19); var b = await f.Command(right, 19);
        if (left == InventoryCommandKind.TreatmentAssignment)
        {
            await using var db = f.CreateDbContext();
            a = a with { TreatmentChemicalId = await db.TreatmentChemicals.Where(x => x.IsActive && x.ApplicationLevel == "Room").Select(x => x.Id).FirstAsync() };
        }
        await AssertRace(f, a, b);
        Assert.InRange(await f.Physical(), 0, 19);
        Assert.InRange(await f.Physical(9003), 0, 19);
    }

    [InventoryPostgresFact]
    public async Task Receipt_correction_versus_transfer_allocation_edit()
    {
        await using var f = await Fixture.Create();
        var dispatch = (await f.Command(InventoryCommandKind.InterCompanyDispatch, 2)) with { CustodyGroup = "EBS" };
        var sent = await f.Execute(dispatch);
        Assert.True(sent.Status == InventoryCommandStatus.Committed, sent.Detail);
        var correction = await f.Command(InventoryCommandKind.ReceiptCorrection, 17);
        correction = correction with { Lines = [correction.Lines[0] with { ReceiptId = 100000 }] };
        var edit = (await f.Command(InventoryCommandKind.TransferEdit, 17)) with { OriginalOperationKey = dispatch.OperationKey, ExpectedTransferVersion = 1 };
        await AssertRace(f, correction, edit);
        Assert.Equal(0, await f.Physical());
    }

    [InventoryPostgresFact]
    public async Task Return_versus_reopen_of_completed_truck_receipt()
    {
        await using var f = await Fixture.Create();
        var receive = await f.ReceiveCommand();
        var received = await f.Execute(receive);
        Assert.True(received.Status == InventoryCommandStatus.Committed, received.Detail);
        var a = (await f.Command(InventoryCommandKind.Return, 19, 9007, 9006)) with
        { OriginalOperationKey = receive.OperationKey, ReceivingEvidence = receive.ReceivingEvidence! with { ExpectedVersion = receive.ReceivingEvidence.ExpectedVersion + 1 } };
        var b = a with { OperationKey = Guid.NewGuid().ToString("N"), Kind = InventoryCommandKind.ReopenTransfer };
        await AssertRace(f, a, b);
        await using var db = f.CreateDbContext();
        Assert.Null((await db.Receipts.SingleAsync(x => x.Id == receive.ReceivingEvidence!.ReceiptId)).TransferCompletedAt);
        Assert.Equal(1, await db.InventoryCommands.CountAsync(x => x.ReversesOperationKey == receive.OperationKey));
        Assert.Equal(0, (await new RoomInventoryLedgerQueryService(db).GetSnapshotsAsync(9006, [9007], default)).Sum(x => x.CurrentBins));
    }

    [InventoryPostgresFact]
    public async Task Concurrent_identical_intent_replays_one_durable_result()
    {
        await using var f = await Fixture.Create();
        var c = await f.Command(InventoryCommandKind.Dump, 19);
        var results = await Together(f, c, c);
        Assert.Single(results, x => x.Status == InventoryCommandStatus.Committed);
        Assert.Single(results, x => x.Status == InventoryCommandStatus.Replayed);
        await using var db = f.CreateDbContext();
        Assert.Equal(1, await db.InventoryCommands.CountAsync());
        Assert.Equal(19, await db.BinsRunEntries.SumAsync(x => x.BinsRun));
    }

    private static async Task AssertRace(Fixture f, InventoryCommand a, InventoryCommand b)
    {
        var results = await Together(f, a, b);
        Assert.True(results.Count(x => x.Status == InventoryCommandStatus.Committed) == 1, string.Join("; ", results.Select(x => $"{x.Status}: {x.Detail}")));
        Assert.True(results.Count(x => x.Status is InventoryCommandStatus.Stale or InventoryCommandStatus.Conflict or InventoryCommandStatus.RetryRequired) == 1,
            string.Join("; ", results.Select(x => $"{x.Status}: {x.Detail}")));
        Assert.All(results, x => Assert.InRange(x.Attempts, 1, 3));
    }
    private static async Task<InventoryCommandResult[]> Together(Fixture f, InventoryCommand a, InventoryCommand b)
    {
        var ready = 0; var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = new Observer(async (stage, attempt) =>
        {
            if (stage != "Resolved" || attempt != 1) return;
            if (Interlocked.Increment(ref ready) == 2) gate.SetResult();
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(30));
        });
        return await Task.WhenAll(f.Execute(a, observer), f.Execute(b, observer));
    }
}
