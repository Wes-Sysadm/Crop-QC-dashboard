using System.Security.Claims;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Auth;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class RoomInventoryLossTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-12T06:00:00Z");

    [Fact]
    public async Task Dropped_bins_preserve_receipt_and_reduce_only_exact_packable_identity()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Service.CreateReviewedCorrectionAsync(fixture.Request("loss-1", 2), Fixture.AdminId, CancellationToken.None);

        Assert.True(result.Success, result.Error);
        Assert.Equal(28, (await fixture.Db.Receipts.FindAsync(Fixture.ReceiptId))!.BinCount);
        var loss = Assert.Single(await fixture.Db.RoomInventoryLosses.Include(x => x.InventoryAdjustments).ToListAsync());
        var adjustment = Assert.Single(loss.InventoryAdjustments);
        Assert.Equal(RoomInventoryLossTypes.Dropped, loss.LossType);
        Assert.Equal(-2, adjustment.ChangeAmount);
        Assert.Equal(28, adjustment.OldBinCount);
        Assert.Equal(26, adjustment.NewBinCount);
        Assert.Equal(loss.Id, adjustment.RoomInventoryLossId);
        var snapshots = await fixture.Ledger.GetSnapshotsAsync(Fixture.WarehouseId, [Fixture.RoomId], CancellationToken.None);
        Assert.Equal(26, snapshots.Single(x => x.FruitProfileId == Fixture.ConventionalFruitId).CurrentBins);
        Assert.Equal(2, snapshots.Single(x => x.FruitProfileId == Fixture.ConventionalFruitId).DroppedBins);
        Assert.Equal(10, snapshots.Single(x => x.FruitProfileId == Fixture.OrganicFruitId).CurrentBins);
        Assert.Empty(await fixture.Db.BinsRunEntries.ToListAsync());
        Assert.Empty(await fixture.Db.RoomTransfers.ToListAsync());
    }

    [Fact]
    public async Task Generic_room_loss_does_not_infer_receipt_from_latest_adjustment()
    {
        await using var fixture = await Fixture.CreateAsync();

        var error = await fixture.Service.CreateAsync(new RoomInventoryLossForm
        {
            OperationKey = "generic-room-loss",
            RoomId = Fixture.RoomId,
            InventoryAdjustmentId = 9810,
            ExpectedCurrentBins = 28,
            BinCount = 2,
            OccurredAt = null,
            Notes = "Generic room-level evidence"
        }, CancellationToken.None);

        Assert.Null(error);
        var loss = Assert.Single(await fixture.Db.RoomInventoryLosses.Include(x => x.InventoryAdjustments).ToListAsync());
        Assert.Null(loss.ReceiptId);
        Assert.Null(Assert.Single(loss.InventoryAdjustments).ReceiptId);
        Assert.Equal(28, (await fixture.Db.Receipts.FindAsync(Fixture.ReceiptId))!.BinCount);
    }

    [Fact]
    public async Task Manual_true_up_uses_canonical_identity_balance_and_refuses_reduction()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddSameIdentityReceiptAsync(29);
        var before = await fixture.Db.RoomInventoryAdjustments.CountAsync();

        var error = await fixture.DashboardService().CreateRoomInventoryTrueUpAsync(new RoomInventoryTrueUpForm
        {
            RoomId = Fixture.RoomId,
            ReceiptId = Fixture.ReceiptId,
            NewBinCount = 55,
            AdjustmentAt = Now,
            Reason = "Attempted manual reduction"
        }, CancellationToken.None);

        Assert.Contains("Dropped Bins, Bins Run, or Transfer", error);
        Assert.Equal(before, await fixture.Db.RoomInventoryAdjustments.CountAsync());
    }

    [Fact]
    public async Task Manual_true_up_allows_increase_from_canonical_identity_balance()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddSameIdentityReceiptAsync(29);

        var error = await fixture.DashboardService().CreateRoomInventoryTrueUpAsync(new RoomInventoryTrueUpForm
        {
            RoomId = Fixture.RoomId,
            ReceiptId = Fixture.ReceiptId,
            NewBinCount = 60,
            AdjustmentAt = Now,
            Reason = "Reviewed positive correction"
        }, CancellationToken.None);

        Assert.Null(error);
        var adjustment = await fixture.Db.RoomInventoryAdjustments.SingleAsync(x => x.AdjustmentType == "ManualTrueUp");
        Assert.Equal(57, adjustment.OldBinCount);
        Assert.Equal(3, adjustment.ChangeAmount);
        Assert.Equal(60, adjustment.NewBinCount);
    }

    [Fact]
    public async Task Overdraw_fails_before_any_write()
    {
        await using var fixture = await Fixture.CreateAsync();
        var result = await fixture.Service.CreateReviewedCorrectionAsync(fixture.Request("overdraw", 29), Fixture.AdminId, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("only 28", result.Error);
        Assert.Empty(await fixture.Db.RoomInventoryLosses.ToListAsync());
        Assert.Equal(2, await fixture.Db.RoomInventoryAdjustments.CountAsync());
        Assert.Empty(await fixture.Db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Duplicate_operation_key_is_idempotent_and_different_payload_is_rejected()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Service.CreateReviewedCorrectionAsync(fixture.Request("same-key", 2), Fixture.AdminId, CancellationToken.None);
        var second = await fixture.Service.CreateReviewedCorrectionAsync(fixture.Request("same-key", 2), Fixture.AdminId, CancellationToken.None);
        var different = await fixture.Service.CreateReviewedCorrectionAsync(fixture.Request("same-key", 3), Fixture.AdminId, CancellationToken.None);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.True(second.AlreadyApplied);
        Assert.Equal(first.LossId, second.LossId);
        Assert.False(different.Success);
        Assert.Single(await fixture.Db.RoomInventoryLosses.ToListAsync());
        Assert.Equal(3, await fixture.Db.RoomInventoryAdjustments.CountAsync());
    }

    [Fact]
    public async Task Admin_reversal_restores_inventory_without_changing_receipt_or_deleting_history()
    {
        await using var fixture = await Fixture.CreateAsync();
        var created = await fixture.Service.CreateReviewedCorrectionAsync(fixture.Request("reverse-source", 2), Fixture.AdminId, CancellationToken.None);
        var error = await fixture.Service.ReverseAsync(new ReverseRoomInventoryLossForm
        {
            Id = created.LossId!.Value,
            OperationKey = "reverse-once",
            Reason = "Recorded against the wrong pallet count"
        }, CancellationToken.None);
        var repeated = await fixture.Service.ReverseAsync(new ReverseRoomInventoryLossForm
        {
            Id = created.LossId.Value,
            OperationKey = "reverse-twice",
            Reason = "Duplicate attempt"
        }, CancellationToken.None);

        Assert.Null(error);
        Assert.Null(repeated);
        var loss = await fixture.Db.RoomInventoryLosses.Include(x => x.InventoryAdjustments).SingleAsync();
        Assert.True(loss.IsReversed);
        Assert.Equal(2, loss.InventoryAdjustments.Count);
        Assert.Contains(loss.InventoryAdjustments, x => x.AdjustmentType == RoomInventoryLossAdjustmentTypes.DroppedBinsReversal && x.ChangeAmount == 2);
        Assert.Equal(28, (await fixture.Db.Receipts.FindAsync(Fixture.ReceiptId))!.BinCount);
        var snapshot = (await fixture.Ledger.GetSnapshotsAsync(Fixture.WarehouseId, [Fixture.RoomId], Fixture.ConventionalFruitId, CancellationToken.None)).Single();
        Assert.Equal(28, snapshot.CurrentBins);
        Assert.Equal(2, snapshot.DroppedBinsRestored);
        Assert.Equal(2, await fixture.Db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task Ledger_reconciliation_names_loss_parent_and_excludes_drop_from_other_adjustments()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.True((await fixture.Service.CreateReviewedCorrectionAsync(fixture.Request("reconcile", 2), Fixture.AdminId, CancellationToken.None)).Success);

        var page = await new RoomInventoryReconciliationService(fixture.Db, fixture.Ledger, fixture.Invariant)
            .GetPageAsync(new RoomInventoryReconciliationFilter { RoomId = Fixture.RoomId }, CancellationToken.None);
        Assert.Contains(page.NegativeAdjustments, x => x.ParentType == "Room Inventory Loss" && x.ParentMatches && x.Quantity == 2);
        Assert.Contains(page.Rows, x => x.DroppedBins == 2 && x.OtherAdjustmentBins == 28 && x.LedgerBalance == 26);
    }

    [Fact]
    public void Endpoints_require_existing_permissions_and_antiforgery()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "HomeController.cs"));
        Assert.Contains("AccessPolicyNames.RoomTransactionsEdit", source);
        Assert.Contains("AccessPolicyNames.RoomTransactionsAdmin", source);
        Assert.Equal(2, source.Split("[ValidateAntiForgeryToken]", StringSplitOptions.None).Length - 1);
        Assert.Contains("The original receipt quantity will remain unchanged", File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "Room.cshtml")));
    }

    [Fact]
    public async Task Invariant_rejects_orphan_and_wrong_identity_loss_adjustments()
    {
        await using var fixture = await Fixture.CreateAsync();
        var orphan = fixture.SourceAdjustment(99, Fixture.ConventionalFruitId, 28);
        orphan.ChangeAmount = -2;
        orphan.OldBinCount = 28;
        orphan.NewBinCount = 26;
        orphan.AdjustmentType = RoomInventoryLossAdjustmentTypes.DroppedBins;
        orphan.InventoryInvariantVersion = InventoryDeductionInvariantService.CurrentVersion;
        orphan.InventoryOperationKey = "orphan-loss";
        fixture.Db.RoomInventoryAdjustments.Add(orphan);
        await Assert.ThrowsAsync<InventoryDeductionInvariantException>(() => fixture.Invariant.ValidateBeforeCommitAsync(CancellationToken.None));
        fixture.Db.ChangeTracker.Clear();

        var created = await fixture.Service.CreateReviewedCorrectionAsync(fixture.Request("wrong-identity", 2), Fixture.AdminId, CancellationToken.None);
        Assert.True(created.Success);
        var persisted = await fixture.Db.RoomInventoryAdjustments.SingleAsync(x => x.RoomInventoryLossId == created.LossId && x.ChangeAmount < 0);
        persisted.FruitProfileId = Fixture.OrganicFruitId;
        await Assert.ThrowsAsync<InventoryDeductionInvariantException>(() => fixture.Invariant.ValidateBeforeCommitAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Unauthorized_user_cannot_record_dropped_bins()
    {
        await using var fixture = await Fixture.CreateAsync("viewer@fruitandland.com");
        var error = await fixture.Service.CreateAsync(new RoomInventoryLossForm
        {
            OperationKey = "unauthorized",
            RoomId = Fixture.RoomId,
            InventoryAdjustmentId = 9810,
            ExpectedCurrentBins = 28,
            BinCount = 2
        }, CancellationToken.None);

        Assert.Equal("Room Transactions Edit access is required to mark bins dropped.", error);
        Assert.Empty(await fixture.Db.RoomInventoryLosses.ToListAsync());
        Assert.Equal(2, await fixture.Db.RoomInventoryAdjustments.CountAsync());
        Assert.Empty(await fixture.Db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task Invariant_failure_rolls_back_loss_adjustment_and_audit()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.ServiceWithInvariant(new RejectingInvariantService());

        var result = await service.CreateReviewedCorrectionAsync(fixture.Request("rollback-all", 2), Fixture.AdminId, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Empty(await fixture.Db.RoomInventoryLosses.ToListAsync());
        Assert.Equal(2, await fixture.Db.RoomInventoryAdjustments.CountAsync());
        Assert.Empty(await fixture.Db.AuditLogs.ToListAsync());
        Assert.Equal(28, (await fixture.Db.Receipts.FindAsync(Fixture.ReceiptId))!.BinCount);
    }

    [Fact]
    public async Task Tr108859_correction_refuses_later_true_up_with_zero_writes()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddManualTrueUpAsync(218);
        var beforeAdjustments = await fixture.Db.RoomInventoryAdjustments.CountAsync();
        var beforeAudits = await fixture.Db.AuditLogs.CountAsync();

        var result = await fixture.CorrectionService().RunAsync(
            fixture.CorrectionRequest(apply: true, targetFingerprint: "not-used", protectedFingerprint: "not-used"),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Refused", result.Preflight.State);
        Assert.Equal(246, result.Preflight.Evidence!.CurrentLedgerBalance);
        Assert.NotEmpty(result.Preflight.Issues);
        Assert.Empty(await fixture.Db.RoomInventoryLosses.ToListAsync());
        Assert.Equal(beforeAdjustments, await fixture.Db.RoomInventoryAdjustments.CountAsync());
        Assert.Equal(beforeAudits, await fixture.Db.AuditLogs.CountAsync());
        Assert.Equal(28, (await fixture.Db.Receipts.FindAsync(Fixture.ReceiptId))!.BinCount);
    }

    [Fact]
    public async Task Tr108859_case_a_rehearsal_applies_once_and_rerun_is_zero_write()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.AddVerifiedRun64BackupAsync();
        var correction = fixture.CorrectionService();
        var preflight = await correction.PreflightAsync(CancellationToken.None);
        Assert.Equal("Ready", preflight.State);

        var first = await correction.RunAsync(
            fixture.CorrectionRequest(true, preflight.TargetFingerprint, preflight.ProtectedFingerprint),
            CancellationToken.None);
        var adjustmentCount = await fixture.Db.RoomInventoryAdjustments.CountAsync();
        var auditCount = await fixture.Db.AuditLogs.CountAsync();
        var second = await correction.RunAsync(
            fixture.CorrectionRequest(true, first.Preflight.TargetFingerprint, first.Preflight.ProtectedFingerprint),
            CancellationToken.None);

        Assert.True(first.Success, first.Message);
        Assert.True(first.Applied);
        Assert.Equal("AlreadyApplied", first.Preflight.State);
        Assert.Equal(28, first.Preflight.Evidence!.BinCount);
        Assert.Equal(26, first.Preflight.Evidence.CurrentLedgerBalance);
        Assert.True(second.Success, second.Message);
        Assert.True(second.AlreadyApplied);
        Assert.False(second.Applied);
        Assert.Equal(adjustmentCount, await fixture.Db.RoomInventoryAdjustments.CountAsync());
        Assert.Equal(auditCount, await fixture.Db.AuditLogs.CountAsync());
        Assert.Equal(28, (await fixture.Db.Receipts.FindAsync(Fixture.ReceiptId))!.BinCount);
        Assert.Single(await fixture.Db.RoomInventoryLosses.ToListAsync());
    }

    [Fact]
    public void Receipt_and_room_views_present_dropped_bins_as_a_separate_fact()
    {
        var room = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "Room.cshtml"));
        var receipt = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Receipts", "Details.cshtml"));
        var reconciliation = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "RoomInventory", "Reconciliation.cshtml"));

        Assert.Contains("Mark bins dropped", room);
        Assert.Contains("The original receipt quantity will remain unchanged", room);
        Assert.Contains("Dropped after receiving", receipt);
        Assert.Contains("Current packable inventory", receipt);
        Assert.Contains("Dropped / Restored", reconciliation);
    }

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
        public const int AdminId = 9801;
        public const int WarehouseId = 9802;
        public const int RoomId = 9803;
        public const int ConventionalFruitId = 9804;
        public const int OrganicFruitId = 9805;
        public const long ReceiptId = 9806;
        private readonly SqliteConnection connection;

        private Fixture(SqliteConnection connection, CropQcDbContext db, string principalEmail)
        {
            this.connection = connection;
            Db = db;
            Ledger = new TestLedgerQueryService(db);
            Invariant = new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance);
            Accessor = new FixedHttpContextAccessor(new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Email, principalEmail)], "Test"))
            });
            Service = ServiceWithInvariant(Invariant);
        }

        public CropQcDbContext Db { get; }
        public IRoomInventoryLedgerQueryService Ledger { get; }
        public InventoryDeductionInvariantService Invariant { get; }
        public RoomInventoryLossService Service { get; }
        private FixedHttpContextAccessor Accessor { get; }

        public static async Task<Fixture> CreateAsync(string principalEmail = ApplicationAreas.OwnerEmail)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var warehouse = new Warehouse { Id = WarehouseId, Code = "LOSS-EBS", Name = "Loss Test Evans" };
            var room = new Room { Id = RoomId, Warehouse = warehouse, WarehouseId = WarehouseId, Code = "LOSS-EVANS-7", Name = "Loss Test Evans 7" };
            var conventional = new FruitProfile { Id = ConventionalFruitId, Name = "Gala", VarietyCode = "LOSS-GALA", FruitType = "Apple", ProductionType = "Conventional" };
            var organic = new FruitProfile { Id = OrganicFruitId, Name = "Gala", VarietyCode = "LOSS-ORGA", FruitType = "Apple", ProductionType = "Organic", IsOrganic = true };
            var admin = new User { Id = AdminId, Email = ApplicationAreas.OwnerEmail, DisplayName = "Wes", Domain = "fruitandland.com", CreatedAt = Now };
            var viewer = new User { Id = AdminId + 1, Email = "viewer@fruitandland.com", DisplayName = "Viewer", Domain = "fruitandland.com", CreatedAt = Now };
            var adminRole = await db.Roles.SingleAsync(x => x.Name == BuiltInRoleNames.Admin);
            var receipt = Receipt(ReceiptId, "TR108859", conventional, 28);
            var organicReceipt = Receipt(ReceiptId + 1, "ORGANIC-SAME-VARIETY", organic, 10);
            db.AddRange(warehouse, room, conventional, organic, admin, viewer, receipt, organicReceipt);
            db.UserRoles.Add(new UserRole { User = admin, Role = adminRole });
            db.RoomInventoryAdjustments.AddRange(
                SourceAdjustmentStatic(9810, receipt, 28),
                SourceAdjustmentStatic(9811, organicReceipt, 10));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return new Fixture(connection, db, principalEmail);

            Receipt Receipt(long id, string reference, FruitProfile fruit, int bins) => new()
            {
                Id = id,
                CropYear = 2026,
                ReceivedAt = Now.AddDays(-2),
                CompuTechReceiptId = reference,
                Warehouse = warehouse,
                WarehouseId = WarehouseId,
                Room = room,
                RoomId = RoomId,
                FruitProfile = fruit,
                FruitProfileId = fruit.Id,
                GrowerNumber = "9040",
                GrowerName = "DL & JJ FARMS - CLARENCE",
                LotCode = "9040",
                BinCount = bins,
                CreatedAt = Now.AddDays(-2),
                UpdatedAt = Now.AddDays(-2)
            };
        }

        public RoomInventoryLossCreateRequest Request(string key, int bins) => new(
            key, RoomId, 9810, 28, bins, null, "Bins were dropped", null, ReceiptId, "Test");

        public RoomInventoryLossService ServiceWithInvariant(IInventoryDeductionInvariantService invariant) => new(
            Db, Ledger, invariant,
            new UserAccessService(Db, new ConfigurationBuilder().Build()),
            Accessor,
            new PacificBusinessTimeService(new FixedClock(Now)),
            NullLogger<RoomInventoryLossService>.Instance);

        public DashboardDataService DashboardService()
        {
            var configuration = new ConfigurationBuilder().Build();
            return new DashboardDataService(
                Db,
                null!,
                new CropQc.Shared.Storage.FileStorageOptions(),
                new EmailOptions(),
                null!,
                new GoogleAuthenticationOptions(),
                null!,
                null!,
                new QcPhotoRequirementPolicy(),
                null!,
                new CropYearService(Db, configuration),
                Accessor,
                configuration,
                NullLogger<DashboardDataService>.Instance,
                new UserAccessService(Db, configuration),
                roomInventoryLedgerQueryService: Ledger);
        }

        public Tr108859DroppedBinsCorrectionService CorrectionService(bool production = false) => new(
            Db,
            new AppEnvironmentOptions { Kind = production ? AppEnvironmentKinds.Production : AppEnvironmentKinds.Development },
            new PacificBusinessTimeService(new FixedClock(Now)),
            Ledger,
            Service,
            NullLogger<Tr108859DroppedBinsCorrectionService>.Instance);

        public Tr108859DroppedBinsCorrectionRequest CorrectionRequest(bool apply, string targetFingerprint, string protectedFingerprint) => new(
            apply,
            false,
            true,
            Tr108859DroppedBinsCorrectionConstants.VerifiedRestoreBackupRunId,
            Tr108859DroppedBinsCorrectionConstants.VerifiedRestorePackageSha256,
            ApplicationAreas.OwnerEmail,
            "Record the reviewed two-bin dropped loss while preserving 28 received.",
            targetFingerprint,
            protectedFingerprint,
            Tr108859DroppedBinsCorrectionConstants.ApplyAuthorizationToken);

        public async Task AddVerifiedRun64BackupAsync()
        {
            Db.BackupRunRecords.Add(new BackupRunRecord
            {
                Id = Tr108859DroppedBinsCorrectionConstants.VerifiedRestoreBackupRunId,
                BackupType = BackupRunTypes.PreDeployment,
                Status = BackupRunStatuses.Succeeded,
                EnvironmentName = "Disposable restored production",
                DatabaseProvider = "PostgreSQL",
                RetentionCategory = BackupRunTypes.PreDeployment,
                StartedAt = Now.AddMinutes(-5),
                CompletedAt = Now,
                VerifiedAt = Now,
                RetentionProcessedAt = Now,
                LeaseReleasedAt = Now,
                Sha256 = Tr108859DroppedBinsCorrectionConstants.VerifiedRestorePackageSha256
            });
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async Task AddManualTrueUpAsync(int bins)
        {
            var adjustment = SourceAdjustment(9812, ConventionalFruitId, bins);
            adjustment.OldBinCount = 28;
            adjustment.NewBinCount = 28 + bins;
            adjustment.AdjustmentType = "ManualTrueUp";
            adjustment.Reason = "Two Dropped Bins";
            adjustment.AdjustmentAt = Now.AddDays(-1);
            Db.RoomInventoryAdjustments.Add(adjustment);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async Task AddSameIdentityReceiptAsync(int bins)
        {
            var original = await Db.Receipts.Include(x => x.FruitProfile).SingleAsync(x => x.Id == ReceiptId);
            var receipt = new Receipt
            {
                Id = ReceiptId + 10,
                CropYear = original.CropYear,
                ReceivedAt = original.ReceivedAt.AddHours(1),
                CompuTechReceiptId = "SAME-CANONICAL-IDENTITY",
                WarehouseId = original.WarehouseId,
                RoomId = original.RoomId,
                FruitProfileId = original.FruitProfileId,
                GrowerLotId = original.GrowerLotId,
                GrowerNumber = original.GrowerNumber,
                GrowerName = original.GrowerName,
                LotCode = original.LotCode,
                BinCount = bins,
                CreatedAt = original.CreatedAt.AddHours(1),
                UpdatedAt = original.UpdatedAt.AddHours(1)
            };
            Db.Receipts.Add(receipt);
            Db.RoomInventoryAdjustments.Add(SourceAdjustmentStatic(9812, receipt, bins));
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public RoomInventoryAdjustment SourceAdjustment(long id, int fruitId, int bins)
        {
            var receipt = Db.Receipts.Local.SingleOrDefault(x => x.FruitProfileId == fruitId)
                ?? Db.Receipts.Include(x => x.FruitProfile).Single(x => x.FruitProfileId == fruitId);
            return SourceAdjustmentStatic(id, receipt, bins);
        }

        private static RoomInventoryAdjustment SourceAdjustmentStatic(long id, Receipt receipt, int bins) => new()
        {
            Id = id,
            Receipt = receipt,
            ReceiptId = receipt.Id,
            CropYear = receipt.CropYear,
            WarehouseId = receipt.WarehouseId,
            RoomId = receipt.RoomId,
            FruitProfileId = receipt.FruitProfileId,
            GrowerName = receipt.GrowerName,
            LotNumber = receipt.GrowerNumber!,
            VarietyCode = receipt.FruitProfile.VarietyCode,
            InventoryStatus = receipt.FruitProfile.ProductionType,
            OldBinCount = null,
            ChangeAmount = bins,
            NewBinCount = bins,
            AdjustmentType = "ReceiptAdd",
            AdjustmentAt = receipt.ReceivedAt,
            CreatedAt = receipt.CreatedAt
        };

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock { public DateTimeOffset UtcNow => utcNow; }

    private sealed class FixedHttpContextAccessor(HttpContext context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }

    private sealed class RejectingInvariantService : IInventoryDeductionInvariantService
    {
        public Task ValidateBeforeCommitAsync(CancellationToken cancellationToken) =>
            throw new InventoryDeductionInvariantException("Injected invariant failure.");

        public Task<InventoryDeductionReadinessResult> VerifyReadinessAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new InventoryDeductionReadinessResult(0, 0, 0, []));
    }

    private sealed class TestLedgerQueryService(CropQcDbContext db) : IRoomInventoryLedgerQueryService
    {
        public Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(int? warehouseId, IReadOnlyCollection<int>? roomIds, CancellationToken cancellationToken) =>
            GetSnapshotsAsync(warehouseId, roomIds, null, cancellationToken);

        public async Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(int? warehouseId, IReadOnlyCollection<int>? roomIds, int? fruitProfileId, CancellationToken cancellationToken)
        {
            var rows = await db.RoomInventoryAdjustments.AsNoTracking()
                .Include(x => x.Warehouse).Include(x => x.Room).Include(x => x.FruitProfile).Include(x => x.Receipt).ThenInclude(x => x!.FruitProfile)
                .Where(x => warehouseId == null || x.WarehouseId == warehouseId)
                .Where(x => roomIds == null || roomIds.Contains(x.RoomId))
                .Where(x => fruitProfileId == null || x.FruitProfileId == fruitProfileId)
                .ToListAsync(cancellationToken);
            return rows.GroupBy(x => new { x.WarehouseId, x.RoomId, x.CropYear, x.GrowerLotId, x.FruitProfileId, x.LotNumber })
                .Select(group =>
                {
                    var latest = group.OrderByDescending(x => x.AdjustmentAt).ThenByDescending(x => x.Id).First();
                    var profile = latest.FruitProfile ?? latest.Receipt?.FruitProfile;
                    var dropped = group.Where(x => x.AdjustmentType == RoomInventoryLossAdjustmentTypes.DroppedBins).Sum(x => -x.ChangeAmount);
                    var restored = group.Where(x => x.AdjustmentType == RoomInventoryLossAdjustmentTypes.DroppedBinsReversal).Sum(x => x.ChangeAmount);
                    var current = group.Sum(x => x.ChangeAmount);
                    return new RoomInventoryLedgerSnapshot(
                        latest.WarehouseId, latest.Warehouse.Code, latest.RoomId,
                        latest.Room.CropQcRoomName ?? latest.Room.DisplayName ?? latest.Room.Code, "",
                        latest.CropYear, latest.GrowerLotId, latest.FruitProfileId, latest.GrowerName,
                        latest.Receipt?.GrowerNumber, latest.LotNumber, latest.PoolStart, latest.VarietyCode ?? "",
                        profile?.VarietyCode ?? latest.VarietyCode ?? "", profile?.Name ?? latest.VarietyCode ?? "",
                        profile?.FruitType ?? "", profile?.ProductionType ?? latest.InventoryStatus ?? "", profile?.IsOrganic,
                        latest.InventoryStatus ?? "", group.Where(x => x.ChangeAmount > 0).Sum(x => x.ChangeAmount),
                        group.Where(x => x.ChangeAmount < 0).Sum(x => x.ChangeAmount), 0, 0, 0, 0, 0, 0,
                        current + dropped - restored, current, group.Count(), group.Min(x => x.AdjustmentAt), group.Max(x => x.AdjustmentAt), latest.Id,
                        latest.Source ?? "", dropped, restored);
                }).ToList();
        }
    }
}
