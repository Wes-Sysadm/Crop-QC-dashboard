using System.Security.Claims;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace CropQc.Api.Tests;

public sealed class SiteNavigationRedesignTests
{
    [Fact]
    public void Catalog_HasUniqueKeysRequiredCategoriesAndDeterministicOrder()
    {
        Assert.Equal(SiteNavigationCatalog.Categories.Count, SiteNavigationCatalog.Categories.Select(x => x.Key).Distinct().Count());
        Assert.Equal(SiteNavigationCatalog.Items.Count, SiteNavigationCatalog.Items.Select(x => x.Key).Distinct().Count());
        Assert.Equal(SiteNavigationCatalog.Categories.OrderBy(x => x.SortOrder), SiteNavigationCatalog.Categories);
        Assert.Equal(
            ["dashboard", "qc", "inventory", "receiving", "rooms", "runs", "transfers", "shipments", "growers-reports", "admin"],
            SiteNavigationCatalog.Categories.Select(x => x.Key));
        Assert.DoesNotContain(SiteNavigationCatalog.Items, x => x.Label.Contains("EBS Historical Cleanup", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(SiteNavigationCatalog.Items, x => x.Key == "unmatched-identities");
    }

    [Theory]
    [InlineData("/BinsRun/ActualRuns/2", null, "actual-runs")]
    [InlineData("/BinsRun/Packout/15", null, "actual-runs")]
    [InlineData("/BinsRun", "Planner", "run-planner")]
    [InlineData("/BinsRun", "Transfer", "room-transfers")]
    [InlineData("/BinsRun", "TrueUp", "true-up")]
    [InlineData("/Receipts/123", null, "receipts")]
    [InlineData("/Rooms/19", null, "room-overview")]
    [InlineData("/Admin/VarietyColors", null, "variety-colors")]
    [InlineData("/RunReporting/Growers", null, "grower-progress")]
    public void ActiveRouteMatching_IsDeterministic(string path, string? section, string expectedKey)
    {
        var match = SiteNavigationService.BestMatch(SiteNavigationCatalog.Items, path, Query(section));
        Assert.Equal(expectedKey, match?.Key);
    }

    [Fact]
    public async Task Viewer_SeesOnlyAuthorizedDestinationsAndNoEmptyCategories()
    {
        var access = new FakeAccess(new Dictionary<string, PageAccessLevel>
        {
            [ApplicationAreas.Dashboard] = PageAccessLevel.View,
            [ApplicationAreas.Receipts] = PageAccessLevel.View,
            [ApplicationAreas.Rooms] = PageAccessLevel.View
        });
        var model = await Service(access).BuildAsync(Principal("viewer@example.com"), "/", Query(), "WP", CancellationToken.None);

        Assert.Equal(["dashboard", "receiving", "rooms"], model.Categories.Select(x => x.Key));
        Assert.DoesNotContain(model.Categories, x => x.Items.Count == 0);
        Assert.All(model.Categories.SelectMany(x => x.Items), item => Assert.DoesNotContain("Admin", item.Label));
    }

    [Fact]
    public async Task OperationalUser_GetsRunsWithoutProtectedNeedsReviewOrTrueUp()
    {
        var access = new FakeAccess(new Dictionary<string, PageAccessLevel>
        {
            [ApplicationAreas.BinsRun] = PageAccessLevel.View,
            [ApplicationAreas.ProjectionPlanner] = PageAccessLevel.View,
            [ApplicationAreas.ActualRuns] = PageAccessLevel.View
        });
        var model = await Service(access).BuildAsync(Principal("operator@example.com"), "/BinsRun", Query("Actual"), null, CancellationToken.None);

        Assert.Equal(["run-planner", "actual-runs", "recent-activity"], model.Categories.Single(x => x.Key == "runs").Items.Select(x => x.Key));
        Assert.Equal(["room-transfers"], model.Categories.Single(x => x.Key == "transfers").Items.Select(x => x.Key));
        Assert.DoesNotContain(model.Categories.SelectMany(x => x.Items), x => x.Key is "needs-review" or "true-up");
    }

    [Fact]
    public async Task AdminCatalog_IsFlatGroupedAndOwnerOnlyItemDoesNotLeak()
    {
        var access = new FakeAccess(SiteNavigationCatalog.Items
            .Where(x => x.ApplicationArea is not null)
            .Select(x => x.ApplicationArea!)
            .Distinct()
            .ToDictionary(x => x, _ => PageAccessLevel.Admin));
        var model = await Service(access).BuildAsync(Principal("admin@example.com"), "/Admin/Users", Query(), null, CancellationToken.None);
        var admin = model.Categories.Single(x => x.Key == "admin");

        Assert.Equal(["Access & Devices", "Master Data", "System", "Data Maintenance"], admin.Items.Select(x => x.Group).Distinct());
        Assert.DoesNotContain(admin.Items, x => x.Key == "crop-year-review");
        Assert.Contains(admin.Items, x => x.Key == "backups");
        Assert.Contains(admin.Items, x => x.Key == "data-cleanup");
    }

    [Fact]
    public async Task EndOfDayFill_IsVisibleOnlyWithAnActiveAssignment()
    {
        var access = new FakeAccess(new Dictionary<string, PageAccessLevel> { [ApplicationAreas.Rooms] = PageAccessLevel.View });
        var withoutAssignment = await Service(access, false).BuildAsync(Principal("room@example.com"), "/Rooms", Query(), null, CancellationToken.None);
        var withAssignment = await Service(access, true).BuildAsync(Principal("room@example.com"), "/Rooms", Query(), null, CancellationToken.None);

        Assert.DoesNotContain(withoutAssignment.Categories.SelectMany(x => x.Items), x => x.Key == "end-of-day-fill");
        Assert.Contains(withAssignment.Categories.SelectMany(x => x.Items), x => x.Key == "end-of-day-fill");
    }

    [Theory]
    [InlineData("/BinsRun/ActualRuns/2", "Runs", "Actual Runs", "Actual Run #2")]
    [InlineData("/BinsRun/Packout/15", "Runs", "Actual Runs", "Packout Result")]
    [InlineData("/Receipts/123", "Receiving", "Receipts", "Receipt #123")]
    [InlineData("/FieldSamples/8", "QC", "Field Samples", "Field Sample #8")]
    public async Task Breadcrumbs_ShowLinkedParentsAndCurrentDetail(string path, params string[] labels)
    {
        var model = await Service(FakeAccess.Admin()).BuildAsync(Principal(ApplicationAreas.OwnerEmail), path, Query(), null, CancellationToken.None);

        Assert.Equal(labels, model.Breadcrumbs.Select(x => x.Label));
        Assert.All(model.Breadcrumbs.SkipLast(1), x => Assert.False(x.IsCurrent));
        Assert.True(model.Breadcrumbs[^1].IsCurrent);
        Assert.Null(model.Breadcrumbs[^1].Url);
    }

    [Fact]
    public async Task RoomDetailBreadcrumb_OmitsRedundantOverviewLevel()
    {
        var model = await Service(FakeAccess.Admin()).BuildAsync(Principal(ApplicationAreas.OwnerEmail), "/Rooms/19", Query(), null, CancellationToken.None);
        Assert.Equal(["Rooms", "Room 19"], model.Breadcrumbs.Select(x => x.Label));
    }

    [Fact]
    public async Task MasterDataBreadcrumb_UsesTheSharedHierarchy()
    {
        var model = await Service(FakeAccess.Admin()).BuildAsync(Principal(ApplicationAreas.OwnerEmail), "/MasterData/fruit-profiles", Query(), null, CancellationToken.None);
        Assert.Equal(["Admin", "Master Data", "Fruit Profiles / Varieties"], model.Breadcrumbs.Select(x => x.Label));
    }

    [Fact]
    public async Task VarietyColorsBreadcrumb_RemainsADedicatedAdminDestination()
    {
        var model = await Service(FakeAccess.Admin()).BuildAsync(Principal(ApplicationAreas.OwnerEmail), "/Admin/VarietyColors", Query(), null, CancellationToken.None);

        Assert.Equal(["Admin", "Variety Colors"], model.Breadcrumbs.Select(x => x.Label));
        Assert.True(model.Categories.Single(x => x.Key == "admin").Items.Single(x => x.Key == "variety-colors").IsActive);
    }

    [Fact]
    public async Task Facility_IsPreservedOnlyForCompatibleDestinations()
    {
        var model = await Service(FakeAccess.Admin()).BuildAsync(Principal(ApplicationAreas.OwnerEmail), "/BinsRun", Query("Planner"), "EBS", CancellationToken.None);
        var items = model.Categories.SelectMany(x => x.Items).ToDictionary(x => x.Key);

        Assert.Contains("Facility=EBS", items["run-planner"].Url);
        Assert.Contains("Facility=EBS", items["receipts"].Url);
        Assert.DoesNotContain("Facility=", items["processor-shipments"].Url);
        Assert.DoesNotContain("Facility=", items["configuration"].Url);
    }

    [Fact]
    public async Task ActualRunDetailBreadcrumb_PreservesFacilityOnBothNavigableParents()
    {
        var model = await Service(FakeAccess.Admin()).BuildAsync(
            Principal(ApplicationAreas.OwnerEmail), "/BinsRun/ActualRuns/2", Query(), "WP", CancellationToken.None);

        Assert.Equal(["Runs", "Actual Runs", "Actual Run #2"], model.Breadcrumbs.Select(x => x.Label));
        Assert.Contains("Section=Planner", model.Breadcrumbs[0].Url);
        Assert.Contains("Facility=WP", model.Breadcrumbs[0].Url);
        Assert.Contains("Section=Actual", model.Breadcrumbs[1].Url);
        Assert.Contains("Facility=WP", model.Breadcrumbs[1].Url);
    }

    [Fact]
    public async Task ReceiptDetailBreadcrumb_PreservesFacilityOnNavigableParents()
    {
        var model = await Service(FakeAccess.Admin()).BuildAsync(
            Principal(ApplicationAreas.OwnerEmail), "/Receipts/208", Query(), "EBS", CancellationToken.None);

        Assert.Equal(["Receiving", "Receipts", "Receipt #208"], model.Breadcrumbs.Select(x => x.Label));
        Assert.All(model.Breadcrumbs.Take(2), crumb => Assert.Contains("Facility=EBS", crumb.Url));
    }

    [Fact]
    public async Task RoomDetailBreadcrumb_PreservesFacilityOnRoomsParent()
    {
        var model = await Service(FakeAccess.Admin()).BuildAsync(
            Principal(ApplicationAreas.OwnerEmail), "/Rooms/19", Query(), "WP", CancellationToken.None);

        Assert.Equal(["Rooms", "Room 19"], model.Breadcrumbs.Select(x => x.Label));
        Assert.Contains("Facility=WP", model.Breadcrumbs[0].Url);
    }

    [Theory]
    [InlineData("/BinsRun/Projections/4", "Run Planner")]
    [InlineData("/Inventory/ByVariety/GALA", "Inventory by Variety")]
    public async Task FacilityAwareDetailBreadcrumbs_UseResolvedDestinationUrl(string path, string parentLabel)
    {
        var model = await Service(FakeAccess.Admin()).BuildAsync(
            Principal(ApplicationAreas.OwnerEmail), path, Query(), "WP", CancellationToken.None);
        var parent = Assert.Single(model.Breadcrumbs, x => x.Label == parentLabel);

        Assert.Contains("Facility=WP", parent.Url);
    }

    [Fact]
    public async Task LocalDetailBreadcrumb_DoesNotGainFacility()
    {
        var model = await Service(FakeAccess.Admin()).BuildAsync(
            Principal(ApplicationAreas.OwnerEmail), "/ProcessorShipments/22", Query(), "WP", CancellationToken.None);

        Assert.Equal(["Shipments", "Processor Shipments", "Shipment #22"], model.Breadcrumbs.Select(x => x.Label));
        Assert.All(model.Breadcrumbs.Take(2), crumb => Assert.DoesNotContain("Facility=", crumb.Url));
    }

    [Fact]
    public void UnmatchedIdentities_RemainsAValidPageLocalDestination()
    {
        var import = Read("src", "CropQc.Web", "Views", "OrchardRecipientImports", "Index.cshtml");

        Assert.DoesNotContain(SiteNavigationCatalog.Items, x => x.Key == "unmatched-identities");
        Assert.Contains("href=\"#recent-review-batches\"", import);
        Assert.Contains("id=\"recent-review-batches\"", import);
    }

    [Fact]
    public void ResponsiveMarkup_UsesOneCatalogAndAccessibleControls()
    {
        var layout = Read("src", "CropQc.Web", "Views", "Shared", "_Layout.cshtml");
        var css = Read("src", "CropQc.Web", "wwwroot", "css", "site.css");

        Assert.Contains("siteNavigation.Categories", layout);
        Assert.Contains("data-mobile-menu-button", layout);
        Assert.Contains("data-nav-category", layout);
        Assert.Contains("aria-label=\"Primary navigation\"", layout);
        Assert.Contains("aria-label=\"Breadcrumb\"", layout);
        Assert.Contains("aria-current=\"page\"", layout);
        Assert.Contains("@media (max-width: 1180px)", css);
        Assert.Contains("site-nav-panel-wide", css);
        Assert.Contains("grid-template-columns: minmax(0, 1fr)", css);
    }

    [Fact]
    public void DuplicateModuleNavigationWasRemovedWithoutRemovingPageControls()
    {
        var binsRun = Read("src", "CropQc.Web", "Views", "BinsRun", "Index.cshtml");
        var shipments = Read("src", "CropQc.Web", "Views", "ProcessorShipments", "Index.cshtml");
        var masterData = Read("src", "CropQc.Web", "Views", "MasterData", "Index.cshtml");

        Assert.DoesNotContain("aria-label=\"Bins Run and Transfers sections\"", binsRun);
        Assert.DoesNotContain("aria-label=\"Runs and Transfers operations\"", shipments);
        Assert.DoesNotContain("_MasterDataNavigation", masterData);
        Assert.Contains("aria-label=\"Planning facility\"", binsRun);
        Assert.Contains("ProjectionVisibility", binsRun);
    }

    private static SiteNavigationService Service(FakeAccess access, bool eodAssignment = false) =>
        new(access, new FakeEndOfDayFill(eodAssignment));

    private static ClaimsPrincipal Principal(string email) => new(new ClaimsIdentity(
        [new Claim(ClaimTypes.Email, email), new Claim(ClaimTypes.Name, email)], "Test"));

    private static QueryCollection Query(string? section = null) => section is null
        ? new QueryCollection()
        : new QueryCollection(new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase)
        {
            ["Section"] = section
        });

    private static string Read(params string[] segments) =>
        File.ReadAllText(Path.Combine(RepositoryRoot(), Path.Combine(segments)));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "CropQc.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }

    private sealed class FakeAccess(IReadOnlyDictionary<string, PageAccessLevel> levels) : IUserAccessService
    {
        public static FakeAccess Admin() => new(SiteNavigationCatalog.Items
            .Where(x => x.ApplicationArea is not null)
            .Select(x => x.ApplicationArea!)
            .Distinct()
            .ToDictionary(x => x, _ => PageAccessLevel.Admin));

        public Task<bool> HasAccessAsync(ClaimsPrincipal principal, string areaKey, PageAccessLevel minimumLevel, CancellationToken cancellationToken) =>
            Task.FromResult(levels.GetValueOrDefault(areaKey) >= minimumLevel);
        public Task<PageAccessLevel> GetAccessLevelAsync(string? email, string areaKey, CancellationToken cancellationToken) =>
            Task.FromResult(levels.GetValueOrDefault(areaKey));
        public void InvalidateAll() { }
    }

    private sealed class FakeEndOfDayFill(bool assignment) : IEndOfDayFillService
    {
        public Task<bool> HasActiveAssignmentAsync(string? email, CancellationToken cancellationToken) => Task.FromResult(assignment);
        public Task<bool> HasGroupAssignmentAsync(string? email, int groupId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EndOfDayFillPreviewViewModel> GetPreviewAsync(string? email, int? groupId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EndOfDayFillSendResult> SendAsync(string? email, EndOfDayFillSendForm form, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EndOfDayFillHistoryPageViewModel> GetHistoryAsync(string? email, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<EndOfDayFillHistoryDetailViewModel?> GetHistoryDetailAsync(string? email, long id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
