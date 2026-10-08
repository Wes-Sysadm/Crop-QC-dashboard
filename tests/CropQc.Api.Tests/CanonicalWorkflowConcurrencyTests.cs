using CropQc.Data;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class CanonicalWorkflowConcurrencyTests
{
    [InventoryPostgresFact]
    public async Task Truck_completion_versus_partial_return_through_normal_services()
    {
        await using var f = await Fixture.Create();
        var receiving = await f.ReceiveCommand();
        var id = receiving.Lines[0].Source.Location.CustodyRecordId!.Value;
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var left = factory.CreateDbContext();
        await using var right = factory.CreateDbContext();
        var parent = await left.InterCrewTransfers.AsNoTracking().SingleAsync(x => x.Id == id);
        var dispatch = await left.TreatmentLineageMovements.AsNoTracking().SingleAsync(x => x.InterCrewTransferId == id && x.MovementType == "InterCrewDispatch");
        var beforeCommands = await left.InventoryCommands.CountAsync();
        var arrived = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = new Observer(async (stage, attempt) =>
        {
            if (stage != "Resolved" || attempt != 1) return;
            if (Interlocked.Increment(ref arrived) == 2) gate.SetResult();
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(45));
        });
        var completion = CanonicalTruckReceiptWorkflowTests.Service(left, new InventoryCommandExecutor(factory, observer));
        var returns = CanonicalTruckReceiptWorkflowTests.Service(right, new InventoryCommandExecutor(factory, observer));
        var results = await Task.WhenAll(
            completion.CompleteAsync(new()
            {
                TransferId = id,
                TransferVersion = parent.ConcurrencyVersion,
                ReceiptId = receiving.ReceivingEvidence!.ReceiptId,
                ReceiptVersion = receiving.ReceivingEvidence.ExpectedVersion
            }, default),
            returns.EditTransferAsync(new()
            {
                TransferId = id,
                TransferVersion = parent.ConcurrencyVersion,
                DispatchMovementId = dispatch.Id,
                Bins = 5,
                Reason = "Local concurrent partial return"
            }, default));
        Assert.True(results.Count(x => x == null) == 1, string.Join("; ", results.Select(x => x ?? "Committed")));
        await using var check = factory.CreateDbContext();
        Assert.Equal(beforeCommands + 1, await check.InventoryCommands.CountAsync());
        var source = await f.Physical();
        var destination = (await new CropQc.Data.Inventory.RoomInventoryLedgerQueryService(check).GetSnapshotsAsync(9006, [9007], default)).Sum(x => x.CurrentBins);
        var transit = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(check)).ResolveAsync(new(9001, [], InventoryCustody.InTransit),
            new(AllowedCustody: InventoryCustody.InTransit), DateTimeOffset.UtcNow);
        Assert.All(transit.Positions, x => Assert.True(x.IsOperable));
        Assert.Equal(19, source + destination + transit.Positions.Sum(x => x.AuthoritativeQuantity));
        Assert.Equal(results[0] == null ? 0 : 5, source);
        Assert.Equal(results[0] == null ? 19 : 0, destination);
        Assert.Equal(results[0] == null ? 0 : 14, transit.Positions.Sum(x => x.AuthoritativeQuantity));
        Assert.Equal(results[0] == null, await check.Receipts.Where(x => x.Id == receiving.ReceivingEvidence.ReceiptId).Select(x => x.TransferCompletedAt != null).SingleAsync());
        var retained = await check.TreatmentLineageMovements.AsNoTracking().SingleAsync(x => x.Id == dispatch.Id);
        Assert.Equal(dispatch.BinCount, retained.BinCount); Assert.Equal(dispatch.SourceSegmentId, retained.SourceSegmentId);
    }

    [InventoryPostgresFact]
    public Task Dump_versus_dump_through_normal_services() => Race("Dump", "Dump");
    [InventoryPostgresFact]
    public Task Room_move_versus_dump_through_normal_services() => Race("Move", "Dump");
    [InventoryPostgresFact]
    public Task Processor_versus_transfer_through_normal_services() => Race("Processor", "Move");
    [InventoryPostgresFact]
    public Task Treatment_versus_transfer_through_normal_services() => Race("Treatment", "Move");
    [InventoryPostgresFact]
    public Task Receipt_correction_versus_transfer_through_normal_services() => Race("Correction", "Move");

    [InventoryCommandRestoreFact]
    public Task Restored_room_move_versus_dump_preserves_unrelated_history() => Race("Move", "Dump", true);
    [InventoryCommandRestoreFact]
    public Task Restored_treatment_versus_move_preserves_unrelated_history() => Race("Treatment", "Move", true);

    private static async Task Race(string first, string second, bool restored = false)
    {
        await using var f = restored ? await CanonicalRestoreFixture.Clone() : await Fixture.Create();
        Dictionary<string, string>? protectedFilters = null; string? protectedBefore = null;
        long receiptId = 100000;
        if (restored)
        {
            protectedFilters = await CanonicalRestoreFixture.ExistingRows(f);
            protectedFilters["Warehouses"] += " AND \"Code\" NOT IN ('WP','BASE-WP')";
            protectedBefore = await f.Snapshot(protectedFilters);
            await CanonicalRestoreFixture.SeedIsolatedRooms(f);
            await using var masters = f.CreateDbContext();
            masters.GrowerLots.Add(new() { Id = 100000, Grower = "Corpus", LotNumber = "CORPUS-100000" });
            await masters.SaveChangesAsync();
            // Restored history can already contain canonical commands. Seed the
            // isolated race through a real authorized origin, never a legacy write
            // or removal of the historical journal to disable its guard.
            var receiving = new InventoryCommandExecutor(new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection));
            var ticket = "RESTORED-RACE-" + Guid.NewGuid().ToString("N");
            var arrival = await receiving.ExecuteAsync(new(Guid.NewGuid().ToString("N"), InventoryCommandKind.ReceiveStock,
                8000, DateTimeOffset.UtcNow, "Disposable restored race origin", [],
                Receipt: new(2026, 9001, 9002, 100000, 9004, ticket, 19)));
            Assert.True(arrival.Status == InventoryCommandStatus.Committed, arrival.Detail);
            receiptId = await masters.Receipts.Where(x => x.CompuTechReceiptId == ticket).Select(x => x.Id).SingleAsync();
        }
        await using (var seed = f.CreateDbContext())
        {
            var actor = await seed.Users.SingleAsync(x => x.Id == 8000);
            actor.EmploymentFacility = "WP"; actor.EmploymentEffectiveAt = DateTimeOffset.UtcNow.AddDays(-1);
            seed.Processors.Add(new() { Id = 9005, Name = "Local race processor" });
            await seed.SaveChangesAsync();
        }
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        var arrived = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = new Observer(async (stage, attempt) =>
        {
            if (stage != "Resolved" || attempt != 1) return;
            if (Interlocked.Increment(ref arrived) == 2) gate.SetResult();
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(45));
        });
        await using var left = factory.CreateDbContext();
        await using var right = factory.CreateDbContext();
        var oldNormalizations = await left.AuditLogs.CountAsync(x => x.Action == "CanonicalInventoryNormalization");
        var oldCommands = await left.InventoryCommands.CountAsync();
        var a = await Prepare(first, left, new InventoryCommandExecutor(factory, observer, new CanonicalRunExpectationWriter()));
        var b = await Prepare(second, right, new InventoryCommandExecutor(factory, observer, new CanonicalRunExpectationWriter()));
        var results = await Task.WhenAll(a(), b());
        Assert.True(results.Count(x => x == null) == 1, string.Join("; ", results.Select(x => x ?? "Committed")));
        await using var check = f.CreateDbContext();
        Assert.Equal(oldCommands + 1, await check.InventoryCommands.CountAsync());
        Assert.Equal(oldNormalizations + (restored ? 0 : 1), await check.AuditLogs.CountAsync(x => x.Action == "CanonicalInventoryNormalization"));
        var receiptBins = (await check.Receipts.SingleAsync(x => x.Id == receiptId)).BinCount;
        if (first == "Correction")
        {
            Assert.Equal(results[0] == null ? 17 : 19, receiptBins);
            Assert.Equal(receiptBins, await f.Physical() + await f.Physical(9003));
        }
        else Assert.Equal(19, receiptBins);
        Assert.InRange(await f.Physical(), 0, 19);
        Assert.InRange(await f.Physical(9003), 0, 19);
        Assert.All(await check.TreatmentLineageSegments.Where(x => x.WarehouseId == 9001).ToListAsync(), x => Assert.True(x.CurrentBins >= 0));
        if (restored) Assert.Equal(protectedBefore, await f.Snapshot(protectedFilters));
    }

    private static async Task<Func<Task<string?>>> Prepare(string operation, CropQcDbContext db, InventoryCommandExecutor executor)
    {
        var http = CanonicalReceivingWorkflowTests.Operator();
        var principal = http.HttpContext!.User;
        var access = new CanonicalOutsideWorkflowTests.Access();
        var time = new PacificBusinessTimeService(new SystemClock());
        var ledger = new CropQc.Web.Services.RoomInventoryLedgerQueryService(db);
        var treatment = new RoomTreatmentService(db, ledger, access, http, time, NullLogger<RoomTreatmentService>.Instance, executor);
        if (operation == "Correction")
        {
            var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, executor);
            var form = await CanonicalReceiptCorrectionWorkflowTests.Form(db, service, 100000, 17);
            return async () => (await service.ApplyEditAsync(form, principal, default)).Error;
        }
        if (operation == "Dump")
        {
            var service = new BinsRunService(db, access, NullLogger<BinsRunService>.Instance, canonicalCommands: executor);
            var option = Assert.Single((await service.GetPageAsync(new() { Section = "Actual", WarehouseId = 9001, RoomIds = [9002] }, principal, default)).AvailableInventory);
            var form = new ActualRunForm
            {
                RunFacilityWarehouseId = 9001,
                RunAt = DateTimeOffset.UtcNow,
                SalesDeskId = await db.SalesDesks.Where(x => x.IsActive).Select(x => x.Id).FirstAsync(),
                Lines = [new() { InventoryKey = option.InventoryKey, TreatmentSignature = option.TreatmentSignature,
                    CanonicalFingerprint = option.CanonicalFingerprint, BinsRun = 19, ExpectedAvailableBins = 19 }]
            };
            return () => service.CreateActualRunAsync(form, principal, default);
        }
        if (operation == "Move")
        {
            var service = CanonicalReceivingWorkflowTests.Dashboard(db, executor, http);
            var page = await service.GetRoomDetailAsync(9002, default);
            var option = Assert.Single(page.TransferLotOptions);
            var form = new RoomTransferForm
            {
                FromRoomId = 9002,
                DestinationRoomId = 9003,
                DestinationWarehouseId = 9001,
                SourceLotKey = option.LotKey,
                TreatmentSignature = option.TreatmentSignature,
                BinCount = 19,
                TransferAt = DateTimeOffset.UtcNow,
                Reason = "Local race"
            };
            return () => service.CreateRoomTransferAsync(form, default);
        }
        if (operation == "Treatment")
        {
            var form = new RoomTreatmentApplyForm
            {
                RoomId = 9002,
                AppliedAt = DateTimeOffset.UtcNow,
                TreatmentChemicalId = await db.TreatmentChemicals.Where(x => x.IsActive && x.ApplicationLevel == "Room" && x.Crop == "Apples").Select(x => x.Id).FirstAsync()
            };
            Assert.Null((await treatment.GetApplyPageAsync(form, true, default)).Error);
            form.ConfirmedReview = true;
            return async () => (await treatment.ApplyAsync(form, default)).Error;
        }
        var processor = new ProcessorShipmentService(db, ledger, treatment, treatment,
            new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance), access, http, time, executor);
        var stock = Assert.Single((await processor.GetPageAsync(null, false, null, null, null, null, default)).Inventory.Where(x => x.WarehouseId == 9001));
        var shipment = new ProcessorShipmentForm
        {
            ProcessorId = 9005,
            SaleRate = 12,
            PricingBasis = "PerBin",
            ShippedAt = time.NowPacific.DateTime,
            ConfirmedReview = true,
            Lines = [new() { SourceKey = stock.SourceKey, ExpectedAvailableBins = 19, BinsSent = 19 }]
        };
        return async () => (await processor.CreateAsync(shipment, default)).Error;
    }
}
