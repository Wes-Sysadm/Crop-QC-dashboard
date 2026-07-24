using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Models;

namespace CropQc.Api.Tests;

public sealed class ProjectionOutcomeTests
{
    [Theory]
    [InlineData("44.99", 44)]
    [InlineData("44.10", 44)]
    [InlineData("44.00", 44)]
    [InlineData("0.99", 0)]
    public void CompletePackCounts_AlwaysFloor(string value, int expected) =>
        Assert.Equal(expected, ProjectionOutcomeCalculator.Floor(decimal.Parse(value)));

    [Fact]
    public void AllocationService_DoesNotRoundPackCategoriesUp()
    {
        var result = CommercialPackAllocationService.Allocate(
            Plan(Pack("80", 80, 40m)),
            [Pool(1, 80, 44.99m * 40m)],
            40m);

        Assert.Equal(44, Assert.Single(result.Packs).RoundedPacks);
        Assert.Equal(39.6m, result.RoundingResidualPounds);
        Assert.Equal("2.0", result.CalculationVersion);
    }

    [Fact]
    public void LessThanOnePack_ProducesZeroAndPreservesResidual()
    {
        var result = CommercialPackAllocationService.Allocate(
            Plan(Pack("80", 80, 40m)),
            [Pool(1, 80, 39.6m)],
            40m);

        var pack = Assert.Single(result.Packs);
        Assert.Equal(0, pack.RoundedPacks);
        Assert.Equal(39.6m, pack.RoundingResidualPounds);
    }

    [Fact]
    public void Outcome_ReconcilesCompleteResidualUnallocatedAndCullByWeight()
    {
        var outcome = ProjectionOutcomeCalculator.Build(Projection(), DateTimeOffset.UtcNow);

        Assert.Equal(1760m, outcome.CompletePackPounds
            + outcome.ResidualPackedPounds
            + outcome.UnallocatedPackedPounds
            + outcome.CullPounds);
        Assert.Equal(0m, outcome.ReconciliationDifference);
    }

    [Fact]
    public void CullOutput_UsesThirtyFiveFortyTwentyFiveOfCullOnly()
    {
        var outcome = ProjectionOutcomeCalculator.Build(Projection(), DateTimeOffset.UtcNow);

        Assert.Equal(154m, outcome.CullTotals.PeelerPounds);
        Assert.Equal(176m, outcome.CullTotals.JuicePounds);
        Assert.Equal(110m, outcome.CullTotals.WastePounds);
        Assert.Equal(outcome.CullTotals.TotalCullPounds,
            outcome.CullTotals.PeelerPounds + outcome.CullTotals.JuicePounds + outcome.CullTotals.WastePounds);
        Assert.NotEqual(outcome.Projection.TotalProjectedPounds * ProjectionOutcomeCalculator.PeelerRate,
            outcome.CullTotals.PeelerPounds);
    }

    [Fact]
    public void MixedCommodities_KeepSeparatePoundsPerBin()
    {
        var projection = Projection();
        projection.Sources =
        [
            Source(1, "Apple block", "Apple", 880m, 880m, 660m, 220m),
            Source(2, "Pear block", "Pear", 920m, 920m, 690m, 230m)
        ];
        projection.TotalProjectedPounds = 1800m;
        projection.TotalCullProjectedPounds = 450m;
        projection.PackUnallocatedPounds = 150m;

        var outcome = ProjectionOutcomeCalculator.Build(projection, DateTimeOffset.UtcNow);

        Assert.Equal(880m, outcome.CullByCommodity.Single(x => x.Commodity == "Apple").PoundsPerBin);
        Assert.Equal(920m, outcome.CullByCommodity.Single(x => x.Commodity == "Pear").PoundsPerBin);
        Assert.True(outcome.HasMixedCommodities);
    }

    [Fact]
    public void SourceCullContributions_SumToCombinedTotals()
    {
        var outcome = ProjectionOutcomeCalculator.Build(Projection(), DateTimeOffset.UtcNow);

        Assert.Equal(outcome.CullTotals.PeelerPounds, outcome.SourceContributions.Sum(x => x.PeelerPounds));
        Assert.Equal(outcome.CullTotals.JuicePounds, outcome.SourceContributions.Sum(x => x.JuicePounds));
        Assert.Equal(outcome.CullTotals.WastePounds, outcome.SourceContributions.Sum(x => x.WastePounds));
    }

