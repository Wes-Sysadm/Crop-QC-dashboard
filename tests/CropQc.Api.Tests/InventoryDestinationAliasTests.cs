using System.Text.Json;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class InventoryDestinationAliasTests
{
    [Fact]
    public void Proven_normalization_includes_depleted_aliases_without_inventing_receipt_ancestry()
    {
        var evidence = InventoryEvidenceCorpus.Wp7();
        var depleted = evidence.Projections[0] with { Id = 999999, Quantity = 0 };
        evidence = evidence with { Projections = evidence.Projections.Add(depleted) };
        var result = InventoryAvailabilityResolver.Resolve(evidence, new());
        var plan = InventoryNormalizationPlanner.Plan(evidence, result)!;
        Assert.Null(plan.ReplacementReceiptId);
        var retired = Assert.Single(plan.Changes, x => x.Id == depleted.Id);
        Assert.Equal(0, retired.BeforeQuantity); Assert.Equal(0, retired.AfterQuantity);
        Assert.Equal("Historical", retired.AfterDisposition);
        Assert.Equal(depleted.RawKey, retired.RawIdentityKey);
        Assert.Equal(depleted.ReceiptId, retired.ReceiptId);
        Assert.Equal(depleted.ApplicationIds, retired.ApplicationIds);
        Assert.Equal(result.AuthoritativeQuantity, plan.ReplacementQuantity);
    }

    [InventoryPostgresFact]
    public async Task Concurrent_arrivals_into_depleted_alias_conserve_both_origins_and_replay_once()
    {
        await using var f = await Fixture.Create();
        await AddDepletedAlias(f);
        var first = ReceiptCommand(); var second = ReceiptCommand();
        var reached = 0;
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = new Observer(async (stage, attempt) =>
        {
            if (stage != "Normalized" || attempt != 1) return;
            if (Interlocked.Increment(ref reached) == 2) barrier.SetResult();
            await barrier.Task.WaitAsync(TimeSpan.FromSeconds(30));
        });
        var results = await Task.WhenAll(f.Execute(first, observer), f.Execute(second, observer));
        // Serializable contention may consume the executor's bounded retries
        // before the other request commits. RetryRequired is a client retry,
        // never permission to lose an arrival or claim it already committed.
        var commands = new[] { first, second };
        for (var index = 0; index < results.Length; index++)
        {
            if (results[index].Status != InventoryCommandStatus.RetryRequired) continue;
            Assert.Empty(results[index].Effects);
            await using var pending = f.CreateDbContext();
            Assert.False(await pending.InventoryCommands.AnyAsync(x => x.OperationKey == commands[index].OperationKey));
            results[index] = await f.Execute(commands[index]);
        }
        Assert.All(results, x => Assert.True(x.Status == InventoryCommandStatus.Committed, x.Detail));
        Assert.Equal(14, await f.Physical(9003)); Assert.Equal(33, await f.Physical() + await f.Physical(9003));
        var committed = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Replayed, (await f.Execute(first)).Status);
        Assert.Equal(committed, await f.Snapshot());
        await using var db = f.CreateDbContext();
        Assert.Equal(2, await db.Receipts.CountAsync(x => x.CompuTechReceiptId.StartsWith("LOCAL-NEW-")));
        Assert.Equal(2, await db.TreatmentLineageSegments.CountAsync(x => x.RoomId == 9003 && x.CurrentBins > 0));
    }

    [InventoryPostgresFact]
    public async Task Arrival_after_treatment_keeps_mixed_histories_through_repeated_partial_moves()
    {
        await using var f = await Fixture.Create();
        await using var db = f.CreateDbContext();
        var treatment = await f.Command(InventoryCommandKind.TreatmentAssignment, 19);
        treatment = treatment with { TreatmentChemicalId = await db.TreatmentChemicals.Where(x => x.IsActive && x.ApplicationLevel == "Room").Select(x => x.Id).FirstAsync() };
        var applied = await f.Execute(treatment);
        Assert.True(applied.Status == InventoryCommandStatus.Committed, applied.Detail);
        await InsertLegacyAlias(f, 9002);
        var applicationBefore = JsonSerializer.Serialize(await db.RoomTreatmentApplications.AsNoTracking().ToArrayAsync());
        var sourcesBefore = JsonSerializer.Serialize(await db.RoomTreatmentApplicationSources.AsNoTracking().ToArrayAsync());
        var arrival = ReceiptCommand(); arrival = arrival with { Receipt = arrival.Receipt! with { RoomId = 9002 } };
        var result = await f.Execute(arrival);
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
        var transfer = await f.Command(InventoryCommandKind.RoomMove, 7);
        var moved = await f.Execute(transfer);
        Assert.True(moved.Status == InventoryCommandStatus.Committed, moved.Detail);
        var back = await f.Command(InventoryCommandKind.RoomMove, 3, room: 9003);
        back = back with { Lines = [back.Lines[0] with { Destination = new(9001, 9002) }] };
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(back)).Status);
        Assert.Equal(22, await f.Physical()); Assert.Equal(4, await f.Physical(9003));
        Assert.Equal(19, await db.TreatmentLineageSegments.Where(x => x.Disposition == "Current" && x.TreatmentState == "Confirmed").SumAsync(x => x.CurrentBins));
        Assert.Equal(7, await db.TreatmentLineageSegments.Where(x => x.Disposition == "Current" && x.TreatmentState == "Untreated").SumAsync(x => x.CurrentBins));
        Assert.Equal(applicationBefore, JsonSerializer.Serialize(await db.RoomTreatmentApplications.AsNoTracking().ToArrayAsync()));
        Assert.Equal(sourcesBefore, JsonSerializer.Serialize(await db.RoomTreatmentApplicationSources.AsNoTracking().ToArrayAsync()));
    }

    [InventoryPostgresTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Depleted_destination_alias_cannot_veto_receiving_or_room_movement(bool receiving)
    {
        await using var f = await Fixture.Create();
        await AddDepletedAlias(f);
        await using var db = f.CreateDbContext();
        var beforeAlias = JsonSerializer.Serialize(await db.TreatmentLineageSegments.AsNoTracking().SingleAsync(x => x.Id == 200002));
        var command = receiving ? ReceiptCommand() : await f.Command(InventoryCommandKind.RoomMove, 7);
        var result = await f.Execute(command);
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
        Assert.Equal(7, await f.Physical(9003));
        Assert.Equal(receiving ? 19 : 12, await f.Physical());
        Assert.Equal(beforeAlias, JsonSerializer.Serialize(await db.TreatmentLineageSegments.AsNoTracking().SingleAsync(x => x.Id == 200002)));
        var incoming = Assert.Single(await db.TreatmentLineageSegments.AsNoTracking().Where(x => x.CohortKey == command.OperationKey).ToArrayAsync());
        Assert.Equal(7, incoming.CurrentBins);
        Assert.Equal("Untreated", incoming.TreatmentState);
        Assert.Empty(await db.TreatmentLineageSegmentApplications.Where(x => x.TreatmentLineageSegmentId == incoming.Id).ToArrayAsync());
        var committed = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Replayed, (await f.Execute(command)).Status);
        Assert.Equal(committed, await f.Snapshot());
        // The formerly empty room must remain usable as a source; no zombie alias
        // may veto normalization during a later partial move or reversal.
        var onward = await f.Command(InventoryCommandKind.RoomMove, 3, room: 9003);
        onward = onward with { Lines = [onward.Lines[0] with { Destination = new(9001, 9002) }] };
        var moved = await f.Execute(onward);
        Assert.True(moved.Status == InventoryCommandStatus.Committed, moved.Detail);
        Assert.Equal(4, await f.Physical(9003));
        Assert.Equal(receiving ? 26 : 19, await f.Physical() + await f.Physical(9003));
    }

    [InventoryCommandRestoreFact]
    public async Task Lamb15_Fuji_recorded_histories_accept_new_receipts_without_rewriting_depleted_projections()
    {
        await using var f = await CanonicalRestoreFixture.Clone();
        await using var db = f.CreateDbContext();
        var actor = await db.Users.Where(x => x.IsActive).Select(x => x.Id).FirstAsync();
        var lastHistoricalAudit = await db.AuditLogs.MaxAsync(x => x.Id);
        foreach (var (lot, segment) in new[] { (404, 591L), (519, 629L) })
        {
            var prior = await db.TreatmentLineageSegments.AsNoTracking().SingleAsync(x => x.Id == segment);
            Assert.Equal(8, prior.RoomId); Assert.Equal(1, prior.FruitProfileId);
            Assert.Equal(0, prior.CurrentBins); Assert.EndsWith("|CONVENTIONAL", prior.IdentityKey);
            var historical = await f.Snapshot(new Dictionary<string, string>
            {
                ["Receipts"] = "\"CompuTechReceiptId\" NOT LIKE 'LOCAL-LAMB15-%'",
                ["RoomInventoryAdjustments"] = "\"InventoryOperationKey\" IS NULL OR \"InventoryOperationKey\" NOT LIKE 'local-lamb15-%'",
                ["TreatmentLineageMovements"] = "\"OperationKey\" NOT LIKE 'local-lamb15-%'",
                ["TreatmentLineageSegments"] = "\"CohortKey\" NOT LIKE 'local-lamb15-%'",
                ["InventoryCommands"] = "\"OperationKey\" NOT LIKE 'local-lamb15-%'",
                ["AuditLogs"] = $"\"Id\" <= {lastHistoricalAudit}"
            });
            var c = new InventoryCommand($"local-lamb15-{lot}", InventoryCommandKind.ReceiveStock, actor, DateTimeOffset.UtcNow,
                "Disposable restored Lamb15 Fuji regression", [], Receipt: new(2026, 1, 8, lot, 1, $"LOCAL-LAMB15-{lot}", 7));
            var result = await f.Execute(c);
            Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
            Assert.Equal(JsonSerializer.Serialize(prior), JsonSerializer.Serialize(await db.TreatmentLineageSegments.AsNoTracking().SingleAsync(x => x.Id == segment)));
            var after = (await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(1, [8]), new(), DateTimeOffset.UtcNow))
                .Positions.Single(x => x.Identity.GrowerLotId == lot && x.Identity.FruitProfileId == 1);
            Assert.Equal(7, after.AuthoritativeQuantity);
            Assert.True(after.IsOperable, string.Join(';', after.Blockers));
            Assert.Equal(historical, await f.Snapshot(new Dictionary<string, string>
            {
                ["Receipts"] = "\"CompuTechReceiptId\" NOT LIKE 'LOCAL-LAMB15-%'",
                ["RoomInventoryAdjustments"] = "\"InventoryOperationKey\" IS NULL OR \"InventoryOperationKey\" NOT LIKE 'local-lamb15-%'",
                ["TreatmentLineageMovements"] = "\"OperationKey\" NOT LIKE 'local-lamb15-%'",
                ["TreatmentLineageSegments"] = "\"CohortKey\" NOT LIKE 'local-lamb15-%'",
                ["InventoryCommands"] = "\"OperationKey\" NOT LIKE 'local-lamb15-%'",
                ["AuditLogs"] = $"\"Id\" <= {lastHistoricalAudit}"
            }));
        }
    }

    internal static InventoryCommand ReceiptCommand() => new(Guid.NewGuid().ToString("N"), InventoryCommandKind.ReceiveStock,
        8000, DateTimeOffset.UtcNow, "Disposable incoming receipt", [], Receipt: new(2026, 9001, 9003, 100000, 9004, "LOCAL-NEW-" + Guid.NewGuid().ToString("N"), 7));

    internal static async Task AddDepletedAlias(Fixture f, int room = 9003)
    {
        // Recorded arrival and full departure keep this zero-stock position in
        // canonical evidence, as in Lamb15; a projection by itself is not stock.
        var arrival = await f.Command(InventoryCommandKind.RoomMove, 19);
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(arrival)).Status);
        var departure = await f.Command(InventoryCommandKind.RoomMove, 19, room: room);
        departure = departure with { Lines = [departure.Lines[0] with { Destination = new(9001, 9002) }] };
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(departure)).Status);
        await InsertLegacyAlias(f, room);
    }

    internal static async Task InsertLegacyAlias(Fixture f, int room)
    {
        // Fixture-only historical shape, after real canonical arrival/departure.
        // Never grant the application a bypass capability or touch a remote DB.
        CropQc.Data.ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(f.Connection);
        await using var connection = new NpgsqlConnection(f.Connection);
        await connection.OpenAsync();
        await using var insert = new NpgsqlCommand("""
            INSERT INTO "TreatmentLineageSegments"
            SELECT (jsonb_populate_record(NULL::"TreatmentLineageSegments", to_jsonb(s) ||
              jsonb_build_object('Id',200002,'RoomId',@room,'WarehouseId',(SELECT "WarehouseId" FROM "Rooms" WHERE "Id"=@room),'ReceiptId',NULL,'CurrentBins',0,
              'IdentityKey',rtrim(s."IdentityKey",'|') || '|CONVENTIONAL','InventoryStatusSnapshot','Conventional',
              'Disposition','Current','RetiredAt',NULL,'RetiredByCommandKey',NULL,'RetiredQuantity',NULL))).*
            FROM "TreatmentLineageSegments" s WHERE s."Id"=100000
            """, connection);
        insert.Parameters.AddWithValue("room", room);
        Assert.Equal(1, await insert.ExecuteNonQueryAsync());
    }
}
