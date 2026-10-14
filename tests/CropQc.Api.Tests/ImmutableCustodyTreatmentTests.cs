using System.Collections.Immutable;
using System.Text.Json;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class ImmutableCustodyTreatmentTests
{
    [Theory]
    [InlineData("missing-proof")]
    [InlineData("unknown")]
    [InlineData("different-source")]
    [InlineData("different-receipt")]
    [InlineData("unlinked-application")]
    [InlineData("future-application")]
    [InlineData("late-recording")]
    [InlineData("duplicate-allocation")]
    public void Custody_requires_original_membership_and_movement_proof(string defect)
    {
        var e = InventoryEvidenceCorpus.Custody(InventoryCustody.InTransit);
        var p = e.Projections[0];
        var proof = p.CustodyTreatment!;
        var at = proof.Movement.At;
        if (defect is "unlinked-application" or "future-application" or "late-recording")
        {
            p = p with { State = "Confirmed", Signature = "u|a:1", ApplicationIds = [1] };
            proof = proof with
            {
                SourceState = "Confirmed",
                SourceSignature = "u|a:1",
                ApplicationIds = [1],
                Movement = proof.Movement with { State = "Confirmed", Signature = "u|a:1" }
            };
            e = e with
            {
                Applications = defect == "unlinked-application" ? [] :
                [new(1, defect == "future-application" ? at.AddSeconds(1) : at, null, p.ReceiptId)
                    { RecordedAt = defect == "late-recording" ? at.AddSeconds(1) : at }]
            };
        }
        if (defect == "unknown") { p = p with { State = "Unknown" }; proof = proof with { SourceState = "Unknown", Movement = proof.Movement with { State = "Unknown" } }; }
        if (defect == "different-source") proof = proof with { Movement = proof.Movement with { SourceProjectionId = 999 } };
        if (defect == "different-receipt") proof = proof with { Movement = proof.Movement with { ReceiptId = 999 } };
        p = p with { CustodyTreatment = defect == "missing-proof" ? null : proof };
        e = e with
        {
            Projections = defect == "duplicate-allocation" ? [p, p] : [p],
            AuthoritativeQuantity = defect == "duplicate-allocation" ? 38 : 19
        };
        var before = JsonSerializer.Serialize(e);
        var result = InventoryAvailabilityResolver.Resolve(e, new(AllowedCustody: InventoryCustody.InTransit));
        Assert.False(result.IsOperable);
        Assert.Equal(0, result.AvailableQuantity);
        Assert.Equal(before, JsonSerializer.Serialize(e));
    }

    [Fact]
    public void Unknown_receipt_ancestry_is_not_invented_by_transit_treatment_proof()
    {
        var e = InventoryEvidenceCorpus.Custody(InventoryCustody.InTransit);
        var p = e.Projections[0]; var proof = p.CustodyTreatment!;
        p = p with { ReceiptId = null, CustodyTreatment = proof with { SourceReceiptId = null, Movement = proof.Movement with { ReceiptId = null } } };
        e = e with { Projections = [p], Receipts = [] };
        var result = InventoryAvailabilityResolver.Resolve(e, new(AllowedCustody: InventoryCustody.InTransit));
        Assert.True(result.IsOperable);
        Assert.Equal(19, result.AvailableQuantity);
        Assert.Equal(InventoryConfidence.Unknown, result.ReceiptProvenance.Confidence);
        Assert.Empty(result.TreatmentSlices.Single().ReceiptEvidenceIds);
        Assert.False(InventoryAvailabilityResolver.Resolve(e, new(RequireExactReceipt: true, AllowedCustody: InventoryCustody.InTransit)).IsOperable);
    }

    [InventoryPostgresTheory]
    [InlineData("together")]
    [InlineData("treated-only")]
    [InlineData("untreated-only")]
    [InlineData("repeated-partials")]
    public async Task Same_lot_departures_preserve_exact_treatment_and_exclusive_custody(string selection)
    {
        await using var f = await Fixture.Create(0);
        await using var db = f.CreateDbContext();
        db.GrowerLots.Add(new() { Id = 100000, Grower = "Custody proof", LotNumber = "MIXED" });
        db.Rooms.AddRange(new Room { Id = 9010, WarehouseId = 1, Code = "HELD", Name = "Receipt placement" },
            new Room { Id = 9011, WarehouseId = 1, Code = "NEXT", Name = "Subsequent move" });
        db.TreatmentChemicals.Add(new() { Id = 990001, ProductName = "Receipt treatment", ApplicationLevel = "Receiving", Crop = "Apples", Volume = 1, Unit = "BIN", UnitPrice = 1, Currency = "USD", IsActive = true });
        await db.SaveChangesAsync();
        async Task<InventoryCommandResult> Commit(InventoryCommand c)
        {
            var r = await f.Execute(c); Assert.True(r.Status == InventoryCommandStatus.Committed, r.Detail);
            db.ChangeTracker.Clear(); return r;
        }
        async Task<long> Receive(string key) => (await Commit(new(key, InventoryCommandKind.ReceiveStock, 8000,
            DateTimeOffset.UtcNow, "Recorded arrival", [], Receipt: new(2026, 9001, 9002, 100000, 9004, key, 15)))).Effects.Single().ParentId!.Value;
        async Task<InventoryAvailabilityResult> Position(int room) => Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db))
            .ResolveAsync(new(null, [room]), new(), DateTimeOffset.UtcNow)).Positions);
        async Task<InventoryCommandLine> Line(int room, int quantity, string signature, InventoryCommandDestination? destination = null)
        {
            var p = await Position(room); Assert.True(p.IsOperable, string.Join(',', p.Blockers.Select(x => x.Code)));
            return new(new(p.Identity, p.Location, p.Watermark.Fingerprint, p.Watermark.Versions), quantity, signature, destination);
        }
        var treatedReceipt = await Receive("mixed-treated");
        var application = (await Commit(new("mixed-treatment", InventoryCommandKind.ReceiptTreatmentAssignment, 8000,
            DateTimeOffset.UtcNow, "Only first arrival present", [(await Line(9002, 15, "u")) with { ReceiptId = treatedReceipt }], TreatmentChemicalId: 990001))).Effects.Single().ParentId!.Value;
        var untreatedReceipt = await Receive("mixed-later");
        var applicationBefore = JsonSerializer.Serialize(await db.RoomTreatmentApplications.AsNoTracking().SingleAsync());
        var sourceBefore = JsonSerializer.Serialize(await db.RoomTreatmentApplicationSources.AsNoTracking().ToArrayAsync());
        var signature = $"u|a:{application}";
        var transfers = new List<long>();
        foreach (var quantity in selection == "repeated-partials" ? new[] { 7, 8 } : new[] { 15 })
        {
            var lines = ImmutableArray.CreateBuilder<InventoryCommandLine>();
            if (selection != "untreated-only") lines.Add(await Line(9002, quantity, signature));
            if (selection != "treated-only") lines.Add(await Line(9002, quantity, "u"));
            var command = new InventoryCommand($"mixed-dispatch-{quantity}", InventoryCommandKind.InterCompanyDispatch, 8000,
                DateTimeOffset.UtcNow, "Immutable mixed allocation", lines.ToImmutable(), CustodyGroup: "EBS");
            transfers.Add((await Commit(command)).Effects[0].ParentId!.Value);
            var snapshot = await f.Snapshot();
            Assert.Equal(InventoryCommandStatus.Replayed, (await f.Execute(command)).Status);
            Assert.Equal(snapshot, await f.Snapshot());
            await Prove();
        }
        if (selection == "together")
        {
            var transfer = await db.InterCrewTransfers.SingleAsync(x => x.Id == transfers[0]);
            var receipt = new Receipt
            {
                CropYear = 2026,
                CompuTechReceiptId = "MIXED-TRANSFER",
                ReceiptType = "Truck receipt",
                IsTransferReceipt = true,
                WarehouseId = 1,
                RoomId = 9010,
                FruitProfileId = 9004,
                GrowerLotId = 100000,
                GrowerName = "Transfer",
                LotCode = "TRANSFER",
                BinCount = 30,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            receipt.VarietyLines.Add(new() { FruitProfileId = 9004, BinCount = 30 });
            transfer.ReceivingReceipt = receipt; transfer.ConcurrencyVersion++; await db.SaveChangesAsync();
            async Task<InventoryCommand> Custody(InventoryCommandKind kind, ImmutableArray<InventoryCustodyQuantity> quantities, string key)
            {
                var t = await db.InterCrewTransfers.AsNoTracking().SingleAsync(x => x.Id == transfers[0]);
                var r = await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == receipt.Id);
                return new(key, kind, 8000, DateTimeOffset.UtcNow, "Exact allocation custody", [], ReceiptCustody:
                    new(t.Id, r.Id, t.ConcurrencyVersion, r.ConcurrencyVersion, quantities, kind == InventoryCommandKind.PlaceReceiptCustody ? new(1, 9010) : null));
            }
            var dispatch = await db.TreatmentLineageMovements.Where(x => x.InterCrewTransferId == transfer.Id).OrderBy(x => x.Id).ToArrayAsync();
            await Commit(await Custody(InventoryCommandKind.AcknowledgeTransfer, dispatch.Select(x => new InventoryCustodyQuantity(x.Id, 10)).ToImmutableArray(), "mixed-ack"));
            await Prove();
            var acks = await db.ReceiptCustodyAcknowledgments.OrderBy(x => x.Id).ToArrayAsync();
            await Commit(await Custody(InventoryCommandKind.ReverseReceiptAcknowledgment, [new(acks[1].Id, 2)], "mixed-unack"));
            await Prove();
            await Commit(await Custody(InventoryCommandKind.AcknowledgeTransfer, [new(dispatch[1].Id, 2)], "mixed-reack"));
            await Prove();
            await Commit(await Custody(InventoryCommandKind.PlaceReceiptCustody, acks.Select(x => new InventoryCustodyQuantity(x.Id, 7)).ToImmutableArray(), "mixed-place"));
            await Prove();
            var placement = await db.ReceiptCustodyPlacements.OrderBy(x => x.Id).FirstAsync();
            await Commit(await Custody(InventoryCommandKind.ReverseReceiptPlacement, [new(placement.Id, 2)], "mixed-unplace"));
            await Prove();
            await Commit(await Custody(InventoryCommandKind.PlaceReceiptCustody, [new(placement.AcknowledgmentId, 2)], "mixed-replace"));
            await Commit(new("mixed-room-move", InventoryCommandKind.RoomMove, 8000, DateTimeOffset.UtcNow, "Treatment follows bins",
                [await Line(9010, 3, signature, new(1, 9011)), await Line(9010, 3, "u", new(1, 9011))]));
            await Prove();
            // Finish both original cohorts: the destination rooms hold exactly 15+15.
            await Commit(await Custody(InventoryCommandKind.AcknowledgeTransfer, dispatch.Select(x => new InventoryCustodyQuantity(x.Id, 5)).ToImmutableArray(), "mixed-ack-rest"));
            var remaining = await db.ReceiptCustodyAcknowledgments.WithCustodyEvidence().AsNoTracking().ToArrayAsync();
            await Commit(await Custody(InventoryCommandKind.PlaceReceiptCustody, remaining.Where(x => x.HeldQuantity > 0).Select(x => new InventoryCustodyQuantity(x.Id, x.HeldQuantity)).ToImmutableArray(), "mixed-place-rest"));
            await Prove();
            Assert.Equal(30, await db.RoomInventoryAdjustments.Where(x => x.RoomId == 9010 || x.RoomId == 9011).SumAsync(x => x.ChangeAmount));
        }
        Assert.Equal(applicationBefore, JsonSerializer.Serialize(await db.RoomTreatmentApplications.AsNoTracking().SingleAsync()));
        Assert.Equal(sourceBefore, JsonSerializer.Serialize(await db.RoomTreatmentApplicationSources.AsNoTracking().ToArrayAsync()));
        Assert.Equal(15, await db.Receipts.Where(x => x.Id == treatedReceipt).Select(x => x.BinCount).SingleAsync());
        Assert.Equal(15, await db.Receipts.Where(x => x.Id == untreatedReceipt).Select(x => x.BinCount).SingleAsync());

        async Task Prove()
        {
            db.ChangeTracker.Clear();
            var beforeReadiness = await f.Snapshot();
            var topology = await InterCrewTopologyReadiness.VerifyAsync(db, default);
            Assert.True(topology.Issues.Count == 0, JsonSerializer.Serialize(topology));
            Assert.Equal(beforeReadiness, await f.Snapshot());
            var resolver = new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db));
            var positions = new List<InventoryAvailabilityResult>();
            foreach (var custody in new[] { InventoryCustody.Room, InventoryCustody.InTransit, InventoryCustody.ReceiptHeld })
                positions.AddRange((await resolver.ResolveAsync(new(null, [9002, 9010, 9011], custody), new(AllowedCustody: custody), DateTimeOffset.UtcNow)).Positions.Where(x => x.Identity.GrowerLotId == 100000));
            Assert.All(positions.Where(x => x.AuthoritativeQuantity > 0), p => Assert.True(p.IsOperable, string.Join(',', p.Blockers.Select(x => x.Code))));
            Assert.Equal(30, positions.Sum(x => x.AuthoritativeQuantity));
            var slices = positions.SelectMany(x => x.TreatmentSlices).ToArray();
            Assert.Equal(15, slices.Where(x => x.Signature == signature).Sum(x => x.Quantity));
            Assert.Equal(15, slices.Where(x => x.Signature == "u").Sum(x => x.Quantity));
            Assert.All(slices, s =>
            {
                Assert.Equal(s.Signature == "u" ? untreatedReceipt : treatedReceipt, Assert.Single(s.ReceiptEvidenceIds));
                Assert.Equal(s.Signature == "u" ? Array.Empty<long>() : [application], s.ApplicationIds.ToArray());
            });
        }
    }
}

