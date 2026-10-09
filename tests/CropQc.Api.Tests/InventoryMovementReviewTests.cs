using System.Collections.Immutable;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class InventoryMovementReviewTests
{
    private static InventoryPositionEvidence MixedArrivals()
    {
        var e = InventoryMovementEvidenceTests.ReadObservation().Single(x => x.Location.RoomId == 55 && x.Identity.Lot == "1242");
        var origin = Assert.Single(e.Ledger);
        return e with
        {
            AuthoritativeQuantity = 44,
            Projections = [],
            ApplicationAllocationsLoaded = true,
            Ledger = [origin, new(900001, 10, "TransferIn", origin.At.AddHours(1), null, "room:900001", true, origin.At.AddHours(1))],
            Movements = [new(900001, "Transfer", 10, origin.At.AddHours(1), origin.At.AddHours(1), "u", "Untreated", null, null, null, true, false, "room:900001", null, true)],
            Applications = [new(900001, origin.At.AddHours(2), null, null, 55)
            {
                RecordedAt = origin.At.AddHours(2),
                Allocations = [new(1, 10, null, "u", "u|a:900001", true), new(2, 34, origin.ReceiptId, "u", "u|a:900001", true)]
            }]
        };
    }

    [Fact]
    public void Room_application_keeps_shared_and_receipt_owned_allocations_disjoint()
    {
        var e = MixedArrivals();
        var replay = InventoryEventReplay.Replay(e);
        Assert.True(replay.QuantityConserved);
        Assert.Empty(replay.UnresolvedEvents);
        Assert.Equal(44, replay.Cohorts.Sum(x => x.Quantity));
        Assert.Equal(10, Assert.Single(replay.Cohorts, x => x.ReceiptId == null).Quantity);
        Assert.All(replay.Cohorts, x => Assert.Equal("u|a:900001", x.Signature));
        Assert.True(InventoryDestinationAdmission.Assess(e).Allowed);
    }

    [Fact]
    public void Invalid_application_allocations_are_diagnostics_without_throwing_or_blocking_incoming_inventory()
    {
        var e = MixedArrivals();
        e = e with
        {
            Applications = [e.Applications[0] with { Allocations = e.Applications[0].Allocations.SetItem(0,
            e.Applications[0].Allocations[0] with { Quantity = 11 }) }]
        };
        var replay = InventoryEventReplay.Replay(e);
        Assert.True(replay.QuantityConserved);
        Assert.NotEmpty(replay.UnresolvedEvents);
        Assert.All(replay.Cohorts, x => Assert.Equal("Unknown", x.State));
        Assert.True(InventoryDestinationAdmission.Assess(e).Allowed);
    }

    [Fact]
    public void Backdated_transfer_cannot_be_reconstructed_as_untreated_across_recorded_room_treatment()
    {
        var e = MixedArrivals();
        var incoming = e.Ledger[1];
        e = e with
        {
            Ledger = [e.Ledger[0], incoming with { RecordedAt = incoming.At.AddHours(3) }],
            Movements = [e.Movements[0] with { CreatedAt = incoming.At.AddHours(3) }],
            Applications = [e.Applications[0] with { Allocations = [e.Applications[0].Allocations[1]] }]
        };
        var replay = InventoryEventReplay.Replay(e);
        Assert.True(replay.QuantityConserved);
        Assert.Equal("Unknown", Assert.Single(replay.Cohorts, x => x.ReceiptId == null).State);
        Assert.Contains(replay.UnresolvedEvents, x => x.Contains("900001"));
    }

    [InventoryPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Committed_scalar_receipt_correction_retains_its_origin_for_subsequent_movement(bool correctIdentity)
    {
        await using var f = await Fixture.Create();
        var correction = await f.Command(InventoryCommandKind.ReceiptCorrection, 3);
        correction = correction with { Lines = [correction.Lines[0] with { ReceiptId = 100000 }] };
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(correction)).Status);
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        if (correctIdentity)
        {
            db.GrowerLots.Add(new() { Id = 100001, Grower = "Second", LotNumber = "SECOND", IsActive = true });
            await db.SaveChangesAsync();
            var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, new InventoryCommandExecutor(f));
            var form = await CanonicalReceiptCorrectionWorkflowTests.Form(db, service, 100000, 16);
            form.GrowerLotId = 100001;
            var changed = await service.ApplyEditAsync(form, CanonicalReceivingWorkflowTests.Operator().HttpContext!.User, default);
            Assert.Null(changed.Error);
        }
        var position = Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db))
            .ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow)).Positions, x => x.AuthoritativeQuantity > 0);
        var command = new InventoryCommand(Guid.NewGuid().ToString("N"), InventoryCommandKind.RoomMove, 8000,
            DateTimeOffset.UtcNow, "Move after committed corrections",
            [new(new(position.Identity, position.Location, position.Watermark.Fingerprint, position.Watermark.Versions), 7, "u", new(9001, 9003))]);
        var moved = await f.Execute(command);
        Assert.True(moved.Status == InventoryCommandStatus.Committed, moved.Detail);
        Assert.Equal(9, await f.Physical()); Assert.Equal(7, await f.Physical(9003));
        Assert.Equal(16, await db.RoomInventoryAdjustments.SumAsync(x => x.ChangeAmount));
        Assert.Equal("19", await db.ReceiptInventoryOverrides.OrderBy(x => x.CreatedAt).Select(x => x.BeforeReceiptSnapshotJson).FirstAsync());
    }

    [Fact]
    public void Shared_withdrawal_does_not_move_later_arrivals_before_a_backdated_application()
    {
        var e = MixedArrivals();
        var time = e.Ledger[0].At;
        e = e with
        {
            AuthoritativeQuantity = 39,
            Ledger = e.Ledger.Add(new(900002, -5, "ReceiptAdminOverride", time.AddHours(2), null, null, true, time.AddHours(2))),
            Applications = [e.Applications[0] with
            {
                AppliedAt = time.AddMinutes(30), RecordedAt = time.AddHours(3),
                Allocations = [new(1, 39, null, "u", "u|a:900001", true)]
            }]
        };
        var replay = InventoryEventReplay.Replay(e);
        Assert.True(replay.QuantityConserved);
        Assert.Equal("Unknown", Assert.Single(replay.Cohorts).State);
        Assert.Contains(replay.UnresolvedEvents, x => x.Contains("900001") && x.Contains("arrival"));
    }
}
