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
        Assert.DoesNotContain(detail.Varieties, x => x.Bins is 70 or 90 or 99);
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
        Assert.Contains(page.Issues, x => x.EntryId == 5 && x.IssueType == "Crop year outside reporting period" && x.ExcludedBins == 90);
        Assert.Equal(beforeEntries, await db.BinsRunEntries.CountAsync());
        Assert.Equal(beforeAdjustments, await db.RoomInventoryAdjustments.CountAsync());
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

    private static RunReportingService CreateService(CropQcDbContext db)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RunReporting:CropYearStartMonth"] = "7",
            ["RunReporting:CropYearStartDay"] = "15"
        }).Build();
        return new RunReportingService(
            db,
            new PacificBusinessTimeService(new FixedClock(new DateTimeOffset(2026, 8, 3, 19, 0, 0, TimeSpan.Zero))),
            new AllowAccess(),
            configuration);
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
        db.BinsRunEntries.AddRange(
            Entry(1, 40, 2026, "1084", sourceWarehouse, reportingWarehouse, room, fruit, user, active, currentRevision),
            Entry(2, 20, 2026, "1084", sourceWarehouse, reportingWarehouse, room, fruit, user),
            Entry(3, 99, 2026, "1084", sourceWarehouse, reportingWarehouse, room, fruit, user, canceled, canceledRevision),
            Entry(4, 70, 2026, null, sourceWarehouse, reportingWarehouse, room, fruit, user),
            Entry(5, 90, 2024, "1084", sourceWarehouse, reportingWarehouse, room, fruit, user),
            Entry(6, 111, 2026, "1084", sourceWarehouse, reportingWarehouse, room, fruit, user,
                runAt: DateTimeOffset.Parse("2026-08-04T19:00:00Z")));
        await db.SaveChangesAsync();
    }

    private static BinsRunEntry Entry(
        long id,
        int bins,
        int cropYear,
        string? growerNumber,
        Warehouse sourceWarehouse,
        Warehouse reportingWarehouse,
        Room room,
        FruitProfile fruit,
        User user,
        ActualRun? run = null,
        ActualRunRevision? revision = null,
        DateTimeOffset? runAt = null)
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
            CreatedAt = DateTimeOffset.UtcNow
        };
        return new BinsRunEntry
        {
            Id = id,
            InventoryAdjustment = adjustment,
            Warehouse = sourceWarehouse,
            Room = room,
            FruitProfile = fruit,
            GrowerName = "Grower",
            LotNumber = adjustment.LotNumber,
            VarietyCode = fruit.VarietyCode,
            PreviousAvailableBins = bins,
            BinsRun = bins,
            NewAvailableBins = 0,
            RunAt = effectiveRunAt,
            CreatedByUser = user,
            CreatedAt = DateTimeOffset.UtcNow,
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
