using System.Text.Json;
using CropQc.Data.Entities;

namespace CropQc.Data.Inventory;

/// <summary>Reads the recorded v1 Admin reclassification contract. This proves
/// an identity-changing custody credit, never treatment or exact surviving receipt ownership.</summary>
internal static class LegacyReceiptReclassificationOrigin
{
    internal static bool IsProven(ReceiptInventoryOverride change, RoomInventoryAdjustment[] legs,
        AuditLog[] audits, DateTimeOffset asOf)
    {
        if (!change.IsComplete || change.ActionType != ReceiptInventoryOverrideActionTypes.InventoryReclassification
            || change.CreatedAt > asOf || change.InventoryDelta != 0 || change.OldReceiptBinCount <= 0
            || change.OldReceiptBinCount != change.NewReceiptBinCount || change.CurrentInventoryBefore <= 0
            || change.CurrentInventoryBefore != change.CurrentInventoryAfter || change.CurrentInventoryBefore != change.OldReceiptBinCount
            || string.IsNullOrWhiteSpace(change.Reason) || !Guid.TryParse(change.OperationKey, out _)
            || change.ExpectedAdjustmentCount != legs.Length || legs.Length != 2 || audits.Length != 1)
            return false;
        var debit = legs[0]; var credit = legs[1]; var audit = audits[0];
        if (debit.ChangeAmount != -change.CurrentInventoryBefore || credit.ChangeAmount != change.CurrentInventoryAfter
            || debit.OldBinCount != change.CurrentInventoryBefore || debit.NewBinCount != 0
            || credit.OldBinCount != 0 || credit.NewBinCount != change.CurrentInventoryAfter
            || audit.Action != change.ActionType || audit.UserId != change.AdministratorUserId
            || audit.CreatedAt != change.CreatedAt || audit.SourceApplication != "CropQc.Web") return false;
        for (var index = 0; index < legs.Length; index++)
        {
            var leg = legs[index];
            if (leg.ReceiptId != change.ReceiptId || leg.ReceiptInventoryOverrideId != change.Id
                || leg.AdjustmentType != "ReceiptAdminOverride" || leg.InventoryInvariantVersion != 1
                || leg.InventoryOperationKey != $"receipt-override:{change.OperationKey}:{index + 1}"
                || leg.CreatedAt != change.CreatedAt || leg.AdjustmentAt != change.CreatedAt
                || leg.CreatedByUserId != change.AdministratorUserId || leg.Reason != change.Reason
                || leg.InventoryIdentityCorrectionId != null || leg.RoomTransferId != null || leg.InterCrewTransferId != null
                || leg.OutsideWarehouseTransferId != null || leg.ProcessorShipmentLineId != null || leg.ActualRunId != null || leg.ActualRunRevisionId != null
                || leg.RoomInventoryLossId != null || leg.RoomDepletionId != null) return false;
        }
        try
        {
            using var beforeDoc = JsonDocument.Parse(change.BeforeReceiptSnapshotJson);
            using var afterDoc = JsonDocument.Parse(change.AfterReceiptSnapshotJson);
            using var affectedDoc = JsonDocument.Parse(change.AffectedInventorySnapshotJson);
            using var auditBeforeDoc = JsonDocument.Parse(audit.BeforeValuesJson ?? "null");
            using var auditAfterDoc = JsonDocument.Parse(audit.AfterValuesJson ?? "null");
            var before = beforeDoc.RootElement; var after = afterDoc.RootElement; var result = auditAfterDoc.RootElement;
            if (!ReceiptMatches(before, debit, change.OldReceiptBinCount) || !ReceiptMatches(after, credit, change.NewReceiptBinCount)
                || before.GetProperty("receivedAt").GetDateTimeOffset() != after.GetProperty("receivedAt").GetDateTimeOffset()
                || before.GetProperty("receivedAt").GetDateTimeOffset() > change.CreatedAt
                || after.GetProperty("concurrencyVersion").GetInt64() != before.GetProperty("concurrencyVersion").GetInt64() + 1
                || !JsonElement.DeepEquals(before, auditBeforeDoc.RootElement)
                || result.GetProperty("inventoryDelta").GetInt32() != 0
                || result.GetProperty("currentInventoryBefore").GetInt32() != change.CurrentInventoryBefore
                || result.GetProperty("currentInventoryAfter").GetInt32() != change.CurrentInventoryAfter
                || result.GetProperty("reason").GetString() != change.Reason) return false;
            using var auditReceipt = JsonDocument.Parse(result.GetProperty("afterReceiptSnapshotJson").GetString()!);
            if (!JsonElement.DeepEquals(after, auditReceipt.RootElement)) return false;
            var affected = affectedDoc.RootElement.EnumerateArray().ToArray();
            if (affected.Length != 1 || !IdentityMatches(affected[0], debit, "lot")
                || !LocationMatches(affected[0], debit) || affected[0].GetProperty("currentBins").GetInt32() != -debit.ChangeAmount
                || affected[0].GetProperty("variety").GetString() != debit.VarietyCode
                || affected[0].GetProperty("inventoryStatus").GetString() != debit.InventoryStatus) return false;
            var auditedLegs = result.GetProperty("ledgerAdjustments").EnumerateArray().ToArray();
            return auditedLegs.Length == legs.Length && auditedLegs.Zip(legs).All(pair => LocationMatches(pair.First, pair.Second)
                && pair.First.GetProperty("changeAmount").GetInt32() == pair.Second.ChangeAmount
                && pair.First.GetProperty("newBinCount").GetInt32() == pair.Second.NewBinCount);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException or ArgumentException)
        { return false; }
    }

    private static bool ReceiptMatches(JsonElement snapshot, RoomInventoryAdjustment leg, int quantity) =>
        snapshot.GetProperty("id").GetInt64() == leg.ReceiptId && snapshot.GetProperty("binCount").GetInt32() == quantity
        && !snapshot.GetProperty("isDeleted").GetBoolean() && IdentityMatches(snapshot, leg, "lotCode") && LocationMatches(snapshot, leg);
    private static bool IdentityMatches(JsonElement snapshot, RoomInventoryAdjustment leg, string lotProperty) =>
        snapshot.GetProperty("cropYear").GetInt32() == leg.CropYear && snapshot.GetProperty("growerLotId").GetInt32() == leg.GrowerLotId
        && snapshot.GetProperty("fruitProfileId").GetInt32() == leg.FruitProfileId && snapshot.GetProperty(lotProperty).GetString() == leg.LotNumber;
    private static bool LocationMatches(JsonElement snapshot, RoomInventoryAdjustment leg) =>
        snapshot.GetProperty("warehouseId").GetInt32() == leg.WarehouseId && snapshot.GetProperty("roomId").GetInt32() == leg.RoomId;
}
