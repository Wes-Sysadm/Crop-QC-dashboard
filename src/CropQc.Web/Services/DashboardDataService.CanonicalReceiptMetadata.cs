using System.Text.Json;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Models;

namespace CropQc.Web.Services;

public sealed partial class DashboardDataService
{
    private async Task<string?> UpdateCanonicalReceiptMetadataAsync(UpdateReceiptForm form, Receipt receipt, GrowerLot? lot, string type, CancellationToken ct)
    {
        if (canonicalCommands == null) return "Canonical receipt editing is unavailable.";
        if (!await HasAccessAsync(ApplicationAreas.Receipts, PageAccessLevel.Create, ct)) return "Receipts Edit access is required.";
        var actor = await GetCurrentUserAsync(ct);
        if (actor == null) return "The active operator could not be resolved.";
        var number = lot?.LotNumber ?? form.GrowerNumber.Trim();
        if (!string.Equals(number, receipt.GrowerNumber ?? receipt.LotCode, StringComparison.OrdinalIgnoreCase))
            return "Identity changes require an administrator inventory correction.";
        var resolver = await (canonicalGrowerService ?? new CanonicalGrowerService(dbContext)).LoadResolutionSetAsync(ct);
        var name = resolver.DisplayName(lot?.Grower ?? form.GrowerName.Trim(), number);
        var metadata = new InventoryReceiptMetadata(form.Id, form.ReceiptVersion, form.ReceivedAt, form.CompuTechReceiptId.Trim(), name,
            new(form.CropYear, form.WarehouseId, form.RoomId, form.GrowerLotId ?? 0, form.FruitProfileId, receipt.CompuTechReceiptId, form.BinCount, type));
        return CanonicalInventoryMessages.Result(await new CanonicalReceiptMetadataService(dbContext, canonicalCommands)
            .UpdateAsync(form.OperationKey, actor.Id, metadata, "Ordinary receipt metadata edit", JsonSerializer.Serialize(form), ct));
    }
}
