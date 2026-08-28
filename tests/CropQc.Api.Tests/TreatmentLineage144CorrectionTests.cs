using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class TreatmentLineage144CorrectionTests
{
    [Fact]
    public async Task ExactProductionShape_AppliesOnce_ThenReturnsAlreadyAppliedWithoutWrites()
    {
        await using var fixture = await Fixture.CreateAsync();
        var preflight = await fixture.Service.PreflightAsync(default);

        Assert.True(preflight.State == "Ready", $"State={preflight.State}: {string.Join("; ", preflight.Issues)}");
        Assert.Empty(preflight.Issues);
        Assert.Equal(132, preflight.Evidence!.SegmentBins);
        Assert.Equal(224, preflight.Evidence.ExplicitLineageBins);
        Assert.Equal(92, preflight.Evidence.AuthoritativeInventoryBins);
        var protectedFingerprint = preflight.ProtectedFingerprint;

        var applied = await fixture.Service.RunAsync(fixture.Request(preflight), default);

        Assert.True(applied.Success);
        Assert.True(applied.Applied);
        Assert.Equal("AlreadyApplied", applied.Preflight.State);
        Assert.Equal(protectedFingerprint, applied.Preflight.ProtectedFingerprint);
        Assert.Equal(0, (await fixture.Db.TreatmentLineageSegments.FindAsync(144L))!.CurrentBins);
        Assert.Equal(92, applied.Preflight.Evidence!.ExplicitLineageBins);
        Assert.Equal(92, applied.Preflight.Evidence.AuthoritativeInventoryBins);
        Assert.Equal(132, (await fixture.Db.TreatmentLineageMovements.FindAsync(203L))!.BinCount);
        Assert.Equal(1, (await fixture.Db.TreatmentLineageMovements.FindAsync(204L))!.BinCount);
        Assert.Equal(132, (await fixture.Db.BinsRunEntries.FindAsync(188L))!.BinsRun);
        Assert.Equal(1, (await fixture.Db.BinsRunEntries.FindAsync(189L))!.BinsRun);
        Assert.Equal(93, await fixture.Db.Receipts.SumAsync(x => x.BinCount));
        var audit = Assert.Single(await fixture.Db.AuditLogs
            .Where(x => x.SourceApplication == TreatmentLineage144CorrectionConstants.AuditSource)
            .ToListAsync());
        Assert.Contains("stale pre-run CurrentBins", audit.AfterValuesJson);

        var auditCount = await fixture.Db.AuditLogs.CountAsync();
        var rerun = await fixture.Service.RunAsync(fixture.Request(applied.Preflight), default);

        Assert.True(rerun.Success);
        Assert.True(rerun.AlreadyApplied);
        Assert.False(rerun.Applied);
        Assert.Equal(auditCount, await fixture.Db.AuditLogs.CountAsync());
    }

    [Theory]
    [InlineData("segment")]
    [InlineData("movement")]
    [InlineData("reversal")]
    [InlineData("first-entry")]
    [InlineData("second-entry")]
    [InlineData("remaining")]
    [InlineData("receipt")]
    [InlineData("inventory")]
    public async Task ConflictingEvidence_IsRefusedAndWritesNothing(string mutation)
    {
        await using var fixture = await Fixture.CreateAsync();
        switch (mutation)
        {
            case "segment": (await fixture.Db.TreatmentLineageSegments.FindAsync(144L))!.CurrentBins = 131; break;
            case "movement": (await fixture.Db.TreatmentLineageMovements.FindAsync(203L))!.BinCount = 131; break;
            case "reversal": fixture.Db.TreatmentLineageMovements.Add(fixture.Reversal()); break;
            case "first-entry": (await fixture.Db.BinsRunEntries.FindAsync(188L))!.NewAvailableBins = 94; break;
            case "second-entry": (await fixture.Db.BinsRunEntries.FindAsync(189L))!.TreatmentSignatureSnapshot = "u"; break;
            case "remaining": (await fixture.Db.TreatmentLineageSegments.FindAsync(175L))!.CurrentBins = 23; break;
            case "receipt": (await fixture.Db.Receipts.FindAsync(944L))!.BinCount = 20; break;
            case "inventory": fixture.Ledger.CurrentBins = 91; break;
        }
        await fixture.Db.SaveChangesAsync();

        var preflight = await fixture.Service.PreflightAsync(default);
        var result = await fixture.Service.RunAsync(fixture.Request(preflight), default);

        Assert.Equal("Refused", preflight.State);
        Assert.False(result.Success);
        Assert.Equal(mutation == "segment" ? 131 : 132, (await fixture.Db.TreatmentLineageSegments.FindAsync(144L))!.CurrentBins);
        Assert.Empty(await fixture.Db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task ReleaseReadiness_ReportsBoundedOperationalIdentityEvidence_ThenPassesAfterCorrection()
    {
        await using var fixture = await Fixture.CreateAsync();
        var readiness = new TreatmentLineageReadinessService(fixture.Ledger, fixture.Db);

        var failed = await readiness.VerifyAsync(default);

        Assert.False(failed.Success);
        Assert.Equal(1, failed.BlockingIssueCount);
        var issue = Assert.Single(failed.BlockingIssues);
        Assert.Equal("TREATMENT_LINEAGE_EXCEEDS_AUTHORITATIVE_INVENTORY", issue.Code);
        Assert.Equal("EBS", issue.Facility);
        Assert.Equal(8, issue.RoomId);
        Assert.Equal("LAMB-15", issue.Room);
        Assert.Equal(2026, issue.CropYear);
        Assert.Equal("9100", issue.GrowerNumber);
        Assert.Equal("9100", issue.Lot);
        Assert.Equal("GALA", issue.Variety);
        Assert.Equal(92, issue.AuthoritativeBins);
        Assert.Equal(224, issue.ExplicitLineageBins);
        Assert.Equal(132, issue.Difference);
        Assert.Equal(TreatmentLineage144CorrectionConstants.IdentityKey, issue.IdentityKey);

        (await fixture.Db.TreatmentLineageSegments.FindAsync(144L))!.CurrentBins = 0;
        await fixture.Db.SaveChangesAsync();

        var passed = await readiness.VerifyAsync(default);

        Assert.True(passed.Success);
        Assert.Equal(0, passed.BlockingIssueCount);
        Assert.Empty(passed.BlockingIssues);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private const string BackupSha = "7c9545aa841679a5970938ae93e338f74bb1eb719930e9162137155b9a9c7c1d";
        private readonly DateTimeOffset at = DateTimeOffset.Parse("2026-08-28T08:00:00Z");

        public CropQcDbContext Db { get; }
        public FixedLedger Ledger { get; } = new();
        public TreatmentLineage144CorrectionService Service { get; }

        private Fixture(CropQcDbContext db)
        {
            Db = db;
            Service = new(
                db,
                Ledger,
                new AppEnvironmentOptions { Kind = AppEnvironmentKinds.Development },
                new PacificBusinessTimeService(new FixedClock(at)),
                NullLogger<TreatmentLineage144CorrectionService>.Instance);
        }

        public static async Task<Fixture> CreateAsync()
        {
            var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>()
                .UseInMemoryDatabase($"treatment-lineage-144-{Guid.NewGuid():N}").Options);
            await db.Database.EnsureCreatedAsync();
            var fixture = new Fixture(db);
            await fixture.SeedAsync();
            return fixture;
        }

        public TreatmentLineage144CorrectionRequest Request(TreatmentLineage144CorrectionPreflight preflight) => new(
            true,
            false,
            true,
            113,
            BackupSha,
            "admin@example.test",
            "Remove the rematerialized untreated lineage proven consumed by movement #203.",
            preflight.TargetFingerprint,
            preflight.ProtectedFingerprint,
            TreatmentLineage144CorrectionConstants.ApplyAuthorizationToken);

        public TreatmentLineageMovement Reversal() => new()
        {
            Id = 999,
            OperationKey = "test-reversal",
            MovementType = TreatmentLineageMovementTypes.BinsRunReversal,
            IdentityKey = TreatmentLineage144CorrectionConstants.IdentityKey,
            TreatmentStateSnapshot = TreatmentLineageStates.Untreated,
            TreatmentSignatureSnapshot = "u",
            BinCount = 132,
            ReversesTreatmentLineageMovementId = 203,
            OccurredAt = at,
            CreatedAt = at
        };

        private async Task SeedAsync()
        {
            var role = await Db.Roles.SingleOrDefaultAsync(x => x.Name == BuiltInRoleNames.Admin)
                ?? new Role { Id = 9000, Name = BuiltInRoleNames.Admin, NormalizedName = BuiltInRoleNames.Normalize(BuiltInRoleNames.Admin), IsSystemRole = true, IsActive = true };
            role.IsActive = true;
            var admin = new User { Id = 9000, Email = "admin@example.test", DisplayName = "Correction Admin", Domain = "example.test", EmploymentFacility = "EBS", IsActive = true, CreatedAt = at };
            admin.UserRoles.Add(new UserRole { User = admin, Role = role });
            Db.Users.Add(admin);

            Db.TreatmentLineageSegments.AddRange(
                Segment(144, null, TreatmentLineageStates.Untreated, "u", 132),
                Segment(175, 930, TreatmentLineageStates.Confirmed, "u|a:6", 24),
                Segment(176, 927, TreatmentLineageStates.Confirmed, "u|a:7", 24),
                Segment(180, 938, TreatmentLineageStates.Confirmed, "u|a:10", 24),
                Segment(184, 944, TreatmentLineageStates.Confirmed, "u|a:12", 20));
            Db.TreatmentLineageMovements.AddRange(
                Movement(203, 188, 144, 132, "u", TreatmentLineageStates.Untreated),
                Movement(204, 189, 184, 1, "u|a:12", TreatmentLineageStates.Confirmed));
            Db.BinsRunEntries.AddRange(
                Entry(188, 225, 132, 93, TreatmentLineageStates.Untreated, "u", null),
                Entry(189, 93, 1, 92, TreatmentLineageStates.Confirmed, "u|a:12", "MCP"));
            Db.Receipts.AddRange(
                Receipt(927, "TR109201", 24),
                Receipt(930, "TR109204", 24),
                Receipt(938, "TR109211", 24),
                Receipt(944, "TR109214", 21));
            Db.BackupRunRecords.Add(new BackupRunRecord
            {
                Id = 113,
                BackupType = BackupRunTypes.PreDeployment,
                Status = BackupRunStatuses.Succeeded,
                EnvironmentName = "DisposableRestore",
                DatabaseProvider = "PostgreSQL",
                RetentionCategory = "Protected",
                StartedAt = at,
                CompletedAt = at,
                Sha256 = BackupSha,
                VerifiedAt = at,
                RetentionProcessedAt = at,
                LeaseReleasedAt = at
            });
            await Db.SaveChangesAsync();
        }

        private TreatmentLineageSegment Segment(long id, long? receiptId, string state, string signature, int bins) => new()
        {
            Id = id,
            WarehouseId = 1,
            RoomId = 8,
            ReceiptId = receiptId,
            CropYear = 2026,
            GrowerLotId = 98,
            FruitProfileId = 2,
            IdentityKey = TreatmentLineage144CorrectionConstants.IdentityKey,
            GrowerNumberSnapshot = "9100",
            GrowerNameSnapshot = "DL & JJ FARMS-HOME CONV",
            LotNumberSnapshot = "9100",
            VarietyCodeSnapshot = "GALA",
            ProductionTypeSnapshot = "Conventional",
            IsOrganicSnapshot = false,
            TreatmentState = state,
            TreatmentSignature = signature,
            CurrentBins = bins,
            CreatedAt = at,
            UpdatedAt = at,
            ConcurrencyVersion = 1
        };

        private TreatmentLineageMovement Movement(long id, long entryId, long segmentId, int bins, string signature, string state) => new()
        {
            Id = id,
            OperationKey = $"actual-run-56-entry-{entryId}",
            MovementType = TreatmentLineageMovementTypes.BinsRun,
            SourceSegmentId = segmentId,
            SourceRoomId = 8,
            IdentityKey = TreatmentLineage144CorrectionConstants.IdentityKey,
            TreatmentStateSnapshot = state,
            TreatmentSignatureSnapshot = signature,
            BinCount = bins,
            BinsRunEntryId = entryId,
            OccurredAt = at,
            CreatedAt = at
        };

        private BinsRunEntry Entry(long id, int previous, int bins, int next, string state, string signature, string? summary) => new()
        {
            Id = id,
            InventoryAdjustmentId = id,
            WarehouseId = 1,
            RoomId = 8,
            CropYear = 2026,
            GrowerLotId = 98,
            FruitProfileId = 2,
            GrowerName = "DL & JJ FARMS-HOME CONV",
            LotNumber = "9100",
            VarietyCode = "GALA",
            PreviousAvailableBins = previous,
            BinsRun = bins,
            NewAvailableBins = next,
            RunAt = at,
            CreatedAt = at,
            ActualRunId = 56,
            ActualRunRevisionId = 62,
            TransactionType = ActualRunTransactionTypes.Depletion,
            TreatmentStateSnapshot = state,
            TreatmentSignatureSnapshot = signature,
            TreatmentSummarySnapshot = summary
        };

        private Receipt Receipt(long id, string number, int bins) => new()
        {
            Id = id,
            CropYear = 2026,
            ReceivedAt = at,
            CompuTechReceiptId = number,
            WarehouseId = 1,
            RoomId = 8,
            FruitProfileId = 2,
            GrowerLotId = 98,
            GrowerNumber = "9100",
            GrowerName = "DL & JJ FARMS-HOME CONV",
            LotCode = "9100",
            BinCount = bins,
            CreatedAt = at,
            UpdatedAt = at
        };

        public async ValueTask DisposeAsync() => await Db.DisposeAsync();
    }

    private sealed class FixedLedger : IRoomInventoryLedgerQueryService
    {
        public int CurrentBins { get; set; } = 92;

        public Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(
            int? warehouseId,
            IReadOnlyCollection<int>? roomIds,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RoomInventoryLedgerSnapshot>>([Snapshot()]);

        public Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(
            int? warehouseId,
            IReadOnlyCollection<int>? roomIds,
            int? fruitProfileId,
            CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<RoomInventoryLedgerSnapshot>>([Snapshot()]);

        private RoomInventoryLedgerSnapshot Snapshot() => new(
            1, "EBS", 8, "LAMB-15", "Lamb Street 15", 2026, 98, 2,
            "DL & JJ FARMS-HOME CONV", "9100", "9100", null, "GALA", "GALA", "Gala", "Apple",
            "Conventional", false, "", CurrentBins, 0, 0, 0, 0, 0, 0, 0, CurrentBins, CurrentBins, 1,
            DateTimeOffset.Parse("2026-08-28T00:47:00Z"), DateTimeOffset.Parse("2026-08-28T00:47:00Z"), 1894);
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
