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
        new(ProjectionPlanner, "Projection Planner", "Planning", "/BinsRun?Section=Planner", BinsRun),
        new(ProjectionOutcome, "Projection Outcome", "Planning", "/BinsRun?Section=Planner", BinsRun),
        new(Rooms, "Rooms", "Inventory", "/Rooms"),
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

public sealed class PageAccessRequirement(string areaKey, PageAccessLevel minimumLevel) : IAuthorizationRequirement
{
    public string AreaKey { get; } = areaKey;
    public PageAccessLevel MinimumLevel { get; } = minimumLevel;
}

public interface IUserAccessService
{
    Task<bool> HasAccessAsync(ClaimsPrincipal principal, string areaKey, PageAccessLevel minimumLevel, CancellationToken cancellationToken);
    Task<PageAccessLevel> GetAccessLevelAsync(string? email, string areaKey, CancellationToken cancellationToken);
    Task<IReadOnlyList<UserAccessMatrixRow>> GetMatrixAsync(CancellationToken cancellationToken);
    Task EnsureAccessMatrixAsync(CancellationToken cancellationToken);
    Task<string?> SaveMatrixAsync(UserAccessMatrixForm form, string changedByEmail, CancellationToken cancellationToken);
}

public sealed class UserAccessService(CropQcDbContext dbContext, IConfiguration configuration) : IUserAccessService
{
    private readonly ConcurrentDictionary<string, Lazy<Task<IReadOnlyDictionary<string, string>>>> accessLevelsByEmail =
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
        if (RequiresAllowedEmail(areaKey) && !ConfiguredEmails($"{ConfigPrefix(areaKey)}:AllowedEmails", ApplicationAreas.OwnerEmail).Contains(email, StringComparer.OrdinalIgnoreCase))
        {
            return PageAccessLevel.None;
        }

