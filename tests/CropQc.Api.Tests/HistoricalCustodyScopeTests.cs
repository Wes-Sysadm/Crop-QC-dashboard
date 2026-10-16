using System.Collections.Immutable;
using System.Data.Common;
using System.Reflection;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class HistoricalCustodyScopeTests
{
    [InventoryPostgresFact]
    public async Task Scope_retains_original_receipt_revision_when_another_identity_triggers_its_validation()
    {
        await using var f = await LegacyTransferEligibilityTests.Legacy();
        await using var db = f.CreateDbContext();
        var at = InventoryEvidenceCorpus.Start;
        db.RoomInventoryAdjustments.Add(new()
        {
            Id = 600000,
            ReceiptId = 100000,
            WarehouseId = 9001,
            RoomId = 9002,
            CropYear = 2026,
            GrowerLotId = null,
            FruitProfileId = 9004,
            GrowerName = "Historical identity",
            LotNumber = "OTHER",
            VarietyCode = "CGAL",
            ChangeAmount = 19,
            NewBinCount = 19,
            AdjustmentType = "ReceiptAdd",
            AdjustmentAt = at,
            CreatedAt = at
        });
        db.ReceiptInventoryOverrides.Add(new()
        {
            Id = Guid.NewGuid(),
            OperationKey = Guid.NewGuid().ToString(),
            ReceiptId = 100000,
            ActionType = "InventoryReclassification",
            OldReceiptBinCount = 19,
            NewReceiptBinCount = 19,
            AdministratorUserId = 8000,
            Reason = "Contradictory original identity is not proof",
            CreatedAt = at.AddHours(1),
            IsComplete = true,
            BeforeReceiptSnapshotJson =
                "{\"id\":100000,\"binCount\":19,\"cropYear\":2026,\"growerLotId\":999999,\"fruitProfileId\":9004}",
            AfterReceiptSnapshotJson = "{}",
            AffectedInventorySnapshotJson = "[]"
        });
        await db.SaveChangesAsync();
        var loader = new InventoryEvidenceLoader(db);
        var method = typeof(InventoryEvidenceLoader).GetMethod("LoadRoomsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var full = await (Task<InventoryEvidenceBatch>)method.Invoke(loader,
            [new InventoryScope(null, [9002, 9003]), DateTimeOffset.UtcNow, "RepeatableRead", CancellationToken.None, false, null, null])!;
        var target = Assert.Single(full.Positions, x => x.Location.RoomId == 9002 && x.Identity.Lot == "CORPUS-100000");
        Assert.False(Assert.Single(target.Receipts).OriginalIdentityVerified);
        var scoped = await (Task<InventoryEvidenceBatch>)method.Invoke(loader,
            [new InventoryScope(null, [9002, 9003]), DateTimeOffset.UtcNow, "RepeatableRead", CancellationToken.None, false,
                new HashSet<string> { target.Identity.Key }, new[] { target.Identity.Lot }])!;
        Assert.Equal(JsonSerializer.Serialize(target), JsonSerializer.Serialize(Assert.Single(scoped.Positions,
            x => x.Location.RoomId == 9002 && x.Identity.Key == target.Identity.Key)));
    }

    [InventoryPostgresFact]
    public async Task Wide_history_preserves_proof_across_scopes_without_aggregating_other_lots()
    {
        await using var f = await WideHistory();
        var before = await f.Snapshot();
        var queries = new Queries();
        await using var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>()
            .UseNpgsql(f.Connection).AddInterceptors(queries).Options);
        var loader = new InventoryEvidenceLoader(db);
        var at = DateTimeOffset.UtcNow;
        var single = await loader.LoadAsync(new(null, [9003]), at, default);
        var target = Assert.Single(single.Positions, x => x.Identity.Lot == "1110");
        Assert.Equal(286, target.AuthoritativeQuantity);
        var replay = InventoryEventReplay.Replay(target);
        Assert.True(replay.QuantityConserved);
        Assert.Empty(replay.UnresolvedEvents);
        Assert.Equal(220, replay.Cohorts.Where(x => x.ReceiptId == null).Sum(x => x.Quantity));
        Assert.Equal(66, replay.Cohorts.Where(x => x.ReceiptId == 100004).Sum(x => x.Quantity));
        Assert.All(replay.Cohorts, x => Assert.Equal(800000, Assert.Single(x.ApplicationIds)));
        var aggregates = queries.Sql.Where(x => x.Contains("GROUP BY") && x.Contains("StartingInventoryImport")).ToArray();
        Assert.Equal(2, aggregates.Length);
        Assert.Contains("normalizedLots", aggregates[1]); // bound BEFORE the expensive aggregate
        Assert.DoesNotContain("normalizedLots", aggregates[0]); // ordinary canonical read unchanged
        Assert.True(single.RowsLoaded < 1500, $"Unexpected full closure: {single.RowsLoaded} evidence rows");
        var warehouse = await loader.LoadAsync(new(9001, []), at, default);
        var rooms = await db.Rooms.Where(x => x.WarehouseId == 9001).Select(x => x.Id).ToArrayAsync();
        var broad = await loader.LoadAsync(new(null, rooms.ToImmutableArray()), at, default);
        foreach (var batch in new[] { warehouse, broad })
            Assert.Equal(JsonSerializer.Serialize(target), JsonSerializer.Serialize(Assert.Single(batch.Positions,
                x => x.Location.RoomId == 9003 && x.Identity.Key == target.Identity.Key)));
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Equal(before, await f.Snapshot());

        // A source-room neighbour is not part of this proof or fingerprint.
        await using (var change = f.CreateDbContext())
        {
            var other = await change.Receipts.FirstAsync(x => x.Id >= 200000 && x.RoomId == 9200);
            other.ConcurrencyVersion++;
            other.UpdatedAt = DateTimeOffset.UtcNow;
            await change.SaveChangesAsync();
        }
        var unchanged = await loader.LoadAsync(new(null, [9003]), DateTimeOffset.UtcNow, default);
        Assert.Equal(target.Watermark.Fingerprint, unchanged.Positions.Single(x => x.Identity.Key == target.Identity.Key).Watermark.Fingerprint);
        await using (var change = f.CreateDbContext())
        {
            var origin = await change.Receipts.SingleAsync(x => x.Id == 100000);
            origin.ConcurrencyVersion++;
            origin.UpdatedAt = DateTimeOffset.UtcNow;
            await change.SaveChangesAsync();
        }
        var stale = await new InventoryAvailabilityResolver(loader).ResolveAsync(new(null, [9003]),
            new(ExpectedFingerprint: target.Watermark.Fingerprint), DateTimeOffset.UtcNow);
        Assert.Contains(stale.Positions.Single(x => x.Identity.Key == target.Identity.Key).Blockers, x => x.Code == InventoryBlockerCode.StaleRead);
    }

    [InventoryPostgresFact]
    public async Task Scoped_aggregate_retains_room_baseline_and_nullable_identity_semantics()
    {
        await using var f = await HistoricalInterCrewDispatchTests.HistoricalPool();
        await using var db = f.CreateDbContext();
        var origin = await db.RoomInventoryAdjustments.SingleAsync(x => x.ReceiptId == 100000);
        origin.CropYear = null; origin.GrowerLotId = null; origin.FruitProfileId = null;
        origin.LotNumber = ""; // canonical receipt fallback, not raw-row filtering
        var row = new RoomInventoryAdjustment
        {
            Id = 600000,
            WarehouseId = 9001,
            RoomId = 9100,
            CropYear = 2026,
            GrowerName = "Unrelated baseline",
            LotNumber = "OTHER",
            VarietyCode = "CGAL",
            FruitProfileId = 9004,
            AdjustmentType = InventoryLedgerKinds.StartingInventoryImport,
            NewBinCount = 7,
            AdjustmentAt = InventoryEvidenceCorpus.Start.AddHours(1),
            CreatedAt = InventoryEvidenceCorpus.Start.AddHours(1)
        };
        db.Add(row);
        await db.SaveChangesAsync();
        var reader = new RoomInventoryLedgerQueryService(db);
        var at = DateTimeOffset.UtcNow;
        var full = await reader.GetSnapshotsAsOfAsync(null, [9100], at, default);
        var scoped = await Scoped(reader, [9100], ["1110"], at);
        Assert.Equal(JsonSerializer.Serialize(full.Where(x => x.Lot.Trim().ToUpperInvariant() == "1110")), JsonSerializer.Serialize(scoped));
        Assert.Equal(-32, Assert.Single(scoped).CurrentBins); // baseline of another lot still supersedes the receipt
        var resolved = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(null, [9003]), new(), at);
        Assert.NotEmpty(Assert.Single(resolved.Positions).Blockers);
    }

    internal static async Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> Scoped(RoomInventoryLedgerQueryService reader,
        int[] rooms, string[] lots, DateTimeOffset at) => await (Task<IReadOnlyList<RoomInventoryLedgerSnapshot>>)
        typeof(RoomInventoryLedgerQueryService).GetMethod("GetHistorySnapshotsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(reader, [rooms, lots, at, CancellationToken.None])!;

    [InventoryPostgresFact]
    public async Task Connected_run_labels_cannot_resurrect_depleted_inventory_or_merge_a_different_grower()
    {
        await using var f = await Fixture.Create();
        await using var db = f.CreateDbContext();
        var start = InventoryEvidenceCorpus.Start;
        db.RoomInventoryAdjustments.Add(new()
        {
            Id = 600000,
            WarehouseId = 9001,
            RoomId = 9002,
            CropYear = 2026,
            GrowerLotId = 100000,
            FruitProfileId = 9004,
            GrowerName = "Corpus",
            LotNumber = "OLD DISPLAY",
            VarietyCode = "CGAL",
            ChangeAmount = -19,
            NewBinCount = 0,
            AdjustmentType = "BinsRun",
            AdjustmentAt = start.AddDays(1),
            CreatedAt = start.AddDays(1)
        });
        db.BinsRunEntries.Add(new()
        {
            Id = 600000,
            SourceInventoryAdjustmentId = 100000,
            InventoryAdjustmentId = 600000,
            WarehouseId = 9001,
            RoomId = 9002,
            CropYear = 2026,
            GrowerLotId = 100000,
            FruitProfileId = 9004,
            GrowerName = "Corpus",
            LotNumber = "OLD DISPLAY",
            BinsRun = 19,
            PreviousAvailableBins = 19,
            RunAt = start.AddDays(1),
            CreatedAt = start.AddDays(1)
        });
        db.GrowerLots.Add(new() { Id = 600001, Grower = "Different grower", LotNumber = "CORPUS-100000" });
        db.RoomInventoryAdjustments.Add(new()
        {
            Id = 600001,
            WarehouseId = 9001,
            RoomId = 9002,
            CropYear = 2026,
            GrowerLotId = 600001,
            FruitProfileId = 9004,
            GrowerName = "Different grower",
            LotNumber = "CORPUS-100000",
            VarietyCode = "CGAL",
            ChangeAmount = 5,
            NewBinCount = 5,
            AdjustmentType = "ManualTrueUp",
            AdjustmentAt = start,
            CreatedAt = start
        });
        await db.SaveChangesAsync();
        var at = DateTimeOffset.UtcNow;
        var reader = new RoomInventoryLedgerQueryService(db);
        var full = await reader.GetSnapshotsAsOfAsync(null, [9002], at, default);
        var scoped = await Scoped(reader, [9002], ["CORPUS-100000"], at);
        Assert.Equal(JsonSerializer.Serialize(full), JsonSerializer.Serialize(scoped));
        Assert.Equal(0, Assert.Single(scoped, x => x.GrowerLotId == 100000).CurrentBins);
        Assert.Equal(5, Assert.Single(scoped, x => x.GrowerLotId == 600001).CurrentBins);
    }

    [InventoryPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Multihop_treatment_and_intermediate_reversal_never_treat_later_arrivals(bool reverse)
    {
        await using var f = await LegacyTransferEligibilityTests.Legacy(treated: true);
        await using var db = f.CreateDbContext();
        var start = InventoryEvidenceCorpus.Start;
        var lot = "CORPUS-100000";
        db.Rooms.Add(new() { Id = 9008, WarehouseId = 9001, Code = "LAST", Name = "Last historical hop" });
        var application = await db.RoomTreatmentApplications.SingleAsync();
        if (reverse) application.ReversedAt = start.AddHours(36);
        db.RoomTransfers.Add(new()
        {
            Id = 600000,
            OperationKey = "last-hop",
            SourceWarehouseId = 9001,
            SourceRoomId = 9003,
            DestinationWarehouseId = 9001,
            DestinationRoomId = 9008,
            CropYear = 2026,
            GrowerLotId = 100000,
            FruitProfileId = 9004,
            GrowerName = "Corpus",
            LotNumber = lot,
            VarietyCode = "CGAL",
            BinCount = 19,
            Reason = "Recorded hop",
            TransferredAt = start.AddDays(2),
            CreatedAt = start.AddDays(2)
        });
        foreach (var (id, room, quantity, kind) in new[] { (600000, 9003, -19, "TransferOut"), (600001, 9008, 19, "TransferIn"), (600002, 9008, 7, "ReceiptAdd") })
            db.RoomInventoryAdjustments.Add(new()
            {
                Id = id,
                WarehouseId = 9001,
                RoomId = room,
                CropYear = 2026,
                GrowerLotId = 100000,
                FruitProfileId = 9004,
                GrowerName = "Corpus",
                LotNumber = lot,
                VarietyCode = "CGAL",
                ChangeAmount = quantity,
                NewBinCount = Math.Max(0, quantity),
                AdjustmentType = kind,
                RoomTransferId = id == 600002 ? null : 600000,
                ReceiptId = id == 600002 ? 600002 : null,
                AdjustmentAt = start.AddDays(id == 600002 ? 3 : 2),
                CreatedAt = start.AddDays(id == 600002 ? 3 : 2)
            });
        db.Receipts.Add(new()
        {
            Id = 600002,
            CompuTechReceiptId = "LATER",
            WarehouseId = 9001,
            RoomId = 9008,
            CropYear = 2026,
            GrowerLotId = 100000,
            FruitProfileId = 9004,
            GrowerName = "Corpus",
            GrowerNumber = lot,
            LotCode = lot,
            BinCount = 7,
            ReceivedAt = start.AddDays(3),
            CreatedAt = start.AddDays(3),
            UpdatedAt = start.AddDays(3)
        });
        await db.SaveChangesAsync();
        var before = await f.Snapshot();
        var evidence = Assert.Single((await new InventoryEvidenceLoader(db).LoadAsync(new(null, [9008]), DateTimeOffset.UtcNow, default)).Positions);
        var replay = InventoryEventReplay.Replay(evidence);
        Assert.True(replay.QuantityConserved);
        Assert.Empty(replay.UnresolvedEvents);
        Assert.Equal(26, replay.Cohorts.Sum(x => x.Quantity));
        Assert.Equal(7, replay.Cohorts.Where(x => x.ReceiptId == 600002 && x.Signature == "u" && x.ApplicationIds.IsEmpty).Sum(x => x.Quantity));
        Assert.Equal(19, replay.Cohorts.Where(x => x.ReceiptId == 100000 && x.Signature == (reverse ? "u" : "u|a:800000")).Sum(x => x.Quantity));
        Assert.Equal(before, await f.Snapshot());
    }

    [InventoryPostgresFact]
    public async Task Scope_filter_keeps_aliases_contradictory_segment_links_and_historical_cutoffs()
    {
        await using var f = await LegacyTransferEligibilityTests.Legacy(treated: true);
        await using var db = f.CreateDbContext();
        var segment = await db.TreatmentLineageSegments.SingleAsync();
        var key = segment.IdentityKey;
        segment.IdentityKey = string.Join(" | ", key.Split('|')).ToLowerInvariant() + " conventional ";
        db.TreatmentLineageMovements.Add(new()
        {
            Id = 600000,
            OperationKey = "contradictory-link",
            MovementType = "RoomTransfer",
            SourceRoomId = 9002,
            DestinationRoomId = 9003,
            SourceSegmentId = segment.Id,
            IdentityKey = "contradictory-key",
            TreatmentStateSnapshot = "Untreated",
            TreatmentSignatureSnapshot = "u",
            BinCount = 18,
            RoomTransferId = 100000,
            OccurredAt = InventoryEvidenceCorpus.Start.AddDays(1),
            CreatedAt = InventoryEvidenceCorpus.Start.AddDays(1)
        });
        await db.SaveChangesAsync();
        await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var loader = new InventoryEvidenceLoader(db);
        var method = typeof(InventoryEvidenceLoader).GetMethod("LoadRoomsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var at in new[] { InventoryEvidenceCorpus.Start.AddHours(12), DateTimeOffset.UtcNow })
        {
            async Task<InventoryEvidenceBatch> Read(bool scoped) => await (Task<InventoryEvidenceBatch>)method.Invoke(loader,
                [new InventoryScope(null, [9002, 9003]), at, "RepeatableRead", CancellationToken.None, false,
                    scoped ? new HashSet<string> { key } : null, scoped ? new[] { "CORPUS-100000" } : null])!;
            var full = await Read(false);
            var narrowed = await Read(true);
            foreach (var original in full.Positions.Where(x => x.Identity.Key == key))
            {
                var actual = Assert.Single(narrowed.Positions, x => x.Location.RoomId == original.Location.RoomId && x.Identity.Key == key);
                Assert.Equal(JsonSerializer.Serialize(original), JsonSerializer.Serialize(actual));
                if (original.Location.RoomId == 9002) Assert.Contains(actual.Movements, x => x.Id == 600000 && !x.ExactIdentity);
            }
        }
    }

    // Synthetic only: the original 286-bin history plus nineteen depleted hops
    // and 500 unrelated identities / 2,000 receipts occupying those same rooms.
    internal static async Task<Fixture> WideHistory()
    {
        var f = await HistoricalInterCrewDispatchTests.HistoricalPool();
        await using var db = f.CreateDbContext();
        var start = InventoryEvidenceCorpus.Start;
        for (var i = 0; i < 19; i++) db.Rooms.Add(new() { Id = 9200 + i, WarehouseId = 9001, Code = $"HISTORY-{i}", Name = $"History {i}" });
        var path = new[] { 9100 }.Concat(Enumerable.Range(9200, 19)).Append(9100).ToArray();
        for (var i = 0; i + 1 < path.Length; i++)
        {
            var at = start.AddMinutes(i + 1);
            var parent = new RoomTransfer
            {
                Id = 400000 + i,
                OperationKey = $"wide-hop-{i}",
                SourceWarehouseId = 9001,
                DestinationWarehouseId = 9001,
                SourceRoomId = path[i],
                DestinationRoomId = path[i + 1],
                CropYear = 2026,
                GrowerLotId = 100000,
                FruitProfileId = 9004,
                GrowerName = "Frenchman",
                LotNumber = "1110",
                VarietyCode = "CGAL",
                BinCount = 32,
                Reason = "Synthetic historical hop",
                TransferredAt = at,
                CreatedAt = at
            };
            db.Add(parent);
            foreach (var (room, qty, side) in new[] { (path[i], -32, 0), (path[i + 1], 32, 1) }) db.RoomInventoryAdjustments.Add(new()
            {
                Id = 400000 + i * 2 + side,
                WarehouseId = 9001,
                RoomId = room,
                CropYear = 2026,
                GrowerLotId = 100000,
                FruitProfileId = 9004,
                GrowerName = "Frenchman",
                LotNumber = side == 0 ? "1110" : " 1110 ",
                VarietyCode = "CGAL",
                InventoryStatus = side == 0 ? "" : "Conventional",
                ChangeAmount = qty,
                NewBinCount = Math.Max(0, qty),
                AdjustmentType = side == 0 ? "TransferOut" : "TransferIn",
                RoomTransferId = parent.Id,
                AdjustmentAt = at,
                CreatedAt = at.AddMilliseconds(side)
            });
        }
        var rooms = new[] { 9002, 9003, 9100, 9101, 9102, 9103 }.Concat(Enumerable.Range(9200, 19)).ToArray();
        var index = 0;
        foreach (var room in rooms)
            for (var n = 0; n < 20; n++)
            {
                var grower = 200000 + index++;
                var lot = $"NEIGHBOUR-{grower}";
                db.GrowerLots.Add(new() { Id = grower, Grower = "Synthetic neighbour", LotNumber = lot });
                for (var j = 0; j < 4; j++)
                {
                    var id = 200000 + (index - 1) * 4 + j;
                    db.Receipts.Add(new()
                    {
                        Id = id,
                        CropYear = 2026,
                        CompuTechReceiptId = $"TEST-{id}",
                        WarehouseId = 9001,
                        RoomId = room,
                        GrowerLotId = grower,
                        FruitProfileId = 9004,
                        GrowerName = "Synthetic neighbour",
                        GrowerNumber = lot,
                        LotCode = lot,
                        BinCount = 10,
                        ReceivedAt = start,
                        CreatedAt = start,
                        UpdatedAt = start
                    });
                    db.RoomInventoryAdjustments.Add(new()
                    {
                        Id = id,
                        CropYear = 2026,
                        ReceiptId = id,
                        WarehouseId = 9001,
                        RoomId = room,
                        GrowerLotId = grower,
                        FruitProfileId = 9004,
                        GrowerName = "Synthetic neighbour",
                        LotNumber = lot,
                        VarietyCode = "CGAL",
                        ChangeAmount = 10,
                        NewBinCount = 10,
                        AdjustmentType = "ReceiptAdd",
                        AdjustmentAt = start,
                        CreatedAt = start
                    });
                }
            }
        await db.SaveChangesAsync();
        return f;
    }

    private sealed class Queries : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { Sql.Add(command.CommandText); return ValueTask.FromResult(result); }
    }
}
