using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using CropQc.Shared.Inventory;

namespace CropQc.Api.Tests;

public sealed class CanonicalInventoryActivationTests
{
    [Fact]
    public async Task Registered_executor_is_dormant_while_feature_is_off()
    {
        var services = new ServiceCollection();
        services.AddDbContext<CropQcDbContext>(o => o.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddCanonicalInventoryCommands(false);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var commands = scope.ServiceProvider.GetRequiredService<IInventoryCommandExecutor>();
        var result = await commands.ExecuteAsync(new("off", InventoryCommandKind.ReceiveStock, 1, DateTimeOffset.UtcNow, "Dormancy test", []));
        Assert.Equal(InventoryCommandStatus.Blocked, result.Status);
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<CropQcDbContext>().InventoryCommands.ToListAsync());
    }

    [InventoryPostgresFact]
    public async Task Disabling_flag_after_canonical_history_does_not_reenable_legacy_physical_writes()
    {
        await using var f = await InventoryCommandTests.Fixture.Create();
        var command = await f.Command(InventoryCommandKind.Loss, 19);
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(command)).Status);
        await using var off = f.CreateDbContext();
        Assert.False(off.CanonicalInventoryEnabled);
        var before = await f.Snapshot();
        var segment = await off.TreatmentLineageSegments.FirstAsync();
        segment.CurrentBins += 1;
        await Assert.ThrowsAsync<InventoryWriterNotMigratedException>(() => off.SaveChangesAsync());
        off.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InventoryWriterNotMigratedException>(() => off.Database.ExecuteSqlRawAsync("UPDATE \"Receipts\" SET \"BinCount\" = 999"));
        await Assert.ThrowsAsync<InventoryWriterNotMigratedException>(() => off.Receipts.ExecuteUpdateAsync(s => s.SetProperty(x => x.BinCount, 999)));
        Assert.Equal(before, await f.Snapshot());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Ordinary_projection_writer_is_rejected_only_when_global_mode_is_on(bool enabled)
    {
        var options = new DbContextOptionsBuilder<CropQcDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new CropQcDbContext(options, new(enabled));
        db.TreatmentLineageSegments.Add(new()
        {
            IdentityKey = "fixture",
            TreatmentSignature = "u",
            TreatmentState = "Untreated",
            GrowerNameSnapshot = "fixture",
            LotNumberSnapshot = "1",
            VarietyCodeSnapshot = "BART",
            InventoryStatusSnapshot = "Conventional",
            ProductionTypeSnapshot = "Conventional"
        });
        if (enabled) await Assert.ThrowsAsync<InventoryWriterNotMigratedException>(() => db.SaveChangesAsync());
        else await db.SaveChangesAsync();
        await using var verify = new CropQcDbContext(options);
        Assert.Equal(enabled ? 0 : 1, await verify.TreatmentLineageSegments.CountAsync());
        Assert.Empty(await verify.InventoryCommands.ToListAsync());
    }
}
