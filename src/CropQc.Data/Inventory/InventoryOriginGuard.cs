using System.Text.Json;
using CropQc.Data.Entities;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

/// <summary>Read-only validation of the single origin/custody chain before a
/// command consumes inventory. Neither projection counts nor treatment labels
/// can replace an authorized origin or the debit funding a custody credit.</summary>
internal static class InventoryOriginGuard
{
    internal static async Task<string?> ValidateAsync(CropQcDbContext db,
        IEnumerable<InventoryPositionEvidence> sources, DateTimeOffset asOf, CancellationToken ct, bool includeEmptyHistory = false)
    {
        var selected = sources.Where(x => x.AuthoritativeQuantity > 0 || includeEmptyHistory && !x.Ledger.IsDefaultOrEmpty).ToArray();
        var initialIds = selected.SelectMany(x => x.Ledger).Select(x => x.Id).Distinct().ToArray();
        if (selected.Length == 0) return null;
        if (initialIds.Length == 0) return "No recorded origin/custody ledger events support the selected inventory.";
        var all = (await Read(db.RoomInventoryAdjustments.AsNoTracking().Where(x => initialIds.Contains(x.Id)), ct)).ToDictionary(x => x.Id);
        for (var depth = 0; ; depth++)
        {
            if (depth == 32) return "Inventory origin chain exceeds the supported 32-step evidence bound.";
            var growers = all.Values.Select(x => x.GrowerLotId ?? -1).Where(x => x != -1).Distinct().ToArray();
            var receipts = all.Values.Select(x => x.ReceiptId ?? -1).Where(x => x != -1).Distinct().ToArray();
            var transfers = all.Values.Select(x => x.RoomTransferId ?? -1).Where(x => x != -1).Distinct().ToArray();
            var crews = all.Values.Select(x => x.InterCrewTransferId ?? -1).Where(x => x != -1).Distinct().ToArray();
            var outsides = all.Values.Select(x => x.OutsideWarehouseTransferId ?? -1).Where(x => x != -1).Distinct().ToArray();
            var processors = all.Values.Select(x => x.ProcessorShipmentLineId ?? -1).Where(x => x != -1).Distinct().ToArray();
            var runs = all.Values.Select(x => x.ActualRunId ?? -1).Where(x => x != -1).Distinct().ToArray();
            var losses = all.Values.Select(x => x.RoomInventoryLossId ?? -1).Where(x => x != -1).Distinct().ToArray();
            var depletions = all.Values.Select(x => x.RoomDepletionId ?? -1).Where(x => x != -1).Distinct().ToArray();
            var overrides = all.Values.Select(x => x.ReceiptInventoryOverrideId ?? Guid.Empty).Where(x => x != Guid.Empty).Distinct().ToArray();
            var identities = all.Values.Select(x => x.InventoryIdentityCorrectionId ?? Guid.Empty).Where(x => x != Guid.Empty).Distinct().ToArray();
            var expanded = await Read(db.RoomInventoryAdjustments.AsNoTracking().Where(x => x.CreatedAt <= asOf &&
                (growers.Contains(x.GrowerLotId ?? -1) || receipts.Contains(x.ReceiptId ?? -1)
                || transfers.Contains(x.RoomTransferId ?? -1) || crews.Contains(x.InterCrewTransferId ?? -1)
                || outsides.Contains(x.OutsideWarehouseTransferId ?? -1) || processors.Contains(x.ProcessorShipmentLineId ?? -1)
                || runs.Contains(x.ActualRunId ?? -1) || losses.Contains(x.RoomInventoryLossId ?? -1)
                || depletions.Contains(x.RoomDepletionId ?? -1) || overrides.Contains(x.ReceiptInventoryOverrideId ?? Guid.Empty)
                || identities.Contains(x.InventoryIdentityCorrectionId ?? Guid.Empty))), ct);
            var changed = false;
            foreach (var row in expanded) changed |= all.TryAdd(row.Id, row);
            if (all.Count > InventoryEvidenceLoader.MaximumEvidenceRowsPerTable) return "Inventory origin evidence exceeds the safe row bound.";
            if (!changed) break;
        }
        var receiptIds = all.Values.Where(x => x.ReceiptId != null).Select(x => x.ReceiptId!.Value)
            .Concat(selected.SelectMany(x => x.Receipts).Select(x => x.Id)).Distinct().ToArray();
        var recordedReceipts = (await Read(db.Receipts.AsNoTracking().Where(x => receiptIds.Contains(x.Id)), ct)).ToDictionary(x => x.Id);
        var revisions = await Read(db.ReceiptInventoryOverrides.AsNoTracking().Where(x => receiptIds.Contains(x.ReceiptId)), ct);
        var ledgerIds = all.Keys.ToArray();
        var runEntries = await Read(db.BinsRunEntries.AsNoTracking().Where(x => ledgerIds.Contains(x.InventoryAdjustmentId)), ct);
        var identityMap = await CanonicalIdentityMap.LoadAsync(db, receiptIds, ct);
        var correctionReceiptIds = identityMap.Corrections.Where(x => x.CorrectedReceiptId != null).Select(x => x.CorrectedReceiptId!.Value).Distinct().ToArray();
        var mappedMovements = correctionReceiptIds.Length == 0 ? [] : await Read(db.TreatmentLineageMovements.AsNoTracking()
            .Where(x => correctionReceiptIds.Contains(x.ReceiptId ?? -1)), ct);
        var operationKeys = all.Values.Where(x => x.ChangeAmount > 0 && x.AdjustmentType is "ManualTrueUp" or "StartingInventoryImport")
            .SelectMany(x => Prefixes(x.InventoryOperationKey)).Distinct().ToArray();
        var commands = await Read(db.InventoryCommands.AsNoTracking().Where(x => operationKeys.Contains(x.OperationKey)), ct);
        var memo = new Dictionary<long, bool>();
        var visiting = new HashSet<long>();
        string? failure = null;
        foreach (var row in initialIds.Select(id => all[id]).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id))
            if (!Validate(row)) return failure ?? $"Ledger {row.Id}: the recorded origin/custody chain is incomplete.";
        return null;

