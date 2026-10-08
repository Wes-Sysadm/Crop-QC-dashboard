using System.Collections.Immutable;
using CropQc.Data.Entities;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

// Maintenance evidence only. No operational resolver, treatment guard or receipt
// attribution rule is changed. Entity snapshots are detached, never rewritten.
public sealed record RecordedInventoryHistory(
    InventoryPositionEvidence[] Positions, RoomInventoryAdjustment[] Ledger,
    Receipt[] Receipts, ReceiptInventoryOverride[] Corrections, AuditLog[] Audits,
    RoomTransfer[] Transfers, BinsRunEntry[] Entries, ActualRun[] Runs, ActualRunRevision[] RunRevisions,
    string[]? AdditionalEventBlockers = null);

public sealed record RecordedHistoryAssessment(bool Proven, ImmutableArray<string> Blockers,
    ImmutableArray<InventoryEvidenceReference> Evidence, InventoryNormalizationPlan? Plan);

public static class RecordedInventoryHistoryReconstruction
{
    public static async Task<RecordedInventoryHistory> LoadAsync(CropQcDbContext db,
        InventoryPositionEvidence target, CancellationToken ct)
    {
        var positions = new List<InventoryPositionEvidence> { target };
        var transfers = new Dictionary<long, RoomTransfer>();
        for (var index = 0; index < positions.Count; index++)
        {
            if (positions.Count > 32) throw new InvalidOperationException("Recorded ancestry exceeds the bounded maintenance scope.");
            var e = positions[index];
            var ids = e.Ledger.Where(x => x.Kind is "TransferIn" or "TransferOut")
                .Select(x => x.MovementParent).Where(x => x?.StartsWith("room:", StringComparison.Ordinal) == true)
                .Select(x => long.Parse(x![5..], System.Globalization.CultureInfo.InvariantCulture)).Distinct().ToArray();
            foreach (var transfer in await db.RoomTransfers.AsNoTracking().Where(x => ids.Contains(x.Id)
                || x.CropYear == e.Identity.CropYear && x.GrowerLotId == e.Identity.GrowerLotId && x.FruitProfileId == e.Identity.FruitProfileId
                    && (x.SourceRoomId == e.Location.RoomId || x.DestinationRoomId == e.Location.RoomId)).ToArrayAsync(ct))
            {
                transfers[transfer.Id] = transfer;
                if (transfer.DestinationRoomId != e.Location.RoomId || positions.Any(x => x.Location.RoomId == transfer.SourceRoomId)) continue;
                var batch = await new InventoryEvidenceLoader(db).LoadAsync(new(transfer.SourceWarehouseId, [transfer.SourceRoomId]), DateTimeOffset.UtcNow, ct);
                var source = batch.Positions.SingleOrDefault(x => x.Identity.Key == target.Identity.Key);
                if (source != null) positions.Add(source);
            }
        }
        var rooms = positions.Select(x => x.Location.RoomId!.Value).ToArray();
        var transferIds = transfers.Keys.ToArray();
        var ledger = await db.RoomInventoryAdjustments.AsNoTracking().Where(x =>
            (rooms.Contains(x.RoomId) && x.CropYear == target.Identity.CropYear && x.GrowerLotId == target.Identity.GrowerLotId
                && x.FruitProfileId == target.Identity.FruitProfileId) || transferIds.Contains(x.RoomTransferId ?? -1)).ToArrayAsync(ct);
        var receiptIds = ledger.Where(x => x.ReceiptId != null).Select(x => x.ReceiptId!.Value).Distinct().ToArray();
        var receipts = await db.Receipts.AsNoTracking().Where(x => receiptIds.Contains(x.Id)
            || rooms.Contains(x.RoomId) && x.CropYear == target.Identity.CropYear && x.GrowerLotId == target.Identity.GrowerLotId
                && x.FruitProfileId == target.Identity.FruitProfileId).ToArrayAsync(ct);
        receiptIds = receipts.Select(x => x.Id).ToArray();
        var corrections = await db.ReceiptInventoryOverrides.AsNoTracking().Where(x => receiptIds.Contains(x.ReceiptId)).ToArrayAsync(ct);
        // Include every original arrival and correction effect, including any
        // contradictory extra effect outside the expected room.
        var correctionIds = corrections.Select(x => x.Id).ToArray();
        var extra = await db.RoomInventoryAdjustments.AsNoTracking().Where(x =>
            receiptIds.Contains(x.ReceiptId ?? -1) && x.AdjustmentType == "ReceiptAdd"
            || correctionIds.Contains(x.ReceiptInventoryOverrideId ?? Guid.Empty)).ToArrayAsync(ct);
        ledger = ledger.Concat(extra).DistinctBy(x => x.Id).OrderBy(x => x.Id).ToArray();
        var keys = corrections.Select(x => x.Id.ToString()).ToArray();
        var audits = await db.AuditLogs.AsNoTracking().Where(x => x.EntityName == "ReceiptInventoryOverride" && keys.Contains(x.EntityKey)).ToArrayAsync(ct);
        var ledgerIds = ledger.Select(x => x.Id).ToArray();
        var entries = await db.BinsRunEntries.AsNoTracking().Where(x => ledgerIds.Contains(x.InventoryAdjustmentId)
            || rooms.Contains(x.RoomId) && x.CropYear == target.Identity.CropYear && x.GrowerLotId == target.Identity.GrowerLotId
                && x.FruitProfileId == target.Identity.FruitProfileId).ToArrayAsync(ct);
        var runIds = ledger.Select(x => x.ActualRunId ?? -1).Distinct().ToArray();
        var runs = await db.ActualRuns.AsNoTracking().Where(x => runIds.Contains(x.Id)).ToArrayAsync(ct);
        var revisions = await db.ActualRunRevisions.AsNoTracking().Where(x => runIds.Contains(x.ActualRunId)).ToArrayAsync(ct);
        // Search the operational parents independently of their ledger links so
        // an orphan event cannot disappear from a supposedly complete replay.
        var additional = new List<string>();
        foreach (var id in await db.RoomInventoryLosses.AsNoTracking().Where(x => rooms.Contains(x.RoomId)
            && x.CropYear == target.Identity.CropYear && x.GrowerLotId == target.Identity.GrowerLotId && x.FruitProfileId == target.Identity.FruitProfileId).Select(x => x.Id).ToArrayAsync(ct))
            additional.Add($"RoomInventoryLoss {id}: committed loss/reversal evidence requires a supported allocation proof.");
        foreach (var id in await db.RoomDepletions.AsNoTracking().Where(x => receiptIds.Contains(x.ReceiptId)
            || rooms.Contains(x.RoomId) && x.FruitProfileId == target.Identity.FruitProfileId && x.LotCode == target.Identity.Lot).Select(x => x.Id).ToArrayAsync(ct))
            additional.Add($"RoomDepletion {id}: committed depletion/void evidence requires a supported allocation proof.");
        foreach (var id in await db.InterCrewTransfers.AsNoTracking().Where(x => (rooms.Contains(x.SourceRoomId) || rooms.Contains(x.DestinationRoomId ?? -1))
            && x.CropYear == target.Identity.CropYear && x.GrowerLotId == target.Identity.GrowerLotId && x.FruitProfileId == target.Identity.FruitProfileId).Select(x => x.Id).ToArrayAsync(ct))
            additional.Add($"InterCrewTransfer {id}: dispatch/receipt/return allocation proof is required.");
        foreach (var id in await db.OutsideWarehouseTransfers.AsNoTracking().Where(x => rooms.Contains(x.SourceRoomId)
            && x.CropYear == target.Identity.CropYear && x.GrowerLotId == target.Identity.GrowerLotId && x.FruitProfileId == target.Identity.FruitProfileId).Select(x => x.Id).ToArrayAsync(ct))
            additional.Add($"OutsideWarehouseTransfer {id}: committed custody/reversal allocation proof is required.");
        foreach (var id in await db.ProcessorShipmentLines.AsNoTracking().Where(x => rooms.Contains(x.RoomId)
            && x.CropYear == target.Identity.CropYear && x.GrowerLotId == target.Identity.GrowerLotId && x.FruitProfileId == target.Identity.FruitProfileId).Select(x => x.Id).ToArrayAsync(ct))
            additional.Add($"ProcessorShipmentLine {id}: committed custody/reversal allocation proof is required.");
        return new(positions.ToArray(), ledger, receipts, corrections, audits, transfers.Values.OrderBy(x => x.Id).ToArray(), entries, runs, revisions, additional.Order().ToArray());
    }

