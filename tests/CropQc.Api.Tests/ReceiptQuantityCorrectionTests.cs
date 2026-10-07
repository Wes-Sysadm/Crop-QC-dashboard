using System.Text.Json;
using System.Collections.Immutable;
using CropQc.Web.Models;
using Microsoft.Extensions.Logging.Abstractions;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Shared.Time;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class ReceiptQuantityCorrectionTests
{
    [InventoryPostgresFact]
    public async Task TR110044_buffer_correction_proves_new_receipt_inside_older_unassigned_pool()
    {
        await using var f = await Create(true);
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var id = await Target(db);
        var executor = new InventoryCommandExecutor(factory);
        var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, executor);
        var state = (await new InventoryReceiptAvailability(db).ReadAsync(id, default))!;
        Assert.Null(state.Blocker); Assert.Equal(35, state.RoomQuantity);
        var evidence = Assert.Single((await new InventoryEvidenceLoader(db).LoadAsync(new(9001, [9002]), DateTimeOffset.UtcNow, default)).Positions);
        var pool = InventoryAvailabilityResolver.Resolve(evidence, new());
        Assert.NotEqual(InventoryConfidence.Proven, pool.ReceiptProvenance.Confidence);
        Assert.False(InventoryAvailabilityResolver.Resolve(evidence, new(RequireExactReceipt: true)).IsOperable);
        Assert.True(InventoryAvailabilityResolver.Resolve(evidence, new(RequireExactReceipt: true, ReceiptId: id)).IsOperable);
        Assert.False(InventoryAvailabilityResolver.Resolve(evidence, new(RequireExactReceipt: true, ReceiptId: 100000)).IsOperable);
        var protectedBefore = await Protected(db, id);
        var form = await CanonicalReceiptCorrectionWorkflowTests.Form(db, service, id, 20);
        form.Reason = "Incorrect original bin count / 15 buffer bins included";
        Assert.Null((await service.ApplyEditAsync(form, Actor(), default)).Error);
        Assert.Equal(554, await f.Physical()); // 490 old pool + 44 other receipt + 20 corrected fruit.
        var after = (await new InventoryReceiptAvailability(db).ReadAsync(id, default))!;
        Assert.Null(after.Blocker); Assert.Equal(20, after.RoomQuantity); Assert.Equal(20, after.Receipt.BinCount);
        Assert.Equal(9002, after.Receipt.RoomId); Assert.Equal("9501", after.Receipt.LotCode);
        Assert.Equal("ORRD", after.Receipt.FruitProfile.VarietyCode); Assert.True(after.Receipt.FruitProfile.IsOrganic);
        Assert.All(after.Allocations, x => { Assert.Equal("u", x.Slice.Signature); Assert.Equal(new[] { id }, x.Slice.ReceiptEvidenceIds); });
        Assert.Equal(protectedBefore, await Protected(db, id));
        var correction = Assert.Single(await db.ReceiptInventoryOverrides.AsNoTracking().ToArrayAsync());
        Assert.Equal(35, correction.OldReceiptBinCount); Assert.Equal(20, correction.NewReceiptBinCount);
        Assert.Equal(-15, correction.InventoryDelta); Assert.Equal(35, correction.CurrentInventoryBefore); Assert.Equal(20, correction.CurrentInventoryAfter);
        Assert.Equal(form.Reason, correction.Reason); Assert.True(correction.IsComplete);
        Assert.Single(await db.AuditLogs.Where(x => x.Action == "CanonicalReceiptQuantityCorrected").ToArrayAsync());
        Assert.Contains(await db.AuditLogs.Where(x => x.Action == "CanonicalReceiptCreated").Select(x => x.AfterValuesJson).ToArrayAsync(), x => x!.Contains("\"quantity\":35"));
        Assert.Equal(15, await db.TreatmentLineageMovements.Where(x => x.MovementType == "ReceiptQuantityCorrection").SumAsync(x => x.BinCount));
        Assert.Empty(await db.RoomInventoryLosses.ToArrayAsync()); Assert.Empty(await db.RoomDepletions.ToArrayAsync());
        var saved = await f.Snapshot();
        Assert.True((await service.ApplyEditAsync(form, Actor(), default)).WasIdempotent); Assert.Equal(saved, await f.Snapshot());
        // Ordinary selectors use the corrected authoritative amount, never the original receipt count.
        var room = await CanonicalReceivingWorkflowTests.Dashboard(db, executor).GetRoomDetailAsync(9002, default);
        Assert.Equal(554, Assert.Single(room.TransferLotOptions).CurrentBins);
        Assert.Equal(20, room.DepletionReceiptOptions.Single(x => x.ReceiptId == id).CurrentBins);
        Assert.DoesNotContain(room.DepletionReceiptOptions, x => x.ReceiptId == 100000);
        var access = new CanonicalOutsideWorkflowTests.Access();
        var accessor = CanonicalReceivingWorkflowTests.Operator();
        var time = new PacificBusinessTimeService(new SystemClock());
        var ledger = new CropQc.Web.Services.RoomInventoryLedgerQueryService(db);
        var treatments = new RoomTreatmentService(db, ledger, access, accessor, time, NullLogger<RoomTreatmentService>.Instance);
        var invariant = new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance);
        var processor = new ProcessorShipmentService(db, ledger, treatments, treatments, invariant, access, accessor, time);
        Assert.Equal(554, Assert.Single((await processor.GetPageAsync(null, false, null, null, null, null, default)).Inventory).AvailableBins);
        var dump = new BinsRunService(db, access, NullLogger<BinsRunService>.Instance, roomTreatmentService: treatments);
        Assert.Equal(554, Assert.Single((await dump.GetPageAsync(new() { Section = "Actual", WarehouseId = 9001, RoomIds = [9002] }, Actor(), default)).AvailableInventory).CurrentBins);
    }

    [InventoryPostgresFact]
    public async Task Five_dumped_three_transferred_correction_removes_fifteen_only_from_selected_source()
    {
        await using var f = await Create();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var id = await Target(db); var executor = new InventoryCommandExecutor(factory);
        var dashboard = CanonicalReceivingWorkflowTests.Dashboard(db, executor);
        await Dump(dashboard, id, 5); await Transfer(dashboard, 3);
        Assert.Equal(27, await f.Physical()); Assert.Equal(3, await f.Physical(9003));
        var history = await History(db);
        var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, executor);
        var form = await CanonicalReceiptCorrectionWorkflowTests.Form(db, service, id, 20);
        var before = await f.Snapshot();
        Assert.Contains("Select which", (await service.ApplyEditAsync(form, Actor(), default)).Error);
        Assert.Equal(before, await f.Snapshot());
        var state = (await new InventoryReceiptAvailability(db).ReadAsync(id, default))!;
        form.TrueUpAllocations = [new() { TargetKey = state.Allocations.Single(x => x.Position.Location.RoomId == 9002).Key, Bins = 15 }];
        Assert.Null((await service.ApplyEditAsync(form, Actor(), default)).Error);
        Assert.Equal(12, await f.Physical()); Assert.Equal(3, await f.Physical(9003));
        Assert.Equal(history, await History(db));
        Assert.Equal(15, (await new InventoryReceiptAvailability(db).ReadAsync(id, default))!.RoomQuantity);
        Assert.Equal(20, 12 + 3 + await db.RoomInventoryAdjustments.Where(x => x.RoomDepletionId != null).SumAsync(x => -x.ChangeAmount));
    }

    [InventoryPostgresFact]
    public async Task Impossible_and_depleted_corrections_never_create_negative_or_restore_consumed_stock()
    {
        foreach (var consumed in new[] { 24, 35 })
        {
            await using var f = await Create();
            var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
            await using var db = factory.CreateDbContext();
            var id = await Target(db); var executor = new InventoryCommandExecutor(factory);
            await Dump(CanonicalReceivingWorkflowTests.Dashboard(db, executor), id, consumed);
            var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, executor);
            var form = await CanonicalReceiptCorrectionWorkflowTests.Form(db, service, id, 20);
            var before = await f.Snapshot();
            var result = await service.ApplyEditAsync(form, Actor(), default);
            Assert.NotNull(result.Error);
            if (consumed == 24) Assert.Contains("24 bins", result.Error);
            Assert.Equal(before, await f.Snapshot()); Assert.Equal(35 - consumed, await f.Physical());
        }
    }

    [InventoryPostgresFact]
    public async Task Unexplained_pool_withdrawal_does_not_become_exact_receipt_evidence()
    {
        await using var f = await Create(true);
        await using var db = f.CreateDbContext();
        var id = await Target(db);
        var evidence = Assert.Single((await new InventoryEvidenceLoader(db).LoadAsync(new(9001, [9002]), DateTimeOffset.UtcNow, default)).Positions);
        evidence = evidence with
        {
            AuthoritativeQuantity = evidence.AuthoritativeQuantity - 5,
            Projections = evidence.Projections.Select(x => x.Id == 100000 ? x with { Quantity = x.Quantity - 5 } : x).ToImmutableArray(),
            Ledger = evidence.Ledger.Add(new(999999, -5, "UnknownLoss", DateTimeOffset.UtcNow, null, null, true, DateTimeOffset.UtcNow))
        };
        Assert.False(InventoryAvailabilityResolver.Resolve(evidence, new(RequireExactReceipt: true, ReceiptId: id)).IsOperable);
    }

    [InventoryPostgresFact]
    public async Task Mixed_treatments_require_selection_and_preserve_applications_and_remaining_treatment()
    {
        await using var f = await Create();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var id = await Target(db); var executor = new InventoryCommandExecutor(factory);
        var dashboard = CanonicalReceivingWorkflowTests.Dashboard(db, executor);
        await Transfer(dashboard, 25);
        var treatments = new RoomTreatmentService(db, new CropQc.Web.Services.RoomInventoryLedgerQueryService(db), new CanonicalOutsideWorkflowTests.Access(),
            CanonicalReceivingWorkflowTests.Operator(), new PacificBusinessTimeService(new SystemClock()), NullLogger<RoomTreatmentService>.Instance, executor);
        var treatment = new RoomTreatmentApplyForm
        {
            RoomId = 9002,
            AppliedAt = DateTimeOffset.UtcNow,
            TreatmentChemicalId = await db.TreatmentChemicals.Where(x => x.IsActive && x.ApplicationLevel == "Room" && x.Crop == "Apples").Select(x => x.Id).FirstAsync()
        };
        Assert.Null((await treatments.GetApplyPageAsync(treatment, true, default)).Error); treatment.ConfirmedReview = true;
        Assert.Null((await treatments.ApplyAsync(treatment, default)).Error);
        await Transfer(dashboard, 25, 9003, 9002);
        var history = await History(db);
        var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, executor);
        var form = await CanonicalReceiptCorrectionWorkflowTests.Form(db, service, id, 20);
        var saved = await f.Snapshot();
        Assert.Contains("Select which", (await service.ApplyEditAsync(form, Actor(), default)).Error);
        Assert.Equal(saved, await f.Snapshot());
        var state = (await new InventoryReceiptAvailability(db).ReadAsync(id, default))!;
        Assert.Equal(2, state.Allocations.Length);
        form.TrueUpAllocations = [new() { TargetKey = state.Allocations.Single(x => x.Slice.Signature == "u").Key, Bins = 15 }];
        Assert.Null((await service.ApplyEditAsync(form, Actor(), default)).Error);
        Assert.Equal(20, await f.Physical()); Assert.Equal(history, await History(db));
        var after = (await new InventoryReceiptAvailability(db).ReadAsync(id, default))!;
        Assert.Null(after.Blocker); Assert.Equal(new[] { 10, 10 }, after.Allocations.Select(x => x.Slice.Quantity));
        Assert.Contains(after.Allocations, x => x.Slice.State == "Confirmed" && x.Slice.ApplicationIds.Length == 1);
    }

    [InventoryPostgresFact]
    public async Task Concurrent_receipt_consumption_rolls_back_quantity_correction_and_requires_refresh()
    {
        await using var f = await Create();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var id = await Target(db);
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        // Pause before the correction's writes, so a genuine competing command can commit.
        var executor = new InventoryCommandExecutor(factory, new Observer(async (stage, attempt) =>
        {
            if (stage == "Resolved" && attempt == 1) { reached.SetResult(); await release.Task.WaitAsync(TimeSpan.FromSeconds(45)); }
        }));
        var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, executor);
        var form = await CanonicalReceiptCorrectionWorkflowTests.Form(db, service, id, 20);
        var correction = service.ApplyEditAsync(form, Actor(), default);
        await reached.Task.WaitAsync(TimeSpan.FromSeconds(45));
        string before;
        try
        {
            await using var second = factory.CreateDbContext();
            await Dump(CanonicalReceivingWorkflowTests.Dashboard(second, new InventoryCommandExecutor(factory)), id, 5);
            before = await f.Snapshot();
        }
        finally { release.TrySetResult(); }
        Assert.True((await correction).IsConflict);
        Assert.Equal(before, await f.Snapshot()); Assert.Equal(30, await f.Physical());
    }

    [InventoryPostgresFact]
    public async Task Stale_quantity_review_and_audit_failure_leave_every_record_unchanged()
    {
        foreach (var stage in new[] { "stale", "OperationAudit", "BeforeCommit" })
        {
            await using var f = await Create();
            var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
            await using var db = factory.CreateDbContext();
            var id = await Target(db);
            var executor = new InventoryCommandExecutor(factory, new Observer((at, _) => at == stage
                ? Task.FromException(new InvalidOperationException("Local audit rollback injection")) : Task.CompletedTask));
            var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, executor);
            var form = await CanonicalReceiptCorrectionWorkflowTests.Form(db, service, id, 20);
            if (stage == "stale") await Dump(CanonicalReceivingWorkflowTests.Dashboard(db, executor), id, 5);
            var before = await f.Snapshot();
            Assert.NotNull((await service.ApplyEditAsync(form, Actor(), default)).Error);
            Assert.Equal(before, await f.Snapshot());
        }
    }

    [InventoryPostgresFact]
    public async Task Completed_truck_link_and_receiving_receipt_remain_historical_after_quantity_correction()
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
        var form = await CanonicalReceiptCorrectionWorkflowTests.Form(db, service, 100000, 20);
        Assert.Null((await service.ApplyEditAsync(form, Actor(), default)).Error);
        Assert.Equal(before, await TruckHistory());
        Assert.Equal(20, (await new InventoryReceiptAvailability(db).ReadAsync(100000, default))!.RoomQuantity);
        Assert.Equal(1, (await db.ReceiptInventoryOverrides.SingleAsync()).InventoryDelta);
    }

    internal static async Task<Fixture> Create(bool pooled = false)
    {
        var f = await Fixture.Create();
        await using (var db = f.CreateDbContext())
        {
            (await db.FruitProfiles.SingleAsync(x => x.VarietyCode == "ORRD")).VarietyCode = "BASE-ORRD";
            await db.SaveChangesAsync();
            var profile = await db.FruitProfiles.SingleAsync(x => x.Id == 9004);
            profile.VarietyCode = "ORRD"; profile.Name = "Organic Red Delicious"; profile.ProductionType = "Organic"; profile.IsOrganic = true;
            var lot = await db.GrowerLots.SingleAsync(x => x.Id == 100000); lot.LotNumber = "9501"; lot.Grower = "W&H-PESCLLO 20 ORG CHIL";
            var receipt = await db.Receipts.SingleAsync(x => x.Id == 100000);
            receipt.BinCount = pooled ? 490 : 0; receipt.LotCode = receipt.GrowerNumber = "9501"; receipt.GrowerName = lot.Grower;
            var ledger = await db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 100000);
            ledger.ChangeAmount = ledger.NewBinCount = receipt.BinCount; ledger.LotNumber = "9501"; ledger.VarietyCode = "ORRD";
            var segment = await db.TreatmentLineageSegments.SingleAsync(x => x.Id == 100000);
            segment.IdentityKey = new InventoryIdentity(2026, 100000, 9004, "9501", "9501", "ORRD", "Organic", true, "").Key;
            segment.CurrentBins = receipt.BinCount; segment.LotNumberSnapshot = segment.GrowerNumberSnapshot = "9501";
            segment.VarietyCodeSnapshot = "ORRD"; segment.ProductionTypeSnapshot = "Organic"; segment.IsOrganicSnapshot = true;
            (await db.Rooms.SingleAsync(x => x.Id == 9002)).Code = "BM-4";
            (await db.Warehouses.SingleAsync(x => x.Code == "EBS")).Code = "BASE-EBS";
            await db.SaveChangesAsync();
            (await db.Warehouses.SingleAsync(x => x.Id == 9001)).Code = "EBS";
            await db.SaveChangesAsync();
        }
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using (var db = factory.CreateDbContext())
        {
            var receiving = new CanonicalReceivingService(db, new InventoryCommandExecutor(factory));
            if (pooled) Assert.Equal(InventoryCommandStatus.Committed, (await receiving.ReceiveAsync(Guid.NewGuid().ToString("N"), 8000, 2026,
                DateTimeOffset.UtcNow, 9001, 9002, 9004, 100000, "", "OTHER-CURRENT-RECEIPT", 44, "local", default)).Status);
            Assert.Equal(InventoryCommandStatus.Committed, (await receiving.ReceiveAsync(Guid.NewGuid().ToString("N"), 8000, 2026,
                DateTimeOffset.UtcNow, 9001, 9002, 9004, 100000, "", "TR110044", 35, "local", default)).Status);
        }
        return f;
    }
    internal static Task<long> Target(CropQcDbContext db) => db.Receipts.Where(x => x.CompuTechReceiptId == "TR110044").Select(x => x.Id).SingleAsync();
    private static System.Security.Claims.ClaimsPrincipal Actor() => CanonicalReceivingWorkflowTests.Operator().HttpContext!.User;
    private static async Task Dump(DashboardDataService dashboard, long receipt, int quantity)
    {
        var option = (await dashboard.GetRoomDetailAsync(9002, default)).DepletionReceiptOptions.Single(x => x.ReceiptId == receipt);
        Assert.Null(await dashboard.CreateRoomDepletionAsync(new()
        {
            RoomId = 9002,
            ReceiptId = receipt,
            BinCount = quantity,
            TreatmentSignature = option.TreatmentSignature,
            CanonicalFingerprint = option.CanonicalFingerprint,
            Destination = "Local packline",
            Notes = "Local quantity regression"
        }, default));
    }
    private static async Task Transfer(DashboardDataService dashboard, int quantity, int source = 9002, int destination = 9003)
    {
        var option = Assert.Single((await dashboard.GetRoomDetailAsync(source, default)).TransferLotOptions);
        Assert.Null(await dashboard.CreateRoomTransferAsync(new()
        {
            FromRoomId = source,
            DestinationRoomId = destination,
            DestinationWarehouseId = 9001,
            SourceLotKey = option.LotKey,
            TreatmentSignature = option.TreatmentSignature,
            BinCount = quantity,
            TransferAt = DateTimeOffset.UtcNow,
            Reason = "Local quantity regression"
        }, default));
    }
    private static async Task<string> History(CropQcDbContext db) => JsonSerializer.Serialize(new
    {
        Movements = await db.TreatmentLineageMovements.AsNoTracking().Where(x => x.MovementType != "ReceiptQuantityCorrection").OrderBy(x => x.Id).ToArrayAsync(),
        Runs = await db.BinsRunEntries.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Transfers = await db.RoomTransfers.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Depletions = await db.RoomDepletions.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Truck = await db.InterCrewTransfers.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Applications = await db.RoomTreatmentApplications.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()
    });
    private static async Task<string> Protected(CropQcDbContext db, long id) => JsonSerializer.Serialize(new
    {
        History = await History(db),
        Receipts = await db.Receipts.AsNoTracking().Where(x => x.Id != id).OrderBy(x => x.Id).ToArrayAsync(),
        Segments = await db.TreatmentLineageSegments.AsNoTracking().Where(x => x.ReceiptId != id || x.ReceiptId == null).OrderBy(x => x.Id).ToArrayAsync()
    });
}



