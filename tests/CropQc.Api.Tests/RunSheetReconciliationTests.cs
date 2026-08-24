using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Api.Tests;

public sealed class RunSheetReconciliationTests
{
    [Fact]
    public void EbsParser_FindsHeaderAndAcceptsCatigoryTypo()
    {
        var runs = RunSheetParser.ParseWorksheet(EmploymentFacilities.Ebs, Rows(
            ["2026 EBS Packout"],
            [],
            ["Date", "Bins Dumped", "Grower Name", "Grower #", "Pool", "CATIGORY", "Variety"],
            ["7/31/2026", "128", "First", "1565", "P1", "APPLE", "GALA"],
            ["7/31/2026", "260", "Second", "9290", "P1", "APPLE", "GALA"]), Options());

        var run = Assert.Single(runs);
        Assert.Equal(388, run.TotalBins);
        Assert.Equal(128, run.GrowerBins["1565"]);
        Assert.Equal(260, run.GrowerBins["9290"]);
        Assert.Equal(RunSheetParser.ConventionalProductionType, run.ProductionType);
    }

    [Fact]
    public void EbsParser_AcceptsCorrectCategoryHeader()
    {
        var run = Assert.Single(RunSheetParser.ParseWorksheet(EmploymentFacilities.Ebs, EbsRows(
            ["8/14/2026", 408, "9332", "ORG APPLE", "ORGA"]), Options()));

        Assert.Equal(RunSheetParser.OrganicProductionType, run.ProductionType);
    }

    [Theory]
    [InlineData("DMX", "Domex")]
    [InlineData("HB", "Honey Bear")]
    [InlineData("VIVA", "Viva Tierra")]
    [InlineData(" dmx ", "Domex")]
    public void WpParser_MapsConfiguredSalesCodes(string code, string expected)
    {
        var run = Assert.Single(RunSheetParser.ParseWorksheet(EmploymentFacilities.Wp, WpRows(
            [code, "7/27/2026", "184", "1084", "APPLE", "BART"]), Options()));

        Assert.Equal(expected, run.SalesDesk);
        Assert.Null(run.UnknownSalesDeskCode);
    }

    [Fact]
    public void WpParser_SurfacesUnknownSalesCode()
    {
        var run = Assert.Single(RunSheetParser.ParseWorksheet(EmploymentFacilities.Wp, WpRows(
            ["NEW", "8/1/2026", 20, "1001", "APPLE", "GALA"]), Options()));

        Assert.Null(run.SalesDesk);
        Assert.Equal("NEW", run.UnknownSalesDeskCode);
    }

    [Fact]
    public void Parser_IgnoresNoiseBlankSummaryAndOtherCropYears()
    {
        var runs = RunSheetParser.ParseWorksheet(EmploymentFacilities.Ebs, EbsRows(
            ["NP", 40, "1001", "APPLE", "GALA"],
            ["SUN", 40, "1001", "APPLE", "GALA"],
            ["", 40, "1001", "APPLE", "GALA"],
            ["8/1/2025", 40, "1001", "APPLE", "GALA"],
            ["8/1/2026", 0, "1001", "APPLE", "GALA"],
            ["8/1/2026", 40, "", "APPLE", "GALA"],
            ["8/1/2026", 40, "1001", "APPLE", ""],
            ["8/1/2026", "1,234", "9,350.00", "APPLE", "GALA"]), Options());

        var run = Assert.Single(runs);
        Assert.Equal(1234, run.TotalBins);
        Assert.Equal(1234, run.GrowerBins["9350"]);
    }

    [Theory]
    [InlineData("APPLE", "Conventional")]
    [InlineData("PEAR", "Conventional")]
    [InlineData("ORG APPLE", "Organic")]
    [InlineData("ORG AP", "Organic")]
    [InlineData("ORG PR", "Organic")]
    [InlineData("organic pear", "Organic")]
    public void ProductionTypeNormalization_IsCentralized(string category, string expected) =>
        Assert.Equal(expected, RunSheetParser.NormalizeProductionType(category));

    [Fact]
    public void Parser_RequiresHeadersWithinBoundedRange()
    {
        var options = Options();
        options.HeaderSearchRows = 2;

        Assert.Throws<RunSheetConfigurationException>(() => RunSheetParser.ParseWorksheet(
            EmploymentFacilities.Ebs,
            Rows(["title"], ["still title"], ["Date", "Bins Dumped", "Grower #", "CATEGORY", "Variety"]),
            options));
    }

