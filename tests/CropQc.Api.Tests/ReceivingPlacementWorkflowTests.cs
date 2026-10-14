using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class ReceivingPlacementWorkflowTests
{
    [InventoryPostgresFact]
    public async Task Zero_bin_status_alias_is_retired_before_receiving_without_manufacturing_inventory()
    {
        await using var f = await Fixture.Create();
        await using (var db = f.CreateDbContext())
        {
            var ledger = await db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 100000);
            ledger.ChangeAmount = 0; ledger.NewBinCount = 0;
            var alias = await db.TreatmentLineageSegments.SingleAsync(x => x.Id == 100000);
            alias.CurrentBins = 0; alias.IdentityKey += "CONVENTIONAL"; alias.InventoryStatusSnapshot = "Conventional";
            await db.SaveChangesAsync();
        }
        var command = new InventoryCommand(Guid.NewGuid().ToString("N"), InventoryCommandKind.ReceiveStock, 8000,
            DateTimeOffset.UtcNow, "Empty status-alias placement regression", [], Receipt: new(2026, 9001, 9002, 100000, 9004, "LOCAL-EMPTY-ALIAS", 40));
        var result = await f.Execute(command);
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
        await using var verify = f.CreateDbContext();
        Assert.Equal(40, await f.Physical());
        var retired = await verify.TreatmentLineageSegments.SingleAsync(x => x.Id == 100000);
        Assert.Equal("Historical", retired.Disposition); Assert.Equal(0, retired.CurrentBins);
        Assert.Single(await verify.Receipts.Where(x => x.CompuTechReceiptId == "LOCAL-EMPTY-ALIAS").ToArrayAsync());
        Assert.Single(await verify.AuditLogs.Where(x => x.Action == "CanonicalInventoryNormalization").ToArrayAsync());
        Assert.Equal(40, await verify.TreatmentLineageSegments.Where(x => x.Disposition == "Current").SumAsync(x => x.CurrentBins));
        var snapshot = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Replayed, (await f.Execute(command)).Status);
        Assert.Equal(snapshot, await f.Snapshot());
    }

    [InventoryPostgresTheory]
    [InlineData("unproven-treatment")]
    [InlineData("negative-authority")]
    [InlineData("sealed")]
    [InlineData("inactive")]
    public async Task Administrator_cannot_bypass_genuine_placement_constraints(string restriction)
    {
        await using var f = await Fixture.Create();
        await using (var db = f.CreateDbContext())
        {
            var admin = await db.Roles.SingleAsync(x => x.Name == BuiltInRoleNames.Admin);
            db.UserRoles.Add(new() { UserId = 8000, RoleId = admin.Id });
            if (restriction == "unproven-treatment")
            {
                var projection = await db.TreatmentLineageSegments.SingleAsync(x => x.Id == 100000);
                projection.TreatmentSignature = "x"; projection.TreatmentState = "Unknown";
            }
            else if (restriction == "negative-authority")
            {
                var ledger = await db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 100000);
                ledger.ChangeAmount = -1; ledger.NewBinCount = -1;
            }
            else
            {
                var room = await db.Rooms.SingleAsync(x => x.Id == 9002);
                if (restriction == "sealed") { room.IsSealed = true; room.SealedAt = DateTimeOffset.UtcNow.AddHours(-1); }
                else room.IsActive = false;
            }
            await db.SaveChangesAsync();
        }
        var command = new InventoryCommand(Guid.NewGuid().ToString("N"), InventoryCommandKind.ReceiveStock, 8000,
            DateTimeOffset.UtcNow, "Local blocked receiving", [], Receipt: new(2026, 9001, 9002, 100000, 9004, "LOCAL-BLOCKED", 40));
        var before = await f.Snapshot();
        var result = await f.Execute(command);
        Assert.Equal(InventoryCommandStatus.Blocked, result.Status);
        Assert.Equal(before, await f.Snapshot());
        if (restriction is "unproven-treatment" or "negative-authority")
        {
            Assert.Contains("LOCAL-BLOCKED", result.Detail);
            Assert.Contains("R9002", result.Detail);
            Assert.Contains("administrator", result.Detail);
            Assert.Contains("No changes were saved", result.Detail);
        }
    }

    [InventoryPostgresTheory]
    [InlineData("receipts")]
    [InlineData("same-number")]
    [InlineData("same-intent")]
    [InlineData("move")]
    public async Task Concurrent_receiving_preserves_single_origin_and_conservation(string scenario)
    {
        await using var f = await Fixture.Create();
        await SeedLegacyLaterReceipt(f);
        var a = new InventoryCommand(Guid.NewGuid().ToString("N"), InventoryCommandKind.ReceiveStock, 8000,
            DateTimeOffset.UtcNow, "Local concurrent receiving", [], Receipt: new(2026, 9001, 9002, 100000, 9004, "LOCAL-RACE-A", 7));
        var b = scenario == "move" ? await f.Command(InventoryCommandKind.RoomMove, 3)
            : a with
            {
                OperationKey = scenario == "same-intent" ? a.OperationKey : Guid.NewGuid().ToString("N"),
                Receipt = a.Receipt! with { ReceiptNumber = scenario == "receipts" ? "LOCAL-RACE-B" : "LOCAL-RACE-A" }
            };
        var ready = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = new Observer(async (stage, attempt) =>
        {
            if (stage != "Resolved" || attempt != 1) return;
            if (Interlocked.Increment(ref ready) == 2) gate.SetResult();
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(30));
        });
        var results = await Task.WhenAll(f.Execute(a, observer), f.Execute(b, observer));
        Assert.Contains(results, x => x.Status == InventoryCommandStatus.Committed);
        if (scenario != "same-number")
            Assert.True(results[0].Status is InventoryCommandStatus.Committed or InventoryCommandStatus.Replayed,
                string.Join("; ", results.Select(x => x.Detail)));
        var expectedReceipts = scenario == "receipts" ? 2 : 1;
        await using var db = f.CreateDbContext();
        Assert.Equal(expectedReceipts, await db.Receipts.CountAsync(x => x.CompuTechReceiptId.StartsWith("LOCAL-RACE-")));
        Assert.Equal(26 + 7 * expectedReceipts, await f.Physical() + await f.Physical(9003));
        Assert.Equal(19, await db.RoomTreatmentApplicationSources.SumAsync(x => x.BinsTreated));
        if (scenario == "same-number") Assert.Contains(results, x => x.Status == InventoryCommandStatus.Conflict);
        if (scenario == "same-intent") Assert.Contains(results, x => x.Status == InventoryCommandStatus.Replayed);
        if (scenario == "move") Assert.True(results[1].Status is InventoryCommandStatus.Committed or InventoryCommandStatus.Stale, results[1].Detail);
    }

    [InventoryPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Receipt_or_transfer_into_occupied_previously_treated_room_preserves_each_arrival(bool transfer)
    {
        await using var f = await Fixture.Create();
        await SeedLegacyLaterReceipt(f);
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        var beforeTreatment = await db.RoomTreatmentApplicationSources.AsNoTracking().Select(x => new { x.Id, x.BinsTreated }).ToArrayAsync();
        var receipt = new InventoryCommand(Guid.NewGuid().ToString("N"), InventoryCommandKind.ReceiveStock, 8000,
            DateTimeOffset.UtcNow, "Local placement regression", [], Receipt: new(2026, 9001, transfer ? 9003 : 9002, 100000, 9004, "LOCAL-PLACEMENT-40", 40));
        var received = await executor.ExecuteAsync(receipt);
        Assert.True(received.Status == InventoryCommandStatus.Committed, received.Detail);
        if (transfer)
        {
            var move = await f.Command(InventoryCommandKind.RoomMove, 40, 9003);
            move = move with { Lines = [move.Lines[0] with { Destination = new(9001, 9002) }] };
            var moved = await executor.ExecuteAsync(move);
            Assert.True(moved.Status == InventoryCommandStatus.Committed, moved.Detail);
            Assert.Equal(0, await f.Physical(9003));
            var snapshot = await f.Snapshot();
            Assert.Equal(InventoryCommandStatus.Replayed, (await executor.ExecuteAsync(move)).Status);
            Assert.Equal(snapshot, await f.Snapshot());
        }
        Assert.Equal(66, await f.Physical());
        var p = Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db))
            .ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow)).Positions);
        Assert.True(p.IsOperable, string.Join(", ", p.Blockers));
        Assert.Equal(p.AuthoritativeQuantity, p.RawProjectionQuantity);
        Assert.Equal(19, p.TreatmentSlices.Where(x => x.State == "Confirmed").Sum(x => x.Quantity));
        Assert.Equal(47, p.TreatmentSlices.Where(x => x.State == "Untreated").Sum(x => x.Quantity));
        Assert.Equal(beforeTreatment, await db.RoomTreatmentApplicationSources.AsNoTracking().Select(x => new { x.Id, x.BinsTreated }).ToArrayAsync());
        var receiptId = received.Effects.Single().ParentId;
        Assert.All(await db.TreatmentLineageSegments.Include(x => x.Applications).Where(x => x.ReceiptId == receiptId).ToArrayAsync(),
            x => { Assert.Equal("u", x.TreatmentSignature); Assert.Empty(x.Applications); });
        Assert.Single(await db.Receipts.Where(x => x.CompuTechReceiptId == "LOCAL-PLACEMENT-40").ToArrayAsync());
        Assert.NotEmpty(await db.AuditLogs.Where(x => x.Action == "CanonicalReceiptCreated").ToArrayAsync());
        // The same proof must survive later exact consumption of either the old
        // treated fruit or the legacy untreated receipt.
        foreach (var signature in new[] { p.TreatmentSlices.Single(x => x.State == "Confirmed").Signature, "u" })
        {
            var move = await f.Command(InventoryCommandKind.RoomMove, 3);
            move = move with { Lines = [move.Lines[0] with { TreatmentSignature = signature }] };
            var moved = await executor.ExecuteAsync(move);
            Assert.True(moved.Status == InventoryCommandStatus.Committed, moved.Detail);
        }
        Assert.Equal(60, await f.Physical());
        Assert.Equal(6, await f.Physical(9003));
    }

    [InventoryPostgresTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Forty_fuji_bins_from_lot_1033_have_one_origin_without_touching_other_room_discrepancies(int occupied)
    {
        await using var f = await Fixture.Create();
        await using (var seed = f.CreateDbContext())
        {
            seed.GrowerLots.Add(new() { Id = 100033, LotNumber = "1033", Grower = "Fixture grower" });
            if (occupied == 2)
            {
                // Unrelated unproven lineage stays visible; receiving a different
                // identity must not normalize or consume it.
                var row = await seed.TreatmentLineageSegments.SingleAsync(x => x.Id == 100000);
                row.TreatmentState = "Unknown"; row.TreatmentSignature = "x";
            }
            await seed.SaveChangesAsync();
        }
        var room = occupied == 0 ? 9003 : 9002;
        var command = new InventoryCommand(Guid.NewGuid().ToString("N"), InventoryCommandKind.ReceiveStock, 8000,
            DateTimeOffset.UtcNow, "Local Fuji receiving", [], Receipt: new(2026, 9001, room, 100033, 1, "LOCAL-1033", 40));
        var before = await f.Snapshot();
        await Assert.ThrowsAsync<IOException>(() => f.Execute(command, new Observer((stage, _) => stage == "BeforeCommit"
            ? Task.FromException(new IOException("Interrupted local transaction")) : Task.CompletedTask)));
        Assert.Equal(before, await f.Snapshot());
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(command)).Status);
        var committed = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Replayed, (await f.Execute(command)).Status);
        Assert.Equal(committed, await f.Snapshot());
        Assert.Equal(InventoryCommandStatus.Conflict, (await f.Execute(command with { OperationKey = Guid.NewGuid().ToString("N") })).Status);
        Assert.Equal(committed, await f.Snapshot());
        await using var db = f.CreateDbContext();
        var receipt = await db.Receipts.SingleAsync(x => x.CompuTechReceiptId == "LOCAL-1033");
        Assert.Equal(40, receipt.BinCount);
        Assert.Equal(40, await db.RoomInventoryAdjustments.Where(x => x.ReceiptId == receipt.Id).SumAsync(x => x.ChangeAmount));
        Assert.Equal(40, await db.TreatmentLineageSegments.Where(x => x.ReceiptId == receipt.Id && x.Disposition == "Current").SumAsync(x => x.CurrentBins));
        Assert.Single(await db.TreatmentLineageMovements.Where(x => x.ReceiptId == receipt.Id).ToArrayAsync());
        Assert.Equal(29, (await db.TreatmentLineageSegments.SingleAsync(x => x.Id == 100000)).CurrentBins);
        Assert.Equal(40 + (occupied == 0 ? 0 : 19), await f.Physical(room));
    }

    internal static async Task SeedLegacyLaterReceipt(Fixture f)
    {
        await using var db = f.CreateDbContext();
        var at = DateTimeOffset.UtcNow.AddHours(-3);
        var chemical = await db.TreatmentChemicals.FirstAsync(x => x.IsActive && x.ApplicationLevel == "Room");
        var app = new RoomTreatmentApplication
        {
            OperationKey = "local-historical-treatment",
            WarehouseId = 9001,
            RoomId = 9002,
            TreatmentChemicalId = chemical.Id,
            AppliedAt = at,
            AppliedByUserId = 8000,
            CreatedByUserId = 8000,
            CreatedAt = at,
            TotalBinsSnapshot = 19,
            ProductNameSnapshot = chemical.ProductName,
            CropSnapshot = chemical.Crop,
            UnitSnapshot = chemical.Unit,
            CurrencySnapshot = chemical.Currency
        };
        db.RoomTreatmentApplications.Add(app);
        await db.SaveChangesAsync();
        var prior = await db.TreatmentLineageSegments.SingleAsync(x => x.Id == 100000);
        prior.CurrentBins = 19; prior.ReceiptId = 100000;
        prior.TreatmentState = "Confirmed"; prior.TreatmentSignature = $"u|a:{app.Id}";
        prior.Applications.Add(new() { RoomTreatmentApplicationId = app.Id });
        app.Sources.Add(new()
        {
            ReceiptId = 100000,
            CropYear = 2026,
            GrowerLotId = 100000,
            FruitProfileId = 9004,
            IdentityKey = prior.IdentityKey,
            GrowerNameSnapshot = prior.GrowerNameSnapshot,
            LotNumberSnapshot = prior.LotNumberSnapshot,
            VarietyCodeSnapshot = "CGAL",
            ProductionTypeSnapshot = "Conventional",
            IsOrganicSnapshot = false,
            BinsTreated = 19,
            PriorTreatmentSignature = "u",
            ResultTreatmentSignature = prior.TreatmentSignature
        });
        var source = await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == 100000);
        var receipt = new Receipt
        {
            CropYear = source.CropYear,
            WarehouseId = source.WarehouseId,
            RoomId = source.RoomId,
            FruitProfileId = source.FruitProfileId,
            GrowerLotId = source.GrowerLotId,
            GrowerName = source.GrowerName,
            GrowerNumber = source.GrowerNumber,
            LotCode = source.LotCode,
            CompuTechReceiptId = "LOCAL-LEGACY-LATER",
            BinCount = 7,
            ReceivedAt = at.AddHours(1),
            CreatedAt = at.AddHours(1),
            UpdatedAt = at.AddHours(1)
        };
        db.Receipts.Add(receipt);
        await db.SaveChangesAsync();
        db.RoomInventoryAdjustments.Add(new()
        {
            CropYear = receipt.CropYear,
            WarehouseId = receipt.WarehouseId,
            RoomId = receipt.RoomId,
            FruitProfileId = receipt.FruitProfileId,
            GrowerLotId = receipt.GrowerLotId,
            ReceiptId = receipt.Id,
            GrowerName = receipt.GrowerName,
            LotNumber = receipt.LotCode,
            VarietyCode = "CGAL",
            ChangeAmount = 7,
            NewBinCount = 26,
            AdjustmentType = "ReceiptAdd",
            AdjustmentAt = receipt.ReceivedAt,
            CreatedAt = receipt.CreatedAt
        });
        var identity = new InventoryIdentity(2026, 100000, 9004, source.LotCode, source.GrowerNumber, "CGAL", "Conventional", false, "");
        db.TreatmentLineageSegments.Add(new()
        {
            IdentityKey = identity.Key,
            WarehouseId = 9001,
            RoomId = 9002,
            CropYear = 2026,
            GrowerLotId = 100000,
            FruitProfileId = 9004,
            GrowerNameSnapshot = source.GrowerName,
            GrowerNumberSnapshot = source.GrowerNumber,
            LotNumberSnapshot = source.LotCode,
            VarietyCodeSnapshot = "CGAL",
            ProductionTypeSnapshot = "Conventional",
            IsOrganicSnapshot = false,
            TreatmentState = "Untreated",
            TreatmentSignature = "u",
            ReceiptId = receipt.Id,
            CurrentBins = 7,
            CreatedAt = receipt.CreatedAt,
            UpdatedAt = receipt.UpdatedAt
        });
        await db.SaveChangesAsync();
    }
}
