using System.Text.Json;
using System.Text.RegularExpressions;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Shared.Time;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class LegacyTransferEligibilityTests
{
    [InventoryPostgresFact]
    public async Task Http_transfer_selectors_render_blocked_stock_without_an_enabled_source_control()
    {
        await using var f = await Legacy();
        await using (var seed = f.CreateDbContext())
        {
            (await seed.RoomTransfers.SingleAsync()).BinCount = 18;
            seed.OutsideWarehouses.Add(new() { Id = 9005, Code = "LOCAL-OUT", Name = "Local outside destination", Address = "Test" });
            await seed.SaveChangesAsync();
        }
        await using var baseHost = new ReceiptLocationHttpTests.ReceiptHost(f.Connection);
        await using var host = baseHost.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(
            new Dictionary<string, string?> { ["TransferCustody:Enabled"] = "true" })));
        using var client = host.CreateClient(new() { AllowAutoRedirect = false });
        var before = await f.Snapshot();
        foreach (var path in new[] { "/BinsRun?Section=Transfer&TransferType=Outside&WarehouseId=9001&RoomId=9003",
            "/BinsRun?Section=Transfer&TransferType=InterCrew&WarehouseId=9001&RoomId=9003", "/ProcessorShipments" })
        {
            var html = await client.GetStringAsync(path);
            Assert.Contains("Needs Review", html);
            Assert.Contains("19 / 0", html);
            Assert.Contains("Position total / Treatment selection", html);
            Assert.Contains("not final transfer approval", html);
            Assert.DoesNotContain("Transferable", html);
            var controls = Regex.Matches(html, "<input\\b[^>]*>").Select(x => x.Value)
                .Where(x => x.Contains("name=\"SourceKey\"") || x.Contains(".BinsSent\"")).ToArray();
            Assert.NotEmpty(controls);
            Assert.All(controls, x => Assert.Contains("disabled", x));
        }
        Assert.Equal(before, await f.Snapshot());
    }

    [InventoryPostgresFact]
    public async Task Legacy_treated_departure_carries_the_original_application_into_a_modern_move()
    {
        await using var f = await Legacy(treated: true);
        await using var db = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection).CreateDbContext();
        var application = await db.RoomTreatmentApplications.AsNoTracking().SingleAsync();
        var before = JsonSerializer.Serialize(application);
        var option = Assert.Single(await Outside(db).GetInventoryAsync(default));
        Assert.Equal($"u|a:{application.Id}", option.TreatmentSignature);
        Assert.Equal(19, option.TreatmentSelectionBins);
        var command = await Move(f, 9003, 9002, 7);
        command = command with { Lines = [command.Lines[0] with { TreatmentSignature = option.TreatmentSignature }] };
        var result = await new InventoryCommandExecutor(new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection)).ExecuteAsync(command);
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
        Assert.Equal(19, await f.Physical(9002) + await f.Physical(9003));
        var current = await db.TreatmentLineageSegments.AsNoTracking().Include(x => x.Applications).Where(x => x.Disposition == "Current" && x.CurrentBins > 0).ToArrayAsync();
        Assert.Equal(19, current.Sum(x => x.CurrentBins));
        Assert.All(current, x => Assert.Equal(application.Id, Assert.Single(x.Applications).RoomTreatmentApplicationId));
        Assert.Equal(before, JsonSerializer.Serialize(await db.RoomTreatmentApplications.AsNoTracking().SingleAsync()));
    }

    [InventoryCommandRestoreFact]
    public async Task Restored_all_facilities_preserve_original_history_and_conserve_each_selected_identity()
    {
        await using var f = await CanonicalRestoreFixture.Clone();
        var examples = new[] { (Warehouse: 1, Room: 11, Lot: "1565", Profile: 2), (2, 33, "4151", 20),
            (3, 67, "1110", 19), (4, 1, "1012", 22) };
        await using var db = f.CreateDbContext();
        var actor = await db.Users.Where(x => x.IsActive).Select(x => x.Id).FirstAsync();
        for (var i = 0; i < examples.Length; i++)
            db.Rooms.Add(new() { Id = 95000 + i, WarehouseId = examples[i].Item1, Code = $"LOCAL-TRANSFER-{i}", Name = "Disposable transfer target" });
        await db.SaveChangesAsync();
        var filters = new Dictionary<string, string>();
        foreach (var table in new[] { "RoomInventoryAdjustments", "RoomTransfers", "TreatmentLineageMovements", "AuditLogs" })
        {
#pragma warning disable EF1002 // Fixed local-test table allowlist, never user SQL.
            var max = await db.Database.SqlQueryRaw<long>($"SELECT COALESCE(MAX(\"Id\"),0)::bigint AS \"Value\" FROM \"{table}\"").SingleAsync();
#pragma warning restore EF1002
            filters[table] = $"\"Id\" <= {max}";
        }
        var prefix = "eligibility-restore-" + Guid.NewGuid().ToString("N");
        filters["InventoryCommands"] = $"\"OperationKey\" NOT LIKE '{prefix}%'";
        filters["TreatmentLineageSegments"] = "\"RoomId\" < 95000 AND NOT (" + string.Join(" OR ", examples.Select(x =>
            $"(\"RoomId\"={x.Item2} AND \"LotNumberSnapshot\"='{x.Item3}' AND \"FruitProfileId\"={x.Item4})")) + ")";
        var before = await f.Snapshot(filters);
        var executor = new InventoryCommandExecutor(new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection));
        for (var i = 0; i < examples.Length; i++)
        {
            var (warehouse, room, lot, profile) = examples[i];
            var r = Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(
                new(warehouse, [room]), new() { AllowIndependentCohorts = true }, DateTimeOffset.UtcNow)).Positions,
                p => p.Identity.Lot == lot && p.Identity.FruitProfileId == profile && p.AuthoritativeQuantity > 0);
            Assert.True(r.IsOperable, string.Join(';', r.Blockers.Select(x => x.Detail)));
            await using var screenDb = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection).CreateDbContext();
            var screen = Assert.Single(await Outside(screenDb).GetInventoryAsync(default), o => o.RoomId == room
                && o.LotNumber == lot && o.FruitProfileId == profile && o.TreatmentSignature == "u");
            Assert.EndsWith(r.Watermark.Fingerprint, screen.SourceKey);
            var command = new InventoryCommand(prefix + i, InventoryCommandKind.RoomMove, actor, DateTimeOffset.UtcNow,
                "Isolated all-facility rehearsal", [new(new(r.Identity, r.Location, r.Watermark.Fingerprint, r.Watermark.Versions),
                    1, "u", new(warehouse, 95000 + i))]);
            var result = await executor.ExecuteAsync(command);
            Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
            Assert.Equal(InventoryCommandStatus.Replayed, (await executor.ExecuteAsync(command)).Status);
            var after = (await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(
                new(warehouse, [room, 95000 + i]), new() { AllowIndependentCohorts = true }, DateTimeOffset.UtcNow)).Positions
                .Where(p => p.Identity.Key == r.Identity.Key).ToArray();
            Assert.Equal(r.AuthoritativeQuantity, after.Sum(p => p.AuthoritativeQuantity));
            Assert.Equal(1, Assert.Single(after, p => p.Location.RoomId == 95000 + i).AvailableQuantity);
            Assert.All(after.SelectMany(p => p.TreatmentSlices), slice => Assert.Equal("u", slice.Signature));
        }
        Assert.Equal(before, await f.Snapshot(filters));
    }

    [InventoryPostgresFact]
    public async Task Legacy_parent_replay_moves_repeatedly_and_retries_without_changing_original_evidence()
    {
        await using var f = await Legacy();
        await using (var alias = f.CreateDbContext())
        {
            // Existing canonical keys normalize case/space; historical custody
            // closure must use that same identity rather than exact text.
            (await alias.RoomInventoryAdjustments.SingleAsync(x => x.Id == 100002)).LotNumber = " corpus-100000 ";
            await alias.SaveChangesAsync();
        }
        var historical = await History(f);
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        var executor = new InventoryCommandExecutor(factory);
        await using var db = factory.CreateDbContext();
        var outside = Outside(db);
        var option = Assert.Single(await outside.GetInventoryAsync(default));
        Assert.Equal(19, option.AvailableBins);
        Assert.True(option.IsAvailable);
        Assert.Equal(19, option.AuthoritativeBins);
        var dashboard = CanonicalReceivingWorkflowTests.Dashboard(db, executor);
        var page = await dashboard.GetRoomDetailAsync(9003, default);
        Assert.Equal(19, page.TransferAvailableBins);
        var command = await Move(f, 9003, 9002, 7);
        var first = await executor.ExecuteAsync(command);
        Assert.True(first.Status == InventoryCommandStatus.Committed, first.Detail);
        Assert.Equal(InventoryCommandStatus.Replayed, (await executor.ExecuteAsync(command)).Status);
        Assert.Equal(InventoryCommandStatus.Committed, (await executor.ExecuteAsync(await Move(f, 9002, 9003, 3))).Status);
        Assert.Equal(19, await f.Physical(9002) + await f.Physical(9003));
        Assert.Equal(4, await f.Physical(9002));
        Assert.Equal(15, await f.Physical(9003));
        Assert.Equal(historical, await History(f));
        Assert.Equal(19, await db.TreatmentLineageSegments.Where(x => x.Disposition == "Current").SumAsync(x => x.CurrentBins));
        Assert.False(await db.RoomTreatmentApplications.AnyAsync());
    }

    [InventoryPostgresFact]
    public async Task Missing_parent_is_visible_nonselectable_and_command_rejects_without_writes()
    {
        await using var f = await Legacy();
        await using (var seed = f.CreateDbContext())
        {
            var transfer = await seed.RoomTransfers.SingleAsync();
            transfer.BinCount = 18; // Contradictory committed parent: never infer a replacement.
            await seed.SaveChangesAsync();
        }
        var before = await f.Snapshot();
        await using var db = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection).CreateDbContext();
        var option = Assert.Single(await Outside(db).GetInventoryAsync(default));
        Assert.False(option.IsAvailable);
        Assert.Equal(19, option.AuthoritativeBins);
        Assert.Equal(0, option.TreatmentSelectionBins);
        Assert.NotEmpty(option.UnavailableReason!);
        var result = await new InventoryCommandExecutor(new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection)).ExecuteAsync(await Move(f, 9003, 9002, 1));
        Assert.Equal(InventoryCommandStatus.Blocked, result.Status);
        Assert.Equal(before, await f.Snapshot());
    }

    [InventoryPostgresFact]
    public async Task Concurrent_legacy_source_moves_cannot_double_spend_and_source_changes_invalidate_selection()
    {
        await using var f = await Legacy();
        var a = await Move(f, 9003, 9002, 12);
        var b = a with { OperationKey = Guid.NewGuid().ToString("N") };
        var ready = 0;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var executor = new InventoryCommandExecutor(new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection), new Observer(async (stage, attempt) =>
        {
            if (stage != "Resolved" || attempt != 1) return;
            if (Interlocked.Increment(ref ready) == 2) gate.SetResult();
            await gate.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }));
        var results = await Task.WhenAll(executor.ExecuteAsync(a), executor.ExecuteAsync(b));
        Assert.Single(results, x => x.Status == InventoryCommandStatus.Committed);
        Assert.Single(results, x => x.Status is InventoryCommandStatus.Stale or InventoryCommandStatus.RetryRequired);
        Assert.Equal(19, await f.Physical(9002) + await f.Physical(9003));
        Assert.Equal(7, await f.Physical(9003));
        var stale = await Move(f, 9003, 9002, 1);
        await using (var seed = f.CreateDbContext())
        {
            (await seed.Receipts.SingleAsync()).ConcurrencyVersion++;
            await seed.SaveChangesAsync();
        }
        Assert.Equal(InventoryCommandStatus.Stale, (await executor.ExecuteAsync(stale)).Status);
    }

    [InventoryPostgresFact]
    public async Task Sealed_source_keeps_its_quantity_visible_and_cannot_move()
    {
        await using var f = await Legacy();
        await using (var seed = f.CreateDbContext())
        {
            var room = await seed.Rooms.SingleAsync(x => x.Id == 9003);
            room.IsSealed = true; room.SealedAt = DateTimeOffset.UtcNow.AddDays(-1);
            await seed.SaveChangesAsync();
        }
        await using var db = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection).CreateDbContext();
        var option = Assert.Single(await Outside(db).GetInventoryAsync(default));
        Assert.True(option.IsRoomSealed);
        Assert.Equal(19, option.AuthoritativeBins);
        Assert.Equal(0, option.TreatmentSelectionBins);
        var before = await f.Snapshot();
        var result = await new InventoryCommandExecutor(new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection)).ExecuteAsync(await Move(f, 9003, 9002, 1));
        Assert.Equal(InventoryCommandStatus.Blocked, result.Status);
        Assert.Equal(before, await f.Snapshot());
    }

    private static OutsideWarehouseTransferService Outside(CropQcDbContext db) => new(db,
        new CropQc.Web.Services.RoomInventoryLedgerQueryService(db), null!, null!, null!, null!, null!,
        new PacificBusinessTimeService(new SystemClock()));

    private static async Task<InventoryCommand> Move(Fixture f, int source, int destination, int bins)
    {
        await using var db = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection).CreateDbContext();
        var p = Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db))
            .ResolveAsync(new(9001, [source]), new() { AllowIndependentCohorts = true }, DateTimeOffset.UtcNow)).Positions,
            x => x.AuthoritativeQuantity > 0);
        return new(Guid.NewGuid().ToString("N"), InventoryCommandKind.RoomMove, 8000, DateTimeOffset.UtcNow,
            "Isolated legacy custody regression", [new(new(p.Identity, p.Location, p.Watermark.Fingerprint, p.Watermark.Versions), bins, "u", new(9001, destination))]);
    }

    private static async Task<string> History(Fixture f)
    {
        await using var db = f.CreateDbContext();
        return JsonSerializer.Serialize(new
        {
            Receipt = await db.Receipts.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Rows = await db.RoomInventoryAdjustments.AsNoTracking().Where(x => x.Id >= 100000 && x.Id <= 100002).OrderBy(x => x.Id).ToArrayAsync(),
            Parent = await db.RoomTransfers.AsNoTracking().Where(x => x.Id == 100000).ToArrayAsync()
        });
    }

    internal static async Task<Fixture> Legacy(bool treated = false)
    {
        var f = await Fixture.Create();
        await using var db = f.CreateDbContext();
        if (treated)
        {
            var chemical = await db.TreatmentChemicals.FirstAsync(x => x.IsActive && x.ApplicationLevel == "Room");
            var segment = await db.TreatmentLineageSegments.SingleAsync();
            var application = new RoomTreatmentApplication
            {
                Id = 800000,
                OperationKey = "legacy-treatment",
                TreatmentChemicalId = chemical.Id,
                WarehouseId = 9001,
                RoomId = 9002,
                AppliedByUserId = 8000,
                CreatedByUserId = 8000,
                TotalBinsSnapshot = 19,
                ProductNameSnapshot = chemical.ProductName,
                CropSnapshot = chemical.Crop,
                UnitSnapshot = chemical.Unit,
                CurrencySnapshot = chemical.Currency,
                AppliedAt = InventoryEvidenceCorpus.Start.AddHours(1),
                CreatedAt = InventoryEvidenceCorpus.Start.AddHours(1)
            };
            application.Sources.Add(new()
            {
                ReceiptId = 100000,
                CropYear = 2026,
                GrowerLotId = 100000,
                FruitProfileId = 9004,
                IdentityKey = segment.IdentityKey,
                GrowerNameSnapshot = "Corpus",
                LotNumberSnapshot = "CORPUS-100000",
                VarietyCodeSnapshot = "CGAL",
                ProductionTypeSnapshot = "Conventional",
                IsOrganicSnapshot = false,
                BinsTreated = 19,
                PriorTreatmentSignature = "u",
                ResultTreatmentSignature = "u|a:800000"
            });
            db.RoomTreatmentApplications.Add(application);
            segment.CurrentBins = 0;
        }
        else db.TreatmentLineageSegments.RemoveRange(await db.TreatmentLineageSegments.ToListAsync());
        var at = InventoryEvidenceCorpus.Start.AddDays(1);
        db.RoomTransfers.Add(new()
        {
            Id = 100000,
            OperationKey = "legacy-transfer",
            SourceWarehouseId = 9001,
            SourceRoomId = 9002,
            DestinationWarehouseId = 9001,
            DestinationRoomId = 9003,
            CropYear = 2026,
            GrowerLotId = 100000,
            FruitProfileId = 9004,
            GrowerName = "Corpus",
            LotNumber = "CORPUS-100000",
            VarietyCode = "CGAL",
            BinCount = 19,
            Reason = "Historical transfer",
            TransferredAt = at,
            CreatedAt = at
        });
        foreach (var (id, room, quantity, kind) in new[] { (100001, 9002, -19, "TransferOut"), (100002, 9003, 19, "TransferIn") })
            db.RoomInventoryAdjustments.Add(new()
            {
                Id = id,
                WarehouseId = 9001,
                RoomId = room,
                CropYear = 2026,
                GrowerLotId = 100000,
                FruitProfileId = 9004,
                GrowerName = "Corpus",
                LotNumber = "CORPUS-100000",
                VarietyCode = "CGAL",
                RoomTransferId = 100000,
                ChangeAmount = quantity,
                NewBinCount = quantity > 0 ? quantity : 0,
                AdjustmentType = kind,
                AdjustmentAt = at,
                CreatedAt = treated ? at : at.AddMilliseconds(id - 100000)
            });
        await db.SaveChangesAsync();
        return f;
    }
}
