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
    public async Task Broad_history_reuses_aggregate_without_losing_complete_position_proof()
    {
        await using var f = await BroadHistory();
        var before = await f.Snapshot();
        var queries = new Queries();
        await using var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>()
            .UseNpgsql(f.Connection).AddInterceptors(queries).Options);
        var rooms = await db.Rooms.Where(x => x.WarehouseId == 9001).Select(x => x.Id).ToArrayAsync();
        var at = DateTimeOffset.UtcNow;
        var loader = new InventoryEvidenceLoader(db);
        var broad = await loader.LoadAsync(new(null, rooms.ToImmutableArray()), at, default);
        Assert.Single(queries.Sql, x => x.Contains("GROUP BY") && x.Contains("StartingInventoryImport"));
        Assert.Equal(59, rooms.Length);
        // Warehouse-filtered reads deliberately cannot claim complete room coverage;
        // this independently executes the fresh historical aggregate as a reference.
        queries.Sql.Clear();
        var fresh = await loader.LoadAsync(new(9001, []), at, default);
        Assert.Equal(2, queries.Sql.Count(x => x.Contains("GROUP BY") && x.Contains("StartingInventoryImport")));
        static string Position(InventoryPositionEvidence p) => JsonSerializer.Serialize(p);
        Assert.Equal(fresh.Positions.OrderBy(p => p.Location.RoomId).ThenBy(p => p.Identity.Key).Select(Position),
            broad.Positions.OrderBy(p => p.Location.RoomId).ThenBy(p => p.Identity.Key).Select(Position));
        foreach (var room in rooms)
        {
            // The command reader uses this narrower scope, including depleted rooms.
            var single = await loader.LoadAsync(new(null, [room]), at, default);
            foreach (var expected in broad.Positions.Where(p => p.Location.RoomId == room))
            {
                var actual = Assert.Single(single.Positions, p => p.Identity.Key == expected.Identity.Key);
                Assert.Equal(expected.AuthoritativeQuantity, actual.AuthoritativeQuantity);
                Assert.Equal(JsonSerializer.Serialize(expected.Ledger), JsonSerializer.Serialize(actual.Ledger));
                Assert.Equal(Position(expected), Position(actual));
            }
        }
        var frenchman = Assert.Single(broad.Positions, p => p.Location.RoomId == 9003 && p.Identity.Lot == "1110");
        var replay = InventoryEventReplay.Replay(frenchman);
        Assert.True(replay.QuantityConserved);
        Assert.Equal(286, frenchman.AuthoritativeQuantity);
        Assert.Equal(220, replay.Cohorts.Where(c => c.ReceiptId == null).Sum(c => c.Quantity));
        Assert.Equal(66, replay.Cohorts.Where(c => c.ReceiptId == 100004).Sum(c => c.Quantity));
        foreach (var i in new[] { 1, 2 })
        {
            var moved = Assert.Single(broad.Positions, p => p.Location.RoomId == 9305 + i && p.Identity.Lot == $"BROAD-{i:D2}");
            var history = InventoryEventReplay.Replay(moved);
            Assert.True(history.QuantityConserved);
            Assert.Empty(history.UnresolvedEvents);
            Assert.Equal(26, moved.AuthoritativeQuantity);
            Assert.Equal(19, history.Cohorts.Where(c => c.ReceiptId == 700000 + i
                && c.Signature == (i == 1 ? "u|a:810001" : "u")).Sum(c => c.Quantity));
            Assert.Equal(7, history.Cohorts.Where(c => c.ReceiptId == 720000 + i
                && c.Signature == "u" && c.ApplicationIds.IsEmpty).Sum(c => c.Quantity));
        }
        Assert.Equal(before, await f.Snapshot());
        var fingerprint = frenchman.Watermark.Fingerprint;
        var neighbour = broad.Positions.First(p => p.AuthoritativeQuantity > 0 && p.Identity.Lot == "BROAD-30");
        foreach (var mutation in new[] { "receipt", "parent", "correction", "treatment" })
        {
            await using (var change = f.CreateDbContext())
            {
                if (mutation == "receipt") (await change.Receipts.SingleAsync(r => r.Id == 100000)).ConcurrencyVersion++;
                if (mutation == "parent") (await change.RoomTransfers.SingleAsync(t => t.Id == 100000)).BinCount--;
                if (mutation == "correction") (await change.ReceiptInventoryOverrides.SingleAsync()).IsComplete = false;
                if (mutation == "treatment") (await change.RoomTreatmentApplications.SingleAsync(a => a.Id == 800000)).ReversedAt = InventoryEvidenceCorpus.Start.AddDays(9);
                await change.SaveChangesAsync();
            }
            var changed = await loader.LoadAsync(new(null, rooms.ToImmutableArray()), at, default);
            var selected = Assert.Single(changed.Positions, p => p.Location == frenchman.Location && p.Identity.Key == frenchman.Identity.Key);
            Assert.NotEqual(fingerprint, selected.Watermark.Fingerprint);
            fingerprint = selected.Watermark.Fingerprint;
            Assert.Equal(neighbour.Watermark.Fingerprint, Assert.Single(changed.Positions,
                p => p.Location == neighbour.Location && p.Identity.Key == neighbour.Identity.Key).Watermark.Fingerprint);
            Assert.Equal(286, selected.AuthoritativeQuantity); // changed proof never rewrites the ledger
            var command = await loader.LoadAsync(new(9001, [9003]), at, default);
            Assert.Equal(Position(selected), Position(Assert.Single(command.Positions, p => p.Identity.Key == selected.Identity.Key)));
        }
    }

    [InventoryPostgresFact]
    public async Task Aggregate_reuse_is_transaction_and_effective_time_bound()
    {
        await using var f = await LegacyTransferEligibilityTests.Legacy();
        await using var db = f.CreateDbContext();
        var reader = new RoomInventoryLedgerQueryService(db);
        var capture = typeof(RoomInventoryLedgerQueryService).GetMethod("GetSnapshotsForHistoryAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var at = DateTimeOffset.UtcNow;
        await using (var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead))
        {
            await (Task)capture.Invoke(reader, [null, new[] { 9002, 9003 }, at, CancellationToken.None])!;
            var earlier = await Scoped(reader, [9002, 9003], ["CORPUS-100000"], InventoryEvidenceCorpus.Start.AddHours(12));
            Assert.Equal(19, Assert.Single(earlier).CurrentBins);
            Assert.Equal(9002, Assert.Single(earlier).RoomId);
        }
        // A reused service instance must never reuse groups from the closed read.
        (await db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 100002)).ChangeAmount = 18;
        await db.SaveChangesAsync();
        await using var next = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var changed = await Scoped(reader, [9002, 9003], ["CORPUS-100000"], at);
        Assert.Equal(18, Assert.Single(changed, x => x.RoomId == 9003).CurrentBins);
    }

    [InventoryPostgresFact]
    public async Task Reused_groups_preserve_provider_normalization_and_combined_row_limit()
    {
        await using var f = await LegacyTransferEligibilityTests.Legacy();
        await using var db = f.CreateDbContext();
        var at = DateTimeOffset.UtcNow;
        db.RoomInventoryAdjustments.Add(new()
        {
            Id = 950000,
            WarehouseId = 9001,
            RoomId = 9002,
            CropYear = 2026,
            GrowerLotId = 100000,
            FruitProfileId = 9004,
            GrowerName = "Synthetic boundary",
            LotNumber = "\tCORPUS-100000\t",
            VarietyCode = "CGAL",
            ChangeAmount = 1,
            AdjustmentType = "ManualTrueUp",
            AdjustmentAt = InventoryEvidenceCorpus.Start,
            CreatedAt = InventoryEvidenceCorpus.Start
        });
        for (var i = 0; i < RoomInventoryLedgerQueryService.MaximumRoomLotRows - 1; i++)
            db.RoomInventoryAdjustments.Add(new()
            {
                Id = 950001 + i,
                WarehouseId = 9001,
                RoomId = 9003,
                CropYear = 2026,
                GrowerLotId = 100000,
                FruitProfileId = 9004,
                GrowerName = "Synthetic boundary",
                LotNumber = "CORPUS-100000",
                VarietyCode = $"BOUND-{i}",
                ChangeAmount = 1,
                AdjustmentType = "ManualTrueUp",
                AdjustmentAt = InventoryEvidenceCorpus.Start,
                CreatedAt = InventoryEvidenceCorpus.Start
            });
        await db.SaveChangesAsync();
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var reader = new RoomInventoryLedgerQueryService(db);
        var capture = typeof(RoomInventoryLedgerQueryService).GetMethod("GetSnapshotsForHistoryAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        await (Task)capture.Invoke(reader, [null, new[] { 9002 }, at, CancellationToken.None])!;
        var reused = await Scoped(reader, [9002], ["CORPUS-100000"], at);
        var fresh = await Scoped(new RoomInventoryLedgerQueryService(db), [9002], ["CORPUS-100000"], at);
        Assert.Equal(JsonSerializer.Serialize(fresh), JsonSerializer.Serialize(reused));
        Assert.Equal(1, Assert.Single(reused).CurrentBins); // preserve the provider's translated whitespace handling
        Assert.Equal(2000, (await Scoped(new RoomInventoryLedgerQueryService(db), [9003], ["CORPUS-100000"], at)).Count);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Scoped(reader, [9002, 9003], ["CORPUS-100000"], at));
        Assert.Contains("safe limit of 2000", error.Message); // combined, never per-query truncation
    }

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
            [new InventoryScope(null, [9002, 9003]), DateTimeOffset.UtcNow, "RepeatableRead", CancellationToken.None, false, null, null, null])!;
        var target = Assert.Single(full.Positions, x => x.Location.RoomId == 9002 && x.Identity.Lot == "CORPUS-100000");
        Assert.False(Assert.Single(target.Receipts).OriginalIdentityVerified);
        var scoped = await (Task<InventoryEvidenceBatch>)method.Invoke(loader,
            [new InventoryScope(null, [9002, 9003]), DateTimeOffset.UtcNow, "RepeatableRead", CancellationToken.None, false,
                new HashSet<string> { target.Identity.Key }, new[] { target.Identity.Lot }, null])!;
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
                    scoped ? new HashSet<string> { key } : null, scoped ? new[] { "CORPUS-100000" } : null, null])!;
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

    // 59 rooms, 42 independent historical identities plus one connected run alias.
    // Independent chains share occupied rooms; room co-location is not provenance.
    internal static async Task<Fixture> BroadHistory()
    {
        var f = await WideHistory();
        await using var db = f.CreateDbContext();
        var start = InventoryEvidenceCorpus.Start;
        for (var i = 0; i < 34; i++)
            db.Rooms.Add(new() { Id = 9300 + i, WarehouseId = 9001, Code = $"BROAD-{i}", Name = $"Broad {i}" });
        var chemical = await db.TreatmentChemicals.FirstAsync(x => x.IsActive && x.ApplicationLevel == "Room");
        for (var i = 0; i < 41; i++)
        {
            var grower = 700000 + i;
            var lot = $"BROAD-{i:D2}";
            var path = Enumerable.Range(0, 5).SelectMany(hop => new[]
                { 9300 + (i + hop) % 34, 9200 + (i + hop) % 19 }).Append(9300 + (i + 5) % 34).ToArray();
            db.GrowerLots.Add(new() { Id = grower, Grower = "Similar grower", LotNumber = lot });
            AddReceipt(grower, path[0], grower, lot, 19, start);
            if (i is 1 or 2)
            {
                var application = new RoomTreatmentApplication
                {
                    Id = 810000 + i,
                    OperationKey = $"broad-treatment-{i}",
                    TreatmentChemicalId = chemical.Id,
                    WarehouseId = 9001,
                    RoomId = path[0],
                    AppliedByUserId = 8000,
                    CreatedByUserId = 8000,
                    TotalBinsSnapshot = 19,
                    ProductNameSnapshot = chemical.ProductName,
                    CropSnapshot = chemical.Crop,
                    UnitSnapshot = chemical.Unit,
                    CurrencySnapshot = chemical.Currency,
                    AppliedAt = start.AddHours(1),
                    CreatedAt = start.AddHours(1),
                    ReversedAt = i == 2 ? start.AddHours(36) : null
                };
                application.Sources.Add(new()
                {
                    ReceiptId = grower,
                    CropYear = 2026,
                    GrowerLotId = grower,
                    FruitProfileId = 9004,
                    IdentityKey = new InventoryIdentity(2026, grower, 9004, lot, lot, "CGAL", "Conventional", false, "").Key,
                    GrowerNameSnapshot = "Similar grower",
                    LotNumberSnapshot = lot,
                    VarietyCodeSnapshot = "CGAL",
                    ProductionTypeSnapshot = "Conventional",
                    IsOrganicSnapshot = false,
                    BinsTreated = 19,
                    PriorTreatmentSignature = "u",
                    ResultTreatmentSignature = $"u|a:{810000 + i}"
                });
                db.RoomTreatmentApplications.Add(application);
            }
            for (var hop = 0; hop + 1 < path.Length; hop++)
            {
                var id = 700000 + i * 10 + hop;
                var at = start.AddDays(hop + 1);
                db.RoomTransfers.Add(new()
                {
                    Id = id,
                    OperationKey = $"broad-{i}-{hop}",
                    SourceWarehouseId = 9001,
                    DestinationWarehouseId = 9001,
                    SourceRoomId = path[hop],
                    DestinationRoomId = path[hop + 1],
                    CropYear = 2026,
                    GrowerLotId = grower,
                    FruitProfileId = 9004,
                    GrowerName = "Similar grower",
                    LotNumber = lot,
                    VarietyCode = "CGAL",
                    BinCount = 19,
                    Reason = "Synthetic broad history",
                    TransferredAt = at,
                    CreatedAt = at
                });
                foreach (var (room, quantity, side) in new[] { (path[hop], -19, 0), (path[hop + 1], 19, 1) })
                    db.RoomInventoryAdjustments.Add(new()
                    {
                        Id = 710000 + i * 30 + hop * 2 + side,
                        WarehouseId = 9001,
                        RoomId = room,
                        CropYear = 2026,
                        GrowerLotId = grower,
                        FruitProfileId = 9004,
                        GrowerName = "Similar grower",
                        LotNumber = lot,
                        VarietyCode = "CGAL",
                        ChangeAmount = quantity,
                        NewBinCount = Math.Max(0, quantity),
                        AdjustmentType = side == 0 ? "TransferOut" : "TransferIn",
                        RoomTransferId = id,
                        AdjustmentAt = at,
                        CreatedAt = at
                    });
            }
            // Later arrivals do not inherit the source's point-in-time treatment.
            AddReceipt(720000 + i, path[^1], grower, lot, 7, start.AddDays(12));
        }
        // Active neighbours make every historical room part of a broad selector.
        for (var i = 0; i < 34; i++)
            AddReceipt(730000 + i, 9300 + i, 700000, "BROAD-00", 2, start.AddDays(13));
        // A connected old label is depleted; a different grower with the same
        // display label remains a distinct, unproven origin rather than new stock.
        AddReceipt(740000, 9300, 700000, "BROAD-00", 3, start.AddDays(14));
        db.RoomInventoryAdjustments.Add(new()
        {
            Id = 740001,
            WarehouseId = 9001,
            RoomId = 9300,
            CropYear = null,
            GrowerLotId = null,
            FruitProfileId = 9004,
            GrowerName = "Similar grower",
            LotNumber = "OLD BROAD DISPLAY",
            VarietyCode = "CGAL",
            ChangeAmount = -3,
            AdjustmentType = "BinsRun",
            AdjustmentAt = start.AddDays(15),
            CreatedAt = start.AddDays(15)
        });
        db.BinsRunEntries.Add(new()
        {
            Id = 740001,
            SourceInventoryAdjustmentId = 740000,
            InventoryAdjustmentId = 740001,
            WarehouseId = 9001,
            RoomId = 9300,
            CropYear = 2026,
            GrowerLotId = 700000,
            FruitProfileId = 9004,
            GrowerName = "Similar grower",
            LotNumber = "OLD BROAD DISPLAY",
            BinsRun = 3,
            PreviousAvailableBins = 3,
            RunAt = start.AddDays(15),
            CreatedAt = start.AddDays(15)
        });
        AddReceipt(740002, 9300, 700001, "BROAD-00", 5, start.AddDays(16));
        // The correlated canonical origin lookups also depend on receipt-history
        // density, not just the number of selected room/lot groups. Keep the same
        // neighbours and group count while reproducing the expensive broad plan.
        var neighbours = (await db.Receipts.AsNoTracking().Where(r => r.Id >= 200000 && r.Id < 202000)
            .ToArrayAsync()).DistinctBy(r => r.GrowerLotId).OrderBy(r => r.Id).ToArray();
        for (var i = 0; i < neighbours.Length; i++)
            for (var j = 0; j < 8; j++)
                AddReceipt(750000 + i * 8 + j, neighbours[i].RoomId, neighbours[i].GrowerLotId!.Value,
                    neighbours[i].LotCode, 10, start);
        await db.SaveChangesAsync();
        return f;

        void AddReceipt(long id, int room, int grower, string lot, int bins, DateTimeOffset at)
        {
            db.Receipts.Add(new()
            {
                Id = id,
                CompuTechReceiptId = $"SYNTHETIC-{id}",
                WarehouseId = 9001,
                RoomId = room,
                CropYear = 2026,
                GrowerLotId = grower,
                FruitProfileId = 9004,
                GrowerName = "Similar grower",
                GrowerNumber = lot,
                LotCode = lot,
                BinCount = bins,
                ReceivedAt = at,
                CreatedAt = at,
                UpdatedAt = at
            });
            db.RoomInventoryAdjustments.Add(new()
            {
                Id = id,
                ReceiptId = id,
                WarehouseId = 9001,
                RoomId = room,
                CropYear = 2026,
                GrowerLotId = grower,
                FruitProfileId = 9004,
                GrowerName = "Similar grower",
                LotNumber = lot,
                VarietyCode = "CGAL",
                ChangeAmount = bins,
                NewBinCount = bins,
                AdjustmentType = "ReceiptAdd",
                AdjustmentAt = at,
                CreatedAt = at
            });
        }
    }

    private sealed class Queries : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data,
            InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        { Sql.Add(command.CommandText); return ValueTask.FromResult(result); }
    }
}
