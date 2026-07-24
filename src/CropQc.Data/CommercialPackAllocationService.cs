using CropQc.Data.Entities;

namespace CropQc.Data;

public sealed record CommercialPackEligibleSizeSnapshot(
    int SizeCategory,
    int Priority,
    decimal? TargetPercent,
    decimal? MinimumPercent,
    decimal? MaximumPercent);

public sealed record CommercialPackDefinitionSnapshot(
    int DefinitionId,
    string Code,
    string DisplayName,
    string Commodity,
    string PackType,
    decimal PackageWeightPounds,
    bool AllowsMixedSizes,
    string MixRule,
    int Priority,
    IReadOnlyList<int> FruitProfileIds,
    IReadOnlyList<CommercialPackEligibleSizeSnapshot> EligibleSizes);

public sealed record CommercialPackPlanSnapshot(
    int PlanId,
    string Code,
    string DisplayName,
    string Commodity,
    string PlanType,
    int CropYear,
    IReadOnlyList<CommercialPackDefinitionSnapshot> Packs);

public sealed record CommercialPackJointGradeCount(string GradeCode, int Count);
public sealed record CommercialPackJointSizeGradeSnapshot(int SizeCategory, string GradeCode, int Count);

public sealed record CommercialPackSizePool(
    long SourceId,
    string SourceLabel,
    int FruitProfileId,
    string Commodity,
    int SizeCategory,
    decimal GrossPounds,
    decimal PackedPounds,
    decimal CullPounds,
    IReadOnlyList<CommercialPackJointGradeCount> JointGrades);

public sealed record CommercialPackContribution(
    long SourceId,
    string SourceLabel,
    int SizeCategory,
    decimal AssignedPounds,
    decimal GrossPounds,
    decimal CullPounds);

public sealed record CommercialPackGradeAllocation(string GradeCode, decimal AssignedPounds);

public sealed record CommercialPackOutput(
    int DefinitionId,
    string PackCode,
    string PackName,
    string Commodity,
    string PackType,
    decimal PackageWeightPounds,
    bool IsMixedSize,
    string MixRule,
    IReadOnlyList<int> EligibleSizes,
    decimal GrossAssignedPounds,
    decimal AssignedPounds,
    decimal CullPounds,
    decimal UnroundedPacks,
    int RoundedPacks,
    decimal RoundingResidualPounds,
    decimal PercentageOfProjectedPackout,
    IReadOnlyList<CommercialPackContribution> Contributions,
    int JointBasisFruitCount,
    IReadOnlyList<CommercialPackGradeAllocation> GradeAllocations,
    string? GradeWarning);

public sealed record CommercialPackUnallocatedFruit(
    long SourceId,
    string SourceLabel,
    string Commodity,
    int SizeCategory,
    decimal Pounds,
    decimal StandardBoxEquivalents,
    string Reason);

public sealed record CommercialPackAllocationResult(
    string CalculationVersion,
    decimal TotalPackedPoundsAvailable,
    decimal TotalAssignedPounds,
    decimal TotalUnallocatedPounds,
    decimal TotalRoundedPackageWeight,
    decimal RoundingResidualPounds,
    IReadOnlyList<CommercialPackOutput> Packs,
    IReadOnlyList<CommercialPackUnallocatedFruit> Unallocated,
    IReadOnlyList<string> Warnings)
{
    public const string CurrentVersion = "2.0";
    public bool IsComplete => Warnings.Count == 0;
}

public static class CommercialPackAllocationService
{
    public static CommercialPackAllocationResult Allocate(
        CommercialPackPlanSnapshot plan,
        IEnumerable<CommercialPackSizePool> sourcePools,
        decimal standardBoxWeightPounds,
        int minimumJointGradeFruit = 10)
    {
        if (standardBoxWeightPounds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(standardBoxWeightPounds));
        }

        var warnings = new List<string>();
        if (plan.Packs.Count == 0)
        {
            warnings.Add("The selected pack plan has no active commercial pack definitions for this crop year.");
        }
        var pools = sourcePools
            .Where(x => x.PackedPounds > 0)
            .Select((x, order) => new PoolWork(x, order))
            .ToList();
        var totalAvailable = pools.Sum(x => x.RemainingPounds);
        var output = new List<OutputWork>();

