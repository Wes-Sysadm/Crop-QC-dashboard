using CropQc.Api.Services;
using CropQc.Data;
using CropQc.Shared.Storage;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
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
app.MapControllers();

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
        RootFolderId = configuration["GoogleDrive:RootFolderId"] ?? "",
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
