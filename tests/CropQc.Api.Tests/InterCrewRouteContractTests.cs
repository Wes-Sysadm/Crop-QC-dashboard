using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class InterCrewRouteContractTests
{
    [Theory]
    [InlineData("McDougall", "WP_DH", true, false)]
    [InlineData(" mcd ", "WP_DH", true, false)]
    [InlineData("McDougall", "EBS", true, true)]
    [InlineData("EBS", "WP_DH", true, true)]
    [InlineData("WP", "EBS", true, true)]
    [InlineData("DH", "EBS", true, true)]
    [InlineData("WP", "WP_DH", false, false)]
    [InlineData("DH", "WP_DH", false, false)]
    [InlineData("EBS", "EBS", false, false)]
    [InlineData("Unknown", "WP_DH", false, false)]
    [InlineData("McDougall", "Unknown", false, false)]
    [InlineData(null, "EBS", false, false)]
    public void Supported_dispatch_and_cross_company_receipt_are_distinct(string? source, string group, bool allowed, bool crossCompany)
    {
        Assert.Equal(allowed, TruckReceiptRoutes.AllowsInterCrewDispatch(source, group));
        Assert.Equal(allowed, TruckReceiptRoutes.InterCrewDestinations(source).Contains(group));
        Assert.Equal(crossCompany, TruckReceiptRoutes.RequiresReceiptForGroup(source, group));
    }

    [InventoryPostgresTheory]
    [InlineData("EBS", "WP", "WP_DH")]
    [InlineData("McDougall", "EBS", "EBS")]
    public async Task Cross_company_dispatch_requires_exact_matched_receipt_and_cannot_use_internal_receive(string source, string destination, string group)
    {
        await using var f = await RouteFixture(source, destination);
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        var executor = new InventoryCommandExecutor(factory);
        var option = Assert.Single(await CanonicalTruckReceiptWorkflowTests.Inventory(db, executor).GetInventoryAsync(default));
        var service = HistoricalInterCrewDispatchTests.Service(db, executor);
        var form = new InterCrewDispatchForm
        {
            SourceWarehouseId = 9001,
            SourceRoomId = 9002,
            SourceKey = option.SourceKey,
            ExpectedAvailableBins = 19,
            DestinationCustodyGroup = group,
            BinsLoaded = 19,
            LoadedAt = new PacificBusinessTimeService(new SystemClock()).NowPacific.DateTime,
            TruckLoadBolNumber = "LOCAL-CROSS-COMPANY",
            ConfirmedReview = true
        };
        var sent = await service.DispatchAsync(form, default);
        Assert.True(sent.Success, sent.Error);
        var parent = await db.InterCrewTransfers.AsNoTracking().SingleAsync();
        Assert.True(parent.RequiresTruckReceipt);
        Assert.Equal(0, await f.Physical());
        var saved = await f.Snapshot();
        Assert.False((await service.ReceiveAsync(new()
        {
            TransferId = parent.Id,
            DestinationRoomId = 9007,
            BinsReceived = 19,
            ReceivedAt = new PacificBusinessTimeService(new SystemClock()).NowPacific.DateTime
        }, default)).Success);
        Assert.Equal(saved, await f.Snapshot());
        var receipt = new Receipt
        {
            CropYear = 2026,
            CompuTechReceiptId = "LOCAL-CROSS-RECEIPT",
            IsTransferReceipt = true,
            GrowerName = "Transfer",
            LotCode = "TRANSFER",
            WarehouseId = 9006,
            RoomId = 9007,
            FruitProfileId = 9004,
            GrowerLotId = 100000,
            BinCount = 18,
            ReceivedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        receipt.VarietyLines.Add(new() { FruitProfileId = 9004, BinCount = 18 });
        db.Receipts.Add(receipt);
        await db.SaveChangesAsync();
        var truck = CanonicalTruckReceiptWorkflowTests.Service(db, executor);
        async Task<TruckReceiptActionForm> Reconcile() => new()
        {
            TransferId = parent.Id,
            ReceiptId = receipt.Id,
            TransferVersion = await db.InterCrewTransfers.Where(x => x.Id == parent.Id).Select(x => x.ConcurrencyVersion).SingleAsync(),
            ReceiptVersion = await db.Receipts.Where(x => x.Id == receipt.Id).Select(x => x.ConcurrencyVersion).SingleAsync(),
            Reason = "Disposable cross-company receipt reconciliation"
        };
        saved = await f.Snapshot();
        Assert.NotNull(await truck.CompleteAsync(await Reconcile(), default)); // unmatched
        Assert.Equal(saved, await f.Snapshot());
        Assert.Null(await truck.MatchAsync(await Reconcile(), default));
        saved = await f.Snapshot();
        Assert.NotNull(await truck.CompleteAsync(await Reconcile(), default)); // 18 != 19
        Assert.Equal(saved, await f.Snapshot());
        var edit = await Reconcile(); edit.Lines = [new() { FruitProfileId = 9004, BinCount = 19 }];
        Assert.Null(await truck.EditReceiptAsync(edit, default));
        var complete = await Reconcile();
        Assert.Null(await truck.CompleteAsync(complete, default));
        Assert.Equal(19, await db.RoomInventoryAdjustments.Where(x => x.RoomId == 9007).SumAsync(x => x.ChangeAmount));
        Assert.Equal(19, await db.RoomInventoryAdjustments.SumAsync(x => x.ChangeAmount));
        var currentTransit = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(
            new(9001, [], InventoryCustody.InTransit), new(AllowedCustody: InventoryCustody.InTransit), DateTimeOffset.UtcNow);
        Assert.Empty(currentTransit.Positions);
        Assert.Equal(1, await db.RoomInventoryAdjustments.CountAsync(x => x.AdjustmentType == "ReceiptAdd"));
        Assert.False(await db.RoomInventoryAdjustments.AnyAsync(x => x.ReceiptId == receipt.Id));
        var segments = await db.TreatmentLineageSegments.AsNoTracking().Where(x => x.CurrentBins > 0 && x.Disposition == "Current").ToArrayAsync();
        Assert.Equal(19, segments.Sum(x => x.CurrentBins));
        Assert.All(segments, x => Assert.Equal(100000, x.ReceiptId));
        Assert.Empty((await InterCrewTopologyReadiness.VerifyAsync(db, default)).Issues);
        saved = await f.Snapshot();
        Assert.Null(await truck.CompleteAsync(complete, default));
        Assert.Equal(saved, await f.Snapshot());
    }

    [InventoryPostgresTheory]
    [InlineData("WP", "WP_DH")]
    [InlineData("DH", "WP_DH")]
    [InlineData("EBS", "EBS")]
    [InlineData("Unknown", "EBS")]
    [InlineData("MCD", "Unknown")]
    public async Task Unsupported_routes_reject_at_the_canonical_writer_without_any_changes(string source, string group)
    {
        await using var f = await RouteFixture(source, source == "EBS" ? "WP" : "EBS");
        var command = (await f.Command(InventoryCommandKind.InterCompanyDispatch, 7)) with { CustodyGroup = group };
        var saved = await f.Snapshot();
        var result = await f.Execute(command);
        Assert.Equal(InventoryCommandStatus.Blocked, result.Status);
        Assert.Equal(saved, await f.Snapshot());
    }

    private static async Task<Fixture> RouteFixture(string source, string destination)
    {
        var f = await Fixture.Create();
        await using var db = f.CreateDbContext();
        foreach (var existing in await db.Warehouses.Where(x => x.Id != 9001 && (x.Code == source || x.Code == destination)).ToArrayAsync())
            existing.Code = $"BASE-{existing.Id}";
        await db.SaveChangesAsync();
        (await db.Warehouses.SingleAsync(x => x.Id == 9001)).Code = source;
        db.Warehouses.Add(new() { Id = 9006, Code = destination, Name = "Disposable route destination" });
        db.Rooms.Add(new() { Id = 9007, WarehouseId = 9006, Code = "ARRIVAL", Name = "Disposable arrival" });
        await db.SaveChangesAsync();
        return f;
    }
}
