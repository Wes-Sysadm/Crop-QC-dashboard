using System.Security.Claims;
using CropQc.Data;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class CanonicalActualRunWorkflowTests
{
    [InventoryPostgresFact]
    public async Task Normal_run_metadata_edit_keeps_consumption_revision_and_inventory_unchanged()
    {
        await using var f = await Fixture.Create();
        await using (var seed = f.CreateDbContext())
        {
            var actor = await seed.Users.SingleAsync(x => x.Id == 8000);
            actor.EmploymentFacility = "WP"; actor.EmploymentEffectiveAt = DateTimeOffset.UtcNow.AddDays(-1);
            await seed.SaveChangesAsync();
        }
        var factory = new EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var service = new BinsRunService(db, new CanonicalOutsideWorkflowTests.Access(), NullLogger<BinsRunService>.Instance,
            canonicalCommands: new InventoryCommandExecutor(factory, runExpectations: new CanonicalRunExpectationWriter()));
        var principal = CanonicalReceivingWorkflowTests.Operator().HttpContext!.User;
        var option = Assert.Single((await service.GetPageAsync(new() { Section = "Actual", WarehouseId = 9001, RoomIds = [9002] }, principal, default)).AvailableInventory);
        var form = new ActualRunForm
        {
            RunFacilityWarehouseId = 9001,
            RunAt = DateTimeOffset.UtcNow,
            SalesDeskId = await db.SalesDesks.Where(x => x.IsActive).Select(x => x.Id).FirstAsync(),
            Lines = [new() { InventoryKey = option.InventoryKey, TreatmentSignature = option.TreatmentSignature,
                CanonicalFingerprint = option.CanonicalFingerprint, ExpectedAvailableBins = 19, BinsRun = 7 }]
        };
        Assert.Null(await service.CreateActualRunAsync(form, principal, default));
        var run = await db.ActualRuns.AsNoTracking().SingleAsync();
        var protectedRows = new Dictionary<string, string> { ["ActualRuns"] = "false", ["ActualRunDetailCorrections"] = "false", ["AuditLogs"] = "false" };
        var before = await f.Snapshot(protectedRows);
        form.OperationKey = Guid.NewGuid().ToString("N"); form.Id = run.Id; form.ConcurrencyVersion = run.ConcurrencyVersion;
        form.Notes = "Local metadata-only correction"; form.CorrectionReason = "Correct operator notes";
        Assert.Null(await service.UpdateActualRunAsync(run.Id, form, principal, default));
        Assert.Equal(before, await f.Snapshot(protectedRows));
        Assert.Equal(12, await f.Physical()); Assert.Equal(1, await db.ActualRunDetailCorrections.CountAsync());
        var saved = await f.Snapshot(); Assert.Null(await service.UpdateActualRunAsync(run.Id, form, principal, default)); Assert.Equal(saved, await f.Snapshot());
        form.Notes = "Different replay payload";
        Assert.NotNull(await service.UpdateActualRunAsync(run.Id, form, principal, default)); Assert.Equal(saved, await f.Snapshot());
    }

    [InventoryCommandRestoreFact]
    public async Task Normal_ActualRun_selector_and_submission_consume_68_plus_104_atomically()
    {
        await using var cleanup = await CanonicalRestoreFixture.Clone();
        var factory = new EnabledFactory(cleanup.Connection);
        await using var db = factory.CreateDbContext();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, ApplicationAreas.OwnerEmail)], "test"));
        var access = new UserAccessService(db, new ConfigurationBuilder().Build());
        var service = new BinsRunService(db, access, NullLogger<BinsRunService>.Instance,
            canonicalCommands: new InventoryCommandExecutor(factory, runExpectations: new CanonicalRunExpectationWriter()));
        var beforeRead = await cleanup.Snapshot();
        var page = await service.GetPageAsync(new() { Section = "Actual", WarehouseId = 4, RoomIds = [1, 4] }, principal, default);
        Assert.Equal(beforeRead, await cleanup.Snapshot());
        var sources = page.AvailableInventory.Where(x => x.Lot == "1372" && x.FruitProfileId == 17 && x.GrowerLotId == 448).OrderBy(x => x.RoomId).ToArray();
        Assert.Equal(new[] { 68, 1122 }, sources.Select(x => x.CurrentBins));
        Assert.All(sources, x => { Assert.True(x.IsAvailable, x.UnavailableReason); Assert.NotEmpty(x.CanonicalFingerprint); });
        var form = new ActualRunForm
        {
            RunFacilityWarehouseId = 4,
            SalesDeskId = await db.SalesDesks.Where(x => x.IsActive).Select(x => x.Id).FirstAsync(),
            RunAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            Notes = "Disposable Phase 3 workflow acceptance",
            Lines = sources.Select(x => new ActualRunLineForm
            {
                InventoryKey = x.InventoryKey,
                TreatmentSignature = x.TreatmentSignature,
                CanonicalFingerprint = x.CanonicalFingerprint,
                ExpectedAvailableBins = x.CurrentBins,
                BinsRun = x.RoomId == 1 ? 68 : 104
            }).ToList()
        };
        var originalRows = await CanonicalRestoreFixture.ExistingRows(cleanup);
        originalRows["TreatmentLineageSegments"] = "NOT (\"RoomId\" IN (1,4) AND \"FruitProfileId\"=17 AND \"GrowerLotId\"=448 AND \"CropYear\"=2026 AND \"LotNumberSnapshot\"='1372')";
        var protectedBefore = await cleanup.Snapshot(originalRows);
        var proof = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(4, [1, 4]), new(), DateTimeOffset.UtcNow);
        var wp7 = Assert.Single(proof.Positions.Where(x => x.Location.RoomId == 4 && x.Identity.Lot == "1372" && x.Identity.FruitProfileId == 17));
        Assert.Equal(1568, wp7.RawProjectionQuantity);
        Assert.Equal(324, wp7.HistoricalProjections.Where(x => x.Reason == ProjectionExclusionReason.DuplicateStatusAlias).Sum(x => x.ExcludedQuantity));
        Assert.Equal(122, wp7.HistoricalProjections.Where(x => x.Reason == ProjectionExclusionReason.ConsumedHistoricalRepresentation).Sum(x => x.ExcludedQuantity));
        var beforeWrite = await cleanup.Snapshot();
        foreach (var failAt in new[] { "Normalized", "Source1", "PersistedSource1", "Source2", "PersistedSource2", "Movement", "OperationAudit", "BeforeCommit" })
        {
            var failing = new BinsRunService(db, access, NullLogger<BinsRunService>.Instance,
                canonicalCommands: new InventoryCommandExecutor(factory, new Observer((stage, _) => stage == failAt
                    ? Task.FromException(new IOException(failAt)) : Task.CompletedTask), new CanonicalRunExpectationWriter()));
            await Assert.ThrowsAsync<IOException>(() => failing.CreateActualRunAsync(form, principal, default));
            Assert.Equal(beforeWrite, await cleanup.Snapshot());
        }
        Assert.Null(await service.CreateActualRunAsync(form, principal, default));
        Assert.Equal(protectedBefore, await cleanup.Snapshot(originalRows));
        var normalization = await db.AuditLogs.AsNoTracking().Where(x => x.Action == "CanonicalInventoryNormalization" && x.EntityKey == wp7.PositionKey).OrderByDescending(x => x.Id).FirstAsync();
        var plan = System.Text.Json.JsonSerializer.Deserialize<InventoryNormalizationPlan>(normalization.AfterValuesJson!, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        Assert.Equal(1122, plan.ReplacementQuantity);
        Assert.Equal(1568, plan.Changes.Sum(x => x.BeforeQuantity));
        foreach (var change in plan.Changes)
        {
            var retained = await db.TreatmentLineageSegments.AsNoTracking().SingleAsync(x => x.Id == change.Id);
            Assert.Equal("Historical", retained.Disposition); Assert.Equal(change.BeforeQuantity, retained.RetiredQuantity); Assert.Equal(0, retained.CurrentBins);
        }
        var revision = await db.ActualRunRevisions.AsNoTracking().SingleAsync(x => x.OperationKey == form.OperationKey);
        Assert.Equal(172, await db.BinsRunEntries.Where(x => x.ActualRunRevisionId == revision.Id).SumAsync(x => x.BinsRun));
        Assert.Equal(1, await db.RunExpectations.CountAsync(x => x.ActualRunRevisionId == revision.Id));
        var after = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(4, [1, 4]), new(), DateTimeOffset.UtcNow);
        Assert.Equal(new[] { 0, 1018 }, after.Positions.Where(x => x.Identity.Lot == "1372" && x.Identity.FruitProfileId == 17)
            .OrderBy(x => x.Location.RoomId).Select(x => x.AuthoritativeQuantity));
        var fingerprint = await cleanup.Snapshot();
        Assert.Null(await service.CreateActualRunAsync(form, principal, default));
        Assert.Equal(fingerprint, await cleanup.Snapshot());
        form.Lines[0].BinsRun++;
        Assert.NotNull(await service.CreateActualRunAsync(form, principal, default));
        Assert.Equal(fingerprint, await cleanup.Snapshot());
        var run = await db.ActualRuns.AsNoTracking().SingleAsync(x => x.Id == revision.ActualRunId);
        var editPage = await service.GetPageAsync(new() { Section = "Actual", EditActualRunId = run.Id }, principal, default);
        var editSources = editPage.AvailableInventory.Where(x => x.Lot == "1372" && x.FruitProfileId == 17 && x.GrowerLotId == 448).OrderBy(x => x.RoomId).ToArray();
        Assert.Equal(new[] { 68, 1122 }, editSources.Select(x => x.CurrentBins));
        var edit = new ActualRunForm
        {
            Id = run.Id,
            ConcurrencyVersion = run.ConcurrencyVersion,
            RunFacilityWarehouseId = 4,
            SalesDeskId = form.SalesDeskId,
            RunAt = form.RunAt,
            CorrectionReason = "Local corrected run: 170 bins",
            Lines = editSources.Select(x => new ActualRunLineForm
            {
                InventoryKey = x.InventoryKey,
                TreatmentSignature = x.TreatmentSignature,
                CanonicalFingerprint = x.CanonicalFingerprint,
                ExpectedAvailableBins = x.CurrentBins,
                BinsRun = x.RoomId == 1 ? 68 : 102
            }).ToList()
        };
        Assert.Null(await service.UpdateActualRunAsync(run.Id, edit, principal, default));
        after = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(4, [1, 4]), new(), DateTimeOffset.UtcNow);
        Assert.Equal(new[] { 0, 1020 }, after.Positions.Where(x => x.Identity.Lot == "1372" && x.Identity.FruitProfileId == 17)
            .OrderBy(x => x.Location.RoomId).Select(x => x.AuthoritativeQuantity));
        fingerprint = await cleanup.Snapshot();
        Assert.Null(await service.UpdateActualRunAsync(run.Id, edit, principal, default));
        Assert.Equal(fingerprint, await cleanup.Snapshot());
        edit.Lines[1].BinsRun++;
        Assert.NotNull(await service.UpdateActualRunAsync(run.Id, edit, principal, default));
        Assert.Equal(fingerprint, await cleanup.Snapshot());
        run = await db.ActualRuns.AsNoTracking().SingleAsync(x => x.Id == revision.ActualRunId);
        var cancel = new CancelActualRunForm { Id = run.Id, ConcurrencyVersion = run.ConcurrencyVersion, Reason = "Local acceptance reversal" };
        Assert.Null(await service.CancelActualRunAsync(cancel, principal, default));
        after = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(4, [1, 4]), new(), DateTimeOffset.UtcNow);
        Assert.Equal(new[] { 68, 1122 }, after.Positions.Where(x => x.Identity.Lot == "1372" && x.Identity.FruitProfileId == 17)
            .OrderBy(x => x.Location.RoomId).Select(x => x.AuthoritativeQuantity));
        Assert.Equal(4, await db.BinsRunEntries.CountAsync(x => x.ActualRunId == run.Id && x.ReversesBinsRunEntryId != null));
        Assert.Equal(1, await db.RunExpectations.CountAsync(x => x.ActualRunRevisionId == revision.Id));
        fingerprint = await cleanup.Snapshot();
        Assert.Null(await service.CancelActualRunAsync(cancel, principal, default));
        Assert.Equal(fingerprint, await cleanup.Snapshot());
    }

    internal sealed class EnabledFactory(string connection) : IDbContextFactory<CropQcDbContext>
    {
        public CropQcDbContext CreateDbContext() => new(new DbContextOptionsBuilder<CropQcDbContext>().UseNpgsql(connection).Options, new(true));
    }
}
