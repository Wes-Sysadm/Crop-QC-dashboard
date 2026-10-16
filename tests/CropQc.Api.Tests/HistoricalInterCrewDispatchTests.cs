using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class HistoricalInterCrewDispatchTests
{
    [InventoryPostgresFact]
    public async Task Historical_286_bin_source_is_selectable_but_WpDh_dispatch_is_rejected_by_current_route_contract()
    {
        await using var f = await HistoricalPool();
        var history = await History(f);
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        Assert.False(await db.TreatmentLineageMovements.AnyAsync());
        Assert.False(await db.TreatmentLineageSegments.AnyAsync());
        var executor = new RecordingExecutor(new InventoryCommandExecutor(factory));
        var option = await Source(db, executor);
        Assert.True(option.IsAvailable, option.UnavailableReason);
        Assert.Equal(286, option.TreatmentSelectionBins);
        Assert.Equal("u|a:800000", option.TreatmentSignature);
        var resolved = Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db))
            .ResolveAsync(new(9001, [9003]), new() { AllowIndependentCohorts = true }, DateTimeOffset.UtcNow)).Positions);
        Assert.EndsWith(resolved.Watermark.Fingerprint, option.SourceKey);
        var evidence = Assert.Single((await new InventoryEvidenceLoader(db).LoadAsync(new(9001, [9003]), DateTimeOffset.UtcNow, default)).Positions);
        var replay = InventoryEventReplay.Replay(evidence);
        Assert.True(replay.QuantityConserved);
        Assert.Empty(replay.UnresolvedEvents);
        Assert.Equal(220, replay.Cohorts.Where(x => x.ReceiptId == null).Sum(x => x.Quantity));
        Assert.Equal(66, replay.Cohorts.Where(x => x.ReceiptId == 100004).Sum(x => x.Quantity));
        Assert.All(replay.Cohorts, x => Assert.Equal(800000, Assert.Single(x.ApplicationIds)));
        await AssertRendered(f, option);
        var form = Form(option, 40);
        var service = Service(db, executor);
        var beforeDispatch = await f.Snapshot();
        var sent = await service.DispatchAsync(form, default);
        Assert.False(sent.Success);
        Assert.Equal("Inter-company dispatch requires a configured cross-company route.", sent.Error);
        var command = Assert.Single(executor.Commands);
        Assert.Equal(InventoryCommandKind.InterCompanyDispatch, command.Kind);
        Assert.Equal(8000, command.ActorId);
        Assert.Equal("WP_DH", command.CustodyGroup);
        Assert.Equal(resolved.Identity.Key, Assert.Single(command.Lines).Source.Identity.Key);
        Assert.Equal(resolved.Watermark.Fingerprint, command.Lines[0].Source.ExpectedFingerprint);
        Assert.Equal(InventoryCommandStatus.Blocked, Assert.Single(executor.Results).Status);
        Assert.Equal("WP_DH", TruckReceiptRoutes.Group("MCD"));
        Assert.Equal(286, await f.Physical(9003));
        Assert.False(await db.InterCrewTransfers.AnyAsync());
        Assert.False(await db.TreatmentLineageMovements.AnyAsync());
        Assert.False(await db.TreatmentLineageSegments.AnyAsync());
        Assert.Equal(5, await db.RoomInventoryAdjustments.CountAsync(x => x.AdjustmentType == "ReceiptAdd"));
        Assert.Equal(beforeDispatch, await f.Snapshot());
        Assert.Equal(history, await History(f));
        // This is a blocker regression, not evidence of a completed dispatch or Truck Receipt rehearsal.
    }

    [InventoryPostgresFact]
    public async Task Qualified_selection_does_not_certify_origin_and_origin_rejection_has_no_writes()
    {
        await using var f = await HistoricalPool();
        await using (var seed = f.CreateDbContext())
        {
            var original = await seed.RoomInventoryAdjustments.AsNoTracking().SingleAsync(x => x.Id == 100000);
            var duplicate = JsonSerializer.Deserialize<RoomInventoryAdjustment>(JsonSerializer.Serialize(original))!;
            duplicate.Id = 0;
            duplicate.RoomId = 9002;
            duplicate.AdjustmentAt = DateTimeOffset.UtcNow;
            duplicate.CreatedAt = duplicate.AdjustmentAt;
            seed.Add(duplicate); // duplicate origin outside the selected source's historical occupancy
            await seed.SaveChangesAsync();
        }
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new RecordingExecutor(new InventoryCommandExecutor(factory));
        var option = await Source(db, executor);
        Assert.True(option.IsAvailable, option.UnavailableReason);
        Assert.Equal(286, option.TreatmentSelectionBins);
        await AssertRendered(f, option);
        var before = await f.Snapshot();
        var result = await Service(db, executor).DispatchAsync(Form(option, 40), default);
        Assert.False(result.Success);
        Assert.Contains("duplicate inventory origins", result.Error!);
        Assert.Equal(InventoryCommandStatus.Blocked, Assert.Single(executor.Results).Status);
        Assert.Equal(before, await f.Snapshot());
    }

    private static async Task AssertRendered(Fixture f, OutsideWarehouseInventoryOptionViewModel option)
    {
        await using var baseHost = new ReceiptLocationHttpTests.ReceiptHost(f.Connection, true);
        await using var host = baseHost.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(
            new Dictionary<string, string?> { ["TransferCustody:Enabled"] = "true" })));
        using var client = host.CreateClient(new() { AllowAutoRedirect = false });
        var before = await f.Snapshot();
        var html = await client.GetStringAsync("/BinsRun?Section=Transfer&TransferType=InterCrew&WarehouseId=9001&RoomId=9003");
        Assert.Contains("Position total / Treatment selection", html);
        Assert.Contains("not final transfer approval", html);
        Assert.Contains("Origin, custody, permissions and current inventory are validated when submitted", html);
        Assert.DoesNotContain("Transferable", html);
        Assert.Contains("286 / 286", html);
        var input = Assert.Single(Regex.Matches(html, "<input\\b[^>]*>").Select(x => WebUtility.HtmlDecode(x.Value)),
            x => x.Contains("name=\"SourceKey\"") && x.Contains(option.SourceKey));
        Assert.DoesNotContain("disabled", input);
        Assert.Equal(before, await f.Snapshot());
    }

    private static async Task<OutsideWarehouseInventoryOptionViewModel> Source(CropQcDbContext db, IInventoryCommandExecutor executor)
    {
        var option = Assert.Single(await CanonicalTruckReceiptWorkflowTests.Inventory(db, executor).GetInventoryAsync(default), x => x.RoomId == 9003);
        if (!option.IsAvailable)
        {
            var batch = await new InventoryEvidenceLoader(db).LoadAsync(new(9001, [9003]), DateTimeOffset.UtcNow, default);
            Assert.Fail(JsonSerializer.Serialize(batch.Positions.Select(p => new
            {
                p.Identity,
                p.AuthoritativeQuantity,
                Replay = InventoryEventReplay.Replay(p),
                p.RecordedArrivals,
                p.Applications
            })));
        }
        return option;
    }

    private static InterCrewTransferService Service(CropQcDbContext db, IInventoryCommandExecutor executor) => new(db,
        CanonicalTruckReceiptWorkflowTests.Inventory(db, executor), new CropQc.Web.Services.RoomInventoryLedgerQueryService(db),
        null!, null!, null!, new CanonicalOutsideWorkflowTests.Access(), CanonicalReceivingWorkflowTests.Operator(),
        new PacificBusinessTimeService(new SystemClock()), truckReceiptOptions: new() { Enabled = true }, canonicalCommands: executor);

    private static InterCrewDispatchForm Form(OutsideWarehouseInventoryOptionViewModel option, int bins) => new()
    {
        SourceWarehouseId = 9001,
        SourceRoomId = 9003,
        SourceKey = option.SourceKey,
        ExpectedAvailableBins = 286,
        DestinationCustodyGroup = "WP_DH",
        BinsLoaded = bins,
        LoadedAt = new PacificBusinessTimeService(new SystemClock()).NowPacific.DateTime,
        TruckLoadBolNumber = "LOCAL-HISTORICAL",
        Notes = "Disposable historical dispatch",
        ConfirmedReview = true
    };

    private sealed class RecordingExecutor(IInventoryCommandExecutor inner) : IInventoryCommandExecutor
    {
        public List<InventoryCommand> Commands { get; } = [];
        public List<InventoryCommandResult> Results { get; } = [];
        public async Task<InventoryCommandResult> ExecuteAsync(InventoryCommand command, CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            var result = await inner.ExecuteAsync(command, cancellationToken);
            Results.Add(result);
            return result;
        }
    }

    private static async Task<string> History(Fixture f)
    {
        await using var db = f.CreateDbContext();
        return JsonSerializer.Serialize(new
        {
            Receipts = await db.Receipts.AsNoTracking().Where(x => x.Id >= 100000 && x.Id <= 100004).OrderBy(x => x.Id).ToArrayAsync(),
            Ledger = await db.RoomInventoryAdjustments.AsNoTracking().Where(x => x.Id >= 100000 && x.Id <= 100016).OrderBy(x => x.Id).ToArrayAsync(),
            Transfers = await db.RoomTransfers.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
            Corrections = await db.ReceiptInventoryOverrides.AsNoTracking().ToArrayAsync(),
            Applications = await db.RoomTreatmentApplications.AsNoTracking().ToArrayAsync(),
            Sources = await db.RoomTreatmentApplicationSources.AsNoTracking().ToArrayAsync(),
            Audit = await db.AuditLogs.AsNoTracking().Where(x => x.Id == 100000).ToArrayAsync()
        });
    }

    // Synthetic analogue of Frenchman 1110 / MCD-15: +32,+64,+64,+60,-64,+66,+64 = 286.
    // Five historical transfer parents, an audited same-identity location correction, no modern movement/projection rows.
    // Treatment is explicitly recorded after the final arrival; no production treatment claim is implied by this fixture.
    private static async Task<Fixture> HistoricalPool()
    {
        var f = await Fixture.Create();
        await using var db = f.CreateDbContext();
        db.TreatmentLineageSegments.RemoveRange(await db.TreatmentLineageSegments.ToArrayAsync());
        db.RoomInventoryAdjustments.RemoveRange(await db.RoomInventoryAdjustments.ToArrayAsync());
        db.Receipts.RemoveRange(await db.Receipts.ToArrayAsync());
        await db.SaveChangesAsync();
        (await db.Warehouses.SingleAsync(x => x.Id == 9001)).Code = "MCD";
        (await db.Rooms.SingleAsync(x => x.Id == 9003)).Code = "MCD-15";
        (await db.Rooms.SingleAsync(x => x.Id == 9002)).Code = "MCD-16";
        var grower = await db.GrowerLots.SingleAsync(x => x.Id == 100000);
        grower.Grower = "Frenchman"; grower.LotNumber = "1110";
        db.Warehouses.Add(new() { Id = 9006, Code = "WP", Name = "Disposable WP" });
        db.Rooms.Add(new() { Id = 9007, WarehouseId = 9006, Code = "ARRIVAL", Name = "Disposable arrival" });
        for (var i = 0; i < 4; i++) db.Rooms.Add(new() { Id = 9100 + i, WarehouseId = 9001, Code = $"ORIGIN-{i}", Name = $"Historical source {i}" });
        var quantities = new[] { 32, 64, 64, 60, 66 };
        var start = InventoryEvidenceCorpus.Start;
        long ledgerId = 100000;
        RoomInventoryAdjustment Row(int room, int qty, string kind, DateTimeOffset at) => new()
        {
            Id = ledgerId++,
            CropYear = 2026,
            WarehouseId = 9001,
            RoomId = room,
            GrowerLotId = 100000,
            FruitProfileId = 9004,
            GrowerName = "Frenchman",
            LotNumber = "1110",
            VarietyCode = "CGAL",
            ChangeAmount = qty,
            NewBinCount = Math.Max(0, qty),
            AdjustmentType = kind,
            AdjustmentAt = at,
            CreatedAt = at
        };
        for (var i = 0; i < quantities.Length; i++)
        {
            var room = i == 4 ? 9003 : 9100 + i;
            var at = i == 4 ? start.AddDays(6) : start;
            db.Receipts.Add(new()
            {
                Id = 100000 + i,
                CropYear = 2026,
                CompuTechReceiptId = $"LOCAL-ORIGIN-{i}",
                WarehouseId = 9001,
                RoomId = i == 1 ? 9002 : room,
                GrowerLotId = 100000,
                FruitProfileId = 9004,
                GrowerName = "Frenchman",
                GrowerNumber = "1110",
                LotCode = "1110",
                BinCount = quantities[i],
                ReceivedAt = at,
                CreatedAt = at,
                UpdatedAt = at,
                ConcurrencyVersion = i == 1 ? 1 : 0
            });
            var row = Row(room, quantities[i], "ReceiptAdd", at); row.ReceiptId = 100000 + i; db.Add(row);
        }
        void Transfer(long id, int source, int bins, DateTimeOffset at)
        {
            db.RoomTransfers.Add(new()
            {
                Id = id,
                OperationKey = $"local-history-{id}",
                SourceWarehouseId = 9001,
                SourceRoomId = source,
                DestinationWarehouseId = 9001,
                DestinationRoomId = 9003,
                CropYear = 2026,
                GrowerLotId = 100000,
                FruitProfileId = 9004,
                GrowerName = "Frenchman",
                LotNumber = "1110",
                VarietyCode = "CGAL",
                BinCount = bins,
                Reason = "Recorded source movement",
                TransferredAt = at,
                CreatedAt = at
            });
            var debit = Row(source, -bins, "TransferOut", at); debit.RoomTransferId = id;
            var credit = Row(9003, bins, "TransferIn", at); credit.RoomTransferId = id;
            credit.CreatedAt = at.AddMilliseconds(1);
            db.AddRange(debit, credit);
        }
        for (var i = 0; i < 4; i++) Transfer(100000 + i, 9100 + i, quantities[i], start.AddDays(i + 1));
        string Snapshot(int room, int version) => JsonSerializer.Serialize(new
        {
            id = 100001,
            cropYear = 2026,
            warehouseId = 9001,
            roomId = room,
            growerLotId = 100000,
            fruitProfileId = 9004,
            growerNumber = "1110",
            lotCode = "1110",
            receiptType = "Truck receipt",
            receivedAt = start,
            binCount = 64,
            isDeleted = false,
            concurrencyVersion = version
        });
        var correction = new ReceiptInventoryOverride
        {
            Id = Guid.NewGuid(),
            ReceiptId = 100001,
            ActionType = "InventoryReclassification",
            OldReceiptBinCount = 64,
            NewReceiptBinCount = 64,
            InventoryDelta = 0,
            CurrentInventoryBefore = 220,
            CurrentInventoryAfter = 156,
            AdministratorUserId = 8000,
            Reason = "Recorded location correction; not evidence of exact physical allocation",
            OperationKey = "local-history-correction",
            CreatedAt = start.AddDays(5),
            BeforeReceiptSnapshotJson = Snapshot(9003, 0),
            AfterReceiptSnapshotJson = Snapshot(9002, 1),
            AffectedInventorySnapshotJson = "[]",
            ExpectedAdjustmentCount = 2,
            IsComplete = true
        };
        db.Add(correction);
        foreach (var (room, qty) in new[] { (9003, -64), (9002, 64) })
        {
            var row = Row(room, qty, "ReceiptAdminOverride", correction.CreatedAt);
            row.ReceiptId = 100001; row.ReceiptInventoryOverrideId = correction.Id; db.Add(row);
        }
        db.AuditLogs.Add(new()
        {
            Id = 100000,
            EntityName = "ReceiptInventoryOverride",
            EntityKey = correction.Id.ToString(),
            Action = correction.ActionType,
            UserId = 8000,
            CreatedAt = correction.CreatedAt,
            BeforeValuesJson = correction.BeforeReceiptSnapshotJson,
            AfterValuesJson = JsonSerializer.Serialize(new { afterReceiptSnapshotJson = correction.AfterReceiptSnapshotJson })
        });
        Transfer(100004, 9002, 64, start.AddDays(7));
        var chemical = await db.TreatmentChemicals.FirstAsync(x => x.IsActive && x.ApplicationLevel == "Room");
        var identity = new InventoryIdentity(2026, 100000, 9004, "1110", "1110", "CGAL", "Conventional", false, "");
        var treatment = new RoomTreatmentApplication
        {
            Id = 800000,
            OperationKey = "local-history-treatment",
            TreatmentChemicalId = chemical.Id,
            WarehouseId = 9001,
            RoomId = 9003,
            AppliedByUserId = 8000,
            CreatedByUserId = 8000,
            TotalBinsSnapshot = 286,
            ProductNameSnapshot = chemical.ProductName,
            CropSnapshot = chemical.Crop,
            UnitSnapshot = chemical.Unit,
            CurrencySnapshot = chemical.Currency,
            AppliedAt = start.AddDays(8),
            CreatedAt = start.AddDays(8)
        };
        foreach (var (receipt, bins) in new[] { ((long?)null, 220), ((long?)100004, 66) }) treatment.Sources.Add(new()
        {
            ReceiptId = receipt,
            CropYear = 2026,
            GrowerLotId = 100000,
            FruitProfileId = 9004,
            IdentityKey = identity.Key,
            GrowerNumberSnapshot = "1110",
            GrowerNameSnapshot = "Frenchman",
            LotNumberSnapshot = "1110",
            VarietyCodeSnapshot = "CGAL",
            ProductionTypeSnapshot = "Conventional",
            IsOrganicSnapshot = false,
            BinsTreated = bins,
            PriorTreatmentSignature = "u",
            ResultTreatmentSignature = "u|a:800000"
        });
        db.Add(treatment);
        await db.SaveChangesAsync();
        return f;
    }
}
