using System.Reflection;
using System.Security.Claims;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Controllers;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CropQc.Api.Tests;

public sealed class BinsRunWorkflowTests
{
    [Fact]
    public void BinsRun_IsTopLevelPermissionedNavigation()
    {
        var layout = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_Layout.cshtml"));
        var access = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "UserAccessService.cs"));
        var program = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Program.cs"));
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "BinsRun", "Index.cshtml"));
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "BinsRunService.cs"));

        Assert.Contains("ApplicationAreas.BinsRun", access);
        Assert.Contains("AccessPolicyNames.BinsRunView", program);
        Assert.Contains("AccessPolicyNames.BinsRunEdit", program);
        Assert.Contains("AccessPolicyNames.BinsRunAdmin", program);
        Assert.Contains("canAccessBinsRun", layout);
        Assert.Contains("<a href=\"/BinsRun\">Bins Run</a>", layout);
        Assert.Contains("Select Room", view);
        Assert.Contains("Size Distribution", view);
        Assert.Contains("Expected Grade", view);
        Assert.Contains("Lot Inventory", view);
        Assert.Contains("No sizing data is available for the current inventory in this room.", view);
        Assert.Contains("No grade information is available for the current inventory in this room.", view);
        Assert.Contains("32, 36, 40, 48, 56, 64, 72, 80, 88, 100, 113, 125, 138, 150, 163, 175, 198, 216", service);
        AssertActionPolicy<BinsRunController>(nameof(BinsRunController.Index), AccessPolicyNames.BinsRunView);
        AssertActionPolicy<BinsRunController>(nameof(BinsRunController.Create), AccessPolicyNames.BinsRunEdit);
        AssertActionPolicy<BinsRunController>(nameof(BinsRunController.Edit), AccessPolicyNames.BinsRunEdit);
        AssertActionPolicy<BinsRunController>(nameof(BinsRunController.Reverse), AccessPolicyNames.BinsRunAdmin);
    }

    [Fact]
    public async Task ViewOnlyUser_CanReviewButCannotCreate()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var viewOnly = Principal("viewer@fruitandland.com");

        var page = await service.GetPageAsync(new BinsRunFilterForm(), viewOnly, CancellationToken.None);
        var error = await service.CreateAsync(new BinsRunForm
        {
            InventoryKey = page.AvailableInventory[0].InventoryKey,
            BinsRun = 5,
            ExpectedAvailableBins = page.AvailableInventory[0].CurrentBins,
            RunAt = DateTimeOffset.UtcNow
        }, viewOnly, CancellationToken.None);

        Assert.False(page.CanRecord);
        Assert.NotNull((await service.GetPageAsync(new BinsRunFilterForm { RoomId = 1001 }, viewOnly, CancellationToken.None)).RoomSummary);
        Assert.Equal("Bins Run Edit access is required to record bins run.", error);
        Assert.Empty(await db.BinsRunEntries.ToListAsync());
    }

    [Fact]
    public async Task RoomSummaryAndLotSubmenu_UseOnlyCurrentAvailableInventoryForSelectedRoom()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var page = await CreateService(db).GetPageAsync(new BinsRunFilterForm { RoomId = 1001 }, Principal("manager@fruitandland.com"), CancellationToken.None);

        Assert.NotNull(page.RoomSummary);
        Assert.Equal("Evans-12", page.RoomSummary!.RoomName);
        Assert.Equal("EBS", page.RoomSummary.Facility);
        Assert.Equal(190, page.RoomSummary.TotalAvailableBins);
        Assert.Equal(3, page.RoomSummary.ActiveLotCount);
        Assert.All(page.AvailableInventory, x => Assert.Equal(1001, x.RoomId));
        Assert.Contains(page.AvailableInventory, x => x.Lot == "LOT-120" && x.CurrentBins == 120);
        Assert.Contains(page.AvailableInventory, x => x.Lot == "LOT-30" && x.CurrentBins == 30);
        Assert.Contains(page.AvailableInventory, x => x.Lot == "HISTORY" && x.CurrentBins == 40);
        Assert.DoesNotContain(page.AvailableInventory, x => x.Lot == "LOT-ZERO");
        Assert.DoesNotContain(page.AvailableInventory, x => x.RoomId == 1002);
    }

    [Fact]
    public async Task RoomSummary_WeightsSizingAndGradeByCurrentAvailableBins()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var summary = (await CreateService(db).GetPageAsync(new BinsRunFilterForm { RoomId = 1001 }, Principal("manager@fruitandland.com"), CancellationToken.None)).RoomSummary!;

        Assert.Equal(32, summary.SizeDistribution.First().Size);
        Assert.Equal(216, summary.SizeDistribution.Last().Size);
        Assert.Equal(60m, summary.SizeDistribution.Single(x => x.Size == 80).EstimatedBins);
        Assert.Equal(90m, summary.SizeDistribution.Single(x => x.Size == 100).EstimatedBins);
        Assert.Equal(60m, summary.GradeSummary.Single(x => x.Grade == "W1").EstimatedBins);
        Assert.Equal(90m, summary.GradeSummary.Single(x => x.Grade == "W2").EstimatedBins);
        Assert.Equal(2, summary.SizeDataLotCount);
        Assert.Equal(2, summary.GradeDataLotCount);
    }

    [Fact]
    public async Task MissingSizingAndGradeData_ProduceEmptySummaryStates()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        db.QcFruitReadings.RemoveRange(db.QcFruitReadings);
        await db.SaveChangesAsync();

        var summary = (await CreateService(db).GetPageAsync(new BinsRunFilterForm { RoomId = 1001 }, Principal("viewer@fruitandland.com"), CancellationToken.None)).RoomSummary!;

        Assert.Empty(summary.SizeDistribution);
        Assert.Empty(summary.GradeSummary);
        Assert.Equal(0, summary.SizeDataLotCount);
        Assert.Equal(0, summary.GradeDataLotCount);
    }

    [Fact]
    public async Task CreatingBinsRun_ReducesAvailableQuantityAndAuditsWithoutChangingHistory()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var user = Principal("manager@fruitandland.com");
        var option = (await service.GetPageAsync(new BinsRunFilterForm { RoomId = 1001 }, user, CancellationToken.None))
            .AvailableInventory.Single(x => x.Lot == "LOT-120");

        var error = await service.CreateAsync(new BinsRunForm
        {
            WarehouseId = option.WarehouseId,
            RoomId = option.RoomId,
            InventoryKey = option.InventoryKey,
            BinsRun = 30,
            ExpectedAvailableBins = option.CurrentBins,
            RunAt = DateTimeOffset.Parse("2026-07-10T08:00:00-07:00"),
            Notes = "Packing line run"
        }, user, CancellationToken.None);

        Assert.Null(error);
        var refreshed = await service.GetPageAsync(new BinsRunFilterForm { RoomId = 1001 }, user, CancellationToken.None);
        Assert.Equal(90, refreshed.AvailableInventory.Single(x => x.Lot == "LOT-120").CurrentBins);
        Assert.Equal(160, refreshed.RoomSummary!.TotalAvailableBins);
        var entry = Assert.Single(await db.BinsRunEntries.ToListAsync());
        Assert.Equal(120, entry.PreviousAvailableBins);
        Assert.Equal(30, entry.BinsRun);
        Assert.Equal(90, entry.NewAvailableBins);
        Assert.Contains(await db.AuditLogs.ToListAsync(), x => x.Action == "Create" && x.EntityName == nameof(BinsRunEntry));
        Assert.Equal(40, (await db.Receipts.SingleAsync(x => x.Id == 7001)).BinCount);
        Assert.Equal(1, await db.QcSamples.CountAsync(x => x.ReceiptId == 7001));
    }

    [Fact]
    public async Task BinsRun_CannotExceedAvailableOrUseStaleExpectedQuantity()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var user = Principal("manager@fruitandland.com");
        var option = (await service.GetPageAsync(new BinsRunFilterForm { RoomId = 1001 }, user, CancellationToken.None))
            .AvailableInventory.Single(x => x.Lot == "LOT-120");

        var tooMany = await service.CreateAsync(new BinsRunForm
        {
            InventoryKey = option.InventoryKey,
            BinsRun = 121,
            ExpectedAvailableBins = 120,
            RunAt = DateTimeOffset.UtcNow
        }, user, CancellationToken.None);
        var first = await service.CreateAsync(new BinsRunForm
        {
            InventoryKey = option.InventoryKey,
            BinsRun = 5,
            ExpectedAvailableBins = 120,
            RunAt = DateTimeOffset.UtcNow
        }, user, CancellationToken.None);
        var stale = await service.CreateAsync(new BinsRunForm
        {
            InventoryKey = option.InventoryKey,
            BinsRun = 5,
            ExpectedAvailableBins = 120,
            RunAt = DateTimeOffset.UtcNow
        }, user, CancellationToken.None);

        Assert.Contains("only 120 bins", tooMany);
        Assert.Null(first);
        Assert.Contains("Available quantity changed before save", stale);
        Assert.DoesNotContain(await db.RoomInventoryAdjustments.ToListAsync(), x => x.NewBinCount < 0);
    }

    [Fact]
    public async Task EditingAndReversing_AdjustsAndRestoresInventory()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var user = Principal("manager@fruitandland.com");
        var admin = Principal("admin@fruitandland.com");
        var option = (await service.GetPageAsync(new BinsRunFilterForm { RoomId = 1001 }, user, CancellationToken.None))
            .AvailableInventory.Single(x => x.Lot == "LOT-120");
        await service.CreateAsync(new BinsRunForm
        {
            InventoryKey = option.InventoryKey,
            BinsRun = 30,
            ExpectedAvailableBins = 120,
            RunAt = DateTimeOffset.UtcNow
        }, user, CancellationToken.None);
        var entry = await db.BinsRunEntries.SingleAsync();
        var currentOption = (await service.GetPageAsync(new BinsRunFilterForm { RoomId = 1001 }, user, CancellationToken.None))
            .AvailableInventory.Single(x => x.Lot == "LOT-120");

        var editError = await service.UpdateAsync(entry.Id, new BinsRunForm
        {
            InventoryKey = currentOption.InventoryKey,
            BinsRun = 45,
            RunAt = DateTimeOffset.UtcNow
        }, user, CancellationToken.None);
        var afterEdit = await service.GetPageAsync(new BinsRunFilterForm { RoomId = 1001 }, user, CancellationToken.None);
        var reverseError = await service.ReverseAsync(new ReverseBinsRunForm { Id = entry.Id, Reason = "Correction" }, admin, CancellationToken.None);
        var afterReverse = await service.GetPageAsync(new BinsRunFilterForm { RoomId = 1001 }, user, CancellationToken.None);

        Assert.Null(editError);
        Assert.Equal(75, afterEdit.AvailableInventory.Single(x => x.Lot == "LOT-120").CurrentBins);
        Assert.Equal(145, afterEdit.RoomSummary!.TotalAvailableBins);
        Assert.Null(reverseError);
        Assert.Equal(120, afterReverse.AvailableInventory.Single(x => x.Lot == "LOT-120").CurrentBins);
        Assert.Equal(190, afterReverse.RoomSummary!.TotalAvailableBins);
        Assert.Contains(await db.AuditLogs.ToListAsync(), x => x.Action == "Update" && x.EntityName == nameof(BinsRunEntry));
        Assert.Contains(await db.AuditLogs.ToListAsync(), x => x.Action == "Reverse" && x.EntityName == nameof(BinsRunEntry));
        Assert.True((await db.BinsRunEntries.SingleAsync()).IsReversed);
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

    private static async Task SeedInventoryAsync(CropQcDbContext db)
    {
        var warehouse = new Warehouse { Id = 1000, Code = "EBS", Name = "EBS", IsActive = true };
        var room = new Room { Id = 1001, WarehouseId = warehouse.Id, Warehouse = warehouse, Code = "EVANCA12", Name = "Evans 12", CropQcRoomName = "Evans-12", IsActive = true };
        var otherRoom = new Room { Id = 1002, WarehouseId = warehouse.Id, Warehouse = warehouse, Code = "LAMBCA17", Name = "Lamb 17", CropQcRoomName = "Lamb-17", IsActive = true };
        var fruit = new FruitProfile { Id = 1000, Name = "Fuji", VarietyCode = "FUJI", FruitType = "Apple", ProductionType = "Conventional", IsActive = true };
        var sampleType = new SampleType { Id = 1000, Name = "Receiving Sample", IsActive = true };
        var doorSampleType = new SampleType { Id = 1001, Name = "Door Sample", IsActive = true };
        var grade1 = new Grade { Id = 1000, Code = "W1", Name = "W1", IsActive = true };
        var grade2 = new Grade { Id = 1001, Code = "W2", Name = "W2", IsActive = true };
        db.Warehouses.Add(warehouse);
        db.Rooms.AddRange(room, otherRoom);
        db.FruitProfiles.Add(fruit);
        db.SampleTypes.AddRange(sampleType, doorSampleType);
        db.Grades.AddRange(grade1, grade2);
        db.Users.AddRange(
            User(1000, "admin@fruitandland.com", PageAccessLevel.Admin),
            User(1001, "manager@fruitandland.com", PageAccessLevel.Edit),
            User(1002, "viewer@fruitandland.com", PageAccessLevel.View));
        db.RoomInventoryAdjustments.AddRange(
            Adjustment(8001, warehouse, room, fruit, "LOT-120", 120),
            Adjustment(8004, warehouse, room, fruit, "LOT-30", 30),
            Adjustment(8002, warehouse, room, fruit, "LOT-ZERO", 0),
            Adjustment(8003, warehouse, otherRoom, fruit, "LOT-OTHER", 60));
        db.Receipts.AddRange(new Receipt
        {
            Id = 7001,
            CropYear = 2026,
            ReceivedAt = DateTimeOffset.UtcNow,
            CompuTechReceiptId = "TRUCK-HISTORY",
            ReceiptType = "Truck receipt",
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            RoomId = room.Id,
            Room = room,
            FruitProfileId = fruit.Id,
            FruitProfile = fruit,
            GrowerName = "History Grower",
            LotCode = "HISTORY",
            BinCount = 40,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        },
        SampleReceipt(7002, "QC-LOT-120", "LOT-120", warehouse, room, fruit),
        SampleReceipt(7003, "QC-LOT-30", "LOT-30", warehouse, room, fruit));
        db.QcSamples.Add(new QcSample
        {
            Id = 7101,
            ReceiptId = 7001,
            SampleTypeId = sampleType.Id,
            SampleType = sampleType,
            Status = "Complete",
            StarchStatus = "Complete",
            PhotoStatus = "Complete",
            EmailStatus = "Not Sent",
            SampleTakenAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.QcSamples.AddRange(
            Sample(7102, 7002, doorSampleType, DateTimeOffset.Parse("2026-07-09T08:00:00-07:00")),
            Sample(7103, 7003, doorSampleType, DateTimeOffset.Parse("2026-07-09T09:00:00-07:00")));
        db.QcFruitReadings.AddRange(
            FruitReading(7201, 7102, 1, 80, grade1),
            FruitReading(7202, 7102, 2, 100, grade2),
            FruitReading(7203, 7103, 1, 100, grade2),
            FruitReading(7204, 7103, 2, 100, grade2));
        await db.SaveChangesAsync();
    }

    private static Receipt SampleReceipt(long id, string receiptId, string lot, Warehouse warehouse, Room room, FruitProfile fruit) => new()
    {
        Id = id,
        CropYear = 2026,
        ReceivedAt = DateTimeOffset.Parse("2026-07-09T07:00:00-07:00"),
        CompuTechReceiptId = receiptId,
        ReceiptType = "Door sample",
        WarehouseId = warehouse.Id,
        Warehouse = warehouse,
        RoomId = room.Id,
        Room = room,
        FruitProfileId = fruit.Id,
        FruitProfile = fruit,
        GrowerName = "QC Grower",
        GrowerNumber = lot,
        LotCode = lot,
        BinCount = 999,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static User User(int id, string email, PageAccessLevel binsRunLevel) => new()
    {
        Id = id,
        Email = email,
        DisplayName = email,
        Domain = "fruitandland.com",
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
        PageAccesses =
        {
            new UserPageAccess { AreaKey = ApplicationAreas.BinsRun, AccessLevel = binsRunLevel.ToString(), UpdatedAt = DateTimeOffset.UtcNow }
        }
    };

    private static RoomInventoryAdjustment Adjustment(long id, Warehouse warehouse, Room room, FruitProfile fruit, string lot, int bins) => new()
    {
        Id = id,
        CropYear = 2026,
        WarehouseId = warehouse.Id,
        Warehouse = warehouse,
        RoomId = room.Id,
        Room = room,
        FruitProfileId = fruit.Id,
        FruitProfile = fruit,
        GrowerName = "Wes Verified Current Inventory",
        LotNumber = lot,
        VarietyCode = fruit.VarietyCode,
        OldBinCount = null,
        ChangeAmount = bins,
        NewBinCount = bins,
        AdjustmentType = RoomInventoryImportService.StartingInventoryAdjustmentType,
        Source = "Current Inventory Baseline",
        Reason = "Test seed",
        AdjustmentAt = DateTimeOffset.Parse("2026-06-18T00:00:00-07:00"),
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static QcSample Sample(long id, long receiptId, SampleType sampleType, DateTimeOffset sampleTakenAt) => new()
    {
        Id = id,
        ReceiptId = receiptId,
        SampleTypeId = sampleType.Id,
        SampleType = sampleType,
        Status = "Complete",
        StarchStatus = "Complete",
        PhotoStatus = "Complete",
        EmailStatus = "Not Sent",
        SampleTakenAt = sampleTakenAt,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static QcFruitReading FruitReading(long id, long sampleId, int row, int size, Grade grade) => new()
    {
        Id = id,
        QcSampleId = sampleId,
        RowNumber = row,
        GradeId = grade.Id,
        Grade = grade,
        SizeCategory = size,
        SizeStatus = "Sized",
        IsCompleted = true,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static BinsRunService CreateService(CropQcDbContext db) =>
        new(db, new UserAccessService(db, new ConfigurationBuilder().Build()));

    private static ClaimsPrincipal Principal(string email) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Email, email)], "TestAuth"));

    private static void AssertActionPolicy<TController>(string actionName, string policy)
    {
        var method = typeof(TController).GetMethod(actionName);
        Assert.NotNull(method);
        var attributes = method!.GetCustomAttributes<AuthorizeAttribute>().ToList();
        Assert.Contains(attributes, x => x.Policy == policy);
    }

    private static string FindRepositoryFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file {Path.Combine(pathParts)}.");
    }
}
