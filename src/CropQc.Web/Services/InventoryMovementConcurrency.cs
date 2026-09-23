using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public static class InventoryMovementConcurrency
{
    public const string RefreshMessage = "Inventory changed during the transfer. Refresh and review the current room inventory before retrying.";

    public static bool IsConflict(Exception exception)
    {
        // Npgsql can wrap serialization failures more than one level deep.
        for (Exception? current = exception; current is not null; current = current.InnerException)
            if (current is DbUpdateConcurrencyException
                || current is Npgsql.PostgresException { SqlState: "40001" or "40P01" or "23505" }) return true;
        return false;
    }
}
