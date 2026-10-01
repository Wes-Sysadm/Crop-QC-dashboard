using System.Text.Json;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

public sealed class CanonicalReceiptMetadataService(CropQcDbContext db, IInventoryCommandExecutor commands)
{
    public async Task<InventoryCommandResult> UpdateAsync(string key, int actor, InventoryReceiptMetadata metadata, string reason, string submission, CancellationToken ct)
    {
        var prior = await db.InventoryCommands.AsNoTracking().SingleOrDefaultAsync(x => x.OperationKey == key, ct);
        if (prior != null)
        {
            var old = JsonSerializer.Deserialize<InventoryCommand>(prior.IntentJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            return old.Kind == InventoryCommandKind.UpdateReceiptMetadata && old.ActorId == actor && old.ApplicationIntent == submission
                ? await commands.ExecuteAsync(old, ct) : new(InventoryCommandStatus.Conflict, key, "Operation key was used with different input.", []);
        }
        return await commands.ExecuteAsync(new(key, InventoryCommandKind.UpdateReceiptMetadata, actor, DateTimeOffset.UtcNow,
            reason, [], ReceiptMetadata: metadata, ApplicationIntent: submission), ct);
    }
}
