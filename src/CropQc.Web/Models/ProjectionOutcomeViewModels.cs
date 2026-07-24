using System.Text.Json;

namespace CropQc.Web.Models;

public sealed record RunProjectionTrendSizePoint(int Size, decimal Percentage);

public sealed record RunProjectionTrendPointSnapshot(
    long SampleId,
    DateTimeOffset SampleTakenAt,
    string Variety,
    string CompletionStatus,
    int TargetSampleSize,
    int EnteredFruitCount,
    decimal? AverageWeightGrams,
    decimal? AveragePressureLbs,
    decimal? AverageStarch,
    decimal? DefectAffectedPercentage,
    IReadOnlyList<RunProjectionTrendSizePoint> SizeDistribution);

public sealed class ProjectionOutcomeViewModel
{
    public RunProjectionDetailViewModel Projection { get; set; } = new();
    public DateTimeOffset GeneratedAt { get; set; }
    public IReadOnlyList<ProjectionOutcomePackRow> Packs { get; set; } = [];
    public IReadOnlyList<ProjectionOutcomeGradeRow> Grades { get; set; } = [];
    public IReadOnlyList<string> GradeNames { get; set; } = [];
    public IReadOnlyList<ProjectionOutcomeMatrixRow> Matrix { get; set; } = [];
    public IReadOnlyList<ProjectionOutcomeCullCommodityRow> CullByCommodity { get; set; } = [];
    public ProjectionOutcomeCullTotals CullTotals { get; set; } = new(0m, 0m, 0m, 0m);
    public IReadOnlyList<ProjectionOutcomeSourceContributionRow> SourceContributions { get; set; } = [];
    public IReadOnlyList<ProjectionOutcomeTrendSourceRow> TrendSources { get; set; } = [];
    public IReadOnlyList<string> Warnings { get; set; } = [];
    public string Confidence { get; set; } = "Needs attention";
    public int JointBasisFruitCount { get; set; }
    public int CompletePackCount { get; set; }
    public decimal CompletePackPounds { get; set; }
    public decimal ResidualPackedPounds { get; set; }
    public decimal UnallocatedPackedPounds { get; set; }
    public decimal CullPounds { get; set; }
    public decimal ReconciliationDifference { get; set; }
    public bool HasMixedCommodities => CullByCommodity.Count > 1;
}

public sealed record ProjectionOutcomePackRow(
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
    int CompletePacks,
    decimal CompletePackPounds,
    decimal ResidualPounds,
    decimal PartialPackFraction,
    int JointBasisFruitCount,
    IReadOnlyList<RunProjectionPackContributionViewModel> Contributions,
    IReadOnlyList<RunProjectionPackGradeViewModel> GradeAllocations,
    string? GradeWarning,
    string ContributionTooltip);

public sealed record ProjectionOutcomeGradeRow(
    string Grade,
    decimal UnroundedBoxes,
    int CompleteBoxes,
    decimal ResidualPounds);

public sealed record ProjectionOutcomeMatrixRow(
    string PackCode,
    string PackName,
    int PackCompleteCount,
    int JointBasisFruitCount,
    IReadOnlyDictionary<string, int> CompleteBoxesByGrade,
    IReadOnlyDictionary<string, decimal> ResidualPoundsByGrade,
    string? Warning)
{
    public int TotalCompleteBoxes => CompleteBoxesByGrade.Values.Sum();
}

public sealed record ProjectionOutcomeCullCommodityRow(
    string Commodity,
    decimal PoundsPerBin,
    decimal TotalCullPounds,
    decimal TotalCullBinEquivalents,
    decimal PeelerPounds,
    decimal PeelerBinEquivalents,
    decimal JuicePounds,
    decimal JuiceBinEquivalents,
    decimal WastePounds,
    decimal WasteBinEquivalents);

public sealed record ProjectionOutcomeCullTotals(
    decimal TotalCullPounds,
    decimal PeelerPounds,
    decimal JuicePounds,
    decimal WastePounds);

public sealed record ProjectionOutcomeSourceContributionRow(
    long SourceId,
    string Source,
    string Commodity,
    int Bins,
    decimal? ExpectedPackoutPercent,
    decimal GrossPounds,
    decimal PackedPounds,
    decimal PackedBoxEquivalents,
    int CompletePackedBoxes,
    decimal PackedResidualPounds,
    decimal PoundsPerBin,
    decimal TotalCullPounds,
    decimal PeelerPounds,
    decimal PeelerBins,
    decimal JuicePounds,
    decimal JuiceBins,
    decimal WastePounds,
    decimal WasteBins);

public sealed record ProjectionOutcomeTrendSourceRow(
    long SourceId,
    string Source,
    string QcBasis,
    IReadOnlyList<RunProjectionTrendPointSnapshot> Points);

