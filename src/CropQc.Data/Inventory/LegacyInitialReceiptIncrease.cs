using System.Text.Json;
using CropQc.Data.Entities;

namespace CropQc.Data.Inventory;

/// <summary>Reads the old initial receiving-increase format: one arrival,
/// one quantity-only edit and its immutable before/after audit. No treatment grant.</summary>
internal static class LegacyInitialReceiptIncrease
{
    internal static bool IsProven(Receipt receipt, RoomInventoryAdjustment origin, RoomInventoryAdjustment edit,
        AuditLog[] audits, DateTimeOffset asOf)
    {
        if (receipt.IsTransferReceipt || receipt.IsDeleted || receipt.ConcurrencyVersion != 1 || audits.Length != 1
            || receipt.ReceiptType != "Truck receipt" || origin.ChangeAmount <= 0 || edit.ChangeAmount <= 0
            || origin.NewBinCount != origin.ChangeAmount || edit.OldBinCount != origin.ChangeAmount
            || origin.InventoryInvariantVersion != 0 || origin.InventoryOperationKey != null
            || edit.NewBinCount != receipt.BinCount || (long)origin.ChangeAmount + edit.ChangeAmount != receipt.BinCount
            || origin.CreatedAt >= receipt.UpdatedAt || receipt.UpdatedAt > edit.AdjustmentAt
            || edit.AdjustmentAt > edit.CreatedAt || edit.CreatedAt > audits[0].CreatedAt || audits[0].CreatedAt > asOf
            || origin.AdjustmentAt != receipt.ReceivedAt || receipt.ReceivedAt > origin.CreatedAt
            || edit.InventoryInvariantVersion != 0 || edit.InventoryOperationKey != null
            || edit.Reason != "Receipt source adjustment" || edit.CreatedByUserId == null
            || audits[0].Action != "Update" || audits[0].SourceApplication != "Web"
            || audits[0].UserId != edit.CreatedByUserId) return false;
        foreach (var leg in new[] { origin, edit })
            if (leg.ReceiptId != receipt.Id || leg.CropYear != receipt.CropYear || leg.GrowerLotId != receipt.GrowerLotId
                || leg.FruitProfileId != receipt.FruitProfileId || leg.LotNumber != receipt.LotCode
                || leg.WarehouseId != receipt.WarehouseId || leg.RoomId != receipt.RoomId
                || leg.ReceiptInventoryOverrideId != null || leg.InventoryIdentityCorrectionId != null
                || leg.RoomTransferId != null || leg.InterCrewTransferId != null || leg.OutsideWarehouseTransferId != null
                || leg.ProcessorShipmentLineId != null || leg.ActualRunId != null || leg.ActualRunRevisionId != null || leg.RoomInventoryLossId != null
                || leg.RoomDepletionId != null) return false;
        try
        {
            using var before = JsonDocument.Parse(audits[0].BeforeValuesJson ?? "null");
            using var after = JsonDocument.Parse(audits[0].AfterValuesJson ?? "null");
            return Matches(before.RootElement, receipt, origin.ChangeAmount) && Matches(after.RootElement, receipt, receipt.BinCount)
                && before.RootElement.EnumerateObject().Count() == after.RootElement.EnumerateObject().Count()
                && before.RootElement.EnumerateObject().Where(x => x.Name != "BinCount")
                    .All(x => JsonElement.DeepEquals(x.Value, after.RootElement.GetProperty(x.Name)));
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException or OverflowException)
        { return false; }
    }

    private static bool Matches(JsonElement snapshot, Receipt receipt, int quantity) =>
        snapshot.GetProperty("CropYear").GetInt32() == receipt.CropYear
        && snapshot.GetProperty("ReceivedAt").GetDateTimeOffset() == receipt.ReceivedAt
        && snapshot.GetProperty("CompuTechReceiptId").GetString() == receipt.CompuTechReceiptId
        && snapshot.GetProperty("ReceiptType").GetString() == receipt.ReceiptType
        && snapshot.GetProperty("WarehouseId").GetInt32() == receipt.WarehouseId
        && snapshot.GetProperty("RoomId").GetInt32() == receipt.RoomId
        && snapshot.GetProperty("FruitProfileId").GetInt32() == receipt.FruitProfileId
        && snapshot.GetProperty("GrowerLotId").GetInt32() == receipt.GrowerLotId
        && snapshot.GetProperty("GrowerNumber").GetString() == receipt.GrowerNumber
        && snapshot.GetProperty("GrowerName").GetString() == receipt.GrowerName
        && snapshot.GetProperty("LotCode").GetString() == receipt.LotCode
        && snapshot.GetProperty("BinCount").GetInt32() == quantity;
}
