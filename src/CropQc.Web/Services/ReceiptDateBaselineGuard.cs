using CropQc.Data;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

/// <summary>Prevents metadata edits from changing existing receipt-linked ledger eligibility.</summary>
internal static class ReceiptDateBaselineGuard
{
    public static async Task<bool> WouldChangeEligibilityAsync(
        CropQcDbContext db,
        long receiptId,
        DateTimeOffset oldReceivedAt,
        DateTimeOffset newReceivedAt,
        CancellationToken cancellationToken)
    {
        if (oldReceivedAt == newReceivedAt) return false;

        // Baselines are room-wide, including prior crops/identities. Inspect every room
        // with linked history, not just the receipt's original room or current balance.
        var affectedRooms = db.RoomInventoryAdjustments.Where(x => x.ReceiptId == receiptId)
            .Select(x => x.RoomId);
        var cutoffs = await db.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => affectedRooms.Contains(x.RoomId)
                && x.ReceiptId == null
                && x.AdjustmentType == RoomInventoryImportService.StartingInventoryAdjustmentType)
            .GroupBy(x => x.RoomId)
            .Select(x => x.Max(row => row.AdjustmentAt))
            .ToListAsync(cancellationToken);

        // Any baseline at/after ReceivedAt supersedes a linked row: the latest
        // cutoff is sufficient. Equality is superseded. Older shadowed baselines
        // must not prohibit a harmless edit. Keep conservation arithmetic independent.
        return cutoffs.Any(cutoff => (oldReceivedAt <= cutoff) != (newReceivedAt <= cutoff));
    }
}
