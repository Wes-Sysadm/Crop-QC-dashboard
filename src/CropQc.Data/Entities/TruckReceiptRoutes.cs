namespace CropQc.Data.Entities;

public static class TruckReceiptRoutes
{
    // Warehouse.Code is the existing custody configuration. Never infer company from a display name or ID.
    public static string? Group(string? code) => code?.Trim().ToUpperInvariant() switch
    {
        "EBS" => TransferCustodyGroups.Ebs,
        "WP" or "DH" or "MCD" or "MCDOUGALL" => TransferCustodyGroups.WpDh,
        _ => null
    };

    public static bool RequiresReceipt(string? sourceCode, string? destinationCode) =>
        Group(sourceCode) is string source && Group(destinationCode) is string destination && source != destination;

    public static bool RequiresReceiptForGroup(string? sourceCode, string destinationGroup) =>
        Group(sourceCode) is string source && TransferCustodyGroups.IsValid(destinationGroup) && source != destinationGroup;
}
