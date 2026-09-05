namespace CropQc.Web.Services;

public static class OperationalInventoryPosition
{
    public const string Operable = "Operable";
    public const string NeedsReconciliation = "NeedsReconciliation";

    public static string Key(RoomInventoryLedgerSnapshot snapshot) =>
        string.Join(':',
            "P",
            snapshot.WarehouseId,
            snapshot.RoomId,
            snapshot.CropYear?.ToString() ?? "-",
            snapshot.GrowerLotId?.ToString() ?? "-",
            snapshot.FruitProfileId?.ToString() ?? "-",
            Normalize(snapshot.GrowerNumber),
            Normalize(snapshot.ProductionType),
            snapshot.IsOrganic?.ToString() ?? "-");

    public static string CanonicalIdentityKey(RoomInventoryLedgerSnapshot snapshot) =>
        string.Join(':',
            snapshot.CropYear?.ToString() ?? "-",
            snapshot.GrowerLotId?.ToString() ?? "-",
            snapshot.FruitProfileId?.ToString() ?? "-",
            Normalize(snapshot.GrowerNumber),
            Normalize(snapshot.ProductionType),
            snapshot.IsOrganic?.ToString() ?? "-");

    public static string? UnavailableReason(RoomInventoryLedgerSnapshot snapshot)
    {
        if (snapshot.CropYear is null) return "Needs Reconciliation — crop year is missing from immutable inventory evidence.";
        if (snapshot.GrowerLotId is null) return "Needs Reconciliation — canonical Grower Lot is not proven by immutable inventory evidence.";
        if (snapshot.FruitProfileId is null) return "Needs Reconciliation — canonical Fruit Profile is missing.";
        if (string.IsNullOrWhiteSpace(snapshot.GrowerNumber)) return "Needs Reconciliation — grower identity is ambiguous.";
        if (string.IsNullOrWhiteSpace(snapshot.ProductionType) || snapshot.IsOrganic is null)
            return "Needs Reconciliation — Organic/Conventional identity is incomplete.";
        return null;
    }

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? "-"
            : string.Concat(value.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit));
}
