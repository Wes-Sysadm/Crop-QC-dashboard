using System.Collections.Immutable;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class RecordedMixedDispatchTests(ITestOutputHelper output)
{
    [InventoryPostgresFact]
    public async Task Recorded_mixed_dispatch_reconciles_each_debit_without_losing_treatment_membership()
    {
        await using var f = await History();
        await using var read = f.CreateDbContext();
        var before = Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(read))
            .ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow)).Positions);
        Assert.Equal(652, before.AuthoritativeQuantity);
        Assert.Equal(442, before.RawProjectionQuantity);
        Assert.True(before.IsOperable);
        Assert.True(before.UsesRecordedCohorts);
        Assert.False(before.UsesIndependentCohorts);
        Assert.Equal(582, before.TreatmentSlices.Where(s => s.Signature == "u").Sum(s => s.Quantity));
        Assert.Equal(70, before.TreatmentSlices.Where(s => s.Signature == "u|a:800000").Sum(s => s.Quantity));
        var factory = new CapturingFactory(f.Connection);
        var stages = new List<InventoryAvailabilityResult>();
        var original = await OriginalHistory(read);
        var executor = new InventoryCommandExecutor(factory, new Observer(async (stage, _) =>
        {
            if (stage is not ("Normalized" or "PersistedSource1" or "PersistedSource2")) return;
            var db = factory.Current!;
            Assert.NotNull(db.Database.CurrentTransaction);
            var evidence = Assert.Single((await new InventoryEvidenceLoader(db).LoadAsync(new(9001, [9002]), DateTimeOffset.UtcNow, default)).Positions);
            var final = InventoryAvailabilityResolver.Resolve(evidence, new());
            stages.Add(final);
            output.WriteLine(JsonSerializer.Serialize(new
            {
                stage,
                final.AuthoritativeQuantity,
                final.RawProjectionQuantity,
                final.IsOperable,
                final.Blockers,
                final.UsesRecordedCohorts,
                final.UsesIndependentCohorts,
                final.TreatmentSlices,
                evidence.Projections,
                Replay = InventoryEventReplay.Replay(evidence),
                Debits = evidence.Ledger.Where(x => x.Quantity < 0),
                Departures = evidence.Movements.Where(x => x.Outgoing),
                Evidence = evidence
            }));
        }));
        var source = new InventoryCommandSource(before.Identity, before.Location, before.Watermark.Fingerprint, before.Watermark.Versions);
        var command = new InventoryCommand("synthetic-mixed-dispatch", InventoryCommandKind.InterCompanyDispatch, 8000,
            DateTimeOffset.UtcNow, "Synthetic recorded mixed treatment dispatch", [new(source, 2, "u"), new(source, 2, "u|a:800000")])
        { CustodyGroup = "EBS" };
        var result = await executor.ExecuteAsync(command);
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
        Assert.Equal(new[] { 652, 650, 648 }, stages.Select(x => x.AuthoritativeQuantity));
        Assert.All(stages, x => { Assert.True(x.IsOperable); Assert.Equal(x.AuthoritativeQuantity, x.RawProjectionQuantity); });
        Assert.Equal(580, stages[^1].TreatmentSlices.Where(s => s.Signature == "u").Sum(s => s.Quantity));
        Assert.Equal(68, stages[^1].TreatmentSlices.Where(s => s.Signature == "u|a:800000").Sum(s => s.Quantity));
        Assert.Equal(56, stages[^1].TreatmentSlices.Where(s => s.Signature == "u" && s.ReceiptEvidenceIds.IsEmpty).Sum(s => s.Quantity));
        var parent = await read.InterCrewTransfers.AsNoTracking().SingleAsync();
        var transit = Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(read)).ResolveAsync(
            new(9001, [], InventoryCustody.InTransit, parent.Id), new(AllowedCustody: InventoryCustody.InTransit), DateTimeOffset.UtcNow)).Positions);
        Assert.True(transit.IsOperable);
        Assert.Equal(4, transit.AuthoritativeQuantity);
        Assert.Equal(new[] { "u", "u|a:800000" }, transit.TreatmentSlices.Select(s => s.Signature).Order());
        Assert.All(transit.TreatmentSlices, s => Assert.Equal(2, s.Quantity));
        Assert.Equal(10, await read.RoomInventoryAdjustments.CountAsync(x => x.AdjustmentType == "ReceiptAdd"));
        Assert.Equal(652, stages[^1].AuthoritativeQuantity + transit.AuthoritativeQuantity);
        Assert.Equal(original, await OriginalHistory(read));
        Assert.Equal(442, await read.TreatmentLineageSegments.Where(x => x.Disposition == "Historical").SumAsync(x => x.RetiredQuantity));
        var committed = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Replayed, (await executor.ExecuteAsync(command)).Status);
        Assert.Equal(InventoryCommandStatus.Conflict, (await executor.ExecuteAsync(command with { Reason = "Conflicting retry" })).Status);
        Assert.Equal(InventoryCommandStatus.Stale, (await executor.ExecuteAsync(command with { OperationKey = "stale-mixed" })).Status);
        Assert.Equal(committed, await f.Snapshot());

        // A subsequent command can append a slice to the same load. It must not
        // relink the earlier debits to the newly added departure.
        var current = stages[^1];
        var edit = command with
        {
            Kind = InventoryCommandKind.TransferEdit,
            OperationKey = "synthetic-mixed-edit",
            PhysicalParentId = parent.Id,
            ExpectedTransferVersion = parent.ConcurrencyVersion,
            Lines = [new(new(current.Identity, current.Location, current.Watermark.Fingerprint, current.Watermark.Versions), 1, "u")]
        };
        var edited = await executor.ExecuteAsync(edit);
        Assert.True(edited.Status == InventoryCommandStatus.Committed, edited.Detail);
        Assert.Equal(647, await f.Physical());
        Assert.Equal(5, (await read.InterCrewTransfers.AsNoTracking().SingleAsync()).BinsLoaded);
        Assert.Equal(original, await OriginalHistory(read));
    }

    [Theory]
    [InlineData("cmd:1:s7", true)]
    [InlineData("cmd:10:s7", false)]
    [InlineData("other:1:s7", false)]
    [InlineData(null, false)]
    public void Canonical_custody_requires_the_same_line_and_commit(string? key, bool related)
    {
        var at = InventoryEvidenceCorpus.Start;
        var ledger = new InventoryLedgerEvidence(1, -2, "InterCrewTransferDispatch", at, null, "crew:1", true, at)
        { InvariantVersion = InventoryLedgerKinds.CanonicalCommandInvariantVersion, OperationKey = "cmd:1:out" };
        var move = new InventoryMovementEvidence(1, "InterCrewDispatch", 2, at, at, "u", "Untreated", null, 7, null,
            false, true, "crew:1", null, true)
        { OperationKey = key };
        Assert.Equal(related, InventoryCohortEvidence.Related(ledger, move));
        Assert.False(InventoryCohortEvidence.Related(ledger, move with { CreatedAt = at.AddSeconds(1) }));
        Assert.False(InventoryCohortEvidence.Related(ledger, move with { Parent = "crew:2" }));
        Assert.True(InventoryCohortEvidence.Related(ledger with { InvariantVersion = 0, OperationKey = null }, move));
        Assert.False(InventoryCohortEvidence.Related(ledger with { OperationKey = null }, move));
    }

    private static async Task<string> OriginalHistory(CropQcDbContext db) => JsonSerializer.Serialize(new
    {
        Receipts = await db.Receipts.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Ledger = await db.RoomInventoryAdjustments.AsNoTracking().Where(x => x.Id >= 100000).OrderBy(x => x.Id).ToArrayAsync(),
        Transfer = await db.RoomTransfers.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Application = await db.RoomTreatmentApplications.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Sources = await db.RoomTreatmentApplicationSources.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Movement = await db.TreatmentLineageMovements.AsNoTracking().Where(x => x.Id == 800000).ToArrayAsync()
    });

    private sealed class CapturingFactory(string connection) : IDbContextFactory<CropQcDbContext>
    {
        public CropQcDbContext? Current { get; private set; }
        public CropQcDbContext CreateDbContext() => Current = new CanonicalActualRunWorkflowTests.EnabledFactory(connection).CreateDbContext();
    }

    internal static async Task<Fixture> History(long applicationId = 800000)
    {
        var f = await Fixture.Create();
        await using var db = f.CreateDbContext();
        var at = InventoryEvidenceCorpus.Start;
        var receipt = await db.Receipts.SingleAsync();
        receipt.BinCount = 70;
        var origin = await db.RoomInventoryAdjustments.SingleAsync();
        origin.ChangeAmount = origin.NewBinCount = 70;
        var segment = await db.TreatmentLineageSegments.SingleAsync();
        segment.CurrentBins = 70; segment.ReceiptId = receipt.Id;
        segment.TreatmentState = "Confirmed"; segment.TreatmentSignature = "u|a:" + applicationId;
        var chemical = await db.TreatmentChemicals.FirstAsync(x => x.IsActive && x.ApplicationLevel == "Room");
        var application = new RoomTreatmentApplication
        {
            Id = applicationId,
            OperationKey = "synthetic-application",
            TreatmentChemicalId = chemical.Id,
            WarehouseId = 9001,
            RoomId = 9002,
            ReceiptId = receipt.Id,
            AppliedByUserId = 8000,
            CreatedByUserId = 8000,
            TotalBinsSnapshot = 70,
            ProductNameSnapshot = chemical.ProductName,
            CropSnapshot = chemical.Crop,
            UnitSnapshot = chemical.Unit,
            CurrencySnapshot = chemical.Currency,
            AppliedAt = at.AddHours(1),
            CreatedAt = at.AddHours(1)
        };
        application.Sources.Add(new()
        {
            ReceiptId = receipt.Id,
            CropYear = 2026,
            GrowerLotId = 100000,
            FruitProfileId = 9004,
            IdentityKey = segment.IdentityKey,
            GrowerNameSnapshot = "Corpus",
            LotNumberSnapshot = receipt.LotCode,
            VarietyCodeSnapshot = "CGAL",
            ProductionTypeSnapshot = "Conventional",
            BinsTreated = 70,
            PriorTreatmentSignature = "u",
            ResultTreatmentSignature = segment.TreatmentSignature
        });
        db.RoomTreatmentApplications.Add(application);
        segment.Applications.Add(new() { RoomTreatmentApplicationId = applicationId });
        // Historical synthetic origin and exact transfer custody; null movement receipt
        // deliberately preserves shared ancestry. No production rows are copied.
        db.Receipts.Add(new()
        {
            Id = 100001,
            CropYear = 2026,
            CompuTechReceiptId = "SYNTH-SHARED",
            WarehouseId = 9001,
            RoomId = 9003,
            FruitProfileId = 9004,
            GrowerLotId = 100000,
            GrowerName = "Corpus",
            GrowerNumber = receipt.GrowerNumber,
            LotCode = receipt.LotCode,
            BinCount = 56,
            ReceivedAt = at,
            CreatedAt = at,
            UpdatedAt = at
        });
        db.RoomInventoryAdjustments.Add(Ledger(100001, 9003, 56, "ReceiptAdd", at, 100001));
        var sizes = new[] { 56, 56, 68, 68, 68, 70, 70, 70 };
        for (var index = 0; index < sizes.Length; index++)
        {
            var id = 100010 + index;
            var arrived = index < 5 ? at : at.AddHours(3 + index);
            db.Receipts.Add(new()
            {
                Id = id,
                CropYear = 2026,
                CompuTechReceiptId = "SYNTH-ARRIVAL-" + index,
                WarehouseId = 9001,
                RoomId = 9002,
                FruitProfileId = 9004,
                GrowerLotId = 100000,
                GrowerName = "Corpus",
                GrowerNumber = receipt.GrowerNumber,
                LotCode = receipt.LotCode,
                BinCount = sizes[index],
                ReceivedAt = arrived,
                CreatedAt = arrived,
                UpdatedAt = arrived
            });
            db.RoomInventoryAdjustments.Add(Ledger(id, 9002, sizes[index], "ReceiptAdd", arrived, id));
        }
        var transfer = new RoomTransfer
        {
            Id = 800000,
            OperationKey = "synthetic-shared-arrival",
            SourceWarehouseId = 9001,
            SourceRoomId = 9003,
            DestinationWarehouseId = 9001,
            DestinationRoomId = 9002,
            CropYear = 2026,
            GrowerLotId = 100000,
            FruitProfileId = 9004,
            GrowerName = "Corpus",
            LotNumber = receipt.LotCode,
            VarietyCode = "CGAL",
            BinCount = 56,
            Reason = "Synthetic historical custody",
            TransferredAt = at.AddHours(2),
            CreatedAt = at.AddHours(2)
        };
        db.RoomTransfers.Add(transfer);
        foreach (var row in new[] { Ledger(100002, 9003, -56, "TransferOut", at.AddHours(2)), Ledger(100003, 9002, 56, "TransferIn", at.AddHours(2)) })
        { row.RoomTransferId = transfer.Id; db.RoomInventoryAdjustments.Add(row); }
        db.TreatmentLineageMovements.Add(new()
        {
            Id = 800000,
            OperationKey = "synthetic-arrival-movement",
            MovementType = "RoomTransfer",
            SourceRoomId = 9003,
            DestinationRoomId = 9002,
            IdentityKey = segment.IdentityKey,
            TreatmentStateSnapshot = "Untreated",
            TreatmentSignatureSnapshot = "u",
            BinCount = 56,
            RoomTransferId = transfer.Id,
            OccurredAt = at.AddHours(2),
            CreatedAt = at.AddHours(2)
        });
        db.TreatmentLineageSegments.Add(new()
        {
            Id = 100001,
            WarehouseId = 9001,
            RoomId = 9002,
            CropYear = 2026,
            GrowerLotId = 100000,
            FruitProfileId = 9004,
            IdentityKey = segment.IdentityKey,
            GrowerNameSnapshot = "Corpus",
            GrowerNumberSnapshot = receipt.GrowerNumber!,
            LotNumberSnapshot = receipt.LotCode,
            VarietyCodeSnapshot = "CGAL",
            ProductionTypeSnapshot = "Conventional",
            IsOrganicSnapshot = false,
            TreatmentState = "Untreated",
            TreatmentSignature = "u",
            CurrentBins = 372,
            CreatedAt = at.AddHours(2),
            UpdatedAt = at.AddHours(2)
        });
        await db.SaveChangesAsync();
        return f;

        RoomInventoryAdjustment Ledger(long id, int room, int quantity, string kind, DateTimeOffset time, long? receiptId = null) => new()
        {
            Id = id,
            WarehouseId = 9001,
            RoomId = room,
            CropYear = 2026,
            GrowerLotId = 100000,
            FruitProfileId = 9004,
            GrowerName = "Corpus",
            LotNumber = receipt.LotCode,
            VarietyCode = "CGAL",
            ChangeAmount = quantity,
            NewBinCount = quantity > 0 ? quantity : 0,
            AdjustmentType = kind,
            AdjustmentAt = time,
            CreatedAt = time,
            ReceiptId = receiptId
        };
    }
}
