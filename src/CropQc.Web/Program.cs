using CropQc.Data;
using CropQc.Shared.Storage;
using CropQc.Shared.Time;
using CropQc.Web.Auth;
using CropQc.Web.ModelBinding;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Encodings.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
var packoutProcessingOptions = PackoutProcessingOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(packoutProcessingOptions);
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = packoutProcessingOptions.MaximumTotalUploadBytes;
});
builder.Services.AddControllersWithViews(options =>
{
    options.ModelBinderProviders.Insert(0, new PacificDateTimeOffsetModelBinderProvider());
});
ConfigureDataProtection(builder.Services, builder.Configuration);
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});
var googleAuthOptions = GoogleAuthenticationOptions.FromConfiguration(builder.Configuration);
var gmailOptions = CreateGmailOptions(builder.Configuration);
var appEnvironmentOptions = AppEnvironmentOptions.FromConfiguration(builder.Configuration, builder.Environment);
builder.Services.AddSingleton(googleAuthOptions);
builder.Services.AddSingleton(gmailOptions);
builder.Services.AddSingleton(appEnvironmentOptions);
builder.Services.AddSingleton(EmailOptionsFactory.Create(builder.Configuration, builder.Environment.IsProduction()));
builder.Services.AddSingleton(BackupOptions.FromConfiguration(builder.Configuration));
builder.Services.AddSingleton(PerformanceDiagnosticsOptions.FromConfiguration(builder.Configuration, builder.Environment));
builder.Services.AddSingleton<IClock, CropQc.Shared.Time.SystemClock>();
builder.Services.AddSingleton<IBusinessTimeService, PacificBusinessTimeService>();
var authenticationBuilder = builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        var sessionLifetime = TimeSpan.FromDays(googleAuthOptions.SessionDays);
        options.LoginPath = "/Login";
        options.LogoutPath = "/Logout";
        options.AccessDeniedPath = "/AccessDenied";
        options.ExpireTimeSpan = sessionLifetime;
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.Events.OnValidatePrincipal = async context =>
        {
            var email = context.Principal?.FindFirstValue(ClaimTypes.Email);
            if (string.IsNullOrWhiteSpace(email))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            var dbContext = context.HttpContext.RequestServices.GetRequiredService<CropQcDbContext>();
            var isActive = await dbContext.Users.AsNoTracking()
                .AnyAsync(x => x.Email == email && x.IsActive, context.HttpContext.RequestAborted);
            if (!isActive)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };
    });
