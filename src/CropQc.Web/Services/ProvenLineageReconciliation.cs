using CropQc.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

/// <summary>
/// Read-only proof for legacy shared, untreated projections. Quantity alone is
/// never evidence: replay the exact ledger position from an empty boundary and
/// prove every arrival in its current occupancy has untreated provenance.
/// </summary>
public sealed class ProvenLineageReconciliation(CropQcDbContext db)
{
    public sealed record Proof(Dictionary<long, int> Quantities, long[] LedgerIds,
        long[] ReceiptIds, long[] IncomingMovementIds, DateTimeOffset OccupiedSince);

    public async Task<Proof?> ProveAsync(RoomInventoryLedgerSnapshot snapshot,
        IReadOnlyList<TreatmentLineageSegment> segments, CancellationToken cancellationToken)
    {
        var key = RoomTreatmentService.IdentityKey(snapshot);
        var active = segments.Where(x => x.CurrentBins != 0).ToList();
        if (snapshot.CurrentBins < 0 || snapshot.CropYear is null || snapshot.GrowerLotId is null
            || snapshot.FruitProfileId is null || active.Count == 0
            || active.Sum(x => (long)x.CurrentBins) <= snapshot.CurrentBins
            || active.Any(x => x.CurrentBins < 0 || x.ReceiptId != null
                || x.TreatmentState != TreatmentLineageStates.Untreated || x.TreatmentSignature != "u"
                || x.Applications.Count != 0 || x.WarehouseId != snapshot.WarehouseId
                || x.RoomId != snapshot.RoomId || x.CropYear != snapshot.CropYear
                || x.GrowerLotId != snapshot.GrowerLotId || x.FruitProfileId != snapshot.FruitProfileId
                || x.IsOrganicSnapshot != snapshot.IsOrganic
                || InventoryStatusIdentity.NormalizeLineageKey(x.IdentityKey) != key
                || !Equal(x.LotNumberSnapshot, snapshot.Lot)
                || !Equal(x.VarietyCodeSnapshot, snapshot.Variety)
                || !Equal(x.ProductionTypeSnapshot, snapshot.ProductionType)
                || InventoryStatusIdentity.Normalize(x.InventoryStatusSnapshot, x.ProductionTypeSnapshot)
                    != InventoryStatusIdentity.Normalize(snapshot.InventoryStatus, snapshot.ProductionType)))
            return null;

        // Imported baselines and inferred/missing identities need the separate
        // reviewed correction path. Never guess how to replay those ledgers.
        var profile = await db.FruitProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == snapshot.FruitProfileId, cancellationToken);
        if (profile is null || !Equal(profile.VarietyCode, snapshot.Variety)
            || !Equal(profile.ProductionType, snapshot.ProductionType) || profile.IsOrganic != snapshot.IsOrganic) return null;
        var roomRows = await db.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => x.RoomId == snapshot.RoomId && x.Id <= snapshot.LatestAdjustmentId)
            .ToListAsync(cancellationToken);
        if (roomRows.Any(x => x.AdjustmentType == RoomInventoryImportService.StartingInventoryAdjustmentType))
            return null;
        var rows = roomRows.Where(x => Equal(x.LotNumber, snapshot.Lot)
            && (x.FruitProfileId == snapshot.FruitProfileId || x.FruitProfileId == null))
            .OrderBy(x => x.AdjustmentAt).ThenBy(x => x.Id).ToList();
        if (rows.Count == 0 || rows.Any(x => x.WarehouseId != snapshot.WarehouseId
            || x.CropYear != snapshot.CropYear || x.GrowerLotId != snapshot.GrowerLotId
            || x.FruitProfileId != snapshot.FruitProfileId
            || (!string.IsNullOrWhiteSpace(x.VarietyCode) && !Equal(x.VarietyCode, snapshot.Variety))
            || InventoryStatusIdentity.Normalize(x.InventoryStatus, snapshot.ProductionType)
                != InventoryStatusIdentity.Normalize(snapshot.InventoryStatus, snapshot.ProductionType)))
            return null;
        long balance = 0;
        var epoch = new List<RoomInventoryAdjustment>();
        foreach (var row in rows)
        {
            if (balance == 0) epoch.Clear();
            balance += row.ChangeAmount;
            if (balance < 0) return null;
            epoch.Add(row);
        }
        if (balance != snapshot.CurrentBins || epoch.Count == 0) return null;
        var arrivals = epoch.Where(x => x.ChangeAmount > 0).ToList();
        if (arrivals.Count == 0) return null;
        var receiptIds = arrivals.Where(x => x.AdjustmentType == "ReceiptAdd" && x.ReceiptId != null)
            .Select(x => x.ReceiptId!.Value).Distinct().ToArray();
        var receipts = await db.Receipts.AsNoTracking().Where(x => receiptIds.Contains(x.Id))
            .ToListAsync(cancellationToken);
        var movements = await db.TreatmentLineageMovements.AsNoTracking()
            .Where(x => x.DestinationRoomId == snapshot.RoomId || x.SourceRoomId == snapshot.RoomId)
            .ToListAsync(cancellationToken);
        var incoming = new List<long>();
        foreach (var arrival in arrivals)
        {
            if (arrival.AdjustmentType == "ReceiptAdd" && arrival.ReceiptId is not null)
            {
                var receipt = receipts.SingleOrDefault(x => x.Id == arrival.ReceiptId);
                if (receipt is null || receipt.IsTransferReceipt || receipt.IsDeleted || receipt.CropYear != snapshot.CropYear
                    || receipt.GrowerLotId != snapshot.GrowerLotId || receipt.FruitProfileId != snapshot.FruitProfileId
                    || receipt.BinCount != arrival.ChangeAmount
                    || !Equal(receipt.GrowerNumber ?? receipt.LotCode, snapshot.Lot)
                    || arrivals.Count(x => x.ReceiptId == receipt.Id) != 1) return null;
                continue;
            }
            // An incoming transfer requires exact persisted movement evidence.
            // Unsupported true-ups, corrections and returns stay fail-closed.
            if (arrival.AdjustmentType != "TransferIn" || arrival.RoomTransferId is null) return null;
            var evidence = movements.Where(x => x.DestinationRoomId == snapshot.RoomId && x.RoomTransferId == arrival.RoomTransferId
                && x.MovementType == TreatmentLineageMovementTypes.Transfer
                && x.ReversesTreatmentLineageMovementId == null).ToList();
            if (evidence.Count == 0 || evidence.Sum(x => x.BinCount) != arrival.ChangeAmount
                || evidence.Any(x => x.BinCount <= 0 || x.TreatmentStateSnapshot != TreatmentLineageStates.Untreated
                    || x.TreatmentSignatureSnapshot != "u"
                    || InventoryStatusIdentity.NormalizeLineageKey(x.IdentityKey) != key)) return null;
            incoming.AddRange(evidence.Select(x => x.Id));
        }
        var occupiedSince = arrivals.Min(x => x.AdjustmentAt);
        var segmentIds = segments.Select(x => x.Id).ToHashSet();
        if (movements.Any(x => x.OccurredAt >= occupiedSince
            && (InventoryStatusIdentity.NormalizeLineageKey(x.IdentityKey) == key
                || (x.SourceRoomId == snapshot.RoomId && segmentIds.Contains(x.SourceSegmentId ?? -1))
                || (x.DestinationRoomId == snapshot.RoomId && segmentIds.Contains(x.DestinationSegmentId ?? -1)))
            && (x.TreatmentStateSnapshot != TreatmentLineageStates.Untreated || x.TreatmentSignatureSnapshot != "u")))
            return null;
        // Even reversed applications need an explicit provenance reconstruction;
        // do not erase their ambiguity by assuming reversal means never treated.
        var applications = await db.RoomTreatmentApplications.AsNoTracking().Where(x =>
            (x.ReceiptId != null && receiptIds.Contains(x.ReceiptId.Value))
            || (x.RoomId == snapshot.RoomId && x.ReceiptId == null))
            .Select(x => new { x.ReceiptId, x.AppliedAt }).ToListAsync(cancellationToken);
        // Compare offsets in memory: SQLite cannot translate DateTimeOffset ordering.
        if (applications.Any(x => x.ReceiptId != null || x.AppliedAt >= occupiedSince))
            return null;

        // Keep an existing shared segment as the current projection. Zeroed rows,
        // keys, receipt/application links and original movements remain historical
        // evidence. Stable ordering makes read-only selection and writes agree.
        var retained = active.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id).First();
        return new(active.ToDictionary(x => x.Id, x => x.Id == retained.Id ? snapshot.CurrentBins : 0),
            rows.Select(x => x.Id).ToArray(), receiptIds, incoming.ToArray(), occupiedSince);
    }

    private static bool Equal(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