    public static RecordedHistoryAssessment Assess(InventoryPositionEvidence target, RecordedInventoryHistory history)
    {
        var problems = new List<string>();
        problems.AddRange(history.AdditionalEventBlockers ?? []);
        var references = new HashSet<InventoryEvidenceReference>();
        var visited = new HashSet<int>();
        var active = new HashSet<int>();
        CheckPool(target);
        var current = target.Projections.Where(x => x.Disposition == "Current").ToArray();
        if (target.AuthoritativeQuantity <= 0 || current.Sum(x => x.Quantity) <= target.AuthoritativeQuantity
            || current.Select(x => x.RawKey).Distinct().Count() < 2)
            problems.Add("Target is not a positive pool with excess historical status aliases.");
        if (current.Any(x => !x.ExactIdentity || x.Quantity < 0 || x.State != "Untreated" || x.Signature != "u" || !x.ApplicationIds.IsEmpty))
            problems.Add("Target projections contain conflicting identity or treatment evidence.");
        var proof = references.OrderBy(x => x.Entity).ThenBy(x => x.Id).ToImmutableArray();
        var plan = problems.Count == 0 ? new InventoryNormalizationPlan("recorded-history-shared-pool/v1", "recorded-origins-revisions-runs-transfers/v1",
            target.Watermark.Fingerprint, "Closed recorded history proves an untreated shared pool; supersede projections without allocating surviving bins to receipts.",
            new(InventoryConfidence.Unknown, [], "Exact surviving receipt allocation remains unresolved; correction reasons do not allocate fruit."), proof,
            current.Where(x => x.Quantity > 0).OrderBy(x => x.Id).Select(x => new InventoryProjectionChange(x.Id, x.Quantity, 0,
                x.Version, checked(x.Version + 1), "Current", "Historical", x.Signature, x.ReceiptId, x.ApplicationIds, x.RawKey, x.State, x.UpdatedAt)).ToImmutableArray(),
            target.AuthoritativeQuantity, null) : null;
        return new(problems.Count == 0, problems.Distinct().ToImmutableArray(), proof, plan);

        void Ref(string entity, object id) => references.Add(new(entity, id.ToString()!));
        void Fail(string message) => problems.Add(message);
        bool Identity(RoomInventoryAdjustment x, InventoryPositionEvidence e) => x.CropYear == e.Identity.CropYear
            && x.GrowerLotId == e.Identity.GrowerLotId && x.FruitProfileId == e.Identity.FruitProfileId
            && x.LotNumber.Trim().Equals(e.Identity.Lot, StringComparison.OrdinalIgnoreCase)
            && InventoryStatusIdentity.Normalize(x.InventoryStatus, e.Identity.ProductionType) == InventoryStatusIdentity.Normalize(e.Identity.Status, e.Identity.ProductionType);
        void CheckPool(InventoryPositionEvidence e)
        {
            var room = e.Location.RoomId ?? -1;
            if (active.Contains(room)) { Fail($"Room {room}: cyclic transfer ancestry requires a time-bounded allocation proof."); return; }
            if (!visited.Add(room)) return;
            active.Add(room);
            if (!e.IdentityVerified || !e.CustodyVerified || e.HistoricalSnapshotUnavailable || e.CommittedQuantity != 0 || e.Identity.Key != target.Identity.Key)
                Fail($"Room {room}: identity, custody or complete current history is unverified.");
            var rows = history.Ledger.Where(x => x.RoomId == room && Identity(x, e)).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToArray();
            if (rows.Length == 0 || rows.Length != e.Ledger.Length || e.Ledger.Any(x => !x.ExactIdentity || !rows.Any(r => r.Id == x.Id
                    && r.WarehouseId == e.Location.WarehouseId && r.ChangeAmount == x.Quantity && r.AdjustmentType == x.Kind
                    && r.AdjustmentAt == x.At && r.CreatedAt == x.RecordedAt && r.ReceiptId == x.ReceiptId))
                || rows.Sum(x => x.ChangeAmount) != e.AuthoritativeQuantity)
                Fail($"Room {room}: raw ledger and canonical evidence disagree or a recorded event is missing.");
            Replay(rows, "recorded");
            Replay(rows.OrderBy(x => x.AdjustmentAt).ThenBy(x => x.CreatedAt).ThenBy(x => x.Id), "effective");
            var firstArrival = rows.Where(x => x.ChangeAmount > 0).Select(x => x.AdjustmentAt).DefaultIfEmpty(DateTimeOffset.MinValue).Min();
            foreach (var app in e.Applications.Where(x => x.ReceiptId != null || x.RoomId == null || x.RoomId == room && x.AppliedAt >= firstArrival))
                Fail($"Room {room}: application {app.Id} at {app.AppliedAt:O} requires an exact treatment allocation; untreated reconstruction is unsupported.");
            if (e.Movements.Any(x => !x.ExactIdentity || x.Quantity <= 0 || x.State != "Untreated" || x.Signature != "u" || x.ReversesId != null)
                || e.Projections.Any(x => x.Quantity > 0 && (x.State != "Untreated" || x.Signature != "u" || !x.ApplicationIds.IsEmpty)))
                Fail($"Room {room}: movement/projection treatment or reversal evidence contradicts a uniformly untreated history.");
            var matchedMovements = new HashSet<long>();
            var balance = 0;
            foreach (var row in rows)
            {
                Ref("RoomInventoryAdjustment", row.Id);
                switch (row.AdjustmentType)
                {
                    case "ReceiptAdd":
                        var receipt = history.Receipts.SingleOrDefault(x => x.Id == row.ReceiptId);
                        if (receipt == null || receipt.IsTransferReceipt || receipt.RoomId != room || receipt.WarehouseId != row.WarehouseId
                            || receipt.CropYear != row.CropYear || receipt.GrowerLotId != row.GrowerLotId || receipt.FruitProfileId != row.FruitProfileId
                            || receipt.ReceivedAt != row.AdjustmentAt || row.ChangeAmount <= 0
                            || !e.Receipts.Any(x => x.Id == receipt.Id && x.ExactIdentity)
                            || history.Ledger.Count(x => x.ReceiptId == row.ReceiptId && x.AdjustmentType == "ReceiptAdd") != 1)
                        { Fail($"Ledger {row.Id}: original receipt {row.ReceiptId} arrival is missing, duplicated or contradictory."); break; }
                        Ref("Receipt", receipt.Id);
                        var revisions = history.Corrections.Where(x => x.ReceiptId == receipt.Id).ToArray();
                        if (revisions.Length == 0)
                        {
                            if (receipt.IsDeleted || receipt.BinCount != row.ChangeAmount)
                                Fail($"Receipt {receipt.Id}: missing committed revision from arrival {row.ChangeAmount} to current effective quantity.");
                        }
                        else
                        {
                            var assessment = ReceiptRevisionEvidenceValidator.Evaluate(e, receipt, revisions, history.Ledger, history.Audits, history.Ledger);
                            if (!assessment.CorrectionChainValid || revisions.Any(x => x.InventoryDelta > 0 || x.CreatedAt < row.CreatedAt))
                                Fail($"Receipt {receipt.Id}: missing/contradictory revision, audit or compensating ledger event; positive corrections need separate origin evidence.");
                            foreach (var revision in revisions) Ref("ReceiptInventoryOverride", revision.Id);
                            foreach (var audit in assessment.AuditIds) Ref("AuditLog", audit);
                        }
                        break;
                    case "ReceiptAdminOverride":
                        var correction = history.Corrections.SingleOrDefault(x => x.Id == row.ReceiptInventoryOverrideId && x.ReceiptId == row.ReceiptId);
                        if (correction == null || row.ChangeAmount >= 0 || !rows.Any(x => x.ReceiptId == row.ReceiptId && x.AdjustmentType == "ReceiptAdd")
                            || row.AdjustmentAt != correction.CreatedAt)
                            Fail($"Ledger {row.Id}: exact committed negative receipt revision {row.ReceiptInventoryOverrideId} is missing or contradictory.");
                        break;
                    case "BinsRun":
                        var entries = history.Entries.Where(x => x.InventoryAdjustmentId == row.Id).ToArray();
                        var entry = entries.Length == 1 ? entries[0] : null;
                        var revisionRow = history.RunRevisions.SingleOrDefault(x => x.Id == row.ActualRunRevisionId);
                        var run = history.Runs.SingleOrDefault(x => x.Id == row.ActualRunId);
                        if (entry == null || run == null || revisionRow == null || revisionRow.ActualRunId != run.Id
                            || entry.ActualRunId != run.Id || entry.ActualRunRevisionId != revisionRow.Id || !revisionRow.IsCurrent
                            || run.CurrentRevisionNumber != revisionRow.RevisionNumber || run.Status != "Active" || run.RunAt != entry.RunAt
                            || string.IsNullOrWhiteSpace(revisionRow.OperationKey)
                            || entry.RoomId != room || entry.WarehouseId != row.WarehouseId || entry.GrowerLotId != row.GrowerLotId
                            || entry.FruitProfileId != row.FruitProfileId || entry.CropYear != row.CropYear || entry.RunAt != row.AdjustmentAt
                            || !entry.LotNumber.Trim().Equals(e.Identity.Lot, StringComparison.OrdinalIgnoreCase)
                            || InventoryStatusIdentity.Normalize(entry.InventoryStatus, e.Identity.ProductionType) != InventoryStatusIdentity.Normalize(e.Identity.Status, e.Identity.ProductionType)
                            || entry.CreatedAt > row.CreatedAt || revisionRow.CreatedAt != entry.CreatedAt || row.ChangeAmount >= 0
                            || entry.BinsRun != -row.ChangeAmount || entry.PreviousAvailableBins != balance || entry.NewAvailableBins != balance + row.ChangeAmount
                            || entry.IsReversed || entry.ReversesBinsRunEntryId != null || entry.IsOverdrawOverride || entry.TransactionType != "Depletion"
                            || entry.TreatmentStateSnapshot is not (null or "Untreated") || entry.TreatmentSignatureSnapshot is not (null or "u"))
                            Fail($"Ledger {row.Id}: packing entry/run/revision {entry?.Id}/{row.ActualRunId}/{row.ActualRunRevisionId} is missing or contradictory.");
                        else
                        {
                            Ref("BinsRunEntry", entry.Id); Ref("ActualRun", run.Id); Ref("ActualRunRevision", revisionRow.Id);
                            var moves = e.Movements.Where(x => x.Outgoing && x.Parent == $"entry:{entry.Id}").ToArray();
                            // Before lineage materialization, the run entry is the debit
                            // evidence. Only a independently proven uniform pool permits it.
                            if (moves.Length > 0 || entry.TreatmentSignatureSnapshot != null || entry.TreatmentStateSnapshot != null)
                                MatchMoves(moves, -row.ChangeAmount, "BinsRun", row);
                        }
                        break;
                    case "TransferIn":
                    case "TransferOut":
                        var transfer = history.Transfers.SingleOrDefault(x => x.Id == row.RoomTransferId);
                        var pair = history.Ledger.Where(x => x.RoomTransferId == row.RoomTransferId).ToArray();
                        if (transfer == null || transfer.IsReversed || transfer.ReversesRoomTransferId != null || transfer.BinCount <= 0
                            || transfer.CropYear != e.Identity.CropYear || transfer.GrowerLotId != e.Identity.GrowerLotId || transfer.FruitProfileId != e.Identity.FruitProfileId
                            || pair.Length != 2 || !pair.All(x => Identity(x, e) && x.AdjustmentAt == transfer.TransferredAt && x.CreatedAt >= transfer.CreatedAt)
                            || !pair.Any(x => x.RoomId == transfer.SourceRoomId && x.WarehouseId == transfer.SourceWarehouseId && x.AdjustmentType == "TransferOut" && x.ChangeAmount == -transfer.BinCount)
                            || !pair.Any(x => x.RoomId == transfer.DestinationRoomId && x.WarehouseId == transfer.DestinationWarehouseId && x.AdjustmentType == "TransferIn" && x.ChangeAmount == transfer.BinCount))
                        { Fail($"Ledger {row.Id}: transfer {row.RoomTransferId} lacks an exact matched source/destination ledger pair."); break; }
                        Ref("RoomTransfer", transfer.Id);
                        foreach (var leg in pair) Ref("RoomInventoryAdjustment", leg.Id);
                        var incoming = row.AdjustmentType == "TransferIn";
                        MatchMoves(e.Movements.Where(x => (incoming ? x.Incoming : x.Outgoing) && x.Parent == $"room:{transfer.Id}").ToArray(), transfer.BinCount, "Transfer", row);
                        if (incoming)
                        {
                            var source = history.Positions.SingleOrDefault(x => x.Location.RoomId == transfer.SourceRoomId && x.Location.WarehouseId == transfer.SourceWarehouseId);
                            if (source == null) Fail($"Transfer {transfer.Id}: source room {transfer.SourceRoomId} recorded origin history is missing.");
                            else CheckPool(source);
                        }
                        break;
                    default:
                        Fail($"Ledger {row.Id}: {row.AdjustmentType} requires its committed parent/reversal and treatment allocation proof; this maintenance proof does not support it.");
                        break;
                }
                balance += row.ChangeAmount;
            }
            foreach (var movement in e.Movements.Where(x => !matchedMovements.Contains(x.Id)))
                Fail($"Room {room}: lineage movement {movement.Id} has no matching validated ledger event.");
            foreach (var receipt in history.Receipts.Where(x => x.RoomId == room && !x.IsTransferReceipt
                && !rows.Any(r => r.ReceiptId == x.Id && r.AdjustmentType == "ReceiptAdd")))
                Fail($"Receipt {receipt.Id}: recorded receipt has no original ledger arrival in room {room}.");
            foreach (var entry in history.Entries.Where(x => x.RoomId == room && !rows.Any(r => r.Id == x.InventoryAdjustmentId)))
                Fail($"BinsRunEntry {entry.Id}: recorded consumption has no matching ledger effect in room {room}.");
            foreach (var transfer in history.Transfers.Where(x => (x.SourceRoomId == room || x.DestinationRoomId == room)
                && !rows.Any(r => r.RoomTransferId == x.Id)))
                Fail($"RoomTransfer {transfer.Id}: recorded transfer has no matching ledger effect in room {room}.");
            active.Remove(room);

            void MatchMoves(InventoryMovementEvidence[] moves, int quantity, string kind, RoomInventoryAdjustment row)
            {
                if (moves.Length == 0 || moves.Sum(x => x.Quantity) != quantity || moves.Any(x => x.Kind != kind || x.At != row.AdjustmentAt))
                    Fail($"Ledger {row.Id}: {kind} lineage quantities or effective timestamps disagree.");
                foreach (var move in moves) { matchedMovements.Add(move.Id); Ref("TreatmentLineageMovement", move.Id); }
            }
            void Replay(IEnumerable<RoomInventoryAdjustment> sequence, string clock)
            {
                long bins = 0;
                foreach (var row in sequence)
                {
                    bins += row.ChangeAmount;
                    if (bins < 0) { Fail($"Room {room}: {clock}-time inventory is negative at ledger {row.Id}; missing arrival or contradictory event time."); break; }
                }
            }
        }
    }
}