if (googleAuthOptions.IsGoogleConfigured)
{
    authenticationBuilder.AddGoogle("Google", options =>
    {
        options.ClientId = googleAuthOptions.ClientId!;
        options.ClientSecret = googleAuthOptions.ClientSecret!;
        options.CallbackPath = "/signin-google";
        options.SaveTokens = true;
        options.Scope.Add(gmailOptions.SendScope);
        options.AccessType = "offline";
        options.Events.OnCreatingTicket = async context =>
        {
            var sessionLifetime = TimeSpan.FromDays(googleAuthOptions.SessionDays);
            var configuredOptions = context.HttpContext.RequestServices.GetRequiredService<GoogleAuthenticationOptions>();
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GoogleAuth");
            var provisioner = context.HttpContext.RequestServices.GetRequiredService<IGoogleUserProvisioningService>();
            var credentialStore = context.HttpContext.RequestServices.GetRequiredService<IGoogleCredentialStore>();
            var email = context.Principal?.FindFirstValue(ClaimTypes.Email);
            var displayName = context.Principal?.FindFirstValue(ClaimTypes.Name);
            var googleSubjectId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!configuredOptions.IsAllowedEmail(email))
            {
                var domain = GoogleAuthenticationOptions.GetEmailDomain(email) ?? "(missing)";
                logger.LogWarning("Google login rejected for {Email}; domain {Domain} is not allowed.", email ?? "(missing)", domain);
                context.Fail("This Google account is not allowed for the Crop QC Dashboard.");
                return;
            }

            ProvisionedUserAccess access;
            try
            {
                access = await provisioner.ProvisionAllowedUserAsync(email!, displayName, googleSubjectId, context.HttpContext.RequestAborted);
            }
            catch (UnauthorizedAccessException ex)
            {
                context.Fail(ex.Message);
                return;
            }

            var identity = (ClaimsIdentity?)context.Principal?.Identity;
            if (identity is not null)
            {
                foreach (var role in access.Roles)
                {
                    identity.AddClaim(new Claim(ClaimTypes.Role, role));
                }
            }

            context.Properties.IsPersistent = true;
            context.Properties.ExpiresUtc = DateTimeOffset.UtcNow.Add(sessionLifetime);
            await credentialStore.SaveFromAuthenticationPropertiesAsync(access.User, context.Properties, context.HttpContext.RequestAborted);
            context.Properties.StoreTokens(Array.Empty<AuthenticationToken>());
        };
        options.Events.OnRemoteFailure = context =>
        {
            context.HandleResponse();
            var message = UrlEncoder.Default.Encode(context.Failure?.Message ?? "Google login failed.");
            context.Response.Redirect($"/Login?error={message}");
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAuthorizationEndpoint = context =>
        {
            var separator = context.RedirectUri.Contains('?', StringComparison.Ordinal) ? '&' : '?';
            var redirectUri = context.RedirectUri.Contains("prompt=", StringComparison.OrdinalIgnoreCase)
                ? context.RedirectUri
                : $"{context.RedirectUri}{separator}prompt=consent";
            context.Response.Redirect(redirectUri);
            return Task.CompletedTask;
        };
    });
}
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.AddPolicy("RequireAuthenticatedUser", policy => policy.RequireAuthenticatedUser());
    AddAccessPolicy(options, AccessPolicyNames.DashboardView, ApplicationAreas.Dashboard, PageAccessLevel.View);
    AddAccessPolicy(options, AccessPolicyNames.DailyQcView, ApplicationAreas.DailyQc, PageAccessLevel.View);
    AddAccessPolicy(options, AccessPolicyNames.DailyQcEdit, ApplicationAreas.DailyQc, PageAccessLevel.Edit);
    AddAccessPolicy(options, AccessPolicyNames.DailyQcAdmin, ApplicationAreas.DailyQc, PageAccessLevel.Admin);
    AddAccessPolicy(options, AccessPolicyNames.FieldSamplesView, ApplicationAreas.FieldSamples, PageAccessLevel.View);
    AddAccessPolicy(options, AccessPolicyNames.FieldSamplesEdit, ApplicationAreas.FieldSamples, PageAccessLevel.Edit);
    AddAccessPolicy(options, AccessPolicyNames.FieldSamplesAdmin, ApplicationAreas.FieldSamples, PageAccessLevel.Admin);
    AddAccessPolicy(options, AccessPolicyNames.ReceiptsView, ApplicationAreas.Receipts, PageAccessLevel.View);
    AddAccessPolicy(options, AccessPolicyNames.ReceiptsEdit, ApplicationAreas.Receipts, PageAccessLevel.Edit);
    AddAccessPolicy(options, AccessPolicyNames.ReceiptEditEdit, ApplicationAreas.Receipts, PageAccessLevel.Create);
    AddAccessPolicy(options, AccessPolicyNames.ReceiptDeleteAdmin, ApplicationAreas.Receipts, PageAccessLevel.Admin);
    AddAccessPolicy(options, AccessPolicyNames.CurrentLotsView, ApplicationAreas.CurrentLots, PageAccessLevel.View);
    AddAccessPolicy(options, AccessPolicyNames.CurrentLotsAdmin, ApplicationAreas.CurrentLots, PageAccessLevel.Admin);
    AddAccessPolicy(options, AccessPolicyNames.BinsRunView, ApplicationAreas.BinsRun, PageAccessLevel.View);
    AddAccessPolicy(options, AccessPolicyNames.BinsRunEdit, ApplicationAreas.BinsRun, PageAccessLevel.Edit);
    AddAccessPolicy(options, AccessPolicyNames.BinsRunAdmin, ApplicationAreas.BinsRun, PageAccessLevel.Admin);
    AddAccessPolicy(options, AccessPolicyNames.RoomsView, ApplicationAreas.Rooms, PageAccessLevel.View);
    AddAccessPolicy(options, AccessPolicyNames.RoomTransactionsEdit, ApplicationAreas.RoomTransactions, PageAccessLevel.Edit);
    AddAccessPolicy(options, AccessPolicyNames.RoomTransactionsAdmin, ApplicationAreas.RoomTransactions, PageAccessLevel.Admin);
    AddAccessPolicy(options, AccessPolicyNames.GrowerLotsView, ApplicationAreas.GrowerLots, PageAccessLevel.View);
    AddAccessPolicy(options, AccessPolicyNames.CropYearReviewView, ApplicationAreas.CropYearReview, PageAccessLevel.View);
    AddAccessPolicy(options, AccessPolicyNames.MasterDataView, ApplicationAreas.MasterData, PageAccessLevel.View);
    AddAccessPolicy(options, AccessPolicyNames.MasterDataEdit, ApplicationAreas.MasterData, PageAccessLevel.Edit);
    AddAccessPolicy(options, AccessPolicyNames.MasterDataAdmin, ApplicationAreas.MasterData, PageAccessLevel.Admin);
    AddAccessPolicy(options, AccessPolicyNames.UsersAdmin, ApplicationAreas.Users, PageAccessLevel.Admin);
    AddAccessPolicy(options, AccessPolicyNames.QcStationsView, ApplicationAreas.QcStations, PageAccessLevel.View);
    AddAccessPolicy(options, AccessPolicyNames.QcStationsAdmin, ApplicationAreas.QcStations, PageAccessLevel.Admin);
    AddAccessPolicy(options, AccessPolicyNames.DownloadsView, ApplicationAreas.Downloads, PageAccessLevel.View);
    AddAccessPolicy(options, AccessPolicyNames.ConfigurationAdmin, ApplicationAreas.Configuration, PageAccessLevel.Admin);
    AddAccessPolicy(options, AccessPolicyNames.VarietyColorsView, ApplicationAreas.VarietyColors, PageAccessLevel.View);
    AddAccessPolicy(options, AccessPolicyNames.VarietyColorsAdmin, ApplicationAreas.VarietyColors, PageAccessLevel.Admin);
    AddAccessPolicy(options, AccessPolicyNames.BackupsAdmin, ApplicationAreas.Backups, PageAccessLevel.Admin);
    AddAccessPolicy(options, AccessPolicyNames.DataCleanupAdmin, ApplicationAreas.DataCleanup, PageAccessLevel.Admin);
    AddAccessPolicy(options, AccessPolicyNames.ProjectionPlannerView, ApplicationAreas.ProjectionPlanner, PageAccessLevel.View);
    AddAccessPolicy(options, AccessPolicyNames.ProjectionPlannerCreate, ApplicationAreas.ProjectionPlanner, PageAccessLevel.Create);
    AddAccessPolicy(options, AccessPolicyNames.ProjectionPlannerAdmin, ApplicationAreas.ProjectionPlanner, PageAccessLevel.Admin);
    AddAccessPolicy(options, AccessPolicyNames.ProjectionOutcomeView, ApplicationAreas.ProjectionOutcome, PageAccessLevel.View);
    AddAccessPolicy(options, AccessPolicyNames.ProjectionOutcomeCreate, ApplicationAreas.ProjectionOutcome, PageAccessLevel.Create);
    AddAccessPolicy(options, AccessPolicyNames.ProjectionOutcomeAdmin, ApplicationAreas.ProjectionOutcome, PageAccessLevel.Admin);
    AddAccessPolicy(options, AccessPolicyNames.ActualRunsView, ApplicationAreas.ActualRuns, PageAccessLevel.View);
    AddAccessPolicy(options, AccessPolicyNames.ActualRunsCreate, ApplicationAreas.ActualRuns, PageAccessLevel.Create);
    AddAccessPolicy(options, AccessPolicyNames.ActualRunsAdmin, ApplicationAreas.ActualRuns, PageAccessLevel.Admin);
    AddAccessPolicy(options, AccessPolicyNames.PackoutResultsView, ApplicationAreas.PackoutResults, PageAccessLevel.View);
    AddAccessPolicy(options, AccessPolicyNames.PackoutResultsCreate, ApplicationAreas.PackoutResults, PageAccessLevel.Create);
    AddAccessPolicy(options, AccessPolicyNames.PackoutResultsAdmin, ApplicationAreas.PackoutResults, PageAccessLevel.Admin);
    AddAccessPolicy(options, AccessPolicyNames.HistoricalInventoryCleanupAdmin, ApplicationAreas.HistoricalInventoryCleanup, PageAccessLevel.Admin);
    AddAccessPolicy(options, AccessPolicyNames.TransfersCreate, ApplicationAreas.Transfers, PageAccessLevel.Create);
    AddAccessPolicy(options, AccessPolicyNames.TransfersAdmin, ApplicationAreas.Transfers, PageAccessLevel.Admin);
    AddAccessPolicy(options, AccessPolicyNames.TrueUpAdmin, ApplicationAreas.TrueUp, PageAccessLevel.Admin);
    AddAccessPolicy(options, AccessPolicyNames.PermissionMatrixAdmin, ApplicationAreas.PermissionMatrix, PageAccessLevel.Admin);
    AddAccessPolicy(options, AccessPolicyNames.OrchardManagersView, ApplicationAreas.OrchardManagers, PageAccessLevel.View);
    AddAccessPolicy(options, AccessPolicyNames.OrchardManagersCreate, ApplicationAreas.OrchardManagers, PageAccessLevel.Create);
    AddAccessPolicy(options, AccessPolicyNames.OrchardManagersAdmin, ApplicationAreas.OrchardManagers, PageAccessLevel.Admin);
    AddAccessPolicy(options, AccessPolicyNames.BackupHistoryView, ApplicationAreas.BackupHistory, PageAccessLevel.View);
    AddAccessPolicy(options, AccessPolicyNames.BackupHistoryAdmin, ApplicationAreas.BackupHistory, PageAccessLevel.Admin);
    AddAccessPolicy(options, AccessPolicyNames.ImportToolsAdmin, ApplicationAreas.ImportTools, PageAccessLevel.Admin);
    AddAccessPolicy(options, AccessPolicyNames.ExportToolsAdmin, ApplicationAreas.ExportTools, PageAccessLevel.Admin);
    AddAccessPolicy(options, AccessPolicyNames.EmailConfigurationAdmin, ApplicationAreas.EmailConfiguration, PageAccessLevel.Admin);
});
builder.Services.AddScoped<PerformanceQueryCounter>();
builder.Services.AddScoped<IPerformanceQueryCounter>(services => services.GetRequiredService<PerformanceQueryCounter>());
builder.Services.AddSingleton<IPerformanceExternalCallCounter, PerformanceExternalCallCounter>();
builder.Services.AddSingleton<IPerformanceRequestMetricSink, BoundedPerformanceRequestMetricSink>();
builder.Services.AddSingleton<IRequestActivityTracker, RequestActivityTracker>();
builder.Services.AddScoped<PerformanceDbCommandInterceptor>();
builder.Services.AddDbContext<CropQcDbContext>((services, options) =>
{
    CropQcDatabase.Configure(
        options,
        builder.Configuration["DATABASE_PROVIDER"] ?? builder.Configuration["Database:Provider"],
        builder.Configuration.GetConnectionString(builder.Configuration["Database:ConnectionStringName"] ?? CropQcDatabase.DefaultConnectionStringName),
        sqlOptions => sqlOptions.CommandTimeout(3));
    options.AddInterceptors(services.GetRequiredService<PerformanceDbCommandInterceptor>());
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddScoped<IDashboardDataService, DashboardDataService>();
builder.Services.AddScoped<IEndOfDayFillService, EndOfDayFillService>();
builder.Services.AddScoped<IEndOfDayFillAdminService, EndOfDayFillAdminService>();
builder.Services.AddScoped<IEndOfDayFillInventorySource, EndOfDayFillInventorySource>();
builder.Services.AddSingleton<IEndOfDayFillWarehouseLabelResolver, EndOfDayFillWarehouseLabelResolver>();
builder.Services.AddScoped<IEndOfDayFillWarehouseConfigurationSyncService, EndOfDayFillWarehouseConfigurationSyncService>();
builder.Services.AddScoped<IGoogleUserProvisioningService, GoogleUserProvisioningService>();
builder.Services.AddScoped<IGoogleCredentialStore, GoogleCredentialStore>();
builder.Services.AddScoped<IQcEmailSender, GmailUserEmailSender>();
builder.Services.AddScoped<IQcPhotoRequirementPolicy, QcPhotoRequirementPolicy>();
builder.Services.AddScoped<IQcSummaryEmailComposer, QcSummaryEmailComposer>();
builder.Services.AddScoped<IQcEmailRecipientResolver, QcEmailRecipientResolver>();
builder.Services.AddScoped<IOrchardRecipientAdminService, OrchardRecipientAdminService>();
builder.Services.AddScoped<IOrchardContactWorkbookParser, OrchardContactWorkbookParser>();
builder.Services.AddScoped<IOrchardContactImportService, OrchardContactImportService>();
builder.Services.AddScoped<IOrchardIdentityResolverService, OrchardIdentityResolverService>();
builder.Services.AddScoped<IOrchardIdentityReconciliationService, OrchardIdentityReconciliationService>();
builder.Services.AddScoped<IMasterDataSeeder, MasterDataSeeder>();
builder.Services.AddScoped<IReceivingExportService, ReceivingExportService>();
builder.Services.AddScoped<IAdminAuthorizationService, AdminAuthorizationService>();
builder.Services.AddScoped<IUserAccessService, UserAccessService>();
builder.Services.AddScoped<IAuthorizationHandler, PageAccessAuthorizationHandler>();
builder.Services.AddScoped<IAdminManagementService, AdminManagementService>();
builder.Services.AddScoped<IRoomInventoryImportService, RoomInventoryImportService>();
builder.Services.AddScoped<IRoomInventoryLedgerQueryService, RoomInventoryLedgerQueryService>();
builder.Services.AddScoped<IRoomInventoryReconciliationService, RoomInventoryReconciliationService>();
builder.Services.AddScoped<IRoomInventoryLossService, RoomInventoryLossService>();
builder.Services.AddScoped<RoomTreatmentService>();
builder.Services.AddScoped<IRoomTreatmentService>(sp => sp.GetRequiredService<RoomTreatmentService>());
builder.Services.AddScoped<IReceivingTreatmentService>(sp => sp.GetRequiredService<RoomTreatmentService>());
builder.Services.AddScoped<IInventoryByVarietyService, InventoryByVarietyService>();
builder.Services.AddScoped<ITreatmentReportAttachmentService, TreatmentReportAttachmentService>();
builder.Services.AddScoped<ITr108859DroppedBinsCorrectionService, Tr108859DroppedBinsCorrectionService>();
builder.Services.AddScoped<IEbsInventoryCleanupService, EbsInventoryCleanupService>();
builder.Services.AddScoped<IInventoryDeductionInvariantService, InventoryDeductionInvariantService>();
builder.Services.AddScoped<IInventoryDiagnosticAcknowledgmentService, InventoryDiagnosticAcknowledgmentService>();
builder.Services.AddScoped<IReceiptInventoryOverrideService, ReceiptInventoryOverrideService>();
builder.Services.AddScoped<IBinsRunService, BinsRunService>();
builder.Services.AddScoped<IRunReportingService, RunReportingService>();
builder.Services.AddScoped<IGrowerLotProgressService, GrowerLotProgressService>();
builder.Services.AddScoped<IRunExpectationService, RunExpectationService>();
builder.Services.AddSingleton<IPackoutSourceAllocationService, PackoutSourceAllocationService>();
builder.Services.AddScoped<IRunProjectionService, RunProjectionService>();
builder.Services.AddScoped<IPackoutReportParser, PackoutReportParser>();
builder.Services.AddScoped<IPackoutFeedbackWorkbookService, PackoutFeedbackWorkbookService>();
builder.Services.AddScoped<IPackoutReconciliationService, PackoutReconciliationService>();
builder.Services.AddSingleton<IPackoutOperationCoordinator, PackoutOperationCoordinator>();
builder.Services.AddScoped<IPackoutHistoricalSuggestionService, PackoutHistoricalSuggestionService>();
builder.Services.AddScoped<IFacilityContextService, FacilityContextService>();
builder.Services.AddScoped<ICommercialPackAdminService, CommercialPackAdminService>();
builder.Services.AddScoped<IEbsDailyBinsEmailService, EbsDailyBinsEmailService>();
builder.Services.AddScoped<IUserAdminService, UserAdminService>();
builder.Services.AddScoped<IQcStationAdminService, QcStationAdminService>();
builder.Services.AddScoped<ICropYearService, CropYearService>();
builder.Services.AddScoped<IDataCleanupService, DataCleanupService>();
builder.Services.AddScoped<IVarietyColorService, VarietyColorService>();
builder.Services.AddSingleton<CanonicalGrowerResolutionCache>();
builder.Services.AddScoped<ICanonicalGrowerService, CanonicalGrowerService>();
builder.Services.AddSingleton<IReviewedGrowerMasterSource, ReviewedGrowerMasterSource>();
builder.Services.AddScoped<IReviewedGrowerMasterSyncService, ReviewedGrowerMasterSyncService>();
builder.Services.AddScoped<ReviewedGrowerLotSyncService>();
builder.Services.AddScoped<IReviewedGrowerLotSyncService>(services => services.GetRequiredService<ReviewedGrowerLotSyncService>());
if (appEnvironmentOptions.IsProduction)
{
    builder.Services.AddScoped<IReviewedGrowerLotPolicy>(services => services.GetRequiredService<ReviewedGrowerLotSyncService>());
}
builder.Services.AddScoped<IFieldSampleService, FieldSampleService>();
builder.Services.AddScoped<IFieldSampleTrendService, FieldSampleTrendService>();
builder.Services.AddScoped<IFieldSampleDeletionService, FieldSampleDeletionService>();
builder.Services.AddScoped<IFieldSampleReportService, FieldSampleReportService>();
builder.Services.AddScoped<IBackupService, BackupService>();
builder.Services.AddScoped<IBackupNotificationService, BackupNotificationService>();
builder.Services.AddScoped<IReceiptPurgeService, ReceiptPurgeService>();
builder.Services.AddScoped<IJuly27ActualRunNormalizationService, July27ActualRunNormalizationService>();
builder.Services.AddScoped<IJuly28ActualRunExpectationBackfillService, July28ActualRunExpectationBackfillService>();
builder.Services.AddHostedService<EbsDailyBinsEmailHostedService>();
builder.Services.AddHostedService<BackupNotificationHostedService>();
builder.Services.AddHostedService<RuntimeMemoryTelemetryHostedService>();
builder.Services.AddSingleton(CreateFileStorageOptions(builder.Configuration));
builder.Services.AddSingleton(CreateGoogleDriveStorageOptions(builder.Configuration));
builder.Services.AddSingleton<IFileStorageService>(services => CreateFileStorageService(
    services.GetRequiredService<FileStorageOptions>(),
    services.GetRequiredService<GoogleDriveStorageOptions>(),
    services.GetRequiredService<ILogger<GoogleDriveStorageService>>(),
    services.GetRequiredService<IPerformanceExternalCallCounter>()));

var app = builder.Build();
LogEmailConfiguration(app);
LogEnvironmentConfiguration(app);
var ensureCreatedOnStartup = app.Configuration.GetValue<bool>("Database:EnsureCreatedOnStartup");
var seedMasterDataOnStartup = app.Configuration.GetValue<bool>("Database:SeedMasterDataOnStartup");
ProductionDatabaseSafety.RejectProductionStartupMutation(
    appEnvironmentOptions.IsProduction,
    ensureCreatedOnStartup,
    seedMasterDataOnStartup);
var isRender = !string.IsNullOrWhiteSpace(app.Configuration["RENDER_EXTERNAL_HOSTNAME"])
    || !string.IsNullOrWhiteSpace(app.Configuration["RENDER_EXTERNAL_URL"]);
var useForwardedHeaders = isRender || app.Configuration.GetValue<bool>("ASPNETCORE_FORWARDEDHEADERS_ENABLED");

if (ensureCreatedOnStartup)
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}

var schemaVerificationCommand = args.FirstOrDefault(
    x => x.StartsWith("--verify-schema=", StringComparison.OrdinalIgnoreCase));
if (schemaVerificationCommand is not null)
{
    var expectedMigration = schemaVerificationCommand[(schemaVerificationCommand.IndexOf('=') + 1)..];
    var schemaIsReady = await DatabaseStartupDiagnostics.VerifyRequiredSchemaAsync(
        app.Services,
        app.Configuration,
        app.Environment,
        expectedMigration);
    var deductionsAreReady = schemaIsReady
        && await VerifyInventoryDeductionReadinessAsync(app.Services);
    Environment.ExitCode = schemaIsReady && deductionsAreReady ? 0 : 1;
    return;
}

if (args.Contains("--verify-inventory-deductions", StringComparer.OrdinalIgnoreCase))
{
    Environment.ExitCode = await VerifyInventoryDeductionReadinessAsync(app.Services) ? 0 : 1;
    return;
}

if (args.Contains(ReviewedGrowerMasterSyncConstants.CommandName, StringComparer.OrdinalIgnoreCase))
{
    static string? ReviewedGrowerCommandValue(string[] commandArgs, string key) =>
        commandArgs.FirstOrDefault(x => x.StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase)) is { } item
            ? item[(item.IndexOf('=') + 1)..]
            : null;
    var backupRunId = long.TryParse(ReviewedGrowerCommandValue(args, "--backup-run-id"), out var parsedBackupRunId)
        ? parsedBackupRunId
        : (long?)null;
    using var syncScope = app.Services.CreateScope();
    var result = await syncScope.ServiceProvider.GetRequiredService<IReviewedGrowerMasterSyncService>().RunAsync(new(
        args.Contains("--apply", StringComparer.OrdinalIgnoreCase),
        args.Contains("--confirm-production", StringComparer.OrdinalIgnoreCase),
        args.Contains("--confirm-disposable-restore", StringComparer.OrdinalIgnoreCase),
        backupRunId,
        ReviewedGrowerCommandValue(args, "--verified-backup-package-sha256"),
        ReviewedGrowerCommandValue(args, "--requested-by") ?? "command",
        ReviewedGrowerCommandValue(args, "--reason") ?? "",
        ReviewedGrowerCommandValue(args, "--expected-target-fingerprint"),
        ReviewedGrowerCommandValue(args, "--expected-protected-fingerprint"),
        ReviewedGrowerCommandValue(args, "--authorization-token")), CancellationToken.None);
    var report = System.Text.Json.JsonSerializer.Serialize(result, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { WriteIndented = true });
    var syncLogger = syncScope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("ReviewedGrowerMasterSyncCommand");
    Console.Out.WriteLine(report);
    if (result.Success)
    {
        syncLogger.LogInformation(
            "Reviewed grower master sync completed. State: {State}; applied: {Applied}; already applied: {AlreadyApplied}; source: {SourceVersion}; rows: {ReviewedRows}",
            result.Preflight.State,
            result.Applied,
            result.AlreadyApplied,
            result.Preflight.SourceVersion,
            result.Preflight.ReviewedRowCount);
    }
    else
    {
        syncLogger.LogError(
            "Reviewed grower master sync failed. State: {State}; source: {SourceVersion}; message: {Message}",
            result.Preflight.State,
            result.Preflight.SourceVersion,
            result.Message);
    }
    Environment.ExitCode = result.Success ? 0 : 1;
    return;
}

