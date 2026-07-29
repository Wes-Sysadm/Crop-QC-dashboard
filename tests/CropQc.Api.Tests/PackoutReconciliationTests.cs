using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class PackoutReconciliationTests
{
    [Fact]
    public async Task DefectStatus_FollowsPresenceOfDefectRecords()
    {
        await using var db = Db();
        var sampleType = new SampleType { Name = "Receiving Sample" };
        var defectType = new DefectType { Name = "Bruise" };
        var sample = Sample(sampleType);
        var row = new QcFruitReading
        {
            QcSample = sample,
            RowNumber = 1,
            SizeStatus = "Not calculated",
            WeightGrams = 200m,
            CreatedAt = DateTimeOffset.UtcNow
        };
        sample.FruitReadings.Add(row);
        db.AddRange(sampleType, defectType, sample);
        await db.SaveChangesAsync();

        Assert.Equal(DefectInspectionStatuses.NoDefectsFound, sample.DefectInspectionStatus);

        var defect = new QcFruitDefect { QcFruitReading = row, DefectType = defectType };
        db.QcFruitDefects.Add(defect);
        await db.SaveChangesAsync();
        Assert.Equal(DefectInspectionStatuses.DefectsFound, sample.DefectInspectionStatus);

        db.QcFruitDefects.Remove(defect);
        await db.SaveChangesAsync();
        Assert.Equal(DefectInspectionStatuses.NoDefectsFound, sample.DefectInspectionStatus);
        Assert.Empty(await db.QcFruitDefects.ToListAsync());
    }

    [Fact]
    public async Task DefectStatus_IsCorrectWhenNewGraphAlreadyContainsDefect()
    {
        await using var db = Db();
        var sampleType = new SampleType { Name = "Field Sample" };
        var defectType = new DefectType { Name = "Scuff" };
        var sample = Sample(sampleType);
        var row = new QcFruitReading
        {
            QcSample = sample,
            RowNumber = 1,
            SizeStatus = "Not calculated",
            CreatedAt = DateTimeOffset.UtcNow
        };
        row.Defects.Add(new QcFruitDefect { QcFruitReading = row, DefectType = defectType });
        sample.FruitReadings.Add(row);
        db.AddRange(sampleType, defectType, sample);

        await db.SaveChangesAsync();

        Assert.Equal(DefectInspectionStatuses.DefectsFound, sample.DefectInspectionStatus);
    }

    [Fact]
    public void ActualCalculation_UsesPackWeightsAndDumpedWeightDenominators()
    {
        var result = PackoutReconciliationCalculationService.Calculate(
            dumpedBins: 10m,
            poundsPerBin: 920m,
            actualLines:
            [
                new(100m, 40m, PackoutProductCategories.Packed, 80, "US1"),
                new(2m, 900m, PackoutProductCategories.Juice),
                new(1m, 920m, PackoutProductCategories.Waste)
            ],
            projectedSizePercentages: new Dictionary<string, decimal> { ["80"] = 100m },
            projectedGradePercentages: new Dictionary<string, decimal> { ["US1"] = 100m },
            projectedPackoutPercent: 50m,
            projectedJuicePercent: 20m,
            projectedPeelerSlicerPercent: 0m,
            projectedWastePercent: 10m);

        Assert.Equal(4000m, result.PackedProductPounds);
        Assert.Equal(1800m, result.JuicePounds);
        Assert.Equal(920m, result.WastePounds);
        Assert.Equal(decimal.Round(4000m / 9200m * 100m, 4), result.PackoutPercent);
        Assert.Equal(decimal.Round(1800m / 9200m * 100m, 4), result.JuicePercent);
    }

    [Fact]
    public void ActualCalculation_NegativeConfirmedLinesReduceExactBucket()
    {
        var result = PackoutReconciliationCalculationService.Calculate(
            1m,
            880m,
            [
                new(10m, 40m, PackoutProductCategories.Packed, 80, "W1"),
                new(-2m, 40m, PackoutProductCategories.Packed, 80, "W1")
            ],
            new Dictionary<string, decimal> { ["80"] = 100m },
            new Dictionary<string, decimal> { ["W1"] = 100m },
            36.3636m,
            0m,
            0m,
            0m);

        Assert.Equal(320m, result.PackedProductPounds);
        Assert.Equal(320m, Assert.Single(result.SizeDistribution).Pounds);
    }

    [Fact]
    public void Accuracy_UsesRequiredFormulaAndDoesNotRedistributeMissingComponents()
    {
        var result = PackoutReconciliationCalculationService.Calculate(
            10m,
            880m,
            [new(100m, 40m, PackoutProductCategories.Packed, 80, null)],
            new Dictionary<string, decimal> { ["80"] = 50m, ["90"] = 50m },
            new Dictionary<string, decimal> { ["W1"] = 100m },
            50m,
            0m,
            0m,
            0m);

        Assert.Equal(50m, result.SizeAccuracy);
        Assert.Null(result.GradeAccuracy);
        Assert.True(result.OverallAccuracy < 65m);
    }

    [Theory]
    [InlineData(968, 880, 88, false)]
    [InlineData(969, 880, 89, true)]
    public void ReconciliationWarning_IsStrictlyGreaterThanTenPercent(
        decimal packedPounds,
        decimal dumpedPounds,
        decimal expectedDifference,
        bool expectedWarning)
    {
        var quantity = packedPounds / 40m;
        var result = PackoutReconciliationCalculationService.Calculate(
            1m,
            dumpedPounds,
            [new(quantity, 40m, PackoutProductCategories.Packed)],
            new Dictionary<string, decimal>(),
            new Dictionary<string, decimal>(),
            null,
            null,
            null,
            null);

        Assert.Equal(expectedDifference, Math.Abs(result.ReconciliationDifferencePounds));
        Assert.Equal(expectedWarning, result.HasReconciliationWarning);
    }

    [Theory]
    [InlineData("900L", "Juice", 900)]
    [InlineData("650l", "Juice", 650)]
    [InlineData("WP", null, null)]
    public void PackCodeClassification_HandlesNumericLCodes(
        string code,
        string? category,
        int? pounds)
    {
        var result = PackoutReportParser.ClassifyPackCode(code);

        Assert.Equal(category, result.ProductCategory);
        Assert.Equal(pounds is null ? null : (decimal?)pounds.Value, result.NetWeightPounds);
    }

    [Fact]
    public async Task CsvParser_ReturnsParsedMetadataWithoutOriginalBytes()
    {
        var parser = new PackoutReportParser(NullLogger<PackoutReportParser>.Instance);
        var bytes = System.Text.Encoding.UTF8.GetBytes("REG BART US1 80 WP 12\nREG BART NOGR 900L 2");

        var result = await parser.ParseAsync(new("run.csv", "text/csv", bytes), CancellationToken.None);

        Assert.Equal("run.csv", result.FileName);
        Assert.Equal(bytes.Length, result.FileSizeBytes);
        Assert.Equal(64, result.Sha256.Length);
        Assert.Equal(2, result.Lines.Count);
        Assert.DoesNotContain(result.GetType().GetProperties(), x => x.PropertyType == typeof(byte[]));
    }

    [Fact]
    public void Parser_RecognizesNegativeAdjustmentsAsReviewItems()
    {
        var result = Assert.Single(PackoutReportParser.ParseText("REG BART US1 80 WP -2"));

        Assert.Equal(-2m, result.Quantity);
        Assert.True(result.RequiresReview);
    }

    [Fact]
    public async Task HistorySuggestion_NoHistoryHasNoPackoutAndUsesFixedCullMix()
    {
        await using var db = Db();
        var suggestion = await new PackoutHistoricalSuggestionService(db).GetAsync(
            new DateOnly(2026, 7, 29), 2026, "1084", "Bartlett", false, CancellationToken.None);

        Assert.Null(suggestion.PackoutPercent);
        Assert.Equal(0.35m, suggestion.JuiceCullShare);
        Assert.Equal(0.35m, suggestion.PeelerSlicerCullShare);
        Assert.Equal(0.30m, suggestion.WasteCullShare);
    }

    [Fact]
    public async Task HistorySuggestion_UsesExactLotOnlyAfterOneHundredCurrentYearBins()
    {
        await using var db = Db();
        db.PackoutRuns.AddRange(
            Run(1, "1084", "Bartlett", 2026, 60m, 80m, 10m, 6m, 4m),
            Run(2, "1084", "Bartlett", 2026, 40m, 90m, 5m, 3m, 2m),
            Run(3, "OTHER", "Bartlett", 2026, 200m, 50m, 25m, 15m, 10m));
        await db.SaveChangesAsync();

        var suggestion = await new PackoutHistoricalSuggestionService(db).GetAsync(
            new DateOnly(2026, 7, 29), 2026, "1084", "Bartlett", false, CancellationToken.None);

        Assert.Equal("Exact lot history", suggestion.Basis);
        Assert.Equal(new long[] { 1, 2 }, suggestion.RunIds);
        Assert.DoesNotContain(3, suggestion.RunIds);
        Assert.Equal(100m, suggestion.TotalDumpedBins);
    }

    [Fact]
    public async Task HistorySuggestion_FallsBackByVarietyBeforeThreshold()
    {
        await using var db = Db();
        db.PackoutRuns.AddRange(
            Run(1, "1084", "Bartlett", 2026, 99m, 80m, 10m, 6m, 4m),
            Run(2, "OTHER", "Bartlett", 2026, 1m, 60m, 20m, 12m, 8m),
            Run(3, "1084", "Bartlett", 2026, 100m, 20m, 40m, 24m, 16m, organic: true));
        await db.SaveChangesAsync();

        var suggestion = await new PackoutHistoricalSuggestionService(db).GetAsync(
            new DateOnly(2026, 7, 29), 2026, "1084", "Bartlett", false, CancellationToken.None);

        Assert.Equal("Variety fallback history", suggestion.Basis);
        Assert.Equal(new long[] { 1, 2 }, suggestion.RunIds);
    }

    [Fact]
    public void Migration_BackfillsDefectsAndUsesProviderSpecificTypes()
    {
        var migration = Read("src", "CropQc.Data", "Migrations", "20260729165910_AddPackoutProjectionReconciliation.cs");

        Assert.Contains("Defects found", migration);
        Assert.Contains("No defects found", migration);
        Assert.Contains("MigrationProviderTypes.StoreType", migration);
        Assert.Contains("NpgsqlValueGenerationStrategy.IdentityByDefaultColumn", migration);
        Assert.Contains("timestamp with time zone", migration);
        Assert.Contains("boolean", migration);
    }

    [Fact]
    public void UserInterface_RequiresRunIdentityAndExplicitNegativeConfirmation()
    {
        var upload = Read("src", "CropQc.Web", "Views", "BinsRun", "ProjectionOutcome.cshtml");
        var review = Read("src", "CropQc.Web", "Views", "BinsRun", "PackoutReview.cshtml");

        Assert.Contains("name=\"PackingDate\"", upload);
        Assert.Contains("name=\"RunNumber\"", upload);
        Assert.Contains("NegativeQuantityConfirmed", review);
        Assert.Contains("Original uploads are deleted after parsing", review);
        Assert.Contains("Projection Outcome Admin", Read("src", "CropQc.Web", "Services", "PackoutReconciliationService.cs"));
    }

    private static QcSample Sample(SampleType type) => new()
    {
        SampleType = type,
        Status = "In Progress",
        StarchStatus = "Pending",
        PhotoStatus = "Pending",
        EmailStatus = "Not Sent",
        SampleTakenAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static PackoutRun Run(
        long id,
        string lot,
        string variety,
        int cropYear,
        decimal dumpedBins,
        decimal packout,
        decimal juice,
        decimal peeler,
        decimal waste,
        bool organic = false) => new()
        {
            Id = id,
            RunProjectionId = id,
            RunProjection = new RunProjection
            {
                Id = id,
                Name = $"Projection {id}",
                Status = RunProjectionStatuses.Ready,
                PlannedRunDate = new DateOnly(2026, 7, 20),
                CropYear = cropYear,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            },
            Status = PackoutRunStatuses.Finalized,
            FacilitySnapshot = "WP",
            PackingDate = new DateOnly(2026, 7, 20),
            RunNumber = (int)id,
            LotNumberSnapshot = lot,
            VarietySnapshot = variety,
            IsOrganicSnapshot = organic,
            CropYearSnapshot = cropYear,
            DumpedBins = dumpedBins,
            PoundsPerBin = 920m,
            DumpedPounds = dumpedBins * 920m,
            ActualPackoutPercent = packout,
            ActualJuicePercent = juice,
            ActualPeelerSlicerPercent = peeler,
            ActualWastePercent = waste,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            FinalizedAt = DateTimeOffset.UtcNow
        };

    private static CropQcDbContext Db()
    {
        var options = new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new CropQcDbContext(options);
    }

    private static string Read(params string[] parts)
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        return File.ReadAllText(Path.Combine([root, .. parts]));
    }
}
