using System.Net;
using CropQc.Data;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace CropQc.Api.Tests;

public sealed class RunSheetSplitVarietyTests
{
    private static readonly DateOnly RunDate = new(2026, 8, 24);
    private static readonly DateTimeOffset RunAt = new(2026, 8, 24, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ActualRun46_ParserKeepsTwoVarieties_BuilderKeepsOneRun_ReconcilesExactly()
    {
        IReadOnlyList<IReadOnlyList<object?>> rows = [
            ["Sales", "Date", "Bins Dumped", "Grower #", "Category", "Variety"],
            ["DMX", "8/24/2026", 100, "1531", "ORG PR", "ORBA"],
            ["DMX", "8/24/2026", 30, "4301", "ORG PR", "ORBA"],
            ["DMX", "8/24/2026", 40, "1531", "ORG PR", "ORRB"],
            ["DMX", "8/24/2026", 45, "4301", "ORG PR", "ORRB"]];
        var options = new RunSheetReconciliationOptions { CropYear = 2026 };
        options.SalesDeskMappings["DMX"] = "Domex";
        var sheets = RunSheetParser.ParseWorksheet("WP", rows, options);
        var crops = RunSheetCropRunBuilder.Build("WP", [
            new(46, RunAt, "Domex", "ORBA", "Organic", true, "1531", 100),
            new(46, RunAt, "Domex", "ORBA", "Organic", true, "4301", 30),
            new(46, RunAt, "Domex", "ORRB", "Organic", true, "1531", 40),
            new(46, RunAt, "Domex", "ORRB", "Organic", true, "4301", 45)],
            new PacificBusinessTimeService(new FixedClock()));

        Assert.Equal(2, sheets.Count);
        var crop = Assert.Single(crops);
        Assert.Equal(new[] { "ORBA", "ORRB" }, crop.Varieties);
        Assert.Equal(215, crop.TotalBins);
        Assert.Equal(140, crop.GrowerBins["1531"]);
        Assert.Equal(75, crop.GrowerBins["4301"]);
        var item = Assert.Single(Reconcile(sheets, crops));
        Assert.Equal(RunSheetReconciliationStates.Match, item.State);
        Assert.Empty(item.Reasons);
        Assert.Equal(new long[] { 46 }, item.ActualRunIds);
        Assert.Equal("ORBA / ORRB", item.SheetVariety);
        Assert.Equal(215, item.SheetBins);
        Assert.All(item.Growers, grower => Assert.Equal(grower.CropQcBins, grower.SheetBins));
        Assert.Contains("2 variety-specific runs", item.InformationMessage);
        Assert.Null(item.DiagnosticMessage);
    }

    [Theory]
    [InlineData("bins")]
    [InlineData("growers")]
    [InlineData("missing-variety")]
    [InlineData("extra-variety")]
    [InlineData("production")]
    [InlineData("desk")]
    [InlineData("unknown-desk")]
    [InlineData("blank-desk")]
    [InlineData("date")]
    [InlineData("partial-growers")]
    [InlineData("unrelated-growers")]
    [InlineData("mixed-production")]
    [InlineData("facility")]
    [InlineData("unassigned-crop")]
    public void SplitVarieties_RequiresEveryExactDimension(string difference)
    {
        var sheets = Sheets().ToList();
        var crop = Crop();
        switch (difference)
        {
            case "bins":
                sheets[1] = sheets[1] with { TotalBins = 86, GrowerBins = Growers(40, 46) };
                break;
            case "growers":
                sheets[1] = sheets[1] with { GrowerBins = Growers(39, 46) };
                break;
            case "missing-variety":
                sheets.RemoveAt(1);
                break;
            case "extra-variety":
                sheets[1] = sheets[1] with { TotalBins = 75, GrowerBins = Growers(30, 45) };
                sheets.Add(sheets[1] with { Variety = "ORGA", TotalBins = 10, GrowerBins = new Dictionary<string, int> { ["1531"] = 10 } });
                break;
            case "production": sheets[1] = sheets[1] with { ProductionType = "Conventional" }; break;
            case "desk": sheets[1] = sheets[1] with { SalesDesk = "Honey Bear" }; break;
            case "unknown-desk": sheets[1] = sheets[1] with { UnknownSalesDeskCode = "NEW" }; break;
            case "blank-desk": sheets[1] = sheets[1] with { SalesDesk = null }; break;
            case "date": sheets[1] = sheets[1] with { Date = RunDate.AddDays(1) }; break;
            case "partial-growers":
                sheets[1] = sheets[1] with { GrowerBins = new Dictionary<string, int> { ["1531"] = 40, ["9999"] = 45 } };
                break;
            case "unrelated-growers":
                sheets = sheets.Select(x => x with { GrowerBins = new Dictionary<string, int> { ["9999"] = x.TotalBins } }).ToList();
                break;
            case "mixed-production": crop = crop with { ProductionTypes = ["Organic", "Conventional"] }; break;
            case "facility": sheets[1] = sheets[1] with { Facility = "EBS" }; break;
            case "unassigned-crop": crop = crop with { SalesDesk = "Unassigned" }; break;
        }

        var items = Reconcile(sheets, [crop]);
        Assert.DoesNotContain(items, item => item.State == RunSheetReconciliationStates.Match);
        Assert.All(items, item => Assert.Equal(RunSheetReconciliationStates.Attention, item.State));
        Assert.Single(items, item => item.ActualRunIds.Contains(46));
    }

    [Fact]
    public void DifferentDatesForEntireGroup_NeverSilentlyMatches()
    {
        var items = Reconcile(Sheets().Select(x => x with { Date = RunDate.AddDays(1) }).ToList(), [Crop()]);
        Assert.DoesNotContain(items, item => item.State == RunSheetReconciliationStates.Match);
    }

    [Theory]
    [InlineData("WP")]
    [InlineData("EBS")]
    public void TrueExtraVariety_RejectsExactSubsetAndKeepsAllSheetEvidence(string facility)
    {
        var sheets = Sheets().Select(x => x with { Facility = facility }).ToList();
        sheets.Add(sheets[0] with { Variety = "GALA", TotalBins = 10, GrowerBins = new Dictionary<string, int> { ["1531"] = 10 } });
        var crop = Crop() with { Facility = facility };
        Assert.Equal(215, sheets.Take(2).Sum(x => x.TotalBins));
        Assert.Equal(140, sheets.Take(2).Sum(x => x.GrowerBins["1531"]));
        Assert.Equal(75, sheets.Take(2).Sum(x => x.GrowerBins["4301"]));

        var items = Reconcile(sheets, [crop]);

        Assert.All(items, item => Assert.Equal(RunSheetReconciliationStates.Attention, item.State));
        Assert.Single(items, item => item.ActualRunIds.Contains(46));
        Assert.DoesNotContain(items, item => item.InformationMessage?.Contains("one combined Actual Run") == true);
        // Later ordinary discrepancy matching may pair a row, but must not hide or consume
        // any of the three Sheet records as a successful grouped match.
        Assert.Equal(3, items.Count(item => item.SheetBins.HasValue));
        Assert.Equal(225, items.Sum(item => item.SheetBins ?? 0));
        foreach (var sheet in sheets)
        {
            var evidence = Assert.Single(items, item => item.SheetVariety == sheet.Variety);
            Assert.Equal(sheet.TotalBins, evidence.SheetBins);
            foreach (var grower in sheet.GrowerBins)
                Assert.Equal(grower.Value, Assert.Single(evidence.Growers, row => row.GrowerNumber == grower.Key).SheetBins);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExtraVarietyConsumedByExactMatch_DoesNotBlockSplitGroup(bool reverse)
    {
        var sheets = Sheets().ToList();
        var gala = sheets[0] with { Variety = "GALA", TotalBins = 10, GrowerBins = new Dictionary<string, int> { ["1531"] = 10 } };
        sheets.Add(gala);
        var single = Crop() with { Varieties = ["GALA"], TotalBins = 10, GrowerBins = gala.GrowerBins, ActualRunIds = [47] };
        var crops = new[] { Crop(), single };
        if (reverse)
        {
            sheets.Reverse();
            Array.Reverse(crops);
        }

        var items = Reconcile(sheets, crops);

        Assert.Equal(2, items.Count);
        Assert.All(items, item =>
        {
            Assert.Equal(RunSheetReconciliationStates.Match, item.State);
            Assert.Empty(item.Reasons);
        });
        var exact = Assert.Single(items, item => item.ActualRunIds.Contains(47));
        Assert.Equal("GALA", exact.SheetVariety);
        Assert.Equal(10, exact.SheetBins);
        Assert.Null(exact.InformationMessage);
        var grouped = Assert.Single(items, item => item.ActualRunIds.Contains(46));
        Assert.Equal("ORBA / ORRB", grouped.SheetVariety);
        Assert.Equal(215, grouped.SheetBins);
        Assert.Contains("2 variety-specific runs", grouped.InformationMessage);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultipleExactCombinations_AreAmbiguousRegardlessOfInputOrder(bool reverse)
    {
        var sheets = Sheets().ToList();
        // Two distinct Sheet entries with identical evidence are two possible assignments.
        sheets.Add(sheets[0] with { GrowerBins = Growers(100, 30) });
        if (reverse) sheets.Reverse();
        var items = Reconcile(sheets, [Crop()]);
        var item = Assert.Single(items, x => x.ActualRunIds.Contains(46));
        Assert.Equal(RunSheetReconciliationStates.Attention, item.State);
        Assert.Contains(RunSheetReconciliationReasons.AmbiguousSplitVarieties, item.Reasons);
        Assert.Contains("no Sheet runs were selected", item.InformationMessage);
        Assert.Equal(3, items.Count(x => x.Reasons.Contains(RunSheetReconciliationReasons.MissingFromCropQc)));
        Assert.DoesNotContain(items, x => x.State == RunSheetReconciliationStates.Match);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompetingCropRuns_DoNotStealTheSameSheetGroup(bool reverse)
    {
        var crops = new[] { Crop(), Crop() with { ActualRunIds = [47] } };
        if (reverse) Array.Reverse(crops);
        var items = Reconcile(Sheets(), crops);
        Assert.Equal(2, items.Count(x => x.Reasons.Contains(RunSheetReconciliationReasons.AmbiguousSplitVarieties)));
        Assert.DoesNotContain(items, x => x.State == RunSheetReconciliationStates.Match);
    }

    [Fact]
    public void ExactOneToOne_TakesPriorityAndCannotBeReused()
    {
        var sheets = Sheets();
        var single = Crop() with { Varieties = ["ORBA"], TotalBins = 130, GrowerBins = Growers(100, 30), ActualRunIds = [47] };
        var items = Reconcile(sheets, [Crop(), single]);
        var match = Assert.Single(items, x => x.State == RunSheetReconciliationStates.Match);
        Assert.Equal(new long[] { 47 }, match.ActualRunIds);
        Assert.Equal(130, match.SheetBins);
        Assert.DoesNotContain(items, x => x.ActualRunIds.Contains(46) && x.State == RunSheetReconciliationStates.Match);
    }

    [Fact]
    public void ExactGroup_PrecedesSingleVarietyDateMismatch()
    {
        var nextDay = Crop() with { Date = RunDate.AddDays(1), Varieties = ["ORBA"], TotalBins = 130, GrowerBins = Growers(100, 30), ActualRunIds = [47] };
        var items = Reconcile(Sheets(), [nextDay, Crop()]);
        Assert.Equal(new long[] { 46 }, Assert.Single(items, x => x.State == RunSheetReconciliationStates.Match).ActualRunIds);
        Assert.Contains(items, x => x.ActualRunIds.Contains(47) && x.Reasons.Contains(RunSheetReconciliationReasons.MissingFromSheet));
    }

    [Fact]
    public void SingleVarietyExactAndProbableDateMismatch_AreUnchanged()
    {
        var sheet = Sheets()[0];
        var single = Crop() with { Varieties = ["ORBA"], TotalBins = 130, GrowerBins = Growers(100, 30) };
        Assert.Equal(RunSheetReconciliationStates.Match, Assert.Single(Reconcile([sheet], [single])).State);
        var nextDay = Assert.Single(Reconcile([sheet], [single with { Date = RunDate.AddDays(1) }]));
        Assert.Equal(RunSheetReconciliationStates.Attention, nextDay.State);
        Assert.Equal(new[] { RunSheetReconciliationReasons.ProbableDateMismatch }, nextDay.Reasons);
    }

    [Fact]
    public void EbsSplitVarieties_RetainSalesDeskNotApplicableSemantics()
    {
        var sheets = Sheets().Select(x => x with { Facility = "EBS", SalesDesk = null }).ToList();
        var item = Assert.Single(Reconcile(sheets, [Crop() with { Facility = "EBS", SalesDesk = "N/A" }]));
        Assert.Equal(RunSheetReconciliationStates.Match, item.State);
        Assert.Equal("N/A", item.SheetSalesDesk);
        Assert.Equal("N/A", item.CropQcSalesDesk);
    }

    [Fact]
    public void Grouping_NormalizesCodesAndGrowersWithoutChangingInputs()
    {
        var sheets = Sheets();
        sheets[0] = sheets[0] with { Variety = " orba ", GrowerBins = new Dictionary<string, int> { ["1,531.00"] = 100, ["4301"] = 30 } };
        var crop = Crop() with { GrowerBins = new Dictionary<string, int> { ["1531.00"] = 140, ["4301"] = 75 } };
        var item = Assert.Single(Reconcile(sheets, [crop]));
        Assert.Equal(RunSheetReconciliationStates.Match, item.State);
        Assert.Equal("ORBA / ORRB", item.SheetVariety);
        Assert.Equal(140, Assert.Single(item.Growers, x => x.GrowerNumber == "1531").SheetBins);
        Assert.All(item.Growers, grower => Assert.Equal(grower.SheetBins, grower.CropQcBins));
        Assert.Equal(" orba ", sheets[0].Variety);
        Assert.Contains("1,531.00", sheets[0].GrowerBins.Keys);
    }

    [Fact]
    public void Grouping_IsBoundedAndFailsClosed()
    {
        var sheets = Sheets();
        var many = Enumerable.Range(0, 17).SelectMany(_ => sheets.Select(x => x with { })).ToList();
        var items = Reconcile(many, [Crop()]);
        var item = Assert.Single(items, x => x.ActualRunIds.Contains(46));
        Assert.Contains(RunSheetReconciliationReasons.AmbiguousSplitVarieties, item.Reasons);
        Assert.Contains("bounded candidate limit", item.InformationMessage);
        Assert.DoesNotContain(items, x => x.State == RunSheetReconciliationStates.Match);
    }

    [Fact]
    public void NonmatchingAlternative_DoesNotPreventUniqueExactGroup()
    {
        var sheets = Sheets().ToList();
        sheets.Add(sheets[0] with { TotalBins = 131, GrowerBins = Growers(101, 30) });
        var items = Reconcile(sheets, [Crop()]);
        Assert.Equal(215, Assert.Single(items, x => x.State == RunSheetReconciliationStates.Match).SheetBins);
        Assert.Equal(131, Assert.Single(items, x => x.Reasons.Contains(RunSheetReconciliationReasons.MissingFromCropQc)).SheetBins);
    }

    [Fact]
    public void ThreeVarieties_CanReconcileOneActualRun()
    {
        var sheets = Sheets().ToList();
        sheets.Add(sheets[1] with { Variety = "ORGA", TotalBins = 5, GrowerBins = new Dictionary<string, int> { ["1531"] = 5 } });
        var crop = Crop() with { Varieties = ["ORBA", "ORRB", "ORGA"], TotalBins = 220, GrowerBins = Growers(145, 75) };
        var item = Assert.Single(Reconcile(sheets, [crop]));
        Assert.Equal(RunSheetReconciliationStates.Match, item.State);
        Assert.Equal("ORBA / ORGA / ORRB", item.SheetVariety);
        Assert.Contains("3 variety-specific runs", item.InformationMessage);
    }

    private static ExternalPhysicalRun[] Sheets() => [
        new("WP", RunDate, "ORBA", "Organic", "Domex", null, 130, Growers(100, 30)),
        new("WP", RunDate, "ORRB", "Organic", "Domex", null, 85, Growers(40, 45))];

    [Fact]
    public async Task RunTotalsView_RendersGroupedMatchInformationAndActualRunLink()
    {
        await using var factory = new RenderFactory();
        await using var scope = factory.Services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var item = Assert.Single(Reconcile(Sheets(), [Crop()]));
        var model = new BinsRunPageViewModel
        {
            Filter = new() { Section = "RunTotals" },
            RunReporting = new()
            {
                Detail = new()
                {
                    Facility = "WP",
                    CropYear = 2026,
                    SheetReconciliation = new()
                    {
                        Availability = RunSheetReconciliationStates.Available,
                        MatchedCount = 1,
                        Items = [item]
                    }
                }
            }
        };
        var context = new ActionContext(new DefaultHttpContext { RequestServices = provider },
            new RouteData(), new ActionDescriptor());
        var view = provider.GetRequiredService<IRazorViewEngine>().GetView(null, "/Views/BinsRun/Index.cshtml", false);
        Assert.True(view.Success);
        var data = new ViewDataDictionary<BinsRunPageViewModel>(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = model };
        using var writer = new StringWriter();
        await view.View.RenderAsync(new ViewContext(context, view.View, data,
            new TempDataDictionary(context.HttpContext, provider.GetRequiredService<ITempDataProvider>()), writer, new HtmlHelperOptions()));
        var html = WebUtility.HtmlDecode(writer.ToString());
        Assert.Contains("<strong>MATCH</strong>", html);
        Assert.Contains("ORBA / ORRB", html);
        Assert.Contains($"<p class=\"notice\">{item.InformationMessage}</p>", html);
        Assert.Contains("href=\"/BinsRun/ActualRuns/46\"", html);
        Assert.DoesNotContain("ATTENTION NEEDED", html);
        Assert.DoesNotContain("Crop QC data-integrity issue", html);
    }

    private sealed class RenderFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:EnsureCreatedOnStartup"] = "true",
                ["Database:SeedMasterDataOnStartup"] = "false",
                ["Backups:Enabled"] = "false",
                ["EbsDailyBinsEmail:Enabled"] = "false",
                ["RENDER_EXTERNAL_HOSTNAME"] = "integration-test.local"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<CropQcDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<CropQcDbContext>>();
                services.RemoveAll<CropQcDbContext>();
                services.RemoveAll<IHostedService>();
                services.AddDbContext<CropQcDbContext>(options => options.UseInMemoryDatabase($"split-variety-render-{Guid.NewGuid():N}"));
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
            });
        }
    }

    private static CropPhysicalRun Crop() =>
        new("WP", RunDate, ["ORBA", "ORRB"], ["Organic"], "Domex", 215, Growers(140, 75), [46], RunAt);

    private static Dictionary<string, int> Growers(int first, int second) => new() { ["1531"] = first, ["4301"] = second };

    private static IReadOnlyList<RunSheetReconciliationItemViewModel> Reconcile(
        IReadOnlyList<ExternalPhysicalRun> sheets, IReadOnlyList<CropPhysicalRun> crops) =>
        RunSheetMatcher.Reconcile("WP", sheets, crops, RunAt.AddDays(3), TimeSpan.FromHours(24));

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => RunAt;
    }
}