if (args.Contains(ReviewedGrowerLotSyncConstants.CommandName, StringComparer.OrdinalIgnoreCase))
{
    static string? ReviewedGrowerLotCommandValue(string[] commandArgs, string key) =>
        commandArgs.FirstOrDefault(x => x.StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase)) is { } item
            ? item[(item.IndexOf('=') + 1)..]
            : null;
    var backupRunId = long.TryParse(ReviewedGrowerLotCommandValue(args, "--backup-run-id"), out var parsedBackupRunId)
        ? parsedBackupRunId
        : (long?)null;
    using var syncScope = app.Services.CreateScope();
    var result = await syncScope.ServiceProvider.GetRequiredService<IReviewedGrowerLotSyncService>().RunAsync(new(
        args.Contains("--apply", StringComparer.OrdinalIgnoreCase),
        args.Contains("--confirm-production", StringComparer.OrdinalIgnoreCase),
        args.Contains("--confirm-disposable-restore", StringComparer.OrdinalIgnoreCase),
        backupRunId,
        ReviewedGrowerLotCommandValue(args, "--verified-backup-package-sha256"),
        ReviewedGrowerLotCommandValue(args, "--requested-by") ?? "command",
        ReviewedGrowerLotCommandValue(args, "--reason") ?? "",
        ReviewedGrowerLotCommandValue(args, "--expected-target-fingerprint"),
        ReviewedGrowerLotCommandValue(args, "--expected-protected-fingerprint"),
        ReviewedGrowerLotCommandValue(args, "--authorization-token")), CancellationToken.None);
    Console.Out.WriteLine(System.Text.Json.JsonSerializer.Serialize(
        result,
        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { WriteIndented = true }));
    Environment.ExitCode = result.Success ? 0 : 1;
    return;
}

if (args.Contains(EndOfDayFillWarehouseConfigurationSyncConstants.CommandName, StringComparer.OrdinalIgnoreCase))
{
    static string? EndOfDayFillWarehouseCommandValue(string[] commandArgs, string key) =>
        commandArgs.FirstOrDefault(x => x.StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase)) is { } item
            ? item[(item.IndexOf('=') + 1)..]
            : null;
    using var syncScope = app.Services.CreateScope();
    var result = await syncScope.ServiceProvider.GetRequiredService<IEndOfDayFillWarehouseConfigurationSyncService>().RunAsync(new(
        args.Contains("--apply", StringComparer.OrdinalIgnoreCase),
        args.Contains("--confirm-production", StringComparer.OrdinalIgnoreCase),
        args.Contains("--confirm-disposable-restore", StringComparer.OrdinalIgnoreCase),
        EndOfDayFillWarehouseCommandValue(args, "--requested-by") ?? "command",
        EndOfDayFillWarehouseCommandValue(args, "--reason") ?? "",
        EndOfDayFillWarehouseCommandValue(args, "--expected-target-fingerprint"),
        EndOfDayFillWarehouseCommandValue(args, "--expected-protected-fingerprint")), CancellationToken.None);
    Console.Out.WriteLine(System.Text.Json.JsonSerializer.Serialize(
        result,
        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { WriteIndented = true }));
    Environment.ExitCode = result.Success ? 0 : 1;
    return;
}

if (args.Contains("--verify-end-of-day-fill-warehouse-previews", StringComparer.OrdinalIgnoreCase))
{
    static string? EndOfDayFillPreviewCommandValue(string[] commandArgs, string key) =>
        commandArgs.FirstOrDefault(x => x.StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase)) is { } item
            ? item[(item.IndexOf('=') + 1)..]
            : null;

    var requestedBy = EndOfDayFillPreviewCommandValue(args, "--requested-by") ?? "";
    using var previewScope = app.Services.CreateScope();
    var previewDb = previewScope.ServiceProvider.GetRequiredService<CropQcDbContext>();
    var previewService = previewScope.ServiceProvider.GetRequiredService<IEndOfDayFillService>();
    var inventorySource = previewScope.ServiceProvider.GetRequiredService<IEndOfDayFillInventorySource>();
    var groups = await previewDb.EndOfDayFillReportGroups.AsNoTracking()
        .Include(x => x.Warehouse)
        .Include(x => x.Rooms)
        .Where(x => x.IsActive)
        .OrderByDescending(x => x.WarehouseId)
        .ToListAsync();
    var previewResults = new List<object>();
    var previewTotal = 0;
    var previewsAreValid = groups.Count == 4;
    foreach (var group in groups)
    {
        var preview = await previewService.GetPreviewAsync(requestedBy, group.Id, CancellationToken.None);
        var roomTotal = preview.RoomSummary.TotalCurrentBins;
        var detailTotal = preview.Rooms.Sum(x => x.Varieties.Sum(v => v.Growers.Sum(g => g.Bins)));
        previewTotal += roomTotal;
        var configuredRooms = group.Rooms.Where(x => x.IsActive).OrderBy(x => x.SortOrder).ThenBy(x => x.Code).ToList();
        var occupiedCapacity = preview.Rooms.Sum(x => x.CapacityBins);
        var configuredCapacity = configuredRooms.Sum(x => x.CapacityBins);
        var previewDataIssues = preview.Issues.Where(x => x.Code != "gmail").ToArray();
        previewsAreValid &= preview.SelectedGroupId == group.Id
            && preview.WarehouseId == group.WarehouseId
            && roomTotal == detailTotal
            && configuredRooms.All(x => x.WarehouseId == group.WarehouseId)
            && preview.RoomSummary.Rooms.Select(x => x.RoomId).SequenceEqual(configuredRooms.Select(x => x.Id))
            && preview.RoomSummary.TotalCapacityBins == configuredCapacity
            && previewDataIssues.Length == 0;
        previewResults.Add(new
        {
            groupId = group.Id,
            groupName = group.Name,
            warehouseId = group.WarehouseId,
            storedWarehouseCode = group.Warehouse.Code,
            storedWarehouseName = group.Warehouse.Name,
            displayedWarehouseLabel = preview.WarehouseLabel,
            operatingFacility = group.Facility,
            configuredRoomCount = configuredRooms.Count,
            configuredRoomCodes = configuredRooms.Select(x => x.Code).ToArray(),
            occupiedRoomCount = preview.Rooms.Count,
            currentBins = roomTotal,
            detailBins = detailTotal,
            occupiedCapacityBins = occupiedCapacity,
            configuredCapacityBins = configuredCapacity,
            capacityBins = preview.RoomSummary.TotalCapacityBins,
            percentFull = preview.RoomSummary.TotalPercentFull,
            dataIssues = previewDataIssues.Select(x => new { x.Code, x.Message, x.RoomId }).ToArray(),
            emailReadinessIssues = preview.Issues.Where(x => x.Code == "gmail").Select(x => new { x.Code, x.Message }).ToArray()
        });
    }

    var includedRoomIds = groups.SelectMany(x => x.Rooms).Where(x => x.IsActive).Select(x => x.Id).Distinct().ToArray();
    var allRoomIds = await previewDb.Rooms.AsNoTracking()
        .Where(x => x.IsActive && new[] { 1, 2, 3, 4 }.Contains(x.WarehouseId))
        .Select(x => x.Id)
        .ToArrayAsync();
    // Every preview above is produced by the authoritative inventory source. Reuse those
    // results instead of running the expensive all-room reconciliation query a second time.
    var includedAuthoritativeTotal = previewTotal;
    var excludedRoomIds = allRoomIds.Except(includedRoomIds).ToArray();
    var excludedAuthoritativeTotal = excludedRoomIds.Length == 0
        ? 0
        : (await inventorySource.GetCurrentLotsAsync(excludedRoomIds, CancellationToken.None)).Sum(x => x.CurrentBins);
    var allRoomAuthoritativeTotal = includedAuthoritativeTotal + excludedAuthoritativeTotal;
    var report = new
    {
        success = previewsAreValid,
        requestedBy,
        previewCount = previewResults.Count,
        previews = previewResults,
        reconciliation = new
        {
            previewTotal,
            includedAuthoritativeTotal,
            allRoomAuthoritativeTotal,
            excludedAuthoritativeTotal,
            authoritativeBasis = "Sum of four independently built authoritative previews plus any excluded-room authoritative inventory.",
            includedRoomCount = includedRoomIds.Length,
            allRoomCount = allRoomIds.Length,
            excludedRoomCount = excludedRoomIds.Length
        }
    };
    Console.Out.WriteLine(System.Text.Json.JsonSerializer.Serialize(
        report,
        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { WriteIndented = true }));
    Environment.ExitCode = previewsAreValid ? 0 : 1;
    return;
}

if (args.Contains(Tr108859DroppedBinsCorrectionConstants.CommandName, StringComparer.OrdinalIgnoreCase))
{
    static string? Tr108859CommandValue(string[] commandArgs, string key) =>
        commandArgs.FirstOrDefault(x => x.StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase)) is { } item
            ? item[(item.IndexOf('=') + 1)..]
            : null;
    var backupRunId = long.TryParse(Tr108859CommandValue(args, "--backup-run-id"), out var parsedBackupRunId) ? parsedBackupRunId : (long?)null;
    using var correctionScope = app.Services.CreateScope();
    var correction = await correctionScope.ServiceProvider.GetRequiredService<ITr108859DroppedBinsCorrectionService>().RunAsync(new(
        args.Contains("--apply", StringComparer.OrdinalIgnoreCase),
        args.Contains("--confirm-production", StringComparer.OrdinalIgnoreCase),
        args.Contains("--confirm-disposable-restore", StringComparer.OrdinalIgnoreCase),
        backupRunId,
        Tr108859CommandValue(args, "--verified-backup-package-sha256"),
        Tr108859CommandValue(args, "--requested-by") ?? "command",
        Tr108859CommandValue(args, "--reason") ?? "",
        Tr108859CommandValue(args, "--expected-target-fingerprint"),
        Tr108859CommandValue(args, "--expected-protected-fingerprint"),
        Tr108859CommandValue(args, "--authorization-token")), CancellationToken.None);
    var report = System.Text.Json.JsonSerializer.Serialize(correction, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { WriteIndented = true });
    var commandLogger = correctionScope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Tr108859DroppedBinsCorrectionCommand");
    if (correction.Success) commandLogger.LogInformation("{CorrectionReport}", report); else commandLogger.LogError("{CorrectionReport}", report);
    Environment.ExitCode = correction.Success ? 0 : 1;
    return;
}

