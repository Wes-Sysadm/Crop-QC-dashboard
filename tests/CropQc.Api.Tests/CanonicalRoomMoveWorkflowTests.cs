using CropQc.Data.Inventory;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class CanonicalRoomMoveWorkflowTests
{
    [InventoryPostgresFact]
    public async Task Room_screen_bulk_move_and_exact_reversal_conserve_each_lot_and_preserve_history()
    {
        await using var f = await Fixture.Create(2);
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        var dashboard = CanonicalReceivingWorkflowTests.Dashboard(db, executor);
        var page = await dashboard.GetRoomDetailAsync(9002, default);
        Assert.Null(page.DataWarning);
        Assert.Equal(38, page.TransferAvailableBins);
        Assert.Equal(2, page.TransferLotOptions.Count);
        var form = page.TransferForm;
        form.TransferAllEligible = true; form.BinCount = 38; form.DestinationWarehouseId = 9001;
        form.DestinationRoomId = 9003; form.Reason = "Local bulk move";
        Assert.Null(await dashboard.CreateRoomTransferAsync(form, default));
        Assert.Equal(0, await f.Physical());
        Assert.Equal(38, await f.Physical(9003));
        var saved = await f.Snapshot();
        Assert.Null(await dashboard.CreateRoomTransferAsync(form, default));
        Assert.Equal(saved, await f.Snapshot());
        var parents = await db.RoomTransfers.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
        Assert.Equal(2, parents.Length);
        foreach (var parent in parents)
        {
            var reverse = new ReverseRoomTransferForm { Id = parent.Id, Reason = "Local exact return" };
            Assert.Null(await dashboard.ReverseRoomTransferAsync(reverse, default));
            saved = await f.Snapshot();
            Assert.Null(await dashboard.ReverseRoomTransferAsync(reverse, default));
            Assert.Equal(saved, await f.Snapshot());
        }
        Assert.Equal(38, await f.Physical());
        Assert.Equal(0, await f.Physical(9003));
        Assert.Equal(2, await db.RoomTransfers.CountAsync(x => x.IsReversed));
        Assert.Equal(2, await db.TreatmentLineageMovements.CountAsync(x => x.ReversesTreatmentLineageMovementId != null));
        Assert.Equal(38, await db.TreatmentLineageSegments.Where(x => x.Disposition == "Current").SumAsync(x => x.CurrentBins));
    }
}