        foreach (var pack in plan.Packs.OrderBy(x => x.Priority).ThenBy(x => x.Code, StringComparer.OrdinalIgnoreCase))
        {
            var configurationError = Validate(pack);
            if (configurationError is not null)
            {
                warnings.Add($"{pack.Code}: {configurationError}");
                continue;
            }

            var applicable = pools
                .Where(x => x.RemainingPounds > 0
                    && x.Pool.Commodity.Equals(pack.Commodity, StringComparison.OrdinalIgnoreCase)
                    && (pack.FruitProfileIds.Count == 0 || pack.FruitProfileIds.Contains(x.Pool.FruitProfileId))
                    && pack.EligibleSizes.Any(size => size.SizeCategory == x.Pool.SizeCategory))
                .ToList();
            if (applicable.Count == 0) continue;

            var requestedBySize = RequestedPoundsBySize(pack, applicable, warnings);
            if (requestedBySize.Count == 0) continue;

            var contributions = new List<CommercialPackContribution>();
            foreach (var request in requestedBySize.OrderBy(x => EligiblePriority(pack, x.Key)).ThenBy(x => x.Key))
            {
                Consume(applicable.Where(x => x.Pool.SizeCategory == request.Key).ToList(), request.Value, contributions);
            }

            var assigned = contributions.Sum(x => x.AssignedPounds);
            if (assigned <= 0) continue;
            var gradeAllocations = AllocateGrades(contributions, pools, out var jointBasis, out var missingJointBasis);
            string? gradeWarning = null;
            if (missingJointBasis)
            {
                gradeWarning = "Grade by pack is incomplete because some assigned fruit lacks both size and grade.";
            }
            else if (jointBasis < minimumJointGradeFruit)
            {
                gradeWarning = $"Sparse grade-by-pack basis: {jointBasis} jointly sized and graded fruit.";
            }

            output.Add(new OutputWork(
                pack,
                assigned,
                contributions,
                jointBasis,
                gradeAllocations,
                gradeWarning));
        }

