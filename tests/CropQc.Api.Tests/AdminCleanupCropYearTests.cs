using System.Reflection;
using CropQc.Web.Controllers;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;

namespace CropQc.Api.Tests;

public sealed class AdminCleanupCropYearTests
{
    [Fact]
    public void CropYear_DefaultUsesCentralizedActiveYear2026()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["CropYear:ActiveYear"] = "2026"
        }).Build();

        Assert.Equal(2026, new CropYearService(null!, configuration).GetCurrentCropYear(DateTimeOffset.Parse("2026-08-01T00:00:00-07:00")));
        Assert.Equal(2026, new CropYearService(null!, configuration).GetCurrentCropYear(DateTimeOffset.Parse("2027-07-31T23:00:00-07:00")));
        Assert.Equal(2026, new CropYearService(null!, configuration).GetCurrentCropYear(DateTimeOffset.Parse("2026-06-05T12:00:00-07:00")));
    }

    [Fact]
    public void CropYear_CandidateListSupportsOverlapConfirmation()
    {
        var service = new CropYearService(null!, new ConfigurationBuilder().Build());

        var juneCandidates = service.GetCandidateCropYears(DateTimeOffset.Parse("2026-06-15T12:00:00-07:00"));
        var novemberCandidates = service.GetCandidateCropYears(DateTimeOffset.Parse("2026-11-15T12:00:00-07:00"));

        Assert.Contains(2025, juneCandidates);
        Assert.Contains(2026, juneCandidates);
        Assert.Contains(2025, novemberCandidates);
        Assert.Contains(2026, novemberCandidates);
        Assert.True(service.RequiresConfirmation(DateTimeOffset.Parse("2026-06-15T12:00:00-07:00"), 2024));
    }

    [Fact]
    public void ReceiptsView_HasCropYearFilterAllYearsAndReviewColumns()
    {
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Receipts", "Index.cshtml"));

        Assert.Contains("<select name=\"CropYear\"", view);
        Assert.Contains("All crop years", view);
        Assert.Contains("Model.CurrentCropYear", view);
        Assert.Contains("Confirm Crop Year", view);
        Assert.Contains("receipt.SampleCount", view);
        Assert.Contains("receipt.QcStatus", view);
        Assert.Contains("receipt.LastUpdatedAt", view);
    }

    [Fact]
    public void ReceiptDetail_ShowsAllLinkedSampleReviewActionsAndAdminDelete()
    {
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Receipts", "Details.cshtml"));

        Assert.Contains("sample.SampleType", view);
        Assert.Contains("sample.CompletedFruitCount", view);
        Assert.Contains("sample.AveragePressureLbs", view);
        Assert.Contains("cropqcstation://sample/@sample.Id", view);
        Assert.Contains("/Samples/@sample.Id/Delete", view);
        Assert.Contains("Model.CanDeleteSamples", view);
    }

    [Fact]
    public void SampleDeleteAndDataCleanup_AreAdminOnly()
    {
        AssertActionPolicy<SamplesController>(nameof(SamplesController.Delete), AccessPolicyNames.DailyQcAdmin);
        AssertActionPolicy<SamplesController>(nameof(SamplesController.ConfirmDelete), AccessPolicyNames.DailyQcAdmin);
        AssertActionPolicy<AdminController>(nameof(AdminController.DataCleanup), AccessPolicyNames.DataCleanupAdmin);
        AssertActionPolicy<AdminController>(nameof(AdminController.ExecuteDataCleanup), AccessPolicyNames.DataCleanupAdmin);
    }

    [Fact]
    public void DataCleanup_UsesOnlyTheExplicitPermissionMatrix()
    {
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "AdminController.cs"));
        var navigation = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "SiteNavigationService.cs"));
        Assert.DoesNotContain("DataCleanup:AllowedEmails", controller);
        Assert.DoesNotContain("IsDataCleanupAllowed", controller);
        Assert.Contains("ApplicationAreas.DataCleanup", navigation);
        Assert.Contains("\"data-cleanup\"", navigation);
    }

    [Fact]
    public void CleanupService_RequiresTypedConfirmationAndAuditLogs()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DataCleanupService.cs"));

        Assert.Contains("DELETE TEST DATA", service);
        Assert.Contains("soft-cleanup", service);
        Assert.Contains("hard-purge", service);
        Assert.Contains("AuditLogs.Add", service);
        Assert.Contains("IncludeEmailedSamples", service);
        Assert.Contains("IncludePhotoMetadata", service);
    }

    [Fact]
    public void DashboardService_SoftDeletesSamplesAndHidesDeletedData()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));

        Assert.Contains("SoftDeleteSampleAsync", service);
        Assert.Contains("sample.IsDeleted = true", service);
        Assert.Contains("DeleteReason", service);
        Assert.Contains("Daily QC Admin access is required to delete QC samples.", service);
        Assert.Contains("query = query.Where(x => !x.IsDeleted)", service);
        Assert.Contains("Confirm Crop Year before saving", service);
    }

    [Fact]
    public void Program_EnsuresCleanupColumnsForRenderPostgresAndSqlServer()
    {
        var program = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Program.cs"));

        Assert.Contains("EnsureCleanupColumnsAsync", program);
        Assert.Contains("ALTER TABLE \"QcSamples\" ADD COLUMN IF NOT EXISTS \"IsDeleted\"", program);
        Assert.Contains("IF COL_LENGTH('QcSamples', 'IsDeleted') IS NULL", program);
        Assert.Contains("IX_Receipts_CropYear_IsDeleted", program);
    }

    [Fact]
    public void DashboardCards_LinkToActionableFilteredPages()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var homeView = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "Index.cshtml"));
        var dailyQcView = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "DailyQc", "Index.cshtml"));

        Assert.Contains("/Receipts?Facility={encodedFacility}&DateFilter=today&SampleType=Receiving", service);
        Assert.Contains("/DailyQc?Facility={encodedFacility}&status=ReadyToSend", service);
        Assert.Contains("/DailyQc?Facility={encodedFacility}&status=MissingData", service);
        Assert.Contains("/DailyQc?Facility={encodedFacility}&status=NeedsReview", service);
        Assert.Contains("Samples Ready to Email", service);
        Assert.DoesNotContain("Samples ready to send", service);
        Assert.Contains("card.HelperText", homeView);
        Assert.Contains("ReviewReasons", dailyQcView);
        Assert.Contains("Missing Items", dailyQcView);
    }

    [Fact]
    public void ResponsiveLayout_AvoidsWideQcStationActionTable()
    {
        var css = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "wwwroot", "css", "site.css"));
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Admin", "QcStations.cshtml"));

        Assert.Contains("overflow-x: hidden", css);
        Assert.Contains("responsive-card-grid", css);
        Assert.Contains("station-card", view);
        Assert.Contains("action-bar", view);
        Assert.DoesNotContain("<th>Actions</th>", view);
    }

    private static void AssertActionPolicy<TController>(string actionName, string policy)
    {
        var attributes = typeof(TController)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Single(x => x.Name == actionName)
            .GetCustomAttributes<AuthorizeAttribute>();
        Assert.Contains(attributes, x => x.Policy == policy);
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
