using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class CanonicalLegacyRunWorkflowTests
{
    [InventoryPostgresFact]
    public async Task Legacy_create_correct_reverse_preserve_original_entries_and_conserve_inventory()
    {
        await using var f = await Fixture.Create();
        await using (var seed = f.CreateDbContext())
        {
            var actor = await seed.Users.SingleAsync(x => x.Id == 8000);
            actor.EmploymentFacility = "WP"; actor.EmploymentEffectiveAt = DateTimeOffset.UtcNow.AddDays(-1);
            await seed.SaveChangesAsync();
        }
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var service = new BinsRunService(db, new CanonicalOutsideWorkflowTests.Access(), NullLogger<BinsRunService>.Instance,
            canonicalCommands: new InventoryCommandExecutor(factory));
        var actorPrincipal = CanonicalReceivingWorkflowTests.Operator().HttpContext!.User;
        var option = Assert.Single((await service.GetPageAsync(new() { Section = "Actual", WarehouseId = 9001, RoomIds = [9002] }, actorPrincipal, default)).AvailableInventory);
        var form = new BinsRunForm
        {
            WarehouseId = 9001,
            RoomId = 9002,
            InventoryKey = option.InventoryKey,
            TreatmentSignature = option.TreatmentSignature,
            CanonicalFingerprint = option.CanonicalFingerprint,
            ExpectedAvailableBins = 19,
            BinsRun = 19,
            Notes = "Local legacy run"
        };
        Assert.Null(await service.CreateAsync(form, actorPrincipal, default));
        Assert.Equal(0, await f.Physical());
        var old = await db.BinsRunEntries.AsNoTracking().SingleAsync();
        var saved = await f.Snapshot();
        Assert.Null(await service.CreateAsync(form, actorPrincipal, default)); Assert.Equal(saved, await f.Snapshot());
        var current = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow);
        var available = (await new InventoryRunCorrectionAvailability(db).ReadLegacyAsync(current, old.Id, default)).Values.Single();
        Assert.Null(available.Blocker); Assert.Equal(19, available.AvailableAfterOwnReversal.Sum(x => x.Quantity));
        form.OperationKey = Guid.NewGuid().ToString("N"); form.CanonicalFingerprint = current.Positions.Single().Watermark.Fingerprint; form.BinsRun = 17;
        Assert.Null(await service.UpdateAsync(old.Id, form, actorPrincipal, default));
        Assert.Equal(2, await f.Physical());
        var replacement = await db.BinsRunEntries.AsNoTracking().SingleAsync(x => !x.IsReversed && x.ReversesBinsRunEntryId == null);
        Assert.Equal(17, replacement.BinsRun); Assert.Equal(19, (await db.BinsRunEntries.AsNoTracking().SingleAsync(x => x.Id == old.Id)).BinsRun);
        saved = await f.Snapshot();
        Assert.Null(await service.UpdateAsync(old.Id, form, actorPrincipal, default)); Assert.Equal(saved, await f.Snapshot());
        Assert.Null(await service.ReverseAsync(new() { Id = replacement.Id, Reason = "Local final reversal" }, actorPrincipal, default));
        Assert.Equal(19, await f.Physical());
        Assert.Equal(4, await db.BinsRunEntries.CountAsync()); Assert.Equal(0, await db.ActualRuns.CountAsync());
        Assert.Equal(19, await db.TreatmentLineageSegments.Where(x => x.Disposition == "Current").SumAsync(x => x.CurrentBins));
    }
}
