using CropQc.Data;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IFieldSampleTrendService
{
    Task<FieldSampleBlockTrendViewModel?> GetForSampleAsync(long sampleId, CancellationToken cancellationToken);
    Task<IReadOnlyList<FieldSampleBlockTrendViewModel>> GetCardsAsync(
        IReadOnlyCollection<long> filteredSampleIds,
        CancellationToken cancellationToken);
}

public sealed class FieldSampleTrendService(CropQcDbContext dbContext) : IFieldSampleTrendService
{
    private const string FieldSampleTypeName = "Field Sample";
    private const int MinimumSampleSize = 10;
    private const int MaximumSampleSize = 50;

    public async Task<FieldSampleBlockTrendViewModel?> GetForSampleAsync(long sampleId, CancellationToken cancellationToken)
    {
        var anchor = await dbContext.QcSamples.AsNoTracking()
            .Where(x => x.Id == sampleId
                && !x.IsDeleted
                && x.SampleType.Name == FieldSampleTypeName
                && x.CanonicalOrchardBlockId != null)
            .Select(x => new { x.CanonicalOrchardBlockId, x.SampleTakenAt })
            .SingleOrDefaultAsync(cancellationToken);
        if (anchor is null)
        {
            return null;
        }

        var start = anchor.SampleTakenAt.AddDays(-30);
        var query = BaseQuery()
            .Where(x => x.CanonicalOrchardBlockId == anchor.CanonicalOrchardBlockId
                && x.SampleTakenAt >= start
                && x.SampleTakenAt <= anchor.SampleTakenAt);
        var rows = await Project(query
                .OrderBy(x => x.SampleTakenAt)
                .ThenBy(x => x.Id))
            .ToListAsync(cancellationToken);
        return BuildCards(rows, applyPerBlockWindow: false).SingleOrDefault();
    }

    public async Task<IReadOnlyList<FieldSampleBlockTrendViewModel>> GetCardsAsync(
        IReadOnlyCollection<long> filteredSampleIds,
        CancellationToken cancellationToken)
    {
        if (filteredSampleIds.Count == 0)
        {
            return [];
        }

        var query = BaseQuery().Where(x => filteredSampleIds.Contains(x.Id));
        var rows = await Project(query
                .OrderBy(x => x.CanonicalOrchardBlock!.CanonicalBlockName)
                .ThenBy(x => x.SampleTakenAt)
                .ThenBy(x => x.Id))
            .ToListAsync(cancellationToken);
        return BuildCards(rows, applyPerBlockWindow: true);
    }

    public static FieldSampleMetricSummary BuildSummary(IReadOnlyList<TrendFruitRow> rows)
    {
        var entered = rows.Where(HasEnteredData).ToList();
        var weights = entered.Where(x => x.WeightGrams is not null).Select(x => x.WeightGrams!.Value).ToList();
        var starch = entered.Where(x => x.Starch is not null).Select(x => x.Starch!.Value).ToList();
        var pressures = PressureCalculationService.ValidSideReadings(entered.Select(x => (x.Pressure1Lbs, x.Pressure2Lbs)));
        var inspected = entered;
        var affected = inspected.Where(x => x.Defects.Count > 0).ToList();
        var defectDistribution = inspected.Count == 0
            ? []
            : inspected.SelectMany(x => x.Defects)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x.Key)
                .Select(x => new FieldSampleDefectSummaryPoint(
                    x.Key,
                    x.Count(),
                    decimal.Round(x.Count() / (decimal)inspected.Count * 100m, 2)))
                .ToList();