    [Fact]
    public void MultipleSheetGrowerRows_FormOnePhysicalRun()
    {
        var run = Assert.Single(RunSheetParser.ParseWorksheet(EmploymentFacilities.Ebs, EbsRows(
            ["8/14/2026", 296, "9332", "ORG APPLE", "ORGA"],
            ["8/14/2026", 112, "9671", "ORG APPLE", "ORGA"]), Options()));

        Assert.Equal(408, run.TotalBins);
        Assert.Equal(2, run.GrowerBins.Count);
    }

    [Fact]
    public void EbsProductionShapedExactRun_Matches()
    {
        var sheet = External(EmploymentFacilities.Ebs, new(2026, 7, 31), "GALA", "Conventional", null, 388,
            ("1565", 128), ("9290", 260));
        var crop = Crop(EmploymentFacilities.Ebs, new(2026, 7, 31), ["GALA"], ["Conventional"], "N/A", 388,
            [20], ("1565", 128), ("9290", 260));

        var item = Assert.Single(Match(EmploymentFacilities.Ebs, [sheet], [crop]));
        Assert.Equal(RunSheetReconciliationStates.Match, item.State);
        Assert.Empty(item.Reasons);
    }

    [Fact]
    public void SplitCropActualRuns_406Plus2_AggregateAndMatch408()
    {
        var time = BusinessTime(new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero));
        var lines = new[]
        {
            Line(25, "9332", 294), Line(25, "9671", 112), Line(26, "9332", 2)
        };
        var crop = Assert.Single(RunSheetCropRunBuilder.Build(EmploymentFacilities.Ebs, lines, time));
        var sheet = External(EmploymentFacilities.Ebs, new(2026, 8, 14), "ORGA", "Organic", null, 408,
            ("9332", 296), ("9671", 112));

