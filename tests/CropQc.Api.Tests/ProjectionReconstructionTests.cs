using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CropQc.Api.Tests;

public sealed class ProjectionReconstructionTests
{
    [InventoryPostgresFact]
    public async Task Preview_approval_reconstruction_replay_preserve_authority_and_full_history()
    {
        await using var f = await Fixture.Create();
        var beforePreview = await f.Snapshot();
        var preview = await f.Executor.PreviewProjectionReconstructionAsync(f.Target);
        Assert.True(preview.Eligible, string.Join(';', preview.Blockers));
        Assert.Equal(beforePreview, await f.Snapshot());
        Assert.Equal(19, preview.AuthoritativeQuantity);
        Assert.Equal(48, preview.ProjectedQuantity);
        Assert.Null(preview.Plan!.ReplacementReceiptId);
        var approval = f.Approval(preview);
        var id = await f.Executor.ApproveProjectionReconstructionAsync(approval, true);
        Assert.Equal(id, await f.Executor.ApproveProjectionReconstructionAsync(approval, true));
        var command = new ProjectionReconstructionRequest(Guid.NewGuid().ToString("N"), id, 8000, true, preview);
        var result = await f.Executor.ReconstructProjectionAsync(command, true);
        Assert.Equal("Committed", result.Status);
        Assert.Equal("Verified", (await f.Executor.VerifyProjectionReconstructionAsync(command.OperationKey)).Status);
        await using var db = f.CreateDbContext();
        var rows = await db.TreatmentLineageSegments.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        var current = Assert.Single(rows, x => x.CurrentBins > 0);
        Assert.Equal(19, current.CurrentBins); Assert.Null(current.ReceiptId);
        Assert.Equal("u", current.TreatmentSignature); Assert.Empty(current.Applications);
        Assert.Equal(2, rows.Count(x => x.Disposition == "Historical"));
        Assert.All(rows.Where(x => x.Disposition == "Historical"), x =>
        { Assert.Equal(0, x.CurrentBins); Assert.Equal(command.OperationKey, x.RetiredByCommandKey); Assert.NotNull(x.RetiredQuantity); });
        Assert.Equal(19, await db.RoomInventoryAdjustments.SumAsync(x => x.ChangeAmount));
        Assert.Equal(19, (await db.Receipts.SingleAsync(x => x.Id == 100000)).BinCount);
        Assert.Equal(2, await db.AuditLogs.CountAsync(x => x.EntityName == "ProjectionReconstruction"));
        var committed = await f.Snapshot();
        Assert.Equal("Replayed", (await f.Executor.ReconstructProjectionAsync(command, true)).Status);
        Assert.Equal(committed, await f.Snapshot());
        Assert.Equal("Blocked", (await f.Executor.ReconstructProjectionAsync(command with { OperationKey = "different-key" }, true)).Status);
        Assert.Equal(committed, await f.Snapshot());
        Assert.Equal("Blocked", (await f.Executor.ReconstructProjectionAsync(command with { Preview = preview with { AuthoritativeQuantity = 20 } }, true)).Status);
        Assert.Equal(committed, await f.Snapshot());
    }

    [InventoryPostgresFact]
    public async Task Administrator_alone_forged_approval_and_unrelated_targets_do_not_authorize_writes()
    {
        await using var f = await Fixture.Create();
        var p = await f.Executor.PreviewProjectionReconstructionAsync(f.Target);
        await Assert.ThrowsAnyAsync<Exception>(() => f.Executor.ApproveProjectionReconstructionAsync(f.Approval(p) with { ExplicitlyApprove = false }, true));
        var request = new ProjectionReconstructionRequest("no-approval", 99999, 8000, true, p);
        var baseline = await f.Snapshot();
        Assert.Equal("Blocked", (await f.Executor.ReconstructProjectionAsync(request, true)).Status);
        Assert.Equal(baseline, await f.Snapshot());
        var id = await f.Executor.ApproveProjectionReconstructionAsync(f.Approval(p), true);
        baseline = await f.Snapshot();
        foreach (var forged in new[]
        {
            request with { ApprovalAuditId = id, ExplicitlyExecute = false },
            request with { ApprovalAuditId = id, OperatorId = 9999 },
            request with { ApprovalAuditId = id, Preview = p with { Target = p.Target with { RoomId = 9003 } } },
            request with { ApprovalAuditId = id, Preview = p with { Plan = p.Plan! with { Changes = [p.Plan!.Changes[0]] } } },
            request with { ApprovalAuditId = id, Preview = p with { ExpectedTreatmentSignature = "u|a:1" } }
        })
        { Assert.Equal("Blocked", (await f.Executor.ReconstructProjectionAsync(forged, true)).Status); Assert.Equal(baseline, await f.Snapshot()); }
    }

