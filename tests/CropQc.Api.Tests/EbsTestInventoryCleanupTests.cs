using System.Security.Claims;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CropQc.Api.Tests;

public sealed class EbsTestInventoryCleanupTests
{
    [Theory]
    [InlineData("EVANS-7", "Evans Street 7", null, null, null)]
    [InlineData("OTHER", "Other", "Evans 7", null, null)]
    [InlineData("OTHER", "Other", null, "EVANCA07", null)]
    public void Evans7Identity_IsResolvedFromPersistedRoomIdentity_NotFruit(
        string code,
        string name,
        string? cropQcRoomName,
        string? compuTechRoomCode,
        string? displayName)
    {
        var room = new EbsInventoryCleanupService.ProtectedRoomIdentity(
            7,
            code,
            name,
            cropQcRoomName,
            compuTechRoomCode,
            displayName);

        Assert.True(EbsInventoryCleanupService.IsEvans7Room(room));
    }

    [Fact]
    public async Task Review_SelectsEveryNonEvans7Balance_AndNeverSelectsEvans7()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var service = CreateService(db);

        var page = await service.GetReviewAsync(1, 100, Principal(), CancellationToken.None);

        Assert.Equal(7, page.ProtectedRoomId);
        Assert.Equal(3, page.TotalRows);
        Assert.DoesNotContain(page.Rows, x => x.RoomId == 7);
        Assert.Contains(page.Rows, x => x.Variety.Contains("Pink Lady", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(page.Rows, x => x.Variety.Contains("Red Delicious", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(page.Rows, x => x.Variety.Contains("Granny Smith", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(95, page.CandidateCurrentBins);
    }

    [Fact]
    public async Task CleanupSelection_LeavesEvans7ReceiptsLotsAndLedgerUnchanged_AndOnlyEvans7InventoryRemains()
    {
        await using var db = CreateDb();
        await SeedAsync(db);
        var service = CreateService(db);
        var protectedBefore = await ProtectedRowsJsonAsync(db);

        var page = await service.GetReviewAsync(1, 100, Principal(), CancellationToken.None);
        var selectedAdjustmentIds = page.Rows.Select(x => x.InventorySnapshotId).ToList();
        db.RoomInventoryAdjustments.RemoveRange(
            db.RoomInventoryAdjustments.Where(x => selectedAdjustmentIds.Contains(x.Id)));
        await db.SaveChangesAsync();

        var protectedAfter = await ProtectedRowsJsonAsync(db);
        var current = await new RoomInventoryLedgerQueryService(db)
            .GetSnapshotsAsync(1, null, CancellationToken.None);
        Assert.Equal(protectedBefore, protectedAfter);
        Assert.NotEmpty(current);
        Assert.All(current, x => Assert.Equal(7, x.RoomId));
        Assert.All(current, x => Assert.Contains("Gala", x.VarietyName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OperationalPackage_IsExplicitFailClosedAndDoesNotCreateHistoricalOperations()
    {
        var preflight = Read("scripts", "postgresql", "preflight-ebs-test-inventory-cleanup.sql");
        var apply = Read("scripts", "postgresql", "apply-ebs-test-inventory-cleanup.sql");
        var verify = Read("scripts", "postgresql", "verify-ebs-test-inventory-cleanup.sql");

        Assert.Contains("BEGIN TRANSACTION READ ONLY", preflight, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("REMOVE_NON_EVANS7_EBS_TEST_INVENTORY", apply);
        Assert.Contains("operator_email", apply);
        Assert.Contains("evans7_before", apply);
        Assert.Contains("evans7_after", apply);
        Assert.Contains("wp_ledger_before", apply);
        Assert.Contains("wp_ledger_after", apply);
        Assert.Contains("DELETE FROM \"RoomInventoryAdjustments\"", apply);
        Assert.DoesNotContain("INSERT INTO \"BinsRunEntries\"", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO \"RoomTransfers\"", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("INSERT INTO \"ActualRuns\"", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE \"RoomInventoryAdjustments\"", apply, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cleanup_complete", verify);
        Assert.Contains("non_evans7_ebs_balance", verify);
    }

    private static CropQcDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new CropQcDbContext(options);
    }

    private static EbsInventoryCleanupService CreateService(CropQcDbContext db) =>
        new(
            db,
            new RoomInventoryLedgerQueryService(db),
            new UserAccessService(db, new ConfigurationBuilder().Build()));

    private static async Task SeedAsync(CropQcDbContext db)
    {
        var ebs = new Warehouse { Code = "EBS", Name = "EBS", IsActive = true };
        var wp = new Warehouse { Code = "WP", Name = "Windy Point", IsActive = true };
        var evans7 = new Room
        {
            Id = 7,
            Warehouse = ebs,
            Code = "EVANS-7",
            Name = "Evans Street 7",
            CropQcRoomName = "Evans-7",
            IsActive = true
        };
        var lamb = new Room { Id = 8, Warehouse = ebs, Code = "LAMB-17", Name = "Lamb 17", IsActive = true };
        var blue = new Room { Id = 9, Warehouse = ebs, Code = "BM-1", Name = "Blue Mountain 1", IsActive = true };
        var blueTwo = new Room { Id = 11, Warehouse = ebs, Code = "BM-2", Name = "Blue Mountain 2", IsActive = true };
        var wpRoom = new Room { Id = 10, Warehouse = wp, Code = "WP-1", Name = "WP 1", IsActive = true };
        var gala = Fruit("GALA", "Gala");
        var pink = Fruit("PINK", "Pink Lady");
        var red = Fruit("RED", "Red Delicious");
        var granny = Fruit("GSMT", "Granny Smith");
        var lot = new GrowerLot
        {
            Grower = "Evans 7 Grower",
            LotNumber = "GALA-7",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.AddRange(ebs, wp, evans7, lamb, blue, blueTwo, wpRoom, gala, pink, red, granny, lot);
        db.Receipts.Add(new Receipt
        {
            CropYear = 2026,
            ReceivedAt = DateTimeOffset.UtcNow,
            CompuTechReceiptId = "EVANS7-GALA",
            Warehouse = ebs,
            Room = evans7,
            FruitProfile = gala,
            GrowerLot = lot,
            GrowerName = lot.Grower,
            LotCode = lot.LotNumber,
            BinCount = 120,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.RoomInventoryAdjustments.AddRange(
            Adjustment(ebs, evans7, gala, "GALA-7", 120),
            Adjustment(ebs, lamb, pink, "PINK-TEST", 30),
            Adjustment(ebs, blue, red, "RED-TEST", 40),
            Adjustment(ebs, blueTwo, granny, "GRANNY-TEST", 25),
            Adjustment(wp, wpRoom, gala, "WP-KEEP", 50));
        await db.SaveChangesAsync();
    }

    private static FruitProfile Fruit(string code, string name) => new()
    {
        VarietyCode = code,
        Name = name,
        FruitType = "Apple",
        ProductionType = "Conventional",
        IsActive = true
    };

    private static RoomInventoryAdjustment Adjustment(
        Warehouse warehouse,
        Room room,
        FruitProfile fruit,
        string lot,
        int bins) => new()
        {
            CropYear = 2026,
            Warehouse = warehouse,
            Room = room,
            FruitProfile = fruit,
            GrowerName = "Test Grower",
            LotNumber = lot,
            VarietyCode = fruit.VarietyCode,
            ChangeAmount = bins,
            NewBinCount = bins,
            AdjustmentType = RoomInventoryImportService.StartingInventoryAdjustmentType,
            Source = "Test baseline",
            AdjustmentAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };

    private static async Task<string> ProtectedRowsJsonAsync(CropQcDbContext db)
    {
        var receipt = await db.Receipts.AsNoTracking().SingleAsync(x => x.RoomId == 7);
        var lot = await db.GrowerLots.AsNoTracking().SingleAsync(x => x.Id == receipt.GrowerLotId);
        var adjustment = await db.RoomInventoryAdjustments.AsNoTracking().SingleAsync(x => x.RoomId == 7);
        return JsonSerializer.Serialize(new
        {
            Receipt = new
            {
                receipt.Id,
                receipt.CropYear,
                receipt.CompuTechReceiptId,
                receipt.RoomId,
                receipt.FruitProfileId,
                receipt.GrowerLotId,
                receipt.GrowerName,
                receipt.LotCode,
                receipt.BinCount,
                receipt.IsTestData,
                receipt.IsDeleted
            },
            Lot = new { lot.Id, lot.Grower, lot.LotNumber, lot.PoolStart, lot.Notes, lot.IsActive },
            Adjustment = new
            {
                adjustment.Id,
                adjustment.WarehouseId,
                adjustment.RoomId,
                adjustment.GrowerLotId,
                adjustment.FruitProfileId,
                adjustment.LotNumber,
                adjustment.VarietyCode,
                adjustment.ChangeAmount,
                adjustment.NewBinCount,
                adjustment.AdjustmentType
            }
        });
    }

    private static ClaimsPrincipal Principal() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Email, ApplicationAreas.OwnerEmail)], "Test"));

    private static string Read(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(Path.Combine(parts));
    }
}
