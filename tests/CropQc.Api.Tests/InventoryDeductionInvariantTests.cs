using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class InventoryDeductionInvariantTests
{
    [Fact]
    public async Task NewNegativeAdjustmentWithoutParent_IsRejected()
    {
        using var db = CreateDb();
        db.RoomInventoryAdjustments.Add(Adjustment(-10));
        var service = Service(db);

        var exception = await Assert.ThrowsAsync<InventoryDeductionInvariantException>(
            () => service.ValidateBeforeCommitAsync(CancellationToken.None));

        Assert.Contains("required Bins Run or Transfer relationship", exception.Message);
    }

    [Fact]
    public async Task MatchingBinsRunAndAdjustment_AreAcceptedExactlyOnce()
    {
        using var db = CreateDb();
        var adjustment = Adjustment(-10);
        var entry = Entry(adjustment, 10);
        db.RoomInventoryAdjustments.Add(adjustment);
        db.BinsRunEntries.Add(entry);

        await Service(db).ValidateBeforeCommitAsync(CancellationToken.None);

        Assert.Same(adjustment, entry.InventoryAdjustment);
        Assert.Equal(-10, adjustment.ChangeAmount);
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("facility")]
    [InlineData("room")]
    [InlineData("crop")]
    [InlineData("lot")]
    [InlineData("profile")]
    [InlineData("variety")]
    [InlineData("organic")]
    public async Task BinsRunIdentityMismatch_IsRejected(string mismatch)
    {
        using var db = CreateDb();
        var adjustment = Adjustment(-10);
        var entry = Entry(adjustment, 10);
        if (mismatch == "amount") entry.BinsRun = 9;
        if (mismatch == "facility") entry.WarehouseId = 2;
        if (mismatch == "room") entry.RoomId = 3;
        if (mismatch == "crop") entry.CropYear = 2025;
        if (mismatch == "lot") entry.LotNumber = "OTHER";
        if (mismatch == "profile") entry.FruitProfileId = 99;
        if (mismatch == "variety") entry.VarietyCode = "GALA";
        if (mismatch == "organic") entry.InventoryStatus = "Conventional";
        db.RoomInventoryAdjustments.Add(adjustment);
        db.BinsRunEntries.Add(entry);

        await Assert.ThrowsAsync<InventoryDeductionInvariantException>(
            () => Service(db).ValidateBeforeCommitAsync(CancellationToken.None));
    }

    [Fact]
    public async Task TransferRequiresMatchingPersistedPair()
    {
        using var db = CreateDb();
        var transfer = Transfer();
        var outgoing = Adjustment(-12);
        outgoing.AdjustmentType = "TransferOut";
        outgoing.RoomTransfer = transfer;
        outgoing.RoomTransferId = transfer.Id;
        outgoing.InventoryOperationKey = "transfer:test:out";
        var incoming = Adjustment(12);
        incoming.AdjustmentType = "TransferIn";
        incoming.WarehouseId = 1;
        incoming.RoomId = 2;
        incoming.OldBinCount = 0;
        incoming.NewBinCount = 12;
        incoming.RoomTransfer = transfer;
        incoming.RoomTransferId = transfer.Id;
        incoming.InventoryOperationKey = "transfer:test:in";
        transfer.InventoryAdjustments.Add(outgoing);
        transfer.InventoryAdjustments.Add(incoming);
        db.RoomTransfers.Add(transfer);
        db.RoomInventoryAdjustments.AddRange(outgoing, incoming);

        await Service(db).ValidateBeforeCommitAsync(CancellationToken.None);
    }

    [Fact]
    public async Task PartialTransfer_IsRejectedBeforeCommit()
    {
        using var db = CreateDb();
        var transfer = Transfer();
        var outgoing = Adjustment(-12);
        outgoing.AdjustmentType = "TransferOut";
        outgoing.RoomTransfer = transfer;
        outgoing.RoomTransferId = transfer.Id;
        transfer.InventoryAdjustments.Add(outgoing);
        db.RoomTransfers.Add(transfer);
        db.RoomInventoryAdjustments.Add(outgoing);

        await Assert.ThrowsAsync<InventoryDeductionInvariantException>(
            () => Service(db).ValidateBeforeCommitAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Readiness_CountsHistoricalAndNewOrphansAndFailsClosed()
    {
        using var db = CreateDb();
        var historical = Adjustment(-4);
        historical.InventoryInvariantVersion = 0;
        historical.InventoryOperationKey = null;
        var current = Adjustment(-5);
        current.InventoryOperationKey = "orphan:new";
        db.RoomInventoryAdjustments.AddRange(historical, current);
        await db.SaveChangesAsync();

        var result = await Service(db).VerifyReadinessAsync(CancellationToken.None);

        Assert.Equal(2, result.NegativeAdjustmentCount);
        Assert.Equal(1, result.HistoricalNegativeCount);
        Assert.Equal(1, result.NewFormatNegativeCount);
        Assert.Contains(result.Issues, x => x.BlocksDeployment && x.AdjustmentId == historical.Id);
        Assert.Contains(result.Issues, x => x.BlocksDeployment && x.AdjustmentId == current.Id);
        Assert.False(result.IsReady);
    }

    [Fact]
    public void MigrationAndProductionScripts_AreProviderCompatibleAndNonDestructive()
    {
        var migration = ReadRepositoryFile(
            "src", "CropQc.Data", "Migrations", "20260730150926_EnforceRoomInventoryDeductionParents.cs");
        var apply = ReadRepositoryFile(
            "scripts", "postgresql", "apply-room-inventory-deduction-parents-schema.sql");
        var preflight = ReadRepositoryFile(
            "scripts", "postgresql", "preflight-room-inventory-deduction-parents.sql");

        Assert.Contains("MigrationProviderTypes.StoreType", migration);
        Assert.Contains("NpgsqlValueGenerationStrategy.IdentityByDefaultColumn", migration);
        Assert.Contains("begin transaction read only", preflight, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("create table if not exists \"RoomTransfers\"", apply);
        Assert.DoesNotContain("truncate ", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("delete from", apply, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("update \"RoomInventoryAdjustments\"", apply, StringComparison.OrdinalIgnoreCase);
    }

    private static InventoryDeductionInvariantService Service(CropQcDbContext db) =>
        new(db, NullLogger<InventoryDeductionInvariantService>.Instance);

    private static CropQcDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase($"inventory-invariant-{Guid.NewGuid():N}")
            .Options);

    private static RoomInventoryAdjustment Adjustment(int change) => new()
    {
        CropYear = 2026,
        WarehouseId = 1,
        RoomId = 1,
        FruitProfileId = 10,
        GrowerName = "Grower",
        LotNumber = "LOT-1",
        VarietyCode = "BART",
        InventoryStatus = "Organic",
        OldBinCount = change < 0 ? 20 : 0,
        ChangeAmount = change,
        NewBinCount = change < 0 ? 20 + change : change,
        AdjustmentType = change < 0 ? BinsRunService.AdjustmentType : BinsRunService.ReversalAdjustmentType,
        AdjustmentAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow,
        InventoryInvariantVersion = InventoryDeductionInvariantService.CurrentVersion,
        InventoryOperationKey = $"test:{Guid.NewGuid():N}"
    };

    private static BinsRunEntry Entry(RoomInventoryAdjustment adjustment, int bins) => new()
    {
        InventoryAdjustment = adjustment,
        WarehouseId = adjustment.WarehouseId,
        RoomId = adjustment.RoomId,
        CropYear = adjustment.CropYear,
        FruitProfileId = adjustment.FruitProfileId,
        GrowerName = adjustment.GrowerName,
        LotNumber = adjustment.LotNumber,
        VarietyCode = adjustment.VarietyCode,
        InventoryStatus = adjustment.InventoryStatus,
        PreviousAvailableBins = adjustment.OldBinCount ?? 0,
        BinsRun = bins,
        NewAvailableBins = adjustment.NewBinCount,
        RunAt = adjustment.AdjustmentAt,
        CreatedAt = DateTimeOffset.UtcNow,
        TransactionType = ActualRunTransactionTypes.Depletion
    };

    private static RoomTransfer Transfer() => new()
    {
        OperationKey = $"transfer-{Guid.NewGuid():N}",
        SourceWarehouseId = 1,
        SourceRoomId = 1,
        DestinationWarehouseId = 1,
        DestinationRoomId = 2,
        CropYear = 2026,
        FruitProfileId = 10,
        GrowerName = "Grower",
        LotNumber = "LOT-1",
        VarietyCode = "BART",
        InventoryStatus = "Organic",
        BinCount = 12,
        Reason = "Move",
        TransferredAt = DateTimeOffset.UtcNow,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static string ReadRepositoryFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(path)) return File.ReadAllText(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(string.Join('/', parts));
    }
}