    [InventoryPostgresFact]
    public async Task Stale_versions_quantities_treatment_receipts_movements_and_audits_require_new_approval()
    {
        foreach (var sql in new[]
        {
            "UPDATE \"TreatmentLineageSegments\" SET \"ConcurrencyVersion\"=\"ConcurrencyVersion\"+1 WHERE \"Id\"=100000",
            "UPDATE \"RoomInventoryAdjustments\" SET \"ChangeAmount\"=18 WHERE \"Id\"=100000",
            "UPDATE \"TreatmentLineageSegments\" SET \"TreatmentSignature\"='unknown' WHERE \"Id\"=100000",
            "UPDATE \"Receipts\" SET \"BinCount\"=18 WHERE \"Id\"=100000",
            "UPDATE \"TreatmentLineageSegments\" SET \"CurrentBins\"=-1 WHERE \"Id\"=100000",
            "UPDATE \"AuditLogs\" SET \"Action\"='Tampered' WHERE \"Action\"='Approve'"
        })
        {
            await using var f = await Fixture.Create();
            var request = await f.Request();
            await using (var conn = new NpgsqlConnection(f.Connection)) { await conn.OpenAsync(); await new NpgsqlCommand(sql, conn).ExecuteNonQueryAsync(); }
            var baseline = await f.Snapshot();
            Assert.Equal("Blocked", (await f.Executor.ReconstructProjectionAsync(request, true)).Status);
            Assert.Equal(baseline, await f.Snapshot());
        }
    }

    [InventoryPostgresFact]
    public async Task Interrupted_stages_roll_back_every_projection_and_audit()
    {
        foreach (var stage in new[] { "ReconstructionValidated", "ReconstructionRetired", "ReconstructionReplaced", "ReconstructionBeforeCommit" })
        {
            await using var f = await Fixture.Create();
            var request = await f.Request(); var before = await f.Snapshot();
            var executor = new InventoryCommandExecutor(f, new Fail(stage));
            Assert.Equal("Blocked", (await executor.ReconstructProjectionAsync(request, true)).Status);
            Assert.Equal(before, await f.Snapshot());
        }
    }

    [InventoryPostgresFact]
    public async Task Lost_commit_acknowledgment_is_recovered_by_the_persisted_operation_key()
    {
        await using var f = await Fixture.Create(); var request = await f.Request();
        var result = await new InventoryCommandExecutor(f, new Fail("ReconstructionAfterCommit")).ReconstructProjectionAsync(request, true);
        Assert.Equal("Replayed", result.Status);
        var before = await f.Snapshot();
        Assert.Equal("Replayed", (await f.Executor.ReconstructProjectionAsync(request, true)).Status);
        Assert.Equal(before, await f.Snapshot());
    }

    [InventoryPostgresFact]
    public async Task Unavailable_independent_commit_lookup_reports_unknown_and_same_key_recovers()
    {
        await using var f = await Fixture.Create(); var request = await f.Request();
        var context = new UnavailableAfterCommit(f);
        var result = await new InventoryCommandExecutor(context, context).ReconstructProjectionAsync(request, true);
        Assert.Equal("OutcomeUnknown", result.Status);
        var before = await f.Snapshot();
        Assert.Equal("Replayed", (await f.Executor.ReconstructProjectionAsync(request, true)).Status);
        Assert.Equal("Verified", (await f.Executor.VerifyProjectionReconstructionAsync(request.OperationKey)).Status);
        Assert.Equal(before, await f.Snapshot());
    }

