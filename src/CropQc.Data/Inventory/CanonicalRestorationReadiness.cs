using System.Collections.Immutable;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using CropQc.Data.Entities;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

/// <summary>Read-only proof of canonical split restorations. Historical parents
/// describe the departure; each credit must prove its identity at restoration.
/// Never uses a current projection quantity as physical authority.</summary>
public static class CanonicalRestorationReadiness
{
    public static async Task<string?> VerifyAsync(CropQcDbContext db, RoomInventoryLoss? loss,
        OutsideWarehouseTransfer? outside, IReadOnlyCollection<RoomInventoryAdjustment> ledger, CancellationToken ct)
    {
        var isLoss = loss != null;
        var id = loss?.Id ?? outside!.Id;
        var quantity = loss?.BinCount ?? outside!.BinCount;
        var room = loss?.RoomId ?? outside!.SourceRoomId;
        var warehouse = loss?.WarehouseId ?? outside!.SourceWarehouseId;
        var receipt = loss?.ReceiptId ?? outside?.ReceiptId;
        var operation = loss?.OperationKey ?? outside!.OperationKey;
        var reversedAt = loss?.ReversedAt ?? outside?.ReversedAt;
        var debitType = isLoss ? InventoryLedgerKinds.DroppedBins : OutsideWarehouseTransferAdjustmentTypes.Transfer;
        var creditType = isLoss ? InventoryLedgerKinds.DroppedBinsReversal : OutsideWarehouseTransferAdjustmentTypes.Reversal;
        var departureType = isLoss ? "InventoryLoss" : "OutsideWarehouseTransfer";
        var returnType = isLoss ? "InventoryLossReversal" : "OutsideWarehouseTransferReversal";
        var debits = ledger.Where(x => x.AdjustmentType == debitType).ToArray();
        var credits = ledger.Where(x => x.AdjustmentType == creditType).ToArray();
        if (quantity <= 0 || reversedAt == null || operation.LastIndexOf(':') <= 0
            || isLoss && (loss!.LossType != RoomInventoryLossTypes.Dropped || string.IsNullOrWhiteSpace(loss.Reason))
            || debits.Length != 1 || debits[0].ChangeAmount != -quantity || credits.Length == 0
            || credits.Any(x => x.ChangeAmount <= 0) || credits.Sum(x => (long)x.ChangeAmount) != quantity
            || ledger.Count != 1 + credits.Length
            || ledger.Any(x => x.InventoryInvariantVersion != InventoryLedgerKinds.CanonicalCommandInvariantVersion
                || x.Source != "CanonicalInventory/v1" || x.WarehouseId != warehouse || x.RoomId != room || x.ReceiptId != receipt
                || x.OldBinCount == null || (long)x.OldBinCount + x.ChangeAmount != x.NewBinCount))
            return "Canonical restoration parent, ledger sides or before/after quantities do not reconcile.";

        var moves = await db.TreatmentLineageMovements.AsNoTracking()
            .Include(x => x.SourceSegment).ThenInclude(x => x!.Applications)
            .Include(x => x.DestinationSegment).ThenInclude(x => x!.Applications)
            .Where(x => isLoss ? x.RoomInventoryLossId == id : x.OutsideWarehouseTransferId == id).ToListAsync(ct);
        var departures = moves.Where(x => x.MovementType == departureType && x.ReversesTreatmentLineageMovementId == null).ToArray();
        var returns = moves.Where(x => x.MovementType == returnType && x.ReversesTreatmentLineageMovementId != null).ToArray();
        if (departures.Length == 0 || departures.Any(x => x.SourceSegment == null) || moves.Count != departures.Length + returns.Length
            || departures.Sum(x => (long)x.BinCount) != quantity || returns.Sum(x => (long)x.BinCount) != quantity)
            return "Canonical restoration has missing or contradictory departure/reversal movements.";
        var historical = CanonicalIdentityMap.Historical(departures[0].SourceSegment!);
        var parentMatches = isLoss
            ? historical.CropYear == loss!.CropYear && historical.GrowerLotId == loss.GrowerLotId
                && historical.FruitProfileId == loss.FruitProfileId && Same(historical.Lot, loss.LotNumber)
                && Same(historical.GrowerNumber, loss.GrowerNumber) && Same(historical.Variety, loss.VarietyCode)
                && Same(historical.Status, loss.InventoryStatus)
            : historical.Key == new InventoryIdentity(outside!.CropYear, outside.GrowerLotId, outside.FruitProfileId,
                outside.LotNumberSnapshot, outside.GrowerNumberSnapshot, outside.VarietyCodeSnapshot,
                outside.ProductionTypeSnapshot, outside.IsOrganicSnapshot, outside.InventoryStatusSnapshot ?? "").Key;
        if (!historical.IsComplete || !parentMatches || !Matches(debits[0], historical)
            || departures.Any(x => x.SourceSegment == null || x.SourceRoomId != room || x.DestinationRoomId != null
                || x.DestinationSegmentId != null || x.SourceSegment.RoomId != room || x.SourceSegment.WarehouseId != warehouse
                || x.IdentityKey != historical.Key || CanonicalIdentityMap.Historical(x.SourceSegment).Key != historical.Key
                || x.ReceiptId != x.SourceSegment.ReceiptId || x.BinCount <= 0
                || x.CreatedAt != debits[0].CreatedAt || x.OccurredAt != debits[0].AdjustmentAt
                || x.OperationKey != operation + ":s" + x.SourceSegmentId
                || !isLoss && (x.TreatmentStateSnapshot != outside!.TreatmentStateSnapshot
                    || x.TreatmentSignatureSnapshot != outside.TreatmentSignatureSnapshot)))
            return "Canonical departure identity, parent, receipt or immutable movement snapshot conflicts.";

        var departureIds = departures.Select(x => x.Id).ToArray();
        var allReversals = await db.TreatmentLineageMovements.AsNoTracking()
            .Where(x => x.ReversesTreatmentLineageMovementId != null && departureIds.Contains(x.ReversesTreatmentLineageMovementId.Value))
            .Select(x => x.Id).ToArrayAsync(ct);
        if (!allReversals.Order().SequenceEqual(returns.Select(x => x.Id).Order())
            || departures.Any(x => returns.Count(r => r.ReversesTreatmentLineageMovementId == x.Id) != 1))
            return "Canonical departure is missing its unique reversal or is claimed by another parent.";
        var map = await CanonicalIdentityMap.LoadAsync(db, departures.Where(x => x.ReceiptId != null).Select(x => x.ReceiptId!.Value).ToArray(), ct);
        var appIds = departures.SelectMany(x => x.SourceSegment!.Applications).Select(x => x.RoomTreatmentApplicationId).Distinct().ToArray();
        var apps = await db.RoomTreatmentApplications.AsNoTracking().Where(x => appIds.Contains(x.Id))
            .Select(x => new InventoryApplicationEvidence(x.Id, x.AppliedAt, x.ReversedAt, x.ReceiptId, x.RoomId) { RecordedAt = x.CreatedAt }).ToArrayAsync(ct);
        foreach (var returned in returns)
        {
            var original = departures.SingleOrDefault(x => x.Id == returned.ReversesTreatmentLineageMovementId);
            if (original == null || returned.BinCount != original.BinCount || returned.ReceiptId != original.ReceiptId
                || returned.SourceSegmentId != original.SourceSegmentId || returned.SourceRoomId != null
                || returned.DestinationRoomId != room || returned.DestinationSegment == null
                || returned.DestinationSegment.RoomId != room || returned.DestinationSegment.WarehouseId != warehouse
                || returned.DestinationSegment.ReceiptId != original.ReceiptId || returned.CreatedAt != reversedAt
                || returned.CreatedAt < original.CreatedAt)
                return "Canonical reversal does not preserve its exact original allocation and custody.";
            InventoryIdentity expected;
            try { expected = map.Resolve(historical, original.ReceiptId, returned.CreatedAt).Current; }
            catch (InvalidOperationException) { return "Canonical restoration identity correction history is contradictory."; }
            if (returned.IdentityKey != expected.Key || CanonicalIdentityMap.Historical(returned.DestinationSegment).Key != expected.Key)
                return "Canonical restoration identity is not proved by its receipt mapping at committed reversal time.";
            var source = original.SourceSegment!;
            var ids = source.Applications.Select(x => x.RoomTreatmentApplicationId).Order().ToImmutableArray();
            if (source.TreatmentSignature != original.TreatmentSignatureSnapshot || source.TreatmentState != original.TreatmentStateSnapshot
                || (source.TreatmentState == "Untreated" ? source.TreatmentSignature != "u" || !ids.IsEmpty
                    : source.TreatmentState != "Confirmed" || ids.IsEmpty || ids.Distinct().Count() != ids.Length
                        || source.TreatmentSignature != "u|a:" + string.Join(',', ids)
                        || ids.Any(a => !apps.Any(x => x.Id == a && x.AppliedAt <= original.OccurredAt && x.RecordedAt <= original.CreatedAt))))
                return "Canonical departure treatment membership is unknown or lacks point-in-time proof.";
            var atReturn = apps.Select(x => x with { ReversedAt = x.ReversedAt <= returned.CreatedAt ? x.ReversedAt : null });
            var treatment = InventoryEffectiveTreatment.Read(source.TreatmentSignature, source.TreatmentState, ids, atReturn);
            if (returned.TreatmentSignatureSnapshot != treatment.Signature || returned.TreatmentStateSnapshot != treatment.State
                || returned.DestinationSegment.TreatmentSignature != treatment.Signature || returned.DestinationSegment.TreatmentState != treatment.State
                || !returned.DestinationSegment.Applications.Select(x => x.RoomTreatmentApplicationId).Order().SequenceEqual(treatment.ApplicationIds.Order()))
                return "Canonical restoration changed the original allocation's effective treatment membership.";
        }

        var originalKey = operation[..operation.LastIndexOf(':')];
        var reversalKey = isLoss ? credits[0].InventoryOperationKey?.Split(":restore:")[0] : outside!.ReversalOperationKey;
        var records = await db.InventoryCommands.AsNoTracking().Where(x => x.OperationKey == originalKey || x.OperationKey == reversalKey).ToListAsync(ct);
        return !ProvesCommand(originalKey, isLoss ? InventoryCommandKind.Loss : InventoryCommandKind.OutsideWarehouseTransfer, debits, departures)
            || !ProvesCommand(reversalKey, isLoss ? InventoryCommandKind.ReverseLoss : InventoryCommandKind.Return, credits, returns)
            ? "Canonical restoration lacks matching committed intent/result, ledger and movement effects." : null;

        bool ProvesCommand(string? key, InventoryCommandKind kind, RoomInventoryAdjustment[] rows, TreatmentLineageMovement[] allocations)
        {
            var record = records.SingleOrDefault(x => x.OperationKey == key);
            if (record == null) return false;
            try
            {
                var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
                var intent = JsonSerializer.Deserialize<InventoryCommand>(record.IntentJson, options);
                var result = JsonSerializer.Deserialize<InventoryCommandResult>(record.ResultJson, options);
                if (intent?.Kind != kind || intent.OperationKey != key || result?.Status != InventoryCommandStatus.Committed
                    || result.OperationKey != key || intent.ActorId != record.ActorId
                    || Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(record.IntentJson))) != record.IntentHash) return false;
                var effects = result.Effects.Where(x => x.ParentId == id).ToArray();
                if (!effects.SelectMany(x => x.LedgerIds).Order().SequenceEqual(rows.Select(x => x.Id).Order())
                    || !effects.SelectMany(x => x.MovementIds).Order().SequenceEqual(allocations.Select(x => x.Id).Order())) return false;
                foreach (var effect in effects)
                {
                    if (effect.LedgerIds.Length != 1) return false;
                    var row = rows.Single(x => x.Id == effect.LedgerIds[0]);
                    var subset = allocations.Where(x => effect.MovementIds.Contains(x.Id)).ToArray();
                    if (subset.Length == 0 || subset.Sum(x => (long)x.BinCount) != Math.Abs((long)row.ChangeAmount)
                        || effect.Quantity != Math.Abs((long)row.ChangeAmount)
                        // Return effects describe the source custody debit; the linked
                        // positive ledger describes the destination room credit.
                        || (kind == InventoryCommandKind.Return ? effect.Before - effect.After != row.ChangeAmount
                            : effect.Before != row.OldBinCount || effect.After != row.NewBinCount || effect.After - effect.Before != row.ChangeAmount)
                        || row.CreatedAt != record.CommittedAt || row.AdjustmentAt.UtcTicks / 10 != intent.EffectiveAt.UtcTicks / 10
                        || subset.Any(x => x.CreatedAt != record.CommittedAt || x.OccurredAt.UtcTicks / 10 != intent.EffectiveAt.UtcTicks / 10
                            || !Matches(row, CanonicalIdentityMap.Historical((row.ChangeAmount < 0 ? x.SourceSegment : x.DestinationSegment)!)))) return false;
                }
                return true;
            }
            catch (Exception e) when (e is JsonException or InvalidOperationException or ArgumentException) { return false; }
        }
    }

    private static bool Matches(RoomInventoryAdjustment row, InventoryIdentity identity) => row.CropYear == identity.CropYear
        && row.GrowerLotId == identity.GrowerLotId && row.FruitProfileId == identity.FruitProfileId
        && Same(row.LotNumber, identity.Lot) && Same(row.VarietyCode, identity.Variety) && Same(row.InventoryStatus, identity.Status);
    private static bool Same(string? a, string? b) => string.Equals(a?.Trim() ?? "", b?.Trim() ?? "", StringComparison.OrdinalIgnoreCase);
}
