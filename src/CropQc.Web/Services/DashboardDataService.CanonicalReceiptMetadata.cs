using System.Text.Json;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public sealed partial class DashboardDataService
{
    private async Task<string?> UpdateCanonicalReceiptMetadataAsync(UpdateReceiptForm form, Receipt receipt, GrowerLot? lot, string type, CancellationToken ct)
    {
        if (canonicalCommands == null) return "Canonical receipt editing is unavailable.";
        if (!await HasAccessAsync(ApplicationAreas.Receipts, PageAccessLevel.Create, ct)) return "Receipts Edit access is required.";
        var actor = await GetCurrentUserAsync(ct);
        if (actor == null) return "The active operator could not be resolved.";
        var submission = JsonSerializer.Serialize(form);
        var prior = await dbContext.InventoryCommands.AsNoTracking().SingleOrDefaultAsync(x => x.OperationKey == form.OperationKey, ct);
        if (prior != null)
        {
            var command = JsonSerializer.Deserialize<InventoryCommand>(prior.IntentJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            if (command.Kind is not (InventoryCommandKind.UpdateReceiptMetadata or InventoryCommandKind.ActivateReceiptInventory)
                || command.ActorId != actor.Id || command.ApplicationIntent != submission) return "The save identifier was used with different input.";
            return CanonicalInventoryMessages.Result(await canonicalCommands.ExecuteAsync(command, ct));
        }
        if (IsInventoryReceiptType(type) && !await dbContext.RoomInventoryAdjustments.AnyAsync(x => x.ReceiptId == receipt.Id, ct))
        {
            if (lot == null) return "Select the reviewed Grower Number before adding this receipt to inventory.";
            return CanonicalInventoryMessages.Result(await canonicalCommands.ExecuteAsync(new(form.OperationKey, InventoryCommandKind.ActivateReceiptInventory,
                actor.Id, form.ReceivedAt, "Add ordinary receipt to inventory", [], Receipt: new(form.CropYear, form.WarehouseId, form.RoomId,
                    lot.Id, form.FruitProfileId, form.CompuTechReceiptId.Trim(), form.BinCount), PhysicalParentId: receipt.Id,
                ExpectedParentVersion: form.ReceiptVersion, ApplicationIntent: submission), ct));
        }
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
