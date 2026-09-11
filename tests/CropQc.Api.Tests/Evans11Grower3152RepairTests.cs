using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class Evans11Grower3152RepairTests
{
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

        private Fixture(CropQcDbContext db, FixedLedger ledger)
        {
            Db = db;
            Ledger = ledger;
            Service = new Evans11Grower3152RepairService(
                db,
                ledger,
                null!,
                null!,
                null!,
                new PacificBusinessTimeService(new FixedClock(CorrectionAt.AddHours(1))),
                new ConfigurationBuilder().Build(),
                NullLogger<Evans11Grower3152RepairService>.Instance);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>()
                .UseInMemoryDatabase($"evans11-3152-repair-{Guid.NewGuid():N}").Options);
            await db.Database.EnsureCreatedAsync();
            var ledger = new FixedLedger();
            var fixture = new Fixture(db, ledger);
            await fixture.SeedAsync();
            return fixture;
        }

        private async Task SeedAsync()
        {
            Db.Receipts.Add(new Receipt
            {
                Id = 1391,
                CropYear = 2026,
                ReceivedAt = DateTimeOffset.Parse("2026-09-08T17:00:00Z"),
                CompuTechReceiptId = "TR109381",
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
            IdentityKey = $"test-{growerLotId}",
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

        public ValueTask DisposeAsync() => Db.DisposeAsync();
    }

    private sealed class FixedLedger : IRoomInventoryLedgerQueryService
    {
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
                Snapshot(513, "3162", 10)
            };
            if (TargetBins != 0) rows.Add(Snapshot(511, "3152", TargetBins));
            return rows;
        }

        private static RoomInventoryLedgerSnapshot Snapshot(int growerLotId, string lot, int bins) => new(
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
            FirstTransactionAt: CorrectionAt.AddDays(-1),
            LastTransactionAt: CorrectionAt.AddDays(-1),
            LatestAdjustmentId: growerLotId);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
