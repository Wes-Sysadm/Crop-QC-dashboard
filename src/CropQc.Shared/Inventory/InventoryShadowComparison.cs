using System.Collections.Immutable;

namespace CropQc.Shared.Inventory;

public sealed record InventoryWorkflowObservation(string Workflow, int AvailableQuantity,
    ImmutableArray<string> TreatmentInterpretation, ImmutableArray<string> ReceiptInterpretation, string? Blocker);
public sealed record InventoryWorkflowDivergence(string Workflow, int ExistingAvailable, int CanonicalAvailable,
    bool QuantityDiffers, bool TreatmentDiffers, string Explanation);
public sealed record InventoryShadowPosition(InventoryAvailabilityResult Canonical,
    ImmutableArray<InventoryWorkflowObservation> Existing, ImmutableArray<InventoryWorkflowDivergence> Divergences);

public static class InventoryShadowComparison
{
    public static InventoryShadowPosition Compare(InventoryAvailabilityResult canonical, IEnumerable<InventoryWorkflowObservation> existing)
    {
        var observations = existing.ToImmutableArray();
        var signatures = canonical.TreatmentSlices.Select(x => x.Signature).Distinct().Order().ToArray();
        return new(canonical, observations, observations.Select(x =>
        {
            var quantity = x.AvailableQuantity != canonical.AvailableQuantity;
            var treatment = !x.TreatmentInterpretation.Distinct().Order().SequenceEqual(signatures);
            return new InventoryWorkflowDivergence(x.Workflow, x.AvailableQuantity, canonical.AvailableQuantity, quantity, treatment,
                !quantity && !treatment ? "Same quantity and treatment interpretation."
                : canonical.Blockers.Length > 0 ? $"Canonical proof is blocked: {string.Join(", ", canonical.Blockers.Select(b => b.Code))}; existing result is not assumed correct."
                : canonical.Proof.NormalizationRequired ? "Canonical evidence proves a physical treatment pool and excludes historical representation; legacy gates may reject that proof shape. Exact receipt attribution remains a separate requirement."
                : "Different workflow interpretation; review evidence and business requirements before any cutover.");
        }).ToImmutableArray());
    }
}
