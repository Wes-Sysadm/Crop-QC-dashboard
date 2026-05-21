var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/", () => "Crop QC Dashboard web placeholder - MVP 1 Receiving/QC only.");

app.Run();
