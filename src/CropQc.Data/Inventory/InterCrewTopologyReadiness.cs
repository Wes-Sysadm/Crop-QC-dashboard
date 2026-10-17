using System.Collections.Immutable;
using System.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

public sealed record InterCrewTopologyIssue(long TransferId, string Detail);
public sealed record InterCrewCustodyBalance(long TransferId, int Dispatched, int Acknowledged, int Placed, int Held, int Transit);
public sealed record InterCrewTopologyResult(IReadOnlyList<InterCrewTopologyIssue> Issues, IReadOnlyList<InterCrewCustodyBalance> Custody);

/// <summary>Read-only release topology. Quantities come from immutable dispatch,
/// acknowledgement and compensation events, never current projection balances.</summary>
public static class InterCrewTopologyReadiness
{
    public static async Task<InterCrewTopologyResult> VerifyAsync(CropQcDbContext db, CancellationToken ct)
    {
        // A single snapshot prevents a concurrent acknowledgement/placement from
        // appearing as a torn parent/child transition. Never own a writer transaction.
        if (db.Database.CurrentTransaction != null)
            throw new InvalidOperationException("Topology readiness requires its own read snapshot.");
        await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
        var parents = await Read(db.InterCrewTransfers.AsNoTracking().Include(x => x.SourceWarehouse).Include(x => x.SourceRoom)
            .Include(x => x.DestinationRoom).Include(x => x.ReceivingReceipt).ThenInclude(x => x!.Warehouse)
            .Include(x => x.ReceivingReceipt).ThenInclude(x => x!.VarietyLines), ct);
        var acknowledgments = await Read(db.ReceiptCustodyAcknowledgments.WithCustodyEvidence().AsNoTracking(), ct);
        // Existing historical, non-canonical transfers retain their established
        // topology contract. Durable canonical dispatch intents and any custody
        // history select the stronger contract, including before the first ack.
        var keys = parents.Select(x => x.OperationKey.EndsWith(":load", StringComparison.Ordinal) ? x.OperationKey[..^5] : x.OperationKey).ToArray();
        var commands = await Read(db.InventoryCommands.AsNoTracking().Where(x => keys.Contains(x.OperationKey)), ct);
        var issues = new List<InterCrewTopologyIssue>();
        var balances = new List<InterCrewCustodyBalance>();
        foreach (var parent in parents)
        {
            var acks = acknowledgments.Where(x => x.InterCrewTransferId == parent.Id).ToArray();
            var canonical = commands.Any(x => x.OperationKey + ":load" == parent.OperationKey);
            if (canonical && (!TruckReceiptRoutes.AllowsInterCrewDispatch(parent.SourceWarehouse?.Code, parent.DestinationCustodyGroup)
                || parent.RequiresTruckReceipt && !TruckReceiptRoutes.RequiresReceiptForGroup(parent.SourceWarehouse?.Code, parent.DestinationCustodyGroup)))
            {
                issues.Add(new(parent.Id, "Canonical dispatch route or persisted Truck Receipt policy is inconsistent."));
                continue;
            }
            // Full-load legacy receive/return uses its existing lifecycle, not the
            // partial receipt compensation lifecycle. Do not reinterpret its history.
            if (acks.Length == 0 && (!canonical || parent.Status != InterCrewTransferStatuses.InTransit))
            {
                if (!LegacyFieldsValid(parent)) issues.Add(new(parent.Id, "Historical transfer status/destination fields are inconsistent."));
                continue;
            }
            if (acks.Length == 0)
            {
                // Reuse the existing full-load transit/return/reopen proof. Those
                // immutable transitions are not receipt-placement compensations.
                var evidence = await new InventoryEvidenceLoader(db).LoadAsync(new(null, [], InventoryCustody.InTransit, parent.Id), DateTimeOffset.UtcNow, ct);
                var failure = !LegacyFieldsValid(parent) || evidence.Positions.IsEmpty
                    || evidence.Positions.Any(x => !InventoryAvailabilityResolver.Resolve(x, new(AllowedCustody: InventoryCustody.InTransit)).IsOperable)
                    ? "Unacknowledged transfer lacks proven transit custody or has unsupported receiving metadata."
                    : await InventoryOriginGuard.ValidateAsync(db, evidence.Positions, DateTimeOffset.UtcNow, ct);
                if (failure != null) issues.Add(new(parent.Id, failure));
                else balances.Add(new(parent.Id, parent.BinsLoaded, 0, 0, 0, parent.BinsLoaded));
                continue;
            }
            var moves = await Read(db.TreatmentLineageMovements.AsNoTracking()
                .Include(x => x.SourceSegment).ThenInclude(x => x!.Applications)
                .Include(x => x.DestinationSegment).ThenInclude(x => x!.Applications)
                .Where(x => x.InterCrewTransferId == parent.Id), ct);
            var ledger = await Read(db.RoomInventoryAdjustments.AsNoTracking().Where(x => x.InterCrewTransferId == parent.Id), ct);
            var appIds = moves.SelectMany(x => x.SourceSegment?.Applications.Select(a => a.RoomTreatmentApplicationId) ?? []).Distinct().ToArray();
            var apps = (await Read(db.RoomTreatmentApplications.AsNoTracking().Where(x => appIds.Contains(x.Id)), ct))
                .Select(x => new InventoryApplicationEvidence(x.Id, x.AppliedAt, x.ReversedAt, x.ReceiptId, x.RoomId) { RecordedAt = x.CreatedAt }).ToImmutableArray();
            var roomIds = moves.Select(x => x.DestinationRoomId).Append(parent.ReceivingReceipt?.RoomId).ToArray();
            var rooms = await Read(db.Rooms.AsNoTracking().Where(x => roomIds.Contains(x.Id)), ct);
            var map = await CanonicalIdentityMap.LoadAsync(db, moves.Where(x => x.ReceiptId != null).Select(x => x.ReceiptId!.Value).ToArray(), ct);
            InventoryIdentity Identity(TreatmentLineageSegment segment, long? receiptId) => map.Resolve(CanonicalIdentityMap.Historical(segment), receiptId).Current;
            var error = Validate(parent, acks, moves, ledger, apps, rooms, Identity);
            if (error == null && parent.ReceivingReceiptId != null && parents.Count(x => x.ReceivingReceiptId == parent.ReceivingReceiptId) != 1)
                error = "Receiving receipt is claimed by more than one transfer parent.";
            if (error == null)
            {
                var reversals = acks.SelectMany(x => x.Reversals).Where(x => x.PlacementId != null).ToArray();
                var reversedSegments = reversals.Select(x => x.Movement!.SourceSegmentId).ToArray();
                var outbound = await Read(db.TreatmentLineageMovements.AsNoTracking().Where(x => x.SourceRoomId != null
                    && reversedSegments.Contains(x.SourceSegmentId)), ct);
                foreach (var reversal in reversals)
                    if (outbound.Any(x => x.SourceSegmentId == reversal.Movement!.SourceSegmentId && x.CreatedAt <= reversal.ReversedAt
                        && !reversals.Any(r => r.MovementId == x.Id))
                        || apps.Any(x => x.ReversedAt <= reversal.ReversedAt
                            && InventoryEventReplay.ApplicationIds(reversal.Movement!.TreatmentSignatureSnapshot).Contains(x.Id)))
                        error = $"Placement reversal {reversal.Id} had an earlier movement/treatment dependency; untouched custody is not proven.";
            }
            if (error == null)
            {
                var original = moves.First(x => x.MovementType == "InterCrewDispatch");
                var identity = Identity(original.SourceSegment!, original.ReceiptId);
                var evidence = new InventoryPositionEvidence(identity, new(InventoryCustody.InTransit, parent.SourceWarehouseId, null, "", "", parent.Id),
                    parent.BinsLoaded, 0, true, true,
                    ledger.Select(x => new InventoryLedgerEvidence(x.Id, x.ChangeAmount, x.AdjustmentType, x.AdjustmentAt, x.ReceiptId, $"crew:{parent.Id}", true, x.CreatedAt)).ToImmutableArray(),
                    [], [], [], [], new("", "RepeatableRead", []));
                error = await InventoryOriginGuard.ValidateAsync(db, [evidence], DateTimeOffset.UtcNow, ct, includeEmptyHistory: true);
            }
            if (error != null) issues.Add(new(parent.Id, error));
            else balances.Add(new(parent.Id, parent.BinsLoaded, acks.Sum(x => x.NetQuantity), acks.Sum(x => x.PlacedQuantity),
                acks.Sum(x => x.HeldQuantity), parent.BinsLoaded - acks.Sum(x => x.NetQuantity)));
        }
        foreach (var ack in acknowledgments.Where(x => parents.All(p => p.Id != x.InterCrewTransferId)))
            issues.Add(new(ack.InterCrewTransferId, $"Acknowledgement {ack.Id} has no transfer parent."));
        return new(issues, balances);
    }

