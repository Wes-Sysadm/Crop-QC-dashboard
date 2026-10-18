using System.Collections.Immutable;
using System.Text.Json;
using CropQc.Shared.Inventory;

namespace CropQc.Api.Tests;

public sealed class InventoryRecordedCustodyReplayTests
{
    private static readonly DateTimeOffset Start = InventoryEvidenceCorpus.Start;

    [Theory]
    [InlineData(0)]
    [InlineData(40)]
    [InlineData(-5)]
    public void Projection_quantity_or_identity_cannot_override_complete_recorded_custody(int projected)
    {
        var (source, destination, link) = History();
        source = source with { Projections = [InventoryEvidenceCorpus.Projection(20, projected) with { ExactIdentity = false }] };
        destination = destination with
        {
            ApplicationAllocationsLoaded = true,
            Projections = [InventoryEvidenceCorpus.Projection(21, projected) with { ExactIdentity = false }]
        };
        var enriched = InventoryRecordedCustodyReplay.Enrich([source, destination], [link])[1];
        var result = InventoryAvailabilityResolver.Resolve(enriched, new());
        Assert.True(result.IsOperable);
        Assert.Equal(6, result.AvailableQuantity);
        Assert.Equal(InventoryConfidence.Unknown, result.ReceiptProvenance.Confidence);
        Assert.True(result.Proof.NormalizationRequired);
    }

    [Fact]
    public void Partial_legacy_move_preserves_treatment_but_does_not_invent_receipt_ancestry()
    {
        var (source, destination, link) = History();
        var before = JsonSerializer.Serialize(new[] { source, destination });
        var result = InventoryRecordedCustodyReplay.Enrich([source, destination], [link]);
        var replay = InventoryEventReplay.Replay(result[1]);
        Assert.True(replay.QuantityConserved);
        Assert.Empty(replay.UnresolvedEvents);
        var cohort = Assert.Single(replay.Cohorts);
        Assert.Equal(6, cohort.Quantity);
        Assert.Null(cohort.ReceiptId);
        Assert.Equal("u", cohort.Signature);
        Assert.Equal(10, result.Sum(x => x.AuthoritativeQuantity));
        Assert.Equal(before, JsonSerializer.Serialize(new[] { source, destination }));
        Assert.False(InventoryAvailabilityResolver.Resolve(result[1], new(RequireExactReceipt: true, ReceiptId: 905)).IsOperable);
    }

    [Theory]
    [InlineData("missing debit")]
    [InlineData("duplicate link")]
    [InlineData("wrong quantity")]
    [InlineData("wrong identity")]
    [InlineData("baseline mismatch")]
    [InlineData("insufficient custody")]
    [InlineData("backdated application")]
    public void Incomplete_or_contradictory_evidence_never_authorizes_an_arrival(string defect)
    {
        var (source, destination, link) = History();
        IReadOnlyList<InventoryRecordedCustodyLink> links = [link];
        switch (defect)
        {
            case "missing debit": source = source with { Ledger = source.Ledger.RemoveAt(2) }; break;
            case "duplicate link": links = [link, link]; break;
            case "wrong quantity": links = [link with { Quantity = 7 }]; break;
            case "wrong identity": destination = destination with { Identity = destination.Identity with { Lot = "OTHER" } }; break;
            case "baseline mismatch": source = source with { AuthoritativeQuantity = 14 }; break;
            case "insufficient custody": source = source with { Ledger = source.Ledger.SetItem(0, source.Ledger[0] with { Quantity = 0 }), AuthoritativeQuantity = -1 }; break;
            case "backdated application": source = source with { Applications = [new(1, Start.AddHours(1), null, null, 904) { RecordedAt = Start.AddDays(3) }] }; break;
        }
        var result = InventoryRecordedCustodyReplay.Enrich([source, destination], links)[1];
        Assert.Empty(result.RecordedArrivals);
        Assert.False(InventoryEventReplay.Replay(result).QuantityConserved);
    }

    [Fact]
    public void Partial_mixed_treatment_requires_allocation_but_whole_pool_preserves_both_histories()
    {
        var (source, destination, link) = History();
        source = source with { Applications = [new(7, Start.AddHours(1), null, 905, 904)] };
        Assert.Empty(InventoryRecordedCustodyReplay.Enrich([source, destination], [link])[1].RecordedArrivals);
        source = source with { AuthoritativeQuantity = 0, Ledger = source.Ledger.SetItem(2, source.Ledger[2] with { Quantity = -10 }) };
        destination = destination with { AuthoritativeQuantity = 10, Ledger = [destination.Ledger[0] with { Quantity = 10 }] };
        var proof = Assert.Single(InventoryRecordedCustodyReplay.Enrich([source, destination], [link with { Quantity = 10 }])[1].RecordedArrivals);
        Assert.Equal(5, Assert.Single(proof.Cohorts, x => x.Signature == "u|a:7").Quantity);
        Assert.Equal(5, Assert.Single(proof.Cohorts, x => x.Signature == "u").Quantity);
    }

