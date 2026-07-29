using CropQc.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public sealed record PackoutHistorySuggestion(
    decimal? PackoutPercent,
    decimal JuiceCullShare,
    decimal PeelerSlicerCullShare,
    decimal WasteCullShare,
    string Basis,
    IReadOnlyList<long> RunIds,
    IReadOnlyList<int> CropYears,
    decimal TotalDumpedBins,
    IReadOnlyDictionary<int, decimal> DumpedPoundsByCropYear);

public interface IPackoutHistoricalSuggestionService
{
    Task<PackoutHistorySuggestion> GetAsync(
        DateOnly projectionDate,
        int cropYear,
        string lotNumber,
        string variety,
        bool isOrganic,
        CancellationToken cancellationToken);
}

public sealed class PackoutHistoricalSuggestionService(CropQcDbContext dbContext)
    : IPackoutHistoricalSuggestionService
{
    public const decimal LotSpecificThresholdBins = 100m;
    public const decimal DefaultJuiceCullShare = 0.35m;
    public const decimal DefaultPeelerSlicerCullShare = 0.35m;
    public const decimal DefaultWasteCullShare = 0.30m;

    public async Task<PackoutHistorySuggestion> GetAsync(
        DateOnly projectionDate,
        int cropYear,
        string lotNumber,
        string variety,
        bool isOrganic,
        CancellationToken cancellationToken)
    {
        var normalizedLot = lotNumber.Trim();
        var normalizedVariety = variety.Trim();
        var eligible = await dbContext.PackoutRuns.AsNoTracking()
            .Where(x => x.Status == PackoutRunStatuses.Finalized
                && x.PackingDate < projectionDate
                && x.IsOrganicSnapshot == isOrganic
                && x.VarietySnapshot == normalizedVariety)
            .ToListAsync(cancellationToken);
        var currentLotBins = eligible
            .Where(x => x.CropYearSnapshot == cropYear && x.LotNumberSnapshot == normalizedLot)
            .Sum(x => x.DumpedBins);
        var lotSpecific = currentLotBins >= LotSpecificThresholdBins;
        var selected = lotSpecific
            ? eligible.Where(x => x.LotNumberSnapshot == normalizedLot).ToList()
            : eligible;
        if (selected.Count == 0)
        {
            return new(
                null,
                DefaultJuiceCullShare,
                DefaultPeelerSlicerCullShare,
                DefaultWasteCullShare,
                "No finalized history",
                [],
                [],
                0m,
                new Dictionary<int, decimal>());
        }

        var configuration = await dbContext.PackoutAnalysisConfigurations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == 1, cancellationToken);
        var currentWeight = configuration?.CurrentCropYearHistoryWeight ?? 80m;
        var priorWeight = configuration?.PriorCropYearHistoryWeight ?? 20m;
        var yearGroups = selected
            .GroupBy(x => x.CropYearSnapshot)
            .ToDictionary(x => x.Key, x => x.ToList());
        var current = yearGroups.GetValueOrDefault(cropYear);
        var prior = yearGroups.Where(x => x.Key != cropYear).OrderByDescending(x => x.Key).ToList();

        var blended = new List<(decimal Weight, RunMetrics Metrics)>();
        if (current is { Count: > 0 } && prior.Count > 0)
        {
            blended.Add((currentWeight, WeightedWithinYear(current)));
            var perPriorYear = priorWeight / prior.Count;
            blended.AddRange(prior.Select(x => (perPriorYear, WeightedWithinYear(x.Value))));
        }
        else if (current is { Count: > 0 })
        {
            blended.Add((100m, WeightedWithinYear(current)));
        }
        else
        {
            var perPriorYear = 100m / prior.Count;
            blended.AddRange(prior.Select(x => (perPriorYear, WeightedWithinYear(x.Value))));
        }

        var metrics = Blend(blended);
        var cullTotal = metrics.JuicePercent + metrics.PeelerSlicerPercent + metrics.WastePercent;
        var juiceShare = cullTotal <= 0m ? DefaultJuiceCullShare : metrics.JuicePercent / cullTotal;
        var peelerShare = cullTotal <= 0m ? DefaultPeelerSlicerCullShare : metrics.PeelerSlicerPercent / cullTotal;
        var wasteShare = cullTotal <= 0m ? DefaultWasteCullShare : metrics.WastePercent / cullTotal;
        return new(
            decimal.Round(metrics.PackoutPercent, 4),
            decimal.Round(juiceShare, 6),
            decimal.Round(peelerShare, 6),
            decimal.Round(wasteShare, 6),
            lotSpecific ? "Exact lot history" : "Variety fallback history",
            selected.Select(x => x.Id).OrderBy(x => x).ToList(),
            yearGroups.Keys.OrderByDescending(x => x).ToList(),
            selected.Sum(x => x.DumpedBins),
            yearGroups.ToDictionary(x => x.Key, x => x.Value.Sum(y => y.DumpedPounds)));
    }

    private static RunMetrics WeightedWithinYear(IReadOnlyList<PackoutRun> runs)
    {
        var total = runs.Where(x => x.DumpedPounds > 0m).Sum(x => x.DumpedPounds);
        if (total <= 0m) return new();
        decimal Weighted(Func<PackoutRun, decimal?> selector) =>
            runs.Where(x => x.DumpedPounds > 0m && selector(x) is not null)
                .Sum(x => selector(x)!.Value * x.DumpedPounds) / total;
        return new(
            Weighted(x => x.ActualPackoutPercent),
            Weighted(x => x.ActualJuicePercent),
            Weighted(x => x.ActualPeelerSlicerPercent),
            Weighted(x => x.ActualWastePercent));
    }

    private static RunMetrics Blend(IReadOnlyList<(decimal Weight, RunMetrics Metrics)> values)
    {
        var total = values.Sum(x => x.Weight);
        if (total <= 0m) return new();
        return new(
            values.Sum(x => x.Weight * x.Metrics.PackoutPercent) / total,
            values.Sum(x => x.Weight * x.Metrics.JuicePercent) / total,
            values.Sum(x => x.Weight * x.Metrics.PeelerSlicerPercent) / total,
            values.Sum(x => x.Weight * x.Metrics.WastePercent) / total);
    }

    private sealed record RunMetrics(
        decimal PackoutPercent = 0m,
        decimal JuicePercent = 0m,
        decimal PeelerSlicerPercent = 0m,
        decimal WastePercent = 0m);
}
