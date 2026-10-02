using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class CanonicalReceiptLocationWorkflowTests
{
    [InventoryPostgresFact]
    public async Task Receipt_corrections_after_baseline_use_current_effective_dates_and_location_correction_preserves_later_movement()
    {
        await using var f = await Fixture.Create(2);
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        var import = CanonicalBaselineWorkflowTests.Service(db, executor);
        var baseline = await CanonicalBaselineWorkflowTests.Prepare(import, CanonicalBaselineWorkflowTests.Csv((100000, 19), (100001, 19)));
        Assert.Null((await import.ApplyAsync(baseline, "canonical-test@example.invalid", default)).Error);
        var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, executor);
        var actor = CanonicalReceivingWorkflowTests.Operator();
        var up = await CanonicalReceiptCorrectionWorkflowTests.Form(db, service, 100000, 20);
        Assert.Null((await service.ApplyEditAsync(up, actor.HttpContext!.User, default)).Error);
        Assert.Equal(39, await f.Physical());
        var down = await CanonicalReceiptCorrectionWorkflowTests.Form(db, service, 100000, 18);
        Assert.Null((await service.ApplyEditAsync(down, actor.HttpContext!.User, default)).Error);
        Assert.Equal(37, await f.Physical());
        var location = await CanonicalReceiptCorrectionWorkflowTests.Form(db, service, 100000, 18);
        location.RoomId = 9003;
        Assert.Null((await service.ApplyEditAsync(location, actor.HttpContext!.User, default)).Error);
        Assert.Equal(19, await f.Physical()); Assert.Equal(18, await f.Physical(9003));
        var saved = await f.Snapshot();
        Assert.True((await service.ApplyEditAsync(location, actor.HttpContext!.User, default)).WasIdempotent);
        Assert.Equal(saved, await f.Snapshot());
        var dashboard = CanonicalReceivingWorkflowTests.Dashboard(db, executor, actor);
        var option = Assert.Single((await dashboard.GetRoomDetailAsync(9003, default)).TransferLotOptions);
        Assert.Null(await dashboard.CreateRoomTransferAsync(new()
        {
            FromRoomId = 9003,
            DestinationRoomId = 9002,
            DestinationWarehouseId = 9001,
            SourceLotKey = option.LotKey,
            TreatmentSignature = option.TreatmentSignature,
            BinCount = 4,
            TransferAt = DateTimeOffset.UtcNow,
            Reason = "Real workflow in disposable database"
        }, default));
        var ledgerCount = await db.RoomInventoryAdjustments.CountAsync();
        var movementCount = await db.TreatmentLineageMovements.CountAsync();
        var provenance = await CanonicalReceiptCorrectionWorkflowTests.Form(db, service, 100000, 18);
        provenance.RoomId = 9002;
        Assert.Null((await service.ApplyEditAsync(provenance, actor.HttpContext!.User, default)).Error);
        Assert.Equal(23, await f.Physical()); Assert.Equal(14, await f.Physical(9003));
        Assert.Equal(ledgerCount, await db.RoomInventoryAdjustments.CountAsync());
        Assert.Equal(movementCount, await db.TreatmentLineageMovements.CountAsync());
        var receipt = await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == 100000);
        Assert.Equal(9002, receipt.RoomId); Assert.Equal(18, receipt.BinCount);
        Assert.Equal(19, (await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == 100001)).BinCount);
        Assert.Equal(2, await db.AuditLogs.CountAsync(x => x.Action == "CanonicalReceiptLocationCorrected"));
        Assert.All(await db.RoomInventoryAdjustments.Where(x => x.InventoryOperationKey != null).ToListAsync(),
            x => Assert.Equal(InventoryLedgerKinds.CanonicalCommandInvariantVersion, x.InventoryInvariantVersion));
    }

    [InventoryPostgresFact]
    public async Task Correcting_receiving_provenance_does_not_move_external_custody_and_return_still_uses_original_source()
    {
        await using var f = await Fixture.Create();
        await using (var seed = f.CreateDbContext())
        {
            seed.OutsideWarehouses.Add(new() { Id = 9005, Code = "LOCAL-OUT", Name = "Local outside", Address = "Local" });
            await seed.SaveChangesAsync();
        }
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        var outside = CanonicalTruckReceiptWorkflowTests.Inventory(db, executor);
        var source = Assert.Single(await outside.GetInventoryAsync(default));
        var sent = await outside.CreateAsync(new()
        {
            OutsideWarehouseId = 9005,
            SourceKey = source.SourceKey,
            BinCount = 19,
            TransferredAt = DateTime.Now,
            ExpectedAvailableBins = 19,
            ConfirmedReview = true,
            TruckLoadBolNumber = "LOCAL-RECEIVING-CORRECTION"
        }, default);
        Assert.Null(sent.Error);
        var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, executor);
        var form = await CanonicalReceiptCorrectionWorkflowTests.Form(db, service, 100000, 19);
        form.RoomId = 9003;
        var ledgerCount = await db.RoomInventoryAdjustments.CountAsync();
        var movementCount = await db.TreatmentLineageMovements.CountAsync();
        Assert.Null((await service.ApplyEditAsync(form, CanonicalReceivingWorkflowTests.Operator().HttpContext!.User, default)).Error);
        Assert.Equal(0, await f.Physical()); Assert.Equal(0, await f.Physical(9003));
        Assert.Equal(ledgerCount, await db.RoomInventoryAdjustments.CountAsync());
        Assert.Equal(movementCount, await db.TreatmentLineageMovements.CountAsync());
        Assert.Null(await outside.ReverseAsync(new() { TransferId = sent.TransferId!.Value, Reason = "Local return" }, default));
        Assert.Equal(19, await f.Physical()); Assert.Equal(0, await f.Physical(9003));
        Assert.Equal(9003, (await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == 100000)).RoomId);
    }

    [InventoryPostgresFact]
    public async Task Location_correction_rolls_back_projection_ledger_receipt_and_audit_at_each_boundary()
    {
        await using var f = await Fixture.Create();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        foreach (var stage in new[] { "Movement", "OperationAudit", "BeforeCommit" })
        {
            var reached = false;
            var executor = new InventoryCommandExecutor(factory, new Observer((at, _) =>
            {
                if (at != stage) return Task.CompletedTask;
                reached = true; return Task.FromException(new InvalidOperationException("Local receipt location failure"));
            }));
            var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, executor);
            var form = await CanonicalReceiptCorrectionWorkflowTests.Form(db, service, 100000, 19);
            form.RoomId = 9003;
            var before = await f.Snapshot();
            Assert.NotNull((await service.ApplyEditAsync(form, CanonicalReceivingWorkflowTests.Operator().HttpContext!.User, default)).Error);
            Assert.True(reached, stage); Assert.Equal(before, await f.Snapshot());
        }
    }
}
