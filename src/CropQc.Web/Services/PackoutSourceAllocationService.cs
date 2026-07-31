using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;

namespace CropQc.Web.Services;

public interface IPackoutSourceAllocationService
{
    IReadOnlyList<PackoutSourceAllocation> Allocate(
        PackoutRun packout,
        RunExpectation expectation,
        DateTimeOffset calculatedAt);
}

public sealed class PackoutSourceAllocationService : IPackoutSourceAllocationService
{
    public IReadOnlyList<PackoutSourceAllocation> Allocate(
        PackoutRun packout,
        RunExpectation expectation,
        DateTimeOffset calculatedAt)
    {
        var sources = expectation.Sources.OrderBy(x => x.Id).ToList();
        if (sources.Count == 0 || expectation.TotalBins <= 0)
        {
            return [];
        }

        var totalWholeBoxes = RunProjectionCalculationService.RoundPlanningBoxes(
            packout.PackedProductPounds / RunProjectionCalculationService.DefaultStandardBoxWeightPounds);
        var wholeBoxes = AllocateWholeUnits(
            totalWholeBoxes,
            sources.Select(x => (x.Id, x.BinsContributed / (decimal)expectation.TotalBins)).ToList());
        var packCodes = packout.Lines
            .Where(x => x.ProductCategory == PackoutProductCategories.Packed && x.ExtendedWeightPounds != null)
            .GroupBy(x => x.NormalizedPackCode ?? x.RawPackCode ?? "Unclassified", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.ExtendedWeightPounds ?? 0m), StringComparer.OrdinalIgnoreCase);
        var sizes = packout.Lines
            .Where(x => x.ProductCategory == PackoutProductCategories.Packed && x.SizeCategory != null && x.ExtendedWeightPounds != null)
            .GroupBy(x => x.SizeCategory!.Value.ToString())
            .ToDictionary(x => x.Key, x => x.Sum(y => y.ExtendedWeightPounds ?? 0m), StringComparer.OrdinalIgnoreCase);
        var grades = packout.Lines
            .Where(x => x.ProductCategory == PackoutProductCategories.Packed && x.Grade != null && x.ExtendedWeightPounds != null)
            .GroupBy(x => x.Grade!.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.ExtendedWeightPounds ?? 0m), StringComparer.OrdinalIgnoreCase);

        var result = new List<PackoutSourceAllocation>(sources.Count);
        decimal allocatedPacked = 0m;
        decimal allocatedJuice = 0m;
        decimal allocatedPeeler = 0m;
        decimal allocatedWaste = 0m;
        var allocatedPackCodes = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var allocatedSizes = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        var allocatedGrades = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < sources.Count; index++)
        {
            var source = sources[index];
            var share = source.BinsContributed / (decimal)expectation.TotalBins;
            var isLast = index == sources.Count - 1;
            var packed = ReconciledShare(packout.PackedProductPounds, share, isLast, allocatedPacked);
            var juice = ReconciledShare(packout.JuicePounds, share, isLast, allocatedJuice);
            var peeler = ReconciledShare(packout.PeelerSlicerPounds, share, isLast, allocatedPeeler);
            var waste = ReconciledShare(packout.WastePounds, share, isLast, allocatedWaste);
            allocatedPacked += packed;
            allocatedJuice += juice;
            allocatedPeeler += peeler;
            allocatedWaste += waste;
            result.Add(new PackoutSourceAllocation
            {
                PackoutRunId = packout.Id,
                RunExpectationSourceId = source.Id,
                BinsContributed = source.BinsContributed,
                ContributionPercent = decimal.Round(share * 100m, 6),
                AllocatedPackedPounds = packed,
                AllocatedWholeBoxes = wholeBoxes.GetValueOrDefault(source.Id),
                AllocatedResidualPounds = packed
                    - wholeBoxes.GetValueOrDefault(source.Id) * RunProjectionCalculationService.DefaultStandardBoxWeightPounds,
                AllocatedJuicePounds = juice,
                AllocatedPeelerPounds = peeler,
                AllocatedWastePounds = waste,
                PackCodeAllocationJson = JsonSerializer.Serialize(AllocateDecimalMap(packCodes, share, isLast, allocatedPackCodes)),
                SizeAllocationJson = JsonSerializer.Serialize(AllocateDecimalMap(sizes, share, isLast, allocatedSizes)),
                GradeAllocationJson = JsonSerializer.Serialize(AllocateDecimalMap(grades, share, isLast, allocatedGrades)),
                AllocationVersion = ActualAllocationVersions.Current,
                CalculatedAt = calculatedAt
            });
        }

        return result;
    }

    private static decimal ReconciledShare(decimal total, decimal share, bool isLast, decimal allocated) =>
        isLast ? total - allocated : decimal.Round(total * share, 6, MidpointRounding.ToEven);

    private static IReadOnlyDictionary<string, decimal> AllocateDecimalMap(
        IReadOnlyDictionary<string, decimal> totals,
        decimal share,
        bool isLast,
        IDictionary<string, decimal> allocated)
    {
        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var total in totals)
        {
            var previouslyAllocated = allocated.TryGetValue(total.Key, out var prior) ? prior : 0m;
            var value = isLast
                ? total.Value - previouslyAllocated
                : decimal.Round(total.Value * share, 6, MidpointRounding.ToEven);
            result[total.Key] = value;
            allocated[total.Key] = previouslyAllocated + value;
        }
        return result;
    }

    /// <summary>
    /// Deterministic largest-remainder allocation. Ties are resolved by source id.
    /// </summary>
    public static IReadOnlyDictionary<long, int> AllocateWholeUnits(
        int totalUnits,
        IReadOnlyList<(long SourceId, decimal Share)> shares)
    {
        var work = shares.Select(x =>
        {
            var exact = totalUnits * x.Share;
            var floor = exact <= 0m ? 0 : (int)decimal.Floor(exact);
            return new AllocationWork(x.SourceId, floor, exact - floor);
        }).ToList();
        var remaining = Math.Max(0, totalUnits - work.Sum(x => x.Units));
        foreach (var item in work
            .OrderByDescending(x => x.Remainder)
            .ThenBy(x => x.SourceId)
            .Take(remaining))
        {
            item.Units++;
        }

        return work.ToDictionary(x => x.SourceId, x => x.Units);
    }

    private sealed class AllocationWork(long sourceId, int units, decimal remainder)
    {
        public long SourceId { get; } = sourceId;
        public int Units { get; set; } = units;
        public decimal Remainder { get; } = remainder;
    }
}