        return new FieldSampleMetricSummary
        {
            EnteredFruitCount = entered.Count,
            AverageWeightGrams = Average(weights),
            PeakWeightGrams = weights.Count == 0 ? null : weights.Max(),
            MinimumWeightGrams = weights.Count == 0 ? null : weights.Min(),
            WeightRepresentedFruitCount = weights.Count,
            MissingWeightCount = entered.Count(x => x.WeightGrams is null),
            AverageStarch = Average(starch),
            StarchRepresentedFruitCount = starch.Count,
            MissingStarchCount = entered.Count(x => x.StarchScaleValueId is null),
            AveragePressureLbs = Average(pressures),
            PeakPressureLbs = pressures.Count == 0 ? null : pressures.Max(),
            MinimumPressureLbs = pressures.Count == 0 ? null : pressures.Min(),
            PressureStandardDeviationLbs = SampleStandardDeviation(pressures),
            PressureReadingCount = pressures.Count,
            MissingPressureCount = entered.Count(x => x.Pressure1Lbs is null && x.Pressure2Lbs is null),
            GradeDistribution = BuildDistribution(entered.Select(x => x.Grade)),
            StarchDistribution = BuildDistribution(entered.Select(x => x.Starch?.ToString("0.0"))),
            DefectInspectedFruitCount = inspected.Count,
            DefectAffectedFruitCount = affected.Count,
            DefectAffectedPercentage = inspected.Count == 0 ? null : decimal.Round(affected.Count / (decimal)inspected.Count * 100m, 2),
            DefectDistribution = defectDistribution
        };
    }

    public static IReadOnlyList<FieldSampleSizePoint> BuildSizeDistribution(IReadOnlyList<TrendFruitRow> rows)
    {
        var represented = rows.Where(x => x.SizeCategory is not null).ToList();
        if (represented.Count == 0)
        {
            return [];
        }

        return ProjectionDistributionMath.SizeDisplayOrder
            .Select(size => new FieldSampleSizePoint(
                size,
                decimal.Round(represented.Count(x => x.SizeCategory == size) / (decimal)represented.Count * 100m, 2)))
            .Where(x => x.Percentage > 0)
            .ToList();
    }

    private IQueryable<CropQc.Data.Entities.QcSample> BaseQuery() =>
        dbContext.QcSamples.AsNoTracking()
            .Where(x => !x.IsDeleted
                && x.SampleType.Name == FieldSampleTypeName
                && x.CanonicalOrchardBlockId != null);

    private static IQueryable<TrendSampleRow> Project(IQueryable<CropQc.Data.Entities.QcSample> query) =>
        query.Select(x => new TrendSampleRow(
                x.Id,
                x.CanonicalOrchardBlockId!.Value,
                x.CanonicalOrchardBlock!.CanonicalOrchard.OrchardName,
                x.FieldSampleGrowerName ?? "",
                x.FieldSampleGrowerNumber ?? "",
                x.CanonicalOrchardBlock.CanonicalBlockName,
                x.SampleTakenAt,
                x.FieldSampleFruitProfile == null ? "" : x.FieldSampleFruitProfile.Name,
                x.Status,
                x.EmailStatus,
                x.ActualSampleSize,
                x.FruitReadings.Select(row => new TrendFruitRow(
                    row.RowNumber,
                    row.Pressure1Lbs,
                    row.Pressure2Lbs,
                    row.WeightGrams,
                    row.StarchScaleValue == null ? null : row.StarchScaleValue.Value,
                    row.StarchScaleValueId,
                    row.SizeCategory,
                    row.Grade == null ? null : row.Grade.Code,
                    row.DefectsInspected,
                    row.Defects.Select(defect => defect.DefectType.Name).ToList())).ToList()));

    private static IReadOnlyList<FieldSampleBlockTrendViewModel> BuildCards(
        IReadOnlyList<TrendSampleRow> rows,
        bool applyPerBlockWindow)
    {
        return rows
            .GroupBy(x => x.CanonicalBlockId)
            .Select(group =>
            {
                var ordered = group.OrderBy(x => x.SampleTakenAt).ThenBy(x => x.SampleId).ToList();
                var end = ordered[^1].SampleTakenAt;
                var start = end.AddDays(-30);
                if (applyPerBlockWindow)
                {
                    ordered = ordered.Where(x => x.SampleTakenAt >= start).ToList();
                }

                var points = ordered.Select(ToPoint).ToList();
                var latest = ordered[^1];
                return new FieldSampleBlockTrendViewModel
                {
                    CanonicalBlockId = latest.CanonicalBlockId,
                    OrchardName = latest.OrchardName,
                    GrowerName = latest.GrowerName,
                    GrowerNumber = latest.GrowerNumber,
                    BlockName = latest.CanonicalBlockName,
                    Varieties = ordered.Select(x => x.Variety).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x).ToList(),
                    WindowStart = start,
                    WindowEnd = end,
                    Points = points
                };
            })
            .OrderByDescending(x => x.WindowEnd)
            .ThenBy(x => x.OrchardName)
            .ThenBy(x => x.GrowerName)
            .ThenBy(x => x.BlockName)
            .ToList();
    }

    private static FieldSampleTrendPoint ToPoint(TrendSampleRow sample)
    {
        var highestPersistedRow = sample.Rows.Count == 0 ? 0 : sample.Rows.Max(row => row.RowNumber);
        return new FieldSampleTrendPoint
        {
            SampleId = sample.SampleId,
            SampleTakenAt = sample.SampleTakenAt,
            Variety = sample.Variety,
            CompletionStatus = NormalizeLifecycleStatus(sample.Status, sample.EmailStatus),
            TargetSampleSize = Math.Clamp(
                Math.Max(MinimumSampleSize, Math.Max(sample.ActualSampleSize ?? MinimumSampleSize, highestPersistedRow)),
                MinimumSampleSize,
                MaximumSampleSize),
            Summary = BuildSummary(sample.Rows),
            SizeDistribution = BuildSizeDistribution(sample.Rows)
        };
    }

    private static bool HasEnteredData(TrendFruitRow row) =>
        row.Pressure1Lbs is not null
        || row.Pressure2Lbs is not null
        || row.WeightGrams is not null
        || row.StarchScaleValueId is not null
        || row.SizeCategory is not null
        || !string.IsNullOrWhiteSpace(row.Grade)
        || row.DefectsInspected
        || row.Defects.Count > 0;

    private static decimal? Average(IReadOnlyCollection<decimal> values) =>
        values.Count == 0 ? null : decimal.Round(values.Average(), 2);

    private static IReadOnlyList<FieldSampleDistributionPoint> BuildDistribution(IEnumerable<string?> values)
    {
        var represented = values.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).ToList();
        return represented.Count == 0
            ? []
            : represented.GroupBy(x => x, StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x.Key)
                .Select(x => new FieldSampleDistributionPoint(x.Key, decimal.Round(x.Count() / (decimal)represented.Count * 100m, 2)))
                .ToList();
    }

    private static decimal? SampleStandardDeviation(IReadOnlyList<decimal> values)
    {
        if (values.Count < 2)
        {
            return null;
        }

        var mean = values.Average();
        var variance = values.Sum(value => (value - mean) * (value - mean)) / (values.Count - 1);
        return decimal.Round((decimal)Math.Sqrt((double)variance), 2);
    }

    private static string NormalizeLifecycleStatus(string? status, string? emailStatus)
    {
        if (string.Equals(emailStatus, "Needs Resend", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Changed Since Last Send", StringComparison.OrdinalIgnoreCase))
        {
            return "Changed Since Last Send";
        }
        if (string.Equals(emailStatus, "Sent", StringComparison.OrdinalIgnoreCase)
            || string.Equals(status, "Sent", StringComparison.OrdinalIgnoreCase))
        {
            return "Sent";
        }
        return string.Equals(status, "Complete", StringComparison.OrdinalIgnoreCase) ? "Complete" : "In Progress";
    }

    public sealed record TrendFruitRow(
        int RowNumber,
        decimal? Pressure1Lbs,
        decimal? Pressure2Lbs,
        decimal? WeightGrams,
        decimal? Starch,
        int? StarchScaleValueId,
        int? SizeCategory,
        string? Grade,
        bool DefectsInspected,
        IReadOnlyList<string> Defects);

    private sealed record TrendSampleRow(
        long SampleId,
        int CanonicalBlockId,
        string OrchardName,
        string GrowerName,
        string GrowerNumber,
        string CanonicalBlockName,
        DateTimeOffset SampleTakenAt,
        string Variety,
        string Status,
        string EmailStatus,
        int? ActualSampleSize,
        IReadOnlyList<TrendFruitRow> Rows);
}
