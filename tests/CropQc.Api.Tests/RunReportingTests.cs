using System.Security.Claims;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Auth;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CropQc.Api.Tests;

public sealed class RunReportingTests
{
    [Theory]
    [InlineData(2026, 7, 14, 2025)]
    [InlineData(2026, 7, 15, 2026)]
    [InlineData(2027, 1, 14, 2026)]
    public void CropYearAndWeekBoundaries_ArePacificAndSundayBased(int year, int month, int day, int expectedCropYear)
    {
        var date = new DateOnly(year, month, day);

        Assert.Equal(expectedCropYear, RunReportingService.CurrentCropYear(date));
        Assert.Equal(DayOfWeek.Sunday, RunReportingService.WeekStart(date).DayOfWeek);
        Assert.Equal(date.AddYears(-1), RunReportingService.EquivalentPriorCutoff(date));
    }

    [Fact]
    public async Task SummaryAndDetail_CountEachValidQuantityOnce_AndReconcileVarieties()
    {
        using var db = CreateDbContext();
        await SeedAsync(db);
        var service = CreateService(db);
        var principal = Principal();

        var summary = await service.GetAsync(new BinsRunFilterForm(), principal, CancellationToken.None);
        var detailPage = await service.GetAsync(new BinsRunFilterForm
        {
            Section = "RunTotals",
            ReportFacility = EmploymentFacilities.Wp,
            ReportCropYear = 2026
        }, principal, CancellationToken.None);

        Assert.Equal(60, summary.FacilitySummaries.Single(x => x.Facility == EmploymentFacilities.Wp)
            .CropYears.Single(x => x.CropYear == 2026).Bins);
        var detail = Assert.IsType<RunTotalsDetailViewModel>(detailPage.Detail);
        Assert.Equal(60, detail.TotalBins);
        Assert.Equal(detail.TotalBins, detail.Varieties.Sum(x => x.Bins));
        Assert.Equal(detail.TotalBins, detail.Weeks.Sum(x => x.Bins));
        Assert.Equal(0, detail.PriorBins);
        Assert.False(detail.HasAuthoritativePriorBaseline);
        Assert.Null(detail.PriorCropYear);
        Assert.DoesNotContain(detail.Varieties, x => x.Bins is 70 or 90 or 99);
        Assert.All(summary.FacilitySummaries.SelectMany(x => x.CropYears), x => Assert.True(x.CropYear >= 2026));
    }

