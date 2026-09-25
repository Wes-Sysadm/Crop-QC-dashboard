using CropQc.Data;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public static class TruckReceiptReleaseSafety
{
    public const string Migration = "20260923202144_AddTruckReceiptReconciliation";

    public static async Task VerifySchemaAsync(CropQcDbContext db, CancellationToken ct)
    {
        if (!(await db.Database.GetAppliedMigrationsAsync(ct)).Contains(Migration))
            throw new InvalidOperationException("Truck Receipt migration is not recorded. Apply the reviewed schema package before starting this web build.");
        // These projections execute even on an empty database; a partial migration fails visibly.
        await db.Receipts.Select(x => new { x.IsTransferReceipt, x.TransferCompletedAt }).Take(1).ToListAsync(ct);
        await db.InterCrewTransfers.Select(x => new { x.RequiresTruckReceipt, x.ReceivingReceiptId }).Take(1).ToListAsync(ct);
        await db.ReceiptVarietyLines.Select(x => new { x.Id, x.ReceiptId, x.FruitProfileId, x.BinCount }).Take(1).ToListAsync(ct);
        if (db.Database.IsNpgsql())
        {
            var indexes = await db.Database.SqlQueryRaw<int>("""
                SELECT count(*)::integer AS "Value" FROM pg_indexes WHERE schemaname=current_schema() AND
                  indexname IN ('IX_InterCrewTransfers_ReceivingReceiptId','IX_ReceiptVarietyLines_ReceiptId_FruitProfileId') AND indexdef LIKE 'CREATE UNIQUE%'
                """).SingleAsync(ct);
            var foreignKeys = await db.Database.SqlQueryRaw<int>("""
                SELECT count(*)::integer AS "Value" FROM pg_constraint c JOIN pg_namespace n ON n.oid=c.connamespace
                WHERE n.nspname=current_schema() AND c.contype='f' AND c.confdeltype='r' AND c.conname IN
                  ('FK_InterCrewTransfers_Receipts_ReceivingReceiptId','FK_ReceiptVarietyLines_Receipts_ReceiptId','FK_ReceiptVarietyLines_FruitProfiles_FruitProfileId')
                """).SingleAsync(ct);
            if (indexes != 2 || foreignKeys != 3
                || !await TruckReceiptLedgerIndexContract.IsSatisfiedAsync(db.Database.GetDbConnection(), db.Database.ProviderName!, ct))
                throw new InvalidOperationException("Truck Receipt indexes or restrictive relationships are incomplete.");
        }
    }

    // Includes cancelled, unlinked and soft-deleted evidence. Disabling the flag does not undo first use.
    public static async Task<bool> CanUsePreFeatureApplicationAsync(CropQcDbContext db, CancellationToken ct) =>
        !await db.Receipts.AnyAsync(x => x.IsTransferReceipt || x.TransferCompletedAt != null, ct)
        && !await db.InterCrewTransfers.AnyAsync(x => x.RequiresTruckReceipt || x.ReceivingReceiptId != null, ct)
        && !await db.ReceiptVarietyLines.AnyAsync(ct)
        && !await db.RoomInventoryAdjustments.AnyAsync(x => x.AdjustmentType == TruckReceiptReconciliationService.ReturnToSource
            || x.AdjustmentType == TruckReceiptReconciliationService.ReopenDestination, ct);
}
