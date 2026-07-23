namespace CropQc.Data;

public sealed record RunProjectionSizeObservation(int SizeCategory);
public sealed record RunProjectionGradeObservation(string GradeCode);

public sealed record RunProjectionDistributionAllocation(
    string Key,
    int SampleCount,
    decimal Percentage,
    decimal GrossBoxes,
    int RoundedGrossBoxes,
    decimal PackedBoxes,
    int RoundedPackedBoxes,
    decimal CullBoxes,
    int RoundedCullBoxes);

public sealed record RunProjectionSizeAllocation(
    string Commodity,
    int SizeCategory,
    int SampleCount,
    decimal Percentage,
    decimal UnroundedProjectedBoxes,
    int RoundedProjectedBoxes,
    decimal PackedProjectedBoxes,
    int RoundedPackedProjectedBoxes,
    decimal CullProjectedBoxes,
    int RoundedCullProjectedBoxes);

public sealed record RunProjectionLineCalculation(
    string Commodity,
    int PlannedBins,
    decimal PoundsPerBin,
    decimal StandardBoxWeightPounds,
    decimal ProjectedPounds,
    decimal ProjectedBoxes,
    int RoundedProjectedBoxes,
    decimal? ExpectedPackoutPercent,
    decimal? ExpectedCullPercent,
    decimal PackedProjectedPounds,
    decimal PackedProjectedBoxes,
    int RoundedPackedProjectedBoxes,
    decimal CullProjectedPounds,
    decimal CullProjectedBoxes,
    int RoundedCullProjectedBoxes,
    IReadOnlyList<RunProjectionSizeAllocation> SizeAllocations,
    IReadOnlyList<RunProjectionDistributionAllocation> GradeAllocations,
    int SizeBasisFruitCount,
    int GradeBasisFruitCount,
    int JointSizeGradeBasisFruitCount,
    string? Warning);

public static class RunProjectionCalculationService
{
    public const decimal DefaultApplePoundsPerBin = 880m;
    public const decimal DefaultPearPoundsPerBin = 920m;
    public const decimal DefaultStandardBoxWeightPounds = 40m;
    public const decimal DefaultExpectedPackoutPercent = 85m;
    public const string CurrentCalculationVersion = "2.0";

    public static RunProjectionLineCalculation Calculate(
        string? fruitType,
        int plannedBins,
        decimal applePoundsPerBin,
        decimal pearPoundsPerBin,
        decimal standardBoxWeightPounds,
        IEnumerable<RunProjectionSizeObservation> observations) =>
        Calculate(fruitType, plannedBins, applePoundsPerBin, pearPoundsPerBin, standardBoxWeightPounds, null, observations, []);

    public static RunProjectionLineCalculation Calculate(
        string? fruitType,
        int plannedBins,
        decimal applePoundsPerBin,
        decimal pearPoundsPerBin,
        decimal standardBoxWeightPounds,
        decimal? expectedPackoutPercent,
        IEnumerable<RunProjectionSizeObservation> sizeObservations,
        IEnumerable<RunProjectionGradeObservation> gradeObservations,
        int jointSizeGradeBasisFruitCount = 0,
        int minimumDistributionFruit = 10)
    {
        if (plannedBins < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(plannedBins), "Planned bins cannot be negative.");
        }