    [InventoryPostgresFact]
    public async Task Concurrent_receipt_transfer_treatment_consumption_and_duplicate_submission_cannot_interleave()
    {
        foreach (var table in new[] { "Receipts", "RoomTransfers", "RoomTreatmentApplications", "BinsRunEntries", "TreatmentLineageSegments" })
        {
            await using var f = await Fixture.Create(); var request = await f.Request(); var before = await f.Snapshot();
            await using var conn = new NpgsqlConnection(f.Connection); await conn.OpenAsync();
            await using var competing = await conn.BeginTransactionAsync();
            await new NpgsqlCommand($"LOCK TABLE \"{table}\" IN ROW EXCLUSIVE MODE", conn, competing).ExecuteNonQueryAsync();
            Assert.Equal("Conflict", (await f.Executor.ReconstructProjectionAsync(request, true)).Status);
            Assert.Equal(before, await f.Snapshot());
            await competing.RollbackAsync();
        }
        await using var duplicate = await Fixture.Create(); var command = await duplicate.Request();
        var results = await Task.WhenAll(duplicate.Executor.ReconstructProjectionAsync(command, true), duplicate.Executor.ReconstructProjectionAsync(command, true));
        Assert.Single(results, x => x.Status == "Committed");
        Assert.All(results, x => Assert.Contains(x.Status, new[] { "Committed", "Replayed", "Conflict" }));
        Assert.Equal("Replayed", (await duplicate.Executor.ReconstructProjectionAsync(command, true)).Status);
    }

    [InventoryPostgresFact]
    public async Task Ambiguous_treatment_missing_movement_depleted_and_negative_positions_are_not_candidates()
    {
        foreach (var mutation in new[] { "treated", "missing-movement", "depleted", "negative" })
        {
            await using var f = await Fixture.Create();
            await using (var db = f.LegacyDb())
            {
                if (mutation == "treated") (await db.TreatmentLineageSegments.FirstAsync()).TreatmentSignature = "u|a:123";
                else
                {
                    db.RoomInventoryAdjustments.Add(new()
                    {
                        RoomId = 9002,
                        WarehouseId = 9001,
                        CropYear = 2026,
                        GrowerLotId = 100000,
                        FruitProfileId = 9004,
                        LotNumber = "CORPUS-100000",
                        VarietyCode = "CGAL",
                        GrowerName = "Corpus",
                        ChangeAmount = mutation == "missing-movement" ? -1 : mutation == "depleted" ? -19 : -20,
                        AdjustmentType = "BinsRun",
                        AdjustmentAt = DateTimeOffset.UtcNow,
                        CreatedAt = DateTimeOffset.UtcNow
                    });
                }
                await db.SaveChangesAsync();
            }
            var before = await f.Snapshot(); var p = await f.Executor.PreviewProjectionReconstructionAsync(f.Target);
            Assert.False(p.Eligible); Assert.NotEmpty(p.Blockers);
            Assert.Equal(mutation == "negative" ? "AuthoritativeInventoryProblem" : "AdditionalReconciliationEvidence", p.Classification);
            Assert.Equal(before, await f.Snapshot());
        }
    }

    private sealed class Fail(string stage) : IInventoryCommandObserver
    { public Task AtAsync(string at, int attempt, CancellationToken ct) => at == stage ? throw new IOException("Injected failure") : Task.CompletedTask; }

    private sealed class UnavailableAfterCommit(Fixture fixture) : IDbContextFactory<CropQcDbContext>, IInventoryCommandObserver
    {
        private bool unavailable;
        public CropQcDbContext CreateDbContext() => unavailable ? throw new IOException("Injected independent lookup outage") : fixture.CreateDbContext();
        public Task AtAsync(string stage, int attempt, CancellationToken ct)
        {
            if (stage == "ReconstructionAfterCommit") { unavailable = true; throw new IOException("Injected lost acknowledgment"); }
            return Task.CompletedTask;
        }
    }

