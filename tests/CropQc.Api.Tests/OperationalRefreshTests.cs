namespace CropQc.Api.Tests;

public sealed class OperationalRefreshTests
{
    [Fact]
    public void OperationalPages_ShowRefreshControlsAndLastUpdated()
    {
        var partial = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_PageRefreshControls.cshtml"));
        var dashboard = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "Index.cshtml"));
        var rooms = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "Rooms.cshtml"));
        var room = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "Room.cshtml"));
        var growerLots = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "GrowerLots.cshtml"));
        var receipts = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Receipts", "Index.cshtml"));
        var receiptDetail = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Receipts", "Details.cshtml"));
        var dailyQc = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "DailyQc", "Index.cshtml"));
        var roomBreakdown = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "RoomCountBreakdown.cshtml"));
        var roomInventory = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "RoomInventory", "Index.cshtml"));

        Assert.Contains("data-page-refresh", partial);
        Assert.Contains("Refresh", partial);
        Assert.Contains("Last updated:", partial);
        Assert.Contains("PageRefresh:Enabled", partial);
        Assert.Contains("PageRefresh:IntervalSeconds", partial);
        Assert.Contains("New data may be available. Save or discard your changes to refresh.", partial);
        Assert.Contains("Html.PartialAsync(\"_PageRefreshControls\")", dashboard);
        Assert.Contains("Html.PartialAsync(\"_PageRefreshControls\")", rooms);
        Assert.Contains("Html.PartialAsync(\"_PageRefreshControls\")", room);
        Assert.Contains("Html.PartialAsync(\"_PageRefreshControls\")", growerLots);
        Assert.Contains("Html.PartialAsync(\"_PageRefreshControls\")", receipts);
        Assert.Contains("Html.PartialAsync(\"_PageRefreshControls\")", receiptDetail);
        Assert.Contains("Html.PartialAsync(\"_PageRefreshControls\")", dailyQc);
        Assert.Contains("Html.PartialAsync(\"_PageRefreshControls\")", roomBreakdown);
        Assert.Contains("Html.PartialAsync(\"_PageRefreshControls\")", roomInventory);
    }

    [Fact]
    public void SharedRefreshScript_PreservesFiltersScrollAndProtectsUnsavedEdits()
    {
        var layout = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_Layout.cshtml"));

        Assert.Contains("window.location.reload()", layout);
        Assert.Contains("sessionStorage.setItem(\"cropqc-refresh-scroll\"", layout);
        Assert.Contains("window.scrollTo(0, Number(scroll))", layout);
        Assert.Contains("form[data-dirty='true']", layout);
        Assert.Contains("Refresh paused: unsaved changes.", layout);
        Assert.Contains("Save changes", layout);
        Assert.Contains("Discard changes", layout);
        Assert.Contains("Cancel", layout);
        Assert.Contains("form.requestSubmit()", layout);
        Assert.Contains("bar.dataset.refreshIntervalSeconds", layout);
    }

    [Fact]
    public void DetailPages_AutoRefreshWhenConfigured()
    {
        var room = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "Room.cshtml"));
        var receiptDetail = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Receipts", "Details.cshtml"));
        var growerLots = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "GrowerLots.cshtml"));

        Assert.Contains("ViewData[\"AutoRefresh\"] = \"true\"", room);
        Assert.Contains("ViewData[\"AutoRefresh\"] = \"true\"", receiptDetail);
        Assert.Contains("ViewData[\"AutoRefresh\"] = \"true\"", growerLots);
    }

    [Fact]
    public void SampleDetail_RefreshesQcStationPressureRowsWithoutOverwritingDirtyEdits()
    {
        var sample = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Samples", "Details.cshtml"));
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "SamplesController.cs"));
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));

        Assert.Contains("[HttpGet(\"{id:long}/refresh\")]", controller);
        Assert.Contains("GetSampleRefreshAsync", service);
        Assert.Contains("data-refresh-url=\"/Samples/@Model.Sample.Id/refresh\"", sample);
        Assert.Contains("id=\"refresh-sample-data\">Refresh</button>", sample);
        Assert.Contains("Last updated:", sample);
        Assert.Contains("Updated from QC Station.", sample);
        Assert.Contains("New pressure data is available from QC Station. Save or discard your changes to refresh.", sample);
        Assert.Contains("pendingRefreshAfterSave", sample);
        Assert.Contains("if (dirty)", sample);
        Assert.Contains("PageRefresh:Enabled", sample);
        Assert.Contains("PageRefresh:IntervalSeconds", sample);
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