    [Fact]
    public void Matrix_UsesSavedJointGradeAllocationsAndFloorsEachCategory()
    {
        var outcome = ProjectionOutcomeCalculator.Build(Projection(), DateTimeOffset.UtcNow);
        var row = Assert.Single(outcome.Matrix);

        Assert.Equal(20, row.CompleteBoxesByGrade["Extra Fancy"]);
        Assert.Equal(9, row.CompleteBoxesByGrade["Fancy"]);
        Assert.Equal(29, row.TotalCompleteBoxes);
        Assert.Equal(30, row.PackCompleteCount);
        Assert.Equal(18, outcome.JointBasisFruitCount);
    }

    [Fact]
    public void IndependentSizeAndGradeDistributions_DoNotCreateMatrix()
    {
        var projection = Projection();
        projection.PackResults = [PackResult([], jointBasis: 0)];
        projection.Sources[0].JointSizeGradeBasisFruitCount = 0;
        projection.Sources[1].JointSizeGradeBasisFruitCount = 0;

        var outcome = ProjectionOutcomeCalculator.Build(projection, DateTimeOffset.UtcNow);

        Assert.NotEmpty(outcome.Grades);
        Assert.Empty(outcome.GradeNames);
        Assert.All(outcome.Matrix, row => Assert.Empty(row.CompleteBoxesByGrade));
        Assert.Contains(outcome.Warnings, x => x.Contains("both calculated size and grade", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SparseJointBasis_ProducesVisibleLowConfidenceWarning()
    {
        var projection = Projection();
        projection.Sources[0].JointSizeGradeBasisFruitCount = 3;
        projection.Sources[1].JointSizeGradeBasisFruitCount = 3;

        var outcome = ProjectionOutcomeCalculator.Build(projection, DateTimeOffset.UtcNow);

        Assert.Equal("Low", outcome.Confidence);
        Assert.Contains(outcome.Warnings, x => x.Contains("only 6 fruit", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StoredTrendSnapshot_IsRenderedWithoutLiveSampleLookup()
    {
        var projection = Projection();
        projection.Sources[0].SelectedQcSourceType = RunProjectionQcSourceTypes.FieldSample;
        projection.Sources[0].FieldSampleTrendSnapshotJson = JsonSerializer.Serialize(new[]
        {
            new RunProjectionTrendPointSnapshot(
                7,
                DateTimeOffset.Parse("2026-07-20T18:00:00Z"),
                "Bartlett",
                "Complete",
                10,
                10,
                180m,
                14m,
                3m,
                10m,
                [new(80, 60m), new(90, 40m)])
        });

        var outcome = ProjectionOutcomeCalculator.Build(projection, DateTimeOffset.UtcNow);

        var point = Assert.Single(outcome.TrendSources.Single(x => x.SourceId == 1).Points);
        Assert.Equal(7, point.SampleId);
        Assert.Equal(180m, point.AverageWeightGrams);
    }

    [Fact]
    public void BuildingOutcome_DoesNotMutateProjectionOrInventoryState()
    {
        var projection = Projection();
        var before = JsonSerializer.Serialize(projection);

        _ = ProjectionOutcomeCalculator.Build(projection, DateTimeOffset.UtcNow);

        Assert.Equal(before, JsonSerializer.Serialize(projection));
    }

    [Fact]
    public void PlannerCards_OpenOutcomeAndDeletedCardsDoNotUseActiveOutcomeLink()
    {
        var view = ReadRepositoryFile("src", "CropQc.Web", "Views", "BinsRun", "Index.cshtml");

        Assert.Contains("$\"/BinsRun/Projections/{item.Id}/Outcome\"", view);
        Assert.Contains("item.IsDeleted", view);
        Assert.Contains("View Outcome", view);
    }

    [Fact]
    public void OutcomeSections_AppearInRequiredOperationalOrder()
    {
        var view = ReadRepositoryFile("src", "CropQc.Web", "Views", "BinsRun", "ProjectionOutcome.cshtml");
        var ordered = new[]
        {
            "Lots and Blocks Included",
            "Projected Packed Boxes by Size / Commercial Pack",
            "Projected Packed Boxes by Grade",
            "Size-by-Grade Production Matrix",
            "Projected Cull Output",
            "Peeler, Juice, and Waste",
            "Source-Level Contribution",
            "Growth Trend and Field Sample Basis",
            "Assumptions, Confidence, and Warnings"
        };

        var prior = -1;
        foreach (var heading in ordered)
        {
            var next = view.IndexOf(heading, StringComparison.Ordinal);
            Assert.True(next > prior, $"{heading} must follow the prior report section.");
            prior = next;
        }
    }

    [Fact]
    public void OutcomePage_ContainsIdentitySourcesWarningsAndProjectionStatement()
    {
        var view = ReadRepositoryFile("src", "CropQc.Web", "Views", "BinsRun", "ProjectionOutcome.cshtml");

        Assert.Contains("Projection #@projection.Id", view);
        Assert.Contains("Grower number", view);
        Assert.Contains("Canonical block", view);
        Assert.Contains("Source warning", view);
        Assert.Contains("This is a projection, not actual production", view);
    }

    [Fact]
    public void PrintView_ContainsReportContentAndExcludesChromeThroughPrintCss()
    {
        var view = ReadRepositoryFile("src", "CropQc.Web", "Views", "BinsRun", "ProjectionOutcome.cshtml");
        var css = ReadRepositoryFile("src", "CropQc.Web", "wwwroot", "css", "site.css");

        Assert.Contains("Print Projection", view);
        Assert.Contains("window.print()", view);
        Assert.Contains("outcome-print-running-footer", view);
        Assert.Contains("@page", css);
        Assert.Contains("size: letter portrait", css);
        Assert.Contains(".topbar", css);
        Assert.Contains("break-inside: avoid", css);
        Assert.Contains("thead", css);
        Assert.Contains(".outcome-bar-fill.grade", css);
        Assert.Contains("background: #444 !important", css);
    }

    [Fact]
    public void OutcomeAccess_UsesBinsRunViewPolicy()
    {
        var controller = ReadRepositoryFile("src", "CropQc.Web", "Controllers", "BinsRunController.cs");

        var outcomeAction = controller.IndexOf("ProjectionOutcome(long id", StringComparison.Ordinal);
        var policy = controller.LastIndexOf("[Authorize(Policy = AccessPolicyNames.BinsRunView)]", outcomeAction, StringComparison.Ordinal);
        Assert.True(policy >= 0 && outcomeAction - policy < 250);
    }

    [Fact]
    public void CsvExport_UsesSameOutcomeFloorsResidualsMatrixAndCull()
    {
        var controller = ReadRepositoryFile("src", "CropQc.Web", "Controllers", "BinsRunController.cs");

        Assert.Contains("GetOutcomeAsync(id", controller);
        Assert.Contains("pack.CompletePacks", controller);
        Assert.Contains("Size-by-grade basis", controller);
        Assert.Contains("outcome.ResidualPackedPounds", controller);
        Assert.Contains("Peeler,35", controller);
    }

    [Fact]
    public void OutcomeTrendMigration_IsAdditiveAndProviderCompatible()
    {
        var migration = Directory.GetFiles(RepositoryRoot(), "*AddRunProjectionOutcomeTrendSnapshots.cs", SearchOption.AllDirectories)
            .Single(x => !x.EndsWith(".Designer.cs", StringComparison.OrdinalIgnoreCase));
        var text = File.ReadAllText(migration);

        Assert.Contains("AddColumn<string>", text);
        Assert.Contains("FieldSampleTrendSnapshotJson", text);
        Assert.Contains("MigrationProviderTypes.StoreType", text);
        Assert.DoesNotContain("DropTable", text);
        Assert.DoesNotContain("DeleteData", text);
        Assert.DoesNotContain("UpdateData", text);
    }

    [Fact]
    public void CommercialPackProjection_RemainsSideEffectFree()
    {
        var service = ReadRepositoryFile("src", "CropQc.Data", "CommercialPackAllocationService.cs");

        Assert.DoesNotContain("SaveChanges", service);
        Assert.DoesNotContain("RoomInventory", service);
        Assert.DoesNotContain("BinsRunEntry", service);
    }

    private static RunProjectionDetailViewModel Projection()
    {
        var sources = new[]
        {
            Source(1, "North block", "Apple", 880m, 880m, 660m, 220m),
            Source(2, "South block", "Apple", 880m, 880m, 660m, 220m)
        };
        sources[0].JointSizeGradeBasisFruitCount = 9;
        sources[1].JointSizeGradeBasisFruitCount = 9;
        sources[0].GradeResults = [Grade("Extra Fancy", 10m), Grade("Fancy", 6.5m)];
        sources[1].GradeResults = [Grade("Extra Fancy", 10.5m), Grade("Fancy", 6m)];
        return new RunProjectionDetailViewModel
        {
            Id = 42,
            Name = "Meeting run",
            FacilityCode = "WP",
            ProjectionMode = RunProjectionModes.Preharvest,
            PlannedRunDate = new DateOnly(2026, 7, 30),
            CropYear = 2026,
            Status = RunProjectionStatuses.Ready,
            Creator = "Planner",
            UpdatedAt = DateTimeOffset.Parse("2026-07-24T17:00:00Z"),
            ApplePoundsPerBin = 880m,
            PearPoundsPerBin = 920m,
            StandardBoxWeightPounds = 40m,
            TotalPlannedBins = 2,
            TotalProjectedPounds = 1760m,
            TotalPackedProjectedPounds = 1320m,
            TotalPackedProjectedBoxes = 33m,
            TotalCullProjectedPounds = 440m,
            Sources = sources,
            PackResults = [PackResult([new("Extra Fancy", 820m), new("Fancy", 380m)], jointBasis: 18)],
            PackAssignedPounds = 1200m,
            PackUnallocatedPounds = 120m,
            PackPlanName = "Saved standard plan",
            PackCalculationVersion = "2.0"
        };
    }

    private static RunProjectionSourceViewModel Source(
        long id,
        string block,
        string commodity,
        decimal poundsPerBin,
        decimal gross,
        decimal packed,
        decimal cull) =>
        new()
        {
            Id = id,
            SourceType = RunProjectionSourceTypes.FieldSample,
            SourceLabel = block,
            Block = block,
            Orchard = "WP ORCHARD",
            Grower = "WP ORCHARD",
            GrowerNumber = "1080",
            Variety = commodity == "Pear" ? "Bartlett" : "Gala",
            Commodity = commodity,
            PlannedBins = 1,
            ExpectedPackoutPercent = 75m,
            ExpectedCullPercent = 25m,
            PoundsPerBin = poundsPerBin,
            ProjectedPounds = gross,
            PackedProjectedPounds = packed,
            PackedProjectedBoxes = packed / 40m,
            CullProjectedPounds = cull,
            QcBasis = "Saved Field Sample snapshot",
            SelectedQcSourceType = RunProjectionQcSourceTypes.FieldSample
        };

    private static RunProjectionGradeResultViewModel Grade(string grade, decimal packedBoxes) =>
        new(grade, 10, 50m, packedBoxes / 0.75m, 0, packedBoxes, 0, 0m, 0);

    private static RunProjectionPackResultViewModel PackResult(
        IReadOnlyList<RunProjectionPackGradeViewModel> grades,
        int jointBasis) =>
        new(
            1,
            "80",
            "80 Count",
            "Apple",
            CommercialPackTypes.Standard,
            40m,
            false,
            CommercialPackMixRules.SingleSize,
            [80],
            1600m,
            1200m,
            400m,
            30m,
            30,
            0m,
            90.91m,
            [
                new(1, "North block", 80, 600m, 800m, 200m),
                new(2, "South block", 80, 600m, 800m, 200m)
            ],
            jointBasis,
            grades,
            null);

    private static CommercialPackPlanSnapshot Plan(params CommercialPackDefinitionSnapshot[] packs) =>
        new(1, "PLAN", "Plan", "Apple", CommercialPackPlanTypes.Standard, 2026, packs);

    private static CommercialPackDefinitionSnapshot Pack(string code, int size, decimal weight) =>
        new(
            1,
            code,
            $"{code} Count",
            "Apple",
            CommercialPackTypes.Standard,
            weight,
            false,
            CommercialPackMixRules.SingleSize,
            1,
            [],
            [new(size, 1, null, null, null)]);

    private static CommercialPackSizePool Pool(long sourceId, int size, decimal packedPounds) =>
        new(sourceId, $"Source {sourceId}", 1, "Apple", size, packedPounds / 0.75m, packedPounds, packedPounds / 3m, []);

    private static string ReadRepositoryFile(params string[] path) =>
        File.ReadAllText(Path.Combine([RepositoryRoot(), .. path]));

    private static string RepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "CropQc.sln")))
        {
            current = current.Parent;
        }
        return current?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
