using System.Collections.Immutable;
using System.Text.Json;
using CropQc.Data.Entities;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

public sealed partial class InventoryEvidenceLoader
{
    private async Task<InventoryEvidenceBatch> IncludeRecordedCustodyAsync(InventoryEvidenceBatch original,
        DateTimeOffset asOf, string consistency, CancellationToken ct, RoomInventoryLedgerQueryService ledger)
    {
        var requested = original.Positions.Where(p => p.AuthoritativeQuantity > 0 && p.Ledger.Any(l => l.Quantity > 0
            && l.Kind is "TransferIn" or "ReceiptAdminOverride"
            && !p.Movements.Any(m => m.Incoming && InventoryCohortEvidence.Related(l, m)))).ToArray();
        if (requested.Length == 0) return original;
        var requestedPositions = requested.Select(p => (p.Location.RoomId, p.Identity.Key)).ToHashSet();
        var keys = requested.Select(p => p.Identity.Key).ToHashSet(StringComparer.Ordinal);
        var lots = requested.Select(p => N(p.Identity.Lot)).Distinct().ToArray();
        // Include depleted source rooms and earlier hops. Room/facility names
        // never select a special proof path. Every query retains the row bound.
        var related = await Bounded(db.RoomInventoryAdjustments.AsNoTracking()
            .Where(l => lots.Contains(l.LotNumber.Trim().ToUpper()) && l.CreatedAt <= asOf), ct);
        var rooms = related.Select(l => l.RoomId).Distinct().ToImmutableArray();
        var closure = await LoadRoomsAsync(new(null, rooms), asOf, consistency, ct,
            includeRecordedCustody: false, historyKeys: keys, historyLots: lots, historicalLedger: ledger);
        var positions = closure.Positions.Where(p => keys.Contains(p.Identity.Key)).ToArray();
        var rowIds = positions.SelectMany(p => p.Ledger).Select(l => l.Id).ToHashSet();
        var rows = related.Where(l => rowIds.Contains(l.Id)).ToArray();
        var transferIds = rows.Where(l => l.RoomTransferId != null).Select(l => l.RoomTransferId!.Value).Distinct().ToArray();
        var revisionIds = rows.Where(l => l.ReceiptInventoryOverrideId != null).Select(l => l.ReceiptInventoryOverrideId!.Value).Distinct().ToArray();
        var transfers = await Bounded(db.RoomTransfers.AsNoTracking().Where(t => transferIds.Contains(t.Id) && t.CreatedAt <= asOf), ct);
        var revisions = await Bounded(db.ReceiptInventoryOverrides.AsNoTracking().Where(r => revisionIds.Contains(r.Id) && r.CreatedAt <= asOf), ct);
        var revisionKeys = revisions.Select(r => r.Id.ToString()).ToArray();
        var audits = await Bounded(db.AuditLogs.AsNoTracking().Where(a => revisionKeys.Contains(a.EntityKey)
            && a.EntityName == "ReceiptInventoryOverride" && a.CreatedAt <= asOf), ct);
        var locationReceiptIds = revisions.Where(r => r.ActionType == ReceiptInventoryOverrideActionTypes.LocationCorrection)
            .Select(r => r.ReceiptId).Distinct().ToArray();
        var locationMoves = locationReceiptIds.Length == 0 ? [] : await Bounded(db.TreatmentLineageMovements.AsNoTracking()
            .Where(m => locationReceiptIds.Contains(m.ReceiptId ?? -1) && m.MovementType == "ReceiptLocationCorrection"), ct);
        var ledgerParents = new Dictionary<long, string>();
        var movementParents = new Dictionary<long, string>();
        var links = new List<InventoryRecordedCustodyLink>();
        foreach (var transfer in transfers)
        {
            var pair = related.Where(l => l.RoomTransferId == transfer.Id).ToArray();
            if (pair.Length != 2 || transfer.BinCount <= 0 || transfer.ReversesRoomTransferId != null) continue;
            var debit = pair.FirstOrDefault(l => l.ChangeAmount == -transfer.BinCount && l.AdjustmentType == "TransferOut");
            var credit = pair.FirstOrDefault(l => l.ChangeAmount == transfer.BinCount && l.AdjustmentType == "TransferIn");
            if (debit == null || credit == null || debit.WarehouseId != transfer.SourceWarehouseId || debit.RoomId != transfer.SourceRoomId
                || credit.WarehouseId != transfer.DestinationWarehouseId || credit.RoomId != transfer.DestinationRoomId
                || pair.Any(l => l.CropYear != transfer.CropYear || l.GrowerLotId != transfer.GrowerLotId
                    || l.FruitProfileId != transfer.FruitProfileId || N(l.LotNumber) != N(transfer.LotNumber)
                    || l.AdjustmentAt != transfer.TransferredAt || l.CreatedAt < transfer.CreatedAt)) continue;
            links.Add(new(debit.Id, credit.Id, transfer.BinCount, new("RoomTransfer", transfer.Id.ToString())));
        }
        foreach (var revision in revisions)
        {
            if (!revision.IsComplete || revision.ActionType is not (ReceiptInventoryOverrideActionTypes.InventoryReclassification
                    or ReceiptInventoryOverrideActionTypes.LocationCorrection)
                || revision.InventoryDelta != 0 || revision.NewReceiptBinCount != revision.OldReceiptBinCount
                || revision.ExpectedAdjustmentCount != 2 || string.IsNullOrWhiteSpace(revision.OperationKey)
                || revisions.Count(r => r.OperationKey == revision.OperationKey) != 1) continue;
            var pair = related.Where(l => l.ReceiptInventoryOverrideId == revision.Id).ToArray();
            if (pair.Length != 2 || pair.Any(l => l.ReceiptId != revision.ReceiptId || l.AdjustmentType != "ReceiptAdminOverride")) continue;
            var debit = pair.FirstOrDefault(l => l.ChangeAmount < 0);
            var credit = pair.FirstOrDefault(l => l.ChangeAmount > 0);
            if (debit == null || credit == null || debit.ChangeAmount != -credit.ChangeAmount
                || debit.CropYear != credit.CropYear || debit.GrowerLotId != credit.GrowerLotId
                || debit.FruitProfileId != credit.FruitProfileId || N(debit.LotNumber) != N(credit.LotNumber)
                || credit.ChangeAmount != revision.OldReceiptBinCount || pair.Any(l => l.CreatedAt != revision.CreatedAt)) continue;
            if (!Matches(revision.BeforeReceiptSnapshotJson, debit, revision.ReceiptId)
                || !Matches(revision.AfterReceiptSnapshotJson, credit, revision.ReceiptId)
                || !AuditedLocationChange(revision, audits)) continue;
            if (revision.ActionType == ReceiptInventoryOverrideActionTypes.LocationCorrection)
            {
                // Legacy location corrections already recorded immutable treatment
                // allocations, but used ReceiptAdminOverride ledger kinds and did
                // not have a movement FK to the revision. Prove that exact link;
                // never replace the allocation with the source room's shared pool.
                var prefix = $"receipt-location-correction:{revision.OperationKey}:";
                var moves = locationMoves.Where(m => m.OperationKey.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
                if (pair.Any(l => l.InventoryInvariantVersion != 1 || l.AdjustmentAt != revision.CreatedAt
                        || l.CreatedByUserId != revision.AdministratorUserId)
                    || debit.RoomId == credit.RoomId
                    || debit.InventoryOperationKey != $"receipt-override:{revision.OperationKey}:1"
                    || credit.InventoryOperationKey != $"receipt-override:{revision.OperationKey}:2"
                    || moves.Length == 0 || moves.Sum(m => (long)m.BinCount) != credit.ChangeAmount
                    || moves.Select(m => m.SourceSegmentId).Distinct().Count() != moves.Length
                    || moves.Any(m => m.BinCount <= 0 || m.ReceiptId != revision.ReceiptId
                        || m.SourceSegmentId == null || m.DestinationSegmentId == null
                        || m.SourceRoomId != debit.RoomId || m.DestinationRoomId != credit.RoomId
                        || m.OccurredAt != revision.CreatedAt || m.CreatedAt < revision.CreatedAt || m.CreatedAt > asOf
                        || m.CreatedByUserId != revision.AdministratorUserId || Parent(m) != null || m.ReversesTreatmentLineageMovementId != null
                        || m.OperationKey != prefix + m.SourceSegmentId
                        || !positions.Any(p => p.Location.RoomId == credit.RoomId && p.Movements.Any(e => e.Id == m.Id && e.ExactIdentity))
                        || !positions.Any(p => p.Location.RoomId == debit.RoomId && p.Movements.Any(e => e.Id == m.Id && e.ExactIdentity)))) continue;
                var parent = $"receipt-change:{revision.Id}";
                foreach (var leg in pair) ledgerParents.Add(leg.Id, parent);
                foreach (var move in moves) movementParents.Add(move.Id, parent);
                continue;
            }
            links.Add(new(debit.Id, credit.Id, credit.ChangeAmount, new("ReceiptInventoryOverride", revision.Id.ToString())));
        }
        positions = positions.Select(p => p with
        {
            Ledger = p.Ledger.Select(l => ledgerParents.TryGetValue(l.Id, out var parent) ? l with { MovementParent = parent } : l).ToImmutableArray(),
            Movements = p.Movements.Select(m => movementParents.TryGetValue(m.Id, out var parent) ? m with { Parent = parent } : m).ToImmutableArray()
        }).ToArray();
        var enriched = InventoryRecordedCustodyReplay.Enrich(positions, links).ToDictionary(p => (p.Location.RoomId, p.Identity.Key));
        return new(original.Positions.Select(p =>
        {
            // The full closure includes intermediate and modern-backed peers.
            // Attach its proof only to positions whose own ledger needs the
            // bridge, independently of which other rooms share this read scope.
            var position = (p.Location.RoomId, p.Identity.Key);
            if (!requestedPositions.Contains(position) || !enriched.TryGetValue(position, out var updated)) return p;
            // A selection must have the same fingerprint on an all-room screen
            // and in a command's narrower scope. Unrelated identities cannot
            // become dependencies merely because they share a page load.
            var peers = positions.Where(x => x.Identity.Key == p.Identity.Key).OrderBy(x => x.Location.RoomId).ToArray();
            var dependencies = peers.Select(x => new { x.Location, x.Identity.Key, x.Watermark.Fingerprint }).ToArray();
            var peerRows = peers.SelectMany(x => x.Ledger).Select(x => x.Id).ToHashSet();
            var parentLinks = links.Where(x => peerRows.Contains(x.CreditId)).OrderBy(x => x.CreditId).ToArray();
            var peerRevisionIds = rows.Where(l => peerRows.Contains(l.Id)).Select(l => l.ReceiptInventoryOverrideId).ToHashSet();
            var locationRevisions = revisions.Where(r => peerRevisionIds.Contains(r.Id)
                && r.ActionType == ReceiptInventoryOverrideActionTypes.LocationCorrection).OrderBy(r => r.Id).ToArray();
            if (updated.RecordedArrivals.IsEmpty && locationRevisions.Length == 0) return p;
            var locationKeys = locationRevisions.Select(r => r.Id.ToString()).ToHashSet();
            var locationEvidence = new
            {
                Revisions = locationRevisions,
                Audits = audits.Where(a => locationKeys.Contains(a.EntityKey)).OrderBy(a => a.Id).ToArray(),
                Movements = locationMoves.Where(m => locationRevisions.Any(r => r.ReceiptId == m.ReceiptId)).OrderBy(m => m.Id).ToArray()
            };
            return p with
            {
                Ledger = updated.Ledger,
                Movements = updated.Movements,
                RecordedArrivals = updated.RecordedArrivals,
                Applications = p.Applications.Concat(peers.SelectMany(x => x.Applications)
                    .Where(a => updated.RecordedArrivals.SelectMany(x => x.Cohorts).SelectMany(x => x.ApplicationIds).Contains(a.Id)))
                    .DistinctBy(a => a.Id).OrderBy(a => a.Id).ToImmutableArray(),
                Watermark = Watermark(new { Original = p.Watermark.Fingerprint, dependencies, parentLinks, updated.RecordedArrivals, locationEvidence }, consistency,
                    p.Watermark.Versions.Concat(peers.SelectMany(x => x.Watermark.Versions)).Distinct().ToImmutableArray())
            };
        }).ToImmutableArray(), original.RowsLoaded + related.Count + closure.RowsLoaded + transfers.Count + revisions.Count + audits.Count + locationMoves.Count);

        static bool Matches(string snapshot, RoomInventoryAdjustment row, long receiptId)
        {
            try
            {
                using var json = JsonDocument.Parse(snapshot);
                var r = json.RootElement;
                return r.GetProperty("id").GetInt64() == receiptId && r.GetProperty("warehouseId").GetInt32() == row.WarehouseId
                    && r.GetProperty("roomId").GetInt32() == row.RoomId && r.GetProperty("cropYear").GetInt32() == row.CropYear
                    && r.GetProperty("growerLotId").GetInt32() == row.GrowerLotId && r.GetProperty("fruitProfileId").GetInt32() == row.FruitProfileId
                    && N(r.GetProperty("lotCode").GetString()) == N(row.LotNumber);
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException) { return false; }
        }

        static bool AuditedLocationChange(ReceiptInventoryOverride revision, IReadOnlyList<AuditLog> audits)
        {
            var matches = audits.Where(a => a.EntityKey == revision.Id.ToString() && a.Action == revision.ActionType).ToArray();
            if (matches.Length != 1 || matches[0].CreatedAt != revision.CreatedAt || matches[0].UserId != revision.AdministratorUserId) return false;
            try
            {
                using var before = JsonDocument.Parse(revision.BeforeReceiptSnapshotJson);
                using var after = JsonDocument.Parse(revision.AfterReceiptSnapshotJson);
                using var auditBefore = JsonDocument.Parse(matches[0].BeforeValuesJson!);
                using var auditAfter = JsonDocument.Parse(matches[0].AfterValuesJson!);
                using var recordedAfter = JsonDocument.Parse(auditAfter.RootElement.GetProperty("afterReceiptSnapshotJson").GetString()!);
                var b = before.RootElement;
                var a = after.RootElement;
                string[] unchanged = ["id", "cropYear", "fruitProfileId", "growerLotId", "growerNumber", "lotCode", "receiptType", "receivedAt", "binCount"];
                return unchanged.All(f => b.GetProperty(f).ToString() == a.GetProperty(f).ToString())
                    && b.GetProperty("binCount").GetInt32() == revision.OldReceiptBinCount
                    && !b.GetProperty("isDeleted").GetBoolean() && !a.GetProperty("isDeleted").GetBoolean()
                    && a.GetProperty("concurrencyVersion").GetInt64() == b.GetProperty("concurrencyVersion").GetInt64() + 1
                    && b.GetRawText() == auditBefore.RootElement.GetRawText() && a.GetRawText() == recordedAfter.RootElement.GetRawText();
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentNullException or FormatException) { return false; }
        }
    }
}
