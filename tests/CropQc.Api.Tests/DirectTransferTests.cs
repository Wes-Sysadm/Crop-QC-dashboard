using System.Collections.Immutable;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using CropQc.Data;
using CropQc.Web.Services;
using RoomInventoryLedgerQueryService = CropQc.Web.Services.RoomInventoryLedgerQueryService;
using Fixture = CropQc.Api.Tests.InventoryCommandTests.Fixture;

namespace CropQc.Api.Tests;

public sealed class DirectTransferTests
{
    [InventoryPostgresFact]
    public async Task Cross_company_direct_transfer_conserves_all_allocations_without_receiving_and_supports_later_use()
    {
        await using var f = await Fixture.Create(2);
        await using var db = f.CreateDbContext();
        db.Rooms.Add(new() { Id = 9008, WarehouseId = 1, Code = "DIRECT-EBS", Name = "Direct destination" });
        await db.SaveChangesAsync();
        var positions = (await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db))
            .ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow)).Positions;
        var command = new InventoryCommand(Guid.NewGuid().ToString("N"), InventoryCommandKind.DirectTransfer, 8000,
            DateTimeOffset.UtcNow, "Direct cross-company movement", positions.Select(p => Line(p, 11, new(1, 9008))).ToImmutableArray());
        var result = await f.Execute(command);
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
        Assert.Equal(2, result.Effects.Length);
        Assert.Equal(16, await f.Physical());
        Assert.Equal(0, await db.InterCrewTransfers.CountAsync());
        Assert.Equal(0, await db.ReceiptCustodyAcknowledgments.CountAsync());
        Assert.All(await db.RoomTransfers.ToListAsync(), x => Assert.True(x.IsDirectTransfer));
        Assert.All(await db.RoomInventoryAdjustments.Where(x => x.RoomTransferId != null).GroupBy(x => x.RoomTransferId)
            .Select(x => x.Sum(a => a.ChangeAmount)).ToListAsync(), x => Assert.Equal(0, x));
        var snapshot = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Replayed, (await f.Execute(command)).Status);
        Assert.Equal(snapshot, await f.Snapshot());
        var incoming = await Position(f, 1, 9008);
        Assert.Equal(22, incoming.Sum(x => x.AuthoritativeQuantity));
        var onward = command with { OperationKey = Guid.NewGuid().ToString("N"), Lines = incoming.Select(p => Line(p, 11, new(9001, 9003))).ToImmutableArray() };
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(onward)).Status);
        Assert.Equal(22, await f.Physical(9003));
    }

    [InventoryPostgresFact]
    public async Task Missing_quantity_is_assigned_to_source_without_faking_verification_and_can_be_relocated_with_evidence()
    {
        await using var f = await Fixture.Create();
        var original = await f.Command(InventoryCommandKind.DirectTransfer, 11);
        var sent = await f.Execute(original);
        Assert.True(sent.Status == InventoryCommandStatus.Committed, sent.Detail);
        var destination = Assert.Single(await Position(f, 9001, 9003));
        var correction = new InventoryCommand(Guid.NewGuid().ToString("N"), InventoryCommandKind.CorrectDirectTransfer, 8000,
            DateTimeOffset.UtcNow.AddDays(-1), "Three bins reported missing", [Line(destination, 3, new(9001, 9002))],
            PhysicalParentId: sent.Effects.Single().ParentId, LocationVerification: new(false, "Destination reported a shortage; source presence not confirmed."));
        var returned = await f.Execute(correction);
        Assert.True(returned.Status == InventoryCommandStatus.Committed, returned.Detail);
        Assert.Equal(11, await f.Physical());
        Assert.Equal(8, await f.Physical(9003));
        await using var db = f.CreateDbContext();
        var record = await db.RoomTransfers.SingleAsync(x => x.Id == returned.Effects.Single().ParentId);
        Assert.False(record.PhysicalLocationVerified);
        var report = await new InventoryConservationReportService(db, new RoomInventoryLedgerQueryService(db), 2026).AnalyzeAsync(default);
        Assert.Equal(record.Id, Assert.Single(report.UnverifiedTransferAssignments).TransferId);
        Assert.Empty(report.UnbalancedRoomTransfers);
        Assert.Equal(sent.Effects.Single().ParentId, record.CorrectsRoomTransferId);
        Assert.True(record.TransferredAt > correction.EffectiveAt);
        Assert.False((await db.RoomTransfers.SingleAsync(x => x.Id == sent.Effects.Single().ParentId)).IsReversed);
        var snapshot = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Replayed, (await f.Execute(correction)).Status);
        Assert.Equal(snapshot, await f.Snapshot());
        var source = Assert.Single(await Position(f, 9001, 9002));
        var verified = correction with
        {
            OperationKey = Guid.NewGuid().ToString("N"),
            PhysicalParentId = record.Id,
            Lines = [Line(source, 3, new(9001, 9003))],
            LocationVerification = new(true, "Operator located and moved the three assigned bins to the destination.")
        };
        var resolved = await f.Execute(verified);
        Assert.True(resolved.Status == InventoryCommandStatus.Committed, resolved.Detail);
        Assert.Equal(8, await f.Physical());
        Assert.Equal(11, await f.Physical(9003));
        Assert.False((await db.RoomTransfers.AsNoTracking().SingleAsync(x => x.Id == record.Id)).PhysicalLocationVerified);
        Assert.True((await db.RoomTransfers.SingleAsync(x => x.Id == resolved.Effects.Single().ParentId)).PhysicalLocationVerified);
        report = await new InventoryConservationReportService(db, new RoomInventoryLedgerQueryService(db), 2026).AnalyzeAsync(default);
        Assert.Empty(report.UnverifiedTransferAssignments);
        Assert.Equal(1, await db.Receipts.CountAsync());
    }

    [InventoryPostgresFact]
    public async Task Overdraw_duplicate_correction_and_unverified_other_destination_are_rejected_without_writes()
    {
        await using var f = await Fixture.Create();
        await using var db = f.CreateDbContext();
        db.Rooms.Add(new() { Id = 9008, WarehouseId = 9001, Code = "THIRD", Name = "Other location" });
        await db.SaveChangesAsync();
        var tooMany = await f.Command(InventoryCommandKind.DirectTransfer, 20);
        var before = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Blocked, (await f.Execute(tooMany)).Status);
        Assert.Equal(before, await f.Snapshot());
        var sent = await f.Execute(await f.Command(InventoryCommandKind.DirectTransfer, 5));
        var correction = new InventoryCommand(Guid.NewGuid().ToString("N"), InventoryCommandKind.CorrectDirectTransfer, 8000,
            DateTimeOffset.UtcNow, "Missing", [Line(Assert.Single(await Position(f, 9001, 9003)), 5, new(9001, 9002))],
            PhysicalParentId: sent.Effects.Single().ParentId, LocationVerification: new(false, "Reported missing"));
        before = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Blocked, (await f.Execute(correction with
        { Lines = [correction.Lines[0] with { Destination = new(9001, 9008) }] })).Status);
        Assert.Equal(before, await f.Snapshot());
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(correction)).Status);
        before = await f.Snapshot();
        Assert.NotEqual(InventoryCommandStatus.Committed, (await f.Execute(correction with { OperationKey = Guid.NewGuid().ToString("N") })).Status);
        Assert.Equal(before, await f.Snapshot());
    }

    internal static InventoryCommandLine Line(InventoryAvailabilityResult p, int quantity, InventoryCommandDestination destination) =>
        new(new(p.Identity, p.Location, p.Watermark.Fingerprint, p.Watermark.Versions), quantity, p.TreatmentSlices[0].Signature, destination);
    internal static async Task<ImmutableArray<InventoryAvailabilityResult>> Position(Fixture f, int warehouse, int room)
    {
        await using var db = f.CreateDbContext();
        return (await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(warehouse, [room]), new(), DateTimeOffset.UtcNow)).Positions;
    }

    [InventoryPostgresFact]
    public async Task Conversion_moves_only_proven_held_remainder_and_retains_unresolved_history()
    {
        await using var f = await Fixture.Create();
        var receiving = await f.ReceiveCommand();
        await ReceiptCustodyCorrectionTests.Commit(f, receiving, InventoryCommandKind.AcknowledgeTransfer, 18);
        await ReceiptCustodyCorrectionTests.Commit(f, receiving, InventoryCommandKind.PlaceReceiptCustody, 7);
        await DirectTransferMigrationTests.InstallWriterFence(f);
        var placement = await ReceiptCustodyCorrectionTests.Build(f, receiving, InventoryCommandKind.PlaceReceiptCustody, 11);
        var conversion = placement with { Kind = InventoryCommandKind.ConvertLegacyTransfer };
        var converted = await f.Execute(conversion);
        Assert.True(converted.Status == InventoryCommandStatus.Committed, converted.Detail);
        await using var db = f.CreateDbContext();
        Assert.Equal(18, await db.ReceiptCustodyPlacements.SumAsync(x => x.Quantity));
        Assert.Equal(18, await db.ReceiptCustodyAcknowledgments.SumAsync(x => x.Quantity));
        var transfer = await db.InterCrewTransfers.SingleAsync();
        Assert.Equal(19, transfer.BinsLoaded);
        Assert.Equal("InTransit", transfer.Status);
        Assert.Equal(18, transfer.BinsReceived);
        Assert.Equal(2, await db.Receipts.CountAsync());
        var snapshot = await f.Snapshot();
        Assert.False(await db.RoomTransfers.AnyAsync());
        await DirectTransferMigrationTests.AssertOldWriterRejected(f);
        Assert.Equal(snapshot, await f.Snapshot());
        Assert.Equal(InventoryCommandStatus.Replayed, (await f.Execute(conversion)).Status);
        Assert.Equal(snapshot, await f.Snapshot());
        var unsupported = await ReceiptCustodyCorrectionTests.Build(f, receiving, InventoryCommandKind.PlaceReceiptCustody, 1);
        Assert.Equal(InventoryCommandStatus.Blocked, (await f.Execute(unsupported with { Kind = InventoryCommandKind.ConvertLegacyTransfer })).Status);
        Assert.Equal(snapshot, await f.Snapshot());
        var transit = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db))
            .ResolveAsync(new(9001, [], InventoryCustody.InTransit, transfer.Id), new(AllowedCustody: InventoryCustody.InTransit), DateTimeOffset.UtcNow);
        Assert.Equal(1, transit.Positions.Sum(x => x.AuthoritativeQuantity));
        Assert.All(transit.Positions, p => Assert.True(p.IsOperable));
        var reverse = await ReceiptCustodyCorrectionTests.Build(f, receiving, InventoryCommandKind.ReverseReceiptPlacement, 1);
        var convertedPlacement = await db.ReceiptCustodyPlacements.SingleAsync(x => x.OperationKey == conversion.OperationKey);
        reverse = reverse with { ReceiptCustody = reverse.ReceiptCustody! with { Allocations = [new(convertedPlacement.Id, 1)] } };
        var reversed = await f.Execute(reverse);
        Assert.True(reversed.Status == InventoryCommandStatus.Committed, reversed.Detail);
        var report = await new InventoryConservationReportService(db, new RoomInventoryLedgerQueryService(db), 2026).AnalyzeAsync(default);
        Assert.Empty(report.InvalidReceiptCompensations);
        Assert.Equal(1, report.ReceiptHeldBins);
    }

    [InventoryPostgresFact]
    public async Task Treated_and_untreated_allocations_keep_distinct_histories_in_one_direct_transfer()
    {
        await using var f = await Fixture.Create(2);
        await using var db = f.CreateDbContext();
        var chemical = new CropQc.Data.Entities.TreatmentChemical
        {
            ProductName = "Direct transfer receiving treatment",
            ApplicationLevel = "Receiving",
            Crop = "Apples",
            Volume = 1,
            Unit = "BIN",
            UnitPrice = 1,
            Currency = "USD",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.TreatmentChemicals.Add(chemical);
        await db.SaveChangesAsync();
        var p = (await Position(f, 9001, 9002)).Single(x => x.Identity.GrowerLotId == 100000);
        var treatment = new InventoryCommand(Guid.NewGuid().ToString("N"), InventoryCommandKind.ReceiptTreatmentAssignment, 8000,
            DateTimeOffset.UtcNow, "Treat first receipt only", [Line(p, 19, new(9001, 9003)) with { Destination = null, ReceiptId = 100000 }], TreatmentChemicalId: chemical.Id);
        var applied = await f.Execute(treatment);
        Assert.True(applied.Status == InventoryCommandStatus.Committed, applied.Detail);
        var arrival = await f.Execute(new(Guid.NewGuid().ToString("N"), InventoryCommandKind.ReceiveStock, 8000,
            DateTimeOffset.UtcNow, "Later untreated fruit of the same identity", [],
            Receipt: new(2026, 9001, 9002, 100000, 9004, "LATER-DIRECT-TEST", 19)));
        Assert.True(arrival.Status == InventoryCommandStatus.Committed, arrival.Detail);
        var positions = await Position(f, 9001, 9002);
        Assert.Equal(2, positions.SelectMany(x => x.TreatmentSlices).Select(x => x.Signature).Distinct().Count());
        var command = new InventoryCommand(Guid.NewGuid().ToString("N"), InventoryCommandKind.DirectTransfer, 8000,
            DateTimeOffset.UtcNow, "Mixed treatment direct movement", positions.SelectMany(x => x.TreatmentSlices
                .GroupBy(s => s.Signature).Select(s => Line(x, 13, new(9001, 9003)) with { TreatmentSignature = s.Key })).ToImmutableArray());
        var sent = await f.Execute(command);
        Assert.True(sent.Status == InventoryCommandStatus.Committed, sent.Detail);
        var target = await Position(f, 9001, 9003);
        Assert.Equal(39, target.Sum(x => x.AuthoritativeQuantity));
        Assert.Equal(26, target.SelectMany(x => x.TreatmentSlices).Where(x => x.Signature == "u").Sum(x => x.Quantity));
        Assert.Equal(13, target.SelectMany(x => x.TreatmentSlices).Where(x => x.Signature != "u").Sum(x => x.Quantity));
        Assert.Equal(1, await db.RoomTreatmentApplications.CountAsync());
        Assert.Equal(19, await db.RoomTreatmentApplicationSources.SumAsync(x => x.BinsTreated));
    }

    [InventoryPostgresFact]
    public async Task Concurrent_direct_transfers_and_same_key_retries_conserve_stock()
    {
        await using var f = await Fixture.Create();
        var command = await f.Command(InventoryCommandKind.DirectTransfer, 12);
        var race = new Rendezvous();
        var results = await Task.WhenAll(f.Execute(command, race), f.Execute(command with { OperationKey = Guid.NewGuid().ToString("N") }, race));
        Assert.Single(results, x => x.Status == InventoryCommandStatus.Committed);
        Assert.Single(results, x => x.Status is InventoryCommandStatus.Stale or InventoryCommandStatus.Blocked or InventoryCommandStatus.RetryRequired);
        Assert.Equal(7, await f.Physical());
        Assert.Equal(12, await f.Physical(9003));
        var next = await f.Command(InventoryCommandKind.DirectTransfer, 7);
        race = new Rendezvous();
        results = await Task.WhenAll(f.Execute(next, race), f.Execute(next, race));
        Assert.Single(results, x => x.Status == InventoryCommandStatus.Committed);
        Assert.Single(results, x => x.Status == InventoryCommandStatus.Replayed);
        Assert.Equal(0, await f.Physical());
        Assert.Equal(19, await f.Physical(9003));
    }

    [InventoryPostgresFact]
    public async Task Direct_transfer_failure_rolls_back_all_allocations_history_and_audits()
    {
        await using var f = await Fixture.Create(2);
        var positions = await Position(f, 9001, 9002);
        var c = new InventoryCommand(Guid.NewGuid().ToString("N"), InventoryCommandKind.DirectTransfer, 8000,
            DateTimeOffset.UtcNow, "Atomic test", positions.Select(p => Line(p, 11, new(9001, 9003))).ToImmutableArray());
        var before = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Blocked, (await f.Execute(c, new FailSecondAllocation())).Status);
        Assert.Equal(before, await f.Snapshot());
    }

    private sealed class FailSecondAllocation : IInventoryCommandObserver
    {
        public Task AtAsync(string stage, int attempt, CancellationToken ct)
        {
            if (stage == "PersistedSource2") throw new InvalidOperationException("Injected second allocation failure");
            return Task.CompletedTask;
        }
    }

    [InventoryPostgresFact]
    public async Task Ordinary_movement_preserves_unverified_assignment_history_at_its_next_destination()
    {
        await using var f = await Fixture.Create();
        var transfer = await f.Execute(await f.Command(InventoryCommandKind.DirectTransfer, 19));
        var correction = new InventoryCommand(Guid.NewGuid().ToString("N"), InventoryCommandKind.CorrectDirectTransfer, 8000,
            DateTimeOffset.UtcNow, "Missing quantity", [Line(Assert.Single(await Position(f, 9001, 9003)), 3, new(9001, 9002))],
            PhysicalParentId: transfer.Effects.Single().ParentId, LocationVerification: new(false, "Source presence not checked"));
        var assigned = await f.Execute(correction);
        Assert.Equal(InventoryCommandStatus.Committed, assigned.Status);
        var onward = await f.Execute(await f.Command(InventoryCommandKind.DirectTransfer, 3));
        Assert.Equal(InventoryCommandStatus.Committed, onward.Status);
        await using var db = f.CreateDbContext();
        Assert.Equal(assigned.Effects.Single().ParentId, Assert.Single(await DirectTransferAssignmentHistory.ForRoomAsync(db, 9003, default)));
        Assert.False((await db.RoomTransfers.AsNoTracking().SingleAsync(x => x.Id == assigned.Effects.Single().ParentId)).PhysicalLocationVerified);
        Assert.Equal(19, await f.Physical(9003));
        Assert.Equal(0, await f.Physical());
    }
    private sealed class Rendezvous : IInventoryCommandObserver
    {
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int arrivals;
        public async Task AtAsync(string stage, int attempt, CancellationToken ct)
        {
            if (stage != "Resolved" || attempt != 1) return;
            if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), ct);
        }
    }
}
