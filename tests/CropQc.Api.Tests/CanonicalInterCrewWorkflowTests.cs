using CropQc.Data.Inventory;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class CanonicalInterCrewWorkflowTests
{
    [InventoryPostgresFact]
    public async Task Normal_dispatch_persists_Truck_Receipt_policy_across_pause_and_reactivation()
    {
        foreach (var enabledAtDispatch in new[] { false, true })
        {
            await using var f = await Fixture.Create();
            var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
            await using var db = factory.CreateDbContext();
            (await db.Warehouses.SingleAsync(x => x.Code == "EBS")).Code = "BASE-EBS";
            await db.SaveChangesAsync();
            db.Warehouses.Add(new() { Id = 9006, Code = "EBS", Name = "Local EBS" });
            db.Rooms.Add(new() { Id = 9007, WarehouseId = 9006, Code = "DEST", Name = "Local destination" }); await db.SaveChangesAsync();
            var executor = new InventoryCommandExecutor(factory);
            var time = new PacificBusinessTimeService(new SystemClock());
            var inventory = CanonicalTruckReceiptWorkflowTests.Inventory(db, executor);
            var option = Assert.Single(await inventory.GetInventoryAsync(default));
            InterCrewTransferService Service(bool enabled) => new(db, inventory, new CropQc.Web.Services.RoomInventoryLedgerQueryService(db),
                null!, null!, null!, new CanonicalOutsideWorkflowTests.Access(), CanonicalReceivingWorkflowTests.Operator(), time,
                truckReceiptOptions: new() { Enabled = enabled }, canonicalCommands: executor);
            var form = new InterCrewDispatchForm
            {
                SourceWarehouseId = 9001,
                SourceRoomId = 9002,
                SourceKey = option.SourceKey,
                ExpectedAvailableBins = 19,
                DestinationCustodyGroup = "EBS",
                BinsLoaded = 19,
                LoadedAt = time.NowPacific.DateTime,
                TruckLoadBolNumber = "LOCAL-CREW",
                Notes = "Local normal dispatch",
                ConfirmedReview = true
            };
            var sent = await Service(enabledAtDispatch).DispatchAsync(form, default);
            Assert.True(sent.Success, sent.Error); Assert.Equal(0, await f.Physical());
            var parent = await db.InterCrewTransfers.AsNoTracking().SingleAsync();
            Assert.Equal(enabledAtDispatch, parent.RequiresTruckReceipt); Assert.Equal(form.TruckLoadBolNumber, parent.TruckLoadBolNumber);
            var saved = await f.Snapshot();
            Assert.True((await Service(!enabledAtDispatch).DispatchAsync(form, default)).AlreadyApplied);
            Assert.Equal(saved, await f.Snapshot());
            var receive = new InterCrewReceiveForm
            {
                TransferId = parent.Id,
                DestinationRoomId = 9007,
                BinsReceived = 19,
                ReceivedAt = time.NowPacific.DateTime,
                Note = "Local receive"
            };
            var result = await Service(!enabledAtDispatch).ReceiveAsync(receive, default);
            Assert.Equal(!enabledAtDispatch, result.Success);
            if (enabledAtDispatch) Assert.Equal(saved, await f.Snapshot());
            else
            {
                Assert.Equal(19, (await new CropQc.Data.Inventory.RoomInventoryLedgerQueryService(db).GetSnapshotsAsync(9006, [9007], default)).Sum(x => x.CurrentBins));
                saved = await f.Snapshot();
                Assert.True((await Service(true).ReceiveAsync(receive, default)).AlreadyApplied);
                Assert.Equal(saved, await f.Snapshot());
            }
        }
    }
}