        Assert.Equal(new long[] { 25, 26 }, crop.ActualRunIds);
        Assert.Equal(408, crop.TotalBins);
        Assert.Equal(RunSheetReconciliationStates.Match, Assert.Single(Match(EmploymentFacilities.Ebs, [sheet], [crop])).State);
    }

    [Fact]
    public void ProbableDateMismatch_IsOnePairedAttentionItem()
    {
        var growers = new[] { ("3162", 126), ("9372", 11), ("9392", 107), ("9682", 121) };
        var sheet = External(EmploymentFacilities.Ebs, new(2026, 8, 13), "GALA", "Conventional", null, 365, growers);
        var crop = Crop(EmploymentFacilities.Ebs, new(2026, 8, 14), ["GALA"], ["Conventional"], "N/A", 365, [21, 22], growers);

        var item = Assert.Single(Match(EmploymentFacilities.Ebs, [sheet], [crop]));
        Assert.Equal(RunSheetReconciliationStates.Attention, item.State);
        Assert.Equal([RunSheetReconciliationReasons.ProbableDateMismatch], item.Reasons);
        Assert.DoesNotContain(item.Reasons, x => x.Contains("Missing", StringComparison.Ordinal));
    }

    [Fact]
    public void BinMismatch_ReportsBothTotals()
    {
        var item = Assert.Single(Match(EmploymentFacilities.Ebs,
            [External(EmploymentFacilities.Ebs, new(2026, 8, 2), "GALA", "Conventional", null, 100, ("1001", 100))],
            [Crop(EmploymentFacilities.Ebs, new(2026, 8, 2), ["GALA"], ["Conventional"], "N/A", 98, [1], ("1001", 98))]));

        Assert.Contains(RunSheetReconciliationReasons.BinMismatch, item.Reasons);
        Assert.Equal(100, item.SheetBins);
        Assert.Equal(98, item.CropQcBins);
    }

    [Fact]
    public void WpGrowerMismatchAndMissingSalesDesk_Coexist()
    {
        var sheet = External(EmploymentFacilities.Wp, new(2026, 8, 4), "BART", "Conventional", "Domex", 153,
            ("1084", 22), ("1121", 65), ("1162", 46), ("1531", 8), ("4402", 12));
        var crop = Crop(EmploymentFacilities.Wp, new(2026, 8, 4), ["BART"], ["Conventional"], "Unassigned", 153, [23],
            ("1084", 22), ("1121", 65), ("1162", 46), ("1532", 8), ("4302", 12));

        var item = Assert.Single(Match(EmploymentFacilities.Wp, [sheet], [crop]));
        Assert.Contains(RunSheetReconciliationReasons.GrowerMismatch, item.Reasons);
        Assert.Contains(RunSheetReconciliationReasons.SalesDeskMissing, item.Reasons);
        Assert.Contains(item.Growers, x => x.GrowerNumber == "1531" && x.SheetBins == 8 && x.CropQcBins == 0);
        Assert.Contains(item.Growers, x => x.GrowerNumber == "1532" && x.SheetBins == 0 && x.CropQcBins == 8);
    }

    [Fact]
    public void WpMixedVarietyAndMissingSalesDesk_RemainVisible()
    {
        var sheet = External(EmploymentFacilities.Wp, new(2026, 8, 18), "ORBA", "Organic", "Domex", 194,
            ("1084", 58), ("1121", 64), ("1162", 72));
        var crop = Crop(EmploymentFacilities.Wp, new(2026, 8, 18), ["ORGA", "ORBA"], ["Organic"], "Unassigned", 194, [30],
            ("1084", 58), ("1121", 64), ("1162", 72));

        var item = Assert.Single(Match(EmploymentFacilities.Wp, [sheet], [crop]));
        Assert.Contains(RunSheetReconciliationReasons.VarietyMismatch, item.Reasons);
        Assert.Contains(RunSheetReconciliationReasons.SalesDeskMissing, item.Reasons);
        Assert.Equal("ORGA / ORBA", item.CropQcVariety);
    }

    [Fact]
    public void CropRunBuilder_DoesNotSplitMixedActualRunIntoPerfectGroups()
    {
        var time = BusinessTime(new DateTimeOffset(2026, 8, 19, 12, 0, 0, TimeSpan.Zero));
        var lines = new[]
        {
            new RunSheetCropLine(30, new(2026, 8, 19, 5, 0, 0, TimeSpan.Zero), null, "ORGA", "Organic", true, "1084", 58),
            new RunSheetCropLine(30, new(2026, 8, 19, 5, 0, 0, TimeSpan.Zero), null, "ORBA", "Organic", true, "1121", 136)
        };

        var run = Assert.Single(RunSheetCropRunBuilder.Build(EmploymentFacilities.Wp, lines, time));
        Assert.Equal(new[] { "ORBA", "ORGA" }, run.Varieties);
        Assert.Equal([30], run.ActualRunIds);
    }

    [Fact]
    public void ProductionTypeMismatch_IsAttention()
    {
        var item = Assert.Single(Match(EmploymentFacilities.Ebs,
            [External(EmploymentFacilities.Ebs, new(2026, 8, 5), "GALA", "Organic", null, 50, ("1001", 50))],
            [Crop(EmploymentFacilities.Ebs, new(2026, 8, 5), ["GALA"], ["Conventional"], "N/A", 50, [1], ("1001", 50))]));
        Assert.Contains(RunSheetReconciliationReasons.ProductionTypeMismatch, item.Reasons);
    }

    [Fact]
    public void WpSalesDeskMismatch_IsAttention()
    {
        var item = Assert.Single(Match(EmploymentFacilities.Wp,
            [External(EmploymentFacilities.Wp, new(2026, 8, 5), "BART", "Conventional", "Honey Bear", 50, ("1084", 50))],
            [Crop(EmploymentFacilities.Wp, new(2026, 8, 5), ["BART"], ["Conventional"], "Domex", 50, [1], ("1084", 50))]));
        Assert.Contains(RunSheetReconciliationReasons.SalesDeskMismatch, item.Reasons);
    }

    [Fact]
    public void HistoricUnassignedWpRun_IsFlagged()
    {
        var item = Assert.Single(Match(EmploymentFacilities.Wp,
            [External(EmploymentFacilities.Wp, new(2026, 7, 27), "BART", "Conventional", "Domex", 184, ("1084", 184))],
            [Crop(EmploymentFacilities.Wp, new(2026, 7, 27), ["BART"], ["Conventional"], "Unassigned", 184, [20], ("1084", 184))]));
        Assert.Contains(RunSheetReconciliationReasons.SalesDeskMissing, item.Reasons);
    }

    [Fact]
    public void IntentionalActualRun20Domex_MatchesExactly()
    {
        var item = Assert.Single(Match(EmploymentFacilities.Wp,
            [External(EmploymentFacilities.Wp, new(2026, 7, 27), "BART", "Conventional", "Domex", 184, ("1084", 184))],
            [Crop(EmploymentFacilities.Wp, new(2026, 7, 27), ["BART"], ["Conventional"], "Domex", 184, [20], ("1084", 184))]));
        Assert.Equal(RunSheetReconciliationStates.Match, item.State);
        Assert.Equal([20], item.ActualRunIds);
    }

    [Fact]
    public void UnknownSalesCode_IsConfigurationAttention()
    {
        var sheet = External(EmploymentFacilities.Wp, new(2026, 8, 6), "BART", "Conventional", null, 50, ("1084", 50)) with
        {
            UnknownSalesDeskCode = "NEW"
        };
        var item = Assert.Single(Match(EmploymentFacilities.Wp, [sheet],
            [Crop(EmploymentFacilities.Wp, new(2026, 8, 6), ["BART"], ["Conventional"], "Domex", 50, [1], ("1084", 50))]));
        Assert.Contains(RunSheetReconciliationReasons.UnknownSalesDeskCode, item.Reasons);
    }

    [Fact]
    public void SheetOnlyRun_IsMissingFromCropQcImmediately()
    {
        var item = Assert.Single(Match(EmploymentFacilities.Wp,
            [External(EmploymentFacilities.Wp, new(2026, 8, 8), "BART", "Conventional", "Honey Bear", 192,
                ("1084", 33), ("1121", 56), ("1162", 13), ("1532", 38), ("4302", 20), ("4402", 32))], []));
        Assert.Equal(RunSheetReconciliationStates.Attention, item.State);
        Assert.Contains(RunSheetReconciliationReasons.MissingFromCropQc, item.Reasons);
    }

    [Theory]
    [InlineData(23.99, "PendingSheetVerification")]
    [InlineData(24, "AttentionNeeded")]
    [InlineData(48, "AttentionNeeded")]
    public void CropOnlyRun_UsesTwentyFourHourGrace(double hoursOld, string expectedState)
    {
        var now = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
        var crop = Crop(EmploymentFacilities.Ebs, new(2026, 8, 20), ["GALA"], ["Conventional"], "N/A", 50, [1], ("1001", 50)) with
        {
            LatestRunAt = now.AddHours(-hoursOld)
        };
        var item = Assert.Single(RunSheetMatcher.Reconcile(EmploymentFacilities.Ebs, [], [crop], now, TimeSpan.FromHours(24)));

        Assert.Equal(expectedState, item.State);
        if (expectedState == RunSheetReconciliationStates.Pending) Assert.Empty(item.Reasons);
        else Assert.Contains(RunSheetReconciliationReasons.MissingFromSheet, item.Reasons);
    }

    [Fact]
    public void CropRunBuilder_UsesPacificBusinessDate()
    {
        var utc = new DateTimeOffset(2026, 8, 15, 6, 30, 0, TimeSpan.Zero);
        var run = Assert.Single(RunSheetCropRunBuilder.Build(EmploymentFacilities.Ebs,
            [new RunSheetCropLine(1, utc, null, "GALA", "Conventional", false, "1001", 20)],
            BusinessTime(utc.AddHours(1))));
        Assert.Equal(new DateOnly(2026, 8, 14), run.Date);
    }

    [Fact]
    public void EbsMatching_IgnoresSalesDesk()
    {
        var item = Assert.Single(Match(EmploymentFacilities.Ebs,
            [External(EmploymentFacilities.Ebs, new(2026, 8, 1), "GALA", "Conventional", null, 20, ("1001", 20))],
            [Crop(EmploymentFacilities.Ebs, new(2026, 8, 1), ["GALA"], ["Conventional"], "Domex", 20, [1], ("1001", 20))]));
        Assert.Equal(RunSheetReconciliationStates.Match, item.State);
        Assert.DoesNotContain(item.Reasons, x => x.Contains("Sales Desk", StringComparison.Ordinal));
    }

    [Fact]
    public void Facilities_AreNeverCrossMatched()
    {
        var items = Match(EmploymentFacilities.Wp,
            [External(EmploymentFacilities.Ebs, new(2026, 8, 1), "GALA", "Conventional", null, 20, ("1001", 20))],
            [Crop(EmploymentFacilities.Wp, new(2026, 8, 1), ["GALA"], ["Conventional"], "Domex", 20, [1], ("1001", 20))]);
        Assert.Equal(2, items.Count);
        Assert.DoesNotContain(items, x => x.State == RunSheetReconciliationStates.Match);
    }

    [Fact]
    public void Matching_IsDeterministicAndNeverUsesCropRunTwice()
    {
        var sheetA = External(EmploymentFacilities.Ebs, new(2026, 8, 1), "GALA", "Conventional", null, 20, ("1001", 20));
        var sheetB = sheetA with { Date = new(2026, 8, 2) };
        var crop = Crop(EmploymentFacilities.Ebs, new(2026, 8, 1), ["GALA"], ["Conventional"], "N/A", 20, [1], ("1001", 20));

        var first = Match(EmploymentFacilities.Ebs, [sheetB, sheetA], [crop]);
        var second = Match(EmploymentFacilities.Ebs, [sheetA, sheetB], [crop]);
        Assert.Equal(first.Select(Signature), second.Select(Signature));
        Assert.Single(first, x => x.ActualRunIds.Contains(1));
    }

    [Fact]
    public async Task SnapshotFailureWithoutCache_IsUnavailableNotMissing()
    {
        using var db = Db();
        var clock = new FixedClock(new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero));
        var options = Options();
        options.Enabled = true;
        var store = new RunSheetSnapshotStore(options, clock);
        store.RecordFailure("Google Sheets is temporarily unavailable.", clock.UtcNow);
        var service = new RunSheetReconciliationService(db, store, options, new PacificBusinessTimeService(clock));

        var model = await service.GetAsync(EmploymentFacilities.Wp, 2026, CancellationToken.None);

        Assert.NotNull(model);
        Assert.Equal(RunSheetReconciliationStates.Unavailable, model!.Availability);
        Assert.Empty(model.Items);
        Assert.Equal(0, model.AttentionNeededCount);
    }

    [Fact]
    public void FailedRefresh_PreservesCacheAndMarksItStale()
    {
        var clock = new FixedClock(new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero));
        var options = Options();
        var store = new RunSheetSnapshotStore(options, clock);
        store.RecordSuccess([], clock.UtcNow.AddMinutes(-5));
        store.RecordFailure("Quota unavailable.", clock.UtcNow);

        var state = store.GetState();
        Assert.NotNull(state.Snapshot);
        Assert.True(state.IsStale);
        Assert.Equal("Quota unavailable.", state.FailureMessage);
    }

    [Fact]
    public async Task ReconciliationGet_PerformsZeroDatabaseWrites()
    {
        using var db = Db();
        var clock = new FixedClock(new(2026, 8, 20, 12, 0, 0, TimeSpan.Zero));
        var options = Options();
        options.Enabled = true;
        var store = new RunSheetSnapshotStore(options, clock);
        store.RecordSuccess([External(EmploymentFacilities.Ebs, new(2026, 8, 1), "GALA", "Conventional", null, 20, ("1001", 20))], clock.UtcNow);
        var service = new RunSheetReconciliationService(db, store, options, new PacificBusinessTimeService(clock));

        var model = await service.GetAsync(EmploymentFacilities.Ebs, 2026, CancellationToken.None);

        Assert.NotNull(model);
        Assert.False(db.ChangeTracker.HasChanges());
        Assert.Empty(db.ActualRuns);
        Assert.Empty(db.BinsRunEntries);
        Assert.Empty(db.AuditLogs);
    }

    [Fact]
    public async Task Reconciliation_IsLimitedToEnabledConfiguredCropYear()
    {
        using var db = Db();
        var clock = new FixedClock(DateTimeOffset.UtcNow);
        var options = Options();
        options.Enabled = true;
        var service = new RunSheetReconciliationService(db, new RunSheetSnapshotStore(options, clock), options, new PacificBusinessTimeService(clock));

        Assert.Null(await service.GetAsync(EmploymentFacilities.Wp, 2025, CancellationToken.None));
        options.Enabled = false;
        Assert.Null(await service.GetAsync(EmploymentFacilities.Wp, 2026, CancellationToken.None));
    }

    [Fact]
    public void GoogleReader_IsReadOnlyAndExcludesCherryApricotTab()
    {
        var source = File.ReadAllText(RepositoryFile("src", "CropQc.Web", "Services", "GoogleRunSheetReader.cs"));
        var options = Options();

        Assert.Contains("SheetsService.Scope.SpreadsheetsReadonly", source);
        Assert.Contains("Spreadsheets.Values.Get", source);
        Assert.DoesNotContain(".Update(", source);
        Assert.DoesNotContain(".Append(", source);
        Assert.DoesNotContain(".Clear(", source);
        Assert.DoesNotContain(".BatchUpdate(", source);
        Assert.Equal("DAILY APPLE/PEAR", options.WpSheetName);
        Assert.DoesNotContain("CHERRY", options.WpSheetName, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Ui_ShowsAttentionPendingMatchedStaleAndSideBySideDetailsWithoutCorrectionControls()
    {
        var source = File.ReadAllText(RepositoryFile("src", "CropQc.Web", "Views", "BinsRun", "Index.cshtml"));

        Assert.Contains("ATTENTION NEEDED", source);
        Assert.Contains("Pending Sheet Verification", source);
        Assert.Contains("Verified / Matched", source);
        Assert.Contains("Stale verification snapshot", source);
        Assert.Contains("Google Sheet", source);
        Assert.Contains("Crop QC", source);
        Assert.Contains("Grower allocation", source);
        Assert.Contains("/BinsRun/ActualRuns/", source);
        Assert.DoesNotContain("Dismiss reconciliation", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Ignore reconciliation", source, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(typeof(HttpRequestException), "Google Sheets is temporarily unavailable.")]
    [InlineData(typeof(InvalidOperationException), "Run verification refresh failed (InvalidOperationException).")]
    public void RefreshFailures_ReturnSafeDiagnostics(Type exceptionType, string expected)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, "secret-token-value")!;
        var message = RunSheetRefreshHostedService.SafeFailureMessage(exception);

        Assert.Equal(expected, message);
        Assert.DoesNotContain("secret-token-value", message);
    }

    private static IReadOnlyList<RunSheetReconciliationItemViewModel> Match(
        string facility,
        IReadOnlyList<ExternalPhysicalRun> sheets,
        IReadOnlyList<CropPhysicalRun> crops) =>
        RunSheetMatcher.Reconcile(facility, sheets, crops, new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero), TimeSpan.FromHours(24));

    private static ExternalPhysicalRun External(
        string facility,
        DateOnly date,
        string variety,
        string production,
        string? desk,
        int bins,
        params (string Grower, int Bins)[] growers) =>
        new(facility, date, variety, production, desk, null, bins, Dictionary(growers));

    private static CropPhysicalRun Crop(
        string facility,
        DateOnly date,
        IReadOnlyList<string> varieties,
        IReadOnlyList<string> production,
        string desk,
        int bins,
        IReadOnlyList<long> ids,
        params (string Grower, int Bins)[] growers) =>
        new(facility, date, varieties, production, desk, bins, Dictionary(growers), ids,
            new DateTimeOffset(date.ToDateTime(new TimeOnly(12)), TimeSpan.Zero));

    private static RunSheetCropLine Line(long id, string grower, int bins) =>
        new(id, new DateTimeOffset(2026, 8, 14, 20, 0, 0, TimeSpan.Zero), null, "ORGA", "Organic", true, grower, bins);

    private static Dictionary<string, int> Dictionary(IEnumerable<(string Grower, int Bins)> growers) =>
        growers.ToDictionary(x => x.Grower, x => x.Bins, StringComparer.OrdinalIgnoreCase);

    private static string Signature(RunSheetReconciliationItemViewModel item) =>
        $"{item.State}|{item.SheetDate}|{item.CropQcDate}|{string.Join(',', item.ActualRunIds)}|{string.Join(',', item.Reasons)}";

    private static IReadOnlyList<IReadOnlyList<object?>> EbsRows(params object?[][] data) =>
        new object?[][] { ["Date", "Bins Dumped", "Grower #", "CATEGORY", "Variety"] }
            .Concat(data)
            .Select(x => (IReadOnlyList<object?>)x)
            .ToList();

    private static IReadOnlyList<IReadOnlyList<object?>> WpRows(params object?[][] data) =>
        new object?[][] { ["SALES", "Date", "Bins Dumped", "Grower #", "CATEGORY", "Variety"] }
            .Concat(data)
            .Select(x => (IReadOnlyList<object?>)x)
            .ToList();

    private static IReadOnlyList<IReadOnlyList<object?>> Rows(params object?[][] rows) =>
        rows.Select(x => (IReadOnlyList<object?>)x).ToList();

    private static RunSheetReconciliationOptions Options() => new();

    private static PacificBusinessTimeService BusinessTime(DateTimeOffset now) => new(new FixedClock(now));

    private static CropQcDbContext Db()
    {
        var options = new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new CropQcDbContext(options);
    }

    private static string RepositoryFile(params string[] path)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CropQc.sln")))
        {
            directory = directory.Parent;
        }
        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Repository root not found."), Path.Combine(path));
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }
}
