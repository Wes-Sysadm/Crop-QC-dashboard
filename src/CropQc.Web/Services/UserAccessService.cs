using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public enum PageAccessLevel
{
    None = 0,
    View = 1,
    Create = 2,
    Edit = Create,
    Admin = 3
}

public sealed record ApplicationArea(string Key, string Name, string Group, string Route, string? LegacyAreaKey = null);

public static class ApplicationAreas
{
    public const string Dashboard = "dashboard";
    public const string DailyQc = "daily-qc";
    public const string FieldSamples = "field-samples";
    public const string Receipts = "receipts";
    public const string ReceiptEdit = "receipt-edit";
    public const string ReceiptDelete = "receipt-delete";
    public const string CurrentLots = "current-lots";
    public const string BinsRun = "bins-run";
    public const string ProcessorShipments = "processor-shipments";
    public const string Rooms = "rooms";
    public const string RoomTransactions = "room-transactions";
    public const string GrowerLots = "grower-lots";
    public const string CropYearReview = "crop-year-review";
    public const string MasterData = "master-data";
    public const string Users = "users";
    public const string QcStations = "qc-stations";
    public const string Downloads = "downloads";
    public const string Configuration = "configuration";
    public const string VarietyColors = "variety-colors";
    public const string Backups = "backups";
    public const string DataCleanup = "data-cleanup";
    public const string QcReports = "qc-reports";
    public const string ProjectionPlanner = "projection-planner";
    public const string ProjectionOutcome = "projection-outcome";
    public const string ActualRuns = "actual-runs";
    public const string PackoutResults = "packout-results";
    public const string HistoricalInventoryCleanup = "historical-inventory-cleanup";
    public const string Transfers = "transfers";
    public const string TrueUp = "true-up";
    public const string Inventory = "inventory";
    public const string OrchardRecipients = "orchard-recipients";
    public const string OrchardManagers = "orchard-managers";
    public const string PermissionMatrix = "permission-matrix";
    public const string Facilities = "facilities";
    public const string Varieties = "varieties";
    public const string Grades = "grades";
    public const string Defects = "defects";
    public const string SizeConfiguration = "size-configuration";
    public const string EmailConfiguration = "email-configuration";
    public const string BackupHistory = "backup-history";
    public const string AuditHistory = "audit-history";
    public const string ImportTools = "import-tools";
    public const string ExportTools = "export-tools";
    public const string OwnerEmail = "wes@fruitandland.com";

    public static readonly IReadOnlyList<ApplicationArea> All =
    [
        new(Dashboard, "Dashboard", "Operations", "/"),
        new(DailyQc, "Receipt QC", "QC", "/DailyQc"),
        new(FieldSamples, "Field Samples", "QC", "/FieldSamples"),
        new(QcReports, "QC Reports", "QC", "/DailyQc", DailyQc),
        new(Receipts, "Receipts", "Operations", "/Receipts"),
        new(CurrentLots, "Current Lots", "Inventory", "/Admin/RoomInventory"),
        new(BinsRun, "Bins Run", "Inventory", "/BinsRun"),
        new(ProcessorShipments, "Processor Shipments", "Operations", "/ProcessorShipments", BinsRun),
        new(ProjectionPlanner, "Projection Planner", "Planning", "/BinsRun?Section=Planner", BinsRun),
        new(ProjectionOutcome, "Planning Projection Reports", "Planning", "/BinsRun?Section=Planner", BinsRun),
        new(ActualRuns, "Actual Runs", "Operations", "/BinsRun?Section=Actual", BinsRun),
        new(PackoutResults, "Packout Results", "Operations", "/BinsRun?Section=Actual", ProjectionOutcome),
        new(HistoricalInventoryCleanup, "EBS Historical Cleanup", "Admin/System", "/Admin/EbsInventoryCleanup", DataCleanup),
        new(Rooms, "Rooms", "Inventory", "/Rooms"),
        new(RoomTransactions, "Room Transactions", "Inventory", "/BinsRun"),
        new(Transfers, "Transfers", "Inventory", "/BinsRun?Section=Transfer", RoomTransactions),
        new(TrueUp, "True Up", "Inventory", "/BinsRun?Section=TrueUp", RoomTransactions),
        new(Inventory, "Inventory", "Inventory", "/Admin/RoomInventory", CurrentLots),
        new(GrowerLots, "Grower Lots", "Inventory", "/GrowerLots/Current"),
        new(CropYearReview, "Crop Year Review", "QC", "/CropYearReview"),
        new(MasterData, "Master Data", "Admin/System", "/MasterData"),
        new(Users, "Users", "Admin/System", "/Admin/Users"),
        new(PermissionMatrix, "Permission Matrix", "Admin/System", "/Admin/Users", Users),
        new(QcStations, "QC Stations", "Admin/System", "/Admin/QcStations"),
        new(Downloads, "Downloads", "Admin/System", "/Admin/Downloads"),
        new(Configuration, "Configuration", "Admin/System", "/Admin/Configuration"),
        new(VarietyColors, "Variety Colors", "Master Data", "/MasterData/fruit-profiles"),
        new(Backups, "Backups", "Admin/System", "/Admin/Backups"),
        new(OrchardRecipients, "Orchard Recipients", "Admin/System", "/OrchardRecipients", Configuration),
        new(OrchardManagers, "Orchard Managers", "Admin/System", "/OrchardRecipients", Configuration),
        new(Facilities, "Facilities", "Master Data", "/MasterData/warehouses", MasterData),
        new(Varieties, "Varieties", "Master Data", "/MasterData/fruit-profiles", MasterData),
        new(Grades, "Grades", "Master Data", "/MasterData/grades", MasterData),
        new(Defects, "Defects", "Master Data", "/MasterData/defect-types", MasterData),
        new(SizeConfiguration, "Size Configuration", "Master Data", "/MasterData/fruit-size-thresholds", MasterData),
        new(EmailConfiguration, "Email Configuration", "Admin/System", "/Admin/Configuration", Configuration),
        new(BackupHistory, "Backup History", "Admin/System", "/Admin/Backups", Backups),
        new(AuditHistory, "Audit History", "Admin/System", "/MasterData/audit-logs", MasterData),
        new(ImportTools, "Import Tools", "Admin/System", "/MasterData", MasterData),
        new(ExportTools, "Export Tools", "Admin/System", "/MasterData", MasterData),
        new(DataCleanup, "Data Cleanup", "Admin/System", "/Admin/DataCleanup")
    ];
}