        var levels = await accessLevelsByEmail.GetOrAdd(
            email,
            normalizedEmail => new Lazy<Task<IReadOnlyDictionary<string, string>>>(
                () => LoadAccessLevelsAsync(normalizedEmail, cancellationToken),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        if (levels.TryGetValue(ApplicationAreas.MasterData, out var masterData)
            && ParseLevel(masterData) == PageAccessLevel.Admin)
        {
            return PageAccessLevel.Admin;
        }
        return levels.TryGetValue(areaKey, out var level) ? ParseLevel(level) : PageAccessLevel.None;
    }

    public async Task<IReadOnlyList<UserAccessMatrixRow>> GetMatrixAsync(CancellationToken cancellationToken)
    {
        await EnsureAccessMatrixAsync(cancellationToken);
        var users = await dbContext.Users.AsNoTracking()
            .Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .Include(x => x.PageAccesses)
            .OrderBy(x => x.Email)
            .ToListAsync(cancellationToken);

        return users.Select(user => new UserAccessMatrixRow(
            user.Id,
            user.Email,
            user.DisplayName,
            user.IsActive,
            user.UserRoles.OrderBy(x => x.RoleId).Select(x => x.Role.Name).FirstOrDefault() ?? "Viewer",
            ApplicationAreas.All.ToDictionary(
                area => area.Key,
                area => IsOwner(user.Email)
                    || ParseLevel(user.PageAccesses.SingleOrDefault(x => x.AreaKey == ApplicationAreas.MasterData)?.AccessLevel) == PageAccessLevel.Admin
                        ? PageAccessLevel.Admin
                        : ParseLevel(user.PageAccesses.SingleOrDefault(x => x.AreaKey == area.Key)?.AccessLevel))))
            .ToList();
    }

    public async Task EnsureAccessMatrixAsync(CancellationToken cancellationToken)
    {
        await EnsureSchemaAsync(cancellationToken);
        var users = await dbContext.Users
            .Include(x => x.UserRoles).ThenInclude(x => x.Role)
            .Include(x => x.PageAccesses)
            .ToListAsync(cancellationToken);

        foreach (var user in users)
        {
            foreach (var area in ApplicationAreas.All)
            {
                if (user.PageAccesses.Any(x => x.AreaKey == area.Key)) continue;
                var inherited = area.LegacyAreaKey is null
                    ? DefaultForRole(user.UserRoles.Select(x => x.Role.Name), area.Key)
                    : ParseLevel(user.PageAccesses.SingleOrDefault(x => x.AreaKey == area.LegacyAreaKey)?.AccessLevel);
                dbContext.UserPageAccesses.Add(new UserPageAccess
                {
                    UserId = user.Id,
                    AreaKey = area.Key,
                    AccessLevel = PersistedLevel(inherited),
                    UpdatedAt = DateTimeOffset.UtcNow
                });
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<string?> SaveMatrixAsync(UserAccessMatrixForm form, string changedByEmail, CancellationToken cancellationToken)
    {
        await EnsureAccessMatrixAsync(cancellationToken);
        var user = await dbContext.Users.Include(x => x.PageAccesses).SingleOrDefaultAsync(x => x.Id == form.UserId, cancellationToken);
        if (user is null) return "User not found.";
        var changedBy = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Email == changedByEmail, cancellationToken);

        user.IsActive = form.IsActive;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        foreach (var area in ApplicationAreas.All)
        {
            var requested = form.Access.TryGetValue(area.Key, out var raw) ? ParseLevel(raw) : PageAccessLevel.None;
            var existing = user.PageAccesses.SingleOrDefault(x => x.AreaKey == area.Key);
            if (existing is null)
            {
                existing = new UserPageAccess { UserId = user.Id, AreaKey = area.Key, AccessLevel = PageAccessLevel.None.ToString(), UpdatedAt = DateTimeOffset.UtcNow };
                dbContext.UserPageAccesses.Add(existing);
            }

            var old = ParseLevel(existing.AccessLevel);
            if (old == requested) continue;
            existing.AccessLevel = PersistedLevel(requested);
            existing.UpdatedAt = DateTimeOffset.UtcNow;
            existing.UpdatedByUserId = changedBy?.Id;
            dbContext.AuditLogs.Add(new AuditLog
            {
                Action = "update",
                EntityName = "user-page-access",
                EntityKey = $"{user.Id}:{area.Key}",
                UserId = changedBy?.Id,
                BeforeValuesJson = JsonSerializer.Serialize(new { user.Email, Area = area.Key, AccessLevel = old.ToString() }),
                AfterValuesJson = JsonSerializer.Serialize(new { user.Email, Area = area.Key, AccessLevel = requested.ToString() }),
                SourceApplication = "CropQc.Web",
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        accessLevelsByEmail.TryRemove(user.Email, out _);
        return null;
    }

    private async Task<IReadOnlyDictionary<string, string>> LoadAccessLevelsAsync(
        string email,
        CancellationToken cancellationToken)
    {
        var rows = await dbContext.UserPageAccesses.AsNoTracking()
            .Where(x => x.User.Email == email && x.User.IsActive)
            .Select(x => new { x.AreaKey, x.AccessLevel })
            .ToListAsync(cancellationToken);
        return rows.ToDictionary(x => x.AreaKey, x => x.AccessLevel, StringComparer.OrdinalIgnoreCase);
    }

    public static PageAccessLevel DefaultForRole(IEnumerable<string> roles, string areaKey)
    {
        var role = roles.FirstOrDefault() ?? "Viewer";
        if (string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase)) return PageAccessLevel.Admin;
        if (areaKey is ApplicationAreas.Users or ApplicationAreas.Configuration or ApplicationAreas.VarietyColors or ApplicationAreas.Backups or ApplicationAreas.Downloads or ApplicationAreas.DataCleanup or ApplicationAreas.CropYearReview)
        {
            return PageAccessLevel.None;
        }

        if (string.Equals(role, "Manager", StringComparison.OrdinalIgnoreCase))
        {
            return areaKey is ApplicationAreas.QcStations or ApplicationAreas.MasterData or ApplicationAreas.CurrentLots or ApplicationAreas.RoomTransactions
                ? PageAccessLevel.Admin
                : areaKey is ApplicationAreas.BinsRun
                    ? PageAccessLevel.Create
                : PageAccessLevel.Create;
        }

        if (string.Equals(role, "QC User", StringComparison.OrdinalIgnoreCase))
        {
            return areaKey is ApplicationAreas.Dashboard or ApplicationAreas.Receipts or ApplicationAreas.DailyQc or ApplicationAreas.FieldSamples or ApplicationAreas.ReceiptEdit
                ? PageAccessLevel.Create
                : areaKey is ApplicationAreas.Rooms or ApplicationAreas.GrowerLots ? PageAccessLevel.View : PageAccessLevel.None;
        }

        return areaKey is ApplicationAreas.Dashboard or ApplicationAreas.Receipts or ApplicationAreas.DailyQc or ApplicationAreas.FieldSamples ? PageAccessLevel.View : PageAccessLevel.None;
    }

    private async Task EnsureSchemaAsync(CancellationToken cancellationToken)
    {
        var provider = dbContext.Database.ProviderName ?? "";
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "UserPageAccesses" (
                    "Id" integer GENERATED BY DEFAULT AS IDENTITY,
                    "UserId" integer NOT NULL,
                    "AreaKey" character varying(100) NOT NULL,
                    "AccessLevel" character varying(25) NOT NULL,
                    "UpdatedByUserId" integer NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    CONSTRAINT "PK_UserPageAccesses" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_UserPageAccesses_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_UserPageAccesses_UserId_AreaKey" ON "UserPageAccesses" ("UserId", "AreaKey");
                """, cancellationToken);
        }
        else if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'[UserPageAccesses]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [UserPageAccesses] (
                        [Id] int IDENTITY(1,1) NOT NULL,
                        [UserId] int NOT NULL,
                        [AreaKey] nvarchar(100) NOT NULL,
                        [AccessLevel] nvarchar(25) NOT NULL,
                        [UpdatedByUserId] int NULL,
                        [UpdatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_UserPageAccesses_UpdatedAt] DEFAULT SYSDATETIMEOFFSET(),
                        CONSTRAINT [PK_UserPageAccesses] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_UserPageAccesses_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE
                    );
                    CREATE UNIQUE INDEX [IX_UserPageAccesses_UserId_AreaKey] ON [UserPageAccesses] ([UserId], [AreaKey]);
                END
                """, cancellationToken);
        }
    }

    public static PageAccessLevel ParseLevel(string? value) =>
        string.Equals(value, "Edit", StringComparison.OrdinalIgnoreCase)
            ? PageAccessLevel.Create
            : Enum.TryParse<PageAccessLevel>(value, true, out var parsed) ? parsed : PageAccessLevel.None;

    private static string PersistedLevel(PageAccessLevel level) =>
        level == PageAccessLevel.Create ? nameof(PageAccessLevel.Create) : level.ToString();

    private static string? NormalizeEmail(string? email) => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
    public static bool IsOwner(string? email) => string.Equals(NormalizeEmail(email), ApplicationAreas.OwnerEmail, StringComparison.OrdinalIgnoreCase);
    private static bool RequiresAllowedEmail(string areaKey) => areaKey is ApplicationAreas.DataCleanup or ApplicationAreas.CropYearReview;
    private static string ConfigPrefix(string areaKey) => areaKey == ApplicationAreas.CropYearReview ? "CropYearReview" : "DataCleanup";
    private IReadOnlyList<string> ConfiguredEmails(string key, string fallback) => (configuration[key] ?? fallback).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
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