        if (applePoundsPerBin <= 0 || pearPoundsPerBin <= 0 || standardBoxWeightPounds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(standardBoxWeightPounds), "Projection weight assumptions must be positive.");
        }

        if (expectedPackoutPercent is < 0 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedPackoutPercent), "Expected Packout % must be between 0 and 100.");
        }

        var commodity = NormalizeCommodity(fruitType);
        var poundsPerBin = commodity switch
        {
            "Apple" => applePoundsPerBin,
            "Pear" => pearPoundsPerBin,
            _ => 0m
        };
        var sizeGroups = sizeObservations
            .GroupBy(x => x.SizeCategory)
            .OrderBy(x => x.Key)
            .Select(x => new DistributionInput(x.Key.ToString(), x.Count()))
            .ToList();
        var gradeGroups = gradeObservations
            .Where(x => !string.IsNullOrWhiteSpace(x.GradeCode))
            .GroupBy(x => x.GradeCode.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => new DistributionInput(x.Key, x.Count()))
            .ToList();

        if (poundsPerBin <= 0)
        {
            return EmptyCalculation(
                commodity,
                plannedBins,
                standardBoxWeightPounds,
                expectedPackoutPercent,
                sizeGroups.Sum(x => x.Count),
                gradeGroups.Sum(x => x.Count),
                jointSizeGradeBasisFruitCount,
                "Commodity is not configured as Apple or Pear. Resolve the fruit profile before calculating boxes.");
        }

        var grossPounds = plannedBins * poundsPerBin;
        var grossBoxes = grossPounds / standardBoxWeightPounds;
        var roundedGross = RoundPlanningBoxes(grossBoxes);
        var rate = expectedPackoutPercent / 100m;
        decimal? cullRate = rate is null ? null : 1m - rate.Value;
        var packedPounds = rate is null ? 0m : grossPounds * rate.Value;
        var packedBoxes = rate is null ? 0m : grossBoxes * rate.Value;
        var roundedPacked = rate is null ? 0 : RoundPlanningBoxes(packedBoxes);
        var cullPounds = rate is null ? 0m : grossPounds - packedPounds;
        var cullBoxes = rate is null ? 0m : grossBoxes - packedBoxes;
        var roundedCull = rate is null ? 0 : roundedGross - roundedPacked;

        var sizeAllocations = Allocate(sizeGroups, grossBoxes, roundedGross, rate, roundedPacked, cullRate, roundedCull)
            .Select(x => new RunProjectionSizeAllocation(
                commodity,
                int.Parse(x.Key),
                x.SampleCount,
                x.Percentage,
                x.GrossBoxes,
                x.RoundedGrossBoxes,
                x.PackedBoxes,
                x.RoundedPackedBoxes,
                x.CullBoxes,
                x.RoundedCullBoxes))
            .ToList();
        var gradeAllocations = Allocate(gradeGroups, grossBoxes, roundedGross, rate, roundedPacked, cullRate, roundedCull);
        var warnings = new List<string>();
        if (sizeGroups.Count == 0) warnings.Add("No meaningful calculated size data is available for the selected QC sample.");
        else if (sizeGroups.Sum(x => x.Count) < minimumDistributionFruit) warnings.Add($"Sparse sample: only {sizeGroups.Sum(x => x.Count)} fruit have calculated size data.");
        if (gradeGroups.Count == 0) warnings.Add("Grade breakdown is unavailable because the selected sample has no meaningful grade data.");
        else if (gradeGroups.Sum(x => x.Count) < minimumDistributionFruit) warnings.Add($"Sparse grade sample: only {gradeGroups.Sum(x => x.Count)} fruit have grade data.");
        if (expectedPackoutPercent is null) warnings.Add("Expected Packout % is required before packed and cull output can be calculated.");

        return new RunProjectionLineCalculation(
            commodity,
            plannedBins,
            poundsPerBin,
            standardBoxWeightPounds,
            grossPounds,
            grossBoxes,
            roundedGross,
            expectedPackoutPercent,
            expectedPackoutPercent is null ? null : 100m - expectedPackoutPercent.Value,
            packedPounds,
            packedBoxes,
            roundedPacked,
            cullPounds,
            cullBoxes,
            roundedCull,
            sizeAllocations,
            gradeAllocations,
            sizeGroups.Sum(x => x.Count),
            gradeGroups.Sum(x => x.Count),
            jointSizeGradeBasisFruitCount,
            warnings.Count == 0 ? null : string.Join(" ", warnings));
    }

    public static int RoundPlanningBoxes(decimal boxes) =>
        (int)decimal.Round(boxes, 0, MidpointRounding.AwayFromZero);

    public static string NormalizeCommodity(string? fruitType) =>
        fruitType?.Trim().ToUpperInvariant() switch
        {
            "APPLE" => "Apple",
            "PEAR" => "Pear",
            _ => "Unknown"
        };

    private static IReadOnlyList<RunProjectionDistributionAllocation> Allocate(
        IReadOnlyList<DistributionInput> groups,
        decimal grossBoxes,
        int roundedGross,
        decimal? packedRate,
        int roundedPacked,
        decimal? cullRate,
        int roundedCull)
    {
        if (groups.Count == 0) return [];
        var total = groups.Sum(x => x.Count);
        var work = groups.Select((x, order) =>
        {
            var share = x.Count / (decimal)total;
            return new AllocationWork(
                x.Key,
                order,
                x.Count,
                share,
                grossBoxes * share,
                packedRate is null ? 0m : grossBoxes * share * packedRate.Value,
                cullRate is null ? 0m : grossBoxes * share * cullRate.Value);
        }).ToList();
        AllocateRounded(work, roundedGross, x => x.GrossExact, (x, value) => x.GrossRounded = value);
        AllocateRounded(work, roundedPacked, x => x.PackedExact, (x, value) => x.PackedRounded = value);
        AllocateRounded(work, roundedCull, x => x.CullExact, (x, value) => x.CullRounded = value);
        return work.Select(x => new RunProjectionDistributionAllocation(
            x.Key,
            x.Count,
            decimal.Round(x.Share * 100m, 4),
            x.GrossExact,
            x.GrossRounded,
            x.PackedExact,
            x.PackedRounded,
            x.CullExact,
            x.CullRounded)).ToList();
    }

    private static void AllocateRounded(
        IReadOnlyList<AllocationWork> work,
        int target,
        Func<AllocationWork, decimal> exact,
        Action<AllocationWork, int> assign)
    {
        var floors = work.ToDictionary(x => x, x => (int)decimal.Floor(exact(x)));
        foreach (var item in work) assign(item, floors[item]);
        foreach (var item in work
                     .OrderByDescending(x => exact(x) - floors[x])
                     .ThenBy(x => x.Order)
                     .Take(Math.Max(0, target - floors.Values.Sum())))
        {
            assign(item, floors[item] + 1);
        }
    }

    private static RunProjectionLineCalculation EmptyCalculation(
        string commodity,
        int plannedBins,
        decimal standardBoxWeightPounds,
        decimal? expectedPackoutPercent,
        int sizeBasis,
        int gradeBasis,
        int jointBasis,
        string warning) =>
        new(
            commodity,
            plannedBins,
            0m,
            standardBoxWeightPounds,
            0m,
            0m,
            0,
            expectedPackoutPercent,
            expectedPackoutPercent is null ? null : 100m - expectedPackoutPercent.Value,
            0m,
            0m,
            0,
            0m,
            0m,
            0,
            [],
            [],
            sizeBasis,
            gradeBasis,
            jointBasis,
            warning);

    private sealed record DistributionInput(string Key, int Count);

    private sealed class AllocationWork(
        string key,
        int order,
        int count,
        decimal share,
        decimal grossExact,
        decimal packedExact,
        decimal cullExact)
    {
        public string Key { get; } = key;
        public int Order { get; } = order;
        public int Count { get; } = count;
        public decimal Share { get; } = share;
        public decimal GrossExact { get; } = grossExact;
        public decimal PackedExact { get; } = packedExact;
        public decimal CullExact { get; } = cullExact;
        public int GrossRounded { get; set; }
        public int PackedRounded { get; set; }
        public int CullRounded { get; set; }
    }
}
