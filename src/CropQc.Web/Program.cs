using CropQc.Data;
using CropQc.Shared.Storage;
using CropQc.Web.Auth;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Encodings.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Services.AddControllersWithViews();
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
var stagingEnvironmentOptions = StagingEnvironmentOptions.FromConfiguration(builder.Configuration);
builder.Services.AddSingleton(googleAuthOptions);
builder.Services.AddSingleton(gmailOptions);
builder.Services.AddSingleton(appEnvironmentOptions);
builder.Services.AddSingleton(stagingEnvironmentOptions);
builder.Services.AddSingleton(EmailOptionsFactory.Create(builder.Configuration, builder.Environment.IsProduction()));
builder.Services.AddSingleton(BackupOptions.FromConfiguration(builder.Configuration));
builder.Services.AddSingleton(PerformanceDiagnosticsOptions.FromConfiguration(builder.Configuration, builder.Environment));
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
            var appEnvironment = context.HttpContext.RequestServices.GetRequiredService<AppEnvironmentOptions>();
            var stagingOptions = context.HttpContext.RequestServices.GetRequiredService<StagingEnvironmentOptions>();
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

            if (appEnvironment.IsStaging && !stagingOptions.IsAllowedTestUser(email))
            {
                logger.LogWarning("Google login rejected for {Email}; account is not on the staging allowlist.", email ?? "(missing)");
                context.Fail("This Google account is not on the staging test-user allowlist.");
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
    AddAccessPolicy(options, AccessPolicyNames.ReceiptsView, ApplicationAreas.Receipts, PageAccessLevel.View);
    AddAccessPolicy(options, AccessPolicyNames.ReceiptsEdit, ApplicationAreas.Receipts, PageAccessLevel.Edit);
    AddAccessPolicy(options, AccessPolicyNames.ReceiptEditEdit, ApplicationAreas.ReceiptEdit, PageAccessLevel.Edit);
    AddAccessPolicy(options, AccessPolicyNames.ReceiptDeleteAdmin, ApplicationAreas.ReceiptDelete, PageAccessLevel.Admin);
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
});
builder.Services.AddScoped<PerformanceQueryCounter>();
builder.Services.AddScoped<IPerformanceQueryCounter>(services => services.GetRequiredService<PerformanceQueryCounter>());
builder.Services.AddSingleton<IPerformanceExternalCallCounter, PerformanceExternalCallCounter>();
builder.Services.AddSingleton<IPerformanceRequestMetricSink, BoundedPerformanceRequestMetricSink>();
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
builder.Services.AddScoped<IGoogleUserProvisioningService, GoogleUserProvisioningService>();
builder.Services.AddScoped<IGoogleCredentialStore, GoogleCredentialStore>();
builder.Services.AddScoped<IQcEmailSender, GmailUserEmailSender>();
builder.Services.AddScoped<IQcPhotoRequirementPolicy, QcPhotoRequirementPolicy>();
builder.Services.AddScoped<IQcSummaryEmailComposer, QcSummaryEmailComposer>();
builder.Services.AddScoped<IQcEmailRecipientResolver, QcEmailRecipientResolver>();
builder.Services.AddScoped<IMasterDataSeeder, MasterDataSeeder>();
builder.Services.AddScoped<IReceivingExportService, ReceivingExportService>();
builder.Services.AddScoped<IAdminAuthorizationService, AdminAuthorizationService>();
builder.Services.AddScoped<IUserAccessService, UserAccessService>();
builder.Services.AddScoped<IAuthorizationHandler, PageAccessAuthorizationHandler>();
builder.Services.AddScoped<IAdminManagementService, AdminManagementService>();
builder.Services.AddScoped<IRoomInventoryImportService, RoomInventoryImportService>();
builder.Services.AddScoped<IBinsRunService, BinsRunService>();
builder.Services.AddScoped<IEbsDailyBinsEmailService, EbsDailyBinsEmailService>();
builder.Services.AddScoped<IUserAdminService, UserAdminService>();
builder.Services.AddScoped<IQcStationAdminService, QcStationAdminService>();
builder.Services.AddScoped<ICropYearService, CropYearService>();
builder.Services.AddScoped<IDataCleanupService, DataCleanupService>();
builder.Services.AddScoped<IVarietyColorService, VarietyColorService>();
builder.Services.AddScoped<ICanonicalGrowerService, CanonicalGrowerService>();
builder.Services.AddScoped<IBackupService, BackupService>();
builder.Services.AddHostedService<EbsDailyBinsEmailHostedService>();
builder.Services.AddSingleton(CreateFileStorageOptions(builder.Configuration));
builder.Services.AddSingleton(CreateGoogleDriveStorageOptions(builder.Configuration));
builder.Services.AddSingleton<IFileStorageService>(services => CreateFileStorageService(
    services.GetRequiredService<FileStorageOptions>(),
    services.GetRequiredService<GoogleDriveStorageOptions>(),
    services.GetRequiredService<ILogger<GoogleDriveStorageService>>(),
    services.GetRequiredService<IPerformanceExternalCallCounter>()));

var app = builder.Build();
StagingEnvironmentValidator.Validate(
    app.Configuration,
    app.Services.GetRequiredService<AppEnvironmentOptions>(),
    app.Services.GetRequiredService<StagingEnvironmentOptions>(),
    app.Services.GetRequiredService<GoogleAuthenticationOptions>(),
    app.Services.GetRequiredService<EmailOptions>(),
    app.Services.GetRequiredService<FileStorageOptions>(),
    app.Services.GetRequiredService<GoogleDriveStorageOptions>(),
    app.Services.GetRequiredService<PerformanceDiagnosticsOptions>());
LogEmailConfiguration(app);
LogEnvironmentConfiguration(app);
var isRender = !string.IsNullOrWhiteSpace(app.Configuration["RENDER_EXTERNAL_HOSTNAME"])
    || !string.IsNullOrWhiteSpace(app.Configuration["RENDER_EXTERNAL_URL"]);
var useForwardedHeaders = isRender || app.Configuration.GetValue<bool>("ASPNETCORE_FORWARDEDHEADERS_ENABLED");

if (app.Configuration.GetValue<bool>("Database:EnsureCreatedOnStartup"))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
}

if (app.Configuration.GetValue<bool>("Database:SeedMasterDataOnStartup"))
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
await EnsureAccessMatrixAsync(app.Services);

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
}).RequireAuthorization(AccessPolicyNames.ConfigurationAdmin);
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
}).RequireAuthorization(AccessPolicyNames.BackupsAdmin);
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

static async Task EnsureAccessMatrixAsync(IServiceProvider services)
{
    using var scope = services.CreateScope();
    var accessService = scope.ServiceProvider.GetRequiredService<IUserAccessService>();
    await accessService.EnsureAccessMatrixAsync(CancellationToken.None);
}
