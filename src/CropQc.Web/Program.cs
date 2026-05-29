using CropQc.Data;
using CropQc.Shared.Storage;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();
builder.Services.AddControllersWithViews();
builder.Services.AddDbContext<CropQcDbContext>(options =>
    CropQcDatabase.Configure(
        options,
        builder.Configuration["DATABASE_PROVIDER"] ?? builder.Configuration["Database:Provider"],
        builder.Configuration.GetConnectionString(builder.Configuration["Database:ConnectionStringName"] ?? CropQcDatabase.DefaultConnectionStringName),
        sqlOptions => sqlOptions.CommandTimeout(3)));
builder.Services.AddScoped<IDashboardDataService, DashboardDataService>();
builder.Services.AddSingleton(CreateFileStorageOptions(builder.Configuration));
builder.Services.AddSingleton<IFileStorageService, LocalFileStorageService>();

var app = builder.Build();
var isRender = !string.IsNullOrWhiteSpace(app.Configuration["RENDER_EXTERNAL_HOSTNAME"])
    || !string.IsNullOrWhiteSpace(app.Configuration["RENDER_EXTERNAL_URL"]);

if (app.Configuration.GetValue<bool>("Database:EnsureCreatedOnStartup"))
{
    using var scope = app.Services.CreateScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
    await dbContext.Database.EnsureCreatedAsync();
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
app.MapGet("/health", () => Results.Text("Crop QC Dashboard OK", "text/plain"));
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
});
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
