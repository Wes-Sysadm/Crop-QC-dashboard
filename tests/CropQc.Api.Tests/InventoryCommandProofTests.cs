using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class InventoryCommandProofTests
{
    public static IEnumerable<object[]> Shapes() => InventoryEvidenceCorpus.Cases().Select(x => new object[] { x.Name, x.Evidence, x.Available });
    [Theory]
    [MemberData(nameof(Shapes))]
    public void Executable_plans_require_independent_physical_and_treatment_proof(string name, InventoryPositionEvidence e, int expected)
    {
        Assert.False(string.IsNullOrWhiteSpace(name));
        var result = InventoryAvailabilityResolver.Resolve(e, new(AllowedCustody: e.Location.Custody));
        Assert.Equal(expected, result.AvailableQuantity);
        if (!result.IsOperable || result.TreatmentConfidence != InventoryConfidence.Proven)
        {
            Assert.Throws<InvalidOperationException>(() => InventoryNormalizationPlanner.Plan(e, result));
            return;
        }
        var plan = InventoryNormalizationPlanner.Plan(e, result);
        if (plan == null) { Assert.Equal(e.AuthoritativeQuantity, e.Projections.Sum(x => x.Quantity)); return; }
        Assert.Equal(e.AuthoritativeQuantity, plan.ReplacementQuantity);
        Assert.All(plan.Changes, x => { Assert.Equal(0, x.AfterQuantity); Assert.Equal("Historical", x.AfterDisposition); Assert.Equal(x.BeforeVersion + 1, x.AfterVersion); });
        Assert.Equal(e.Projections.Where(x => x.Quantity > 0).Select(x => x.Id).Order(), plan.Changes.Select(x => x.Id));
        Assert.All(plan.Changes, x => Assert.Equal(e.Projections.Single(p => p.Id == x.Id).ReceiptId, x.ReceiptId));
    }

    [Fact]
    public void Ambiguous_receipt_is_valid_for_dump_but_never_for_receipt_specific_correction()
    {
        var e = InventoryEvidenceCorpus.Wp7();
        var line = new InventoryCommandLine(new(e.Identity, e.Location, e.Watermark.Fingerprint, []), 104, "u", ReceiptId: 905);
        Assert.True(InventoryAvailabilityResolver.Resolve(e, InventoryCommandPolicy.Requirements(InventoryCommandKind.Dump, line)).IsOperable);
        Assert.False(InventoryAvailabilityResolver.Resolve(e, InventoryCommandPolicy.Requirements(InventoryCommandKind.ReceiptCorrection, line)).IsOperable);
        var ambiguous = InventoryEvidenceCorpus.Treated() with { AuthoritativeQuantity = 10 };
        Assert.Throws<InvalidOperationException>(() => InventoryNormalizationPlanner.Plan(ambiguous, InventoryAvailabilityResolver.Resolve(ambiguous, new())));
    }

    [InventoryPostgresFact]
    public async Task Actual_audit_movement_and_parent_save_failures_roll_back_all_tables()
    {
        await using var f = await Fixture.Create();
        var c = await f.Command(InventoryCommandKind.Dump, 19);
        var before = await f.Snapshot();
        foreach (var name in new[] { "CanonicalInventoryNormalization", "CanonicalInventoryCommand", "movement", "parent" })
        {
            var factory = new Fixture(f.Connection, new FailingWrite(name));
            await Assert.ThrowsAsync<IOException>(() => new InventoryCommandExecutor(factory).ExecuteAsync(c));
            Assert.Equal(before, await f.Snapshot());
        }
    }

    [InventoryPostgresFact]
    public async Task Serialization_failure_retries_whole_intent_once_with_no_duplicate_history()
    {
        await using var f = await Fixture.Create();
        var c = await f.Command(InventoryCommandKind.Dump, 19);
        var interceptor = new FailingWrite("CanonicalInventoryCommand", serialization: true);
        var result = await new InventoryCommandExecutor(new Fixture(f.Connection, interceptor)).ExecuteAsync(c);
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
        Assert.Equal(2, result.Attempts);
        await using var db = f.CreateDbContext();
        Assert.Equal(1, await db.InventoryCommands.CountAsync()); Assert.Equal(1, await db.ActualRuns.CountAsync());
        Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.Action == "CanonicalInventoryNormalization"));
        Assert.Equal(1, await db.TreatmentLineageMovements.CountAsync());
    }

    private sealed class FailingWrite(string name, bool serialization = false) : SaveChangesInterceptor
    {
        private bool failed;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken ct = default)
        {
            var entries = eventData.Context!.ChangeTracker.Entries().Where(x => x.State == EntityState.Added).ToArray();
            if (!failed && entries.Any(x => x.Entity is AuditLog audit && audit.Action == name
                || name == "movement" && x.Entity is TreatmentLineageMovement || name == "parent" && x.Entity is ActualRun))
            {
                failed = true;
                if (serialization) throw new PostgresException("Injected serialization conflict", "ERROR", "ERROR", "40001");
                throw new IOException($"Injected {name} SaveChanges failure");
            }
            return ValueTask.FromResult(result);
        }
    }
}
