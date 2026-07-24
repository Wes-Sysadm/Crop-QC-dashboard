using CropQc.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IFacilityContextService
{
    IReadOnlyList<string> SelectableFacilities { get; }
    string Normalize(string? facility);
    string GetFacilityCode(string? warehouseCode, string? warehouseName = null);
    bool Matches(string? warehouseCode, string? warehouseName, string? selectedFacility);
    Task<IReadOnlySet<int>> GetWarehouseIdsAsync(string? selectedFacility, CancellationToken cancellationToken);
}

/// <summary>
/// Centralizes the operational facility viewing context. Warehouse records remain the
/// persisted identity; aliases are used only here to map legacy warehouse codes/names
/// to the stable WP/EBS facility context.
/// </summary>
public sealed class FacilityContextService(CropQcDbContext dbContext) : IFacilityContextService
{
    private static readonly string[] Facilities = ["All", "MCD", "WP", "EBS", "DH"];

    public IReadOnlyList<string> SelectableFacilities => Facilities;

    public string Normalize(string? facility)
    {
        var candidate = string.IsNullOrWhiteSpace(facility) ? "All" : facility.Trim();
        return Facilities.FirstOrDefault(x => x.Equals(candidate, StringComparison.OrdinalIgnoreCase)) ?? "All";
    }

    public string GetFacilityCode(string? warehouseCode, string? warehouseName = null)
    {
        var code = warehouseCode?.Trim() ?? "";
        var name = warehouseName?.Trim() ?? "";

        if (code.Equals("WP", StringComparison.OrdinalIgnoreCase)) return "WP";
        if (code.Equals("EBS", StringComparison.OrdinalIgnoreCase)) return "EBS";
        if (code.Equals("MCD", StringComparison.OrdinalIgnoreCase)) return "MCD";
        if (code.Equals("DH", StringComparison.OrdinalIgnoreCase)) return "DH";

        var combined = $"{code} {name}".Trim();
        if (combined.Contains("McDougall", StringComparison.OrdinalIgnoreCase)) return "MCD";
        if (combined.Contains("Earl Brown", StringComparison.OrdinalIgnoreCase)) return "EBS";
        if (combined.Contains("Windy Point", StringComparison.OrdinalIgnoreCase)) return "WP";
        return string.IsNullOrWhiteSpace(code) ? "Other" : code.ToUpperInvariant();
    }

    public bool Matches(string? warehouseCode, string? warehouseName, string? selectedFacility)
    {
        var selected = Normalize(selectedFacility);
        return selected == "All" || GetFacilityCode(warehouseCode, warehouseName).Equals(selected, StringComparison.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlySet<int>> GetWarehouseIdsAsync(string? selectedFacility, CancellationToken cancellationToken)
    {
        var selected = Normalize(selectedFacility);
        var warehouses = await dbContext.Warehouses
            .AsNoTracking()
            .Select(x => new { x.Id, x.Code, x.Name })
            .ToListAsync(cancellationToken);

        return warehouses
            .Where(x => selected == "All" || Matches(x.Code, x.Name, selected))
            .Select(x => x.Id)
            .ToHashSet();
    }
}