        bool Validate(RoomInventoryAdjustment row)
        {
            if (memo.TryGetValue(row.Id, out var known)) return known;
            if (!visiting.Add(row.Id)) return Fail(row, "custody cycle has no earlier authorized origin");
            var valid = row.ChangeAmount == 0 || (row.ChangeAmount < 0 ? Debit(row) : Credit(row));
            visiting.Remove(row.Id);
            memo[row.Id] = valid;
            return valid;
        }

        bool Debit(RoomInventoryAdjustment row)
        {
            var prior = all.Values.Where(x => SamePosition(x, row) && Earlier(x, row)).ToArray();
            if (prior.Sum(x => (long)x.ChangeAmount) < -(long)row.ChangeAmount)
                return Fail(row, "withdrawal exceeds custody established by earlier committed events");
            return prior.Where(x => x.ChangeAmount > 0).All(Validate);
        }

        bool Credit(RoomInventoryAdjustment row)
        {
            if (row.AdjustmentType == "ReceiptAdd")
            {
                if (row.ReceiptId is not long id || !recordedReceipts.TryGetValue(id, out var receipt) || receipt.IsTransferReceipt)
                    return Fail(row, "missing ordinary receipt origin; transfer evidence cannot originate inventory");
                if (all.Values.Count(x => x.ReceiptId == id && x.AdjustmentType == "ReceiptAdd") != 1)
                    return Fail(row, $"receipt {id} has duplicate inventory origins");
                var first = revisions.Where(x => x.ReceiptId == id).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).FirstOrDefault();
                if (first == null)
                    return row.CropYear == receipt.CropYear && row.GrowerLotId == receipt.GrowerLotId
                        && row.FruitProfileId == receipt.FruitProfileId && row.LotNumber == receipt.LotCode
                        && row.ChangeAmount == receipt.BinCount || Fail(row, $"receipt {id} differs from its origin without a recorded revision");
                try
                {
                    using var before = JsonDocument.Parse(first.BeforeReceiptSnapshotJson);
                    var original = before.RootElement;
                    // The original typed ReceiptCorrection command recorded a
                    // scalar bin count. The committed revision proves quantity;
                    // unchanged receipt identity still proves the origin.
                    if (original.ValueKind == JsonValueKind.Number)
                    {
                        var mapped = identityMap.Resolve(new(row.CropYear, row.GrowerLotId, row.FruitProfileId,
                            row.LotNumber, row.LotNumber, "", "", null, ""), id).Current;
                        return first.IsComplete && first.ActionType == ReceiptInventoryOverrideActionTypes.QuantityCorrection
                            && original.TryGetInt32(out var oldBins) && oldBins == first.OldReceiptBinCount && oldBins == row.ChangeAmount
                            && mapped.CropYear == receipt.CropYear && mapped.GrowerLotId == receipt.GrowerLotId
                            && mapped.FruitProfileId == receipt.FruitProfileId && mapped.Lot == receipt.LotCode
                            || Fail(row, $"receipt {id}'s scalar correction does not prove its original quantity and identity");
                    }
                    return first.IsComplete && original.GetProperty("id").GetInt64() == id
                        && original.GetProperty("binCount").GetInt32() == row.ChangeAmount
                        && original.GetProperty("cropYear").GetInt32() == row.CropYear
                        && original.GetProperty("growerLotId").GetInt32() == row.GrowerLotId
                        && original.GetProperty("fruitProfileId").GetInt32() == row.FruitProfileId
                        || Fail(row, $"receipt {id}'s original revision snapshot does not match its arrival");
                }
                catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
                { return Fail(row, $"receipt {id}'s original revision snapshot is missing or malformed"); }
            }
            if (row.ReceiptInventoryOverrideId is Guid changeId)
            {
                var change = revisions.SingleOrDefault(x => x.Id == changeId);
                if (change is { IsComplete: true, InventoryDelta: > 0 }
                    && change.ActionType == ReceiptInventoryOverrideActionTypes.QuantityCorrection
                    && all.Values.Where(x => x.ReceiptInventoryOverrideId == changeId).Sum(x => x.ChangeAmount) == change.InventoryDelta)
                {
                    var origins = all.Values.Where(x => x.ReceiptId == change.ReceiptId && x.AdjustmentType == "ReceiptAdd").ToArray();
                    return origins.Length == 1 && Validate(origins[0]) || Fail(row, "receipt increase lacks one original authorized receipt");
                }
            }
            if (row.AdjustmentType is "ManualTrueUp" or "StartingInventoryImport")
            {
                foreach (var command in commands.Where(x => row.InventoryOperationKey?.StartsWith(x.OperationKey + ":", StringComparison.Ordinal) == true))
                {
                    var intent = JsonSerializer.Deserialize<InventoryCommand>(command.IntentJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    var result = JsonSerializer.Deserialize<InventoryCommandResult>(command.ResultJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    if (intent?.Kind is InventoryCommandKind.ManualStockAddition or InventoryCommandKind.BaselineAdjustment or InventoryCommandKind.ImportBaseline
                        && result?.Effects.Any(x => x.LedgerIds.Contains(row.Id)) == true) return true;
                }
                return Fail(row, "opening/addition entry lacks its authorized origin command");
            }
            var parent = Parent(row);
            if (row.AdjustmentType == "BinsRunReversal")
            {
                var reversal = runEntries.SingleOrDefault(x => x.InventoryAdjustmentId == row.Id);
                var original = reversal?.ReversesBinsRunEntryId is long entryId ? runEntries.SingleOrDefault(x => x.Id == entryId) : null;
                if (original == null || !all.TryGetValue(original.InventoryAdjustmentId, out var debit)
                    || row.ChangeAmount > -debit.ChangeAmount
                    || runEntries.Where(x => x.ReversesBinsRunEntryId == original.Id).Sum(x => x.BinsRun) != -debit.ChangeAmount)
                    return Fail(row, "packing restoration lacks its unique original consumption entry");
                return Validate(debit);
            }
            if (parent == null) return Fail(row, $"{row.AdjustmentType} credit lacks a recorded origin or custody parent");
            var historicalParent = all.Values.Where(x => Parent(x) == parent && (Earlier(x, row) || x.CreatedAt == row.CreatedAt)).ToArray();
            var correctedAllocation = mappedMovements.Where(m => MovementParent(m) == parent && m.DestinationRoomId == row.RoomId
                && m.CreatedAt == row.CreatedAt && m.ReceiptId != null).ToArray();
            var followsCorrection = correctedAllocation.Length > 0 && correctedAllocation.Sum(x => x.BinCount) == row.ChangeAmount
                && correctedAllocation.All(m =>
                {
                    var fields = m.IdentityKey.Split('|');
                    if (fields.Length != 9 || !int.TryParse(fields[0], out var crop) || !int.TryParse(fields[1], out var grower)
                        || !int.TryParse(fields[2], out var profile)) return false;
                    var current = identityMap.Resolve(new(crop, grower, profile, fields[4], fields[3], fields[5], fields[6],
                        bool.TryParse(fields[7], out var organic) ? organic : null, fields[8]), m.ReceiptId).Current;
                    return current.CropYear == row.CropYear && current.GrowerLotId == row.GrowerLotId && current.FruitProfileId == row.FruitProfileId
                        && identityMap.Corrections.Any(c => c.CorrectedReceiptId == m.ReceiptId
                            && historicalParent.Any(debit => debit.ChangeAmount < 0 && c.SourceCropYear == debit.CropYear
                                && c.SourceGrowerLotId == debit.GrowerLotId && c.SourceFruitProfileId == debit.FruitProfileId));
                });
            var siblings = historicalParent.Where(x => row.InventoryIdentityCorrectionId != null || followsCorrection || SameIdentity(x, row)).ToArray();
            var debits = siblings.Where(x => x.ChangeAmount < 0).ToArray();
            if (debits.Length == 0 || siblings.Sum(x => (long)x.ChangeAmount) > 0)
                return Fail(row, $"custody parent {parent} credits more inventory than its recorded debits");
            return debits.All(Validate);
        }

        bool Fail(RoomInventoryAdjustment row, string detail)
        {
            failure ??= $"Ledger {row.Id}: {detail}.";
            return false;
        }

        string? MovementParent(TreatmentLineageMovement move) => move.RoomTransferId is long room ? $"room:{room}"
            : move.InterCrewTransferId is long crew ? $"crew:{crew}" : move.OutsideWarehouseTransferId is long outside ? $"outside:{outside}"
            : move.ProcessorShipmentLineId is long processor ? $"processor:{processor}" : move.RoomInventoryLossId is long loss ? $"loss:{loss}"
            : move.InventoryIdentityCorrectionId is Guid identity ? $"identity:{identity}"
            : move.BinsRunEntryId is long entry && runEntries.SingleOrDefault(x => x.Id == entry) is { } runEntry
                && all.TryGetValue(runEntry.InventoryAdjustmentId, out var adjustment) ? Parent(adjustment) : null;
    }

