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
        var segmentIds = movements.SelectMany(x => new[] { x.SourceSegmentId, x.DestinationSegmentId }).Where(x => x != null).Select(x => x!.Value).Distinct().ToArray();
        var segments = await Bounded(db.TreatmentLineageSegments.AsNoTracking().Include(x => x.Applications).AsSingleQuery().Where(x => segmentIds.Contains(x.Id)), ct);
        var receiptIds = movements.Where(x => x.ReceiptId != null).Select(x => x.ReceiptId!.Value).Distinct().ToArray();
        var receipts = await Bounded(db.Receipts.AsNoTracking().Where(x => receiptIds.Contains(x.Id)), ct);
        var identities = await CanonicalIdentityMap.LoadAsync(db, receiptIds, ct);
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
                    .GroupBy(x => new { x.WarehouseId, x.RoomId })
                    .All(x => x.Sum(y => y.ChangeAmount) == 0)
                && !pm.Any(x => x.MovementType == "InterCrewReceive" && x.BinCount != reversals[x.Id].Sum(y => y.BinCount));
            valid &= InventoryPhysicalFlow.Matches(pr, pm, segmentIndex);
            foreach (var reversed in pm.Where(x => x.ReversesTreatmentLineageMovementId != null))
            {
                var original = pm.SingleOrDefault(x => x.Id == reversed.ReversesTreatmentLineageMovementId);
                var originalSide = original?.SourceRoomId != null ? original.SourceSegmentId : original?.DestinationSegmentId;
                var reversedSide = reversed.SourceRoomId != null ? reversed.SourceSegmentId : reversed.DestinationSegmentId;
                valid &= original != null && original.ReceiptId == reversed.ReceiptId && originalSide != null && reversedSide != null
                    && segmentIndex.ContainsKey(originalSide.Value) && segmentIndex.ContainsKey(reversedSide.Value)
                    && identities.Resolve(Identity(segmentIndex[originalSide.Value]), original.ReceiptId).Current.Key
                        == identities.Resolve(Identity(segmentIndex[reversedSide.Value]), reversed.ReceiptId).Current.Key
                    && reversals[original.Id].Sum(x => x.BinCount) <= original.BinCount;
            }
            // Parent ledger conservation is proven against immutable physical flows.
            // Receipt-scoped audited corrections can then split its current identities
            // without repartitioning or rewriting the original dispatch records.
            var groups = allocations.Where(x => x.Quantity > 0 && segmentIndex.ContainsKey(x.Movement.SourceSegmentId ?? -1))
                .Select(x => new
                {
                    x.Movement,
                    x.Quantity,
                    Original = Identity(segmentIndex[x.Movement.SourceSegmentId!.Value]),
                    Resolution = identities.Resolve(Identity(segmentIndex[x.Movement.SourceSegmentId!.Value]), x.Movement.ReceiptId)
                })
                .GroupBy(x => x.Resolution.Current).ToArray();
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
                var projections = group.Select(x => Projection(segmentIndex[x.Movement.SourceSegmentId!.Value], x.Original, parent.Warehouse) with
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
                var me = group.Select(x => Movement(x.Movement, x.Original, parent.Room)).ToImmutableArray();
                var receiptEvidence = receipts.Where(x => group.Any(y => y.Movement.ReceiptId == x.Id)).Select(x => Receipt(x, identity)).ToImmutableArray();
                var ae = allApplications;
                var quantity = group.Sum(x => x.Quantity);
                var correctionIds = group.SelectMany(x => x.Resolution.Corrections).Distinct().Order().ToArray();
                var correctionEvidence = identities.Corrections.Where(x => correctionIds.Contains(x.Id))
                    .Select(x => new
                    {
                        x.Id,
                        x.SourceCropYear,
                        x.SourceGrowerLotId,
                        x.SourceFruitProfileId,
                        x.TargetCropYear,
                        x.TargetGrowerLotId,
                        x.TargetFruitProfileId,
                        x.CorrectedReceiptId,
                        x.CreatedAt
                    }).ToArray();
                var le = pr.Select(x => new InventoryLedgerEvidence(x.Id, x.ChangeAmount, x.AdjustmentType, x.AdjustmentAt, x.ReceiptId, Parent(x), valid)).ToImmutableArray();
                result.Add(new(identity, new(scope.Custody, parent.Warehouse, null, "", parent.Name, parent.Id), quantity, 0,
                    identity.IsComplete && projections.All(x => x.ExactIdentity), valid
                        && me.All(x => x.ExactIdentity), le, projections, me, receiptEvidence, ae,
                    Watermark(new { parent, identity, le, projections, me, receiptEvidence, ae, correctionEvidence }, consistency,
                        [new(scope.Custody.ToString(), parent.Id.ToString(), parent.Version, parent.At)]),
                    pm.Any(x => x.CreatedAt > asOf) || pr.Any(x => x.CreatedAt > asOf) || projections.Any(x => x.UpdatedAt > asOf) || ae.Any(x => x.ReversedAt > asOf)
                        || correctionEvidence.Any(x => x.CreatedAt > asOf), correctionIds.Select(x => new InventoryEvidenceReference("InventoryIdentityCorrection", x.ToString())).ToImmutableArray()));
            }
        }
        return new(result.ToImmutable(), parents.Count + rows.Count + movements.Count + segments.Count + receipts.Count + apps.Count);
    }
}
