using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class ReceiptCorrectionTreatmentTests
{
    [InventoryPostgresFact]
    public async Task Added_bins_do_not_inherit_the_selected_allocations_historical_treatment()
    {
        await using var f = await Fixture.Create();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var chemical = await db.TreatmentChemicals.FirstAsync(x => x.IsActive && x.ApplicationLevel == "Room");
        var treatment = await f.Execute((await f.Command(InventoryCommandKind.TreatmentAssignment, 19)) with { TreatmentChemicalId = chemical.Id });
        Assert.Equal(InventoryCommandStatus.Committed, treatment.Status);
        var executor = new InventoryCommandExecutor(factory);
        var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, executor);
        var form = await CanonicalReceiptCorrectionWorkflowTests.Form(db, service, 100000, 24);
        var result = await service.ApplyEditAsync(form, CanonicalReceivingWorkflowTests.Operator().HttpContext!.User, default);
        Assert.Null(result.Error);
        var current = await db.TreatmentLineageSegments.AsNoTracking().Include(x => x.Applications)
            .Where(x => x.ReceiptId == 100000 && x.Disposition == "Current" && x.CurrentBins > 0).ToListAsync();
        Assert.Equal(19, current.Where(x => x.TreatmentState == "Confirmed").Sum(x => x.CurrentBins));
        var additional = Assert.Single(current, x => x.TreatmentSignature == "u");
        Assert.Equal(5, additional.CurrentBins);
        Assert.Empty(additional.Applications);
        Assert.Equal(19, await db.RoomTreatmentApplicationSources.SumAsync(x => x.BinsTreated));
        Assert.Equal(24, await f.Physical());
        var saved = await f.Snapshot();
        Assert.True((await service.ApplyEditAsync(form, CanonicalReceivingWorkflowTests.Operator().HttpContext!.User, default)).WasIdempotent);
        Assert.Equal(saved, await f.Snapshot());
        form.ConfirmAdditionalBinsUntreated = false;
        Assert.NotNull((await service.ApplyEditAsync(form, CanonicalReceivingWorkflowTests.Operator().HttpContext!.User, default)).Error);
        Assert.Equal(saved, await f.Snapshot());
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(await f.Command(InventoryCommandKind.RoomMove, 5))).Status);
        var treatedMove = await f.Command(InventoryCommandKind.RoomMove, 3);
        treatedMove = treatedMove with { Lines = [treatedMove.Lines[0] with { TreatmentSignature = $"u|a:{treatment.Effects[0].ParentId}" }] };
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(treatedMove)).Status);
        var moved = await db.TreatmentLineageSegments.AsNoTracking().Where(x => x.RoomId == 9003 && x.Disposition == "Current").ToListAsync();
        Assert.Equal(5, moved.Where(x => x.TreatmentState == "Untreated").Sum(x => x.CurrentBins));
        Assert.Equal(3, moved.Where(x => x.TreatmentState == "Confirmed").Sum(x => x.CurrentBins));
        Assert.Equal(19, await db.RoomTreatmentApplicationSources.SumAsync(x => x.BinsTreated));
        Assert.Equal(24, await f.Physical(9002) + await f.Physical(9003));
    }

    [InventoryPostgresFact]
    public async Task Direct_command_cannot_add_bins_without_explicit_treatment_confirmation()
    {
        await using var f = await Fixture.Create();
        await using var db = f.CreateDbContext();
        var state = (await new InventoryReceiptAvailability(db).ReadAsync(100000, default))!;
        var command = new InventoryCommand(Guid.NewGuid().ToString("N"), InventoryCommandKind.CorrectReceiptQuantity, 8000,
            DateTimeOffset.UtcNow, "Count evidence reviewed", [], ReceiptChange: new(100000, state.Receipt.ConcurrencyVersion,
                state.Fingerprint, 24, [new(state.Allocations.Single().Key, 5)]));
        var before = await f.Snapshot();
        var result = await f.Execute(command);
        Assert.Equal(InventoryCommandStatus.Blocked, result.Status);
        Assert.Contains("untreated", result.Detail);
        Assert.Equal(before, await f.Snapshot());
    }

    [InventoryPostgresTheory]
    [InlineData("Movement")]
    [InlineData("BeforeCommit")]
    public async Task Positive_correction_failure_is_atomic_and_retry_records_only_one_origin(string stage)
    {
        await using var f = await Fixture.Create();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, new InventoryCommandExecutor(factory));
        var form = await CanonicalReceiptCorrectionWorkflowTests.Form(db, service, 100000, 24);
        var principal = CanonicalReceivingWorkflowTests.Operator().HttpContext!.User;
        var before = await f.Snapshot();
        var failing = CanonicalReceiptCorrectionWorkflowTests.Service(db, new InventoryCommandExecutor(factory,
            new Observer((at, _) => at == stage ? Task.FromException(new IOException("Injected correction failure")) : Task.CompletedTask)));
        await Assert.ThrowsAsync<IOException>(() => failing.ApplyEditAsync(form, principal, default));
        Assert.Equal(before, await f.Snapshot());
        Assert.Null((await service.ApplyEditAsync(form, principal, default)).Error);
        Assert.Equal(24, await f.Physical());
        Assert.Single(await db.ReceiptInventoryOverrides.ToListAsync());
        var saved = await f.Snapshot();
        Assert.True((await service.ApplyEditAsync(form, principal, default)).WasIdempotent);
        Assert.Equal(saved, await f.Snapshot());
    }

    [InventoryPostgresFact]
    public async Task Confirmed_additions_are_posted_now_even_if_a_direct_caller_supplies_an_old_effective_time()
    {
        await using var f = await Fixture.Create();
        await using var db = f.CreateDbContext();
        var state = (await new InventoryReceiptAvailability(db).ReadAsync(100000, default))!;
        var started = DateTimeOffset.UtcNow;
        var command = new InventoryCommand(Guid.NewGuid().ToString("N"), InventoryCommandKind.CorrectReceiptQuantity, 8000,
            started.AddDays(-7), "Verified additional untreated bins now", [], ReceiptChange: new(100000, state.Receipt.ConcurrencyVersion,
                state.Fingerprint, 24, [new(state.Allocations.Single().Key, 5)], ConfirmAdditionalBinsUntreated: true));
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(command)).Status);
        var ledger = await db.RoomInventoryAdjustments.SingleAsync(x => x.ReceiptInventoryOverrideId != null);
        var movement = await db.TreatmentLineageMovements.SingleAsync(x => x.MovementType == "ReceiptQuantityCorrection");
        Assert.True(ledger.AdjustmentAt >= started);
        Assert.Equal(ledger.AdjustmentAt, movement.OccurredAt);
        Assert.Equal("u", movement.TreatmentSignatureSnapshot);
        Assert.Equal(5, movement.BinCount);
        Assert.Equal(24, await f.Physical());
    }
}
