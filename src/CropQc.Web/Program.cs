using CropQc.Data;
using CropQc.Shared.Storage;
using CropQc.Web.Auth;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Encodings.Web;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Services.AddControllersWithViews();
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
});
builder.Services.AddDbContext<CropQcDbContext>(options =>
    CropQcDatabase.Configure(
        options,
        builder.Configuration["DATABASE_PROVIDER"] ?? builder.Configuration["Database:Provider"],
        builder.Configuration.GetConnectionString(builder.Configuration["Database:ConnectionStringName"] ?? CropQcDatabase.DefaultConnectionStringName),
        sqlOptions => sqlOptions.CommandTimeout(3)));
builder.Services.AddScoped<IDashboardDataService, DashboardDataService>();
builder.Services.AddScoped<IGoogleUserProvisioningService, GoogleUserProvisioningService>();
builder.Services.AddScoped<IMasterDataSeeder, MasterDataSeeder>();
builder.Services.AddScoped<IReceivingExportService, ReceivingExportService>();
builder.Services.AddScoped<IAdminAuthorizationService, AdminAuthorizationService>();
builder.Services.AddScoped<IAdminManagementService, AdminManagementService>();
builder.Services.AddScoped<IUserAdminService, UserAdminService>();
builder.Services.AddSingleton(CreateFileStorageOptions(builder.Configuration));
builder.Services.AddSingleton<IFileStorageService, LocalFileStorageService>();

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
            sizeThresholds = await dbContext.FruitSizeConversionThresholds.CountAsync(cancellationToken)
        });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Master data health check failed: {ex.Message}", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}).AllowAnonymous();
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
