namespace CropQc.Shared.Inventory;

public static class CanonicalInventoryMessages
{
    public static string? Blocker(InventoryAvailabilityResult result)
    {
        if (result.IsOperable) return null;
        if (result.Blockers.Any(x => x.Code == InventoryBlockerCode.StaleRead)) return "Inventory changed; reload and try again.";
        if (result.Blockers.Any(x => x.Code == InventoryBlockerCode.MissingReceiptProvenance)) return "Receipt-specific allocation is ambiguous.";
        if (result.Blockers.Any(x => x.Code == InventoryBlockerCode.InvalidCustody)) return "Inventory custody is inconsistent.";
        if (result.AuthoritativeQuantity < 0) return "This inventory has a negative balance and requires administrator review.";
        return "Inventory identity or treatment cannot be proven. Ask an administrator to review it.";
    }
    public static string? Result(InventoryCommandResult result) => result.Status switch
    {
        InventoryCommandStatus.Committed or InventoryCommandStatus.Replayed => null,
        InventoryCommandStatus.Stale or InventoryCommandStatus.RetryRequired => "Inventory changed; reload and try again.",
        InventoryCommandStatus.Conflict => "This operation conflicts with an earlier submission. Reload and review its status.",
        _ => result.Detail
    };
}