if (args.Contains(July27ActualRunNormalizationConstants.CommandName, StringComparer.OrdinalIgnoreCase))
{
    static string? NormalizationCommandValue(string[] commandArgs, string key) =>
        commandArgs.FirstOrDefault(x => x.StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase)) is { } item
            ? item[(item.IndexOf('=') + 1)..]
            : null;

    var apply = args.Contains("--apply", StringComparer.OrdinalIgnoreCase);
    var confirmProduction = args.Contains("--confirm-production", StringComparer.OrdinalIgnoreCase);
    var confirmDisposableRestore = args.Contains("--confirm-disposable-restore", StringComparer.OrdinalIgnoreCase);
    var backupRunId = long.TryParse(NormalizationCommandValue(args, "--backup-run-id"), out var parsedBackupRunId)
        ? parsedBackupRunId
        : (long?)null;
    using var normalizationScope = app.Services.CreateScope();
    var normalizationService = normalizationScope.ServiceProvider.GetRequiredService<IJuly27ActualRunNormalizationService>();
    var normalizationResult = await normalizationService.RunAsync(
        new July27ActualRunNormalizationRequest(
            apply,
            confirmProduction,
            confirmDisposableRestore,
            backupRunId,
            NormalizationCommandValue(args, "--verified-backup-package-sha256"),
            NormalizationCommandValue(args, "--requested-by") ?? "command",
            NormalizationCommandValue(args, "--reason") ?? "",
            NormalizationCommandValue(args, "--expected-target-fingerprint"),
            NormalizationCommandValue(args, "--expected-protected-fingerprint"),
            NormalizationCommandValue(args, "--authorization-token")),
        CancellationToken.None);
    var normalizationLogger = normalizationScope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("July27ActualRunNormalizationCommand");
    var safeReport = System.Text.Json.JsonSerializer.Serialize(
        normalizationResult,
        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { WriteIndented = true });
    if (normalizationResult.Success) normalizationLogger.LogInformation("{NormalizationReport}", safeReport);
    else normalizationLogger.LogError("{NormalizationReport}", safeReport);
    Environment.ExitCode = normalizationResult.Success ? 0 : 1;
    return;
}

if (args.Contains(July28ActualRunExpectationBackfillConstants.CommandName, StringComparer.OrdinalIgnoreCase))
{
    static string? ExpectationBackfillCommandValue(string[] commandArgs, string key) =>
        commandArgs.FirstOrDefault(x => x.StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase)) is { } item
            ? item[(item.IndexOf('=') + 1)..]
            : null;

    var apply = args.Contains("--apply", StringComparer.OrdinalIgnoreCase);
    var confirmProduction = args.Contains("--confirm-production", StringComparer.OrdinalIgnoreCase);
    var confirmDisposableRestore = args.Contains("--confirm-disposable-restore", StringComparer.OrdinalIgnoreCase);
    var backupRunId = long.TryParse(ExpectationBackfillCommandValue(args, "--backup-run-id"), out var parsedBackupRunId)
        ? parsedBackupRunId
        : (long?)null;
    using var expectationBackfillScope = app.Services.CreateScope();
    var expectationBackfillService = expectationBackfillScope.ServiceProvider.GetRequiredService<IJuly28ActualRunExpectationBackfillService>();
    var expectationBackfillResult = await expectationBackfillService.RunAsync(
        new July28ActualRunExpectationBackfillRequest(
            apply,
            confirmProduction,
            confirmDisposableRestore,
            backupRunId,
            ExpectationBackfillCommandValue(args, "--verified-backup-package-sha256"),
            ExpectationBackfillCommandValue(args, "--requested-by") ?? "command",
            ExpectationBackfillCommandValue(args, "--reason") ?? "",
            ExpectationBackfillCommandValue(args, "--expected-target-fingerprint"),
            ExpectationBackfillCommandValue(args, "--expected-protected-fingerprint"),
            ExpectationBackfillCommandValue(args, "--authorization-token")),
        CancellationToken.None);
    var expectationBackfillLogger = expectationBackfillScope.ServiceProvider.GetRequiredService<ILoggerFactory>()
        .CreateLogger("July28ActualRunExpectationBackfillCommand");
    var safeReport = System.Text.Json.JsonSerializer.Serialize(
        expectationBackfillResult,
        new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { WriteIndented = true });
    if (expectationBackfillResult.Success) expectationBackfillLogger.LogInformation("{ExpectationBackfillReport}", safeReport);
    else expectationBackfillLogger.LogError("{ExpectationBackfillReport}", safeReport);
    Environment.ExitCode = expectationBackfillResult.Success ? 0 : 1;
    return;
}

await DatabaseStartupDiagnostics.InspectAsync(app.Services, app.Configuration, app.Environment);

var backupCommand = args.FirstOrDefault(x => x.StartsWith("--run-backup=", StringComparison.OrdinalIgnoreCase));
if (backupCommand is not null)
{
    var requestedType = backupCommand[(backupCommand.IndexOf('=') + 1)..];
    var normalizedBackupType = requestedType.ToLowerInvariant();
    var backupType = normalizedBackupType switch
    {
        "scheduled" or "daily" => CropQc.Data.Entities.BackupRunTypes.Daily,
        "weekly" => CropQc.Data.Entities.BackupRunTypes.Weekly,
        "predeployment" or "pre-deployment" => CropQc.Data.Entities.BackupRunTypes.PreDeployment,
        "manual" => CropQc.Data.Entities.BackupRunTypes.Manual,
        _ => throw new InvalidOperationException("Unknown backup command type.")
    };
    using var backupScope = app.Services.CreateScope();
    var backupService = backupScope.ServiceProvider.GetRequiredService<IBackupService>();
    var backupResult = normalizedBackupType == "scheduled"
        ? await backupService.RunScheduledCandidateAsync(CancellationToken.None)
        : await backupService.RunBackupAsync(backupType, $"command:{requestedType}", CancellationToken.None);
    var backupLogger = backupScope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("BackupCommand");
    if (backupResult.Success) backupLogger.LogInformation("{BackupMessage}", backupResult.Message);
    else backupLogger.LogError("{BackupMessage}", backupResult.Message);
    Environment.ExitCode = backupResult.Success ? 0 : 1;
    return;
}

var receiptPurgeCommand = args.FirstOrDefault(x => x.StartsWith("--purge-receipts=", StringComparison.OrdinalIgnoreCase));
if (receiptPurgeCommand is not null)
{
    if (!int.TryParse(receiptPurgeCommand[(receiptPurgeCommand.IndexOf('=') + 1)..], out var targetCropYear))
    {
        throw new InvalidOperationException("The receipt purge command requires an explicit numeric target crop year.");
    }

    static string? CommandValue(string[] commandArgs, string key) =>
        commandArgs.FirstOrDefault(x => x.StartsWith($"{key}=", StringComparison.OrdinalIgnoreCase)) is { } item
            ? item[(item.IndexOf('=') + 1)..]
            : null;

    var apply = args.Contains("--apply", StringComparer.OrdinalIgnoreCase);
    var confirmProduction = args.Contains("--confirm-production", StringComparer.OrdinalIgnoreCase);
    var backupRunId = long.TryParse(CommandValue(args, "--backup-run-id"), out var parsedBackupRunId)
        ? parsedBackupRunId
        : (long?)null;
    var requestedBy = CommandValue(args, "--requested-by") ?? "command";
    var reason = CommandValue(args, "--reason") ?? "";
    using var purgeScope = app.Services.CreateScope();
    var purgeService = purgeScope.ServiceProvider.GetRequiredService<IReceiptPurgeService>();
    var purgeResult = await purgeService.PurgeAsync(
        new ReceiptPurgeRequest(targetCropYear, apply, confirmProduction, backupRunId, requestedBy, reason),
        CancellationToken.None);
    var purgeLogger = purgeScope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("ReceiptPurgeCommand");
    var safeReport = System.Text.Json.JsonSerializer.Serialize(purgeResult, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web) { WriteIndented = true });
    if (purgeResult.Success) purgeLogger.LogInformation("{ReceiptPurgeReport}", safeReport);
    else purgeLogger.LogError("{ReceiptPurgeReport}", safeReport);
    Environment.ExitCode = purgeResult.Success ? 0 : 1;
    return;
}

if (seedMasterDataOnStartup)
{
    using var scope = app.Services.CreateScope();
    var seeder = scope.ServiceProvider.GetRequiredService<IMasterDataSeeder>();
    await seeder.SeedAsync(CancellationToken.None);
}

await EnsurePhotoStorageColumnsAsync(app.Services);
await EnsurePhotoSoftDeleteColumnsAsync(app.Services);
await EnsureCleanupColumnsAsync(app.Services);
await EnsureFruitRowLimitAsync(app.Services);
await EnsureRoomDepletionSchemaAsync(app.Services);
await EnsureGrowerLotSchemaAsync(app.Services);
await EnsureCanonicalGrowerSchemaAsync(app.Services);
await EnsureRoomMetadataSchemaAsync(app.Services);
await EnsureRoomInventoryAdjustmentSchemaAsync(app.Services);
await EnsureBinsRunSchemaAsync(app.Services);
await EnsureRequiredSampleTypesAsync(app.Services);

if (useForwardedHeaders)
{
    app.UseForwardedHeaders();
}

app.UseMiddleware<RequestPerformanceDiagnosticsMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

