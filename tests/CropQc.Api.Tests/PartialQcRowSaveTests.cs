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

    public static IEnumerable<object[]> PartialRows()
    {
        yield return [new FruitReadingEditRow { RowNumber = 1, Pressure1Lbs = 11.1m }];
        yield return [new FruitReadingEditRow { RowNumber = 1, Pressure1Lbs = 11.1m, Pressure2Lbs = 12.2m }];
        yield return [new FruitReadingEditRow { RowNumber = 1, WeightGrams = 185m }];
        yield return [new FruitReadingEditRow { RowNumber = 1, GradeId = 2 }];
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
