using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class ReceiptCustodyCorrectionTests
{
    [InventoryPostgresTheory]
    [InlineData(InventoryCommandKind.RoomMove)]
    [InlineData(InventoryCommandKind.Loss)]
    [InlineData(InventoryCommandKind.TreatmentAssignment)]
    public async Task Downstream_movement_consumption_or_treatment_blocks_unsafe_reversal_but_preserves_held_corrections(InventoryCommandKind dependency)
    {
        await using var f = await Fixture.Create();
        var receiving = await f.ReceiveCommand();
        await Commit(f, receiving, InventoryCommandKind.AcknowledgeTransfer, 19);
        await Commit(f, receiving, InventoryCommandKind.PlaceReceiptCustody, 18);
        await using var db = f.CreateDbContext();
        db.Rooms.Add(new() { Id = 9010, WarehouseId = 9006, Code = "DOWNSTREAM", Name = "Later destination" });
        await db.SaveChangesAsync();
        var resolver = new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db));
        var position = Assert.Single((await resolver.ResolveAsync(new(9006, [9007]), new(), DateTimeOffset.UtcNow)).Positions);
        var chemical = await db.TreatmentChemicals.FirstAsync(x => x.IsActive && x.ApplicationLevel == "Room");
        var operation = new InventoryCommand(Guid.NewGuid().ToString("N"), dependency, 8000, DateTimeOffset.UtcNow,
            "Real downstream dependency in disposable PostgreSQL",
            [new(new(position.Identity, position.Location, position.Watermark.Fingerprint, position.Watermark.Versions),
                dependency == InventoryCommandKind.TreatmentAssignment ? 18 : 1, "u",
                dependency == InventoryCommandKind.RoomMove ? new(9006, 9010) : null)], TreatmentChemicalId: chemical.Id);
        var downstream = await f.Execute(operation);
        Assert.True(downstream.Status == InventoryCommandStatus.Committed, downstream.Detail);
        var undo = await Build(f, receiving, InventoryCommandKind.ReverseReceiptPlacement, 1);
        var before = await f.Snapshot();
        var blocked = await f.Execute(undo);
        Assert.Equal(InventoryCommandStatus.Blocked, blocked.Status);
        Assert.Contains("dependencies", blocked.Detail);
        Assert.Equal(before, await f.Snapshot());
        await Commit(f, receiving, InventoryCommandKind.ReverseReceiptAcknowledgment, 1);
        await Prove(f, receiving, 18, 18);
        Assert.Equal(dependency == InventoryCommandKind.Loss ? 17 : 18,
            await db.RoomInventoryAdjustments.Where(x => x.WarehouseId == 9006).SumAsync(x => x.ChangeAmount));
    }

    [InventoryPostgresFact]
    public async Task Over_acknowledgement_compensates_held_only_and_can_be_acknowledged_again()
    {
        await using var f = await Fixture.Create();
        var receiving = await f.ReceiveCommand();
        await Commit(f, receiving, InventoryCommandKind.AcknowledgeTransfer, 19);
        await Prove(f, receiving, 19, 0);
        var reverse = await Build(f, receiving, InventoryCommandKind.ReverseReceiptAcknowledgment, 1);
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(reverse)).Status);
        await Prove(f, receiving, 18, 0);
        var snapshot = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Replayed, (await f.Execute(reverse)).Status);
        Assert.Equal(snapshot, await f.Snapshot());
        await Commit(f, receiving, InventoryCommandKind.AcknowledgeTransfer, 1);
        await Prove(f, receiving, 19, 0);
        // Correct the new acknowledgement without altering the first compensation.
        await Commit(f, receiving, InventoryCommandKind.ReverseReceiptAcknowledgment, 1, latest: true);
        await Prove(f, receiving, 18, 0);
        await using var db = f.CreateDbContext();
        Assert.Equal(new[] { 19, 1 }, await db.ReceiptCustodyAcknowledgments.OrderBy(x => x.Id).Select(x => x.Quantity).ToArrayAsync());
        Assert.Equal(2, await db.ReceiptCustodyReversals.CountAsync());
    }

    [InventoryPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Partial_or_complete_placement_can_be_compensated_then_placed_in_the_correct_room(bool complete)
    {
        await using var f = await Fixture.Create();
        var receiving = await f.ReceiveCommand(treated: true);
        await Commit(f, receiving, InventoryCommandKind.AcknowledgeTransfer, 19);
        await Commit(f, receiving, InventoryCommandKind.PlaceReceiptCustody, complete ? 19 : 18);
        await Prove(f, receiving, 19, complete ? 19 : 18);
        await Commit(f, receiving, InventoryCommandKind.ReverseReceiptPlacement, 1);
        await Prove(f, receiving, 19, complete ? 18 : 17);
        await Commit(f, receiving, InventoryCommandKind.ReverseReceiptAcknowledgment, 1);
        await Prove(f, receiving, 18, complete ? 18 : 17);
        await Commit(f, receiving, InventoryCommandKind.AcknowledgeTransfer, 1);
        await using (var db = f.CreateDbContext())
        {
            db.Rooms.Add(new() { Id = 9010, WarehouseId = 9006, Code = "CORRECT", Name = "Correct room" });
            await db.SaveChangesAsync();
        }
        var place = await Build(f, receiving, InventoryCommandKind.PlaceReceiptCustody, 1, latest: true);
        place = place with { ReceiptCustody = place.ReceiptCustody! with { Destination = new(9006, 9010) } };
        var result = await f.Execute(place);
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
        await Prove(f, receiving, 19, complete ? 19 : 18);
        await using var verify = f.CreateDbContext();
        var target = await verify.TreatmentLineageSegments.Include(x => x.Applications).SingleAsync(x => x.RoomId == 9010);
        Assert.Equal("Confirmed", target.TreatmentState);
        Assert.Single(target.Applications);
        Assert.Equal(19, (await verify.RoomTreatmentApplications.SingleAsync()).TotalBinsSnapshot);
        Assert.Equal(1, target.CurrentBins);
    }

    [InventoryPostgresTheory]
    [InlineData(InventoryCommandKind.ReverseReceiptAcknowledgment, 0)]
    [InlineData(InventoryCommandKind.ReverseReceiptAcknowledgment, -1)]
    [InlineData(InventoryCommandKind.ReverseReceiptAcknowledgment, 20)]
    [InlineData(InventoryCommandKind.ReverseReceiptPlacement, 0)]
    [InlineData(InventoryCommandKind.ReverseReceiptPlacement, -1)]
    [InlineData(InventoryCommandKind.ReverseReceiptPlacement, 20)]
    public async Task Invalid_compensation_is_atomic(InventoryCommandKind kind, int quantity)
    {
        await using var f = await Fixture.Create();
        var receiving = await f.ReceiveCommand();
        await Commit(f, receiving, InventoryCommandKind.AcknowledgeTransfer, 19);
        await Commit(f, receiving, InventoryCommandKind.PlaceReceiptCustody, 18);
        var command = await Build(f, receiving, kind, quantity);
        var snapshot = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Blocked, (await f.Execute(command)).Status);
        Assert.Equal(snapshot, await f.Snapshot());
        await Prove(f, receiving, 19, 18);
    }

    [InventoryPostgresTheory]
    [InlineData(InventoryCommandKind.ReverseReceiptAcknowledgment)]
    [InlineData(InventoryCommandKind.ReverseReceiptPlacement)]
    public async Task Concurrent_compensations_commit_only_once(InventoryCommandKind kind)
    {
        await using var f = await Fixture.Create();
        var receiving = await f.ReceiveCommand();
        await Commit(f, receiving, InventoryCommandKind.AcknowledgeTransfer, 19);
        if (kind == InventoryCommandKind.ReverseReceiptPlacement) await Commit(f, receiving, InventoryCommandKind.PlaceReceiptCustody, 18);
        var command = await Build(f, receiving, kind, 1);
        var arrived = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = new Observer(async (stage, attempt) =>
        {
            if (stage != "Resolved" || attempt != 1) return;
            if (Interlocked.Increment(ref arrived) == 2) gate.SetResult();
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(30));
        });
        var results = await Task.WhenAll(f.Execute(command, observer), f.Execute(command with { OperationKey = Guid.NewGuid().ToString("N") }, observer));
        Assert.Single(results, x => x.Status == InventoryCommandStatus.Committed);
        Assert.Single(results, x => x.Status == InventoryCommandStatus.Stale);
        await Prove(f, receiving, kind == InventoryCommandKind.ReverseReceiptPlacement ? 19 : 18,
            kind == InventoryCommandKind.ReverseReceiptPlacement ? 17 : 0);
    }

    [InventoryPostgresTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Interrupted_compensation_preserves_atomicity_and_unknown_commit_replays(bool placement, bool afterCommit)
    {
        await using var f = await Fixture.Create();
        var receiving = await f.ReceiveCommand();
        await Commit(f, receiving, InventoryCommandKind.AcknowledgeTransfer, 19);
        if (placement) await Commit(f, receiving, InventoryCommandKind.PlaceReceiptCustody, 18);
        var command = await Build(f, receiving, placement ? InventoryCommandKind.ReverseReceiptPlacement : InventoryCommandKind.ReverseReceiptAcknowledgment, 1);
        var before = await f.Snapshot();
        var observer = new Observer((stage, _) => stage == (afterCommit ? "Committed" : "BeforeCommit")
            ? throw new IOException("Disposable interrupted compensation") : Task.CompletedTask);
        if (afterCommit)
            await Assert.ThrowsAsync<IOException>(() => new Fixture(f.Connection, new LostResponse()).Execute(command));
        else
            await Assert.ThrowsAsync<IOException>(() => f.Execute(command, observer));
        if (!afterCommit) Assert.Equal(before, await f.Snapshot());
        var retry = await f.Execute(command);
        Assert.Equal(afterCommit ? InventoryCommandStatus.Replayed : InventoryCommandStatus.Committed, retry.Status);
        await Prove(f, receiving, placement ? 19 : 18, placement ? 17 : 0);
    }

    [InventoryPostgresFact]
    public async Task Wrong_company_destination_and_reversal_of_placed_acknowledgement_are_blocked()
    {
        await using var f = await Fixture.Create();
        var receiving = await f.ReceiveCommand(); // WP to EBS route.
        await Commit(f, receiving, InventoryCommandKind.AcknowledgeTransfer, 19);
        var wrong = await Build(f, receiving, InventoryCommandKind.PlaceReceiptCustody, 18);
        wrong = wrong with { ReceiptCustody = wrong.ReceiptCustody! with { Destination = new(9001, 9003) } };
        var before = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Blocked, (await f.Execute(wrong)).Status);
        Assert.Equal(before, await f.Snapshot());
        await Commit(f, receiving, InventoryCommandKind.PlaceReceiptCustody, 18);
        var undo = await Build(f, receiving, InventoryCommandKind.ReverseReceiptAcknowledgment, 2);
        before = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Blocked, (await f.Execute(undo)).Status);
        Assert.Equal(before, await f.Snapshot());
        // The unplaced bin can still be corrected without changing any of the 18 placed bins.
        await Commit(f, receiving, InventoryCommandKind.ReverseReceiptAcknowledgment, 1);
        await Prove(f, receiving, 18, 18);
    }

    private sealed class LostResponse : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) =>
            Task.FromException(new IOException("Disposable lost correction response"));
    }

    internal static async Task<InventoryCommand> Build(Fixture f, InventoryCommand receiving, InventoryCommandKind kind, int quantity, bool latest = false)
    {
        await using var db = f.CreateDbContext();
        var transfer = await db.InterCrewTransfers.SingleAsync(x => x.Id == receiving.Lines[0].Source.Location.CustodyRecordId);
        var receipt = await db.Receipts.SingleAsync(x => x.Id == receiving.ReceivingEvidence!.ReceiptId);
        long id;
        if (kind == InventoryCommandKind.AcknowledgeTransfer)
            id = await db.TreatmentLineageMovements.Where(x => x.InterCrewTransferId == transfer.Id && x.MovementType == "InterCrewDispatch").Select(x => x.Id).SingleAsync();
        else if (kind == InventoryCommandKind.ReverseReceiptPlacement)
            id = await db.ReceiptCustodyPlacements.OrderBy(x => x.Id).Select(x => x.Id).FirstAsync();
        else
        {
            var ids = await db.ReceiptCustodyAcknowledgments.OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
            id = latest ? ids[^1] : ids[0];
        }
        return new(Guid.NewGuid().ToString("N"), kind, 8000, DateTimeOffset.UtcNow, "Receiver correcting a recorded allocation mistake", [],
            ReceiptCustody: new(transfer.Id, receipt.Id, transfer.ConcurrencyVersion, receipt.ConcurrencyVersion, [new(id, quantity)],
                kind == InventoryCommandKind.PlaceReceiptCustody ? new(9006, 9007) : null));
    }

    internal static async Task Commit(Fixture f, InventoryCommand receiving, InventoryCommandKind kind, int quantity, bool latest = false)
    {
        var result = await f.Execute(await Build(f, receiving, kind, quantity, latest));
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
    }

    internal static async Task Prove(Fixture f, InventoryCommand receiving, int acknowledged, int placed)
    {
        var before = await f.Snapshot();
        await using var db = f.CreateDbContext();
        var transfer = await db.InterCrewTransfers.SingleAsync(x => x.Id == receiving.Lines[0].Source.Location.CustodyRecordId);
        var acks = await db.ReceiptCustodyAcknowledgments.WithCustodyEvidence().AsNoTracking().ToArrayAsync();
        Assert.All(acks, x => Assert.True(ReceiptCustodyProof.Valid(x)));
        Assert.Equal(acknowledged, acks.Sum(x => x.NetQuantity));
        Assert.Equal(placed, acks.Sum(x => x.PlacedQuantity));
        Assert.Equal(acknowledged, transfer.BinsReceived);
        Assert.Equal(19, transfer.BinsLoaded);
        Assert.Equal(-19, await db.RoomInventoryAdjustments.Where(x => x.InterCrewTransferId == transfer.Id && x.RoomId == 9002).SumAsync(x => x.ChangeAmount));
        Assert.Equal(placed, await db.RoomInventoryAdjustments.Where(x => x.InterCrewTransferId == transfer.Id && x.RoomId != 9002).SumAsync(x => x.ChangeAmount));
        var resolver = new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db));
        var unresolved = await resolver.ResolveAsync(new(9001, [], InventoryCustody.InTransit, transfer.Id), new(AllowedCustody: InventoryCustody.InTransit), DateTimeOffset.UtcNow);
        var held = await resolver.ResolveAsync(new(9006, [], InventoryCustody.ReceiptHeld, receiving.ReceivingEvidence!.ReceiptId), new(AllowedCustody: InventoryCustody.ReceiptHeld), DateTimeOffset.UtcNow);
        Assert.Equal(19 - acknowledged, unresolved.Positions.Sum(x => x.AuthoritativeQuantity));
        Assert.Equal(acknowledged - placed, held.Positions.Sum(x => x.AuthoritativeQuantity));
        Assert.All(unresolved.Positions.Where(x => x.AuthoritativeQuantity > 0).Concat(held.Positions), x => Assert.True(x.IsOperable, string.Join(';', x.Blockers)));
        Assert.Equal(19, placed + held.Positions.Sum(x => x.AuthoritativeQuantity) + unresolved.Positions.Sum(x => x.AuthoritativeQuantity));
        var readiness = await new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance).VerifyReadinessAsync(default);
        Assert.True(!readiness.Issues.Any(x => x.BlocksDeployment), string.Join(';', readiness.Issues.Select(x => x.ToString())));
        Assert.Equal(await db.InventoryCommands.CountAsync(), await db.AuditLogs.CountAsync(x => x.Action == "CanonicalInventoryCommand"));
        Assert.Equal(before, await f.Snapshot());
    }
}
