using System.Security.Claims;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class OutsideWarehouseTransferTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-27T18:00:00Z");

    [Fact]
    public async Task Partial_transfer_removes_exact_bins_without_creating_destination_inventory()
    {
        await using var fixture = await Fixture.CreateAsync();
        var roomCountBefore = await fixture.Db.Rooms.CountAsync();
        var warehouseCountBefore = await fixture.Db.Warehouses.CountAsync();
        var result = await fixture.Service.CreateAsync(await fixture.FormAsync("outside-partial", 200), default);

        Assert.True(result.Success, result.Error);
        var transfer = Assert.Single(await fixture.Db.OutsideWarehouseTransfers.ToListAsync());
        Assert.Equal(200, transfer.BinCount);
        Assert.Equal("CUSTOM", transfer.OutsideWarehouseCodeSnapshot);
        Assert.Equal("9350", transfer.GrowerNumberSnapshot);
        Assert.Equal("9350", transfer.LotNumberSnapshot);
        Assert.Equal("TEST-GALA", transfer.VarietyCodeSnapshot);
        Assert.Equal("LOAD-42", transfer.TruckLoadBolNumber);
        Assert.Equal(100, await fixture.CurrentBinsAsync());
        Assert.Equal(roomCountBefore, await fixture.Db.Rooms.CountAsync());
        Assert.Equal(warehouseCountBefore, await fixture.Db.Warehouses.CountAsync());
        Assert.DoesNotContain(await fixture.Db.Rooms.ToListAsync(), x => x.Code == "CUSTOM");
        var adjustment = Assert.Single(await fixture.Db.RoomInventoryAdjustments.Where(x => x.OutsideWarehouseTransferId != null).ToListAsync());
        Assert.Equal(-200, adjustment.ChangeAmount);
        Assert.Equal(OutsideWarehouseTransferAdjustmentTypes.Transfer, adjustment.AdjustmentType);
        Assert.Equal(transfer.Id, adjustment.OutsideWarehouseTransferId);
        Assert.Empty(await fixture.Db.ActualRuns.ToListAsync());
        Assert.Empty(await fixture.Db.BinsRunEntries.ToListAsync());
        Assert.Empty(await fixture.Db.RoomTransfers.ToListAsync());
    }

    [Fact]
    public async Task Full_transfer_removes_source_from_all_shared_current_inventory_selectors()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Service.CreateAsync(await fixture.FormAsync("outside-full", 300), default);
        Assert.True(result.Success, result.Error);
        Assert.Equal(0, await fixture.CurrentBinsAsync());
        Assert.Empty((await fixture.Service.GetPageAsync(new() { TransferType = "Outside" }, default)).Inventory);
        Assert.DoesNotContain(await fixture.Db.TreatmentLineageSegments.ToListAsync(), x => x.CurrentBins > 0);
    }

    [Fact]
    public async Task Duplicate_operation_key_is_idempotent_and_deducts_once()
    {
        await using var fixture = await Fixture.CreateAsync();
        var form = await fixture.FormAsync("outside-retry", 80);
        var first = await fixture.Service.CreateAsync(form, default);
        var second = await fixture.Service.CreateAsync(form, default);
        Assert.True(first.Success, first.Error);
        Assert.True(second.Success, second.Error);
        Assert.True(second.AlreadyApplied);
        Assert.Equal(first.TransferId, second.TransferId);
        Assert.Single(await fixture.Db.OutsideWarehouseTransfers.ToListAsync());
        Assert.Single(await fixture.Db.RoomInventoryAdjustments.Where(x => x.OutsideWarehouseTransferId != null).ToListAsync());
        Assert.Equal(220, await fixture.CurrentBinsAsync());
    }

    [Fact]
    public async Task Shared_identity_across_receipts_is_one_operator_choice_but_retains_exact_receipt_lineage()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddMatchingReceiptAsync(40);

        var page = await fixture.Service.GetPageAsync(new() { TransferType = "Outside" }, default);
        var choice = Assert.Single(page.Inventory);
        Assert.Equal(340, choice.AvailableBins);

        var result = await fixture.Service.CreateAsync(await fixture.FormAsync("outside-multi-receipt", 320), default);
        Assert.True(result.Success, result.Error);
        var transfer = await fixture.Db.OutsideWarehouseTransfers.SingleAsync();
        Assert.Null(transfer.ReceiptId);
        Assert.Equal(20, await fixture.CurrentBinsAsync());

        var movements = await fixture.Db.TreatmentLineageMovements
            .Where(x => x.OutsideWarehouseTransferId == transfer.Id && x.ReversesTreatmentLineageMovementId == null)
            .ToListAsync();
        Assert.Equal(320, movements.Sum(x => x.BinCount));
        Assert.Equal([8844L, 8850L], movements.Select(x => x.ReceiptId!.Value).Distinct().Order().ToArray());
    }

    [Fact]
    public async Task Insufficient_or_stale_inventory_fails_with_zero_writes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var insufficient = await fixture.FormAsync("outside-overdraw", 301);
        var overdraw = await fixture.Service.CreateAsync(insufficient, default);
        Assert.False(overdraw.Success);
        Assert.Contains("Only 300", overdraw.Error);

        var stale = await fixture.FormAsync("outside-stale", 10);
        stale.ExpectedAvailableBins = 299;
        var staleResult = await fixture.Service.CreateAsync(stale, default);
        Assert.False(staleResult.Success);
        Assert.Contains("changed", staleResult.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await fixture.Db.OutsideWarehouseTransfers.ToListAsync());
        Assert.Empty(await fixture.Db.RoomInventoryAdjustments.Where(x => x.OutsideWarehouseTransferId != null).ToListAsync());
        Assert.Equal(300, await fixture.CurrentBinsAsync());
    }

    [Fact]
    public async Task Sealed_source_fails_closed_without_transfer_ledger_or_audit_writes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var room = await fixture.Db.Rooms.SingleAsync();
        room.IsSealed = true;
        room.SealedAt = Now.AddMinutes(-1);
        room.SealRecordedAt = Now.AddMinutes(-2);
        await fixture.Db.SaveChangesAsync();
        var auditBefore = await fixture.Db.AuditLogs.CountAsync();
        var result = await fixture.Service.CreateAsync(await fixture.FormAsync("outside-sealed", 20), default);
        Assert.False(result.Success);
        Assert.Contains("sealed", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await fixture.Db.OutsideWarehouseTransfers.ToListAsync());
        Assert.Equal(auditBefore, await fixture.Db.AuditLogs.CountAsync());
        Assert.Equal(300, await fixture.CurrentBinsAsync());
    }

    [Fact]
    public async Task Reversal_restores_exact_identity_and_treatment_provenance_and_retains_history()
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.Service.CreateAsync(await fixture.FormAsync("outside-reverse", 125), default);
        Assert.True(created.Success, created.Error);
        var originalMovements = await fixture.Db.TreatmentLineageMovements.Where(x => x.OutsideWarehouseTransferId == created.TransferId && x.ReversesTreatmentLineageMovementId == null).ToListAsync();
        Assert.NotEmpty(originalMovements);
        Assert.Equal(125, originalMovements.Sum(x => x.BinCount));

        var error = await fixture.Service.ReverseAsync(new() { TransferId = created.TransferId!.Value, OperationKey = "outside-reverse-op", Reason = "Incorrect destination entry" }, default);
        Assert.Null(error);
        Assert.Equal(300, await fixture.CurrentBinsAsync());
        var saved = await fixture.Db.OutsideWarehouseTransfers.SingleAsync();
        Assert.True(saved.IsReversed);
        Assert.Equal("Incorrect destination entry", saved.ReverseReason);
        Assert.Equal("9350", saved.GrowerNumberSnapshot);
        Assert.Equal("TEST-GALA", saved.VarietyCodeSnapshot);
        var adjustments = await fixture.Db.RoomInventoryAdjustments.Where(x => x.OutsideWarehouseTransferId == saved.Id).OrderBy(x => x.Id).ToListAsync();
        Assert.Equal([-125, 125], adjustments.Select(x => x.ChangeAmount));
        var reversals = await fixture.Db.TreatmentLineageMovements.Where(x => x.OutsideWarehouseTransferId == saved.Id && x.ReversesTreatmentLineageMovementId != null).ToListAsync();
        Assert.Equal(125, reversals.Sum(x => x.BinCount));
        Assert.Equal(2, await fixture.Db.AuditLogs.CountAsync(x => x.EntityName == nameof(OutsideWarehouseTransfer)));
    }

    [Fact]
    public async Task Reversal_requires_admin_and_reason_and_is_idempotent()
    {
        await using var fixture = await Fixture.CreateAsync(canAdmin: false);
        var created = await fixture.Service.CreateAsync(await fixture.FormAsync("outside-auth", 50), default);
        Assert.True(created.Success, created.Error);
        var denied = await fixture.Service.ReverseAsync(new() { TransferId = created.TransferId!.Value, OperationKey = "denied", Reason = "Correction" }, default);
        Assert.Contains("Admin", denied);

        fixture.Access.CanAdmin = true;
        var blank = await fixture.Service.ReverseAsync(new() { TransferId = created.TransferId.Value, OperationKey = "blank", Reason = "  " }, default);
        Assert.Contains("reason", blank, StringComparison.OrdinalIgnoreCase);
        Assert.Null(await fixture.Service.ReverseAsync(new() { TransferId = created.TransferId.Value, OperationKey = "valid-reverse", Reason = "Entry mistake" }, default));
        var adjustmentCount = await fixture.Db.RoomInventoryAdjustments.CountAsync(x => x.OutsideWarehouseTransferId != null);
        Assert.Null(await fixture.Service.ReverseAsync(new() { TransferId = created.TransferId.Value, OperationKey = "valid-reverse", Reason = "Entry mistake" }, default));
        Assert.Equal(adjustmentCount, await fixture.Db.RoomInventoryAdjustments.CountAsync(x => x.OutsideWarehouseTransferId != null));
    }

    [Fact]
    public async Task Active_location_is_selectable_inactive_location_is_history_only()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Db.OutsideWarehouses.Add(new OutsideWarehouse { Name = "Inactive Historic", Code = "OLD", IsActive = false, CreatedAt = Now, UpdatedAt = Now });
        await fixture.Db.SaveChangesAsync();
        var page = await fixture.Service.GetPageAsync(new() { TransferType = "Outside" }, default);
        Assert.Contains(page.OutsideWarehouses, x => x.Code == "CUSTOM");
        Assert.DoesNotContain(page.OutsideWarehouses, x => x.Code == "OLD");
        Assert.Contains(page.ReportOutsideWarehouses, x => x.Code == "OLD" && !x.IsActive);
    }

    [Fact]
    public async Task Source_facility_and_room_filter_limit_outside_transfer_inventory()
    {
        await using var fixture = await Fixture.CreateAsync();

        var matching = await fixture.Service.GetPageAsync(new()
        {
            TransferType = "Outside",
            WarehouseId = Fixture.WarehouseId,
            RoomId = Fixture.RoomId
        }, default);
        Assert.Single(matching.Inventory);

        var noMatch = await fixture.Service.GetPageAsync(new()
        {
            TransferType = "Outside",
            WarehouseId = Fixture.WarehouseId,
            RoomId = 999999
        }, default);
        Assert.Empty(noMatch.Inventory);

        var view = Source("src", "CropQc.Web", "Views", "BinsRun", "Index.cshtml");
        Assert.Contains("name=\"TransferType\" value=\"@Model.Filter.TransferType\"", view);
        Assert.Contains("TransferType=Outside&amp;WarehouseId=@Model.Filter.WarehouseId&amp;RoomId=@Model.Filter.RoomId", view);
    }

    [Fact]
    public async Task Outside_warehouse_master_lifecycle_is_unique_audited_and_history_safe()
    {
        await using var fixture = await Fixture.CreateAsync();
        var admin = new AdminManagementService(fixture.Db, new VarietyColorService(fixture.Db));
        var actor = "outside@test.local";
        Assert.Null(await admin.SaveMasterDataAsync(new MasterDataEditForm
        {
            Type = "outside-warehouses",
            Name = "Other Synthetic Packer",
            Code = " other ",
            Address = "200 Test Way",
            Description = "Disposable validation",
            IsActive = true
        }, actor, default));
        var other = await fixture.Db.OutsideWarehouses.SingleAsync(x => x.Code == "OTHER");
        Assert.Equal("Other Synthetic Packer", other.Name);
        Assert.Equal("200 Test Way", other.Address);
        Assert.Contains("already exists", await admin.SaveMasterDataAsync(new MasterDataEditForm
        {
            Type = "outside-warehouses",
            Name = "Duplicate",
            Code = "OTHER",
            IsActive = true
        }, actor, default), StringComparison.OrdinalIgnoreCase);

        var created = await fixture.Service.CreateAsync(await fixture.FormAsync("outside-history-location", 10), default);
        Assert.True(created.Success, created.Error);
        Assert.Null(await admin.DeactivateAsync("outside-warehouses", 8845, actor, default));
        Assert.False((await fixture.Db.OutsideWarehouses.FindAsync(8845))!.IsActive);
        var page = await fixture.Service.GetPageAsync(new() { TransferType = "Outside" }, default);
        Assert.DoesNotContain(page.OutsideWarehouses, x => x.Id == 8845);
        Assert.Contains(page.History, x => x.OutsideWarehouseCode == "CUSTOM");
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), x => x.Action == "OutsideWarehouseCreated");
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), x => x.Action == "OutsideWarehouseDeactivated");
    }

    [Fact]
    public void Migration_compatibility_and_803_object_gate_are_exact_and_bounded()
    {
        var migration = Source("src", "CropQc.Data", "Migrations", "20260828012532_AddOutsideWarehouseTransfers.cs");
        var preflight = Source("scripts", "postgresql", "preflight-outside-warehouse-transfers.sql");
        var apply = Source("scripts", "postgresql", "apply-outside-warehouse-transfers-schema.sql");
        var verify = Source("scripts", "postgresql", "verify-outside-warehouse-transfers.sql");
        var gate = Source("src", "CropQc.Web", "Services", "DatabaseStartupDiagnostics.cs");
        Assert.Contains("name: \"OutsideWarehouses\"", migration);
        Assert.Contains("name: \"OutsideWarehouseTransfers\"", migration);
        Assert.Contains("MigrationProviderTypes.StoreType", migration);
        Assert.Contains("Npgsql:ValueGenerationStrategy", migration);
        Assert.Contains("State C", preflight);
        Assert.Contains("state_a_absent", preflight);
        Assert.Contains("state_b_complete_exact", preflight);
        Assert.Contains("BEGIN;", apply);
        Assert.Contains("pg_advisory_xact_lock", apply);
        Assert.DoesNotContain("__EFMigrationsHistory", apply);
        Assert.Contains("82 AS checked_target_objects", verify);
        Assert.Equal("20260828012532_AddOutsideWarehouseTransfers", DatabaseStartupDiagnostics.ExpectedSchemaMigration);
        Assert.Equal(803, gate.Split('\n').Count(x => x.TrimStart().StartsWith("new(", StringComparison.Ordinal)));
    }

    [Fact]
    public void UI_keeps_internal_transfer_default_and_exposes_outside_history_review_and_mobile_safe_fields()
    {
        var index = Source("src", "CropQc.Web", "Views", "BinsRun", "Index.cshtml");
        var outside = Source("src", "CropQc.Web", "Views", "BinsRun", "_OutsideWarehouseTransfer.cshtml");
        var detail = Source("src", "CropQc.Web", "Views", "BinsRun", "OutsideTransferDetails.cshtml");
        var master = Source("src", "CropQc.Web", "Views", "MasterData", "_MasterDataFields.cshtml");
        Assert.Contains("Internal Room Transfer", index);
        Assert.Contains("Outside Warehouse", index);
        Assert.Contains("Current inventory selection", outside);
        Assert.Contains("Truck / Load / BOL #", outside);
        Assert.Contains("Review Outside Transfer", outside);
        Assert.Contains("Outside Transfer History", outside);
        Assert.Contains("inputmode=\"numeric\"", outside);
        Assert.DoesNotContain("Destination Room", outside);
        Assert.Contains("Required reversal reason", detail);
        Assert.Contains("Address", master);
    }

    private static string Source(params string[] segments) => File.ReadAllText(FindRepositoryFile(segments));

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

    private sealed class Fixture : IAsyncDisposable
    {
        public const int WarehouseId = 8840;
        public const int RoomId = 8841;
        private Fixture(CropQcDbContext db, OutsideWarehouseTransferService service, RoomInventoryLedgerQueryService ledger, RoomTreatmentService treatments, ConfigurableAccess access)
        {
            Db = db; Service = service; Ledger = ledger; Treatments = treatments; Access = access;
        }
        public CropQcDbContext Db { get; }
        public OutsideWarehouseTransferService Service { get; }
        public RoomInventoryLedgerQueryService Ledger { get; }
        public RoomTreatmentService Treatments { get; }
        public ConfigurableAccess Access { get; }

        public static async Task<Fixture> CreateAsync(bool canAdmin = true)
        {
            var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseInMemoryDatabase($"outside-transfer-{Guid.NewGuid():N}").Options);
            await db.Database.EnsureCreatedAsync();
            var warehouse = new Warehouse { Id = WarehouseId, Code = "WP", Name = "Warehouse Production" };
            var room = new Room { Id = RoomId, Warehouse = warehouse, WarehouseId = WarehouseId, Code = "TEST-12", Name = "Test 12", CropQcRoomName = "Test 12", CapacityBins = 1000 };
            var fruit = new FruitProfile { Id = 8842, Name = "Test Gala", VarietyCode = "TEST-GALA", FruitType = "Apple", ProductionType = "Conventional" };
            var user = new User { Id = 8843, Email = "outside@test.local", DisplayName = "Outside Tester", Domain = "test.local", CreatedAt = Now };
            var receipt = new Receipt { Id = 8844, CropYear = 2026, CompuTechReceiptId = "TR109999", ReceivedAt = Now.AddDays(-1), Warehouse = warehouse, WarehouseId = WarehouseId, Room = room, RoomId = RoomId, FruitProfile = fruit, FruitProfileId = fruit.Id, GrowerNumber = "9350", GrowerName = "TEST GROWER", LotCode = "9350", BinCount = 300, CreatedAt = Now.AddDays(-1), UpdatedAt = Now.AddDays(-1) };
            var outside = new OutsideWarehouse { Id = 8845, Name = "Custom Apple", Code = "CUSTOM", Address = "123 Test Road", IsActive = true, CreatedAt = Now, UpdatedAt = Now, CreatedByUserId = user.Id, UpdatedByUserId = user.Id };
            db.AddRange(warehouse, room, fruit, user, receipt, outside);
            db.RoomInventoryAdjustments.Add(new RoomInventoryAdjustment { Id = 8846, CropYear = 2026, Receipt = receipt, ReceiptId = receipt.Id, Warehouse = warehouse, WarehouseId = WarehouseId, Room = room, RoomId = RoomId, FruitProfile = fruit, FruitProfileId = fruit.Id, GrowerName = receipt.GrowerName, LotNumber = receipt.LotCode, VarietyCode = fruit.VarietyCode, OldBinCount = 0, ChangeAmount = 300, NewBinCount = 300, AdjustmentType = "Receipt", InventoryStatus = "Packable", AdjustmentAt = Now.AddDays(-1), CreatedAt = Now.AddDays(-1) });
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, user.Email)], "Test"));
            var accessor = new FixedHttpContextAccessor(new DefaultHttpContext { User = principal });
            var access = new ConfigurableAccess { CanAdmin = canAdmin };
            var time = new PacificBusinessTimeService(new FixedClock(Now));
            var ledger = new RoomInventoryLedgerQueryService(db);
            var treatments = new RoomTreatmentService(db, ledger, access, accessor, time, NullLogger<RoomTreatmentService>.Instance);
            var invariant = new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance);
            var service = new OutsideWarehouseTransferService(db, ledger, treatments, treatments, invariant, access, accessor, time);
            return new Fixture(db, service, ledger, treatments, access);
        }

        public async Task<OutsideWarehouseTransferForm> FormAsync(string key, int bins)
        {
            var page = await Service.GetPageAsync(new() { TransferType = "Outside" }, default);
            var option = Assert.Single(page.Inventory);
            return new() { OperationKey = key, OutsideWarehouseId = 8845, SourceKey = option.SourceKey, ExpectedAvailableBins = option.AvailableBins, BinCount = bins, TransferredAt = DateTime.Parse("2026-08-27T10:00"), TruckLoadBolNumber = "LOAD-42", Notes = "Disposable proof", ConfirmedReview = true };
        }

        public async Task AddMatchingReceiptAsync(int bins)
        {
            var warehouse = await Db.Warehouses.SingleAsync(x => x.Id == WarehouseId);
            var room = await Db.Rooms.SingleAsync(x => x.Id == RoomId);
            var fruit = await Db.FruitProfiles.SingleAsync(x => x.Id == 8842);
            var receipt = new Receipt
            {
                Id = 8850,
                CropYear = 2026,
                CompuTechReceiptId = "TR110000",
                ReceivedAt = Now.AddHours(-12),
                Warehouse = warehouse,
                WarehouseId = WarehouseId,
                Room = room,
                RoomId = RoomId,
                FruitProfile = fruit,
                FruitProfileId = fruit.Id,
                GrowerNumber = "9350",
                GrowerName = "TEST GROWER",
                LotCode = "9350",
                BinCount = bins,
                CreatedAt = Now.AddHours(-12),
                UpdatedAt = Now.AddHours(-12)
            };
            Db.Receipts.Add(receipt);
            Db.RoomInventoryAdjustments.Add(new RoomInventoryAdjustment
            {
                Id = 8851,
                CropYear = 2026,
                Receipt = receipt,
                ReceiptId = receipt.Id,
                Warehouse = warehouse,
                WarehouseId = WarehouseId,
                Room = room,
                RoomId = RoomId,
                FruitProfile = fruit,
                FruitProfileId = fruit.Id,
                GrowerName = receipt.GrowerName,
                LotNumber = receipt.LotCode,
                VarietyCode = fruit.VarietyCode,
                OldBinCount = 300,
                ChangeAmount = bins,
                NewBinCount = 300 + bins,
                AdjustmentType = "Receipt",
                InventoryStatus = "Packable",
                AdjustmentAt = Now.AddHours(-12),
                CreatedAt = Now.AddHours(-12)
            });
            await Db.SaveChangesAsync();
            var snapshot = Assert.Single(await Ledger.GetSnapshotsAsync(WarehouseId, [RoomId], default));
            var identityKey = RoomTreatmentService.IdentityKey(snapshot);
            Db.TreatmentLineageSegments.AddRange(
                new TreatmentLineageSegment
                {
                    Id = 8852,
                    WarehouseId = WarehouseId,
                    RoomId = RoomId,
                    ReceiptId = 8844,
                    CropYear = 2026,
                    FruitProfileId = fruit.Id,
                    IdentityKey = identityKey,
                    GrowerNumberSnapshot = "9350",
                    GrowerNameSnapshot = "TEST GROWER",
                    LotNumberSnapshot = "9350",
                    VarietyCodeSnapshot = fruit.VarietyCode,
                    ProductionTypeSnapshot = fruit.ProductionType,
                    InventoryStatusSnapshot = "Packable",
                    TreatmentState = TreatmentLineageStates.Untreated,
                    TreatmentSignature = "u",
                    CurrentBins = 300,
                    CreatedAt = Now,
                    UpdatedAt = Now
                },
                new TreatmentLineageSegment
                {
                    Id = 8853,
                    WarehouseId = WarehouseId,
                    RoomId = RoomId,
                    ReceiptId = receipt.Id,
                    CropYear = 2026,
                    FruitProfileId = fruit.Id,
                    IdentityKey = identityKey,
                    GrowerNumberSnapshot = "9350",
                    GrowerNameSnapshot = "TEST GROWER",
                    LotNumberSnapshot = "9350",
                    VarietyCodeSnapshot = fruit.VarietyCode,
                    ProductionTypeSnapshot = fruit.ProductionType,
                    InventoryStatusSnapshot = "Packable",
                    TreatmentState = TreatmentLineageStates.Untreated,
                    TreatmentSignature = "u",
                    CurrentBins = bins,
                    CreatedAt = Now,
                    UpdatedAt = Now
                });
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async Task<int> CurrentBinsAsync() => (await Ledger.GetSnapshotsAsync(WarehouseId, [RoomId], default)).Sum(x => x.CurrentBins);
        public async ValueTask DisposeAsync() => await Db.DisposeAsync();
    }

    private sealed class FixedClock(DateTimeOffset now) : IClock { public DateTimeOffset UtcNow { get; } = now; }
    private sealed class FixedHttpContextAccessor(HttpContext context) : IHttpContextAccessor { public HttpContext? HttpContext { get; set; } = context; }
    private sealed class ConfigurableAccess : IUserAccessService
    {
        public bool CanAdmin { get; set; } = true;
        public Task<bool> HasAccessAsync(ClaimsPrincipal principal, string areaKey, PageAccessLevel minimumLevel, CancellationToken cancellationToken) =>
            Task.FromResult(minimumLevel <= PageAccessLevel.Create || CanAdmin);
        public Task<PageAccessLevel> GetAccessLevelAsync(string? email, string areaKey, CancellationToken cancellationToken) => Task.FromResult(CanAdmin ? PageAccessLevel.Admin : PageAccessLevel.Create);
        public void InvalidateAll() { }
    }
}
