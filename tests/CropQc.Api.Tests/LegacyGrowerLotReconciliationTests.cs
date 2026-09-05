using System.Security.Claims;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class LegacyGrowerLotReconciliationTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-05T03:00:00Z");

    [Fact]
    public async Task Unique_reviewed_identity_is_classified_auto_resolvable()
    {
        await using var fixture = await Fixture.CreateAsync();

        var diagnostic = await fixture.Service.AnalyzeAsync(CancellationToken.None);

        Assert.Equal(2, diagnostic.AutoResolvableReviewedPositions);
        Assert.Equal(307, diagnostic.AutoResolvableReviewedBins);
        Assert.All(diagnostic.Positions, x =>
        {
            Assert.Equal(LegacyGrowerLotReconciliationClassifications.AutoResolvableReviewedGrowerLot, x.Classification);
            Assert.Equal(474, x.TargetGrowerLotId);
            Assert.NotEmpty(x.StateToken);
        });
    }

    [Fact]
    public async Task Multiple_active_grower_lots_fail_closed()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Db.GrowerLots.Add(new GrowerLot
        {
            Id = 475,
            Grower = "Baldwin Pears ORG CHIL",
            LotNumber = "1531",
            IsActive = true,
            CreatedAt = Now,
            UpdatedAt = Now
        });
        await fixture.Db.SaveChangesAsync();

        var diagnostic = await fixture.Service.AnalyzeAsync(CancellationToken.None);

        Assert.Equal(0, diagnostic.AutoResolvableReviewedPositions);
        Assert.Equal(2, diagnostic.NeedsReconciliationPositions);
        Assert.All(diagnostic.Positions, x => Assert.Contains("exactly one", x.Reason, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("grower")]
    [InlineData("lot")]
    [InlineData("organic")]
    public async Task Conflicting_identity_evidence_fails_closed(string conflict)
    {
        await using var fixture = await Fixture.CreateAsync();
        if (conflict == "grower")
        {
            var receipt = await fixture.Db.Receipts.SingleAsync(x => x.Id == 501);
            receipt.GrowerName = "Different Grower";
            var row = await fixture.Db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 101);
            row.GrowerName = receipt.GrowerName;
        }
        else if (conflict == "lot")
        {
            var row = await fixture.Db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 101);
            row.LotNumber = "1532";
        }
        else
        {
            foreach (var row in await fixture.Db.RoomInventoryAdjustments.Where(x => x.RoomId == Fixture.Wp4RoomId).ToListAsync())
                row.InventoryStatus = "Conventional";
        }
        await fixture.Db.SaveChangesAsync();

        var diagnostic = await fixture.Service.AnalyzeAsync(CancellationToken.None);

        Assert.True(diagnostic.NeedsReconciliationPositions >= 1);
        Assert.DoesNotContain(diagnostic.Positions, x => x.RoomId == Fixture.Wp4RoomId
            && x.Classification == LegacyGrowerLotReconciliationClassifications.AutoResolvableReviewedGrowerLot);
    }

    [Fact]
    public async Task Aggregate_WP4_and_WP8_reconciliation_conserves_quantity_treatment_and_history_and_is_idempotent()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.SeedTreatmentSegmentsAsync();
        var historyBefore = await fixture.HistoryFingerprintAsync();
        var totalBefore = await fixture.TotalInventoryAsync();
        var treatmentBefore = await fixture.Db.TreatmentLineageSegments.SumAsync(x => x.CurrentBins);
        var initial = await fixture.Service.AnalyzeAsync(CancellationToken.None);
        var wp4 = Assert.Single(initial.Positions, x => x.RoomId == Fixture.Wp4RoomId);
        var wp8 = Assert.Single(initial.Positions, x => x.RoomId == Fixture.Wp8RoomId);

        var wp4Result = await fixture.Service.RunAsync(fixture.Request(wp4, "legacy-1531-wp4-v1"), CancellationToken.None);
        var wp4Rerun = await fixture.Service.RunAsync(fixture.Request(wp4, "legacy-1531-wp4-v1"), CancellationToken.None);
        var wp8Result = await fixture.Service.RunAsync(fixture.Request(wp8, "legacy-1531-wp8-v1"), CancellationToken.None);
        var wp8Rerun = await fixture.Service.RunAsync(fixture.Request(wp8, "legacy-1531-wp8-v1"), CancellationToken.None);

        Assert.True(wp4Result.Applied, wp4Result.Message);
        Assert.True(wp4Rerun.AlreadyApplied, wp4Rerun.Message);
        Assert.True(wp8Result.Applied, wp8Result.Message);
        Assert.True(wp8Rerun.AlreadyApplied, wp8Rerun.Message);
        Assert.Equal(2, await fixture.Db.InventoryIdentityCorrections.CountAsync());
        Assert.Equal(4, await fixture.Db.RoomInventoryAdjustments.CountAsync(x => x.InventoryIdentityCorrectionId != null));
        Assert.Equal(0, (await fixture.Db.RoomInventoryAdjustments
            .Where(x => x.InventoryIdentityCorrectionId != null).ToListAsync()).Sum(x => x.ChangeAmount));
        var balances = (await fixture.Ledger.GetSnapshotsAsync(null, null, CancellationToken.None))
            .Where(x => x.CropYear == 2026 && x.FruitProfileId == 19 && x.CurrentBins > 0).ToList();
        Assert.Equal(247, Assert.Single(balances, x => x.RoomId == Fixture.Wp4RoomId && x.GrowerLotId == 474).CurrentBins);
        Assert.Equal(124, Assert.Single(balances, x => x.RoomId == Fixture.Wp8RoomId && x.GrowerLotId == 474).CurrentBins);
        Assert.Equal(371, balances.Where(x => x.GrowerLotId == 474).Sum(x => x.CurrentBins));
        Assert.DoesNotContain(balances, x => x.GrowerLotId is null && x.Lot == "1531");
        Assert.All(balances.Where(x => x.GrowerLotId == 474),
            x => Assert.Null(OperationalInventoryPosition.UnavailableReason(x)));
        Assert.Equal(totalBefore, await fixture.TotalInventoryAsync());
        Assert.Equal(treatmentBefore, await fixture.Db.TreatmentLineageSegments.SumAsync(x => x.CurrentBins));
        Assert.Equal(historyBefore, await fixture.HistoryFingerprintAsync());
        Assert.Equal(2, await fixture.Db.AuditLogs.CountAsync(x => x.Action == "LegacyGrowerLotReconciliation"));
        var finalDiagnostic = await fixture.Service.AnalyzeAsync(CancellationToken.None);
        Assert.DoesNotContain(finalDiagnostic.Positions, x => x.GrowerNumber == "1531");
        var readiness = await fixture.Invariant.VerifyReadinessAsync(CancellationToken.None);
        Assert.True(readiness.IsReady, string.Join("; ", readiness.Issues.Where(x => x.BlocksDeployment).Select(x => x.Code)));
    }

    [Fact]
    public async Task Stale_state_token_refuses_reconciliation_with_zero_writes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var candidate = Assert.Single((await fixture.Service.AnalyzeAsync(CancellationToken.None)).Positions,
            x => x.RoomId == Fixture.Wp4RoomId);
        var before = await fixture.Db.RoomInventoryAdjustments.CountAsync();
        var request = fixture.Request(candidate, "legacy-stale-v1") with { ExpectedStateToken = new string('0', 64) };

        var result = await fixture.Service.RunAsync(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(before, await fixture.Db.RoomInventoryAdjustments.CountAsync());
        Assert.Empty(await fixture.Db.InventoryIdentityCorrections.ToListAsync());
        Assert.Empty(await fixture.Db.AuditLogs.ToListAsync());
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public const int Wp4RoomId = 4;
        public const int Wp8RoomId = 8;
        public CropQcDbContext Db { get; }
        public RoomInventoryLedgerQueryService Ledger { get; }
        public InventoryDeductionInvariantService Invariant { get; }
        public LegacyGrowerLotReconciliationService Service { get; }

        private Fixture(CropQcDbContext db)
        {
            Db = db;
            Ledger = new RoomInventoryLedgerQueryService(db);
            Invariant = new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance);
            var time = new PacificBusinessTimeService(new FixedClock(Now));
            var access = new UserAccessService(db, new ConfigurationBuilder().Build());
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "1"), new Claim(ClaimTypes.Email, "wes@fruitandland.com")], "test"));
            var treatments = new RoomTreatmentService(db, Ledger, access,
                new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } },
                time, NullLogger<RoomTreatmentService>.Instance);
            var canonical = new CanonicalGrowerService(db);
            Service = new LegacyGrowerLotReconciliationService(db, Ledger, canonical, treatments, Invariant, time);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var options = new DbContextOptionsBuilder<CropQcDbContext>()
                .UseInMemoryDatabase($"legacy-grower-lot-{Guid.NewGuid():N}").Options;
            var db = new CropQcDbContext(options);
            var warehouse = new Warehouse { Id = 1, Code = "WP", Name = "WP", IsActive = true };
            var room4 = new Room { Id = Wp4RoomId, Warehouse = warehouse, Code = "WP-4", Name = "WP-4", IsActive = true };
            var room8 = new Room { Id = Wp8RoomId, Warehouse = warehouse, Code = "WP-8", Name = "WP-8", IsActive = true };
            var profile = new FruitProfile
            {
                Id = 19,
                Name = "Organic Bartlett",
                VarietyCode = "ORBA",
                FruitType = "Pear",
                ProductionType = "Organic",
                IsOrganic = true,
                IsActive = true
            };
            var canonical = new CanonicalGrower
            {
                Id = 31,
                DisplayName = "Baldwin Pears ORG CHIL",
                NormalizedKey = CanonicalGrowerService.NormalizeGrowerKey("Baldwin Pears ORG CHIL"),
                IsActive = true,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            canonical.Aliases.Add(new CanonicalGrowerAlias
            {
                Id = 42,
                AliasName = "Baldwin Pears ORG CHILEAN",
                NormalizedAliasKey = CanonicalGrowerService.NormalizeGrowerKey("Baldwin Pears ORG CHILEAN"),
                IsActive = true,
                CreatedAt = Now,
                UpdatedAt = Now
            });
            var adjacentCanonical = new CanonicalGrower
            {
                Id = 33,
                DisplayName = "Baldwin Pears ORG",
                NormalizedKey = CanonicalGrowerService.NormalizeGrowerKey("Baldwin Pears ORG"),
                IsActive = true,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            adjacentCanonical.Aliases.Add(new CanonicalGrowerAlias
            {
                Id = 41,
                AliasName = "Baldwin Pears",
                NormalizedAliasKey = CanonicalGrowerService.NormalizeGrowerKey("Baldwin Pears"),
                IsActive = true,
                CreatedAt = Now,
                UpdatedAt = Now
            });
            adjacentCanonical.GrowerNumbers.Add(new CanonicalGrowerNumber
            {
                Id = 52,
                GrowerNumber = "1530",
                NormalizedGrowerNumber = "1530",
                IsActive = true,
                CreatedAt = Now,
                UpdatedAt = Now
            });
            canonical.GrowerNumbers.Add(new CanonicalGrowerNumber
            {
                Id = 51,
                GrowerNumber = "1531",
                NormalizedGrowerNumber = "1531",
                IsActive = true,
                CreatedAt = Now,
                UpdatedAt = Now
            });
            var differentCanonical = new CanonicalGrower
            {
                Id = 32,
                DisplayName = "Different Grower",
                NormalizedKey = CanonicalGrowerService.NormalizeGrowerKey("Different Grower"),
                IsActive = true,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            var target = new GrowerLot
            {
                Id = 474,
                Grower = "Baldwin Pears ORG CHIL",
                LotNumber = "1531",
                IsActive = true,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            var actor = new User
            {
                Id = 1,
                Email = "wes@fruitandland.com",
                DisplayName = "Wes",
                IsActive = true,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            var receipt4 = Receipt(501, "TR-LEGACY-WP4", room4, profile, null, "Baldwin Pears", 535);
            var receipt8 = Receipt(502, "TR-LEGACY-WP8", room8, profile, null, "Baldwin Pears ORG CHILEAN", 404);
            var correctedReceipt = Receipt(503, "TR508197", room4, profile, target, target.Grower, 64);
            db.AddRange(warehouse, room4, room8, profile, canonical, differentCanonical, adjacentCanonical, target, actor, receipt4, receipt8, correctedReceipt);
            db.RoomInventoryAdjustments.AddRange(
                Adjustment(101, room4, profile, null, "Baldwin Pears", "1531", 535, "ReceiptAdd", receipt4),
                Adjustment(102, room4, profile, null, "Baldwin Pears", "1531", -352, "BinsRun"),
                Adjustment(103, room4, profile, target, target.Grower, "1531", 64, "ReviewedTR508197", correctedReceipt),
                Adjustment(201, room8, profile, null, "Baldwin Pears ORG CHILEAN", "1531", 404, "ReceiptAdd", receipt8),
                Adjustment(202, room8, profile, null, "Baldwin Pears ORG CHILEAN", "1531", -280, "BinsRun"));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
            return new Fixture(db);
        }

        public LegacyGrowerLotReconciliationRequest Request(
            LegacyGrowerLotReconciliationCandidate candidate,
            string operationKey) => new(true, candidate.WarehouseId, candidate.RoomId, candidate.CropYear,
                candidate.GrowerNumber, candidate.Lot, candidate.FruitProfileId, candidate.IsOrganic,
                candidate.TargetGrowerLotId!.Value, candidate.CurrentBins, candidate.StateToken, operationKey,
                "wes@fruitandland.com", "Reviewed legacy Grower Lot evidence uniquely identifies GrowerLot 474");

        public async Task SeedTreatmentSegmentsAsync()
        {
            var snapshots = await Ledger.GetSnapshotsAsync(null, null, CancellationToken.None);
            foreach (var snapshot in snapshots.Where(x => x.CurrentBins > 0))
            {
                Db.TreatmentLineageSegments.Add(new TreatmentLineageSegment
                {
                    WarehouseId = snapshot.WarehouseId,
                    RoomId = snapshot.RoomId,
                    CropYear = snapshot.CropYear,
                    GrowerLotId = snapshot.GrowerLotId,
                    FruitProfileId = snapshot.FruitProfileId,
                    IdentityKey = RoomTreatmentService.IdentityKey(snapshot),
                    GrowerNumberSnapshot = snapshot.GrowerNumber,
                    GrowerNameSnapshot = snapshot.Grower,
                    LotNumberSnapshot = snapshot.Lot,
                    VarietyCodeSnapshot = snapshot.Variety,
                    ProductionTypeSnapshot = snapshot.ProductionType,
                    IsOrganicSnapshot = snapshot.IsOrganic,
                    InventoryStatusSnapshot = snapshot.InventoryStatus,
                    TreatmentState = TreatmentLineageStates.Untreated,
                    TreatmentSignature = "u",
                    CurrentBins = snapshot.CurrentBins,
                    CreatedAt = Now.AddDays(-1),
                    UpdatedAt = Now.AddDays(-1)
                });
            }
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async Task<int> TotalInventoryAsync() =>
            (await Ledger.GetSnapshotsAsync(null, null, CancellationToken.None)).Sum(x => x.CurrentBins);

        public async Task<string> HistoryFingerprintAsync()
        {
            var rows = await Db.RoomInventoryAdjustments.AsNoTracking()
                .Where(x => x.Id == 101 || x.Id == 102 || x.Id == 103 || x.Id == 201 || x.Id == 202)
                .OrderBy(x => x.Id)
                .Select(x => $"{x.Id}|{x.GrowerLotId}|{x.FruitProfileId}|{x.LotNumber}|{x.ChangeAmount}|{x.AdjustmentAt:O}|{x.CreatedAt:O}")
                .ToListAsync();
            return string.Join(';', rows);
        }

        private static RoomInventoryAdjustment Adjustment(long id, Room room, FruitProfile profile,
            GrowerLot? growerLot, string grower, string lot, int change, string type, Receipt? receipt = null) => new()
            {
                Id = id,
                Receipt = receipt,
                Warehouse = room.Warehouse,
                Room = room,
                CropYear = 2026,
                GrowerLot = growerLot,
                FruitProfile = profile,
                GrowerName = grower,
                LotNumber = lot,
                VarietyCode = profile.VarietyCode,
                InventoryStatus = profile.ProductionType,
                ChangeAmount = change,
                NewBinCount = change,
                AdjustmentType = type,
                Source = type,
                Reason = type,
                AdjustmentAt = Now.AddDays(-2).AddMinutes(id),
                CreatedAt = Now.AddDays(-2).AddMinutes(id),
                InventoryInvariantVersion = 0
            };

        private static Receipt Receipt(long id, string number, Room room, FruitProfile profile,
            GrowerLot? growerLot, string grower, int bins) => new()
            {
                Id = id,
                CropYear = 2026,
                ReceivedAt = Now.AddDays(-2),
                CompuTechReceiptId = number,
                ReceiptType = "Truck receipt",
                Warehouse = room.Warehouse,
                Room = room,
                FruitProfile = profile,
                GrowerLot = growerLot,
                GrowerNumber = "1531",
                GrowerName = grower,
                LotCode = "1531",
                BinCount = bins,
                CreatedAt = Now.AddDays(-2),
                UpdatedAt = Now.AddDays(-2)
            };

        public async ValueTask DisposeAsync() => await Db.DisposeAsync();
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }
}
