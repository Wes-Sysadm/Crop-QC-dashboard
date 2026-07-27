namespace CropQc.Api.Tests;

public sealed class ReceiptQcRedesignViewTests
{
    [Fact]
    public void ReceiptSample_UsesSharedAutosaveOnePageControls()
    {
        var view = Read("src", "CropQc.Web", "Views", "Samples", "Details.cshtml");

        Assert.Contains("data-autosave-url", view);
        Assert.Contains("field-sample-autosave.js", view);
        Assert.Contains("initializeFruitRowAutosave", view);
        Assert.Contains("data-autosave-row-field=\"Pressure1Lbs\"", view);
        Assert.Contains("data-autosave-row-field=\"StarchScaleValueId\"", view);
        Assert.Contains("data-field-defect-id", view);
        Assert.Contains("data-size-category", view);
        Assert.DoesNotContain("name=\"Rows[@i].SizeCategory\"", view);
        Assert.Contains("Save Now", view);
        Assert.Contains("href=\"/Samples/@Model.Sample.Id/Report\" data-requires-autosave", view);
    }

    [Fact]
    public void ReceiptReportPreview_IsExplicitAndUsesPersistedReportHtml()
    {
        var preview = Read("src", "CropQc.Web", "Views", "Samples", "ReportPreview.cshtml");
        var controller = Read("src", "CropQc.Web", "Controllers", "SamplesController.cs");

        Assert.Contains("Persisted report data", preview);
        Assert.Contains("@Html.Raw(Model.HtmlBody)", preview);
        Assert.Contains("Send History", preview);
        Assert.Contains("[HttpGet(\"{id:long}/Report\")]", controller);
    }

    [Fact]
    public void DetailPages_PlaceFinalSummaryAfterRowsTrendsAndPhotos()
    {
        var field = Read("src", "CropQc.Web", "Views", "FieldSamples", "Details.cshtml");
        var receipt = Read("src", "CropQc.Web", "Views", "Samples", "Details.cshtml");

        Assert.True(field.IndexOf("Sample Photos", StringComparison.Ordinal) < field.IndexOf("Final Sample Summary", StringComparison.Ordinal));
        Assert.True(field.IndexOf("Pressure Trend", StringComparison.Ordinal) < field.IndexOf("Final Sample Summary", StringComparison.Ordinal));
        Assert.True(receipt.IndexOf("Photos / Requirements", StringComparison.Ordinal) < receipt.IndexOf("Final Sample Summary", StringComparison.Ordinal));
    }

    [Fact]
    public void FieldReport_UsesOneCombinedAveragePressureAndSummaryAtEnd()
    {
        var report = Read("src", "CropQc.Web", "Services", "FieldSampleReportService.cs");

        Assert.Contains("\"Average Pressure\", Format(detail.CurrentSummary.AveragePressureLbs", report);
        Assert.DoesNotContain("\"Average Pressure 1\"", report);
        Assert.DoesNotContain("\"Average Pressure 2\"", report);
        Assert.True(report.IndexOf("AppendTrend(html, detail)", StringComparison.Ordinal) < report.IndexOf("AppendCurrentSummary(html, sample, detail, photos)", StringComparison.Ordinal));
    }

    [Fact]
    public void CropYear2026_IsCentralizedAndVisibleOnDashboard()
    {
        var settings = Read("src", "CropQc.Web", "appsettings.json");
        var service = Read("src", "CropQc.Web", "Services", "CropYearService.cs");
        var dashboard = Read("src", "CropQc.Web", "Views", "Home", "Index.cshtml");

        Assert.Contains("\"ActiveYear\": 2026", settings);
        Assert.Contains("ActiveCropYearKey = \"DefaultCropYear\"", service);
        Assert.Contains("Active crop year", dashboard);
    }

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(Path.Combine(parts));
    }
}
