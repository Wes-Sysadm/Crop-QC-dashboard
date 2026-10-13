using System.Collections.Immutable;
using System.Text.Json;
using CropQc.Data.Entities;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

/// <summary>Diagnostic only. Revision arithmetic never grants reconstruction or exact custody.</summary>
public static class ReceiptRevisionEvidenceValidator
{
    public static async Task<ImmutableArray<ReceiptRevisionAssessment>> EvaluateAsync(CropQcDbContext db,
        InventoryPositionEvidence evidence, CancellationToken ct = default)
    {
        var ids = evidence.Receipts.Select(x => x.Id).ToArray();
        var corrections = await db.ReceiptInventoryOverrides.AsNoTracking().Where(x => ids.Contains(x.ReceiptId))
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(ct);
        var keys = corrections.Select(x => x.Id.ToString()).ToArray();
        var audits = await db.AuditLogs.AsNoTracking().Where(x => keys.Contains(x.EntityKey)
            && (x.EntityName == "ReceiptInventoryOverride" || x.EntityName == "CanonicalInventory" && x.SourceApplication == "CanonicalInventory/v1"))
            .OrderBy(x => x.Id).ToListAsync(ct);
        var correctionIds = corrections.Select(x => x.Id).ToArray();
        var ledger = await db.RoomInventoryAdjustments.AsNoTracking().Where(x => x.ReceiptInventoryOverrideId != null
            && correctionIds.Contains(x.ReceiptInventoryOverrideId.Value)).ToListAsync(ct);
        var arrivals = await db.RoomInventoryAdjustments.AsNoTracking().Where(x => ids.Contains(x.ReceiptId ?? -1)
            && x.AdjustmentType == "ReceiptAdd").ToListAsync(ct);
        var result = ImmutableArray.CreateBuilder<ReceiptRevisionAssessment>();
        foreach (var group in corrections.GroupBy(x => x.ReceiptId))
        {
            var current = await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == group.Key, ct);
            result.Add(Evaluate(evidence, current, group.ToArray(), ledger, audits, arrivals));
        }
        return result.ToImmutable();
    }

    public static ReceiptRevisionAssessment Evaluate(InventoryPositionEvidence evidence, Receipt current,
        IReadOnlyList<ReceiptInventoryOverride> revisions, IReadOnlyList<RoomInventoryAdjustment> ledger,
        IReadOnlyList<AuditLog> audits, IReadOnlyList<RoomInventoryAdjustment>? originalArrivals = null)
    {
        var problems = new List<string>();
        var relevant = revisions.Where(x => x.ReceiptId == current.Id).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToArray();
        var auditIds = new List<long>();
        var originalQuantity = relevant.FirstOrDefault()?.OldReceiptBinCount ?? current.BinCount;
        JsonElement? previousAfter = null;
        foreach (var revision in relevant)
        {
            try
            {
                using var beforeDoc = JsonDocument.Parse(revision.BeforeReceiptSnapshotJson);
                using var afterDoc = JsonDocument.Parse(revision.AfterReceiptSnapshotJson);
                var before = beforeDoc.RootElement; var after = afterDoc.RootElement;
                bool Same(JsonElement a, JsonElement b, string field) => a.GetProperty(field).ToString() == b.GetProperty(field).ToString();
                string[] identityFields = ["id", "cropYear", "warehouseId", "roomId", "fruitProfileId", "growerLotId", "growerNumber", "lotCode", "receiptType", "receivedAt"];
                var quantity = revision.ActionType == ReceiptInventoryOverrideActionTypes.VoidReceipt
                    ? -before.GetProperty("binCount").GetInt32() : after.GetProperty("binCount").GetInt32() - before.GetProperty("binCount").GetInt32();
                var matches = audits.Where(x => x.EntityKey == revision.Id.ToString()
                    && (x.EntityName == "ReceiptInventoryOverride" && x.Action == revision.ActionType
                        || x.EntityName == "CanonicalInventory" && x.SourceApplication == "CanonicalInventory/v1"
                        && x.Action == (revision.ActionType == ReceiptInventoryOverrideActionTypes.VoidReceipt ? "CanonicalReceiptVoided" : "CanonicalReceiptQuantityCorrected"))).ToArray();
                var changes = ledger.Where(x => x.ReceiptInventoryOverrideId == revision.Id).ToArray();
                if (!revision.IsComplete || revision.ActionType is not (ReceiptInventoryOverrideActionTypes.VoidReceipt or ReceiptInventoryOverrideActionTypes.QuantityCorrection)
                    || string.IsNullOrWhiteSpace(revision.OperationKey) || relevant.Count(x => x.OperationKey == revision.OperationKey) != 1
                    || identityFields.Any(f => !Same(before, after, f)) || before.GetProperty("id").GetInt64() != current.Id
                    || before.GetProperty("binCount").GetInt32() != revision.OldReceiptBinCount
                    || (revision.ActionType == ReceiptInventoryOverrideActionTypes.VoidReceipt
                        ? revision.NewReceiptBinCount != 0 || !Same(before, after, "binCount")
                        : after.GetProperty("binCount").GetInt32() != revision.NewReceiptBinCount)
                    || after.GetProperty("concurrencyVersion").GetInt64() != before.GetProperty("concurrencyVersion").GetInt64() + 1
                    || before.GetProperty("isDeleted").GetBoolean()
                    || after.GetProperty("isDeleted").GetBoolean() != (revision.ActionType == ReceiptInventoryOverrideActionTypes.VoidReceipt)
                    || quantity != revision.InventoryDelta || changes.Length != revision.ExpectedAdjustmentCount
                    || changes.Sum(x => x.ChangeAmount) != quantity || changes.Any(x => x.ReceiptId != current.Id
                        || x.RoomId != before.GetProperty("roomId").GetInt32() || x.WarehouseId != before.GetProperty("warehouseId").GetInt32()
                        || x.GrowerLotId != before.GetProperty("growerLotId").GetInt32() || x.FruitProfileId != before.GetProperty("fruitProfileId").GetInt32()
                        || x.CreatedAt != revision.CreatedAt || x.AdjustmentType != "ReceiptAdminOverride")
                    || matches.Length != 1 || matches[0].CreatedAt != revision.CreatedAt
                    || matches[0].UserId != revision.AdministratorUserId)
                    problems.Add($"Correction {revision.Id} lacks an exact complete quantity-only revision, ledger or audit chain.");
                else
                {
                    using var auditBefore = ParseSnapshot(matches[0].BeforeValuesJson!);
                    using var auditAfter = JsonDocument.Parse(matches[0].AfterValuesJson!);
                    using var recordedAfter = JsonDocument.Parse(auditAfter.RootElement.GetProperty("afterReceiptSnapshotJson").GetString()!);
                    if (before.GetRawText() != auditBefore.RootElement.GetRawText() || after.GetRawText() != recordedAfter.RootElement.GetRawText())
                        problems.Add($"Audit snapshots differ for correction {revision.Id}.");
                    auditIds.Add(matches[0].Id);
                }
                if (previousAfter is JsonElement previous && (identityFields.Any(f => !Same(previous, before, f))
                    || !Same(previous, before, "binCount") || !Same(previous, before, "concurrencyVersion") || !Same(previous, before, "isDeleted")))
                    problems.Add($"Missing intermediate revision before {revision.Id}.");
                previousAfter = after.Clone();
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
            { problems.Add($"Malformed or incomplete snapshots for correction {revision.Id}."); }
        }
        try
        {
            if (previousAfter is JsonElement last && (last.GetProperty("binCount").GetInt32() != current.BinCount
                || last.GetProperty("concurrencyVersion").GetInt64() != current.ConcurrencyVersion
                || last.GetProperty("isDeleted").GetBoolean() != current.IsDeleted
                || last.GetProperty("roomId").GetInt32() != current.RoomId || last.GetProperty("warehouseId").GetInt32() != current.WarehouseId
                || last.GetProperty("growerLotId").GetInt32() != current.GrowerLotId || last.GetProperty("fruitProfileId").GetInt32() != current.FruitProfileId
                || last.GetProperty("cropYear").GetInt32() != current.CropYear || last.GetProperty("growerNumber").GetString() != current.GrowerNumber
                || last.GetProperty("lotCode").GetString() != current.LotCode || last.GetProperty("receiptType").GetString() != current.ReceiptType
                || last.GetProperty("receivedAt").GetDateTimeOffset() != current.ReceivedAt))
                problems.Add("Current receipt does not match the final audited revision.");
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        { problems.Add("Final receipt snapshot is incomplete."); }
        var arrival = originalArrivals == null
            ? evidence.Ledger.Where(x => x.ReceiptId == current.Id && x.Kind == "ReceiptAdd").ToArray()
            : originalArrivals.Where(x => x.ReceiptId == current.Id && x.AdjustmentType == "ReceiptAdd")
                .Select(x => new InventoryLedgerEvidence(x.Id, x.ChangeAmount, x.AdjustmentType, x.AdjustmentAt,
                    x.ReceiptId, null, x.CropYear == current.CropYear && x.GrowerLotId == current.GrowerLotId
                        && x.FruitProfileId == current.FruitProfileId, x.CreatedAt)).ToArray();
        if (arrival.Length != 1 || arrival[0].Quantity != originalQuantity || !arrival[0].ExactIdentity)
            problems.Add("Original receipt arrival quantity/identity is not uniquely established.");
        var chainValid = relevant.Length > 0 && problems.Count == 0;
        var pool = InventoryAvailabilityResolver.Resolve(evidence, new());
        var exact = InventoryAvailabilityResolver.Resolve(evidence, new(RequireExactReceipt: true, ReceiptId: current.Id));
        foreach (var debit in evidence.Ledger.Where(x => x.Quantity < 0))
        {
            var movements = evidence.Movements.Where(x => x.Outgoing && x.Parent == debit.MovementParent).ToArray();
            if (debit.MovementParent == null || movements.Length == 0 || movements.Any(x => !x.ExactIdentity)
                || movements.Sum(x => x.Quantity) != -debit.Quantity)
                problems.Add($"Ledger debit {debit.Id}: the ordinary movement-only proof is incomplete; validate its committed revision or packing parent against the full recorded pool history.");
        }
        if (!pool.IsOperable || pool.TreatmentConfidence != InventoryConfidence.Proven)
            problems.Add("The ordinary resolver does not prove the corrected pool; assess original arrivals, committed revision effects, parent records and point-in-time treatment together.");
        if (!exact.IsOperable || exact.ReceiptProvenance.Confidence != InventoryConfidence.Proven)
            problems.Add("Exact surviving receipt allocation is unresolved; retain shared custody unless recorded allocation events establish it. Correction reasons are not allocation evidence.");
        return new(current.Id, chainValid, exact.IsOperable && exact.ReceiptProvenance.Confidence == InventoryConfidence.Proven,
            pool.IsOperable && pool.TreatmentConfidence == InventoryConfidence.Proven
                && pool.TreatmentSlices.All(x => x.Signature == "u" && x.State == "Untreated" && x.ApplicationIds.IsEmpty), originalQuantity, current.BinCount,
            problems.ToImmutableArray(), relevant.Select(x => x.Id.ToString()).ToImmutableArray(), auditIds.ToImmutableArray(), current.IsDeleted);
    }

    private static JsonDocument ParseSnapshot(string json)
    {
        using var value = JsonDocument.Parse(json);
        return JsonDocument.Parse(value.RootElement.ValueKind == JsonValueKind.String ? value.RootElement.GetString()! : json);
    }
}
