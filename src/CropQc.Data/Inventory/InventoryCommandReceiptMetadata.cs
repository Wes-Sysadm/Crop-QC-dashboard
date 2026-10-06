using System.Collections.Immutable;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

public sealed partial class InventoryCommandExecutor
{
    private static async Task<ImmutableArray<InventoryCommandEffect>> UpdateReceiptMetadataAsync(CropQcDbContext db, InventoryCommand c, DateTimeOffset now, CancellationToken ct)
    {
        Require(c.Lines.IsEmpty && c.ReceiptMetadata is { ReceiptId: > 0, ExpectedVersion: >= 0 }, "An exact receipt and reviewed version are required.");
        var m = c.ReceiptMetadata!; var expected = m.ExpectedReceipt;
        var r = await db.Receipts.SingleOrDefaultAsync(x => x.Id == m.ReceiptId && !x.IsDeleted && !x.IsTransferReceipt, ct);
        Require(r != null, "An active ordinary receipt is required.");
        Require(r!.ConcurrencyVersion == m.ExpectedVersion, "Receipt changed; reload before editing.", InventoryCommandStatus.Stale);
        Require(!m.SameDayOnly || r.ReceivedAt.UtcDateTime.Date == now.UtcDateTime.Date, "Only same-day receipt fields can be updated through this API.");
        Require(r.CropYear == expected.CropYear && r.WarehouseId == expected.WarehouseId && r.RoomId == expected.RoomId
            && r.GrowerLotId == (expected.GrowerLotId == 0 ? null : expected.GrowerLotId) && r.FruitProfileId == expected.FruitProfileId && r.BinCount == expected.Quantity
            && r.ReceiptType == expected.ReceiptType, "Quantity, identity and location changes require an administrator inventory correction.");
        Require(m.ReceivedAt != default && !string.IsNullOrWhiteSpace(m.ReceiptNumber) && !string.IsNullOrWhiteSpace(m.GrowerName), "Receipt fields are incomplete.");
        Require(!await db.Receipts.AnyAsync(x => x.Id != r.Id && !x.IsDeleted && x.CompuTechReceiptId == m.ReceiptNumber, ct), "Receipt number already exists.");
        Require(!await ReceiptInventoryDateGuard.WouldChangeEligibilityAsync(db, r.Id, r.ReceivedAt, m.ReceivedAt, ct),
            "This date would change opening inventory accounting. Request a controlled accounting review.");
        var before = ReceiptValues(r);
        if (m.SameDayOnly && r.GrowerName != m.GrowerName.Trim())
            foreach (var sample in await db.QcSamples.Where(x => x.ReceiptId == r.Id).ToListAsync(ct))
            { sample.Status = "Needs Review"; sample.UpdatedAt = now; }
        r.ReceivedAt = m.ReceivedAt.ToUniversalTime(); r.CompuTechReceiptId = m.ReceiptNumber.Trim(); r.GrowerName = m.GrowerName.Trim();
        r.ConcurrencyVersion++; r.UpdatedAt = now;
        AddAudit(db, c, "CanonicalReceiptMetadataUpdated", r.Id.ToString(), before, ReceiptValues(r), now);
        await db.SaveChangesAsync(ct);
        return [new("receipt:" + r.Id, r.BinCount, r.BinCount, 0, r.Id, [], [])];
    }
}

public static class ReceiptInventoryDateGuard
{
    public static async Task<bool> WouldChangeEligibilityAsync(CropQcDbContext db, long receiptId, DateTimeOffset before, DateTimeOffset after, CancellationToken ct)
    {
        if (before == after) return false;
        var rooms = db.RoomInventoryAdjustments.Where(x => x.ReceiptId == receiptId).Select(x => x.RoomId);
        var cutoffs = await db.RoomInventoryAdjustments.AsNoTracking().Where(x => rooms.Contains(x.RoomId)
            && x.ReceiptId == null && x.AdjustmentType == "StartingInventoryImport")
            .GroupBy(x => x.RoomId).Select(x => x.Max(row => row.AdjustmentAt)).ToListAsync(ct);
        return cutoffs.Any(cutoff => (before <= cutoff) != (after <= cutoff));
    }
}
