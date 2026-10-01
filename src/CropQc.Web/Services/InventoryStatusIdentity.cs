namespace CropQc.Web.Services;

public static class InventoryStatusIdentity
{
    public static string Normalize(string? status, string? productionType) =>
        CropQc.Shared.Inventory.InventoryStatusIdentity.Normalize(status, productionType);

    public static string NormalizeLineageKey(string key) =>
        CropQc.Shared.Inventory.InventoryStatusIdentity.NormalizeLineageKey(key);
}
