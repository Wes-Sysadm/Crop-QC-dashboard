namespace CropQc.Data;

public sealed record RunProjectionSizeObservation(int SizeCategory);

public sealed record RunProjectionSizeAllocation(
    string Commodity,
    int SizeCategory,
    int SampleCount,
    decimal Percentage,
    decimal UnroundedProjectedBoxes,
    int RoundedProjectedBoxes);

public sealed record RunProjectionLineCalculation(
    string Commodity,
    int PlannedBins,
    decimal PoundsPerBin,
    decimal StandardBoxWeightPounds,
    decimal ProjectedPounds,
    decimal ProjectedBoxes,
    int RoundedProjectedBoxes,
    IReadOnlyList<RunProjectionSizeAllocation> SizeAllocations,
    string? Warning);

public static class RunProjectionCalculationService
{
    public const decimal DefaultApplePoundsPerBin = 880m;
    public const decimal DefaultPearPoundsPerBin = 920m;
    public const decimal DefaultStandardBoxWeightPounds = 40m;

    public static RunProjectionLineCalculation Calculate(
        string? fruitType,
        int plannedBins,
        decimal applePoundsPerBin,
        decimal pearPoundsPerBin,
        decimal standardBoxWeightPounds,
        IEnumerable<RunProjectionSizeObservation> observations)
    {
        if (plannedBins < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(plannedBins), "Planned bins cannot be negative.");
        }

        if (applePoundsPerBin <= 0 || pearPoundsPerBin <= 0 || standardBoxWeightPounds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(standardBoxWeightPounds), "Projection weight assumptions must be positive.");
        }

        var commodity = NormalizeCommodity(fruitType);
        var poundsPerBin = commodity switch
        {
            "Apple" => applePoundsPerBin,
            "Pear" => pearPoundsPerBin,
            _ => 0m
        };

        if (poundsPerBin <= 0)
        {
            return new RunProjectionLineCalculation(
                commodity,
                plannedBins,
                0m,
                standardBoxWeightPounds,
                0m,
                0m,
                0,
                [],
                "Commodity is not configured as Apple or Pear. Resolve the fruit profile before calculating boxes.");
        }

        var pounds = plannedBins * poundsPerBin;
        var projectedBoxes = pounds / standardBoxWeightPounds;
        var roundedTotal = RoundPlanningBoxes(projectedBoxes);
        var sizes = observations
            .GroupBy(x => x.SizeCategory)
            .OrderBy(x => x.Key)
            .Select(x => new { Size = x.Key, Count = x.Count() })
            .ToList();

        if (sizes.Count == 0)
        {
            return new RunProjectionLineCalculation(
                commodity,
                plannedBins,
                poundsPerBin,
                standardBoxWeightPounds,
                pounds,
                projectedBoxes,
                roundedTotal,
                [],
                "No meaningful calculated size data is available for the selected QC sample.");
        }

        var representedFruit = sizes.Sum(x => x.Count);
        var provisional = sizes.Select(size =>
        {
            var percentage = size.Count / (decimal)representedFruit;
            var exact = projectedBoxes * percentage;
            return new AllocationWork(size.Size, size.Count, percentage, exact, (int)decimal.Floor(exact));
        }).ToList();

        var remaining = roundedTotal - provisional.Sum(x => x.Rounded);
        foreach (var allocation in provisional
                     .OrderByDescending(x => x.Exact - x.Rounded)
                     .ThenBy(x => x.Size)
                     .Take(Math.Max(0, remaining)))
        {
            allocation.Rounded++;
        }

        return new RunProjectionLineCalculation(
            commodity,
            plannedBins,
            poundsPerBin,
            standardBoxWeightPounds,
            pounds,
            projectedBoxes,
            roundedTotal,
            provisional.Select(x => new RunProjectionSizeAllocation(
                    commodity,
                    x.Size,
                    x.Count,
                    decimal.Round(x.Percentage * 100m, 4),
                    x.Exact,
                    x.Rounded))
                .ToList(),
            representedFruit < 10 ? $"Sparse sample: only {representedFruit} fruit have calculated size data." : null);
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

    private sealed class AllocationWork(int size, int count, decimal percentage, decimal exact, int rounded)
    {
        public int Size { get; } = size;
        public int Count { get; } = count;
        public decimal Percentage { get; } = percentage;
        public decimal Exact { get; } = exact;
        public int Rounded { get; set; } = rounded;
    }
}
