var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.UseHttpsRedirection();

app.MapGet("/", () => Results.Ok(new
{
    Name = "Crop QC Dashboard API",
    Scope = "MVP 1 Receiving/QC placeholder"
}));

app.Run();
