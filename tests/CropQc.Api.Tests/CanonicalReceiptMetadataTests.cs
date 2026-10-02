using CropQc.Api.Dtos;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class CanonicalReceiptMetadataTests
{
    [InventoryPostgresFact]
    public async Task Web_and_API_metadata_edits_preserve_all_stock_and_replay_with_reviewed_versions()
    {
        await using var f = await Fixture.Create();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        await CanonicalOperatorSessionTests.GrantReceivingAsync(db);
        var executor = new InventoryCommandExecutor(factory);
        var receiving = new CanonicalReceivingService(db, executor);
        var creation = await receiving.ReceiveAsync(Guid.NewGuid().ToString("N"), 8000, 2026, DateTimeOffset.UtcNow,
            9001, 9002, 9004, 100000, "", "LOCAL-METADATA", 7, "local receiving", default);
        var id = creation.Effects.Single().ParentId!.Value;
        var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, executor);
        var form = await CanonicalReceiptCorrectionWorkflowTests.Form(db, service, id, 7);
        form.ReceiptVersion = form.ExpectedConcurrencyVersion;
        form.CompuTechReceiptId = "LOCAL-METADATA-EDITED";
        form.ReceivedAt = form.ReceivedAt.AddMinutes(-1);
        var filters = new Dictionary<string, string> { ["Receipts"] = "\"Id\" <> " + id, ["AuditLogs"] = "false", ["InventoryCommands"] = "false" };
        var physical = await f.Snapshot(filters);
        var web = CanonicalReceivingWorkflowTests.Dashboard(db, executor);
        Assert.Null(await web.UpdateReceiptAsync(form, default));
        Assert.Equal(physical, await f.Snapshot(filters));
        var saved = await f.Snapshot();
        Assert.Null(await web.UpdateReceiptAsync(form, default));
        Assert.Equal(saved, await f.Snapshot());
        var row = await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == id);
        Assert.Equal(2, row.ConcurrencyVersion);
        Assert.Equal(form.CompuTechReceiptId, row.CompuTechReceiptId);
        var http = CanonicalReceivingWorkflowTests.Operator();
        var api = new CropQc.Api.Services.ReceiptService(db, new CropQc.Api.Services.AuditService(db), receiving, http, executor);
        var request = new UpdateReceiptRequest(row.CropYear, row.ReceivedAt.AddMinutes(-1), row.WarehouseId, row.RoomId, row.FruitProfileId,
            row.GrowerName, row.LotCode, row.BinCount, "Local API metadata edit", Guid.NewGuid().ToString("N"), row.ConcurrencyVersion);
        var update = await api.UpdateSameDayAsync(id, request, default);
        Assert.Null(update.Error); Assert.Equal(3, update.Receipt!.ConcurrencyVersion);
        Assert.Equal(physical, await f.Snapshot(filters));
        saved = await f.Snapshot();
        Assert.Null((await api.UpdateSameDayAsync(id, request, default)).Error);
        Assert.Equal(saved, await f.Snapshot());
        Assert.NotNull((await api.UpdateSameDayAsync(id, request with { OperationKey = Guid.NewGuid().ToString("N"), BinCount = 8 }, default)).Error);
        Assert.Equal(saved, await f.Snapshot());
        Assert.Equal(2, await db.AuditLogs.CountAsync(x => x.Action == "CanonicalReceiptMetadataUpdated"));
    }
}