    private static bool Earlier(RoomInventoryAdjustment a, RoomInventoryAdjustment b) =>
        a.CreatedAt < b.CreatedAt || a.CreatedAt == b.CreatedAt && a.Id < b.Id;
    private static bool SamePosition(RoomInventoryAdjustment a, RoomInventoryAdjustment b) =>
        a.WarehouseId == b.WarehouseId && a.RoomId == b.RoomId && SameIdentity(a, b);
    private static bool SameIdentity(RoomInventoryAdjustment a, RoomInventoryAdjustment b) =>
        a.CropYear == b.CropYear && a.GrowerLotId == b.GrowerLotId && a.FruitProfileId == b.FruitProfileId
        && string.Equals(a.LotNumber.Trim(), b.LotNumber.Trim(), StringComparison.OrdinalIgnoreCase);
    private static string? Parent(RoomInventoryAdjustment row) => row.RoomTransferId is long room ? $"room:{room}"
        : row.InterCrewTransferId is long crew ? $"crew:{crew}" : row.OutsideWarehouseTransferId is long outside ? $"outside:{outside}"
        : row.ProcessorShipmentLineId is long processor ? $"processor:{processor}" : row.RoomInventoryLossId is long loss ? $"loss:{loss}"
        : row.InventoryIdentityCorrectionId is Guid identity ? $"identity:{identity}" : row.ReceiptInventoryOverrideId is Guid change ? $"receipt-change:{change}"
        : row.RoomDepletionId is long depletion ? $"depletion:{depletion}" : row.ActualRunId is long run ? $"run:{run}" : null;
    private static IEnumerable<string> Prefixes(string? key)
    {
        if (key == null) yield break;
        for (var index = key.IndexOf(':'); index >= 0; index = key.IndexOf(':', index + 1)) yield return key[..index];
    }
    private static async Task<List<T>> Read<T>(IQueryable<T> query, CancellationToken ct)
    {
        var rows = await query.Take(InventoryEvidenceLoader.MaximumEvidenceRowsPerTable + 1).ToListAsync(ct);
        if (rows.Count > InventoryEvidenceLoader.MaximumEvidenceRowsPerTable) throw new InvalidOperationException("Origin evidence exceeds the safe row bound.");
        return rows;
    }
}
