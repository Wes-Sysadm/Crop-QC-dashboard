using System.Collections.Immutable;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace CropQc.Api.Tests;

public sealed class InventoryCommandTests
{
    [InventoryPostgresFact]
    public async Task Direct_move_cannot_bypass_a_route_requiring_a_truck_receipt()
    {
        await using var f = await Fixture.Create();
        await using (var db = f.CreateDbContext())
        {
            db.Rooms.Add(new() { Id = 9008, WarehouseId = 1, Code = "EBS-DEST", Name = "EBS destination" });
            await db.SaveChangesAsync();
        }
        var c = await f.Command(InventoryCommandKind.WarehouseTransfer, 19);
        c = c with { Lines = [c.Lines[0] with { Destination = new(1, 9008) }] };
        var before = await f.Snapshot();
        var result = await f.Execute(c);
        Assert.Equal(InventoryCommandStatus.Blocked, result.Status);
        Assert.Contains("Truck Receipt", result.Detail);
        Assert.Equal(before, await f.Snapshot());
    }
    [InventoryPostgresFact]
    public async Task Whole_room_treatment_is_one_application_across_multiple_lots_and_reverses_atomically()
    {
        await using var f = await Fixture.Create(2);
        await using var db = f.CreateDbContext();
        var resolver = new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db));
        var positions = (await resolver.ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow)).Positions;
        static InventoryCommandLine Line(InventoryAvailabilityResult r) => new(new(r.Identity, r.Location, r.Watermark.Fingerprint, r.Watermark.Versions),
            r.AuthoritativeQuantity, r.TreatmentSlices[0].Signature);
        var chemical = await db.TreatmentChemicals.FirstAsync(x => x.IsActive && x.ApplicationLevel == "Room");
        var c = new InventoryCommand(Guid.NewGuid().ToString("N"), InventoryCommandKind.TreatmentAssignment, 8000, DateTimeOffset.UtcNow,
            "Whole-room treatment", positions.Select(Line).ToImmutableArray(), TreatmentChemicalId: chemical.Id);
        var result = await f.Execute(c);
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
        var application = Assert.Single(await db.RoomTreatmentApplications.AsNoTracking().ToListAsync());
        Assert.Equal(38, application.TotalBinsSnapshot);
        Assert.Equal(2, await db.RoomTreatmentApplicationSources.CountAsync());
        positions = (await resolver.ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow)).Positions;
        var reverse = c with
        {
            OperationKey = Guid.NewGuid().ToString("N"),
            Kind = InventoryCommandKind.TreatmentReversal,
            Lines = positions.Select(Line).ToImmutableArray(),
            TreatmentApplicationId = application.Id
        };
        var reversed = await f.Execute(reverse);
        Assert.True(reversed.Status == InventoryCommandStatus.Committed, reversed.Detail);
        Assert.Equal(38, await f.Physical());
        Assert.All((await resolver.ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow)).Positions, x => Assert.Equal(19, x.AvailableQuantity));
    }
    [InventoryPostgresFact]
    public async Task Explicit_positive_correction_adds_only_the_authorized_delta()
    {
        foreach (var kind in new[] { InventoryCommandKind.ReceiptCorrection, InventoryCommandKind.BaselineAdjustment })
        {
            await using var f = await Fixture.Create();
            var c = await f.Command(kind, 7);
            c = c with
            {
                Lines = [c.Lines[0] with { ReceiptId = kind == InventoryCommandKind.ReceiptCorrection ? 100000 : null,
                AdjustmentDirection = InventoryAdjustmentDirection.Increase }]
            };
            var result = await f.Execute(c);
            Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
            Assert.Equal(26, await f.Physical());
            await using var db = f.CreateDbContext();
            Assert.Equal(26, (await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db))
                .ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow)).Positions.Single().AvailableQuantity);
        }
    }
    [InventoryPostgresFact]
    public async Task Room_moves_losses_and_receipt_corrections_conserve_physical_stock()
    {
        foreach (var kind in new[] { InventoryCommandKind.RoomMove, InventoryCommandKind.WarehouseTransfer,
            InventoryCommandKind.Loss, InventoryCommandKind.ReceiptCorrection, InventoryCommandKind.BaselineAdjustment })
        {
            await using var f = await Fixture.Create();
            var c = await f.Command(kind, 7);
            if (kind == InventoryCommandKind.ReceiptCorrection) c = c with { Lines = [c.Lines[0] with { ReceiptId = 100000 }] };
            var r = await f.Execute(c);
            Assert.True(r.Status == InventoryCommandStatus.Committed, $"{kind}: {r.Detail}");
            Assert.Equal(12, await f.Physical());
            Assert.Equal(InventoryCommandPolicy.IsRoomMove(kind) ? 7 : 0, await f.Physical(9003));
            await using var db = f.CreateDbContext();
            Assert.Equal(kind == InventoryCommandKind.ReceiptCorrection ? 12 : 19, (await db.Receipts.SingleAsync(x => x.Id == 100000)).BinCount);
        }
    }

    [InventoryPostgresFact]
    public async Task Outside_and_processor_dispatch_and_return_preserve_custody_without_reviving_old_projection()
    {
        foreach (var kind in new[] { InventoryCommandKind.OutsideWarehouseTransfer, InventoryCommandKind.ProcessorSale })
        {
            await using var f = await Fixture.Create();
            await using (var db = f.CreateDbContext())
            {
                db.OutsideWarehouses.Add(new() { Id = 9005, Code = "OUTTEST", Name = "Outside test" });
                db.Processors.Add(new() { Id = 9005, Name = "Processor test" });
                await db.SaveChangesAsync();
            }
            var c = await f.Command(kind, 19);
            c = c with { CounterpartyId = 9005, ProcessorTerms = new(15, "PerBin", "USD") };
            var r = await f.Execute(c);
            Assert.True(r.Status == InventoryCommandStatus.Committed, r.Detail);
            Assert.Equal(0, await f.Physical());
            var custody = kind == InventoryCommandKind.ProcessorSale ? InventoryCustody.Processor : InventoryCustody.OutsideWarehouse;
            var ret = await f.CustodyCommand(InventoryCommandKind.Return, custody, r.Effects[0].ParentId!.Value);
            ret = ret with { OriginalOperationKey = c.OperationKey };
            var returned = await f.Execute(ret);
            Assert.True(returned.Status == InventoryCommandStatus.Committed, returned.Detail);
            Assert.Equal(19, await f.Physical());
            await using var check = f.CreateDbContext();
            Assert.Equal("Historical", (await check.TreatmentLineageSegments.SingleAsync(x => x.Id == 100000)).Disposition);
            Assert.Equal(19, await check.TreatmentLineageSegments.Where(x => x.Disposition == "Current").SumAsync(x => x.CurrentBins));
        }
    }

    [InventoryPostgresFact]
    public async Task Treatment_assignment_and_reversal_preserve_application_sources()
    {
        await using var f = await Fixture.Create();
        await using var db = f.CreateDbContext();
        var chemical = await db.TreatmentChemicals.FirstAsync(x => x.IsActive && x.ApplicationLevel == "Room");
        var command = (await f.Command(InventoryCommandKind.TreatmentAssignment, 19)) with { TreatmentChemicalId = chemical.Id };
        var result = await f.Execute(command);
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
        Assert.Equal(19, await f.Physical());
        Assert.Equal(19, await db.RoomTreatmentApplicationSources.SumAsync(x => x.BinsTreated));
        var reverse = await f.Command(InventoryCommandKind.TreatmentReversal, 19);
        reverse = reverse with
        {
            TreatmentApplicationId = result.Effects[0].ParentId,
            Lines = [reverse.Lines[0] with { TreatmentSignature = $"u|a:{result.Effects[0].ParentId}" }]
        };
        var reversed = await f.Execute(reverse);
        Assert.True(reversed.Status == InventoryCommandStatus.Committed, reversed.Detail);
        var position = Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow)).Positions);
        Assert.Equal(19, position.AvailableQuantity);
        Assert.Equal("u", Assert.Single(position.TreatmentSlices).Signature);
        Assert.Equal(19, await db.RoomTreatmentApplicationSources.SumAsync(x => x.BinsTreated));
    }
    [InventoryPostgresFact]
    public async Task Dump_normalizes_exact_rows_consumes_once_and_preserves_history()
    {
        await using var f = await Fixture.Create();
        var command = await f.Command(InventoryCommandKind.Dump, 19);
        var result = await f.Execute(command);
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
        await using var db = f.CreateDbContext();
        Assert.Equal(0, await f.Physical());
        Assert.Equal(19, await db.BinsRunEntries.SumAsync(x => x.BinsRun));
        var old = await db.TreatmentLineageSegments.SingleAsync(x => x.Id == 100000);
        Assert.Equal("Historical", old.Disposition);
        Assert.Equal(29, old.RetiredQuantity);
        Assert.Equal(0, old.CurrentBins);
        Assert.Equal(19, (await db.Receipts.SingleAsync(x => x.Id == 100000)).BinCount);
        Assert.Single(await db.AuditLogs.Where(x => x.Action == "CanonicalInventoryNormalization").ToListAsync());
        var fingerprint = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Replayed, (await f.Execute(command)).Status);
        Assert.Equal(fingerprint, await f.Snapshot());
        Assert.Equal(InventoryCommandStatus.Conflict, (await f.Execute(command with { Reason = "different intent" })).Status);
        Assert.Equal(fingerprint, await f.Snapshot());
    }

    [InventoryPostgresFact]
    public async Task Failure_at_every_transaction_boundary_restores_all_rows()
    {
        await using var f = await Fixture.Create();
        var command = await f.Command(InventoryCommandKind.Dump, 19);
        var before = await f.Snapshot();
        foreach (var stage in new[] { "NormalizationAudit", "Normalized", "Parent", "Source1", "Movement", "OperationAudit", "BeforeCommit" })
        {
            await Assert.ThrowsAsync<IOException>(() => f.Execute(command, new Observer((s, _) =>
                s == stage ? Task.FromException(new IOException(stage)) : Task.CompletedTask)));
            Assert.Equal(before, await f.Snapshot());
        }
    }

    [InventoryPostgresFact]
    public async Task Stale_and_overdraw_intents_make_no_changes()
    {
        await using var f = await Fixture.Create();
        var command = await f.Command(InventoryCommandKind.Dump, 20);
        var before = await f.Snapshot();
        Assert.Equal(InventoryCommandStatus.Blocked, (await f.Execute(command)).Status);
        var source = command.Lines[0].Source;
        var stale = command with { Lines = [command.Lines[0] with { Quantity = 19, Source = source with { ExpectedFingerprint = "stale" } }] };
        Assert.Equal(InventoryCommandStatus.Stale, (await f.Execute(stale)).Status);
        var version = command with
        {
            Lines = [command.Lines[0] with { Quantity = 19, Source = source with
            { ExpectedVersions = [new("TreatmentLineageSegment", "100000", 999, DateTimeOffset.UtcNow)] } }]
        };
        Assert.Equal(InventoryCommandStatus.Stale, (await f.Execute(version)).Status);
        Assert.Equal(before, await f.Snapshot());
    }

    [InventoryPostgresFact]
    public async Task Partial_depletion_then_final_depletion_never_renormalizes_or_reuses_consumed_bins()
    {
        await using var f = await Fixture.Create();
        var first = await f.Command(InventoryCommandKind.Dump, 7);
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(first)).Status);
        var second = await f.Command(InventoryCommandKind.Dump, 12);
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(second)).Status);
        Assert.Equal(0, await f.Physical());
        Assert.Equal(InventoryCommandStatus.Replayed, (await f.Execute(first)).Status);
        await using var db = f.CreateDbContext();
        Assert.Equal(19, await db.BinsRunEntries.SumAsync(x => x.BinsRun));
        Assert.Equal(1, await db.AuditLogs.CountAsync(x => x.Action == "CanonicalInventoryNormalization"));
        var third = await f.Command(InventoryCommandKind.Dump, 1);
        Assert.Equal(InventoryCommandStatus.Blocked, (await f.Execute(third)).Status);
    }

    internal sealed class Observer(Func<string, int, Task> action) : IInventoryCommandObserver
    {
        public Task AtAsync(string stage, int attempt, CancellationToken ct) => action(stage, attempt);
    }

    internal sealed class Fixture(string connection, params IInterceptor[] interceptors) : IDbContextFactory<CropQcDbContext>, IAsyncDisposable
    {
        public string Connection { get; } = connection;
        public CropQcDbContext CreateDbContext() => new(new DbContextOptionsBuilder<CropQcDbContext>().UseNpgsql(Connection).AddInterceptors(interceptors).Options);
        public static async Task<Fixture> Create(int positions = 1)
        {
            var original = Environment.GetEnvironmentVariable("CANONICAL_INVENTORY_TEST_POSTGRES")!;
            ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(original);
            var f = new Fixture(new NpgsqlConnectionStringBuilder(original) { Database = $"command_{Guid.NewGuid():N}_test" }.ConnectionString);
            await using var db = f.CreateDbContext();
            await db.Database.EnsureCreatedAsync();
            await InventoryAvailabilityDatabaseTests.SeedAsync(db, positions);
            (await db.Warehouses.SingleAsync(x => x.Id == 4)).Code = "BASE-WP";
            await db.SaveChangesAsync();
            (await db.Warehouses.SingleAsync(x => x.Id == 9001)).Code = "WP";
            db.Users.Add(new() { Id = 8000, Email = "canonical-test@example.invalid", DisplayName = "Test operator" });
            db.Rooms.Add(new() { Id = 9003, WarehouseId = 9001, Code = "DEST", Name = "Destination" });
            await db.SaveChangesAsync();
            return f;
        }
        public async Task<InventoryCommand> Command(InventoryCommandKind kind, int quantity, int room = 9002, int warehouse = 9001)
        {
            await using var db = CreateDbContext();
            var result = Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db))
                .ResolveAsync(new(warehouse, [room]), new(), DateTimeOffset.UtcNow)).Positions);
            return new(Guid.NewGuid().ToString("N"), kind, 8000, DateTimeOffset.UtcNow, "Disposable command validation",
                [new(new(result.Identity, result.Location, result.Watermark.Fingerprint, result.Watermark.Versions), quantity, "u",
                    InventoryCommandPolicy.IsRoomMove(kind) ? new(9001, 9003) : null)]);
        }
        public Task<InventoryCommandResult> Execute(InventoryCommand command, IInventoryCommandObserver? observer = null) =>
            new InventoryCommandExecutor(this, observer).ExecuteAsync(command);
        public async Task<InventoryCommand> CustodyCommand(InventoryCommandKind kind, InventoryCustody custody, long id)
        {
            await using var db = CreateDbContext();
            var r = Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db))
                .ResolveAsync(new(9001, [], custody, id), new(AllowedCustody: custody), DateTimeOffset.UtcNow)).Positions);
            Assert.True(r.IsOperable, string.Join(",", r.Blockers.Select(x => x.Code)));
            return new(Guid.NewGuid().ToString("N"), kind, 8000, DateTimeOffset.UtcNow, "Disposable custody validation",
                [new(new(r.Identity, r.Location, r.Watermark.Fingerprint, r.Watermark.Versions), r.AuthoritativeQuantity,
                    r.TreatmentSlices[0].Signature, kind == InventoryCommandKind.ReceiveTransfer ? new(9006, 9007) : null)]);
        }
        public async Task<InventoryCommand> ReceiveCommand(Func<Task>? prepareDestination = null)
        {
            await using var db = CreateDbContext();
            (await db.Warehouses.SingleAsync(x => x.Code == "EBS")).Code = "BASE-EBS";
            await db.SaveChangesAsync();
            (await db.Warehouses.SingleAsync(x => x.Id == 9001)).Code = "WP";
            db.Warehouses.Add(new() { Id = 9006, Code = "EBS", Name = "Receiving site" });
            db.Rooms.Add(new() { Id = 9007, WarehouseId = 9006, Code = "RECV", Name = "Receiving room" });
            await db.SaveChangesAsync();
            if (prepareDestination != null) await prepareDestination();
            var dispatch = (await Command(InventoryCommandKind.InterCompanyDispatch, 19)) with { CustodyGroup = "EBS" };
            var sent = await Execute(dispatch);
            Assert.True(sent.Status == InventoryCommandStatus.Committed, sent.Detail);
            var receipt = new CropQc.Data.Entities.Receipt
            {
                CropYear = 2026,
                CompuTechReceiptId = "TR-TEST",
                ReceiptType = "Truck receipt",
                GrowerName = "Transfer",
                LotCode = "TRANSFER",
                WarehouseId = 9006,
                RoomId = 9007,
                FruitProfileId = 9004,
                GrowerLotId = 100000,
                BinCount = 19,
                IsTransferReceipt = true,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            receipt.VarietyLines.Add(new() { FruitProfileId = 9004, BinCount = 19 });
            var transfer = await db.InterCrewTransfers.SingleAsync(x => x.Id == sent.Effects[0].ParentId);
            transfer.ReceivingReceipt = receipt; transfer.ConcurrencyVersion++;
            await db.SaveChangesAsync();
            return (await CustodyCommand(InventoryCommandKind.ReceiveTransfer, InventoryCustody.InTransit, transfer.Id))
                with
            { ReceivingEvidence = new(receipt.Id, receipt.ConcurrencyVersion) };
        }
        public async Task<int> Physical(int room = 9002)
        {
            await using var db = CreateDbContext();
            return (await new RoomInventoryLedgerQueryService(db).GetSnapshotsAsync(9001, [room], default)).Sum(x => x.CurrentBins);
        }
        public async Task<string> Snapshot(IReadOnlyDictionary<string, string>? filters = null)
        {
            await using var connection = new NpgsqlConnection(Connection);
            await connection.OpenAsync();
            var tables = new List<string>();
            await using (var command = new NpgsqlCommand("SELECT tablename FROM pg_tables WHERE schemaname='public' ORDER BY tablename", connection))
            await using (var reader = await command.ExecuteReaderAsync())
                while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
            var values = new List<object>();
            foreach (var table in tables)
            {
                var predicate = filters != null && filters.TryGetValue(table, out var filter) ? " WHERE " + filter : "";
                await using var command = new NpgsqlCommand($"SELECT COALESCE(md5(string_agg(to_jsonb(t)::text, '' ORDER BY to_jsonb(t)::text)), '') FROM \"{table.Replace("\"", "\"\"")}\" t{predicate}", connection);
                values.Add(new { table, rows = await command.ExecuteScalarAsync() });
            }
            return JsonSerializer.Serialize(values);
        }
        public async ValueTask DisposeAsync()
        {
            ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(Connection);
            await using var db = CreateDbContext();
            await db.Database.EnsureDeletedAsync();
        }
    }
}
