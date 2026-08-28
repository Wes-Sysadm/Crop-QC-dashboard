using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class BinsRunReversal1820CorrectionTests
{
    [Fact]
    public async Task ExactProductionShape_AppliesOneMetadataChange_ThenRerunsAlreadyApplied()
    {
        await using var fixture = await Fixture.CreateAsync();
        var preflight = await fixture.Service.PreflightAsync(default);
        Assert.True(preflight.State == "Ready", $"State={preflight.State}: {string.Join("; ", preflight.Issues)}");
        Assert.Empty(preflight.Issues);
        Assert.Null(preflight.Evidence!.InventoryStatus);
        Assert.Equal("Conventional", preflight.Evidence.ParentInventoryStatus);
        Assert.Equal("Conventional", preflight.Evidence.OriginalInventoryStatus);
        var protectedFingerprint = preflight.ProtectedFingerprint;
        var adjustmentSum = preflight.Evidence.AdjustmentChangeAmountSum;
        var binsRunSum = preflight.Evidence.BinsRunQuantitySum;
        var receiptSum = preflight.Evidence.ReceiptQuantitySum;

        var applied = await fixture.Service.RunAsync(fixture.Request(preflight), default);

        Assert.True(applied.Success);
        Assert.True(applied.Applied);
        Assert.False(applied.AlreadyApplied);
        Assert.Equal("AlreadyApplied", applied.Preflight.State);
        Assert.Equal(protectedFingerprint, applied.Preflight.ProtectedFingerprint);
        Assert.Equal(adjustmentSum, applied.Preflight.Evidence!.AdjustmentChangeAmountSum);
        Assert.Equal(binsRunSum, applied.Preflight.Evidence.BinsRunQuantitySum);
        Assert.Equal(receiptSum, applied.Preflight.Evidence.ReceiptQuantitySum);
        Assert.Equal("Conventional", (await fixture.Db.RoomInventoryAdjustments.FindAsync(1820L))!.InventoryStatus);
        Assert.Equal("Conventional", (await fixture.Db.BinsRunEntries.FindAsync(164L))!.InventoryStatus);
        Assert.Equal("Conventional", (await fixture.Db.BinsRunEntries.FindAsync(42L))!.InventoryStatus);
        Assert.Single(await fixture.Db.AuditLogs.Where(x => x.SourceApplication == BinsRunReversal1820CorrectionConstants.AuditSource).ToListAsync());

        var writes = await fixture.Db.AuditLogs.CountAsync();
        var rerun = await fixture.Service.RunAsync(fixture.Request(applied.Preflight), default);
        Assert.True(rerun.Success);
        Assert.True(rerun.AlreadyApplied);
        Assert.False(rerun.Applied);
        Assert.Equal(writes, await fixture.Db.AuditLogs.CountAsync());
    }

    [Theory]
    [InlineData("adjustment")]
    [InlineData("parent")]
    [InlineData("original")]
    [InlineData("fruit")]
    [InlineData("conflicting-status")]
    public async Task ChangedEvidence_IsStateCAndWritesNothing(string mutation)
    {
        await using var fixture = await Fixture.CreateAsync();
        switch (mutation)
        {
            case "adjustment": (await fixture.Db.RoomInventoryAdjustments.FindAsync(1820L))!.ChangeAmount = 9; break;
            case "parent": (await fixture.Db.BinsRunEntries.FindAsync(164L))!.NewAvailableBins = 31; break;
            case "original": (await fixture.Db.BinsRunEntries.FindAsync(42L))!.IsReversed = false; break;
            case "fruit": (await fixture.Db.FruitProfiles.FindAsync(17))!.IsOrganic = true; break;
            case "conflicting-status": (await fixture.Db.RoomInventoryAdjustments.FindAsync(1820L))!.InventoryStatus = "Organic"; break;
        }
        await fixture.Db.SaveChangesAsync();

        var preflight = await fixture.Service.PreflightAsync(default);
        var result = await fixture.Service.RunAsync(fixture.Request(preflight), default);

        Assert.Equal("Refused", preflight.State);
        Assert.False(result.Success);
        Assert.Empty(await fixture.Db.AuditLogs.ToListAsync());
    }

    [Fact]
    public void Command_IsNarrowAndNoMigrationWasAdded()
    {
        var root = RepositoryRoot();
        var program = File.ReadAllText(Path.Combine(root, "src", "CropQc.Web", "Program.cs"));
        var service = File.ReadAllText(Path.Combine(root, "src", "CropQc.Web", "Services", "BinsRunReversal1820CorrectionService.cs"));
        Assert.Contains("BinsRunReversal1820CorrectionConstants.CommandName", program);
        Assert.Contains("AdjustmentId = 1820", service);
        Assert.Contains("ParentEntryId = 164", service);
        Assert.Contains("OriginalEntryId = 42", service);
        Assert.DoesNotContain("Controller", service);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CropQc.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private const string BackupSha = "7c9545aa841679a5970938ae93e338f74bb1eb719930e9162137155b9a9c7c1d";
        public CropQcDbContext Db { get; }
        public BinsRunReversal1820CorrectionService Service { get; }

        private Fixture(CropQcDbContext db)
        {
            Db = db;
            Service = new BinsRunReversal1820CorrectionService(
                db,
                new AppEnvironmentOptions { Kind = AppEnvironmentKinds.Development },
                new PacificBusinessTimeService(new FixedClock(new DateTimeOffset(2026, 8, 28, 8, 0, 0, TimeSpan.Zero))),
                NullLogger<BinsRunReversal1820CorrectionService>.Instance);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>()
                .UseInMemoryDatabase($"bins-run-reversal-1820-{Guid.NewGuid():N}").Options);
            await db.Database.EnsureCreatedAsync();
            var fixture = new Fixture(db);
            await fixture.SeedAsync();
            return fixture;
        }

        public BinsRunReversal1820CorrectionRequest Request(BinsRunReversal1820CorrectionPreflight preflight) => new(
            true,
            false,
            true,
            110,
            BackupSha,
            "admin@example.test",
            "Align the reviewed historical reversal metadata with its persisted Conventional transaction identity.",
            preflight.TargetFingerprint,
            preflight.ProtectedFingerprint,
            BinsRunReversal1820CorrectionConstants.ApplyAuthorizationToken);

        private async Task SeedAsync()
        {
            var at = DateTimeOffset.Parse("2026-08-27T18:24:45.191766Z");
            var warehouse = await Db.Warehouses.FindAsync(4)
                ?? new Warehouse { Id = 4, Code = "WP", Name = "Windy Point", IsActive = true };
            warehouse.Code = "WP";
            warehouse.Name = "Windy Point";
            warehouse.IsActive = true;
            if (Db.Entry(warehouse).State == EntityState.Detached) Db.Warehouses.Add(warehouse);
            var room = await Db.Rooms.FindAsync(1)
                ?? new Room { Id = 1, Warehouse = warehouse, Code = "WP-1", Name = "WP-1", IsActive = true };
            room.Warehouse = warehouse;
            room.Code = "WP-1";
            room.Name = "WP-1";
            room.IsActive = true;
            if (Db.Entry(room).State == EntityState.Detached) Db.Rooms.Add(room);
            var fruit = await Db.FruitProfiles.FindAsync(17)
                ?? new FruitProfile { Id = 17, VarietyCode = "BART", Name = "Bartlett", FruitType = "Pear", ProductionType = "Conventional", IsOrganic = false, IsActive = true };
            fruit.VarietyCode = "BART";
            fruit.Name = "Bartlett";
            fruit.FruitType = "Pear";
            fruit.ProductionType = "Conventional";
            fruit.IsOrganic = false;
            fruit.IsActive = true;
            if (Db.Entry(fruit).State == EntityState.Detached) Db.FruitProfiles.Add(fruit);
            var admin = new User { Id = 9000, Email = "admin@example.test", DisplayName = "Correction Admin", Domain = "example.test", EmploymentFacility = "WP", CreatedAt = at };
            var role = await Db.Roles.SingleOrDefaultAsync(x => x.Name == BuiltInRoleNames.Admin)
                ?? new Role { Id = 9000, Name = BuiltInRoleNames.Admin, NormalizedName = BuiltInRoleNames.Normalize(BuiltInRoleNames.Admin), IsSystemRole = true, IsActive = true };
            role.IsActive = true;
            admin.UserRoles.Add(new UserRole { User = admin, Role = role });
            var run = new ActualRun { Id = 9, Status = ActualRunStatuses.Active, CurrentRevisionNumber = 2, RunAt = at.AddDays(-1), CreatedAt = at.AddDays(-1), CreatedByUser = admin };
            var originalRevision = new ActualRunRevision { Id = 9, ActualRun = run, RevisionNumber = 1, OperationType = ActualRunRevisionTypes.Create, OperationKey = "actual-run-9-create", CreatedAt = at.AddDays(-1), CreatedByUser = admin };
            var reversalRevision = new ActualRunRevision { Id = 55, ActualRun = run, RevisionNumber = 2, OperationType = ActualRunRevisionTypes.Edit, OperationKey = "actual-run-9-reversal", IsCurrent = true, CreatedAt = at, CreatedByUser = admin };
            var originalAdjustment = Adjustment(500, -8, 24, 16, BinsRunService.AdjustmentType, "Actual Run #9", "Conventional", originalRevision);
            var reversalAdjustment = Adjustment(1820, 8, 22, 30, BinsRunService.ReversalAdjustmentType, "Actual Run #9 reversal", null, reversalRevision);
            var original = Entry(42, originalAdjustment, originalRevision, 24, 16, ActualRunTransactionTypes.Depletion, "Conventional");
            original.IsReversed = true;
            var reversal = Entry(164, reversalAdjustment, reversalRevision, 22, 30, ActualRunTransactionTypes.Reversal, "Conventional");
            reversal.ReversesBinsRunEntryId = 42;
            Db.AddRange(admin, run, originalRevision, reversalRevision, originalAdjustment, reversalAdjustment, original, reversal);
            Db.Receipts.Add(new Receipt { Id = 208, CropYear = 2026, ReceivedAt = at.AddDays(-30), CompuTechReceiptId = "TR-TEST", ReceiptType = "Truck receipt", Warehouse = warehouse, Room = room, FruitProfile = fruit, GrowerName = "BALDWIN PEARS CONV", LotCode = "1532", BinCount = 28, CreatedAt = at.AddDays(-30), UpdatedAt = at.AddDays(-30) });
            Db.BackupRunRecords.Add(new BackupRunRecord { Id = 110, BackupType = BackupRunTypes.PreDeployment, Status = BackupRunStatuses.Succeeded, EnvironmentName = "DisposableRestore", DatabaseProvider = "PostgreSQL", RetentionCategory = "Protected", StartedAt = at, CompletedAt = at, Sha256 = BackupSha, VerifiedAt = at, RetentionProcessedAt = at, LeaseReleasedAt = at });
            await Db.SaveChangesAsync();
            reversalAdjustment.InventoryStatus = null;
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();

            RoomInventoryAdjustment Adjustment(long id, int change, int oldBins, int newBins, string type, string source, string? status, ActualRunRevision revision) => new()
            {
                Id = id,
                CropYear = 2026,
                Warehouse = warehouse,
                Room = room,
                FruitProfile = fruit,
                GrowerName = "BALDWIN PEARS CONV",
                LotNumber = "1532",
                VarietyCode = "BART",
                OldBinCount = oldBins,
                ChangeAmount = change,
                NewBinCount = newBins,
                AdjustmentType = type,
                Source = source,
                Reason = type == BinsRunService.ReversalAdjustmentType ? "Actual Run revision" : "Create",
                Notes = "ENTER WRON GROWER",
                InventoryStatus = status,
                AdjustmentAt = at,
                CreatedAt = at,
                InventoryInvariantVersion = 1,
                ActualRun = run,
                ActualRunRevision = revision
            };

            BinsRunEntry Entry(long id, RoomInventoryAdjustment adjustment, ActualRunRevision revision, int previous, int next, string type, string? status) => new()
            {
                Id = id,
                InventoryAdjustment = adjustment,
                Warehouse = warehouse,
                Room = room,
                CropYear = 2026,
                FruitProfile = fruit,
                GrowerName = "BALDWIN PEARS CONV",
                LotNumber = "1532",
                VarietyCode = "BART",
                InventoryStatus = status,
                PreviousAvailableBins = previous,
                BinsRun = 8,
                NewAvailableBins = next,
                Notes = "ENTER WRON GROWER",
                RunAt = at,
                CreatedAt = at,
                ActualRun = run,
                ActualRunRevision = revision,
                TransactionType = type
            };
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
