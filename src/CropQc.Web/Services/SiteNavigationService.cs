using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace CropQc.Web.Services;

public enum NavigationFacilityBehavior
{
    None,
    Preserve
}

public enum NavigationSpecialAccess
{
    None,
    EndOfDayFillAssignment,
    OwnerOnly
}

public sealed record NavigationRouteMatch(
    string Path,
    bool Prefix = false,
    string? Section = null,
    bool DefaultSection = false);

public sealed record NavigationItemDefinition(
    string Key,
    string CategoryKey,
    string Label,
    string Url,
    string? ApplicationArea,
    PageAccessLevel MinimumAccess,
    int SortOrder,
    string? Group = null,
    NavigationFacilityBehavior FacilityBehavior = NavigationFacilityBehavior.None,
    NavigationSpecialAccess SpecialAccess = NavigationSpecialAccess.None,
    params NavigationRouteMatch[] Matches);

public sealed record NavigationCategoryDefinition(string Key, string Label, int SortOrder, string Icon);

public static class SiteNavigationCatalog
{
    public static readonly IReadOnlyList<NavigationCategoryDefinition> Categories =
    [
        new("dashboard", "Dashboard", 10, "dashboard"),
        new("qc", "QC", 20, "qc"),
        new("inventory", "Inventory", 30, "inventory"),
        new("receiving", "Receiving", 40, "receiving"),
        new("rooms", "Rooms", 50, "rooms"),
        new("runs", "Runs", 60, "runs"),
        new("transfers", "Transfers", 70, "transfers"),
        new("shipments", "Shipments", 80, "shipments"),
        new("growers-reports", "Growers & Reports", 90, "reports"),
        new("admin", "Admin", 100, "admin")
    ];

