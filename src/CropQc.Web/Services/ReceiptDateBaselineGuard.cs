using CropQc.Data;

namespace CropQc.Web.Services;

/// <summary>Shared guard for ordinary Web/API metadata edits and canonical transactions.</summary>
internal static class ReceiptDateBaselineGuard
{
    public static Task<bool> WouldChangeEligibilityAsync(CropQcDbContext db, long receiptId,
        DateTimeOffset oldReceivedAt, DateTimeOffset newReceivedAt, CancellationToken cancellationToken) =>
        CropQc.Data.Inventory.ReceiptInventoryDateGuard.WouldChangeEligibilityAsync(db, receiptId, oldReceivedAt, newReceivedAt, cancellationToken);
}
