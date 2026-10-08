using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

// Reuse committed canonical command effects and the existing physical-flow
// validator. This certifies event membership, not a second room balance.
internal static class ReconstructionCommandEvidence
{
    internal sealed record Coverage(ImmutableArray<long> MovementIds, ImmutableArray<long> LedgerIds, ImmutableArray<string> InvalidCommands);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal static async Task<Coverage> ValidateAsync(CropQcDbContext db,
        InventoryPositionEvidence evidence, CancellationToken ct)
    {
        var ids = evidence.Movements.Select(x => x.Id).ToArray();
        var movements = await db.TreatmentLineageMovements.AsNoTracking().Where(x => ids.Contains(x.Id)).ToArrayAsync(ct);
        var keys = movements.SelectMany(x => Prefixes(x.OperationKey)).Distinct().ToArray();
        var commands = await db.InventoryCommands.AsNoTracking().Where(x => keys.Contains(x.OperationKey)).ToArrayAsync(ct);
        var valid = ImmutableArray.CreateBuilder<long>();
        var validLedger = ImmutableArray.CreateBuilder<long>();
        var invalid = new HashSet<string>();
        foreach (var command in commands)
        {
            invalid.Add(command.OperationKey);
            if (command.IntentHash != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(command.IntentJson)))) continue;
            using var document = JsonDocument.Parse(command.ResultJson);
            if (!document.RootElement.TryGetProperty("effects", out _)) continue;
            var intent = JsonSerializer.Deserialize<InventoryCommand>(command.IntentJson, Json)!;
            var result = JsonSerializer.Deserialize<InventoryCommandResult>(command.ResultJson, Json)!;
            if (result.Status != InventoryCommandStatus.Committed || result.OperationKey != command.OperationKey
                || intent.OperationKey != command.OperationKey || intent.ActorId != command.ActorId
                || intent.Kind is InventoryCommandKind.ManualStockAddition or InventoryCommandKind.ImportBaseline or InventoryCommandKind.BaselineAdjustment) continue;
            var ledgerIds = result.Effects.SelectMany(x => x.LedgerIds).Distinct().ToArray();
            var moveIds = result.Effects.SelectMany(x => x.MovementIds).Distinct().ToArray();
            var ledger = await db.RoomInventoryAdjustments.AsNoTracking().Where(x => ledgerIds.Contains(x.Id)).ToArrayAsync(ct);
            var moves = await db.TreatmentLineageMovements.AsNoTracking().Where(x => moveIds.Contains(x.Id)).ToArrayAsync(ct);
            // The partial-custody correction contract posts newly added untreated
            // bins at commit time, deliberately avoiding an older treatment clock.
            // Read the persisted contract extension without duplicating its writer
            // or requiring that pending schema on this main-based branch.
            using var persistedIntent = JsonDocument.Parse(command.IntentJson);
            var arrivalAtCommit = intent.Kind == InventoryCommandKind.CorrectReceiptQuantity
                && persistedIntent.RootElement.TryGetProperty("receiptChange", out var change)
                && change.TryGetProperty("confirmAdditionalBinsUntreated", out var confirmed) && confirmed.ValueKind == JsonValueKind.True
                && ledger.Length > 0 && ledger.All(x => x.ChangeAmount > 0 && x.AdjustmentType == "ReceiptAdminOverride"
                    && x.ReceiptId == intent.ReceiptChange!.ReceiptId && x.ReceiptInventoryOverrideId != null)
                && moves.Length > 0 && moves.All(x => x.MovementType == "ReceiptQuantityCorrection"
                    && x.ReceiptId == intent.ReceiptChange!.ReceiptId && x.SourceSegmentId == null
                    && x.TreatmentStateSnapshot == "Untreated" && x.TreatmentSignatureSnapshot == "u");
            var effectiveAt = arrivalAtCommit ? command.CommittedAt : intent.EffectiveAt;
            if (ledger.Length != ledgerIds.Length || moves.Length != moveIds.Length
                || ledger.Any(x => x.InventoryInvariantVersion != InventoryLedgerKinds.CanonicalCommandInvariantVersion
                    || !Prefixes(x.InventoryOperationKey ?? "").Contains(command.OperationKey) || x.CreatedAt != command.CommittedAt || x.AdjustmentAt.UtcTicks / 10 != effectiveAt.UtcTicks / 10)
                || moves.Any(x => !Prefixes(x.OperationKey).Contains(command.OperationKey) || x.CreatedAt != command.CommittedAt
                    || x.OccurredAt.UtcTicks / 10 != effectiveAt.UtcTicks / 10 || !ParentPresent(x))) continue;
            var allLedger = await db.RoomInventoryAdjustments.AsNoTracking().Where(x => x.InventoryOperationKey == command.OperationKey
                || x.InventoryOperationKey!.StartsWith(command.OperationKey + ":")).Select(x => x.Id).ToArrayAsync(ct);
            var allMoves = await db.TreatmentLineageMovements.AsNoTracking().Where(x => x.OperationKey == command.OperationKey
                || x.OperationKey.StartsWith(command.OperationKey + ":")).Select(x => x.Id).ToArrayAsync(ct);
            if (!allLedger.Order().SequenceEqual(ledgerIds.Order()) || !allMoves.Order().SequenceEqual(moveIds.Order())) continue;
            var segmentIds = moves.SelectMany(x => new[] { x.SourceSegmentId, x.DestinationSegmentId }).Where(x => x != null).Select(x => x!.Value).Distinct().ToArray();
            var segments = await db.TreatmentLineageSegments.AsNoTracking().Where(x => segmentIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
            if (!InventoryPhysicalFlow.Matches(ledger, moves, segments)) continue;
            var parentsValid = true;
            foreach (var group in moves.Where(x => x.RoomTransferId != null).GroupBy(x => x.RoomTransferId!.Value))
            {
                var parent = await db.RoomTransfers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == group.Key, ct);
                if (parent == null || parent.BinCount != group.Sum(x => x.BinCount)
                    || group.Any(x => x.SourceRoomId != parent.SourceRoomId || x.DestinationRoomId != parent.DestinationRoomId)) parentsValid = false;
            }
            foreach (var group in moves.Where(x => x.BinsRunEntryId != null).GroupBy(x => x.BinsRunEntryId!.Value))
            {
                var parent = await db.BinsRunEntries.AsNoTracking().SingleOrDefaultAsync(x => x.Id == group.Key, ct);
                if (parent == null || parent.BinsRun != group.Sum(x => x.BinCount)
                    || !ledger.Any(x => x.Id == parent.InventoryAdjustmentId)) parentsValid = false;
            }
            foreach (var group in moves.Where(x => x.RoomInventoryLossId != null).GroupBy(x => x.RoomInventoryLossId!.Value))
            {
                var parent = await db.RoomInventoryLosses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == group.Key, ct);
                if (parent == null || parent.BinCount != group.Sum(x => x.BinCount)) parentsValid = false;
            }
            if (!parentsValid) continue;
            var receiptIds = moves.Where(x => x.MovementType == "Receipt").Select(x => x.ReceiptId!.Value).ToArray();
            if (await db.Receipts.AnyAsync(x => receiptIds.Contains(x.Id) && x.IsTransferReceipt, ct)) continue;
            if (!await db.AuditLogs.AnyAsync(x => x.Action == "CanonicalInventoryCommand" && x.EntityKey == command.OperationKey
                && x.SourceApplication == "CanonicalInventory/v1" && x.UserId == command.ActorId && x.CreatedAt == command.CommittedAt, ct)) continue;
            valid.AddRange(moveIds);
            validLedger.AddRange(ledgerIds);
            invalid.Remove(command.OperationKey);
        }
        return new(valid.Distinct().ToImmutableArray(), validLedger.Distinct().ToImmutableArray(), invalid.Order().ToImmutableArray());
    }

    private static IEnumerable<string> Prefixes(string key)
    {
        yield return key;
        for (var i = 0; i < key.Length; i++) if (key[i] == ':') yield return key[..i];
    }

    private static bool ParentPresent(Entities.TreatmentLineageMovement m) => m.MovementType switch
    {
        "Receipt" => m.ReceiptId != null && m.SourceRoomId == null && m.DestinationRoomId != null,
        "ReceiptQuantityCorrection" or "ReceiptVoid" or "ManualTrueUp" => m.ReceiptId != null,
        "Transfer" or "TransferReversal" => m.RoomTransferId != null,
        "BinsRun" or "BinsRunReversal" => m.BinsRunEntryId != null,
        "InventoryLoss" or "InventoryLossReversal" => m.RoomInventoryLossId != null,
        _ => m.InterCrewTransferId != null || m.OutsideWarehouseTransferId != null || m.ProcessorShipmentLineId != null
    };
}
