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
        Assert.Empty(detail.Weeks);
        var selectedPage = await service.GetAsync(new BinsRunFilterForm
        {
            Section = "RunTotals",
            ReportFacility = EmploymentFacilities.Wp,
            ReportCropYear = 2026,
            ReportVarietyKey = Assert.Single(detail.Varieties).VarietyKey
        }, principal, CancellationToken.None);
        var selectedDetail = Assert.IsType<RunTotalsDetailViewModel>(selectedPage.Detail);
        Assert.Equal(selectedDetail.TotalBins, selectedDetail.Weeks.Sum(x => x.Bins));
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
    public async Task WpSalesDeskTotals_FilterAndReconcileIncludingDynamicAndUnassignedDesks()
    {
        using var db = CreateDbContext();
        await SeedAsync(db);
        var activeRun = await db.ActualRuns.SingleAsync(x => x.Id == 1);
        var fourthDesk = new SalesDesk
        {
            Id = 4,
            Name = "Fourth Desk",
            IsActive = true,
            DisplayOrder = 40,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var fourthRun = new ActualRun
        {
            Id = 4,
            Status = ActualRunStatuses.Active,
            CurrentRevisionNumber = 1,
            ConcurrencyVersion = 1,
            RunAt = DateTimeOffset.Parse("2026-08-03T20:00:00Z"),
            CreatedAt = DateTimeOffset.UtcNow,
            CreatedByUser = await db.Users.SingleAsync(x => x.Id == 9000),
            RunFacilityWarehouse = await db.Warehouses.SingleAsync(x => x.Id == 9001),
            RunFacilityCodeSnapshot = EmploymentFacilities.Wp,
            RunFacilityAssignmentSource = RunFacilityAssignmentSources.Employment,
            SalesDesk = fourthDesk,
            SalesDeskNameSnapshot = fourthDesk.Name
        };
        var fourthRevision = new ActualRunRevision
        {
            Id = 4,
            ActualRun = fourthRun,
            RevisionNumber = 1,
            OperationType = ActualRunRevisionTypes.Create,
            OperationKey = "fourth-desk",
            IsCurrent = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        activeRun.SalesDeskId = 1;
        activeRun.SalesDeskNameSnapshot = "Domex";
        db.AddRange(fourthDesk, fourthRun, fourthRevision);
        db.BinsRunEntries.Add(Entry(
            30,
            40,
            2026,
            "1084",
            await db.Warehouses.SingleAsync(x => x.Id == 9000),
            await db.Warehouses.SingleAsync(x => x.Id == 9001),
            await db.Rooms.SingleAsync(x => x.Id == 9000),
            await db.FruitProfiles.SingleAsync(x => x.Id == 9000),
            await db.Users.SingleAsync(x => x.Id == 9000),
            fourthRun,
            fourthRevision));
        await db.SaveChangesAsync();

        var allPage = await CreateService(db).GetAsync(new BinsRunFilterForm
        {
            Section = "RunTotals",
            ReportFacility = EmploymentFacilities.Wp,
            ReportCropYear = 2026
        }, Principal(), CancellationToken.None);
        var all = Assert.IsType<RunTotalsDetailViewModel>(allPage.Detail);
        Assert.Equal(100, all.TotalBins);
        Assert.Equal(all.TotalBins, all.SalesDeskTotals.Sum(x => x.Bins));
        Assert.Contains(all.SalesDeskTotals, x => x.SalesDeskId == 1 && x.SalesDesk == "Domex" && x.Bins == 40);
        Assert.Contains(all.SalesDeskTotals, x => x.SalesDeskId == 4 && x.SalesDesk == "Fourth Desk" && x.Bins == 40);
        Assert.Contains(all.SalesDeskTotals, x => x.IsUnassigned && x.Bins == 20);
        Assert.Equal(new[] { "Domex", "Fourth Desk", "Unassigned" }, all.SalesDeskTotals.Select(x => x.SalesDesk));

        var fourthPage = await CreateService(db).GetAsync(new BinsRunFilterForm
        {
            Section = "RunTotals",
            ReportFacility = EmploymentFacilities.Wp,
            ReportCropYear = 2026,
            ReportSalesDesk = "4"
        }, Principal(), CancellationToken.None);
        var fourth = Assert.IsType<RunTotalsDetailViewModel>(fourthPage.Detail);
        Assert.Equal("4", fourth.SelectedSalesDesk);
        Assert.Equal(40, fourth.TotalBins);
        Assert.All(fourth.SupportingRecords, x => Assert.Equal("Fourth Desk", x.SalesDesk));

        var unassignedPage = await CreateService(db).GetAsync(new BinsRunFilterForm
        {
            Section = "RunTotals",
            ReportFacility = EmploymentFacilities.Wp,
            ReportCropYear = 2026,
            ReportSalesDesk = "Unassigned"
        }, Principal(), CancellationToken.None);
        var unassigned = Assert.IsType<RunTotalsDetailViewModel>(unassignedPage.Detail);
        Assert.Equal(20, unassigned.TotalBins);
        Assert.All(unassigned.SupportingRecords, x => Assert.Equal("Unassigned", x.SalesDesk));
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
    public async Task PostgreSql_RestoredWpRunsRemainUnassignedAndDisposableDeskTotalsReconcile_WhenConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROPQC_SALES_DESK_RESTORE_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connectionString);
        var optionsBuilder = new DbContextOptionsBuilder<CropQcDbContext>();
        CropQcDatabase.Configure(optionsBuilder, DatabaseProviders.PostgreSql, connectionString);
        await using var db = new CropQcDbContext(optionsBuilder.Options);
        await using var transaction = await db.Database.BeginTransactionAsync();

        var historicalWpRuns = await db.ActualRuns
            .Where(x => x.RunFacilityCodeSnapshot == EmploymentFacilities.Wp)
            .ToListAsync();
        Assert.NotEmpty(historicalWpRuns);
        Assert.All(historicalWpRuns, x =>
        {
            Assert.Null(x.SalesDeskId);
            Assert.Null(x.SalesDeskNameSnapshot);
        });
        Assert.Equal(0, await db.ActualRuns.CountAsync(x =>
            x.RunFacilityCodeSnapshot == EmploymentFacilities.Ebs
            && (x.SalesDeskId != null || x.SalesDeskNameSnapshot != null)));

        var currentPage = await CreateService(db, DateTimeOffset.Parse("2026-08-22T19:00:00Z"))
            .GetAsync(new BinsRunFilterForm
            {
                Section = "RunTotals",
                ReportFacility = EmploymentFacilities.Wp,
                ReportCropYear = 2026
            }, Principal(), CancellationToken.None);
        var current = Assert.IsType<RunTotalsDetailViewModel>(currentPage.Detail);
        Assert.Equal(current.TotalBins, current.SalesDeskTotals.Sum(x => x.Bins));
        Assert.Equal(0, current.SalesDeskTotals.Where(x => !x.IsUnassigned).Sum(x => x.Bins));
        Assert.Equal(current.TotalBins, Assert.Single(current.SalesDeskTotals, x => x.IsUnassigned).Bins);
        Console.WriteLine($"Restored WP total={current.TotalBins}; assigned=0; unassigned={current.TotalBins}.");

        var template = await AuthoritativeRunReportingQuery.ApplyValidRules(db.BinsRunEntries.AsNoTracking())
            .Where(x => (x.ActualRunId != null
                ? x.ActualRun!.RunFacilityCodeSnapshot
                : x.ReportingFacilityCodeSnapshot) == EmploymentFacilities.Wp)
            .OrderBy(x => x.Id)
            .FirstAsync();
        var userId = template.CreatedByUserId ?? await db.Users.Select(x => x.Id).FirstAsync();
        var desks = await db.SalesDesks.OrderBy(x => x.DisplayOrder).Take(3).ToListAsync();
        Assert.Equal(new[] { "Domex", "Honey Bear", "Viva Tierra" }, desks.Select(x => x.Name));

        foreach (var (desk, bins) in desks.Zip(new[] { 100, 80, 60 }))
        {
            AddDisposableRun(db, template, userId, desk, bins, $"sales-desk-{desk.Id}");
        }
        await db.SaveChangesAsync();

        var threePage = await CreateService(db, DateTimeOffset.Parse("2027-08-04T19:00:00Z"))
            .GetAsync(new BinsRunFilterForm
            {
                Section = "RunTotals",
                ReportFacility = EmploymentFacilities.Wp,
                ReportCropYear = 2027
            }, Principal(), CancellationToken.None);
        var three = Assert.IsType<RunTotalsDetailViewModel>(threePage.Detail);
        Assert.Equal(240, three.TotalBins);
        Assert.Equal(100, three.SalesDeskTotals.Single(x => x.SalesDesk == "Domex").Bins);
        Assert.Equal(80, three.SalesDeskTotals.Single(x => x.SalesDesk == "Honey Bear").Bins);
        Assert.Equal(60, three.SalesDeskTotals.Single(x => x.SalesDesk == "Viva Tierra").Bins);
        Assert.Equal(three.TotalBins, three.SalesDeskTotals.Sum(x => x.Bins));

        var fourth = new SalesDesk
        {
            Name = "Fourth Desk",
            IsActive = true,
            DisplayOrder = 40,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.SalesDesks.Add(fourth);
        await db.SaveChangesAsync();
        AddDisposableRun(db, template, userId, fourth, 40, "sales-desk-fourth");
        await db.SaveChangesAsync();

        var fourPage = await CreateService(db, DateTimeOffset.Parse("2027-08-04T19:00:00Z"))
            .GetAsync(new BinsRunFilterForm
            {
                Section = "RunTotals",
                ReportFacility = EmploymentFacilities.Wp,
                ReportCropYear = 2027
            }, Principal(), CancellationToken.None);
        var four = Assert.IsType<RunTotalsDetailViewModel>(fourPage.Detail);
        Assert.Equal(280, four.TotalBins);
        Assert.Equal(40, four.SalesDeskTotals.Single(x => x.SalesDesk == "Fourth Desk").Bins);
        Assert.Equal(four.TotalBins, four.SalesDeskTotals.Sum(x => x.Bins));
        Console.WriteLine("Disposable totals: WP=280; Domex=100; Honey Bear=80; Viva Tierra=60; Fourth Desk=40.");

        await transaction.RollbackAsync();
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

    private static void AddDisposableRun(
        CropQcDbContext db,
        BinsRunEntry template,
        int userId,
        SalesDesk desk,
        int bins,
        string operationKey)
    {
        var runAt = DateTimeOffset.Parse("2027-08-03T19:00:00Z");
        var run = new ActualRun
        {
            Status = ActualRunStatuses.Active,
            CurrentRevisionNumber = 1,
            ConcurrencyVersion = 1,
            RunAt = runAt,
            CreatedAt = runAt,
            CreatedByUserId = userId,
            RunFacilityWarehouseId = template.ReportingFacilityWarehouseId,
            RunFacilityCodeSnapshot = EmploymentFacilities.Wp,
            RunFacilityAssignmentSource = RunFacilityAssignmentSources.Employment,
            SalesDesk = desk,
            SalesDeskNameSnapshot = desk.Name
        };
        var revision = new ActualRunRevision
        {
            ActualRun = run,
            RevisionNumber = 1,
            OperationType = ActualRunRevisionTypes.Create,
            OperationKey = operationKey,
            IsCurrent = true,
            CreatedByUserId = userId,
            CreatedAt = runAt
        };
        var adjustment = new RoomInventoryAdjustment
        {
            WarehouseId = template.WarehouseId,
            RoomId = template.RoomId,
            CropYear = 2027,
            FruitProfileId = template.FruitProfileId,
            GrowerLotId = template.GrowerLotId,
            GrowerName = template.GrowerName,
            LotNumber = template.LotNumber,
            VarietyCode = template.VarietyCode,
            ChangeAmount = -bins,
            OldBinCount = bins,
            NewBinCount = 0,
            AdjustmentType = BinsRunService.AdjustmentType,
            AdjustmentAt = runAt,
            Reason = "Disposable Sales Desk reporting rehearsal",
            Source = "PostgreSql restored-copy test",
            InventoryInvariantVersion = 1,
            InventoryOperationKey = operationKey,
            ActualRun = run,
            ActualRunRevision = revision,
            CreatedByUserId = userId,
            CreatedAt = runAt
        };
        db.BinsRunEntries.Add(new BinsRunEntry
        {
            InventoryAdjustment = adjustment,
            ActualRun = run,
            ActualRunRevision = revision,
            WarehouseId = template.WarehouseId,
            RoomId = template.RoomId,
            CropYear = 2027,
            FruitProfileId = template.FruitProfileId,
            GrowerLotId = template.GrowerLotId,
            GrowerName = template.GrowerName,
            LotNumber = template.LotNumber,
            VarietyCode = template.VarietyCode,
            PreviousAvailableBins = bins,
            BinsRun = bins,
            NewAvailableBins = 0,
            RunAt = runAt,
            CreatedByUserId = userId,
            CreatedAt = runAt,
            TransactionType = ActualRunTransactionTypes.Depletion,
            ReportingFacilityWarehouseId = template.ReportingFacilityWarehouseId,
            ReportingFacilityCodeSnapshot = EmploymentFacilities.Wp,
            ReportingFacilityAssignmentSource = RunFacilityAssignmentSources.Employment,
            ReportingCropYearSnapshot = 2027,
            ReportingFruitProfileIdSnapshot = template.ReportingFruitProfileIdSnapshot,
            ReportingVarietyCodeSnapshot = template.ReportingVarietyCodeSnapshot,
            ProductionTypeSnapshot = template.ProductionTypeSnapshot,
            IsOrganicSnapshot = template.IsOrganicSnapshot,
            GrowerNumberSnapshot = template.GrowerNumberSnapshot,
            TreatmentStateSnapshot = template.TreatmentStateSnapshot,
            TreatmentSignatureSnapshot = template.TreatmentSignatureSnapshot,
            TreatmentSummarySnapshot = template.TreatmentSummarySnapshot
        });
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
        public void InvalidateAll() { }
    }
}