public static class AccessPolicyNames
{
    public const string DashboardView = "DashboardView";
    public const string DailyQcView = "DailyQcView";
    public const string DailyQcEdit = "DailyQcEdit";
    public const string DailyQcAdmin = "DailyQcAdmin";
    public const string FieldSamplesView = "FieldSamplesView";
    public const string FieldSamplesEdit = "FieldSamplesEdit";
    public const string FieldSamplesAdmin = "FieldSamplesAdmin";
    public const string ReceiptsView = "ReceiptsView";
    public const string ReceiptsEdit = "ReceiptsEdit";
    public const string ReceiptEditEdit = "ReceiptEditEdit";
    public const string ReceiptDeleteAdmin = "ReceiptDeleteAdmin";
    public const string CurrentLotsView = "CurrentLotsView";
    public const string CurrentLotsAdmin = "CurrentLotsAdmin";
    public const string BinsRunView = "BinsRunView";
    public const string BinsRunEdit = "BinsRunEdit";
    public const string BinsRunAdmin = "BinsRunAdmin";
    public const string ProcessorShipmentsView = "ProcessorShipmentsView";
    public const string ProcessorShipmentsEdit = "ProcessorShipmentsEdit";
    public const string ProcessorShipmentsAdmin = "ProcessorShipmentsAdmin";
    public const string RoomsView = "RoomsView";
    public const string RoomTransactionsEdit = "RoomTransactionsEdit";
    public const string RoomTransactionsAdmin = "RoomTransactionsAdmin";
    public const string GrowerLotsView = "GrowerLotsView";
    public const string CropYearReviewView = "CropYearReviewView";
    public const string MasterDataView = "MasterDataView";
    public const string MasterDataEdit = "MasterDataEdit";
    public const string MasterDataAdmin = "MasterDataAdmin";
    public const string UsersAdmin = "UsersAdmin";
    public const string QcStationsView = "QcStationsView";
    public const string QcStationsAdmin = "QcStationsAdmin";
    public const string DownloadsView = "DownloadsView";
    public const string ConfigurationAdmin = "ConfigurationAdmin";
    public const string VarietyColorsView = "VarietyColorsView";
    public const string VarietyColorsAdmin = "VarietyColorsAdmin";
    public const string BackupsAdmin = "BackupsAdmin";
    public const string DataCleanupAdmin = "DataCleanupAdmin";
    public const string ProjectionPlannerView = "ProjectionPlannerView";
    public const string ProjectionPlannerCreate = "ProjectionPlannerCreate";
    public const string ProjectionPlannerAdmin = "ProjectionPlannerAdmin";
    public const string ProjectionOutcomeView = "ProjectionOutcomeView";
    public const string ProjectionOutcomeCreate = "ProjectionOutcomeCreate";
    public const string ProjectionOutcomeAdmin = "ProjectionOutcomeAdmin";
    public const string ActualRunsView = "ActualRunsView";
    public const string ActualRunsCreate = "ActualRunsCreate";
    public const string ActualRunsAdmin = "ActualRunsAdmin";
    public const string PackoutResultsView = "PackoutResultsView";
    public const string PackoutResultsCreate = "PackoutResultsCreate";
    public const string PackoutResultsAdmin = "PackoutResultsAdmin";
    public const string HistoricalInventoryCleanupAdmin = "HistoricalInventoryCleanupAdmin";
    public const string TransfersCreate = "TransfersCreate";
    public const string TransfersAdmin = "TransfersAdmin";
    public const string TrueUpAdmin = "TrueUpAdmin";
    public const string PermissionMatrixAdmin = "PermissionMatrixAdmin";
    public const string OrchardManagersView = "OrchardManagersView";
    public const string OrchardManagersCreate = "OrchardManagersCreate";
    public const string OrchardManagersAdmin = "OrchardManagersAdmin";
    public const string BackupHistoryView = "BackupHistoryView";
    public const string BackupHistoryAdmin = "BackupHistoryAdmin";
    public const string ImportToolsAdmin = "ImportToolsAdmin";
    public const string ExportToolsAdmin = "ExportToolsAdmin";
    public const string EmailConfigurationAdmin = "EmailConfigurationAdmin";
}

