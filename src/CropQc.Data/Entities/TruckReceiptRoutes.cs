namespace CropQc.Data.Entities;

public static class TruckReceiptRoutes
{
    // Dispatch support is distinct from company membership. McDougall's existing
    // internal loads travel in transit to WP/DH without Truck Receipt matching.
    public static IReadOnlyList<string> InterCrewDestinations(string? sourceCode) => sourceCode?.Trim().ToUpperInvariant() switch
    {
        "WP" or "DH" => [TransferCustodyGroups.Ebs],
        "EBS" => [TransferCustodyGroups.WpDh],
        "MCD" or "MCDOUGALL" => [TransferCustodyGroups.WpDh, TransferCustodyGroups.Ebs],
        _ => []
    };

    public static bool AllowsInterCrewDispatch(string? sourceCode, string? destinationGroup) =>
        InterCrewDestinations(sourceCode).Contains(destinationGroup, StringComparer.Ordinal);

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
