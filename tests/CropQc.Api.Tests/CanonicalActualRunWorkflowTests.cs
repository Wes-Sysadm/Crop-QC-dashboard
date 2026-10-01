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
        var connection = Environment.GetEnvironmentVariable("CANONICAL_INVENTORY_RESTORE_POSTGRES")!;
        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connection);
        var template = new NpgsqlConnectionStringBuilder(connection);
        Assert.True(template.Host is "localhost" or "127.0.0.1");
        var name = $"command_phase3_{Guid.NewGuid():N}_test";
        await using (var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connection) { Database = "postgres" }.ConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{name}\" TEMPLATE \"{template.Database!.Replace("\"", "\"\"")}\"", admin);
            await create.ExecuteNonQueryAsync();
        }
        await using var cleanup = new Fixture(new NpgsqlConnectionStringBuilder(connection) { Database = name }.ConnectionString);
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
        Assert.Null(await service.CreateActualRunAsync(form, principal, default));
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
