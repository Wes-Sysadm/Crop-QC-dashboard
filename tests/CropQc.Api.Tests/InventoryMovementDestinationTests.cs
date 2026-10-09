using System.Collections.Immutable;
using System.Text.Json;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class InventoryMovementDestinationTests
{
    [InventoryPostgresFact]
    public async Task Unfunded_recorded_destination_origin_is_a_specific_transaction_failure()
    {
        await using var f = await Fixture.Create();
        await SeedDestination(f, 9);
        await using (var db = f.CreateDbContext())
        {
            var origin = await db.RoomInventoryAdjustments.SingleAsync(x => x.RoomId == 9003);
            origin.ReceiptId = null; await db.SaveChangesAsync();
        }
        var command = await f.Command(InventoryCommandKind.RoomMove, 7);
        var before = await f.Snapshot();
        var result = await f.Execute(command);
        Assert.Equal(InventoryCommandStatus.Blocked, result.Status);
        Assert.Contains("missing ordinary receipt origin", result.Detail);
        Assert.Equal(before, await f.Snapshot());
    }

    [InventoryPostgresFact]
    public async Task Later_receipt_treatment_follows_the_incoming_cohort_without_claiming_older_unknown_stock()
    {
        await using var f = await Fixture.Create();
        await SeedDestination(f, 9);
        var move = await f.Command(InventoryCommandKind.RoomMove, 19);
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(move)).Status);
        await using var db = f.CreateDbContext();
        var chemical = new TreatmentChemical
        {
            ProductName = "Local receipt treatment",
            ApplicationLevel = "Receiving",
            Crop = "Apples",
            Unit = "bin",
            Currency = "USD",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.TreatmentChemicals.Add(chemical); await db.SaveChangesAsync();
        var apply = (await f.Command(InventoryCommandKind.ReceiptTreatmentAssignment, 19, room: 9003)) with { TreatmentChemicalId = chemical.Id };
        apply = apply with { Lines = [apply.Lines[0] with { ReceiptId = 100000 }] };
        var applied = await f.Execute(apply);
        Assert.True(applied.Status == InventoryCommandStatus.Committed, applied.Detail);
        var signature = $"u|a:{applied.Effects.Single().ParentId}";
        var after = await new InventoryReceiptAvailability(db).ReadAsync(100000, default);
        Assert.NotNull(after); Assert.Null(after.Blocker); Assert.Equal(19, after.RoomQuantity);
        Assert.Equal(signature, Assert.Single(after.Allocations).Slice.Signature);
        var onward = await f.Command(InventoryCommandKind.RoomMove, 7, room: 9003);
        onward = onward with { Lines = [onward.Lines[0] with { TreatmentSignature = signature, Destination = new(9001, 9002) }] };
        var moved = await f.Execute(onward);
        Assert.True(moved.Status == InventoryCommandStatus.Committed, moved.Detail);
        Assert.Equal(7, await f.Physical()); Assert.Equal(29, await f.Physical(9003));
        Assert.Equal(19, await db.RoomTreatmentApplicationSources.Where(x => x.RoomTreatmentApplicationId == applied.Effects.Single().ParentId).SumAsync(x => x.BinsTreated));
        Assert.Equal("x", await db.TreatmentLineageSegments.Where(x => x.Id == 200001).Select(x => x.TreatmentSignature).SingleAsync());
    }

    [InventoryPostgresFact]
    public async Task Complete_source_events_rebuild_a_defective_projection_before_partial_treated_move()
    {
        await using var f = await Fixture.Create();
        await SeedDestination(f, 9);
        await using (var db = f.CreateDbContext())
        {
            var p = await db.TreatmentLineageSegments.SingleAsync(x => x.Id == 200001);
            db.RoomTreatmentApplicationSources.Add(new()
            {
                RoomTreatmentApplicationId = 200001,
                ReceiptId = 200001,
                CropYear = p.CropYear,
                GrowerLotId = p.GrowerLotId,
                FruitProfileId = p.FruitProfileId,
                IdentityKey = p.IdentityKey,
                GrowerNumberSnapshot = p.GrowerNumberSnapshot,
                GrowerNameSnapshot = p.GrowerNameSnapshot,
                LotNumberSnapshot = p.LotNumberSnapshot,
                VarietyCodeSnapshot = p.VarietyCodeSnapshot,
                ProductionTypeSnapshot = p.ProductionTypeSnapshot,
                IsOrganicSnapshot = p.IsOrganicSnapshot,
                BinsTreated = 17,
                PriorTreatmentSignature = "u",
                ResultTreatmentSignature = "u|a:200001"
            });
            await db.SaveChangesAsync();
        }
        var command = await f.Command(InventoryCommandKind.RoomMove, 7, room: 9003);
        command = command with { Lines = [command.Lines[0] with { TreatmentSignature = "u|a:200001", Destination = new(9001, 9002) }] };
        var result = await f.Execute(command);
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
        Assert.Equal(10, await f.Physical(9003)); Assert.Equal(26, await f.Physical());
        await using var verify = f.CreateDbContext();
        var retired = await verify.TreatmentLineageSegments.SingleAsync(x => x.Id == 200001);
        Assert.Equal("Historical", retired.Disposition); Assert.Equal(9, retired.RetiredQuantity);
        Assert.Single(await verify.AuditLogs.Where(x => x.Action == "CanonicalRecordedCohortReconstruction").ToArrayAsync());
        var current = await verify.TreatmentLineageSegments.Where(x => x.Disposition == "Current" && x.CurrentBins > 0).ToArrayAsync();
        Assert.Equal(17, current.Where(x => x.TreatmentSignature == "u|a:200001").Sum(x => x.CurrentBins));
        Assert.Equal(19, current.Where(x => x.TreatmentSignature == "u").Sum(x => x.CurrentBins));
        Assert.Equal(36, await verify.RoomInventoryAdjustments.SumAsync(x => x.ChangeAmount));
        Assert.Equal(17, await verify.RoomTreatmentApplicationSources.SumAsync(x => x.BinsTreated));
    }

    [InventoryPostgresTheory]
    [InlineData(0)]
    [InlineData(9)]
    [InlineData(30)]
    [InlineData(-4)]
    public async Task Incoming_cohort_coexists_with_defective_destination_and_can_move_again(int projected)
    {
        await using var f = await Fixture.Create();
        var retained = await SeedDestination(f, projected);
        var command = await f.Command(InventoryCommandKind.RoomMove, 7);
        var result = await f.Execute(command);
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
        Assert.Equal(12, await f.Physical());
        Assert.Equal(24, await f.Physical(9003));
        await using (var db = f.CreateDbContext())
        {
            Assert.Equal(retained, await RetainedDestination(db));
            var target = Assert.Single(await db.TreatmentLineageSegments.Where(x => x.RoomId == 9003 && x.CohortKey == command.OperationKey).ToArrayAsync());
            Assert.Equal(7, target.CurrentBins);
            Assert.Equal("u", target.TreatmentSignature);
            Assert.Equal("Untreated", target.TreatmentState);
            Assert.Equal(100000L, target.ReceiptId);
            Assert.Empty(await db.TreatmentLineageSegmentApplications.Where(x => x.TreatmentLineageSegmentId == target.Id).ToArrayAsync());
            Assert.Single(await db.AuditLogs.Where(x => x.Action == "CanonicalDestinationReconciliation").ToArrayAsync());
            var e = Assert.Single((await new InventoryEvidenceLoader(db).LoadAsync(new(9001, [9003]), DateTimeOffset.UtcNow, default)).Positions);
            Assert.False(InventoryAvailabilityResolver.Resolve(e, new()).IsOperable); // Strict readiness is not relaxed.
            var eligible = InventoryAvailabilityResolver.Resolve(e, new() { AllowIndependentCohorts = true });
            Assert.True(eligible.IsOperable, string.Join(',', eligible.Blockers.Select(x => x.Code)));
            Assert.Equal(7, eligible.AvailableQuantity);
            Assert.Equal(InventoryConfidence.Unknown, eligible.TreatmentConfidence); // Older remainder is still unresolved.
        }
        var second = await f.Command(InventoryCommandKind.RoomMove, 3, room: 9003);
        second = second with { Lines = [second.Lines[0] with { Destination = new(9001, 9002) }] };
        var moved = await f.Execute(second);
        Assert.True(moved.Status == InventoryCommandStatus.Committed, moved.Detail);
        Assert.Equal(15, await f.Physical());
        Assert.Equal(21, await f.Physical(9003));
        Assert.Equal(InventoryCommandStatus.Replayed, (await f.Execute(command)).Status);
        await using var verify = f.CreateDbContext();
        Assert.Equal(retained, await RetainedDestination(verify));
        Assert.Equal(36, await verify.RoomInventoryAdjustments.SumAsync(x => x.ChangeAmount));
    }

    [InventoryPostgresFact]
    public async Task Overdraw_and_stale_duplicate_custody_roll_back_every_table()
    {
        await using var f = await Fixture.Create();
        await SeedDestination(f, 9);
        var tooMany = await f.Command(InventoryCommandKind.RoomMove, 20);
        var before = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Blocked, (await f.Execute(tooMany)).Status);
        Assert.Equal(before, await f.Snapshot());
        var first = await f.Command(InventoryCommandKind.RoomMove, 19);
        var duplicate = first with { OperationKey = Guid.NewGuid().ToString("N") };
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(first)).Status);
        var committed = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Stale, (await f.Execute(duplicate)).Status);
        Assert.Equal(committed, await f.Snapshot());
        Assert.Equal(0, await f.Physical());
        Assert.Equal(36, await f.Physical(9003));
    }

    [InventoryPostgresFact]
    public async Task Exact_room_reversal_restores_only_the_incoming_cohort()
    {
        await using var f = await Fixture.Create();
        var retained = await SeedDestination(f, 9);
        var command = await f.Command(InventoryCommandKind.RoomMove, 7);
        var moved = await f.Execute(command);
        Assert.True(moved.Status == InventoryCommandStatus.Committed, moved.Detail);
        var reversal = await f.Command(InventoryCommandKind.ReverseRoomMove, 7, room: 9003);
        reversal = reversal with { PhysicalParentId = moved.Effects.Single().ParentId, Lines = [reversal.Lines[0] with { Destination = new(9001, 9002) }] };
        var reversed = await f.Execute(reversal);
        Assert.True(reversed.Status == InventoryCommandStatus.Committed, reversed.Detail);
        Assert.Equal(19, await f.Physical());
        Assert.Equal(17, await f.Physical(9003));
        await using var db = f.CreateDbContext();
        Assert.Equal(retained, await RetainedDestination(db));
        var originalIds = moved.Effects.Single().MovementIds.ToArray();
        Assert.Equal(7, await db.TreatmentLineageMovements.Where(x => originalIds.Contains(x.Id)).SumAsync(x => x.BinCount));
        Assert.Equal(7, await db.TreatmentLineageMovements.Where(x => originalIds.Contains(x.ReversesTreatmentLineageMovementId ?? -1)).SumAsync(x => x.BinCount));
    }

    [InventoryPostgresFact]
    public async Task Transfer_receipt_preserves_exclusive_custody_in_an_unresolved_destination()
    {
        await using var f = await Fixture.Create();
        var receive = await f.ReceiveCommand(async () => { await SeedDestination(f, 9, 9007, 9006); });
        var result = await f.Execute(receive);
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
        Assert.Equal(0, await f.Physical());
        await using var db = f.CreateDbContext();
        Assert.Equal(36, await db.RoomInventoryAdjustments.Where(x => x.RoomId == 9007).SumAsync(x => x.ChangeAmount));
        var cohort = Assert.Single(await db.TreatmentLineageSegments.Where(x => x.CohortKey == receive.OperationKey).ToArrayAsync());
        Assert.Equal(19, cohort.CurrentBins);
        Assert.Equal(100000, cohort.ReceiptId); // Ordinary origin, never the transfer TR.
        Assert.Equal("u", cohort.TreatmentSignature);
        var saved = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Replayed, (await f.Execute(receive)).Status);
        Assert.Equal(saved, await f.Snapshot());
        var duplicate = receive with { OperationKey = Guid.NewGuid().ToString("N") };
        Assert.NotEqual(InventoryCommandStatus.Committed, (await f.Execute(duplicate)).Status);
        Assert.Equal(saved, await f.Snapshot());
    }

    [InventoryPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_duplicate_origin_is_rejected_even_with_a_balanced_projection(bool duplicate)
    {
        await using var f = await Fixture.Create();
        await using (var db = f.CreateDbContext())
        {
            var origin = await db.RoomInventoryAdjustments.SingleAsync(x => x.AdjustmentType == "ReceiptAdd");
            if (duplicate)
            {
                var copy = JsonSerializer.Deserialize<RoomInventoryAdjustment>(JsonSerializer.Serialize(origin))!;
                copy.Id = 0; copy.RoomId = 9003;
                db.RoomInventoryAdjustments.Add(copy);
            }
            else origin.ReceiptId = null;
            var projections = await db.TreatmentLineageSegments.Where(x => x.RoomId == 9002).OrderBy(x => x.Id).ToArrayAsync();
            foreach (var projection in projections) projection.CurrentBins = 0;
            projections[0].CurrentBins = 19;
            await db.SaveChangesAsync();
        }
        var command = await f.Command(InventoryCommandKind.RoomMove, 7);
        var before = await f.Snapshot();
        var result = await f.Execute(command);
        Assert.Equal(InventoryCommandStatus.Blocked, result.Status);
        Assert.Contains(duplicate ? "duplicate inventory origins" : "origin", result.Detail);
        Assert.Equal(before, await f.Snapshot());
    }

    internal static async Task<string> SeedDestination(Fixture fixture, int projected, int room = 9003, int warehouse = 9001)
    {
        await using var db = fixture.CreateDbContext();
        var source = await db.Receipts.SingleAsync(x => x.Id == 100000);
        var time = InventoryEvidenceCorpus.Start.AddDays(1);
        var receipt = new Receipt
        {
            Id = 200001,
            CropYear = source.CropYear,
            CompuTechReceiptId = "DEST-HISTORY",
            WarehouseId = warehouse,
            RoomId = room,
            FruitProfileId = source.FruitProfileId,
            GrowerLotId = source.GrowerLotId,
            GrowerNumber = source.GrowerNumber,
            GrowerName = source.GrowerName,
            LotCode = source.LotCode,
            BinCount = 17,
            ReceivedAt = time,
            CreatedAt = time,
            UpdatedAt = time
        };
        var identity = new InventoryIdentity(2026, source.GrowerLotId, source.FruitProfileId, source.LotCode,
            source.GrowerNumber, "CGAL", "Conventional", false, "");
        db.Receipts.Add(receipt);
        db.RoomInventoryAdjustments.Add(new()
        {
            Receipt = receipt,
            WarehouseId = warehouse,
            RoomId = room,
            CropYear = 2026,
            GrowerLotId = source.GrowerLotId,
            FruitProfileId = source.FruitProfileId,
            LotNumber = source.LotCode,
            GrowerName = source.GrowerName,
            ChangeAmount = 17,
            NewBinCount = 17,
            AdjustmentType = "ReceiptAdd",
            AdjustmentAt = time,
            CreatedAt = time
        });
        if (projected != 0) db.TreatmentLineageSegments.Add(new()
        {
            Id = 200001,
            WarehouseId = warehouse,
            RoomId = room,
            CropYear = 2026,
            GrowerLotId = source.GrowerLotId,
            FruitProfileId = source.FruitProfileId,
            IdentityKey = identity.Key,
            GrowerNumberSnapshot = source.GrowerNumber,
            GrowerNameSnapshot = source.GrowerName,
            LotNumberSnapshot = source.LotCode,
            VarietyCodeSnapshot = "CGAL",
            ProductionTypeSnapshot = "Conventional",
            IsOrganicSnapshot = false,
            CurrentBins = projected,
            TreatmentState = "Unknown",
            TreatmentSignature = "x",
            CreatedAt = time,
            UpdatedAt = time
        });
        var chemical = await db.TreatmentChemicals.FirstAsync();
        db.RoomTreatmentApplications.Add(new()
        {
            Id = 200001,
            OperationKey = "destination-historical-treatment",
            TreatmentChemicalId = chemical.Id,
            ApplicationLevel = "Receiving",
            Receipt = receipt,
            WarehouseId = warehouse,
            RoomId = room,
            AppliedAt = time.AddHours(1),
            AppliedByUserId = 8000,
            CreatedByUserId = 8000,
            CreatedAt = time.AddHours(1),
            TotalBinsSnapshot = 17,
            ProductNameSnapshot = "Historical treatment",
            CropSnapshot = "Apples",
            UnitSnapshot = "test",
            CurrencySnapshot = "USD"
        });
        await db.SaveChangesAsync();
        return await RetainedDestination(db);
    }

    private static async Task<string> RetainedDestination(CropQc.Data.CropQcDbContext db) => JsonSerializer.Serialize(new
    {
        Projections = await db.TreatmentLineageSegments.AsNoTracking().Where(x => x.RoomId == 9003 && x.CohortKey == "")
            .OrderBy(x => x.Id).Select(x => new { x.Id, x.CurrentBins, x.ConcurrencyVersion, x.UpdatedAt, x.TreatmentSignature, x.TreatmentState, x.ReceiptId, x.Disposition }).ToArrayAsync(),
        Applications = await db.RoomTreatmentApplications.AsNoTracking().Where(x => x.Id == 200001)
            .Select(x => new { x.Id, x.ReceiptId, x.AppliedAt, x.ReversedAt, x.TotalBinsSnapshot }).ToArrayAsync()
    });
}