    [Fact]
    public async Task RunTotalsVarietyCards_UseSharedConfiguredColorAndReadableContrast()
    {
        using var db = CreateDbContext();
        await SeedAsync(db);
        db.VarietyColorConfigurations.Add(new VarietyColorConfiguration
        {
            VarietyKey = "BART",
            VarietyName = "BART",
            HexColor = "#F5E66A",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var page = await CreateService(db).GetAsync(new BinsRunFilterForm
        {
            Section = "RunTotals",
            ReportFacility = EmploymentFacilities.Wp,
            ReportCropYear = 2026
        }, Principal(), CancellationToken.None);

        var variety = Assert.Single(Assert.IsType<RunTotalsDetailViewModel>(page.Detail).Varieties);
        Assert.Equal("#F5E66A", variety.ColorHex);
        Assert.Equal("#17212B", variety.TextColorHex);
        Assert.True(variety.IsColorConfigured);
    }

    [Fact]
    public async Task NeedsReview_IsReadOnly_AndFlagsExcludedIdentityAndPeriodProblems()
    {
        using var db = CreateDbContext();
        await SeedAsync(db);
        var beforeEntries = await db.BinsRunEntries.CountAsync();
        var beforeAdjustments = await db.RoomInventoryAdjustments.CountAsync();

        var page = await CreateService(db).GetAsync(new BinsRunFilterForm { Section = "NeedsReview" }, Principal(), CancellationToken.None);

        Assert.Contains(page.Issues, x => x.EntryId == 4 && x.IssueType == "Missing grower number" && x.ExcludedBins == 70);
        Assert.Contains(page.Issues, x => x.EntryId == 8 && x.IssueType == "Missing crop year");
        Assert.Contains(page.Issues, x => x.EntryId == 11 && x.IssueType == "Missing reporting crop-year snapshot");
        Assert.DoesNotContain(page.Issues, x => x.EntryId is 5 or 7 or 9 or 10);
        Assert.Equal(beforeEntries, await db.BinsRunEntries.CountAsync());
        Assert.Equal(beforeAdjustments, await db.RoomInventoryAdjustments.CountAsync());
    }

    [Fact]
    public async Task AuthoritativeStartCropYear_DefaultsTo2026_WhenConfigurationIsAbsent()
    {
        using var db = CreateDbContext();
        var service = new RunReportingService(
            db,
            new PacificBusinessTimeService(new FixedClock(DateTimeOffset.Parse("2026-08-03T19:00:00Z"))),
            new AllowAccess(),
            new ConfigurationBuilder().Build());

        var page = await service.GetAsync(new BinsRunFilterForm(), Principal(), CancellationToken.None);

        Assert.Equal(2026, page.AuthoritativeStartCropYear);
        Assert.All(page.FacilitySummaries, facility =>
            Assert.Equal(new[] { 2026 }, facility.CropYears.Select(x => x.CropYear)));
    }

    [Fact]
    public async Task SummaryYears_StartAtAuthoritativeFloor_AndNeverShowLegacyYears()
    {
        var cases = new[]
        {
            ("2026-08-03T19:00:00Z", new[] { 2026 }),
            ("2027-08-03T19:00:00Z", new[] { 2027, 2026 }),
            ("2028-08-03T19:00:00Z", new[] { 2028, 2027, 2026 }),
            ("2029-08-03T19:00:00Z", new[] { 2029, 2028, 2027 })
        };
        foreach (var (utcNow, expectedYears) in cases)
        {
            using var db = CreateDbContext();
            var page = await CreateService(db, DateTimeOffset.Parse(utcNow))
                .GetAsync(new BinsRunFilterForm(), Principal(), CancellationToken.None);

            Assert.All(page.FacilitySummaries, facility =>
                Assert.Equal(expectedYears, facility.CropYears.Select(x => x.CropYear)));
            Assert.DoesNotContain(page.OlderCropYears, year => year < 2026);
        }
    }

    [Fact]
    public async Task Crop2027_ComparesTo2026_AndIncludesPriorYearOnlyVariety()
    {
        using var db = CreateDbContext();
        await SeedAsync(db);
        var wp = await db.Warehouses.SingleAsync(x => x.Id == 9001);
        var source = await db.Warehouses.SingleAsync(x => x.Id == 9000);
        var room = await db.Rooms.SingleAsync(x => x.Id == 9000);
        var user = await db.Users.SingleAsync(x => x.Id == 9000);
        var priorOnly = new FruitProfile
        {
            Id = 9002,
            Name = "Prior only",
            VarietyCode = "PRIOR",
            FruitType = "Pear",
            ProductionType = "Organic",
            IsActive = true
        };
        var currentOnly = new FruitProfile
        {
            Id = 9003,
            Name = "Current only",
            VarietyCode = "CURRENT",
            FruitType = "Pear",
            ProductionType = "Conventional",
            IsActive = true
        };
        db.AddRange(priorOnly, currentOnly);
        db.BinsRunEntries.AddRange(
            Entry(20, 12, 2026, "1084", source, wp, room, priorOnly, user),
            Entry(21, 15, 2027, "1084", source, wp, room, currentOnly, user,
                runAt: DateTimeOffset.Parse("2027-08-03T19:00:00Z")));
        await db.SaveChangesAsync();

        var page = await CreateService(db, DateTimeOffset.Parse("2027-08-03T20:00:00Z"))
            .GetAsync(new BinsRunFilterForm
            {
                Section = "RunTotals",
                ReportFacility = EmploymentFacilities.Wp,
                ReportCropYear = 2027
            }, Principal(), CancellationToken.None);

        var detail = Assert.IsType<RunTotalsDetailViewModel>(page.Detail);
        Assert.Equal(2026, detail.PriorCropYear);
        Assert.True(detail.HasAuthoritativePriorBaseline);
        Assert.Contains(detail.Varieties, x => x.Variety == "PRIOR" && x.Bins == 0 && x.PriorBins == 12);
        Assert.Contains(detail.Varieties, x => x.Variety == "CURRENT" && x.Bins == 15 && x.PriorBins == 0);
    }

    [Fact]
    public void EmploymentValidation_UsesAssignmentEffectiveAtRunTime()
    {
        var history = new List<RunReportingService.EmploymentTransition>
        {
            new(1, EmploymentFacilities.Wp, EmploymentFacilities.Ebs, DateTimeOffset.Parse("2026-09-01T07:00:00Z"))
        };

        Assert.Equal(EmploymentFacilities.Wp, RunReportingService.ResolveEmploymentAt(
            EmploymentFacilities.Ebs,
            DateTimeOffset.Parse("2026-09-01T07:00:00Z"),
            history,
            DateTimeOffset.Parse("2026-08-01T07:00:00Z")));
        Assert.Equal(EmploymentFacilities.Ebs, RunReportingService.ResolveEmploymentAt(
            EmploymentFacilities.Ebs,
            DateTimeOffset.Parse("2026-09-01T07:00:00Z"),
            history,
            DateTimeOffset.Parse("2026-10-01T07:00:00Z")));
    }

    [Fact]
    public async Task NeedsReview_PagesAllAuthoritativeRecordsBeyondFormerTwoThousandRowLimit()
    {
        using var db = CreateDbContext();
        await SeedAsync(db);
        var source = await db.Warehouses.SingleAsync(x => x.Id == 9000);
        var reporting = await db.Warehouses.SingleAsync(x => x.Id == 9001);
        var room = await db.Rooms.SingleAsync(x => x.Id == 9000);
        var fruit = await db.FruitProfiles.SingleAsync(x => x.Id == 9000);
        var user = await db.Users.SingleAsync(x => x.Id == 9000);
        for (var index = 0; index < 2005; index++)
        {
            db.BinsRunEntries.Add(Entry(
                10000 + index,
                1,
                2026,
                null,
                source,
                reporting,
                room,
                fruit,
                user,
                runAt: DateTimeOffset.Parse("2026-08-03T19:00:00Z")));
        }
        await db.SaveChangesAsync();

        var page = await CreateService(db).GetAsync(
            new BinsRunFilterForm { Section = "NeedsReview", ReportPage = 21 },
            Principal(),
            CancellationToken.None);

        Assert.Contains(page.Issues, x => x.EntryId == 10000 && x.IssueType == "Missing grower number");
    }

    [Fact]
    public async Task EmploymentChange_NormalizesPersistsHistoryAndAuditsBeforeAfter()
    {
        using var db = CreateDbContext();
        var admin = new User { Id = 9100, Email = "admin@fruitandland.com", DisplayName = "Admin", Domain = "fruitandland.com", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var employee = new User { Id = 9101, Email = "employee@fruitandland.com", DisplayName = "Employee", Domain = "fruitandland.com", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        db.Users.AddRange(admin, employee);
        await db.SaveChangesAsync();
        var service = new UserAdminService(db, new GoogleAuthenticationOptions(), new AllowAccess());
        var effectiveAt = DateTimeOffset.Parse("2026-08-01T07:00:00Z");
        var form = new UpdateUserEmploymentForm { UserId = employee.Id, EmploymentFacility = "Shared / Management", EffectiveAt = effectiveAt };

        Assert.Null(await service.UpdateUserEmploymentAsync(form, admin.Email, CancellationToken.None));
        Assert.Null(await service.UpdateUserEmploymentAsync(form, admin.Email, CancellationToken.None));

        Assert.Equal(EmploymentFacilities.Shared, employee.EmploymentFacility);
        Assert.Equal(admin.Id, employee.EmploymentUpdatedByUserId);
        var history = Assert.Single(await db.UserEmploymentHistory.ToListAsync());
        Assert.Equal(EmploymentFacilities.Unassigned, history.PreviousEmploymentFacility);
        Assert.Equal(EmploymentFacilities.Shared, history.EmploymentFacility);
        Assert.Equal(effectiveAt, history.EffectiveAt);
        var audit = Assert.Single(await db.AuditLogs.Where(x => x.Action == "update-employment").ToListAsync());
        Assert.Contains("Unassigned", audit.BeforeValuesJson);
        Assert.Contains("Shared", audit.AfterValuesJson);
    }

    private static CropQcDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new CropQcDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static RunReportingService CreateService(CropQcDbContext db, DateTimeOffset? utcNow = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RunReporting:CropYearStartMonth"] = "7",
            ["RunReporting:CropYearStartDay"] = "15",
            ["RunReporting:AuthoritativeStartCropYear"] = "2026"
        }).Build();
        return new RunReportingService(
            db,
            new PacificBusinessTimeService(new FixedClock(utcNow ?? new DateTimeOffset(2026, 8, 3, 19, 0, 0, TimeSpan.Zero))),
            new AllowAccess(),
            configuration,
            new VarietyColorService(db));
    }

    private static async Task SeedAsync(CropQcDbContext db)
    {
        var sourceWarehouse = new Warehouse { Id = 9000, Code = EmploymentFacilities.Ebs, Name = "Source EBS", IsActive = true };
        var reportingWarehouse = new Warehouse { Id = 9001, Code = EmploymentFacilities.Wp, Name = "Reporting WP", IsActive = true };
        var room = new Room { Id = 9000, Warehouse = sourceWarehouse, Code = "ROOM", Name = "Room", IsActive = true };
        var fruit = new FruitProfile
        {
            Id = 9000,
            Name = "Bartlett",
            VarietyCode = "BART",
            FruitType = "Pear",
            ProductionType = "Conventional",
            IsActive = true
        };
        var user = new User
        {
            Id = 9000,
            Email = "reporter@wp-packing.com",
            DisplayName = "WP Reporter",
            Domain = "wp-packing.com",
            EmploymentFacility = EmploymentFacilities.Wp,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.AddRange(sourceWarehouse, reportingWarehouse, room, fruit, user);

        var active = new ActualRun
        {
            Id = 1,
            Status = ActualRunStatuses.Active,
            CurrentRevisionNumber = 1,
            RunAt = DateTimeOffset.Parse("2026-08-03T19:00:00Z"),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUser = user,
            RunFacilityWarehouse = reportingWarehouse,
            RunFacilityCodeSnapshot = EmploymentFacilities.Wp,
            RunFacilityAssignmentSource = RunFacilityAssignmentSources.Employment
        };
        var currentRevision = new ActualRunRevision
        {
            Id = 1,
            ActualRun = active,
            RevisionNumber = 1,
            OperationType = ActualRunRevisionTypes.Create,
            OperationKey = "active",
            IsCurrent = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var canceled = new ActualRun
        {
            Id = 2,
            Status = ActualRunStatuses.Canceled,
            CurrentRevisionNumber = 1,
            RunAt = DateTimeOffset.Parse("2026-08-03T19:00:00Z"),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUser = user,
            RunFacilityWarehouse = reportingWarehouse,
            RunFacilityCodeSnapshot = EmploymentFacilities.Wp,
            RunFacilityAssignmentSource = RunFacilityAssignmentSources.Employment
        };
        var canceledRevision = new ActualRunRevision
        {
            Id = 2,
            ActualRun = canceled,
            RevisionNumber = 1,
            OperationType = ActualRunRevisionTypes.Create,
            OperationKey = "canceled",
            IsCurrent = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.AddRange(active, currentRevision, canceled, canceledRevision);
        var preAuthoritativeUnresolved = Entry(7, 306, 2025, null, sourceWarehouse, reportingWarehouse, room, fruit, user);
        preAuthoritativeUnresolved.ReportingFacilityWarehouse = null;
        preAuthoritativeUnresolved.ReportingFacilityWarehouseId = null;
        preAuthoritativeUnresolved.ReportingFacilityCodeSnapshot = null;
        var authoritativeMissingCrop = Entry(8, 5, null, "1084", sourceWarehouse, reportingWarehouse, room, fruit, user,
            createdAt: DateTimeOffset.Parse("2026-08-03T19:00:00Z"));
        var testingEraMissingCrop = Entry(9, 5, null, null, sourceWarehouse, reportingWarehouse, room, fruit, user,
            runAt: DateTimeOffset.Parse("2025-06-01T19:00:00Z"),
            createdAt: DateTimeOffset.Parse("2025-06-01T19:00:00Z"));
        var latePreAuthoritativeCrop = Entry(10, 5, 2025, null, sourceWarehouse, reportingWarehouse, room, fruit, user,
            createdAt: DateTimeOffset.Parse("2026-08-03T19:00:00Z"));
        latePreAuthoritativeCrop.ReportingCropYearSnapshot = null;
        var authoritativeMissingSnapshot = Entry(11, 5, 2026, "1084", sourceWarehouse, reportingWarehouse, room, fruit, user,
            createdAt: DateTimeOffset.Parse("2026-06-01T19:00:00Z"));
        authoritativeMissingSnapshot.ReportingCropYearSnapshot = null;
        db.BinsRunEntries.AddRange(
            Entry(1, 40, 2026, "1084", sourceWarehouse, reportingWarehouse, room, fruit, user, active, currentRevision),
            Entry(2, 20, 2026, "1084", sourceWarehouse, reportingWarehouse, room, fruit, user),
            Entry(3, 99, 2026, "1084", sourceWarehouse, reportingWarehouse, room, fruit, user, canceled, canceledRevision),
            Entry(4, 70, 2026, null, sourceWarehouse, reportingWarehouse, room, fruit, user),
            Entry(5, 90, 2024, "1084", sourceWarehouse, reportingWarehouse, room, fruit, user),
            Entry(6, 111, 2026, "1084", sourceWarehouse, reportingWarehouse, room, fruit, user,
                runAt: DateTimeOffset.Parse("2026-08-04T19:00:00Z")),
            preAuthoritativeUnresolved,
            authoritativeMissingCrop,
            testingEraMissingCrop,
            latePreAuthoritativeCrop,
            authoritativeMissingSnapshot);
        await db.SaveChangesAsync();
    }

    private static BinsRunEntry Entry(
        long id,
        int bins,
        int? cropYear,
        string? growerNumber,
        Warehouse sourceWarehouse,
        Warehouse reportingWarehouse,
        Room room,
        FruitProfile fruit,
        User user,
        ActualRun? run = null,
        ActualRunRevision? revision = null,
        DateTimeOffset? runAt = null,
        DateTimeOffset? createdAt = null)
    {
        var effectiveRunAt = runAt ?? DateTimeOffset.Parse("2026-08-03T19:00:00Z");
        var adjustment = new RoomInventoryAdjustment
        {
            Id = 100 + id,
            CropYear = cropYear,
            Warehouse = sourceWarehouse,
            Room = room,
            FruitProfile = fruit,
            GrowerName = "Grower",
            LotNumber = growerNumber ?? "UNKNOWN",
            VarietyCode = fruit.VarietyCode,
            ChangeAmount = -bins,
            NewBinCount = 0,
            AdjustmentType = BinsRunService.AdjustmentType,
            AdjustmentAt = effectiveRunAt,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow
        };
        return new BinsRunEntry
        {
            Id = id,
            InventoryAdjustment = adjustment,
            Warehouse = sourceWarehouse,
            Room = room,
            CropYear = cropYear,
            FruitProfile = fruit,
            GrowerName = "Grower",
            LotNumber = adjustment.LotNumber,
            VarietyCode = fruit.VarietyCode,
            PreviousAvailableBins = bins,
            BinsRun = bins,
            NewAvailableBins = 0,
            RunAt = effectiveRunAt,
            CreatedByUser = user,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            ActualRun = run,
            ActualRunRevision = revision,
            TransactionType = run is null ? ActualRunTransactionTypes.Legacy : ActualRunTransactionTypes.Depletion,
            ReportingFacilityWarehouse = reportingWarehouse,
            ReportingFacilityCodeSnapshot = EmploymentFacilities.Wp,
            ReportingFacilityAssignmentSource = RunFacilityAssignmentSources.Employment,
            ReportingCropYearSnapshot = cropYear,
            ReportingFruitProfileIdSnapshot = fruit.Id,
            ReportingVarietyCodeSnapshot = fruit.VarietyCode,
            ProductionTypeSnapshot = fruit.ProductionType,
            IsOrganicSnapshot = fruit.IsOrganic,
            GrowerNumberSnapshot = growerNumber
        };
    }

    private static ClaimsPrincipal Principal() => new(
        new ClaimsIdentity([new Claim(ClaimTypes.Email, "owner@fruitandland.com")], "Test"));

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
    }

    private sealed class AllowAccess : IUserAccessService
    {
        public Task<bool> HasAccessAsync(ClaimsPrincipal principal, string areaKey, PageAccessLevel minimumLevel, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<PageAccessLevel> GetAccessLevelAsync(string? email, string areaKey, CancellationToken cancellationToken) => Task.FromResult(PageAccessLevel.Admin);
        public Task<IReadOnlyList<UserAccessMatrixRow>> GetMatrixAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task EnsureAccessMatrixAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> SaveMatrixAsync(UserAccessMatrixForm form, string changedByEmail, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
