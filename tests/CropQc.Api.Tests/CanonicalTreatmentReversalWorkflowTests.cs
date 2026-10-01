using System.Text.Json;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class CanonicalTreatmentReversalWorkflowTests
{
    [InventoryPostgresFact]
    public async Task Reversal_follows_current_room_stock_and_preserves_historical_movements_and_sources()
    {
        await using var f = await Fixture.Create();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        var service = new RoomTreatmentService(db, new CropQc.Web.Services.RoomInventoryLedgerQueryService(db), new CanonicalOutsideWorkflowTests.Access(),
            CanonicalReceivingWorkflowTests.Operator(), new PacificBusinessTimeService(new SystemClock()), NullLogger<RoomTreatmentService>.Instance, executor);
        var form = new RoomTreatmentApplyForm
        {
            RoomId = 9002,
            AppliedAt = DateTimeOffset.UtcNow,
            TreatmentChemicalId = await db.TreatmentChemicals.Where(x => x.IsActive && x.ApplicationLevel == "Room" && x.Crop == "Apples").Select(x => x.Id).FirstAsync()
        };
        Assert.Null((await service.GetApplyPageAsync(form, true, default)).Error); form.ConfirmedReview = true;
        var applied = await service.ApplyAsync(form, default); Assert.Null(applied.Error);
        var dashboard = CanonicalReceivingWorkflowTests.Dashboard(db, executor);
        var option = Assert.Single((await dashboard.GetRoomDetailAsync(9002, default)).TransferLotOptions);
        Assert.Null(await dashboard.CreateRoomTransferAsync(new()
        {
            FromRoomId = 9002,
            DestinationRoomId = 9003,
            DestinationWarehouseId = 9001,
            SourceLotKey = option.LotKey,
            TreatmentSignature = option.TreatmentSignature,
            BinCount = 7,
            TransferAt = DateTimeOffset.UtcNow,
            Reason = "Local treated move"
        }, default));
        var movements = JsonSerializer.Serialize(await db.TreatmentLineageMovements.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.BinCount, x.SourceSegmentId, x.DestinationSegmentId, x.TreatmentSignatureSnapshot }).ToArrayAsync());
        var formReverse = new ReverseRoomTreatmentApplicationForm { Id = applied.ApplicationId!.Value, Reason = "Local reversal after movement" };
        Assert.Null(await service.ReverseAsync(formReverse, default));
        var positions = (await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(9001, [9002, 9003]), new(), DateTimeOffset.UtcNow)).Positions;
        Assert.All(positions, p => { Assert.True(p.IsOperable); Assert.All(p.TreatmentSlices, s => Assert.Equal("u", s.Signature)); });
        Assert.Equal(12, await f.Physical()); Assert.Equal(7, await f.Physical(9003));
        Assert.Equal(19, await db.RoomTreatmentApplicationSources.SumAsync(x => x.BinsTreated));
        Assert.Equal(movements, JsonSerializer.Serialize(await db.TreatmentLineageMovements.AsNoTracking().OrderBy(x => x.Id)
            .Select(x => new { x.Id, x.BinCount, x.SourceSegmentId, x.DestinationSegmentId, x.TreatmentSignatureSnapshot }).ToArrayAsync()));
        var saved = await f.Snapshot(); Assert.Null(await service.ReverseAsync(formReverse, default)); Assert.Equal(saved, await f.Snapshot());
    }

    [InventoryPostgresFact]
    public async Task Fully_consumed_treatment_can_be_reversed_without_restoring_any_stock()
    {
        await using var f = await Fixture.Create();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        var chemical = await db.TreatmentChemicals.FirstAsync(x => x.IsActive && x.ApplicationLevel == "Room");
        var applied = await executor.ExecuteAsync((await f.Command(InventoryCommandKind.TreatmentAssignment, 19)) with { TreatmentChemicalId = chemical.Id });
        Assert.Equal(InventoryCommandStatus.Committed, applied.Status);
        var app = applied.Effects[0].ParentId!.Value;
        var dump = await f.Command(InventoryCommandKind.Dump, 19);
        dump = dump with { Lines = [dump.Lines[0] with { TreatmentSignature = "u|a:" + app }] };
        Assert.Equal(InventoryCommandStatus.Committed, (await executor.ExecuteAsync(dump)).Status);
        var service = new RoomTreatmentService(db, new CropQc.Web.Services.RoomInventoryLedgerQueryService(db), new CanonicalOutsideWorkflowTests.Access(),
            CanonicalReceivingWorkflowTests.Operator(), new PacificBusinessTimeService(new SystemClock()), NullLogger<RoomTreatmentService>.Instance, executor);
        Assert.Null(await service.ReverseAsync(new() { Id = app, Reason = "Local reversal after consumption" }, default));
        Assert.Equal(0, await f.Physical());
        Assert.Equal(0, await db.TreatmentLineageSegments.Where(x => x.Disposition == "Current").SumAsync(x => x.CurrentBins));
        Assert.Equal(19, await db.RoomTreatmentApplicationSources.SumAsync(x => x.BinsTreated));
        Assert.NotNull((await db.RoomTreatmentApplications.AsNoTracking().SingleAsync()).ReversedAt);
        var run = await db.ActualRuns.AsNoTracking().SingleAsync();
        var cancel = new InventoryCommand(Guid.NewGuid().ToString("N"), InventoryCommandKind.CancelRun, 8000, DateTimeOffset.UtcNow,
            "Local cancel after treatment reversal", [], PhysicalParentId: run.Id, ExpectedParentVersion: run.ConcurrencyVersion);
        var restored = await executor.ExecuteAsync(cancel);
        Assert.True(restored.Status == InventoryCommandStatus.Committed, restored.Detail);
        Assert.Equal(19, await f.Physical());
        var p = Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow)).Positions);
        Assert.True(p.IsOperable); Assert.Equal("u", Assert.Single(p.TreatmentSlices).Signature);
    }

    [InventoryPostgresFact]
    public async Task External_custody_survives_audited_treatment_reversal_and_returns_with_current_treatment()
    {
        await using var f = await Fixture.Create();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        db.OutsideWarehouses.Add(new() { Id = 9005, Code = "OUTTEST", Name = "Local outside" }); await db.SaveChangesAsync();
        var executor = new InventoryCommandExecutor(factory);
        var chemical = await db.TreatmentChemicals.FirstAsync(x => x.IsActive && x.ApplicationLevel == "Room");
        var applied = await executor.ExecuteAsync((await f.Command(InventoryCommandKind.TreatmentAssignment, 19)) with { TreatmentChemicalId = chemical.Id });
        Assert.Equal(InventoryCommandStatus.Committed, applied.Status); var app = applied.Effects[0].ParentId!.Value;
        var dispatch = await f.Command(InventoryCommandKind.OutsideWarehouseTransfer, 19);
        dispatch = dispatch with { CounterpartyId = 9005, Lines = [dispatch.Lines[0] with { TreatmentSignature = "u|a:" + app }] };
        var sent = await executor.ExecuteAsync(dispatch); Assert.Equal(InventoryCommandStatus.Committed, sent.Status);
        var movement = await db.TreatmentLineageMovements.AsNoTracking().SingleAsync(x => x.OutsideWarehouseTransferId != null);
        var service = new RoomTreatmentService(db, new CropQc.Web.Services.RoomInventoryLedgerQueryService(db), new CanonicalOutsideWorkflowTests.Access(),
            CanonicalReceivingWorkflowTests.Operator(), new PacificBusinessTimeService(new SystemClock()), NullLogger<RoomTreatmentService>.Instance, executor);
        Assert.Null(await service.ReverseAsync(new() { Id = app, Reason = "Local reversal while outside" }, default));
        Assert.Equal(0, await f.Physical());
        var id = sent.Effects[0].ParentId!.Value;
        var custody = Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(
            new(9001, [], InventoryCustody.OutsideWarehouse, id), new(AllowedCustody: InventoryCustody.OutsideWarehouse), DateTimeOffset.UtcNow)).Positions);
        Assert.True(custody.IsOperable); Assert.Equal(19, custody.AuthoritativeQuantity); Assert.Equal("u", Assert.Single(custody.TreatmentSlices).Signature);
        Assert.Equal("u|a:" + app, (await db.TreatmentLineageMovements.AsNoTracking().SingleAsync(x => x.Id == movement.Id)).TreatmentSignatureSnapshot);
        var returned = await f.CustodyCommand(InventoryCommandKind.Return, InventoryCustody.OutsideWarehouse, id);
        var result = await executor.ExecuteAsync(returned with { OriginalOperationKey = dispatch.OperationKey });
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
        Assert.Equal(19, await f.Physical());
        Assert.All(await db.TreatmentLineageSegments.Where(x => x.Disposition == "Current" && x.CurrentBins > 0).ToListAsync(), x => Assert.Equal("u", x.TreatmentSignature));
    }
}
