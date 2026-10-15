using System.Collections.Immutable;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class CanonicalRestorationReadinessTests
{
    [InventoryPostgresTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Split_restoration_preserves_history_and_strict_readiness_distinguishes_preexisting_excess(bool loss, bool excess)
    {
        await using var f = await Fixture.Create(2);
        await using (var seed = f.CreateDbContext())
        {
            seed.OutsideWarehouses.Add(new() { Id = 9005, Code = "RESTORE", Name = "Outside" });
            seed.GrowerLots.Add(new() { Id = 100002, Grower = "Later corrected", LotNumber = "LATER" });
            if (!excess) foreach (var segment in await seed.TreatmentLineageSegments.Where(x => x.RoomId == 9002).ToListAsync()) segment.CurrentBins = 19;
            await seed.SaveChangesAsync();
        }
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        var initialTreatment = await Treatment();
        Assert.Equal(!excess, initialTreatment.Success);
        var receipt = (await Commit(new(Guid.NewGuid().ToString("N"), InventoryCommandKind.ReceiveStock, 8000, DateTimeOffset.UtcNow,
            "Exact seven", [], Receipt: new(2026, 9001, 9002, 100000, 9004, "RESTORE-SEVEN", 7)))).Effects.Single().ParentId!.Value;
        var source = (await Resolve()).Positions.Single(x => x.Identity.GrowerLotId == 100000);
        var dispatch = new InventoryCommand(Guid.NewGuid().ToString("N"), loss ? InventoryCommandKind.Loss : InventoryCommandKind.OutsideWarehouseTransfer,
            8000, DateTimeOffset.UtcNow, "Recorded original departure", [Line(source, 26)], CounterpartyId: loss ? null : 9005);
        var sent = await Commit(dispatch); var parent = sent.Effects.Single().ParentId!.Value;
        var preserved = await History();
        await Correct(100001);
        Assert.Equal(preserved, await History());
        var custody = loss ? null : await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(
            new(9001, [], InventoryCustody.OutsideWarehouse, parent), new(AllowedCustody: InventoryCustody.OutsideWarehouse), DateTimeOffset.UtcNow);
        var restore = new InventoryCommand(Guid.NewGuid().ToString("N"), loss ? InventoryCommandKind.ReverseLoss : InventoryCommandKind.Return,
            8000, DateTimeOffset.UtcNow, "Restore each current receipt identity", custody?.Positions.Select(x => Line(x, x.AuthoritativeQuantity)).ToImmutableArray() ?? [],
            OriginalOperationKey: loss ? null : dispatch.OperationKey, PhysicalParentId: parent);
        await Commit(restore); Assert.Equal(preserved, await History());
        var snapshot = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Replayed, (await executor.ExecuteAsync(restore)).Status);
        Assert.Equal(InventoryCommandStatus.Conflict, (await executor.ExecuteAsync(restore with { Reason = "Different retry" })).Status);
        Assert.Equal(snapshot, await f.Snapshot());
        var readiness = await Ready(); Assert.True(readiness.IsReady, JsonSerializer.Serialize(readiness.Issues));
        Assert.Equal(snapshot, await f.Snapshot());
        var treatment = await Treatment(); Assert.Equal(!excess, treatment.Success);
        if (excess) Assert.Contains(treatment.BlockingIssues, x => x.AuthoritativeBins == 26 && x.ExplicitLineageBins == 36);
        var positions = (await Resolve()).Positions;
        Assert.Equal(45, positions.Sum(x => x.AuthoritativeQuantity));
        var target = positions.Single(x => x.Identity.GrowerLotId == 100001);
        Assert.Equal(26, target.AvailableQuantity); Assert.Equal(excess ? 10 : 0, target.ProjectionExcess);
        var exact = (await new InventoryReceiptAvailability(db).ReadAsync(receipt, default))!;
        Assert.Null(exact.Blocker); Assert.Equal(7, exact.RoomQuantity);
        Assert.All(exact.Allocations, x => Assert.Equal(receipt, Assert.Single(x.Slice.ReceiptEvidenceIds)));
        if (!excess)
        {
            var table = loss ? "RoomInventoryLosses" : "OutsideWarehouseTransfers";
            var parentColumn = loss ? "RoomInventoryLossId" : "OutsideWarehouseTransferId";
            var negative = sent.Effects.Single().LedgerIds.Single();
            var movement = sent.Effects.Single().MovementIds.First();
            var credits = await db.RoomInventoryAdjustments.Where(x => x.ChangeAmount > 0
                && (loss ? x.RoomInventoryLossId == parent : x.OutsideWarehouseTransferId == parent)).Select(x => x.Id).ToArrayAsync();
            var corruptions = new[]
            {
                $"UPDATE \"{table}\" SET \"GrowerLotId\"=100001 WHERE \"Id\"={parent}",
                $"UPDATE \"RoomInventoryAdjustments\" SET \"ChangeAmount\"=\"ChangeAmount\"+1 WHERE \"Id\"={credits[0]}",
                $"UPDATE \"TreatmentLineageMovements\" SET \"ReversesTreatmentLineageMovementId\"=\"Id\" WHERE \"ReversesTreatmentLineageMovementId\"={movement}",
                $"UPDATE \"TreatmentLineageMovements\" SET \"{parentColumn}\"=NULL WHERE \"Id\"={movement}",
                $"UPDATE \"TreatmentLineageSegments\" SET \"TreatmentState\"='Unknown' WHERE \"Id\"=(SELECT \"SourceSegmentId\" FROM \"TreatmentLineageMovements\" WHERE \"Id\"={movement})",
                $"UPDATE \"TreatmentLineageMovements\" SET \"ReceiptId\"=NULL WHERE \"{parentColumn}\"={parent} AND \"ReceiptId\"={receipt} AND \"ReversesTreatmentLineageMovementId\" IS NULL",
                $"UPDATE \"RoomInventoryAdjustments\" SET \"GrowerLotId\"=100002 WHERE \"Id\"={negative}",
                $"UPDATE \"InventoryCommands\" SET \"IntentHash\"='invalid' WHERE \"OperationKey\"='{restore.OperationKey}'"
            };
            foreach (var sql in corruptions)
            {
                // Deliberate corruption in a disposable fixture only. No application guard is changed.
                await using var transaction = await db.Database.BeginTransactionAsync();
                ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(f.Connection);
                await using var corrupt = new NpgsqlCommand(sql, (NpgsqlConnection)db.Database.GetDbConnection(),
                    (NpgsqlTransaction)transaction.GetDbTransaction());
                await corrupt.ExecuteNonQueryAsync();
                Assert.False((await Ready()).IsReady, sql);
                await transaction.RollbackAsync(); db.ChangeTracker.Clear();
            }
            Assert.Equal(snapshot, await f.Snapshot());
            // Later mappings cannot retroactively change the identity of an earlier committed return.
            await Correct(100002);
            var later = await Ready(); Assert.True(later.IsReady, JsonSerializer.Serialize(later.Issues));
        }
        async Task<InventoryCommandResult> Commit(InventoryCommand command)
        { var r = await executor.ExecuteAsync(command); Assert.True(r.Status == InventoryCommandStatus.Committed, r.Detail); db.ChangeTracker.Clear(); return r; }
        async Task Correct(int targetLot)
        {
            var s = (await new InventoryReceiptAvailability(db).ReadAsync(receipt, default))!; Assert.Null(s.Blocker);
            var r = s.Receipt; var lot = await db.GrowerLots.SingleAsync(x => x.Id == targetLot);
            await Commit(new(Guid.NewGuid().ToString("N"), InventoryCommandKind.CorrectReceiptIdentity, 8000, DateTimeOffset.UtcNow, "Audited receipt identity", [],
                ReceiptIdentity: new(r.Id, r.ConcurrencyVersion, s.Fingerprint, new(r.CropYear, r.WarehouseId, r.RoomId, r.GrowerLotId!.Value, r.FruitProfileId, r.CompuTechReceiptId, r.BinCount),
                    InventoryReceiptAvailability.ReceiptIdentity(r) with { GrowerLotId = targetLot, GrowerNumber = lot.LotNumber, Lot = lot.LotNumber })));
        }
        Task<InventoryAvailabilityBatch> Resolve() => new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow);
        Task<InventoryDeductionReadinessResult> Ready() => new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance).VerifyReadinessAsync(default);
        Task<TreatmentLineageReadinessResult> Treatment() => new TreatmentLineageReadinessService(new CropQc.Web.Services.RoomInventoryLedgerQueryService(db), db).VerifyAsync(default);
        async Task<string> History() => JsonSerializer.Serialize(new
        {
            Ledger = await db.RoomInventoryAdjustments.AsNoTracking().Where(x => x.Id <= sent.Effects.Single().LedgerIds.Single()).OrderBy(x => x.Id).ToArrayAsync(),
            Movements = await db.TreatmentLineageMovements.AsNoTracking().Where(x => x.Id <= sent.Effects.Single().MovementIds.Max()).OrderBy(x => x.Id).ToArrayAsync()
        });
    }
    private static InventoryCommandLine Line(InventoryAvailabilityResult p, int bins) => new(new(p.Identity, p.Location, p.Watermark.Fingerprint, p.Watermark.Versions), bins, "u");
}
