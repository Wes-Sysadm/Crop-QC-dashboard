using System.Security.Claims;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class CanonicalOutsideWorkflowTests
{
    [InventoryPostgresFact]
    public async Task Outside_normal_workflow_and_return_conserve_stock_and_match_dump_and_processor_selectors()
    {
        await using var f = await Fixture.Create();
        await using (var seed = f.CreateDbContext())
        {
            seed.OutsideWarehouses.Add(new() { Id = 9005, Code = "OUTTEST", Name = "Outside test", Address = "Test address" });
            await seed.SaveChangesAsync();
        }
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, "canonical-test@example.invalid")], "test"));
        var access = new Access();
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };
        var time = new PacificBusinessTimeService(new Clock());
        var ledger = new CropQc.Web.Services.RoomInventoryLedgerQueryService(db);
        var treatment = new RoomTreatmentService(db, ledger, access, accessor, time, NullLogger<RoomTreatmentService>.Instance);
        var invariant = new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance);
        var executor = new InventoryCommandExecutor(factory);
        var outside = new OutsideWarehouseTransferService(db, ledger, treatment, treatment, invariant, access, accessor, time, executor);
        var before = await f.Snapshot();
        var option = Assert.Single(await outside.GetInventoryAsync(default));
        var processor = new ProcessorShipmentService(db, ledger, treatment, treatment, invariant, access, accessor, time);
        var processorOption = Assert.Single((await processor.GetPageAsync(null, false, null, null, null, null, default)).Inventory);
        var dump = new BinsRunService(db, access, NullLogger<BinsRunService>.Instance, roomTreatmentService: treatment);
        var dumpOption = Assert.Single((await dump.GetPageAsync(new() { Section = "Actual", WarehouseId = 9001, RoomIds = [9002] }, principal, default)).AvailableInventory);
        Assert.Equal(19, option.AvailableBins); Assert.Equal(19, processorOption.AvailableBins); Assert.Equal(19, dumpOption.CurrentBins);
        Assert.Equal(option.TreatmentSignature, processorOption.TreatmentSignature); Assert.Equal(option.TreatmentSignature, dumpOption.TreatmentSignature);
        Assert.Equal(before, await f.Snapshot());
        var form = new OutsideWarehouseTransferForm
        {
            SourceKey = option.SourceKey,
            ExpectedAvailableBins = 19,
            BinCount = 19,
            OutsideWarehouseId = 9005,
            TransferredAt = time.ToPacific(DateTimeOffset.UtcNow.AddMinutes(-1)).DateTime,
            ConfirmedReview = true,
            TruckLoadBolNumber = "REAL-ADAPTER-TEST",
            Notes = "Local test"
        };
        var result = await outside.CreateAsync(form, default);
        Assert.True(result.Success, result.Error);
        Assert.Equal(0, await f.Physical());
        var parent = await db.OutsideWarehouseTransfers.AsNoTracking().SingleAsync(x => x.Id == result.TransferId);
        Assert.Equal(form.TruckLoadBolNumber, parent.TruckLoadBolNumber); Assert.Equal(form.Notes, parent.Notes);
        var fingerprint = await f.Snapshot();
        Assert.True((await outside.CreateAsync(form, default)).AlreadyApplied);
        Assert.Equal(fingerprint, await f.Snapshot());
        Assert.Null(await outside.ReverseAsync(new() { TransferId = result.TransferId!.Value, Reason = "Local authorized return" }, default));
        Assert.Equal(19, await f.Physical());
        Assert.Equal(19, await db.TreatmentLineageSegments.Where(x => x.Disposition == "Current").SumAsync(x => x.CurrentBins));
        Assert.Equal(29, await db.TreatmentLineageSegments.Where(x => x.Disposition == "Historical").SumAsync(x => x.RetiredQuantity ?? 0));
    }

    [InventoryPostgresFact]
    public async Task Raw_sql_and_execute_update_cannot_bypass_global_write_boundary()
    {
        await using var f = await Fixture.Create();
        await using var db = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection).CreateDbContext();
        var before = await f.Snapshot();
        await Assert.ThrowsAsync<InventoryWriterNotMigratedException>(() => db.TreatmentLineageSegments.ExecuteUpdateAsync(s => s.SetProperty(x => x.CurrentBins, 999)));
        await Assert.ThrowsAsync<InventoryWriterNotMigratedException>(() => db.Database.ExecuteSqlRawAsync("UPDATE \"TreatmentLineageSegments\" SET \"CurrentBins\"=999"));
        Assert.Equal(before, await f.Snapshot());
    }

    internal sealed class Access : IUserAccessService
    {
        public Task<bool> HasAccessAsync(ClaimsPrincipal principal, string areaKey, PageAccessLevel minimumLevel, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<PageAccessLevel> GetAccessLevelAsync(string? email, string areaKey, CancellationToken cancellationToken) => Task.FromResult(PageAccessLevel.Admin);
        public void InvalidateAll() { }
    }
    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
}
