using System.Data.Common;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;

namespace CropQc.Api.Tests;

public sealed class GrowerLotProgressTests
{
    [Fact]
    public async Task Overview_ReconcilesGrowerVarietyLotAndWeeklyTotals_WithoutWrites()
    {
        await using var db = CreateDbContext();
        var seed = await SeedAsync(db);
        var beforeReceipts = await db.Receipts.CountAsync();
        var beforeAdjustments = await db.RoomInventoryAdjustments.CountAsync();
        var service = CreateService(db);

        var overview = await service.GetAsync(new GrowerLotProgressFilterForm
        {
            CropYear = 2026,
            Facility = "All",
            ExpandedGrowerNumber = "1084"
        }, CancellationToken.None);

        var grower = Assert.Single(overview.Growers);
        Assert.Equal("1084", grower.GrowerNumber);
        Assert.Equal(100, grower.BinsReceived);
        Assert.Equal(60, grower.BinsRun);
        Assert.Equal(grower.BinsReceived, grower.Varieties.Sum(x => x.BinsReceived));
        Assert.Equal(grower.BinsRun, grower.Varieties.Sum(x => x.BinsRun));
        var variety = Assert.Single(grower.Varieties);
        Assert.Equal("#123456", variety.ColorHex);
        Assert.Equal("Conventional", variety.IsOrganic ? "Organic" : "Conventional");

        var lotPage = await service.GetAsync(new GrowerLotProgressFilterForm
        {
            CropYear = 2026,
            ExpandedGrowerNumber = "1084",
            ExpandedVarietyKey = variety.VarietyKey
        }, CancellationToken.None);
        var lot = Assert.Single(Assert.Single(Assert.Single(lotPage.Growers).Varieties).Lots);
        Assert.Equal(100, lot.BinsReceived);
        Assert.Equal(60, lot.BinsRun);
        Assert.Equal(seed.GrowerLot.Id, lot.GrowerLotId);

        var weeklyPage = await service.GetAsync(new GrowerLotProgressFilterForm
        {
            CropYear = 2026,
            ExpandedGrowerNumber = "1084",
            ExpandedVarietyKey = variety.VarietyKey,
            SelectedLotKey = lot.LotKey
        }, CancellationToken.None);
        var selectedLot = Assert.Single(Assert.Single(Assert.Single(weeklyPage.Growers).Varieties).Lots);
        Assert.Equal(selectedLot.BinsRun, selectedLot.Weeks.Sum(x => x.BinsRun));
        Assert.All(selectedLot.Weeks, x => Assert.Equal(DayOfWeek.Sunday, x.WeekStart.DayOfWeek));
        Assert.Equal(beforeReceipts, await db.Receipts.CountAsync());
        Assert.Equal(beforeAdjustments, await db.RoomInventoryAdjustments.CountAsync());
    }