public static class BuiltInRoleAccessDefaults
{
    public static IReadOnlyDictionary<string, PageAccessLevel> For(string roleName)
    {
        var access = ApplicationAreas.All.ToDictionary(x => x.Key, _ => PageAccessLevel.None, StringComparer.OrdinalIgnoreCase);
        if (string.Equals(roleName, BuiltInRoleNames.Admin, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var area in ApplicationAreas.All) access[area.Key] = PageAccessLevel.Admin;
            return access;
        }

        Grant(access, PageAccessLevel.View,
            ApplicationAreas.Dashboard, ApplicationAreas.DailyQc, ApplicationAreas.FieldSamples,
            ApplicationAreas.QcReports, ApplicationAreas.Receipts, ApplicationAreas.CurrentLots,
            ApplicationAreas.Rooms, ApplicationAreas.Inventory, ApplicationAreas.GrowerLots,
            ApplicationAreas.ProcessorShipments);

        if (string.Equals(roleName, BuiltInRoleNames.Viewer, StringComparison.OrdinalIgnoreCase)) return access;

        Grant(access, PageAccessLevel.Create,
            ApplicationAreas.DailyQc, ApplicationAreas.FieldSamples, ApplicationAreas.Receipts);

        if (string.Equals(roleName, BuiltInRoleNames.QcTech, StringComparison.OrdinalIgnoreCase)) return access;

        if (string.Equals(roleName, BuiltInRoleNames.QcAdmin, StringComparison.OrdinalIgnoreCase))
        {
            Grant(access, PageAccessLevel.Admin,
                ApplicationAreas.DailyQc, ApplicationAreas.FieldSamples, ApplicationAreas.QcReports,
                ApplicationAreas.QcStations, ApplicationAreas.Varieties, ApplicationAreas.Grades,
                ApplicationAreas.Defects, ApplicationAreas.SizeConfiguration, ApplicationAreas.VarietyColors,
                ApplicationAreas.OrchardRecipients, ApplicationAreas.OrchardManagers);
            access[ApplicationAreas.MasterData] = PageAccessLevel.View;
            return access;
        }

        if (string.Equals(roleName, BuiltInRoleNames.Manager, StringComparison.OrdinalIgnoreCase))
        {
            Grant(access, PageAccessLevel.Admin,
                ApplicationAreas.DailyQc, ApplicationAreas.FieldSamples, ApplicationAreas.QcReports,
                ApplicationAreas.Receipts, ApplicationAreas.CurrentLots, ApplicationAreas.BinsRun,
                ApplicationAreas.Rooms, ApplicationAreas.RoomTransactions, ApplicationAreas.GrowerLots,
                ApplicationAreas.ProjectionPlanner, ApplicationAreas.ProjectionOutcome, ApplicationAreas.ActualRuns,
                ApplicationAreas.PackoutResults, ApplicationAreas.Transfers, ApplicationAreas.TrueUp,
                ApplicationAreas.ProcessorShipments,
                ApplicationAreas.Inventory, ApplicationAreas.MasterData, ApplicationAreas.QcStations,
                ApplicationAreas.OrchardRecipients, ApplicationAreas.OrchardManagers,
                ApplicationAreas.Facilities, ApplicationAreas.Varieties, ApplicationAreas.Grades,
                ApplicationAreas.Defects, ApplicationAreas.SizeConfiguration, ApplicationAreas.VarietyColors,
                ApplicationAreas.ImportTools, ApplicationAreas.ExportTools);
            access[ApplicationAreas.Downloads] = PageAccessLevel.View;
            access[ApplicationAreas.AuditHistory] = PageAccessLevel.View;
        }

        return access;
    }

