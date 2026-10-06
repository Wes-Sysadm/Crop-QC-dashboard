using CropQc.Data.Entities;

namespace CropQc.Data.Inventory;

/// <summary>ORAS is an organic product code, not a user-selected production status.</summary>
public static class OrasProductDefinition
{
    public const string Error = "ORAS means Organic Asian Pear. Its product definition must be Pear / Organic / organic. Ask an administrator to use the ORAS historical definition correction before receiving this product.";
    public static bool Applies(string? code) => string.Equals(code?.Trim(), "ORAS", StringComparison.OrdinalIgnoreCase);
    public static bool IsValid(FruitProfile profile) => !Applies(profile.VarietyCode)
        || profile.FruitType == "Pear" && profile.ProductionType == "Organic" && profile.IsOrganic;
}
