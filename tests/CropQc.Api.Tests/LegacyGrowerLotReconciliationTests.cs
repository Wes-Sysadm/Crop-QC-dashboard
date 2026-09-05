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
    public async Task Non_receipt_positive_transfer_with_unknown_historical_label_is_auto_resolvable()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Db.RoomInventoryAdjustments.Add(new RoomInventoryAdjustment
        {
            Id = 104,
            WarehouseId = 1,
            RoomId = Fixture.Wp4RoomId,
            CropYear = 2026,
            FruitProfileId = 19,
            GrowerName = "Legacy Baldwin label",
            LotNumber = "1531",
            VarietyCode = fixture.Profile.VarietyCode,
            InventoryStatus = fixture.Profile.ProductionType,
            ChangeAmount = 20,
            NewBinCount = 20,
            AdjustmentType = "RoomTransfer",
            Source = "RoomTransfer",
            Reason = "Historical transfer identity",
            AdjustmentAt = Now,
            CreatedAt = Now,
            InventoryInvariantVersion = 0
        });
        await fixture.Db.SaveChangesAsync();

        var diagnostic = await fixture.Service.AnalyzeAsync(CancellationToken.None);
        var candidate = Assert.Single(diagnostic.Positions, x => x.RoomId == Fixture.Wp4RoomId);

        Assert.Equal(LegacyGrowerLotReconciliationClassifications.AutoResolvableReviewedGrowerLot, candidate.Classification);
        Assert.Equal(203, candidate.CurrentBins);
        Assert.Contains(104, candidate.PositiveSourceAdjustmentIds);
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

    [Theory]
    [InlineData("LAMB-14", 15, 70, 15, 85)]
    [InlineData("MCD-16", 582, 312, 516, 894)]
    public async Task Consolidated_canonical_treatment_retires_stale_legacy_segment_without_double_counting(
        string scenario, int sourceInventory, int targetInventory, int sourceTreatment, int targetTreatment)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ConfigureWp4BalancesAsync(sourceInventory, targetInventory);
        await fixture.SeedTreatmentSegmentsAsync();
        await fixture.ConfigureWp4TreatmentAsync(sourceTreatment, targetTreatment);
        var historyBefore = await fixture.HistoryFingerprintAsync();
        var candidate = Assert.Single((await fixture.Service.AnalyzeAsync(CancellationToken.None)).Positions,
            x => x.RoomId == Fixture.Wp4RoomId);

        var result = await fixture.Service.RunAsync(fixture.Request(candidate, $"{scenario}-stale-treatment-v1"), CancellationToken.None);

        Assert.True(result.Applied, result.Message);
        var balances = await fixture.Ledger.GetSnapshotsAsync(null, [Fixture.Wp4RoomId], CancellationToken.None);
        Assert.DoesNotContain(balances, x => x.GrowerLotId is null && x.CurrentBins > 0);
        Assert.Equal(sourceInventory + targetInventory,
            Assert.Single(balances, x => x.GrowerLotId == 474).CurrentBins);
        var treatment = await fixture.Db.TreatmentLineageSegments
            .Where(x => x.RoomId == Fixture.Wp4RoomId).OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(0, Assert.Single(treatment, x => x.GrowerLotId == null).CurrentBins);
        Assert.Equal(targetTreatment, Assert.Single(treatment, x => x.GrowerLotId == 474).CurrentBins);
        Assert.Equal(historyBefore, await fixture.HistoryFingerprintAsync());
        var movement = Assert.Single(await fixture.Db.TreatmentLineageMovements.ToListAsync());
        Assert.Equal(TreatmentLineageMovementTypes.IdentityReclassificationRetirement, movement.MovementType);
        Assert.Equal(sourceTreatment, movement.BinCount);
    }

    [Fact]
    public async Task Ambiguous_treatment_totals_fail_closed_with_zero_writes()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ConfigureWp4BalancesAsync(15, 70);
        await fixture.SeedTreatmentSegmentsAsync();
        await fixture.ConfigureWp4TreatmentAsync(15, 84);
        var candidate = Assert.Single((await fixture.Service.AnalyzeAsync(CancellationToken.None)).Positions,
            x => x.RoomId == Fixture.Wp4RoomId);

        var result = await fixture.Service.RunAsync(fixture.Request(candidate, "ambiguous-treatment-v1"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Empty(await fixture.Db.InventoryIdentityCorrections.ToListAsync());
        Assert.Empty(await fixture.Db.TreatmentLineageMovements.ToListAsync());
    }

    [Fact]
    public async Task Zero_current_receipt_identity_conflict_is_historical_warning_not_a_current_blocker()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ConfigureTr508352ShapeAsync();

        var diagnostic = await fixture.Service.AnalyzeAsync(CancellationToken.None);
        var warning = Assert.Single(diagnostic.Positions, x => x.RoomId == Fixture.Wp4RoomId);

        Assert.Equal(LegacyGrowerLotReconciliationClassifications.HistoricalOnlyWarning, warning.Classification);
        Assert.Equal(0, warning.CurrentBins);
        Assert.Equal(0, diagnostic.NeedsReconciliationPositions);
        Assert.Equal(0, diagnostic.NeedsReconciliationBins);
        Assert.Contains(101, warning.PositiveSourceAdjustmentIds);
    }

    [Fact]
    public async Task Unique_current_identity_without_recorded_treatment_lineage_materializes_untreated_once()
    {
        await using var fixture = await Fixture.CreateAsync();
        var candidate = Assert.Single((await fixture.Service.AnalyzeAsync(CancellationToken.None)).Positions,
            x => x.RoomId == Fixture.Wp4RoomId);

        var result = await fixture.Service.RunAsync(fixture.Request(candidate, "no-treatment-lineage-v1"), CancellationToken.None);

        Assert.True(result.Applied, result.Message);
        var segments = await fixture.Db.TreatmentLineageSegments.Where(x => x.RoomId == Fixture.Wp4RoomId).ToListAsync();
        Assert.Equal(0, Assert.Single(segments, x => x.GrowerLotId == null).CurrentBins);
        Assert.Equal(candidate.CurrentBins, Assert.Single(segments, x => x.GrowerLotId == 474).CurrentBins);
    }

    [Fact]
    public async Task Proven_untreated_lineage_gap_backfills_exact_coverage_without_a_treatment_application()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ConfigureEvans7UntreatedGapAsync();
        var historyBefore = await fixture.HistoryFingerprintAsync();
        var candidate = Assert.Single((await fixture.Service.AnalyzeAsync(CancellationToken.None)).Positions,
            x => x.RoomId == Fixture.Wp4RoomId);

        var result = await fixture.Service.RunAsync(fixture.Request(candidate, "evans7-untreated-gap-v1"), CancellationToken.None);
        var rerun = await fixture.Service.RunAsync(fixture.Request(candidate, "evans7-untreated-gap-v1"), CancellationToken.None);

        Assert.True(result.Applied, result.Message);
        Assert.True(rerun.AlreadyApplied, rerun.Message);
        var balances = await fixture.Ledger.GetSnapshotsAsync(null, [Fixture.Wp4RoomId], CancellationToken.None);
        Assert.Equal(170, Assert.Single(balances, x => x.GrowerLotId == 474).CurrentBins);
        Assert.DoesNotContain(balances, x => x.GrowerLotId is null && x.CurrentBins > 0);
        var treatment = await fixture.Db.TreatmentLineageSegments.Where(x => x.RoomId == Fixture.Wp4RoomId).ToListAsync();
        Assert.Equal(0, Assert.Single(treatment, x => x.GrowerLotId is null).CurrentBins);
        var canonical = Assert.Single(treatment, x => x.GrowerLotId == 474);
        Assert.Equal(170, canonical.CurrentBins);
        Assert.Equal(TreatmentLineageStates.Untreated, canonical.TreatmentState);
        Assert.Equal("u", canonical.TreatmentSignature);
        Assert.Empty(await fixture.Db.RoomTreatmentApplications.ToListAsync());
        var backfill = Assert.Single(await fixture.Db.TreatmentLineageMovements
            .Where(x => x.MovementType == TreatmentLineageMovementTypes.IdentityReclassificationUntreatedBackfill).ToListAsync());
        Assert.Equal(75, backfill.BinCount);
        Assert.Equal(historyBefore, await fixture.HistoryFingerprintAsync());
    }

    [Fact]
    public async Task Later_source_room_treatment_does_not_retroactively_block_proven_untreated_lineage_backfill()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ConfigureEvans7UntreatedGapAsync();
        await fixture.AddLaterSourceRoomTreatmentApplicationAsync();
        var candidate = Assert.Single((await fixture.Service.AnalyzeAsync(CancellationToken.None)).Positions,
            x => x.RoomId == Fixture.Wp4RoomId);

        var result = await fixture.Service.RunAsync(fixture.Request(candidate, "evans7-later-source-treatment-v1"), CancellationToken.None);

        Assert.True(result.Applied, result.Message);
        Assert.Empty(await fixture.Db.RoomTreatmentApplications
            .Where(x => x.RoomId == Fixture.Wp4RoomId).ToListAsync());
        var canonical = Assert.Single(await fixture.Db.TreatmentLineageSegments
            .Where(x => x.RoomId == Fixture.Wp4RoomId && x.GrowerLotId == 474).ToListAsync());
        Assert.Equal(170, canonical.CurrentBins);
        Assert.Equal(TreatmentLineageStates.Untreated, canonical.TreatmentState);
    }

    [Fact]
    public async Task Fully_exited_historical_receipt_does_not_block_proven_untreated_lineage_backfill()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ConfigureEvans7UntreatedGapAsync();
        await fixture.AddFullyExitedHistoricalReceiptAsync();
        var candidate = Assert.Single((await fixture.Service.AnalyzeAsync(CancellationToken.None)).Positions,
            x => x.RoomId == Fixture.Wp4RoomId);

        var result = await fixture.Service.RunAsync(fixture.Request(candidate, "evans7-fully-exited-receipt-v1"), CancellationToken.None);

        Assert.True(result.Applied, result.Message);
        var canonical = Assert.Single(await fixture.Db.TreatmentLineageSegments
            .Where(x => x.RoomId == Fixture.Wp4RoomId && x.GrowerLotId == 474).ToListAsync());
        Assert.Equal(170, canonical.CurrentBins);
        Assert.Equal(TreatmentLineageStates.Untreated, canonical.TreatmentState);
    }

    [Fact]
    public async Task Stale_inventory_status_treatment_projection_is_normalized_as_part_of_proven_untreated_backfill()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ConfigureEvans7UntreatedGapAsync();
        await fixture.MakeCanonicalTreatmentSegmentInventoryStatusStaleAsync();
        var candidate = Assert.Single((await fixture.Service.AnalyzeAsync(CancellationToken.None)).Positions,
            x => x.RoomId == Fixture.Wp4RoomId);

        var result = await fixture.Service.RunAsync(fixture.Request(candidate, "evans7-stale-status-v1"), CancellationToken.None);

        Assert.True(result.Applied, result.Message);
        var treatment = await fixture.Db.TreatmentLineageSegments
            .Where(x => x.RoomId == Fixture.Wp4RoomId && x.GrowerLotId == 474).ToListAsync();
        Assert.Equal(170, treatment.Sum(x => x.CurrentBins));
        Assert.Equal(0, Assert.Single(treatment, x => x.InventoryStatusSnapshot == "CONVENTIONAL").CurrentBins);
        Assert.Empty(await fixture.Db.RoomTreatmentApplications.ToListAsync());
    }

    [Theory]
    [InlineData("application")]
    [InlineData("mixed-signature")]
    [InlineData("unexplained-gap")]
    public async Task Untreated_lineage_backfill_fails_closed_without_exact_provenance(string condition)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.ConfigureEvans7UntreatedGapAsync();
        if (condition == "application") await fixture.AddApplicableRoomTreatmentApplicationAsync();
        if (condition == "mixed-signature") await fixture.MakeEvansTreatmentSignatureMixedAsync();
        if (condition == "unexplained-gap") await fixture.MakeEvansGapUnexplainedAsync();
        var candidate = Assert.Single((await fixture.Service.AnalyzeAsync(CancellationToken.None)).Positions,
            x => x.RoomId == Fixture.Wp4RoomId);

        var result = await fixture.Service.RunAsync(fixture.Request(candidate, $"evans7-{condition}-v1"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Empty(await fixture.Db.InventoryIdentityCorrections.ToListAsync());
        Assert.Empty(await fixture.Db.TreatmentLineageMovements
            .Where(x => x.MovementType == TreatmentLineageMovementTypes.IdentityReclassificationUntreatedBackfill).ToListAsync());
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
        public Room Wp4Room { get; }
        public FruitProfile Profile { get; }

        private Fixture(CropQcDbContext db, Room wp4Room, FruitProfile profile)
        {
            Db = db;
            Wp4Room = wp4Room;
            Profile = profile;
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
            return new Fixture(db, room4, profile);
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

        public async Task ConfigureWp4BalancesAsync(int sourceBins, int targetBins)
        {
            var sourceAdd = await Db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 101);
            var sourceDepletion = await Db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 102);
            var target = await Db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 103);
            sourceAdd.ChangeAmount = sourceBins;
            sourceAdd.NewBinCount = sourceBins;
            sourceDepletion.ChangeAmount = 0;
            sourceDepletion.NewBinCount = sourceBins;
            target.ChangeAmount = targetBins;
            target.NewBinCount = targetBins;
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async Task ConfigureTr508352ShapeAsync()
        {
            var sourceAdd = await Db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 101);
            var runDepletion = await Db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 102);
            var canonical = await Db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 103);
            sourceAdd.GrowerName = "Ridpath ORG CHIL";
            sourceAdd.ChangeAmount = 6;
            sourceAdd.NewBinCount = 6;
            runDepletion.ChangeAmount = -6;
            runDepletion.NewBinCount = 0;
            canonical.ChangeAmount = 0;
            canonical.NewBinCount = 0;
            var ridpath = new CanonicalGrower
            {
                Id = 99,
                DisplayName = "Ridpath ORG CHIL",
                NormalizedKey = CanonicalGrowerService.NormalizeGrowerKey("Ridpath ORG CHIL"),
                IsActive = true,
                CreatedAt = Now,
                UpdatedAt = Now
            };
            ridpath.GrowerNumbers.Add(new CanonicalGrowerNumber
            {
                Id = 99,
                GrowerNumber = "4701",
                NormalizedGrowerNumber = "4701",
                IsActive = true,
                CreatedAt = Now,
                UpdatedAt = Now
            });
            Db.CanonicalGrowers.Add(ridpath);
            Db.BinsRunEntries.Add(new BinsRunEntry
            {
                Id = 286,
                SourceInventoryAdjustmentId = sourceAdd.Id,
                InventoryAdjustmentId = runDepletion.Id,
                WarehouseId = 1,
                RoomId = Wp4RoomId,
                CropYear = 2026,
                FruitProfileId = Profile.Id,
                GrowerName = sourceAdd.GrowerName,
                LotNumber = "1531",
                PreviousAvailableBins = 6,
                BinsRun = 6,
                NewAvailableBins = 0,
                RunAt = Now.AddDays(-1),
                CreatedAt = Now.AddDays(-1),
                TransactionType = ActualRunTransactionTypes.Depletion
            });
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async Task ConfigureEvans7UntreatedGapAsync()
        {
            var sourceAdd = await Db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 101);
            var sourceTransfer = await Db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 102);
            var targetTransfer = await Db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 103);
            var target = await Db.GrowerLots.SingleAsync(x => x.Id == 474);
            sourceAdd.ChangeAmount = 6;
            sourceAdd.NewBinCount = 6;
            sourceAdd.AdjustmentType = "TransferIn";
            sourceAdd.RoomTransferId = 137;
            sourceTransfer.ChangeAmount = 27;
            sourceTransfer.NewBinCount = 33;
            sourceTransfer.AdjustmentType = "TransferIn";
            sourceTransfer.RoomTransferId = 174;
            targetTransfer.ChangeAmount = 27;
            targetTransfer.NewBinCount = 27;
            targetTransfer.AdjustmentType = "TransferIn";
            targetTransfer.RoomTransferId = 145;
            var receiptAddition = Adjustment(104, Wp4Room, Profile, target, target.Grower, "1531", 42, "ReceiptAdd");
            receiptAddition.ReceiptId = 683;
            var transfer160 = Adjustment(105, Wp4Room, Profile, target, target.Grower, "1531", 44, "TransferIn");
            transfer160.RoomTransferId = 160;
            var transfer167 = Adjustment(106, Wp4Room, Profile, target, target.Grower, "1531", 24, "TransferIn");
            transfer167.RoomTransferId = 167;
            foreach (var addition in new[] { receiptAddition, transfer160, transfer167 })
            {
                addition.Warehouse = null!;
                addition.Room = null!;
                addition.GrowerLot = null;
                addition.FruitProfile = null;
                addition.WarehouseId = 1;
                addition.RoomId = Wp4RoomId;
                addition.GrowerLotId = target.Id;
                addition.FruitProfileId = Profile.Id;
            }
            Db.RoomInventoryAdjustments.AddRange(receiptAddition, transfer160, transfer167);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
            await SeedTreatmentSegmentsAsync();
            await ConfigureWp4TreatmentAsync(27, 68);
            var source = await Db.TreatmentLineageSegments.SingleAsync(x => x.RoomId == Wp4RoomId && x.GrowerLotId == null);
            var canonical = await Db.TreatmentLineageSegments.SingleAsync(x => x.RoomId == Wp4RoomId && x.GrowerLotId == 474);
            Db.TreatmentLineageMovements.AddRange(
                UntreatedMovement(160, 44, canonical),
                UntreatedMovement(167, 24, canonical),
                UntreatedMovement(174, 27, source));
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async Task AddApplicableRoomTreatmentApplicationAsync()
        {
            Db.RoomTreatmentApplications.Add(new RoomTreatmentApplication
            {
                Id = 999,
                OperationKey = "test-applicable-treatment",
                TreatmentChemicalId = 1,
                ApplicationLevel = TreatmentApplicationLevels.Room,
                WarehouseId = 1,
                RoomId = Wp4RoomId,
                AppliedAt = Now.AddDays(-3),
                AppliedByUserId = 1,
                ProductNameSnapshot = "Test chemical",
                CropSnapshot = "Pear",
                UnitSnapshot = "gal",
                CurrencySnapshot = "USD",
                CreatedAt = Now.AddDays(-3),
                CreatedByUserId = 1
            });
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async Task AddLaterSourceRoomTreatmentApplicationAsync()
        {
            Db.RoomTransfers.Add(new RoomTransfer
            {
                Id = 137,
                OperationKey = "test-evans7-source-transfer",
                SourceWarehouseId = 1,
                SourceRoomId = Wp8RoomId,
                DestinationWarehouseId = 1,
                DestinationRoomId = Wp4RoomId,
                CropYear = 2026,
                GrowerLotId = null,
                FruitProfileId = Profile.Id,
                GrowerName = "Legacy grower",
                LotNumber = "1531",
                BinCount = 6,
                Reason = "Test transfer",
                TransferredAt = Now.AddDays(-2).AddMinutes(101),
                CreatedAt = Now.AddDays(-2).AddMinutes(101)
            });
            Db.RoomTreatmentApplications.Add(new RoomTreatmentApplication
            {
                Id = 998,
                OperationKey = "test-later-source-treatment",
                TreatmentChemicalId = 1,
                ApplicationLevel = TreatmentApplicationLevels.Room,
                WarehouseId = 1,
                RoomId = Wp8RoomId,
                AppliedAt = Now.AddDays(-1),
                AppliedByUserId = 1,
                ProductNameSnapshot = "Test chemical",
                CropSnapshot = "Pear",
                UnitSnapshot = "gal",
                CurrencySnapshot = "USD",
                CreatedAt = Now.AddDays(-1),
                CreatedByUserId = 1
            });
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async Task AddFullyExitedHistoricalReceiptAsync()
        {
            var target = await Db.GrowerLots.SingleAsync(x => x.Id == 474);
            var receiptAddition = Adjustment(107, Wp4Room, Profile, target, target.Grower, "1531", 20, "ReceiptAdd");
            var depletion = Adjustment(108, Wp4Room, Profile, target, target.Grower, "1531", -16, "BinsRun");
            var transferIn = Adjustment(109, Wp4Room, Profile, target, target.Grower, "1531", 16, "TransferIn");
            var transferOut = Adjustment(110, Wp4Room, Profile, target, target.Grower, "1531", -20, "TransferOut");
            receiptAddition.ReceiptId = 701;
            transferIn.ReceiptId = 702;
            transferIn.RoomTransferId = 777;
            foreach (var row in new[] { receiptAddition, depletion, transferIn, transferOut })
            {
                row.AdjustmentAt = Now.AddDays(-10).AddMinutes(row.Id);
                row.CreatedAt = row.AdjustmentAt;
                row.Warehouse = null!;
                row.Room = null!;
                row.GrowerLot = null;
                row.FruitProfile = null;
                row.WarehouseId = 1;
                row.RoomId = Wp4RoomId;
                row.GrowerLotId = target.Id;
                row.FruitProfileId = Profile.Id;
            }
            Db.RoomInventoryAdjustments.AddRange(receiptAddition, depletion, transferIn, transferOut);
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async Task MakeCanonicalTreatmentSegmentInventoryStatusStaleAsync()
        {
            var canonical = await Db.TreatmentLineageSegments.SingleAsync(x => x.RoomId == Wp4RoomId && x.GrowerLotId == 474);
            canonical.InventoryStatusSnapshot = "CONVENTIONAL";
            canonical.IdentityKey = $"{canonical.IdentityKey}|CONVENTIONAL";
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async Task MakeEvansTreatmentSignatureMixedAsync()
        {
            var source = await Db.TreatmentLineageSegments.SingleAsync(x => x.RoomId == Wp4RoomId && x.GrowerLotId == null);
            source.TreatmentSignature = "u|a:999";
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async Task MakeEvansGapUnexplainedAsync()
        {
            var addition = await Db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 105);
            addition.ChangeAmount = 43;
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public async Task ConfigureWp4TreatmentAsync(int sourceBins, int targetBins)
        {
            var source = await Db.TreatmentLineageSegments.SingleAsync(x => x.RoomId == Wp4RoomId && x.GrowerLotId == null);
            var target = await Db.TreatmentLineageSegments.SingleAsync(x => x.RoomId == Wp4RoomId && x.GrowerLotId == 474);
            source.CurrentBins = sourceBins;
            target.CurrentBins = targetBins;
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

        public async Task<string> RoomHistoryFingerprintAsync()
        {
            var rows = await Db.RoomInventoryAdjustments.AsNoTracking().Where(x => x.RoomId == Wp4RoomId)
                .OrderBy(x => x.Id).Select(x => $"{x.Id}|{x.GrowerLotId}|{x.ChangeAmount}|{x.AdjustmentType}|{x.RoomTransferId}|{x.ReceiptId}")
                .ToListAsync();
            return string.Join(';', rows);
        }

        private TreatmentLineageMovement UntreatedMovement(long transferId, int bins, TreatmentLineageSegment destination) => new()
        {
            OperationKey = $"test-transfer-{transferId}",
            MovementType = TreatmentLineageMovementTypes.Transfer,
            DestinationSegment = destination,
            DestinationRoomId = Wp4RoomId,
            IdentityKey = destination.IdentityKey,
            TreatmentStateSnapshot = TreatmentLineageStates.Untreated,
            TreatmentSignatureSnapshot = "u",
            RoomTransferId = transferId,
            BinCount = bins,
            OccurredAt = Now.AddDays(-1),
            CreatedAt = Now.AddDays(-1)
        };

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
