using System.Collections.Immutable;
using System.Text.Json;
using CropQc.Shared.Inventory;
using CropQc.Web.Services;

namespace CropQc.Api.Tests;

public sealed class TreatmentLineageReadinessEvidenceTests
{
    // Immutable operational evidence from the 2026-10-08 read-only investigation.
    // Watermarks were replaced; no user identities or credentials are included.
    private static InventoryPositionEvidence[] Evidence()
    {
        using var stream = typeof(TreatmentLineageReadinessEvidenceTests).Assembly
            .GetManifestResourceStream("CropQc.Api.Tests.Fixtures.SixLineageBlockers.json")!;
        return JsonSerializer.Deserialize<InventoryPositionEvidence[]>(stream)!;
    }

    [Theory]
    [InlineData(47, 495, 202, 598, false)]
    [InlineData(15, 511, 61, 162, true)]
    [InlineData(15, 642, 252, 536, true)]
    [InlineData(2, 398, 10, 130, false)]
    [InlineData(4, 448, 1122, 1568, true)]
    [InlineData(5, 495, 170, 362, false)]
    public void Six_findings_explain_pool_proof_without_rewriting_evidence(int room, int lot, int bins, int raw, bool proven)
    {
        var evidence = Evidence().Single(x => x.Location.RoomId == room && x.Identity.GrowerLotId == lot);
        var before = JsonSerializer.Serialize(evidence);
        var issue = Issue(evidence, bins, raw);
        var result = TreatmentLineageReadinessService.Assess(issue, evidence);

        Assert.Equal(proven ? "ProvenPoolRequiresProjectionReview" : "UnresolvedEvidenceRequiresReconciliation", result.Assessment);
        Assert.Equal(proven ? InventoryConfidence.Proven : InventoryConfidence.Unknown, result.TreatmentConfidence);
        Assert.NotEqual(InventoryConfidence.Proven, result.ReceiptConfidence);
        Assert.Equal(raw - bins, result.HistoricalProjections.Sum(x => x.ExcludedQuantity));
        Assert.Equal(before, JsonSerializer.Serialize(evidence));
        Assert.Equal(bins, evidence.Ledger.Sum(x => x.Quantity));
        Assert.Equal(raw, evidence.Projections.Sum(x => x.Quantity));
        if (!proven) Assert.Contains("UnsupportedHistoricalEvidence", result.CanonicalBlockers);
    }

    [Theory]
    [InlineData("missing-arrival")]
    [InlineData("wrong-treatment")]
    [InlineData("during-occupancy")]
    [InlineData("negative-projection")]
    public void Contradictory_evidence_does_not_receive_a_pool_proof(string mutation)
    {
        var e = Evidence().Single(x => x.Location.RoomId == 15 && x.Identity.GrowerLotId == 511);
        e = mutation switch
        {
            "missing-arrival" => e with { Movements = e.Movements.RemoveAt(0) },
            "wrong-treatment" => e with { Movements = e.Movements.SetItem(0, e.Movements[0] with { Signature = "u|a:99", State = "Confirmed" }) },
            "during-occupancy" => e with { Applications = [new(99, e.Ledger.Max(x => x.At), null, null, 15)] },
            _ => e with { Projections = e.Projections.Add(e.Projections[0] with { Id = 999, Quantity = -1 }) }
        };
        var result = TreatmentLineageReadinessService.Assess(Issue(e, 61, e.Projections.Sum(x => x.Quantity)), e);
        Assert.Equal("UnresolvedEvidenceRequiresReconciliation", result.Assessment);
        Assert.NotEmpty(result.CanonicalBlockers);
    }

    [Fact]
    public void Prior_room_treatment_does_not_linger_into_later_arrivals()
    {
        var e = Evidence().Single(x => x.Location.RoomId == 15 && x.Identity.GrowerLotId == 511);
        e = e with { Applications = [new(99, e.Ledger.Min(x => x.At).AddDays(-1), null, null, 15)] };
        var result = TreatmentLineageReadinessService.Assess(Issue(e, 61, 162), e);
        Assert.Equal("ProvenPoolRequiresProjectionReview", result.Assessment);
        Assert.Equal(InventoryConfidence.Proven, result.TreatmentConfidence);
    }

    [Fact]
    public void Missing_or_changed_evidence_cannot_explain_an_earlier_failure()
    {
        var e = Evidence()[0];
        var issue = Issue(e, e.AuthoritativeQuantity, e.Projections.Sum(x => x.Quantity));
        Assert.Equal("EvidenceUnavailable", TreatmentLineageReadinessService.Assess(issue, null).Assessment);
        Assert.Equal("EvidenceChanged", TreatmentLineageReadinessService.Assess(issue with { AuthoritativeBins = issue.AuthoritativeBins + 1 }, e).Assessment);
        Assert.Equal("EvidenceChanged", TreatmentLineageReadinessService.Assess(issue with { ExplicitLineageBins = issue.ExplicitLineageBins + 1 }, e).Assessment);
    }

    private static TreatmentLineageReadinessIssue Issue(InventoryPositionEvidence e, int bins, int raw) => new(
        "TREATMENT_LINEAGE_EXCEEDS_AUTHORITATIVE_INVENTORY", e.Location.Facility, e.Location.RoomId!.Value,
        e.Location.Name, e.Identity.CropYear, e.Identity.GrowerNumber, e.Identity.Lot, e.Identity.Variety,
        bins, raw, raw - bins, e.Identity.Key);
}