    [Fact]
    public void Treatment_follows_departure_without_inheriting_an_earlier_destination_application()
    {
        var (source, destination, link) = History();
        source = source with { Applications = [new(7, Start.AddHours(1), null, null, 904)] };
        destination = destination with { Applications = [new(8, Start.AddHours(2), null, null, 906)] };
        var replay = InventoryEventReplay.Replay(InventoryRecordedCustodyReplay.Enrich([source, destination], [link])[1]);
        Assert.True(replay.QuantityConserved);
        Assert.Empty(replay.UnresolvedEvents);
        Assert.Equal(new long[] { 7 }, Assert.Single(replay.Cohorts).ApplicationIds);
    }

    [Fact]
    public void Contradictory_modern_movement_is_not_replaced_with_legacy_proof()
    {
        var (source, destination, link) = History();
        var enriched = InventoryRecordedCustodyReplay.Enrich([source, destination], [link])[1];
        enriched = enriched with { Movements = [new(9, "RoomTransfer", 5, Start.AddDays(1), Start.AddDays(1), "u", "Untreated", null, null, null, true, false, "room:5", null, true)] };
        Assert.False(InventoryEventReplay.Replay(enriched).QuantityConserved);
    }

    [Fact]
    public void Reversal_between_legacy_hops_is_replayed_without_intermediate_projection_links()
    {
        var (source, middle, first) = History();
        source = source with { Applications = [new(7, Start.AddHours(1), Start.AddHours(36), null, 904)] };
        middle = middle with
        {
            AuthoritativeQuantity = 3,
            Applications = [],
            Ledger = middle.Ledger.Add(new(5, -3, "TransferOut", Start.AddDays(2), null, "room:6", true))
        };
        var destination = middle with
        {
            Location = new(InventoryCustody.Room, 903, 908, "Test", "Final destination"),
            Ledger = [new(6, 3, "TransferIn", Start.AddDays(2), null, "room:6", true)]
        };
        var result = InventoryRecordedCustodyReplay.Enrich([source, middle, destination],
            [first, new(5, 6, 3, new("RoomTransfer", "6"))]);
        Assert.Equal("u|a:7", Assert.Single(Assert.Single(result[1].RecordedArrivals).Cohorts).Signature);
        var replay = InventoryEventReplay.Replay(result[2]);
        Assert.True(replay.QuantityConserved);
        Assert.Empty(replay.UnresolvedEvents);
        Assert.Equal("u", Assert.Single(replay.Cohorts).Signature);
        Assert.Empty(Assert.Single(replay.Cohorts).ApplicationIds);
        Assert.Equal(10, result.Sum(p => p.AuthoritativeQuantity));
    }

    [Fact]
    public void Repeated_room_visits_follow_earlier_commits_not_a_room_cycle_heuristic()
    {
        var (source, destination, link) = History();
        source = source with { AuthoritativeQuantity = 7, Ledger = source.Ledger.Add(new(6, 3, "TransferIn", Start.AddDays(2), null, "room:6", true)) };
        destination = destination with { AuthoritativeQuantity = 3, Ledger = destination.Ledger.Add(new(5, -3, "TransferOut", Start.AddDays(2), null, "room:6", true)) };
        var result = InventoryRecordedCustodyReplay.Enrich([source, destination], [link, new(5, 6, 3, new("RoomTransfer", "6"))]);
        Assert.All(result, p => { var r = InventoryEventReplay.Replay(p); Assert.True(r.QuantityConserved); Assert.Empty(r.UnresolvedEvents); });
        Assert.Equal(10, result.Sum(p => InventoryEventReplay.Replay(p).Cohorts.Sum(c => c.Quantity)));
    }

    private static (InventoryPositionEvidence Source, InventoryPositionEvidence Destination, InventoryRecordedCustodyLink Link) History()
    {
        var source = InventoryEvidenceCorpus.Basic() with
        {
            AuthoritativeQuantity = 4,
            Projections = [],
            Ledger = [new(1, 5, "ReceiptAdd", Start, 905, null, true), new(2, 5, "ReceiptAdd", Start, 907, null, true),
                new(3, -6, "TransferOut", Start.AddDays(1), null, "room:5", true)],
            Receipts = [new(905, 5, true, false, false, Start, 1), new(907, 5, true, false, false, Start, 1)]
        };
        var destination = source with
        {
            Location = new(InventoryCustody.Room, 903, 906, "Test", "Destination"),
            AuthoritativeQuantity = 6,
            Receipts = [],
            Ledger = [new(4, 6, "TransferIn", Start.AddDays(1), null, "room:5", true)]
        };
        return (source, destination, new(3, 4, 6, new("RoomTransfer", "5")));
    }
}
