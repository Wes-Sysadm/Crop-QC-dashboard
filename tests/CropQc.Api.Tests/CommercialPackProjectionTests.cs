using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;

namespace CropQc.Api.Tests;

public sealed class CommercialPackProjectionTests
{
    [Fact]
    public void StandardSize80_MapsToConfigured80Pack()
    {
        var result = Allocate(Plan(Pack("80", 80)), Pool(1, 80, 400));

        Assert.Equal("80", Assert.Single(result.Packs).PackCode);
        Assert.Equal(400m, result.TotalAssignedPounds);
    }

    [Fact]
    public void StandardSize90_MapsToConfigured90Pack()
    {
        var result = Allocate(Plan(Pack("90", 90)), Pool(1, 90, 360));

        Assert.Equal("90", Assert.Single(result.Packs).PackCode);
        Assert.Equal(9m, result.Packs[0].UnroundedPacks);
    }

    [Fact]
    public void StandardPacks_DoNotMixDifferentFruitSizes()
    {
        var result = Allocate(Plan(Pack("80", 80), Pack("90", 90)), Pool(1, 80, 200), Pool(1, 90, 240));

        Assert.All(result.Packs, pack => Assert.Single(pack.EligibleSizes));
        Assert.All(result.Packs, pack => Assert.Single(pack.Contributions.Select(x => x.SizeCategory).Distinct()));
    }

    [Fact]
    public void EuroPack_UsesItsTwoConfiguredSizes()
    {
        var euro = MixedPack("EURO-80-90", CommercialPackMixRules.AnyMixture, Size(80), Size(90));
        var result = Allocate(Plan(euro), Pool(1, 80, 200), Pool(1, 90, 240));

        Assert.Equal([80, 90], Assert.Single(result.Packs).Contributions.Select(x => x.SizeCategory).Distinct().Order().ToArray());
    }

    [Fact]
    public void EuroPack_DoesNotAcceptAnUnconfiguredSizeCombination()
    {
        var result = Allocate(
            Plan(MixedPack("EURO-80-90", CommercialPackMixRules.AnyMixture, Size(80), Size(90))),
            Pool(1, 80, 200),
            Pool(1, 100, 120));

        Assert.Equal(200m, result.TotalAssignedPounds);
        Assert.Equal(120m, Assert.Single(result.Unallocated).Pounds);
        Assert.Equal(100, result.Unallocated[0].SizeCategory);
    }

    [Fact]
    public void PriorityAllocation_DoesNotCountFruitInBothStandardAndEuroPacks()
    {
        var euro = MixedPack("EURO", CommercialPackMixRules.AnyMixture, Size(80), Size(90), priority: 0);
        var standard = Pack("80", 80, priority: 10);
        var result = Allocate(Plan(euro, standard), Pool(1, 80, 200), Pool(1, 90, 200));

        Assert.Equal(400m, result.TotalAssignedPounds);
        Assert.Equal(result.TotalPackedPoundsAvailable, result.TotalAssignedPounds + result.TotalUnallocatedPounds);
        Assert.DoesNotContain(result.Packs, x => x.PackCode == "80");
    }

    [Fact]
    public void PackPlanSelection_ChangesAllocationWithoutChangingGrowthPool()
    {
        var pools = new[] { Pool(1, 80, 200), Pool(1, 90, 200) };
        var standard = Allocate(Plan(Pack("80", 80), Pack("90", 90)), pools);
        var euro = Allocate(Plan(MixedPack("EURO", CommercialPackMixRules.AnyMixture, Size(80), Size(90))), pools);

        Assert.Equal(standard.TotalPackedPoundsAvailable, euro.TotalPackedPoundsAvailable);
        Assert.Equal(["80", "90"], standard.Packs.Select(x => x.PackCode).ToArray());
        Assert.Equal("EURO", Assert.Single(euro.Packs).PackCode);
        Assert.Equal(200m, pools[0].PackedPounds);
    }

