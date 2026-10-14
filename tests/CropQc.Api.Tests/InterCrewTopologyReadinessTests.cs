using System.Collections.Immutable;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class InterCrewTopologyReadinessTests
{
    [InventoryPostgresFact]
    public async Task Recorded_return_before_acknowledgement_preserves_the_reduced_transit_allocation()
    {
        await using var f = await Fixture.Create();
        await f.ReceiveCommand(30, treated: true);
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var transfer = await db.InterCrewTransfers.AsNoTracking().SingleAsync();
        var dispatch = await db.TreatmentLineageMovements.AsNoTracking().SingleAsync(x => x.InterCrewTransferId == transfer.Id);
        var service = CanonicalTruckReceiptWorkflowTests.Service(db, new InventoryCommandExecutor(factory));
        Assert.Null(await service.EditTransferAsync(new CropQc.Web.Models.TransitEditForm
        {
            TransferId = transfer.Id,
            TransferVersion = transfer.ConcurrencyVersion,
            DispatchMovementId = dispatch.Id,
            Bins = 5,
            Reason = "Recorded return before any receiving acknowledgement"
        }, default));
        Assert.Empty((await InterCrewTopologyReadiness.VerifyAsync(db, default)).Issues);
        // Establish the observed transfer receipt metadata before custody is used.
        // This is not a physical origin and does not revise the original receipt.
        db.ChangeTracker.Clear();
        var receipt = await db.Receipts.Include(x => x.VarietyLines).SingleAsync(x => x.Id == transfer.ReceivingReceiptId);
        receipt.BinCount = 25; receipt.VarietyLines.Single().BinCount = 25; receipt.ConcurrencyVersion++;
        await db.SaveChangesAsync();
        transfer = await db.InterCrewTransfers.AsNoTracking().SingleAsync();
        var result = await f.Execute(new("ack-after-return", InventoryCommandKind.AcknowledgeTransfer, 8000,
            DateTimeOffset.UtcNow, "Acknowledge original remaining allocation", [], ReceiptCustody:
            new(transfer.Id, receipt.Id, transfer.ConcurrencyVersion, receipt.ConcurrencyVersion, [new(dispatch.Id, 20)])));
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
        var before = await f.Snapshot();
        var topology = await InterCrewTopologyReadiness.VerifyAsync(db, default);
        Assert.True(topology.Issues.Count == 0, JsonSerializer.Serialize(topology));
        Assert.Equal(new(transfer.Id, 25, 20, 0, 20, 5), Assert.Single(topology.Custody));
        Assert.Equal(5, await f.Physical());
        Assert.Equal(before, await f.Snapshot());
    }

    [InventoryPostgresFact]
    public async Task Readiness_observes_one_snapshot_while_a_separate_writer_acknowledges()
    {
        await using var f = await Fixture.Create();
        await f.ReceiveCommand(30);
        await using var db = f.CreateDbContext();
        var parent = await db.InterCrewTransfers.AsNoTracking().SingleAsync();
        var receipt = await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == parent.ReceivingReceiptId);
        var dispatch = await db.TreatmentLineageMovements.AsNoTracking().SingleAsync(x => x.InterCrewTransferId == parent.Id);
        var barrier = new SnapshotBarrier();
        await using var reader = new Fixture(f.Connection, barrier).CreateDbContext();
        var reading = InterCrewTopologyReadiness.VerifyAsync(reader, default);
        await barrier.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            var result = await f.Execute(new("concurrent-topology-ack", InventoryCommandKind.AcknowledgeTransfer, 8000,
                DateTimeOffset.UtcNow, "Independent writer", [], ReceiptCustody:
                new(parent.Id, receipt.Id, parent.ConcurrencyVersion, receipt.ConcurrencyVersion, [new(dispatch.Id, 10)])));
            Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
        }
        finally { barrier.Continue.SetResult(); }
        var prior = await reading;
        Assert.Empty(prior.Issues);
        Assert.Equal(0, Assert.Single(prior.Custody).Acknowledged);
        var current = await InterCrewTopologyReadiness.VerifyAsync(db, default);
        Assert.Empty(current.Issues);
        Assert.Equal(10, Assert.Single(current.Custody).Acknowledged);
    }

    private sealed class SnapshotBarrier : Microsoft.EntityFrameworkCore.Diagnostics.DbCommandInterceptor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<System.Data.Common.DbDataReader> ReaderExecutedAsync(System.Data.Common.DbCommand command,
            Microsoft.EntityFrameworkCore.Diagnostics.CommandExecutedEventData eventData, System.Data.Common.DbDataReader result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM \"InterCrewTransfers\"", StringComparison.Ordinal) && Started.TrySetResult())
                await Continue.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return result;
        }
    }

    [InventoryPostgresTheory]
    [InlineData("over-ack")]
    [InlineData("over-place")]
    [InlineData("wrong-dispatch")]
    [InlineData("missing-parent")]
    [InlineData("wrong-receipt")]
    [InlineData("duplicate-ack")]
    [InlineData("over-reversal")]
    [InlineData("wrong-reversal")]
    [InlineData("false-completion")]
    [InlineData("wrong-room")]
    [InlineData("missing-treatment")]
    [InlineData("wrong-treatment")]
    [InlineData("placement-treatment")]
    [InlineData("missing-origin")]
    [InlineData("chronology")]
    [InlineData("parent-quantity")]
    public async Task Proven_partial_custody_passes_but_corrupt_recorded_relationships_block(string defect)
    {
        await using var f = await Fixture.Create();
        await f.ReceiveCommand(30, treated: true);
        await using var db = f.CreateDbContext();
        var parent = await db.InterCrewTransfers.AsNoTracking().SingleAsync();
        var dispatch = await db.TreatmentLineageMovements.AsNoTracking().SingleAsync(x => x.InterCrewTransferId == parent.Id);
        async Task<InventoryCommand> Command(InventoryCommandKind kind, long id, int quantity, string key)
        {
            var t = await db.InterCrewTransfers.AsNoTracking().SingleAsync();
            var r = await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == t.ReceivingReceiptId);
            return new(key, kind, 8000, DateTimeOffset.UtcNow, "Topology fixture", [], ReceiptCustody:
                new(t.Id, r.Id, t.ConcurrencyVersion, r.ConcurrencyVersion, [new(id, quantity)],
                    kind == InventoryCommandKind.PlaceReceiptCustody ? new(9006, 9007) : null));
        }
        async Task Commit(InventoryCommand command)
        {
            var result = await f.Execute(command);
            Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
            db.ChangeTracker.Clear();
        }
        async Task Check(int acknowledged, int placed)
        {
            var before = await f.Snapshot();
            var result = await InterCrewTopologyReadiness.VerifyAsync(db, default);
            Assert.True(result.Issues.Count == 0, JsonSerializer.Serialize(result));
            Assert.Equal(new(parent.Id, 30, acknowledged, placed, acknowledged - placed, 30 - acknowledged), Assert.Single(result.Custody));
            Assert.Equal(before, await f.Snapshot());
        }
        await Check(0, 0);
        var stale = await Command(InventoryCommandKind.AcknowledgeTransfer, dispatch.Id, 1, "stale-topology");
        await Commit(await Command(InventoryCommandKind.AcknowledgeTransfer, dispatch.Id, 20, "topology-ack"));
        Assert.Equal(InventoryCommandStatus.Stale, (await f.Execute(stale)).Status);
        await Check(20, 0);
        var ack = await db.ReceiptCustodyAcknowledgments.AsNoTracking().SingleAsync();
        await Commit(await Command(InventoryCommandKind.PlaceReceiptCustody, ack.Id, 14, "topology-place"));
        await Check(20, 14);
        var placement = await db.ReceiptCustodyPlacements.AsNoTracking().SingleAsync();
        await Commit(await Command(InventoryCommandKind.ReverseReceiptPlacement, placement.Id, 2, "topology-unplace"));
        await Check(20, 12);
        await Commit(await Command(InventoryCommandKind.PlaceReceiptCustody, ack.Id, 2, "topology-replace"));
        await Check(20, 14);
        // Corrupt only this clearly disposable EnsureCreated fixture. No production
        // schema trigger is disabled; the purpose is to exercise readiness against
        // evidence a protected production writer must never commit.
        var sql = defect switch
        {
            "over-ack" => "UPDATE \"ReceiptCustodyAcknowledgments\" SET \"Quantity\"=31",
            "over-place" => "UPDATE \"ReceiptCustodyPlacements\" SET \"Quantity\"=40",
            "wrong-dispatch" => $"UPDATE \"ReceiptCustodyAcknowledgments\" SET \"DispatchMovementId\"={placement.MovementId}",
            "missing-parent" => $"UPDATE \"TreatmentLineageMovements\" SET \"InterCrewTransferId\"=NULL WHERE \"Id\"={dispatch.Id}",
            "wrong-receipt" => "UPDATE \"ReceiptCustodyAcknowledgments\" SET \"ReceiptId\"=100000",
            "duplicate-ack" => "INSERT INTO \"ReceiptCustodyAcknowledgments\" (\"ReceiptId\",\"InterCrewTransferId\",\"DispatchMovementId\",\"Quantity\",\"OperationKey\",\"ActorId\",\"AcknowledgedAt\") SELECT \"ReceiptId\",\"InterCrewTransferId\",\"DispatchMovementId\",\"Quantity\",'duplicate-intent',\"ActorId\",\"AcknowledgedAt\" FROM \"ReceiptCustodyAcknowledgments\"",
            "over-reversal" => "UPDATE \"ReceiptCustodyReversals\" SET \"Quantity\"=30",
            "wrong-reversal" => $"UPDATE \"TreatmentLineageMovements\" SET \"ReversesTreatmentLineageMovementId\"={dispatch.Id} WHERE \"MovementType\"='ReceiptPlacementReversal'",
            "false-completion" => "UPDATE \"InterCrewTransfers\" SET \"Status\"='Received'",
            "wrong-room" => "UPDATE \"InterCrewTransfers\" SET \"DestinationRoomId\"=9002",
            "missing-treatment" => $"DELETE FROM \"TreatmentLineageSegmentApplications\" WHERE \"TreatmentLineageSegmentId\"={dispatch.SourceSegmentId}",
            "wrong-treatment" => $"UPDATE \"TreatmentLineageMovements\" SET \"TreatmentSignatureSnapshot\"='u' WHERE \"Id\"={dispatch.Id}",
            "placement-treatment" => $"UPDATE \"TreatmentLineageMovements\" SET \"TreatmentSignatureSnapshot\"='u',\"TreatmentStateSnapshot\"='Untreated' WHERE \"Id\"={placement.MovementId}",
            "missing-origin" => "UPDATE \"RoomInventoryAdjustments\" SET \"AdjustmentType\"='UnrecordedOrigin' WHERE \"AdjustmentType\"='ReceiptAdd'",
            "chronology" => "UPDATE \"ReceiptCustodyReversals\" SET \"ReversedAt\"='2000-01-01'",
            "parent-quantity" => "UPDATE \"InterCrewTransfers\" SET \"BinsReceived\"=21",
            _ => throw new InvalidOperationException(defect)
        };
        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(f.Connection);
        await using (var connection = new Npgsql.NpgsqlConnection(f.Connection))
        {
            await connection.OpenAsync();
            await using var command = new Npgsql.NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
        var corrupted = await f.Snapshot();
        var blocked = await InterCrewTopologyReadiness.VerifyAsync(db, default);
        Assert.Contains(blocked.Issues, x => x.TransferId == parent.Id);
        Assert.Empty(blocked.Custody);
        Assert.Equal(corrupted, await f.Snapshot());
    }

    [Fact]
    public void Release_readiness_has_one_compensation_aware_topology_decision()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "CropQc.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var program = File.ReadAllText(Path.Combine(dir.FullName, "src/CropQc.Web/Program.cs"));
        Assert.Contains("InterCrewTopologyReadiness.VerifyAsync", program);
        Assert.DoesNotContain("readinessDb.InterCrewTransfers.AsNoTracking().CountAsync", program);
        var proof = File.ReadAllText(Path.Combine(dir.FullName, "src/CropQc.Data/Inventory/InterCrewTopologyReadiness.cs"));
        Assert.Contains("InventoryPhysicalFlow.Matches", proof);
        Assert.Contains("ReceiptCustodyProof.Valid", proof);
        Assert.Contains("InventoryOriginGuard.ValidateAsync", proof);
        Assert.Contains("ReadCustodyTreatment", proof);
        Assert.DoesNotContain(".CurrentBins", proof);
        Assert.DoesNotContain("SaveChanges", proof);
    }
}
