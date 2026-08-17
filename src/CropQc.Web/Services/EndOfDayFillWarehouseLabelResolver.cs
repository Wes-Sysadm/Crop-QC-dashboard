namespace CropQc.Web.Services;

public interface IEndOfDayFillWarehouseLabelResolver
{
    string Resolve(int warehouseId, string? warehouseCode, string? warehouseName = null);
}

public sealed class EndOfDayFillWarehouseLabelResolver : IEndOfDayFillWarehouseLabelResolver
{
    public string Resolve(int warehouseId, string? warehouseCode, string? warehouseName = null)
    {
        var code = warehouseCode?.Trim() ?? "";
        var name = warehouseName?.Trim() ?? "";
        if (warehouseId == 3
            && code.Equals("McDougall", StringComparison.OrdinalIgnoreCase)
            && name.Equals("McDougall", StringComparison.OrdinalIgnoreCase))
        {
            return "MCD";
        }

        return string.IsNullOrWhiteSpace(code) ? $"Warehouse {warehouseId}" : code.ToUpperInvariant();
    }
}
