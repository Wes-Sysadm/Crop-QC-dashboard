using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class CanonicalHistoricalWorkflowTests(ITestOutputHelper output)
{
    public static IEnumerable<object[]> Cases => new[]
    {
        ("Room14", 798, 1058, true), ("Evans9722", 19, 29, true), ("Lamb9682", 27, 28, true),
        ("Evans5_3152", 61, 162, false), ("Evans5_9682", 252, 536, false),
        ("StatusAlias", 19, 29, true), ("ReceiptReplacement", 19, 29, false),
        ("PartialMove", 12, 20, true), ("PartialDepletion", 12, 20, true),
        ("ReturnedSource", 20, 30, true), ("BalancedTreatments", 20, 20, true),
        ("TreatmentAmbiguity", 10, 20, false), ("ExactReceipt", 19, 19, true),
        ("AmbiguousReceipt", 14, 14, true), ("Negative", -5, 0, false)
    }.Select(x => new object[] { x.Item1, x.Item2, x.Item3, x.Item4 });

    [InventoryPostgresTheory]
    [MemberData(nameof(Cases))]
    public async Task Normal_selectors_and_outside_roundtrip_preserve_historical_shapes(string shape, int physical, int explicitBins, bool operable)
    {
        await using var f = await Fixture.Create();
        await using (var seed = f.CreateDbContext()) await Seed(seed, shape, physical, explicitBins);
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        var http = CanonicalReceivingWorkflowTests.Operator();
        var access = new CanonicalOutsideWorkflowTests.Access();
        var time = new PacificBusinessTimeService(new SystemClock());
        var ledger = new CropQc.Web.Services.RoomInventoryLedgerQueryService(db);
        var invariant = new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance);
        var treatment = new RoomTreatmentService(db, ledger, access, http, time, NullLogger<RoomTreatmentService>.Instance, executor);
        var outside = new OutsideWarehouseTransferService(db, ledger, treatment, treatment, invariant, access, http, time, executor);
        var processor = new ProcessorShipmentService(db, ledger, treatment, treatment, invariant, access, http, time, executor);
        var dump = new BinsRunService(db, access, NullLogger<BinsRunService>.Instance, canonicalCommands: executor);
        var loss = new RoomInventoryLossService(db, ledger, invariant, access, new CanonicalGrowerService(db), http, time,
            NullLogger<RoomInventoryLossService>.Instance, canonicalCommands: executor);
        var crew = new InterCrewTransferService(db, outside, ledger, treatment, null!, invariant, access, http, time);
        var dashboard = CanonicalReceivingWorkflowTests.Dashboard(db, executor);
        var before = await f.Snapshot();
        var options = await outside.GetInventoryAsync(default);
        var available = operable ? physical : 0;
        Assert.Equal(available, options.Where(x => x.RoomId == 9002 && x.IsAvailable).Sum(x => x.AvailableBins));
        Assert.Equal(available, (await processor.GetPageAsync(null, false, null, null, null, null, default)).Inventory.Where(x => x.RoomId == 9002).Sum(x => x.AvailableBins));
        var runPage = await dump.GetPageAsync(new() { Section = "Actual", WarehouseId = 9001, RoomIds = [9002] }, http.HttpContext!.User, default);
        Assert.Equal(available, runPage.AvailableInventory.Where(x => x.IsAvailable).Sum(x => x.CurrentBins));
        Assert.Equal(available, (await loss.GetRoomDataAsync(9002, default)).Options.Where(x => x.IsAvailable).Sum(x => x.CurrentBins));
        Assert.Equal(available, (await crew.GetPageAsync(new() { RoomId = 9002, WarehouseId = 9001 }, default)).Inventory.Where(x => x.IsAvailable).Sum(x => x.AvailableBins));
        Assert.Equal(available, (await dashboard.GetRoomDetailAsync(9002, default)).TransferAvailableBins);
        var planning = await dump.SearchPlanningInventoryAsync(null, 9001, 9002, 100, default);
        Assert.Equal(available, planning.Sum(x => x.CurrentBins));
        if (planning.Count > 0) Assert.Equal(available, (await dump.GetPlanningInventoryAsync(planning[0].InventoryKey, default))!.CurrentBins);
        if (shape is "ExactReceipt" or "AmbiguousReceipt")
        {
            var receiptPreview = await CanonicalReceiptCorrectionWorkflowTests.Service(db, executor).GetPreviewAsync(100000, default);
            Assert.NotNull(receiptPreview);
            if (shape == "ExactReceipt") Assert.Null(receiptPreview.CanonicalBlocker);
            else Assert.NotNull(receiptPreview.CanonicalBlocker);
        }
        Assert.Equal(before, await f.Snapshot());
        output.WriteLine($"{shape}: authority={physical}, explicit={explicitBins}, all six selectors={available}; preview fingerprint unchanged");
        var chosen = options.FirstOrDefault(x => x.RoomId == 9002 && x.IsAvailable && x.AvailableBins > 0);
        if (!operable)
        {
            Assert.Null(chosen);
            var refused = await outside.CreateAsync(new()
            {
                SourceKey = options.FirstOrDefault()?.SourceKey ?? "unproven",
                OutsideWarehouseId = 9005,
                BinCount = 1,
                ExpectedAvailableBins = physical,
                ConfirmedReview = true,
                TransferredAt = time.NowPacific.DateTime
            }, default);
            Assert.False(refused.Success); Assert.Equal(before, await f.Snapshot()); return;
        }
        Assert.NotNull(chosen);
        var historical = await Historical(db);
        var sent = await outside.CreateAsync(new()
        {
            SourceKey = chosen.SourceKey,
            OutsideWarehouseId = 9005,
            BinCount = 1,
            ExpectedAvailableBins = chosen.AvailableBins,
            ConfirmedReview = true,
            TransferredAt = time.NowPacific.DateTime
        }, default);
        Assert.True(sent.Success, sent.Error);
        Assert.Equal(physical - 1, await f.Physical());
        Assert.Null(await outside.ReverseAsync(new() { TransferId = sent.TransferId!.Value, Reason = "Historical fixture exact return" }, default));
        Assert.Equal(physical, await f.Physical());
        var p = Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(9001, [9002]), new(), time.UtcNow)).Positions);
        Assert.True(p.IsOperable); Assert.Equal(physical, p.RawProjectionQuantity); Assert.Equal(physical, p.AuthoritativeQuantity);
        Assert.Equal(historical, await Historical(db));
        Assert.Equal(2, await db.InventoryCommands.CountAsync());
    }

    private static async Task<string> Historical(CropQcDbContext db) => JsonSerializer.Serialize(new
    {
        receipts = await db.Receipts.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.BinCount, x.GrowerLotId, x.FruitProfileId, x.ReceivedAt }).ToArrayAsync(),
        moves = await db.TreatmentLineageMovements.AsNoTracking().Where(x => x.Id >= 800000 && x.Id < 800010).OrderBy(x => x.Id).Select(x => new { x.Id, x.BinCount, x.IdentityKey, x.SourceSegmentId, x.DestinationSegmentId, x.ReversesTreatmentLineageMovementId }).ToArrayAsync(),
        applications = await db.RoomTreatmentApplications.AsNoTracking().OrderBy(x => x.Id).Select(x => new { x.Id, x.AppliedAt, x.TotalBinsSnapshot, x.ReversedAt }).ToArrayAsync()
    });

    private static async Task Seed(CropQcDbContext db, string shape, int physical, int explicitBins)
    {
        var receipt = await db.Receipts.SingleAsync();
        var ledger = await db.RoomInventoryAdjustments.SingleAsync();
        var segment = await db.TreatmentLineageSegments.SingleAsync();
        receipt.BinCount = physical; ledger.ChangeAmount = physical; ledger.NewBinCount = physical; segment.CurrentBins = explicitBins;
        db.OutsideWarehouses.Add(new() { Id = 9005, Code = "HISTORY", Name = "Historical fixture destination" });
        RoomInventoryAdjustment Row(int id, int delta, string kind, int day, int room = 9002)
        {
            var copy = (RoomInventoryAdjustment)db.Entry(ledger).CurrentValues.Clone().ToObject();
            copy.Id = id; copy.ChangeAmount = delta; copy.AdjustmentType = kind; copy.ReceiptId = null; copy.RoomId = room;
            copy.AdjustmentAt = copy.CreatedAt = InventoryEvidenceCorpus.Start.AddDays(day); copy.NewBinCount = physical; return copy;
        }
        TreatmentLineageSegment Segment(int id, int qty, int room = 9002)
        {
            var copy = (TreatmentLineageSegment)db.Entry(segment).CurrentValues.Clone().ToObject();
            copy.Id = id; copy.RoomId = room; copy.CurrentBins = qty; return copy;
        }
        if (shape == "Room14")
        {
            receipt.BinCount = ledger.ChangeAmount = ledger.NewBinCount = 542; segment.CurrentBins = 802;
            foreach (var (id, bins) in new[] { (100001, 184), (100002, 72) })
            {
                var r = (Receipt)db.Entry(receipt).CurrentValues.Clone().ToObject(); r.Id = id; r.CompuTechReceiptId += id; r.BinCount = bins; db.Receipts.Add(r);
                var l = Row(id, bins, "ReceiptAdd", 0); l.ReceiptId = id; db.RoomInventoryAdjustments.Add(l);
                var s = Segment(id, bins); s.ReceiptId = id; db.TreatmentLineageSegments.Add(s);
            }
        }
        if (shape.StartsWith("Evans5_"))
        {
            var inbound = shape == "Evans5_3152" ? 101 : 284;
            ledger.ReceiptId = null; ledger.AdjustmentType = "TransferIn"; ledger.ChangeAmount = inbound; ledger.NewBinCount = inbound;
            db.RoomInventoryAdjustments.Add(Row(100001, physical - inbound, "TransferOut", 1));
            segment.CurrentBins = physical;
            var alias = Segment(100001, inbound); alias.IdentityKey += "CONVENTIONAL"; alias.GrowerLotId = null;
            db.TreatmentLineageSegments.Add(alias);
        }
        if (shape == "StatusAlias")
        {
            segment.CurrentBins = physical;
            var alias = Segment(100001, explicitBins - physical); alias.IdentityKey += "CONVENTIONAL"; db.TreatmentLineageSegments.Add(alias);
        }
        if (shape == "ReceiptReplacement") receipt.BinCount = 20;
        if (shape is "PartialMove" or "PartialDepletion")
        {
            receipt.BinCount = ledger.ChangeAmount = ledger.NewBinCount = 20;
            var delta = Row(100001, -8, shape == "PartialMove" ? "TransferOut" : "Depletion", 1);
            if (shape == "PartialDepletion") delta.ReceiptId = receipt.Id;
            db.RoomInventoryAdjustments.Add(delta);
        }
        if (shape == "ReturnedSource")
        {
            db.RoomTransfers.Add(new()
            {
                Id = 800000,
                OperationKey = "old-transfer",
                SourceWarehouseId = 9001,
                SourceRoomId = 9002,
                DestinationWarehouseId = 9001,
                DestinationRoomId = 9003,
                CropYear = 2026,
                GrowerLotId = 100000,
                FruitProfileId = 9004,
                GrowerName = "Corpus",
                LotNumber = receipt.LotCode!,
                VarietyCode = "CGAL",
                BinCount = 10,
                Reason = "Historical fixture",
                IsReversed = true,
                TransferredAt = InventoryEvidenceCorpus.Start.AddDays(1),
                CreatedAt = InventoryEvidenceCorpus.Start.AddDays(1),
                ReversedAt = InventoryEvidenceCorpus.Start.AddDays(2)
            });
            var dest = Segment(100001, 0, 9003); db.TreatmentLineageSegments.Add(dest);
            foreach (var (id, delta, kind, day, room) in new[] { (800000, -10, "TransferOut", 1, 9002), (800001, 10, "TransferIn", 1, 9003),
                (800002, 10, "TransferReversalIn", 2, 9002), (800003, -10, "TransferReversalOut", 2, 9003) })
            { var row = Row(id, delta, kind, day, room); row.RoomTransferId = 800000; db.RoomInventoryAdjustments.Add(row); }
            foreach (var reverse in new[] { false, true }) db.TreatmentLineageMovements.Add(new()
            {
                Id = reverse ? 800001 : 800000,
                OperationKey = reverse ? "old-return" : "old-move",
                MovementType = reverse ? "TransferReversal" : "Transfer",
                SourceSegmentId = reverse ? dest.Id : segment.Id,
                DestinationSegmentId = reverse ? segment.Id : dest.Id,
                SourceRoomId = reverse ? 9003 : 9002,
                DestinationRoomId = reverse ? 9002 : 9003,
                IdentityKey = segment.IdentityKey,
                TreatmentStateSnapshot = "Untreated",
                TreatmentSignatureSnapshot = "u",
                ReceiptId = receipt.Id,
                BinCount = 10,
                RoomTransferId = 800000,
                ReversesTreatmentLineageMovementId = reverse ? 800000 : null,
                OccurredAt = InventoryEvidenceCorpus.Start.AddDays(reverse ? 2 : 1),
                CreatedAt = InventoryEvidenceCorpus.Start.AddDays(reverse ? 2 : 1)
            });
        }
        if (shape is "BalancedTreatments" or "TreatmentAmbiguity")
        {
            var chemical = await db.TreatmentChemicals.FirstAsync();
            segment.CurrentBins = 10;
            var second = Segment(100001, 10); db.TreatmentLineageSegments.Add(second);
            foreach (var (s, id) in new[] { (segment, 800000L), (second, 800001L) })
            {
                db.RoomTreatmentApplications.Add(new()
                {
                    Id = id,
                    OperationKey = "old-treatment-" + id,
                    TreatmentChemicalId = chemical.Id,
                    WarehouseId = 9001,
                    RoomId = 9002,
                    AppliedByUserId = 8000,
                    CreatedByUserId = 8000,
                    TotalBinsSnapshot = 10,
                    ProductNameSnapshot = chemical.ProductName,
                    CropSnapshot = chemical.Crop,
                    UnitSnapshot = chemical.Unit,
                    CurrencySnapshot = chemical.Currency,
                    AppliedAt = InventoryEvidenceCorpus.Start.AddDays(1),
                    CreatedAt = InventoryEvidenceCorpus.Start.AddDays(1)
                });
                s.TreatmentState = "Confirmed"; s.TreatmentSignature = "u|a:" + id; s.UpdatedAt = InventoryEvidenceCorpus.Start.AddDays(1);
                db.TreatmentLineageSegmentApplications.Add(new() { TreatmentLineageSegmentId = s.Id, RoomTreatmentApplicationId = id, Sequence = 1 });
            }
        }
        if (shape == "ExactReceipt") segment.ReceiptId = receipt.Id;
        if (shape == "AmbiguousReceipt")
        {
            receipt.BinCount = ledger.ChangeAmount = ledger.NewBinCount = 10;
            var r = (Receipt)db.Entry(receipt).CurrentValues.Clone().ToObject(); r.Id = 100001; r.CompuTechReceiptId += "-B"; r.BinCount = 9; db.Receipts.Add(r);
            var l = Row(100001, 9, "ReceiptAdd", 0); l.ReceiptId = r.Id; db.RoomInventoryAdjustments.Add(l);
            db.RoomInventoryAdjustments.Add(Row(100002, -5, "TransferOut", 1));
        }
        if (shape == "Negative")
        {
            receipt.BinCount = ledger.ChangeAmount = ledger.NewBinCount = 19;
            db.RoomInventoryAdjustments.Add(Row(100001, -24, "Depletion", 1));
        }
        await db.SaveChangesAsync();
    }
}

public sealed class InventoryPostgresTheoryAttribute : TheoryAttribute
{
    public InventoryPostgresTheoryAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CANONICAL_INVENTORY_TEST_POSTGRES")))
            Skip = "Requires an isolated local PostgreSQL test connection; never production.";
    }
}
