using System.Collections.Immutable;
using CropQc.Shared.Inventory;

namespace CropQc.Api.Tests;

internal static class InventoryEvidenceCorpus
{
    internal static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
    internal static readonly InventoryIdentity Identity = new(2026, 901, 902, "CORPUS", "CORPUS", "BART", "Conventional", false, "");
    internal static InventoryProjectionEvidence Projection(long id, int quantity, long? receipt = null) =>
        new(id, Identity.Key, quantity, "Untreated", "u", receipt, Start, Start, 1, true, []);
    internal static InventoryPositionEvidence Basic(int physical = 19, int explicitBins = 29) => new(Identity,
        new(InventoryCustody.Room, 903, 904, "Test", "Source"), physical, 0, true, true,
        [new(1, physical, "ReceiptAdd", Start, 905, null, true)], [Projection(10, explicitBins)], [],
        [new(905, physical, true, false, false, Start, 1)], [], new("fixture-watermark", "Fixture", []));

    internal static InventoryPositionEvidence Wp7()
    {
        var ledger = ImmutableArray.CreateBuilder<InventoryLedgerEvidence>();
        var movements = ImmutableArray.CreateBuilder<InventoryMovementEvidence>();
        var incoming = new[] { (4, 10L, 905L), (62, 11L, (long?)null), (64, 10L, 905L), (66, 10L, 905L),
            (66, 12L, 906L), (64, 12L, 906L), (62, 12L, 906L), (58, 12L, 906L) };
        for (var i = 0; i < incoming.Length; i++)
        {
            var (bins, segment, receipt) = incoming[i];
            ledger.Add(new(i + 1, bins, "InterCrewTransferReceive", Start.AddMinutes(i), null, $"crew:{i}", true));
            movements.Add(new(i + 1, "InterCrewReceive", bins, Start.AddMinutes(i), Start.AddMinutes(i), "u", "Untreated",
                receipt, null, segment, true, false, $"crew:{i}", null, true));
        }
        var consumed = Start.AddDays(1);
        ledger.Add(new(9, -122, "BinsRun", Start.AddDays(-1), null, "entry:99", true, consumed));
        movements.Add(new(9, "BinsRun", 122, Start.AddDays(-1), consumed.AddMilliseconds(12), "u", "Untreated", null, 13, null, false, true, "entry:99", null, true));
        ledger.Add(new(10, 798, "InterCrewTransferReceive", Start.AddDays(2), null, "crew:later", true));
        foreach (var (id, bins, destination, receipt) in new[] { (20, 72, 14, (long?)907), (21, 184, 15, (long?)908), (22, 542, 11, (long?)null) })
            movements.Add(new(id, "InterCrewReceive", bins, Start.AddDays(2), Start.AddDays(2), "u", "Untreated", receipt,
                null, destination, true, false, "crew:later", null, true));
        return Basic() with
        {
            AuthoritativeQuantity = 1122,
            Ledger = ledger.ToImmutable(),
            Movements = movements.ToImmutable(),
            Receipts = [],
            Projections = [Projection(10, 134, 905), Projection(11, 604), Projection(12, 250, 906),
                Projection(13, 324) with { RawKey = Identity.Key + "CONVENTIONAL", CreatedAt = consumed, UpdatedAt = consumed },
                Projection(14, 72, 907) with { CreatedAt = Start.AddDays(2) }, Projection(15, 184, 908) with { CreatedAt = Start.AddDays(2) }]
        };
    }