if (!isRender)
{
    app.UseHttpsRedirection();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health", () => Results.Text("Crop QC Dashboard OK", "text/plain")).AllowAnonymous();
app.MapGet("/health/db", async (CropQcDbContext dbContext, CancellationToken cancellationToken) =>
{
    try
    {
        var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
        return canConnect
            ? Results.Ok(new { status = "OK", database = "Connected" })
            : Results.Problem("Database connection failed.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch (Exception ex)
    {
        return Results.Problem($"Database health check failed: {ex.Message}", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).AllowAnonymous();
app.MapGet("/health/master-data", async (CropQcDbContext dbContext, CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(new
        {
            warehouses = await dbContext.Warehouses.CountAsync(cancellationToken),
            rooms = await dbContext.Rooms.CountAsync(cancellationToken),
            fruitProfiles = await dbContext.FruitProfiles.CountAsync(cancellationToken),
            grades = await dbContext.Grades.CountAsync(cancellationToken),
            defects = await dbContext.DefectTypes.CountAsync(cancellationToken),
            sampleTypes = await dbContext.SampleTypes.CountAsync(cancellationToken),
            starchValues = await dbContext.StarchScaleValues.CountAsync(cancellationToken),
            sizeThresholds = await dbContext.FruitSizeConversionThresholds.CountAsync(cancellationToken),
            expectedSeededRooms = MasterDataSeeder.ExpectedSeededRoomCount
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Master data health check failed: {ex.Message}", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).AllowAnonymous();
app.MapGet("/health/storage", (FileStorageOptions fileStorageOptions, GoogleDriveStorageOptions googleDriveOptions) =>
{
    var provider = string.IsNullOrWhiteSpace(fileStorageOptions.Provider) ? FileStorageProviders.Local : fileStorageOptions.Provider;
    return Results.Ok(new
    {
        provider,
        googleDriveUseSharedDrive = googleDriveOptions.UseSharedDrive,
        googleDriveRootFolderConfigured = !string.IsNullOrWhiteSpace(googleDriveOptions.RootFolderId),
        googleDriveSharedDriveConfigured = !string.IsNullOrWhiteSpace(googleDriveOptions.SharedDriveId),
        googleDriveCredentialsConfigured = !string.IsNullOrWhiteSpace(googleDriveOptions.ServiceAccountJson)
            || !string.IsNullOrWhiteSpace(googleDriveOptions.ServiceAccountJsonPath),
        googleDriveApplicationNameConfigured = !string.IsNullOrWhiteSpace(googleDriveOptions.ApplicationName)
    });
}).RequireAuthorization(AccessPolicyNames.EmailConfigurationAdmin);
app.MapGet("/health/environment", (AppEnvironmentOptions appEnvironment, BackupOptions backupOptions) =>
{
    return Results.Ok(new
    {
        appEnvironment.Kind,
        appEnvironment.DisplayName,
        backupOptions.Enabled,
        backupOptions.Provider,
        backupFolderConfigured = backupOptions.GoogleDriveFolderConfigured
    });
}).RequireAuthorization(AccessPolicyNames.BackupHistoryView);
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

static FileStorageOptions CreateFileStorageOptions(IConfiguration configuration) =>
    new()
    {
        Provider = configuration["FileStorage:Provider"] ?? FileStorageProviders.Local,
        LocalRootPath = configuration["FileStorage:LocalRootPath"] ?? Path.Combine("App_Data", "CropQcFiles"),
        BasePath = configuration["FileStorage:BasePath"] ?? "Crop QC Photos"
    };

static GoogleDriveStorageOptions CreateGoogleDriveStorageOptions(IConfiguration configuration) =>
    new()
    {
        UseSharedDrive = configuration.GetValue<bool>("GoogleDrive:UseSharedDrive"),
        RootFolderId = configuration["GoogleDrive:RootFolderId"] ?? "",
        SharedDriveId = configuration["GoogleDrive:SharedDriveId"] ?? "",
        ServiceAccountJson = configuration["GoogleDrive:ServiceAccountJson"],
        ServiceAccountJsonPath = configuration["GoogleDrive:ServiceAccountJsonPath"],
        ApplicationName = configuration["GoogleDrive:ApplicationName"] ?? "Crop QC Dashboard",
        BaseFolderName = configuration["GoogleDrive:BaseFolderName"] ?? "Photos"
    };

static GmailOptions CreateGmailOptions(IConfiguration configuration) =>
    new()
    {
        SendScope = configuration["Google:Gmail:SendScope"] ?? GmailScopes.Send
    };

static void LogEmailConfiguration(WebApplication app)
{
    var emailOptions = app.Services.GetRequiredService<EmailOptions>();
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("EmailConfiguration");
    logger.LogInformation(
        "Email provider: {EmailProvider}. Default QC recipients configured: {RecipientsConfigured}. Gmail send enabled: {GmailSendEnabled}.",
        emailOptions.Provider,
        emailOptions.QcRecipientList.Count > 0,
        string.Equals(emailOptions.Provider, EmailProviders.GmailUser, StringComparison.OrdinalIgnoreCase));
}

static void LogEnvironmentConfiguration(WebApplication app)
{
    var appEnvironment = app.Services.GetRequiredService<AppEnvironmentOptions>();
    var backupOptions = app.Services.GetRequiredService<BackupOptions>();
    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("AppEnvironment");
    logger.LogInformation(
        "App environment: {EnvironmentKind} ({DisplayName}). Backups enabled: {BackupsEnabled}. Backup provider: {BackupProvider}. Backup folder configured: {BackupFolderConfigured}.",
        appEnvironment.Kind,
        appEnvironment.DisplayName,
        backupOptions.Enabled,
        backupOptions.Provider,
        backupOptions.GoogleDriveFolderConfigured);
}

static IFileStorageService CreateFileStorageService(
    FileStorageOptions fileStorageOptions,
    GoogleDriveStorageOptions googleDriveOptions,
    ILogger<GoogleDriveStorageService> googleDriveLogger,
    IPerformanceExternalCallCounter externalCallCounter)
{
    IFileStorageService storage;
    if (string.Equals(fileStorageOptions.Provider, FileStorageProviders.GoogleDrive, StringComparison.OrdinalIgnoreCase))
    {
        storage = new GoogleDriveStorageService(googleDriveOptions, logger: googleDriveLogger);
    }
    else
    {
        storage = new LocalFileStorageService(fileStorageOptions);
    }

    return new InstrumentedFileStorageService(storage, externalCallCounter);
}

static async Task EnsurePhotoStorageColumnsAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("PhotoStorageSchema");
    try
    {
        var provider = dbContext.Database.ProviderName ?? "";
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "QcPhotos" ADD COLUMN IF NOT EXISTS "StorageProvider" character varying(50) NOT NULL DEFAULT 'Legacy';
                ALTER TABLE "QcPhotos" ADD COLUMN IF NOT EXISTS "DriveId" character varying(200) NULL;
                ALTER TABLE "QcPhotos" ADD COLUMN IF NOT EXISTS "FileId" character varying(200) NULL;
                ALTER TABLE "QcPhotos" ADD COLUMN IF NOT EXISTS "FolderId" character varying(200) NULL;
                ALTER TABLE "QcPhotos" ADD COLUMN IF NOT EXISTS "UploadedAt" timestamp with time zone NULL;
                CREATE INDEX IF NOT EXISTS "IX_QcPhotos_StorageProvider_FileId" ON "QcPhotos" ("StorageProvider", "FileId");
                """);
        }
        else if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""
                IF COL_LENGTH('QcPhotos', 'StorageProvider') IS NULL ALTER TABLE [QcPhotos] ADD [StorageProvider] nvarchar(50) NOT NULL CONSTRAINT [DF_QcPhotos_StorageProvider] DEFAULT N'Legacy';
                IF COL_LENGTH('QcPhotos', 'DriveId') IS NULL ALTER TABLE [QcPhotos] ADD [DriveId] nvarchar(200) NULL;
                IF COL_LENGTH('QcPhotos', 'FileId') IS NULL ALTER TABLE [QcPhotos] ADD [FileId] nvarchar(200) NULL;
                IF COL_LENGTH('QcPhotos', 'FolderId') IS NULL ALTER TABLE [QcPhotos] ADD [FolderId] nvarchar(200) NULL;
                IF COL_LENGTH('QcPhotos', 'UploadedAt') IS NULL ALTER TABLE [QcPhotos] ADD [UploadedAt] datetimeoffset NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_QcPhotos_StorageProvider_FileId' AND object_id = OBJECT_ID(N'[QcPhotos]')) CREATE INDEX [IX_QcPhotos_StorageProvider_FileId] ON [QcPhotos] ([StorageProvider], [FileId]);
                """);
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Photo storage metadata schema check skipped or failed.");
    }
}

static async Task EnsurePhotoSoftDeleteColumnsAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("PhotoSoftDeleteSchema");
    try
    {
        var provider = dbContext.Database.ProviderName ?? "";
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "QcPhotos" ADD COLUMN IF NOT EXISTS "IsDeleted" boolean NOT NULL DEFAULT FALSE;
                ALTER TABLE "QcPhotos" ADD COLUMN IF NOT EXISTS "DeletedAt" timestamp with time zone NULL;
                ALTER TABLE "QcPhotos" ADD COLUMN IF NOT EXISTS "DeletedByUserId" integer NULL;
                ALTER TABLE "QcPhotos" ADD COLUMN IF NOT EXISTS "DeleteReason" character varying(1000) NULL;
                CREATE INDEX IF NOT EXISTS "IX_QcPhotos_QcSampleId_IsDeleted" ON "QcPhotos" ("QcSampleId", "IsDeleted");
                CREATE INDEX IF NOT EXISTS "IX_QcPhotos_ReceiptId_IsDeleted" ON "QcPhotos" ("ReceiptId", "IsDeleted");
                """);
        }
        else if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""
                IF COL_LENGTH('QcPhotos', 'IsDeleted') IS NULL ALTER TABLE [QcPhotos] ADD [IsDeleted] bit NOT NULL CONSTRAINT [DF_QcPhotos_IsDeleted] DEFAULT 0;
                IF COL_LENGTH('QcPhotos', 'DeletedAt') IS NULL ALTER TABLE [QcPhotos] ADD [DeletedAt] datetimeoffset NULL;
                IF COL_LENGTH('QcPhotos', 'DeletedByUserId') IS NULL ALTER TABLE [QcPhotos] ADD [DeletedByUserId] int NULL;
                IF COL_LENGTH('QcPhotos', 'DeleteReason') IS NULL ALTER TABLE [QcPhotos] ADD [DeleteReason] nvarchar(1000) NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_QcPhotos_QcSampleId_IsDeleted' AND object_id = OBJECT_ID(N'[QcPhotos]')) CREATE INDEX [IX_QcPhotos_QcSampleId_IsDeleted] ON [QcPhotos] ([QcSampleId], [IsDeleted]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_QcPhotos_ReceiptId_IsDeleted' AND object_id = OBJECT_ID(N'[QcPhotos]')) CREATE INDEX [IX_QcPhotos_ReceiptId_IsDeleted] ON [QcPhotos] ([ReceiptId], [IsDeleted]);
                """);
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Photo soft-delete schema check skipped or failed.");
    }
}

static async Task EnsureCleanupColumnsAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("CleanupSchema");
    try
    {
        var provider = dbContext.Database.ProviderName ?? "";
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "Receipts" ADD COLUMN IF NOT EXISTS "IsTestData" boolean NOT NULL DEFAULT FALSE;
                ALTER TABLE "Receipts" ADD COLUMN IF NOT EXISTS "IsDeleted" boolean NOT NULL DEFAULT FALSE;
                ALTER TABLE "Receipts" ADD COLUMN IF NOT EXISTS "DeletedAt" timestamp with time zone NULL;
                ALTER TABLE "Receipts" ADD COLUMN IF NOT EXISTS "DeletedByUserId" integer NULL;
                ALTER TABLE "Receipts" ADD COLUMN IF NOT EXISTS "DeleteReason" character varying(1000) NULL;
                ALTER TABLE "Receipts" ADD COLUMN IF NOT EXISTS "ReceiptType" character varying(50) NOT NULL DEFAULT 'Truck receipt';
                ALTER TABLE "Receipts" ADD COLUMN IF NOT EXISTS "GrowerNumber" character varying(50) NULL;
                ALTER TABLE "Receipts" ADD COLUMN IF NOT EXISTS "GrowerLotId" integer NULL;
                ALTER TABLE "Receipts" ADD COLUMN IF NOT EXISTS "PoolStart" character varying(20) NULL;
                ALTER TABLE "QcSamples" ADD COLUMN IF NOT EXISTS "IsTestData" boolean NOT NULL DEFAULT FALSE;
                ALTER TABLE "QcSamples" ADD COLUMN IF NOT EXISTS "IsDeleted" boolean NOT NULL DEFAULT FALSE;
                ALTER TABLE "QcSamples" ADD COLUMN IF NOT EXISTS "DeletedAt" timestamp with time zone NULL;
                ALTER TABLE "QcSamples" ADD COLUMN IF NOT EXISTS "DeletedByUserId" integer NULL;
                ALTER TABLE "QcSamples" ADD COLUMN IF NOT EXISTS "DeleteReason" character varying(1000) NULL;
                CREATE INDEX IF NOT EXISTS "IX_Receipts_CropYear_IsDeleted" ON "Receipts" ("CropYear", "IsDeleted");
                CREATE INDEX IF NOT EXISTS "IX_QcSamples_ReceiptId_IsDeleted" ON "QcSamples" ("ReceiptId", "IsDeleted");
                """);
        }
        else if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""
                IF COL_LENGTH('Receipts', 'IsTestData') IS NULL ALTER TABLE [Receipts] ADD [IsTestData] bit NOT NULL CONSTRAINT [DF_Receipts_IsTestData] DEFAULT 0;
                IF COL_LENGTH('Receipts', 'IsDeleted') IS NULL ALTER TABLE [Receipts] ADD [IsDeleted] bit NOT NULL CONSTRAINT [DF_Receipts_IsDeleted] DEFAULT 0;
                IF COL_LENGTH('Receipts', 'DeletedAt') IS NULL ALTER TABLE [Receipts] ADD [DeletedAt] datetimeoffset NULL;
                IF COL_LENGTH('Receipts', 'DeletedByUserId') IS NULL ALTER TABLE [Receipts] ADD [DeletedByUserId] int NULL;
                IF COL_LENGTH('Receipts', 'DeleteReason') IS NULL ALTER TABLE [Receipts] ADD [DeleteReason] nvarchar(1000) NULL;
                IF COL_LENGTH('Receipts', 'ReceiptType') IS NULL ALTER TABLE [Receipts] ADD [ReceiptType] nvarchar(50) NOT NULL CONSTRAINT [DF_Receipts_ReceiptType] DEFAULT N'Truck receipt';
                IF COL_LENGTH('Receipts', 'GrowerNumber') IS NULL ALTER TABLE [Receipts] ADD [GrowerNumber] nvarchar(50) NULL;
                IF COL_LENGTH('Receipts', 'GrowerLotId') IS NULL ALTER TABLE [Receipts] ADD [GrowerLotId] int NULL;
                IF COL_LENGTH('Receipts', 'PoolStart') IS NULL ALTER TABLE [Receipts] ADD [PoolStart] nvarchar(20) NULL;
                IF COL_LENGTH('QcSamples', 'IsTestData') IS NULL ALTER TABLE [QcSamples] ADD [IsTestData] bit NOT NULL CONSTRAINT [DF_QcSamples_IsTestData] DEFAULT 0;
                IF COL_LENGTH('QcSamples', 'IsDeleted') IS NULL ALTER TABLE [QcSamples] ADD [IsDeleted] bit NOT NULL CONSTRAINT [DF_QcSamples_IsDeleted] DEFAULT 0;
                IF COL_LENGTH('QcSamples', 'DeletedAt') IS NULL ALTER TABLE [QcSamples] ADD [DeletedAt] datetimeoffset NULL;
                IF COL_LENGTH('QcSamples', 'DeletedByUserId') IS NULL ALTER TABLE [QcSamples] ADD [DeletedByUserId] int NULL;
                IF COL_LENGTH('QcSamples', 'DeleteReason') IS NULL ALTER TABLE [QcSamples] ADD [DeleteReason] nvarchar(1000) NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Receipts_CropYear_IsDeleted' AND object_id = OBJECT_ID(N'[Receipts]')) CREATE INDEX [IX_Receipts_CropYear_IsDeleted] ON [Receipts] ([CropYear], [IsDeleted]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_QcSamples_ReceiptId_IsDeleted' AND object_id = OBJECT_ID(N'[QcSamples]')) CREATE INDEX [IX_QcSamples_ReceiptId_IsDeleted] ON [QcSamples] ([ReceiptId], [IsDeleted]);
                """);
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Cleanup schema check skipped or failed.");
    }
}

static async Task EnsureFruitRowLimitAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("FruitRowSchema");
    try
    {
        var provider = dbContext.Database.ProviderName ?? "";
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "QcFruitReadings" DROP CONSTRAINT IF EXISTS "CK_QcFruitReadings_RowNumber_1_25";
                ALTER TABLE "QcFruitReadings" DROP CONSTRAINT IF EXISTS "CK_QcFruitReadings_RowNumber_1_50";
                ALTER TABLE "QcFruitReadings" ADD CONSTRAINT "CK_QcFruitReadings_RowNumber_1_50" CHECK ("RowNumber" >= 1 AND "RowNumber" <= 50);
                """);
        }
        else if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""
                IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_QcFruitReadings_RowNumber_1_25') ALTER TABLE [QcFruitReadings] DROP CONSTRAINT [CK_QcFruitReadings_RowNumber_1_25];
                IF EXISTS (SELECT 1 FROM sys.check_constraints WHERE name = N'CK_QcFruitReadings_RowNumber_1_50') ALTER TABLE [QcFruitReadings] DROP CONSTRAINT [CK_QcFruitReadings_RowNumber_1_50];
                ALTER TABLE [QcFruitReadings] ADD CONSTRAINT [CK_QcFruitReadings_RowNumber_1_50] CHECK ([RowNumber] >= 1 AND [RowNumber] <= 50);
                """);
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Fruit row max-row schema check skipped or failed.");
    }
}

static async Task EnsureGrowerLotSchemaAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("GrowerLotSchema");
    try
    {
        var provider = dbContext.Database.ProviderName ?? "";
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "GrowerLots" (
                    "Id" integer GENERATED BY DEFAULT AS IDENTITY,
                    "Grower" character varying(200) NOT NULL,
                    "LotNumber" character varying(50) NOT NULL,
                    "PoolStart" character varying(20) NULL,
                    "Notes" character varying(1000) NULL,
                    "IsActive" boolean NOT NULL DEFAULT TRUE,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    CONSTRAINT "PK_GrowerLots" PRIMARY KEY ("Id")
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_GrowerLots_Grower_LotNumber" ON "GrowerLots" ("Grower", "LotNumber");
                """);
        }
        else if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'[GrowerLots]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [GrowerLots] (
                        [Id] int NOT NULL IDENTITY,
                        [Grower] nvarchar(200) NOT NULL,
                        [LotNumber] nvarchar(50) NOT NULL,
                        [PoolStart] nvarchar(20) NULL,
                        [Notes] nvarchar(1000) NULL,
                        [IsActive] bit NOT NULL CONSTRAINT [DF_GrowerLots_IsActive] DEFAULT 1,
                        [CreatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_GrowerLots_CreatedAt] DEFAULT SYSDATETIMEOFFSET(),
                        [UpdatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_GrowerLots_UpdatedAt] DEFAULT SYSDATETIMEOFFSET(),
                        CONSTRAINT [PK_GrowerLots] PRIMARY KEY ([Id])
                    );
                    CREATE UNIQUE INDEX [IX_GrowerLots_Grower_LotNumber] ON [GrowerLots] ([Grower], [LotNumber]);
                END
                """);
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Grower lot schema check skipped or failed.");
    }
}

static async Task EnsureCanonicalGrowerSchemaAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("CanonicalGrowerSchema");
    try
    {
        var provider = dbContext.Database.ProviderName ?? "";
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "CanonicalGrowers" (
                    "Id" integer GENERATED BY DEFAULT AS IDENTITY,
                    "DisplayName" character varying(200) NOT NULL,
                    "NormalizedKey" character varying(200) NOT NULL,
                    "IsActive" boolean NOT NULL DEFAULT TRUE,
                    "MergedIntoCanonicalGrowerId" integer NULL,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    CONSTRAINT "PK_CanonicalGrowers" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_CanonicalGrowers_CanonicalGrowers_MergedIntoCanonicalGrowerId" FOREIGN KEY ("MergedIntoCanonicalGrowerId") REFERENCES "CanonicalGrowers" ("Id") ON DELETE RESTRICT
                );
                CREATE INDEX IF NOT EXISTS "IX_CanonicalGrowers_NormalizedKey" ON "CanonicalGrowers" ("NormalizedKey");
                CREATE TABLE IF NOT EXISTS "CanonicalGrowerAliases" (
                    "Id" integer GENERATED BY DEFAULT AS IDENTITY,
                    "CanonicalGrowerId" integer NOT NULL,
                    "AliasName" character varying(200) NOT NULL,
                    "NormalizedAliasKey" character varying(200) NOT NULL,
                    "SourceSystem" character varying(100) NULL,
                    "IsActive" boolean NOT NULL DEFAULT TRUE,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    CONSTRAINT "PK_CanonicalGrowerAliases" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_CanonicalGrowerAliases_CanonicalGrowers_CanonicalGrowerId" FOREIGN KEY ("CanonicalGrowerId") REFERENCES "CanonicalGrowers" ("Id") ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS "IX_CanonicalGrowerAliases_NormalizedAliasKey" ON "CanonicalGrowerAliases" ("NormalizedAliasKey");
                CREATE INDEX IF NOT EXISTS "IX_CanonicalGrowerAliases_CanonicalGrowerId_NormalizedAliasKey" ON "CanonicalGrowerAliases" ("CanonicalGrowerId", "NormalizedAliasKey");
                CREATE TABLE IF NOT EXISTS "CanonicalGrowerNumbers" (
                    "Id" integer GENERATED BY DEFAULT AS IDENTITY,
                    "CanonicalGrowerId" integer NOT NULL,
                    "GrowerNumber" character varying(50) NOT NULL,
                    "NormalizedGrowerNumber" character varying(50) NOT NULL,
                    "SourceSystem" character varying(100) NULL,
                    "Facility" character varying(100) NULL,
                    "CropYear" integer NULL,
                    "IsActive" boolean NOT NULL DEFAULT TRUE,
                    "CreatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    CONSTRAINT "PK_CanonicalGrowerNumbers" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_CanonicalGrowerNumbers_CanonicalGrowers_CanonicalGrowerId" FOREIGN KEY ("CanonicalGrowerId") REFERENCES "CanonicalGrowers" ("Id") ON DELETE CASCADE
                );
                CREATE INDEX IF NOT EXISTS "IX_CanonicalGrowerNumbers_NormalizedGrowerNumber" ON "CanonicalGrowerNumbers" ("NormalizedGrowerNumber");
                CREATE INDEX IF NOT EXISTS "IX_CanonicalGrowerNumbers_CanonicalGrowerId_NormalizedGrowerNumber" ON "CanonicalGrowerNumbers" ("CanonicalGrowerId", "NormalizedGrowerNumber");
                """);
        }
        else if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'[CanonicalGrowers]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [CanonicalGrowers] (
                        [Id] int IDENTITY(1,1) NOT NULL,
                        [DisplayName] nvarchar(200) NOT NULL,
                        [NormalizedKey] nvarchar(200) NOT NULL,
                        [IsActive] bit NOT NULL CONSTRAINT [DF_CanonicalGrowers_IsActive] DEFAULT 1,
                        [MergedIntoCanonicalGrowerId] int NULL,
                        [CreatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_CanonicalGrowers_CreatedAt] DEFAULT SYSDATETIMEOFFSET(),
                        [UpdatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_CanonicalGrowers_UpdatedAt] DEFAULT SYSDATETIMEOFFSET(),
                        CONSTRAINT [PK_CanonicalGrowers] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_CanonicalGrowers_CanonicalGrowers_MergedIntoCanonicalGrowerId] FOREIGN KEY ([MergedIntoCanonicalGrowerId]) REFERENCES [CanonicalGrowers] ([Id]) ON DELETE NO ACTION
                    );
                    CREATE INDEX [IX_CanonicalGrowers_NormalizedKey] ON [CanonicalGrowers] ([NormalizedKey]);
                END
                IF OBJECT_ID(N'[CanonicalGrowerAliases]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [CanonicalGrowerAliases] (
                        [Id] int IDENTITY(1,1) NOT NULL,
                        [CanonicalGrowerId] int NOT NULL,
                        [AliasName] nvarchar(200) NOT NULL,
                        [NormalizedAliasKey] nvarchar(200) NOT NULL,
                        [SourceSystem] nvarchar(100) NULL,
                        [IsActive] bit NOT NULL CONSTRAINT [DF_CanonicalGrowerAliases_IsActive] DEFAULT 1,
                        [CreatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_CanonicalGrowerAliases_CreatedAt] DEFAULT SYSDATETIMEOFFSET(),
                        [UpdatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_CanonicalGrowerAliases_UpdatedAt] DEFAULT SYSDATETIMEOFFSET(),
                        CONSTRAINT [PK_CanonicalGrowerAliases] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_CanonicalGrowerAliases_CanonicalGrowers_CanonicalGrowerId] FOREIGN KEY ([CanonicalGrowerId]) REFERENCES [CanonicalGrowers] ([Id]) ON DELETE CASCADE
                    );
                    CREATE INDEX [IX_CanonicalGrowerAliases_NormalizedAliasKey] ON [CanonicalGrowerAliases] ([NormalizedAliasKey]);
                    CREATE INDEX [IX_CanonicalGrowerAliases_CanonicalGrowerId_NormalizedAliasKey] ON [CanonicalGrowerAliases] ([CanonicalGrowerId], [NormalizedAliasKey]);
                END
                IF OBJECT_ID(N'[CanonicalGrowerNumbers]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [CanonicalGrowerNumbers] (
                        [Id] int IDENTITY(1,1) NOT NULL,
                        [CanonicalGrowerId] int NOT NULL,
                        [GrowerNumber] nvarchar(50) NOT NULL,
                        [NormalizedGrowerNumber] nvarchar(50) NOT NULL,
                        [SourceSystem] nvarchar(100) NULL,
                        [Facility] nvarchar(100) NULL,
                        [CropYear] int NULL,
                        [IsActive] bit NOT NULL CONSTRAINT [DF_CanonicalGrowerNumbers_IsActive] DEFAULT 1,
                        [CreatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_CanonicalGrowerNumbers_CreatedAt] DEFAULT SYSDATETIMEOFFSET(),
                        [UpdatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_CanonicalGrowerNumbers_UpdatedAt] DEFAULT SYSDATETIMEOFFSET(),
                        CONSTRAINT [PK_CanonicalGrowerNumbers] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_CanonicalGrowerNumbers_CanonicalGrowers_CanonicalGrowerId] FOREIGN KEY ([CanonicalGrowerId]) REFERENCES [CanonicalGrowers] ([Id]) ON DELETE CASCADE
                    );
                    CREATE INDEX [IX_CanonicalGrowerNumbers_NormalizedGrowerNumber] ON [CanonicalGrowerNumbers] ([NormalizedGrowerNumber]);
                    CREATE INDEX [IX_CanonicalGrowerNumbers_CanonicalGrowerId_NormalizedGrowerNumber] ON [CanonicalGrowerNumbers] ([CanonicalGrowerId], [NormalizedGrowerNumber]);
                END
                """);
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Canonical grower schema check skipped or failed.");
    }
}

static async Task EnsureRoomDepletionSchemaAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("RoomDepletionSchema");
    try
    {
        var provider = dbContext.Database.ProviderName ?? "";
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "RoomDepletions" (
                    "Id" bigint GENERATED BY DEFAULT AS IDENTITY,
                    "ReceiptId" bigint NOT NULL,
                    "WarehouseId" integer NOT NULL,
                    "RoomId" integer NOT NULL,
                    "FruitProfileId" integer NOT NULL,
                    "GrowerName" character varying(200) NOT NULL,
                    "LotCode" character varying(100) NOT NULL,
                    "BinCountDepleted" integer NOT NULL,
                    "Destination" character varying(100) NULL,
                    "Notes" character varying(1000) NULL,
                    "DepletedAt" timestamp with time zone NOT NULL,
                    "CreatedByUserId" integer NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "IsVoided" boolean NOT NULL DEFAULT FALSE,
                    "VoidedAt" timestamp with time zone NULL,
                    "VoidedByUserId" integer NULL,
                    "VoidReason" character varying(1000) NULL,
                    CONSTRAINT "PK_RoomDepletions" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_RoomDepletions_Receipts_ReceiptId" FOREIGN KEY ("ReceiptId") REFERENCES "Receipts" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_RoomDepletions_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES "Warehouses" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_RoomDepletions_Rooms_RoomId" FOREIGN KEY ("RoomId") REFERENCES "Rooms" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_RoomDepletions_FruitProfiles_FruitProfileId" FOREIGN KEY ("FruitProfileId") REFERENCES "FruitProfiles" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_RoomDepletions_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL,
                    CONSTRAINT "FK_RoomDepletions_Users_VoidedByUserId" FOREIGN KEY ("VoidedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
                );
                CREATE INDEX IF NOT EXISTS "IX_RoomDepletions_RoomId_IsVoided_DepletedAt" ON "RoomDepletions" ("RoomId", "IsVoided", "DepletedAt");
                CREATE INDEX IF NOT EXISTS "IX_RoomDepletions_ReceiptId_IsVoided" ON "RoomDepletions" ("ReceiptId", "IsVoided");
                """);
        }
        else if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'[RoomDepletions]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [RoomDepletions] (
                        [Id] bigint IDENTITY(1,1) NOT NULL,
                        [ReceiptId] bigint NOT NULL,
                        [WarehouseId] int NOT NULL,
                        [RoomId] int NOT NULL,
                        [FruitProfileId] int NOT NULL,
                        [GrowerName] nvarchar(200) NOT NULL,
                        [LotCode] nvarchar(100) NOT NULL,
                        [BinCountDepleted] int NOT NULL,
                        [Destination] nvarchar(100) NULL,
                        [Notes] nvarchar(1000) NULL,
                        [DepletedAt] datetimeoffset NOT NULL,
                        [CreatedByUserId] int NULL,
                        [CreatedAt] datetimeoffset NOT NULL,
                        [IsVoided] bit NOT NULL CONSTRAINT [DF_RoomDepletions_IsVoided] DEFAULT 0,
                        [VoidedAt] datetimeoffset NULL,
                        [VoidedByUserId] int NULL,
                        [VoidReason] nvarchar(1000) NULL,
                        CONSTRAINT [PK_RoomDepletions] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_RoomDepletions_Receipts_ReceiptId] FOREIGN KEY ([ReceiptId]) REFERENCES [Receipts] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_RoomDepletions_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_RoomDepletions_Rooms_RoomId] FOREIGN KEY ([RoomId]) REFERENCES [Rooms] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_RoomDepletions_FruitProfiles_FruitProfileId] FOREIGN KEY ([FruitProfileId]) REFERENCES [FruitProfiles] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_RoomDepletions_Users_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [Users] ([Id]) ON DELETE SET NULL,
                        CONSTRAINT [FK_RoomDepletions_Users_VoidedByUserId] FOREIGN KEY ([VoidedByUserId]) REFERENCES [Users] ([Id]) ON DELETE SET NULL
                    );
                END;
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RoomDepletions_RoomId_IsVoided_DepletedAt' AND object_id = OBJECT_ID(N'[RoomDepletions]')) CREATE INDEX [IX_RoomDepletions_RoomId_IsVoided_DepletedAt] ON [RoomDepletions] ([RoomId], [IsVoided], [DepletedAt]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RoomDepletions_ReceiptId_IsVoided' AND object_id = OBJECT_ID(N'[RoomDepletions]')) CREATE INDEX [IX_RoomDepletions_ReceiptId_IsVoided] ON [RoomDepletions] ([ReceiptId], [IsVoided]);
                """);
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Room depletion schema check skipped or failed.");
    }
}

static async Task EnsureRoomMetadataSchemaAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("RoomMetadataSchema");
    try
    {
        var provider = db.Database.ProviderName ?? "";
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await db.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "Rooms" ADD COLUMN IF NOT EXISTS "SubLocation" character varying(100) NULL;
                ALTER TABLE "Rooms" ADD COLUMN IF NOT EXISTS "CropQcRoomName" character varying(100) NULL;
                ALTER TABLE "Rooms" ADD COLUMN IF NOT EXISTS "CompuTechRoomCode" character varying(100) NULL;
                ALTER TABLE "Rooms" ADD COLUMN IF NOT EXISTS "DisplayName" character varying(150) NULL;
                ALTER TABLE "Rooms" ADD COLUMN IF NOT EXISTS "SortOrder" integer NOT NULL DEFAULT 0;
                CREATE INDEX IF NOT EXISTS "IX_Rooms_WarehouseId_CompuTechRoomCode" ON "Rooms" ("WarehouseId", "CompuTechRoomCode");
                """);
        }
        else if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            await db.Database.ExecuteSqlRawAsync("""
                IF COL_LENGTH('Rooms', 'SubLocation') IS NULL ALTER TABLE [Rooms] ADD [SubLocation] nvarchar(100) NULL;
                IF COL_LENGTH('Rooms', 'CropQcRoomName') IS NULL ALTER TABLE [Rooms] ADD [CropQcRoomName] nvarchar(100) NULL;
                IF COL_LENGTH('Rooms', 'CompuTechRoomCode') IS NULL ALTER TABLE [Rooms] ADD [CompuTechRoomCode] nvarchar(100) NULL;
                IF COL_LENGTH('Rooms', 'DisplayName') IS NULL ALTER TABLE [Rooms] ADD [DisplayName] nvarchar(150) NULL;
                IF COL_LENGTH('Rooms', 'SortOrder') IS NULL ALTER TABLE [Rooms] ADD [SortOrder] int NOT NULL CONSTRAINT [DF_Rooms_SortOrder] DEFAULT 0;
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Rooms_WarehouseId_CompuTechRoomCode' AND object_id = OBJECT_ID(N'[Rooms]')) CREATE INDEX [IX_Rooms_WarehouseId_CompuTechRoomCode] ON [Rooms] ([WarehouseId], [CompuTechRoomCode]);
                """);
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Could not ensure room metadata schema.");
    }
}

static async Task EnsureRoomInventoryAdjustmentSchemaAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("RoomInventoryAdjustmentSchema");
    try
    {
        var provider = db.Database.ProviderName ?? "";
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "RoomInventoryAdjustments" (
                    "Id" bigint GENERATED BY DEFAULT AS IDENTITY,
                    "ReceiptId" bigint NULL,
                    "CropYear" integer NULL,
                    "RoomDepletionId" bigint NULL,
                    "WarehouseId" integer NOT NULL,
                    "RoomId" integer NOT NULL,
                    "GrowerLotId" integer NULL,
                    "FruitProfileId" integer NULL,
                    "GrowerName" character varying(200) NOT NULL,
                    "LotNumber" character varying(100) NOT NULL,
                    "PoolStart" character varying(20) NULL,
                    "VarietyCode" character varying(50) NULL,
                    "OldBinCount" integer NULL,
                    "ChangeAmount" integer NOT NULL,
                    "NewBinCount" integer NOT NULL,
                    "AdjustmentType" character varying(50) NOT NULL,
                    "Source" character varying(150) NULL,
                    "SourceRoomCode" character varying(100) NULL,
                    "SourceSubLocation" character varying(100) NULL,
                    "InventoryStatus" character varying(100) NULL,
                    "Reason" character varying(500) NULL,
                    "Notes" character varying(1000) NULL,
                    "AdjustmentAt" timestamp with time zone NOT NULL,
                    "CreatedByUserId" integer NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_RoomInventoryAdjustments" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_RoomInventoryAdjustments_Receipts_ReceiptId" FOREIGN KEY ("ReceiptId") REFERENCES "Receipts" ("Id") ON DELETE SET NULL,
                    CONSTRAINT "FK_RoomInventoryAdjustments_RoomDepletions_RoomDepletionId" FOREIGN KEY ("RoomDepletionId") REFERENCES "RoomDepletions" ("Id") ON DELETE SET NULL,
                    CONSTRAINT "FK_RoomInventoryAdjustments_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES "Warehouses" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_RoomInventoryAdjustments_Rooms_RoomId" FOREIGN KEY ("RoomId") REFERENCES "Rooms" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_RoomInventoryAdjustments_GrowerLots_GrowerLotId" FOREIGN KEY ("GrowerLotId") REFERENCES "GrowerLots" ("Id") ON DELETE SET NULL,
                    CONSTRAINT "FK_RoomInventoryAdjustments_FruitProfiles_FruitProfileId" FOREIGN KEY ("FruitProfileId") REFERENCES "FruitProfiles" ("Id") ON DELETE SET NULL,
                    CONSTRAINT "FK_RoomInventoryAdjustments_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
                );
                ALTER TABLE "RoomInventoryAdjustments" ADD COLUMN IF NOT EXISTS "Source" character varying(150) NULL;
                ALTER TABLE "RoomInventoryAdjustments" ADD COLUMN IF NOT EXISTS "CropYear" integer NULL;
                ALTER TABLE "RoomInventoryAdjustments" ADD COLUMN IF NOT EXISTS "SourceRoomCode" character varying(100) NULL;
                ALTER TABLE "RoomInventoryAdjustments" ADD COLUMN IF NOT EXISTS "SourceSubLocation" character varying(100) NULL;
                ALTER TABLE "RoomInventoryAdjustments" ADD COLUMN IF NOT EXISTS "InventoryStatus" character varying(100) NULL;
                CREATE INDEX IF NOT EXISTS "IX_RoomInventoryAdjustments_RoomId_AdjustmentAt" ON "RoomInventoryAdjustments" ("RoomId", "AdjustmentAt");
                CREATE INDEX IF NOT EXISTS "IX_RoomInventoryAdjustments_ReceiptId_AdjustmentAt" ON "RoomInventoryAdjustments" ("ReceiptId", "AdjustmentAt");
                """);
        }
        else if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            await db.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'[RoomInventoryAdjustments]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [RoomInventoryAdjustments] (
                        [Id] bigint NOT NULL IDENTITY,
                        [ReceiptId] bigint NULL,
                        [CropYear] int NULL,
                        [RoomDepletionId] bigint NULL,
                        [WarehouseId] int NOT NULL,
                        [RoomId] int NOT NULL,
                        [GrowerLotId] int NULL,
                        [FruitProfileId] int NULL,
                        [GrowerName] nvarchar(200) NOT NULL,
                        [LotNumber] nvarchar(100) NOT NULL,
                        [PoolStart] nvarchar(20) NULL,
                        [VarietyCode] nvarchar(50) NULL,
                        [OldBinCount] int NULL,
                        [ChangeAmount] int NOT NULL,
                        [NewBinCount] int NOT NULL,
                        [AdjustmentType] nvarchar(50) NOT NULL,
                        [Source] nvarchar(150) NULL,
                        [SourceRoomCode] nvarchar(100) NULL,
                        [SourceSubLocation] nvarchar(100) NULL,
                        [InventoryStatus] nvarchar(100) NULL,
                        [Reason] nvarchar(500) NULL,
                        [Notes] nvarchar(1000) NULL,
                        [AdjustmentAt] datetimeoffset NOT NULL,
                        [CreatedByUserId] int NULL,
                        [CreatedAt] datetimeoffset NOT NULL,
                        CONSTRAINT [PK_RoomInventoryAdjustments] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_RoomInventoryAdjustments_Receipts_ReceiptId] FOREIGN KEY ([ReceiptId]) REFERENCES [Receipts] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_RoomInventoryAdjustments_RoomDepletions_RoomDepletionId] FOREIGN KEY ([RoomDepletionId]) REFERENCES [RoomDepletions] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_RoomInventoryAdjustments_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_RoomInventoryAdjustments_Rooms_RoomId] FOREIGN KEY ([RoomId]) REFERENCES [Rooms] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_RoomInventoryAdjustments_GrowerLots_GrowerLotId] FOREIGN KEY ([GrowerLotId]) REFERENCES [GrowerLots] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_RoomInventoryAdjustments_FruitProfiles_FruitProfileId] FOREIGN KEY ([FruitProfileId]) REFERENCES [FruitProfiles] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_RoomInventoryAdjustments_Users_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
                    );
                END
                IF COL_LENGTH('RoomInventoryAdjustments', 'Source') IS NULL ALTER TABLE [RoomInventoryAdjustments] ADD [Source] nvarchar(150) NULL;
                IF COL_LENGTH('RoomInventoryAdjustments', 'CropYear') IS NULL ALTER TABLE [RoomInventoryAdjustments] ADD [CropYear] int NULL;
                IF COL_LENGTH('RoomInventoryAdjustments', 'SourceRoomCode') IS NULL ALTER TABLE [RoomInventoryAdjustments] ADD [SourceRoomCode] nvarchar(100) NULL;
                IF COL_LENGTH('RoomInventoryAdjustments', 'SourceSubLocation') IS NULL ALTER TABLE [RoomInventoryAdjustments] ADD [SourceSubLocation] nvarchar(100) NULL;
                IF COL_LENGTH('RoomInventoryAdjustments', 'InventoryStatus') IS NULL ALTER TABLE [RoomInventoryAdjustments] ADD [InventoryStatus] nvarchar(100) NULL;
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RoomInventoryAdjustments_RoomId_AdjustmentAt' AND object_id = OBJECT_ID(N'[RoomInventoryAdjustments]')) CREATE INDEX [IX_RoomInventoryAdjustments_RoomId_AdjustmentAt] ON [RoomInventoryAdjustments] ([RoomId], [AdjustmentAt]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_RoomInventoryAdjustments_ReceiptId_AdjustmentAt' AND object_id = OBJECT_ID(N'[RoomInventoryAdjustments]')) CREATE INDEX [IX_RoomInventoryAdjustments_ReceiptId_AdjustmentAt] ON [RoomInventoryAdjustments] ([ReceiptId], [AdjustmentAt]);
                """);
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Could not ensure room inventory adjustment schema.");
    }
}

static async Task EnsureBinsRunSchemaAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("BinsRunSchema");
    try
    {
        var provider = db.Database.ProviderName ?? "";
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await db.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS "BinsRunEntries" (
                    "Id" bigint GENERATED BY DEFAULT AS IDENTITY,
                    "ReceiptId" bigint NULL,
                    "SourceInventoryAdjustmentId" bigint NULL,
                    "InventoryAdjustmentId" bigint NOT NULL,
                    "WarehouseId" integer NOT NULL,
                    "RoomId" integer NOT NULL,
                    "GrowerLotId" integer NULL,
                    "FruitProfileId" integer NULL,
                    "GrowerName" character varying(200) NOT NULL,
                    "LotNumber" character varying(100) NOT NULL,
                    "PoolStart" character varying(20) NULL,
                    "VarietyCode" character varying(50) NULL,
                    "InventoryStatus" character varying(100) NULL,
                    "PreviousAvailableBins" integer NOT NULL,
                    "BinsRun" integer NOT NULL,
                    "NewAvailableBins" integer NOT NULL,
                    "Notes" character varying(1000) NULL,
                    "RunAt" timestamp with time zone NOT NULL,
                    "CreatedByUserId" integer NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NULL,
                    "IsReversed" boolean NOT NULL DEFAULT FALSE,
                    "ReversedAt" timestamp with time zone NULL,
                    "ReversedByUserId" integer NULL,
                    "ReverseReason" character varying(1000) NULL,
                    CONSTRAINT "PK_BinsRunEntries" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_BinsRunEntries_Receipts_ReceiptId" FOREIGN KEY ("ReceiptId") REFERENCES "Receipts" ("Id") ON DELETE SET NULL,
                    CONSTRAINT "FK_BinsRunEntries_SourceInventoryAdjustmentId" FOREIGN KEY ("SourceInventoryAdjustmentId") REFERENCES "RoomInventoryAdjustments" ("Id") ON DELETE SET NULL,
                    CONSTRAINT "FK_BinsRunEntries_InventoryAdjustmentId" FOREIGN KEY ("InventoryAdjustmentId") REFERENCES "RoomInventoryAdjustments" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_BinsRunEntries_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES "Warehouses" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_BinsRunEntries_Rooms_RoomId" FOREIGN KEY ("RoomId") REFERENCES "Rooms" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_BinsRunEntries_GrowerLots_GrowerLotId" FOREIGN KEY ("GrowerLotId") REFERENCES "GrowerLots" ("Id") ON DELETE SET NULL,
                    CONSTRAINT "FK_BinsRunEntries_FruitProfiles_FruitProfileId" FOREIGN KEY ("FruitProfileId") REFERENCES "FruitProfiles" ("Id") ON DELETE SET NULL,
                    CONSTRAINT "FK_BinsRunEntries_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL,
                    CONSTRAINT "FK_BinsRunEntries_Users_ReversedByUserId" FOREIGN KEY ("ReversedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
                );
                CREATE INDEX IF NOT EXISTS "IX_BinsRunEntries_RoomId_RunAt" ON "BinsRunEntries" ("RoomId", "RunAt");
                CREATE INDEX IF NOT EXISTS "IX_BinsRunEntries_ReceiptId_IsReversed" ON "BinsRunEntries" ("ReceiptId", "IsReversed");
                """);
        }
        else if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            await db.Database.ExecuteSqlRawAsync("""
                IF OBJECT_ID(N'[BinsRunEntries]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [BinsRunEntries] (
                        [Id] bigint NOT NULL IDENTITY,
                        [ReceiptId] bigint NULL,
                        [SourceInventoryAdjustmentId] bigint NULL,
                        [InventoryAdjustmentId] bigint NOT NULL,
                        [WarehouseId] int NOT NULL,
                        [RoomId] int NOT NULL,
                        [GrowerLotId] int NULL,
                        [FruitProfileId] int NULL,
                        [GrowerName] nvarchar(200) NOT NULL,
                        [LotNumber] nvarchar(100) NOT NULL,
                        [PoolStart] nvarchar(20) NULL,
                        [VarietyCode] nvarchar(50) NULL,
                        [InventoryStatus] nvarchar(100) NULL,
                        [PreviousAvailableBins] int NOT NULL,
                        [BinsRun] int NOT NULL,
                        [NewAvailableBins] int NOT NULL,
                        [Notes] nvarchar(1000) NULL,
                        [RunAt] datetimeoffset NOT NULL,
                        [CreatedByUserId] int NULL,
                        [CreatedAt] datetimeoffset NOT NULL,
                        [UpdatedAt] datetimeoffset NULL,
                        [IsReversed] bit NOT NULL CONSTRAINT [DF_BinsRunEntries_IsReversed] DEFAULT 0,
                        [ReversedAt] datetimeoffset NULL,
                        [ReversedByUserId] int NULL,
                        [ReverseReason] nvarchar(1000) NULL,
                        CONSTRAINT [PK_BinsRunEntries] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_BinsRunEntries_Receipts_ReceiptId] FOREIGN KEY ([ReceiptId]) REFERENCES [Receipts] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_BinsRunEntries_SourceInventoryAdjustmentId] FOREIGN KEY ([SourceInventoryAdjustmentId]) REFERENCES [RoomInventoryAdjustments] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_BinsRunEntries_InventoryAdjustmentId] FOREIGN KEY ([InventoryAdjustmentId]) REFERENCES [RoomInventoryAdjustments] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_BinsRunEntries_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_BinsRunEntries_Rooms_RoomId] FOREIGN KEY ([RoomId]) REFERENCES [Rooms] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_BinsRunEntries_GrowerLots_GrowerLotId] FOREIGN KEY ([GrowerLotId]) REFERENCES [GrowerLots] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_BinsRunEntries_FruitProfiles_FruitProfileId] FOREIGN KEY ([FruitProfileId]) REFERENCES [FruitProfiles] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_BinsRunEntries_Users_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_BinsRunEntries_Users_ReversedByUserId] FOREIGN KEY ([ReversedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
                    );
                END
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BinsRunEntries_RoomId_RunAt' AND object_id = OBJECT_ID(N'[BinsRunEntries]')) CREATE INDEX [IX_BinsRunEntries_RoomId_RunAt] ON [BinsRunEntries] ([RoomId], [RunAt]);
                IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_BinsRunEntries_ReceiptId_IsReversed' AND object_id = OBJECT_ID(N'[BinsRunEntries]')) CREATE INDEX [IX_BinsRunEntries_ReceiptId_IsReversed] ON [BinsRunEntries] ([ReceiptId], [IsReversed]);
                """);
        }
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Could not ensure Bins Run schema.");
    }
}

static async Task EnsureRequiredSampleTypesAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("RequiredSampleTypes");
    try
    {
        foreach (var name in new[] { "Receiving Sample", "Door Sample", "Lot Sample" })
        {
            if (!await dbContext.SampleTypes.AnyAsync(x => x.Name == name))
            {
                dbContext.SampleTypes.Add(new CropQc.Data.Entities.SampleType { Name = name, IsActive = true });
            }
        }

        await dbContext.SaveChangesAsync();
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Required sample type check skipped or failed.");
    }
}

static async Task<bool> VerifyInventoryDeductionReadinessAsync(IServiceProvider services)
{
    using var invariantScope = services.CreateScope();
    var invariant = invariantScope.ServiceProvider.GetRequiredService<IInventoryDeductionInvariantService>();
    var result = await invariant.VerifyReadinessAsync(CancellationToken.None);
    var invariantLogger = invariantScope.ServiceProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("InventoryDeductionReadiness");
    invariantLogger.LogInformation(
        "Inventory deduction readiness inspected {NegativeCount} negative adjustments: {HistoricalCount} historical, {NewFormatCount} new-format, {IssueCount} issue(s), {BlockingCount} blocking.",
        result.NegativeAdjustmentCount,
        result.HistoricalNegativeCount,
        result.NewFormatNegativeCount,
        result.Issues.Count,
        result.Issues.Count(x => x.BlocksDeployment));
    foreach (var issue in result.Issues)
    {
        invariantLogger.LogWarning(
            "Inventory deduction readiness issue {Code} for adjustment {AdjustmentId}; invariant version {InvariantVersion}; blocking {BlocksDeployment}.",
            issue.Code,
            issue.AdjustmentId,
            issue.InvariantVersion,
            issue.BlocksDeployment);
    }

    return result.IsReady;
}

static void ConfigureDataProtection(IServiceCollection services, IConfiguration configuration)
{
    var applicationName = configuration["DataProtection:ApplicationName"] ?? "CropQcDashboard";
    var dataProtectionBuilder = services.AddDataProtection()
        .SetApplicationName(applicationName);

    if (!configuration.GetValue<bool>("DataProtection:PersistKeysToFileSystem"))
    {
        return;
    }

    var keysPath = configuration["DataProtection:KeysPath"];
    if (string.IsNullOrWhiteSpace(keysPath))
    {
        throw new InvalidOperationException("DataProtection:KeysPath is required when DataProtection:PersistKeysToFileSystem is true.");
    }

    Directory.CreateDirectory(keysPath);
    dataProtectionBuilder.PersistKeysToFileSystem(new DirectoryInfo(keysPath));
}

static void AddAccessPolicy(AuthorizationOptions options, string policyName, string areaKey, PageAccessLevel minimumLevel)
{
    options.AddPolicy(policyName, policy => policy
        .RequireAuthenticatedUser()
        .AddRequirements(new PageAccessRequirement(areaKey, minimumLevel)));
}

public partial class Program;
