using System.Text.Json;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class InternalReturnHistoryCompatibilityTests
{
    [InventoryPostgresTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Completed_internal_return_preserves_receive_history_and_strict_readiness(bool treated)
    {
        await using var f = await Fixture.Create();
        await using var db = f.CreateDbContext();
        // The standard fixture deliberately has projection excess. This regression
        // starts with an exact synthetic source so unrelated excess is not waived.
        (await db.TreatmentLineageSegments.SingleAsync()).CurrentBins = 19;
        (await db.Warehouses.SingleAsync(x => x.Code == "EBS")).Code = "BASE-EBS";
        await db.SaveChangesAsync();
        db.Warehouses.Add(new() { Id = 9006, Code = "EBS", Name = "Synthetic receiving site" });
        db.Rooms.Add(new() { Id = 9007, WarehouseId = 9006, Code = "RECV", Name = "Synthetic destination" });
        await db.SaveChangesAsync();
        var signature = "u";
        if (treated)
        {
            var chemical = await db.TreatmentChemicals.FirstAsync(x => x.IsActive && x.ApplicationLevel == "Room");
            var applied = await f.Execute((await f.Command(InventoryCommandKind.TreatmentAssignment, 19)) with { TreatmentChemicalId = chemical.Id });
            Assert.Equal(InventoryCommandStatus.Committed, applied.Status);
            signature = $"u|a:{applied.Effects[0].ParentId}";
        }
        var dispatch = (await f.Command(InventoryCommandKind.InterCompanyDispatch, 19)) with
        { CustodyGroup = "EBS", Dispatch = new(null, "Synthetic internal history", false) };
        dispatch = dispatch with { Lines = [dispatch.Lines[0] with { TreatmentSignature = signature }] };
        var sent = await f.Execute(dispatch);
        Assert.True(sent.Status == InventoryCommandStatus.Committed, sent.Detail);
        var parentId = sent.Effects[0].ParentId!.Value;
        var receive = await f.CustodyCommand(InventoryCommandKind.ReceiveTransfer, InventoryCustody.InTransit, parentId);
        receive = receive with { Lines = [receive.Lines[0] with { Destination = new(9006, 9007) }] };
        var received = await f.Execute(receive);
        Assert.True(received.Status == InventoryCommandStatus.Committed, received.Detail);
        var parent = await db.InterCrewTransfers.AsNoTracking().SingleAsync();
        var before = ReceiveSnapshot(parent);
        var immutable = JsonSerializer.Serialize(new
        {
            Receipts = await db.Receipts.AsNoTracking().ToArrayAsync(),
            Applications = await db.RoomTreatmentApplications.AsNoTracking().ToArrayAsync(),
            Sources = await db.RoomTreatmentApplicationSources.AsNoTracking().ToArrayAsync(),
            Ledger = await db.RoomInventoryAdjustments.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Movements = await db.TreatmentLineageMovements.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()
        });
        var ledgerIds = await db.RoomInventoryAdjustments.Select(x => x.Id).ToArrayAsync();
        var movementIds = await db.TreatmentLineageMovements.Select(x => x.Id).ToArrayAsync();
        var auditIds = await db.AuditLogs.Select(x => x.Id).ToArrayAsync();
        var audits = JsonSerializer.Serialize(await db.AuditLogs.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync());
        var ret = (await f.Command(InventoryCommandKind.Return, 19, 9007, 9006)) with { PhysicalParentId = parentId };
        ret = ret with { Lines = [ret.Lines[0] with { TreatmentSignature = signature }] };
        var result = await f.Execute(ret);
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
        parent = await db.InterCrewTransfers.AsNoTracking().SingleAsync();
        Assert.Equal(before, ReceiveSnapshot(parent));
        Assert.Equal(InterCrewTransferStatuses.Reversed, parent.Status);
        Assert.Equal(ret.OperationKey, parent.ReversalOperationKey);
        Assert.Equal(8000, parent.ReversedByUserId);
        Assert.NotNull(parent.ReversedAt);
        Assert.Equal(19, await f.Physical()); Assert.Equal(0, await db.RoomInventoryAdjustments.Where(x => x.RoomId == 9007).SumAsync(x => x.ChangeAmount));
        Assert.Equal(1, await db.RoomInventoryAdjustments.CountAsync(x => x.AdjustmentType == "ReceiptAdd"));
        Assert.Equal(immutable, JsonSerializer.Serialize(new
        {
            Receipts = await db.Receipts.AsNoTracking().ToArrayAsync(),
            Applications = await db.RoomTreatmentApplications.AsNoTracking().ToArrayAsync(),
            Sources = await db.RoomTreatmentApplicationSources.AsNoTracking().ToArrayAsync(),
            Ledger = await db.RoomInventoryAdjustments.AsNoTracking().Where(x => ledgerIds.Contains(x.Id)).OrderBy(x => x.Id).ToArrayAsync(),
            Movements = await db.TreatmentLineageMovements.AsNoTracking().Where(x => movementIds.Contains(x.Id)).OrderBy(x => x.Id).ToArrayAsync()
        }));
        Assert.Equal(audits, JsonSerializer.Serialize(await db.AuditLogs.AsNoTracking().Where(x => auditIds.Contains(x.Id)).OrderBy(x => x.Id).ToArrayAsync()));
        var source = Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow)).Positions);
        Assert.Equal(signature, Assert.Single(source.TreatmentSlices).Signature);
        var readiness = await new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance).VerifyReadinessAsync(default);
        Assert.True(readiness.IsReady, JsonSerializer.Serialize(readiness.Issues));
        Assert.True((await new TreatmentLineageReadinessService(new CropQc.Web.Services.RoomInventoryLedgerQueryService(db), db).VerifyAsync(default)).Success);
        Assert.Empty((await InterCrewTopologyReadiness.VerifyAsync(db, default)).Issues);
        var saved = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Replayed, (await f.Execute(ret)).Status);
        Assert.Equal(InventoryCommandStatus.Conflict, (await f.Execute(ret with { Reason = "Changed retry" })).Status);
        Assert.NotEqual(InventoryCommandStatus.Committed, (await f.Execute(ret with { OperationKey = Guid.NewGuid().ToString("N") })).Status);
        Assert.Equal(saved, await f.Snapshot());
    }

    internal static string ReceiveSnapshot(InterCrewTransfer parent) => JsonSerializer.Serialize(new
    { parent.BinsReceived, parent.ReceivedAt, parent.ReceivedByUserId, parent.DestinationRoomId, parent.DestinationWarehouseId, parent.ReceiveOperationKey, parent.VarianceBins });
}
