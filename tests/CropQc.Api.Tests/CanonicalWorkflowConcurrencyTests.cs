using CropQc.Data;
using CropQc.Data.Inventory;
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
    public Task Dump_versus_dump_through_normal_services() => Race("Dump", "Dump");
    [InventoryPostgresFact]
    public Task Room_move_versus_dump_through_normal_services() => Race("Move", "Dump");
    [InventoryPostgresFact]
    public Task Processor_versus_transfer_through_normal_services() => Race("Processor", "Move");
    [InventoryPostgresFact]
    public Task Treatment_versus_transfer_through_normal_services() => Race("Treatment", "Move");

    private static async Task Race(string first, string second)
    {
        await using var f = await Fixture.Create();
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
        var a = await Prepare(first, left, new InventoryCommandExecutor(factory, observer, new CanonicalRunExpectationWriter()));
        var b = await Prepare(second, right, new InventoryCommandExecutor(factory, observer, new CanonicalRunExpectationWriter()));
        var results = await Task.WhenAll(a(), b());
        Assert.True(results.Count(x => x == null) == 1, string.Join("; ", results.Select(x => x ?? "Committed")));
        await using var check = f.CreateDbContext();
        Assert.Equal(1, await check.InventoryCommands.CountAsync());
        Assert.Equal(1, await check.AuditLogs.CountAsync(x => x.Action == "CanonicalInventoryNormalization"));
        Assert.Equal(19, (await check.Receipts.SingleAsync(x => x.Id == 100000)).BinCount);
        Assert.InRange(await f.Physical(), 0, 19);
        Assert.InRange(await f.Physical(9003), 0, 19);
        Assert.All(await check.TreatmentLineageSegments.ToListAsync(), x => Assert.True(x.CurrentBins >= 0));
    }

    private static async Task<Func<Task<string?>>> Prepare(string operation, CropQcDbContext db, InventoryCommandExecutor executor)
    {
        var http = CanonicalReceivingWorkflowTests.Operator();
        var principal = http.HttpContext!.User;
        var access = new CanonicalOutsideWorkflowTests.Access();
        var time = new PacificBusinessTimeService(new SystemClock());
        var ledger = new CropQc.Web.Services.RoomInventoryLedgerQueryService(db);
        var treatment = new RoomTreatmentService(db, ledger, access, http, time, NullLogger<RoomTreatmentService>.Instance, executor);
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
        var stock = Assert.Single((await processor.GetPageAsync(null, false, null, null, null, null, default)).Inventory);
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
