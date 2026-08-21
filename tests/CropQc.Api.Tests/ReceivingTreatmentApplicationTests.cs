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

public sealed class ReceivingTreatmentApplicationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-20T19:00:00Z");

    [Fact]
    public async Task Room_and_receipt_workflows_filter_explicit_application_level_and_reject_tampering()
    {
        await using var fixture = await Fixture.CreateAsync();

        var roomPage = await fixture.Service.GetApplyPageAsync(fixture.RoomForm("room-options", 1), false, default);
        Assert.DoesNotContain(roomPage.TreatmentOptions, x => x.Id is Fixture.AppleMcpId or Fixture.PearMcpId);
        Assert.All(roomPage.TreatmentOptions, x => Assert.Equal("Apples", x.Crop));

        var receiptPage = await fixture.Service.GetReceiptApplyPageAsync(fixture.ReceiptForm("receipt-options", Fixture.AppleMcpId), false, default);
        var receiptOption = Assert.Single(receiptPage.TreatmentOptions);
        Assert.Equal(Fixture.AppleMcpId, receiptOption.Id);
        Assert.Equal("MCP", receiptOption.CommonName);
        Assert.DoesNotContain(receiptPage.TreatmentOptions, x => x.Id == 1);

        var roomTamper = await fixture.Service.ApplyAsync(fixture.RoomForm("room-tamper", Fixture.AppleMcpId), default);
        Assert.Contains("active treatment was not found", roomTamper.Error, StringComparison.OrdinalIgnoreCase);
        var receiptTamper = await fixture.Service.ApplyReceiptAsync(fixture.ReceiptForm("receipt-tamper", 1), default);
        Assert.Contains("not an active Receiving treatment", receiptTamper.Error);
        var wrongCrop = await fixture.Service.ApplyReceiptAsync(fixture.ReceiptForm("receipt-wrong-crop", Fixture.PearMcpId), default);
        Assert.Contains("not an active Receiving treatment", wrongCrop.Error);
    }

    [Fact]
    public async Task Receiving_application_treats_all_and_only_exact_receipt_bins_without_inventory_write()
    {
        await using var fixture = await Fixture.CreateAsync();
        var adjustmentCount = await fixture.Db.RoomInventoryAdjustments.CountAsync();

        var result = await fixture.Service.ApplyReceiptAsync(fixture.ReceiptForm("exact-receipt-a", Fixture.AppleMcpId), default);

        Assert.Null(result.Error);
        var application = await fixture.Db.RoomTreatmentApplications.Include(x => x.Sources).SingleAsync();
        Assert.Equal(TreatmentApplicationLevels.Receiving, application.ApplicationLevel);
        Assert.Equal(Fixture.ReceiptAId, application.ReceiptId);
        Assert.Equal(40, application.TotalBinsSnapshot);
        var source = Assert.Single(application.Sources);
        Assert.Equal(Fixture.ReceiptAId, source.ReceiptId);
        Assert.Equal(40, source.BinsTreated);
        Assert.Equal(adjustmentCount, await fixture.Db.RoomInventoryAdjustments.CountAsync());

        var selections = await fixture.Service.GetSelectionsAsync(fixture.Snapshot(100), default);
        Assert.Contains(selections, x => x.ReceiptId == Fixture.ReceiptAId
            && x.TreatmentState == TreatmentLineageStates.Confirmed && x.CurrentBins == 40);
        Assert.Contains(selections, x => x.ReceiptId is null
            && x.TreatmentState == TreatmentLineageStates.Untreated && x.CurrentBins == 60);
        Assert.Equal(100, selections.Sum(x => x.CurrentBins));
        var audit = await fixture.Db.AuditLogs.SingleAsync(x => x.Action == "ApplyReceivingTreatment");
        Assert.Contains("\"inventoryDelta\":0", audit.AfterValuesJson);
        Assert.Contains($"\"receiptId\":{Fixture.ReceiptAId}", audit.AfterValuesJson);
    }

    [Fact]
    public async Task Each_same_identity_receipt_can_be_treated_independently_and_operation_is_idempotent()
    {
        await using var fixture = await Fixture.CreateAsync();
        var formA = fixture.ReceiptForm("idempotent-a", Fixture.AppleMcpId);
        var first = await fixture.Service.ApplyReceiptAsync(formA, default);
        var repeated = await fixture.Service.ApplyReceiptAsync(formA, default);
        Assert.Equal(first.ApplicationId, repeated.ApplicationId);
        Assert.Single(await fixture.Db.RoomTreatmentApplications.ToListAsync());

        var second = await fixture.Service.ApplyReceiptAsync(
            fixture.ReceiptForm("exact-receipt-b", Fixture.AppleMcpId, Fixture.ReceiptBId), default);

        Assert.Null(second.Error);
        var applications = await fixture.Db.RoomTreatmentApplications.OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(new[] { 40, 60 }, applications.Select(x => x.TotalBinsSnapshot).ToArray());
        var selections = await fixture.Service.GetSelectionsAsync(fixture.Snapshot(100), default);
        Assert.Equal(100, selections.Sum(x => x.CurrentBins));
        Assert.Contains(selections, x => x.ReceiptId == Fixture.ReceiptAId && x.CurrentBins == 40);
        Assert.Contains(selections, x => x.ReceiptId == Fixture.ReceiptBId && x.CurrentBins == 60);
    }

    [Fact]
    public async Task Positive_receipt_override_does_not_expand_historical_treatment()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Null((await fixture.Service.ApplyReceiptAsync(fixture.ReceiptForm("before-positive-override", Fixture.AppleMcpId), default)).Error);
        fixture.Db.RoomInventoryAdjustments.Add(fixture.Adjustment(90003, Fixture.ReceiptAId, 5, 45, "ReceiptInventoryOverride"));
        await fixture.Db.SaveChangesAsync();
        fixture.Ledger.Replace(fixture.Snapshot(105));

        var selections = await fixture.Service.GetSelectionsAsync(fixture.Snapshot(105), default);

        var treated = Assert.Single(selections, x => x.ReceiptId == Fixture.ReceiptAId && x.TreatmentState == TreatmentLineageStates.Confirmed);
        Assert.Equal(40, treated.CurrentBins);
        Assert.Equal(65, selections.Where(x => x.TreatmentState == TreatmentLineageStates.Untreated).Sum(x => x.CurrentBins));
        Assert.Equal(105, selections.Sum(x => x.CurrentBins));
        Assert.Equal(40, (await fixture.Db.RoomTreatmentApplications.SingleAsync()).TotalBinsSnapshot);
    }

    [Fact]
    public async Task Receiving_application_fails_closed_when_same_identity_has_ambiguous_unassigned_history()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Null((await fixture.Service.ApplyAsync(fixture.RoomForm("whole-room-first", 1), default)).Error);
        fixture.Ledger.Replace(fixture.Snapshot(110));

        var result = await fixture.Service.ApplyReceiptAsync(fixture.ReceiptForm("ambiguous-receipt", Fixture.AppleMcpId), default);

        Assert.Contains("cannot guess", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await fixture.Db.RoomTreatmentApplications.ToListAsync());
        Assert.DoesNotContain(await fixture.Db.AuditLogs.ToListAsync(), x => x.Action == "ApplyReceivingTreatment");
    }

    [Fact]
    public async Task Receipt_reversal_requires_receipt_admin_and_preserves_evidence_with_zero_quantity_change()
    {
        await using var fixture = await Fixture.CreateAsync();
        var applied = await fixture.Service.ApplyReceiptAsync(fixture.ReceiptForm("reverse-receiving", Fixture.AppleMcpId), default);
        fixture.Access.Level = PageAccessLevel.Edit;
        Assert.Contains("Receipts Admin", await fixture.Service.ReverseReceiptAsync(new() { Id = applied.ApplicationId!.Value, Reason = "Not admin" }, default));
        fixture.Access.Level = PageAccessLevel.Admin;
        Assert.Contains("reason", await fixture.Service.ReverseReceiptAsync(new() { Id = applied.ApplicationId.Value, Reason = " " }, default), StringComparison.OrdinalIgnoreCase);

        var error = await fixture.Service.ReverseReceiptAsync(new() { Id = applied.ApplicationId.Value, Reason = "Receiving record corrected" }, default);

        Assert.Null(error);
        var application = await fixture.Db.RoomTreatmentApplications.SingleAsync();
        Assert.NotNull(application.ReversedAt);
        Assert.Equal("Receiving record corrected", application.ReversalReason);
        var selections = await fixture.Service.GetSelectionsAsync(fixture.Snapshot(100), default);
        Assert.Equal(100, selections.Sum(x => x.CurrentBins));
        Assert.DoesNotContain(selections, x => x.TreatmentState == TreatmentLineageStates.Confirmed);
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), x => x.Action == "ReverseTreatment");
    }

    [Fact]
    public async Task Receipt_edit_permission_is_required_for_apply_and_admin_permission_for_reverse()
    {
        await using var fixture = await Fixture.CreateAsync(PageAccessLevel.View);
        Assert.Contains("Receipts Edit", (await fixture.Service.ApplyReceiptAsync(fixture.ReceiptForm("no-edit", Fixture.AppleMcpId), default)).Error);

        var controller = Read("src", "CropQc.Web", "Controllers", "ReceiptsController.cs");
        Assert.Contains("AccessPolicyNames.ReceiptsEdit", controller);
        Assert.Contains("AccessPolicyNames.ReceiptDeleteAdmin", controller);
        Assert.Contains("Treatments/{applicationId:long}/Reverse", controller);
        Assert.Contains("Treatments/{applicationId:long}/Reports", controller);
        Assert.True(controller.Split("[ValidateAntiForgeryToken]", StringSplitOptions.None).Length - 1 >= 9);
    }

    [Fact]
    public void Receipt_history_shared_reports_mobile_ui_and_explicit_master_level_are_present()
    {
        var detail = Read("src", "CropQc.Web", "Views", "Receipts", "Details.cshtml");
        var apply = Read("src", "CropQc.Web", "Views", "Receipts", "ApplyTreatment.cshtml");
        var master = Read("src", "CropQc.Web", "Views", "MasterData", "_MasterDataFields.cshtml");
        var service = Read("src", "CropQc.Web", "Services", "TreatmentReportAttachmentService.cs");
        Assert.Contains("Receiving Treatment History", detail);
        Assert.Contains("Add Treatment Report", detail);
        Assert.Contains("Reverse Treatment", detail);
        Assert.Contains("Apply Receiving Treatment", apply);
        Assert.Contains("Treatment Report <span class=\"muted\">(optional)</span>", apply);
        Assert.Contains("class=\"room-metrics treatment-summary\"", apply);
        Assert.Contains("Application Level", master);
        Assert.Contains("TreatmentApplicationLevels.Receiving", service);
        Assert.Contains("ApplicationAreas.Receipts", service);
        var controller = Read("src", "CropQc.Web", "Controllers", "RoomTreatmentsController.cs");
        Assert.Contains("[Authorize]\n    public async Task<IActionResult> ReportContent", controller.Replace("\r\n", "\n"));
    }

    [Fact]
    public void Migration_schema_gate_and_compatibility_packages_are_bounded_and_history_safe()
    {
        var migration = Read("src", "CropQc.Data", "Migrations", "20260820194148_AddReceivingTreatmentApplications.cs");
        var preflight = Read("scripts", "postgresql", "preflight-receiving-treatment-applications.sql");
        var apply = Read("scripts", "postgresql", "apply-receiving-treatment-applications-schema.sql");
        var verify = Read("scripts", "postgresql", "verify-receiving-treatment-applications.sql");
        var config = Read("scripts", "postgresql", "apply-receiving-treatment-chemical-levels.sql");
        var gate = Read("src", "CropQc.Web", "Services", "DatabaseStartupDiagnostics.cs");
        Assert.Contains("AddReceivingTreatmentApplications", migration);
        Assert.DoesNotContain("migrationBuilder.Sql", migration);
        Assert.Contains("State C", preflight);
        Assert.Contains("state_a_absent_safe", preflight);
        Assert.Contains("state_b_complete_exact", preflight);
        Assert.Contains("BEGIN;", apply);
        Assert.Contains("pg_advisory_xact_lock", apply);
        Assert.Contains("Forced Receiving treatment compatibility failure", apply);
        Assert.Contains("17 AS checked_target_objects", verify);
        Assert.DoesNotContain("__EFMigrationsHistory", apply);
        Assert.DoesNotContain("__EFMigrationsHistory", config);
        Assert.Contains("20260820194148_AddReceivingTreatmentApplications", gate);
        Assert.Equal(517, gate.Split('\n').Count(x => x.TrimStart().StartsWith("new(", StringComparison.Ordinal)));
    }

    private static string Read(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine([directory.FullName, .. segments]);
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, segments));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public const int UserId = 90001;
        public const int WarehouseId = 90002;
        public const int RoomId = 90003;
        public const int FruitProfileId = 90004;
        public const long ReceiptAId = 90001;
        public const long ReceiptBId = 90002;
        public const int AppleMcpId = 11;
        public const int PearMcpId = 12;

        private Fixture(CropQcDbContext db, FakeLedger ledger, MutableAccess access, RoomTreatmentService service)
        {
            Db = db;
            Ledger = ledger;
            Access = access;
            Service = service;
        }

        public CropQcDbContext Db { get; }
        public FakeLedger Ledger { get; }
        public MutableAccess Access { get; }
        public RoomTreatmentService Service { get; }

        public static async Task<Fixture> CreateAsync(PageAccessLevel level = PageAccessLevel.Admin)
        {
            var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>()
                .UseInMemoryDatabase($"receiving-treatment-{Guid.NewGuid():N}").Options);
            await db.Database.EnsureCreatedAsync();
            var warehouse = new Warehouse { Id = WarehouseId, Code = "EBS", Name = "EBS" };
            var room = new Room { Id = RoomId, WarehouseId = WarehouseId, Warehouse = warehouse, Code = "EVANS-7", Name = "EVANS-7" };
            var fruit = new FruitProfile { Id = FruitProfileId, Name = "Gala", VarietyCode = "GALA", FruitType = "Apple", ProductionType = "Conventional" };
            var user = new User { Id = UserId, Email = ApplicationAreas.OwnerEmail, DisplayName = "Wes", Domain = "fruitandland.com", CreatedAt = Now };
            var receiptA = Receipt(ReceiptAId, "TR-RECEIVING-A", 40, warehouse, room, fruit);
            var receiptB = Receipt(ReceiptBId, "TR-RECEIVING-B", 60, warehouse, room, fruit);
            db.AddRange(warehouse, room, fruit, user, receiptA, receiptB,
                Chemical(AppleMcpId, "SMARTFRESH INBOX FLEX/250X5G/1.25KG", "Apples"),
                Chemical(PearMcpId, "SMARTFRESH INBOX FLEX/250X5G/1.25KG Pear", "Pears"));
            db.RoomInventoryAdjustments.AddRange(
                Adjustment(90001, receiptA, 40, 40),
                Adjustment(90002, receiptB, 60, 100));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var ledger = new FakeLedger();
            ledger.Replace(SnapshotValue(100));
            var access = new MutableAccess { Level = level };
            var context = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, ApplicationAreas.OwnerEmail)], "Test"))
            };
            var service = new RoomTreatmentService(db, ledger, access, new FixedHttpContextAccessor(context),
                new PacificBusinessTimeService(new FixedClock(Now)), NullLogger<RoomTreatmentService>.Instance);
            return new(db, ledger, access, service);
        }

        public ReceiptTreatmentApplyForm ReceiptForm(string key, int chemicalId, long receiptId = ReceiptAId) => new()
        {
            ReceiptId = receiptId,
            TreatmentChemicalId = chemicalId,
            AppliedAt = Now,
            OperationKey = key,
            ConfirmedReview = true
        };

        public RoomTreatmentApplyForm RoomForm(string key, int chemicalId) => new()
        {
            RoomId = RoomId,
            TreatmentChemicalId = chemicalId,
            AppliedAt = Now,
            OperationKey = key,
            ConfirmedReview = true
        };

        public RoomInventoryAdjustment Adjustment(long id, long receiptId, int change, int current, string type) => new()
        {
            Id = id,
            ReceiptId = receiptId,
            WarehouseId = WarehouseId,
            RoomId = RoomId,
            CropYear = 2026,
            FruitProfileId = FruitProfileId,
            GrowerName = "ROLOFF FARM-NAGLE CONV",
            LotNumber = "9350",
            VarietyCode = "GALA",
            ChangeAmount = change,
            NewBinCount = current,
            AdjustmentType = type,
            AdjustmentAt = Now,
            CreatedAt = Now,
            InventoryInvariantVersion = 1,
            InventoryOperationKey = $"receiving-treatment-adjustment-{id}"
        };

        public RoomInventoryLedgerSnapshot Snapshot(int bins) => SnapshotValue(bins);

        public ValueTask DisposeAsync() => Db.DisposeAsync();

        private static Receipt Receipt(long id, string number, int bins, Warehouse warehouse, Room room, FruitProfile fruit) => new()
        {
            Id = id,
            CropYear = 2026,
            ReceivedAt = Now.AddDays(-1),
            CompuTechReceiptId = number,
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            RoomId = room.Id,
            Room = room,
            FruitProfileId = fruit.Id,
            FruitProfile = fruit,
            GrowerNumber = "9350",
            GrowerName = "ROLOFF FARM-NAGLE CONV",
            LotCode = "9350",
            BinCount = bins,
            CreatedAt = Now.AddDays(-1),
            UpdatedAt = Now.AddDays(-1)
        };

        private static TreatmentChemical Chemical(int id, string product, string crop) => new()
        {
            Id = id,
            ProductName = product,
            CommonName = "MCP",
            Crop = crop,
            ApplicationLevel = TreatmentApplicationLevels.Receiving,
            Volume = 1,
            Unit = "BIN",
            UnitPrice = 1,
            Currency = "USD",
            IsActive = true,
            CreatedAt = Now,
            UpdatedAt = Now
        };

        private static RoomInventoryAdjustment Adjustment(long id, Receipt receipt, int change, int current) => new()
        {
            Id = id,
            Receipt = receipt,
            ReceiptId = receipt.Id,
            WarehouseId = WarehouseId,
            RoomId = RoomId,
            CropYear = 2026,
            FruitProfileId = FruitProfileId,
            GrowerName = receipt.GrowerName,
            LotNumber = receipt.LotCode,
            VarietyCode = "GALA",
            ChangeAmount = change,
            NewBinCount = current,
            AdjustmentType = "Receipt",
            AdjustmentAt = Now.AddDays(-1),
            CreatedAt = Now.AddDays(-1),
            InventoryInvariantVersion = 1,
            InventoryOperationKey = $"receiving-source-{id}"
        };

        private static RoomInventoryLedgerSnapshot SnapshotValue(int bins) => new(
            WarehouseId, "EBS", RoomId, "EVANS-7", "EVANS-7", 2026, null, FruitProfileId,
            "ROLOFF FARM-NAGLE CONV", "9350", "9350", null, "GALA", "GALA", "Gala", "Apple",
            "Conventional", false, "Conventional", bins, 0, 0, 0, 0, 0, 0, 0, 0,
            bins, 90002, Now.AddDays(-1), Now.AddDays(-1), 1);
    }

    private sealed class FakeLedger : IRoomInventoryLedgerQueryService
    {
        private readonly List<RoomInventoryLedgerSnapshot> current = [];
        public void Replace(RoomInventoryLedgerSnapshot snapshot) { current.Clear(); current.Add(snapshot); }
        public Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(int? warehouseId, IReadOnlyCollection<int>? roomIds, CancellationToken cancellationToken) =>
            Task.FromResult(Filter(warehouseId, roomIds, null));
        public Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(int? warehouseId, IReadOnlyCollection<int>? roomIds, int? fruitProfileId, CancellationToken cancellationToken) =>
            Task.FromResult(Filter(warehouseId, roomIds, fruitProfileId));
        public Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsOfAsync(int? warehouseId, IReadOnlyCollection<int>? roomIds, DateTimeOffset asOf, CancellationToken cancellationToken) =>
            Task.FromResult(Filter(warehouseId, roomIds, null));
        private IReadOnlyList<RoomInventoryLedgerSnapshot> Filter(int? warehouseId, IReadOnlyCollection<int>? roomIds, int? fruitProfileId) =>
            current.Where(x => warehouseId is null || x.WarehouseId == warehouseId)
                .Where(x => roomIds is null || roomIds.Contains(x.RoomId))
                .Where(x => fruitProfileId is null || x.FruitProfileId == fruitProfileId).ToList();
    }

    private sealed class MutableAccess : IUserAccessService
    {
        public PageAccessLevel Level { get; set; }
        public Task<bool> HasAccessAsync(ClaimsPrincipal principal, string areaKey, PageAccessLevel minimumLevel, CancellationToken cancellationToken) => Task.FromResult(Level >= minimumLevel);
        public Task<PageAccessLevel> GetAccessLevelAsync(string? email, string areaKey, CancellationToken cancellationToken) => Task.FromResult(Level);
        public void InvalidateAll() { }
    }

    private sealed class FixedHttpContextAccessor(HttpContext context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock { public DateTimeOffset UtcNow => utcNow; }
}
