using System.Reflection;
using CropQc.Web.Models;

namespace CropQc.Api.Tests;

public sealed class PayloadShapeTests
{
    [Fact]
    public void DashboardRoomCards_DoNotExposeHeavyDetailCollections()
    {
        AssertDoesNotExposeHeavyCollections(typeof(RoomSummaryItemViewModel));
        Assert.Contains(nameof(RoomSummaryItemViewModel.CurrentBinsCount), PublicPropertyNames<RoomSummaryItemViewModel>());
        Assert.Contains(nameof(RoomSummaryItemViewModel.VarietyColorSegments), PublicPropertyNames<RoomSummaryItemViewModel>());
    }

    [Fact]
    public void ReceiptListItems_DoNotExposeFullReceiptGraphs()
    {
        AssertDoesNotExposeHeavyCollections(typeof(ReceiptListItemViewModel));
        Assert.Contains("SampleCount", PublicPropertyNames<ReceiptListItemViewModel>());
    }

    [Fact]
    public void DailyQcListItems_DoNotExposeFruitRowsPhotosOrAuditHistory()
    {
        AssertDoesNotExposeHeavyCollections(typeof(SampleListItemViewModel));
        Assert.Contains(nameof(SampleListItemViewModel.CompletedFruitCount), PublicPropertyNames<SampleListItemViewModel>());
    }

    [Fact]
    public void CropYearReviewCards_ExposeRowsButNotRawFruitReadingGraphs()
    {
        AssertDoesNotExposeHeavyCollections(typeof(CropYearReviewGrowerViewModel));
        Assert.Contains(nameof(CropYearReviewGrowerViewModel.Rows), PublicPropertyNames<CropYearReviewGrowerViewModel>());
        Assert.All(typeof(CropYearReviewRowViewModel).GetProperties(BindingFlags.Public | BindingFlags.Instance), property =>
        {
            Assert.DoesNotContain("FruitRead", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Photo", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Audit", property.Name, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static IReadOnlyList<string> PublicPropertyNames<T>() =>
        typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(x => x.Name)
            .ToArray();

    private static void AssertDoesNotExposeHeavyCollections(Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.PropertyType == typeof(string))
            {
                continue;
            }

            Assert.DoesNotContain("FruitRow", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("FruitRead", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Defects", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Photos", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Audit", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("History", property.Name, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ReceiptHistory", property.Name, StringComparison.OrdinalIgnoreCase);
        }
    }
}