    [Fact]
    public void StandardScenario_ReconcilesInternally()
    {
        var result = Allocate(Plan(Pack("80", 80), Pack("90", 90)), Pool(1, 80, 200), Pool(2, 90, 240));

        Assert.Empty(result.Warnings);
        Assert.Equal(result.TotalPackedPoundsAvailable, result.TotalAssignedPounds + result.TotalUnallocatedPounds);
    }

    [Fact]
    public void EuroScenario_ReconcilesInternally()
    {
        var result = Allocate(
            Plan(MixedPack("EURO", CommercialPackMixRules.FixedPercentage, Size(80, target: 50), Size(90, target: 50))),
            Pool(1, 80, 200),
            Pool(2, 90, 160));

        Assert.Equal(result.TotalPackedPoundsAvailable, result.TotalAssignedPounds + result.TotalUnallocatedPounds);
        Assert.Equal(320m, result.TotalAssignedPounds);
    }

    [Fact]
    public void MixedScenario_FollowsConfiguredPackPriorities()
    {
        var plan = Plan(
            MixedPack("EURO-FIRST", CommercialPackMixRules.FixedPercentage, Size(80, target: 50), Size(90, target: 50), priority: 0),
            Pack("80-REMAINDER", 80, priority: 5));
        var result = Allocate(plan, Pool(1, 80, 300), Pool(1, 90, 100));

        Assert.Equal(200m, result.Packs.Single(x => x.PackCode == "EURO-FIRST").AssignedPounds);
        Assert.Equal(200m, result.Packs.Single(x => x.PackCode == "80-REMAINDER").AssignedPounds);
    }

