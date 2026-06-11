using CropQc.Web.Models;
using CropQc.Web.Services;

namespace CropQc.Api.Tests;

public sealed class PartialQcRowSaveTests
{
    [Theory]
    [MemberData(nameof(PartialRows))]
    public void FruitRowEntryStatus_TreatsPartialRowsAsInProgress(FruitReadingEditRow row)
    {
        var status = DashboardDataService.GetFruitRowEntryStatus(row);

        Assert.Equal(FruitRowEntryStatus.InProgress, status);
    }

    [Fact]
    public void FruitRowEntryStatus_TreatsRowsWithCompletionFieldsAsComplete()
    {
        var status = DashboardDataService.GetFruitRowEntryStatus(new FruitReadingEditRow
        {
            RowNumber = 1,
            Pressure1Lbs = 11.1m,
            Pressure2Lbs = 12.2m,
            WeightGrams = 185m,
            GradeId = 2
        });

        Assert.Equal(FruitRowEntryStatus.Complete, status);
    }

    [Fact]
    public void FruitRowEntryStatus_TreatsBlankRowsAsEmpty()
    {
        var status = DashboardDataService.GetFruitRowEntryStatus(new FruitReadingEditRow { RowNumber = 1 });

        Assert.Equal(FruitRowEntryStatus.Empty, status);
    }

    [Fact]
    public void SaveLogic_DoesNotContainOldPartialRowBlockingMessage()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var apiService = File.ReadAllText(FindRepositoryFile("src", "CropQc.Api", "Services", "QcFruitReadingService.cs"));

        Assert.DoesNotContain("partially entered", service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Completed rows require Pressure1Lbs, Pressure2Lbs, WeightGrams, and GradeId", apiService, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StarchSave_AllowsStarchOnlyRows()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Samples", "Starch.cshtml"));

        Assert.DoesNotContain("reading is null || !reading.IsCompleted", service, StringComparison.Ordinal);
        Assert.Contains("reading is null && submittedRow.StarchScaleValueId is null", service, StringComparison.Ordinal);
        Assert.DoesNotContain("disabled=\"@(!row.IsCompleted", view, StringComparison.Ordinal);
        Assert.Contains("Starch values can be saved at any time", view, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiPartialUpsert_PreservesExistingValuesWhenFieldsAreOmitted()
    {
        var apiService = File.ReadAllText(FindRepositoryFile("src", "CropQc.Api", "Services", "QcFruitReadingService.cs"));

        Assert.Contains("if (request.Pressure1Lbs is not null)", apiService, StringComparison.Ordinal);
        Assert.Contains("if (request.Pressure2Lbs is not null)", apiService, StringComparison.Ordinal);
        Assert.Contains("if (request.WeightGrams is not null)", apiService, StringComparison.Ordinal);
        Assert.Contains("if (request.GradeId is not null)", apiService, StringComparison.Ordinal);
        Assert.Contains("if (request.StarchScaleValueId is not null)", apiService, StringComparison.Ordinal);
        Assert.DoesNotContain("""
        reading.WeightGrams = request.WeightGrams;
        reading.GradeId = request.GradeId;
        reading.StarchScaleValueId = request.StarchScaleValueId;
        """, apiService, StringComparison.Ordinal);
    }

    public static IEnumerable<object[]> PartialRows()
    {
        yield return [new FruitReadingEditRow { RowNumber = 1, Pressure1Lbs = 11.1m }];
        yield return [new FruitReadingEditRow { RowNumber = 1, Pressure2Lbs = 12.2m }];
        yield return [new FruitReadingEditRow { RowNumber = 1, Pressure1Lbs = 11.1m, Pressure2Lbs = 12.2m }];
        yield return [new FruitReadingEditRow { RowNumber = 1, WeightGrams = 185m }];
        yield return [new FruitReadingEditRow { RowNumber = 1, GradeId = 2 }];
        yield return [new FruitReadingEditRow { RowNumber = 1, StarchScaleValueId = 3 }];
        yield return [new FruitReadingEditRow { RowNumber = 1, DefectTypeIds = [4] }];
        yield return [new FruitReadingEditRow { RowNumber = 1, OtherDefectNotes = "Stem puncture" }];
        yield return [new FruitReadingEditRow { RowNumber = 1, WeightGrams = 185m, GradeId = 2 }];
    }

    private static string FindRepositoryFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file {Path.Combine(pathParts)}.");
    }
}
