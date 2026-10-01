using System.Collections.Immutable;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class InventoryCommandRestoreTests
{
    [InventoryCommandRestoreFact]
    public async Task Verified_fresh_restore_executes_68_plus_104_with_atomic_rollback_and_protected_history()
    {
        var original = Environment.GetEnvironmentVariable("CANONICAL_INVENTORY_RESTORE_POSTGRES")!;
        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(original);
        var template = new NpgsqlConnectionStringBuilder(original);
        Assert.True(template.Host is "localhost" or "127.0.0.1", "Restore rehearsal is local only.");
        await using var f = await CanonicalRestoreFixture.Clone();
        await using var db = f.CreateDbContext();
        var batch = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(null, [1, 4]), new(), DateTimeOffset.UtcNow);
        var wp4 = Assert.Single(batch.Positions.Where(x => x.Location.RoomId == 1 && x.Identity.Lot == "1372" && x.Identity.FruitProfileId == 17));
        var wp7 = Assert.Single(batch.Positions.Where(x => x.Location.RoomId == 4 && x.Identity.Lot == "1372" && x.Identity.FruitProfileId == 17));
        Assert.Equal(68, wp4.AuthoritativeQuantity); Assert.Equal(1122, wp7.AuthoritativeQuantity); Assert.Equal(1568, wp7.RawProjectionQuantity);
        Assert.True(wp4.IsOperable && wp7.IsOperable);
        var actor = await db.Users.Where(x => x.IsActive).Select(x => x.Id).FirstAsync();
        var knownRooms = (await db.RoomInventoryAdjustments.Where(x => x.LotNumber == "1372" || x.LotNumber == "9682" || x.LotNumber == "9722")
            .Select(x => x.RoomId).Distinct().ToListAsync()).ToImmutableArray();
        var known = (await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(null, knownRooms), new(), DateTimeOffset.UtcNow))
            .Positions.Where(x => x.Identity.Lot is "1372" or "9682" or "9722").Select(x => new
            { x.Identity.Lot, x.Location, x.AuthoritativeQuantity, x.AvailableQuantity, x.RawProjectionQuantity, x.Blockers }).ToArray();
        static InventoryCommandLine Line(InventoryAvailabilityResult r, int bins) => new(new(r.Identity, r.Location, r.Watermark.Fingerprint, r.Watermark.Versions), bins, "u");
        var command = new InventoryCommand(Guid.NewGuid().ToString("N"), InventoryCommandKind.Dump, actor, DateTimeOffset.UtcNow,
            "Phase 2 disposable restore rehearsal", [Line(wp4, 68), Line(wp7, 104)]);
        var appendOnly = new[] { "RoomInventoryAdjustments", "TreatmentLineageMovements", "AuditLogs", "ActualRuns", "ActualRunRevisions", "BinsRunEntries" };
        var filters = new Dictionary<string, string>();
        var cutoffs = new Dictionary<string, long>();
        foreach (var table in appendOnly)
        {
#pragma warning disable EF1002 // Table names come exclusively from the constant allowlist above.
            var max = await db.Database.SqlQueryRaw<long>($"SELECT COALESCE(MAX(\"Id\"),0)::bigint AS \"Value\" FROM \"{table}\"").SingleAsync();
#pragma warning restore EF1002
            cutoffs[table] = max; filters[table] = $"\"Id\" <= {max}";
        }
        filters["TreatmentLineageSegments"] = "NOT (\"RoomId\" IN (1,4) AND \"FruitProfileId\"=17 AND \"GrowerLotId\"=448 AND \"CropYear\"=2026 AND \"LotNumberSnapshot\"='1372')";
        filters["InventoryCommands"] = $"\"OperationKey\" <> '{command.OperationKey}'";
        var protectedBefore = await f.Snapshot(filters);
        async Task<string> HistoricSegmentMetadata() => await db.Database.SqlQueryRaw<string>("""
            SELECT md5(string_agg(v::text,'' ORDER BY v::text)) AS "Value" FROM (
              SELECT to_jsonb(t) - ARRAY['CurrentBins','UpdatedAt','ConcurrencyVersion','Disposition','RetiredAt','RetiredQuantity','RetiredByCommandKey'] AS v
              FROM "TreatmentLineageSegments" t WHERE "Id" <= (SELECT MAX("Id") FROM "TreatmentLineageSegments" WHERE "CreatedAt" < @p0)
              AND "CreatedAt" < @p0) q
            """, command.EffectiveAt).SingleAsync();
        var historicalMetadata = await HistoricSegmentMetadata();
        var before = await f.Snapshot();
        foreach (var stage in new[] { "Normalized", "Source1", "PersistedSource1", "Source2", "PersistedSource2", "Movement", "OperationAudit", "BeforeCommit" })
        {
            await Assert.ThrowsAsync<IOException>(() => f.Execute(command, new Observer((s, _) =>
                s == stage ? Task.FromException(new IOException(stage)) : Task.CompletedTask)));
            Assert.Equal(before, await f.Snapshot());
        }
        var result = await f.Execute(command);
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
        Assert.Equal(new[] { 0, 1018 }, result.Effects.Select(x => x.After));
        Assert.Equal(172, result.Effects.Sum(x => x.Quantity));
        Assert.Equal(protectedBefore, await f.Snapshot(filters));
        Assert.Equal(historicalMetadata, await HistoricSegmentMetadata());
        Assert.Equal(2, await db.RoomInventoryAdjustments.CountAsync(x => x.Id > cutoffs["RoomInventoryAdjustments"]));
        Assert.Equal(2, await db.TreatmentLineageMovements.CountAsync(x => x.Id > cutoffs["TreatmentLineageMovements"]));
        Assert.Equal(1, await db.ActualRuns.CountAsync(x => x.Id > cutoffs["ActualRuns"]));
        Assert.Equal(1, await db.ActualRunRevisions.CountAsync(x => x.Id > cutoffs["ActualRunRevisions"]));
        Assert.Equal(2, await db.BinsRunEntries.CountAsync(x => x.Id > cutoffs["BinsRunEntries"]));
        Assert.Equal(172, await db.BinsRunEntries.Where(x => x.ActualRunId == result.Effects[0].ParentId).SumAsync(x => x.BinsRun));
        var post = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(null, [1, 4]), new(), DateTimeOffset.UtcNow);
        Assert.Equal(1018, post.Positions.Single(x => x.Location.RoomId == 4 && x.Identity.Key == wp7.Identity.Key).AvailableQuantity);
        var audit = await db.AuditLogs.AsNoTracking().Where(x => x.Action == "CanonicalInventoryNormalization"
            && x.EntityKey == wp7.PositionKey).OrderByDescending(x => x.Id).FirstAsync();
        var plan = JsonSerializer.Deserialize<InventoryNormalizationPlan>(audit.AfterValuesJson!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(1568, plan.Changes.Sum(x => x.BeforeQuantity));
        Assert.Equal(1122, plan.ReplacementQuantity);
        Assert.Null(plan.ReplacementReceiptId);
        Assert.Equal(446, plan.Changes.Sum(x => x.BeforeQuantity) - plan.ReplacementQuantity);
        foreach (var row in plan.Changes)
        {
            var retained = await db.TreatmentLineageSegments.AsNoTracking().SingleAsync(x => x.Id == row.Id);
            Assert.Equal("Historical", retained.Disposition); Assert.Equal(row.BeforeQuantity, retained.RetiredQuantity);
        }
        var committedFingerprint = await f.Snapshot();
        var down = db.GetService<IMigrator>().GenerateScript("20261001144023_CanonicalInventoryCommands",
            "20260923202144_AddTruckReceiptReconciliation", MigrationsSqlGenerationOptions.NoTransactions);
        await using (var tx = await db.Database.BeginTransactionAsync())
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(down));
            Assert.Contains("preserve additive schema", error.MessageText);
            await tx.RollbackAsync();
        }
        Assert.Equal(committedFingerprint, await f.Snapshot());
        var report = Environment.GetEnvironmentVariable("CANONICAL_INVENTORY_RESTORE_REPORT");
        if (report != null) await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new
        {
            Backup = template.Database,
            Wp4Before = 68,
            Wp7Before = 1122,
            RawProjectionBefore = 1568,
            HistoricalExcessNeverCredited = 446,
            result,
            plan,
            RollbackStagesPassed = 8,
            ProtectedFingerprintsUnchanged = true,
            OriginalSegmentMetadataUnchanged = true,
            ProtectedFingerprints = protectedBefore,
            DowngradeWithCommandHistoryRefused = true,
            KnownCases = known
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public sealed class InventoryCommandRestoreFactAttribute : FactAttribute
{
    public InventoryCommandRestoreFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CANONICAL_INVENTORY_RESTORE_POSTGRES")))
            Skip = "Requires an independently verified fresh backup restored to isolated local PostgreSQL.";
    }
}
