using System.Security.Claims;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Controllers;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class OrasDefinitionTests
{
    [InventoryPostgresFact]
    public async Task Corrected_Organic_definition_preserves_existing_treatment_evidence_and_normal_reversal()
    {
        await using var f = await HistoricalOras();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        db.UserRoles.Add(new() { UserId = 8000, RoleId = await db.Roles.Where(x => x.Name == "Admin").Select(x => x.Id).SingleAsync() });
        await db.SaveChangesAsync();
        var executor = new InventoryCommandExecutor(factory);
        var service = new RoomTreatmentService(db, new CropQc.Web.Services.RoomInventoryLedgerQueryService(db), new CanonicalOutsideWorkflowTests.Access(),
            CanonicalReceivingWorkflowTests.Operator(), new CropQc.Shared.Time.PacificBusinessTimeService(new CropQc.Shared.Time.SystemClock()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RoomTreatmentService>.Instance, executor);
        var form = new RoomTreatmentApplyForm
        {
            RoomId = 9002,
            AppliedAt = DateTimeOffset.UtcNow,
            TreatmentChemicalId = await db.TreatmentChemicals.Where(x => x.IsActive && x.ApplicationLevel == "Room" && x.Crop == "Pears").Select(x => x.Id).FirstAsync()
        };
        Assert.Null((await service.GetApplyPageAsync(form, true, default)).Error);
        form.ConfirmedReview = true;
        var applied = await service.ApplyAsync(form, default); Assert.Null(applied.Error);
        var app = applied.ApplicationId!.Value;
        var protectedBefore = JsonSerializer.Serialize(await db.RoomTreatmentApplications.AsNoTracking().Select(x => new { x.Id, x.TreatmentChemicalId, x.TotalBinsSnapshot, x.AppliedAt }).ToArrayAsync());
        var preview = await OrasDefinitionCorrection.PreviewAsync(db, 9004);
        var correction = await executor.ExecuteAsync(new("oras:" + Guid.NewGuid().ToString("N"), InventoryCommandKind.CorrectOrasDefinition,
            8000, DateTimeOffset.UtcNow, "Correct identity while preserving historical treatment", [], ProductDefinition: new(9004, preview.Fingerprint)));
        Assert.True(correction.Status == InventoryCommandStatus.Committed, correction.Detail);
        db.ChangeTracker.Clear();
        Assert.Equal(protectedBefore, JsonSerializer.Serialize(await db.RoomTreatmentApplications.AsNoTracking().Select(x => new { x.Id, x.TreatmentChemicalId, x.TotalBinsSnapshot, x.AppliedAt }).ToArrayAsync()));
        var source = Assert.Single(await db.RoomTreatmentApplicationSources.ToListAsync());
        Assert.Equal(app, source.RoomTreatmentApplicationId); Assert.Equal(19, source.BinsTreated);
        Assert.True(source.IsOrganicSnapshot); Assert.Equal("Organic", source.ProductionTypeSnapshot);
        var position = Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow)).Positions);
        Assert.Equal(19, position.AvailableQuantity); Assert.True(position.IsOperable); Assert.True(position.Identity.IsOrganic);
        Assert.Contains(app, Assert.Single(position.TreatmentSlices).ApplicationIds);
        Assert.Null(await service.ReverseAsync(new() { Id = app, Reason = "Normal reversal after classification correction" }, default));
        Assert.Equal(19, await f.Physical());
    }

    [Fact]
    public void Existing_command_serialization_does_not_gain_a_null_field_that_invalidates_prior_idempotency_hashes()
    {
        var command = new InventoryCommand("existing", InventoryCommandKind.ReceiveStock, 1,
            DateTimeOffset.UnixEpoch, "Existing receipt", [], Receipt: new(2026, 1, 2, 3, 4, "TR", 10));
        var json = JsonSerializer.Serialize(command, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.DoesNotContain("productDefinition", json);
        Assert.Equal(json, JsonSerializer.Serialize(JsonSerializer.Deserialize<InventoryCommand>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web)), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }
    [InventoryPostgresFact]
    public async Task Historical_move_can_be_reversed_after_definition_correction_and_Truck_Receipt_still_completes_and_reopens()
    {
        await using var f = await HistoricalOras();
        await using (var setup = f.CreateDbContext())
        {
            setup.UserRoles.Add(new() { UserId = 8000, RoleId = await setup.Roles.Where(x => x.Name == "Admin").Select(x => x.Id).SingleAsync() });
            await setup.SaveChangesAsync();
        }
        var moved = await f.Execute(await f.Command(InventoryCommandKind.RoomMove, 5));
        Assert.True(moved.Status == InventoryCommandStatus.Committed, moved.Detail);
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        var preview = await OrasDefinitionCorrection.PreviewAsync(db, 9004);
        var corrected = await executor.ExecuteAsync(new("oras:" + Guid.NewGuid().ToString("N"), InventoryCommandKind.CorrectOrasDefinition,
            8000, DateTimeOffset.UtcNow, "Correct definition including historical transfer", [], ProductDefinition: new(9004, preview.Fingerprint)));
        Assert.True(corrected.Status == InventoryCommandStatus.Committed, corrected.Detail);
        Assert.Equal(14, await f.Physical()); Assert.Equal(5, await f.Physical(9003));
        db.ChangeTracker.Clear();
        var dashboard = CanonicalReceivingWorkflowTests.Dashboard(db, executor);
        Assert.Null(await dashboard.ReverseRoomTransferAsync(new() { Id = moved.Effects[0].ParentId!.Value, Reason = "Return corrected historical stock" }, default));
        Assert.Equal(19, await f.Physical()); Assert.Equal(0, await f.Physical(9003));
        var receiving = await f.ReceiveCommand();
        var transferId = receiving.Lines[0].Source.Location.CustodyRecordId!.Value;
        var receiptId = receiving.ReceivingEvidence!.ReceiptId;
        db.ChangeTracker.Clear();
        var truck = CanonicalTruckReceiptWorkflowTests.Service(db, executor);
        async Task<TruckReceiptActionForm> Form() => new()
        {
            TransferId = transferId,
            ReceiptId = receiptId,
            Reason = "Local ORAS Truck Receipt verification",
            TransferVersion = await db.InterCrewTransfers.Where(x => x.Id == transferId).Select(x => x.ConcurrencyVersion).SingleAsync(),
            ReceiptVersion = await db.Receipts.Where(x => x.Id == receiptId).Select(x => x.ConcurrencyVersion).SingleAsync()
        };
        Assert.Null(await truck.CompleteAsync(await Form(), default));
        var position = Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db))
            .ResolveAsync(new(9006, [9007]), new(), DateTimeOffset.UtcNow)).Positions);
        Assert.Equal(19, position.AuthoritativeQuantity); Assert.True(position.Identity.IsOrganic); Assert.True(position.IsOperable);
        Assert.Null(await truck.ReopenAsync(await Form(), default));
        Assert.Equal(19, (await truck.ActiveAllocationsAsync(transferId, default)).Sum(x => x.Bins));
        Assert.All(await db.TreatmentLineageMovements.AsNoTracking().ToListAsync(), x => Assert.Contains("|ORAS|ORGANIC|True|", x.IdentityKey));
    }

    [InventoryCommandRestoreFact]
    public async Task Normal_admin_workflow_corrects_current_and_consumed_history_on_verified_restore_without_physical_changes()
    {
        await using var f = await CanonicalRestoreFixture.Clone();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var oldAuditIds = await db.AuditLogs.Select(x => x.Id).ToArrayAsync();
        var expectedChanged = new Dictionary<string, string>
        {
            ["FruitProfiles"] = "\"Id\" <> 30",
            ["BinsRunEntries"] = "\"Id\" NOT IN (402,434)",
            ["RunExpectationSources"] = "\"Id\" NOT IN (326,358)",
            ["TreatmentLineageSegments"] = "\"Id\" <> 649",
            ["TreatmentLineageMovements"] = "\"Id\" NOT IN (664,749)",
            ["AuditLogs"] = $"\"Id\" <= {oldAuditIds.Max()}",
            ["InventoryCommands"] = "false"
        };
        var protectedBefore = await f.Snapshot(expectedChanged);
        var columnsBefore = await PreservedColumns(f.Connection);
        var controller = Controller(factory, new InventoryCommandExecutor(factory), ApplicationAreas.OwnerEmail);
        var beforePreview = await f.Snapshot();
        var form = Assert.IsType<OrasDefinitionForm>(Assert.IsType<ViewResult>(await controller.Index(30, default)).Model);
        Assert.True(controller.ModelState.IsValid, string.Join(";", controller.ModelState.Values.SelectMany(x => x.Errors).Select(x => x.ErrorMessage)));
        Assert.Equal(beforePreview, await f.Snapshot());
        Assert.Equal(288, form.Preview!.LedgerNetBins);
        Assert.Equal(362, form.Preview.ReceivedBins);
        Assert.Equal(8, form.Preview.Changes.Select(x => (x.Entity, x.Id)).Distinct().Count());
        form.Reason = "ORAS always meant Organic Asian Pear; correct original classification including history.";
        form.ConfirmHistory = true;
        Assert.IsType<RedirectToActionResult>(await controller.Correct(30, form, default));
        Assert.Null(controller.TempData["Error"]);
        Assert.NotNull(controller.TempData["Success"]);
        Assert.Equal(protectedBefore, await f.Snapshot(expectedChanged));
        Assert.Equal(columnsBefore, await PreservedColumns(f.Connection));
        Assert.Equal(1, await db.InventoryCommands.CountAsync());
        Assert.Equal(8, await db.AuditLogs.CountAsync(x => x.Action == "OrasHistoricalDefinitionCorrection"));
        Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.Action == "CanonicalInventoryCommand"));
        Assert.Equal(0, await db.AuditLogs.CountAsync(x => x.Action == "CanonicalInventoryNormalization"));
        Assert.True((await db.FruitProfiles.SingleAsync(x => x.Id == 30)).IsOrganic);
        Assert.All(await db.BinsRunEntries.Where(x => x.FruitProfileId == 30).ToListAsync(), x =>
        { Assert.True(x.IsOrganicSnapshot); Assert.Equal("Organic", x.ProductionTypeSnapshot); });
        Assert.All(await db.RunExpectationSources.Where(x => x.FruitProfileId == 30).ToListAsync(), x => Assert.True(x.IsOrganicSnapshot));
        Assert.All(await db.TreatmentLineageMovements.Where(x => x.Id == 664 || x.Id == 749).ToListAsync(), x => Assert.Contains("|ORAS|ORGANIC|True|", x.IdentityKey));
        var resolver = new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db));
        foreach (var (warehouse, room, bins) in new[] { (2, 33, 234), (4, 5, 54) })
        {
            var position = Assert.Single((await resolver.ResolveAsync(new(warehouse, [room]), new(), DateTimeOffset.UtcNow))
                .Positions.Where(x => x.Identity.FruitProfileId == 30));
            Assert.Equal(bins, position.AuthoritativeQuantity);
            Assert.Equal(bins, position.AvailableQuantity);
            Assert.True(position.Identity.IsOrganic);
            Assert.Equal("Organic", position.Identity.ProductionType);
            Assert.True(position.IsOperable, string.Join(',', position.Blockers.Select(x => x.Code)));
        }
        var committed = await f.Snapshot();
        await controller.Correct(30, form, default);
        Assert.Equal(committed, await f.Snapshot());
    }

    [InventoryPostgresFact]
    public async Task Correction_is_atomic_stale_safe_and_requires_real_admin_even_when_executor_called_directly()
    {
        await using var f = await HistoricalOras();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var preview = await OrasDefinitionCorrection.PreviewAsync(db, 9004);
        var command = new InventoryCommand("oras:" + Guid.NewGuid().ToString("N"), InventoryCommandKind.CorrectOrasDefinition,
            8000, DateTimeOffset.UtcNow, "Correct original historical classification", [], ProductDefinition: new(9004, preview.Fingerprint));
        var before = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Blocked, (await new InventoryCommandExecutor(factory).ExecuteAsync(command)).Status);
        Assert.Equal(before, await f.Snapshot());
        var denied = Controller(factory, new InventoryCommandExecutor(factory), "canonical-test@example.invalid");
        Assert.IsType<ForbidResult>(await denied.Index(9004, default));
        await using (var seed = f.CreateDbContext())
        {
            seed.UserRoles.Add(new() { UserId = 8000, RoleId = await seed.Roles.Where(x => x.Name == "Admin").Select(x => x.Id).SingleAsync() });
            await seed.SaveChangesAsync();
        }
        before = await f.Snapshot();
        var executor = new InventoryCommandExecutor(factory);
        Assert.Equal(InventoryCommandStatus.Stale, (await executor.ExecuteAsync(command with { ProductDefinition = new(9004, "stale") })).Status);
        Assert.Equal(before, await f.Snapshot());
        var failing = new InventoryCommandExecutor(factory, new Observer((stage, _) => stage == "BeforeCommit"
            ? Task.FromException(new IOException("Disposable rollback test")) : Task.CompletedTask));
        await Assert.ThrowsAsync<IOException>(() => failing.ExecuteAsync(command));
        Assert.Equal(before, await f.Snapshot());
        var saved = await executor.ExecuteAsync(command);
        Assert.True(saved.Status == InventoryCommandStatus.Committed, saved.Detail);
        Assert.Equal(19, await f.Physical());
        Assert.Equal(29, await db.TreatmentLineageSegments.AsNoTracking().Where(x => x.Id == 100000).Select(x => x.CurrentBins).SingleAsync());
        Assert.Equal(0, await db.AuditLogs.CountAsync(x => x.Action == "CanonicalInventoryNormalization"));
        before = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Replayed, (await executor.ExecuteAsync(command)).Status);
        Assert.Equal(before, await f.Snapshot());
        Assert.Equal(InventoryCommandStatus.Conflict, (await executor.ExecuteAsync(command with { Reason = "different intent" })).Status);
        Assert.Equal(before, await f.Snapshot());
    }

    [InventoryPostgresFact]
    public async Task Web_API_and_Truck_receiving_resolve_Organic_ORAS_and_replay_without_stock_duplication()
    {
        await using var f = await HistoricalOras();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using (var setup = f.CreateDbContext())
        {
            setup.UserRoles.Add(new() { UserId = 8000, RoleId = await setup.Roles.Where(x => x.Name == "Admin").Select(x => x.Id).SingleAsync() });
            await setup.SaveChangesAsync();
        }
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        var preview = await OrasDefinitionCorrection.PreviewAsync(db, 9004);
        var result = await executor.ExecuteAsync(new("oras:" + Guid.NewGuid().ToString("N"), InventoryCommandKind.CorrectOrasDefinition,
            8000, DateTimeOffset.UtcNow, "Fix definition", [], ProductDefinition: new(9004, preview.Fingerprint)));
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
        db.ChangeTracker.Clear();
        var http = CanonicalReceivingWorkflowTests.Operator();
        var web = CanonicalReceivingWorkflowTests.Dashboard(db, executor, http, truckReceipts: true);
        var grower = await db.GrowerLots.SingleAsync(x => x.Id == 100000);
        var form = new CreateReceiptForm
        {
            CropYear = 2026,
            ConfirmCropYear = true,
            ReceivedAt = DateTimeOffset.UtcNow,
            CompuTechReceiptId = "ORAS-WEB",
            WarehouseId = 9001,
            RoomId = 9003,
            FruitProfileId = 9004,
            GrowerLotId = 100000,
            GrowerNumber = grower.LotNumber,
            GrowerName = grower.Grower,
            BinCount = 7,
            ReceiptType = "Truck receipt"
        };
        var created = await web.CreateReceiptAsync(form, default);
        Assert.Null(created.Error);
        Assert.NotNull(created.ReceiptId);
        var before = await f.Snapshot();
        Assert.Null((await web.CreateReceiptAsync(form, default)).Error);
        Assert.Equal(before, await f.Snapshot());
        var api = new CropQc.Api.Services.ReceiptService(db, new CropQc.Api.Services.AuditService(db), new(db, executor), http);
        var apiCreated = await api.CreateAsync(new(2026, DateTimeOffset.UtcNow, "ORAS-API", 9001, 9003, 9004,
            grower.Grower, grower.LotNumber, 11, Guid.NewGuid().ToString("N"), 100000), default);
        Assert.Null(apiCreated.Error);
        Assert.Equal(18, await f.Physical(9003));
        var available = Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db))
            .ResolveAsync(new(9001, [9003]), new(), DateTimeOffset.UtcNow)).Positions);
        Assert.True(available.IsOperable);
        Assert.Equal(18, available.AvailableQuantity);
        Assert.True(available.Identity.IsOrganic);
        Assert.Equal("Organic", available.Identity.ProductionType);
    }

    [InventoryPostgresFact]
    public async Task Bad_existing_ORAS_blocks_new_receiving_and_imports_and_normal_master_edit_keeps_its_guard()
    {
        await using var f = await HistoricalOras();
        await using var db = f.CreateDbContext();
        var service = new AdminManagementService(db, new VarietyColorService(db));
        var form = (await service.GetEditFormAsync("fruit-profiles", 9004, default))!;
        form.ProductionType = "Organic";
        var before = await f.Snapshot();
        Assert.NotNull(await service.SaveMasterDataAsync(form, "canonical-test@example.invalid", default));
        Assert.Equal(before, await f.Snapshot());
        var profile = await db.FruitProfiles.SingleAsync(x => x.Id == 9004);
        profile.Name = "Attempt a conventional ORAS definition";
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Equal(OrasProductDefinition.Error, error.Message);
        db.ChangeTracker.Clear();
        var api = new CropQc.Api.Services.ReceiptService(db, new CropQc.Api.Services.AuditService(db));
        var request = new CropQc.Api.Dtos.CreateReceiptRequest(2026, DateTimeOffset.UtcNow, "BAD-ORAS", 9001, 9003,
            9004, "Corpus", "CORPUS-100000", 1);
        Assert.Equal(OrasProductDefinition.Error, (await api.CreateAsync(request, default)).Error);
        db.Receipts.Add(new()
        {
            CropYear = 2026,
            CompuTechReceiptId = "IMPORTED-ORAS",
            WarehouseId = 9001,
            RoomId = 9003,
            FruitProfileId = 9004,
            GrowerName = "Corpus",
            LotCode = "CORPUS-100000",
            BinCount = 1
        });
        Assert.Equal(OrasProductDefinition.Error, (await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync())).Message);
        Assert.Equal(before, await f.Snapshot());
    }

    [InventoryPostgresFact]
    public async Task New_ORAS_master_definition_overrides_conventional_default_and_cannot_revert()
    {
        await using var f = await Fixture.Create();
        await using var db = f.CreateDbContext();
        var service = new AdminManagementService(db, new VarietyColorService(db));
        Assert.Null(await service.SaveMasterDataAsync(new()
        {
            Type = "fruit-profiles",
            Code = "oras",
            Name = "Organic Asian Pear",
            FruitType = "Apple",
            ProductionType = "Conventional",
            IsActive = true
        }, "canonical-test@example.invalid", default));
        var profile = await db.FruitProfiles.SingleAsync(x => x.VarietyCode == "oras");
        Assert.Equal("Pear", profile.FruitType); Assert.Equal("Organic", profile.ProductionType); Assert.True(profile.IsOrganic);
        profile.IsOrganic = false;
        Assert.Equal(OrasProductDefinition.Error, (await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync())).Message);
    }

    private static async Task<Fixture> HistoricalOras()
    {
        var f = await Fixture.Create();
        await using var db = f.CreateDbContext();
        // Reproduce a pre-fix historical defect only in the disposable PostgreSQL fixture.
        await db.Database.ExecuteSqlRawAsync("""
            UPDATE "FruitProfiles" SET "VarietyCode"='ORAS', "Name"='Organic Asian Pear', "FruitType"='Pear' WHERE "Id"=9004;
            UPDATE "RoomInventoryAdjustments" SET "VarietyCode"='ORAS' WHERE "FruitProfileId"=9004;
            UPDATE "TreatmentLineageSegments" SET "VarietyCodeSnapshot"='ORAS', "IdentityKey"=replace("IdentityKey",'CGAL','ORAS') WHERE "FruitProfileId"=9004;
            """);
        return f;
    }

    private static OrasDefinitionController Controller(IDbContextFactory<CropQcDbContext> factory, IInventoryCommandExecutor executor, string email)
    {
        var context = new DefaultHttpContext { User = new(new ClaimsIdentity([new Claim(ClaimTypes.Email, email)], "test")) };
        var db = factory.CreateDbContext();
        return new(factory, executor, new UserAccessService(db, new ConfigurationBuilder().Build()))
        { ControllerContext = new() { HttpContext = context }, TempData = new TempDataDictionary(context, new TempProvider()) };
    }
    private sealed class TempProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
    private static async Task<string> PreservedColumns(string connection)
    {
        await using var db = new NpgsqlConnection(connection); await db.OpenAsync();
        var values = new List<string>();
        foreach (var (table, fields) in new[] {
            ("FruitProfiles", "ProductionType,IsOrganic"), ("BinsRunEntries", "ProductionTypeSnapshot,IsOrganicSnapshot"),
            ("RunExpectationSources", "ProductionTypeSnapshot,IsOrganicSnapshot"),
            ("TreatmentLineageSegments", "ProductionTypeSnapshot,IsOrganicSnapshot,IdentityKey,ConcurrencyVersion"),
            ("TreatmentLineageMovements", "IdentityKey") })
        {
            var excluded = string.Join(',', fields.Split(',').Select(x => "'" + x + "'"));
            await using var command = new NpgsqlCommand($"SELECT md5(string_agg(v::text, '' ORDER BY v::text)) FROM (SELECT to_jsonb(t) - ARRAY[{excluded}] AS v FROM \"{table}\" t) s", db);
            values.Add(table + ":" + await command.ExecuteScalarAsync());
        }
        return JsonSerializer.Serialize(values);
    }
}
