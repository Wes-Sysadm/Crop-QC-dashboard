using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace CropQc.Api.Tests;

public sealed class Evans11Grower3152RepairTests
{
    [Fact]
    public async Task InvariantFailure_RollsBackLedgerSegmentsAndMovements()
    {
        await using var fixture = await Fixture.CreateAsync(new RejectingInvariant());
        var before = await DurableStateAsync(fixture.Db);
        var result = await fixture.Service.RunAsync(true, false, "wes@fruitandland.com", 1, Fixture.BackupHash, default);
        Assert.False(result.Success);
        Assert.Contains("rolled back", result.Message);
        Assert.Equal(before, await DurableStateAsync(fixture.Db));
        var parent = await fixture.Db.InventoryIdentityCorrections.AsNoTracking().SingleAsync();
        Assert.Equal(0, parent.ExpectedAdjustmentCount);
        Assert.Equal(0, parent.ExpectedTreatmentMovementCount);
    }

    private sealed class RejectingInvariant : IInventoryDeductionInvariantService
    {
        public Task ValidateBeforeCommitAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("Simulated invariant rejection after lineage writes.");
        public Task<InventoryDeductionReadinessResult> VerifyReadinessAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Apply_ConservesTenBins_RetiresStaleFour_AndRerunWritesNothing()
    {
        await using var fixture = await Fixture.CreateAsync();
        var receiptBefore = System.Text.Json.JsonSerializer.Serialize(await fixture.Db.Receipts.AsNoTracking().IgnoreAutoIncludes().SingleAsync());
        var before = (await fixture.Ledger.GetSnapshotsAsync(1, new[] { 21 }, default)).Sum(x => x.CurrentBins);
        var result = await fixture.Service.RunAsync(true, false, "wes@fruitandland.com", 1, Fixture.BackupHash, default);
        Assert.True(result.Success, result.Message);
        Assert.True(result.Applied);
        var rows = await fixture.Db.RoomInventoryAdjustments.AsNoTracking().OrderBy(x => x.ChangeAmount).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(new[] { -10, 10 }, rows.Select(x => x.ChangeAmount));
        Assert.All(rows, x => Assert.Equal(Fixture.CorrectionId, x.InventoryIdentityCorrectionId));
        var after = await fixture.Ledger.GetSnapshotsAsync(1, new[] { 21 }, default);
        Assert.Equal(before, after.Sum(x => x.CurrentBins));
        Assert.Equal(0, after.Single(x => x.GrowerLotId == 513).CurrentBins);
        Assert.Equal(10, after.Single(x => x.GrowerLotId == 511).CurrentBins);
        Assert.Equal(0, await fixture.Db.TreatmentLineageSegments.Where(x => x.GrowerLotId == 513).SumAsync(x => x.CurrentBins));
        Assert.Equal(10, await fixture.Db.TreatmentLineageSegments.Where(x => x.GrowerLotId == 511).SumAsync(x => x.CurrentBins));
        var movements = await fixture.Db.TreatmentLineageMovements.AsNoTracking().ToListAsync();
        Assert.Equal(2, movements.Count);
        Assert.Contains(movements, x => x.SourceSegmentId == 38 && x.BinCount == 4 && x.MovementType == TreatmentLineageMovementTypes.IdentityReclassificationRetirement);
        Assert.Contains(movements, x => x.SourceSegmentId == 70 && x.BinCount == 10 && x.MovementType == TreatmentLineageMovementTypes.IdentityReclassification);
        Assert.Equal(receiptBefore, System.Text.Json.JsonSerializer.Serialize(await fixture.Db.Receipts.AsNoTracking().IgnoreAutoIncludes().SingleAsync()));
        fixture.Db.ChangeTracker.Clear();
        var durableBefore = await DurableStateAsync(fixture.Db);
        var repeat = await fixture.Service.RunAsync(true, false, "wes@fruitandland.com", 1, Fixture.BackupHash, default);
        Assert.True(repeat.AlreadyApplied, repeat.Message);
        Assert.False(repeat.Applied);
        Assert.Equal(durableBefore, await DurableStateAsync(fixture.Db));
        Assert.False(fixture.Db.ChangeTracker.HasChanges());
    }

