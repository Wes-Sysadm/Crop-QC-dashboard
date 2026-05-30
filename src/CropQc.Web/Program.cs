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
builder.Services.AddSingleton(googleAuthOptions);
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
        options.Events.OnCreatingTicket = async context =>
        {
            var sessionLifetime = TimeSpan.FromDays(googleAuthOptions.SessionDays);
            var configuredOptions = context.HttpContext.RequestServices.GetRequiredService<GoogleAuthenticationOptions>();
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("GoogleAuth");
            var provisioner = context.HttpContext.RequestServices.GetRequiredService<IGoogleUserProvisioningService>();
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
        };
        options.Events.OnRemoteFailure = context =>
        {
            context.HandleResponse();
            var message = UrlEncoder.Default.Encode(context.Failure?.Message ?? "Google login failed.");
            context.Response.Redirect($"/Login?error={message}");
            return Task.CompletedTask;
        };
    });
}
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
    options.AddPolicy("RequireAdmin", policy => policy.RequireRole("Admin"));
    options.AddPolicy("RequireManagerOrAdmin", policy => policy.RequireRole("Admin", "Manager"));
    options.AddPolicy("RequireQcUserOrHigher", policy => policy.RequireRole("Admin", "Manager", "QC User"));
    options.AddPolicy("RequireAuthenticatedUser", policy => policy.RequireAuthenticatedUser());
});
builder.Services.AddDbContext<CropQcDbContext>(options =>
    CropQcDatabase.Configure(
        options,
        builder.Configuration["DATABASE_PROVIDER"] ?? builder.Configuration["Database:Provider"],
        builder.Configuration.GetConnectionString(builder.Configuration["Database:ConnectionStringName"] ?? CropQcDatabase.DefaultConnectionStringName),
        sqlOptions => sqlOptions.CommandTimeout(3)));
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IDashboardDataService, DashboardDataService>();
builder.Services.AddScoped<IGoogleUserProvisioningService, GoogleUserProvisioningService>();
builder.Services.AddScoped<IMasterDataSeeder, MasterDataSeeder>();
builder.Services.AddScoped<IReceivingExportService, ReceivingExportService>();
builder.Services.AddScoped<IAdminAuthorizationService, AdminAuthorizationService>();
builder.Services.AddScoped<IAdminManagementService, AdminManagementService>();
builder.Services.AddScoped<IUserAdminService, UserAdminService>();
builder.Services.AddScoped<IQcStationAdminService, QcStationAdminService>();
builder.Services.AddSingleton(CreateFileStorageOptions(builder.Configuration));
builder.Services.AddSingleton(CreateGoogleDriveStorageOptions(builder.Configuration));
builder.Services.AddSingleton<IFileStorageService>(services => CreateFileStorageService(
    services.GetRequiredService<FileStorageOptions>(),
    services.GetRequiredService<GoogleDriveStorageOptions>(),
    services.GetRequiredService<ILogger<GoogleDriveStorageService>>()));

var app = builder.Build();
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

if (useForwardedHeaders)
{
    app.UseForwardedHeaders();
}

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
}).RequireAuthorization("RequireAdmin");
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

static IFileStorageService CreateFileStorageService(
    FileStorageOptions fileStorageOptions,
    GoogleDriveStorageOptions googleDriveOptions,
    ILogger<GoogleDriveStorageService> googleDriveLogger)
{
    if (string.Equals(fileStorageOptions.Provider, FileStorageProviders.GoogleDrive, StringComparison.OrdinalIgnoreCase))
    {
        return new GoogleDriveStorageService(googleDriveOptions, logger: googleDriveLogger);
    }

    return new LocalFileStorageService(fileStorageOptions);
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
