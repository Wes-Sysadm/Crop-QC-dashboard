using System.Collections.Immutable;
using CropQc.Data.Entities;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

public sealed partial class InventoryEvidenceLoader
{
    private sealed record CustodyParent(long Id, string ParentKey, InventoryIdentity Identity, int Warehouse, int Room,
        string Name, int Quantity, bool Active, long Version, DateTimeOffset At);

    private async Task<InventoryEvidenceBatch> LoadCustodyAsync(InventoryScope scope, DateTimeOffset asOf, string consistency, CancellationToken ct)
    {
        var parents = new List<CustodyParent>();
        var roomIds = scope.RoomIds.IsDefaultOrEmpty ? null : scope.RoomIds.ToArray();
        if (scope.Custody == InventoryCustody.InTransit)
        {
            var loads = await Bounded(db.InterCrewTransfers.AsNoTracking().Where(x =>
                (scope.CustodyRecordId == null ? x.Status == InterCrewTransferStatuses.InTransit : x.Id == scope.CustodyRecordId)
                && (scope.WarehouseId == null || x.SourceWarehouseId == scope.WarehouseId) && (roomIds == null || roomIds.Contains(x.SourceRoomId))), ct);
            parents.AddRange(loads.Select(x => new CustodyParent(x.Id, $"crew:{x.Id}",
                new(x.CropYear, x.GrowerLotId, x.FruitProfileId, x.LotNumberSnapshot, x.GrowerNumberSnapshot, x.VarietyCodeSnapshot,
                    x.ProductionTypeSnapshot, x.IsOrganicSnapshot, x.InventoryStatusSnapshot ?? ""), x.SourceWarehouseId, x.SourceRoomId,
                x.DestinationCustodyGroup, x.BinsLoaded, x.Status == InterCrewTransferStatuses.InTransit && x.BinsReceived == null,
                x.ConcurrencyVersion, x.LoadedAt)));
        }
        else if (scope.Custody == InventoryCustody.OutsideWarehouse)
        {
            var transfers = await Bounded(db.OutsideWarehouseTransfers.AsNoTracking().Where(x =>
                (scope.CustodyRecordId == null ? !x.IsReversed : x.Id == scope.CustodyRecordId)
                && (scope.WarehouseId == null || x.SourceWarehouseId == scope.WarehouseId) && (roomIds == null || roomIds.Contains(x.SourceRoomId))), ct);
            parents.AddRange(transfers.Select(x => new CustodyParent(x.Id, $"outside:{x.Id}",
                new(x.CropYear, x.GrowerLotId, x.FruitProfileId, x.LotNumberSnapshot, x.GrowerNumberSnapshot, x.VarietyCodeSnapshot,
                    x.ProductionTypeSnapshot, x.IsOrganicSnapshot, x.InventoryStatusSnapshot ?? ""), x.SourceWarehouseId, x.SourceRoomId,
                x.OutsideWarehouseNameSnapshot, x.BinCount, !x.IsReversed, x.ConcurrencyVersion, x.TransferredAt)));
        }
        else if (scope.Custody == InventoryCustody.Processor)
        {
            var lines = await Bounded(db.ProcessorShipmentLines.AsNoTracking().Include(x => x.ProcessorShipment).Where(x =>
                (scope.CustodyRecordId == null ? x.ProcessorShipment.ReversedAt == null : x.Id == scope.CustodyRecordId)
                && (scope.WarehouseId == null || x.WarehouseId == scope.WarehouseId) && (roomIds == null || roomIds.Contains(x.RoomId))), ct);
            parents.AddRange(lines.Select(x => new CustodyParent(x.Id, $"processor:{x.Id}",
                new(x.CropYear, x.GrowerLotId, x.FruitProfileId, x.LotNumberSnapshot, x.GrowerNumberSnapshot, x.VarietyCodeSnapshot,
                    x.ProductionTypeSnapshot, x.IsOrganicSnapshot, x.InventoryStatusSnapshot ?? ""), x.WarehouseId, x.RoomId,
                x.ProcessorShipment.ProcessorNameSnapshot, x.BinsSent, x.ProcessorShipment.ReversedAt == null,
                x.ProcessorShipment.ConcurrencyVersion, x.ProcessorShipment.ShippedAt)));
        }
        else throw new ArgumentOutOfRangeException(nameof(scope));
        var parentIds = parents.Select(x => x.Id).ToArray();
        var ledgerQuery = db.RoomInventoryAdjustments.AsNoTracking();
        var movementQuery = db.TreatmentLineageMovements.AsNoTracking();
        if (scope.Custody == InventoryCustody.InTransit)
        {
            ledgerQuery = ledgerQuery.Where(x => parentIds.Contains(x.InterCrewTransferId ?? -1));
            movementQuery = movementQuery.Where(x => parentIds.Contains(x.InterCrewTransferId ?? -1));
        }
        else if (scope.Custody == InventoryCustody.OutsideWarehouse)
        {
            ledgerQuery = ledgerQuery.Where(x => parentIds.Contains(x.OutsideWarehouseTransferId ?? -1));
            movementQuery = movementQuery.Where(x => parentIds.Contains(x.OutsideWarehouseTransferId ?? -1));
        }
        else
        {
            ledgerQuery = ledgerQuery.Where(x => parentIds.Contains(x.ProcessorShipmentLineId ?? -1));
            movementQuery = movementQuery.Where(x => parentIds.Contains(x.ProcessorShipmentLineId ?? -1));
        }
        var rows = await Bounded(ledgerQuery, ct);
        var movements = await Bounded(movementQuery, ct);
        var segmentIds = movements.Where(x => x.SourceSegmentId != null).Select(x => x.SourceSegmentId!.Value).Distinct().ToArray();
        var segments = await Bounded(db.TreatmentLineageSegments.AsNoTracking().Include(x => x.Applications).AsSingleQuery().Where(x => segmentIds.Contains(x.Id)), ct);
        var receiptIds = movements.Where(x => x.ReceiptId != null).Select(x => x.ReceiptId!.Value).Distinct().ToArray();
        var receipts = await Bounded(db.Receipts.AsNoTracking().Where(x => receiptIds.Contains(x.Id)), ct);
        var appIds = segments.SelectMany(x => x.Applications).Select(x => x.RoomTreatmentApplicationId).Distinct().ToArray();
        var apps = await Bounded(db.RoomTreatmentApplications.AsNoTracking().Where(x => appIds.Contains(x.Id)), ct);
        var segmentIndex = segments.ToDictionary(x => x.Id);
        var movementsByParent = movements.ToLookup(Parent);
        var rowsByParent = rows.ToLookup(Parent);
        var reversals = movements.Where(x => x.ReversesTreatmentLineageMovementId != null).ToLookup(x => x.ReversesTreatmentLineageMovementId!.Value);
        var result = ImmutableArray.CreateBuilder<InventoryPositionEvidence>();
        foreach (var parent in parents.OrderBy(x => x.Id))
        {
            var pm = movementsByParent[parent.ParentKey].OrderBy(x => x.Id).ToArray();
            var pr = rowsByParent[parent.ParentKey].OrderBy(x => x.Id).ToArray();
            var kind = scope.Custody switch { InventoryCustody.InTransit => "InterCrewDispatch", InventoryCustody.Processor => "ProcessorShipment", _ => "OutsideWarehouseTransfer" };
            var allocations = pm.Where(x => x.MovementType == kind && x.ReversesTreatmentLineageMovementId == null)
                .Select(x => (Movement: x, Quantity: x.BinCount - reversals[x.Id].Sum(y => y.BinCount))).ToArray();
            var valid = parent.Active && parent.At <= asOf && parent.Quantity > 0 && allocations.Length > 0
                && allocations.All(x => x.Quantity >= 0 && x.Movement.SourceSegmentId != null && segmentIndex.ContainsKey(x.Movement.SourceSegmentId.Value))
                && allocations.Sum(x => x.Quantity) == parent.Quantity && pr.Sum(x => x.ChangeAmount) == -parent.Quantity
                && pr.Where(x => x.WarehouseId == parent.Warehouse && x.RoomId == parent.Room).Sum(x => x.ChangeAmount) == -parent.Quantity
                && pr.Where(x => x.WarehouseId != parent.Warehouse || x.RoomId != parent.Room)
                    .GroupBy(x => new { x.WarehouseId, x.RoomId, x.CropYear, x.GrowerLotId, x.FruitProfileId, x.LotNumber })
                    .All(x => x.Sum(y => y.ChangeAmount) == 0)
                && !pm.Any(x => x.MovementType == "InterCrewReceive" && x.BinCount != reversals[x.Id].Sum(y => y.BinCount));
            // A mixed load is resolved by its immutable dispatch slices, not its display identity.
            var groups = allocations.Where(x => x.Quantity > 0 && segmentIndex.ContainsKey(x.Movement.SourceSegmentId ?? -1))
                .GroupBy(x => Identity(segmentIndex[x.Movement.SourceSegmentId!.Value])).ToArray();
            if (groups.Length == 0)
            {
                result.Add(new(parent.Identity, new(scope.Custody, parent.Warehouse, null, "", parent.Name, parent.Id),
                    parent.Active ? parent.Quantity : 0, 0, parent.Identity.IsComplete, false, [], [], [], [], [],
                    Watermark(new { parent, pr = pr.Select(x => new { x.Id, x.ChangeAmount }) }, consistency, [])));
                continue;
            }
            foreach (var group in groups)
            {
                var identity = group.Key;
                var projections = group.Select(x => Projection(segmentIndex[x.Movement.SourceSegmentId!.Value], identity, parent.Warehouse) with
                {
                    Quantity = x.Quantity,
                    // This is an immutable custody allocation, even when its source-room projection is retired.
                    Disposition = "Current",
                    RetiredQuantity = null,
                    Signature = x.Movement.TreatmentSignatureSnapshot,
                    State = x.Movement.TreatmentStateSnapshot,
                    ReceiptId = x.Movement.ReceiptId
                }).ToImmutableArray();
                var originalAppIds = projections.SelectMany(x => x.ApplicationIds).ToHashSet();
                var allApplications = apps.Where(x => originalAppIds.Contains(x.Id)).Select(x => new InventoryApplicationEvidence(x.Id, x.AppliedAt, x.ReversedAt, x.ReceiptId)).ToImmutableArray();
                projections = projections.Select(p =>
                {
                    var effective = InventoryEffectiveTreatment.Read(p.Signature, p.State, p.ApplicationIds, allApplications);
                    return p with { Signature = effective.Signature, State = effective.State, ApplicationIds = effective.ApplicationIds };
                }).ToImmutableArray();
                var me = group.Select(x => Movement(x.Movement, identity, parent.Room)).ToImmutableArray();
                var receiptEvidence = receipts.Where(x => group.Any(y => y.Movement.ReceiptId == x.Id)).Select(x => Receipt(x, identity)).ToImmutableArray();
                var ae = allApplications;
                var quantity = group.Sum(x => x.Quantity);
                var identityLedger = pr.Where(x => x.CropYear == identity.CropYear && x.GrowerLotId == identity.GrowerLotId
                    && x.FruitProfileId == identity.FruitProfileId && N(x.LotNumber) == N(identity.Lot)).ToArray();
                var le = identityLedger.Select(x => new InventoryLedgerEvidence(x.Id, x.ChangeAmount, x.AdjustmentType, x.AdjustmentAt, x.ReceiptId, Parent(x), true)).ToImmutableArray();
                result.Add(new(identity, new(scope.Custody, parent.Warehouse, null, "", parent.Name, parent.Id), quantity, 0,
                    identity.IsComplete && projections.All(x => x.ExactIdentity), valid && identityLedger.Sum(x => x.ChangeAmount) == -quantity
                        && me.All(x => x.ExactIdentity), le, projections, me, receiptEvidence, ae,
                    Watermark(new { parent, le, projections, me, receiptEvidence, ae }, consistency,
                        [new(scope.Custody.ToString(), parent.Id.ToString(), parent.Version, parent.At)]),
                    pm.Any(x => x.CreatedAt > asOf) || pr.Any(x => x.CreatedAt > asOf) || projections.Any(x => x.UpdatedAt > asOf) || ae.Any(x => x.ReversedAt > asOf)));
            }
        }
        return new(result.ToImmutable(), parents.Count + rows.Count + movements.Count + segments.Count + receipts.Count + apps.Count);
    }
}
