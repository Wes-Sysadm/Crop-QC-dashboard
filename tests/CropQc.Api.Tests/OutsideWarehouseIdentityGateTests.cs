using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Inventory;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class OutsideWarehouseIdentityGateTests
{
    [InventoryCommandRestoreFact]
    public async Task Restored_postgres_gate_accepts_the_four_reviewed_shapes_without_any_writes()
    {
        await using var fixture = await CanonicalRestoreFixture.Clone();
        await using var db = fixture.CreateDbContext();
        Assert.False(await db.RoomInventoryAdjustments.AnyAsync(x => x.Id >= 4128));
        Assert.False(await db.TreatmentLineageSegments.AnyAsync(x => x.Id >= 746));
        Assert.False(await db.OutsideWarehouseTransfers.AnyAsync(x => x.Id >= 40));
        if (!await db.OutsideWarehouses.AnyAsync(x => x.Id == 6))
            db.OutsideWarehouses.Add(new() { Id = 6, Code = "OUTSIDE-GATE-FIXTURE", Name = "Disposable gate fixture" });
        var shapes = new[]
        {
            Seed(db, 4128, 40, 658, "9932", "MFR - BAKER CONV", 48, 49, 811, 746),
            Seed(db, 4129, 41, 97, "9285", "DL & JJ - GRANT CONV", 12, 63, 812, 747),
            Seed(db, 4137, 42, 658, "9932", "MFR - BAKER CONV", 1, 1, 819, 746),
            Seed(db, 4138, 43, 97, "9285", "DL & JJ - GRANT CONV", 47, 51, 820, 747)
        };
        foreach (var (_, transfer, movement) in shapes.DistinctBy(x => x.Item3.SourceSegmentId))
            db.TreatmentLineageSegments.Add(new()
            {
                // Historical anchors for later dispatch fixtures do not replace or
                // normalize the older restore's current source projections.
                Disposition = "Historical",
                Id = movement.SourceSegmentId!.Value,
                WarehouseId = 1,
                RoomId = 6,
                CropYear = 2026,
                GrowerLotId = transfer.GrowerLotId,
                FruitProfileId = 3,
                IdentityKey = movement.IdentityKey,
                GrowerNumberSnapshot = transfer.GrowerNumberSnapshot,
                GrowerNameSnapshot = transfer.GrowerNameSnapshot,
                LotNumberSnapshot = transfer.LotNumberSnapshot,
                VarietyCodeSnapshot = "GOLD",
                ProductionTypeSnapshot = "Conventional",
                IsOrganicSnapshot = false,
                InventoryStatusSnapshot = "",
                TreatmentState = "Untreated",
                TreatmentSignature = "u",
                CreatedAt = transfer.CreatedAt,
                UpdatedAt = transfer.CreatedAt
            });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var before = await fixture.Snapshot();
        var result = await Service(db).VerifyReadinessAsync(default);
        Assert.True(result.IsReady, string.Join("; ", result.Issues.Where(x => x.BlocksDeployment).Select(x => $"{x.AdjustmentId}:{x.Code}")));
        Assert.Equal(before, await fixture.Snapshot());
        Assert.False(db.ChangeTracker.HasChanges());
    }

    [Theory]
    [InlineData(4128, 40, 658, "9932", "MFR - BAKER CONV", 48, 49, 811, 746)]
    [InlineData(4129, 41, 97, "9285", "DL & JJ - GRANT CONV", 12, 63, 812, 747)]
    [InlineData(4137, 42, 658, "9932", "MFR - BAKER CONV", 1, 1, 819, 746)]
    [InlineData(4138, 43, 97, "9285", "DL & JJ - GRANT CONV", 47, 51, 820, 747)]
    public async Task Production_shapes_pass_without_rewriting_historical_evidence(
        long adjustmentId, long transferId, int growerLot, string number, string name, int bins, int before, long movementId, long segmentId)
    {
        using var db = CreateDb();
        Seed(db, adjustmentId, transferId, growerLot, number, name, bins, before, movementId, segmentId);
        await db.SaveChangesAsync();
        await AssertReadOnlyPass(db);
    }

    [Theory]
    [InlineData("Current renamed grower", "Original historical grower")]
    [InlineData(" mfr - baker conv ", "MFR - BAKER CONV")]
    [InlineData("9932", "MFR - BAKER CONV")]
    public async Task Names_are_display_evidence_and_master_rename_does_not_rewrite_snapshot(string ledgerName, string historicalName)
    {
        using var db = CreateDb();
        var (adjustment, transfer, _) = Seed(db);
        adjustment.GrowerName = ledgerName;
        transfer.GrowerNameSnapshot = historicalName;
        db.GrowerLots.Add(new() { Id = 658, LotNumber = "9932", Grower = historicalName, IsActive = true });
        await db.SaveChangesAsync();
        (await db.GrowerLots.SingleAsync()).Grower = "Subsequent master-data rename";
        await db.SaveChangesAsync();
        await AssertReadOnlyPass(db);
        Assert.Equal(historicalName, (await db.OutsideWarehouseTransfers.SingleAsync()).GrowerNameSnapshot);
    }

    [Theory]
    [InlineData("growerLot")]
    [InlineData("growerNumber")]
    [InlineData("profile")]
    [InlineData("lot")]
    [InlineData("crop")]
    [InlineData("warehouse")]
    [InlineData("room")]
    [InlineData("receipt")]
    [InlineData("variety")]
    [InlineData("organic")]
    [InlineData("status")]
    [InlineData("quantity")]
    [InlineData("balance")]
    [InlineData("reversal")]
    [InlineData("operation")]
    [InlineData("movementIdentity")]
    [InlineData("movementQuantity")]
    [InlineData("treatment")]
    [InlineData("missingMovement")]
    [InlineData("secondParent")]
    public async Task Real_identity_quantity_and_provenance_mismatches_still_block(string mismatch)
    {
        using var db = CreateDb();
        var (a, t, m) = Seed(db);
        switch (mismatch)
        {
            case "growerLot": a.GrowerLotId = 97; break;
            case "growerNumber": t.GrowerNumberSnapshot = "9285"; break;
            case "profile": a.FruitProfileId = 12; break;
            case "lot": a.LotNumber = "9285"; break;
            case "crop": a.CropYear = 2025; break;
            case "warehouse": a.WarehouseId = 2; break;
            case "room": a.RoomId = 7; break;
            case "receipt": a.ReceiptId = 2475; break;
            case "variety": a.VarietyCode = "GALA"; break;
            case "organic": t.IsOrganicSnapshot = true; break;
            case "status": a.InventoryStatus = "Organic"; break;
            case "quantity": a.ChangeAmount--; break;
            case "balance": a.NewBinCount++; break;
            case "reversal": t.IsReversed = true; break;
            case "operation": m.OperationKey = "unrelated-operation:s746"; break;
            case "movementIdentity": m.IdentityKey = m.IdentityKey.Replace("|9932|", "|9285|"); break;
            case "movementQuantity": m.BinCount++; break;
            case "treatment": m.TreatmentSignatureSnapshot = "treated:1"; break;
            case "missingMovement": db.TreatmentLineageMovements.Remove(m); break;
            case "secondParent": a.RoomInventoryLossId = 999; break;
        }
        await db.SaveChangesAsync();
        var result = await Service(db).VerifyReadinessAsync(default);
        Assert.False(result.IsReady);
        Assert.Contains(result.Issues, x => x.BlocksDeployment && x.AdjustmentId == a.Id);
        await Assert.ThrowsAsync<InventoryDeductionInvariantException>(() => Service(db).ValidateBeforeCommitAsync(default));
    }

    [Fact]
    public async Task Incomplete_legacy_identity_does_not_gain_a_name_only_exception()
    {
        using var db = CreateDb();
        var (a, t, _) = Seed(db);
        a.InventoryInvariantVersion = 1;
        a.GrowerLotId = t.GrowerLotId = null;
        await db.SaveChangesAsync();
        Assert.Contains((await Service(db).VerifyReadinessAsync(default)).Issues,
            x => x.Code == "OutsideWarehouseTransferIdentityMismatch" && x.BlocksDeployment);
    }

    [Fact]
    public async Task Canonical_reversal_preserves_original_snapshot_and_checks_both_ledger_sides()
    {
        using var db = CreateDb();
        var (a, t, _) = Seed(db);
        t.IsReversed = true;
        t.ReversalOperationKey = "return";
        var reversal = JsonSerializer.Deserialize<RoomInventoryAdjustment>(JsonSerializer.Serialize(a,
            new JsonSerializerOptions { ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles }))!;
        reversal.Id = 5000; reversal.OutsideWarehouseTransfer = t;
        reversal.AdjustmentType = OutsideWarehouseTransferAdjustmentTypes.Reversal;
        reversal.ChangeAmount = 48; reversal.OldBinCount = 1; reversal.NewBinCount = 49;
        reversal.InventoryOperationKey = "return:credit";
        db.RoomInventoryAdjustments.Add(reversal);
        await db.SaveChangesAsync();
        await AssertReadOnlyPass(db);
        reversal.FruitProfileId = 12;
        await db.SaveChangesAsync();
        Assert.False((await Service(db).VerifyReadinessAsync(default)).IsReady);
    }

    private static async Task AssertReadOnlyPass(CropQcDbContext db)
    {
        var before = Snapshot(db);
        var result = await Service(db).VerifyReadinessAsync(default);
        Assert.True(result.IsReady, string.Join("; ", result.Issues.Select(x => x.Code)));
        await Service(db).ValidateBeforeCommitAsync(default);
        Assert.Equal(before, Snapshot(db));
        Assert.False(db.ChangeTracker.HasChanges());
    }

    private static string Snapshot(CropQcDbContext db) => JsonSerializer.Serialize(new
    {
        Adjustments = db.RoomInventoryAdjustments.AsNoTracking().OrderBy(x => x.Id).ToArray(),
        Transfers = db.OutsideWarehouseTransfers.AsNoTracking().OrderBy(x => x.Id).ToArray(),
        Movements = db.TreatmentLineageMovements.AsNoTracking().OrderBy(x => x.Id).ToArray()
    });

    private static (RoomInventoryAdjustment, OutsideWarehouseTransfer, TreatmentLineageMovement) Seed(CropQcDbContext db,
        long adjustmentId = 4128, long transferId = 40, int growerLot = 658, string number = "9932",
        string name = "MFR - BAKER CONV", int bins = 48, int before = 49, long movementId = 811, long segmentId = 746)
    {
        var key = "fixture-" + transferId + ":0";
        var at = DateTimeOffset.Parse("2026-10-06T16:26:29Z");
        var t = new OutsideWarehouseTransfer
        {
            Id = transferId,
            OperationKey = key,
            OutsideWarehouseId = 6,
            OutsideWarehouseCodeSnapshot = "SUNFAIR",
            OutsideWarehouseNameSnapshot = "Sunfair Marketing",
            SourceWarehouseId = 1,
            SourceRoomId = 6,
            CropYear = 2026,
            GrowerLotId = growerLot,
            FruitProfileId = 3,
            GrowerNumberSnapshot = number,
            GrowerNameSnapshot = name,
            LotNumberSnapshot = number,
            VarietyCodeSnapshot = "GOLD",
            ProductionTypeSnapshot = "Conventional",
            IsOrganicSnapshot = false,
            InventoryStatusSnapshot = "",
            TreatmentStateSnapshot = "Untreated",
            TreatmentSignatureSnapshot = "u",
            TreatmentSummarySnapshot = "Untreated",
            BinCount = bins,
            CreatedAt = at,
            TransferredAt = at,
            CreatedByUserId = 2
        };
        var a = new RoomInventoryAdjustment
        {
            Id = adjustmentId,
            WarehouseId = 1,
            RoomId = 6,
            CropYear = 2026,
            GrowerLotId = growerLot,
            FruitProfileId = 3,
            GrowerName = number,
            LotNumber = number,
            VarietyCode = "GOLD",
            InventoryStatus = "",
            OldBinCount = before,
            ChangeAmount = -bins,
            NewBinCount = before - bins,
            AdjustmentType = OutsideWarehouseTransferAdjustmentTypes.Transfer,
            Source = "CanonicalInventory/v1",
            InventoryInvariantVersion = 3,
            InventoryOperationKey = key + ":out",
            OutsideWarehouseTransfer = t,
            OutsideWarehouseTransferId = t.Id,
            CreatedAt = at,
            AdjustmentAt = at
        };
        var m = new TreatmentLineageMovement
        {
            Id = movementId,
            OperationKey = key + ":s" + segmentId,
            MovementType = OutsideWarehouseTransferAdjustmentTypes.Transfer,
            SourceSegmentId = segmentId,
            SourceRoomId = 6,
            IdentityKey = new InventoryIdentity(2026, growerLot, 3, number, number, "GOLD", "Conventional", false, "").Key,
            TreatmentStateSnapshot = "Untreated",
            TreatmentSignatureSnapshot = "u",
            BinCount = bins,
            OutsideWarehouseTransfer = t,
            OutsideWarehouseTransferId = t.Id,
            CreatedAt = at,
            OccurredAt = at
        };
        db.AddRange(t, a, m);
        return (a, t, m);
    }

    private static CropQcDbContext CreateDb() => new(new DbContextOptionsBuilder<CropQcDbContext>()
        .UseInMemoryDatabase("outside-identity-" + Guid.NewGuid().ToString("N")).Options);
    private static InventoryDeductionInvariantService Service(CropQcDbContext db) => new(db, NullLogger<InventoryDeductionInvariantService>.Instance);
}
