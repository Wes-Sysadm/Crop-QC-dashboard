using System.Collections.Immutable;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

public sealed partial class InventoryEvidenceLoader
{
    public async Task<IReadOnlyDictionary<long, InventoryIdentity>> ResolveMovementIdentitiesAsync(long[] ids, CancellationToken ct)
    {
        var movements = await Bounded(db.TreatmentLineageMovements.AsNoTracking().Include(x => x.SourceSegment)
            .Where(x => ids.Contains(x.Id)), ct);
        var map = await CanonicalIdentityMap.LoadAsync(db, movements.Where(x => x.ReceiptId != null)
            .Select(x => x.ReceiptId!.Value).ToArray(), ct);
        if (movements.Any(x => x.SourceSegment == null) || movements.Count != ids.Distinct().Count())
            throw new InvalidOperationException("Original dispatch identity evidence is missing.");
        return movements.ToDictionary(x => x.Id, x => map.Resolve(Identity(x.SourceSegment!), x.ReceiptId).Current);
    }

    private async Task<InventoryEvidenceBatch> LoadReceiptHeldAsync(InventoryScope scope, DateTimeOffset asOf, string consistency, CancellationToken ct)
    {
        var rooms = scope.RoomIds.IsDefaultOrEmpty ? null : scope.RoomIds.ToArray();
        var acks = await Bounded(db.ReceiptCustodyAcknowledgments.AsNoTracking().Include(x => x.Receipt).Include(x => x.InterCrewTransfer)
            .Include(x => x.DispatchMovement).ThenInclude(x => x.SourceSegment).ThenInclude(x => x!.Applications)
            .Include(x => x.Placements).ThenInclude(x => x.InventoryAdjustment).Include(x => x.Placements).ThenInclude(x => x.Movement)
            .Where(x => (scope.CustodyRecordId == null || x.ReceiptId == scope.CustodyRecordId)
                && (scope.WarehouseId == null || x.Receipt.WarehouseId == scope.WarehouseId)
                && (rooms == null || rooms.Contains(x.InterCrewTransfer.SourceRoomId) || rooms.Contains(x.Receipt.RoomId))), ct);
        var receiptIds = acks.Where(x => x.DispatchMovement.ReceiptId != null).Select(x => x.DispatchMovement.ReceiptId!.Value).Distinct().ToArray();
        var transferIds = acks.Select(x => x.InterCrewTransferId).Distinct().ToArray();
        var ledger = await Bounded(db.RoomInventoryAdjustments.AsNoTracking().Where(x => transferIds.Contains(x.InterCrewTransferId ?? -1)), ct);
        var movements = await Bounded(db.TreatmentLineageMovements.AsNoTracking().Include(x => x.SourceSegment).Include(x => x.DestinationSegment)
            .Where(x => transferIds.Contains(x.InterCrewTransferId ?? -1)), ct);
        var segmentIndex = movements.SelectMany(x => new[] { x.SourceSegment, x.DestinationSegment }).Where(x => x != null)
            .DistinctBy(x => x!.Id).ToDictionary(x => x!.Id, x => x!);
        var validParents = acks.GroupBy(x => x.InterCrewTransferId).ToDictionary(g => g.Key, g =>
        {
            var parent = g.First().InterCrewTransfer;
            var rows = ledger.Where(x => x.InterCrewTransferId == g.Key).ToArray();
            var flow = movements.Where(x => x.InterCrewTransferId == g.Key).ToArray();
            var placements = g.SelectMany(x => x.Placements).ToArray();
            return InventoryPhysicalFlow.Matches(rows, flow, segmentIndex)
                && parent.BinsReceived == g.Sum(x => x.Quantity) && parent.BinsReceived <= parent.BinsLoaded
                && rows.Sum(x => x.ChangeAmount) == -parent.BinsLoaded + placements.Sum(x => x.Quantity)
                && flow.Where(x => x.MovementType == "InterCrewReceive").All(x => placements.Any(p => p.MovementId == x.Id))
                && g.GroupBy(x => x.DispatchMovementId).All(a => a.Sum(x => x.Quantity)
                    <= a.First().DispatchMovement.BinCount - flow.Where(x => x.ReversesTreatmentLineageMovementId == a.Key).Sum(x => x.BinCount));
        });
        var origins = await Bounded(db.Receipts.AsNoTracking().Where(x => receiptIds.Contains(x.Id)), ct);
        var identities = await CanonicalIdentityMap.LoadAsync(db, receiptIds, ct);
        var appIds = acks.SelectMany(x => x.DispatchMovement.SourceSegment?.Applications.Select(a => a.RoomTreatmentApplicationId) ?? []).Distinct().ToArray();
        var applications = await Bounded(db.RoomTreatmentApplications.AsNoTracking().Where(x => appIds.Contains(x.Id)), ct);
        var apps = applications.Select(x => new InventoryApplicationEvidence(x.Id, x.AppliedAt, x.ReversedAt, x.ReceiptId, x.RoomId)).ToImmutableArray();
        var result = ImmutableArray.CreateBuilder<InventoryPositionEvidence>();
        foreach (var group in acks.Where(x => x.DispatchMovement.SourceSegment != null).GroupBy(x => new
        {
            x.ReceiptId,
            Identity = identities.Resolve(Identity(x.DispatchMovement.SourceSegment!), x.DispatchMovement.ReceiptId).Current
        }))
        {
            var receipt = group.First().Receipt;
            var identity = group.Key.Identity;
            var quantity = group.Sum(x => x.Quantity - x.Placements.Sum(p => p.Quantity));
            var valid = group.All(x => validParents[x.InterCrewTransferId] && x.Quantity > 0 && x.InterCrewTransfer.ReceivingReceiptId == x.ReceiptId
                && x.DispatchMovement.InterCrewTransferId == x.InterCrewTransferId && x.DispatchMovement.MovementType == "InterCrewDispatch"
                && x.Quantity <= x.DispatchMovement.BinCount && x.Placements.Sum(p => p.Quantity) <= x.Quantity
                && x.Placements.All(p => p.Quantity > 0 && p.InventoryAdjustment.InterCrewTransferId == x.InterCrewTransferId
                    && p.InventoryAdjustment.ChangeAmount == p.Quantity && p.Movement.InterCrewTransferId == x.InterCrewTransferId
                    && p.Movement.BinCount == p.Quantity && p.Movement.SourceSegmentId == x.DispatchMovement.SourceSegmentId))
                && group.GroupBy(x => x.DispatchMovementId).All(g => g.Sum(x => x.Quantity) <= g.First().DispatchMovement.BinCount);
            var projections = group.Select(x =>
            {
                var source = x.DispatchMovement.SourceSegment!;
                var treatment = InventoryEffectiveTreatment.Read(x.DispatchMovement.TreatmentSignatureSnapshot, x.DispatchMovement.TreatmentStateSnapshot,
                    source.Applications.Select(a => a.RoomTreatmentApplicationId).ToImmutableArray(), apps);
                return Projection(source, Identity(source), source.WarehouseId) with
                {
                    Quantity = x.Quantity - x.Placements.Sum(p => p.Quantity),
                    Disposition = "Current",
                    RetiredQuantity = null,
                    Signature = treatment.Signature,
                    State = treatment.State,
                    ApplicationIds = treatment.ApplicationIds,
                    ReceiptId = x.DispatchMovement.ReceiptId
                };
            }).ToImmutableArray();
            var receipts = origins.Where(x => group.Any(a => a.DispatchMovement.ReceiptId == x.Id)).Select(x => Receipt(x, identity)).ToImmutableArray();
            var watermark = Watermark(new
            {
                receipt.Id,
                receipt.ConcurrencyVersion,
                Acknowledgments = group.Select(x => new
                { x.Id, x.Quantity, x.DispatchMovementId, Placements = x.Placements.Select(p => new { p.Id, p.Quantity, p.MovementId, p.InventoryAdjustmentId }) }),
                projections
            }, consistency, []);
            result.Add(new(identity, new(InventoryCustody.ReceiptHeld, receipt.WarehouseId, null, "", receipt.CompuTechReceiptId, receipt.Id),
                quantity, 0, identity.IsComplete, valid, [], projections, [], receipts, apps, watermark,
                group.Any(x => x.AcknowledgedAt > asOf || x.Placements.Any(p => p.PlacedAt > asOf)),
                group.Select(x => new InventoryEvidenceReference("ReceiptCustodyAcknowledgment", x.Id.ToString())).ToImmutableArray()));
        }
        return new(result.ToImmutable(), acks.Count + origins.Count + applications.Count);
    }
}
