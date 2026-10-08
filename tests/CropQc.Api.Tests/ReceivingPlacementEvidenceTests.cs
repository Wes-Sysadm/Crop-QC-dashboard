using CropQc.Shared.Inventory;

namespace CropQc.Api.Tests;

public sealed class ReceivingPlacementEvidenceTests
{
    [Fact]
    public void Later_receipt_with_exact_legacy_ledger_does_not_inherit_a_previous_room_treatment()
    {
        var evidence = LaterReceipt();
        var result = InventoryAvailabilityResolver.Resolve(evidence, new());
        Assert.True(result.IsOperable, string.Join(", ", result.Blockers));
        Assert.Equal(59, result.AuthoritativeQuantity);
        Assert.Equal(59, result.RawProjectionQuantity);
        Assert.Equal(19, Assert.Single(result.TreatmentSlices, x => x.State == "Confirmed").Quantity);
        Assert.Equal(40, Assert.Single(result.TreatmentSlices, x => x.State == "Untreated").Quantity);
    }

    [Theory]
    [InlineData("treatment-after-arrival")]
    [InlineData("wrong-receipt-count")]
    [InlineData("transfer-evidence")]
    [InlineData("unassigned-consumption")]
    public void Ambiguous_legacy_arrival_cannot_prove_untreated_stock(string defect)
    {
        var e = LaterReceipt();
        e = defect switch
        {
            "treatment-after-arrival" => e with { Applications = [e.Applications[0] with { AppliedAt = InventoryEvidenceCorpus.Start.AddDays(3) }] },
            "wrong-receipt-count" => e with { Receipts = [e.Receipts[0], e.Receipts[1] with { Quantity = 41 }] },
            "transfer-evidence" => e with { Receipts = [e.Receipts[0], e.Receipts[1] with { IsTransferReceipt = true }] },
            _ => e with
            {
                AuthoritativeQuantity = 58,
                Ledger = e.Ledger.Add(new(3, -1, "BinsRun", InventoryEvidenceCorpus.Start.AddDays(3), null, "entry:3", true)),
                Projections = [e.Projections[0] with { Quantity = 18 }, e.Projections[1]]
            }
        };
        var result = InventoryAvailabilityResolver.Resolve(e, new());
        Assert.False(result.IsOperable);
        Assert.Equal(0, result.AvailableQuantity);
    }

    internal static InventoryPositionEvidence LaterReceipt()
    {
        var start = InventoryEvidenceCorpus.Start;
        var e = InventoryEvidenceCorpus.Basic(59, 19);
        return e with
        {
            Ledger = [new(1, 19, "ReceiptAdd", start, 905, null, true), new(2, 40, "ReceiptAdd", start.AddDays(2), 906, null, true)],
            Receipts = [new(905, 19, true, false, false, start, 1), new(906, 40, true, false, false, start.AddDays(2), 1)],
            Projections =
            [
                e.Projections[0] with { ReceiptId = 905, State = "Confirmed", Signature = "u|a:1", ApplicationIds = [1] },
                InventoryEvidenceCorpus.Projection(11, 40, 906) with { CreatedAt = start.AddDays(2), UpdatedAt = start.AddDays(2) }
            ],
            Applications = [new(1, start.AddDays(1), null, null, e.Location.RoomId)]
        };
    }
}
