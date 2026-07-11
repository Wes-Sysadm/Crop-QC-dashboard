namespace CropQc.Api.Tests;

public sealed class DashboardOperationalQcStatsTests
{
    [Fact]
    public void DashboardCardsExposeOperationalQcStatistics()
    {
        var model = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Models", "DashboardViewModels.cs"));
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "Index.cshtml"));

        Assert.Contains("RoomCapacityBins", model);
        Assert.Contains("PercentFull", model);
        Assert.Contains("ReceivingStarchRepresentedBins", model);
        Assert.Contains("ReceivingPressureRepresentedBins", model);
        Assert.Contains("LatestPressureLbs", model);
        Assert.Contains("PressureChangeRepresentedBins", model);
        Assert.Contains("PressureStandardDeviationRepresentedBins", model);
        Assert.Contains("Current bins / capacity", view);
        Assert.Contains("Room fill", view);
        Assert.Contains("Receiving starch", view);
        Assert.Contains("Receiving pressure", view);
        Assert.Contains("Latest pressure", view);
        Assert.Contains("30-day pressure change", view);
        Assert.Contains("Latest pressure SD", view);
    }

    [Fact]
    public void DashboardQcSummaryUsesCurrentInventoryWeightsAndCoverage()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));

        Assert.Contains("BuildDashboardRoomQcSummariesAsync", service);
        Assert.Contains("row.QcSample.SampleType.Name", service);
        Assert.Contains("\"Receiving Sample\"", service);
        Assert.Contains("\"Door Sample\"", service);
        Assert.Contains("\"Lot Sample\"", service);
        Assert.Contains("receivingStarch.Add((starch, lot.CurrentBins))", service);
        Assert.Contains("latestPressure.Add((latestPressureValue, lot.CurrentBins))", service);
        Assert.Contains("WeightedStatistics.NormalizeChangeToThirtyDays", service);
        Assert.Contains("WeightedStatistics.WeightedSampleStandardDeviation", service);
        Assert.Contains("Math.Max(0, totalBins - receivingStarchBins)", service);
        Assert.Contains("Math.Max(0, totalBins - latestPressureBins)", service);
    }

    [Fact]
    public void DashboardQcSummaryAvoidsFullSampleEntityGraphForCardStats()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var start = service.IndexOf("private async Task<IReadOnlyDictionary<int, RoomQcSummary>> BuildDashboardRoomQcSummariesAsync", StringComparison.Ordinal);
        var end = service.IndexOf("private static RoomQcSummary BuildRoomQcSummary", StringComparison.Ordinal);
        var method = service[start..end];

        Assert.Contains("dbContext.QcFruitReadings.AsNoTracking()", service);
        Assert.Contains("new DashboardQcMeasurement", service);
        Assert.DoesNotContain(".Include(", method);
    }

    [Fact]
    public void RoomCapacityIsExistingMasterDataField()
    {
        var entity = File.ReadAllText(FindRepositoryFile("src", "CropQc.Data", "Entities", "MasterDataModels.cs"));
        var admin = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "AdminManagementService.cs"));
        var fields = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "MasterData", "_MasterDataFields.cshtml"));

        Assert.Contains("public int CapacityBins", entity);
        Assert.Contains("Capacity Bins", admin);
        Assert.Contains("name=\"CapacityBins\"", fields);
    }

    private static string FindRepositoryFile(params string[] pathParts)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            var candidate = Path.Combine(new[] { current }.Concat(pathParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = Directory.GetParent(current)?.FullName ?? "";
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, pathParts));
    }
}
