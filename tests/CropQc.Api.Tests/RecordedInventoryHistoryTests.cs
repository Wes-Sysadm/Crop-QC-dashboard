using System.Collections.Immutable;
using System.Text.Json;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;

namespace CropQc.Api.Tests;

public sealed class RecordedInventoryHistoryTests
{
    private static RecordedInventoryHistory Load(int room)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "CropQc.sln"))) root = root.Parent;
        return JsonSerializer.Deserialize<RecordedInventoryHistory>(File.ReadAllText(Path.Combine(root!.FullName,
            $"tests/CropQc.Api.Tests/Fixtures/RecordedHistory-{room}.json")))!;
    }

    [Theory]
    [InlineData(47, 202, 598, 13, 2)]
    [InlineData(2, 10, 130, 19, 1)]
    [InlineData(5, 170, 362, 12, 3)]
    public void Complete_recorded_timelines_prove_only_shared_treatment_and_preserve_every_event(
        int room, int quantity, int projected, int ledgerRows, int ancestryRooms)
    {
        var h = Load(room);
        var e = h.Positions[0];
        var before = JsonSerializer.Serialize(h);
        var result = RecordedInventoryHistoryReconstruction.Assess(e, h);
        Assert.True(result.Proven, string.Join(';', result.Blockers));
        Assert.Equal(ledgerRows, e.Ledger.Length);
        Assert.Equal(ancestryRooms, h.Positions.Length);
        Assert.Equal(quantity, e.Ledger.Sum(x => x.Quantity));
        Assert.Equal(projected, result.Plan!.Changes.Sum(x => x.BeforeQuantity));
        Assert.Equal(quantity, result.Plan.ReplacementQuantity);
        Assert.Null(result.Plan.ReplacementReceiptId);
        Assert.Equal(InventoryConfidence.Unknown, result.Plan.ReceiptProvenance.Confidence);
        Assert.All(e.Ledger, x => Assert.Contains(result.Evidence, r => r.Entity == "RoomInventoryAdjustment" && r.Id == x.Id.ToString()));
        Assert.Contains(result.Evidence, x => x.Entity == "ReceiptInventoryOverride");
        Assert.Equal(before, JsonSerializer.Serialize(h));
        Assert.False(InventoryAvailabilityResolver.Resolve(e, new(RequireExactReceipt: true)).IsOperable);
        var shared = e.Projections[0] with { Id = 999999, RawKey = e.Identity.Key, Quantity = quantity, ReceiptId = null, Version = 1 };
        var after = e with { Projections = e.Projections.Select(x => x with { Quantity = 0, Disposition = "Historical", RetiredQuantity = x.Quantity }).Append(shared).ToImmutableArray() };
        Assert.True(InventoryAvailabilityResolver.Resolve(after, new()).IsOperable);
        Assert.False(InventoryAvailabilityResolver.Resolve(after, new(RequireExactReceipt: true)).IsOperable);
        var prior = DateTimeOffset.MinValue;
        Assert.Contains(e.Ledger.OrderBy(x => x.RecordedAt).ThenBy(x => x.Id), x =>
        { var inverted = x.At < prior; prior = x.At > prior ? x.At : prior; return inverted; });
    }

    [Theory]
    [InlineData(47)]
    [InlineData(2)]
    [InlineData(5)]
    public void A_room_application_before_any_arrival_is_not_attached_to_later_inventory(int room)
    {
        var h = Load(room);
        var e = h.Positions[0];
        e = e with { Applications = [new(999, e.Ledger.Min(x => x.At).AddDays(-1), null, null, room)] };
        Assert.True(RecordedInventoryHistoryReconstruction.Assess(e, h).Proven);
    }

    [Theory]
    [InlineData(47)]
    [InlineData(2)]
    [InlineData(5)]
    public void Correction_reason_never_changes_treatment_proof_or_assigns_receipts(int room)
    {
        var h = Load(room);
        var before = RecordedInventoryHistoryReconstruction.Assess(h.Positions[0], h);
        foreach (var correction in h.Corrections) correction.Reason = "All surviving fruit belongs to this receipt and is untreated";
        Assert.Equal(JsonSerializer.Serialize(before), JsonSerializer.Serialize(RecordedInventoryHistoryReconstruction.Assess(h.Positions[0], h)));
    }

    [Theory]
    [InlineData(47, "missing-original")]
    [InlineData(2, "missing-original")]
    [InlineData(5, "missing-original")]
    [InlineData(47, "missing-correction")]
    [InlineData(2, "missing-correction")]
    [InlineData(5, "missing-correction")]
    [InlineData(47, "missing-audit")]
    [InlineData(2, "missing-audit")]
    [InlineData(5, "missing-audit")]
    [InlineData(47, "treatment")]
    [InlineData(2, "treatment")]
    [InlineData(5, "treatment")]
    [InlineData(47, "effective-overdraw")]
    [InlineData(2, "effective-overdraw")]
    [InlineData(5, "effective-overdraw")]
    public void Missing_or_contradictory_events_and_treatment_remain_blocked(int room, string fault)
    {
        var h = Load(room);
        var e = h.Positions[0];
        if (fault == "missing-original") h = h with { Ledger = h.Ledger.Where(x => x.Id != e.Ledger.First(x => x.Kind == "ReceiptAdd").Id).ToArray() };
        if (fault == "missing-correction") h = h with { Corrections = [] };
        if (fault == "missing-audit") h = h with { Audits = [] };
        if (fault == "treatment")
            e = e with { Applications = [new(999, e.Ledger.Max(x => x.At), null, null, room)] };
        if (fault == "effective-overdraw")
        {
            var debit = e.Ledger.First(x => x.Quantity < 0);
            var earlier = e.Ledger.Min(x => x.At).AddDays(-1);
            e = e with { Ledger = e.Ledger.Replace(debit, debit with { At = earlier }) };
            h.Ledger.Single(x => x.Id == debit.Id).AdjustmentAt = earlier;
        }
        var result = RecordedInventoryHistoryReconstruction.Assess(e, h);
        Assert.False(result.Proven); Assert.Null(result.Plan); Assert.NotEmpty(result.Blockers);
        Assert.DoesNotContain(result.Blockers, x => x.Contains("paperwork") || x.Contains("onsite"));
    }

    [Theory]
    [InlineData("missing-entry")]
    [InlineData("missing-revision")]
    [InlineData("changed-quantity")]
    [InlineData("changed-treatment")]
    public void Legacy_packing_debits_require_exact_committed_run_evidence(string fault)
    {
        var h = Load(2);
        Assert.Contains(h.Entries, x => x.Id == 39 && x.BinsRun == 8);
        Assert.Contains(h.Entries, x => x.Id == 53 && x.BinsRun == 34);
        if (fault == "missing-entry") h = h with { Entries = h.Entries.Where(x => x.Id != 53).ToArray() };
        if (fault == "missing-revision") h = h with { RunRevisions = h.RunRevisions.Where(x => x.Id != 14).ToArray() };
        if (fault == "changed-quantity") h.Entries.Single(x => x.Id == 53).BinsRun++;
        if (fault == "changed-treatment") h.Entries.Single(x => x.Id == 53).TreatmentStateSnapshot = "Unknown";
        var result = RecordedInventoryHistoryReconstruction.Assess(h.Positions[0], h);
        Assert.False(result.Proven);
        Assert.Contains(result.Blockers, x => x.Contains("Ledger 221"));
    }

    [Theory]
    [InlineData("source-receipt")]
    [InlineData("source-treatment")]
    [InlineData("transfer-pair")]
    [InlineData("missing-movement")]
    public void Wp8_requires_actual_ancestry_through_Dh15_to_original_receipt_1288(string fault)
    {
        var h = Load(5);
        var source = h.Positions.Single(x => x.Location.RoomId == 36);
        if (fault == "source-receipt") h = h with { Receipts = h.Receipts.Where(x => x.Id != 1288).ToArray() };
        if (fault == "source-treatment") h.Positions[Array.IndexOf(h.Positions, source)] = source with
        { Applications = [new(999, source.Ledger.Min(x => x.At).AddMinutes(1), null, null, 36)] };
        if (fault == "transfer-pair") h = h with { Ledger = h.Ledger.Where(x => x.Id != 2459).ToArray() };
        if (fault == "missing-movement") h.Positions[0] = h.Positions[0] with { Movements = h.Positions[0].Movements.RemoveAt(0) };
        Assert.False(RecordedInventoryHistoryReconstruction.Assess(h.Positions[0], h).Proven);
    }

    [Theory]
    [InlineData("receipt")]
    [InlineData("packing")]
    [InlineData("transfer")]
    [InlineData("loss")]
    public void Operational_parents_cannot_be_ignored_when_their_ledger_effect_is_missing(string parent)
    {
        var h = Load(5);
        if (parent == "receipt")
        {
            var orphan = JsonSerializer.Deserialize<CropQc.Data.Entities.Receipt>(JsonSerializer.Serialize(h.Receipts[0]))!;
            orphan.Id = 999999;
            h = h with { Receipts = [.. h.Receipts, orphan] };
        }
        if (parent == "packing") h.Entries[0].InventoryAdjustmentId = 999999;
        if (parent == "transfer")
        {
            var orphan = JsonSerializer.Deserialize<CropQc.Data.Entities.RoomTransfer>(JsonSerializer.Serialize(h.Transfers[0]))!;
            orphan.Id = 999999;
            h = h with { Transfers = [.. h.Transfers, orphan] };
        }
        if (parent == "loss") h = h with { AdditionalEventBlockers = ["RoomInventoryLoss 999999: missing committed ledger effect."] };
        var result = RecordedInventoryHistoryReconstruction.Assess(h.Positions[0], h);
        Assert.False(result.Proven); Assert.Null(result.Plan);
    }
}
