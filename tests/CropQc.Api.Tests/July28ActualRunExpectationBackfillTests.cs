using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class July28ActualRunExpectationBackfillTests
{
    private static readonly DateTimeOffset BackfillAt = DateTimeOffset.Parse("2026-08-11T20:30:00Z");

    [Fact]
    public async Task Exact_reviewed_run_backfills_one_truthful_expectation_without_operational_changes_and_is_idempotent()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.Service();
        var before = await service.PreflightAsync(CancellationToken.None);
        var adjustmentCount = await fixture.Db.RoomInventoryAdjustments.CountAsync();
        var adjustmentQuantity = await fixture.Db.RoomInventoryAdjustments.SumAsync(x => x.ChangeAmount);
        var entryCount = await fixture.Db.BinsRunEntries.CountAsync();
        var entryQuantity = await fixture.Db.BinsRunEntries.SumAsync(x => x.BinsRun);

        var first = await service.RunAsync(Request(before), CancellationToken.None);
        var second = await service.RunAsync(Request(before), CancellationToken.None);

        Assert.True(first.Success);
        Assert.True(first.Applied);
        Assert.Equal("AlreadyApplied", first.Preflight.State);
        Assert.True(second.Success);
        Assert.True(second.AlreadyApplied);
        Assert.False(second.Applied);
        var expectation = await fixture.Db.RunExpectations.Include(x => x.Sources).SingleAsync();
        Assert.Equal(1, expectation.ActualRunId);
        Assert.Equal(1, expectation.ActualRunRevisionId);
        Assert.Equal(DateTimeOffset.Parse("2026-07-29T00:31:00Z"), expectation.RunAtSnapshot);
        Assert.Equal(BackfillAt, expectation.CalculatedAt);
        Assert.Equal(184, expectation.TotalBins);
        Assert.Equal(1, expectation.CreatedByUserId);
        var source = Assert.Single(expectation.Sources);
        Assert.Equal(31, source.BinsRunEntryId);
        Assert.Equal(2026, source.CropYearSnapshot);
        Assert.Equal(17, source.FruitProfileId);
        Assert.Null(source.QcSampleTakenAtSnapshot);
        Assert.Equal(184, source.BinsContributed);
        Assert.Equal(100m, source.ContributionPercent);
        Assert.True(RunExpectationMetadata.TryGetHistoricalReconstruction(expectation.ConfigurationSnapshotJson, out var reconstruction));
        Assert.Equal(expectation.RunAtSnapshot, reconstruction!.QcEvidenceCutoff);
        Assert.Equal(July28ActualRunExpectationBackfillConstants.HistoricalReconstructionPackageIdentifier, reconstruction.CorrectionPackageIdentifier);
        Assert.Equal(adjustmentCount, await fixture.Db.RoomInventoryAdjustments.CountAsync());
        Assert.Equal(adjustmentQuantity, await fixture.Db.RoomInventoryAdjustments.SumAsync(x => x.ChangeAmount));
        Assert.Equal(entryCount, await fixture.Db.BinsRunEntries.CountAsync());
        Assert.Equal(entryQuantity, await fixture.Db.BinsRunEntries.SumAsync(x => x.BinsRun));
        Assert.Equal(before.ProtectedFingerprint, first.Preflight.ProtectedFingerprint);
        Assert.Equal(before.Integrity, first.Preflight.Integrity);
        var audit = await fixture.Db.AuditLogs.SingleAsync(x => x.EntityName == July28ActualRunExpectationBackfillConstants.AuditEntityName);
        Assert.Equal(1, audit.UserId);
        Assert.Contains("Historical reconstruction calculated", audit.AfterValuesJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Existing_expectation_without_exact_backfill_audit_is_refused()
    {
        await using var fixture = await Fixture.CreateAsync();
        var run = await fixture.Db.ActualRuns.SingleAsync();
        var revision = await fixture.Db.ActualRunRevisions.SingleAsync();
        var entry = await fixture.Db.BinsRunEntries.SingleAsync();
        await new TestExpectationService(fixture.Db).CreateFrozenAsync(run, revision, [entry], 1, BackfillAt, CancellationToken.None);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.ChangeTracker.Clear();

        var preflight = await fixture.Service().PreflightAsync(CancellationToken.None);

        Assert.Equal("Refused", preflight.State);
        Assert.Contains(preflight.Issues, x => x.Contains("audit", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("revision")]
    [InlineData("quantity")]
    [InlineData("entry-set")]
    [InlineData("reversed")]
    [InlineData("facility")]
    [InlineData("organic")]
    [InlineData("operator")]
    [InlineData("packout")]
    public async Task Preflight_fails_closed_when_reviewed_evidence_changes(string mutation)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.MutateAsync(mutation);

        var preflight = await fixture.Service().PreflightAsync(CancellationToken.None);

        Assert.Equal("Refused", preflight.State);
        Assert.NotEmpty(preflight.Issues);
        Assert.Empty(await fixture.Db.RunExpectations.ToListAsync());
        Assert.Empty(await fixture.Db.AuditLogs.Where(x => x.EntityName == July28ActualRunExpectationBackfillConstants.AuditEntityName).ToListAsync());
    }

    [Fact]
    public async Task Apply_refuses_stale_protected_fingerprint_without_writes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.Service();
        var reviewed = await service.PreflightAsync(CancellationToken.None);
        fixture.Db.GrowerLots.Add(new GrowerLot
        {
            Grower = "Concurrent grower",
            LotNumber = "CHANGED",
            CreatedAt = BackfillAt,
            UpdatedAt = BackfillAt
        });
        await fixture.Db.SaveChangesAsync();

        var result = await service.RunAsync(Request(reviewed), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("fingerprint", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await fixture.Db.RunExpectations.ToListAsync());
        Assert.Empty(await fixture.Db.AuditLogs.Where(x => x.EntityName == July28ActualRunExpectationBackfillConstants.AuditEntityName).ToListAsync());
    }

    [Fact]
    public async Task Failed_expectation_calculation_leaves_no_partial_rows()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.Service(expectationService: new ThrowingExpectationService());
        var preflight = await service.PreflightAsync(CancellationToken.None);

        var result = await service.RunAsync(Request(preflight), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("rolled back", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await fixture.Db.RunExpectations.ToListAsync());
        Assert.Empty(await fixture.Db.RunExpectationSources.ToListAsync());
        Assert.Empty(await fixture.Db.AuditLogs.Where(x => x.EntityName == July28ActualRunExpectationBackfillConstants.AuditEntityName).ToListAsync());
    }

    [Fact]
    public async Task Forced_post_write_failure_rolls_back_expectation_sources_and_audit()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.Service(invariant: new ThrowingInvariantService());
        var preflight = await service.PreflightAsync(CancellationToken.None);

        var result = await service.RunAsync(Request(preflight), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Empty(await fixture.Db.RunExpectations.ToListAsync());
        Assert.Empty(await fixture.Db.RunExpectationSources.ToListAsync());
        Assert.Empty(await fixture.Db.AuditLogs.Where(x => x.EntityName == July28ActualRunExpectationBackfillConstants.AuditEntityName).ToListAsync());
        Assert.Equal(-128, await fixture.Db.RoomInventoryAdjustments.SumAsync(x => x.ChangeAmount));
        Assert.Equal(184, await fixture.Db.BinsRunEntries.SumAsync(x => x.BinsRun));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Exact_run62_package_attestation_is_limited_to_disposable_restores(bool production, bool succeeds)
    {
        await using var fixture = await Fixture.CreateAsync(production);
        var backup = await fixture.Db.BackupRunRecords.FindAsync(62L);
        backup!.Status = BackupRunStatuses.Running;
        backup.VerifiedAt = null;
        backup.RetentionProcessedAt = null;
        backup.LeaseReleasedAt = null;
        await fixture.Db.SaveChangesAsync();
        var service = fixture.Service();
        var preflight = await service.PreflightAsync(CancellationToken.None);
        var request = Request(preflight) with
        {
            ConfirmDisposableRestore = true,
            VerifiedBackupPackageSha256 = July28ActualRunExpectationBackfillConstants.VerifiedRestorePackageSha256
        };

        var result = await service.RunAsync(request, CancellationToken.None);

        Assert.Equal(succeeds, result.Success);
        Assert.Equal(succeeds, result.Applied);
    }

    private static July28ActualRunExpectationBackfillRequest Request(July28ActualRunExpectationBackfillPreflight preflight) => new(
        true,
        true,
        false,
        62,
        null,
        "wes@fruitandland.com",
        "Reconstruct the missing historical expectation for reviewed Actual Run #1 without inventory movement.",
        preflight.TargetFingerprint,
        preflight.ProtectedFingerprint,
        July28ActualRunExpectationBackfillConstants.ApplyAuthorizationToken);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly bool production;
        public CropQcDbContext Db { get; }

        private Fixture(SqliteConnection connection, CropQcDbContext db, bool production)
        {
            this.connection = connection;
            Db = db;
            this.production = production;
        }

        public static async Task<Fixture> CreateAsync(bool production = false)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var fixture = new Fixture(connection, db, production);
            await fixture.SeedAsync();
            return fixture;
        }

        public July28ActualRunExpectationBackfillService Service(
            IRunExpectationService? expectationService = null,
            IInventoryDeductionInvariantService? invariant = null) => new(
                Db,
                new AppEnvironmentOptions { Kind = production ? AppEnvironmentKinds.Production : AppEnvironmentKinds.Development },
                new PacificBusinessTimeService(new FixedClock(BackfillAt)),
                expectationService ?? new TestExpectationService(Db),
                invariant ?? new TestInvariantService(),
                NullLogger<July28ActualRunExpectationBackfillService>.Instance);

        public async Task MutateAsync(string mutation)
        {
            var run = await Db.ActualRuns.SingleAsync();
            var revision = await Db.ActualRunRevisions.SingleAsync();
            var entry = await Db.BinsRunEntries.SingleAsync();
            switch (mutation)
            {
                case "revision":
                    revision.IsCurrent = false;
                    Db.ActualRunRevisions.Add(new ActualRunRevision
                    {
                        ActualRun = run,
                        RevisionNumber = 2,
                        OperationType = ActualRunRevisionTypes.Edit,
                        OperationKey = "unexpected-current-revision",
                        IsCurrent = true,
                        CreatedAt = BackfillAt,
                        CreatedByUserId = 8
                    });
                    run.CurrentRevisionNumber = 2;
                    break;
                case "quantity":
                    entry.BinsRun = 183;
                    break;
                case "entry-set":
                    entry.ActualRunId = null;
                    entry.ActualRunRevisionId = null;
                    entry.TransactionType = ActualRunTransactionTypes.Legacy;
                    break;
                case "reversed":
                    entry.IsReversed = true;
                    break;
                case "facility":
                    run.RunFacilityCodeSnapshot = EmploymentFacilities.Ebs;
                    break;
                case "organic":
                    entry.IsOrganicSnapshot = true;
                    break;
                case "operator":
                    run.CreatedByUserId = 5;
                    break;
                case "packout":
                    Db.PackoutRuns.Add(Packout(run));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation));
            }
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        private async Task SeedAsync()
        {
            var warehouse = await Db.Warehouses.FindAsync(4)
                ?? new Warehouse { Id = 4, Code = EmploymentFacilities.Wp, Name = "WP" };
            var room = await Db.Rooms.FindAsync(1)
                ?? new Room { Id = 1, Warehouse = warehouse, WarehouseId = 4, Code = "WP-4", Name = "Room 4", CropQcRoomName = "Room 4" };
            var profile = await Db.FruitProfiles.FindAsync(17)
                ?? new FruitProfile { Id = 17, Name = "Bartlett", VarietyCode = "BART", FruitType = "Pear", ProductionType = "Conventional", IsOrganic = false };
            var lot = new GrowerLot { Id = 398, Grower = "WP Orchard Conventional", LotNumber = "1084", CreatedAt = BackfillAt, UpdatedAt = BackfillAt };
            var alexis = new User { Id = 8, Email = July28ActualRunExpectationBackfillConstants.HistoricalOperatorEmail, DisplayName = "Alexis Ledezma", CreatedAt = BackfillAt, EmploymentFacility = EmploymentFacilities.Wp };
            var wes = new User { Id = 1, Email = "wes@fruitandland.com", DisplayName = "Wes", CreatedAt = BackfillAt, EmploymentFacility = EmploymentFacilities.Shared };
            var ada = new User { Id = 5, Email = "ada@wp-packing.com", DisplayName = "Ada", CreatedAt = BackfillAt, EmploymentFacility = EmploymentFacilities.Wp };
            var adminRole = await Db.Roles.SingleAsync(x => x.Name == BuiltInRoleNames.Admin);
            wes.UserRoles.Add(new UserRole { Role = adminRole });
            if (Db.Entry(warehouse).State == EntityState.Detached) Db.Add(warehouse);
            if (Db.Entry(room).State == EntityState.Detached) Db.Add(room);
            if (Db.Entry(profile).State == EntityState.Detached) Db.Add(profile);
            Db.AddRange(lot, alexis, wes, ada);
            var receipt = new Receipt
            {
                Id = 122,
                CropYear = 2026,
                ReceivedAt = DateTimeOffset.Parse("2026-07-30T22:31:00Z"),
                CompuTechReceiptId = "TR508180",
                Warehouse = warehouse,
                WarehouseId = 4,
                Room = room,
                RoomId = 1,
                FruitProfile = profile,
                FruitProfileId = 17,
                GrowerLot = lot,
                GrowerLotId = 398,
                GrowerNumber = "1084",
                GrowerName = "WP Orchard Conventional",
                LotCode = "1084",
                BinCount = 56,
                CreatedAt = DateTimeOffset.Parse("2026-07-30T22:33:34.921188Z"),
                UpdatedAt = DateTimeOffset.Parse("2026-07-30T22:33:34.921188Z")
            };
            var runAt = DateTimeOffset.Parse("2026-07-29T00:31:00Z");
            var createdAt = DateTimeOffset.Parse("2026-07-31T00:32:42.065870Z");
            var run = new ActualRun
            {
                Id = 1,
                Status = ActualRunStatuses.Active,
                CurrentRevisionNumber = 1,
                ConcurrencyVersion = 1,
                RunAt = runAt,
                CreatedByUser = alexis,
                CreatedByUserId = 8,
                CreatedAt = createdAt,
                RunFacilityWarehouseId = 4,
                RunFacilityCodeSnapshot = EmploymentFacilities.Wp,
                RunFacilityAssignmentSource = "ReviewedProductionBackfill:20260804-run40",
                RunFacilityAssignedAt = DateTimeOffset.Parse("2026-08-05T04:28:53.562855Z")
            };
            var revision = new ActualRunRevision
            {
                Id = 1,
                ActualRun = run,
                ActualRunId = 1,
                RevisionNumber = 1,
                OperationType = ActualRunRevisionTypes.Create,
                OperationKey = "2dc80673fb2a40c8a3a4fbd3a75658b0",
                IsCurrent = true,
                CreatedByUser = alexis,
                CreatedByUserId = 8,
                CreatedAt = createdAt
            };
            var sourceAdjustment = new RoomInventoryAdjustment
            {
                Id = 114,
                Receipt = receipt,
                ReceiptId = 122,
                Warehouse = warehouse,
                WarehouseId = 4,
                Room = room,
                RoomId = 1,
                GrowerLot = lot,
                GrowerLotId = 398,
                FruitProfile = profile,
                FruitProfileId = 17,
                GrowerName = "WP Orchard Conventional",
                LotNumber = "1084",
                CropYear = 2026,
                ChangeAmount = 56,
                NewBinCount = 56,
                AdjustmentType = "ReceiptAdd",
                Source = "Receiving inventory added",
                AdjustmentAt = DateTimeOffset.Parse("2026-07-30T22:31:00Z"),
                CreatedByUserId = 5,
                CreatedAt = DateTimeOffset.Parse("2026-07-30T22:33:35.036268Z")
            };
            var depletion = new RoomInventoryAdjustment
            {
                Id = 115,
                ActualRun = run,
                ActualRunId = 1,
                ActualRunRevision = revision,
                ActualRunRevisionId = 1,
                Warehouse = warehouse,
                WarehouseId = 4,
                Room = room,
                RoomId = 1,
                GrowerLot = lot,
                GrowerLotId = 398,
                FruitProfile = profile,
                FruitProfileId = 17,
                GrowerName = "WP Orchard Conventional",
                LotNumber = "1084",
                VarietyCode = "BART",
                OldBinCount = 261,
                ChangeAmount = -184,
                NewBinCount = 77,
                AdjustmentType = "BinsRun",
                Source = "Actual Run #1",
                AdjustmentAt = runAt,
                CreatedByUserId = 8,
                CreatedAt = DateTimeOffset.Parse("2026-07-31T00:32:42.289775Z"),
                InventoryInvariantVersion = 1
            };
            var entry = new BinsRunEntry
            {
                Id = 31,
                SourceInventoryAdjustment = sourceAdjustment,
                SourceInventoryAdjustmentId = 114,
                InventoryAdjustment = depletion,
                InventoryAdjustmentId = 115,
                ActualRun = run,
                ActualRunId = 1,
                ActualRunRevision = revision,
                ActualRunRevisionId = 1,
                Warehouse = warehouse,
                WarehouseId = 4,
                Room = room,
                RoomId = 1,
                CropYear = 2026,
                GrowerLot = lot,
                GrowerLotId = 398,
                FruitProfile = profile,
                FruitProfileId = 17,
                GrowerName = "WP Orchard Conventional",
                GrowerNumberSnapshot = "1084",
                LotNumber = "1084",
                PreviousAvailableBins = 261,
                BinsRun = 184,
                NewAvailableBins = 77,
                RunAt = runAt,
                CreatedByUser = alexis,
                CreatedByUserId = 8,
                CreatedAt = createdAt,
                TransactionType = ActualRunTransactionTypes.Depletion,
                ReportingFacilityWarehouseId = 4,
                ReportingFacilityCodeSnapshot = EmploymentFacilities.Wp,
                ReportingFacilityAssignmentSource = "ReviewedProductionBackfill:20260804-run40",
                ReportingFacilityAssignedAt = DateTimeOffset.Parse("2026-08-05T04:28:53.562855Z"),
                ReportingCropYearSnapshot = 2026,
                ReportingFruitProfileIdSnapshot = 17,
                ReportingVarietyCodeSnapshot = "BART",
                ProductionTypeSnapshot = "Conventional",
                IsOrganicSnapshot = false
            };
            Db.AddRange(receipt, run, revision, sourceAdjustment, depletion, entry);
            Db.BackupRunRecords.Add(new BackupRunRecord
            {
                Id = 62,
                BackupType = BackupRunTypes.PreDeployment,
                Status = BackupRunStatuses.Succeeded,
                EnvironmentName = "Restored test",
                DatabaseProvider = "Sqlite",
                RetentionCategory = BackupRunTypes.PreDeployment,
                StartedAt = BackfillAt.AddMinutes(-5),
                CompletedAt = BackfillAt,
                VerifiedAt = BackfillAt,
                RetentionProcessedAt = BackfillAt,
                LeaseReleasedAt = BackfillAt
            });
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        private static PackoutRun Packout(ActualRun run) => new()
        {
            ActualRun = run,
            ActualRunId = run.Id,
            Status = PackoutRunStatuses.Review,
            FacilitySnapshot = EmploymentFacilities.Wp,
            PackingDate = new DateOnly(2026, 7, 28),
            RunNumber = 1,
            LotNumberSnapshot = "1084",
            VarietySnapshot = "BART",
            CropYearSnapshot = 2026,
            DumpedBins = 184,
            PoundsPerBin = 920,
            DumpedPounds = 169280,
            CreatedAt = BackfillAt,
            UpdatedAt = BackfillAt
        };

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class TestInvariantService : IInventoryDeductionInvariantService
    {
        public Task ValidateBeforeCommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<InventoryDeductionReadinessResult> VerifyReadinessAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new InventoryDeductionReadinessResult(1, 0, 1, []));
    }

    private sealed class TestExpectationService(CropQcDbContext db) : IRunExpectationService
    {
        public Task<RunExpectation> CreateFrozenAsync(
            ActualRun actualRun,
            ActualRunRevision revision,
            IReadOnlyList<BinsRunEntry> activeEntries,
            int userId,
            DateTimeOffset calculatedAt,
            CancellationToken cancellationToken)
        {
            var expectation = new RunExpectation
            {
                ActualRun = actualRun,
                ActualRunId = actualRun.Id,
                ActualRunRevision = revision,
                ActualRunRevisionId = revision.Id,
                RevisionNumber = revision.RevisionNumber,
                FacilityWarehouseId = 4,
                FacilitySnapshot = EmploymentFacilities.Wp,
                RunAtSnapshot = actualRun.RunAt,
                TotalBins = activeEntries.Sum(x => x.BinsRun),
                GrossPounds = 169280m,
                ExpectedPackoutPercent = 80m,
                ExpectedPackedPounds = 135424m,
                ExpectedPackedBoxes = 3385.6m,
                ExpectedWholeBoxes = 3386,
                ExpectedCullPounds = 33856m,
                ExpectedJuicePounds = 13542.4m,
                ExpectedPeelerPounds = 10156.8m,
                ExpectedWastePounds = 10156.8m,
                SizeDistributionSnapshotJson = "{}",
                GradeDistributionSnapshotJson = "{}",
                ConfigurationSnapshotJson = "{}",
                CalculationVersion = RunExpectationCalculationVersions.Current,
                CalculatedAt = calculatedAt,
                CreatedByUserId = userId
            };
            foreach (var entry in activeEntries)
            {
                expectation.Sources.Add(new RunExpectationSource
                {
                    BinsRunEntryId = entry.Id,
                    WarehouseId = entry.WarehouseId,
                    RoomId = entry.RoomId,
                    FacilitySnapshot = EmploymentFacilities.Wp,
                    RoomSnapshot = "Room 4",
                    CropYearSnapshot = entry.ReportingCropYearSnapshot ?? entry.CropYear,
                    GrowerLotId = entry.GrowerLotId,
                    FruitProfileId = entry.ReportingFruitProfileIdSnapshot ?? entry.FruitProfileId,
                    GrowerSnapshot = entry.GrowerName,
                    LotSnapshot = entry.LotNumber,
                    VarietySnapshot = "Bartlett",
                    ProductionTypeSnapshot = "Conventional",
                    IsOrganicSnapshot = false,
                    BinsContributed = entry.BinsRun,
                    ContributionPercent = 100m,
                    QcMeasurementSnapshotJson = "{}",
                    SizeDistributionSnapshotJson = "{}",
                    GradeDistributionSnapshotJson = "{}"
                });
            }
            db.RunExpectations.Add(expectation);
            return Task.FromResult(expectation);
        }
    }

    private sealed class ThrowingInvariantService : IInventoryDeductionInvariantService
    {
        public Task ValidateBeforeCommitAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Expected forced post-write rollback.");
        public Task<InventoryDeductionReadinessResult> VerifyReadinessAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException();
    }

    private sealed class ThrowingExpectationService : IRunExpectationService
    {
        public Task<RunExpectation> CreateFrozenAsync(ActualRun actualRun, ActualRunRevision revision, IReadOnlyList<BinsRunEntry> activeEntries, int userId, DateTimeOffset calculatedAt, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Expected expectation calculation failure.");
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }
}
