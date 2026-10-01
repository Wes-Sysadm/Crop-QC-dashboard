using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Api.Tests;

public sealed class CanonicalInventoryActivationTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Ordinary_projection_writer_is_rejected_only_when_global_mode_is_on(bool enabled)
    {
        var options = new DbContextOptionsBuilder<CropQcDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new CropQcDbContext(options, new(enabled));
        db.TreatmentLineageSegments.Add(new()
        {
            IdentityKey = "fixture", TreatmentSignature = "u", TreatmentState = "Untreated",
            GrowerNameSnapshot = "fixture", LotNumberSnapshot = "1", VarietyCodeSnapshot = "BART", InventoryStatusSnapshot = "Conventional", ProductionTypeSnapshot = "Conventional"
        });
        if (enabled) await Assert.ThrowsAsync<InventoryWriterNotMigratedException>(() => db.SaveChangesAsync());
        else await db.SaveChangesAsync();
        await using var verify = new CropQcDbContext(options);
        Assert.Equal(enabled ? 0 : 1, await verify.TreatmentLineageSegments.CountAsync());
        Assert.Empty(await verify.InventoryCommands.ToListAsync());
    }
}
