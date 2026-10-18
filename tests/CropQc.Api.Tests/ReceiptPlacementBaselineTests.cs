using System.Collections.Immutable;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class ReceiptPlacementBaselineTests
{
    [InventoryPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Empty_destination_mixed_allocations_use_one_zero_baseline(bool splitAcknowledgments)
    {
        await using var f = await RecordedMixedDispatchTests.History(29);
        await using var db = f.CreateDbContext();
        db.Rooms.Add(new() { Id = 9010, WarehouseId = 1, Code = "BASELINE", Name = "Synthetic placement" });
        await db.SaveChangesAsync();
        var source = Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db))
            .ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow)).Positions);
        Assert.Equal(652, source.AuthoritativeQuantity);
        Assert.Equal(442, source.RawProjectionQuantity);
        var originals = await History(db);
        var dispatch = new InventoryCommand("baseline-mixed", InventoryCommandKind.InterCompanyDispatch, 8000, DateTimeOffset.UtcNow,
            "Synthetic mixed placement", [Line(source, 2, "u"), Line(source, 2, "u|a:29")], CustodyGroup: "EBS");
        var sent = await f.Execute(dispatch); Assert.True(sent.Status == InventoryCommandStatus.Committed, sent.Detail);
        var parent = await db.InterCrewTransfers.SingleAsync(x => x.Id == sent.Effects[0].ParentId);
        var receipt = new Receipt
        {
            CropYear = 2026,
            CompuTechReceiptId = "SYNTH-BASELINE",
            ReceiptType = "Truck receipt",
            IsTransferReceipt = true,
            WarehouseId = 1,
            RoomId = 9010,
            FruitProfileId = 9004,
            GrowerLotId = 100000,
            GrowerName = "Corpus",
            LotCode = "CORPUS-100000",
            BinCount = 4,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        receipt.VarietyLines.Add(new() { FruitProfileId = 9004, BinCount = 4 });
        parent.ReceivingReceipt = receipt; parent.ConcurrencyVersion++; await db.SaveChangesAsync();
        var moves = await db.TreatmentLineageMovements.AsNoTracking().Where(x => x.InterCrewTransferId == parent.Id).OrderBy(x => x.Id).ToArrayAsync();
        for (var n = 0; n < (splitAcknowledgments ? 2 : 1); n++)
            await Commit(f, await Command(f, parent.Id, receipt.Id, InventoryCommandKind.AcknowledgeTransfer,
                moves.Select(x => new InventoryCustodyQuantity(x.Id, splitAcknowledgments ? 1 : 2)).ToImmutableArray(), 1, 9010));
        var acks = await db.ReceiptCustodyAcknowledgments.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
        await MixedProof(db, 4, 0);
        var place = await Command(f, parent.Id, receipt.Id, InventoryCommandKind.PlaceReceiptCustody,
            acks.Select(x => new InventoryCustodyQuantity(x.Id, x.Quantity)).ToImmutableArray(), 1, 9010);
        await Commit(f, place);
        await MixedProof(db, 0, 4);
        Assert.Equal(originals, await History(db));
        Assert.Equal(10, await db.RoomInventoryAdjustments.CountAsync(x => x.AdjustmentType == "ReceiptAdd"));
        var placements = await db.ReceiptCustodyPlacements.AsNoTracking().Include(x => x.Movement).Include(x => x.InventoryAdjustment).ToArrayAsync();
        Assert.Equal(acks.Length, placements.Length);
        Assert.All(placements, x => { Assert.Equal(x.Quantity, x.Movement!.BinCount); Assert.Equal(x.Quantity, x.InventoryAdjustment!.ChangeAmount); });
        var after = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Replayed, (await f.Execute(place)).Status);
        Assert.Equal(InventoryCommandStatus.Conflict, (await f.Execute(place with { Reason = "Different intent" })).Status);
        Assert.Equal(InventoryCommandStatus.Stale, (await f.Execute(place with { OperationKey = "stale-place" })).Status);
        Assert.Equal(after, await f.Snapshot());
    }

    [InventoryPostgresTheory]
    [InlineData("empty")]
    [InlineData("occupied")]
    [InlineData("stale")]
    public async Task Repeated_same_cohort_credits_preserve_the_original_destination(string destination)
    {
        await using var f = await Fixture.Create();
        var receiving = await f.ReceiveCommand(bins: 19, prepareDestination: destination == "empty" ? null : async () =>
        {
            await InventoryMovementDestinationTests.SeedDestination(f, destination == "stale" ? 9 : 17, 9007, 9006);
            if (destination == "occupied")
            {
                await using var seed = f.CreateDbContext();
                var segment = await seed.TreatmentLineageSegments.SingleAsync(x => x.Id == 200001);
                segment.ReceiptId = 200001; segment.TreatmentState = "Confirmed"; segment.TreatmentSignature = "u|a:200001";
                segment.Applications.Add(new() { RoomTreatmentApplicationId = 200001 });
                var application = await seed.RoomTreatmentApplications.SingleAsync(x => x.Id == 200001);
                application.Sources.Add(new()
                {
                    ReceiptId = 200001,
                    CropYear = 2026,
                    GrowerLotId = 100000,
                    FruitProfileId = 9004,
                    IdentityKey = segment.IdentityKey,
                    GrowerNameSnapshot = "Corpus",
                    LotNumberSnapshot = "CORPUS-100000",
                    VarietyCodeSnapshot = "CGAL",
                    ProductionTypeSnapshot = "Conventional",
                    BinsTreated = 17,
                    PriorTreatmentSignature = "u",
                    ResultTreatmentSignature = segment.TreatmentSignature
                });
                await seed.SaveChangesAsync();
            }
        });
        await using var db = f.CreateDbContext();
        var originalSegments = await db.TreatmentLineageSegments.AsNoTracking().Include(x => x.Applications).Where(x => x.RoomId == 9007).OrderBy(x => x.Id).ToArrayAsync();
        var original = SegmentRows(db, originalSegments);
        var beforeQuantity = await db.RoomInventoryAdjustments.Where(x => x.RoomId == 9007).SumAsync(x => x.ChangeAmount);
        for (var n = 0; n < 2; n++) await ReceiptCustodyCorrectionTests.Commit(f, receiving, InventoryCommandKind.AcknowledgeTransfer, 2);
        var acks = await db.ReceiptCustodyAcknowledgments.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
        var place = await Command(f, receiving.Lines[0].Source.Location.CustodyRecordId!.Value, receiving.ReceivingEvidence!.ReceiptId,
            InventoryCommandKind.PlaceReceiptCustody, acks.Select(x => new InventoryCustodyQuantity(x.Id, 1)).ToImmutableArray(), 9006, 9007);
        await Commit(f, place);
        Assert.Equal(beforeQuantity + 2, await db.RoomInventoryAdjustments.Where(x => x.RoomId == 9007).SumAsync(x => x.ChangeAmount));
        Assert.Equal(2, (await db.ReceiptCustodyAcknowledgments.WithCustodyEvidence().AsNoTracking().ToArrayAsync()).Sum(x => x.HeldQuantity));
        var ids = originalSegments.Select(x => x.Id).ToArray();
        Assert.Equal(original, SegmentRows(db, await db.TreatmentLineageSegments.AsNoTracking().Include(x => x.Applications).Where(x => ids.Contains(x.Id)).OrderBy(x => x.Id).ToArrayAsync()));
        await Commit(f, await Command(f, place.ReceiptCustody!.TransferId, place.ReceiptCustody.ReceiptId, InventoryCommandKind.PlaceReceiptCustody,
            acks.Select(x => new InventoryCustodyQuantity(x.Id, 1)).ToImmutableArray(), 9006, 9007));
        var placement = await db.ReceiptCustodyPlacements.AsNoTracking().OrderBy(x => x.Id).FirstAsync();
        await Commit(f, await Command(f, place.ReceiptCustody.TransferId, place.ReceiptCustody.ReceiptId, InventoryCommandKind.ReverseReceiptPlacement, [new(placement.Id, 1)], 9006, 9007));
        await Commit(f, await Command(f, place.ReceiptCustody.TransferId, place.ReceiptCustody.ReceiptId, InventoryCommandKind.PlaceReceiptCustody, [new(placement.AcknowledgmentId, 1)], 9006, 9007));
        Assert.Equal(beforeQuantity + 4, await db.RoomInventoryAdjustments.Where(x => x.RoomId == 9007).SumAsync(x => x.ChangeAmount));
        await ReceiptCustodyCorrectionTests.Prove(f, receiving, 4, 4);
    }

    [InventoryPostgresTheory]
    [InlineData("duplicate")]
    [InlineData("excess")]
    [InlineData("stale")]
    [InlineData("sealed")]
    [InlineData("receipt")]
    [InlineData("route")]
    public async Task Invalid_multi_allocation_placement_preserves_every_table(string defect)
    {
        await using var f = await Fixture.Create(); var receiving = await f.ReceiveCommand();
        for (var n = 0; n < 2; n++) await ReceiptCustodyCorrectionTests.Commit(f, receiving, InventoryCommandKind.AcknowledgeTransfer, 2);
        await using var db = f.CreateDbContext(); var acks = await db.ReceiptCustodyAcknowledgments.OrderBy(x => x.Id).ToArrayAsync();
        var command = await Command(f, receiving.Lines[0].Source.Location.CustodyRecordId!.Value, receiving.ReceivingEvidence!.ReceiptId,
            InventoryCommandKind.PlaceReceiptCustody, acks.Select(x => new InventoryCustodyQuantity(x.Id, 2)).ToImmutableArray(), 9006, 9007);
        var intent = command.ReceiptCustody!;
        if (defect == "duplicate") intent = intent with { Allocations = [intent.Allocations[0], intent.Allocations[0]] };
        if (defect == "excess") intent = intent with { Allocations = [new(acks[0].Id, 3), intent.Allocations[1]] };
        if (defect == "stale") intent = intent with { TransferVersion = intent.TransferVersion - 1 };
        if (defect == "route") intent = intent with { Destination = new(9001, 9003) };
        if (defect == "receipt") { var wrong = await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == 100000); intent = intent with { ReceiptId = wrong.Id, ReceiptVersion = wrong.ConcurrencyVersion }; }
        if (defect == "sealed") { (await db.Rooms.SingleAsync(x => x.Id == 9007)).IsSealed = true; await db.SaveChangesAsync(); }
        var before = await f.Snapshot(); var result = await f.Execute(command with { ReceiptCustody = intent });
        Assert.Equal(defect == "stale" ? InventoryCommandStatus.Stale : InventoryCommandStatus.Blocked, result.Status);
        Assert.Equal(before, await f.Snapshot());
    }

    [InventoryPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_multi_allocation_placements_credit_once(bool sameKey)
    {
        await using var f = await Fixture.Create(); var receiving = await f.ReceiveCommand();
        for (var n = 0; n < 2; n++) await ReceiptCustodyCorrectionTests.Commit(f, receiving, InventoryCommandKind.AcknowledgeTransfer, 2);
        await using var db = f.CreateDbContext(); var acks = await db.ReceiptCustodyAcknowledgments.OrderBy(x => x.Id).ToArrayAsync();
        var command = await Command(f, receiving.Lines[0].Source.Location.CustodyRecordId!.Value, receiving.ReceivingEvidence!.ReceiptId,
            InventoryCommandKind.PlaceReceiptCustody, acks.Select(x => new InventoryCustodyQuantity(x.Id, 2)).ToImmutableArray(), 9006, 9007);
        var arrived = 0; var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = new Observer(async (stage, attempt) =>
        {
            if (stage != "Resolved" || attempt != 1) return;
            if (Interlocked.Increment(ref arrived) == 2) gate.SetResult();
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(30));
        });
        var results = await Task.WhenAll(f.Execute(command, observer), f.Execute(sameKey ? command : command with { OperationKey = "competitor" }, observer));
        Assert.Single(results, x => x.Status == InventoryCommandStatus.Committed);
        Assert.Single(results, x => x.Status == (sameKey ? InventoryCommandStatus.Replayed : InventoryCommandStatus.Stale));
        Assert.Equal(2, await db.ReceiptCustodyPlacements.CountAsync());
        await ReceiptCustodyCorrectionTests.Prove(f, receiving, 4, 4);
    }

    internal static InventoryCommandLine Line(InventoryAvailabilityResult p, int n, string signature) =>
        new(new(p.Identity, p.Location, p.Watermark.Fingerprint, p.Watermark.Versions), n, signature);

    private static string SegmentRows(CropQcDbContext db, IEnumerable<TreatmentLineageSegment> segments) => JsonSerializer.Serialize(segments.Select(s => new
    {
        Row = db.Entry(s).CurrentValues.Properties.ToDictionary(p => p.Name, p => db.Entry(s).CurrentValues[p]),
        Links = s.Applications.OrderBy(a => a.RoomTreatmentApplicationId).Select(a => db.Entry(a).CurrentValues.Properties.ToDictionary(p => p.Name, p => db.Entry(a).CurrentValues[p])).ToArray()
    }).ToArray());

    internal static async Task Commit(Fixture f, InventoryCommand command)
    { var result = await f.Execute(command); Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail); }

    internal static async Task<InventoryCommand> Command(Fixture f, long transfer, long receipt, InventoryCommandKind kind,
        ImmutableArray<InventoryCustodyQuantity> quantities, int warehouse, int room)
    {
        await using var db = f.CreateDbContext(); var t = await db.InterCrewTransfers.AsNoTracking().SingleAsync(x => x.Id == transfer);
        var r = await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == receipt);
        return new(Guid.NewGuid().ToString("N"), kind, 8000, DateTimeOffset.UtcNow, "Synthetic placement baseline", [],
            ReceiptCustody: new(t.Id, r.Id, t.ConcurrencyVersion, r.ConcurrencyVersion, quantities, kind == InventoryCommandKind.PlaceReceiptCustody ? new(warehouse, room) : null));
    }

    internal static async Task MixedProof(CropQcDbContext db, int held, int placed)
    {
        db.ChangeTracker.Clear(); var resolver = new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db));
        var all = new List<InventoryAvailabilityResult>();
        foreach (var custody in new[] { InventoryCustody.Room, InventoryCustody.InTransit, InventoryCustody.ReceiptHeld })
            all.AddRange((await resolver.ResolveAsync(new(null, [9002, 9010], custody), new(AllowedCustody: custody), DateTimeOffset.UtcNow)).Positions.Where(x => x.Identity.GrowerLotId == 100000));
        Assert.Equal(652, all.Sum(x => x.AuthoritativeQuantity));
        Assert.Equal(648, all.Where(x => x.Location.RoomId == 9002).Sum(x => x.AuthoritativeQuantity));
        Assert.Equal(held, all.Where(x => x.Location.Custody == InventoryCustody.ReceiptHeld).Sum(x => x.AuthoritativeQuantity));
        Assert.Equal(placed, all.Where(x => x.Location.RoomId == 9010).Sum(x => x.AuthoritativeQuantity));
        Assert.All(all.Where(x => x.AuthoritativeQuantity > 0), x => Assert.True(x.IsOperable, JsonSerializer.Serialize(x.Blockers)));
        var slices = all.SelectMany(x => x.TreatmentSlices).ToArray();
        Assert.Equal(582, slices.Where(x => x.Signature == "u").Sum(x => x.Quantity));
        Assert.Equal(70, slices.Where(x => x.Signature == "u|a:29").Sum(x => x.Quantity));
        Assert.Equal(56, slices.Where(x => x.ReceiptEvidenceIds.IsEmpty).Sum(x => x.Quantity));
        Assert.All(slices, x => Assert.Equal(x.Signature == "u" ? [] : new long[] { 29 }, x.ApplicationIds.ToArray()));
        Assert.Empty((await InterCrewTopologyReadiness.VerifyAsync(db, default)).Issues);
        Assert.True((await new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance).VerifyReadinessAsync(default)).IsReady);
        Assert.True((await new TreatmentLineageReadinessService(new CropQc.Web.Services.RoomInventoryLedgerQueryService(db), db).VerifyAsync(default)).Success);
    }

    internal static async Task<string> History(CropQcDbContext db) => JsonSerializer.Serialize(new
    {
        Receipts = await db.Receipts.AsNoTracking().Where(x => x.Id >= 100000).OrderBy(x => x.Id).ToArrayAsync(),
        Ledger = await db.RoomInventoryAdjustments.AsNoTracking().Where(x => x.Id >= 100000).OrderBy(x => x.Id).ToArrayAsync(),
        Applications = await db.RoomTreatmentApplications.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Sources = await db.RoomTreatmentApplicationSources.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()
    });
}
