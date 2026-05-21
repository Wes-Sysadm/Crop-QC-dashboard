using CropQc.Api.Services;
using CropQc.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddDbContext<CropQcDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("CropQc")
        ?? "Server=(localdb)\\mssqllocaldb;Database=CropQcDashboard;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True"));
builder.Services.AddScoped<IAuditService, AuditService>();
builder.Services.AddScoped<IMasterDataService, MasterDataService>();
builder.Services.AddScoped<IReceiptService, ReceiptService>();
builder.Services.AddScoped<IQcSampleService, QcSampleService>();
builder.Services.AddScoped<IQcFruitReadingService, QcFruitReadingService>();
builder.Services.AddScoped<IQcPhotoService, QcPhotoService>();
builder.Services.AddScoped<IQcSummaryService, QcSummaryService>();
builder.Services.AddScoped<IQcSummaryEmailLogService, QcSummaryEmailLogService>();

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