    // Production names label shapes only. No production identifier is used by the resolver.
    internal static IEnumerable<(string Name, InventoryPositionEvidence Evidence, int Available)> Cases()
    {
        yield return ("Room 14 Bartlett 1372 original pooled overcount", Basic(798, 1058) with
        {
            Projections = [Projection(10, 184, 907), Projection(11, 72, 908), Projection(12, 802)],
            Ledger = [new(1, 184, "ReceiptAdd", Start, 907, null, true), new(2, 72, "ReceiptAdd", Start, 908, null, true),
                new(3, 542, "ReceiptAdd", Start, 905, null, true)],
            Receipts = [new(907, 184, true, false, false, Start, 1), new(908, 72, true, false, false, Start, 1),
                new(905, 542, true, false, false, Start, 1)]
        }, 798);
        yield return ("EVANS-3 lot 9722", Basic(19, 29), 19);
        yield return ("LAMB-14 lot 9682", Basic(27, 28), 27);
        yield return ("WP-7 Bartlett lot 1372", Wp7(), 1122);
        yield return ("Status-key alias duplicate", Wp7(), 1122);
        yield return ("Receipt replacement/correction", Basic() with { Receipts = [new(905, 20, true, false, false, Start, 2)] }, 0);
        yield return ("Returned/recreated source bins", Basic(20, 30) with
        {
            Ledger = [new(1, 20, "ReceiptAdd", Start, 905, null, true), new(2, -10, "TransferOut", Start.AddDays(1), null, "room:1", true),
                new(3, 10, "TransferReversalIn", Start.AddDays(2), null, "room:1", true)],
            Movements = [new(99, "Transfer", 10, Start.AddDays(1), Start.AddDays(1), "u", "Untreated", 905, 10, null, false, true, "room:1", null, true),
                new(1, "TransferReversal", 10, Start.AddDays(2), Start.AddDays(2), "u", "Untreated", 905, null, 10, true, false, "room:1", 99, true)]
        }, 20);
        yield return ("Split transfer", Basic(20, 20) with { Projections = [Projection(10, 8, 905), Projection(11, 12, 905)] }, 20);
        yield return ("Partial movement", Basic(12, 20) with
        {
            Receipts = [new(905, 20, true, false, false, Start, 1)],
            Ledger = [new(1, 20, "ReceiptAdd", Start, 905, null, true), new(2, -8, "TransferOut", Start.AddDays(1), null, "room:1", true)]
        }, 12);
        yield return ("Partial depletion", Basic(12, 20) with
        {
            Receipts = [new(905, 20, true, false, false, Start, 1)],
            Ledger = [new(1, 20, "ReceiptAdd", Start, 905, null, true), new(2, -8, "Depletion", Start.AddDays(1), 905, null, true)]
        }, 12);
        yield return ("Stale depleted projection", Basic(0, 20) with
        {
            Receipts = [new(905, 20, true, false, false, Start, 1)],
            Ledger = [new(1, 20, "ReceiptAdd", Start, 905, null, true), new(2, -20, "BinsRun", Start.AddDays(1), null, "entry:1", true)]
        }, 0);
        yield return ("Receipt true-up surviving movement", Basic(0, 5) with
        {
            Ledger = [new(1, 5, "ManualTrueUp", Start, 905, null, true),
            new(2, -5, "TransferOut", Start.AddDays(1), null, "room:1", true)]
        }, 0);
        yield return ("Transfer projection surviving depletion", Basic(0, 8) with
        {
            Ledger = [new(1, 8, "TransferIn", Start, null, "room:1", true),
            new(2, -8, "TransferOut", Start.AddDays(1), null, "room:2", true)],
            Movements = [new(1, "Transfer", 8, Start, Start,
                "u", "Untreated", 905, null, 10, true, false, "room:1", null, true)]
        }, 0);
        yield return ("Multiple balanced treatment signatures", Treated(), 20);
        yield return ("Balanced lineage", Basic(19, 19), 19);
        yield return ("Genuine treatment ambiguity", Treated() with { AuthoritativeQuantity = 10 }, 0);
        yield return ("Exact receipt provenance", Basic(19, 19) with { Projections = [Projection(10, 19, 905)] }, 19);
        yield return ("Unresolved receipt provenance", Wp7(), 1122);
        yield return ("Negative ledger position", Basic(-5, 0), 0);
        yield return ("Missing projection treatment evidence", Basic(19, 0) with { Ledger = [new(1, 19, "ManualTrueUp", Start, null, null, true)] }, 0);
        yield return ("Truck Receipt InTransit inventory", Custody(InventoryCustody.InTransit), 19);
        yield return ("Processor Sale custody", Custody(InventoryCustody.Processor), 19);
        yield return ("Outside Warehouse custody", Custody(InventoryCustody.OutsideWarehouse), 19);
        yield return ("Pack-run dump state", Basic(19, 29), 19);
        yield return ("Room transfer state", Basic(19, 29), 19);
    }

    internal static InventoryPositionEvidence Treated() => Basic(20, 20) with
    {
        Projections = [Projection(10, 10) with { Signature = "u|a:1", State = "Confirmed", ApplicationIds = [1] },
            Projection(11, 10) with { Signature = "u|a:2", State = "Confirmed", ApplicationIds = [2] }],
        Applications = [new(1, Start, null, null), new(2, Start, null, null)]
    };
    internal static InventoryPositionEvidence Custody(InventoryCustody custody) => Basic(19, 19) with
    {
        Location = new(custody, 903, null, "Test", "Destination", 50),
        Ledger = []
    };
}
