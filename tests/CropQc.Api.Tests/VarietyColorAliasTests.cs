using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Api.Tests;

public sealed class VarietyColorAliasTests
{
    [Theory]
    [InlineData("GSMT")]
    [InlineData("gsmt")]
    [InlineData("Grannysmith")]
    [InlineData("granny smith")]
    [InlineData("  Granny   Smith  ")]
    public void GrannySmithAliases_NormalizeToCanonicalVariety(string value)
    {
        var identity = VarietyColorService.NormalizeIdentity(value, value);

        Assert.Equal("GRANNY_SMITH", identity.Key);
        Assert.Equal("Granny Smith", identity.Name);
    }

    [Theory]
    [InlineData("Pink")]
    [InlineData("pink lady")]
    [InlineData("PINK_LADY")]
    [InlineData("  Pink   Lady  ")]
    public void PinkAliases_NormalizeToCanonicalVariety(string value)
    {
        var identity = VarietyColorService.NormalizeIdentity(value, value);

        Assert.Equal("PINK_LADY", identity.Key);
        Assert.Equal("Pink Lady", identity.Name);
    }

    [Theory]
    [InlineData("Red")]
    [InlineData("red delicious")]
    [InlineData("RED_DELICIOUS")]
    [InlineData("  Red   Delicious  ")]
    public void RedAliases_NormalizeToCanonicalVariety(string value)
    {
        var identity = VarietyColorService.NormalizeIdentity(value, value);

        Assert.Equal("RED_DELICIOUS", identity.Key);
        Assert.Equal("Red Delicious", identity.Name);
    }

    [Fact]
    public void UnrelatedVarieties_RemainSeparate()
    {
        var fuji = VarietyColorService.NormalizeIdentity("Fuji", "Fuji");
        var gala = VarietyColorService.NormalizeIdentity("Gala", "Gala");

        Assert.Equal("FUJI", fuji.Key);
        Assert.Equal("Fuji", fuji.Name);
        Assert.Equal("GALA", gala.Key);
        Assert.Equal("Gala", gala.Name);
        Assert.NotEqual(fuji.Key, gala.Key);
    }

    [Fact]
    public void OrganicAndConventionalProfiles_ShareCanonicalVarietyIdentity()
    {
        var conventional = new FruitProfile
        {
            Name = "GSMT",
            VarietyCode = "GSMT",
            FruitType = "Apple",
            ProductionType = "Conventional",
            IsOrganic = false
        };
        var organic = new FruitProfile
        {
            Name = "Organic Grannysmith",
            VarietyCode = "GRANNYSMITH",
            FruitType = "Apple",
            ProductionType = "Organic",
            IsOrganic = true
        };

        Assert.Equal(VarietyColorService.IdentityFromProfile(conventional), VarietyColorService.IdentityFromProfile(organic));
    }

    [Fact]
    public async Task AdminVarietyColors_ShowOneSelectorPerCanonicalVariety()
    {
        await using var db = CreateDbContext();
        SeedAdjustmentVarieties(db, "GSMT", "Grannysmith", "Fuji", "Pink", "Pink Lady", "Red", "Red delicious");
        await db.SaveChangesAsync();
        var service = new VarietyColorService(db);

        var page = await service.GetAdminPageAsync(canManage: true, CancellationToken.None);

        Assert.Single(page.Varieties, x => x.VarietyKey == "GRANNY_SMITH");
        Assert.Single(page.Varieties, x => x.VarietyKey == "PINK_LADY");
        Assert.Single(page.Varieties, x => x.VarietyKey == "RED_DELICIOUS");
        Assert.DoesNotContain(page.Varieties, x => x.VarietyKey is "GSMT" or "GRANNYSMITH" or "PINK" or "RED");
        Assert.Contains(page.Varieties, x => x.VarietyKey == "FUJI");
    }

