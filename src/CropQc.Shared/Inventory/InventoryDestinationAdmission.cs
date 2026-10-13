using System.Collections.Immutable;

namespace CropQc.Shared.Inventory;

public sealed record InventoryDestinationAssessment(bool Allowed, ImmutableArray<string> Failures,
    bool RequiresReconciliation);

/// <summary>
/// Incoming bins are proven at their source/origin. A destination projection is
/// neither that proof nor a veto on that proof. Writers must create an isolated
/// event-backed cohort and verify the exact ledger/projection delta atomically.
/// </summary>
public static class InventoryDestinationAdmission
{
    public static InventoryDestinationAssessment Assess(InventoryPositionEvidence evidence)
    {
        var failures = ImmutableArray.CreateBuilder<string>();
        if (evidence.AuthoritativeQuantity < 0)
            failures.Add("Destination ledger has a negative quantity; identify the recorded event causing it.");
        if (!evidence.CustodyVerified || evidence.Location.Custody != InventoryCustody.Room
            || evidence.CommittedQuantity < 0 || evidence.CommittedQuantity > evidence.AuthoritativeQuantity)
            failures.Add("Destination recorded custody is invalid.");
        if (!evidence.Identity.IsComplete || !evidence.IdentityVerified && !evidence.Ledger.IsDefaultOrEmpty)
            failures.Add("Destination ledger identity cannot be matched to the proposed inventory identity.");
        if (evidence.HistoricalSnapshotUnavailable)
            failures.Add("Destination evidence changed after the transaction cutoff; refresh the operation.");
        var diagnostic = InventoryAvailabilityResolver.Resolve(evidence, new());
        return new(failures.Count == 0, failures.ToImmutable(), !diagnostic.IsOperable
            || diagnostic.RawProjectionQuantity != diagnostic.AuthoritativeQuantity);
    }
}
