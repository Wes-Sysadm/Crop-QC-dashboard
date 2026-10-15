using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class CanonicalReceiptIdentityWorkflowTests
{
    [InventoryPostgresFact]
    public async Task Identity_restoration_migration_changes_only_indexes_and_prevents_downgrade_after_commands()
    {
        await using var f = await Fixture.Create();
        await using var db = f.CreateDbContext();
        var migration = new CropQc.Data.Migrations.CanonicalIdentityRestorationSides { ActiveProvider = "Npgsql.EntityFrameworkCore.PostgreSQL" };
        var generator = db.GetService<IMigrationsSqlGenerator>();
        var before = await f.Snapshot();
        // EnsureCreated uses Npgsql convention-truncated names, while the existing
        // cross-provider migrations created SQL Server-named indexes truncated by PG.
        // Reconstruct those exact pre-migration definitions before testing Up.
        var parents = new[] { "OutsideWarehouseTransferId", "ProcessorShipmentLineId", "RoomInventoryLossId", "RoomTransferId" };
        var indexes = db.Model.FindEntityType(typeof(CropQc.Data.Entities.RoomInventoryAdjustment))!.GetIndexes()
            .Where(x => x.Properties.Count == 2 && parents.Contains(x.Properties[0].Name) && x.Properties[1].Name == "AdjustmentType");
        foreach (var index in indexes)
            foreach (var command in generator.Generate([new DropIndexOperation { Name = index.GetDatabaseName()!, Table = "RoomInventoryAdjustments" }], db.Model))
                await db.Database.ExecuteSqlRawAsync(command.CommandText);
        foreach (var command in generator.Generate(migration.DownOperations.OfType<CreateIndexOperation>().Cast<MigrationOperation>().ToArray(), db.Model))
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
        Assert.Equal(before, await f.Snapshot());
        foreach (var command in generator.Generate(migration.UpOperations, db.Model))
            await db.Database.ExecuteSqlRawAsync(command.CommandText);
        Assert.Equal(before, await f.Snapshot());
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        Assert.Equal(InventoryCommandStatus.Committed, (await new InventoryCommandExecutor(factory).ExecuteAsync(await f.Command(InventoryCommandKind.Loss, 1))).Status);
        var committed = await f.Snapshot();
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            var error = await Assert.ThrowsAsync<PostgresException>(async () =>
            {
                foreach (var command in generator.Generate(migration.DownOperations, db.Model))
                    await db.Database.ExecuteSqlRawAsync(command.CommandText);
            });
            Assert.Contains("retain identity-restoration schema", error.MessageText);
            await transaction.RollbackAsync();
        }
        Assert.Equal(committed, await f.Snapshot());
    }
    [InventoryPostgresFact]
    public Task Normal_run_cancellation_restores_mixed_consumption_to_current_receipt_identities() => RestoreCorrectedConsumption(false);

    [InventoryPostgresFact]
    public Task Normal_loss_reversal_restores_mixed_consumption_to_current_receipt_identities() => RestoreCorrectedConsumption(true);

    [InventoryPostgresFact]
    public async Task Normal_run_edit_can_select_corrected_restoration_credit_without_inventing_physical_stock()
    {
        await using var f = await Fixture.Create();
        await using (var seed = f.CreateDbContext())
        {
            seed.GrowerLots.Add(new() { Id = 100001, Grower = "Corrected grower", LotNumber = "NEW-TARGET" });
            var actor = await seed.Users.SingleAsync(x => x.Id == 8000);
            actor.EmploymentFacility = "WP"; actor.EmploymentEffectiveAt = DateTimeOffset.UtcNow.AddDays(-1);
            await seed.SaveChangesAsync();
        }
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory, runExpectations: new CanonicalRunExpectationWriter());
        var captured = new Capture(executor);
        var received = await Receive(db, executor);
        var principal = CanonicalReceivingWorkflowTests.Operator().HttpContext!.User;
        var service = new BinsRunService(db, new CanonicalOutsideWorkflowTests.Access(), NullLogger<BinsRunService>.Instance, canonicalCommands: captured);
        var option = Assert.Single((await service.GetPageAsync(new() { Section = "Actual", WarehouseId = 9001, RoomIds = [9002] }, principal, default)).AvailableInventory);
        var initial = new ActualRunForm
        {
            RunFacilityWarehouseId = 9001,
            RunAt = DateTimeOffset.UtcNow,
            SalesDeskId = await db.SalesDesks.Where(x => x.IsActive).Select(x => x.Id).FirstAsync(),
            Lines = [new() { InventoryKey = option.InventoryKey, TreatmentSignature = option.TreatmentSignature,
                CanonicalFingerprint = option.CanonicalFingerprint, BinsRun = 26, ExpectedAvailableBins = 26 }]
        };
        Assert.Null(await service.CreateActualRunAsync(initial, principal, default));
        var correction = CanonicalReceiptCorrectionWorkflowTests.Service(db, executor);
        Assert.Null((await correction.ApplyEditAsync(await Correction(db, correction, received), principal, default)).Error);
        Assert.Equal(0, await f.Physical());
        var run = await db.ActualRuns.AsNoTracking().SingleAsync();
        var beforeRead = await f.Snapshot();
        var page = await service.GetPageAsync(new() { Section = "Actual", EditActualRunId = run.Id }, principal, default);
        Assert.Equal(beforeRead, await f.Snapshot());
        Assert.Equal(19, page.AvailableInventory.Single(x => x.GrowerLotId == 100000).CurrentBins);
        Assert.Equal(7, page.AvailableInventory.Single(x => x.GrowerLotId == 100001).CurrentBins);
        var edit = new ActualRunForm
        {
            Id = run.Id,
            ConcurrencyVersion = run.ConcurrencyVersion,
            RunFacilityWarehouseId = 9001,
            RunAt = initial.RunAt,
            SalesDeskId = initial.SalesDeskId,
            CorrectionReason = "Local corrected lot allocation",
            Lines = page.AvailableInventory.Select(x => new ActualRunLineForm
            {
                InventoryKey = x.InventoryKey,
                TreatmentSignature = x.TreatmentSignature,
                CanonicalFingerprint = x.CanonicalFingerprint,
                ExpectedAvailableBins = x.CurrentBins,
                BinsRun = x.GrowerLotId == 100000 ? 19 : 5
            }).ToList()
        };
        var error = await service.UpdateActualRunAsync(run.Id, edit, principal, default);
        Assert.True(error == null, $"{error}: {captured.Last}");
        Assert.Equal(2, await f.Physical());
        var saved = await f.Snapshot(); Assert.Null(await service.UpdateActualRunAsync(run.Id, edit, principal, default)); Assert.Equal(saved, await f.Snapshot());
        run = await db.ActualRuns.AsNoTracking().SingleAsync();
        Assert.Null(await service.CancelActualRunAsync(new() { Id = run.Id, ConcurrencyVersion = run.ConcurrencyVersion, Reason = "Local cancellation" }, principal, default));
        Assert.Equal(26, await f.Physical());
        var room = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow);
        Assert.Equal(19, room.Positions.Single(x => x.Identity.GrowerLotId == 100000).AuthoritativeQuantity);
        Assert.Equal(7, room.Positions.Single(x => x.Identity.GrowerLotId == 100001).AuthoritativeQuantity);
    }

    private static async Task RestoreCorrectedConsumption(bool loss)
    {
        await using var f = await Fixture.Create(2);
        await using (var seed = f.CreateDbContext())
        {
            var actor = await seed.Users.SingleAsync(x => x.Id == 8000);
            actor.EmploymentFacility = "WP"; actor.EmploymentEffectiveAt = DateTimeOffset.UtcNow.AddDays(-1);
            await seed.SaveChangesAsync();
        }
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory, runExpectations: new CanonicalRunExpectationWriter());
        var captured = new Capture(executor);
        var received = await Receive(db, executor);
        var principal = CanonicalReceivingWorkflowTests.Operator().HttpContext!.User;
        Func<Task<string?>> reverse;
        if (loss)
        {
            var service = new RoomInventoryLossService(db, new CropQc.Web.Services.RoomInventoryLedgerQueryService(db),
                new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance), new CanonicalOutsideWorkflowTests.Access(),
                new CanonicalGrowerService(db), CanonicalReceivingWorkflowTests.Operator(), new PacificBusinessTimeService(new SystemClock()),
                NullLogger<RoomInventoryLossService>.Instance, canonicalCommands: captured);
            var option = (await service.GetRoomDataAsync(9002, default)).Options.Single(x => x.CurrentBins == 26);
            Assert.Null(await service.CreateAsync(new()
            {
                RoomId = 9002,
                InventoryAdjustmentId = option.InventoryAdjustmentId,
                ExpectedCurrentBins = 26,
                TreatmentSignature = option.TreatmentSignature,
                CanonicalFingerprint = option.CanonicalFingerprint!,
                BinCount = 26
            }, default));
            var id = await db.RoomInventoryLosses.Select(x => x.Id).SingleAsync();
            var form = new ReverseRoomInventoryLossForm { Id = id, Reason = "Local restore after receipt correction" };
            reverse = () => service.ReverseAsync(form, default);
        }
        else
        {
            var service = new BinsRunService(db, new CanonicalOutsideWorkflowTests.Access(), NullLogger<BinsRunService>.Instance, canonicalCommands: captured);
            var option = (await service.GetPageAsync(new() { Section = "Actual", WarehouseId = 9001, RoomIds = [9002] }, principal, default))
                .AvailableInventory.Single(x => x.CurrentBins == 26);
            Assert.Null(await service.CreateActualRunAsync(new()
            {
                RunFacilityWarehouseId = 9001,
                RunAt = DateTimeOffset.UtcNow,
                SalesDeskId = await db.SalesDesks.Where(x => x.IsActive).Select(x => x.Id).FirstAsync(),
                Lines = [new() { InventoryKey = option.InventoryKey, TreatmentSignature = option.TreatmentSignature,
                    CanonicalFingerprint = option.CanonicalFingerprint, BinsRun = 26, ExpectedAvailableBins = 26 }]
            }, principal, default));
            var run = await db.ActualRuns.AsNoTracking().SingleAsync();
            var form = new CancelActualRunForm { Id = run.Id, ConcurrencyVersion = run.ConcurrencyVersion, Reason = "Local restore after receipt correction" };
            reverse = () => service.CancelActualRunAsync(form, principal, default);
        }
        Assert.Equal(19, await f.Physical());
        var history = await db.TreatmentLineageMovements.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
        var retainedHistory = await CaptureHistory(db);
        var retainedBefore = await retainedHistory();
        var historicalIds = history.Select(x => x.Id).ToArray();
        var correction = CanonicalReceiptCorrectionWorkflowTests.Service(db, executor);
        Assert.Null((await correction.ApplyEditAsync(await Correction(db, correction, received), principal, default)).Error);
        Assert.Equal(19, await f.Physical());
        if (!loss)
        {
            var beforePreview = await f.Snapshot();
            var batch = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow);
            var options = await new InventoryRunCorrectionAvailability(db).ReadAsync(batch, await db.ActualRuns.Select(x => x.Id).SingleAsync(), default);
            Assert.All(options.Values, x => Assert.Null(x.Blocker));
            Assert.Equal(19, options.Values.Single(x => x.Current.Identity.GrowerLotId == 100000).AvailableAfterOwnReversal.Sum(x => x.Quantity));
            Assert.Equal(26, options.Values.Single(x => x.Current.Identity.GrowerLotId == 100001).AvailableAfterOwnReversal.Sum(x => x.Quantity));
            Assert.Equal(beforePreview, await f.Snapshot());
        }
        var error = await reverse(); Assert.True(error == null, $"{error}: {captured.Last}");
        Assert.Equal(45, await f.Physical());
        var room = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow);
        Assert.Equal(19, room.Positions.Single(x => x.Identity.GrowerLotId == 100000).AuthoritativeQuantity);
        Assert.Equal(26, room.Positions.Single(x => x.Identity.GrowerLotId == 100001).AuthoritativeQuantity);
        AssertPosition(room.Positions.Single(x => x.Identity.GrowerLotId == 100000), 19, 0);
        AssertPosition(room.Positions.Single(x => x.Identity.GrowerLotId == 100001), 26, 10);
        await AssertReceipt(db, received, 7, 0, 100001);
        Assert.Equal(JsonSerializer.Serialize(history), JsonSerializer.Serialize(await db.TreatmentLineageMovements.AsNoTracking().Where(x => historicalIds.Contains(x.Id)).OrderBy(x => x.Id).ToArrayAsync()));
        var saved = await f.Snapshot(); Assert.Null(await reverse()); Assert.Equal(saved, await f.Snapshot());
        Assert.Equal(retainedBefore, await retainedHistory());
    }

    [InventoryPostgresFact]
    public async Task Exact_receipt_reclassification_merges_into_existing_identity_without_changing_other_receipts_or_history()
    {
        await using var f = await Fixture.Create(2);
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        var received = await Receive(db, executor);
        var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, executor);
        var form = await Correction(db, service, received);
        var retainedHistory = await CaptureHistory(db);
        var retainedBefore = await retainedHistory();
        var oldReceipts = JsonSerializer.Serialize(await db.Receipts.AsNoTracking().Where(x => x.Id != received).OrderBy(x => x.Id).ToArrayAsync());
        var oldMovementIds = await db.TreatmentLineageMovements.Select(x => x.Id).ToArrayAsync();
        var oldMovements = JsonSerializer.Serialize(await db.TreatmentLineageMovements.AsNoTracking().Where(x => oldMovementIds.Contains(x.Id)).OrderBy(x => x.Id).ToArrayAsync());
        Assert.Null((await service.ApplyEditAsync(form, CanonicalReceivingWorkflowTests.Operator().HttpContext!.User, default)).Error);
        Assert.Equal(45, await f.Physical());
        var inventory = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow);
        Assert.Equal(19, inventory.Positions.Single(x => x.Identity.GrowerLotId == 100000).AuthoritativeQuantity);
        Assert.Equal(26, inventory.Positions.Single(x => x.Identity.GrowerLotId == 100001).AuthoritativeQuantity);
        AssertPosition(inventory.Positions.Single(x => x.Identity.GrowerLotId == 100000), 19, 10);
        AssertPosition(inventory.Positions.Single(x => x.Identity.GrowerLotId == 100001), 26, 10);
        await AssertReceipt(db, received, 7, 0, 100001);
        Assert.Equal(oldReceipts, JsonSerializer.Serialize(await db.Receipts.AsNoTracking().Where(x => x.Id != received).OrderBy(x => x.Id).ToArrayAsync()));
        Assert.Equal(oldMovements, JsonSerializer.Serialize(await db.TreatmentLineageMovements.AsNoTracking().Where(x => oldMovementIds.Contains(x.Id)).OrderBy(x => x.Id).ToArrayAsync()));
        var saved = await f.Snapshot();
        Assert.True((await service.ApplyEditAsync(form, CanonicalReceivingWorkflowTests.Operator().HttpContext!.User, default)).WasIdempotent);
        Assert.Equal(saved, await f.Snapshot());
        var cycle = await CanonicalReceiptCorrectionWorkflowTests.Form(db, service, received, 7); cycle.GrowerLotId = 100000;
        Assert.NotNull((await service.ApplyEditAsync(cycle, CanonicalReceivingWorkflowTests.Operator().HttpContext!.User, default)).Error);
        Assert.Equal(saved, await f.Snapshot());
        var correction = Assert.Single(await db.InventoryIdentityCorrections.ToListAsync());
        Assert.Equal(received, correction.CorrectedReceiptId); Assert.True(correction.IsComplete);
        Assert.Equal(2, correction.ExpectedAdjustmentCount); Assert.Equal(2, correction.ExpectedTreatmentMovementCount);
        Assert.Equal(retainedBefore, await retainedHistory());
    }

    [InventoryPostgresFact]
    public async Task Receipt_correction_splits_mixed_outside_custody_and_normal_return_restores_each_current_identity()
    {
        await using var f = await Fixture.Create(2);
        await using (var seed = f.CreateDbContext())
        {
            seed.OutsideWarehouses.Add(new() { Id = 9005, Code = "IDENTITY-OUT", Name = "Local identity outside", Address = "Local" });
            await seed.SaveChangesAsync();
        }
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        var received = await Receive(db, executor);
        var captured = new Capture(executor);
        var outside = CanonicalTruckReceiptWorkflowTests.Inventory(db, captured);
        var source = (await outside.GetInventoryAsync(default)).Single(x => x.AvailableBins == 26);
        var sent = await outside.CreateAsync(new()
        {
            OutsideWarehouseId = 9005,
            SourceKey = source.SourceKey,
            BinCount = 26,
            ExpectedAvailableBins = 26,
            // This form accepts Pacific business time, not the runner's local zone.
            TransferredAt = new PacificBusinessTimeService(new SystemClock()).NowPacific.DateTime,
            ConfirmedReview = true,
            TruckLoadBolNumber = "LOCAL-IDENTITY-RETURN"
        }, default);
        Assert.Null(sent.Error);
        var beforeRead = await f.Snapshot();
        await AssertReceipt(db, received, 0, 7, 100000);
        var selected = Assert.Single((await new InventoryReceiptAvailability(db).ReadAsync(received, default))!.Allocations).Position;
        Assert.Equal(7, selected.AvailableQuantity);
        Assert.Equal(InventoryConfidence.Unknown, selected.ReceiptProvenance.Confidence);
        var shared = Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(
            new(9001, [], InventoryCustody.OutsideWarehouse, sent.TransferId), new(AllowedCustody: InventoryCustody.OutsideWarehouse), DateTimeOffset.UtcNow)).Positions);
        Assert.Equal(26, shared.AuthoritativeQuantity);
        Assert.Equal(InventoryConfidence.Unknown, shared.ReceiptProvenance.Confidence);
        Assert.Equal(19, shared.TreatmentSlices.Where(s => s.ReceiptEvidenceIds.IsEmpty).Sum(s => s.Quantity));
        Assert.Equal(beforeRead, await f.Snapshot());
        var retainedHistory = await CaptureHistory(db);
        var retainedBefore = await retainedHistory();
        var original = JsonSerializer.Serialize(await db.OutsideWarehouseTransfers.AsNoTracking().SingleAsync());
        var movements = JsonSerializer.Serialize(await db.TreatmentLineageMovements.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync());
        var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, executor);
        var form = await Correction(db, service, received);
        Assert.Null((await service.ApplyEditAsync(form, CanonicalReceivingWorkflowTests.Operator().HttpContext!.User, default)).Error);
        Assert.Equal(original, JsonSerializer.Serialize(await db.OutsideWarehouseTransfers.AsNoTracking().SingleAsync()));
        Assert.Equal(movements, JsonSerializer.Serialize(await db.TreatmentLineageMovements.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()));
        var custody = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(9001, [], InventoryCustody.OutsideWarehouse, sent.TransferId),
            new(AllowedCustody: InventoryCustody.OutsideWarehouse), DateTimeOffset.UtcNow);
        Assert.All(custody.Positions, p => Assert.True(p.IsOperable));
        Assert.Equal(19, custody.Positions.Single(x => x.Identity.GrowerLotId == 100000).AuthoritativeQuantity);
        Assert.Equal(7, custody.Positions.Single(x => x.Identity.GrowerLotId == 100001).AuthoritativeQuantity);
        Assert.Contains(custody.Positions.Single(x => x.Identity.GrowerLotId == 100001).Proof.Evidence, e => e.Entity == "InventoryIdentityCorrection");
        var error = await outside.ReverseAsync(new() { TransferId = sent.TransferId!.Value, Reason = "Return corrected custody" }, default);
        Assert.True(error == null, $"{error}: {captured.Last}");
        Assert.Equal(45, await f.Physical());
        var room = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow);
        Assert.Equal(19, room.Positions.Single(x => x.Identity.GrowerLotId == 100000).AuthoritativeQuantity);
        Assert.Equal(26, room.Positions.Single(x => x.Identity.GrowerLotId == 100001).AuthoritativeQuantity);
        AssertPosition(room.Positions.Single(x => x.Identity.GrowerLotId == 100000), 19, 0);
        AssertPosition(room.Positions.Single(x => x.Identity.GrowerLotId == 100001), 26, 10);
        await AssertReceipt(db, received, 7, 0, 100001);
        Assert.Equal(retainedBefore, await retainedHistory());
    }

    [InventoryPostgresFact]
    public async Task Receipt_identity_failure_rolls_back_compensations_map_receipt_projection_and_audit()
    {
        await using var f = await Fixture.Create(2);
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        foreach (var stage in new[] { "Movement", "OperationAudit", "BeforeCommit" })
        {
            var reached = false;
            var executor = new InventoryCommandExecutor(factory, new Observer((at, _) =>
            {
                if (at != stage) return Task.CompletedTask;
                reached = true; return Task.FromException(new InvalidOperationException("Local identity rollback"));
            }));
            var service = CanonicalReceiptCorrectionWorkflowTests.Service(db, executor);
            var form = await CanonicalReceiptCorrectionWorkflowTests.Form(db, service, 100000, 19); form.GrowerLotId = 100001;
            var before = await f.Snapshot();
            Assert.NotNull((await service.ApplyEditAsync(form, CanonicalReceivingWorkflowTests.Operator().HttpContext!.User, default)).Error);
            Assert.True(reached, stage); Assert.Equal(before, await f.Snapshot());
        }
    }

    private static async Task<Func<Task<string>>> CaptureHistory(CropQcDbContext db)
    {
        var ledger = await db.RoomInventoryAdjustments.Select(x => x.Id).ToArrayAsync();
        var moves = await db.TreatmentLineageMovements.Select(x => x.Id).ToArrayAsync();
        var audits = await db.AuditLogs.Select(x => x.Id).ToArrayAsync();
        return async () => JsonSerializer.Serialize(new
        {
            Ledger = await db.RoomInventoryAdjustments.AsNoTracking().Where(x => ledger.Contains(x.Id)).OrderBy(x => x.Id).ToArrayAsync(),
            Moves = await db.TreatmentLineageMovements.AsNoTracking().Where(x => moves.Contains(x.Id)).OrderBy(x => x.Id).ToArrayAsync(),
            Audits = await db.AuditLogs.AsNoTracking().Where(x => audits.Contains(x.Id)).OrderBy(x => x.Id).ToArrayAsync(),
            Applications = await db.RoomTreatmentApplications.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Sources = await db.RoomTreatmentApplicationSources.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync()
        });
    }

    private static void AssertPosition(InventoryAvailabilityResult position, int quantity, int excess)
    {
        Assert.True(position.IsOperable, string.Join("; ", position.Blockers.Select(x => x.Detail)));
        Assert.Equal(quantity, position.AuthoritativeQuantity);
        Assert.Equal(quantity, position.AvailableQuantity);
        Assert.Equal(quantity + excess, position.RawProjectionQuantity);
        Assert.Equal(excess, position.ProjectionExcess);
        Assert.Equal(excess, position.HistoricalProjections.Where(x => x.Reason == ProjectionExclusionReason.UnprovenProjection).Sum(x => x.ExcludedQuantity));
        Assert.Equal(quantity, position.TreatmentSlices.Sum(x => x.Quantity));
        Assert.All(position.TreatmentSlices, x => { Assert.Equal("u", x.Signature); Assert.Empty(x.ApplicationIds); });
    }

    private static async Task AssertReceipt(CropQcDbContext db, long receipt, int room, int custody, int grower)
    {
        var state = await new InventoryReceiptAvailability(db).ReadAsync(receipt, default);
        Assert.NotNull(state);
        Assert.Null(state.Blocker);
        Assert.Equal(room, state.RoomQuantity);
        Assert.Equal(custody, state.CustodyQuantity);
        Assert.All(state.Allocations, a =>
        {
            Assert.Equal(grower, a.Position.Identity.GrowerLotId);
            Assert.Equal(receipt, Assert.Single(a.Slice.ReceiptEvidenceIds));
            Assert.Equal(InventoryConfidence.Proven, a.Slice.Confidence);
        });
    }

    private sealed class Capture(IInventoryCommandExecutor next) : IInventoryCommandExecutor
    {
        public InventoryCommandResult? Last { get; private set; }
        public async Task<InventoryCommandResult> ExecuteAsync(InventoryCommand command, CancellationToken cancellationToken = default) =>
            Last = await next.ExecuteAsync(command, cancellationToken);
    }

    private static async Task<long> Receive(CropQcDbContext db, InventoryCommandExecutor executor)
    {
        var result = await new CanonicalReceivingService(db, executor).ReceiveAsync(Guid.NewGuid().ToString("N"), 8000, 2026,
            DateTimeOffset.UtcNow, 9001, 9002, 9004, 100000, "", "LOCAL-IDENTITY", 7, "Local receipt identity", default);
        Assert.Equal(InventoryCommandStatus.Committed, result.Status);
        return result.Effects.Single().ParentId!.Value;
    }

    private static async Task<AdminReceiptInventoryOverrideForm> Correction(CropQcDbContext db, CropQc.Web.Services.ReceiptInventoryOverrideService service, long receipt)
    {
        var form = await CanonicalReceiptCorrectionWorkflowTests.Form(db, service, receipt, 7);
        form.GrowerLotId = 100001;
        return form;
    }
}
