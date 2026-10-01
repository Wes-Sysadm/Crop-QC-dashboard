using System.Collections.Immutable;
using System.Text.Json;
using CropQc.Shared.Inventory;

namespace CropQc.Api.Tests;

public sealed class InventoryAvailabilityTests
{
    public static IEnumerable<object[]> Cases => InventoryEvidenceCorpus.Cases().Select(x => new object[] { x.Name });
    [Theory]
    [MemberData(nameof(Cases))]
    public void Corpus_is_explained_without_mutation(string name)
    {
        var fixture = InventoryEvidenceCorpus.Cases().Single(x => x.Name == name);
        var before = JsonSerializer.Serialize(fixture.Evidence);
        var requirements = new InventoryOperationRequirements(AllowedCustody: fixture.Evidence.Location.Custody);
        var result = InventoryAvailabilityResolver.Resolve(fixture.Evidence, requirements);
        Assert.Equal(fixture.Available, result.AvailableQuantity);
        Assert.Equal(fixture.Evidence.AuthoritativeQuantity, result.AuthoritativeQuantity);
        Assert.InRange(result.AvailableQuantity, 0, Math.Max(0, result.AuthoritativeQuantity));
        Assert.Equal(before, JsonSerializer.Serialize(fixture.Evidence));
        Assert.Equal(JsonSerializer.Serialize(result), JsonSerializer.Serialize(InventoryAvailabilityResolver.Resolve(fixture.Evidence, requirements)));
    }

    [Fact]
    public void Wp7_accounts_for_duplicate_and_consumed_history_without_inventing_receipt_allocation()
    {
        var e = InventoryEvidenceCorpus.Wp7();
        var r = InventoryAvailabilityResolver.Resolve(e, new());
        Assert.Equal(1122, r.AvailableQuantity);
        Assert.Equal(1568, r.RawProjectionQuantity);
        Assert.Equal(446, r.ProjectionExcess);
        Assert.Equal(324, Assert.Single(r.HistoricalProjections, x => x.Reason == ProjectionExclusionReason.DuplicateStatusAlias).ExcludedQuantity);
        Assert.Equal(122, Assert.Single(r.HistoricalProjections, x => x.Reason == ProjectionExclusionReason.ConsumedHistoricalRepresentation).ExcludedQuantity);
        Assert.Equal(InventoryConfidence.Proven, r.TreatmentConfidence);
        Assert.NotEqual(InventoryConfidence.Proven, r.ReceiptProvenance.Confidence);
        Assert.True(r.Proof.NormalizationRequired);
        Assert.Equal(new long[] { 9 }, r.Proof.BackdatedLedgerIds);
        Assert.Contains(r.Proof.Candidates, x => !x.ExactRowAllocationProven);
        var receipt = InventoryAvailabilityResolver.Resolve(e, new(RequireExactReceipt: true, ReceiptId: 905));
        Assert.Equal(0, receipt.AvailableQuantity);
        Assert.Contains(receipt.Blockers, x => x.Code == InventoryBlockerCode.MissingReceiptProvenance);
    }

    [Fact]
    public void Negative_authority_is_not_clamped_or_treated_as_valid()
    {
        var r = InventoryAvailabilityResolver.Resolve(InventoryEvidenceCorpus.Basic(-5, 0), new());
        Assert.Equal(-5, r.AuthoritativeQuantity);
        Assert.False(r.IsOperable);
        Assert.Contains(r.Blockers, x => x.Code == InventoryBlockerCode.NegativeAuthoritativeBalance);
    }

    [Fact]
    public void Proved_current_pool_does_not_invent_the_cause_of_old_excess()
    {
        var r = InventoryAvailabilityResolver.Resolve(InventoryEvidenceCorpus.Basic(19, 29), new());
        Assert.Equal(ProjectionExclusionReason.StaleHistoricalPool, Assert.Single(r.HistoricalProjections).Reason);
        Assert.DoesNotContain(r.HistoricalProjections, x => x.Reason == ProjectionExclusionReason.ConsumedHistoricalRepresentation);
    }

    [Fact]
    public void Gap_is_not_untreated_without_arrival_evidence()
    {
        var e = InventoryEvidenceCorpus.Basic(100, 0) with { Ledger = [] };
        var r = InventoryAvailabilityResolver.Resolve(e, new());
        Assert.Empty(r.TreatmentSlices);
        Assert.Contains(r.Blockers, x => x.Code == InventoryBlockerCode.UnknownTreatment);
    }

    [Fact]
    public void Unlinked_treatment_and_unproven_return_cannot_be_masked_by_a_balanced_count()
    {
        var e = InventoryEvidenceCorpus.Basic(19, 19) with { Applications = [new(1, InventoryEvidenceCorpus.Start, null, null)] };
        Assert.Equal(0, InventoryAvailabilityResolver.Resolve(e, new()).AvailableQuantity);
        var returned = InventoryEvidenceCorpus.Cases().Single(x => x.Name == "Returned/recreated source bins").Evidence;
        Assert.Equal(0, InventoryAvailabilityResolver.Resolve(returned with { Movements = returned.Movements.Where(x => x.Id != 99).ToImmutableArray() }, new()).AvailableQuantity);
    }

    [Fact]
    public void Requirements_filter_slices_without_replacing_physical_arithmetic()
    {
        var e = InventoryEvidenceCorpus.Treated();
        var r = InventoryAvailabilityResolver.Resolve(e, new(TreatmentSignature: "u|a:1"));
        Assert.Equal(20, r.AuthoritativeQuantity);
        Assert.Equal(10, r.AvailableQuantity);
        Assert.Equal(0, InventoryAvailabilityResolver.Resolve(e, new(TreatmentSignature: "absent")).AvailableQuantity);
        Assert.Contains(InventoryAvailabilityResolver.Resolve(e, new(ExpectedFingerprint: "stale")).Blockers, x => x.Code == InventoryBlockerCode.StaleRead);
        Assert.Contains(InventoryAvailabilityResolver.Resolve(e with { HistoricalSnapshotUnavailable = true }, new()).Blockers,
            x => x.Code == InventoryBlockerCode.HistoricalSnapshotUnavailable);
    }

    [Fact]
    public void Divergence_is_visible_and_does_not_silently_prefer_new_logic()
    {
        var r = InventoryAvailabilityResolver.Resolve(InventoryEvidenceCorpus.Basic(), new());
        var comparison = InventoryShadowComparison.Compare(r,
            [new("Transfer", 19, ["u"], ["shared"], null), new("Dump", 0, [], ["shared"], "review")]);
        Assert.False(comparison.Divergences[0].QuantityDiffers);
        Assert.True(comparison.Divergences[1].QuantityDiffers);
    }

    [Fact]
    public void Randomized_projection_quantities_never_create_physical_stock()
    {
        var random = new Random(41291);
        for (var i = 0; i < 1000; i++)
        {
            var physical = random.Next(-50, 5000);
            var e = InventoryEvidenceCorpus.Basic(physical, random.Next(0, 10000));
            var r = InventoryAvailabilityResolver.Resolve(e, new());
            Assert.Equal(physical, r.AuthoritativeQuantity);
            Assert.InRange(r.AvailableQuantity, 0, Math.Max(0, physical));
            if (physical < 0) Assert.False(r.IsOperable);
        }
    }
}
