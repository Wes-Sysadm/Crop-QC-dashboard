using System.Text.Json;
using System.Collections.Immutable;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class CanonicalProducerStateMachineTests(ITestOutputHelper output)
{
    [InventoryPostgresTheory]
    [InlineData(255)]
    [InlineData(1372)]
    [InlineData(9722)]
    public Task Normal_producers_preserve_invariants_after_each_generated_operation(int seed) => Run(seed, false);

    [InventoryCommandRestoreFact]
    public Task Restored_production_preserves_unrelated_history_through_normal_workflow_lifecycle() => Run(255, true);

    private async Task Run(int seed, bool restored)
    {
        await using var f = restored ? await CanonicalRestoreFixture.Clone() : await Fixture.Create(0);
        Dictionary<string, string>? protectedFilters = null;
        string? protectedBefore = null;
        if (restored)
        {
            protectedFilters = await CanonicalRestoreFixture.ExistingRows(f);
            // Two facility-code aliases are explicit disposable fixture setup only.
            protectedFilters["Warehouses"] += " AND \"Code\" NOT IN ('WP','EBS','BASE-WP','BASE-EBS')";
            protectedBefore = await f.Snapshot(protectedFilters);
            await CanonicalRestoreFixture.SeedIsolatedRooms(f);
        }
        await using (var setup = f.CreateDbContext())
        {
            setup.GrowerLots.Add(new() { Id = 100000, Grower = "Lifecycle", LotNumber = "LOCAL-LIFE" });
            setup.OutsideWarehouses.Add(new() { Id = 9005, Code = "LIFE", Name = "Local outside" });
            setup.Processors.Add(new() { Id = 9005, Name = "Local processor" });
            (await setup.Warehouses.SingleAsync(x => x.Code == "EBS")).Code = "BASE-EBS";
            var actor = await setup.Users.SingleAsync(x => x.Id == 8000); actor.EmploymentFacility = "WP"; actor.EmploymentEffectiveAt = DateTimeOffset.UtcNow.AddDays(-1);
            await setup.SaveChangesAsync();
            setup.Warehouses.Add(new() { Id = 9006, Code = "EBS", Name = "Local EBS" });
            setup.Rooms.Add(new() { Id = 9007, WarehouseId = 9006, Code = "DEST", Name = "EBS destination" });
            await setup.SaveChangesAsync();
        }
        var factory = new ObservedFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory, new Observer(async (stage, _) =>
        {
            if (!stage.StartsWith("PersistedSource", StringComparison.Ordinal)) return;
            var evidence = await new InventoryEvidenceLoader(factory.Last!).LoadAsync(new(null, [9002, 9003, 9007]), DateTimeOffset.UtcNow, default);
            var bad = evidence.Positions.Where(x => !InventoryAvailabilityResolver.Resolve(x, new()).IsOperable).ToArray();
            if (bad.Length > 0) output.WriteLine("Persisted proof failure: " + JsonSerializer.Serialize(bad.Select(e => new { e.Identity, e.Location, e.AuthoritativeQuantity, e.Ledger, e.Projections, e.Movements, e.Receipts, e.Applications, Blockers = InventoryAvailabilityResolver.Resolve(e, new()).Blockers })));
        }), runExpectations: new CanonicalRunExpectationWriter());
        var http = CanonicalReceivingWorkflowTests.Operator(); var principal = http.HttpContext!.User;
        var access = new CanonicalOutsideWorkflowTests.Access(); var time = new PacificBusinessTimeService(new SystemClock());
        var ledger = new CropQc.Web.Services.RoomInventoryLedgerQueryService(db);
        var invariant = new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance);
        var dashboard = CanonicalReceivingWorkflowTests.Dashboard(db, executor, http, truckReceipts: true);
        var outside = CanonicalTruckReceiptWorkflowTests.Inventory(db, executor);
        var treatment = new RoomTreatmentService(db, ledger, access, http, time, NullLogger<RoomTreatmentService>.Instance, executor);
        var processor = new ProcessorShipmentService(db, ledger, treatment, treatment, invariant, access, http, time, executor);
        var loss = new RoomInventoryLossService(db, ledger, invariant, access, new CanonicalGrowerService(db), http, time,
            NullLogger<RoomInventoryLossService>.Instance, canonicalCommands: executor);
        var runs = new BinsRunService(db, access, NullLogger<BinsRunService>.Instance, canonicalCommands: executor);
        var correction = CanonicalReceiptCorrectionWorkflowTests.Service(db, executor);
        var truck = CanonicalTruckReceiptWorkflowTests.Service(db, executor);
        var crew = new InterCrewTransferService(db, outside, ledger, treatment, null!, invariant, access, http, time,
            truck, new() { Enabled = true }, executor);
        var random = new Random(seed); var trace = new List<object>(); var step = 0; long primaryReceipt = 0;
        var historicalRows = new Dictionary<long, string>(); var movementRows = new Dictionary<long, string>(); var ledgerRows = new Dictionary<long, string>();
        async Task<string> ScanUnrelated()
        {
            var rooms = (await db.Rooms.Where(x => x.Id < 9000).Select(x => x.Id).ToListAsync()).ToImmutableArray();
            var resolver = new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db));
            var positions = new List<InventoryAvailabilityResult>();
            foreach (var custody in Enum.GetValues<InventoryCustody>())
                positions.AddRange((await resolver.ResolveAsync(new(null, rooms, custody),
                    new(AllowedCustody: custody), time.UtcNow)).Positions.Where(x => x.Location.WarehouseId < 9000));
            return JsonSerializer.Serialize(positions.OrderBy(x => x.PositionKey).Select(x => new
            {
                x.PositionKey,
                x.AuthoritativeQuantity,
                x.AvailableQuantity,
                x.RawProjectionQuantity,
                x.Blockers,
                x.TreatmentSlices,
                x.ReceiptProvenance
            }));
        }
        var scanBefore = restored ? await ScanUnrelated() : null;
        async Task Verify(string action)
        {
            db.ChangeTracker.Clear();
            var resolver = new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db));
            var room = (await resolver.ResolveAsync(new(null, [9002, 9003, 9007]), new(), time.UtcNow)).Positions;
            Assert.All(room, p => { Assert.True(p.IsOperable, action + ": " + string.Join(",", p.Blockers)); Assert.True(p.AuthoritativeQuantity >= 0); Assert.Equal(p.AuthoritativeQuantity, p.RawProjectionQuantity); });
            var external = 0;
            foreach (var custody in new[] { InventoryCustody.InTransit, InventoryCustody.OutsideWarehouse, InventoryCustody.Processor })
            {
                var state = (await resolver.ResolveAsync(new(null, [9002, 9003], custody), new(AllowedCustody: custody), time.UtcNow)).Positions;
                Assert.All(state, p => { Assert.True(p.IsOperable, action + ": " + string.Join(",", p.Blockers)); Assert.True(p.AuthoritativeQuantity >= 0); });
                external += state.Sum(x => x.AuthoritativeQuantity);
            }
            var received = await db.Receipts.Where(x => !x.IsTransferReceipt && x.WarehouseId == 9001).SumAsync(x => x.BinCount);
            var consumed = await db.BinsRunEntries.Where(x => !x.IsReversed && x.ReversesBinsRunEntryId == null && x.WarehouseId == 9001).SumAsync(x => x.BinsRun);
            var lost = await db.RoomInventoryLosses.Where(x => !x.IsReversed && x.WarehouseId == 9001).SumAsync(x => x.BinCount);
            Assert.Equal(received, room.Sum(x => x.AuthoritativeQuantity) + external + consumed + lost);
            var segments = await db.TreatmentLineageSegments.AsNoTracking().Where(x => x.WarehouseId == 9001 || x.WarehouseId == 9006).OrderBy(x => x.Id).ToListAsync();
            Assert.All(segments.Where(x => x.Disposition == "Historical"), x => Assert.Equal(0, x.CurrentBins));
            Assert.All(segments.Where(x => x.Disposition == "Current" && x.CurrentBins > 0)
                .GroupBy(x => new { x.WarehouseId, x.RoomId, x.IdentityKey, x.TreatmentSignature, x.ReceiptId }), g => Assert.Single(g));
            foreach (var row in segments.Where(x => x.Disposition == "Historical"))
            {
                var value = JsonSerializer.Serialize(new { row.Id, row.CurrentBins, row.RetiredQuantity, row.Disposition, row.ConcurrencyVersion, row.TreatmentSignature, row.ReceiptId });
                if (historicalRows.TryGetValue(row.Id, out var previous)) Assert.Equal(previous, value);
                historicalRows[row.Id] = value;
            }
            var moves = await db.TreatmentLineageMovements.AsNoTracking().Where(x => x.SourceRoomId == 9002 || x.SourceRoomId == 9003 || x.SourceRoomId == 9007 || x.DestinationRoomId == 9002 || x.DestinationRoomId == 9003 || x.DestinationRoomId == 9007).OrderBy(x => x.Id).ToListAsync();
            Assert.True(moves.Count >= movementRows.Count);
            foreach (var row in moves)
            {
                var value = JsonSerializer.Serialize(new { row.Id, row.OperationKey, row.IdentityKey, row.SourceSegmentId, row.DestinationSegmentId, row.SourceRoomId, row.DestinationRoomId, row.BinCount, row.ReceiptId, row.TreatmentSignatureSnapshot, row.ReversesTreatmentLineageMovementId });
                if (movementRows.TryGetValue(row.Id, out var previous)) Assert.Equal(previous, value);
                movementRows[row.Id] = value;
            }
            var adjustments = await db.RoomInventoryAdjustments.AsNoTracking().Where(x => x.WarehouseId == 9001 || x.WarehouseId == 9006).OrderBy(x => x.Id).ToListAsync();
            Assert.True(adjustments.Count >= ledgerRows.Count);
            foreach (var row in adjustments)
            {
                var value = JsonSerializer.Serialize(new { row.Id, row.ChangeAmount, row.AdjustmentType, row.WarehouseId, row.RoomId, row.CropYear, row.GrowerLotId, row.FruitProfileId, row.ReceiptId, row.AdjustmentAt, row.InventoryOperationKey });
                if (ledgerRows.TryGetValue(row.Id, out var previous)) Assert.Equal(previous, value);
                ledgerRows[row.Id] = value;
            }
            Assert.True(await db.AuditLogs.CountAsync() >= await db.InventoryCommands.CountAsync());
            if (restored) Assert.Equal(protectedBefore, await f.Snapshot(protectedFilters));
            trace.Add(new { step = step++, action, received, roomBins = room.Sum(x => x.AuthoritativeQuantity), external, consumed, lost, movements = moves.Count, ledger = adjustments.Count });
        }
        async Task<long> Receive(int quantity, bool transfer = false, int profile = 9004, int growerId = 100000)
        {
            var grower = await db.GrowerLots.AsNoTracking().SingleAsync(x => x.Id == growerId);
            var result = await dashboard.CreateReceiptAsync(new()
            {
                CropYear = 2026,
                ConfirmCropYear = true,
                ReceivedAt = time.UtcNow,
                CompuTechReceiptId = $"LIFE-{seed}-{step}",
                WarehouseId = transfer ? 9006 : 9001,
                RoomId = transfer ? 9007 : 9002,
                FruitProfileId = profile,
                GrowerLotId = growerId,
                GrowerNumber = grower.LotNumber,
                GrowerName = grower.Grower,
                BinCount = quantity,
                ReceiptType = "Truck receipt",
                IsTransferReceipt = transfer
            }, default);
            Assert.Null(result.Error); await Verify(transfer ? "Truck Receipt evidence only" : "Receive"); return result.ReceiptId!.Value;
        }
        async Task<OutsideWarehouseInventoryOptionViewModel> Pick() => (await outside.GetInventoryAsync(default)).Where(x => x.IsAvailable && x.AvailableBins > 4 && x.WarehouseId == 9001).OrderByDescending(x => x.AvailableBins).First();
        async Task Move()
        {
            var source = await Pick(); var page = await dashboard.GetRoomDetailAsync(source.RoomId, default);
            var option = page.TransferLotOptions.First(x => x.IsAvailable && x.CurrentBins > 4);
            Assert.Null(await dashboard.CreateRoomTransferAsync(new()
            {
                FromRoomId = source.RoomId,
                DestinationWarehouseId = 9001,
                DestinationRoomId = source.RoomId == 9002 ? 9003 : 9002,
                SourceLotKey = option.LotKey,
                TreatmentSignature = option.TreatmentSignature,
                BinCount = random.Next(1, 4),
                TransferAt = time.UtcNow,
                Reason = "Generated room movement"
            }, default));
            await Verify("Room move");
        }
        async Task Treat()
        {
            var source = await Pick(); var form = new RoomTreatmentApplyForm
            {
                RoomId = source.RoomId,
                AppliedAt = time.UtcNow,
                TreatmentChemicalId = await db.TreatmentChemicals.Where(x => x.IsActive && x.ApplicationLevel == "Room" && x.Crop == "Apples").Select(x => x.Id).FirstAsync()
            };
            Assert.Null((await treatment.GetApplyPageAsync(form, true, default)).Error); form.ConfirmedReview = true;
            Assert.Null((await treatment.ApplyAsync(form, default)).Error); await Verify("Treatment assignment");
        }
        async Task ReverseTreatment()
        {
            if (!await db.RoomTreatmentApplications.AnyAsync(x => x.ReversedAt == null && (x.RoomId == 9002 || x.RoomId == 9003))) await Treat();
            var id = await db.RoomTreatmentApplications.Where(x => x.ReversedAt == null && (x.RoomId == 9002 || x.RoomId == 9003)).OrderBy(x => x.Id).Select(x => x.Id).FirstAsync();
            Assert.Null(await treatment.ReverseAsync(new() { Id = id, Reason = "Generated treatment reversal" }, default)); await Verify("Treatment reversal");
        }
        async Task Dump(bool cancel)
        {
            var p = await runs.GetPageAsync(new() { Section = "Actual", WarehouseId = 9001, RoomIds = [9002, 9003] }, principal, default);
            var source = p.AvailableInventory.Where(x => x.IsAvailable && x.CurrentBins > 4).OrderByDescending(x => x.CurrentBins).First();
            var quantity = random.Next(1, 4);
            Assert.Null(await runs.CreateActualRunAsync(new()
            {
                RunFacilityWarehouseId = 9001,
                RunAt = time.UtcNow,
                SalesDeskId = await db.SalesDesks.Where(x => x.IsActive).Select(x => x.Id).FirstAsync(),
                Lines = [new() { InventoryKey = source.InventoryKey, TreatmentSignature = source.TreatmentSignature, CanonicalFingerprint = source.CanonicalFingerprint,
                    ExpectedAvailableBins = source.CurrentBins, BinsRun = quantity }]
            }, principal, default));
            await Verify("ActualRun dump");
            if (!cancel) return;
            var run = await db.ActualRuns.AsNoTracking().OrderByDescending(x => x.Id).FirstAsync();
            Assert.Null(await runs.CancelActualRunAsync(new() { Id = run.Id, ConcurrencyVersion = run.ConcurrencyVersion, Reason = "Generated run cancellation" }, principal, default));
            await Verify("Run cancellation");
        }
        async Task Outside()
        {
            var source = await Pick(); var sent = await outside.CreateAsync(new()
            {
                SourceKey = source.SourceKey,
                OutsideWarehouseId = 9005,
                BinCount = random.Next(1, 4),
                ExpectedAvailableBins = source.AvailableBins,
                ConfirmedReview = true,
                TransferredAt = time.NowPacific.DateTime
            }, default);
            Assert.True(sent.Success, sent.Error); await Verify("Outside transfer");
            Assert.Null(await outside.ReverseAsync(new() { TransferId = sent.TransferId!.Value, Reason = "Generated outside return" }, default)); await Verify("Outside return");
        }
        async Task Processor()
        {
            var p = await processor.GetPageAsync(null, false, null, null, null, null, default);
            var source = p.Inventory.Where(x => x.AvailableBins > 4 && x.WarehouseId == 9001).OrderByDescending(x => x.AvailableBins).First();
            var sent = await processor.CreateAsync(new()
            {
                ProcessorId = 9005,
                SaleRate = 1,
                PricingBasis = "PerBin",
                ConfirmedReview = true,
                ShippedAt = time.NowPacific.DateTime,
                Lines = [new() { SourceKey = source.SourceKey, ExpectedAvailableBins = source.AvailableBins, BinsSent = random.Next(1, 4) }]
            }, default);
            Assert.True(sent.Success, sent.Error); await Verify("Processor sale");
            Assert.Null(await processor.ReverseAsync(new() { ShipmentId = sent.ShipmentId!.Value, Reason = "Generated processor return" }, default)); await Verify("Processor return");
        }
        async Task Loss()
        {
            var source = await Pick(); var option = (await loss.GetRoomDataAsync(source.RoomId, default)).Options.First(x => x.IsAvailable && x.CurrentBins > 4);
            Assert.Null(await loss.CreateAsync(new()
            {
                RoomId = source.RoomId,
                InventoryAdjustmentId = option.InventoryAdjustmentId,
                ExpectedCurrentBins = option.CurrentBins,
                TreatmentSignature = option.TreatmentSignature,
                CanonicalFingerprint = option.CanonicalFingerprint!,
                BinCount = random.Next(1, 4)
            }, default));
            await Verify("Loss"); var id = await db.RoomInventoryLosses.OrderByDescending(x => x.Id).Select(x => x.Id).FirstAsync();
            Assert.Null(await loss.ReverseAsync(new() { Id = id, Reason = "Generated loss restoration" }, default)); await Verify("Loss reversal");
        }
        async Task Correct(bool identity)
        {
            var r = await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == primaryReceipt);
            var form = await CanonicalReceiptCorrectionWorkflowTests.Form(db, correction, r.Id, identity ? r.BinCount : r.BinCount + 1);
            if (identity)
            {
                var grower = new GrowerLot { Grower = "Generated correction", LotNumber = $"LIFE-{seed}-{step}" }; db.GrowerLots.Add(grower); await db.SaveChangesAsync();
                form.GrowerLotId = grower.Id;
            }
            else
            {
                var preview = (await correction.GetPreviewAsync(r.Id, default))!;
                form.TrueUpAllocations = [new() { TargetKey = preview.TrueUpPositions.First(x => x.IsEligible).TargetKey, Bins = 1 }];
            }
            Assert.Null((await correction.ApplyEditAsync(form, principal, default)).Error); await Verify(identity ? "Receipt identity correction" : "Receipt quantity correction");
        }
        async Task Transit()
        {
            var source = await Pick();
            var sent = await crew.DispatchAsync(new()
            {
                SourceWarehouseId = source.WarehouseId,
                SourceRoomId = source.RoomId,
                SourceKey = source.SourceKey,
                ExpectedAvailableBins = source.AvailableBins,
                DestinationCustodyGroup = "EBS",
                BinsLoaded = 4,
                LoadedAt = time.NowPacific.DateTime,
                TruckLoadBolNumber = $"LIFE-{seed}-{step}",
                ConfirmedReview = true
            }, default);
            Assert.True(sent.Success, sent.Error); await Verify("Inter-crew dispatch"); var id = sent.TransferId!.Value;
            var allocation = Assert.Single(await truck.ActiveAllocationsAsync(id, default));
            Assert.Null(await truck.EditTransferAsync(new()
            {
                TransferId = id,
                TransferVersion = await db.InterCrewTransfers.Where(x => x.Id == id).Select(x => x.ConcurrencyVersion).SingleAsync(),
                DispatchMovementId = allocation.Movement.Id,
                Bins = 1,
                Reason = "Generated partial return"
            }, default)); await Verify("Partial transit return");
            var receipt = await Receive(3, true, source.FruitProfileId!.Value, source.GrowerLotId!.Value);
            async Task<TruckReceiptActionForm> Form() => new()
            {
                TransferId = id,
                ReceiptId = receipt,
                Reason = "Generated reopen",
                TransferVersion = await db.InterCrewTransfers.Where(x => x.Id == id).Select(x => x.ConcurrencyVersion).SingleAsync(),
                ReceiptVersion = await db.Receipts.Where(x => x.Id == receipt).Select(x => x.ConcurrencyVersion).SingleAsync()
            };
            Assert.Null(await truck.MatchAsync(await Form(), default)); await Verify("Truck match (nonphysical)");
            Assert.Null(await truck.CompleteAsync(await Form(), default)); await Verify("Truck completion");
            Assert.Null(await truck.ReopenAsync(await Form(), default)); await Verify("Truck reopen");
            Assert.Null(await truck.EditTransferAsync(new()
            {
                TransferId = id,
                TransferVersion = (await Form()).TransferVersion,
                DispatchMovementId = allocation.Movement.Id,
                Bins = 3,
                Reason = "Generated final transit return"
            }, default)); await Verify("Transit cancellation return");
        }
        try
        {
            primaryReceipt = await Receive(60);
            // Deterministic cross-workflow lifecycle before generated permutations.
            await Treat(); await Move(); await Transit(); await Dump(false); await Correct(false); await ReverseTreatment(); await Move();
            string[] choices = ["Receive", "Move", "Treat", "ReverseTreatment", "Dump", "CancelRun", "Outside", "Processor", "Loss", "Quantity", "Identity", "Transit"];
            foreach (var choice in Enumerable.Range(0, 2).SelectMany(_ => choices.OrderBy(_ => random.Next()).ToArray()))
            {
                switch (choice)
                {
                    case "Receive": await Receive(random.Next(2, 7)); break;
                    case "Move": await Move(); break;
                    case "Treat": await Treat(); break;
                    case "ReverseTreatment": await ReverseTreatment(); break;
                    case "Dump": await Dump(false); break;
                    case "CancelRun": await Dump(true); break;
                    case "Outside": await Outside(); break;
                    case "Processor": await Processor(); break;
                    case "Loss": await Loss(); break;
                    case "Quantity": await Correct(false); break;
                    case "Identity": await Correct(true); break;
                    case "Transit": await Transit(); break;
                }
            }
            if (restored) Assert.Equal(scanBefore, await ScanUnrelated());
        }
        finally
        {
            var serialized = JsonSerializer.Serialize(new { seed, restored, trace }, new JsonSerializerOptions { WriteIndented = true });
            output.WriteLine(serialized);
            var folder = Environment.GetEnvironmentVariable("CANONICAL_STATE_MACHINE_REPORT_DIRECTORY");
            if (!string.IsNullOrWhiteSpace(folder)) { Directory.CreateDirectory(folder); await File.WriteAllTextAsync(Path.Combine(folder, $"phase3-{(restored ? "restore" : "seed")}-{seed}.json"), serialized); }
        }
    }
    private sealed class ObservedFactory(string connection) : IDbContextFactory<CropQcDbContext>
    {
        internal CropQcDbContext? Last;
        public CropQcDbContext CreateDbContext() => Last = new(new DbContextOptionsBuilder<CropQcDbContext>().UseNpgsql(connection).Options, new(true));
    }

}
