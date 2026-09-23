namespace CropQc.Web.Services;

/// <summary>Inventory status identity, independent of legacy display spelling.</summary>
public static class InventoryStatusIdentity
{
    public static string Normalize(string? status, string? productionType)
    {
        var value = (status ?? "").Trim().ToUpperInvariant();
        // Older transfers copied production type into status. It is already an
        // identity field; its redundant display value is not a second bucket.
        return value == (productionType ?? "").Trim().ToUpperInvariant() ? "" : value;
    }

    public static string NormalizeLineageKey(string key)
    {
        var parts = key.Split('|');
        if (parts.Length != 9) return key; // Never reinterpret unknown formats.
        for (var i = 0; i < parts.Length; i++) parts[i] = parts[i].Trim().ToUpperInvariant();
        if (bool.TryParse(parts[7], out var organic)) parts[7] = organic.ToString();
        parts[8] = Normalize(parts[8], parts[6]);
        return string.Join('|', parts);
    }
}
