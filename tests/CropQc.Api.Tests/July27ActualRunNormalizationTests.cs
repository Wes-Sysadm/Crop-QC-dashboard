using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class July27ActualRunNormalizationTests
{
    private static readonly DateTimeOffset CorrectionAt = DateTimeOffset.Parse("2026-08-11T18:00:00Z");

    [Fact]
    public async Task Exact_reviewed_rows_convert_once_without_new_inventory_or_reporting_changes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.Service();
        var before = await service.PreflightAsync(CancellationToken.None);
        var adjustmentCount = await fixture.Db.RoomInventoryAdjustments.CountAsync();
        var entryCount = await fixture.Db.BinsRunEntries.CountAsync();

        var first = await service.RunAsync(Request(before), CancellationToken.None);
        var second = await service.RunAsync(Request(before), CancellationToken.None);

        Assert.True(first.Success);
        Assert.True(first.Applied);
        Assert.Equal("AlreadyApplied", first.Preflight.State);
        Assert.True(second.Success);
        Assert.True(second.AlreadyApplied);
        Assert.False(second.Applied);
        Assert.Equal(adjustmentCount, await fixture.Db.RoomInventoryAdjustments.CountAsync());
        Assert.Equal(entryCount, await fixture.Db.BinsRunEntries.CountAsync());
        Assert.Single(await fixture.Db.ActualRuns.ToListAsync());
        Assert.Single(await fixture.Db.ActualRunRevisions.ToListAsync());
        var expectation = Assert.Single(await fixture.Db.RunExpectations.Include(x => x.Sources).ToListAsync());
        Assert.Equal(184, expectation.TotalBins);
        Assert.Equal([28L, 29L, 30L], expectation.Sources.OrderBy(x => x.BinsRunEntryId).Select(x => x.BinsRunEntryId));
        Assert.Equal(1, expectation.CreatedByUserId);
        var run = await fixture.Db.ActualRuns.SingleAsync();
        Assert.Equal(8, run.CreatedByUserId);
        Assert.Equal(July27ActualRunNormalizationConstants.HistoricalOperatorEmail, (await fixture.Db.Users.FindAsync(run.CreatedByUserId))!.Email);
        Assert.All(await fixture.Db.BinsRunEntries.OrderBy(x => x.Id).ToListAsync(), entry =>
        {
            Assert.Equal(run.Id, entry.ActualRunId);
            Assert.Equal(ActualRunTransactionTypes.Depletion, entry.TransactionType);
        });
        Assert.Equal(new[] { 64, 62, 58 }, await fixture.Db.BinsRunEntries.OrderBy(x => x.Id).Select(x => x.BinsRun).ToArrayAsync());
        Assert.Equal(new[] { -64, -62, -58 }, await fixture.Db.RoomInventoryAdjustments.Where(x => x.Id >= 89).OrderBy(x => x.Id).Select(x => x.ChangeAmount).ToArrayAsync());
        var audit = Assert.Single(await fixture.Db.AuditLogs.Where(x => x.EntityName == July27ActualRunNormalizationConstants.AuditEntityName).ToListAsync());
        Assert.Equal(1, audit.UserId);
        Assert.Equal(before.ProtectedFingerprint, first.Preflight.ProtectedFingerprint);
        Assert.Equal(before.Reporting, first.Preflight.Reporting);
    }

    [Theory]
    [InlineData("quantity")]
    [InlineData("linked")]
    [InlineData("adjustment")]
    [InlineData("reversed")]
    [InlineData("facility")]
    [InlineData("organic")]
    [InlineData("grouping")]
    [InlineData("duplicate-run")]
    [InlineData("expectation")]
    [InlineData("operator")]
    public async Task Preflight_fails_closed_for_reviewed_negative_cases(string mutation)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.MutateAsync(mutation);

        var preflight = await fixture.Service().PreflightAsync(CancellationToken.None);

        Assert.Equal("Refused", preflight.State);
        Assert.NotEmpty(preflight.Issues);
        Assert.Empty(await fixture.Db.AuditLogs.Where(x => x.EntityName == July27ActualRunNormalizationConstants.AuditEntityName).ToListAsync());
    }

    [Fact]
    public async Task Apply_refuses_stale_protected_fingerprint_without_writes()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.Service();
        var reviewed = await service.PreflightAsync(CancellationToken.None);
        fixture.Db.Receipts.Add(Fixture.Receipt(999, "CONCURRENT", null, 1));
        await fixture.Db.SaveChangesAsync();

        var result = await service.RunAsync(Request(reviewed), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("fingerprint", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await fixture.Db.ActualRuns.ToListAsync());
        Assert.Empty(await fixture.Db.ActualRunRevisions.ToListAsync());
        Assert.Empty(await fixture.Db.RunExpectations.ToListAsync());
    }

    [Fact]
    public async Task Mid_transaction_failure_rolls_back_every_normalization_write()
    {
        await using var fixture = await Fixture.CreateAsync();
        var preflight = await fixture.Service().PreflightAsync(CancellationToken.None);

        var result = await fixture.Service(new ThrowingExpectationService())
            .RunAsync(Request(preflight), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("rolled back", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await fixture.Db.ActualRuns.ToListAsync());
        Assert.Empty(await fixture.Db.ActualRunRevisions.ToListAsync());
        Assert.Empty(await fixture.Db.RunExpectations.ToListAsync());
        Assert.Empty(await fixture.Db.AuditLogs.Where(x => x.EntityName == July27ActualRunNormalizationConstants.AuditEntityName).ToListAsync());
        Assert.All(await fixture.Db.BinsRunEntries.ToListAsync(), x =>
        {
            Assert.Null(x.ActualRunId);
            Assert.Equal(ActualRunTransactionTypes.Legacy, x.TransactionType);
        });
        Assert.All(await fixture.Db.RoomInventoryAdjustments.Where(x => x.Id >= 89).ToListAsync(), x => Assert.Null(x.ActualRunId));
    }

    [Fact]
    public async Task Apply_requires_token_backup_reason_and_production_confirmation()
    {
        await using var fixture = await Fixture.CreateAsync(production: true);
        var service = fixture.Service();
        var preflight = await service.PreflightAsync(CancellationToken.None);

        var result = await service.RunAsync(new(
            true, false, false, null, null, "wes@fruitandland.com", "", preflight.TargetFingerprint,
            preflight.ProtectedFingerprint, "wrong"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("token", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await fixture.Db.ActualRuns.ToListAsync());
    }

    [Fact]
    public async Task Apply_requires_an_active_builtin_admin_as_the_correction_identity()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.Service();
        var preflight = await service.PreflightAsync(CancellationToken.None);
        var wesRole = await fixture.Db.UserRoles.SingleAsync(x => x.UserId == 1);
        fixture.Db.UserRoles.Remove(wesRole);
        await fixture.Db.SaveChangesAsync();

        var result = await service.RunAsync(Request(preflight), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("Admin role", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await fixture.Db.ActualRuns.ToListAsync());
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
            VerifiedBackupPackageSha256 = July27ActualRunNormalizationConstants.VerifiedRestorePackageSha256
        };

        var result = await service.RunAsync(request, CancellationToken.None);

        Assert.Equal(succeeds, result.Success);
        Assert.Equal(succeeds, result.Applied);
    }

    private static July27ActualRunNormalizationRequest Request(July27ActualRunNormalizationPreflight preflight) => new(
        true,
        true,
        false,
        62,
        null,
        "wes@fruitandland.com",
        "Normalize the exact reviewed July 27 historical Bins Run into one Actual Run without inventory movement.",
        preflight.TargetFingerprint,
        preflight.ProtectedFingerprint,
        July27ActualRunNormalizationConstants.ApplyAuthorizationToken);

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

        public July27ActualRunNormalizationService Service(IRunExpectationService? expectationService = null) => new(
            Db,
            new AppEnvironmentOptions { Kind = production ? AppEnvironmentKinds.Production : AppEnvironmentKinds.Development },
            new PacificBusinessTimeService(new FixedClock(CorrectionAt)),
            expectationService ?? new TestExpectationService(Db),
            new TestInvariantService(),
            NullLogger<July27ActualRunNormalizationService>.Instance);

        public async Task MutateAsync(string mutation)
        {
            switch (mutation)
            {
                case "quantity":
                    (await Db.BinsRunEntries.FindAsync(28L))!.BinsRun = 63;
                    break;
                case "linked":
                    var linked = OtherRun(700);
                    Db.ActualRuns.Add(linked.Run);
                    Db.ActualRunRevisions.Add(linked.Revision);
                    await Db.SaveChangesAsync();
                    var entry = (await Db.BinsRunEntries.FindAsync(28L))!;
                    entry.ActualRunId = linked.Run.Id;
                    entry.ActualRunRevisionId = linked.Revision.Id;
                    entry.TransactionType = ActualRunTransactionTypes.Depletion;
                    break;
                case "adjustment":
                    (await Db.RoomInventoryAdjustments.FindAsync(89L))!.ChangeAmount = -63;
                    break;
                case "reversed":
                    (await Db.BinsRunEntries.FindAsync(28L))!.IsReversed = true;
                    break;
                case "facility":
                    (await Db.BinsRunEntries.FindAsync(28L))!.ReportingFacilityCodeSnapshot = EmploymentFacilities.Ebs;
                    break;
                case "organic":
                    (await Db.BinsRunEntries.FindAsync(28L))!.IsOrganicSnapshot = true;
                    break;
                case "grouping":
                    var groupingReceipt = (await Db.Receipts.FindAsync(92L))!;
                    Db.RoomInventoryAdjustments.Add(DepletionAdjustment(99, groupingReceipt, null, 1, -1, 0, "WINDY POINT"));
                    Db.BinsRunEntries.Add(Line(31, groupingReceipt, 99, 82, 1, 1, 0, "WINDY POINT", null, DateTimeOffset.Parse("2026-07-28T05:15:00Z")));
                    break;
                case "duplicate-run":
                    var duplicate = OtherRun(701, DateTimeOffset.Parse("2026-07-28T05:11:00Z"));
                    Db.ActualRuns.Add(duplicate.Run);
                    Db.ActualRunRevisions.Add(duplicate.Revision);
                    break;
                case "expectation":
                    var unexpected = OtherRun(702);
                    Db.ActualRuns.Add(unexpected.Run);
                    Db.ActualRunRevisions.Add(unexpected.Revision);
                    await Db.SaveChangesAsync();
                    var expectation = TestExpectationService.Expectation(unexpected.Run, unexpected.Revision, 8);
                    expectation.Sources.Add(TestExpectationService.Source(28));
                    Db.RunExpectations.Add(expectation);
                    break;
                case "operator":
                    (await Db.Users.FindAsync(8))!.Email = "changed@example.com";
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
            var lot = new GrowerLot { Id = 398, Grower = "WP Orchard Conventional", LotNumber = "1084", CreatedAt = CorrectionAt, UpdatedAt = CorrectionAt };
            var alexis = new User { Id = 8, Email = July27ActualRunNormalizationConstants.HistoricalOperatorEmail, DisplayName = "Alexis Ledezma", CreatedAt = CorrectionAt, EmploymentFacility = EmploymentFacilities.Wp };
            var wes = new User { Id = 1, Email = "wes@fruitandland.com", DisplayName = "Wes", CreatedAt = CorrectionAt, EmploymentFacility = EmploymentFacilities.Shared };
            var ada = new User { Id = 5, Email = "ada@wp-packing.com", DisplayName = "Ada", CreatedAt = CorrectionAt, EmploymentFacility = EmploymentFacilities.Wp };
            var adminRole = await Db.Roles.SingleAsync(x => x.Name == BuiltInRoleNames.Admin);
            wes.UserRoles.Add(new UserRole { Role = adminRole });
            if (Db.Entry(warehouse).State == EntityState.Detached) Db.Add(warehouse);
            if (Db.Entry(room).State == EntityState.Detached) Db.Add(room);
            if (Db.Entry(profile).State == EntityState.Detached) Db.Add(profile);
            Db.AddRange(lot, alexis, wes, ada);
            var receipts = new[] { Receipt(92, "TR508157", null, 64), Receipt(94, "TR508160", 398, 64), Receipt(96, "TR508162", 398, 62) };
            foreach (var receipt in receipts)
            {
                receipt.Warehouse = warehouse;
                receipt.Room = room;
                receipt.FruitProfile = profile;
                if (receipt.GrowerLotId is not null) receipt.GrowerLot = lot;
            }
            Db.Receipts.AddRange(receipts);
            Db.RoomInventoryAdjustments.AddRange(
                SourceAdjustment(82, receipts[0], null, 64, 0, 64, "WINDY POINT", 8),
                SourceAdjustment(84, receipts[1], 398, null, 64, 64, "WP Orchard Conventional", 5),
                SourceAdjustment(86, receipts[2], 398, null, 62, 62, "WP Orchard Conventional", 5),
                DepletionAdjustment(89, receipts[0], null, 64, -64, 0, "WINDY POINT"),
                DepletionAdjustment(90, receipts[2], 398, 62, -62, 0, "WP Orchard Conventional"),
                DepletionAdjustment(91, receipts[1], 398, 64, -58, 6, "WP Orchard Conventional"));
            Db.BinsRunEntries.AddRange(
                Line(28, receipts[0], 89, 82, 64, 64, 0, "WINDY POINT", null, DateTimeOffset.Parse("2026-07-28T05:11:26.393444Z")),
                Line(29, receipts[2], 90, 86, 62, 62, 0, "WP Orchard Conventional", 398, DateTimeOffset.Parse("2026-07-28T05:11:51.587430Z")),
                Line(30, receipts[1], 91, 84, 58, 64, 6, "WP Orchard Conventional", 398, DateTimeOffset.Parse("2026-07-28T05:12:15.093102Z")));
            Db.BackupRunRecords.Add(new BackupRunRecord
            {
                Id = 62,
                BackupType = BackupRunTypes.PreDeployment,
                Status = BackupRunStatuses.Succeeded,
                EnvironmentName = "Restored test",
                DatabaseProvider = "Sqlite",
                RetentionCategory = BackupRunTypes.PreDeployment,
                StartedAt = CorrectionAt.AddMinutes(-5),
                CompletedAt = CorrectionAt,
                VerifiedAt = CorrectionAt,
                RetentionProcessedAt = CorrectionAt,
                LeaseReleasedAt = CorrectionAt
            });
            await Db.SaveChangesAsync();
            Db.ChangeTracker.Clear();
        }

        public static Receipt Receipt(long id, string number, int? growerLotId, int bins) => new()
        {
            Id = id,
            CropYear = 2026,
            ReceivedAt = DateTimeOffset.Parse("2026-07-27T19:32:00Z"),
            CompuTechReceiptId = number,
            WarehouseId = 4,
            RoomId = 1,
            FruitProfileId = 17,
            GrowerLotId = growerLotId,
            GrowerNumber = "1084",
            GrowerName = growerLotId is null ? "WINDY POINT" : "WP Orchard Conventional",
            LotCode = "1084",
            BinCount = bins,
            CreatedAt = CorrectionAt,
            UpdatedAt = CorrectionAt
        };

        private static RoomInventoryAdjustment SourceAdjustment(long id, Receipt receipt, int? lotId, int? old, int change, int next, string grower, int userId) =>
            Adjustment(id, receipt, lotId, old, change, next, grower, userId, change == 0 ? "ReceiptEdit" : "ReceiptAdd", change == 0 ? "Admin receipt correction" : "Receiving inventory added");

        private static RoomInventoryAdjustment DepletionAdjustment(long id, Receipt receipt, int? lotId, int old, int change, int next, string grower) =>
            Adjustment(id, receipt, lotId, old, change, next, grower, 8, "BinsRun", "Bins Run");

        private static RoomInventoryAdjustment Adjustment(long id, Receipt receipt, int? lotId, int? old, int change, int next, string grower, int userId, string type, string source) => new()
        {
            Id = id,
            Receipt = receipt,
            ReceiptId = receipt.Id,
            WarehouseId = 4,
            RoomId = 1,
            GrowerLotId = lotId,
            FruitProfileId = 17,
            GrowerName = grower,
            LotNumber = "1084",
            VarietyCode = type == "BinsRun" ? "BART" : null,
            OldBinCount = old,
            ChangeAmount = change,
            NewBinCount = next,
            AdjustmentType = type,
            Source = source,
            Reason = type == "BinsRun" ? "BinsRun" : source,
            AdjustmentAt = DateTimeOffset.Parse("2026-07-28T05:11:00Z"),
            CreatedByUserId = userId,
            CreatedAt = CorrectionAt,
            InventoryInvariantVersion = 0
        };

        private static BinsRunEntry Line(long id, Receipt receipt, long adjustmentId, long sourceId, int bins, int previous, int next, string grower, int? lotId, DateTimeOffset recordedAt) => new()
        {
            Id = id,
            Receipt = receipt,
            ReceiptId = receipt.Id,
            InventoryAdjustmentId = adjustmentId,
            SourceInventoryAdjustmentId = sourceId,
            WarehouseId = 4,
            RoomId = 1,
            GrowerLotId = lotId,
            FruitProfileId = 17,
            GrowerName = grower,
            LotNumber = "1084",
            PreviousAvailableBins = previous,
            BinsRun = bins,
            NewAvailableBins = next,
            RunAt = DateTimeOffset.Parse("2026-07-28T05:11:00Z"),
            CreatedByUserId = 8,
            CreatedAt = recordedAt,
            TransactionType = ActualRunTransactionTypes.Legacy,
            ReportingFacilityWarehouseId = 4,
            ReportingFacilityCodeSnapshot = EmploymentFacilities.Wp,
            ReportingFacilityAssignmentSource = "ReviewedProductionBackfill:20260804-run40",
            ReportingCropYearSnapshot = 2026,
            ReportingFruitProfileIdSnapshot = 17,
            ReportingVarietyCodeSnapshot = "BART",
            ProductionTypeSnapshot = "Conventional",
            IsOrganicSnapshot = false,
            GrowerNumberSnapshot = "1084"
        };

        private static (ActualRun Run, ActualRunRevision Revision) OtherRun(long seed, DateTimeOffset? runAt = null)
        {
            var run = new ActualRun
            {
                Status = ActualRunStatuses.Active,
                CurrentRevisionNumber = 1,
                RunAt = runAt ?? CorrectionAt,
                CreatedByUserId = 8,
                CreatedAt = CorrectionAt,
                RunFacilityWarehouseId = 4,
                RunFacilityCodeSnapshot = EmploymentFacilities.Wp,
                RunFacilityAssignmentSource = RunFacilityAssignmentSources.Employment
            };
            var revision = new ActualRunRevision
            {
                ActualRun = run,
                RevisionNumber = 1,
                OperationType = ActualRunRevisionTypes.Create,
                OperationKey = $"negative-case-{seed}",
                IsCurrent = true,
                CreatedByUserId = 8,
                CreatedAt = CorrectionAt
            };
            return (run, revision);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class TestExpectationService(CropQcDbContext db) : IRunExpectationService
    {
        public Task<RunExpectation> CreateFrozenAsync(ActualRun run, ActualRunRevision revision, IReadOnlyList<BinsRunEntry> entries, int userId, DateTimeOffset calculatedAt, CancellationToken cancellationToken)
        {
            var expectation = Expectation(run, revision, userId);
            foreach (var entry in entries) expectation.Sources.Add(Source(entry.Id, entry.BinsRun));
            db.RunExpectations.Add(expectation);
            return Task.FromResult(expectation);
        }

        public static RunExpectation Expectation(ActualRun run, ActualRunRevision revision, int userId) => new()
        {
            ActualRun = run,
            ActualRunId = run.Id,
            ActualRunRevision = revision,
            ActualRunRevisionId = revision.Id,
            RevisionNumber = 1,
            FacilityWarehouseId = 4,
            FacilitySnapshot = EmploymentFacilities.Wp,
            RunAtSnapshot = run.RunAt,
            TotalBins = 184,
            SizeDistributionSnapshotJson = "{}",
            GradeDistributionSnapshotJson = "{}",
            ConfigurationSnapshotJson = "{}",
            CalculationVersion = RunExpectationCalculationVersions.Current,
            CalculatedAt = CorrectionAt,
            CreatedByUserId = userId
        };

        public static RunExpectationSource Source(long entryId, int bins = 1) => new()
        {
            BinsRunEntryId = entryId,
            WarehouseId = 4,
            RoomId = 1,
            FacilitySnapshot = EmploymentFacilities.Wp,
            RoomSnapshot = "Room 4",
            GrowerSnapshot = "WP",
            LotSnapshot = "1084",
            VarietySnapshot = "Bartlett",
            ProductionTypeSnapshot = "Conventional",
            BinsContributed = bins,
            QcMeasurementSnapshotJson = "{}",
            SizeDistributionSnapshotJson = "{}",
            GradeDistributionSnapshotJson = "{}"
        };
    }

    private sealed class TestInvariantService : IInventoryDeductionInvariantService
    {
        public Task ValidateBeforeCommitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<InventoryDeductionReadinessResult> VerifyReadinessAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new InventoryDeductionReadinessResult(3, 3, 0, []));
    }

    private sealed class ThrowingExpectationService : IRunExpectationService
    {
        public Task<RunExpectation> CreateFrozenAsync(ActualRun actualRun, ActualRunRevision revision, IReadOnlyList<BinsRunEntry> activeEntries, int userId, DateTimeOffset calculatedAt, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Expected transaction rollback test failure.");
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }
}
