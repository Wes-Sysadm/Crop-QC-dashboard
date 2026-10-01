using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Inventory;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class CanonicalSelectorCoverageTests(ITestOutputHelper output)
{
    [InventoryPostgresFact]
    public async Task Six_normal_selectors_agree_and_large_room_query_counts_remain_bounded()
    {
        var measurements = new List<object>();
        Dictionary<string, int>? single = null;
        foreach (var size in new[] { 1, 100 })
        {
            await using var f = await Fixture.Create(size);
            var counter = new Counter();
            await using var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseNpgsql(f.Connection).AddInterceptors(counter).Options, new CanonicalInventoryMode(true));
            var access = new CanonicalOutsideWorkflowTests.Access();
            var http = CanonicalReceivingWorkflowTests.Operator();
            var time = new PacificBusinessTimeService(new SystemClock());
            var ledger = new CropQc.Web.Services.RoomInventoryLedgerQueryService(db);
            var invariant = new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance);
            var treatment = new RoomTreatmentService(db, ledger, access, http, time, NullLogger<RoomTreatmentService>.Instance);
            var executor = new InventoryCommandExecutor(new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection));
            var outside = new OutsideWarehouseTransferService(db, ledger, treatment, treatment, invariant, access, http, time, executor);
            var processor = new ProcessorShipmentService(db, ledger, treatment, treatment, invariant, access, http, time, executor);
            var crew = new InterCrewTransferService(db, outside, ledger, treatment, null!, invariant, access, http, time);
            var dump = new BinsRunService(db, access, NullLogger<BinsRunService>.Instance, canonicalCommands: executor);
            var loss = new RoomInventoryLossService(db, ledger, invariant, access, new CanonicalGrowerService(db), http, time,
                NullLogger<RoomInventoryLossService>.Instance, canonicalCommands: executor);
            var dashboard = CanonicalReceivingWorkflowTests.Dashboard(db, executor);
            var snapshot = await f.Snapshot();
            var counts = new Dictionary<string, int>();
            async Task Check(string name, Func<Task<(int Qty, string Treatment, bool Available)[]>> read)
            {
                counter.Reads = 0;
                var watch = Stopwatch.StartNew();
                var rows = await read();
                watch.Stop();
                counts[name] = counter.Reads;
                Assert.Equal(size, rows.Length);
                Assert.All(rows, x => { Assert.Equal(19, x.Qty); Assert.Equal("u", x.Treatment); Assert.True(x.Available); });
                measurements.Add(new { size, selector = name, queries = counter.Reads, elapsedMs = watch.Elapsed.TotalMilliseconds });
            }
            await Check("room move", async () => (await dashboard.GetRoomDetailAsync(9002, default)).TransferLotOptions.Select(x => (x.CurrentBins, x.TreatmentSignature, true)).ToArray());
            await Check("transfer", async () => (await crew.GetPageAsync(new() { RoomId = 9002, WarehouseId = 9001 }, default)).Inventory.Select(x => (x.AvailableBins, x.TreatmentSignature, x.IsAvailable)).ToArray());
            await Check("dump", async () => (await dump.GetPageAsync(new() { Section = "Actual", RoomIds = [9002], WarehouseId = 9001 }, http.HttpContext!.User, default)).AvailableInventory.Select(x => (x.CurrentBins, x.TreatmentSignature, x.IsAvailable)).ToArray());
            await Check("processor", async () => (await processor.GetPageAsync(null, false, null, null, null, null, default)).Inventory.Select(x => (x.AvailableBins, x.TreatmentSignature, !x.IsRoomSealed)).ToArray());
            await Check("outside", async () => (await outside.GetInventoryAsync(default)).Select(x => (x.AvailableBins, x.TreatmentSignature, x.IsAvailable)).ToArray());
            await Check("loss", async () => (await loss.GetRoomDataAsync(9002, default)).Options.Select(x => (x.CurrentBins, x.TreatmentSignature, true)).ToArray());
            Assert.Equal(snapshot, await f.Snapshot());
            output.WriteLine(JsonSerializer.Serialize(measurements));
            if (single == null) single = counts;
            else foreach (var (name, count) in counts) Assert.True(count <= single[name] + 2, $"{name} introduced per-position queries: {single[name]} -> {count}");
        }
        var path = Environment.GetEnvironmentVariable("CANONICAL_SELECTOR_PERFORMANCE_REPORT");
        if (!string.IsNullOrWhiteSpace(path)) await File.WriteAllTextAsync(path, JsonSerializer.Serialize(measurements, new JsonSerializerOptions { WriteIndented = true }));
    }

    [InventoryPostgresFact]
    public async Task Transit_queue_proof_is_batched_across_loads()
    {
        int? one = null;
        foreach (var size in new[] { 1, 10 })
        {
            await using var f = await Fixture.Create(size);
            var counter = new Counter();
            await using var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseNpgsql(f.Connection).AddInterceptors(counter).Options, new CanonicalInventoryMode(true));
            var executor = new InventoryCommandExecutor(new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection));
            var time = new PacificBusinessTimeService(new SystemClock());
            var inventory = CanonicalTruckReceiptWorkflowTests.Inventory(db, executor);
            var service = new InterCrewTransferService(db, inventory, new CropQc.Web.Services.RoomInventoryLedgerQueryService(db), null!, null!, null!,
                new CanonicalOutsideWorkflowTests.Access(), CanonicalReceivingWorkflowTests.Operator(), time,
                truckReceiptOptions: new() { Enabled = true }, canonicalCommands: executor);
            for (var n = 0; n < size; n++)
            {
                var option = (await inventory.GetInventoryAsync(default)).First(x => x.AvailableBins > 0);
                var result = await service.DispatchAsync(new()
                {
                    SourceWarehouseId = 9001,
                    SourceRoomId = 9002,
                    SourceKey = option.SourceKey,
                    ExpectedAvailableBins = 19,
                    DestinationCustodyGroup = "EBS",
                    BinsLoaded = 19,
                    LoadedAt = time.NowPacific.DateTime,
                    TruckLoadBolNumber = $"BATCH-{n}",
                    ConfirmedReview = true
                }, default);
                Assert.True(result.Success, result.Error);
            }
            var before = await f.Snapshot();
            counter.Reads = 0;
            var page = await service.GetPageAsync(new() { RoomId = 9002, WarehouseId = 9001 }, default);
            Assert.Equal(size, page.Queue.Count);
            Assert.All(page.Queue, x => { Assert.True(x.CanReceive); Assert.Equal("Awaiting Receipt", x.ReconciliationStatus); Assert.Equal(19, x.BinsLoaded); });
            output.WriteLine($"Transit queue {size} loads: {counter.Reads} queries");
            if (one == null) one = counter.Reads;
            else Assert.True(counter.Reads <= one + 2, $"Transit proof became N+1: {one} -> {counter.Reads}");
            Assert.Equal(before, await f.Snapshot());
        }
    }

    private sealed class Counter : DbCommandInterceptor
    {
        internal int Reads;
        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        { Reads++; return result; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Reads++; return ValueTask.FromResult(result); }
    }
}
