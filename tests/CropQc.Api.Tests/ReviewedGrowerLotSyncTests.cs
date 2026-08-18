using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class ReviewedGrowerLotSyncTests
{
    [Fact]
    public async Task Sync_CreatesMissingUpdatesNameDeactivatesSafeLegacyAndPreservesIdentityAndPoolStart()
    {
        await using var fixture = await Fixture.CreateAsync();
        var existing = await fixture.Db.GrowerLots.SingleAsync(x => x.LotNumber == "100");
        var existingId = existing.Id;
        var receipt = await fixture.AddReceiptAsync(existingId);
        var receiptId = receipt.Id;
        var receiptGrowerLotId = receipt.GrowerLotId;
        var receiptNumber = receipt.GrowerNumber;
        var receiptLot = receipt.LotCode;

        var dryRun = await fixture.Service.RunAsync(Request(false), CancellationToken.None);

        Assert.True(dryRun.Success);
        Assert.Equal("Ready", dryRun.Preflight.State);
        Assert.Equal(2, dryRun.Preflight.ReviewedActiveGrowerCount);
        Assert.Equal(1, dryRun.Preflight.MissingGrowerLotsToCreate);
        Assert.Equal(1, dryRun.Preflight.NamesToUpdate);
        Assert.Equal(0, dryRun.Preflight.RowsToActivate);
        Assert.Equal(1, dryRun.Preflight.RowsToDeactivate);
        Assert.Equal(0, dryRun.Preflight.DuplicateOrConflictCount);
        Assert.Equal(0, dryRun.Preflight.ExistingPoolStartChanges);
        Assert.Equal(0, dryRun.Preflight.HistoricalForeignKeyChanges);

        var applied = await fixture.Service.RunAsync(Request(true, dryRun.Preflight), CancellationToken.None);

        Assert.True(applied.Success, applied.Message);
        Assert.True(applied.Applied);
        Assert.Equal(1, applied.Created);
        Assert.Equal(1, applied.Updated);
        Assert.Equal(0, applied.Activated);
        Assert.Equal(1, applied.Deactivated);
        var active = await fixture.Db.GrowerLots.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.LotNumber).ToListAsync();
        Assert.Equal(new[] { "100", "200" }, active.Select(x => x.LotNumber));
        Assert.Equal(new[] { "Reviewed One", "Reviewed Two" }, active.Select(x => x.Grower));
        Assert.Equal(existingId, active.Single(x => x.LotNumber == "100").Id);
        Assert.Equal("POOL-A", active.Single(x => x.LotNumber == "100").PoolStart);
        Assert.Null(active.Single(x => x.LotNumber == "200").PoolStart);
        Assert.False((await fixture.Db.GrowerLots.SingleAsync(x => x.LotNumber == "300")).IsActive);
        Assert.False((await fixture.Db.GrowerLots.SingleAsync(x => x.LotNumber == "999")).IsActive);
        var preservedReceipt = await fixture.Db.Receipts.AsNoTracking().SingleAsync(x => x.Id == receiptId);
        Assert.Equal(receiptGrowerLotId, preservedReceipt.GrowerLotId);
        Assert.Equal(receiptNumber, preservedReceipt.GrowerNumber);
        Assert.Equal(receiptLot, preservedReceipt.LotCode);
        Assert.Single(await fixture.Db.AuditLogs.Where(x => x.EntityName == ReviewedGrowerLotSyncConstants.AuditEntityName).ToListAsync());
    }

    [Fact]
    public async Task Sync_RerunIsAlreadyAppliedWithZeroWritesAndReceivingSetIsExact()
    {
        await using var fixture = await Fixture.CreateAsync();
        var dryRun = await fixture.Service.RunAsync(Request(false), CancellationToken.None);
        Assert.True((await fixture.Service.RunAsync(Request(true, dryRun.Preflight), CancellationToken.None)).Applied);
        var lotsBefore = await fixture.Db.GrowerLots.CountAsync();
        var auditsBefore = await fixture.Db.AuditLogs.CountAsync(x => x.EntityName == ReviewedGrowerLotSyncConstants.AuditEntityName);

        var rerun = await fixture.Service.RunAsync(Request(true), CancellationToken.None);
        var receivingLots = await fixture.Service.GetAlignedActiveGrowerLotsAsync(CancellationToken.None);

        Assert.True(rerun.Success);
        Assert.True(rerun.AlreadyApplied);
        Assert.False(rerun.Applied);
        Assert.Equal(0, rerun.Created + rerun.Updated + rerun.Activated + rerun.Deactivated);
        Assert.Equal(lotsBefore, await fixture.Db.GrowerLots.CountAsync());
        Assert.Equal(auditsBefore, await fixture.Db.AuditLogs.CountAsync(x => x.EntityName == ReviewedGrowerLotSyncConstants.AuditEntityName));
        Assert.Equal(fixture.ActiveSource.Keys.OrderBy(x => x), receivingLots.Select(x => x.LotNumber).OrderBy(x => x));
        Assert.All(receivingLots, lot => Assert.Equal(fixture.ActiveSource[lot.LotNumber].GrowerName, lot.Grower));
    }

    [Fact]
    public async Task Policy_FailsClosedUntilActiveGrowerAndGrowerLotSetsAreExactlyAligned()
    {
        await using var fixture = await Fixture.CreateAsync();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.GetAlignedActiveGrowerLotsAsync(CancellationToken.None));
        Assert.Contains("not aligned", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Preflight_UnknownActiveRowWithCurrentInventoryFailsClosedWithoutWrites()
    {
        await using var fixture = await Fixture.CreateAsync();
        var legacy = await fixture.Db.GrowerLots.SingleAsync(x => x.LotNumber == "300");
        legacy.LotNumber = "777";
        await fixture.Db.SaveChangesAsync();
        fixture.Ledger.Snapshots = [Snapshot(legacy.Id, 4)];
        var before = await fixture.Db.GrowerLots.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Grower, x.LotNumber, x.IsActive }).ToListAsync();

        var result = await fixture.Service.RunAsync(Request(true), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Refused", result.Preflight.State);
        Assert.Contains(result.Preflight.Issues, x => x.Contains("current operational evidence", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(before, await fixture.Db.GrowerLots.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Grower, x.LotNumber, x.IsActive }).ToListAsync());
        Assert.Empty(await fixture.Db.AuditLogs.Where(x => x.EntityName == ReviewedGrowerLotSyncConstants.AuditEntityName).ToListAsync());
    }

    [Fact]
    public async Task Preflight_DuplicateActiveRowsAreAmbiguousAndNeverRepointHistory()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Db.GrowerLots.Add(new GrowerLot { Grower = "Duplicate", LotNumber = "100", PoolStart = "DUP", IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
        await fixture.Db.SaveChangesAsync();
        var original = await fixture.Db.GrowerLots.OrderBy(x => x.Id).FirstAsync(x => x.LotNumber == "100");
        var receipt = await fixture.AddReceiptAsync(original.Id);

        var result = await fixture.Service.RunAsync(Request(true), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal("Refused", result.Preflight.State);
        Assert.Equal(2, result.Preflight.Changes.Count(x => x.Action == "Conflict" && x.GrowerNumber == "100"));
        Assert.Equal(original.Id, (await fixture.Db.Receipts.AsNoTracking().SingleAsync(x => x.Id == receipt.Id)).GrowerLotId);
        Assert.Equal(2, await fixture.Db.GrowerLots.CountAsync(x => x.IsActive && x.LotNumber == "100"));
    }

    [Fact]
    public async Task Apply_WrongAuthorizationOrStaleFingerprintMakesZeroWrites()
    {
        await using var fixture = await Fixture.CreateAsync();
        var dryRun = await fixture.Service.RunAsync(Request(false), CancellationToken.None);
        var before = await fixture.Db.GrowerLots.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Grower, x.LotNumber, x.IsActive }).ToListAsync();

        var unauthorized = await fixture.Service.RunAsync(Request(true, dryRun.Preflight) with { AuthorizationToken = "wrong" }, CancellationToken.None);
        var stale = await fixture.Service.RunAsync(Request(true, dryRun.Preflight) with { ExpectedTargetFingerprint = new string('0', 64) }, CancellationToken.None);

        Assert.False(unauthorized.Success);
        Assert.False(stale.Success);
        Assert.Equal(before, await fixture.Db.GrowerLots.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Grower, x.LotNumber, x.IsActive }).ToListAsync());
        Assert.Empty(await fixture.Db.AuditLogs.Where(x => x.EntityName == ReviewedGrowerLotSyncConstants.AuditEntityName).ToListAsync());
    }

    [Fact]
    public async Task Apply_ForcedAuditFailureRollsBackEveryGrowerLotWrite()
    {
        await using var fixture = await Fixture.CreateAsync();
        var dryRun = await fixture.Service.RunAsync(Request(false), CancellationToken.None);
        var before = await fixture.Db.GrowerLots.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Grower, x.LotNumber, x.PoolStart, x.IsActive, x.UpdatedAt }).ToListAsync();
        await fixture.Db.Database.ExecuteSqlRawAsync("CREATE TRIGGER fail_reviewed_grower_lot_audit BEFORE INSERT ON \"AuditLogs\" WHEN NEW.\"EntityName\" = 'ReviewedGrowerLotSync' BEGIN SELECT RAISE(ABORT, 'forced audit failure'); END;");

        var result = await fixture.Service.RunAsync(Request(true, dryRun.Preflight), CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.Applied);
        Assert.Equal(before, await fixture.Db.GrowerLots.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.Grower, x.LotNumber, x.PoolStart, x.IsActive, x.UpdatedAt }).ToListAsync());
        Assert.Empty(await fixture.Db.AuditLogs.Where(x => x.EntityName == ReviewedGrowerLotSyncConstants.AuditEntityName).ToListAsync());
    }

    [Fact]
    public async Task AdminManualAndImportPathsCannotIntroduceIndependentActiveGrowerIdentity()
    {
        await using var fixture = await Fixture.CreateAsync();
        var admin = new AdminManagementService(fixture.Db, new VarietyColorService(fixture.Db), reviewedGrowerLotPolicy: fixture.Service);
        var manualError = await admin.SaveMasterDataAsync(new MasterDataEditForm
        {
            Type = "grower-lots",
            Name = "Independent Grower",
            Code = "777",
            IsActive = true
        }, "admin@example.com", CancellationToken.None);
        var preview = await admin.PreviewGrowerLotImportAsync(new GrowerLotImportForm
        {
            CsvText = "Grower,Grower Number,Pool Start\nIndependent Grower,777,ZZ"
        }, CancellationToken.None);

        Assert.Contains("reviewed Grower master", manualError, StringComparison.OrdinalIgnoreCase);
        Assert.False(preview.CanApply);
        Assert.Equal(1, preview.InvalidCount);
        Assert.False(await fixture.Db.GrowerLots.AnyAsync(x => x.LotNumber == "777"));
    }

    [Fact]
    public async Task AdminReviewedRowsUseAuthoritativeNameAndCannotBeDeactivated()
    {
        await using var fixture = await Fixture.CreateAsync();
        var admin = new AdminManagementService(fixture.Db, new VarietyColorService(fixture.Db), reviewedGrowerLotPolicy: fixture.Service);
        var existing = await fixture.Db.GrowerLots.SingleAsync(x => x.LotNumber == "100");
        var saveError = await admin.SaveMasterDataAsync(new MasterDataEditForm
        {
            Type = "grower-lots",
            Id = existing.Id,
            Name = "User-entered stale name",
            Code = "100",
            PoolStart = "POOL-A",
            IsActive = true
        }, "admin@example.com", CancellationToken.None);
        var deactivateError = await admin.DeactivateAsync("grower-lots", existing.Id, "admin@example.com", CancellationToken.None);

        Assert.Null(saveError);
        Assert.Equal("Reviewed One", (await fixture.Db.GrowerLots.AsNoTracking().SingleAsync(x => x.Id == existing.Id)).Grower);
        Assert.Contains("cannot be deactivated manually", deactivateError, StringComparison.OrdinalIgnoreCase);
        Assert.True((await fixture.Db.GrowerLots.AsNoTracking().SingleAsync(x => x.Id == existing.Id)).IsActive);
    }

    [Fact]
    public async Task Reviewed9392RemainsSelectableWithExactCurrentName()
    {
        await using var fixture = await Fixture.CreateAsync(
            [new("9392", "MFR - HOOKER PL CONV", "HX", true, null)],
            [new GrowerLot { Grower = "Old Hooker Name", LotNumber = "9392", PoolStart = "HX", IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow }]);
        var dryRun = await fixture.Service.RunAsync(Request(false), CancellationToken.None);
        Assert.True((await fixture.Service.RunAsync(Request(true, dryRun.Preflight), CancellationToken.None)).Applied);

        var selected = Assert.Single(await fixture.Service.GetAlignedActiveGrowerLotsAsync(CancellationToken.None));
        Assert.Equal("9392", selected.LotNumber);
        Assert.Equal("MFR - HOOKER PL CONV", selected.Grower);
        Assert.Equal("HX", selected.PoolStart);
    }

    private static ReviewedGrowerLotSyncRequest Request(bool apply, ReviewedGrowerLotSyncPreflight? preflight = null) => new(
        apply,
        false,
        true,
        ReviewedGrowerLotSyncConstants.VerifiedRestoreBackupRunId,
        ReviewedGrowerLotSyncConstants.VerifiedRestorePackageSha256,
        "admin@example.com",
        "Reviewed Grower Lot alignment test.",
        preflight?.TargetFingerprint,
        preflight?.ProtectedFingerprint,
        ReviewedGrowerLotSyncConstants.ApplyAuthorizationToken);

    private static RoomInventoryLedgerSnapshot Snapshot(int growerLotId, int currentBins) => new(
        1, "WP", 1, "Room", "WP", 2026, growerLotId, 1, "Legacy", "777", "777", null,
        "GALA", "GALA", "Gala", "Apple", "Conventional", false, "Current",
        currentBins, 0, 0, 0, 0, 0, 0, 0, 0, currentBins, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        public CropQcDbContext Db { get; }
        public ReviewedGrowerLotSyncService Service { get; }
        public FakeLedger Ledger { get; }
        public IReadOnlyDictionary<string, ReviewedGrowerMasterRow> ActiveSource { get; }

        private Fixture(SqliteConnection connection, CropQcDbContext db, FakeLedger ledger, ReviewedGrowerLotSyncService service, IReadOnlyDictionary<string, ReviewedGrowerMasterRow> activeSource)
        {
            this.connection = connection;
            Db = db;
            Ledger = ledger;
            Service = service;
            ActiveSource = activeSource;
        }

        public static async Task<Fixture> CreateAsync(IReadOnlyList<ReviewedGrowerMasterRow>? rows = null, IReadOnlyList<GrowerLot>? lots = null)
        {
            rows ??=
            [
                new("100", "Reviewed One", "A", true, null),
                new("200", "Reviewed Two", "B", true, null),
                new("300", "", null, false, null)
            ];
            lots ??=
            [
                new GrowerLot { Grower = "Old One", LotNumber = "100", PoolStart = "POOL-A", IsActive = true, CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch },
                new GrowerLot { Grower = "Inactive Reviewed Legacy", LotNumber = "300", PoolStart = "LEGACY", IsActive = true, CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch },
                new GrowerLot { Grower = "Historical Only", LotNumber = "999", PoolStart = "HIST", IsActive = false, CreatedAt = DateTimeOffset.UnixEpoch, UpdatedAt = DateTimeOffset.UnixEpoch }
            ];
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var warehouse = await db.Warehouses.FirstAsync();
            if (!await db.Rooms.AnyAsync())
            {
                db.Rooms.Add(new Room { WarehouseId = warehouse.Id, Code = "TEST", Name = "Test Room", SortOrder = 1, CapacityBins = 100, IsActive = true });
            }
            if (!await db.FruitProfiles.AnyAsync())
            {
                db.FruitProfiles.Add(new FruitProfile { Name = "Gala", VarietyCode = "GALA", FruitType = "Apple", ProductionType = "Conventional", IsOrganic = false, IsActive = true });
            }
            db.GrowerLots.RemoveRange(await db.GrowerLots.ToListAsync());
            await db.SaveChangesAsync();
            db.GrowerLots.AddRange(lots);
            var admin = new User { Email = "admin@example.com", DisplayName = "Admin", Domain = "example.com", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
            db.Users.Add(admin);
            await db.SaveChangesAsync();
            db.UserRoles.Add(new UserRole { UserId = admin.Id, RoleId = 1 });
            db.BackupRunRecords.Add(new BackupRunRecord
            {
                Id = ReviewedGrowerLotSyncConstants.VerifiedRestoreBackupRunId,
                BackupType = BackupRunTypes.PreDeployment,
                Status = BackupRunStatuses.Running,
                EnvironmentName = "Production",
                DatabaseProvider = "Npgsql",
                RetentionCategory = BackupRunTypes.PreDeployment,
                StartedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            var source = new FakeSource(rows);
            var ledger = new FakeLedger();
            var service = new ReviewedGrowerLotSyncService(
                db,
                source,
                ledger,
                new FixedCropYear(),
                new AppEnvironmentOptions { Kind = AppEnvironmentKinds.Development },
                NullLogger<ReviewedGrowerLotSyncService>.Instance);
            return new Fixture(connection, db, ledger, service, rows.Where(x => x.IsActive).ToDictionary(x => x.GrowerNumber));
        }

        public async Task<Receipt> AddReceiptAsync(int growerLotId)
        {
            var room = await Db.Rooms.FirstAsync();
            var warehouse = await Db.Warehouses.SingleAsync(x => x.Id == room.WarehouseId);
            var fruit = await Db.FruitProfiles.FirstAsync();
            var lot = await Db.GrowerLots.SingleAsync(x => x.Id == growerLotId);
            var receipt = new Receipt
            {
                CropYear = 2026,
                ReceivedAt = DateTimeOffset.UtcNow,
                CompuTechReceiptId = $"TEST-{Guid.NewGuid():N}",
                WarehouseId = warehouse.Id,
                RoomId = room.Id,
                FruitProfileId = fruit.Id,
                GrowerLotId = lot.Id,
                GrowerNumber = lot.LotNumber,
                GrowerName = lot.Grower,
                LotCode = lot.LotNumber,
                BinCount = 5,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            Db.Receipts.Add(receipt);
            await Db.SaveChangesAsync();
            return receipt;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class FakeSource(IReadOnlyList<ReviewedGrowerMasterRow> rows) : IReviewedGrowerMasterSource
    {
        public Task<ReviewedGrowerMaster> LoadAsync(CancellationToken cancellationToken) => Task.FromResult(new ReviewedGrowerMaster(
            "test.csv", 0, new string('1', 64), ReviewedGrowerMasterConstants.AssetSha256, rows));
    }

    private sealed class FakeLedger : IRoomInventoryLedgerQueryService
    {
        public IReadOnlyList<RoomInventoryLedgerSnapshot> Snapshots { get; set; } = [];
        public Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(int? warehouseId, IReadOnlyCollection<int>? roomIds, CancellationToken cancellationToken) => Task.FromResult(Snapshots);
        public Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(int? warehouseId, IReadOnlyCollection<int>? roomIds, int? fruitProfileId, CancellationToken cancellationToken) => Task.FromResult(Snapshots);
    }

    private sealed class FixedCropYear : ICropYearService
    {
        public int GetCurrentCropYear(DateTimeOffset now) => 2026;
        public IReadOnlyList<int> GetCandidateCropYears(DateTimeOffset date) => [2026];
        public bool RequiresConfirmation(DateTimeOffset receivedAt, int selectedCropYear) => false;
        public Task<IReadOnlyList<int>> GetAvailableCropYearsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<int>>([2026]);
    }
}