    [Fact]
    public void EuroFixedPercentages_FollowConfiguration()
    {
        var result = Allocate(
            Plan(MixedPack("EURO-60-40", CommercialPackMixRules.FixedPercentage, Size(80, target: 60), Size(90, target: 40))),
            Pool(1, 80, 100),
            Pool(1, 90, 100));
        var contributions = Assert.Single(result.Packs).Contributions.GroupBy(x => x.SizeCategory)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.AssignedPounds));

        Assert.Equal(100m, contributions[80]);
        Assert.Equal(66.6667m, contributions[90], 4);
    }

    [Fact]
    public void MissingEuroMixRule_ProducesWarningInsteadOfProjection()
    {
        var invalid = MixedPack("EURO", "NotConfigured", Size(80), Size(90));
        var result = Allocate(Plan(invalid), Pool(1, 80, 100), Pool(1, 90, 100));

        Assert.Empty(result.Packs);
        Assert.Contains(result.Warnings, x => x.Contains("supported allocation rule", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LeftoverFruit_IsReportedAsUnallocated()
    {
        var result = Allocate(Plan(Pack("80", 80)), Pool(1, 80, 100), Pool(1, 100, 60));

        Assert.Equal(60m, Assert.Single(result.Unallocated).Pounds);
        Assert.Contains("No active pack mapping", result.Unallocated[0].Reason);
    }

    [Fact]
    public void UnallocatedFruit_IsNotSilentlyTreatedAsCull()
    {
        var pool = Pool(1, 100, 60, cull: 15);
        var result = Allocate(Plan(Pack("80", 80)), pool);

        Assert.Equal(60m, result.TotalUnallocatedPounds);
        Assert.Equal(15m, pool.CullPounds);
        Assert.NotEqual(result.TotalUnallocatedPounds, pool.CullPounds);
        Assert.Empty(result.Packs);
    }

    [Fact]
    public void NonFortyPoundPackWeight_IsUsed()
    {
        var result = Allocate(Plan(Pack("EURO-20", 80, weight: 20)), Pool(1, 80, 100));

        Assert.Equal(5m, Assert.Single(result.Packs).UnroundedPacks);
        Assert.Equal(5, result.Packs[0].RoundedPacks);
    }

    [Fact]
    public void PackRounding_IsDeterministic()
    {
        var plan = Plan(Pack("80", 80, weight: 37), Pack("90", 90, weight: 22));
        var pools = new[] { Pool(1, 80, 100), Pool(2, 90, 100) };

        var first = Allocate(plan, pools);
        var second = Allocate(plan, pools);

        Assert.Equal(first.Packs.Select(x => x.RoundedPacks), second.Packs.Select(x => x.RoundedPacks));
        Assert.Equal(first.RoundingResidualPounds, second.RoundingResidualPounds);
    }

    [Fact]
    public void SourceContributions_ReconcileToPackTotals()
    {
        var result = Allocate(Plan(Pack("80", 80)), Pool(1, 80, 120), Pool(2, 80, 280));
        var pack = Assert.Single(result.Packs);

        Assert.Equal(pack.AssignedPounds, pack.Contributions.Sum(x => x.AssignedPounds));
        Assert.Equal([1L, 2L], pack.Contributions.Select(x => x.SourceId).Order().ToArray());
    }

    [Fact]
    public void GrossFruit_ReconcilesAcrossAssignedUnallocatedAndCull()
    {
        var pools = new[] { Pool(1, 80, 80, gross: 100, cull: 20), Pool(2, 100, 40, gross: 50, cull: 10) };
        var result = Allocate(Plan(Pack("80", 80)), pools);

        Assert.Equal(
            pools.Sum(x => x.GrossPounds),
            result.TotalAssignedPounds + result.TotalUnallocatedPounds + pools.Sum(x => x.CullPounds));
        Assert.Equal(100m, Assert.Single(result.Packs).GrossAssignedPounds);
    }

    [Fact]
    public void PlannerView_KeepsFruitSizingAndCommercialPackGraphsSeparate()
    {
        var view = ReadRepositoryFile("src", "CropQc.Web", "Views", "BinsRun", "Index.cshtml");

        Assert.Contains("Projected Fruit Sizing by Calculated Fruit Size", view);
        Assert.Contains("Projected Packed Boxes by Pack", view);
        Assert.NotEqual(view.IndexOf("Projected Fruit Sizing", StringComparison.Ordinal), view.IndexOf("Projected Packed Boxes by Pack", StringComparison.Ordinal));
    }

    [Fact]
    public void PackGraph_UsesConfiguredPackNames()
    {
        var view = ReadRepositoryFile("src", "CropQc.Web", "Views", "BinsRun", "Index.cshtml");

        Assert.Contains("@pack.PackName", view);
        Assert.Contains("aria-label=\"Projected commercial packs by configured pack\"", view);
    }

    [Fact]
    public void EuroDetail_IdentifiesSizesRuleContributionsAndLeftover()
    {
        var view = ReadRepositoryFile("src", "CropQc.Web", "Views", "BinsRun", "Index.cshtml");

        Assert.Contains("pack.EligibleSizes", view);
        Assert.Contains("pack.MixRule", view);
        Assert.Contains("pack.Contributions", view);
        Assert.Contains("Leftover eligible packed fruit", view);
    }

    [Fact]
    public void GradeByPack_UsesOnlyJointSizeGradeRows()
    {
        var pool = Pool(1, 80, 100, grades: [new("Fancy", 3), new("US1", 1)]);
        var result = Allocate(Plan(Pack("80", 80)), pool);
        var grades = Assert.Single(result.Packs).GradeAllocations.ToDictionary(x => x.GradeCode, x => x.AssignedPounds);

        Assert.Equal(75m, grades["Fancy"]);
        Assert.Equal(25m, grades["US1"]);
    }

    [Fact]
    public void SparseJointGradeBasis_ProducesWarning()
    {
        var result = Allocate(Plan(Pack("80", 80)), Pool(1, 80, 100, grades: [new("Fancy", 4)]));

        Assert.Contains("Sparse grade-by-pack basis", Assert.Single(result.Packs).GradeWarning);
    }

    [Fact]
    public void SavedProjectionSnapshot_RetainsHistoricalPackConfiguration()
    {
        var historical = Plan(Pack("OLD-80", 80, weight: 40));
        var snapshot = JsonSerializer.Serialize(historical);
        var current = Plan(Pack("NEW-80", 80, weight: 35));

        var restored = JsonSerializer.Deserialize<CommercialPackPlanSnapshot>(snapshot)!;
        Assert.Equal("OLD-80", Assert.Single(restored.Packs).Code);
        Assert.Equal(40m, restored.Packs[0].PackageWeightPounds);
        Assert.NotEqual(current.Packs[0].Code, restored.Packs[0].Code);
    }

    [Fact]
    public void ExplicitRecalculation_UsesCurrentConfiguration()
    {
        var pools = new[] { Pool(1, 80, 200) };
        var saved = Allocate(Plan(Pack("OLD", 80, weight: 40)), pools);
        var recalculated = Allocate(Plan(Pack("CURRENT", 80, weight: 20)), pools);

        Assert.Equal(5m, saved.Packs[0].UnroundedPacks);
        Assert.Equal(10m, recalculated.Packs[0].UnroundedPacks);
        Assert.Equal("CURRENT", recalculated.Packs[0].PackCode);
    }

    [Fact]
    public void PackPlanChanges_DoNotAlterInventoryOrSourcePools()
    {
        var pool = Pool(44, 80, 200);
        var before = JsonSerializer.Serialize(pool);

        _ = Allocate(Plan(Pack("80", 80)), pool);

        Assert.Equal(before, JsonSerializer.Serialize(pool));
        Assert.Equal(44, pool.SourceId);
    }

    [Fact]
    public void PackProjectionService_DoesNotCreateFinishedGoodsInventory()
    {
        var source = ReadRepositoryFile("src", "CropQc.Data", "CommercialPackAllocationService.cs");

        Assert.DoesNotContain("RoomInventory", source);
        Assert.DoesNotContain("InventoryAdjustment", source);
        Assert.DoesNotContain("SaveChanges", source);
    }

    private static CommercialPackAllocationResult Allocate(
        CommercialPackPlanSnapshot plan,
        params CommercialPackSizePool[] pools) =>
        CommercialPackAllocationService.Allocate(plan, pools, 40m);

    private static CommercialPackPlanSnapshot Plan(params CommercialPackDefinitionSnapshot[] packs) =>
        new(1, "PLAN", "Plan", "Apple", CommercialPackPlanTypes.Mixed, 2026, packs);

    private static CommercialPackDefinitionSnapshot Pack(
        string code,
        int size,
        int priority = 0,
        decimal weight = 40m) =>
        new(code.GetHashCode(), code, code, "Apple", CommercialPackTypes.Standard, weight, false,
            CommercialPackMixRules.SingleSize, priority, [], [Size(size)]);

    private static CommercialPackDefinitionSnapshot MixedPack(
        string code,
        string rule,
        CommercialPackEligibleSizeSnapshot first,
        CommercialPackEligibleSizeSnapshot second,
        int priority = 0,
        decimal weight = 40m) =>
        new(code.GetHashCode(), code, code, "Apple", CommercialPackTypes.Euro, weight, true,
            rule, priority, [], [first, second]);

    private static CommercialPackEligibleSizeSnapshot Size(
        int size,
        int priority = 0,
        decimal? target = null,
        decimal? minimum = null,
        decimal? maximum = null) =>
        new(size, priority, target, minimum, maximum);

    private static CommercialPackSizePool Pool(
        long sourceId,
        int size,
        decimal packed,
        decimal? gross = null,
        decimal cull = 0m,
        IReadOnlyList<CommercialPackJointGradeCount>? grades = null) =>
        new(sourceId, $"Source {sourceId}", 1, "Apple", size, gross ?? packed + cull, packed, cull, grades ?? []);

    private static string ReadRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(Path.Combine(parts));
    }
}
