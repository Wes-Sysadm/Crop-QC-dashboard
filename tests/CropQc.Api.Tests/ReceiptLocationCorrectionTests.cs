using System.Text.Json;
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

public sealed partial class ReceiptLocationCorrectionTests
{
    [InventoryPostgresFact]
    public async Task Partial_consumption_and_transfer_move_only_selected_fifty_and_preserve_all_history()
    {
        await using var f = await Hundred();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        var dashboard = CanonicalReceivingWorkflowTests.Dashboard(db, executor);
        await Dump(dashboard, 30);
        await Transfer(dashboard, 9002, 9003, 20);
        var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, executor);
        var preview = (await service.GetPreviewAsync(100000, default))!;
        Assert.Equal(70, preview.CurrentInventory);
        Assert.Equal(new[] { 20, 50 }, preview.Balances.Select(x => x.CurrentBins).Order().ToArray());
        var form = await Form(db, service);
        var before = await f.Snapshot();
        Assert.Contains("split across multiple rooms", (await service.ApplyEditAsync(form, Actor(), default)).Error);
        Assert.Equal(before, await f.Snapshot());
        form.CorrectionSourceRoomId = 9002;
        var history = await History(db);
        var receiptBefore = await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == 100000);
        var applied = await service.ApplyEditAsync(form, Actor(), default);
        Assert.Null(applied.Error);
        Assert.Equal(0, await f.Physical()); Assert.Equal(20, await f.Physical(9003)); Assert.Equal(50, await f.Physical(9008));
        Assert.Equal(history, await History(db, true));
        var current = (await service.GetPreviewAsync(100000, default))!;
        Assert.Null(current.CanonicalBlocker); Assert.Equal(70, current.CurrentInventory);
        var receipt = await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == 100000);
        Assert.Equal(100, receipt.BinCount); Assert.Equal(receiptBefore.ReceivedAt, receipt.ReceivedAt);
        Assert.Equal(receiptBefore.GrowerLotId, receipt.GrowerLotId); Assert.Equal(receiptBefore.FruitProfileId, receipt.FruitProfileId);
        var op = await db.ReceiptInventoryOverrides.AsNoTracking().SingleAsync();
        Assert.Equal(0, op.InventoryDelta); Assert.Equal(70, op.CurrentInventoryBefore); Assert.Equal(70, op.CurrentInventoryAfter);
        Assert.Equal(8000, op.AdministratorUserId); Assert.Equal(form.Reason, op.Reason); Assert.True(op.IsComplete);
        Assert.Equal(9002, JsonDocument.Parse(op.BeforeReceiptSnapshotJson).RootElement.GetProperty("roomId").GetInt32());
        Assert.Equal(9008, JsonDocument.Parse(op.AfterReceiptSnapshotJson).RootElement.GetProperty("roomId").GetInt32());
        var audit = await db.AuditLogs.SingleAsync(x => x.Action == "CanonicalReceiptLocationCorrected");
        Assert.Equal(50, JsonDocument.Parse(audit.AfterValuesJson!).RootElement.GetProperty("relocatedBins").GetInt32());
        Assert.NotNull(await service.GetAuditDetailAsync(op.Id, default));
        Assert.Equal(50, (await dashboard.GetRoomDetailAsync(9008, default)).TransferAvailableBins);
        Assert.All(await db.RoomInventoryAdjustments.Where(x => x.ReceiptInventoryOverrideId == op.Id).ToArrayAsync(),
            x => Assert.Equal(InventoryLedgerKinds.CanonicalCommandInvariantVersion, x.InventoryInvariantVersion));
        before = await f.Snapshot();
        Assert.True((await service.ApplyEditAsync(form, Actor(), default)).WasIdempotent);
        Assert.Equal(before, await f.Snapshot());
    }

    [InventoryPostgresFact]
    public async Task Fully_remaining_and_partially_consumed_untreated_receipts_move_their_remaining_quantity()
    {
        foreach (var consumed in new[] { 0, 30 })
        {
            await using var f = await Hundred();
            var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
            await using var db = factory.CreateDbContext();
            var executor = new InventoryCommandExecutor(factory);
            if (consumed > 0) await Dump(CanonicalReceivingWorkflowTests.Dashboard(db, executor), consumed);
            var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, executor);
            Assert.Null((await service.ApplyEditAsync(await Form(db, service), Actor(), default)).Error);
            Assert.Equal(0, await f.Physical()); Assert.Equal(100 - consumed, await f.Physical(9008));
            var state = (await new InventoryReceiptAvailability(db).ReadAsync(100000, default))!;
            Assert.Equal(100 - consumed, state.RoomQuantity);
            Assert.All(state.Allocations, x => { Assert.Equal("u", x.Slice.Signature); Assert.Equal("Untreated", x.Slice.State); });
        }
    }

    [InventoryPostgresFact]
    public async Task Fully_depleted_receipt_blocks_without_metadata_or_inventory_changes()
    {
        await using var f = await Hundred();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        await Dump(CanonicalReceivingWorkflowTests.Dashboard(db, executor), 100);
        var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, executor);
        var form = await Form(db, service);
        var before = await f.Snapshot();
        Assert.Contains("No current receipt inventory", (await service.ApplyEditAsync(form, Actor(), default)).Error);
        Assert.Equal(before, await f.Snapshot());
    }

    [InventoryPostgresFact]
    public async Task Transfer_after_preview_rejects_stale_correction_without_moving_fifty_when_forty_remain()
    {
        await using var f = await Hundred();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        var dashboard = CanonicalReceivingWorkflowTests.Dashboard(db, executor);
        await Dump(dashboard, 50);
        var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, executor);
        var form = await Form(db, service); form.CorrectionSourceRoomId = 9002;
        await Transfer(dashboard, 9002, 9003, 10);
        var before = await f.Snapshot();
        var result = await service.ApplyEditAsync(form, Actor(), default);
        Assert.True(result.IsConflict); Assert.Contains("reload", result.Error);
        Assert.Equal(before, await f.Snapshot());
        Assert.Equal(40, await f.Physical()); Assert.Equal(10, await f.Physical(9003)); Assert.Equal(0, await f.Physical(9008));
    }

    [InventoryPostgresFact]
    public async Task Treated_and_mixed_slices_preserve_applications_and_prior_movements()
    {
        foreach (var mixed in new[] { false, true })
        {
            await using var f = await Hundred();
            var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
            await using var db = factory.CreateDbContext();
            var executor = new InventoryCommandExecutor(factory);
            var dashboard = CanonicalReceivingWorkflowTests.Dashboard(db, executor);
            if (mixed) await Transfer(dashboard, 9002, 9003, 40);
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
            if (mixed) await Transfer(dashboard, 9003, 9002, 40);
            var before = (await new InventoryReceiptAvailability(db).ReadAsync(100000, default))!;
            var slices = JsonSerializer.Serialize(before.Allocations.OrderBy(x => x.Slice.Signature).Select(x => new { x.Slice.Signature, x.Slice.State, x.Slice.Quantity, x.Slice.ApplicationIds }));
            var history = await History(db);
            var applications = JsonSerializer.Serialize(await db.RoomTreatmentApplications.AsNoTracking().ToArrayAsync());
            var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, executor);
            Assert.Null((await service.ApplyEditAsync(await Form(db, service), Actor(), default)).Error);
            var after = (await new InventoryReceiptAvailability(db).ReadAsync(100000, default))!;
            Assert.Null(after.Blocker);
            Assert.Equal(slices, JsonSerializer.Serialize(after.Allocations.OrderBy(x => x.Slice.Signature).Select(x => new { x.Slice.Signature, x.Slice.State, x.Slice.Quantity, x.Slice.ApplicationIds })));
            Assert.Equal(history, await History(db, true));
            Assert.Equal(applications, JsonSerializer.Serialize(await db.RoomTreatmentApplications.AsNoTracking().ToArrayAsync()));
            Assert.Equal(100, await f.Physical(9008)); Assert.Equal(0, await f.Physical());
            Assert.Equal(mixed ? 2 : 1, after.Allocations.Length);
            Assert.Equal(2, await db.RoomInventoryAdjustments.CountAsync(x => x.ReceiptInventoryOverrideId != null));
        }
    }

    [InventoryPostgresFact]
    public async Task A_different_receipt_of_the_same_lot_is_not_relocated()
    {
        await using var f = await Hundred();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        var received = await new CanonicalReceivingService(db, executor).ReceiveAsync(Guid.NewGuid().ToString("N"), 8000, 2026,
            DateTimeOffset.UtcNow, 9001, 9002, 9004, 100000, "", "OTHER-RECEIPT", 7, "local", default);
        Assert.Equal(InventoryCommandStatus.Committed, received.Status);
        var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, executor);
        Assert.Null((await service.ApplyEditAsync(await Form(db, service), Actor(), default)).Error);
        Assert.Equal(7, await f.Physical()); Assert.Equal(100, await f.Physical(9008));
        var other = (await new InventoryReceiptAvailability(db).ReadAsync(received.Effects.Single().ParentId!.Value, default))!;
        Assert.Equal(7, other.RoomQuantity); Assert.All(other.Allocations, x => Assert.Equal(9002, x.Position.Location.RoomId));
    }

    [InventoryPostgresFact]
    public async Task Invalid_and_sealed_destination_or_source_reject_atomically()
    {
        foreach (var scenario in new[] { "missing", "warehouse", "same-room", "sealed-source", "sealed-destination" })
        {
            await using var f = await Hundred();
            if (scenario.StartsWith("sealed"))
            {
                await using var seed = f.CreateDbContext();
                (await seed.Rooms.SingleAsync(x => x.Id == (scenario == "sealed-source" ? 9002 : 9008))).IsSealed = true;
                await seed.SaveChangesAsync();
            }
            var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
            await using var db = factory.CreateDbContext();
            var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, new InventoryCommandExecutor(factory));
            var form = await Form(db, service);
            if (scenario == "same-room") { form.RoomId = 9002; form.CorrectionSourceRoomId = 9002; }
            if (scenario == "missing") form.RoomId = 123456;
            if (scenario == "warehouse") form.WarehouseId = 123456;
            var before = await f.Snapshot();
            Assert.NotNull((await service.ApplyEditAsync(form, Actor(), default)).Error);
            Assert.Equal(before, await f.Snapshot());
        }
    }

    internal static async Task<Fixture> Hundred()
    {
        var f = await Fixture.Create();
        await using var db = f.CreateDbContext();
        (await db.Receipts.SingleAsync(x => x.Id == 100000)).BinCount = 100;
        var ledger = await db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 100000);
        ledger.ChangeAmount = 100; ledger.NewBinCount = 100;
        var segment = await db.TreatmentLineageSegments.SingleAsync(x => x.Id == 100000);
        segment.CurrentBins = 100; segment.ReceiptId = 100000;
        db.Rooms.Add(new() { Id = 9008, WarehouseId = 9001, Code = "CORRECT", Name = "Corrected room" });
        await db.SaveChangesAsync();
        return f;
    }
    private static System.Security.Claims.ClaimsPrincipal Actor() => CanonicalReceivingWorkflowTests.Operator().HttpContext!.User;
    internal static async Task<AdminReceiptInventoryOverrideForm> Form(CropQcDbContext db, ReceiptInventoryOverrideService service)
    {
        var form = await CanonicalReceiptCorrectionWorkflowTests.Form(db, service, 100000, 100);
        form.RoomId = 9008; return form;
    }
    private static async Task Dump(DashboardDataService dashboard, int quantity)
    {
        var option = Assert.Single((await dashboard.GetRoomDetailAsync(9002, default)).DepletionReceiptOptions);
        Assert.Null(await dashboard.CreateRoomDepletionAsync(new()
        {
            RoomId = 9002,
            ReceiptId = 100000,
            BinCount = quantity,
            TreatmentSignature = option.TreatmentSignature,
            CanonicalFingerprint = option.CanonicalFingerprint,
            Destination = "Local packline",
            Notes = "Local regression"
        }, default));
    }
    private static async Task Transfer(DashboardDataService dashboard, int source, int destination, int quantity)
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
            Reason = "Local transfer regression"
        }, default));
    }
    private static async Task<string> History(CropQcDbContext db, bool excludeCorrection = false) => JsonSerializer.Serialize(new
    {
        Movements = await db.TreatmentLineageMovements.AsNoTracking().Where(x => !excludeCorrection || x.MovementType != "ReceiptLocationCorrection").OrderBy(x => x.Id).ToArrayAsync(),
        Runs = await db.BinsRunEntries.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Transfers = await db.RoomTransfers.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Depletions = await db.RoomDepletions.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()
    });
}
