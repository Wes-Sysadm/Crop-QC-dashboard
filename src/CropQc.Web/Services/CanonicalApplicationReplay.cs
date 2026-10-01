using System.Text.Json;
using CropQc.Data;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

internal static class CanonicalApplicationReplay
{
    public static async Task<InventoryCommandResult?> TryAsync(CropQcDbContext db, IInventoryCommandExecutor commands,
        string key, int actor, InventoryCommandKind kind, string submission, CancellationToken ct)
    {
        var prior = await db.InventoryCommands.AsNoTracking().SingleOrDefaultAsync(x => x.OperationKey == key, ct);
        if (prior == null) return null;
        var command = JsonSerializer.Deserialize<InventoryCommand>(prior.IntentJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        return command.ActorId == actor && command.Kind == kind && command.ApplicationIntent == submission
            ? await commands.ExecuteAsync(command, ct)
            : new(InventoryCommandStatus.Conflict, key, "Operation key was used with different input.", []);
    }
}
