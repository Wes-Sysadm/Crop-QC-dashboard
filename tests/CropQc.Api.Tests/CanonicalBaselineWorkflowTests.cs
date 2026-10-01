using CropQc.Data;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class CanonicalBaselineWorkflowTests
{
    [InventoryPostgresFact]
    public async Task Import_and_room_move_compete_through_normal_services_without_double_use()
    {
        await using var f = await Fixture.Create();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var left = factory.CreateDbContext();
        await using var right = factory.CreateDbContext();
        var arrived = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Observer Observe(string target) => new(async (stage, attempt) =>
        {
            if (stage != target || attempt != 1) return;
            if (Interlocked.Increment(ref arrived) == 2) gate.SetResult();
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(45));
        });
        var import = Service(left, new InventoryCommandExecutor(factory, Observe("BaselineResolved")));
        var form = await Prepare(import, Csv((100000, 24)));
        var move = CanonicalReceivingWorkflowTests.Dashboard(right, new InventoryCommandExecutor(factory, Observe("Resolved")), CanonicalReceivingWorkflowTests.Operator());
        var option = Assert.Single((await move.GetRoomDetailAsync(9002, default)).TransferLotOptions);
        var transfer = new RoomTransferForm
        {
            FromRoomId = 9002,
            DestinationRoomId = 9003,
            DestinationWarehouseId = 9001,
            SourceLotKey = option.LotKey,
            TreatmentSignature = option.TreatmentSignature,
            BinCount = 19,
            TransferAt = DateTimeOffset.UtcNow,
            Reason = "Local baseline race"
        };
        var imports = import.ApplyAsync(form, "canonical-test@example.invalid", default);
        var moves = move.CreateRoomTransferAsync(transfer, default);
        await Task.WhenAll(imports, moves);
        var imported = (await imports).Error == null;
        var moved = await moves == null;
        Assert.True(imported != moved, $"Import: {(await imports).Error}; Move: {await moves}");
        Assert.Equal(imported ? 24 : 0, await f.Physical());
        Assert.Equal(moved ? 19 : 0, await f.Physical(9003));
        await using var check = f.CreateDbContext();
        Assert.Equal(1, await check.InventoryCommands.CountAsync());
        Assert.Equal(1, await check.AuditLogs.CountAsync(x => x.Action == "CanonicalInventoryNormalization"));
        Assert.Equal(imported ? 1 : 0, await check.RoomInventoryAdjustments.CountAsync(x => x.AdjustmentType == "StartingInventoryImport"));
        Assert.All(await check.TreatmentLineageSegments.ToListAsync(), x => Assert.True(x.CurrentBins >= 0));
    }

    [InventoryPostgresFact]
    public async Task Unaffected_later_receipt_and_its_stale_projection_are_not_normalized_by_another_lots_import()
    {
        await using var f = await Fixture.Create(2);
        await using (var seed = f.CreateDbContext())
        {
            (await seed.Receipts.SingleAsync(x => x.Id == 100001)).ReceivedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
            await seed.SaveChangesAsync();
        }
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var protectedProjection = System.Text.Json.JsonSerializer.Serialize(await db.TreatmentLineageSegments.AsNoTracking().SingleAsync(x => x.Id == 100001));
        var service = Service(db, new InventoryCommandExecutor(factory));
        var form = await Prepare(service, Csv((100000, 24)));
        Assert.Null((await service.ApplyAsync(form, "canonical-test@example.invalid", default)).Error);
        Assert.Equal(43, await f.Physical());
        Assert.Equal(protectedProjection, System.Text.Json.JsonSerializer.Serialize(await db.TreatmentLineageSegments.AsNoTracking().SingleAsync(x => x.Id == 100001)));
        Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.Action == "CanonicalInventoryNormalization"));
    }

    [InventoryPostgresFact]
    public async Task Preview_is_read_only_and_import_preserves_other_lots_history_and_replay()
    {
        await using var f = await Fixture.Create(2);
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var capture = new Capture(new InventoryCommandExecutor(factory));
        var service = Service(db, capture);
        var before = await f.Snapshot();
        var form = await Prepare(service, Csv((100000, 24), (100001, 19)));
        Assert.Equal(before, await f.Snapshot());
        var preview = await service.PreviewAsync(form, default);
        Assert.Equal(form.ExpectedFingerprint, preview.ExpectedFingerprint);
        Assert.Equal(new[] { (19, 24), (19, 19) }, preview.CanonicalReview!.Positions.Select(x => (x.Before, x.After)));
        var applied = await service.ApplyAsync(form, "canonical-test@example.invalid", default);
        Assert.True(applied.Error == null, capture.Last?.Detail ?? applied.Error);
        Assert.Equal(43, await f.Physical());
        var after = await f.Snapshot();
        Assert.Null((await service.ApplyAsync(form, "canonical-test@example.invalid", default)).Error);
        Assert.Equal(after, await f.Snapshot());
        form.CsvText = Csv((100000, 25), (100001, 19));
        Assert.NotNull((await service.ApplyAsync(form, "canonical-test@example.invalid", default)).Error);
        Assert.Equal(after, await f.Snapshot());
        await using var check = f.CreateDbContext();
        Assert.Equal(5, await check.TreatmentLineageSegments.Where(x => x.Disposition == "Current" && x.TreatmentState == "Unknown").SumAsync(x => x.CurrentBins));
        Assert.Equal(2, await check.RoomInventoryAdjustments.CountAsync(x => x.AdjustmentType == "ReceiptAdd"));
        Assert.Equal(38, await check.Receipts.SumAsync(x => x.BinCount));
        Assert.Equal(1, await check.AuditLogs.CountAsync(x => x.Action == "CanonicalBaselineImport"));
        Assert.Equal(1, await check.InventoryCommands.CountAsync());
        Assert.Equal(2, await check.TreatmentLineageSegments.CountAsync(x => x.Disposition == "Historical" && x.RetiredQuantity == 29));
        Assert.Equal(1, await check.TreatmentLineageMovements.CountAsync(x => x.MovementType == "BaselineImport" && x.BinCount == 5 && x.ReceiptId == null));
        var replacement = await Prepare(service, Csv((100000, 26), (100001, 19)));
        Assert.True((await service.PreviewAsync(replacement, default)).RequiresReplaceConfirmation);
        replacement.ConfirmReplaceExistingBatch = false;
        Assert.NotNull((await service.ApplyAsync(replacement, "canonical-test@example.invalid", default)).Error);
        Assert.Equal(after, await f.Snapshot());
        replacement.ConfirmReplaceExistingBatch = true;
        Assert.Null((await service.ApplyAsync(replacement, "canonical-test@example.invalid", default)).Error);
        Assert.Equal(45, await f.Physical());
    }

    [InventoryPostgresFact]
    public async Task Missing_unrelated_lot_reduction_and_changed_review_are_rejected_without_writes()
    {
        await using var f = await Fixture.Create(2);
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var service = Service(db, new InventoryCommandExecutor(factory));
        var before = await f.Snapshot();
        var missing = await service.PreviewAsync(new() { CsvText = Csv((100000, 24)) }, default);
        Assert.False(missing.CanApply); Assert.Contains("CORPUS-100001", missing.CanonicalError);
        var reduction = await service.PreviewAsync(new() { CsvText = Csv((100000, 18), (100001, 19)) }, default);
        Assert.False(reduction.CanApply);
        var reviewed = await Prepare(service, Csv((100000, 24), (100001, 19)));
        reviewed.CsvText = Csv((100000, 25), (100001, 19));
        Assert.NotNull((await service.ApplyAsync(reviewed, "canonical-test@example.invalid", default)).Error);
        Assert.Equal(before, await f.Snapshot());
    }

    [InventoryPostgresFact]
    public async Task New_empty_room_import_has_no_invented_receipt_or_untreated_history()
    {
        await using var f = await Fixture.Create(2);
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var service = Service(db, new InventoryCommandExecutor(factory));
        var csv = Csv((100000, 13), (100001, 17)).Replace("R9002", await db.Rooms.Where(x => x.Id == 9003).Select(x => x.Code).SingleAsync());
        var form = await Prepare(service, csv);
        Assert.Null((await service.ApplyAsync(form, "canonical-test@example.invalid", default)).Error);
        Assert.Equal(38, await f.Physical()); Assert.Equal(30, await f.Physical(9003));
        var created = await db.TreatmentLineageSegments.AsNoTracking().Where(x => x.RoomId == 9003).ToListAsync();
        Assert.Equal(2, created.Count);
        Assert.All(created, x => { Assert.Equal("Unknown", x.TreatmentState); Assert.Null(x.ReceiptId); });
        Assert.Equal(new[] { 13, 17 }, created.Select(x => x.CurrentBins).Order());
    }

    [InventoryPostgresFact]
    public async Task Import_rollback_restores_normalization_baseline_movement_and_journal()
    {
        await using var f = await Fixture.Create();
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        foreach (var stage in new[] { "NormalizationAudit", "BaselineLedger", "Movement", "OperationAudit", "BeforeCommit" })
        {
            var reached = false;
            var executor = new InventoryCommandExecutor(factory, new Observer((at, _) =>
            {
                if (at != stage) return Task.CompletedTask;
                reached = true;
                return Task.FromException(new InvalidOperationException("Local baseline rollback injection"));
            }));
            var service = Service(db, executor);
            var form = await Prepare(service, Csv((100000, 24)));
            var before = await f.Snapshot();
            Assert.NotNull((await service.ApplyAsync(form, "canonical-test@example.invalid", default)).Error);
            Assert.True(reached, stage);
            Assert.Equal(before, await f.Snapshot());
        }
    }

    internal static RoomInventoryImportService Service(CropQcDbContext db, IInventoryCommandExecutor executor) =>
        new(db, new Environment(), new CropYearService(db, new ConfigurationBuilder().Build()), canonicalCommands: executor);

    internal static async Task<RoomInventoryImportForm> Prepare(RoomInventoryImportService service, string csv)
    {
        var preview = await service.PreviewAsync(new() { CsvText = csv }, default);
        Assert.True(preview.CanApply, preview.CanonicalError ?? string.Join("; ", preview.Rows.Select(x => x.Message)));
        return new()
        {
            CsvText = csv,
            ConfirmImport = true,
            ConfirmReplaceExistingBatch = true,
            OperationKey = preview.OperationKey,
            ExpectedFingerprint = preview.ExpectedFingerprint
        };
    }

    internal static string Csv(params (int Lot, int Quantity)[] rows) =>
        "CropYear,Warehouse,RoomCode,Grower,Lot,Variety,Bins,Status,EffectiveDate,Notes\n" + string.Join("\n", rows.Select(x =>
            $"2026,WP,R9002,Corpus,CORPUS-{x.Lot},CGAL,{x.Quantity},,{DateTimeOffset.UtcNow.AddDays(-1):yyyy-MM-dd},Local reviewed baseline"));

    private sealed class Environment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "CropQc.Web";
        public string EnvironmentName { get; set; } = "Development";
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class Capture(IInventoryCommandExecutor next) : IInventoryCommandExecutor
    {
        public InventoryCommandResult? Last { get; private set; }
        public async Task<InventoryCommandResult> ExecuteAsync(InventoryCommand command, CancellationToken cancellationToken = default) =>
            Last = await next.ExecuteAsync(command, cancellationToken);
    }
}
