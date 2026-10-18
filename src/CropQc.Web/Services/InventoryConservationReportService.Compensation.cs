using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CropQc.Data.Entities;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public sealed partial class InventoryConservationReportService
{
    // Report-only proof. Do not add this type to IsKnownMovement: that predicate
    // also grants historical receipt-edit eligibility without this proof.
    private async Task<IReadOnlyList<InvalidReceiptCompensation>> ValidateReceiptCompensationsAsync(
        IReadOnlyList<RoomInventoryAdjustment> rows, CancellationToken ct)
    {
        var reversals = await db.ReceiptCustodyReversals.AsNoTracking().ToArrayAsync(ct);
        var candidates = reversals.Where(r => r.PlacementId != null || r.InventoryAdjustmentId != null || r.MovementId != null).ToArray();
        var orphanMoves = await db.TreatmentLineageMovements.AsNoTracking().Where(m => m.MovementType == "ReceiptPlacementReversal").ToArrayAsync(ct);
        if (candidates.Length == 0 && orphanMoves.Length == 0 && !rows.Any(r => r.AdjustmentType == "ReceiptPlacementReversal")) return [];
        var acks = await db.ReceiptCustodyAcknowledgments.AsNoTracking().ToArrayAsync(ct);
        var placements = await db.ReceiptCustodyPlacements.AsNoTracking().ToArrayAsync(ct);
        var transferIds = acks.Where(a => reversals.Any(r => r.AcknowledgmentId == a.Id)).Select(a => a.InterCrewTransferId).Distinct().ToArray();
        var transfers = await db.InterCrewTransfers.AsNoTracking().Include(t => t.ReceivingReceipt)
            .Where(t => transferIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
        var moveIds = acks.Select(a => a.DispatchMovementId).Concat(placements.Select(p => p.MovementId))
            .Concat(candidates.Where(r => r.MovementId != null).Select(r => r.MovementId!.Value)).ToArray();
        var moves = await db.TreatmentLineageMovements.AsNoTracking().Include(m => m.SourceSegment).ThenInclude(s => s!.Applications)
            .Include(m => m.DestinationSegment).ThenInclude(s => s!.Applications)
            .Where(m => moveIds.Contains(m.Id) || (m.InterCrewTransferId != null && transferIds.Contains(m.InterCrewTransferId.Value)))
            .AsSplitQuery().ToDictionaryAsync(m => m.Id, ct);
        var apps = await db.RoomTreatmentApplications.AsNoTracking().ToDictionaryAsync(a => a.Id, ct);
        var keys = placements.Select(p => p.OperationKey).Concat(candidates.Select(r => r.OperationKey)).Distinct().ToArray();
        var commands = await db.InventoryCommands.AsNoTracking().Where(c => keys.Contains(c.OperationKey)).ToDictionaryAsync(c => c.OperationKey, ct);
        var audits = await db.AuditLogs.AsNoTracking().Where(a => a.Action == "CanonicalInventoryCommand" && keys.Contains(a.EntityKey!)).ToArrayAsync(ct);
        var rowMap = rows.ToDictionary(r => r.Id);
        var ackMap = acks.ToDictionary(a => a.Id);
        var placementMap = placements.ToDictionary(p => p.Id);
        var result = new List<InvalidReceiptCompensation>();
        foreach (var r in candidates)
            if (!Valid(r)) result.Add(new(r.Id, r.InventoryAdjustmentId is { } l ? [l] : [], r.MovementId is { } m ? [m] : []));
        foreach (var row in rows.Where(l => l.AdjustmentType == "ReceiptPlacementReversal" && candidates.Count(r => r.InventoryAdjustmentId == l.Id) != 1))
            result.Add(new(null, [row.Id], []));
        foreach (var move in orphanMoves.Where(m => candidates.Count(r => r.MovementId == m.Id) != 1))
            result.Add(new(null, [], [move.Id]));
        return result;

        bool Valid(ReceiptCustodyReversal r)
        {
            if (r.PlacementId is not { } pid || !placementMap.TryGetValue(pid, out var p)
                || !ackMap.TryGetValue(r.AcknowledgmentId, out var a) || p.AcknowledgmentId != a.Id
                || !transfers.TryGetValue(a.InterCrewTransferId, out var t) || t.ReceivingReceiptId != a.ReceiptId
                || t.ReceivingReceipt is not { IsTransferReceipt: true, ReceiptType: "Truck receipt" }
                || r.InventoryAdjustmentId is not { } lid || !rowMap.TryGetValue(lid, out var l)
                || r.MovementId is not { } mid || !moves.TryGetValue(mid, out var m)
                || !rowMap.TryGetValue(p.InventoryAdjustmentId, out var credit) || !moves.TryGetValue(p.MovementId, out var original)
                || !moves.TryGetValue(a.DispatchMovementId, out var dispatch)
                || dispatch.SourceSegment is not { } source || original.DestinationSegment is not { } target) return false;
            var siblings = reversals.Where(x => x.PlacementId == p.Id).ToArray();
            if (r.Quantity <= 0 || p.Quantity <= 0 || a.Quantity <= 0 || r.ActorId <= 0 || string.IsNullOrWhiteSpace(r.Reason)
                || r.ReversedAt < p.PlacedAt || p.PlacedAt < a.AcknowledgedAt || a.AcknowledgedAt < dispatch.CreatedAt
                || siblings.Sum(x => (long)x.Quantity) > p.Quantity || siblings.Any(x => x.Quantity <= 0)
                || candidates.Count(x => x.InventoryAdjustmentId == lid) != 1 || candidates.Count(x => x.MovementId == mid) != 1
                || siblings.Count(x => x.OperationKey == r.OperationKey) != 1
                || placements.Count(x => x.InventoryAdjustmentId == credit.Id) != 1 || placements.Count(x => x.MovementId == original.Id) != 1
                || placements.Count(x => x.OperationKey == p.OperationKey && x.AcknowledgmentId == a.Id) != 1
                || dispatch.MovementType != "InterCrewDispatch" || dispatch.InterCrewTransferId != t.Id
                || dispatch.SourceRoomId != t.SourceRoomId || source.RoomId != t.SourceRoomId || source.WarehouseId != t.SourceWarehouseId
                || dispatch.DestinationRoomId != null || dispatch.DestinationSegmentId != null || dispatch.ReversesTreatmentLineageMovementId != null
                || original.MovementType != "InterCrewReceive" || original.InterCrewTransferId != t.Id
                || original.SourceSegmentId != source.Id || original.SourceRoomId != null || original.ReversesTreatmentLineageMovementId != null
                || original.DestinationRoomId != target.RoomId || original.BinCount != p.Quantity
                || credit.ChangeAmount != p.Quantity || credit.AdjustmentType != "InterCrewTransferReceive" || credit.InterCrewTransferId != t.Id
                || credit.CreatedAt != p.PlacedAt || original.CreatedAt != p.PlacedAt || credit.CreatedByUserId != original.CreatedByUserId
                || credit.InventoryOperationKey != p.OperationKey + ":" + a.Id || original.OperationKey != credit.InventoryOperationKey + ":s" + source.Id
                || target.WarehouseId != t.ReceivingReceipt.WarehouseId || credit.WarehouseId != target.WarehouseId || credit.RoomId != target.RoomId
                || l.AdjustmentType != "ReceiptPlacementReversal" || l.ChangeAmount != -r.Quantity || l.InterCrewTransferId != t.Id
                || l.InventoryOperationKey != r.OperationKey + ":" + p.Id || l.CreatedAt != r.ReversedAt || l.CreatedByUserId != r.ActorId
                || l.Reason?.Trim() != r.Reason || l.WarehouseId != credit.WarehouseId || l.RoomId != credit.RoomId
                || l.CropYear != credit.CropYear || l.GrowerLotId != credit.GrowerLotId || l.FruitProfileId != credit.FruitProfileId
                || l.LotNumber != credit.LotNumber || l.VarietyCode != credit.VarietyCode || l.InventoryStatus != credit.InventoryStatus
                || l.InventoryInvariantVersion != 3 || credit.InventoryInvariantVersion != 3
                || l.OldBinCount == null || (long)l.NewBinCount != l.OldBinCount + (long)l.ChangeAmount
                || credit.OldBinCount == null || (long)credit.NewBinCount != credit.OldBinCount + (long)credit.ChangeAmount
                || m.MovementType != "ReceiptPlacementReversal" || m.InterCrewTransferId != t.Id || m.BinCount != r.Quantity
                || m.ReversesTreatmentLineageMovementId != original.Id || m.SourceSegmentId != target.Id || m.SourceRoomId != target.RoomId
                || m.DestinationRoomId != null || m.DestinationSegmentId != null || m.CreatedAt != r.ReversedAt || m.CreatedByUserId != r.ActorId
                || m.OperationKey != l.InventoryOperationKey + ":s" + target.Id || m.OccurredAt != l.AdjustmentAt
                || m.ReceiptId != original.ReceiptId || original.ReceiptId != dispatch.ReceiptId || target.ReceiptId != dispatch.ReceiptId || source.ReceiptId != dispatch.ReceiptId
                || m.IdentityKey != original.IdentityKey || original.IdentityKey != dispatch.IdentityKey || source.IdentityKey != dispatch.IdentityKey || target.IdentityKey != dispatch.IdentityKey
                || credit.CropYear != target.CropYear || credit.GrowerLotId != target.GrowerLotId || credit.FruitProfileId != target.FruitProfileId
                || credit.LotNumber != target.LotNumberSnapshot || credit.VarietyCode != target.VarietyCodeSnapshot
                || m.TreatmentStateSnapshot != original.TreatmentStateSnapshot || m.TreatmentSignatureSnapshot != original.TreatmentSignatureSnapshot
                || !Treatment(dispatch, original, target, p.PlacedAt, r.ReversedAt) || !Custody(t)) return false;
            return Operation(r.OperationKey, InventoryCommandKind.ReverseReceiptPlacement, r.ActorId, r.ReversedAt, a, p.Id, r.Quantity, l, m)
                && Operation(p.OperationKey, InventoryCommandKind.PlaceReceiptCustody, credit.CreatedByUserId ?? 0, p.PlacedAt, a, a.Id, p.Quantity, credit, original);
        }

        bool Treatment(TreatmentLineageMovement d, TreatmentLineageMovement p, TreatmentLineageSegment target, DateTimeOffset placedAt, DateTimeOffset reversedAt)
        {
            var source = d.SourceSegment!;
            var ids = source.Applications.Select(x => x.RoomTreatmentApplicationId).Order().ToArray();
            if (ids.Distinct().Count() != ids.Length || source.TreatmentSignature != d.TreatmentSignatureSnapshot || source.TreatmentState != d.TreatmentStateSnapshot
                || ids.Any(id => !apps.TryGetValue(id, out var app) || app.AppliedAt > d.OccurredAt || app.CreatedAt > d.CreatedAt)) return false;
            if (d.TreatmentStateSnapshot == "Untreated") { if (ids.Length != 0 || d.TreatmentSignatureSnapshot != "u") return false; }
            else if (d.TreatmentStateSnapshot != "Confirmed" || ids.Length == 0 || d.TreatmentSignatureSnapshot != "u|a:" + string.Join(',', ids)) return false;
            var active = ids.Where(id => apps[id].ReversedAt == null || apps[id].ReversedAt > placedAt).ToArray();
            return active.All(id => apps[id].ReversedAt == null || apps[id].ReversedAt > reversedAt)
                && target.Applications.Select(x => x.RoomTreatmentApplicationId).Order().SequenceEqual(active)
                && p.TreatmentSignatureSnapshot == (active.Length == 0 ? "u" : "u|a:" + string.Join(',', active))
                && p.TreatmentStateSnapshot == (active.Length == 0 ? "Untreated" : "Confirmed")
                && target.TreatmentSignature == p.TreatmentSignatureSnapshot && target.TreatmentState == p.TreatmentStateSnapshot;
        }

        bool Custody(InterCrewTransfer t)
        {
            var group = acks.Where(x => x.InterCrewTransferId == t.Id).ToArray();
            long net = 0, placed = 0;
            foreach (var a in group)
            {
                var ps = placements.Where(x => x.AcknowledgmentId == a.Id).ToArray(); var rs = reversals.Where(x => x.AcknowledgmentId == a.Id).ToArray();
                if (a.ReceiptId != t.ReceivingReceiptId || a.Quantity <= 0 || rs.Any(x => x.Quantity <= 0)
                    || ps.Any(x => x.Quantity <= 0) || rs.Any(x => x.PlacementId == null && (x.InventoryAdjustmentId != null || x.MovementId != null))) return false;
                foreach (var at in ps.Select(x => x.PlacedAt).Concat(rs.Select(x => x.ReversedAt)).Append(a.AcknowledgedAt).Distinct())
                {
                    var n = (long)a.Quantity - rs.Where(x => x.PlacementId == null && x.ReversedAt <= at).Sum(x => (long)x.Quantity);
                    var q = ps.Where(x => x.PlacedAt <= at).Sum(x => (long)x.Quantity) - rs.Where(x => x.PlacementId != null && x.ReversedAt <= at).Sum(x => (long)x.Quantity);
                    if (at < a.AcknowledgedAt || n < 0 || q < 0 || q > n) return false;
                }
                net += a.Quantity - rs.Where(x => x.PlacementId == null).Sum(x => (long)x.Quantity);
                placed += ps.Sum(x => (long)x.Quantity) - rs.Where(x => x.PlacementId != null).Sum(x => (long)x.Quantity);
            }
            foreach (var g in group.GroupBy(a => a.DispatchMovementId))
            {
                if (!moves.TryGetValue(g.Key, out var d) || d.InterCrewTransferId != t.Id
                    || g.GroupBy(a => a.OperationKey).Any(x => x.Count() != 1)) return false;
                foreach (var at in g.Select(a => a.AcknowledgedAt).Distinct())
                    if (g.Where(a => a.AcknowledgedAt <= at).Sum(a => (long)a.Quantity
                        - reversals.Where(r => r.AcknowledgmentId == a.Id && r.PlacementId == null && r.ReversedAt <= at).Sum(r => (long)r.Quantity)) > d.BinCount) return false;
            }
            return net <= t.BinsLoaded && t.BinsReceived == net && t.VarianceBins == net - t.BinsLoaded && placed <= net
                && (t.Status == "Received" ? placed == t.BinsLoaded && net == t.BinsLoaded : t.Status == "InTransit" && placed < t.BinsLoaded)
                && group.GroupBy(a => a.DispatchMovementId).All(g => moves.TryGetValue(g.Key, out var d) && d.InterCrewTransferId == t.Id
                    && g.Sum(a => (long)a.Quantity - reversals.Where(r => r.AcknowledgmentId == a.Id && r.PlacementId == null).Sum(r => (long)r.Quantity)) <= d.BinCount);
        }

        bool Operation(string key, InventoryCommandKind kind, int actor, DateTimeOffset at, ReceiptCustodyAcknowledgment ack,
            long selectedId, int quantity, RoomInventoryAdjustment row, TreatmentLineageMovement move)
        {
            try
            {
                if (!commands.TryGetValue(key, out var journal) || journal.ActorId != actor || journal.CommittedAt != at
                    || journal.IntentHash != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(journal.IntentJson)))) return false;
                var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
                var intent = JsonSerializer.Deserialize<InventoryCommand>(journal.IntentJson, options)!;
                var committed = JsonSerializer.Deserialize<InventoryCommandResult>(journal.ResultJson, options)!;
                var audit = audits.Where(a => a.EntityKey == key).ToArray();
                // Npgsql stores timestamps at microsecond precision; the immutable intent retains 100ns ticks.
                if (intent.Kind != kind || intent.OperationKey != key || intent.ActorId != actor || intent.EffectiveAt.UtcTicks / 10 != row.AdjustmentAt.UtcTicks / 10 || intent.Reason != row.Reason
                    || intent.ReceiptCustody is not { } custody || custody.TransferId != ack.InterCrewTransferId || custody.ReceiptId != ack.ReceiptId
                    || custody.Allocations.IsDefaultOrEmpty || custody.Allocations.Any(x => x.Quantity <= 0)
                    || custody.Allocations.Select(x => x.Id).Distinct().Count() != custody.Allocations.Length
                    || (kind == InventoryCommandKind.ReverseReceiptPlacement ? custody.Destination != null
                        : custody.Destination?.RoomId != row.RoomId || custody.Destination.WarehouseId != row.WarehouseId)
                    || custody.Allocations.Count(x => x.Id == selectedId && x.Quantity == quantity) != 1
                    || committed.Status != InventoryCommandStatus.Committed || committed.OperationKey != key || committed.Effects.IsDefault
                    || committed.Effects.Length != custody.Allocations.Length
                    || committed.Effects.Count(e => e.Quantity == quantity && e.ParentId == ack.InterCrewTransferId
                        && e.LedgerIds.SequenceEqual(new[] { row.Id }) && e.MovementIds.SequenceEqual(new[] { move.Id })) != 1
                    || audit.Length != 1 || audit[0].UserId != actor || audit[0].CreatedAt != at
                    || audit[0].EntityName != "CanonicalInventory" || audit[0].SourceApplication != "CanonicalInventory/v1") return false;
                using var before = JsonDocument.Parse(audit[0].BeforeValuesJson!);
                return before.RootElement.GetProperty("intent").GetString() == journal.IntentJson
                    && before.RootElement.GetProperty("hash").GetString() == journal.IntentHash
                    && JsonNode.DeepEquals(JsonNode.Parse(audit[0].AfterValuesJson!), JsonSerializer.SerializeToNode(committed.Effects, options))
                    && custody.Allocations.Length == (kind == InventoryCommandKind.PlaceReceiptCustody
                        ? placements.Count(p => p.OperationKey == key) : candidates.Count(r => r.OperationKey == key));
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or NullReferenceException) { return false; }
        }
    }
}
