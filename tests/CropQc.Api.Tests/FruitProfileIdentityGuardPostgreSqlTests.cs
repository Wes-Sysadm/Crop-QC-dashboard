using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Api.Tests;

public sealed class FruitProfileIdentityGuardPostgreSqlTests
{
    [FruitProfilePostgreSqlFact]
    public async Task PostgreSql_SaveTimeReferences_Concurrency_Cosmetics_AndAuditAtomicity()
    {
        var connection = Environment.GetEnvironmentVariable("CROPQC_TEST_FRUIT_PROFILE_POSTGRES")!;
        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connection);
        var options = new DbContextOptionsBuilder<CropQcDbContext>().UseNpgsql(connection).Options;
        await using var db = new CropQcDbContext(options);
        await db.Database.EnsureCreatedAsync();
        Assert.False(await db.Receipts.AnyAsync());
        db.Rooms.Add(new Room { Id = 90001, WarehouseId = 1, Code = "TEST-FP", Name = "Test profile room" });
        await db.SaveChangesAsync();
        var service = new AdminManagementService(db, new VarietyColorService(db));
        var form = (await service.GetEditFormAsync("fruit-profiles", 2, default))!;
        form.Code = "TEST-GALA";
        Assert.Null(await service.SaveMasterDataAsync(form, "admin@example.test", default));

        // An in-flight first Receipt takes a FK key-share lock. The edit must fail closed,
        // not see zero committed references and mutate under that new Receipt.
        await using (var writer = new CropQcDbContext(options))
        await using (var transaction = await writer.Database.BeginTransactionAsync())
        {
            writer.Receipts.Add(Receipt());
            await writer.SaveChangesAsync();
            form.Code = "BLOCKED";
            Assert.Contains("Nothing was saved", await service.SaveMasterDataAsync(form, "admin@example.test", default));
            await transaction.RollbackAsync();
        }

        // Snapshot-only IDs need protection too; they have no FruitProfile foreign key.
        await using (var writer = new CropQcDbContext(options))
        await using (var transaction = await writer.Database.BeginTransactionAsync())
        {
            await writer.Database.ExecuteSqlRawAsync("LOCK TABLE \"BinsRunEntries\" IN ROW EXCLUSIVE MODE");
            Assert.Contains("Nothing was saved", await service.SaveMasterDataAsync(form, "admin@example.test", default));
            await transaction.RollbackAsync();
        }

        // A second Master Data request must not overlap the locked profile save.
        await using (var writer = new CropQcDbContext(options))
        await using (var transaction = await writer.Database.BeginTransactionAsync())
        {
            await writer.Database.ExecuteSqlRawAsync("SELECT 1 FROM \"FruitProfiles\" WHERE \"Id\" = 2 FOR UPDATE");
            Assert.Contains("Nothing was saved", await service.SaveMasterDataAsync(form, "admin@example.test", default));
            await transaction.RollbackAsync();
        }

        await using (var writer = new CropQcDbContext(options))
        {
            writer.Receipts.Add(Receipt());
            await writer.SaveChangesAsync();
        }
        var auditCount = await db.AuditLogs.CountAsync();
        form.Name = "Must not save";
        form.VarietyHexColor = "#123456";
        Assert.Contains("operational history", await service.SaveMasterDataAsync(form, "admin@example.test", default));
        Assert.Equal(auditCount, await db.AuditLogs.CountAsync());
        Assert.Empty(await db.VarietyColorConfigurations.ToListAsync());
        var profile = await db.FruitProfiles.AsNoTracking().SingleAsync(x => x.Id == 2);
        Assert.Equal("Gala", profile.Name);
        Assert.Equal("TEST-GALA", profile.VarietyCode);

        form = (await service.GetEditFormAsync("fruit-profiles", 2, default))!;
        form.Description = "Safe cosmetic edit";
        form.VarietyHexColor = "#123456";
        Assert.Null(await service.SaveMasterDataAsync(form, "admin@example.test", default));
        Assert.Equal("#123456", (await db.VarietyColorConfigurations.SingleAsync()).HexColor);
        Assert.Equal(10, (await db.Receipts.SingleAsync()).BinCount);
        Assert.Empty(await db.RoomInventoryAdjustments.ToListAsync());
        Assert.Empty(await db.InventoryIdentityCorrections.ToListAsync());

        // The narrow Fruit Profile transaction rolls back a profile update if its audit fails.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION batch1b_reject_audit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'synthetic audit failure'; END $$;
            CREATE TRIGGER batch1b_audit_failure BEFORE INSERT ON "AuditLogs"
            FOR EACH ROW EXECUTE FUNCTION batch1b_reject_audit();
            """);
        try
        {
            form.Description = "Must roll back";
            await Assert.ThrowsAsync<DbUpdateException>(() => service.SaveMasterDataAsync(form, "admin@example.test", default));
            await using var check = new CropQcDbContext(options);
            Assert.Equal("Safe cosmetic edit", (await check.FruitProfiles.SingleAsync(x => x.Id == 2)).Description);
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER batch1b_audit_failure ON \"AuditLogs\"; DROP FUNCTION batch1b_reject_audit()");
        }
    }

    private static Receipt Receipt() => new()
    {
        Id = 90001,
        CropYear = 2026,
        WarehouseId = 1,
        RoomId = 90001,
        FruitProfileId = 2,
        CompuTechReceiptId = "TEST-BATCH1B",
        GrowerName = "Synthetic",
        LotCode = "TEST",
        BinCount = 10,
        ReceivedAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };
}

public sealed class FruitProfilePostgreSqlFactAttribute : FactAttribute
{
    public FruitProfilePostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CROPQC_TEST_FRUIT_PROFILE_POSTGRES")))
            Skip = "Requires a new isolated PostgreSQL database via CROPQC_TEST_FRUIT_PROFILE_POSTGRES.";
    }
}