    public static readonly IReadOnlyList<NavigationItemDefinition> Items =
    [
        new("dashboard-home", "dashboard", "Dashboard Home", "/", ApplicationAreas.Dashboard, PageAccessLevel.View, 10,
            FacilityBehavior: NavigationFacilityBehavior.Preserve,
            Matches: [new("/", false)]),
        new("inventory-variety", "dashboard", "Inventory by Variety", "/Inventory/ByVariety", ApplicationAreas.Dashboard, PageAccessLevel.View, 20,
            FacilityBehavior: NavigationFacilityBehavior.Preserve,
            Matches: [new("/Inventory/ByVariety", true)]),

        new("field-samples", "qc", "Field Samples", "/FieldSamples", ApplicationAreas.FieldSamples, PageAccessLevel.View, 10,
            Matches: [new("/FieldSamples", true)]),
        new("receipt-qc", "qc", "Receipt QC", "/DailyQc", ApplicationAreas.DailyQc, PageAccessLevel.View, 20,
            FacilityBehavior: NavigationFacilityBehavior.Preserve,
            Matches: [new("/DailyQc", true), new("/Samples", true)]),

        new("current-room-inventory", "inventory", "Current Room Inventory", "/Admin/RoomInventory", ApplicationAreas.CurrentLots, PageAccessLevel.View, 10,
            Matches: [new("/Admin/RoomInventory", false)]),
        new("inventory-reconciliation", "inventory", "Inventory Reconciliation", "/Admin/RoomInventory/Reconciliation", ApplicationAreas.CurrentLots, PageAccessLevel.Admin, 20,
            Matches: [new("/Admin/RoomInventory/Reconciliation", true)]),

        new("receipts", "receiving", "Receipts", "/Receipts", ApplicationAreas.Receipts, PageAccessLevel.View, 10,
            FacilityBehavior: NavigationFacilityBehavior.Preserve,
            Matches: [new("/Receipts", true)]),
        new("voided-receipts", "receiving", "Voided Receipt Administration", "/Receipts/Admin/Voided", ApplicationAreas.ReceiptDelete, PageAccessLevel.Admin, 20,
            Matches: [new("/Receipts/Admin/Voided", true), new("/Receipts/Admin/Overrides", true)]),

        new("room-overview", "rooms", "Room Overview", "/Rooms", ApplicationAreas.Rooms, PageAccessLevel.View, 10,
            FacilityBehavior: NavigationFacilityBehavior.Preserve,
            Matches: [new("/Rooms", true), new("/Dashboard/Rooms", true)]),
        new("end-of-day-fill", "rooms", "End of Day Fill", "/EndOfDayFill", null, PageAccessLevel.None, 20,
            SpecialAccess: NavigationSpecialAccess.EndOfDayFillAssignment,
            Matches: [new("/EndOfDayFill", true)]),

        new("run-planner", "runs", "Run Planner", "/BinsRun?Section=Planner", ApplicationAreas.ProjectionPlanner, PageAccessLevel.View, 10,
            FacilityBehavior: NavigationFacilityBehavior.Preserve,
            Matches: [new("/BinsRun", false, "Planner", true), new("/BinsRun/Projections", true)]),
        new("actual-runs", "runs", "Actual Runs", "/BinsRun?Section=Actual", ApplicationAreas.ActualRuns, PageAccessLevel.View, 20,
            FacilityBehavior: NavigationFacilityBehavior.Preserve,
            Matches: [new("/BinsRun", false, "Actual"), new("/BinsRun", false, "ActualRun"), new("/BinsRun/ActualRuns", true), new("/BinsRun/Packout", true)]),
        new("recent-activity", "runs", "Recent Activity", "/BinsRun?Section=Activity", ApplicationAreas.BinsRun, PageAccessLevel.View, 30,
            FacilityBehavior: NavigationFacilityBehavior.Preserve,
            Matches: [new("/BinsRun", false, "Activity")]),

        new("room-transfers", "transfers", "Room Transfers", "/BinsRun?Section=Transfer", ApplicationAreas.BinsRun, PageAccessLevel.View, 10,
            FacilityBehavior: NavigationFacilityBehavior.Preserve,
            Matches: [new("/BinsRun", false, "Transfer")]),
        new("true-up", "transfers", "True Up", "/BinsRun?Section=TrueUp", ApplicationAreas.TrueUp, PageAccessLevel.Admin, 20,
            FacilityBehavior: NavigationFacilityBehavior.Preserve,
            Matches: [new("/BinsRun", false, "TrueUp")]),

        new("processor-shipments", "shipments", "Processor Shipments", "/ProcessorShipments", ApplicationAreas.ProcessorShipments, PageAccessLevel.View, 10,
            Matches: [new("/ProcessorShipments", true)]),

        new("grower-lots", "growers-reports", "Grower Lots", "/GrowerLots/Current", ApplicationAreas.GrowerLots, PageAccessLevel.View, 10, "Growers",
            FacilityBehavior: NavigationFacilityBehavior.Preserve,
            Matches: [new("/GrowerLots/Current", true)]),
        new("grower-progress", "growers-reports", "Grower & Lot Progress", "/RunReporting/Growers?Facility=All", ApplicationAreas.BinsRun, PageAccessLevel.View, 20, "Growers",
            Matches: [new("/RunReporting/Growers", true)]),
        new("run-totals", "growers-reports", "Run Totals", "/BinsRun?Section=RunTotals&ReportFacility=WP", ApplicationAreas.BinsRun, PageAccessLevel.View, 30, "Reports",
            FacilityBehavior: NavigationFacilityBehavior.Preserve,
            Matches: [new("/BinsRun", false, "RunTotals")]),
        new("needs-review", "growers-reports", "Needs Review", "/BinsRun?Section=NeedsReview", ApplicationAreas.BinsRun, PageAccessLevel.Edit, 40, "Reports",
            Matches: [new("/BinsRun", false, "NeedsReview")]),

        new("users", "admin", "Users", "/Admin/Users", ApplicationAreas.Users, PageAccessLevel.Admin, 10, "Access & Devices",
            Matches: [new("/Admin/Users", true)]),
        new("qc-stations", "admin", "QC Stations", "/Admin/QcStations", ApplicationAreas.QcStations, PageAccessLevel.View, 20, "Access & Devices",
            Matches: [new("/Admin/QcStations", true)]),

        new("master-data", "admin", "Master Data Home", "/MasterData", ApplicationAreas.MasterData, PageAccessLevel.View, 100, "Master Data",
            Matches: [new("/MasterData", false)]),
        new("fruit-profiles", "admin", "Fruit Profiles / Varieties", "/MasterData/fruit-profiles", ApplicationAreas.Varieties, PageAccessLevel.View, 110, "Master Data",
            Matches: [new("/MasterData/fruit-profiles", true)]),
        new("canonical-growers", "admin", "Growers", "/MasterData/canonical-growers", ApplicationAreas.MasterData, PageAccessLevel.View, 120, "Master Data",
            Matches: [new("/MasterData/canonical-growers", true)]),
        new("orchard-blocks", "admin", "Orchards / Blocks", "/MasterData/orchard-blocks", ApplicationAreas.MasterData, PageAccessLevel.View, 130, "Master Data",
            Matches: [new("/MasterData/orchard-blocks", true), new("/MasterData/OrchardIdentity", true)]),
        new("eod-groups", "admin", "End of Day Fill Groups", "/MasterData/end-of-day-fill-groups", ApplicationAreas.MasterData, PageAccessLevel.Admin, 140, "Master Data",
            Matches: [new("/MasterData/end-of-day-fill-groups", true)]),
        new("treatment-chemicals", "admin", "Treatment Chemicals", "/MasterData/treatment-chemicals", ApplicationAreas.MasterData, PageAccessLevel.View, 150, "Master Data",
            Matches: [new("/MasterData/treatment-chemicals", true)]),
        new("qc-recipients", "admin", "QC Recipients", "/Admin/OrchardRecipients", ApplicationAreas.OrchardManagers, PageAccessLevel.View, 160, "Master Data",
            Matches: [new("/Admin/OrchardRecipients", true)]),
        new("manager-import", "admin", "Manager Import", "/Admin/OrchardRecipientImports", ApplicationAreas.ImportTools, PageAccessLevel.Admin, 170, "Master Data",
            Matches: [new("/Admin/OrchardRecipientImports", true)]),
        new("commercial-packs", "admin", "Commercial Packs", "/Admin/CommercialPacks", ApplicationAreas.MasterData, PageAccessLevel.Admin, 180, "Master Data",
            Matches: [new("/Admin/CommercialPacks", true)]),
        new("variety-colors", "admin", "Variety Colors", "/Admin/VarietyColors", ApplicationAreas.VarietyColors, PageAccessLevel.View, 190, "Master Data",
            Matches: [new("/Admin/VarietyColors", true)]),

        new("configuration", "admin", "Configuration", "/Admin/Configuration", ApplicationAreas.EmailConfiguration, PageAccessLevel.Admin, 200, "System",
            Matches: [new("/Admin/Configuration", true)]),
        new("downloads", "admin", "Downloads", "/Admin/Downloads", ApplicationAreas.Downloads, PageAccessLevel.View, 210, "System",
            Matches: [new("/Admin/Downloads", true)]),
        new("backups", "admin", "Backups", "/Admin/Backups", ApplicationAreas.BackupHistory, PageAccessLevel.View, 220, "System",
            Matches: [new("/Admin/Backups", true)]),

        new("crop-year-review", "admin", "Crop Year Review", "/CropYearReview", ApplicationAreas.CropYearReview, PageAccessLevel.View, 300, "Data Maintenance",
            SpecialAccess: NavigationSpecialAccess.OwnerOnly,
            Matches: [new("/CropYearReview", true)]),
        new("audit-history", "admin", "Audit History", "/MasterData/audit-logs", ApplicationAreas.AuditHistory, PageAccessLevel.View, 310, "Data Maintenance",
            Matches: [new("/MasterData/audit-logs", true)]),
        new("data-cleanup", "admin", "Data Cleanup", "/Admin/DataCleanup", ApplicationAreas.DataCleanup, PageAccessLevel.Admin, 320, "Data Maintenance",
            Matches: [new("/Admin/DataCleanup", true)])
    ];
}

