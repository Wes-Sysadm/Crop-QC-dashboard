using CropQc.Data.Inventory;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class CanonicalProcessorWorkflowTests
{
    [InventoryPostgresFact]
    public async Task Multi_lot_shipment_is_one_parent_and_reverses_every_line_atomically()
    {
        await using var f = await Fixture.Create(2);
        await using (var seed = f.CreateDbContext())
        {
            seed.Processors.Add(new() { Id = 9005, Name = "Local processor" });
            await seed.SaveChangesAsync();
        }
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        var ledger = new CropQc.Web.Services.RoomInventoryLedgerQueryService(db);
        var http = CanonicalReceivingWorkflowTests.Operator();
        var access = new CanonicalOutsideWorkflowTests.Access();
        var time = new PacificBusinessTimeService(new SystemClock());
        var treatment = new RoomTreatmentService(db, ledger, access, http, time, NullLogger<RoomTreatmentService>.Instance, executor);
        var service = new ProcessorShipmentService(db, ledger, treatment, treatment,
            new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance), access, http, time, executor);
        var inventory = (await service.GetPageAsync(null, false, null, null, null, null, default)).Inventory;
        Assert.Equal(2, inventory.Count);
        var form = new ProcessorShipmentForm
        {
            ProcessorId = 9005,
            SaleRate = 12,
            PricingBasis = "PerBin",
            ShippedAt = time.NowPacific.DateTime,
            ConfirmedReview = true,
            ReferenceNumber = "LOCAL-MULTI",
            Lines = inventory.Select(x => new ProcessorShipmentLineForm { SourceKey = x.SourceKey, ExpectedAvailableBins = 19, BinsSent = 19 }).ToList()
        };
        var result = await service.CreateAsync(form, default);
        Assert.True(result.Success, result.Error);
        Assert.Equal(0, await f.Physical());
        var parent = Assert.Single(await db.ProcessorShipments.Include(x => x.Lines).AsNoTracking().ToListAsync());
        Assert.Equal(result.ShipmentId, parent.Id);
        Assert.Equal(2, parent.Lines.Count);
        Assert.Equal(38, parent.Lines.Sum(x => x.BinsSent));
        var saved = await f.Snapshot();
        Assert.True((await service.CreateAsync(form, default)).AlreadyApplied);
        Assert.Equal(saved, await f.Snapshot());
        var reversal = new ProcessorShipmentReversalForm { ShipmentId = parent.Id, Reason = "Local all-line reversal" };
        Assert.Null(await service.ReverseAsync(reversal, default));
        Assert.Equal(38, await f.Physical());
        saved = await f.Snapshot();
        Assert.Null(await service.ReverseAsync(reversal, default));
        Assert.Equal(saved, await f.Snapshot());
        Assert.Equal(58, await db.TreatmentLineageSegments.Where(x => x.Disposition == "Historical").SumAsync(x => x.RetiredQuantity ?? 0));
    }
}
