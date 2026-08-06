using System.Data.Common;
using System.Security.Claims;
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
    public async Task AllFacilities_ExcludesReceiptsOutsideWpAndEbs()
    {
        await using var db = CreateDbContext();
        var seed = await SeedAsync(db);
        var third = new Warehouse { Id = 98600, Code = "THIRD", Name = "Third warehouse" };
        var thirdRoom = new Room { Id = 98600, Warehouse = third, Code = "THIRD-R1", Name = "Third room", IsActive = true };
        db.AddRange(third, thirdRoom, NewReceipt(98600, "9999", "Third Grower", "THIRD-LOT", 77, third, thirdRoom, seed.Fruit, null));
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var all = await service.GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026, Facility = "All" }, CancellationToken.None);
        var wp = await service.GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026, Facility = "WP" }, CancellationToken.None);
        var ebs = await service.GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026, Facility = "EBS" }, CancellationToken.None);

        Assert.Equal(wp.BinsReceived + ebs.BinsReceived, all.BinsReceived);
        Assert.Equal(100, all.BinsReceived);
        Assert.DoesNotContain(all.Growers, x => x.GrowerNumber == "9999");
        Assert.Equal(1, all.ReceivedLotCount);
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
        Assert.Contains(page.ExcludedIssues, x => x.IssueType == "Run reporting identity incomplete");
        Assert.Equal(1, page.ExcludedReceiptCount);
        Assert.Equal(1, page.ExcludedRunLineCount);
        Assert.DoesNotContain(page.Growers, x => string.IsNullOrWhiteSpace(x.GrowerNumber));
        var needsReview = await CreateRunReportingService(db).GetAsync(
            new BinsRunFilterForm { Section = "NeedsReview" },
            Principal(),
            CancellationToken.None);
        Assert.Contains(needsReview.Issues, x => x.EntryId == 84 && x.IssueType == "Missing source lot");
    }

    [Fact]
    public async Task VarietySelection_HonorsCanonicalProductionAndOrganicIdentity_AndFailsClosed()
    {
        await using var db = CreateDbContext();
        var seed = await SeedAsync(db);
        var organic = new FruitProfile { Id = 98700, Name = "Organic Gala", VarietyCode = "Gala", FruitType = "Apple", ProductionType = "Organic", IsOrganic = true, IsActive = true };
        var organicLot = new GrowerLot { Id = 98700, Grower = "Smith Orchards", LotNumber = "ORG-7", IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.AddRange(organic, organicLot,
            NewReceipt(98700, "1084", "Smith Orchards", "ORG-7", 25, seed.Ebs, seed.Room, organic, organicLot),
            NewLine(87, 15, 2026, "1084", "ORG-7", organicLot, seed.Ebs, seed.Wp, seed.Room, organic, seed.User));
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var options = (await service.GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026 }, CancellationToken.None)).VarietyOptions;
        var organicKey = options.Single(x => x.VarietyKey.StartsWith("GALA|", StringComparison.Ordinal) && x.IsOrganic).VarietyKey;
        var conventionalKey = options.Single(x => x.VarietyKey.StartsWith("GALA|", StringComparison.Ordinal) && !x.IsOrganic).VarietyKey;
        var organicOnly = await service.GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026, VarietyKey = organicKey }, CancellationToken.None);
        var conventionalOnly = await service.GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026, VarietyKey = conventionalKey }, CancellationToken.None);
        var intersection = await service.GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026, VarietyKey = organicKey, ProductionType = "Conventional" }, CancellationToken.None);
        var malformed = await service.GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026, VarietyKey = "GALA|Organic|true|tampered" }, CancellationToken.None);

        Assert.Equal((25, 15), (organicOnly.BinsReceived, organicOnly.BinsRun));
        Assert.Equal((100, 60), (conventionalOnly.BinsReceived, conventionalOnly.BinsRun));
        Assert.Equal((0, 0), (intersection.BinsReceived, intersection.BinsRun));
        Assert.Equal((0, 0), (malformed.BinsReceived, malformed.BinsRun));
        Assert.NotNull(malformed.FilterValidationMessage);
    }

    [Fact]
    public async Task CanonicalProfileAliases_ReconcileOneLotAndItsWeeklySupportingLines()
    {
        await using var db = CreateDbContext();
        var seed = await SeedAsync(db);
        var receiptProfile = new FruitProfile { Id = 98800, Name = "GSMT", VarietyCode = "GSMT", FruitType = "Apple", ProductionType = "Conventional", IsOrganic = false, IsActive = true };
        var runProfile = new FruitProfile { Id = 98801, Name = "Grannysmith", VarietyCode = "Grannysmith", FruitType = "Apple", ProductionType = "Conventional", IsOrganic = false, IsActive = true };
        var aliasLot = new GrowerLot { Id = 98800, Grower = "Alias Orchard", LotNumber = "GS-88", IsActive = true, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.AddRange(receiptProfile, runProfile, aliasLot,
            NewReceipt(98800, "3088", "Alias Orchard", "GS-88", 40, seed.Ebs, seed.Room, receiptProfile, aliasLot),
            NewLine(88, 30, 2026, "3088", "GS-88", aliasLot, seed.Ebs, seed.Wp, seed.Room, runProfile, seed.User));
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var growerPage = await service.GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026, ExpandedGrowerNumber = "3088" }, CancellationToken.None);
        var variety = Assert.Single(growerPage.Growers.Single(x => x.GrowerNumber == "3088").Varieties);
        Assert.Equal("Granny Smith", variety.Variety);
        var lotPage = await service.GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026, ExpandedGrowerNumber = "3088", ExpandedVarietyKey = variety.VarietyKey }, CancellationToken.None);
        var lot = Assert.Single(Assert.Single(lotPage.Growers.Single(x => x.GrowerNumber == "3088").Varieties).Lots);
        Assert.Equal((40, 30), (lot.BinsReceived, lot.BinsRun));
        var weeklyPage = await service.GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026, ExpandedGrowerNumber = "3088", ExpandedVarietyKey = variety.VarietyKey, SelectedLotKey = lot.LotKey }, CancellationToken.None);
        var selected = Assert.Single(Assert.Single(weeklyPage.Growers.Single(x => x.GrowerNumber == "3088").Varieties).Lots);
        Assert.Equal(selected.BinsRun, selected.Weeks.Sum(x => x.BinsRun));
    }

    [Fact]
    public async Task ExclusionReview_CoversEveryRequiredIdentity_WithoutInactiveFalsePositives()
    {
        await using var db = CreateDbContext();
        var seed = await SeedAsync(db);
        var lines = Enumerable.Range(0, 8)
            .Select(offset => NewLine(200 + offset, 1, 2026, "1084", "EXCLUDED", seed.GrowerLot, seed.Ebs, seed.Wp, seed.Room, seed.Fruit, seed.User))
            .ToArray();
        lines[0].ReportingCropYearSnapshot = null;
        lines[1].ReportingFruitProfileIdSnapshot = null;
        lines[2].ReportingVarietyCodeSnapshot = null;
        lines[3].ProductionTypeSnapshot = null;
        lines[4].IsOrganicSnapshot = null;
        lines[5].GrowerNumberSnapshot = null;
        lines[6].LotNumber = "";
        lines[7].ReportingFacilityCodeSnapshot = null;
        var canceled = NewRun(299, seed.User, seed.Wp, ActualRunStatuses.Canceled);
        var canceledRevision = NewRevision(299, canceled, true);
        var inactive = NewLine(299, 50, 2026, "", "", seed.GrowerLot, seed.Ebs, seed.Wp, seed.Room, seed.Fruit, seed.User, canceled, canceledRevision);
        inactive.ReportingVarietyCodeSnapshot = null;
        db.AddRange(lines);
        db.AddRange(canceled, canceledRevision, inactive);
        await db.SaveChangesAsync();

        var page = await CreateService(db).GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026 }, CancellationToken.None);

        Assert.Equal(8, page.ExcludedRunLineCount);
        Assert.Equal(8, page.ExcludedIssues.Count(x => x.IssueType == "Run reporting identity incomplete"));
        Assert.DoesNotContain(page.ExcludedIssues, x => x.RecordUrl.Contains("299", StringComparison.Ordinal));
    }

    [Fact]
    public async Task OversizedLot_ReturnsControlledWarningAndKeepsTotals()
    {
        await using var db = CreateDbContext();
        var seed = await SeedAsync(db);
        for (var index = 0; index <= GrowerLotProgressService.MaximumLotRunRows; index++)
        {
            db.BinsRunEntries.Add(NewLine(10000 + index, 1, 2026, "1084", "9290", seed.GrowerLot, seed.Ebs, seed.Wp, seed.Room, seed.Fruit, seed.User));
        }
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var expanded = await service.GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026, ExpandedGrowerNumber = "1084" }, CancellationToken.None);
        var variety = Assert.Single(Assert.Single(expanded.Growers).Varieties);
        var lots = await service.GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026, ExpandedGrowerNumber = "1084", ExpandedVarietyKey = variety.VarietyKey }, CancellationToken.None);
        var lot = Assert.Single(Assert.Single(Assert.Single(lots.Growers).Varieties).Lots);
        var selectedPage = await service.GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026, ExpandedGrowerNumber = "1084", ExpandedVarietyKey = variety.VarietyKey, SelectedLotKey = lot.LotKey }, CancellationToken.None);
        var selected = Assert.Single(Assert.Single(Assert.Single(selectedPage.Growers).Varieties).Lots);

        Assert.NotNull(selected.WeeklyDetailWarning);
        Assert.Empty(selected.Weeks);
        Assert.Equal(GrowerLotProgressService.MaximumLotRunRows + 1 + 60, selected.BinsRun);
    }

    [Fact]
    public async Task SupportingPagination_NormalizesAndProvidesPreviousAndNextState()
    {
        await using var db = CreateDbContext();
        var seed = await SeedAsync(db);
        for (var index = 0; index < 51; index++)
        {
            db.BinsRunEntries.Add(NewLine(400 + index, 1, 2026, "1084", "9290", seed.GrowerLot, seed.Ebs, seed.Wp, seed.Room, seed.Fruit, seed.User));
        }
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var expanded = await service.GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026, ExpandedGrowerNumber = "1084" }, CancellationToken.None);
        var variety = Assert.Single(Assert.Single(expanded.Growers).Varieties);
        var lotPage = await service.GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026, ExpandedGrowerNumber = "1084", ExpandedVarietyKey = variety.VarietyKey }, CancellationToken.None);
        var lot = Assert.Single(Assert.Single(Assert.Single(lotPage.Growers).Varieties).Lots);
        var weekPage = await service.GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026, ExpandedGrowerNumber = "1084", ExpandedVarietyKey = variety.VarietyKey, SelectedLotKey = lot.LotKey }, CancellationToken.None);
        var week = Assert.Single(Assert.Single(Assert.Single(Assert.Single(weekPage.Growers).Varieties).Lots).Weeks);
        var secondPage = await service.GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026, ExpandedGrowerNumber = "1084", ExpandedVarietyKey = variety.VarietyKey, SelectedLotKey = lot.LotKey, SelectedWeekStart = week.WeekStart, SupportingPage = 999 }, CancellationToken.None);
        var selectedWeek = Assert.Single(Assert.Single(Assert.Single(Assert.Single(secondPage.Growers).Varieties).Lots).Weeks);

        Assert.Equal(2, selectedWeek.SupportingPage);
        Assert.True(selectedWeek.HasPreviousSupportingRecords);
        Assert.False(selectedWeek.HasMoreSupportingRecords);
        Assert.Equal(2, selectedWeek.SupportingRecords.Count);
    }

    [Fact]
    public async Task GrowerAndRunTotals_ReconcileAcrossEveryDrilldownLevel()
    {
        await using var db = CreateDbContext();
        await SeedAsync(db);
        var growerService = CreateService(db);
        var growerPage = await growerService.GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026, Facility = "WP", ExpandedGrowerNumber = "1084" }, CancellationToken.None);
        var variety = Assert.Single(Assert.Single(growerPage.Growers).Varieties);
        var lotPage = await growerService.GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026, Facility = "WP", ExpandedGrowerNumber = "1084", ExpandedVarietyKey = variety.VarietyKey }, CancellationToken.None);
        var lot = Assert.Single(Assert.Single(Assert.Single(lotPage.Growers).Varieties).Lots);
        var weeklyPage = await growerService.GetAsync(new GrowerLotProgressFilterForm { CropYear = 2026, Facility = "WP", ExpandedGrowerNumber = "1084", ExpandedVarietyKey = variety.VarietyKey, SelectedLotKey = lot.LotKey }, CancellationToken.None);
        var selectedLot = Assert.Single(Assert.Single(Assert.Single(weeklyPage.Growers).Varieties).Lots);
        var runTotals = await CreateRunReportingService(db).GetAsync(new BinsRunFilterForm { Section = "RunTotals", ReportFacility = "WP", ReportCropYear = 2026 }, Principal(), CancellationToken.None);
        var detail = Assert.IsType<RunTotalsDetailViewModel>(runTotals.Detail);

        Assert.Equal(detail.TotalBins, growerPage.BinsRun);
        Assert.Equal(growerPage.BinsRun, growerPage.Growers.Sum(x => x.BinsRun));
        Assert.Equal(growerPage.Growers.Single().BinsRun, growerPage.Growers.Single().Varieties.Sum(x => x.BinsRun));
        Assert.Equal(variety.BinsRun, lotPage.Growers.Single().Varieties.Single().Lots.Sum(x => x.BinsRun));
        Assert.Equal(selectedLot.BinsRun, selectedLot.Weeks.Sum(x => x.BinsRun));
        var selectedWeek = Assert.Single(selectedLot.Weeks);
        var supportPage = await growerService.GetAsync(new GrowerLotProgressFilterForm
        {
            CropYear = 2026,
            Facility = "WP",
            ExpandedGrowerNumber = "1084",
            ExpandedVarietyKey = variety.VarietyKey,
            SelectedLotKey = lot.LotKey,
            SelectedWeekStart = selectedWeek.WeekStart
        }, CancellationToken.None);
        var supportWeek = Assert.Single(Assert.Single(Assert.Single(Assert.Single(supportPage.Growers).Varieties).Lots).Weeks);
        Assert.Equal(supportWeek.BinsRun, supportWeek.SupportingRecords.Sum(x => x.Bins));
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

    private static RunReportingService CreateRunReportingService(CropQcDbContext db) => new(
        db,
        new PacificBusinessTimeService(new FixedClock(DateTimeOffset.Parse("2026-08-20T19:00:00Z"))),
        new AllowAccess(),
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RunReporting:AuthoritativeStartCropYear"] = "2026",
            ["RunReporting:CropYearStartMonth"] = "7",
            ["RunReporting:CropYearStartDay"] = "15"
        }).Build(),
        new VarietyColorService(db));

    private static ClaimsPrincipal Principal() => new(
        new ClaimsIdentity([new Claim(ClaimTypes.Email, "owner@fruitandland.com")], "Test"));

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
    private sealed class AllowAccess : IUserAccessService
    {
        public Task<bool> HasAccessAsync(ClaimsPrincipal principal, string areaKey, PageAccessLevel minimumLevel, CancellationToken cancellationToken) => Task.FromResult(true);
        public Task<PageAccessLevel> GetAccessLevelAsync(string? email, string areaKey, CancellationToken cancellationToken) => Task.FromResult(PageAccessLevel.Admin);
        public Task<IReadOnlyList<UserAccessMatrixRow>> GetMatrixAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task EnsureAccessMatrixAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> SaveMatrixAsync(UserAccessMatrixForm form, string changedByEmail, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
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