    internal sealed class Fixture(string connection) : IDbContextFactory<CropQcDbContext>, IAsyncDisposable
    {
        public string Connection => connection;
        public InventoryCommandExecutor Executor => new(this);
        public ProjectionReconstructionTarget Target => new(9001, 9002, new(2026, 100000, 9004, "CORPUS-100000", "CORPUS-100000", "CGAL", "Conventional", false, ""));
        public CropQcDbContext CreateDbContext() => new(new DbContextOptionsBuilder<CropQcDbContext>().UseNpgsql(connection).Options, new CanonicalInventoryMode(true));
        public CropQcDbContext LegacyDb() => new(new DbContextOptionsBuilder<CropQcDbContext>().UseNpgsql(connection).Options);
        public static async Task<Fixture> Create()
        {
            var original = Environment.GetEnvironmentVariable("CANONICAL_INVENTORY_TEST_POSTGRES")!;
            ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(original);
            var f = new Fixture(new NpgsqlConnectionStringBuilder(original) { Database = "cropqc_test_reconstruction_" + Guid.NewGuid().ToString("N") }.ConnectionString);
            await using var db = f.LegacyDb(); await db.Database.EnsureCreatedAsync();
            await InventoryAvailabilityDatabaseTests.SeedAsync(db, 1);
            db.Users.Add(new() { Id = 8000, Email = "canonical-test@example.invalid", DisplayName = "Maintenance operator" });
            db.UserRoles.Add(new() { UserId = 8000, RoleId = 1 });
            var originalRow = await db.TreatmentLineageSegments.SingleAsync(x => x.Id == 100000);
            var alias = new TreatmentLineageSegment
            {
                IdentityKey = "",
                GrowerNameSnapshot = "",
                LotNumberSnapshot = "",
                VarietyCodeSnapshot = "",
                ProductionTypeSnapshot = "",
                TreatmentState = "",
                TreatmentSignature = ""
            };
            db.Entry(alias).CurrentValues.SetValues(originalRow);
            alias.Id = 100001; alias.IdentityKey += "CONVENTIONAL"; alias.InventoryStatusSnapshot = "Conventional"; alias.CurrentBins = 19;
            db.TreatmentLineageSegments.Add(alias); await db.SaveChangesAsync(); return f;
        }
        public ProjectionReconstructionApprovalRequest Approval(ProjectionReconstructionPreview p) => new(Guid.NewGuid().ToString("N"), 8000, "isolated test approval", "Evidence-bound projection reconstruction", true, p);
        public async Task<ProjectionReconstructionRequest> Request()
        { var p = await Executor.PreviewProjectionReconstructionAsync(Target); Assert.True(p.Eligible, string.Join(';', p.Blockers)); return new(Guid.NewGuid().ToString("N"), await Executor.ApproveProjectionReconstructionAsync(Approval(p), true), 8000, true, p); }
        public async Task<string> Snapshot()
        {
            await using var conn = new NpgsqlConnection(connection); await conn.OpenAsync();
            var values = new List<string>();
            foreach (var table in new[] { "Receipts", "RoomInventoryAdjustments", "TreatmentLineageSegments", "TreatmentLineageMovements", "AuditLogs", "InventoryCommands", "RoomTreatmentApplications" })
                values.Add((string)(await new NpgsqlCommand($"SELECT md5(coalesce(string_agg(to_jsonb(t)::text,'' ORDER BY to_jsonb(t)::text),'')) FROM \"{table}\" t", conn).ExecuteScalarAsync())!);
            return string.Join('|', values);
        }
        public async ValueTask DisposeAsync()
        { ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connection); await using var db = LegacyDb(); await db.Database.EnsureDeletedAsync(); }
    }
}
