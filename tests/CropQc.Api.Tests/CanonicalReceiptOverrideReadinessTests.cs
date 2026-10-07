using CropQc.Data;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json.Nodes;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class CanonicalReceiptOverrideReadinessTests
{
    [InventoryPostgresFact]
    public async Task Normal_64_bin_identity_correction_passes_readiness_without_rewriting_evidence()
    {
        await using var f = await Fixture.Create(2);
        await Correct(f);
        var before = await f.Snapshot();
        await using var db = f.CreateDbContext();
        var result = await Readiness(db);
        Assert.DoesNotContain(result.Issues, x => x.BlocksDeployment);
        var rows = await db.RoomInventoryAdjustments.Where(x => x.ReceiptInventoryOverrideId != null).OrderBy(x => x.ChangeAmount).ToArrayAsync();
        Assert.Equal(new[] { -64, 64 }, rows.Select(x => x.ChangeAmount));
        Assert.Equal(102, await f.Physical());
        Assert.Equal(before, await f.Snapshot());
    }

    [InventoryPostgresTheory]
    [InlineData("missing journal")]
    [InlineData("hash")]
    [InlineData("journal actor")]
    [InlineData("journal result")]
    [InlineData("parent receipt")]
    [InlineData("parent incomplete")]
    [InlineData("wrong type")]
    [InlineData("wrong version")]
    [InlineData("wrong source")]
    [InlineData("wrong lot")]
    [InlineData("wrong variety")]
    [InlineData("wrong status")]
    [InlineData("wrong room")]
    [InlineData("quantity")]
    [InlineData("balance")]
    [InlineData("snapshot missing field")]
    [InlineData("snapshot missing identity")]
    [InlineData("snapshot ambiguous provenance")]
    [InlineData("snapshot duplicated allocation")]
    [InlineData("target snapshot")]
    [InlineData("after receipt")]
    [InlineData("movement quantity")]
    [InlineData("movement treatment")]
    [InlineData("movement identity")]
    public async Task Incomplete_or_conflicting_canonical_evidence_remains_blocked(string damage)
    {
        await using var f = await Fixture.Create(2);
        await Correct(f);
        var before = await f.Snapshot();
        // Damage only a detached in-memory copy: the real writer correctly forbids
        // rewriting committed canonical evidence, even in the disposable database.
        await using var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await using (var source = f.CreateDbContext())
        {
            var parent = await source.ReceiptInventoryOverrides.AsNoTracking().SingleAsync();
            var correction = await source.InventoryIdentityCorrections.AsNoTracking().SingleAsync();
            var command = await source.InventoryCommands.AsNoTracking().SingleAsync(x => x.OperationKey == parent.OperationKey);
            var rows = await source.RoomInventoryAdjustments.AsNoTracking().Where(x => x.ReceiptInventoryOverrideId == parent.Id).ToArrayAsync();
            var moves = await source.TreatmentLineageMovements.AsNoTracking().Where(x => x.InventoryIdentityCorrectionId == correction.Id).ToArrayAsync();
            db.AddRange(parent, correction, command); db.AddRange(rows); db.AddRange(moves);
            var row = rows.Single(x => x.ChangeAmount < 0);
            var move = moves[0];
            switch (damage)
            {
                case "missing journal": db.InventoryCommands.Remove(command); break;
                case "hash": command.IntentHash = new string('0', 64); break;
                case "journal actor": command.ActorId = 1; break;
                case "journal result": command.ResultJson = "{}"; break;
                case "parent receipt": correction.CorrectedReceiptId = 100000; break;
                case "parent incomplete": correction.IsComplete = false; break;
                case "wrong type": row.AdjustmentType = "ReceiptAdminOverride"; break;
                case "wrong version": row.InventoryInvariantVersion = 2; break;
                case "wrong source": row.Source = "Unknown"; break;
                case "wrong lot": row.LotNumber = "WRONG"; break;
                case "wrong variety": row.VarietyCode = "WRONG"; break;
                case "wrong status": row.InventoryStatus = "Organic"; break;
                case "wrong room": row.RoomId = 9003; break;
                case "quantity": row.ChangeAmount++; break;
                case "balance": row.NewBinCount++; break;
                case "target snapshot": correction.TargetIdentitySnapshotJson = "{}"; break;
                case "after receipt": parent.AfterReceiptSnapshotJson = parent.BeforeReceiptSnapshotJson; break;
                case "movement quantity": move.BinCount++; break;
                case "movement treatment": move.TreatmentSignatureSnapshot = "WRONG"; break;
                case "movement identity": move.IdentityKey = "WRONG"; break;
                default:
                    var affected = JsonNode.Parse(parent.AffectedInventorySnapshotJson)!.AsArray();
                    if (damage == "snapshot missing field") affected[0]!["position"]!["identity"]!.AsObject().Remove("status");
                    if (damage == "snapshot missing identity") affected[0]!["position"]!.AsObject().Remove("identity");
                    if (damage == "snapshot ambiguous provenance") affected[0]!["slice"]!["receiptEvidenceIds"] = new JsonArray(100000);
                    if (damage == "snapshot duplicated allocation") affected.Add(affected[0]!.DeepClone());
                    parent.AffectedInventorySnapshotJson = affected.ToJsonString();
                    var sourceSnapshot = JsonNode.Parse(correction.SourceIdentitySnapshotJson)!;
                    sourceSnapshot["allocations"] = affected.DeepClone();
                    correction.SourceIdentitySnapshotJson = sourceSnapshot.ToJsonString();
                    break;
            }
            await db.SaveChangesAsync();
        }
        db.ChangeTracker.Clear();
        Assert.Contains((await Readiness(db)).Issues, x => x.BlocksDeployment);
        Assert.False(db.ChangeTracker.HasChanges());
        Assert.Equal(before, await f.Snapshot());
    }

    internal static async Task Correct(Fixture f)
    {
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        var received = await new CanonicalReceivingService(db, executor).ReceiveAsync(Guid.NewGuid().ToString("N"), 8000, 2026,
            DateTimeOffset.UtcNow, 9001, 9002, 9004, 100000, "", "LOCAL-64-IDENTITY", 64, "Local identity regression", default);
        Assert.Equal(InventoryCommandStatus.Committed, received.Status);
        var receipt = received.Effects.Single().ParentId!.Value;
        var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, executor);
        var form = await CanonicalReceiptCorrectionWorkflowTests.Form(db, service, receipt, 64);
        form.GrowerLotId = 100001;
        Assert.Null((await service.ApplyEditAsync(form, CanonicalReceivingWorkflowTests.Operator().HttpContext!.User, default)).Error);
    }

    internal static Task<InventoryDeductionReadinessResult> Readiness(CropQcDbContext db) =>
        new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance).VerifyReadinessAsync(default);
}
