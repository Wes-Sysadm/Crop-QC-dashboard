using CropQc.Data;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class CanonicalReceiptCorrectionWorkflowTests
{
    [InventoryPostgresFact]
    public async Task Correction_failure_rolls_back_receipt_projection_ledger_override_and_audits()
    {
        foreach (var stage in new[] { "Movement", "OperationAudit", "BeforeCommit" })
        {
            await using var f = await Fixture.Create();
            var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
            await using var db = factory.CreateDbContext();
            var executor = new InventoryCommandExecutor(factory, new Observer((at, _) =>
                at == stage ? Task.FromException(new InvalidOperationException("Local rollback injection")) : Task.CompletedTask));
            var service = Service(db, executor);
            var form = await Form(db, service, 100000, 17);
            var before = await f.Snapshot();
            Assert.NotNull((await service.ApplyEditAsync(form, CanonicalReceivingWorkflowTests.Operator().HttpContext!.User, default)).Error);
            Assert.Equal(before, await f.Snapshot());
        }
    }

    [InventoryPostgresFact]
    public async Task Quantity_correction_and_void_change_only_exact_receipt_stock_with_atomic_audits_and_replay()
    {
        await using var f = await Fixture.Create();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        var received = await new CanonicalReceivingService(db, executor).ReceiveAsync(Guid.NewGuid().ToString("N"), 8000, 2026,
            DateTimeOffset.UtcNow, 9001, 9002, 9004, 100000, "", "LOCAL-CORRECT", 7, "local receipt", default);
        Assert.Equal(InventoryCommandStatus.Committed, received.Status);
        var id = received.Effects.Single().ParentId!.Value;
        var service = Service(db, executor);
        var principal = CanonicalReceivingWorkflowTests.Operator().HttpContext!.User;
        var form = await Form(db, service, id, 10);
        var applied = await service.ApplyEditAsync(form, principal, default);
        Assert.Null(applied.Error);
        Assert.Equal(29, await f.Physical());
        var saved = await f.Snapshot();
        Assert.True((await service.ApplyEditAsync(form, principal, default)).WasIdempotent);
        Assert.Equal(saved, await f.Snapshot());
        form.BinCount = 11;
        Assert.NotNull((await service.ApplyEditAsync(form, principal, default)).Error);
        Assert.Equal(saved, await f.Snapshot());
        form = await Form(db, service, id, 6);
        Assert.Null((await service.ApplyEditAsync(form, principal, default)).Error);
        Assert.Equal(25, await f.Physical());
        var preview = (await service.GetPreviewAsync(id, default))!;
        Assert.Null(preview.CanonicalBlocker); Assert.Equal(6, preview.CurrentInventory);
        var deletion = new DeleteReceiptForm
        {
            Id = id,
            ExpectedConcurrencyVersion = preview.ConcurrencyVersion,
            ExpectedInventoryStateToken = preview.InventoryStateToken,
            ConfirmationValue = "LOCAL-CORRECT",
            ConfirmDeletion = true,
            ConfirmInventoryChange = true,
            Reason = "Void local receipt",
            OperationToken = Guid.NewGuid().ToString("N")
        };
        Assert.Null((await service.VoidAsync(deletion, principal, default)).Error);
        Assert.Equal(19, await f.Physical());
        Assert.Equal(19, await db.TreatmentLineageSegments.Where(x => x.Disposition == "Current" && x.ReceiptId != id).SumAsync(x => x.CurrentBins));
        Assert.Equal(0, await db.TreatmentLineageSegments.Where(x => x.Disposition == "Current" && x.ReceiptId == id).SumAsync(x => x.CurrentBins));
        Assert.True((await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == id)).IsDeleted);
        Assert.Equal(3, await db.ReceiptInventoryOverrides.CountAsync(x => x.ReceiptId == id && x.IsComplete));
        Assert.Single(await db.ReceiptDeletionAudits.Where(x => x.DeletedReceiptId == id).ToListAsync());
        saved = await f.Snapshot();
        Assert.True((await service.VoidAsync(deletion, principal, default)).WasIdempotent);
        Assert.Equal(saved, await f.Snapshot());
        Assert.Equal(29, await db.TreatmentLineageSegments.Where(x => x.Disposition == "Historical" && x.ReceiptId != id).SumAsync(x => x.RetiredQuantity ?? 0));
    }

    internal static ReceiptInventoryOverrideService Service(CropQcDbContext db, IInventoryCommandExecutor executor) =>
        new(db, new CanonicalOutsideWorkflowTests.Access(), null!, null!, null!, null!, null!,
            NullLogger<ReceiptInventoryOverrideService>.Instance, canonicalCommands: executor);

    internal static async Task<AdminReceiptInventoryOverrideForm> Form(CropQcDbContext db, ReceiptInventoryOverrideService service, long id, int quantity)
    {
        var r = await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == id);
        var p = (await service.GetPreviewAsync(id, default))!;
        Assert.Null(p.CanonicalBlocker);
        return new()
        {
            Id = id,
            OperationKey = Guid.NewGuid().ToString("N"),
            ExpectedConcurrencyVersion = p.ConcurrencyVersion,
            ExpectedInventoryStateToken = p.InventoryStateToken,
            CropYear = r.CropYear,
            ConfirmCropYear = true,
            GrowerLotId = r.GrowerLotId,
            FruitProfileId = r.FruitProfileId,
            WarehouseId = r.WarehouseId,
            RoomId = r.RoomId,
            ReceivedAt = r.ReceivedAt,
            CompuTechReceiptId = r.CompuTechReceiptId,
            ReceiptType = r.ReceiptType,
            GrowerNumber = r.GrowerNumber ?? r.LotCode,
            GrowerName = r.GrowerName,
            LotCode = r.LotCode,
            BinCount = quantity,
            ConfirmAdditionalBinsUntreated = quantity > r.BinCount,
            ConfirmInventoryChange = true,
            Reason = "Local correction"
        };
    }
}
