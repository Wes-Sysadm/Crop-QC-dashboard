using CropQc.Api.Services;
using CropQc.Data;
using CropQc.Data.Inventory;
using CropQc.Shared.Storage;
using CropQc.Data.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Antiforgery;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCanonicalInventoryReads();
builder.Services.AddCanonicalInventoryCommands(builder.Configuration.GetValue<bool>("CanonicalInventoryCommandsEnabled"));
builder.Services.AddControllers();
builder.Services.AddHttpContextAccessor();
OperatorSession.ConfigureKeys(builder.Services, builder.Configuration);
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options => OperatorSession.ConfigureCookie(options, builder.Configuration, builder.Environment.IsDevelopment(), api: true));
builder.Services.AddAuthorization();
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.AddScoped<CanonicalReceivingAuthorizationFilter>();
builder.Services.AddOpenApi();
builder.Services.AddDbContext<CropQcDbContext>(options =>
    CropQcDatabase.Configure(
        options,
        builder.Configuration["DATABASE_PROVIDER"] ?? builder.Configuration["Database:Provider"],
        builder.Configuration.GetConnectionString(builder.Configuration["Database:ConnectionStringName"] ?? CropQcDatabase.DefaultConnectionStringName)));
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IMasterDataService, MasterDataService>();
builder.Services.AddScoped<IReceiptService, ReceiptService>();
builder.Services.AddScoped<IQcSampleService, QcSampleService>();
builder.Services.AddScoped<IQcFruitReadingService, QcFruitReadingService>();
builder.Services.AddScoped<IQcPhotoService, QcPhotoService>();
builder.Services.AddScoped<IQcSummaryService, QcSummaryService>();
builder.Services.AddScoped<IQcSummaryEmailLogService, QcSummaryEmailLogService>();
builder.Services.AddScoped<IQcStationApiService, QcStationApiService>();
builder.Services.AddSingleton(CreateFileStorageOptions(builder.Configuration));
builder.Services.AddSingleton(CreateGoogleDriveStorageOptions(builder.Configuration));
builder.Services.AddSingleton<IFileStorageService>(services => CreateFileStorageService(
    services.GetRequiredService<FileStorageOptions>(),
    services.GetRequiredService<GoogleDriveStorageOptions>(),
    services.GetRequiredService<ILogger<GoogleDriveStorageService>>()));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/api/operator-session", async (HttpContext context, CropQcDbContext db, IAntiforgery antiforgery) =>
{
    if (!await OperatorSession.CanReceiveAsync(db, context.User, context.RequestAborted)) return Results.Forbid();
    context.Response.Headers.CacheControl = "no-store";
    var tokens = antiforgery.GetAndStoreTokens(context);
    return Results.Ok(new { tokens.RequestToken, tokens.HeaderName });
}).RequireAuthorization();

app.MapGet("/", () => Results.Ok(new
{
    Name = "Crop QC Dashboard API",
    Scope = "MVP 1 Receiving/QC placeholder"
}));

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