    [Fact]
    public async Task AliasConfigurations_ResolveThroughCanonicalColorLookup()
    {
        await using var db = CreateDbContext();
        db.VarietyColorConfigurations.Add(Config("GSMT", "GSMT", "#123456", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var service = new VarietyColorService(db);

        var colors = await service.GetResolvedColorsAsync(["GRANNY_SMITH"], CancellationToken.None);

        Assert.True(colors["GRANNY_SMITH"].IsConfigured);
        Assert.Equal("#123456", colors["GRANNY_SMITH"].HexColor);
        Assert.Equal("Granny Smith", colors["GRANNY_SMITH"].VarietyName);
    }

    [Fact]
    public void MultiVarietyProportions_AreCalculatedAfterAliasNormalization()
    {
        var lots = new[]
        {
            new { Variety = "GSMT", Bins = 200 },
            new { Variety = "Grannysmith", Bins = 300 },
            new { Variety = "Fuji", Bins = 500 }
        };
        var totalBins = lots.Sum(x => x.Bins);

        var segments = lots
            .GroupBy(x => VarietyColorService.NormalizeIdentity(x.Variety, x.Variety).Key)
            .ToDictionary(
                x => x.Key,
                x => decimal.Round(x.Sum(y => y.Bins) / (decimal)totalBins * 100m, 1));

        Assert.Equal(2, segments.Count);
        Assert.Equal(50m, segments["GRANNY_SMITH"]);
        Assert.Equal(50m, segments["FUJI"]);
    }

    [Fact]
    public async Task AliasConfigurationConflicts_PreferExplicitCanonicalRecordAndAuditConsolidation()
    {
        await using var db = CreateDbContext();
        SeedAdjustmentVarieties(db, "GSMT");
        db.VarietyColorConfigurations.AddRange(
            Config("GRANNY_SMITH", "Granny Smith", "#111111", DateTimeOffset.UtcNow.AddDays(-3)),
            Config("GSMT", "GSMT", "#222222", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var service = new VarietyColorService(db);

        var page = await service.GetAdminPageAsync(canManage: true, CancellationToken.None);

        var row = Assert.Single(page.Varieties, x => x.VarietyKey == "GRANNY_SMITH");
        Assert.Equal("#111111", row.HexColor);
        Assert.Single(await db.VarietyColorConfigurations.Where(x => x.VarietyKey == "GRANNY_SMITH").ToListAsync());
        Assert.DoesNotContain(await db.VarietyColorConfigurations.ToListAsync(), x => x.VarietyKey == "GSMT");
        Assert.Contains(await db.AuditLogs.ToListAsync(), x => x.Action == "consolidate-variety-alias" && x.EntityKey == "GRANNY_SMITH");
    }

    [Fact]
    public async Task AliasConfigurationConflictsWithoutCanonicalRecord_PreferMostRecentlyUpdatedAlias()
    {
        await using var db = CreateDbContext();
        SeedAdjustmentVarieties(db, "GSMT");
        db.VarietyColorConfigurations.AddRange(
            Config("GSMT", "GSMT", "#111111", DateTimeOffset.UtcNow.AddDays(-3)),
            Config("GRANNYSMITH", "Grannysmith", "#222222", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var service = new VarietyColorService(db);

        var page = await service.GetAdminPageAsync(canManage: true, CancellationToken.None);

        var row = Assert.Single(page.Varieties, x => x.VarietyKey == "GRANNY_SMITH");
        Assert.Equal("#222222", row.HexColor);
        Assert.Single(await db.VarietyColorConfigurations.Where(x => x.VarietyKey == "GRANNY_SMITH").ToListAsync());
    }

    [Fact]
    public async Task AdminCanonicalGrouping_DoesNotRewriteSourceInventoryVarietyText()
    {
        await using var db = CreateDbContext();
        SeedAdjustmentVarieties(db, "GSMT", "Grannysmith");
        await db.SaveChangesAsync();
        var service = new VarietyColorService(db);

        await service.GetAdminPageAsync(canManage: true, CancellationToken.None);

        Assert.Contains(await db.RoomInventoryAdjustments.ToListAsync(), x => x.VarietyCode == "GSMT");
        Assert.Contains(await db.RoomInventoryAdjustments.ToListAsync(), x => x.VarietyCode == "Grannysmith");
    }

    [Fact]
    public async Task MasterDataFruitProfiles_ShowOneCanonicalColorRowForAliases()
    {
        await using var db = CreateDbContext();
        db.VarietyColorConfigurations.Add(Config("GSMT", "GSMT", "#123456", DateTimeOffset.UtcNow));
        await db.SaveChangesAsync();
        var colorService = new VarietyColorService(db);
        var masterData = new AdminManagementService(db, colorService);

        var page = await masterData.GetMasterDataAsync("fruit-profiles", canEdit: true, CancellationToken.None);

        var granny = Assert.Single(page.Items, x => x.VarietyColor?.VarietyKey == "GRANNY_SMITH");
        Assert.Equal("#123456", granny.VarietyColor!.HexColor);
        Assert.Contains("GSMT", granny.Cells[0]);
        Assert.Contains("ORGS", granny.Cells[0]);
        Assert.Contains("Grannysmith", granny.Cells[4]);
        Assert.DoesNotContain(page.Items, x => x.VarietyColor?.VarietyKey == "GSMT");
    }

    [Fact]
    public async Task MasterDataFruitProfileEdit_SavesConfiguredColorWithMasterFruitProfileLink()
    {
        await using var db = CreateDbContext();
        var colorService = new VarietyColorService(db);
        var masterData = new AdminManagementService(db, colorService);

        var error = await masterData.SaveMasterDataAsync(new MasterDataEditForm
        {
            Type = "fruit-profiles",
            Id = 4,
            Code = "GSMT",
            Name = "Granny Smith",
            FruitType = "Apple",
            ProductionType = "Conventional",
            IsActive = true,
            VarietyHexColor = "#ABCDEF"
        }, "admin@fruitandland.com", CancellationToken.None);

        Assert.Null(error);
        var config = await db.VarietyColorConfigurations.SingleAsync(x => x.VarietyKey == "GRANNY_SMITH");
        Assert.Equal("#ABCDEF", config.HexColor);
        Assert.Equal(4, config.FruitProfileId);
        Assert.Contains(await db.AuditLogs.ToListAsync(), x => x.EntityName == nameof(VarietyColorConfiguration) && x.EntityKey == "GRANNY_SMITH");
    }

    [Fact]
    public async Task MasterDataFruitProfileEdit_RejectsInvalidColorServerSide()
    {
        await using var db = CreateDbContext();
        var colorService = new VarietyColorService(db);
        var masterData = new AdminManagementService(db, colorService);

        var error = await masterData.SaveMasterDataAsync(new MasterDataEditForm
        {
            Type = "fruit-profiles",
            Id = 4,
            Code = "GSMT",
            Name = "Granny Smith",
            FruitType = "Apple",
            ProductionType = "Conventional",
            IsActive = true,
            VarietyHexColor = "not-a-color"
        }, "admin@fruitandland.com", CancellationToken.None);

        Assert.Equal("Enter a valid hex color such as #2F80ED.", error);
        Assert.Empty(await db.VarietyColorConfigurations.ToListAsync());
    }

    [Fact]
    public void FallbackColors_AreDeterministicForCanonicalAliases()
    {
        var gsmt = VarietyColorService.NormalizeIdentity("GSMT", "GSMT");
        var grannysmith = VarietyColorService.NormalizeIdentity("Grannysmith", "Grannysmith");

        Assert.Equal(VarietyColorService.FallbackColor(gsmt.Key), VarietyColorService.FallbackColor(grannysmith.Key));
    }

    private static CropQcDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new CropQcDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static void SeedAdjustmentVarieties(CropQcDbContext db, params string[] varieties)
    {
        var warehouse = new Warehouse { Id = 900001, Code = "TEST-ALIAS", Name = "Test Alias Warehouse" };
        var room = new Room { Id = 900001, WarehouseId = warehouse.Id, Warehouse = warehouse, Code = "TEST-ALIAS-ROOM", Name = "Test Alias Room" };
        db.Warehouses.Add(warehouse);
        db.Rooms.Add(room);

        var id = 900001L;
        foreach (var variety in varieties)
        {
            db.RoomInventoryAdjustments.Add(new RoomInventoryAdjustment
            {
                Id = id++,
                WarehouseId = warehouse.Id,
                Warehouse = warehouse,
                RoomId = room.Id,
                Room = room,
                GrowerName = "Grower",
                LotNumber = $"LOT-{id}",
                VarietyCode = variety,
                NewBinCount = 10,
                ChangeAmount = 10,
                AdjustmentType = "CurrentBalance",
                AdjustmentAt = DateTimeOffset.UtcNow,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
    }

    private static VarietyColorConfiguration Config(string key, string name, string color, DateTimeOffset updatedAt) =>
        new()
        {
            VarietyKey = key,
            VarietyName = name,
            HexColor = color,
            CreatedAt = updatedAt.AddDays(-1),
            UpdatedAt = updatedAt
        };
}
