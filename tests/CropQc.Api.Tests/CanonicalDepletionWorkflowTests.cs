using CropQc.Data.Inventory;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class CanonicalDepletionWorkflowTests
{
    [InventoryPostgresFact]
    public async Task Exact_receipt_depletion_refuses_overdraw_and_reverses_without_reviving_history()
    {
        await using var f = await Fixture.Create();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var dashboard = CanonicalReceivingWorkflowTests.Dashboard(db, new InventoryCommandExecutor(factory));
        var page = await dashboard.GetRoomDetailAsync(9002, default);
        Assert.Null(page.DataWarning);
        var option = Assert.Single(page.DepletionReceiptOptions);
        Assert.Equal(19, option.CurrentBins);
        var form = new RoomDepletionForm
        {
            RoomId = 9002,
            ReceiptId = option.ReceiptId,
            BinCount = 20,
            TreatmentSignature = option.TreatmentSignature,
            CanonicalFingerprint = option.CanonicalFingerprint,
            ConfirmOverDepletion = true,
            Destination = "Local line",
            Notes = "Local exact receipt"
        };
        var before = await f.Snapshot();
        Assert.NotNull(await dashboard.CreateRoomDepletionAsync(form, default));
        Assert.Equal(before, await f.Snapshot());
        form.BinCount = 19;
        Assert.Null(await dashboard.CreateRoomDepletionAsync(form, default));
        Assert.Equal(0, await f.Physical());
        var parent = await db.RoomDepletions.AsNoTracking().SingleAsync();
        Assert.Equal(form.Destination, parent.Destination);
        var saved = await f.Snapshot();
        Assert.Null(await dashboard.CreateRoomDepletionAsync(form, default)); Assert.Equal(saved, await f.Snapshot());
        var reversal = new VoidRoomDepletionForm { RoomId = 9002, DepletionId = parent.Id, Reason = "Local reverse" };
        Assert.Null(await dashboard.VoidRoomDepletionAsync(reversal, default));
        Assert.Equal(19, await f.Physical());
        saved = await f.Snapshot();
        Assert.Null(await dashboard.VoidRoomDepletionAsync(reversal, default)); Assert.Equal(saved, await f.Snapshot());
        Assert.True((await db.RoomDepletions.AsNoTracking().SingleAsync()).IsVoided);
        Assert.Equal(2, await db.BinsRunEntries.CountAsync());
        Assert.Equal(19, await db.TreatmentLineageSegments.Where(x => x.Disposition == "Current").SumAsync(x => x.CurrentBins));
        Assert.Equal(29, await db.TreatmentLineageSegments.Where(x => x.Disposition == "Historical" && x.Id == 100000).SumAsync(x => x.RetiredQuantity ?? 0));
    }
}