public sealed record SiteNavigationItemViewModel(
    string Key,
    string Label,
    string Url,
    string? Group,
    bool IsActive);

public sealed record SiteNavigationCategoryViewModel(
    string Key,
    string Label,
    string Icon,
    string Url,
    bool IsActive,
    IReadOnlyList<SiteNavigationItemViewModel> Items);

public sealed record BreadcrumbViewModel(string Label, string? Url, bool IsCurrent);

public sealed record SiteNavigationViewModel(
    IReadOnlyList<SiteNavigationCategoryViewModel> Categories,
    IReadOnlyList<BreadcrumbViewModel> Breadcrumbs);

public interface ISiteNavigationService
{
    Task<SiteNavigationViewModel> BuildAsync(
        ClaimsPrincipal principal,
        PathString path,
        IQueryCollection query,
        string? facility,
        CancellationToken cancellationToken);
}

public sealed class SiteNavigationService(
    IUserAccessService userAccess,
    IEndOfDayFillService endOfDayFill) : ISiteNavigationService
{
    public async Task<SiteNavigationViewModel> BuildAsync(
        ClaimsPrincipal principal,
        PathString path,
        IQueryCollection query,
        string? facility,
        CancellationToken cancellationToken)
    {
        var visible = new List<NavigationItemDefinition>();
        foreach (var definition in SiteNavigationCatalog.Items.OrderBy(x => x.SortOrder))
        {
            if (await CanViewAsync(definition, principal, cancellationToken))
            {
                visible.Add(definition);
            }
        }

        var normalizedPath = NormalizePath(path);
        var active = BestMatch(visible, normalizedPath, query);
        var categories = SiteNavigationCatalog.Categories
            .OrderBy(x => x.SortOrder)
            .Select(category =>
            {
                var items = visible.Where(x => x.CategoryKey == category.Key)
                    .OrderBy(x => x.SortOrder)
                    .Select(item => new SiteNavigationItemViewModel(
                        item.Key,
                        item.Label,
                        ResolveDestinationUrl(item, facility),
                        item.Group,
                        item.Key == active?.Key))
                    .ToList();
                return items.Count == 0
                    ? null
                    : new SiteNavigationCategoryViewModel(
                        category.Key,
                        category.Label,
                        category.Icon,
                        items[0].Url,
                        items.Any(x => x.IsActive),
                        items);
            })
            .Where(x => x is not null)
            .Cast<SiteNavigationCategoryViewModel>()
            .ToList();

        return new SiteNavigationViewModel(categories, BuildBreadcrumbs(categories, active, normalizedPath));
    }

    private async Task<bool> CanViewAsync(
        NavigationItemDefinition definition,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        if (definition.SpecialAccess == NavigationSpecialAccess.OwnerOnly
            && !UserAccessService.IsOwner(principal.FindFirstValue(ClaimTypes.Email)))
        {
            return false;
        }

        if (definition.SpecialAccess == NavigationSpecialAccess.EndOfDayFillAssignment)
        {
            return await endOfDayFill.HasActiveAssignmentAsync(
                principal.FindFirstValue(ClaimTypes.Email),
                cancellationToken);
        }

        return definition.ApplicationArea is not null
            && await userAccess.HasAccessAsync(principal, definition.ApplicationArea, definition.MinimumAccess, cancellationToken);
    }

    public static NavigationItemDefinition? BestMatch(
        IEnumerable<NavigationItemDefinition> definitions,
        string path,
        IQueryCollection query) =>
        definitions
            .SelectMany(item => item.Matches.Select(match => new { Item = item, Match = match }))
            .Where(candidate => Matches(candidate.Match, path, query))
            .OrderByDescending(candidate => candidate.Match.Path.Length)
            .ThenByDescending(candidate => candidate.Match.Section is not null)
            .ThenBy(candidate => candidate.Item.SortOrder)
            .Select(candidate => candidate.Item)
            .FirstOrDefault();

    private static bool Matches(NavigationRouteMatch match, string path, IQueryCollection query)
    {
        var pathMatches = match.Prefix
            ? path.Equals(match.Path, StringComparison.OrdinalIgnoreCase)
                || path.StartsWith(match.Path + "/", StringComparison.OrdinalIgnoreCase)
            : path.Equals(match.Path, StringComparison.OrdinalIgnoreCase);
        if (!pathMatches) return false;

        var section = query.FirstOrDefault(x => x.Key.Equals("section", StringComparison.OrdinalIgnoreCase)).Value.FirstOrDefault();
        if (match.DefaultSection)
        {
            return string.IsNullOrWhiteSpace(section)
                || string.Equals(section, match.Section, StringComparison.OrdinalIgnoreCase);
        }

        return match.Section is null || string.Equals(section, match.Section, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<BreadcrumbViewModel> BuildBreadcrumbs(
        IReadOnlyList<SiteNavigationCategoryViewModel> categories,
        NavigationItemDefinition? active,
        string path)
    {
        if (active is null || path.Equals("/Login", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/AccessDenied", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/Error", StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        var category = categories.Single(x => x.Key == active.CategoryKey);
        var activeItem = category.Items.Single(x => x.Key == active.Key);
        if (active.Key == "dashboard-home" && path == "/")
        {
            return [new("Dashboard", null, true)];
        }

        var crumbs = new List<BreadcrumbViewModel>
        {
            new(category.Label, category.Url, false)
        };

        var detail = DetailBreadcrumb(active, path);
        if (NeedsMasterDataParent(active))
        {
            if (active.Key == "master-data")
            {
                crumbs.Add(new("Master Data", null, true));
                return crumbs;
            }

            var masterDataUrl = category.Items.SingleOrDefault(x => x.Key == "master-data")?.Url ?? "/MasterData";
            crumbs.Add(new("Master Data", masterDataUrl, false));
        }

        if (detail is null)
        {
            crumbs.Add(new(active.Label, null, true));
            return crumbs;
        }

        if (!(active.Key == "room-overview" && IsNumericDetail(path, "/Rooms/")))
        {
            crumbs.Add(new(active.Label, activeItem.Url, false));
        }
        crumbs.Add(new(detail, null, true));
        return crumbs;
    }

    private static bool NeedsMasterDataParent(NavigationItemDefinition item) =>
        item.CategoryKey == "admin"
        && item.Group == "Master Data"
        && item.Key != "variety-colors";

    private static string? DetailBreadcrumb(NavigationItemDefinition active, string path)
    {
        if (active.Key == "receipts" && NumericSegment(path, "/Receipts/") is { } receiptId)
            return $"Receipt #{receiptId}";
        if (active.Key == "room-overview" && NumericSegment(path, "/Rooms/") is { } roomId)
            return $"Room {roomId}";
        if (active.Key == "actual-runs" && NumericSegment(path, "/BinsRun/ActualRuns/") is { } actualRunId)
            return $"Actual Run #{actualRunId}";
        if (active.Key == "actual-runs" && NumericSegment(path, "/BinsRun/Packout/") is not null)
            return "Packout Result";
        if (active.Key == "run-planner" && NumericSegment(path, "/BinsRun/Projections/") is { } projectionId)
            return $"Projection #{projectionId}";
        if (active.Key == "processor-shipments" && NumericSegment(path, "/ProcessorShipments/") is { } shipmentId)
            return $"Shipment #{shipmentId}";
        if (active.Key == "field-samples" && NumericSegment(path, "/FieldSamples/") is { } fieldSampleId)
            return $"Field Sample #{fieldSampleId}";
        if (active.Key == "receipt-qc" && NumericSegment(path, "/Samples/") is { } sampleId)
            return $"Sample #{sampleId}";
        if (active.Key == "end-of-day-fill" && NumericSegment(path, "/EndOfDayFill/History/") is { } historyId)
            return $"Report #{historyId}";
        if (active.Key == "end-of-day-fill" && path.Equals("/EndOfDayFill/History", StringComparison.OrdinalIgnoreCase))
            return "History";
        if (active.Key == "inventory-variety" && path.StartsWith("/Inventory/ByVariety/", StringComparison.OrdinalIgnoreCase))
            return Uri.UnescapeDataString(path["/Inventory/ByVariety/".Length..]);
        return null;
    }

    private static bool IsNumericDetail(string path, string prefix) => NumericSegment(path, prefix) is not null;

    private static string? NumericSegment(string path, string prefix)
    {
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var segment = path[prefix.Length..].Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return long.TryParse(segment, out _) ? segment : null;
    }

    private static string ResolveDestinationUrl(NavigationItemDefinition item, string? facility) =>
        item.FacilityBehavior == NavigationFacilityBehavior.Preserve && !string.IsNullOrWhiteSpace(facility)
            ? QueryHelpers.AddQueryString(item.Url, "Facility", facility)
            : item.Url;

    private static string NormalizePath(PathString path)
    {
        var value = path.Value ?? "/";
        if (value.Length > 1) value = value.TrimEnd('/');
        return value;
    }
}
