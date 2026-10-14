using CropQc.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public sealed partial class RoomTreatmentService
{
    private async Task<bool> ProveUntreatedGapAsync(RoomInventoryLedgerSnapshot snapshot,
        IReadOnlyCollection<TreatmentLineageSegment> segments, int missing, CancellationToken ct,
        long? pendingApplicationId = null, DateTimeOffset? asOf = null)
    {
        // Quantity is supplied by the authoritative ledger snapshot, never by a
        // projection. With recorded treatment history, an unrepresented remainder
        // needs independent arrival evidence; absence of a segment is insufficient.
        var applications = await dbContext.RoomTreatmentApplications.AsNoTracking()
            .Where(x => x.RoomId == snapshot.RoomId && x.Id != pendingApplicationId).ToListAsync(ct);
        var treatedMovement = await dbContext.TreatmentLineageMovements.AsNoTracking().AnyAsync(x =>
            (x.SourceRoomId == snapshot.RoomId || x.DestinationRoomId == snapshot.RoomId)
            && x.TreatmentSignatureSnapshot != "u", ct);
        if (applications.Count == 0 && !treatedMovement) return true;
        return await ProveUnrepresentedReceiptArrivalsAsync(snapshot, segments, missing, ct, pendingApplicationId, asOf);
    }

    // Legacy identities can lack structured grower IDs. Require exact untouched
    // receipt arrivals instead of relaxing canonical identity requirements or
    // treating a projection gap as evidence of untreated inventory.
    private async Task<bool> ProveUnrepresentedReceiptArrivalsAsync(RoomInventoryLedgerSnapshot snapshot,
        IReadOnlyCollection<TreatmentLineageSegment> segments, int missing, CancellationToken ct, long? pendingApplicationId, DateTimeOffset? asOf)
    {
        var rows = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => x.RoomId == snapshot.RoomId && x.WarehouseId == snapshot.WarehouseId
                && x.CropYear == snapshot.CropYear && x.FruitProfileId == snapshot.FruitProfileId
                && x.GrowerLotId == snapshot.GrowerLotId).ToListAsync(ct);
        rows = rows.Where(x => (asOf == null || x.AdjustmentAt <= asOf)
            && string.Equals(x.LotNumber.Trim(), snapshot.Lot.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        if (rows.Sum(x => x.ChangeAmount) != snapshot.CurrentBins) return false;
        var unassigned = segments.Where(x => x.ReceiptId == null && x.CurrentBins > 0).ToArray();
        var appIds = unassigned.SelectMany(x => x.Applications.Select(a => a.RoomTreatmentApplicationId)).Distinct().ToArray();
        var appliedAt = await dbContext.RoomTreatmentApplications.AsNoTracking().Where(x => appIds.Contains(x.Id))
            .ToDictionaryAsync(x => x.Id, x => x.AppliedAt, ct);
        var representedThrough = unassigned.Length == 0 ? DateTimeOffset.MinValue : unassigned.Max(x =>
            x.Applications.Count == 0 ? x.UpdatedAt : x.Applications.Max(a => appliedAt.GetValueOrDefault(a.RoomTreatmentApplicationId, x.UpdatedAt)));
        var arrivals = rows.Where(x => x.AdjustmentType is "ReceiptAdd" or "Receipt" && x.ReceiptId != null && x.ChangeAmount > 0
            && x.CreatedAt >= representedThrough && !segments.Any(s => s.ReceiptId == x.ReceiptId)).ToArray();
        if (arrivals.Length == 0 || arrivals.Sum(x => x.ChangeAmount) != missing
            || arrivals.GroupBy(x => x.ReceiptId).Any(g => g.Count() != 1)) return false;
        var ids = arrivals.Select(x => x.ReceiptId!.Value).ToArray();
        var receipts = await dbContext.Receipts.AsNoTracking().Where(x => ids.Contains(x.Id)).ToListAsync(ct);
        if (receipts.Count != ids.Length || receipts.Any(r => r.IsDeleted || r.IsTransferReceipt || r.ReceiptType != "Truck receipt"
            || r.RoomId != snapshot.RoomId || r.WarehouseId != snapshot.WarehouseId || r.CropYear != snapshot.CropYear
            || r.FruitProfileId != snapshot.FruitProfileId || r.GrowerLotId != snapshot.GrowerLotId
            || !string.Equals((r.GrowerNumber ?? r.LotCode).Trim(), snapshot.Lot.Trim(), StringComparison.OrdinalIgnoreCase)
            || arrivals.Single(x => x.ReceiptId == r.Id).ChangeAmount != r.BinCount)) return false;
        var firstArrival = arrivals.Min(x => x.CreatedAt);
        if (rows.Any(x => !arrivals.Contains(x) && (ids.Contains(x.ReceiptId ?? -1)
            || x.ReceiptId == null && x.CreatedAt >= firstArrival && x.ChangeAmount != 0))) return false;
        var movements = await dbContext.TreatmentLineageMovements.AsNoTracking().Where(x => ids.Contains(x.ReceiptId ?? -1)
            || x.SourceRoomId == snapshot.RoomId || x.DestinationRoomId == snapshot.RoomId).ToListAsync(ct);
        if (movements.Any(x => ids.Contains(x.ReceiptId ?? -1) || x.ReceiptId == null && x.CreatedAt >= firstArrival)) return false;
        var applications = await dbContext.RoomTreatmentApplications.AsNoTracking()
            .Where(x => x.RoomId == snapshot.RoomId || ids.Contains(x.ReceiptId ?? -1)).ToListAsync(ct);
        return !applications.Any(x => x.Id != pendingApplicationId
            && (ids.Contains(x.ReceiptId ?? -1) || x.ReceiptId == null && arrivals.Any(a => x.AppliedAt >= a.AdjustmentAt)));
    }
}