    [Theory]
    [InlineData("retention")]
    [InlineData("lease")]
    [InlineData("pruned")]
    [InlineData("commit")]
    [InlineData("hash")]
    public async Task IncompleteOrWrongBackup_RejectsApplyWithoutWrites(string failure)
    {
        await using var fixture = await Fixture.CreateAsync();
        var backup = await fixture.Db.BackupRunRecords.SingleAsync();
        switch (failure)
        {
            case "retention": backup.RetentionProcessedAt = null; break;
            case "lease": backup.LeaseReleasedAt = null; break;
            case "pruned": backup.PrunedAt = backup.StartedAt; break;
            case "commit": backup.DeployedCommit = "wrong"; break;
            case "hash": backup.Sha256 = new string('b', 64); break;
        }
        await fixture.Db.SaveChangesAsync();
        var before = await DurableStateAsync(fixture.Db);
        var result = await fixture.Service.RunAsync(true, false, "wes@fruitandland.com", 1, Fixture.BackupHash, default);
        Assert.False(result.Success);
        Assert.Equal(before, await DurableStateAsync(fixture.Db));
    }

    [Fact]
    public async Task ForgedTreatmentMovement_AfterRepair_IsNotAlreadyApplied()
    {
        await using var fixture = await Fixture.CreateAsync();
        var applied = await fixture.Service.RunAsync(true, false, "wes@fruitandland.com", 1, Fixture.BackupHash, default);
        Assert.True(applied.Applied, applied.Message);
        var movement = await fixture.Db.TreatmentLineageMovements.FirstAsync();
        movement.OperationKey = "unexpected";
        await fixture.Db.SaveChangesAsync();
        var before = await DurableStateAsync(fixture.Db);
        var repeat = await fixture.Service.RunAsync(true, false, "wes@fruitandland.com", 1, Fixture.BackupHash, default);
        Assert.False(repeat.Success);
        Assert.Equal("State C", repeat.State);
        Assert.Equal(before, await DurableStateAsync(fixture.Db));
    }

    private static async Task<string> DurableStateAsync(CropQcDbContext db) => System.Text.Json.JsonSerializer.Serialize(new
    {
        Adjustments = await db.RoomInventoryAdjustments.AsNoTracking().IgnoreAutoIncludes().OrderBy(x => x.Id).ToListAsync(),
        Segments = await db.TreatmentLineageSegments.AsNoTracking().IgnoreAutoIncludes().OrderBy(x => x.Id).ToListAsync(),
        Movements = await db.TreatmentLineageMovements.AsNoTracking().IgnoreAutoIncludes().OrderBy(x => x.Id).ToListAsync(),
        Audits = await db.AuditLogs.AsNoTracking().IgnoreAutoIncludes().OrderBy(x => x.Id).ToListAsync()
    });

    [Fact]
    public async Task ExactReviewedProductionShape_IsReadyAndWritesNothingDuringPreflight()
    {
        await using var fixture = await Fixture.CreateAsync();

        var result = await fixture.Service.RunAsync(
            apply: false,
            createBackup: false,
            requestedBy: "wes@fruitandland.com",
            verifiedBackupRunId: null,
            verifiedBackupSha256: null,
            cancellationToken: default);

        Assert.True(result.Success, result.Message);
        Assert.Equal("State A", result.State);
        Assert.False(result.Applied);
        Assert.False(result.AlreadyApplied);
        Assert.Empty(await fixture.Db.RoomInventoryAdjustments
            .Where(x => x.InventoryIdentityCorrectionId == Fixture.CorrectionId)
            .ToListAsync());
        Assert.Empty(await fixture.Db.TreatmentLineageMovements
            .Where(x => x.InventoryIdentityCorrectionId == Fixture.CorrectionId)
            .ToListAsync());
        Assert.Empty(await fixture.Db.AuditLogs
            .Where(x => x.Action == "Evans11Grower3152Receipt1391Repair")
            .ToListAsync());
    }

