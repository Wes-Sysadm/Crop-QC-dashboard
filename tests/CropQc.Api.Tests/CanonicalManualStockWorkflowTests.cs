using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class CanonicalManualStockWorkflowTests
{
    [InventoryPostgresFact]
    public async Task Positive_true_up_adds_only_declared_unknown_stock_without_inventing_receipt_or_treatment()
    {
        await using var f = await Fixture.Create();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var dashboard = CanonicalReceivingWorkflowTests.Dashboard(db, new InventoryCommandExecutor(factory));
        var page = await dashboard.GetRoomDetailAsync(9002, default);
        Assert.Null(page.DataWarning);
        var option = Assert.Single(page.TrueUpReceiptOptions);
        Assert.Equal(19, option.CurrentBins);
        var form = new RoomInventoryTrueUpForm
        {
            RoomId = 9002,
            ReceiptId = option.ReceiptId,
            CanonicalFingerprint = option.CanonicalFingerprint,
            NewBinCount = 26,
            Reason = "Local verified count",
            Notes = "Unverified treatment for seven newly counted bins"
        };
        Assert.Null(await dashboard.CreateRoomInventoryTrueUpAsync(form, default));
        Assert.Equal(26, await f.Physical());
        Assert.Equal(19, (await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == option.ReceiptId)).BinCount);
        var current = await db.TreatmentLineageSegments.AsNoTracking().Where(x => x.Disposition == "Current").ToListAsync();
        Assert.Equal(19, current.Single(x => x.TreatmentState == "Untreated").CurrentBins);
        var added = current.Single(x => x.TreatmentState == "Unknown");
        Assert.Equal(7, added.CurrentBins); Assert.Equal("x", added.TreatmentSignature); Assert.Null(added.ReceiptId);
        var movement = await db.TreatmentLineageMovements.AsNoTracking().SingleAsync(x => x.MovementType == "ManualTrueUp");
        Assert.Null(movement.SourceSegmentId); Assert.Equal(added.Id, movement.DestinationSegmentId); Assert.Equal(7, movement.BinCount);
        var p = Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow)).Positions);
        Assert.Equal(26, p.AuthoritativeQuantity); Assert.Equal(26, p.RawProjectionQuantity); Assert.False(p.IsOperable);
        var saved = await f.Snapshot();
        Assert.Null(await dashboard.CreateRoomInventoryTrueUpAsync(form, default)); Assert.Equal(saved, await f.Snapshot());
        var refreshed = Assert.Single((await dashboard.GetRoomDetailAsync(9002, default)).TrueUpReceiptOptions);
        form.OperationKey = Guid.NewGuid().ToString("N"); form.CanonicalFingerprint = refreshed.CanonicalFingerprint; form.NewBinCount = 10;
        Assert.NotNull(await dashboard.CreateRoomInventoryTrueUpAsync(form, default)); Assert.Equal(saved, await f.Snapshot());
    }
}
