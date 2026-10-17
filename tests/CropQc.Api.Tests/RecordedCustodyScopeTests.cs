using System.Collections.Immutable;
using System.Text.Json;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class RecordedCustodyScopeTests
{
    // Quantity/arrival-count shapes of all 18 reported positions. IDs are only
    // diagnostic references; every transaction below is disposable synthetic data.
    [InventoryPostgresTheory]
    [InlineData("9/9541", 1, 1, false)]
    [InlineData("15/1011", 350, 1, false)]
    [InlineData("15/3152", 61, 2, true)]
    [InlineData("15/3162", 67, 1, false)]
    [InlineData("15/9092", 126, 2, true)]
    [InlineData("15/9362", 196, 1, false)]
    [InlineData("15/9372", 136, 2, true)]
    [InlineData("15/9682", 252, 2, false)]
    [InlineData("17/3152", 193, 3, true)]
    [InlineData("17/9362", 37, 1, false)]
    [InlineData("18/3032", 55, 1, false)]
    [InlineData("18/9092", 184, 1, true)]
    [InlineData("18/9332", 696, 1, false)]
    [InlineData("18/9362", 66, 1, true)]
    [InlineData("18/9380", 29, 2, false)]
    [InlineData("18/9722", 160, 1, true)]
    [InlineData("63/2200", 296, 4, false)]
    [InlineData("63/2910", 27, 2, true)]
    public async Task Modern_peers_keep_complete_proof_across_all_scopes(string reference, int current, int arrivals, bool treated)
    {
        await using var f = await LegacyTransferEligibilityTests.Legacy(treated);
        var total = current + 33; // 29 consumed, three in another modern peer, one still legacy.
        await using (var seed = f.CreateDbContext())
        {
            (await seed.Receipts.SingleAsync()).BinCount = total;
            (await seed.RoomTransfers.SingleAsync()).BinCount = total;
            foreach (var row in await seed.RoomInventoryAdjustments.ToArrayAsync())
            {
                row.ChangeAmount = row.ChangeAmount < 0 ? -total : total;
                row.NewBinCount = row.ChangeAmount < 0 ? 0 : total;
            }
            if (treated)
            {
                var application = await seed.RoomTreatmentApplications.Include(x => x.Sources).SingleAsync();
                application.TotalBinsSnapshot = total;
                Assert.Single(application.Sources).BinsTreated = total;
            }
            seed.Rooms.AddRange(new() { Id = 9010, WarehouseId = 9001, Code = "MODERN-B", Name = reference },
                new() { Id = 9011, WarehouseId = 9001, Code = "MODERN-C", Name = "Another modern peer" });
            await seed.SaveChangesAsync();
        }
        var remaining = current + 29;
        for (var i = 0; i < arrivals; i++)
        {
            var quantity = i == arrivals - 1 ? remaining : 1;
            await Commit(f, await Command(f, 9003, 9010, quantity));
            remaining -= quantity;
        }
        await Commit(f, await Command(f, 9003, 9011, 3));
        var dump = (await Command(f, 9010, 9002, 29)) with { Kind = InventoryCommandKind.Dump };
        dump = dump with { Lines = [dump.Lines[0] with { Destination = null }] };
        await Commit(f, dump);

        var before = await f.Snapshot();
        await using var db = f.CreateDbContext();
        var at = DateTimeOffset.UtcNow;
        var loader = new InventoryEvidenceLoader(db);
        var allRooms = (await db.Rooms.Select(x => x.Id).ToArrayAsync()).ToImmutableArray();
        InventoryScope[] scopes = [new(null, allRooms), new(9001, []), new(null, [9010]), new(9001, [9010])];
        InventoryPositionEvidence? expected = null;
        string? resolved = null;
        foreach (var scope in scopes)
        {
            var batch = await loader.LoadAsync(scope, at, default);
            var evidence = Assert.Single(batch.Positions, p => p.Location.RoomId == 9010);
            Assert.Equal(current, evidence.AuthoritativeQuantity);
            var result = Assert.Single((await new InventoryAvailabilityResolver(loader).ResolveAsync(scope,
                new() { AllowIndependentCohorts = true }, at)).Positions, p => p.Location.RoomId == 9010);
            Assert.True(result.IsOperable, string.Join(",", result.Blockers.Select(x => x.Code)));
            Assert.Equal(current, result.AvailableQuantity);
            if (expected != null)
            {
                Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(evidence));
                Assert.Equal(resolved, JsonSerializer.Serialize(result));
            }
            expected = evidence;
            resolved = JsonSerializer.Serialize(result);
        }
        Assert.Empty(expected!.RecordedArrivals);
        var broad = await loader.LoadAsync(new(null, allRooms), at, default);
        foreach (var peer in broad.Positions)
        {
            var narrow = await loader.LoadAsync(new(peer.Location.WarehouseId, [peer.Location.RoomId!.Value]), at, default);
            Assert.Equal(JsonSerializer.Serialize(peer), JsonSerializer.Serialize(Assert.Single(narrow.Positions,
                p => p.Identity.Key == peer.Identity.Key)));
        }
        var legacy = Assert.Single(broad.Positions, p => p.Location.RoomId == 9003);
        Assert.NotEmpty(legacy.RecordedArrivals);
        Assert.Equal(1, legacy.AuthoritativeQuantity);
        Assert.Equal(current + 4, broad.Positions.Sum(p => p.AuthoritativeQuantity));
        Assert.Equal(before, await f.Snapshot());

        // A source selected on the broad page is usable by the narrow command
        // reader, and repeating that exact operation cannot move another bin.
        var command = await Command(f, 9010, 9002, 1);
        command = command with
        {
            Lines = [command.Lines[0] with { Source = new(expected.Identity, expected.Location,
            expected.Watermark.Fingerprint, expected.Watermark.Versions) }]
        };
        await Commit(f, command);
        Assert.Equal(InventoryCommandStatus.Replayed, (await Executor(f).ExecuteAsync(command)).Status);
        Assert.Equal(InventoryCommandStatus.Stale, (await Executor(f).ExecuteAsync(command with { OperationKey = Guid.NewGuid().ToString("N") })).Status);
    }

    private static InventoryCommandExecutor Executor(Fixture f) => new(new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection));

    [InventoryPostgresFact]
    public async Task Mixed_modern_peers_preserve_legacy_dependencies_unknown_ancestry_and_safety_blocks()
    {
        await using var f = await MixedHistory();
        var before = await f.Snapshot();
        await using var db = f.CreateDbContext();
        var at = DateTimeOffset.UtcNow;
        var rooms = (await db.Rooms.Select(x => x.Id).ToArrayAsync()).ToImmutableArray();
        var loader = new InventoryEvidenceLoader(db);
        var broad = await loader.LoadAsync(new(null, rooms), at, default);
        foreach (var group in broad.Positions.GroupBy(p => p.Location))
        {
            foreach (var warehouse in new int?[] { null, group.Key.WarehouseId })
            {
                var narrow = await loader.LoadAsync(new(warehouse, [group.Key.RoomId!.Value]), at, default);
                foreach (var expected in group)
                {
                    var actual = Assert.Single(narrow.Positions, p => p.Identity.Key == expected.Identity.Key);
                    Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
                    Assert.Equal(JsonSerializer.Serialize(InventoryAvailabilityResolver.Resolve(expected, new())),
                        JsonSerializer.Serialize(InventoryAvailabilityResolver.Resolve(actual, new())));
                }
            }
        }
        var mixed = Assert.Single(broad.Positions, p => p.Location.RoomId == 9002 && p.Identity.Lot == "BROAD-01");
        var replay = InventoryEventReplay.Replay(mixed);
        Assert.True(replay.QuantityConserved);
        Assert.Equal(5, replay.Cohorts.Where(c => c.ReceiptId == 700001 && c.Signature == "u|a:810001"
            && c.ApplicationIds.SequenceEqual([810001L])).Sum(c => c.Quantity));
        Assert.Equal(2, replay.Cohorts.Where(c => c.ReceiptId == 720001 && c.Signature == "u" && c.ApplicationIds.IsEmpty).Sum(c => c.Quantity));
        var shared = Assert.Single(broad.Positions, p => p.Location.RoomId == 9012);
        var sharedReplay = InventoryEventReplay.Replay(shared);
        Assert.True(sharedReplay.QuantityConserved);
        Assert.Equal(5, sharedReplay.Cohorts.Sum(c => c.Quantity));
        Assert.All(sharedReplay.Cohorts, c =>
        {
            Assert.Null(c.ReceiptId);
            Assert.Equal(800000, Assert.Single(c.ApplicationIds));
        });
        Assert.False(InventoryAvailabilityResolver.Resolve(shared, new(RequireExactReceipt: true, ReceiptId: 100004)).IsOperable);
        var reversed = Assert.Single(broad.Positions, p => p.Location.RoomId == 9013);
        var reversedReplay = InventoryEventReplay.Replay(reversed);
        Assert.True(reversedReplay.QuantityConserved);
        Assert.Equal(5, reversedReplay.Cohorts.Sum(c => c.Quantity));
        Assert.All(reversedReplay.Cohorts, c =>
        {
            Assert.Equal("u", c.Signature);
            Assert.Empty(c.ApplicationIds);
        });
        Assert.Empty(mixed.RecordedArrivals);
        Assert.Empty(shared.RecordedArrivals);
        Assert.Equal(before, await f.Snapshot());

        var selection = await Command(f, 9002, 9012, 1, "BROAD-01", "u|a:810001");
        var unrelated = Assert.Single(broad.Positions, p => p.Identity.Lot == "BROAD-30" && p.AuthoritativeQuantity > 0);
        // Fault-inject only the detached reader input; persisted canonical
        // history and its writer fence remain intact.
        var invalid = mixed with
        {
            Movements = mixed.Movements.Select(m => m.Incoming && m.Signature == "u|a:810001"
                ? m with { Quantity = m.Quantity - 1 } : m).ToImmutableArray()
        };
        Assert.False(InventoryEventReplay.Replay(invalid).QuantityConserved);
        await using (var change = f.CreateDbContext())
        {
            (await change.Rooms.SingleAsync(r => r.Id == 9013)).IsSealed = true;
            await change.SaveChangesAsync();
        }
        await Commit(f, selection with { OperationKey = Guid.NewGuid().ToString("N") });
        at = DateTimeOffset.UtcNow;
        var changed = await loader.LoadAsync(new(null, rooms), at, default);
        var moved = Assert.Single(changed.Positions, p => p.Location == mixed.Location && p.Identity.Key == mixed.Identity.Key);
        Assert.Empty(moved.RecordedArrivals);
        Assert.NotEqual(mixed.Watermark.Fingerprint, moved.Watermark.Fingerprint);
        Assert.Equal(mixed.AuthoritativeQuantity - 1, moved.AuthoritativeQuantity);
        var sealedResult = await Executor(f).ExecuteAsync(await Command(f, 9013, 9012, 1, "BROAD-02", "u"));
        Assert.Equal(InventoryCommandStatus.Blocked, sealedResult.Status);
        Assert.Contains("sealed", sealedResult.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(unrelated.Watermark.Fingerprint, Assert.Single(changed.Positions,
            p => p.Location == unrelated.Location && p.Identity.Key == unrelated.Identity.Key).Watermark.Fingerprint);
        Assert.Equal(InventoryCommandStatus.Stale, (await Executor(f).ExecuteAsync(selection)).Status);
    }

    internal static async Task<Fixture> MixedHistory()
    {
        var f = await HistoricalCustodyScopeTests.BroadHistory();
        await using (var db = f.CreateDbContext())
        {
            db.Rooms.AddRange(new() { Id = 9012, WarehouseId = 9001, Code = "SHARED", Name = "Modern shared ancestry" },
                new() { Id = 9013, WarehouseId = 9001, Code = "REVERSED", Name = "Modern reversed treatment" });
            await db.SaveChangesAsync();
        }
        await Commit(f, await Command(f, 9306, 9002, 5, "BROAD-01", "u|a:810001"));
        await Commit(f, await Command(f, 9306, 9002, 2, "BROAD-01", "u"));
        await Commit(f, await Command(f, 9003, 9012, 5, "1110", "u|a:800000"));
        await Commit(f, await Command(f, 9307, 9013, 5, "BROAD-02", "u"));
        return f;
    }

    private static async Task Commit(Fixture f, InventoryCommand command)
    {
        var result = await Executor(f).ExecuteAsync(command);
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
    }

    private static async Task<InventoryCommand> Command(Fixture f, int source, int destination, int bins, string? lot = null, string? signature = null)
    {
        await using var db = f.CreateDbContext();
        var p = Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(
            new(9001, [source]), new() { AllowIndependentCohorts = true }, DateTimeOffset.UtcNow)).Positions,
            p => p.AuthoritativeQuantity > 0 && (lot == null || p.Identity.Lot == lot));
        return new(Guid.NewGuid().ToString("N"), InventoryCommandKind.RoomMove, 8000, DateTimeOffset.UtcNow,
            "Synthetic scope-deterministic custody proof", [new(new(p.Identity, p.Location, p.Watermark.Fingerprint, p.Watermark.Versions),
                bins, signature ?? Assert.Single(p.TreatmentSlices).Signature, new(9001, destination))]);
    }
}
