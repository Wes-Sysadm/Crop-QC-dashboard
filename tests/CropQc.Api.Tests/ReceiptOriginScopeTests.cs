using System.Collections.Immutable;
using System.Data;
using System.Reflection;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Shared.Time;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class ReceiptOriginScopeTests(ITestOutputHelper output)
{
    [InventoryPostgresFact]
    public async Task Cross_facility_corrected_origins_match_every_source_scope_without_authorizing_unproven_stock()
    {
        await using var f = await CrossFacility();
        var before = await f.Snapshot();
        await using var db = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection).CreateDbContext();
        var scopes = await ReadScopes(db);
        var expected = scopes[0];
        Assert.Equal(238, expected.AuthoritativeQuantity);
        Assert.Equal(new long[] { 100000, 100001, 100002, 100003 }, expected.Receipts.Select(r => r.Id));
        Assert.Equal(56, expected.Receipts[0].OriginalQuantity);
        Assert.True(expected.Receipts[0].OriginalIdentityVerified);
        Assert.Equal(56, expected.Receipts[1].OriginalQuantity);
        Assert.False(expected.Receipts[1].OriginalIdentityVerified);
        Assert.All(expected.Receipts.Skip(2), r => Assert.Null(r.OriginalQuantity));
        Assert.All(scopes, p => Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(p)));

        var options = await new OutsideWarehouseTransferService(db,
            new CropQc.Web.Services.RoomInventoryLedgerQueryService(db), null!, null!, null!, null!, null!,
            new PacificBusinessTimeService(new SystemClock())).GetInventoryAsync(default);
        Assert.All(options.Where(o => o.RoomId == 9003), o => Assert.EndsWith(expected.Watermark.Fingerprint, o.SourceKey));
        Assert.Contains(options, o => o.RoomId == 9003);
        Assert.False(InventoryAvailabilityResolver.Resolve(expected, new(RequireExactReceipt: true, ReceiptId: 100001)).IsOperable);
        // The synthetic correction deliberately has the same independent class
        // of unsupported origin as the reported pool. Equal proof is not approval.
        var guard = typeof(InventoryEvidenceLoader).Assembly.GetType("CropQc.Data.Inventory.InventoryOriginGuard")!;
        var error = await (Task<string?>)guard.GetMethod("ValidateAsync", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [db, new[] { expected }, DateTimeOffset.UtcNow, CancellationToken.None, false])!;
        Assert.NotNull(error);
        Assert.Contains("credits more inventory", error);
        Assert.Equal(before, await f.Snapshot());
        output.WriteLine(JsonSerializer.Serialize(new
        {
            scopes = scopes.Select(p => new { p.Watermark, p.Receipts }),
            originRejection = error,
            expected.AuthoritativeQuantity,
            allTablesUnchanged = true
        }));
    }

    [InventoryPostgresFact]
    public async Task Relevant_original_proof_changes_invalidate_selection_but_unrelated_receipts_do_not()
    {
        await using var f = await CrossFacility();
        await using var db = f.CreateDbContext();
        var previous = (await ReadScopes(db))[0];
        (await db.Receipts.SingleAsync(r => r.Id == 100004)).ConcurrencyVersion++;
        await db.SaveChangesAsync();
        Assert.Equal(previous.Watermark.Fingerprint, (await ReadScopes(db))[0].Watermark.Fingerprint);
        foreach (var change in new[] { "version", "revision", "origin", "treatment" })
        {
            if (change == "version") (await db.Receipts.SingleAsync(r => r.Id == 100000)).ConcurrencyVersion++;
            if (change == "revision") (await db.ReceiptInventoryOverrides.SingleAsync(r => r.ReceiptId == 100000)).IsComplete = false;
            if (change == "origin") (await db.RoomInventoryAdjustments.SingleAsync(r => r.Id == 100001)).ChangeAmount = 55;
            if (change == "treatment") (await db.RoomTreatmentApplications.SingleAsync()).ReversedAt = InventoryEvidenceCorpus.Start.AddDays(3);
            await db.SaveChangesAsync();
            var scopes = await ReadScopes(db);
            Assert.All(scopes, p => Assert.Equal(JsonSerializer.Serialize(scopes[0]), JsonSerializer.Serialize(p)));
            Assert.NotEqual(previous.Watermark.Fingerprint, scopes[0].Watermark.Fingerprint);
            Assert.Contains(InventoryAvailabilityResolver.Resolve(scopes[0], new(ExpectedFingerprint: previous.Watermark.Fingerprint)).Blockers,
                b => b.Code == InventoryBlockerCode.StaleRead);
            Assert.Equal(238, scopes[0].AuthoritativeQuantity);
            previous = scopes[0];
        }
    }

    [InventoryPostgresFact]
    public async Task Earliest_revision_missing_and_contradictory_origins_preserve_unknown_or_false_proof()
    {
        await using var f = await CrossFacility();
        await using var db = f.CreateDbContext();
        var first = await db.ReceiptInventoryOverrides.SingleAsync(r => r.ReceiptId == 100000);
        db.ReceiptInventoryOverrides.Add(Revision(100000, "999", InventoryEvidenceCorpus.Start.AddDays(3)));
        await db.SaveChangesAsync();
        Assert.Equal(56, (await ReadScopes(db))[0].Receipts[0].OriginalQuantity);
        // Current receiving location is not the original location or current
        // physical custody. A location-only edit cannot change original proof.
        var relocated = await db.Receipts.SingleAsync(r => r.Id == 100000);
        relocated.WarehouseId = 9006;
        relocated.RoomId = 9003;
        await db.SaveChangesAsync();
        Assert.All(await ReadScopes(db), p => Assert.True(p.Receipts[0].OriginalIdentityVerified));
        foreach (var snapshot in new[] { "{broken", "{}", "{\"id\":100000,\"binCount\":56,\"cropYear\":2026,\"growerLotId\":100000,\"fruitProfileId\":9005}" })
        {
            first.BeforeReceiptSnapshotJson = snapshot;
            await db.SaveChangesAsync();
            var scopes = await ReadScopes(db);
            Assert.All(scopes, p => Assert.False(p.Receipts[0].OriginalIdentityVerified));
            Assert.All(scopes, p => Assert.Equal(JsonSerializer.Serialize(scopes[0]), JsonSerializer.Serialize(p)));
        }
        // A conflicting duplicate origin must not multiply receipt evidence or
        // choose a later, convenient revision in place of the first one.
        var duplicate = Row(100000, 9002, 9001, 55, "ReceiptAdd", InventoryEvidenceCorpus.Start);
        duplicate.FruitProfileId = 9005;
        db.RoomInventoryAdjustments.Add(duplicate);
        await db.SaveChangesAsync();
        Assert.All(await ReadScopes(db), p =>
        {
            Assert.Equal(4, p.Receipts.Length);
            Assert.False(p.Receipts[0].OriginalIdentityVerified);
            Assert.Equal(238, p.AuthoritativeQuantity);
        });
        db.RoomInventoryAdjustments.Remove(duplicate);
        foreach (var row in await db.RoomInventoryAdjustments.Where(r => r.ReceiptId == 100000 && r.ReceiptInventoryOverrideId != null).ToArrayAsync())
            row.ReceiptInventoryOverrideId = null;
        db.ReceiptInventoryOverrides.RemoveRange(await db.ReceiptInventoryOverrides.Where(r => r.ReceiptId == 100000).ToArrayAsync());
        await db.SaveChangesAsync();
        Assert.All(await ReadScopes(db), p => Assert.Null(p.Receipts[0].OriginalIdentityVerified));
        db.RoomInventoryAdjustments.Remove(await db.RoomInventoryAdjustments.SingleAsync(r => r.Id == 100000));
        await db.SaveChangesAsync();
        Assert.All(await ReadScopes(db), p => Assert.Null(p.Receipts[0].OriginalQuantity));
    }

    [InventoryPostgresFact]
    public async Task Consistent_snapshot_and_historical_cutoff_survive_cross_facility_dependency_loading()
    {
        await using var f = await CrossFacility();
        await using var db = f.CreateDbContext();
        await using (var tx = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead))
        {
            var initial = (await ReadScopes(db))[0];
            await using (var other = f.CreateDbContext())
            {
                (await other.Receipts.SingleAsync(r => r.Id == 100000)).ConcurrencyVersion++;
                await other.SaveChangesAsync();
            }
            Assert.All(await ReadScopes(db), p => Assert.Equal(initial.Watermark.Fingerprint, p.Watermark.Fingerprint));
        }
        var cutoff = InventoryEvidenceCorpus.Start.AddHours(12);
        var rooms = await new InventoryEvidenceLoader(db).LoadAsync(new(null, [9002, 9003]), cutoff, default);
        Assert.DoesNotContain(rooms.Positions, p => p.Location.RoomId == 9003 && p.AuthoritativeQuantity > 0);
        Assert.Contains(rooms.Positions, p => p.HistoricalSnapshotUnavailable);
        await using var weak = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new InventoryEvidenceLoader(db).LoadAsync(new(9006, [9003]), DateTimeOffset.UtcNow, default));
    }

    private static async Task<InventoryPositionEvidence[]> ReadScopes(CropQcDbContext db)
    {
        var at = DateTimeOffset.UtcNow;
        InventoryScope[] scopes = [new(null, [9002, 9003]), new(9006, []), new(null, [9003]), new(9006, [9003])];
        var result = new List<InventoryPositionEvidence>();
        foreach (var scope in scopes)
            result.Add(Assert.Single((await new InventoryEvidenceLoader(db).LoadAsync(scope, at, default)).Positions,
                p => p.Location.RoomId == 9003 && p.Identity.GrowerLotId == 100000));
        return result.ToArray();
    }

    private static ReceiptInventoryOverride Revision(long receipt, string snapshot, DateTimeOffset at) => new()
    {
        Id = Guid.NewGuid(),
        ReceiptId = receipt,
        OperationKey = Guid.NewGuid().ToString(),
        ActionType = ReceiptInventoryOverrideActionTypes.QuantityCorrection,
        OldReceiptBinCount = 56,
        NewReceiptBinCount = receipt == 100000 ? 64 : 56,
        InventoryDelta = receipt == 100000 ? 8 : 0,
        AdministratorUserId = 8000,
        Reason = "Synthetic original evidence",
        CreatedAt = at,
        IsComplete = true,
        BeforeReceiptSnapshotJson = snapshot,
        AfterReceiptSnapshotJson = "{}",
        AffectedInventorySnapshotJson = "[]"
    };

    // Synthetic only: four origins in one facility, corrected metadata and valid
    // debit/credit transfer pairs into another. The unsupported +8 correction is
    // intentionally retained, independently of the receipt-evidence regression.
    internal static async Task<Fixture> CrossFacility()
    {
        var f = await Fixture.Create(5);
        await using var db = f.CreateDbContext();
        var start = InventoryEvidenceCorpus.Start;
        db.Warehouses.Add(new() { Id = 9006, Code = "SYNTHETIC-WP", Name = "Synthetic receiving facility" });
        (await db.Rooms.SingleAsync(r => r.Id == 9003)).WarehouseId = 9006;
        db.FruitProfiles.Add(new() { Id = 9005, Name = "Earlier profile", VarietyCode = "SYNTH-OLD", FruitType = "Pear", ProductionType = "Conventional" });
        db.TreatmentLineageSegments.RemoveRange(await db.TreatmentLineageSegments.ToListAsync());
        var revision = Revision(100000, "56", start.AddHours(2));
        revision.InventoryDelta = 0; // no authorized increase funds the recorded +8
        db.ReceiptInventoryOverrides.Add(revision);
        var identityRevision = Revision(100001,
            "{\"id\":100001,\"binCount\":56,\"cropYear\":2026,\"growerLotId\":100000,\"fruitProfileId\":9005}", start.AddHours(2));
        db.ReceiptInventoryOverrides.Add(identityRevision);
        int[] quantities = [64, 56, 54, 64];
        for (var i = 0; i < 4; i++)
        {
            var id = 100000 + i;
            var receipt = await db.Receipts.SingleAsync(r => r.Id == id);
            receipt.GrowerLotId = 100000; receipt.GrowerNumber = receipt.LotCode = "CORPUS-100000";
            receipt.BinCount = quantities[i]; receipt.UpdatedAt = start.AddHours(2);
            var origin = await db.RoomInventoryAdjustments.SingleAsync(r => r.Id == id);
            origin.GrowerLotId = 100000; origin.LotNumber = "CORPUS-100000";
            origin.ChangeAmount = origin.NewBinCount = i < 2 ? 56 : quantities[i];
            if (i == 1) origin.FruitProfileId = 9005;
            db.RoomTransfers.Add(new()
            {
                Id = id,
                OperationKey = $"cross-scope-{i}",
                SourceWarehouseId = 9001,
                SourceRoomId = 9002,
                DestinationWarehouseId = 9006,
                DestinationRoomId = 9003,
                CropYear = 2026,
                GrowerLotId = 100000,
                FruitProfileId = 9004,
                GrowerName = "Corpus",
                LotNumber = "CORPUS-100000",
                VarietyCode = "CGAL",
                BinCount = quantities[i],
                Reason = "Synthetic recorded movement",
                TransferredAt = start.AddDays(1),
                CreatedAt = start.AddDays(1)
            });
            foreach (var (room, warehouse, quantity, kind) in new[] { (9002, 9001, -quantities[i], "TransferOut"), (9003, 9006, quantities[i], "TransferIn") })
            {
                var row = Row(id, room, warehouse, quantity, kind, start.AddDays(1));
                row.RoomTransferId = id;
                db.RoomInventoryAdjustments.Add(row);
            }
        }
        var extra = Row(100000, 9002, 9001, 8, "ReceiptAdminOverride", start.AddHours(2));
        extra.ReceiptInventoryOverrideId = revision.Id;
        db.RoomInventoryAdjustments.Add(extra);
        // Preserve a balanced recorded identity correction, with original profile
        // deliberately not verified as the current profile by OriginalReceipt.
        var old = Row(100001, 9002, 9001, -56, "ReceiptAdminOverride", start.AddHours(2));
        old.FruitProfileId = 9005;
        old.ReceiptInventoryOverrideId = identityRevision.Id;
        var current = Row(100001, 9002, 9001, 56, "ReceiptAdminOverride", start.AddHours(2));
        current.ReceiptInventoryOverrideId = identityRevision.Id;
        db.RoomInventoryAdjustments.AddRange(old, current);
        var chemical = await db.TreatmentChemicals.FirstAsync(x => x.IsActive && x.ApplicationLevel == "Room");
        db.RoomTreatmentApplications.Add(new()
        {
            Id = 800000,
            OperationKey = "cross-scope-treatment",
            ReceiptId = 100000,
            TreatmentChemicalId = chemical.Id,
            WarehouseId = 9001,
            RoomId = 9002,
            AppliedByUserId = 8000,
            CreatedByUserId = 8000,
            TotalBinsSnapshot = 56,
            ProductNameSnapshot = chemical.ProductName,
            CropSnapshot = chemical.Crop,
            UnitSnapshot = chemical.Unit,
            CurrencySnapshot = chemical.Currency,
            AppliedAt = start.AddHours(1),
            CreatedAt = start.AddHours(1)
        });
        await db.SaveChangesAsync();
        return f;
    }

    private static RoomInventoryAdjustment Row(long receipt, int room, int warehouse, int quantity, string kind, DateTimeOffset at) => new()
    {
        ReceiptId = receipt,
        WarehouseId = warehouse,
        RoomId = room,
        CropYear = 2026,
        GrowerLotId = 100000,
        FruitProfileId = 9004,
        GrowerName = "Corpus",
        LotNumber = "CORPUS-100000",
        VarietyCode = "CGAL",
        ChangeAmount = quantity,
        NewBinCount = Math.Max(0, quantity),
        AdjustmentType = kind,
        AdjustmentAt = at,
        CreatedAt = at
    };
}