public static class ProjectionOutcomeCalculator
{
    public const decimal PeelerRate = 0.35m;
    public const decimal JuiceRate = 0.40m;
    public const decimal WasteRate = 0.25m;
    public const string CullCalculationVersion = "1.0";

    public static ProjectionOutcomeViewModel Build(RunProjectionDetailViewModel projection, DateTimeOffset generatedAt)
    {
        var packs = projection.PackResults
            .Select(pack =>
            {
                var complete = Floor(pack.UnroundedPacks);
                var completePounds = complete * pack.PackageWeightPounds;
                var residual = Math.Max(0m, pack.AssignedPounds - completePounds);
                var tooltip = string.Join("; ", pack.Contributions
                    .GroupBy(x => x.SourceLabel)
                    .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(x => $"{x.Key}: {x.Sum(y => y.AssignedPounds):0.##} lb"));
                return new ProjectionOutcomePackRow(
                    pack.PackCode,
                    pack.PackName,
                    pack.Commodity,
                    pack.PackType,
                    pack.PackageWeightPounds,
                    pack.IsMixedSize,
                    pack.MixRule,
                    pack.EligibleSizes,
                    pack.GrossAssignedPounds,
                    pack.AssignedPounds,
                    pack.CullPounds,
                    pack.UnroundedPacks,
                    complete,
                    completePounds,
                    residual,
                    pack.PackageWeightPounds <= 0 ? 0m : residual / pack.PackageWeightPounds,
                    pack.JointBasisFruitCount,
                    pack.Contributions,
                    pack.GradeAllocations,
                    pack.GradeWarning,
                    tooltip);
            })
            .OrderBy(x => x.PackName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var grades = projection.Sources
            .SelectMany(x => x.GradeResults)
            .GroupBy(x => x.Grade, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var unrounded = group.Sum(x => x.PackedBoxes);
                var complete = Floor(unrounded);
                return new ProjectionOutcomeGradeRow(
                    group.Key,
                    unrounded,
                    complete,
                    Math.Max(0m, unrounded - complete) * projection.StandardBoxWeightPounds);
            })
            .ToList();

        var gradeNames = packs
            .SelectMany(x => x.GradeAllocations)
            .Select(x => x.GradeCode)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var matrix = packs.Select(pack =>
        {
            var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var residuals = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var grade in gradeNames)
            {
                var pounds = pack.GradeAllocations
                    .Where(x => x.GradeCode.Equals(grade, StringComparison.OrdinalIgnoreCase))
                    .Sum(x => x.AssignedPounds);
                var complete = pack.PackageWeightPounds <= 0 ? 0 : Floor(pounds / pack.PackageWeightPounds);
                counts[grade] = complete;
                residuals[grade] = Math.Max(0m, pounds - complete * pack.PackageWeightPounds);
            }
            return new ProjectionOutcomeMatrixRow(
                pack.PackCode,
                pack.PackName,
                pack.CompletePacks,
                pack.JointBasisFruitCount,
                counts,
                residuals,
                pack.GradeWarning);
        }).ToList();