    [Fact]
    public async Task ChangedRoomBalance_FailsClosedAndWritesNothing()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Ledger.TargetBins = 1;

        var result = await fixture.Service.RunAsync(
            apply: false,
            createBackup: false,
            requestedBy: "wes@fruitandland.com",
            verifiedBackupRunId: null,
            verifiedBackupSha256: null,
            cancellationToken: default);

        Assert.False(result.Success);
        Assert.Equal("State C", result.State);
        Assert.False(result.Applied);
        Assert.Empty(await fixture.Db.RoomInventoryAdjustments
            .Where(x => x.InventoryIdentityCorrectionId == Fixture.CorrectionId)
            .ToListAsync());
        Assert.Empty(await fixture.Db.AuditLogs.ToListAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public static readonly Guid CorrectionId = Guid.Parse("7dbadcc9-9ad5-4217-bf94-1e18cc16f599");
        private static readonly DateTimeOffset CorrectionAt = DateTimeOffset.Parse("2026-09-09T04:47:17.586479Z");

        public CropQcDbContext Db { get; }
        public FixedLedger Ledger { get; }
        public Evans11Grower3152RepairService Service { get; }
        private readonly SqliteConnection? connection;
        private readonly string? postgresAdmin;
        private readonly string? postgresDatabase;
        public const string Commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        public static readonly string BackupHash = new('a', 64);

        private Fixture(CropQcDbContext db, FixedLedger ledger, SqliteConnection? connection, IInventoryDeductionInvariantService? invariant,
            string? postgresAdmin = null, string? postgresDatabase = null)
        {
            this.connection = connection;
            this.postgresAdmin = postgresAdmin;
            this.postgresDatabase = postgresDatabase;
            Db = db;
            Ledger = ledger;
            Service = new Evans11Grower3152RepairService(
                db,
                ledger,
                new RoomTreatmentService(db, ledger, new UserAccessService(db, new ConfigurationBuilder().Build()),
                    new HttpContextAccessor(), new PacificBusinessTimeService(new FixedClock(CorrectionAt.AddHours(1))),
                    NullLogger<RoomTreatmentService>.Instance),
                invariant ?? new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance),
                null!,
                new PacificBusinessTimeService(new FixedClock(CorrectionAt.AddHours(1))),
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["SourceVersion"] = Commit }).Build(),
                NullLogger<Evans11Grower3152RepairService>.Instance);
        }

        public static async Task<Fixture> CreateAsync(IInventoryDeductionInvariantService? invariant = null)
        {
            var postgres = Environment.GetEnvironmentVariable("CROPQC_EVANS11_TEST_POSTGRES");
            SqliteConnection? connection = null;
            string? database = null;
            var options = new DbContextOptionsBuilder<CropQcDbContext>();
            if (string.IsNullOrWhiteSpace(postgres))
            {
                connection = new SqliteConnection("Data Source=:memory:");
                await connection.OpenAsync();
                options.UseSqlite(connection);
            }
            else
            {
                var builder = new NpgsqlConnectionStringBuilder(postgres);
                Assert.Equal("127.0.0.1", builder.Host);
                Assert.StartsWith("pr247_", builder.Database);
                await using var admin = new NpgsqlConnection(postgres);
                await admin.OpenAsync();
                Assert.Equal(18, admin.PostgreSqlVersion.Major);
                database = $"pr247_test_{Guid.NewGuid():N}";
                await using var create = new NpgsqlCommand($"CREATE DATABASE {database}", admin);
                await create.ExecuteNonQueryAsync();
                builder.Database = database;
                builder.Pooling = false;
                options.UseNpgsql(builder.ConnectionString);
            }
            var db = new CropQcDbContext(options.Options);
            await db.Database.EnsureCreatedAsync();
            var ledger = new FixedLedger(db);
            var fixture = new Fixture(db, ledger, connection, invariant, postgres, database);
            await fixture.SeedAsync();
            return fixture;
        }

        private async Task SeedAsync()
        {
            Db.Rooms.AddRange(new Room { Id = 7, WarehouseId = 1, Code = "LAMB-14", Name = "LAMB-14" },
                new Room { Id = 21, WarehouseId = 1, Code = "EVANS-11", Name = "Evans Street 11" });
            Db.GrowerLots.AddRange(new GrowerLot { Id = 511, Grower = "MFR - SAMS & BRN CONV", LotNumber = "3152", CreatedAt = CorrectionAt, UpdatedAt = CorrectionAt },
                new GrowerLot { Id = 513, Grower = "Grower 3162", LotNumber = "3162", CreatedAt = CorrectionAt, UpdatedAt = CorrectionAt });
            Db.Users.Add(new User { Id = 1, Email = "wes@fruitandland.com", DisplayName = "Wes", Domain = "fruitandland.com", IsActive = true, CreatedAt = CorrectionAt });
            Db.BackupRunRecords.Add(new BackupRunRecord
            {
                Id = 1,
                BackupType = BackupRunTypes.PreDeployment,
                Status = BackupRunStatuses.Succeeded,
                EnvironmentName = "Production",
                DatabaseProvider = "PostgreSql",
                RetentionCategory = "PreDeployment",
                RequestedBy = "wes@fruitandland.com",
                DeployedCommit = Commit,
                Sha256 = BackupHash,
                StartedAt = CorrectionAt,
                CompletedAt = CorrectionAt,
                VerifiedAt = CorrectionAt,
                RetentionProcessedAt = CorrectionAt,
                LeaseReleasedAt = CorrectionAt,
                FileSizeBytes = 100,
                PackageFileName = "isolated-test.zip",
                PackageStorageKey = "isolated-test",
                ManifestStorageKey = "isolated-manifest"
            });
            Db.Receipts.Add(new Receipt
            {
                Id = 1391,
                CropYear = 2026,
                ReceivedAt = DateTimeOffset.Parse("2026-09-08T17:00:00Z"),
                CompuTechReceiptId = "TR109381",
                ReceiptType = "Truck receipt",
                WarehouseId = 1,
                RoomId = 7,
                FruitProfileId = 2,
                GrowerLotId = 511,
                GrowerNumber = "3152",
                GrowerName = "MFR - SAMS & BRN CONV",
                LotCode = "3152",
                BinCount = 10,
                CreatedAt = CorrectionAt.AddDays(-1),
                UpdatedAt = CorrectionAt,
                ConcurrencyVersion = 1,
                IsDeleted = false
            });
            Db.InventoryIdentityCorrections.Add(new InventoryIdentityCorrection
            {
                Id = CorrectionId,
                OperationKey = "6f24ff89cfcf427aa43d4bee4f07a14d",
                CorrectedReceiptId = 1391,
                SourceCropYear = 2026,
                SourceGrowerLotId = 513,
                SourceFruitProfileId = 2,
                TargetCropYear = 2026,
                TargetGrowerLotId = 511,
                TargetFruitProfileId = 2,
                SourceIdentitySnapshotJson = "{}",
                TargetIdentitySnapshotJson = "{}",
                Reason = "Wrong lot",
                ExpectedAdjustmentCount = 0,
                ExpectedTreatmentMovementCount = 0,
                IsComplete = true,
                IsActive = true,
                CreatedAt = CorrectionAt,
                CreatedByUserId = 1
            });
            Db.TreatmentLineageSegments.AddRange(
                Segment(38, 511, 4),
                Segment(70, 513, 10));
            await Db.SaveChangesAsync();
        }

        private static TreatmentLineageSegment Segment(long id, int growerLotId, int bins) => new()
        {
            Id = id,
            WarehouseId = 1,
            RoomId = 21,
            CropYear = 2026,
            GrowerLotId = growerLotId,
            FruitProfileId = 2,
            IdentityKey = RoomTreatmentService.IdentityKey(FixedLedger.Snapshot(growerLotId, growerLotId == 511 ? "3152" : "3162", bins)),
            GrowerNumberSnapshot = growerLotId == 511 ? "3152" : "3162",
            GrowerNameSnapshot = growerLotId == 511 ? "MFR - SAMS & BRN CONV" : "Grower 3162",
            LotNumberSnapshot = growerLotId == 511 ? "3152" : "3162",
            VarietyCodeSnapshot = "GALA",
            ProductionTypeSnapshot = "Conventional",
            IsOrganicSnapshot = false,
            TreatmentState = TreatmentLineageStates.Untreated,
            TreatmentSignature = "u",
            CurrentBins = bins,
            CreatedAt = CorrectionAt.AddDays(-1),
            UpdatedAt = CorrectionAt.AddDays(-1),
            ConcurrencyVersion = 1
        };

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            if (connection is not null) await connection.DisposeAsync();
            if (postgresDatabase is not null)
            {
                await using var admin = new NpgsqlConnection(postgresAdmin);
                await admin.OpenAsync();
                await using var drop = new NpgsqlCommand($"DROP DATABASE {postgresDatabase}", admin);
                await drop.ExecuteNonQueryAsync();
            }
        }
    }

    private sealed class FixedLedger(CropQcDbContext db) : IRoomInventoryLedgerQueryService
    {
        private static readonly DateTimeOffset SnapshotAt = DateTimeOffset.Parse("2026-09-09T04:47:17.586479Z");
        public int TargetBins { get; set; }

        public Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(
            int? warehouseId,
            IReadOnlyCollection<int>? roomIds,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RoomInventoryLedgerSnapshot>>(Snapshots());

        public Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(
            int? warehouseId,
            IReadOnlyCollection<int>? roomIds,
            int? fruitProfileId,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RoomInventoryLedgerSnapshot>>(Snapshots());

        private IReadOnlyList<RoomInventoryLedgerSnapshot> Snapshots()
        {
            var rows = new List<RoomInventoryLedgerSnapshot>
            {
                Snapshot(513, "3162", 10 + db.RoomInventoryAdjustments.Where(x => x.GrowerLotId == 513).Sum(x => x.ChangeAmount))
            };
            var target = TargetBins + db.RoomInventoryAdjustments.Where(x => x.GrowerLotId == 511).Sum(x => x.ChangeAmount);
            if (target != 0) rows.Add(Snapshot(511, "3152", target));
            return rows;
        }

        public static RoomInventoryLedgerSnapshot Snapshot(int growerLotId, string lot, int bins) => new(
            WarehouseId: 1,
            Facility: "EBS",
            RoomId: 21,
            Room: "Evans Street 11",
            LocationGroup: "Evans Street",
            CropYear: 2026,
            GrowerLotId: growerLotId,
            FruitProfileId: 2,
            Grower: growerLotId == 511 ? "MFR - SAMS & BRN CONV" : "Grower 3162",
            GrowerNumber: lot,
            Lot: lot,
            PoolStart: null,
            StoredVarietyCode: "GALA",
            Variety: "GALA",
            VarietyName: "Gala",
            FruitType: "Apple",
            ProductionType: "Conventional",
            IsOrganic: false,
            InventoryStatus: "",
            PositiveBins: bins,
            NegativeBins: 0,
            ActualRunDepletionBins: 0,
            ActualRunReversalBins: 0,
            LegacyBinsRunDepletionBins: 0,
            TransferInBins: 0,
            TransferOutBins: 0,
            TrueUpBins: 0,
            OtherAdjustmentBins: 0,
            CurrentBins: bins,
            TransactionCount: 1,
            FirstTransactionAt: SnapshotAt.AddDays(-1),
            LastTransactionAt: SnapshotAt.AddDays(-1),
            LatestAdjustmentId: growerLotId);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
