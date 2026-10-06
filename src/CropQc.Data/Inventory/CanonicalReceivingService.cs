using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

/// <summary>Shared Web/API adapter; physical creation belongs exclusively to the command executor.</summary>
public sealed class CanonicalReceivingService(CropQcDbContext db, IInventoryCommandExecutor commands)
{
    public async Task<InventoryCommandResult> ReceiveAsync(string operationKey, int actor, int crop, DateTimeOffset at,
        int warehouse, int room, int profile, int? growerLot, string lot, string receiptNumber, int quantity, string submission, CancellationToken ct)
    {
        if (!db.CanonicalInventoryEnabled) throw new InvalidOperationException("Canonical receiving is not active.");
        if (growerLot == null)
        {
            var matches = await db.GrowerLots.AsNoTracking().Where(x => x.IsActive && x.LotNumber == lot).Select(x => x.Id).Take(2).ToArrayAsync(ct);
            if (matches.Length != 1) return new(InventoryCommandStatus.Blocked, operationKey, "Select one reviewed Grower Number.", []);
            growerLot = matches[0];
        }
        return await commands.ExecuteAsync(new(operationKey, InventoryCommandKind.ReceiveStock, actor, at, "Ordinary receiving", [],
            ApplicationIntent: submission, Receipt: new(crop, warehouse, room, growerLot.Value, profile, receiptNumber.Trim(), quantity)), ct);
    }
}