        var cullByCommodity = projection.Sources
            .Where(x => x.CullProjectedPounds > 0)
            .GroupBy(x => x.Commodity, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var cull = group.Sum(x => x.CullProjectedPounds);
                var poundsPerBin = group.Select(x => x.PoundsPerBin).FirstOrDefault(x => x > 0m);
                if (poundsPerBin <= 0m)
                {
                    poundsPerBin = group.Key.Equals("Pear", StringComparison.OrdinalIgnoreCase)
                        ? projection.PearPoundsPerBin
                        : projection.ApplePoundsPerBin;
                }
                var peeler = cull * projection.PeelerCullShare;
                var juice = cull * projection.JuiceCullShare;
                var waste = cull * projection.WasteCullShare;
                return new ProjectionOutcomeCullCommodityRow(
                    group.Key,
                    poundsPerBin,
                    cull,
                    Divide(cull, poundsPerBin),
                    peeler,
                    Divide(peeler, poundsPerBin),
                    juice,
                    Divide(juice, poundsPerBin),
                    waste,
                    Divide(waste, poundsPerBin));
            })
            .ToList();
        var cullTotals = new ProjectionOutcomeCullTotals(
            cullByCommodity.Sum(x => x.TotalCullPounds),
            cullByCommodity.Sum(x => x.PeelerPounds),
            cullByCommodity.Sum(x => x.JuicePounds),
            cullByCommodity.Sum(x => x.WastePounds));

        var sourceContributions = projection.Sources
            .OrderBy(x => x.SortOrder)
            .ThenBy(x => x.Id)
            .Select(source =>
            {
                var complete = Floor(source.PackedProjectedBoxes);
                var residual = Math.Max(0m, source.PackedProjectedPounds - complete * projection.StandardBoxWeightPounds);
                var poundsPerBin = source.PoundsPerBin > 0m
                    ? source.PoundsPerBin
                    : source.Commodity.Equals("Pear", StringComparison.OrdinalIgnoreCase)
                        ? projection.PearPoundsPerBin
                        : projection.ApplePoundsPerBin;
                var peelerPounds = source.CullProjectedPounds * projection.PeelerCullShare;
                var juicePounds = source.CullProjectedPounds * projection.JuiceCullShare;
                var wastePounds = source.CullProjectedPounds * projection.WasteCullShare;
                return new ProjectionOutcomeSourceContributionRow(
                    source.Id,
                    source.Block ?? source.Lot ?? source.SourceLabel,
                    source.Commodity,
                    source.PlannedBins,
                    source.ExpectedPackoutPercent,
                    source.ProjectedPounds,
                    source.PackedProjectedPounds,
                    source.PackedProjectedBoxes,
                    complete,
                    residual,
                    poundsPerBin,
                    source.CullProjectedPounds,
                    peelerPounds,
                    Divide(peelerPounds, poundsPerBin),
                    juicePounds,
                    Divide(juicePounds, poundsPerBin),
                    wastePounds,
                    Divide(wastePounds, poundsPerBin));
            })
            .ToList();

        var trendSources = projection.Sources
            .Where(x => !string.IsNullOrWhiteSpace(x.FieldSampleTrendSnapshotJson)
                || x.SelectedQcSourceType.Equals("FieldSample", StringComparison.OrdinalIgnoreCase))
            .Select(x => new ProjectionOutcomeTrendSourceRow(
                x.Id,
                x.Block ?? x.Lot ?? x.SourceLabel,
                x.QcBasis,
                DeserializeTrend(x.FieldSampleTrendSnapshotJson)))
            .ToList();

        var warnings = projection.Sources
            .Select(x => x.Warning)
            .Concat(projection.PackWarnings)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var jointBasis = projection.Sources.Sum(x => x.JointSizeGradeBasisFruitCount);
        if (projection.Sources.Any(x => x.ExpectedPackoutPercent is null))
        {
            warnings.Add("One or more sources has no saved Expected Packout percentage.");
        }
        if (packs.Count == 0)
        {
            warnings.Add("No saved commercial pack allocation is available for this projection.");
        }
        if (grades.Count == 0)
        {
            warnings.Add("Grade projection is unavailable because the saved source data has no grade measurements.");
        }
        if (jointBasis == 0)
        {
            warnings.Add("Size-by-grade production is unavailable because no saved fruit row has both calculated size and grade.");
        }
        else if (jointBasis < 10)
        {
            warnings.Add($"Low confidence: only {jointBasis} fruit had both size and grade recorded.");
        }

        var completePackPounds = packs.Sum(x => x.CompletePackPounds);
        var residualPackedPounds = packs.Sum(x => x.ResidualPounds);
        var reconciliationDifference = projection.TotalProjectedPounds
            - completePackPounds
            - residualPackedPounds
            - projection.PackUnallocatedPounds
            - projection.TotalCullProjectedPounds;

        return new ProjectionOutcomeViewModel
        {
            Projection = projection,
            GeneratedAt = generatedAt,
            Packs = packs,
            Grades = grades,
            GradeNames = gradeNames,
            Matrix = matrix,
            CullByCommodity = cullByCommodity,
            CullTotals = cullTotals,
            SourceContributions = sourceContributions,
            TrendSources = trendSources,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            Confidence = Confidence(projection, jointBasis, warnings),
            JointBasisFruitCount = jointBasis,
            CompletePackCount = packs.Sum(x => x.CompletePacks),
            CompletePackPounds = completePackPounds,
            ResidualPackedPounds = residualPackedPounds,
            UnallocatedPackedPounds = projection.PackUnallocatedPounds,
            CullPounds = projection.TotalCullProjectedPounds,
            ReconciliationDifference = reconciliationDifference
        };
    }

    public static int Floor(decimal value) => value <= 0m ? 0 : (int)decimal.Floor(value);

    private static decimal Divide(decimal value, decimal divisor) => divisor <= 0m ? 0m : value / divisor;

    private static string Confidence(
        RunProjectionDetailViewModel projection,
        int jointBasis,
        IReadOnlyCollection<string> warnings)
    {
        if (projection.Sources.Count == 0 || projection.Sources.Any(x => x.ExpectedPackoutPercent is null) || jointBasis == 0)
        {
            return "Needs attention";
        }
        if (jointBasis < 10 || warnings.Count > 0)
        {
            return "Low";
        }
        return jointBasis < 25 ? "Moderate" : "High";
    }

    private static IReadOnlyList<RunProjectionTrendPointSnapshot> DeserializeTrend(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }
        try
        {
            return JsonSerializer.Deserialize<List<RunProjectionTrendPointSnapshot>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }
}
