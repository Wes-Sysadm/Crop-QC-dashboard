using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class CanonicalTruckReceiptWorkflowTests
{
    [InventoryPostgresFact]
    public async Task Partial_return_add_and_cancel_preserve_dispatch_history_and_matched_receipt()
    {
        await using var f = await Fixture.Create();
        var receiving = await f.ReceiveCommand();
        var id = receiving.Lines[0].Source.Location.CustodyRecordId!.Value;
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        var service = Service(db, executor);
        var dispatch = await db.TreatmentLineageMovements.AsNoTracking().SingleAsync(x => x.InterCrewTransferId == id && x.MovementType == "InterCrewDispatch");
        var oldSegment = await db.TreatmentLineageSegments.AsNoTracking().SingleAsync(x => x.Id == dispatch.SourceSegmentId);
        async Task<long> Version() => await db.InterCrewTransfers.Where(x => x.Id == id).Select(x => x.ConcurrencyVersion).SingleAsync();
        var first = new TransitEditForm { TransferId = id, TransferVersion = await Version(), DispatchMovementId = dispatch.Id, Bins = 5, Reason = "Local partial return" };
        Assert.Null(await service.EditTransferAsync(first, default));
        Assert.Equal(5, await f.Physical());
        Assert.Equal(14, await db.InterCrewTransfers.Where(x => x.Id == id).Select(x => x.BinsLoaded).SingleAsync());
        Assert.Equal(receiving.ReceivingEvidence!.ReceiptId, await db.InterCrewTransfers.Where(x => x.Id == id).Select(x => x.ReceivingReceiptId).SingleAsync());
        var saved = await f.Snapshot();
        Assert.Null(await service.EditTransferAsync(first, default));
        Assert.Equal(saved, await f.Snapshot());
        var option = Assert.Single(await Inventory(db, executor).GetInventoryAsync(default));
        var add = new TransitEditForm
        {
            TransferId = id,
            TransferVersion = await Version(),
            SourceKey = option.SourceKey,
            ExpectedAvailableBins = 5,
            Bins = 5,
            Reason = "Local add returned bins"
        };
        Assert.Null(await service.EditTransferAsync(add, default));
        Assert.Equal(0, await f.Physical());
        Assert.Equal(19, await db.InterCrewTransfers.Where(x => x.Id == id).Select(x => x.BinsLoaded).SingleAsync());
        var allocations = await service.ActiveAllocationsAsync(id, default);
        foreach (var allocation in allocations)
            Assert.Null(await service.EditTransferAsync(new()
            {
                TransferId = id,
                TransferVersion = await Version(),
                DispatchMovementId = allocation.Movement.Id,
                Bins = allocation.Bins,
                Reason = "Local whole cancellation"
            }, default));
        var parent = await db.InterCrewTransfers.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(InterCrewTransferStatuses.Reversed, parent.Status);
        Assert.Null(parent.ReceivingReceiptId); Assert.True(parent.RequiresTruckReceipt);
        Assert.Equal(19, await f.Physical());
        var retained = await db.TreatmentLineageSegments.AsNoTracking().SingleAsync(x => x.Id == oldSegment.Id);
        Assert.Equal(oldSegment.Disposition, retained.Disposition); Assert.Equal(oldSegment.CurrentBins, retained.CurrentBins);
        Assert.Equal(oldSegment.ConcurrencyVersion, retained.ConcurrencyVersion);
        Assert.Equal(dispatch.BinCount, (await db.TreatmentLineageMovements.AsNoTracking().SingleAsync(x => x.Id == dispatch.Id)).BinCount);
    }

    [InventoryPostgresFact]
    public async Task Normal_completion_reopen_match_and_recompletion_preserve_one_destination_population()
    {
        await using var f = await Fixture.Create();
        var receiving = await f.ReceiveCommand();
        var parentId = receiving.Lines[0].Source.Location.CustodyRecordId!.Value;
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        var service = Service(db, executor);
        async Task<TruckReceiptActionForm> Form() => new()
        {
            TransferId = parentId,
            ReceiptId = receiving.ReceivingEvidence!.ReceiptId,
            ReceiptVersion = await db.Receipts.Where(x => x.Id == receiving.ReceivingEvidence!.ReceiptId).Select(x => x.ConcurrencyVersion).SingleAsync(),
            TransferVersion = await db.InterCrewTransfers.Where(x => x.Id == parentId).Select(x => x.ConcurrencyVersion).SingleAsync(),
            Reason = "Local exact reopen"
        };
        var initial = await f.Snapshot();
        Assert.NotNull(await Service(db, executor, false).CompleteAsync(await Form(), default));
        Assert.Equal(initial, await f.Snapshot());
        var form = await Form();
        Assert.Null(await service.CompleteAsync(form, default));
        Assert.Equal(19, await Destination());
        var saved = await f.Snapshot();
        Assert.Null(await service.CompleteAsync(form, default));
        Assert.Equal(saved, await f.Snapshot());
        var reopen = await Form();
        Assert.Null(await service.ReopenAsync(reopen, default));
        Assert.Equal(0, await Destination());
        var transit = Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(
            new(9001, [], InventoryCustody.InTransit, parentId), new(AllowedCustody: InventoryCustody.InTransit), DateTimeOffset.UtcNow)).Positions);
        Assert.Equal(19, transit.AvailableQuantity);
        Assert.True((await db.InterCrewTransfers.AsNoTracking().SingleAsync(x => x.Id == parentId)).RequiresTruckReceipt);
        saved = await f.Snapshot();
        Assert.Null(await service.ReopenAsync(reopen, default));
        Assert.Equal(saved, await f.Snapshot());
        Assert.Null(await service.MatchAsync(await Form(), default));
        Assert.Null(await service.CompleteAsync(await Form(), default));
        Assert.Equal(19, await Destination());
        Assert.Equal(0, await f.Physical());
        Assert.Equal(1, await db.Receipts.CountAsync(x => x.IsTransferReceipt));
        Assert.Equal(2, await db.TreatmentLineageMovements.CountAsync(x => x.InterCrewTransferId == parentId && x.MovementType == "InterCrewReceive"));

        async Task<int> Destination() => (await new CropQc.Data.Inventory.RoomInventoryLedgerQueryService(db).GetSnapshotsAsync(9006, [9007], default)).Sum(x => x.CurrentBins);
    }

    internal static TruckReceiptReconciliationService Service(CropQc.Data.CropQcDbContext db, IInventoryCommandExecutor commands, bool enabled = true) => new(
        db, Inventory(db, commands), null!, new CropQc.Web.Services.RoomInventoryLedgerQueryService(db),
        new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance), new CanonicalOutsideWorkflowTests.Access(),
        CanonicalReceivingWorkflowTests.Operator(), new PacificBusinessTimeService(new SystemClock()), new TruckReceiptOptions { Enabled = enabled }, commands);

    internal static OutsideWarehouseTransferService Inventory(CropQc.Data.CropQcDbContext db, IInventoryCommandExecutor commands)
    {
        var ledger = new CropQc.Web.Services.RoomInventoryLedgerQueryService(db);
        var access = new CanonicalOutsideWorkflowTests.Access();
        var actor = CanonicalReceivingWorkflowTests.Operator();
        var time = new PacificBusinessTimeService(new SystemClock());
        var treatment = new RoomTreatmentService(db, ledger, access, actor, time, NullLogger<RoomTreatmentService>.Instance, commands);
        return new(db, ledger, treatment, treatment, new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance), access, actor, time, commands);
    }
}
