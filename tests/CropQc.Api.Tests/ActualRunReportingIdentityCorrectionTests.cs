using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class ActualRunReportingIdentityCorrectionTests
{
    [Fact]
    public async Task ExactStateA_AppliesOnlyReportingIdentity_ThenRerunsAlreadyApplied()
    {
        await using var fixture = await Fixture.CreateAsync();
        var service = fixture.CorrectionService();
        var preflight = await service.PreflightAsync(CancellationToken.None);

        Assert.Equal("Ready", preflight.State);
        Assert.Empty(preflight.Issues);
        Assert.Equal(3, preflight.Evidence!.ActualRunId);
        Assert.Equal(33, preflight.Evidence.BinsRunEntryId);
        Assert.Equal(new DateOnly(2026, 7, 30), preflight.Evidence.PacificRunDate);
        Assert.Equal(173, preflight.Evidence.BinsRun);
        Assert.Equal(225, preflight.Evidence.EntryPreviousAvailableBins);
        Assert.Equal(52, preflight.Evidence.EntryNewAvailableBins);
        Assert.Equal("WINDY POINT", preflight.Evidence.EntryGrowerName);
        Assert.Equal(-173, preflight.Evidence.AdjustmentChangeAmount);
        Assert.Equal("WINDY POINT", preflight.Evidence.AdjustmentGrowerName);
        var protectedBefore = preflight.ProtectedFingerprint;

        var first = await service.RunAsync(fixture.Request(preflight), CancellationToken.None);

        Assert.True(first.Success);
        Assert.True(first.Applied);
        Assert.False(first.AlreadyApplied);
        Assert.Equal("AlreadyApplied", first.Preflight.State);
        Assert.Equal(protectedBefore, first.Preflight.ProtectedFingerprint);
        fixture.Db.ChangeTracker.Clear();
        var entry = await fixture.Db.BinsRunEntries.SingleAsync(x => x.Id == 33);
        Assert.Equal(4, entry.ReportingFacilityWarehouseId);
        Assert.Equal("WP", entry.ReportingFacilityCodeSnapshot);
        Assert.Equal(ActualRun3ReportingIdentityCorrectionConstants.AssignmentSource, entry.ReportingFacilityAssignmentSource);
        Assert.Equal(fixture.Clock.UtcNow, entry.ReportingFacilityAssignedAt);
        Assert.Null(entry.ReportingFacilityAssignedByUserId);
        Assert.Equal("Organic", entry.ProductionTypeSnapshot);
        Assert.True(entry.IsOrganicSnapshot);
        Assert.Equal("1080", entry.GrowerNumberSnapshot);
        Assert.Equal(2026, entry.ReportingCropYearSnapshot);
        Assert.Equal(19, entry.ReportingFruitProfileIdSnapshot);
        Assert.Equal("ORBA", entry.ReportingVarietyCodeSnapshot);
        Assert.Equal(173, entry.BinsRun);
        Assert.Equal(new DateTimeOffset(2026, 7, 31, 0, 33, 0, TimeSpan.Zero), entry.RunAt);
        Assert.Equal(19, entry.FruitProfileId);
        Assert.Equal(394, entry.GrowerLotId);
        Assert.Equal("1080", entry.LotNumber);
        Assert.Equal("ORBA", entry.VarietyCode);
        var adjustment = await fixture.Db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 117);
        Assert.Equal(-173, adjustment.ChangeAmount);
        Assert.Equal(225, adjustment.OldBinCount);
        Assert.Equal(52, adjustment.NewBinCount);
        Assert.Single(await fixture.Db.AuditLogs.Where(x =>
            x.EntityName == ActualRun3ReportingIdentityCorrectionConstants.AuditEntityName).ToListAsync());

        var beforeRerunWrites = await fixture.Db.AuditLogs.CountAsync();
        var rerun = await service.RunAsync(fixture.Request(first.Preflight), CancellationToken.None);

        Assert.True(rerun.Success);
        Assert.False(rerun.Applied);
        Assert.True(rerun.AlreadyApplied);
        Assert.Equal(beforeRerunWrites, await fixture.Db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task CorrectedEntry_EntersAuthoritativeReportingWithExactly173Bins()
    {
        await using var fixture = await Fixture.CreateAsync();
        Assert.Equal(1, await AuthoritativeRunReportingQuery.ApplyValidRules(fixture.Db.BinsRunEntries.AsNoTracking()).CountAsync());

        var preflight = await fixture.CorrectionService().PreflightAsync(CancellationToken.None);
        var applied = await fixture.CorrectionService().RunAsync(fixture.Request(preflight), CancellationToken.None);

        Assert.True(applied.Applied);
        var authoritative = await AuthoritativeRunReportingQuery.ApplyValidRules(fixture.Db.BinsRunEntries.AsNoTracking())
            .OrderBy(x => x.ActualRunId)
            .ToListAsync();
        Assert.Equal(2, authoritative.Count);
        var run3 = Assert.Single(authoritative, x => x.ActualRunId == 3);
        Assert.Equal(173, run3.BinsRun);
        Assert.Equal("ORBA", run3.ReportingVarietyCodeSnapshot);
        Assert.Equal("1080", run3.GrowerNumberSnapshot);
    }

    [Fact]
    public async Task Reconciliation_BeforeSurfacesIncompleteRun_AfterMatchesBothDaysExactly()
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = await fixture.ReconciliationService().GetAsync(EmploymentFacilities.Wp, 2026, CancellationToken.None);

        Assert.NotNull(before);
        Assert.Single(before!.Items, x => x.State == RunSheetReconciliationStates.Match && x.ActualRunIds.SequenceEqual([2L]));
        var diagnostic = Assert.Single(before.Items, x =>
            x.Reasons.Contains(RunSheetReconciliationReasons.IncompleteCropQcReportingIdentity));
        Assert.Equal([3L], diagnostic.ActualRunIds);
        Assert.Equal(new DateOnly(2026, 7, 30), diagnostic.CropQcDate);
        Assert.Equal(173, diagnostic.CropQcBins);
        Assert.Contains("cannot be reconciled safely", diagnostic.DiagnosticMessage);
        Assert.Single(before.Items, x => x.Reasons.Contains(RunSheetReconciliationReasons.MissingFromCropQc));

        var preflight = await fixture.CorrectionService().PreflightAsync(CancellationToken.None);
        Assert.True((await fixture.CorrectionService().RunAsync(fixture.Request(preflight), CancellationToken.None)).Applied);
        var after = await fixture.ReconciliationService().GetAsync(EmploymentFacilities.Wp, 2026, CancellationToken.None);

        Assert.NotNull(after);
        Assert.Equal(2, after!.MatchedCount);
        Assert.Equal(0, after.AttentionNeededCount);
        Assert.Equal(2, after.Items.Count);
        Assert.All(after.Items, x => Assert.Equal(RunSheetReconciliationStates.Match, x.State));
        Assert.Contains(after.Items, x => x.SheetDate == new DateOnly(2026, 7, 29)
            && x.CropQcDate == new DateOnly(2026, 7, 29) && x.SheetBins == 155 && x.CropQcBins == 155
            && x.ActualRunIds.SequenceEqual([2L]));
        Assert.Contains(after.Items, x => x.SheetDate == new DateOnly(2026, 7, 30)
            && x.CropQcDate == new DateOnly(2026, 7, 30) && x.SheetBins == 173 && x.CropQcBins == 173
            && x.ActualRunIds.SequenceEqual([3L]));
        Assert.DoesNotContain(after.Items, x => x.Reasons.Contains(RunSheetReconciliationReasons.MissingFromCropQc));
        Assert.DoesNotContain(after.Items, x => x.Reasons.Contains(RunSheetReconciliationReasons.MissingFromSheet));
    }

    [Theory]
    [InlineData("partial")]
    [InlineData("bins")]
    [InlineData("fruit")]
    [InlineData("grower-lot")]
    [InlineData("facility")]
    [InlineData("revision")]
    [InlineData("ledger-balance")]
    public async Task ConflictingOrChangedEvidence_FailsClosedWithZeroWrites(string mutation)
    {
        await using var fixture = await Fixture.CreateAsync();
        var entry = await fixture.Db.BinsRunEntries.SingleAsync(x => x.Id == 33);
        var run = await fixture.Db.ActualRuns.SingleAsync(x => x.Id == 3);
        var revision = await fixture.Db.ActualRunRevisions.SingleAsync(x => x.Id == 3);
        var adjustment = await fixture.Db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 117);
        switch (mutation)
        {
            case "partial": entry.ReportingFacilityCodeSnapshot = "WP"; break;
            case "bins": entry.BinsRun = 172; break;
            case "fruit": entry.FruitProfileId = 18; break;
            case "grower-lot": entry.GrowerLotId = null; break;
            case "facility": run.RunFacilityCodeSnapshot = "EBS"; break;
            case "revision": revision.IsCurrent = false; break;
            case "ledger-balance": adjustment.NewBinCount = 51; break;
        }
        await fixture.Db.SaveChangesAsync();

        var preflight = await fixture.CorrectionService().PreflightAsync(CancellationToken.None);
        var result = await fixture.CorrectionService().RunAsync(fixture.Request(preflight), CancellationToken.None);

        Assert.Equal("Refused", preflight.State);
        Assert.False(result.Success);
        Assert.False(result.Applied);
        Assert.Empty(await fixture.Db.AuditLogs.ToListAsync());
        fixture.Db.ChangeTracker.Clear();
        var unchanged = await fixture.Db.BinsRunEntries.SingleAsync(x => x.Id == 33);
        Assert.Null(unchanged.ReportingFruitProfileIdSnapshot);
        Assert.Null(unchanged.ReportingVarietyCodeSnapshot);
    }

    [Fact]
    public void CommandAndAssignmentSource_AreBoundedAndNotGenericWebRepair()
    {
        var program = Read("src", "CropQc.Web", "Program.cs");
        var service = Read("src", "CropQc.Web", "Services", "ActualRun3ReportingIdentityCorrectionService.cs");

        Assert.Contains("ActualRun3ReportingIdentityCorrectionConstants.CommandName", program);
        Assert.DoesNotContain("Controller", service);
        Assert.True(ActualRun3ReportingIdentityCorrectionConstants.AssignmentSource.Length <= 50);
        Assert.Contains("TargetRunId = 3", service);
        Assert.Contains("TargetEntryId = 33", service);
        Assert.Contains("TargetAdjustmentId = 117", service);
    }

    [Fact]
    public async Task PostgreSql18Run103Restore_AfterCorrection_ReconcilesBothOrbaRuns_WhenConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROPQC_ACTUAL_RUN3_RESTORED_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connectionString);
        var options = new DbContextOptionsBuilder<CropQcDbContext>();
        CropQcDatabase.Configure(options, DatabaseProviders.PostgreSql, connectionString);
        await using var db = new CropQcDbContext(options.Options);
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 26, 20, 30, 0, TimeSpan.Zero));
        var businessTime = new PacificBusinessTimeService(clock);
        var correction = new ActualRun3ReportingIdentityCorrectionService(
            db,
            new AppEnvironmentOptions { Kind = AppEnvironmentKinds.Development },
            businessTime,
            NullLogger<ActualRun3ReportingIdentityCorrectionService>.Instance);

        var preflight = await correction.PreflightAsync(CancellationToken.None);
        Assert.Equal("AlreadyApplied", preflight.State);
        Assert.Equal(1, preflight.AuditCount);
        Assert.Equal(2, await AuthoritativeRunReportingQuery.ApplyValidRules(db.BinsRunEntries.AsNoTracking())
            .CountAsync(x => x.ActualRunId == 2 || x.ActualRunId == 3));

        var reconciliationOptions = new RunSheetReconciliationOptions { Enabled = true };
        var store = new RunSheetSnapshotStore(reconciliationOptions, clock);
        store.RecordSuccess(
            [
                ReconciliationExternal(new DateOnly(2026, 7, 29), 155),
                ReconciliationExternal(new DateOnly(2026, 7, 30), 173)
            ],
            clock.UtcNow);
        var result = await new RunSheetReconciliationService(db, store, reconciliationOptions, businessTime)
            .GetAsync(EmploymentFacilities.Wp, 2026, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(2, result!.MatchedCount);
        Assert.Contains(result.Items, x => x.State == RunSheetReconciliationStates.Match
            && x.CropQcDate == new DateOnly(2026, 7, 29)
            && x.CropQcBins == 155 && x.ActualRunIds.SequenceEqual([2L]));
        Assert.Contains(result.Items, x => x.State == RunSheetReconciliationStates.Match
            && x.CropQcDate == new DateOnly(2026, 7, 30)
            && x.CropQcBins == 173 && x.ActualRunIds.SequenceEqual([3L]));
        Assert.DoesNotContain(result.Items, x => x.ActualRunIds.Contains(3)
            && x.Reasons.Contains(RunSheetReconciliationReasons.IncompleteCropQcReportingIdentity));

        var entry = await db.BinsRunEntries.AsNoTracking().SingleAsync(x => x.Id == 33);
        var adjustment = await db.RoomInventoryAdjustments.AsNoTracking().SingleAsync(x => x.Id == 117);
        Assert.Equal(173, entry.BinsRun);
        Assert.Equal(-173, adjustment.ChangeAmount);
        Assert.Equal(225, adjustment.OldBinCount);
        Assert.Equal(52, adjustment.NewBinCount);
    }

    [Fact]
    public async Task PostgreSql18Run103Restore_BeforeCorrection_SurfacesRun3Diagnostic_WhenConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROPQC_ACTUAL_RUN3_BEFORE_RESTORED_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connectionString);
        var options = new DbContextOptionsBuilder<CropQcDbContext>();
        CropQcDatabase.Configure(options, DatabaseProviders.PostgreSql, connectionString);
        await using var db = new CropQcDbContext(options.Options);
        var clock = new FixedClock(new DateTimeOffset(2026, 8, 26, 20, 30, 0, TimeSpan.Zero));
        var businessTime = new PacificBusinessTimeService(clock);
        var correction = new ActualRun3ReportingIdentityCorrectionService(
            db,
            new AppEnvironmentOptions { Kind = AppEnvironmentKinds.Development },
            businessTime,
            NullLogger<ActualRun3ReportingIdentityCorrectionService>.Instance);

        Assert.Equal("Ready", (await correction.PreflightAsync(CancellationToken.None)).State);
        Assert.Equal(1, await AuthoritativeRunReportingQuery.ApplyValidRules(db.BinsRunEntries.AsNoTracking())
            .CountAsync(x => x.ActualRunId == 2 || x.ActualRunId == 3));

        var reconciliationOptions = new RunSheetReconciliationOptions { Enabled = true };
        var store = new RunSheetSnapshotStore(reconciliationOptions, clock);
        store.RecordSuccess(
            [
                ReconciliationExternal(new DateOnly(2026, 7, 29), 155),
                ReconciliationExternal(new DateOnly(2026, 7, 30), 173)
            ],
            clock.UtcNow);
        var result = await new RunSheetReconciliationService(db, store, reconciliationOptions, businessTime)
            .GetAsync(EmploymentFacilities.Wp, 2026, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Contains(result!.Items, x => x.State == RunSheetReconciliationStates.Match
            && x.CropQcDate == new DateOnly(2026, 7, 29)
            && x.CropQcBins == 155 && x.ActualRunIds.SequenceEqual([2L]));
        Assert.Contains(result.Items, x => x.Reasons.Contains(RunSheetReconciliationReasons.MissingFromCropQc)
            && x.SheetDate == new DateOnly(2026, 7, 30) && x.SheetBins == 173);
        var diagnostic = Assert.Single(result.Items, x => x.ActualRunIds.SequenceEqual([3L])
            && x.Reasons.Contains(RunSheetReconciliationReasons.IncompleteCropQcReportingIdentity));
        Assert.Equal(new DateOnly(2026, 7, 30), diagnostic.CropQcDate);
        Assert.Equal(173, diagnostic.CropQcBins);
        Assert.Equal("ORBA", diagnostic.CropQcVariety);
        Assert.Contains("cannot be reconciled safely", diagnostic.DiagnosticMessage);
    }

    private static ExternalPhysicalRun ReconciliationExternal(DateOnly date, int bins) => new(
        EmploymentFacilities.Wp,
        date,
        "ORBA",
        "Organic",
        "Domex",
        null,
        bins,
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["1080"] = bins });

    private static string Read(params string[] path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CropQc.sln")))
            directory = directory.Parent;
        return File.ReadAllText(Path.Combine(directory?.FullName
            ?? throw new InvalidOperationException("Repository root not found."), Path.Combine(path)));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        private const string BackupSha = "bdb4d922b011356f1737222244ca3d6c5c78bf133d01c5e1ce4e4e0f73f54c0d";
        private readonly SqliteConnection connection;
        public CropQcDbContext Db { get; }
        public FixedClock Clock { get; } = new(new DateTimeOffset(2026, 8, 26, 20, 30, 0, TimeSpan.Zero));
        private PacificBusinessTimeService BusinessTime => new(Clock);

        private Fixture(SqliteConnection connection, CropQcDbContext db)
        {
            this.connection = connection;
            Db = db;
        }

        public static async Task<Fixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseSqlite(connection).Options);
            await db.Database.EnsureCreatedAsync();
            var fixture = new Fixture(connection, db);
            await fixture.SeedAsync();
            return fixture;
        }

        public ActualRun3ReportingIdentityCorrectionService CorrectionService() => new(
            Db,
            new AppEnvironmentOptions { Kind = AppEnvironmentKinds.Development },
            BusinessTime,
            NullLogger<ActualRun3ReportingIdentityCorrectionService>.Instance);

        public ActualRun3ReportingIdentityCorrectionRequest Request(ActualRun3ReportingIdentityCorrectionPreflight preflight) => new(
            true,
            false,
            true,
            103,
            BackupSha,
            "admin@example.test",
            "Restore reviewed reporting identity for Actual Run #3.",
            preflight.TargetFingerprint,
            preflight.ProtectedFingerprint,
            ActualRun3ReportingIdentityCorrectionConstants.ApplyAuthorizationToken);

        public RunSheetReconciliationService ReconciliationService()
        {
            var options = new RunSheetReconciliationOptions { Enabled = true };
            var store = new RunSheetSnapshotStore(options, Clock);
            store.RecordSuccess(
                [
                    External(new DateOnly(2026, 7, 29), 155),
                    External(new DateOnly(2026, 7, 30), 173)
                ],
                Clock.UtcNow);
            return new RunSheetReconciliationService(Db, store, options, BusinessTime);
        }

        private static ExternalPhysicalRun External(DateOnly date, int bins) => new(
            EmploymentFacilities.Wp,
            date,
            "ORBA",
            "Organic",
            "Domex",
            null,
            bins,
            new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["1080"] = bins });

        private async Task SeedAsync()
        {
            var run2At = new DateTimeOffset(2026, 7, 30, 0, 33, 0, TimeSpan.Zero);
            var run3At = new DateTimeOffset(2026, 7, 31, 0, 33, 0, TimeSpan.Zero);
            var warehouse = await Db.Warehouses.FindAsync(4);
            if (warehouse is null)
            {
                warehouse = new Warehouse { Id = 4, Code = "WP", Name = "WP", IsActive = true };
                Db.Warehouses.Add(warehouse);
            }
            else
            {
                warehouse.Code = "WP";
                warehouse.Name = "WP";
                warehouse.IsActive = true;
            }
            var room = await Db.Rooms.FindAsync(15);
            if (room is null)
            {
                room = new Room { Id = 15, Warehouse = warehouse, Code = "WP-15", Name = "WP-15", IsActive = true };
                Db.Rooms.Add(room);
            }
            else
            {
                room.Warehouse = warehouse;
                room.Code = "WP-15";
                room.Name = "WP-15";
                room.IsActive = true;
            }
            var fruit = await Db.FruitProfiles.FindAsync(19);
            if (fruit is null)
            {
                fruit = new FruitProfile
                {
                    Id = 19,
                    Name = "Organic Bartlett",
                    VarietyCode = "ORBA",
                    FruitType = "Pear",
                    ProductionType = "Organic",
                    IsOrganic = true,
                    IsActive = true
                };
                Db.FruitProfiles.Add(fruit);
            }
            else
            {
                fruit.Name = "Organic Bartlett";
                fruit.VarietyCode = "ORBA";
                fruit.FruitType = "Pear";
                fruit.ProductionType = "Organic";
                fruit.IsOrganic = true;
                fruit.IsActive = true;
            }
            var growerLot = await Db.GrowerLots.FindAsync(394);
            if (growerLot is null)
            {
                growerLot = new GrowerLot
                {
                    Id = 394,
                    Grower = "WP ORCHARD ORG CHIL",
                    LotNumber = "1080",
                    IsActive = true,
                    CreatedAt = run2At,
                    UpdatedAt = run2At
                };
                Db.GrowerLots.Add(growerLot);
            }
            else
            {
                growerLot.Grower = "WP ORCHARD ORG CHIL";
                growerLot.LotNumber = "1080";
                growerLot.IsActive = true;
            }
            var admin = new User
            {
                Id = 10000,
                Email = "admin@example.test",
                DisplayName = "Correction Admin",
                Domain = "example.test",
                EmploymentFacility = "WP",
                CreatedAt = run2At
            };
            var role = await Db.Roles.SingleOrDefaultAsync(x => x.Name == BuiltInRoleNames.Admin);
            if (role is null)
            {
                role = new Role
                {
                    Id = 10000,
                    Name = BuiltInRoleNames.Admin,
                    NormalizedName = BuiltInRoleNames.Normalize(BuiltInRoleNames.Admin),
                    IsSystemRole = true,
                    IsActive = true
                };
                Db.Roles.Add(role);
            }
            else
            {
                role.IsActive = true;
            }
            admin.UserRoles.Add(new UserRole { User = admin, Role = role });
            Db.Users.Add(admin);
            SeedRun(2, 2, 32, 116, run2At, 155, 310, 155, true);
            SeedRun(3, 3, 33, 117, run3At, 173, 225, 52, false);
            Db.BackupRunRecords.Add(new BackupRunRecord
            {
                Id = 103,
                BackupType = BackupRunTypes.PreDeployment,
                Status = BackupRunStatuses.Succeeded,
                EnvironmentName = "DisposableRestore",
                DatabaseProvider = "SQLite",
                RetentionCategory = "Protected",
                StartedAt = run3At,
                CompletedAt = run3At.AddMinutes(1),
                Sha256 = BackupSha,
                VerifiedAt = run3At.AddMinutes(2),
                RetentionProcessedAt = run3At.AddMinutes(3),
                LeaseReleasedAt = run3At.AddMinutes(4)
            });
            await Db.SaveChangesAsync();

            void SeedRun(
                long runId,
                long revisionId,
                long entryId,
                long adjustmentId,
                DateTimeOffset runAt,
                int bins,
                int oldBins,
                int newBins,
                bool completeReporting)
            {
                var run = new ActualRun
                {
                    Id = runId,
                    Status = ActualRunStatuses.Active,
                    CurrentRevisionNumber = 1,
                    RunAt = runAt,
                    CreatedAt = runAt,
                    CreatedByUser = admin,
                    RunFacilityWarehouse = warehouse,
                    RunFacilityCodeSnapshot = "WP",
                    RunFacilityAssignmentSource = RunFacilityAssignmentSources.Employment,
                    SalesDeskNameSnapshot = "Domex"
                };
                var revision = new ActualRunRevision
                {
                    Id = revisionId,
                    ActualRun = run,
                    RevisionNumber = 1,
                    OperationType = ActualRunRevisionTypes.Create,
                    OperationKey = $"actual-run-{runId}",
                    IsCurrent = true,
                    CreatedByUser = admin,
                    CreatedAt = runAt
                };
                var adjustment = new RoomInventoryAdjustment
                {
                    Id = adjustmentId,
                    CropYear = 2026,
                    Warehouse = warehouse,
                    Room = room,
                    GrowerLot = growerLot,
                    FruitProfile = fruit,
                    GrowerName = "WINDY POINT",
                    LotNumber = "1080",
                    VarietyCode = "ORBA",
                    OldBinCount = oldBins,
                    ChangeAmount = -bins,
                    NewBinCount = newBins,
                    AdjustmentType = BinsRunService.AdjustmentType,
                    Source = $"Actual Run #{runId}",
                    AdjustmentAt = runAt,
                    CreatedByUser = admin,
                    CreatedAt = runAt,
                    ActualRun = run,
                    ActualRunRevision = revision
                };
                var entry = new BinsRunEntry
                {
                    Id = entryId,
                    InventoryAdjustment = adjustment,
                    Warehouse = warehouse,
                    Room = room,
                    CropYear = 2026,
                    GrowerLot = growerLot,
                    FruitProfile = fruit,
                    GrowerName = "WINDY POINT",
                    LotNumber = "1080",
                    VarietyCode = "ORBA",
                    PreviousAvailableBins = oldBins,
                    BinsRun = bins,
                    NewAvailableBins = newBins,
                    RunAt = runAt,
                    CreatedByUser = admin,
                    CreatedAt = runAt,
                    ActualRun = run,
                    ActualRunRevision = revision,
                    TransactionType = ActualRunTransactionTypes.Depletion
                };
                if (completeReporting)
                {
                    entry.ReportingFacilityWarehouse = warehouse;
                    entry.ReportingFacilityCodeSnapshot = "WP";
                    entry.ReportingFacilityAssignmentSource = RunFacilityAssignmentSources.Employment;
                    entry.ReportingFacilityAssignedByUserId = admin.Id;
                    entry.ReportingFacilityAssignedAt = runAt;
                    entry.ProductionTypeSnapshot = "Organic";
                    entry.IsOrganicSnapshot = true;
                    entry.GrowerNumberSnapshot = "1080";
                    entry.ReportingCropYearSnapshot = 2026;
                    entry.ReportingFruitProfileIdSnapshot = 19;
                    entry.ReportingVarietyCodeSnapshot = "ORBA";
                }
                Db.AddRange(run, revision, adjustment, entry);
            }
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    public sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
