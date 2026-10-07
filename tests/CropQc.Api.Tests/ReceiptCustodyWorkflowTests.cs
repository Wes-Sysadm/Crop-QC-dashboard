using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using System.Collections.Immutable;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class ReceiptCustodyWorkflowTests
{
    [InventoryPostgresFact]
    public async Task Same_variety_mixed_lots_and_treatment_require_and_preserve_the_exact_arrived_allocation()
    {
        await using var f = await Fixture.Create(2);
        await using var db = f.CreateDbContext();
        db.Rooms.Add(new() { Id = 9008, WarehouseId = 1, Code = "PARTIAL", Name = "Partial receipt room" });
        db.TreatmentChemicals.Add(new()
        {
            Id = 990001,
            ProductName = "Exact receiving treatment",
            ApplicationLevel = "Receiving",
            Crop = "Apples",
            Volume = 1,
            Unit = "BIN",
            UnitPrice = 1,
            Currency = "USD",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var resolver = new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db));
        var initial = await resolver.ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow);
        var treatedOrigin = initial.Positions.Single(x => x.Identity.GrowerLotId == 100000);
        var chemical = await db.TreatmentChemicals.FirstAsync(x => x.IsActive && x.ApplicationLevel == "Receiving" && x.Crop == "Apples");
        var treatment = new InventoryCommand(Guid.NewGuid().ToString("N"), InventoryCommandKind.ReceiptTreatmentAssignment,
            8000, DateTimeOffset.UtcNow, "Treat only the first original receipt",
            [new(new(treatedOrigin.Identity, treatedOrigin.Location, treatedOrigin.Watermark.Fingerprint, treatedOrigin.Watermark.Versions), 19, "u", ReceiptId: 100000)],
            TreatmentChemicalId: chemical.Id);
        var treated = await f.Execute(treatment);
        Assert.True(treated.Status == InventoryCommandStatus.Committed, treated.Detail);
        var sources = await resolver.ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow);
        var dispatched = await f.Execute(new(Guid.NewGuid().ToString("N"), InventoryCommandKind.InterCompanyDispatch,
            8000, DateTimeOffset.UtcNow, "Mixed same-variety dispatch", sources.Positions.Select(x =>
                new InventoryCommandLine(new(x.Identity, x.Location, x.Watermark.Fingerprint, x.Watermark.Versions), 19, x.TreatmentSlices.Single().Signature)).ToImmutableArray(), CustodyGroup: "EBS"));
        Assert.True(dispatched.Status == InventoryCommandStatus.Committed, dispatched.Detail);
        var transferId = dispatched.Effects[0].ParentId!.Value;
        var receiving = new Receipt
        {
            CompuTechReceiptId = "TR-MIXED-PARTIAL",
            CropYear = 2026,
            ReceiptType = "Truck receipt",
            IsTransferReceipt = true,
            WarehouseId = 1,
            RoomId = 9008,
            FruitProfileId = 9004,
            GrowerLotId = 100000,
            GrowerName = "Transfer",
            LotCode = "TRANSFER",
            BinCount = 19,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        receiving.VarietyLines.Add(new() { FruitProfileId = 9004, BinCount = 19 });
        var transfer = await db.InterCrewTransfers.SingleAsync(x => x.Id == transferId);
        transfer.ReceivingReceipt = receiving; transfer.ConcurrencyVersion++;
        await db.SaveChangesAsync();
        var arrived = await db.TreatmentLineageMovements.SingleAsync(x => x.InterCrewTransferId == transferId && x.ReceiptId == 100001);
        var ack = new InventoryCommand(Guid.NewGuid().ToString("N"), InventoryCommandKind.AcknowledgeTransfer,
            8000, DateTimeOffset.UtcNow, "Only untreated second lot arrived", [], ReceiptCustody:
            new(transferId, receiving.Id, transfer.ConcurrencyVersion, receiving.ConcurrencyVersion, [new(arrived.Id, 19)]));
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(ack)).Status);
        var pending = await resolver.ResolveAsync(new(9001, [], InventoryCustody.InTransit, transferId), new(AllowedCustody: InventoryCustody.InTransit), DateTimeOffset.UtcNow);
        Assert.Equal(100000, Assert.Single(pending.Positions).Identity.GrowerLotId);
        Assert.Equal(19, pending.Positions.Single().AuthoritativeQuantity);
        Assert.Equal("Confirmed", Assert.Single(pending.Positions.Single().TreatmentSlices).State);
        var held = await resolver.ResolveAsync(new(1, [], InventoryCustody.ReceiptHeld, receiving.Id), new(AllowedCustody: InventoryCustody.ReceiptHeld), DateTimeOffset.UtcNow);
        Assert.Equal(100001, Assert.Single(held.Positions).Identity.GrowerLotId);
        Assert.Equal("u", Assert.Single(held.Positions.Single().TreatmentSlices).Signature);
        db.ChangeTracker.Clear();
        transfer = await db.InterCrewTransfers.SingleAsync(x => x.Id == transferId);
        receiving = await db.Receipts.SingleAsync(x => x.Id == receiving.Id);
        var acknowledgment = await db.ReceiptCustodyAcknowledgments.SingleAsync();
        var place = ack with
        {
            OperationKey = Guid.NewGuid().ToString("N"),
            Kind = InventoryCommandKind.PlaceReceiptCustody,
            ReceiptCustody = new(transferId, receiving.Id, transfer.ConcurrencyVersion, receiving.ConcurrencyVersion, [new(acknowledgment.Id, 19)], new(1, 9008))
        };
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(place)).Status);
        var destination = await db.TreatmentLineageSegments.Include(x => x.Applications).SingleAsync(x => x.RoomId == 9008 && x.CurrentBins > 0);
        Assert.Equal(100001, destination.ReceiptId); Assert.Equal(19, destination.CurrentBins);
        Assert.Equal("u", destination.TreatmentSignature); Assert.Empty(destination.Applications);
        Assert.Equal(38, 19 + pending.Positions.Sum(x => x.AuthoritativeQuantity));
        Assert.Equal(19, (await db.RoomTreatmentApplications.SingleAsync()).TotalBinsSnapshot);
        Assert.Null(await db.Receipts.Where(x => x.Id == receiving.Id).Select(x => x.TransferCompletedAt).SingleAsync());
    }

    [InventoryPostgresFact]
    public async Task Receipt_identity_correction_preserves_held_and_unresolved_custody_and_comparison_uses_current_identity()
    {
        await using var f = await Fixture.Create();
        var original = await f.ReceiveCommand();
        var transfer = original.Lines[0].Source.Location.CustodyRecordId!.Value;
        var receipt = original.ReceivingEvidence!.ReceiptId;
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(await Command(f, transfer, receipt, InventoryCommandKind.AcknowledgeTransfer, 18))).Status);
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        db.FruitProfiles.Add(new() { Id = 9008, Name = "Corrected variety", FruitType = "Apple", VarietyCode = "CORRECTED", ProductionType = "Conventional" });
        await db.SaveChangesAsync();
        var executor = new InventoryCommandExecutor(factory);
        var corrections = CanonicalReceiptCorrectionWorkflowTests.Service(db, executor);
        var correction = await CanonicalReceiptCorrectionWorkflowTests.Form(db, corrections, 100000, 19);
        correction.FruitProfileId = 9008;
        Assert.Null((await corrections.ApplyEditAsync(correction, CanonicalReceivingWorkflowTests.Operator().HttpContext!.User, default)).Error);
        var service = CanonicalTruckReceiptWorkflowTests.Service(db, executor);
        var edit = new CropQc.Web.Models.TruckReceiptActionForm
        {
            ReceiptId = receipt,
            TransferId = transfer,
            ReceiptVersion = await db.Receipts.Where(x => x.Id == receipt).Select(x => x.ConcurrencyVersion).SingleAsync(),
            TransferVersion = await db.InterCrewTransfers.Where(x => x.Id == transfer).Select(x => x.ConcurrencyVersion).SingleAsync(),
            Lines = [new() { FruitProfileId = 9008, BinCount = 19 }],
            Reason = "Correct current receipt variety"
        };
        Assert.Null(await service.EditReceiptAsync(edit, default));
        var page = await service.GetAsync(receipt, transfer, default);
        Assert.True(page.IsReconciled);
        Assert.Equal(9008, Assert.Single(page.Comparison).FruitProfileId);
        Assert.Equal(19, page.Comparison.Single().Transfer);
        Assert.Equal(18, page.ReceiptHeldBins); Assert.Equal(1, page.UnresolvedBins);
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(await Command(f, transfer, receipt, InventoryCommandKind.PlaceReceiptCustody, 18))).Status);
        await AssertCustody(f, transfer, receipt, 1, 0, 18, false);
        Assert.Equal(9004, await db.TreatmentLineageMovements.Where(x => x.MovementType == "InterCrewDispatch").Select(x => x.SourceSegment!.FruitProfileId).SingleAsync());
        Assert.Equal(9008, await db.TreatmentLineageSegments.Where(x => x.RoomId == 9007 && x.CurrentBins > 0).Select(x => x.FruitProfileId).SingleAsync());
    }

    [InventoryPostgresFact]
    public async Task Treated_allocations_remain_held_when_room_is_sealed_and_place_with_original_history()
    {
        await using var f = await Fixture.Create();
        var original = await f.ReceiveCommand(treated: true);
        var transfer = original.Lines[0].Source.Location.CustodyRecordId!.Value;
        var receipt = original.ReceivingEvidence!.ReceiptId;
        await using var db = f.CreateDbContext();
        var room = await db.Rooms.SingleAsync(x => x.Id == 9007);
        room.IsSealed = true;
        await db.SaveChangesAsync();
        var ack = await Command(f, transfer, receipt, InventoryCommandKind.AcknowledgeTransfer, 18);
        var before = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Blocked, (await f.Execute(ack with
        { ReceiptCustody = ack.ReceiptCustody! with { Allocations = [] } })).Status);
        Assert.Equal(before, await f.Snapshot());
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(ack)).Status);
        var place = await Command(f, transfer, receipt, InventoryCommandKind.PlaceReceiptCustody, 18);
        var held = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Blocked, (await f.Execute(place)).Status);
        Assert.Equal(held, await f.Snapshot());
        var durableAck = await db.ReceiptCustodyAcknowledgments.SingleAsync();
        durableAck.Quantity++;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        room = await db.Rooms.SingleAsync(x => x.Id == 9007);
        room.IsSealed = false;
        await db.SaveChangesAsync();
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(place)).Status);
        await AssertCustody(f, transfer, receipt, 1, 0, 18, false);
        var application = await db.RoomTreatmentApplications.Include(x => x.Sources).SingleAsync();
        Assert.Equal(19, application.TotalBinsSnapshot);
        Assert.Equal(19, application.Sources.Sum(x => x.BinsTreated));
        var destination = await db.TreatmentLineageSegments.Include(x => x.Applications)
            .SingleAsync(x => x.RoomId == 9007 && x.Disposition == "Current");
        Assert.Equal(18, destination.CurrentBins);
        Assert.Equal($"u|a:{application.Id}", destination.TreatmentSignature);
        Assert.Equal(application.Id, Assert.Single(destination.Applications).RoomTreatmentApplicationId);
    }

    [InventoryPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failure_before_commit_preserves_every_table_and_allows_safe_retry(bool placement)
    {
        await using var f = await Fixture.Create();
        var original = await f.ReceiveCommand();
        var transfer = original.Lines[0].Source.Location.CustodyRecordId!.Value;
        var receipt = original.ReceivingEvidence!.ReceiptId;
        var command = await Command(f, transfer, receipt, InventoryCommandKind.AcknowledgeTransfer, 18);
        if (placement)
        {
            Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(command)).Status);
            command = await Command(f, transfer, receipt, InventoryCommandKind.PlaceReceiptCustody, 18);
        }
        var before = await f.Snapshot();
        await Assert.ThrowsAsync<IOException>(() => new Fixture(f.Connection, new InterruptCommit()).Execute(command));
        Assert.Equal(before, await f.Snapshot());
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(command)).Status);
        await AssertCustody(f, transfer, receipt, 1, placement ? 0 : 18, placement ? 18 : 0, false);
    }

    private sealed class InterruptCommit : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction,
            TransactionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<InterceptionResult>(new IOException("Disposable interruption before commit"));
    }

    [InventoryPostgresFact]
    public async Task Additive_migration_preserves_prior_inventory_and_refuses_downgrade_after_acknowledgement()
    {
        await using var f = await Fixture.Create();
        await using var db = f.CreateDbContext();
        // The disposable fixture models the previous schema by removing only empty new tables.
        await db.Database.ExecuteSqlRawAsync("DROP TABLE \"ReceiptCustodyPlacements\"; DROP TABLE \"ReceiptCustodyAcknowledgments\";");
        var before = await f.Snapshot();
        var migration = new CropQc.Data.Migrations.ReceiptHeldTransferCustody { ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL" };
        var generator = db.GetService<IMigrationsSqlGenerator>();
        foreach (var sql in generator.Generate(migration.UpOperations, db.Model)) await db.Database.ExecuteSqlRawAsync(sql.CommandText);
        Assert.Equal(19, await f.Physical());
        Assert.Empty(await db.ReceiptCustodyAcknowledgments.ToListAsync());
        // Empty-schema rollback is permitted and retains every prior row.
        foreach (var sql in generator.Generate(migration.DownOperations, db.Model)) await db.Database.ExecuteSqlRawAsync(sql.CommandText);
        Assert.Equal(before, await f.Snapshot());
        foreach (var sql in generator.Generate(migration.UpOperations, db.Model)) await db.Database.ExecuteSqlRawAsync(sql.CommandText);
        var original = await f.ReceiveCommand();
        var command = await Command(f, original.Lines[0].Source.Location.CustodyRecordId!.Value,
            original.ReceivingEvidence!.ReceiptId, InventoryCommandKind.AcknowledgeTransfer, 18);
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(command)).Status);
        var durable = await f.Snapshot();
        await Assert.ThrowsAsync<Npgsql.PostgresException>(async () =>
        {
            foreach (var sql in generator.Generate(migration.DownOperations, db.Model)) await db.Database.ExecuteSqlRawAsync(sql.CommandText);
        });
        Assert.Equal(durable, await f.Snapshot());
    }

    [InventoryPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lost_commit_response_replays_acknowledgement_or_placement_once(bool placement)
    {
        await using var f = await Fixture.Create();
        var original = await f.ReceiveCommand();
        var transfer = original.Lines[0].Source.Location.CustodyRecordId!.Value;
        var receipt = original.ReceivingEvidence!.ReceiptId;
        var command = await Command(f, transfer, receipt, InventoryCommandKind.AcknowledgeTransfer, 18);
        if (placement)
        {
            Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(command)).Status);
            command = await Command(f, transfer, receipt, InventoryCommandKind.PlaceReceiptCustody, 18);
        }
        var instrumented = new Fixture(f.Connection, new LostResponse());
        await Assert.ThrowsAsync<IOException>(() => instrumented.Execute(command));
        var durable = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Replayed, (await f.Execute(command)).Status);
        Assert.Equal(durable, await f.Snapshot());
        await AssertCustody(f, transfer, receipt, 1, placement ? 0 : 18, placement ? 18 : 0, false);
    }

    private sealed class LostResponse : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default) =>
            Task.FromException(new IOException("Disposable lost acknowledgement response"));
    }

    [InventoryPostgresFact]
    public async Task Partial_acknowledgement_placement_and_later_settlement_conserve_original_dispatch()
    {
        await using var f = await Fixture.Create();
        var original = await f.ReceiveCommand(50);
        var transferId = original.Lines[0].Source.Location.CustodyRecordId!.Value;
        var receiptId = original.ReceivingEvidence!.ReceiptId;
        await SetObserved(f, receiptId, 49);
        var acknowledge = await Command(f, transferId, receiptId, InventoryCommandKind.AcknowledgeTransfer, 49);
        var beforeRoom = await DestinationQuantity(f);
        var result = await f.Execute(acknowledge);
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
        Assert.Equal(beforeRoom, await DestinationQuantity(f));
        await AssertCustody(f, transferId, receiptId, 1, 49, 0, false, 50);
        var afterAck = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Replayed, (await f.Execute(acknowledge)).Status);
        Assert.Equal(afterAck, await f.Snapshot());

        var place = await Command(f, transferId, receiptId, InventoryCommandKind.PlaceReceiptCustody, 49);
        result = await f.Execute(place);
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
        await AssertCustody(f, transferId, receiptId, 1, 0, 49, false, 50);
        var afterPlace = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Replayed, (await f.Execute(place)).Status);
        Assert.Equal(afterPlace, await f.Snapshot());

        await SetObserved(f, receiptId, 50);
        result = await f.Execute(await Command(f, transferId, receiptId, InventoryCommandKind.AcknowledgeTransfer, 1));
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
        await AssertCustody(f, transferId, receiptId, 0, 1, 49, false, 50);
        result = await f.Execute(await Command(f, transferId, receiptId, InventoryCommandKind.PlaceReceiptCustody, 1));
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
        await AssertCustody(f, transferId, receiptId, 0, 0, 50, true, 50);
    }

    [InventoryPostgresTheory]
    [InlineData(19, 19, true)]
    [InlineData(18, 19, false)]
    [InlineData(20, 20, false)]
    public async Task Acknowledgement_is_bounded_by_both_observed_receipt_and_original_dispatch(int observed, int selected, bool success)
    {
        await using var f = await Fixture.Create();
        var original = await f.ReceiveCommand();
        var transfer = original.Lines[0].Source.Location.CustodyRecordId!.Value;
        var receipt = original.ReceivingEvidence!.ReceiptId;
        await SetObserved(f, receipt, observed);
        var command = await Command(f, transfer, receipt, InventoryCommandKind.AcknowledgeTransfer, selected);
        var before = await f.Snapshot();
        var result = await f.Execute(command);
        Assert.Equal(success ? InventoryCommandStatus.Committed : InventoryCommandStatus.Blocked, result.Status);
        if (!success) Assert.Equal(before, await f.Snapshot());
        else await AssertCustody(f, transfer, receipt, 0, 19, 0, false);
    }

    [InventoryPostgresFact]
    public async Task Concurrent_acknowledgements_cannot_consume_the_same_dispatch_twice()
    {
        await using var f = await Fixture.Create();
        var original = await f.ReceiveCommand();
        var transfer = original.Lines[0].Source.Location.CustodyRecordId!.Value;
        var receipt = original.ReceivingEvidence!.ReceiptId;
        var command = await Command(f, transfer, receipt, InventoryCommandKind.AcknowledgeTransfer, 18);
        var arrived = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = new Observer(async (stage, attempt) =>
        {
            if (stage != "Resolved" || attempt != 1) return;
            if (Interlocked.Increment(ref arrived) == 2) gate.SetResult();
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(30));
        });
        var results = await Task.WhenAll(f.Execute(command, observer),
            f.Execute(command with { OperationKey = Guid.NewGuid().ToString("N") }, observer));
        Assert.Single(results, x => x.Status == InventoryCommandStatus.Committed);
        Assert.Single(results, x => x.Status == InventoryCommandStatus.Stale);
        await AssertCustody(f, transfer, receipt, 1, 18, 0, false);
    }

    private static async Task<InventoryCommand> Command(Fixture f, long transferId, long receiptId, InventoryCommandKind kind, int quantity)
    {
        await using var db = f.CreateDbContext();
        var transfer = await db.InterCrewTransfers.SingleAsync(x => x.Id == transferId);
        var receipt = await db.Receipts.SingleAsync(x => x.Id == receiptId);
        var id = kind == InventoryCommandKind.AcknowledgeTransfer
            ? await db.TreatmentLineageMovements.Where(x => x.InterCrewTransferId == transferId && x.MovementType == "InterCrewDispatch").Select(x => x.Id).SingleAsync()
            : await db.ReceiptCustodyAcknowledgments.Where(x => x.ReceiptId == receiptId && x.Quantity > x.Placements.Sum(p => p.Quantity)).Select(x => x.Id).SingleAsync();
        return new(Guid.NewGuid().ToString("N"), kind, 8000, DateTimeOffset.UtcNow, "Disposable exact allocation evidence", [],
            ReceiptCustody: new(transferId, receiptId, transfer.ConcurrencyVersion, receipt.ConcurrencyVersion, [new(id, quantity)],
                kind == InventoryCommandKind.PlaceReceiptCustody ? new(9006, 9007) : null));
    }

    private static async Task SetObserved(Fixture f, long receiptId, int quantity)
    {
        await using var db = f.CreateDbContext();
        var receipt = await db.Receipts.Include(x => x.VarietyLines).SingleAsync(x => x.Id == receiptId);
        receipt.BinCount = quantity; receipt.VarietyLines.Single().BinCount = quantity; receipt.ConcurrencyVersion++;
        await db.SaveChangesAsync();
    }

    private static async Task<int> DestinationQuantity(Fixture f)
    {
        await using var db = f.CreateDbContext();
        return await db.RoomInventoryAdjustments.Where(x => x.RoomId == 9007).SumAsync(x => x.ChangeAmount);
    }

    private static async Task AssertCustody(Fixture f, long transferId, long receiptId, int unresolved, int held, int placed, bool complete, int loaded = 19)
    {
        await using var db = f.CreateDbContext();
        var transfer = await db.InterCrewTransfers.SingleAsync(x => x.Id == transferId);
        var receipt = await db.Receipts.SingleAsync(x => x.Id == receiptId);
        var acknowledged = await db.ReceiptCustodyAcknowledgments.Where(x => x.ReceiptId == receiptId).SumAsync(x => x.Quantity);
        Assert.Equal(loaded, transfer.BinsLoaded);
        Assert.Equal(loaded, unresolved + held + placed);
        Assert.Equal(acknowledged, transfer.BinsReceived);
        Assert.Equal(unresolved, transfer.BinsLoaded - acknowledged);
        Assert.Equal(placed, await db.ReceiptCustodyPlacements.SumAsync(x => x.Quantity));
        Assert.Equal(placed, await DestinationQuantity(f));
        Assert.Equal(complete ? InterCrewTransferStatuses.Received : InterCrewTransferStatuses.InTransit, transfer.Status);
        Assert.Equal(complete, receipt.TransferCompletedAt != null);
        Assert.Equal(-loaded, await db.RoomInventoryAdjustments.Where(x => x.InterCrewTransferId == transferId && x.ChangeAmount < 0).SumAsync(x => x.ChangeAmount));
        Assert.Equal(loaded, await db.TreatmentLineageMovements.Where(x => x.InterCrewTransferId == transferId && x.MovementType == "InterCrewDispatch").SumAsync(x => x.BinCount));
        var availability = new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db));
        var pending = await availability.ResolveAsync(new(9001, [], InventoryCustody.InTransit, transferId), new(AllowedCustody: InventoryCustody.InTransit), DateTimeOffset.UtcNow);
        if (unresolved > 0) Assert.All(pending.Positions, x => Assert.True(x.IsOperable, string.Join(';', x.Blockers)));
        Assert.Equal(unresolved, pending.Positions.Sum(x => x.AuthoritativeQuantity));
        var receiptHeld = await availability.ResolveAsync(new(9006, [], InventoryCustody.ReceiptHeld, receiptId), new(AllowedCustody: InventoryCustody.ReceiptHeld), DateTimeOffset.UtcNow);
        Assert.All(receiptHeld.Positions, x => Assert.True(x.IsOperable, string.Join(';', x.Blockers)));
        Assert.Equal(held, receiptHeld.Positions.Sum(x => x.AuthoritativeQuantity));
        Assert.NotEmpty(await db.AuditLogs.Where(x => x.Action == "CanonicalReceiptCustody").ToArrayAsync());
        var readiness = await new CropQc.Web.Services.InventoryDeductionInvariantService(db,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CropQc.Web.Services.InventoryDeductionInvariantService>.Instance).VerifyReadinessAsync(default);
        Assert.DoesNotContain(readiness.Issues, x => x.BlocksDeployment);
    }
}