    [Fact]
    public async Task CurrentCorrectedReceiptQuantityAndSoftVoid_ImmediatelyChangeReceivedOnly()
    {
        await using var db = CreateDbContext();
        var seed = await SeedAsync(db);
        var service = CreateService(db);

        seed.Receipt.BinCount = 75;
        seed.Receipt.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        var corrected = await service.GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026 }, CancellationToken.None);
        Assert.Equal(75, corrected.BinsReceived);
        Assert.Equal(60, corrected.BinsRun);

        seed.Receipt.IsDeleted = true;
        seed.Receipt.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        var voided = await service.GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026 }, CancellationToken.None);
        Assert.Equal(0, voided.BinsReceived);
        Assert.Equal(60, voided.BinsRun);
        Assert.Empty(await db.ReceiptInventoryOverrides.ToListAsync());
    }

    [Fact]
    public async Task FacilityFilters_UsePhysicalReceiptFacilityAndCreditedRunFacility()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);
        var service = CreateService(db);

        var wp = await service.GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026, Facility = "WP" }, CancellationToken.None);
        var ebs = await service.GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026, Facility = "EBS" }, CancellationToken.None);
        var all = await service.GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026, Facility = "All" }, CancellationToken.None);

        Assert.Equal(0, wp.BinsReceived);
        Assert.Equal(60, wp.BinsRun);
        Assert.Equal(100, ebs.BinsReceived);
        Assert.Equal(0, ebs.BinsRun);
        Assert.Equal(100, all.BinsReceived);
        Assert.Equal(60, all.BinsRun);
    }

    [Fact]
    public async Task PreAuthoritativeAndCanceledOrReversedLines_AreExcluded()
    {
        await using var db = CreateDbContext();
        var seed = await SeedAsync(db);
        var canceled = NewRun(2, seed.User, seed.Wp, ActualRunStatuses.Canceled);
        var canceledRevision = NewRevision(2, canceled, true);
        db.AddRange(canceled, canceledRevision);
        db.BinsRunEntries.Add(NewLine(2, 400, 2026, "1084", "9290", seed.GrowerLot, seed.Ebs, seed.Wp, seed.Room, seed.Fruit, seed.User, canceled, canceledRevision));
        var reversed = NewLine(3, 300, 2026, "1084", "9290", seed.GrowerLot, seed.Ebs, seed.Wp, seed.Room, seed.Fruit, seed.User);
        reversed.IsReversed = true;
        db.BinsRunEntries.Add(reversed);
        db.BinsRunEntries.Add(NewLine(4, 500, 2025, "1084", "9290", seed.GrowerLot, seed.Ebs, seed.Wp, seed.Room, seed.Fruit, seed.User));
        await db.SaveChangesAsync();

        var page = await CreateService(db).GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026 }, CancellationToken.None);

        Assert.Equal(60, page.BinsRun);
        Assert.DoesNotContain(page.CropYears, x => x < 2026);
    }

    [Fact]
    public async Task IdenticalDisplayedLotNumbers_DoNotMergeAcrossGrowersOrVarieties()
    {
        await using var db = CreateDbContext();
        var seed = await SeedAsync(db);
        var fuji = new FruitProfile { Id = 98200, Name = "Fuji", VarietyCode = "Fuji", FruitType = "Apple", ProductionType = "Conventional", IsOrganic = false, IsActive = true };
        var fujiLot = new GrowerLot { Id = 98200, Grower = "Smith Orchards", LotNumber = "9290", IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.AddRange(fuji, fujiLot,
            NewReceipt(98200, "1084", "Smith Orchards", "9290", 20, seed.Ebs, seed.Room, fuji, fujiLot),
            NewReceipt(98300, "2084", "Jones Orchards", "9290", 30, seed.Ebs, seed.Room, seed.Fruit, null));
        await db.SaveChangesAsync();

        var page = await CreateService(db).GetAsync(new GrowerLotProgressFilterForm
        {
            CropYear = 2026,
            ExpandedGrowerNumber = "1084"
        }, CancellationToken.None);

        Assert.Equal(2, page.GrowerCount);
        var smith = page.Growers.Single(x => x.GrowerNumber == "1084");
        Assert.Equal(2, smith.Varieties.Count);
        Assert.Equal(120, smith.BinsReceived);
        Assert.Equal(30, page.Growers.Single(x => x.GrowerNumber == "2084").BinsReceived);
    }

    [Fact]
    public async Task IncompleteAuthoritativeIdentity_IsExcludedAndLinkedForReview()
    {
        await using var db = CreateDbContext();
        var seed = await SeedAsync(db);
        var incompleteReceipt = NewReceipt(98400, "", "Incomplete", "9291", 44, seed.Ebs, seed.Room, seed.Fruit, null);
        var incompleteRun = NewLine(84, 55, 2026, "1084", "", seed.GrowerLot, seed.Ebs, seed.Wp, seed.Room, seed.Fruit, seed.User);
        db.AddRange(incompleteReceipt, incompleteRun);
        await db.SaveChangesAsync();

        var page = await CreateService(db).GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026 }, CancellationToken.None);

        Assert.Equal(100, page.BinsReceived);
        Assert.Equal(60, page.BinsRun);
        Assert.Contains(page.ExcludedIssues, x => x.IssueType == "Receipt identity incomplete" && x.RecordUrl == "/Receipts/98400");
        Assert.Contains(page.ExcludedIssues, x => x.IssueType == "Run lot identity incomplete");
        Assert.DoesNotContain(page.Growers, x => string.IsNullOrWhiteSpace(x.GrowerNumber));
    }

    [Fact]
    public void ColorPresentation_UsesReadableContrastAndCanonicalFallback()
    {
        Assert.Equal("#FFFFFF", ReportingColorPresentation.TextColor("#123456"));
        Assert.Equal("#17212B", ReportingColorPresentation.TextColor("#F5E66A"));
        Assert.Equal(
            VarietyColorService.FallbackColor(VarietyColorService.NormalizeIdentity("GSMT", "GSMT").Key),
            VarietyColorService.FallbackColor(VarietyColorService.NormalizeIdentity("Grannysmith", "Grannysmith").Key));
    }

    [Fact]
    public async Task OrganicAndConventionalCards_ShareBaseColorButRemainDistinctIdentities()
    {
        await using var db = CreateDbContext();
        var seed = await SeedAsync(db);
        var organic = new FruitProfile { Id = 98500, Name = "Organic Gala", VarietyCode = "Gala", FruitType = "Apple", ProductionType = "Organic", IsOrganic = true, IsActive = true };
        var organicLot = new GrowerLot { Id = 98500, Grower = "Smith Orchards", LotNumber = "ORG-1", IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.AddRange(organic, organicLot, NewReceipt(98500, "1084", "Smith Orchards", "ORG-1", 25, seed.Ebs, seed.Room, organic, organicLot));
        await db.SaveChangesAsync();

        var page = await CreateService(db).GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026, ExpandedGrowerNumber = "1084" }, CancellationToken.None);
        var cards = Assert.Single(page.Growers).Varieties;

        Assert.Equal(2, cards.Count);
        Assert.Contains(cards, x => x.IsOrganic);
        Assert.Contains(cards, x => !x.IsOrganic);
        Assert.Single(cards.Select(x => x.ColorHex).Distinct(StringComparer.OrdinalIgnoreCase));
        Assert.NotEqual(cards[0].VarietyKey, cards[1].VarietyKey);
    }

    [Fact]
    public async Task PostgreSql_AggregatesPageAndDrilldowns_ServerSide()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROPQC_TEST_GROWER_PROGRESS_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;
        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connectionString);
        var counter = new CommandCounter();
        var options = new DbContextOptionsBuilder<CropQcDbContext>().UseNpgsql(connectionString).AddInterceptors(counter).Options;
        await using var db = new CropQcDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await SeedAsync(db);
        counter.Reset();

        var overview = await CreateService(db).GetAsync(new GrowerLotProgressFilterForm
        {
            CropYear = 2026,
            Facility = "All",
            Sort = "BinsRun",
            ExpandedGrowerNumber = "1084"
        }, CancellationToken.None);

        var grower = Assert.Single(overview.Growers);
        Assert.Equal(100, grower.BinsReceived);
        Assert.Equal(60, grower.BinsRun);
        Assert.Equal(1, overview.ReceivedLotCount);
        Assert.InRange(counter.ReaderCommandCount, 1, 18);
        Console.WriteLine($"Grower & Lot Progress PostgreSQL overview query count: {counter.ReaderCommandCount}");
    }

    private static GrowerLotProgressService CreateService(CropQcDbContext db) => new(
        db,
        new PacificBusinessTimeService(new FixedClock(DateTimeOffset.Parse("2026-08-20T19:00:00Z"))),
        new VarietyColorService(db),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RunReporting:AuthoritativeStartCropYear"] = "2026",
            ["RunReporting:CropYearStartMonth"] = "7",
            ["RunReporting:CropYearStartDay"] = "15"
        }).Build());

    private static CropQcDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new CropQcDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static async Task<Seed> SeedAsync(CropQcDbContext db)
    {
        var ebs = await db.Warehouses.SingleOrDefaultAsync(x => x.Code == "EBS");
        var wp = await db.Warehouses.SingleOrDefaultAsync(x => x.Code == "WP");
        if (ebs is null)
        {
            ebs = new Warehouse { Id = 98100, Code = "EBS", Name = "EBS" };
            db.Warehouses.Add(ebs);
        }
        if (wp is null)
        {
            wp = new Warehouse { Id = 98101, Code = "WP", Name = "WP" };
            db.Warehouses.Add(wp);
        }
        var room = new Room { Id = 98100, Warehouse = ebs, Code = "PG-GROWER-PROGRESS-R1", Name = "Room 1", IsActive = true };
        var fruit = new FruitProfile { Id = 98100, Name = "Progress Gala", VarietyCode = "Gala", FruitType = "Apple", ProductionType = "Conventional", IsOrganic = false, IsActive = true };
        var growerLot = new GrowerLot { Id = 98100, Grower = "Smith Orchards", LotNumber = "9290", IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        var user = new User { Id = 98100, Email = "grower-progress-runner@wp-packing.com", DisplayName = "Runner", Domain = "wp-packing.com", EmploymentFacility = "WP", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        var receipt = new Receipt
        {
            Id = 98100,
            CropYear = 2026,
            ReceivedAt = DateTimeOffset.Parse("2026-08-01T17:00:00Z"),
            CompuTechReceiptId = "R-8100",
            Warehouse = ebs,
            Room = room,
            FruitProfile = fruit,
            GrowerLot = growerLot,
            GrowerNumber = "1084",
            GrowerName = "Smith Orchards",
            LotCode = "9290",
            BinCount = 100,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var run = NewRun(1, user, wp, ActualRunStatuses.Active);
        var revision = NewRevision(1, run, true);
        db.AddRange(room, fruit, growerLot, user, receipt, run, revision);
        db.BinsRunEntries.Add(NewLine(1, 60, 2026, "1084", "9290", growerLot, ebs, wp, room, fruit, user, run, revision));
        db.VarietyColorConfigurations.Add(new VarietyColorConfiguration
        {
            Id = 98100,
            FruitProfile = fruit,
            VarietyKey = "GALA",
            VarietyName = "Gala",
            HexColor = "#123456",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return new Seed(ebs, wp, room, fruit, growerLot, user, receipt);
    }

    private static ActualRun NewRun(long id, User user, Warehouse facility, string status) => new()
    {
        Id = id,
        Status = status,
        CurrentRevisionNumber = 1,
        RunAt = DateTimeOffset.Parse("2026-08-03T19:00:00Z"),
        CreatedAt = DateTimeOffset.UtcNow,
        CreatedByUser = user,
        RunFacilityWarehouse = facility,
        RunFacilityCodeSnapshot = facility.Code,
        RunFacilityAssignmentSource = RunFacilityAssignmentSources.Employment
    };

    private static ActualRunRevision NewRevision(long id, ActualRun run, bool current) => new()
    {
        Id = id,
        ActualRun = run,
        RevisionNumber = 1,
        OperationType = ActualRunRevisionTypes.Create,
        OperationKey = $"run-{id}",
        IsCurrent = current,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static Receipt NewReceipt(
        long id,
        string growerNumber,
        string growerName,
        string lot,
        int bins,
        Warehouse warehouse,
        Room room,
        FruitProfile fruit,
        GrowerLot? growerLot)
    {
        return new Receipt
        {
            Id = id,
            CropYear = 2026,
            ReceivedAt = DateTimeOffset.Parse("2026-08-02T17:00:00Z"),
            CompuTechReceiptId = $"R-{id}",
            Warehouse = warehouse,
            Room = room,
            FruitProfile = fruit,
            GrowerLot = growerLot,
            GrowerNumber = growerNumber,
            GrowerName = growerName,
            LotCode = lot,
            BinCount = bins,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    private static BinsRunEntry NewLine(
        long id,
        int bins,
        int cropYear,
        string grower,
        string lot,
        GrowerLot growerLot,
        Warehouse source,
        Warehouse reporting,
        Room room,
        FruitProfile fruit,
        User user,
        ActualRun? run = null,
        ActualRunRevision? revision = null)
    {
        var adjustment = new RoomInventoryAdjustment
        {
            Id = 9000 + id,
            CropYear = cropYear,
            Warehouse = source,
            Room = room,
            GrowerLot = growerLot,
            FruitProfile = fruit,
            GrowerName = "Smith Orchards",
            LotNumber = lot,
            VarietyCode = fruit.VarietyCode,
            ChangeAmount = -bins,
            NewBinCount = 0,
            AdjustmentType = BinsRunService.AdjustmentType,
            AdjustmentAt = DateTimeOffset.Parse("2026-08-03T19:00:00Z"),
            CreatedAt = DateTimeOffset.UtcNow
        };
        return new BinsRunEntry
        {
            Id = id,
            InventoryAdjustment = adjustment,
            Warehouse = source,
            Room = room,
            CropYear = cropYear,
            GrowerLot = growerLot,
            FruitProfile = fruit,
            GrowerName = "Smith Orchards",
            LotNumber = lot,
            VarietyCode = fruit.VarietyCode,
            PreviousAvailableBins = bins,
            BinsRun = bins,
            NewAvailableBins = 0,
            RunAt = DateTimeOffset.Parse("2026-08-03T19:00:00Z"),
            CreatedByUser = user,
            CreatedAt = DateTimeOffset.UtcNow,
            ActualRun = run,
            ActualRunRevision = revision,
            TransactionType = run is null ? ActualRunTransactionTypes.Legacy : ActualRunTransactionTypes.Depletion,
            ReportingFacilityWarehouse = reporting,
            ReportingFacilityCodeSnapshot = reporting.Code,
            ReportingFacilityAssignmentSource = RunFacilityAssignmentSources.Employment,
            ReportingCropYearSnapshot = cropYear,
            ReportingFruitProfileIdSnapshot = fruit.Id,
            ReportingVarietyCodeSnapshot = fruit.VarietyCode,
            ProductionTypeSnapshot = fruit.ProductionType,
            IsOrganicSnapshot = fruit.IsOrganic,
            GrowerNumberSnapshot = grower
        };
    }

    private sealed record Seed(Warehouse Ebs, Warehouse Wp, Room Room, FruitProfile Fruit, GrowerLot GrowerLot, User User, Receipt Receipt);
    private sealed class FixedClock(DateTimeOffset utcNow) : IClock { public DateTimeOffset UtcNow => utcNow; }
    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int ReaderCommandCount { get; private set; }
        public void Reset() => ReaderCommandCount = 0;
        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            ReaderCommandCount++;
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ReaderCommandCount++;
            return ValueTask.FromResult(result);
        }
    }
}