    private static bool LegacyFieldsValid(InterCrewTransfer parent) => parent.BinsLoaded > 0 && parent.Status switch
    {
        InterCrewTransferStatuses.InTransit => parent.DestinationWarehouseId == null && parent.DestinationRoomId == null && parent.BinsReceived == null,
        InterCrewTransferStatuses.Received or InterCrewTransferStatuses.ReceivedNeedsReview => parent.DestinationWarehouseId != null && parent.DestinationRoomId != null && parent.BinsReceived != null,
        InterCrewTransferStatuses.Reversed => true,
        _ => false
    };

    internal static string? Validate(InterCrewTransfer parent, IReadOnlyList<ReceiptCustodyAcknowledgment> acks,
        IReadOnlyList<TreatmentLineageMovement> moves, IReadOnlyList<RoomInventoryAdjustment> ledger,
        ImmutableArray<InventoryApplicationEvidence> apps, IReadOnlyList<Room> rooms, Func<TreatmentLineageSegment, long?, InventoryIdentity> resolveIdentity)
    {
        InventoryIdentity Identity(TreatmentLineageMovement move) => resolveIdentity(move.SourceSegment!, move.ReceiptId);
        var dispatch = moves.Where(x => x.MovementType == "InterCrewDispatch" && x.ReversesTreatmentLineageMovementId == null).ToArray();
        var returns = moves.Where(x => dispatch.Any(d => d.Id == x.ReversesTreatmentLineageMovementId)).ToArray();
        int Remaining(TreatmentLineageMovement movement) => movement.BinCount - returns.Where(x => x.ReversesTreatmentLineageMovementId == movement.Id).Sum(x => x.BinCount);
        var placements = acks.SelectMany(x => x.Placements).ToArray();
        var reversals = acks.SelectMany(x => x.Reversals).ToArray();
        var segments = moves.SelectMany(x => new[] { x.SourceSegment, x.DestinationSegment }).Where(x => x != null)
            .DistinctBy(x => x!.Id).ToDictionary(x => x!.Id, x => x!);
        var receipt = parent.ReceivingReceipt;
        if (parent.BinsLoaded <= 0 || parent.SourceRoom?.WarehouseId != parent.SourceWarehouseId || parent.SourceWarehouse == null
            || !TruckReceiptRoutes.RequiresReceiptForGroup(parent.SourceWarehouse.Code, parent.DestinationCustodyGroup)
            || dispatch.Length == 0 || dispatch.Sum(Remaining) != parent.BinsLoaded || dispatch.Any(x => Remaining(x) < 0)
            || dispatch.Any(x => x.SourceRoomId != parent.SourceRoomId || x.SourceSegment?.WarehouseId != parent.SourceWarehouseId
                || x.SourceSegment.RoomId != parent.SourceRoomId || x.DestinationRoomId != null || x.DestinationSegmentId != null
                || x.CreatedAt < parent.CreatedAt
                || InventoryEvidenceLoader.ReadCustodyTreatment(x, apps) == null))
            return "Original dispatch parent, allocation or immutable treatment membership is not proven.";
        foreach (var returned in returns)
        {
            var original = dispatch.Single(x => x.Id == returned.ReversesTreatmentLineageMovementId);
            var treatment = InventoryEvidenceLoader.ReadCustodyTreatment(original,
                apps.Select(a => a with { ReversedAt = a.ReversedAt <= returned.CreatedAt ? a.ReversedAt : null }).ToImmutableArray());
            if (returned.MovementType != "InterCrewReversal" || returned.SourceRoomId != null
                || returned.SourceSegmentId != original.SourceSegmentId || returned.DestinationRoomId != parent.SourceRoomId
                || returned.DestinationSegment?.WarehouseId != parent.SourceWarehouseId
                || returned.ReceiptId != original.ReceiptId || returned.DestinationSegment.ReceiptId != original.ReceiptId
                || resolveIdentity(returned.DestinationSegment, returned.ReceiptId).Key != Identity(original).Key
                || returned.CreatedAt < original.CreatedAt || acks.Any(x => x.AcknowledgedAt <= returned.CreatedAt)
                || treatment == null || returned.TreatmentSignatureSnapshot != treatment.Signature || returned.TreatmentStateSnapshot != treatment.State
                || returned.DestinationSegment.TreatmentSignature != returned.TreatmentSignatureSnapshot
                || returned.DestinationSegment.TreatmentState != returned.TreatmentStateSnapshot
                || !returned.DestinationSegment.Applications.Select(x => x.RoomTreatmentApplicationId).Order().SequenceEqual(treatment.ApplicationIds.Order()))
                return $"Return {returned.Id} lacks an eligible original allocation, source-room restoration or treatment membership.";
        }
        if (moves.Any(x => x.InterCrewTransferId != parent.Id || x.RoomTransferId != null || x.OutsideWarehouseTransferId != null
                || x.ProcessorShipmentLineId != null || x.RoomInventoryLossId != null || x.BinsRunEntryId != null || x.InventoryIdentityCorrectionId != null)
            || ledger.Any(x => x.InterCrewTransferId != parent.Id)
            || !InventoryPhysicalFlow.Matches(ledger, moves, segments)
            || ledger.Where(x => x.RoomId == parent.SourceRoomId && x.WarehouseId == parent.SourceWarehouseId).Sum(x => x.ChangeAmount) != -parent.BinsLoaded
            || ledger.Sum(x => x.ChangeAmount) != -parent.BinsLoaded + acks.Sum(x => x.PlacedQuantity))
            return "Parent ledger and immutable physical movements disagree.";
        if (moves.Any(x => x.MovementType != "InterCrewDispatch" && x.MovementType != "InterCrewReceive" && x.MovementType != "ReceiptPlacementReversal" && !returns.Contains(x))
            || moves.Any(x => x.MovementType == "InterCrewReceive" && placements.Count(p => p.MovementId == x.Id) != 1)
            || moves.Any(x => x.MovementType == "ReceiptPlacementReversal" && reversals.Count(r => r.MovementId == x.Id) != 1)
            || moves.Any(x => x.MovementType == "InterCrewDispatch" && x.ReversesTreatmentLineageMovementId != null))
            return "Unsupported or unlinked custody transition; returns require their separately proven full-load lifecycle.";
        if (!parent.RequiresTruckReceipt || receipt == null || receipt.Id != parent.ReceivingReceiptId || !receipt.IsTransferReceipt || receipt.IsDeleted
            || receipt.ReceiptType != "Truck receipt" || string.IsNullOrWhiteSpace(receipt.CompuTechReceiptId)
            || TruckReceiptRoutes.Group(receipt.Warehouse?.Code) != parent.DestinationCustodyGroup
            || !rooms.Any(x => x.Id == receipt.RoomId && x.WarehouseId == receipt.WarehouseId)
            || receipt.VarietyLines.Count == 0 || receipt.VarietyLines.Any(x => x.BinCount <= 0)
            || receipt.VarietyLines.Sum(x => x.BinCount) != receipt.BinCount || dispatch.Any(x => Identity(x).CropYear != receipt.CropYear))
            return "Matched receipt, company route or canonical variety identity is contradictory.";
        if (acks.Select(x => x.Id).Distinct().Count() != acks.Count
            || acks.GroupBy(x => (x.OperationKey, x.DispatchMovementId)).Any(g => g.Count() != 1)
            || placements.Select(x => x.MovementId).Distinct().Count() != placements.Length
            || placements.Select(x => x.InventoryAdjustmentId).Distinct().Count() != placements.Length
            || placements.GroupBy(x => (x.OperationKey, x.AcknowledgmentId)).Any(g => g.Count() != 1)
            || reversals.Where(x => x.MovementId != null).Select(x => x.MovementId).Distinct().Count() != reversals.Count(x => x.MovementId != null)
            || reversals.Where(x => x.InventoryAdjustmentId != null).Select(x => x.InventoryAdjustmentId).Distinct().Count() != reversals.Count(x => x.InventoryAdjustmentId != null)
            || reversals.GroupBy(x => (x.OperationKey, x.AcknowledgmentId, x.PlacementId)).Any(g => g.Count() != 1))
            return "Duplicated custody acknowledgement, placement or compensation evidence.";
        foreach (var ack in acks)
        {
            var original = dispatch.SingleOrDefault(x => x.Id == ack.DispatchMovementId);
            if (original == null || ack.InterCrewTransferId != parent.Id || ack.ReceiptId != receipt.Id || ack.ActorId <= 0
                || string.IsNullOrWhiteSpace(ack.OperationKey) || ack.AcknowledgedAt < original.CreatedAt || !ReceiptCustodyProof.Valid(ack))
                return $"Acknowledgement {ack.Id} lacks its exact dispatch/receipt or eligible compensation.";
            foreach (var p in ack.Placements)
            {
                var move = moves.SingleOrDefault(x => x.Id == p.MovementId);
                var row = ledger.SingleOrDefault(x => x.Id == p.InventoryAdjustmentId);
                var treatment = InventoryEvidenceLoader.ReadCustodyTreatment(original,
                    apps.Select(a => a with { ReversedAt = a.ReversedAt <= p.PlacedAt ? a.ReversedAt : null }).ToImmutableArray());
                if (move == null || row == null || p.AcknowledgmentId != ack.Id || p.PlacedAt < ack.AcknowledgedAt
                    || move.MovementType != "InterCrewReceive" || move.ReversesTreatmentLineageMovementId != null
                    || move.SourceSegmentId != original.SourceSegmentId || move.SourceRoomId != null
                    || move.DestinationSegment == null || move.DestinationRoomId == parent.SourceRoomId
                    || resolveIdentity(move.DestinationSegment, move.ReceiptId).Key != Identity(original).Key
                    || move.DestinationSegment.WarehouseId != receipt.WarehouseId
                    || !rooms.Any(x => x.Id == move.DestinationRoomId && x.WarehouseId == receipt.WarehouseId)
                    || move.ReceiptId != original.ReceiptId || move.DestinationSegment.ReceiptId != original.ReceiptId
                    || move.BinCount != p.Quantity || row.ChangeAmount != p.Quantity || row.RoomId != move.DestinationRoomId
                    || row.WarehouseId != receipt.WarehouseId || row.AdjustmentType != InterCrewTransferAdjustmentTypes.Receive
                    || row.CreatedAt != p.PlacedAt || move.CreatedAt != p.PlacedAt
                    || treatment == null || move.TreatmentSignatureSnapshot != treatment.Signature || move.TreatmentStateSnapshot != treatment.State
                    || move.DestinationSegment.TreatmentSignature != move.TreatmentSignatureSnapshot
                    || move.DestinationSegment.TreatmentState != move.TreatmentStateSnapshot
                    || !move.DestinationSegment.Applications.Select(a => a.RoomTreatmentApplicationId).Order().SequenceEqual(treatment.ApplicationIds.Order()))
                    return $"Placement {p.Id} lacks its exact acknowledged allocation, room credit or point-in-time treatment membership.";
            }
            // Check committed chronology as well as final balances: a later
            // compensation must not conceal an earlier over-placement/over-reversal.
            foreach (var at in ack.Placements.Select(x => x.PlacedAt).Concat(ack.Reversals.Select(x => x.ReversedAt)).Distinct())
            {
                var net = ack.Quantity - ack.Reversals.Where(x => x.PlacementId == null && x.ReversedAt <= at).Sum(x => x.Quantity);
                var placed = ack.Placements.Where(x => x.PlacedAt <= at).Sum(x => x.Quantity)
                    - ack.Reversals.Where(x => x.PlacementId != null && x.ReversedAt <= at).Sum(x => x.Quantity);
                if (at < ack.AcknowledgedAt || net < 0 || placed < 0 || placed > net
                    || ack.Reversals.Any(x => x.PlacementId != null && x.ReversedAt < ack.Placements.Single(p => p.Id == x.PlacementId).PlacedAt))
                    return $"Acknowledgement {ack.Id} violates committed custody chronology.";
            }
        }
        foreach (var d in dispatch)
        {
            var allocations = acks.Where(x => x.DispatchMovementId == d.Id).ToArray();
            foreach (var at in allocations.Select(x => x.AcknowledgedAt).Distinct())
                if (allocations.Where(x => x.AcknowledgedAt <= at).Sum(x => x.Quantity
                    - x.Reversals.Where(r => r.PlacementId == null && r.ReversedAt <= at).Sum(r => r.Quantity)) > Remaining(d))
                    return $"Dispatch {d.Id} was over-acknowledged.";
        }
        if (receipt.VarietyLines.Any(v => v.BinCount > dispatch.Where(x => Identity(x).FruitProfileId == v.FruitProfileId).Sum(Remaining))
            || acks.GroupBy(x => Identity(dispatch.Single(d => d.Id == x.DispatchMovementId)).FruitProfileId)
                .Any(g => g.Sum(x => x.NetQuantity) > receipt.VarietyLines.Where(v => v.FruitProfileId == g.Key).Sum(v => v.BinCount)))
            return "Acknowledged variety quantities exceed recorded dispatch or observed receipt quantities.";
        var acknowledged = acks.Sum(x => x.NetQuantity);
        var placedTotal = acks.Sum(x => x.PlacedQuantity);
        if ((parent.BinsReceived ?? 0) != acknowledged || acknowledged > parent.BinsLoaded || placedTotal > acknowledged
            || (parent.DestinationWarehouseId == null) != (parent.DestinationRoomId == null)
            || (placements.Length > 0 && (parent.DestinationWarehouseId != receipt.WarehouseId
                || !placements.Any(p => p.Movement.DestinationRoomId == parent.DestinationRoomId)))
            || (placements.Length == 0 && parent.DestinationRoomId != null))
            return "Parent receiving quantities or destination do not match committed custody.";
        var complete = acknowledged == parent.BinsLoaded && placedTotal == parent.BinsLoaded;
        if (complete)
            return parent.Status == InterCrewTransferStatuses.Received && receipt.BinCount == parent.BinsLoaded
                && parent.ReceivedAt != null && parent.ReceivedByUserId > 0 && !string.IsNullOrWhiteSpace(parent.ReceiveOperationKey)
                && receipt.TransferCompletedAt == parent.ReceivedAt ? null : "Completed custody lacks exact receipt/status completion evidence.";
        return parent.Status == InterCrewTransferStatuses.InTransit && parent.ReceivedAt == null && parent.ReceivedByUserId == null
            && parent.ReceiveOperationKey == null && receipt.TransferCompletedAt == null
            ? null : "Partial custody is falsely marked complete.";
    }

    private static async Task<List<T>> Read<T>(IQueryable<T> query, CancellationToken ct)
    {
        var rows = await query.Take(InventoryEvidenceLoader.MaximumEvidenceRowsPerTable + 1).ToListAsync(ct);
        return rows.Count <= InventoryEvidenceLoader.MaximumEvidenceRowsPerTable ? rows
            : throw new InvalidOperationException("Topology evidence exceeds the safe row bound.");
    }
}
