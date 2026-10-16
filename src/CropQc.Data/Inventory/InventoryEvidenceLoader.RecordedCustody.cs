using System.Collections.Immutable;
using System.Text.Json;
using CropQc.Data.Entities;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

public sealed partial class InventoryEvidenceLoader
{
    private async Task<InventoryEvidenceBatch> IncludeRecordedCustodyAsync(InventoryEvidenceBatch original,
        DateTimeOffset asOf, string consistency, CancellationToken ct)
    {
        var requested = original.Positions.Where(p => p.AuthoritativeQuantity > 0 && p.Ledger.Any(l => l.Quantity > 0
            && l.Kind is "TransferIn" or "ReceiptAdminOverride"
            && !p.Movements.Any(m => m.Incoming && InventoryCohortEvidence.Related(l, m)))).ToArray();
        if (requested.Length == 0) return original;
        var keys = requested.Select(p => p.Identity.Key).ToHashSet(StringComparer.Ordinal);
        var lots = requested.Select(p => N(p.Identity.Lot)).Distinct().ToArray();
        // Include depleted source rooms and earlier hops. Room/facility names
        // never select a special proof path. Every query retains the row bound.
        var related = await Bounded(db.RoomInventoryAdjustments.AsNoTracking()
            .Where(l => lots.Contains(l.LotNumber.Trim().ToUpper()) && l.CreatedAt <= asOf), ct);
        var rooms = related.Select(l => l.RoomId).Distinct().ToImmutableArray();
        var closure = await LoadRoomsAsync(new(null, rooms), asOf, consistency, ct, includeRecordedCustody: false);
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
            if (!revision.IsComplete || revision.ActionType != ReceiptInventoryOverrideActionTypes.InventoryReclassification
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
            links.Add(new(debit.Id, credit.Id, credit.ChangeAmount, new("ReceiptInventoryOverride", revision.Id.ToString())));
        }
        var enriched = InventoryRecordedCustodyReplay.Enrich(positions, links).ToDictionary(p => (p.Location.RoomId, p.Identity.Key));
        return new(original.Positions.Select(p =>
        {
            if (!enriched.TryGetValue((p.Location.RoomId, p.Identity.Key), out var updated) || updated.RecordedArrivals.IsEmpty) return p;
            // A selection must have the same fingerprint on an all-room screen
            // and in a command's narrower scope. Unrelated identities cannot
            // become dependencies merely because they share a page load.
            var peers = positions.Where(x => x.Identity.Key == p.Identity.Key).OrderBy(x => x.Location.RoomId).ToArray();
            var dependencies = peers.Select(x => new { x.Location, x.Identity.Key, x.Watermark.Fingerprint }).ToArray();
            var peerRows = peers.SelectMany(x => x.Ledger).Select(x => x.Id).ToHashSet();
            var parentLinks = links.Where(x => peerRows.Contains(x.CreditId)).OrderBy(x => x.CreditId).ToArray();
            return p with
            {
                RecordedArrivals = updated.RecordedArrivals,
                Applications = p.Applications.Concat(peers.SelectMany(x => x.Applications)
                    .Where(a => updated.RecordedArrivals.SelectMany(x => x.Cohorts).SelectMany(x => x.ApplicationIds).Contains(a.Id)))
                    .DistinctBy(a => a.Id).OrderBy(a => a.Id).ToImmutableArray(),
                Watermark = Watermark(new { Original = p.Watermark.Fingerprint, dependencies, parentLinks, updated.RecordedArrivals }, consistency,
                    p.Watermark.Versions.Concat(peers.SelectMany(x => x.Watermark.Versions)).Distinct().ToImmutableArray())
            };
        }).ToImmutableArray(), original.RowsLoaded + related.Count + closure.RowsLoaded + transfers.Count + revisions.Count + audits.Count);

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