        AllocateCompletePackCounts(output);
        var totalAssigned = output.Sum(x => x.AssignedPounds);
        var unallocated = pools
            .Where(x => x.RemainingPounds > 0)
            .OrderBy(x => x.Order)
            .Select(x => new CommercialPackUnallocatedFruit(
                x.Pool.SourceId,
                x.Pool.SourceLabel,
                x.Pool.Commodity,
                x.Pool.SizeCategory,
                x.RemainingPounds,
                x.RemainingPounds / standardBoxWeightPounds,
                "No active pack mapping in the selected plan, or configured mix limits left this fruit unused."))
            .ToList();
        var totalRoundedWeight = output.Sum(x => x.RoundedPacks * x.Pack.PackageWeightPounds);
        return new CommercialPackAllocationResult(
            CommercialPackAllocationResult.CurrentVersion,
            totalAvailable,
            totalAssigned,
            unallocated.Sum(x => x.Pounds),
            totalRoundedWeight,
            totalAssigned - totalRoundedWeight,
            output.Select(x => x.ToResult(totalAvailable)).ToList(),
            unallocated,
            warnings);
    }

    private static string? Validate(CommercialPackDefinitionSnapshot pack)
    {
        if (pack.PackageWeightPounds <= 0) return "Package weight must be greater than zero.";
        if (pack.EligibleSizes.Count == 0) return "At least one eligible size is required.";
        if (!CommercialPackMixRules.All.Contains(pack.MixRule)) return "A supported allocation rule is required.";
        if (!pack.AllowsMixedSizes && pack.EligibleSizes.Count != 1)
        {
            return "A single-size pack must have exactly one eligible size.";
        }
        if (pack.AllowsMixedSizes && pack.EligibleSizes.Count < 2)
        {
            return "A mixed-size pack must have at least two eligible sizes.";
        }
        if (pack.PackType == CommercialPackTypes.Euro && pack.EligibleSizes.Count != 2)
        {
            return "A Euro pack must have exactly two configured eligible sizes.";
        }
        if (!pack.AllowsMixedSizes && pack.MixRule != CommercialPackMixRules.SingleSize)
        {
            return "A single-size pack must use the SingleSize allocation rule.";
        }
        if (pack.MixRule == CommercialPackMixRules.SingleSize && pack.AllowsMixedSizes)
        {
            return "SingleSize cannot be used for a mixed-size pack.";
        }
        if (pack.MixRule == CommercialPackMixRules.FixedPercentage)
        {
            if (pack.EligibleSizes.Any(x => x.TargetPercent is null or <= 0)) return "Every eligible size needs a target percentage.";
            if (decimal.Round(pack.EligibleSizes.Sum(x => x.TargetPercent!.Value), 4) != 100m) return "Fixed target percentages must total 100%.";
            if (pack.EligibleSizes.Any(x =>
                    x.TargetPercent < (x.MinimumPercent ?? 0m)
                    || x.TargetPercent > (x.MaximumPercent ?? 100m)))
            {
                return "A target percentage is outside its configured minimum or maximum.";
            }
        }
        if (pack.EligibleSizes.Any(x => x.MinimumPercent is < 0 or > 100
                || x.MaximumPercent is < 0 or > 100
                || x.MinimumPercent is not null && x.MaximumPercent is not null && x.MinimumPercent > x.MaximumPercent))
        {
            return "Eligible-size minimum and maximum percentages are invalid.";
        }
        if (pack.MixRule != CommercialPackMixRules.FixedPercentage
            && pack.EligibleSizes.Any(x => x.MinimumPercent is not null || x.MaximumPercent is not null)
            && pack.EligibleSizes.Count != 2)
        {
            return "Minimum/maximum mix limits currently require exactly two eligible sizes.";
        }
        return null;
    }

    private static Dictionary<int, decimal> RequestedPoundsBySize(
        CommercialPackDefinitionSnapshot pack,
        IReadOnlyList<PoolWork> applicable,
        ICollection<string> warnings)
    {
        var available = applicable
            .GroupBy(x => x.Pool.SizeCategory)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.RemainingPounds));
        if (pack.MixRule == CommercialPackMixRules.FixedPercentage)
        {
            var total = pack.EligibleSizes
                .Where(x => available.ContainsKey(x.SizeCategory))
                .Min(x => available[x.SizeCategory] / (x.TargetPercent!.Value / 100m));
            if (total <= 0) return [];
            return pack.EligibleSizes.ToDictionary(
                x => x.SizeCategory,
                x => total * (x.TargetPercent!.Value / 100m));
        }
        if (pack.EligibleSizes.Count == 2
            && pack.EligibleSizes.Any(x => x.MinimumPercent is not null || x.MaximumPercent is not null))
        {
            return RequestedConstrainedTwoSize(pack, available, warnings);
        }
        if (pack.MixRule is CommercialPackMixRules.SingleSize
            or CommercialPackMixRules.AnyMixture
            or CommercialPackMixRules.PrimaryThenSupplement
            or CommercialPackMixRules.OptimizeUse)
        {
            return available;
        }
        warnings.Add($"{pack.Code}: allocation rule is missing or ambiguous.");
        return [];
    }

    private static Dictionary<int, decimal> RequestedConstrainedTwoSize(
        CommercialPackDefinitionSnapshot pack,
        IReadOnlyDictionary<int, decimal> available,
        ICollection<string> warnings)
    {
        var sizes = pack.EligibleSizes.OrderBy(x => x.Priority).ThenBy(x => x.SizeCategory).ToList();
        var first = sizes[0];
        var second = sizes[1];
        var firstAvailable = available.GetValueOrDefault(first.SizeCategory);
        var secondAvailable = available.GetValueOrDefault(second.SizeCategory);
        var lowerFirst = Math.Max(
            (first.MinimumPercent ?? 0m) / 100m,
            1m - (second.MaximumPercent ?? 100m) / 100m);
        var upperFirst = Math.Min(
            (first.MaximumPercent ?? 100m) / 100m,
            1m - (second.MinimumPercent ?? 0m) / 100m);
        if (lowerFirst > upperFirst)
        {
            warnings.Add($"{pack.Code}: configured minimum/maximum mix limits do not overlap.");
            return [];
        }

        var total = firstAvailable + secondAvailable;
        if (lowerFirst > 0m) total = Math.Min(total, firstAvailable / lowerFirst);
        if (upperFirst < 1m) total = Math.Min(total, secondAvailable / (1m - upperFirst));
        if (total <= 0m) return [];

        var minimumFirstPounds = Math.Max(lowerFirst * total, total - secondAvailable);
        var maximumFirstPounds = Math.Min(upperFirst * total, firstAvailable);
        if (minimumFirstPounds > maximumFirstPounds + 0.00000001m)
        {
            warnings.Add($"{pack.Code}: available projected fruit cannot satisfy the configured mix limits.");
            return [];
        }

        decimal firstPounds;
        if (pack.MixRule == CommercialPackMixRules.PrimaryThenSupplement)
        {
            firstPounds = maximumFirstPounds;
        }
        else
        {
            var proportional = total == 0m ? 0m : total * firstAvailable / (firstAvailable + secondAvailable);
            firstPounds = Math.Clamp(proportional, minimumFirstPounds, maximumFirstPounds);
        }
        return new Dictionary<int, decimal>
        {
            [first.SizeCategory] = firstPounds,
            [second.SizeCategory] = total - firstPounds
        };
    }

    private static int EligiblePriority(CommercialPackDefinitionSnapshot pack, int sizeCategory) =>
        pack.EligibleSizes.Single(x => x.SizeCategory == sizeCategory).Priority;

    private static void Consume(
        IReadOnlyList<PoolWork> sizePools,
        decimal requested,
        ICollection<CommercialPackContribution> contributions)
    {
        var available = sizePools.Sum(x => x.RemainingPounds);
        var amount = Math.Min(requested, available);
        if (amount <= 0) return;
        decimal assigned = 0;
        for (var index = 0; index < sizePools.Count; index++)
        {
            var pool = sizePools[index];
            var share = index == sizePools.Count - 1
                ? amount - assigned
                : decimal.Round(amount * pool.RemainingPounds / available, 8);
            share = Math.Min(share, pool.RemainingPounds);
            pool.RemainingPounds -= share;
            assigned += share;
            contributions.Add(new CommercialPackContribution(
                pool.Pool.SourceId,
                pool.Pool.SourceLabel,
                pool.Pool.SizeCategory,
                share,
                pool.Pool.PackedPounds <= 0 ? 0 : pool.Pool.GrossPounds * share / pool.Pool.PackedPounds,
                pool.Pool.PackedPounds <= 0 ? 0 : pool.Pool.CullPounds * share / pool.Pool.PackedPounds));
        }
    }

    private static IReadOnlyList<CommercialPackGradeAllocation> AllocateGrades(
        IReadOnlyList<CommercialPackContribution> contributions,
        IReadOnlyList<PoolWork> pools,
        out int jointBasis,
        out bool missingJointBasis)
    {
        jointBasis = 0;
        missingJointBasis = false;
        var grades = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var contribution in contributions)
        {
            var pool = pools.Single(x => x.Pool.SourceId == contribution.SourceId
                && x.Pool.SizeCategory == contribution.SizeCategory);
            var total = pool.Pool.JointGrades.Sum(x => x.Count);
            if (total == 0)
            {
                missingJointBasis = true;
                continue;
            }
            jointBasis += total;
            foreach (var grade in pool.Pool.JointGrades)
            {
                grades[grade.GradeCode] = grades.GetValueOrDefault(grade.GradeCode)
                    + contribution.AssignedPounds * grade.Count / total;
            }
        }
        return grades.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => new CommercialPackGradeAllocation(x.Key, x.Value))
            .ToList();
    }

    private static void AllocateCompletePackCounts(IReadOnlyList<OutputWork> output)
    {
        foreach (var item in output) item.RoundedPacks = (int)decimal.Floor(item.UnroundedPacks);
    }

    private sealed class PoolWork(CommercialPackSizePool pool, int order)
    {
        public CommercialPackSizePool Pool { get; } = pool;
        public int Order { get; } = order;
        public decimal RemainingPounds { get; set; } = pool.PackedPounds;
    }

    private sealed class OutputWork(
        CommercialPackDefinitionSnapshot pack,
        decimal assignedPounds,
        IReadOnlyList<CommercialPackContribution> contributions,
        int jointBasis,
        IReadOnlyList<CommercialPackGradeAllocation> gradeAllocations,
        string? gradeWarning)
    {
        public CommercialPackDefinitionSnapshot Pack { get; } = pack;
        public decimal AssignedPounds { get; } = assignedPounds;
        public decimal UnroundedPacks => AssignedPounds / Pack.PackageWeightPounds;
        public int RoundedPacks { get; set; }

        public CommercialPackOutput ToResult(decimal totalPackedAvailable) => new(
            Pack.DefinitionId,
            Pack.Code,
            Pack.DisplayName,
            Pack.Commodity,
            Pack.PackType,
            Pack.PackageWeightPounds,
            Pack.AllowsMixedSizes,
            Pack.MixRule,
            Pack.EligibleSizes.OrderBy(x => x.Priority).Select(x => x.SizeCategory).ToList(),
            contributions.Sum(x => x.GrossPounds),
            AssignedPounds,
            contributions.Sum(x => x.CullPounds),
            UnroundedPacks,
            RoundedPacks,
            AssignedPounds - RoundedPacks * Pack.PackageWeightPounds,
            totalPackedAvailable <= 0 ? 0 : AssignedPounds / totalPackedAvailable * 100m,
            contributions,
            jointBasis,
            gradeAllocations,
            gradeWarning);
    }
}
