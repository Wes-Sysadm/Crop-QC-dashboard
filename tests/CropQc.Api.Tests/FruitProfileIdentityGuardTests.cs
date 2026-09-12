using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using System.Text.Json;

namespace CropQc.Api.Tests;

public sealed class FruitProfileIdentityGuardTests
{
    [Theory]
    [InlineData("variety")]
    [InlineData("production")]
    [InlineData("commodity")]
    public async Task MasterDataEdit_MustNotReinterpretExistingInventory(string field)
    {
        await using var db = CreateDb();
        var profile = await SeedInventory(db);
        var service = new AdminManagementService(db, new VarietyColorService(db));
        var form = (await service.GetEditFormAsync("fruit-profiles", profile.Id, default))!;
        var before = Assert.Single(await new RoomInventoryLedgerQueryService(db).GetSnapshotsAsync(90001, null, default));
        if (field == "variety") form.Code = "CHANGED-GALA";
        else if (field == "commodity") form.FruitType = "Pear";
        else form.ProductionType = "Organic";

        var error = await service.SaveMasterDataAsync(form, "admin@example.test", default);
        var after = Assert.Single(await new RoomInventoryLedgerQueryService(db).GetSnapshotsAsync(90001, null, default));

        Assert.True(error is not null,
            $"Unsafe edit succeeded: {before.Variety}/{before.ProductionType}/{before.IsOrganic} -> {after.Variety}/{after.ProductionType}/{after.IsOrganic}; no identity correction was created.");
        Assert.Equal(before, after);
        Assert.Empty(await db.InventoryIdentityCorrections.ToListAsync());
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Theory]
    [InlineData("variety")]
    [InlineData("production")]
    [InlineData("commodity")]
    public async Task UnusedProfile_IdentityEditAllowed(string field)
    {
        await using var db = CreateDb();
        var service = new AdminManagementService(db, new VarietyColorService(db));
        var form = (await service.GetEditFormAsync("fruit-profiles", 2, default))!;
        if (field == "variety") form.Code = "NEW-CODE";
        else if (field == "commodity") form.FruitType = "Cherry";
        else form.ProductionType = "Organic";
        Assert.Null(await service.SaveMasterDataAsync(form, "admin@example.test", default));
        var saved = await db.FruitProfiles.FindAsync(2);
        Assert.Equal(form.Code, saved!.VarietyCode);
        Assert.Equal(form.FruitType, saved.FruitType);
        Assert.Equal(form.ProductionType, saved.ProductionType);
        Assert.Equal(form.ProductionType == "Organic", saved.IsOrganic);
        Assert.Single(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task NewProfile_CanBeFullyConfigured_ProductionTypeControlsOrganicFlag()
    {
        await using var db = CreateDb();
        var service = new AdminManagementService(db, new VarietyColorService(db));
        Assert.Null(await service.SaveMasterDataAsync(new MasterDataEditForm
        {
            Type = "fruit-profiles",
            Code = "NEW",
            Name = "New variety",
            FruitType = "Cherry",
            ProductionType = "Organic",
            IsOrganic = false,
            Description = "Description",
            IsActive = true
        }, "admin@example.test", default));
        var saved = await db.FruitProfiles.SingleAsync(x => x.VarietyCode == "NEW");
        Assert.True(saved.IsOrganic);
        Assert.Equal("Organic", saved.ProductionType);
        Assert.Empty(await db.RoomInventoryAdjustments.ToListAsync());
    }

    [Fact]
    public async Task UsedProfile_CosmeticEditAndColorAllowed_IdentityAndHistoryUnchanged()
    {
        await using var db = CreateDb();
        var profile = await SeedInventory(db);
        SeedReference(db, typeof(Receipt), "FruitProfileId", profile.Id);
        await db.SaveChangesAsync();
        var history = OperationalState(db);
        var service = new AdminManagementService(db, new VarietyColorService(db));
        var form = (await service.GetEditFormAsync("fruit-profiles", profile.Id, default))!;
        form.Name = "Gala display label";
        form.Description = "New descriptive text";
        form.IsActive = false;
        form.VarietyHexColor = "#123456";
        // Crafted independent flag is not an alternative classification edit mechanism.
        form.IsOrganic = true;
        Assert.Null(await service.SaveMasterDataAsync(form, "admin@example.test", default));
        Assert.Equal(form.Name, profile.Name);
        Assert.Equal(form.Description, profile.Description);
        Assert.False(profile.IsActive);
        Assert.False(profile.IsOrganic);
        Assert.Equal("Conventional", profile.ProductionType);
        Assert.Equal(history, OperationalState(db));
        Assert.Equal("#123456", (await db.VarietyColorConfigurations.SingleAsync()).HexColor);
        Assert.Contains(await db.AuditLogs.ToListAsync(), x => x.EntityName == "fruit-profiles");
    }

    [Fact]
    public async Task MixedRequest_IsRejectedWithoutAnyPartialWrites()
    {
        await using var db = CreateDb();
        var profile = await SeedInventory(db);
        SeedReference(db, typeof(Receipt), "FruitProfileId", profile.Id);
        SeedReference(db, typeof(BinsRunEntry), "ReportingFruitProfileIdSnapshot", profile.Id);
        SeedReference(db, typeof(TreatmentLineageSegment), "FruitProfileId", profile.Id);
        await db.SaveChangesAsync();
        var history = OperationalState(db);
        var service = new AdminManagementService(db, new VarietyColorService(db));
        var form = (await service.GetEditFormAsync("fruit-profiles", profile.Id, default))!;
        form.Name = "Changed";
        form.Description = "Changed";
        form.ProductionType = "Organic";
        form.VarietyHexColor = "#ABCDEF";
        form.IsActive = false;
        Assert.Contains("operational history", await service.SaveMasterDataAsync(form, "admin@example.test", default));
        Assert.Equal("Gala", profile.Name);
        Assert.Null(profile.Description);
        Assert.True(profile.IsActive);
        Assert.False(profile.IsOrganic);
        Assert.Equal(history, OperationalState(db));
        Assert.Empty(await db.AuditLogs.ToListAsync());
        Assert.Empty(await db.VarietyColorConfigurations.ToListAsync());
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task DepletedHistory_StillBlocksIdentityChange()
    {
        await using var db = CreateDb();
        var profile = await SeedInventory(db);
        var adjustment = await db.RoomInventoryAdjustments.SingleAsync();
        adjustment.ChangeAmount = 0;
        adjustment.NewBinCount = 0;
        await db.SaveChangesAsync();
        var service = new AdminManagementService(db, new VarietyColorService(db));
        var form = (await service.GetEditFormAsync("fruit-profiles", profile.Id, default))!;
        form.ProductionType = "Organic";
        Assert.Contains("operational history", await service.SaveMasterDataAsync(form, "admin@example.test", default));
        Assert.Equal(0, (await db.RoomInventoryAdjustments.SingleAsync()).NewBinCount);
        Assert.False(profile.IsOrganic);
    }

    public static IEnumerable<object[]> OperationalReferences()
    {
        foreach (var type in new[] { typeof(Receipt), typeof(RoomInventoryAdjustment), typeof(RoomDepletion),
            typeof(RoomInventoryLoss), typeof(RoomTransfer), typeof(BinsRunEntry), typeof(TreatmentLineageSegment),
            typeof(RoomTreatmentApplicationSource), typeof(OutsideWarehouseTransfer), typeof(InterCrewTransfer),
            typeof(ProcessorShipmentLine), typeof(RunProjectionSource), typeof(ActualRunOverrideRequestLine) })
            yield return [type, "FruitProfileId"];
        yield return [typeof(BinsRunEntry), "ReportingFruitProfileIdSnapshot"];
        yield return [typeof(QcSample), "FieldSampleFruitProfileId"];
        yield return [typeof(InventoryIdentityCorrection), "SourceFruitProfileId"];
        yield return [typeof(InventoryIdentityCorrection), "TargetFruitProfileId"];
    }

    [Theory]
    [MemberData(nameof(OperationalReferences))]
    public async Task EveryLiveIdentityReference_IsProtectedWithoutCurrentInventory(Type type, string property)
    {
        await using var db = CreateDb();
        SeedReference(db, type, property, 2);
        await db.SaveChangesAsync();
        var service = new AdminManagementService(db, new VarietyColorService(db));
        var form = (await service.GetEditFormAsync("fruit-profiles", 2, default))!;
        form.Code = "NEW-GALA";
        Assert.Contains("operational history", await service.SaveMasterDataAsync(form, "admin@example.test", default));
        Assert.Empty(await db.AuditLogs.ToListAsync());
        Assert.Equal("GALA", (await db.FruitProfiles.FindAsync(2))!.VarietyCode);
    }

    [Fact]
    public async Task PresentationConfigurationAndImmutableExpectationSnapshot_AreNotOperationalUse()
    {
        await using var db = CreateDb();
        SeedReference(db, typeof(VarietyColorConfiguration), "FruitProfileId", 2);
        SeedReference(db, typeof(StarchScale), "FruitProfileId", 2);
        SeedReference(db, typeof(CommercialPackFruitProfileRestriction), "FruitProfileId", 2);
        // RunExpectationSource is a fully captured calculation, not a live profile lookup.
        SeedReference(db, typeof(RunExpectationSource), "FruitProfileId", 2);
        await db.SaveChangesAsync();
        var service = new AdminManagementService(db, new VarietyColorService(db));
        var form = (await service.GetEditFormAsync("fruit-profiles", 2, default))!;
        form.Code = "NEW-GALA";
        Assert.Null(await service.SaveMasterDataAsync(form, "admin@example.test", default));
    }

    [Fact]
    public async Task StaleTrackedProfileAndNewReference_CannotBypassSaveTimeGuard()
    {
        await using var db = CreateDb();
        var profile = await db.FruitProfiles.FindAsync(2);
        var service = new AdminManagementService(db, new VarietyColorService(db));
        var staleForm = (await service.GetEditFormAsync("fruit-profiles", 2, default))!;
        await using (var other = new CropQcDbContext((DbContextOptions<CropQcDbContext>)db.GetService<IDbContextOptions>()))
        {
            var updated = await other.FruitProfiles.FindAsync(2);
            updated!.ProductionType = "Organic";
            updated.IsOrganic = true;
            SeedReference(other, typeof(Receipt), "FruitProfileId", 2);
            await other.SaveChangesAsync();
        }
        Assert.Contains("operational history", await service.SaveMasterDataAsync(staleForm, "admin@example.test", default));
        Assert.True(profile!.IsOrganic);
        Assert.Equal("Organic", profile.ProductionType);
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    [Fact]
    public async Task CosmeticEdit_DoesNotCleanLegacyOrganicCombination()
    {
        await using var db = CreateDb();
        var profile = await SeedInventory(db);
        profile.IsOrganic = true;
        await db.SaveChangesAsync();
        var service = new AdminManagementService(db, new VarietyColorService(db));
        var form = (await service.GetEditFormAsync("fruit-profiles", profile.Id, default))!;
        form.Description = "Presentation only";
        Assert.Null(await service.SaveMasterDataAsync(form, "admin@example.test", default));
        Assert.True(profile.IsOrganic);
        Assert.Equal("Conventional", profile.ProductionType);
    }

    [Fact]
    public async Task UsedOrganicProfile_CannotBecomeConventional()
    {
        await using var db = CreateDb();
        SeedReference(db, typeof(Receipt), "FruitProfileId", 7);
        await db.SaveChangesAsync();
        var service = new AdminManagementService(db, new VarietyColorService(db));
        var form = (await service.GetEditFormAsync("fruit-profiles", 7, default))!;
        form.ProductionType = "Conventional";
        Assert.Contains("operational history", await service.SaveMasterDataAsync(form, "admin@example.test", default));
        Assert.True((await db.FruitProfiles.FindAsync(7))!.IsOrganic);
        Assert.Empty(await db.AuditLogs.ToListAsync());
    }

    private static void SeedReference(CropQcDbContext db, Type type, string property, int profileId)
    {
        var entity = Activator.CreateInstance(type)!;
        foreach (var p in type.GetProperties().Where(x => x.PropertyType == typeof(string) && x.CanWrite))
            if (p.GetValue(entity) is null) p.SetValue(entity, "test");
        type.GetProperty(property)!.SetValue(entity, profileId);
        if (entity is Receipt receipt) { receipt.IsDeleted = true; receipt.IsTestData = true; }
        if (entity is CommercialPackFruitProfileRestriction restriction) restriction.CommercialPackDefinitionId = 90001;
        db.Add(entity);
    }

    private static string OperationalState(CropQcDbContext db) => JsonSerializer.Serialize(db.ChangeTracker.Entries()
        .Where(x => x.Entity is Receipt or RoomInventoryAdjustment or BinsRunEntry or TreatmentLineageSegment)
        .OrderBy(x => x.Metadata.Name).Select(x => x.Properties.ToDictionary(p => p.Metadata.Name, p => p.CurrentValue)));

    private static CropQcDbContext CreateDb()
    {
        var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Database.EnsureCreated();
        return db;
    }

    private static async Task<FruitProfile> SeedInventory(CropQcDbContext db)
    {
        var profile = new FruitProfile { Id = 90001, VarietyCode = "TEST-GALA", Name = "Gala", FruitType = "Apple", ProductionType = "Conventional" };
        db.FruitProfiles.Add(profile);
        db.Warehouses.Add(new Warehouse { Id = 90001, Code = "TEST", Name = "Test" });
        db.Rooms.Add(new Room { Id = 90001, WarehouseId = 90001, Code = "TEST", Name = "Test" });
        db.RoomInventoryAdjustments.Add(new RoomInventoryAdjustment
        {
            Id = 90001,
            WarehouseId = 90001,
            RoomId = 90001,
            FruitProfileId = profile.Id,
            CropYear = 2026,
            GrowerName = "Test grower",
            LotNumber = "90001",
            VarietyCode = profile.VarietyCode,
            ChangeAmount = 10,
            NewBinCount = 10,
            AdjustmentType = "ReceiptAdd",
            AdjustmentAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return profile;
    }
}
