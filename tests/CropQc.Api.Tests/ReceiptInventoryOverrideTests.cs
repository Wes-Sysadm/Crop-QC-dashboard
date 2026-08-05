using System.Security.Claims;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace CropQc.Api.Tests;

public sealed class ReceiptInventoryOverrideTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-04T19:00:00Z");

    [Fact]
    public async Task Receipts_admin_reduction_creates_exact_durable_parent_and_is_idempotent()
    {
        await using var fixture = await OverrideFixture.CreateAsync();
        var operationKey = Guid.NewGuid().ToString("D");
        var form = fixture.Form(binCount: 90, operationKey: operationKey);

        var first = await fixture.Service.ApplyEditAsync(form, fixture.AdminPrincipal, CancellationToken.None);
        var second = await fixture.Service.ApplyEditAsync(form, fixture.AdminPrincipal, CancellationToken.None);

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.True(second.WasIdempotent);
        Assert.Equal(first.OverrideId, second.OverrideId);
        var receiptOverride = Assert.Single(await fixture.Db.ReceiptInventoryOverrides.Include(x => x.InventoryAdjustments).ToListAsync());
        var adjustment = Assert.Single(receiptOverride.InventoryAdjustments);
        Assert.Equal(-10, receiptOverride.InventoryDelta);
        Assert.Equal(-10, adjustment.ChangeAmount);
        Assert.Equal(90, adjustment.NewBinCount);
        Assert.Equal(receiptOverride.Id, adjustment.ReceiptInventoryOverrideId);
        Assert.Equal(90, (await fixture.Db.Receipts.FindAsync(OverrideFixture.ReceiptId))!.BinCount);
        Assert.Empty(await fixture.Db.BinsRunEntries.ToListAsync());
        Assert.Empty(await fixture.Db.RoomTransfers.ToListAsync());
        Assert.Equal(2, await fixture.Db.RoomInventoryAdjustments.CountAsync());
        var audit = await fixture.Service.GetAuditDetailAsync(first.OverrideId!.Value, CancellationToken.None);
        Assert.NotNull(audit);
        Assert.Single(audit!.Adjustments);
    }

    [Fact]
    public async Task Unrelated_user_and_receipt_editor_cannot_use_admin_override_endpoint_service()
    {
        await using var fixture = await OverrideFixture.CreateAsync(includeReceiptEditor: true);
        var beforeAdjustments = await fixture.Db.RoomInventoryAdjustments.CountAsync();

        var result = await fixture.Service.ApplyEditAsync(
            fixture.Form(90, Guid.NewGuid().ToString("D")),
            fixture.EditorPrincipal!,
            CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("Receipts Admin", result.Error);
        Assert.Equal(beforeAdjustments, await fixture.Db.RoomInventoryAdjustments.CountAsync());
        Assert.Empty(await fixture.Db.ReceiptInventoryOverrides.ToListAsync());
        Assert.Equal(100, (await fixture.Db.Receipts.FindAsync(OverrideFixture.ReceiptId))!.BinCount);

        var voidResult = await fixture.Service.VoidAsync(new DeleteReceiptForm
        {
            Id = OverrideFixture.ReceiptId,
            Reason = "Crafted request",
            ConfirmationValue = "OVERRIDE-100",
            ConfirmDeletion = true,
            ConfirmInventoryChange = true,
            OperationToken = Guid.NewGuid().ToString("D")
        }, fixture.EditorPrincipal!, CancellationToken.None);
        Assert.False(voidResult.Succeeded);
        Assert.False((await fixture.Db.Receipts.FindAsync(OverrideFixture.ReceiptId))!.IsDeleted);
    }

    [Fact]
    public async Task Reduction_below_consumed_quantity_requires_separate_negative_acknowledgment()
    {
        await using var fixture = await OverrideFixture.CreateAsync(consumedBins: 90);
        var rejected = fixture.Form(80, Guid.NewGuid().ToString("D"));

        var withoutAcknowledgment = await fixture.Service.ApplyEditAsync(rejected, fixture.AdminPrincipal, CancellationToken.None);

        Assert.False(withoutAcknowledgment.Succeeded);
        Assert.Contains("negative inventory", withoutAcknowledgment.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await fixture.Db.ReceiptInventoryOverrides.ToListAsync());
        Assert.Equal(2, await fixture.Db.RoomInventoryAdjustments.CountAsync());

        var accepted = fixture.Form(80, Guid.NewGuid().ToString("D"));
        accepted.AcknowledgeNegativeInventory = true;
        var withAcknowledgment = await fixture.Service.ApplyEditAsync(accepted, fixture.AdminPrincipal, CancellationToken.None);

        Assert.True(withAcknowledgment.Succeeded);
        var receiptOverride = await fixture.Db.ReceiptInventoryOverrides.SingleAsync();
        Assert.True(receiptOverride.NegativeInventoryAcknowledged);
        Assert.Equal(-10, receiptOverride.CurrentInventoryAfter);
        Assert.DoesNotContain(await fixture.Db.RoomInventoryAdjustments.ToListAsync(), x => x.AdjustmentType is "TransferOut" or "ManualTrueUp" or "BinsRun");
    }

    [Fact]
    public async Task Increase_after_reduction_adds_positive_history_without_mutating_prior_override()
    {
        await using var fixture = await OverrideFixture.CreateAsync();
        var reduction = await fixture.Service.ApplyEditAsync(
            fixture.Form(90, Guid.NewGuid().ToString("D")), fixture.AdminPrincipal, CancellationToken.None);
        Assert.True(reduction.Succeeded);
        var prior = await fixture.Db.ReceiptInventoryOverrides.AsNoTracking().SingleAsync();

        var increaseForm = fixture.Form(100, Guid.NewGuid().ToString("D"));
        increaseForm.ExpectedConcurrencyVersion = 1;
        var increase = await fixture.Service.ApplyEditAsync(increaseForm, fixture.AdminPrincipal, CancellationToken.None);

        Assert.True(increase.Succeeded);
        Assert.Equal(2, await fixture.Db.ReceiptInventoryOverrides.CountAsync());
        Assert.Equal(prior.InventoryDelta, (await fixture.Db.ReceiptInventoryOverrides.AsNoTracking().SingleAsync(x => x.Id == prior.Id)).InventoryDelta);
        var positive = await fixture.Db.RoomInventoryAdjustments.SingleAsync(x => x.ReceiptInventoryOverrideId == increase.OverrideId);
        Assert.Equal(10, positive.ChangeAmount);
        Assert.Equal(100, positive.NewBinCount);
    }

    [Fact]
    public async Task Stale_receipt_version_fails_with_zero_partial_writes()
    {
        await using var fixture = await OverrideFixture.CreateAsync();
        var form = fixture.Form(90, Guid.NewGuid().ToString("D"));
        form.ExpectedConcurrencyVersion = 999;

        var result = await fixture.Service.ApplyEditAsync(form, fixture.AdminPrincipal, CancellationToken.None);

        Assert.True(result.IsConflict);
        Assert.Empty(await fixture.Db.ReceiptInventoryOverrides.ToListAsync());
        Assert.Single(await fixture.Db.RoomInventoryAdjustments.ToListAsync());
        Assert.Equal(100, (await fixture.Db.Receipts.FindAsync(OverrideFixture.ReceiptId))!.BinCount);
    }

    [Fact]
    public async Task Reusing_operation_key_for_different_request_is_rejected()
    {
        await using var fixture = await OverrideFixture.CreateAsync();
        var key = Guid.NewGuid().ToString("D");
        var first = await fixture.Service.ApplyEditAsync(fixture.Form(90, key), fixture.AdminPrincipal, CancellationToken.None);
        Assert.True(first.Succeeded);
        var different = fixture.Form(80, key);
        different.ExpectedConcurrencyVersion = 1;

        var result = await fixture.Service.ApplyEditAsync(different, fixture.AdminPrincipal, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("different", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await fixture.Db.ReceiptInventoryOverrides.ToListAsync());
    }

    [Fact]
    public async Task Admin_void_preserves_bins_run_and_qc_history_and_removes_only_remaining_inventory()
    {
        await using var fixture = await OverrideFixture.CreateAsync(consumedBins: 25, includeHistory: true);
        var form = new DeleteReceiptForm
        {
            Id = OverrideFixture.ReceiptId,
            Reason = "Duplicate receiving record",
            ConfirmationValue = "OVERRIDE-100",
            ConfirmDeletion = true,
            ConfirmInventoryChange = true,
            OperationToken = Guid.NewGuid().ToString("D"),
            ExpectedConcurrencyVersion = 0
        };

        var result = await fixture.Service.VoidAsync(form, fixture.AdminPrincipal, CancellationToken.None);

        Assert.True(result.Succeeded);
        var receipt = await fixture.Db.Receipts.IgnoreQueryFilters().SingleAsync(x => x.Id == OverrideFixture.ReceiptId);
        Assert.True(receipt.IsDeleted);
        Assert.Equal(100, receipt.BinCount);
        Assert.Single(await fixture.Db.BinsRunEntries.ToListAsync());
        Assert.Single(await fixture.Db.ActualRuns.ToListAsync());
        Assert.Single(await fixture.Db.QcSamples.ToListAsync());
        Assert.Single(await fixture.Db.QcPhotos.ToListAsync());
        Assert.Single(await fixture.Db.QcSummaryEmailLogs.ToListAsync());
        var receiptOverride = await fixture.Db.ReceiptInventoryOverrides.Include(x => x.InventoryAdjustments).SingleAsync();
        Assert.Equal(ReceiptInventoryOverrideActionTypes.VoidReceipt, receiptOverride.ActionType);
        Assert.Equal(-75, receiptOverride.InventoryDelta);
        Assert.Equal(0, receiptOverride.CurrentInventoryAfter);
        Assert.Equal(-75, Assert.Single(receiptOverride.InventoryAdjustments).ChangeAmount);
        Assert.Single(await fixture.Db.ReceiptDeletionAudits.ToListAsync());
        Assert.Single(await fixture.Service.GetVoidedReceiptsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Admin_void_without_history_is_a_soft_void_with_exact_inventory_removal()
    {
        await using var fixture = await OverrideFixture.CreateAsync();

        var result = await fixture.Service.VoidAsync(new DeleteReceiptForm
        {
            Id = OverrideFixture.ReceiptId,
            Reason = "Wrong receipt",
            ConfirmationValue = "OVERRIDE-100",
            ConfirmDeletion = true,
            ConfirmInventoryChange = true,
            OperationToken = Guid.NewGuid().ToString("D"),
            ExpectedConcurrencyVersion = 0
        }, fixture.AdminPrincipal, CancellationToken.None);

        Assert.True(result.Succeeded);
        Assert.True((await fixture.Db.Receipts.FindAsync(OverrideFixture.ReceiptId))!.IsDeleted);
        Assert.Equal(-100, (await fixture.Db.ReceiptInventoryOverrides.SingleAsync()).InventoryDelta);
        Assert.Equal(-100, (await fixture.Db.RoomInventoryAdjustments.SingleAsync(x => x.ReceiptInventoryOverrideId != null)).ChangeAmount);
    }

    [Fact]
    public async Task Admin_void_traces_inventory_after_transfer_across_rooms()
    {
        await using var fixture = await OverrideFixture.CreateAsync();
        await fixture.AddTransferAsync(40, completePair: true);

        var result = await fixture.Service.VoidAsync(new DeleteReceiptForm
        {
            Id = OverrideFixture.ReceiptId,
            Reason = "Void after transfer",
            ConfirmationValue = "OVERRIDE-100",
            ConfirmDeletion = true,
            ConfirmInventoryChange = true,
            OperationToken = Guid.NewGuid().ToString("D"),
            ExpectedConcurrencyVersion = 0
        }, fixture.AdminPrincipal, CancellationToken.None);

        Assert.True(result.Succeeded);
        var rows = await fixture.Db.RoomInventoryAdjustments.Where(x => x.ReceiptInventoryOverrideId == result.OverrideId).OrderBy(x => x.RoomId).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(-60, rows[0].ChangeAmount);
        Assert.Equal(-40, rows[1].ChangeAmount);
        Assert.Single(await fixture.Db.RoomTransfers.ToListAsync());
    }

    [Fact]
    public async Task Unresolved_transfer_lineage_fails_closed_with_zero_writes()
    {
        await using var fixture = await OverrideFixture.CreateAsync();
        await fixture.AddTransferAsync(40, completePair: false);
        var before = await fixture.Db.RoomInventoryAdjustments.CountAsync();

        var result = await fixture.Service.VoidAsync(new DeleteReceiptForm
        {
            Id = OverrideFixture.ReceiptId,
            Reason = "Must fail",
            ConfirmationValue = "OVERRIDE-100",
            ConfirmDeletion = true,
            ConfirmInventoryChange = true,
            OperationToken = Guid.NewGuid().ToString("D"),
            ExpectedConcurrencyVersion = 0
        }, fixture.AdminPrincipal, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("lineage", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, await fixture.Db.RoomInventoryAdjustments.CountAsync());
        Assert.Empty(await fixture.Db.ReceiptInventoryOverrides.ToListAsync());
        Assert.False((await fixture.Db.Receipts.FindAsync(OverrideFixture.ReceiptId))!.IsDeleted);
    }

    [Fact]
    public async Task Inventory_reclassification_creates_exact_paired_adjustments_and_preserves_total()
    {
        await using var fixture = await OverrideFixture.CreateAsync();
        var form = fixture.Form(100, Guid.NewGuid().ToString("D"));
        form.RoomId = OverrideFixture.SecondRoomId;
        form.FruitProfileId = OverrideFixture.SecondFruitId;

        var result = await fixture.Service.ApplyEditAsync(form, fixture.AdminPrincipal, CancellationToken.None);

        Assert.True(result.Succeeded);
        var receiptOverride = await fixture.Db.ReceiptInventoryOverrides.Include(x => x.InventoryAdjustments).SingleAsync();
        Assert.Equal(ReceiptInventoryOverrideActionTypes.InventoryReclassification, receiptOverride.ActionType);
        Assert.Equal(0, receiptOverride.InventoryDelta);
        Assert.Equal(100, receiptOverride.CurrentInventoryBefore);
        Assert.Equal(100, receiptOverride.CurrentInventoryAfter);
        Assert.Equal(2, receiptOverride.InventoryAdjustments.Count);
        Assert.Equal(-100, receiptOverride.InventoryAdjustments.Single(x => x.ChangeAmount < 0).ChangeAmount);
        Assert.Equal(100, receiptOverride.InventoryAdjustments.Single(x => x.ChangeAmount > 0).ChangeAmount);
        Assert.Equal(OverrideFixture.SecondRoomId, (await fixture.Db.Receipts.FindAsync(OverrideFixture.ReceiptId))!.RoomId);
    }

    [Fact]
    public async Task Operational_identity_conflict_fails_closed()
    {
        await using var fixture = await OverrideFixture.CreateAsync(includeHistory: true);
        var form = fixture.Form(100, Guid.NewGuid().ToString("D"));
        form.RoomId = OverrideFixture.SecondRoomId;
        var before = await fixture.Db.RoomInventoryAdjustments.CountAsync();

        var result = await fixture.Service.ApplyEditAsync(form, fixture.AdminPrincipal, CancellationToken.None);

        Assert.False(result.Succeeded);
        Assert.Contains("conflicts", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await fixture.Db.ReceiptInventoryOverrides.ToListAsync());
        Assert.Equal(before, await fixture.Db.RoomInventoryAdjustments.CountAsync());
        Assert.Equal(OverrideFixture.RoomId, (await fixture.Db.Receipts.FindAsync(OverrideFixture.ReceiptId))!.RoomId);
    }

    [Fact]
    public async Task Invariant_rejects_receipt_override_quantity_mismatch_and_multiple_parent()
    {
        await using var fixture = await OverrideFixture.CreateAsync();
        var receipt = await fixture.Db.Receipts.SingleAsync(x => x.Id == OverrideFixture.ReceiptId);
        var admin = await fixture.Db.Users.SingleAsync(x => x.Id == OverrideFixture.AdminId);
        var operation = fixture.MalformedOperation(receipt, admin, inventoryDelta: -8, adjustmentCount: 1);
        var adjustment = fixture.OverrideAdjustment(operation, receipt, admin, -10);
        operation.InventoryAdjustments.Add(adjustment);
        fixture.Db.Add(operation);
        fixture.Db.Add(adjustment);

        var exception = await Assert.ThrowsAsync<InventoryDeductionInvariantException>(() =>
            fixture.Invariant.ValidateBeforeCommitAsync(CancellationToken.None));

        Assert.Contains("Receipt Admin Override", exception.Message);

        var binsRun = new BinsRunEntry
        {
            Id = 9090,
            ReceiptId = receipt.Id,
            Receipt = receipt,
            InventoryAdjustment = adjustment,
            WarehouseId = adjustment.WarehouseId,
            RoomId = adjustment.RoomId,
            CropYear = adjustment.CropYear,
            FruitProfileId = adjustment.FruitProfileId,
            GrowerName = adjustment.GrowerName,
            LotNumber = adjustment.LotNumber,
            VarietyCode = adjustment.VarietyCode,
            PreviousAvailableBins = 100,
            BinsRun = 10,
            NewAvailableBins = 90,
            RunAt = Now,
            CreatedAt = Now
        };
        fixture.Db.BinsRunEntries.Add(binsRun);
        var multiple = await Assert.ThrowsAsync<InventoryDeductionInvariantException>(() =>
            fixture.Invariant.ValidateBeforeCommitAsync(CancellationToken.None));
        Assert.Contains("MultipleParents", multiple.Message);
    }

    [Fact]
    public async Task Readiness_flags_tampered_receipt_override_room_identity()
    {
        await using var fixture = await OverrideFixture.CreateAsync();
        var applied = await fixture.Service.ApplyEditAsync(
            fixture.Form(90, Guid.NewGuid().ToString("D")), fixture.AdminPrincipal, CancellationToken.None);
        Assert.True(applied.Succeeded);
        var adjustment = await fixture.Db.RoomInventoryAdjustments.SingleAsync(x => x.ReceiptInventoryOverrideId == applied.OverrideId);
        adjustment.RoomId = OverrideFixture.SecondRoomId;
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var readiness = await fixture.Invariant.VerifyReadinessAsync(CancellationToken.None);

        Assert.Contains(readiness.Issues, x => x.Code == "ReceiptOverrideRoomLotMismatch" && x.BlocksDeployment);
    }

    [Fact]
    public void Override_endpoints_and_controls_are_server_authorized_and_admin_only()
    {
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "ReceiptsController.cs"));
        var dashboardService = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var editView = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Receipts", "Edit.cshtml"));

        Assert.Contains("AdminInventoryOverride", controller);
        Assert.Contains("AccessPolicyNames.ReceiptDeleteAdmin", controller);
        Assert.Contains("Model.CanAdminOverride", editView);
        Assert.Contains("ConfirmInventoryChange", editView);
        Assert.Contains("AcknowledgeNegativeInventory", editView);
        Assert.Contains("data-has-prior-override", editView);
        Assert.Contains("priorOverrideRequiresAuditedIncrease", editView);
        Assert.Contains("hasPriorInventoryOverride", dashboardService);
    }

    [Fact]
    public async Task PostgreSql_admin_override_workflow_when_configured()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROPQC_TEST_RECEIPT_OVERRIDE_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connectionString);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CropQcDbContext>().UseNpgsql(connection).Options;
        await using var db = new CropQcDbContext(options);
        Assert.False(await db.Receipts.AnyAsync(x => x.CompuTechReceiptId.StartsWith("PG-OVERRIDE-")));

        var warehouse = new Warehouse { Id = 93101, Code = "PG-OVR", Name = "PostgreSQL Override" };
        var roomA = new Room { Id = 93201, Warehouse = warehouse, WarehouseId = warehouse.Id, Code = "A", Name = "Room A" };
        var roomB = new Room { Id = 93202, Warehouse = warehouse, WarehouseId = warehouse.Id, Code = "B", Name = "Room B" };
        var conventional = new FruitProfile { Id = 93301, Name = "PG Gala", VarietyCode = "PG-GALA", FruitType = "Apple", ProductionType = "Conventional" };
        var organic = new FruitProfile { Id = 93302, Name = "PG Organic Gala", VarietyCode = "PG-ORG-GALA", FruitType = "Apple", ProductionType = "Organic", IsOrganic = true };
        var admin = new User { Id = 93401, Email = ApplicationAreas.OwnerEmail, DisplayName = "PostgreSQL Receipt Admin", Domain = "fruitandland.com", CreatedAt = Now };
        db.AddRange(warehouse, roomA, roomB, conventional, organic, admin);
        var quantityReceipt = PgReceipt(93501, "PG-OVERRIDE-QUANTITY", warehouse, roomA, conventional);
        var transferReceipt = PgReceipt(93502, "PG-OVERRIDE-TRANSFER", warehouse, roomA, conventional);
        var reclassReceipt = PgReceipt(93503, "PG-OVERRIDE-RECLASS", warehouse, roomA, conventional);
        var unresolvedReceipt = PgReceipt(93504, "PG-OVERRIDE-UNRESOLVED", warehouse, roomA, conventional);
        db.AddRange(quantityReceipt, transferReceipt, reclassReceipt, unresolvedReceipt);
        db.RoomInventoryAdjustments.AddRange(
            PgSource(93601, quantityReceipt, 100),
            PgSource(93602, transferReceipt, 100),
            PgSource(93603, reclassReceipt, 100),
            PgSource(93604, unresolvedReceipt, 100));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        using var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Error));
        var invariant = new InventoryDeductionInvariantService(
            db,
            loggerFactory.CreateLogger<InventoryDeductionInvariantService>());
        var service = new ReceiptInventoryOverrideService(
            db,
            new UserAccessService(db, new ConfigurationBuilder().Build()),
            invariant,
            new PacificBusinessTimeService(new FixedClock(Now)),
            loggerFactory.CreateLogger<ReceiptInventoryOverrideService>());
        var principal = Principal(ApplicationAreas.OwnerEmail);

        var reductionForm = PgForm(quantityReceipt, 90, Guid.NewGuid().ToString("D"));
        var reduction = await service.ApplyEditAsync(reductionForm, principal, CancellationToken.None);
        Assert.True(reduction.Succeeded, reduction.Error);
        Assert.True((await service.ApplyEditAsync(reductionForm, principal, CancellationToken.None)).WasIdempotent);
        var increaseForm = PgForm(quantityReceipt, 100, Guid.NewGuid().ToString("D"));
        increaseForm.ExpectedConcurrencyVersion = 1;
        Assert.True((await service.ApplyEditAsync(increaseForm, principal, CancellationToken.None)).Succeeded);
        var consumed = PgSource(93605, quantityReceipt, -90, "PostgreSqlConsumed");
        consumed.Receipt = null;
        db.RoomInventoryAdjustments.Add(consumed);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var negativeForm = PgForm(quantityReceipt, 80, Guid.NewGuid().ToString("D"));
        negativeForm.ExpectedConcurrencyVersion = 2;
        negativeForm.AcknowledgeNegativeInventory = true;
        Assert.True((await service.ApplyEditAsync(negativeForm, principal, CancellationToken.None)).Succeeded);
        var stale = PgForm(quantityReceipt, 70, Guid.NewGuid().ToString("D"));
        Assert.True((await service.ApplyEditAsync(stale, principal, CancellationToken.None)).IsConflict);

        await AddPgTransferAsync(db, transferReceipt, roomA, roomB, conventional, 93701, completePair: true);
        var voidTransfer = await service.VoidAsync(PgVoid(transferReceipt, Guid.NewGuid().ToString("D")), principal, CancellationToken.None);
        Assert.True(voidTransfer.Succeeded);
        Assert.Equal(2, await db.RoomInventoryAdjustments.CountAsync(x => x.ReceiptInventoryOverrideId == voidTransfer.OverrideId));
        Assert.Single(await db.RoomTransfers.Where(x => x.Id == 93701).ToListAsync());

        var reclassForm = PgForm(reclassReceipt, 100, Guid.NewGuid().ToString("D"));
        reclassForm.RoomId = roomB.Id;
        reclassForm.FruitProfileId = organic.Id;
        var reclass = await service.ApplyEditAsync(reclassForm, principal, CancellationToken.None);
        Assert.True(reclass.Succeeded);
        Assert.Equal(0, (await db.ReceiptInventoryOverrides.SingleAsync(x => x.Id == reclass.OverrideId)).InventoryDelta);

        await AddPgTransferAsync(db, unresolvedReceipt, roomA, roomB, conventional, 93901, completePair: false);
        var unresolved = await service.VoidAsync(PgVoid(unresolvedReceipt, Guid.NewGuid().ToString("D")), principal, CancellationToken.None);
        Assert.False(unresolved.Succeeded);
        Assert.False((await db.Receipts.FindAsync(unresolvedReceipt.Id))!.IsDeleted);

        var readiness = await invariant.VerifyReadinessAsync(CancellationToken.None);
        Assert.True(readiness.IsReady, string.Join("; ", readiness.Issues.Where(x => x.BlocksDeployment).Select(x => x.Code)));
        var reconciliation = await new RoomInventoryReconciliationService(db, new RoomInventoryLedgerQueryService(db), invariant)
            .GetPageAsync(new RoomInventoryReconciliationFilter { WarehouseId = warehouse.Id }, CancellationToken.None);
        Assert.Contains(reconciliation.NegativeAdjustments, x => x.ParentType == "Receipt Admin Override" && x.ParentMatches);
    }

    private static Receipt PgReceipt(long id, string number, Warehouse warehouse, Room room, FruitProfile fruit) => new()
    {
        Id = id,
        CropYear = 2026,
        ReceivedAt = Now.AddDays(-1),
        CompuTechReceiptId = number,
        ReceiptType = "Truck receipt",
        Warehouse = warehouse,
        WarehouseId = warehouse.Id,
        Room = room,
        RoomId = room.Id,
        FruitProfile = fruit,
        FruitProfileId = fruit.Id,
        GrowerNumber = $"G-{id}",
        GrowerName = "PostgreSQL Grower",
        LotCode = $"G-{id}",
        BinCount = 100,
        CreatedAt = Now.AddDays(-1),
        UpdatedAt = Now.AddDays(-1)
    };

    private static RoomInventoryAdjustment PgSource(long id, Receipt receipt, int change, string type = "ReceiptCreate") => new()
    {
        Id = id,
        Receipt = receipt,
        ReceiptId = receipt.Id,
        CropYear = receipt.CropYear,
        WarehouseId = receipt.WarehouseId,
        RoomId = receipt.RoomId,
        FruitProfileId = receipt.FruitProfileId,
        GrowerName = receipt.GrowerName,
        LotNumber = receipt.LotCode,
        VarietyCode = receipt.FruitProfile.VarietyCode,
        InventoryStatus = receipt.FruitProfile.ProductionType,
        OldBinCount = change > 0 ? 0 : 100,
        ChangeAmount = change,
        NewBinCount = change > 0 ? change : 100 + change,
        AdjustmentType = type,
        AdjustmentAt = Now,
        CreatedAt = Now
    };

    private static AdminReceiptInventoryOverrideForm PgForm(Receipt receipt, int bins, string key) => new()
    {
        Id = receipt.Id,
        OperationKey = key,
        Reason = "PostgreSQL workflow validation",
        ConfirmInventoryChange = true,
        ConfirmCropYear = true,
        CropYear = receipt.CropYear,
        ReceivedAt = receipt.ReceivedAt,
        CompuTechReceiptId = receipt.CompuTechReceiptId,
        ReceiptType = receipt.ReceiptType,
        WarehouseId = receipt.WarehouseId,
        RoomId = receipt.RoomId,
        FruitProfileId = receipt.FruitProfileId,
        GrowerNumber = receipt.GrowerNumber ?? receipt.LotCode,
        GrowerName = receipt.GrowerName,
        LotCode = receipt.LotCode,
        BinCount = bins
    };

    private static DeleteReceiptForm PgVoid(Receipt receipt, string key) => new()
    {
        Id = receipt.Id,
        Reason = "PostgreSQL void validation",
        ConfirmationValue = receipt.CompuTechReceiptId,
        ConfirmDeletion = true,
        ConfirmInventoryChange = true,
        OperationToken = key,
        ExpectedConcurrencyVersion = receipt.ConcurrencyVersion
    };

    private static async Task AddPgTransferAsync(
        CropQcDbContext db,
        Receipt receipt,
        Room sourceRoom,
        Room destinationRoom,
        FruitProfile fruit,
        long transferId,
        bool completePair)
    {
        var transfer = new RoomTransfer
        {
            Id = transferId,
            OperationKey = Guid.NewGuid().ToString("N"),
            SourceWarehouseId = sourceRoom.WarehouseId,
            SourceRoomId = sourceRoom.Id,
            DestinationWarehouseId = destinationRoom.WarehouseId,
            DestinationRoomId = destinationRoom.Id,
            CropYear = receipt.CropYear,
            FruitProfileId = fruit.Id,
            GrowerName = receipt.GrowerName,
            LotNumber = receipt.LotCode,
            VarietyCode = fruit.VarietyCode,
            InventoryStatus = fruit.ProductionType,
            BinCount = 40,
            Reason = "PostgreSQL transfer",
            TransferredAt = Now,
            CreatedAt = Now
        };
        db.RoomTransfers.Add(transfer);
        db.RoomInventoryAdjustments.Add(new RoomInventoryAdjustment
        {
            Id = transferId + 100,
            ReceiptId = receipt.Id,
            CropYear = receipt.CropYear,
            WarehouseId = sourceRoom.WarehouseId,
            RoomId = sourceRoom.Id,
            FruitProfileId = fruit.Id,
            GrowerName = receipt.GrowerName,
            LotNumber = receipt.LotCode,
            VarietyCode = fruit.VarietyCode,
            InventoryStatus = fruit.ProductionType,
            OldBinCount = 100,
            ChangeAmount = -40,
            NewBinCount = 60,
            AdjustmentType = "TransferOut",
            AdjustmentAt = Now,
            CreatedAt = Now,
            RoomTransfer = transfer
        });
        if (completePair)
        {
            db.RoomInventoryAdjustments.Add(new RoomInventoryAdjustment
            {
                Id = transferId + 101,
                ReceiptId = receipt.Id,
                CropYear = receipt.CropYear,
                WarehouseId = destinationRoom.WarehouseId,
                RoomId = destinationRoom.Id,
                FruitProfileId = fruit.Id,
                GrowerName = receipt.GrowerName,
                LotNumber = receipt.LotCode,
                VarietyCode = fruit.VarietyCode,
                InventoryStatus = fruit.ProductionType,
                OldBinCount = 0,
                ChangeAmount = 40,
                NewBinCount = 40,
                AdjustmentType = "TransferIn",
                AdjustmentAt = Now,
                CreatedAt = Now,
                RoomTransfer = transfer
            });
        }
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
    }

    private static ClaimsPrincipal Principal(string email) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.Email, email)], "Test"));

    private static string FindRepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(path)) return path;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, segments));
    }

    private sealed class OverrideFixture : IAsyncDisposable
    {
        public const int AdminId = 8101;
        public const long ReceiptId = 8201;
        public const int WarehouseId = 8301;
        public const int RoomId = 8401;
        public const int SecondRoomId = 8402;
        public const int FruitId = 8501;
        public const int SecondFruitId = 8502;
        private readonly SqliteConnection connection;

        private OverrideFixture(SqliteConnection connection, CropQcDbContext db, ClaimsPrincipal adminPrincipal, ClaimsPrincipal? editorPrincipal)
        {
            this.connection = connection;
            Db = db;
            AdminPrincipal = adminPrincipal;
            EditorPrincipal = editorPrincipal;
            Invariant = new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance);
            Service = new ReceiptInventoryOverrideService(
                db,
                new UserAccessService(db, new ConfigurationBuilder().Build()),
                Invariant,
                new PacificBusinessTimeService(new FixedClock(Now)),
                NullLogger<ReceiptInventoryOverrideService>.Instance);
        }

        public CropQcDbContext Db { get; }
        public ClaimsPrincipal AdminPrincipal { get; }
        public ClaimsPrincipal? EditorPrincipal { get; }
        public InventoryDeductionInvariantService Invariant { get; }
        public ReceiptInventoryOverrideService Service { get; }

        public static async Task<OverrideFixture> CreateAsync(int consumedBins = 0, bool includeHistory = false, bool includeReceiptEditor = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var warehouse = new Warehouse { Id = WarehouseId, Code = "OVR-WP", Name = "Override Wapato" };
            var room = new Room { Id = RoomId, Warehouse = warehouse, WarehouseId = WarehouseId, Code = "A", Name = "Room A" };
            var secondRoom = new Room { Id = SecondRoomId, Warehouse = warehouse, WarehouseId = WarehouseId, Code = "B", Name = "Room B" };
            var fruit = new FruitProfile { Id = FruitId, Name = "Gala", VarietyCode = "GALA-OVERRIDE", FruitType = "Apple", ProductionType = "Conventional" };
            var secondFruit = new FruitProfile { Id = SecondFruitId, Name = "Organic Gala", VarietyCode = "ORG-GALA-OVERRIDE", FruitType = "Apple", ProductionType = "Organic", IsOrganic = true };
            var admin = new User { Id = AdminId, Email = ApplicationAreas.OwnerEmail, DisplayName = "Receipt Admin", Domain = "fruitandland.com", CreatedAt = Now };
            User? editor = includeReceiptEditor
                ? new User { Id = AdminId + 1, Email = "receipt-editor@example.com", DisplayName = "Receipt Editor", Domain = "example.com", CreatedAt = Now }
                : null;
            if (editor is not null)
            {
                editor.PageAccesses.Add(new UserPageAccess { AreaKey = ApplicationAreas.Receipts, AccessLevel = PageAccessLevel.Create.ToString(), UpdatedAt = Now });
            }
            var receipt = new Receipt
            {
                Id = ReceiptId,
                CropYear = 2026,
                ReceivedAt = Now.AddDays(-1),
                CompuTechReceiptId = "OVERRIDE-100",
                ReceiptType = "Truck receipt",
                Warehouse = warehouse,
                WarehouseId = WarehouseId,
                Room = room,
                RoomId = RoomId,
                FruitProfile = fruit,
                FruitProfileId = FruitId,
                GrowerNumber = "G-100",
                GrowerName = "Test Grower",
                LotCode = "G-100",
                BinCount = 100,
                CreatedAt = Now.AddDays(-1),
                UpdatedAt = Now.AddDays(-1)
            };
            var source = SourceAdjustment(8601, receipt, 100, "ReceiptCreate");
            db.AddRange(warehouse, room, secondRoom, fruit, secondFruit, admin, receipt, source);
            if (editor is not null) db.Add(editor);
            if (consumedBins > 0)
            {
                db.RoomInventoryAdjustments.Add(SourceAdjustment(8602, receipt, -consumedBins, "LegacyConsumed"));
            }
            if (includeHistory)
            {
                var sampleType = new SampleType { Id = 8701, Name = "Override Sample" };
                var sample = new QcSample
                {
                    Id = 8702,
                    Receipt = receipt,
                    ReceiptId = receipt.Id,
                    SampleType = sampleType,
                    SampleTypeId = sampleType.Id,
                    Status = "Complete",
                    StarchStatus = "Complete",
                    PhotoStatus = "Complete",
                    EmailStatus = "Sent",
                    SampleTakenAt = Now,
                    CreatedAt = Now
                };
                db.AddRange(sampleType, sample);
                db.QcPhotos.Add(new QcPhoto
                {
                    Id = 8703,
                    QcSample = sample,
                    QcSampleId = sample.Id,
                    PhotoType = "Other",
                    PhotoSource = "Test",
                    FileName = "override.jpg",
                    ContentType = "image/jpeg",
                    SharePointDriveId = "drive",
                    SharePointItemId = "item",
                    CapturedAt = Now
                });
                db.QcSummaryEmailLogs.Add(new QcSummaryEmailLog
                {
                    Id = 8704,
                    QcSample = sample,
                    QcSampleId = sample.Id,
                    FromAddress = "qc@example.com",
                    ToAddress = "grower@example.com",
                    Subject = "Preserved",
                    Status = "Sent",
                    CreatedAt = Now
                });
                var actualRun = new ActualRun
                {
                    Id = 8706,
                    Status = "Completed",
                    CurrentRevisionNumber = 1,
                    RunAt = Now,
                    CreatedAt = Now
                };
                db.ActualRuns.Add(actualRun);
                db.BinsRunEntries.Add(new BinsRunEntry
                {
                    Id = 8705,
                    Receipt = receipt,
                    ReceiptId = receipt.Id,
                    InventoryAdjustment = source,
                    Warehouse = warehouse,
                    WarehouseId = warehouse.Id,
                    Room = room,
                    RoomId = room.Id,
                    CropYear = 2026,
                    FruitProfile = fruit,
                    FruitProfileId = fruit.Id,
                    GrowerName = receipt.GrowerName,
                    LotNumber = receipt.LotCode,
                    VarietyCode = fruit.VarietyCode,
                    PreviousAvailableBins = 100,
                    BinsRun = consumedBins,
                    NewAvailableBins = 100 - consumedBins,
                    RunAt = Now,
                    CreatedAt = Now
                    ,
                    ActualRun = actualRun,
                    ActualRunId = actualRun.Id
                });
            }
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return new OverrideFixture(connection, db, Principal(admin.Email), editor is null ? null : Principal(editor.Email));
        }

        public AdminReceiptInventoryOverrideForm Form(int binCount, string operationKey) => new()
        {
            Id = ReceiptId,
            ExpectedConcurrencyVersion = 0,
            OperationKey = operationKey,
            Reason = "Correct receiving entry",
            ConfirmInventoryChange = true,
            CropYear = 2026,
            ConfirmCropYear = true,
            ReceivedAt = Now.AddDays(-1),
            CompuTechReceiptId = "OVERRIDE-100",
            ReceiptType = "Truck receipt",
            WarehouseId = WarehouseId,
            RoomId = RoomId,
            FruitProfileId = FruitId,
            GrowerNumber = "G-100",
            GrowerName = "Test Grower",
            LotCode = "G-100",
            BinCount = binCount
        };

        public ReceiptInventoryOverride MalformedOperation(Receipt receipt, User admin, int inventoryDelta, int adjustmentCount) => new()
        {
            Id = Guid.NewGuid(),
            Receipt = receipt,
            ReceiptId = receipt.Id,
            ActionType = ReceiptInventoryOverrideActionTypes.QuantityCorrection,
            OldReceiptBinCount = 100,
            NewReceiptBinCount = 90,
            InventoryDelta = inventoryDelta,
            CurrentInventoryBefore = 100,
            CurrentInventoryAfter = 90,
            AdministratorUser = admin,
            AdministratorUserId = admin.Id,
            Reason = "Test invariant",
            OperationKey = Guid.NewGuid().ToString("D"),
            CreatedAt = Now,
            BeforeReceiptSnapshotJson = "{}",
            AfterReceiptSnapshotJson = "{}",
            AffectedInventorySnapshotJson = "[]",
            ExpectedAdjustmentCount = adjustmentCount,
            IsComplete = true
        };

        public RoomInventoryAdjustment OverrideAdjustment(ReceiptInventoryOverride operation, Receipt receipt, User admin, int change) => new()
        {
            Receipt = receipt,
            ReceiptId = receipt.Id,
            WarehouseId = receipt.WarehouseId,
            RoomId = receipt.RoomId,
            CropYear = receipt.CropYear,
            FruitProfileId = receipt.FruitProfileId,
            GrowerName = receipt.GrowerName,
            LotNumber = receipt.LotCode,
            VarietyCode = "GALA-OVERRIDE",
            InventoryStatus = "Conventional",
            OldBinCount = 100,
            ChangeAmount = change,
            NewBinCount = 100 + change,
            AdjustmentType = ReceiptInventoryOverrideService.AdjustmentType,
            AdjustmentAt = Now,
            CreatedAt = Now,
            CreatedByUser = admin,
            CreatedByUserId = admin.Id,
            InventoryInvariantVersion = InventoryDeductionInvariantService.CurrentVersion,
            InventoryOperationKey = $"receipt-override:{operation.OperationKey}:1",
            ReceiptInventoryOverride = operation,
            ReceiptInventoryOverrideId = operation.Id
        };

        public async Task AddTransferAsync(int bins, bool completePair)
        {
            var receipt = await Db.Receipts.Include(x => x.FruitProfile).SingleAsync(x => x.Id == ReceiptId);
            var transfer = new RoomTransfer
            {
                Id = 8801,
                OperationKey = Guid.NewGuid().ToString("N"),
                SourceWarehouseId = WarehouseId,
                SourceRoomId = RoomId,
                DestinationWarehouseId = WarehouseId,
                DestinationRoomId = SecondRoomId,
                CropYear = 2026,
                FruitProfileId = FruitId,
                GrowerName = receipt.GrowerName,
                LotNumber = receipt.LotCode,
                VarietyCode = receipt.FruitProfile.VarietyCode,
                InventoryStatus = receipt.FruitProfile.ProductionType,
                BinCount = bins,
                Reason = "Test transfer",
                TransferredAt = Now,
                CreatedAt = Now
            };
            Db.RoomTransfers.Add(transfer);
            Db.RoomInventoryAdjustments.Add(new RoomInventoryAdjustment
            {
                Id = 8802,
                ReceiptId = ReceiptId,
                CropYear = 2026,
                WarehouseId = WarehouseId,
                RoomId = RoomId,
                FruitProfileId = FruitId,
                GrowerName = receipt.GrowerName,
                LotNumber = receipt.LotCode,
                VarietyCode = receipt.FruitProfile.VarietyCode,
                InventoryStatus = receipt.FruitProfile.ProductionType,
                OldBinCount = 100,
                ChangeAmount = -bins,
                NewBinCount = 100 - bins,
                AdjustmentType = "TransferOut",
                AdjustmentAt = Now,
                CreatedAt = Now,
                RoomTransfer = transfer,
                RoomTransferId = transfer.Id
            });
            if (completePair)
            {
                Db.RoomInventoryAdjustments.Add(new RoomInventoryAdjustment
                {
                    Id = 8803,
                    ReceiptId = ReceiptId,
                    CropYear = 2026,
                    WarehouseId = WarehouseId,
                    RoomId = SecondRoomId,
                    FruitProfileId = FruitId,
                    GrowerName = receipt.GrowerName,
                    LotNumber = receipt.LotCode,
                    VarietyCode = receipt.FruitProfile.VarietyCode,
                    InventoryStatus = receipt.FruitProfile.ProductionType,
                    OldBinCount = 0,
                    ChangeAmount = bins,
                    NewBinCount = bins,
                    AdjustmentType = "TransferIn",
                    AdjustmentAt = Now,
                    CreatedAt = Now,
                    RoomTransfer = transfer,
                    RoomTransferId = transfer.Id
                });
            }
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }

        private static RoomInventoryAdjustment SourceAdjustment(long id, Receipt receipt, int change, string type) => new()
        {
            Id = id,
            Receipt = receipt,
            ReceiptId = receipt.Id,
            CropYear = receipt.CropYear,
            WarehouseId = receipt.WarehouseId,
            RoomId = receipt.RoomId,
            FruitProfileId = receipt.FruitProfileId,
            GrowerName = receipt.GrowerName,
            LotNumber = receipt.LotCode,
            VarietyCode = receipt.FruitProfile.VarietyCode,
            InventoryStatus = receipt.FruitProfile.ProductionType,
            OldBinCount = change > 0 ? 0 : 100,
            ChangeAmount = change,
            NewBinCount = change > 0 ? change : 100 + change,
            AdjustmentType = type,
            AdjustmentAt = Now,
            CreatedAt = Now
        };

        private static ClaimsPrincipal Principal(string email) => new(new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, email)], "Test"));
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }
}
