using System.Text.Json;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class DepletedReceiptCorrectionTests
{
    [InventoryPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Additional_origin_after_complete_consumption_preserves_old_treatment_and_movement(bool treated)
    {
        await using var f = await Fixture.Create();
        await using var db = f.CreateDbContext();
        var signature = "u";
        if (treated)
        {
            var chemical = await db.TreatmentChemicals.FirstAsync(x => x.IsActive && x.ApplicationLevel == "Room");
            var applied = await f.Execute((await f.Command(InventoryCommandKind.TreatmentAssignment, 19)) with { TreatmentChemicalId = chemical.Id });
            Assert.Equal(InventoryCommandStatus.Committed, applied.Status);
            signature = $"u|a:{applied.Effects[0].ParentId}";
        }
        var move = await f.Command(InventoryCommandKind.RoomMove, 19);
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(move with { Lines = [move.Lines[0] with { TreatmentSignature = signature }] })).Status);
        var loss = await f.Command(InventoryCommandKind.Loss, 19, room: 9003);
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(loss with { Lines = [loss.Lines[0] with { TreatmentSignature = signature }] })).Status);
        var historical = await History(db);
        var command = await Addition(f, 9002);
        var started = DateTimeOffset.UtcNow;
        var result = await f.Execute(command with { EffectiveAt = started.AddDays(-30) });
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
        Assert.Equal(historical, await History(db));
        Assert.Equal(2, await f.Physical()); Assert.Equal(0, await f.Physical(9003));
        var added = await db.TreatmentLineageSegments.AsNoTracking().Include(x => x.Applications).SingleAsync(x => x.CohortKey == command.OperationKey);
        Assert.Equal(2, added.CurrentBins); Assert.Equal(100000, added.ReceiptId); Assert.Equal("u", added.TreatmentSignature); Assert.Empty(added.Applications);
        var ledger = await db.RoomInventoryAdjustments.SingleAsync(x => x.ReceiptInventoryOverrideId != null);
        Assert.True(ledger.AdjustmentAt >= started);
        Assert.Equal(21, (await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == 100000)).BinCount);
        var proof = await new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance).VerifyReadinessAsync(default);
        Assert.True(proof.IsReady, JsonSerializer.Serialize(proof));
    }

    [InventoryPostgresFact]
    public async Task Missing_location_treatment_sealed_destination_and_stale_preview_do_not_change_history()
    {
        await using var f = await Fixture.Create();
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(await f.Command(InventoryCommandKind.Loss, 19))).Status);
        var command = await Addition(f, 9003);
        foreach (var intent in new[]
        {
            command.ReceiptChange! with { AdditionalStock = null },
            command.ReceiptChange! with { ConfirmAdditionalBinsUntreated = false },
            command.ReceiptChange! with { AdditionalStock = command.ReceiptChange!.AdditionalStock! with { ConfirmPresent = false } },
            command.ReceiptChange! with { NewQuantity = 17 },
            command.ReceiptChange! with { AdditionalStock = command.ReceiptChange!.AdditionalStock! with { ExpectedFingerprint = "stale" } }
        })
        {
            var before = await f.Snapshot();
            Assert.NotEqual(InventoryCommandStatus.Committed, (await f.Execute(command with { ReceiptChange = intent })).Status);
            Assert.Equal(before, await f.Snapshot());
        }
        await using var db = f.CreateDbContext();
        (await db.Rooms.SingleAsync(x => x.Id == 9003)).IsSealed = true;
        await db.SaveChangesAsync();
        var sealedBefore = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Blocked, (await f.Execute(command)).Status);
        Assert.Equal(sealedBefore, await f.Snapshot());
    }

    [InventoryPostgresFact]
    public async Task Concurrent_depleted_corrections_create_one_origin_and_replay_exactly()
    {
        await using var f = await Fixture.Create();
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(await f.Command(InventoryCommandKind.Loss, 19))).Status);
        var first = await Addition(f, 9002);
        var second = first with { OperationKey = Guid.NewGuid().ToString("N") };
        var reached = 0;
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var observer = new Observer(async (stage, attempt) =>
        {
            if (stage != "Normalized" || attempt != 1) return;
            if (Interlocked.Increment(ref reached) == 2) barrier.SetResult();
            await barrier.Task.WaitAsync(TimeSpan.FromSeconds(30));
        });
        var results = await Task.WhenAll(f.Execute(first, observer), f.Execute(second, observer));
        Assert.Single(results, x => x.Status == InventoryCommandStatus.Committed);
        Assert.Single(results, x => x.Status is InventoryCommandStatus.Stale or InventoryCommandStatus.Blocked);
        Assert.Equal(2, await f.Physical());
        var winner = results[0].Status == InventoryCommandStatus.Committed ? first : second;
        var saved = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Replayed, (await f.Execute(winner)).Status);
        Assert.Equal(saved, await f.Snapshot());
    }

    [InventoryCommandRestoreFact]
    public async Task Backup198_three_receipt_corrections_preserve_committed_history_and_separate_quantities()
    {
        await using var f = await CanonicalRestoreFixture.Clone();
        await using var db = f.CreateDbContext();
        var audit = await db.AuditLogs.MaxAsync(x => x.Id);
        var ledger = await db.RoomInventoryAdjustments.MaxAsync(x => x.Id);
        var movement = await db.TreatmentLineageMovements.MaxAsync(x => x.Id);
        var segments = await db.TreatmentLineageSegments.MaxAsync(x => x.Id);
        var filters = new Dictionary<string, string>
        {
            ["Receipts"] = "\"Id\" NOT IN (2475,2476,2490)",
            ["RoomInventoryAdjustments"] = $"\"Id\" <= {ledger}",
            ["TreatmentLineageMovements"] = $"\"Id\" <= {movement}",
            ["TreatmentLineageSegments"] = $"\"Id\" <= {segments} AND \"Id\" <> 768",
            ["ReceiptInventoryOverrides"] = "\"OperationKey\" NOT LIKE 'local-correction-%'",
            ["InventoryCommands"] = "\"OperationKey\" NOT LIKE 'local-correction-%'",
            ["AuditLogs"] = $"\"Id\" <= {audit}"
        };
        var protectedBefore = await f.Snapshot(filters);
        foreach (var (id, old, revised, current, after) in new[] { (2476L, 13, 15, 0, 2), (2475L, 35, 20, 35, 20), (2490L, 27, 31, 27, 31) })
        {
            var reader = new InventoryReceiptAvailability(db);
            var state = (await reader.ReadAsync(id, default))!;
            Assert.Null(state.Blocker); Assert.Equal(old, state.Receipt.BinCount); Assert.Equal(current, state.RoomQuantity);
            var receiptBefore = state.Receipt;
            var target = current == 0 ? (await reader.AdditionTargetsAsync(state, default)).Single(x => x.RoomId == 30) : null;
            var command = new InventoryCommand($"local-correction-{id}", InventoryCommandKind.CorrectReceiptQuantity, 2, DateTimeOffset.UtcNow,
                "Disposable rehearsal only: hypothetical explicit current location and untreated evidence, not production repair approval", [],
                ReceiptChange: new(id, state.Receipt.ConcurrencyVersion, state.Fingerprint, revised,
                    current == 0 ? [] : [new(state.Allocations.Single().Key, Math.Abs(revised - old))], ConfirmAdditionalBinsUntreated: true,
                    AdditionalStock: target == null ? null : new(target.RoomId, target.Fingerprint, true)));
            var result = await f.Execute(command);
            Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
            var updated = (await reader.ReadAsync(id, default))!;
            Assert.Null(updated.Blocker); Assert.Equal(revised, updated.Receipt.BinCount); Assert.Equal(after, updated.RoomQuantity);
            Assert.Equal(receiptBefore.ReceivedAt, updated.Receipt.ReceivedAt); Assert.Equal(receiptBefore.RoomId, updated.Receipt.RoomId);
        }
        Assert.Equal(protectedBefore, await f.Snapshot(filters));
        Assert.Equal(13, (await db.BinsRunEntries.SingleAsync(x => x.Id == 503)).BinsRun);
        var readiness = await new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance).VerifyReadinessAsync(default);
        Assert.True(readiness.IsReady, JsonSerializer.Serialize(readiness));
    }

    [InventoryPostgresTheory]
    [InlineData("Movement")]
    [InlineData("BeforeCommit")]
    public async Task Operator_preview_to_commit_is_atomic_and_preserves_destination_discrepancy(string failureStage)
    {
        await using var f = await Fixture.Create();
        await InventoryMovementDestinationTests.SeedDestination(f, 9);
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(await f.Command(InventoryCommandKind.Loss, 19))).Status);
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, new InventoryCommandExecutor(factory));
        var preview = (await service.GetPreviewAsync(100000, default))!;
        Assert.Equal(19, preview.OriginalRecordedBins);
        Assert.Contains(preview.RecordedMovements, x => x.Kind == "InventoryLoss" && x.Bins == 19);
        var form = await CanonicalReceiptCorrectionWorkflowTests.Form(db, service, 100000, 21);
        form.AdditionalStockTarget = preview.AdditionalStockTargets.Single(x => x.RoomId == 9003).Key;
        form.ConfirmAdditionalStockPresent = true;
        var actor = CanonicalReceivingWorkflowTests.Operator().HttpContext!.User;
        var before = await f.Snapshot();
        var failing = CanonicalReceiptCorrectionWorkflowTests.Service(db, new InventoryCommandExecutor(factory,
            new Observer((stage, _) => stage == failureStage ? Task.FromException(new IOException("Injected additional-origin rollback")) : Task.CompletedTask)));
        await Assert.ThrowsAsync<IOException>(() => failing.ApplyEditAsync(form, actor, default));
        Assert.Equal(before, await f.Snapshot());
        var destinationBefore = JsonSerializer.Serialize(await db.TreatmentLineageSegments.AsNoTracking().Where(x => x.RoomId == 9003).OrderBy(x => x.Id).ToArrayAsync());
        Assert.Null((await service.ApplyEditAsync(form, actor, default)).Error);
        Assert.Equal(19, await f.Physical(9003)); // Existing authoritative 17 + separate 2; projection gap is unchanged.
        Assert.Equal(destinationBefore, JsonSerializer.Serialize(await db.TreatmentLineageSegments.AsNoTracking().Where(x => x.RoomId == 9003 && x.CohortKey != form.OperationKey).OrderBy(x => x.Id).ToArrayAsync()));
        var saved = await f.Snapshot();
        Assert.True((await service.ApplyEditAsync(form, actor, default)).WasIdempotent);
        Assert.Equal(saved, await f.Snapshot());
    }

    private static async Task<InventoryCommand> Addition(Fixture f, int room)
    {
        await using var db = f.CreateDbContext();
        var reader = new InventoryReceiptAvailability(db);
        var state = (await reader.ReadAsync(100000, default))!;
        Assert.Null(state.Blocker); Assert.Empty(state.Allocations);
        var target = (await reader.AdditionTargetsAsync(state, default)).Single(x => x.RoomId == room);
        return new(Guid.NewGuid().ToString("N"), InventoryCommandKind.CorrectReceiptQuantity, 8000, DateTimeOffset.UtcNow,
            "Verified additional bins belong to this receipt and are present untreated", [],
            ReceiptChange: new(100000, state.Receipt.ConcurrencyVersion, state.Fingerprint, 21, [], ConfirmAdditionalBinsUntreated: true,
                AdditionalStock: new(room, target.Fingerprint, true)));
    }

    private static async Task<string> History(CropQc.Data.CropQcDbContext db) => JsonSerializer.Serialize(new
    {
        Losses = await db.RoomInventoryLosses.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Applications = await db.RoomTreatmentApplications.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Sources = await db.RoomTreatmentApplicationSources.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Ledger = await db.RoomInventoryAdjustments.AsNoTracking().Where(x => x.ReceiptInventoryOverrideId == null).OrderBy(x => x.Id).ToArrayAsync(),
        Movements = await db.TreatmentLineageMovements.AsNoTracking().Where(x => x.MovementType != "ReceiptQuantityCorrection").OrderBy(x => x.Id).ToArrayAsync(),
        HistoricalSegments = await db.TreatmentLineageSegments.AsNoTracking().Where(x => x.Disposition == "Historical").OrderBy(x => x.Id).ToArrayAsync()
    });
}