    private static void Grant(IDictionary<string, PageAccessLevel> access, PageAccessLevel level, params string[] keys)
    {
        foreach (var key in keys) access[key] = level;
    }
}

public sealed class PageAccessRequirement(string areaKey, PageAccessLevel minimumLevel) : IAuthorizationRequirement
{
    public string AreaKey { get; } = areaKey;
    public PageAccessLevel MinimumLevel { get; } = minimumLevel;
}

public interface IUserAccessService
{
    Task<bool> HasAccessAsync(ClaimsPrincipal principal, string areaKey, PageAccessLevel minimumLevel, CancellationToken cancellationToken);
    Task<PageAccessLevel> GetAccessLevelAsync(string? email, string areaKey, CancellationToken cancellationToken);
    void InvalidateAll();
}

public sealed class UserAccessService(CropQcDbContext dbContext, IConfiguration configuration) : IUserAccessService
{
    private readonly ConcurrentDictionary<string, Lazy<Task<RoleAccessState>>> accessLevelsByEmail =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<bool> HasAccessAsync(ClaimsPrincipal principal, string areaKey, PageAccessLevel minimumLevel, CancellationToken cancellationToken)
    {
        var email = principal.FindFirstValue(ClaimTypes.Email);
        return await GetAccessLevelAsync(email, areaKey, cancellationToken) >= minimumLevel;
    }

    public async Task<PageAccessLevel> GetAccessLevelAsync(string? email, string areaKey, CancellationToken cancellationToken)
    {
        email = NormalizeEmail(email);
        if (email is null) return PageAccessLevel.None;
        if (IsOwner(email)) return PageAccessLevel.Admin;

        _ = configuration;
        var state = await accessLevelsByEmail.GetOrAdd(
            email,
            normalizedEmail => new Lazy<Task<RoleAccessState>>(
                () => LoadAccessStateAsync(normalizedEmail, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        if (!state.IsValid) return PageAccessLevel.None;
        if (string.Equals(state.RoleName, BuiltInRoleNames.Admin, StringComparison.OrdinalIgnoreCase)) return PageAccessLevel.Admin;
        if (state.Levels.TryGetValue(areaKey, out var level)) return ParseLevel(level);
        var legacyArea = ApplicationAreas.All.SingleOrDefault(x => x.Key == areaKey)?.LegacyAreaKey;
        return legacyArea is not null && state.Levels.TryGetValue(legacyArea, out var legacyLevel)
            ? ParseLevel(legacyLevel)
            : PageAccessLevel.None;
    }

    public void InvalidateAll() => accessLevelsByEmail.Clear();

    private async Task<RoleAccessState> LoadAccessStateAsync(string email, CancellationToken cancellationToken)
    {
        var assignments = await dbContext.UserRoles.AsNoTracking()
            .Include(x => x.Role).ThenInclude(x => x.PageAccesses)
            .Where(x => x.User.Email == email && x.User.IsActive)
            .ToListAsync(cancellationToken);
        if (assignments.Count != 1 || !assignments[0].Role.IsActive) return RoleAccessState.Invalid;
        var role = assignments[0].Role;
        return new RoleAccessState(
            true,
            role.Name,
            role.PageAccesses.ToDictionary(x => x.AreaKey, x => x.AccessLevel, StringComparer.OrdinalIgnoreCase));
    }

    public static PageAccessLevel ParseLevel(string? value) =>
        string.Equals(value, "Edit", StringComparison.OrdinalIgnoreCase)
            ? PageAccessLevel.Create
            : Enum.TryParse<PageAccessLevel>(value, true, out var parsed) ? parsed : PageAccessLevel.None;

    public static string PersistedLevel(PageAccessLevel level) =>
        level == PageAccessLevel.Create ? nameof(PageAccessLevel.Create) : level.ToString();

    private static string? NormalizeEmail(string? email) => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
    public static bool IsOwner(string? email) => string.Equals(NormalizeEmail(email), ApplicationAreas.OwnerEmail, StringComparison.OrdinalIgnoreCase);

    private sealed record RoleAccessState(bool IsValid, string RoleName, IReadOnlyDictionary<string, string> Levels)
    {
        public static RoleAccessState Invalid { get; } = new(false, "", new Dictionary<string, string>());
    }
}

public sealed class PageAccessAuthorizationHandler(IUserAccessService accessService) : AuthorizationHandler<PageAccessRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PageAccessRequirement requirement)
    {
        if (await accessService.HasAccessAsync(context.User, requirement.AreaKey, requirement.MinimumLevel, CancellationToken.None))
        {
            context.Succeed(requirement);
        }
    }
}
