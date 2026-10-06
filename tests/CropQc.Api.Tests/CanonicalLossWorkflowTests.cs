using CropQc.Data.Inventory;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class CanonicalLossWorkflowTests
{
    [InventoryPostgresFact]
    public async Task Loss_and_restoration_use_canonical_stock_and_do_not_revive_historical_excess()
    {
        await using var f = await Fixture.Create();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        var service = new RoomInventoryLossService(db, new CropQc.Web.Services.RoomInventoryLedgerQueryService(db),
            new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance), new CanonicalOutsideWorkflowTests.Access(),
            new CanonicalGrowerService(db), CanonicalReceivingWorkflowTests.Operator(), new PacificBusinessTimeService(new SystemClock()),
            NullLogger<RoomInventoryLossService>.Instance, canonicalCommands: executor);
        var option = Assert.Single((await service.GetRoomDataAsync(9002, default)).Options);
        Assert.Equal(19, option.CurrentBins);
        var form = new RoomInventoryLossForm
        {
            RoomId = 9002,
            InventoryAdjustmentId = option.InventoryAdjustmentId,
            ExpectedCurrentBins = 19,
            TreatmentSignature = option.TreatmentSignature,
            CanonicalFingerprint = option.CanonicalFingerprint!,
            BinCount = 19
        };
        Assert.Null(await service.CreateAsync(form, default));
        Assert.Equal(0, await f.Physical());
        var saved = await f.Snapshot();
        Assert.Null(await service.CreateAsync(form, default));
        Assert.Equal(saved, await f.Snapshot());
        var parent = Assert.Single(await db.RoomInventoryLosses.AsNoTracking().ToArrayAsync());
        var reverse = new ReverseRoomInventoryLossForm { Id = parent.Id, Reason = "Local exact restoration" };
        Assert.Null(await service.ReverseAsync(reverse, default));
        Assert.Equal(19, await f.Physical());
        saved = await f.Snapshot();
        Assert.Null(await service.ReverseAsync(reverse, default));
        Assert.Equal(saved, await f.Snapshot());
        Assert.Equal(29, await db.TreatmentLineageSegments.Where(x => x.Disposition == "Historical").SumAsync(x => x.RetiredQuantity ?? 0));
        Assert.Equal(19, await db.TreatmentLineageSegments.Where(x => x.Disposition == "Current").SumAsync(x => x.CurrentBins));
    }
}
