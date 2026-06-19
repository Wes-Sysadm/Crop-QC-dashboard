using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Storage;
using CropQc.Web.Auth;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using System.Security.Claims;

namespace CropQc.Api.Tests;

public sealed class Evanca12RoomCountTests
{
    [Fact]
    public async Task VerifiedEbsCurrentBalanceCorrectionsSetRoomTotalsAndExcludeSamplesDuplicates()
    {
        await using var db = CreateDbContext();
        await SeedVerifiedEbsInventoryAsync(db);
        var service = CreateService(db);

        var detail = await service.GetRoomDetailAsync(12, CancellationToken.None);
        var breakdown = await service.GetRoomCountBreakdownAsync(12, CancellationToken.None);
        var truckDetail = await service.GetRoomDetailAsync(200, CancellationToken.None);

        await AssertRoomTotalAsync(service, 1, 1201);
        await AssertRoomTotalAsync(service, 12, 1022);
        await AssertRoomTotalAsync(service, 17, 1918);
        await AssertRoomTotalAsync(service, 101, 1178);
        await AssertRoomTotalAsync(service, 106, 514);
        await AssertRoomTotalAsync(service, 104, 0);
        Assert.Equal(40, truckDetail.Summary?.CurrentBinsCount);
        Assert.NotNull(detail.Summary);
        Assert.Equal(1022, detail.Summary!.CurrentBinsCount);
        Assert.Equal("FUJI Sealed: 1022 bins", detail.Summary.VarietyStatusSummary);
        Assert.Equal(3, detail.CurrentLots.Count);
        Assert.Equal(1022, breakdown.IncludedBins);
        Assert.Contains(detail.CurrentLots, x => x.InventoryStatus == "Sealed" && x.LotCode == "1570" && x.CurrentBins == 819);
        Assert.Contains(breakdown.Rows, x => x.SourceType == "Current Inventory Baseline" && x.IsIncluded && x.Lot == "1570" && x.Bins == 819 && x.Variety == "FUJI" && x.Status == "Sealed");
        Assert.Contains(breakdown.Rows, x => x.SourceType == "Current Inventory Baseline" && !x.IsIncluded && x.Bins == 1469 && x.DecisionReason.Contains("superseded", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(breakdown.Rows, x => x.SourceType == "Receipt" && x.SampleType.Contains("Truck Sample", StringComparison.OrdinalIgnoreCase) && !x.IsIncluded && x.DecisionReason.Contains("superseded", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(breakdown.Rows, x => x.SourceType == "Receipt" && x.DisplayReceiptId == "LS-EVANCA12-1" && !x.IsIncluded && x.DecisionReason == "Excluded: LS prefix.");
        Assert.Contains(breakdown.Rows, x => x.SourceType == "Receipt" && x.DisplayReceiptId == "DS-EVANCA12-1" && !x.IsIncluded && x.DecisionReason == "Excluded: DS prefix.");
        Assert.Contains(breakdown.Rows, x => x.SourceType == "Receipt" && x.DisplayReceiptId == "EVANCA12-DOOR-TYPE" && x.SampleType.Contains("Door Sample", StringComparison.OrdinalIgnoreCase) && !x.IsIncluded && x.DecisionReason == "Excluded: Door Sample.");
        Assert.Contains(breakdown.Rows, x => x.SourceType == "Receipt" && x.DisplayReceiptId == "EVANCA12-LOT-TYPE" && x.SampleType.Contains("Lot Sample", StringComparison.OrdinalIgnoreCase) && !x.IsIncluded && x.DecisionReason == "Excluded: Lot Sample.");
    }

    [Fact]
    public async Task CurrentInventoryBaseline_AllowsFutureTruckReceiptsAfterBaseline()
    {
        await using var db = CreateDbContext();
        await SeedVerifiedEbsInventoryAsync(db);
        var warehouse = await db.Warehouses.FirstAsync(x => x.Code == "EBS");
        var room = await db.Rooms.FirstAsync(x => x.Code == "EVANCA12");
        var fuji = await db.FruitProfiles.FirstAsync(x => x.VarietyCode == "FUJI");
        var receivedAt = DateTimeOffset.Parse("2026-06-18T07:30:00-07:00");
        db.Receipts.Add(Receipt(130, "EVANCA12-FUTURE-TRUCK", "Truck receipt", 10, receivedAt, warehouse, room, fuji));
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var detail = await service.GetRoomDetailAsync(12, CancellationToken.None);
        var breakdown = await service.GetRoomCountBreakdownAsync(12, CancellationToken.None);

        Assert.Equal(1032, detail.Summary?.CurrentBinsCount);
        Assert.Contains(breakdown.Rows, x => x.SourceType == "Receipt" && x.DisplayReceiptId == "EVANCA12-FUTURE-TRUCK" && x.IsIncluded && x.DecisionReason == "Included: Truck Receipt.");
    }

    [Fact]
    public async Task CurrentInventoryBaselineImport_ValidExamplePreviewsRoomTotal()
    {
        await using var db = CreateDbContext();
        SeedImportMasterData(db);
        var service = CreateImportService(db);

        var preview = await service.PreviewAsync(new RoomInventoryImportForm
        {
            CsvText = """
CropYear,Warehouse,RoomCode,Grower,Lot,Variety,Bins,Status,EffectiveDate,Notes
2026,EBS,EVANCA12,,1560,FUJI,118,Sealed,2026-06-18,Wes verified baseline
2026,EBS,EVANCA12,,1570,FUJI,819,Sealed,2026-06-18,Wes verified baseline
2026,EBS,EVANCA12,,1030,FUJI,85,Sealed,2026-06-18,Wes verified baseline
"""
        }, CancellationToken.None);

        Assert.True(preview.CanApply, string.Join(" | ", preview.Rows.Select(x => $"{x.RowNumber}:{x.Action}:{x.Message}")));
        Assert.Equal(3, preview.AddCount);
        var total = Assert.Single(preview.RoomTotals);
        Assert.Equal("EVANCA12", total.RoomCode);
        Assert.Equal("FUJI", total.Variety);
        Assert.Equal("Sealed", total.Status);
        Assert.Equal(3, total.LotCount);
        Assert.Equal(1022, total.BinCount);
    }

    [Fact]
    public async Task CurrentInventoryBaselineImport_ValidImportAppliesBaseline()
    {
        await using var db = CreateDbContext();
        SeedImportMasterData(db);
        var service = CreateImportService(db);
        var csv = """
CropYear,Warehouse,RoomCode,Grower,Lot,Variety,Bins,Status,EffectiveDate,Notes
2025,EBS,evanca12,,1560,Fuji,118,Sealed,2026-06-18,Wes verified baseline
2025,EBS,evanca12,,1570,Fuji,819,Sealed,2026-06-18,Wes verified baseline
2025,EBS,evanca12,,1030,Fuji,85,Sealed,2026-06-18,Wes verified baseline
""";

        var result = await service.ApplyAsync(new RoomInventoryImportForm { CsvText = csv, ConfirmImport = true }, "wes@fruitandland.com", CancellationToken.None);

        Assert.Null(result.Error);
        Assert.Equal(3, await db.RoomInventoryAdjustments.CountAsync(CancellationToken.None));
        var current = await CreateService(db).GetRoomDetailAsync(12, CancellationToken.None);
        Assert.Equal(1022, current.Summary?.CurrentBinsCount);
        Assert.Equal("FUJI Sealed: 1022 bins", current.Summary?.VarietyStatusSummary);
    }

    [Fact]
    public async Task CurrentInventoryBaselineImport_BadColumnsShowValidationError()
    {
        await using var db = CreateDbContext();
        SeedImportMasterData(db);
        var service = CreateImportService(db);

        var preview = await service.PreviewAsync(new RoomInventoryImportForm
        {
            CsvText = """
Warehouse,RoomCode,Lot,Variety,Bins
EBS,EVANCA12,1570,FUJI,819
"""
        }, CancellationToken.None);

        Assert.False(preview.CanApply);
        Assert.Equal(1, preview.InvalidCount);
        Assert.Contains(preview.Rows, x => x.Column == "Headers" && x.Message.Contains("CropYear", StringComparison.OrdinalIgnoreCase) && x.Message.Contains("EffectiveDate", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task CurrentInventoryBaselineImport_InvalidBinsShowsRowAndColumn()
    {
        await using var db = CreateDbContext();
        SeedImportMasterData(db);
        var service = CreateImportService(db);

        var preview = await service.PreviewAsync(new RoomInventoryImportForm
        {
            CsvText = """
CropYear,Warehouse,RoomCode,Grower,Lot,Variety,Bins,Status,EffectiveDate,Notes
2025,EBS,EVANCA12,,1570,FUJI,not-a-number,Sealed,2026-06-18,Wes verified baseline
"""
        }, CancellationToken.None);

        var error = Assert.Single(preview.Rows);
        Assert.Equal(2, error.RowNumber);
        Assert.Equal("Bins", error.Column);
        Assert.Contains("not-a-number", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CurrentInventoryBaselineImport_InvalidDateShowsRowAndColumn()
    {
        await using var db = CreateDbContext();
        SeedImportMasterData(db);
        var service = CreateImportService(db);

        var preview = await service.PreviewAsync(new RoomInventoryImportForm
        {
            CsvText = """
CropYear,Warehouse,RoomCode,Grower,Lot,Variety,Bins,Status,EffectiveDate,Notes
2025,EBS,EVANCA12,,1570,FUJI,819,Sealed,06/18/2026,Wes verified baseline
"""
        }, CancellationToken.None);

        var error = Assert.Single(preview.Rows);
        Assert.Equal(2, error.RowNumber);
        Assert.Equal("EffectiveDate", error.Column);
        Assert.Contains("YYYY-MM-DD", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CurrentInventoryBaselineImport_UnknownRoomCodeShowsClearRowError()
    {
        await using var db = CreateDbContext();
        SeedImportMasterData(db);
        var service = CreateImportService(db);

        var preview = await service.PreviewAsync(new RoomInventoryImportForm
        {
            CsvText = """
CropYear,Warehouse,RoomCode,Grower,Lot,Variety,Bins,Status,EffectiveDate,Notes
2025,EBS,UNKNOWN99,,1570,FUJI,819,Sealed,2026-06-18,Wes verified baseline
"""
        }, CancellationToken.None);

        var error = Assert.Single(preview.Rows);
        Assert.Equal(2, error.RowNumber);
        Assert.Equal("RoomCode", error.Column);
        Assert.Contains("UNKNOWN99", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CurrentInventoryBaselineImport_PreventsSilentDuplicateBatchImport()
    {
        await using var db = CreateDbContext();
        SeedImportMasterData(db);
        var service = CreateImportService(db);
        var csv = """
CropYear,Warehouse,RoomCode,Grower,Lot,Variety,Bins,Status,EffectiveDate,Notes
2026,EBS,EVANCA12,,1570,FUJI,819,Sealed,2026-06-18,Wes verified baseline
""";

        var first = await service.ApplyAsync(new RoomInventoryImportForm { CsvText = csv, ConfirmImport = true }, "wes@fruitandland.com", CancellationToken.None);
        Assert.Null(first.Error);

        var secondPreview = await service.PreviewAsync(new RoomInventoryImportForm { CsvText = csv }, CancellationToken.None);
        Assert.False(secondPreview.CanApply);
        Assert.Equal(1, secondPreview.UnchangedCount);
        Assert.Equal(819, secondPreview.RoomTotals.Single().BinCount);

        var changedCsv = csv.Replace(",819,", ",820,", StringComparison.Ordinal);
        var replacement = await service.ApplyAsync(new RoomInventoryImportForm { CsvText = changedCsv, ConfirmImport = true }, "wes@fruitandland.com", CancellationToken.None);
        Assert.NotNull(replacement.Error);
        Assert.True(replacement.Preview.RequiresReplaceConfirmation);

        var confirmed = await service.ApplyAsync(new RoomInventoryImportForm { CsvText = changedCsv, ConfirmImport = true, ConfirmReplaceExistingBatch = true }, "wes@fruitandland.com", CancellationToken.None);
        Assert.Null(confirmed.Error);
        Assert.Equal(2, await db.RoomInventoryAdjustments.CountAsync(CancellationToken.None));
        var current = await CreateService(db).GetRoomDetailAsync(12, CancellationToken.None);
        Assert.Equal(820, current.Summary?.CurrentBinsCount);
    }

    [Fact]
    public async Task CurrentLots_LoadsBaselineRowsAndExcludesLsDsRows()
    {
        await using var db = CreateDbContext();
        await SeedVerifiedEbsInventoryAsync(db);
        var service = CreateService(db);

        var page = await service.GetCurrentGrowerLotsAsync(new CurrentGrowerLotsFilterForm { CropYear = 2026, RoomId = 12 }, CancellationToken.None);

        Assert.Null(page.DataWarning);
        Assert.Equal(3, page.Lots.Count);
        Assert.Equal(1022, page.Lots.Sum(x => x.CurrentBins));
        Assert.Contains(page.Lots, x => x.Lot == "1570" && x.Variety == "FUJI" && x.CurrentBins == 819);
        Assert.DoesNotContain(page.Lots, x => x.CurrentBins is 700 or 800);
    }

    [Fact]
    public async Task LotAndDoorSamplesUpdateCurrentConditionWithoutAddingBins()
    {
        await using var db = CreateDbContext();
        await SeedVerifiedEbsInventoryAsync(db);
        var warehouse = await db.Warehouses.FirstAsync(x => x.Code == "EBS");
        var room = await db.Rooms.FirstAsync(x => x.Code == "EVANCA12");
        var fuji = await db.FruitProfiles.FirstAsync(x => x.VarietyCode == "FUJI");
        var lotSample = await db.SampleTypes.FirstAsync(x => x.Name == "Lot Sample");
        var doorSample = await db.SampleTypes.FirstAsync(x => x.Name == "Door Sample");
        var firstAt = DateTimeOffset.Parse("2026-06-18T10:00:00-07:00");
        var latestAt = DateTimeOffset.Parse("2026-06-19T10:00:00-07:00");
        var doorAt = DateTimeOffset.Parse("2026-06-19T11:00:00-07:00");
        var lotReceipt1 = Receipt(930, "LS-EVANCA12-1570-A", "Truck receipt", 700, firstAt, warehouse, room, fuji);
        var lotReceipt2 = Receipt(931, "LS-EVANCA12-1570-B", "Truck receipt", 800, latestAt, warehouse, room, fuji);
        var doorReceipt = Receipt(932, "DS-EVANCA12-1030", "Truck receipt", 900, doorAt, warehouse, room, fuji);
        lotReceipt1.GrowerNumber = "1570";
        lotReceipt1.LotCode = "1570";
        lotReceipt2.GrowerNumber = "1570";
        lotReceipt2.LotCode = "1570";
        doorReceipt.GrowerNumber = "1030";
        doorReceipt.LotCode = "1030";
        db.Receipts.AddRange(lotReceipt1, lotReceipt2, doorReceipt);
        db.QcSamples.AddRange(
            Sample(9300, lotReceipt1.Id, lotSample, firstAt),
            Sample(9301, lotReceipt2.Id, lotSample, latestAt),
            Sample(9302, doorReceipt.Id, doorSample, doorAt));
        db.QcFruitReadings.AddRange(
            FruitReading(93000, 9300, 14m),
            FruitReading(93001, 9301, 12m),
            FruitReading(93002, 9302, 10m));
        await db.SaveChangesAsync();

        var service = CreateService(db);
        var detail = await service.GetRoomDetailAsync(12, CancellationToken.None);
        var currentLots = await service.GetCurrentGrowerLotsAsync(new CurrentGrowerLotsFilterForm { CropYear = 2026, RoomId = 12 }, CancellationToken.None);
        var review = await service.GetCropYearReviewAsync(new CropYearReviewFilterForm { CropYear = 2026, WarehouseId = warehouse.Id }, CancellationToken.None);

        Assert.Equal(1022, detail.Summary?.CurrentBinsCount);
        Assert.Contains(detail.CurrentLots, x => x.LotCode == "1570" && x.CurrentBins == 819 && x.AveragePressureLbs == 12m && x.LatestQcSource == "Lot Sample");
        Assert.Contains(detail.CurrentLots, x => x.LotCode == "1030" && x.CurrentBins == 85 && x.AveragePressureLbs == 10m && x.LatestQcSource == "Door Sample");
        Assert.Contains(detail.CurrentLots.Single(x => x.LotCode == "1570").Samples, x => x.DisplayReceiptId == "LS-EVANCA12-1570-B" && x.SampleType == "Lot Sample");
        Assert.Contains(detail.CurrentLots.Single(x => x.LotCode == "1030").Samples, x => x.DisplayReceiptId == "DS-EVANCA12-1030" && x.SampleType == "Door Sample");
        Assert.Contains(currentLots.Lots, x => x.Lot == "1570" && x.CurrentBins == 819 && x.LatestAveragePressure == 12m && x.LatestQcSource == "Lot Sample");
        Assert.Contains(currentLots.Lots, x => x.Lot == "1030" && x.CurrentBins == 85 && x.LatestAveragePressure == 10m && x.LatestQcSource == "Door Sample");
        Assert.Contains(review.Rows, x => x.Lot == "1570" && x.SampleType == "Lot Sample" && x.AveragePressure == 12m && x.PressureChange == -2m && x.PressureLossPerWeek == 14m);
        Assert.Contains(review.Rows, x => x.Lot == "1030" && x.SampleType == "Door Sample" && x.AveragePressure == 10m);
    }

    [Fact]
    public async Task CurrentLots_HandlesDuplicateLotNumbersMissingGrowerAndBlankStatus()
    {
        await using var db = CreateDbContext();
        await SeedVerifiedEbsInventoryAsync(db);
        var warehouse = await db.Warehouses.FirstAsync(x => x.Code == "EBS");
        var room = await db.Rooms.FirstAsync(x => x.Code == "LAMBCA17");
        var pink = await db.FruitProfiles.FirstAsync(x => x.VarietyCode == "PINK");
        var at = DateTimeOffset.Parse("2026-06-19T00:00:00-07:00");
        db.RoomInventoryAdjustments.AddRange(
            CurrentCorrection(900, warehouse, room, pink, "1020", 226, at, "Duplicate lot regression"),
            CurrentCorrection(901, warehouse, room, pink, "1020", 333, at, "Duplicate lot regression"),
            CurrentCorrection(902, warehouse, room, pink, "1050", 1359, at, "Duplicate lot regression", "Sealed"));
        db.RoomInventoryAdjustments.Local.Single(x => x.Id == 900).GrowerName = "";
        db.RoomInventoryAdjustments.Local.Single(x => x.Id == 901).GrowerName = "";
        db.RoomInventoryAdjustments.Local.Single(x => x.Id == 900).InventoryStatus = "";
        db.RoomInventoryAdjustments.Local.Single(x => x.Id == 901).InventoryStatus = "";
        var batchCreatedAt = DateTimeOffset.Parse("2026-06-19T08:00:00-07:00");
        db.RoomInventoryAdjustments.Local.Single(x => x.Id == 900).CreatedAt = batchCreatedAt;
        db.RoomInventoryAdjustments.Local.Single(x => x.Id == 901).CreatedAt = batchCreatedAt;
        db.RoomInventoryAdjustments.Local.Single(x => x.Id == 902).CreatedAt = batchCreatedAt;
        await db.SaveChangesAsync();
        var service = CreateService(db);

        var detail = await service.GetRoomDetailAsync(17, CancellationToken.None);
        var page = await service.GetCurrentGrowerLotsAsync(new CurrentGrowerLotsFilterForm { CropYear = 2026, RoomId = 17 }, CancellationToken.None);

        Assert.Equal(1918, detail.Summary?.CurrentBinsCount);
        Assert.Contains(detail.CurrentLots, x => x.LotCode == "1020" && x.CurrentBins == 226);
        Assert.Contains(detail.CurrentLots, x => x.LotCode == "1020" && x.CurrentBins == 333);
        Assert.Contains(page.Lots, x => x.Lot == "1020" && x.CurrentBins == 226 && x.Grower == "");
        Assert.Contains(page.Lots, x => x.Lot == "1020" && x.CurrentBins == 333 && x.Grower == "");
    }

    [Fact]
    public async Task CurrentLots_AdminPageShowsClearDiagnosticForUnknownRoomMapping()
    {
        await using var db = CreateDbContext();
        SeedImportMasterData(db);
        var warehouse = await db.Warehouses.FirstAsync(x => x.Code == "EBS");
        var room = await db.Rooms.FirstAsync(x => x.Code == "EVANCA12");
        var fuji = await db.FruitProfiles.FirstAsync(x => x.VarietyCode == "FUJI");
        db.RoomInventoryAdjustments.Add(new RoomInventoryAdjustment
        {
            Id = 920,
            CropYear = 2026,
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            RoomId = room.Id,
            Room = room,
            FruitProfileId = fuji.Id,
            FruitProfile = fuji,
            GrowerName = "",
            LotNumber = "1570",
            VarietyCode = "FUJI",
            ChangeAmount = 819,
            NewBinCount = 819,
            AdjustmentType = RoomInventoryImportService.StartingInventoryAdjustmentType,
            SourceRoomCode = "UNKNOWN99",
            Source = "Bad row regression",
            Reason = "Bad row regression",
            AdjustmentAt = DateTimeOffset.Parse("2026-06-18T00:00:00-07:00"),
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateImportService(db);

        var page = await service.GetPageAsync(new RoomInventoryImportForm { Facility = "EBS" }, CancellationToken.None);

        Assert.Empty(page.CurrentLots);
        Assert.NotNull(page.CurrentLotWarning);
        var row = Assert.Single(page.CurrentLotBreakdown);
        Assert.False(row.IsIncluded);
        Assert.Equal("UNKNOWN99", row.CompuTechRoomCode);
        Assert.Contains("does not map", row.DecisionReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CurrentInventoryBaselineImport_PageDoesNotOnlyShowGenericError()
    {
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "RoomInventoryController.cs"));
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "RoomInventoryImportService.cs"));
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "RoomInventory", "Index.cshtml"));

        Assert.Contains("logger.LogError(exception", controller);
        Assert.Contains("ImportFailureAsync", controller);
        Assert.Contains("ServerFailurePreview", service);
        Assert.Contains("<th>Column</th>", view);
        Assert.Contains("row.Column", view);
        Assert.Contains("Import failed before it could complete", service);
    }

    [Fact]
    public void RoomCountBreakdown_IsRoutedAndShowsRequiredDebugColumns()
    {
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "HomeController.cs"));
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "RoomCountBreakdown.cshtml"));
        var room = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "Room.cshtml"));

        Assert.Contains("[HttpGet(\"/Rooms/{roomId:int}/CountBreakdown\")]", controller);
        Assert.Contains("GetRoomCountBreakdownAsync", service);
        Assert.Contains("BuildCurrentBalanceCorrectionCutoffsAsync", service);
        Assert.Contains("IsSupersededByRoomCurrentBalanceCorrection", service);
        Assert.Contains("BreakdownSourceType", service);
        Assert.Contains("Current Inventory Baseline", service);
        Assert.Contains("ReceiptStorageExclusionReason", service);
        Assert.Contains("HasStorageExcludedIdentifierPrefix", service);
        Assert.Contains("Excluded: LS prefix.", service);
        Assert.Contains("Excluded: DS prefix.", service);
        Assert.Contains("Excluded: Door Sample.", service);
        Assert.Contains("Excluded: Lot Sample.", service);
        Assert.Contains("Included: Truck Receipt.", service);
        Assert.Contains("Excluded: duplicate.", service);
        Assert.Contains("ReceiptDedupeKey", service);
        Assert.Contains("Source Type", view);
        Assert.Contains("Receipt ID", view);
        Assert.Contains("Sample Type", view);
        Assert.Contains("Included / Excluded", view);
        Assert.Contains("DecisionReason", view);
        Assert.Contains("View Count Breakdown", room);
    }

    [Fact]
    public void DashboardRoomsGrowerLotsAndEbsEmailUseSharedRoomLotCountService()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var email = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "EbsDailyBinsEmailService.cs"));

        Assert.Contains("GetHomeDashboardAsync", service);
        Assert.Contains("GetRoomsAsync", service);
        Assert.Contains("GetCurrentGrowerLotsAsync", service);
        Assert.Contains("BuildRoomLotSummariesAsync", service);
        Assert.Contains("await BuildRoomLotSummariesAsync(null, cancellationToken)", service);
        Assert.Contains("await BuildRoomSummariesAsync", service);
        Assert.Contains("dashboardDataService.GetHomeDashboardAsync", email);
        Assert.Contains("new RoomSummaryFilterForm { Facility = \"EBS\"", email);
    }

    private static CropQcDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new CropQcDbContext(options);
        db.Database.EnsureCreated();
        db.Users.Add(new User
        {
            Id = 9001,
            Email = "wes@fruitandland.com",
            DisplayName = "Wes",
            Domain = "fruitandland.com",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        return db;
    }

    private static async Task AssertRoomTotalAsync(DashboardDataService service, int roomId, int expectedBins)
    {
        var detail = await service.GetRoomDetailAsync(roomId, CancellationToken.None);
        Assert.NotNull(detail.Summary);
        Assert.Equal(expectedBins == 0 ? null : expectedBins, detail.Summary!.CurrentBinsCount);
    }

    private static async Task SeedVerifiedEbsInventoryAsync(CropQcDbContext db)
    {
        var warehouse = new Warehouse { Id = 901, Code = "EBS", Name = "Earl Brown and Sons" };
        var rooms = new[]
        {
            Room(1, "EVANCA01", "Evans 1", "Evans-01", "EVANCA01", "Evans", warehouse),
            Room(12, "EVANCA12", "Evans 12", "Evans-12", "EVANCA12", "Evans", warehouse),
            Room(17, "LAMBCA17", "Lamb 17", "Lamb-17", "LAMBCA17", "Lamb", warehouse),
            Room(101, "BLUECA01", "Blue Mountain 1", "BM-1", "BLUECA01", "BM", warehouse),
            Room(104, "BLUECA04", "Blue Mountain 4", "BM-4", "BLUECA04", "BM", warehouse),
            Room(106, "BLUECA06", "Blue Mountain 6", "BM-6", "BLUECA06", "BM", warehouse),
            Room(200, "TESTTRUCK", "Truck Count Test", "Truck Count Test", "TESTTRUCK", "Test", warehouse)
        };
        var roomByCode = rooms.ToDictionary(x => x.Code);
        var red = new FruitProfile { Id = 901, Name = "Red Delicious", VarietyCode = "RED", FruitType = "Apple", ProductionType = "Conventional" };
        var fuji = new FruitProfile { Id = 902, Name = "Fuji", VarietyCode = "FUJI", FruitType = "Apple", ProductionType = "Conventional" };
        var pink = new FruitProfile { Id = 903, Name = "Pink Lady", VarietyCode = "PINK", FruitType = "Apple", ProductionType = "Conventional" };
        var gsmt = new FruitProfile { Id = 904, Name = "GSMT", VarietyCode = "GSMT", FruitType = "Apple", ProductionType = "Conventional" };
        var truckSample = new SampleType { Id = 901, Name = "Truck Sample", IsActive = true };
        var doorSample = new SampleType { Id = 902, Name = "Door Sample", IsActive = true };
        var lotSample = new SampleType { Id = 903, Name = "Lot Sample", IsActive = true };
        db.Warehouses.Add(warehouse);
        db.Rooms.AddRange(rooms);
        db.FruitProfiles.AddRange(red, fuji, pink, gsmt);
        db.SampleTypes.AddRange(truckSample, doorSample, lotSample);

        var beforeCorrection = DateTimeOffset.Parse("2026-06-14T08:00:00-07:00");
        db.Receipts.AddRange(
            Receipt(100, "EVANCA12-OLD", "Truck receipt", 1103, beforeCorrection, warehouse, roomByCode["EVANCA12"], fuji),
            Receipt(101, "EVANCA12-OLD", "Truck receipt", 1103, beforeCorrection.AddMinutes(5), warehouse, roomByCode["EVANCA12"], fuji),
            Receipt(102, "EVANCA12-DOOR", "Door sample", 500, beforeCorrection.AddMinutes(10), warehouse, roomByCode["EVANCA12"], fuji),
            Receipt(103, "EVANCA12-LOT", "Lot sample", 600, beforeCorrection.AddMinutes(15), warehouse, roomByCode["EVANCA12"], fuji));
        db.QcSamples.AddRange(
            Sample(200, 100, truckSample),
            Sample(201, 102, doorSample),
            Sample(202, 103, lotSample));
        var oldAt = DateTimeOffset.Parse("2026-06-15T17:00:00-07:00");
        db.RoomInventoryAdjustments.AddRange(
            CurrentCorrection(300, warehouse, roomByCode["EVANCA12"], fuji, "Current", 1469, oldAt, "Wes Corrected Current Inventory 2026-06-15", "Sealed"),
            CurrentCorrection(301, warehouse, roomByCode["EVANCA01"], red, "Current", 1462, oldAt, "Wes Corrected Current Inventory 2026-06-15", "Sealed"),
            CurrentCorrection(302, warehouse, roomByCode["BLUECA04"], red, "Current", 186, oldAt, "Wes Corrected Current Inventory 2026-06-15"));
        var verifiedAt = DateTimeOffset.Parse("2026-06-18T00:00:00-07:00");
        db.RoomInventoryAdjustments.AddRange(
            CurrentCorrection(400, warehouse, roomByCode["EVANCA01"], red, "9285", 48, verifiedAt, "Wes Verified Current Inventory Baseline 2026-06-18"),
            CurrentCorrection(401, warehouse, roomByCode["EVANCA01"], red, "9490", 13, verifiedAt, "Wes Verified Current Inventory Baseline 2026-06-18"),
            CurrentCorrection(402, warehouse, roomByCode["EVANCA01"], red, "9570", 101, verifiedAt, "Wes Verified Current Inventory Baseline 2026-06-18"),
            CurrentCorrection(403, warehouse, roomByCode["EVANCA01"], red, "9660", 1039, verifiedAt, "Wes Verified Current Inventory Baseline 2026-06-18"),
            CurrentCorrection(404, warehouse, roomByCode["EVANCA12"], fuji, "1560", 118, verifiedAt, "Wes Verified Current Inventory Baseline 2026-06-18", "Sealed"),
            CurrentCorrection(405, warehouse, roomByCode["EVANCA12"], fuji, "1570", 819, verifiedAt, "Wes Verified Current Inventory Baseline 2026-06-18", "Sealed"),
            CurrentCorrection(406, warehouse, roomByCode["EVANCA12"], fuji, "1030", 85, verifiedAt, "Wes Verified Current Inventory Baseline 2026-06-18", "Sealed"),
            CurrentCorrection(407, warehouse, roomByCode["LAMBCA17"], pink, "1020", 559, verifiedAt, "Wes Verified Current Inventory Baseline 2026-06-18", "Sealed"),
            CurrentCorrection(408, warehouse, roomByCode["LAMBCA17"], pink, "1050", 1359, verifiedAt, "Wes Verified Current Inventory Baseline 2026-06-18", "Sealed"),
            CurrentCorrection(409, warehouse, roomByCode["BLUECA01"], red, "9510", 264, verifiedAt, "Wes Verified Current Inventory Baseline 2026-06-18", "Sealed"),
            CurrentCorrection(410, warehouse, roomByCode["BLUECA01"], red, "9550", 306, verifiedAt, "Wes Verified Current Inventory Baseline 2026-06-18", "Sealed"),
            CurrentCorrection(411, warehouse, roomByCode["BLUECA01"], red, "9560", 608, verifiedAt, "Wes Verified Current Inventory Baseline 2026-06-18", "Sealed"),
            CurrentCorrection(412, warehouse, roomByCode["BLUECA04"], red, "Current", 0, verifiedAt, "Wes Verified Current Inventory Baseline 2026-06-18"),
            CurrentCorrection(413, warehouse, roomByCode["BLUECA06"], gsmt, "1290", 281, verifiedAt, "Wes Verified Current Inventory Baseline 2026-06-18", "Sealed"),
            CurrentCorrection(414, warehouse, roomByCode["BLUECA06"], gsmt, "1560", 183, verifiedAt, "Wes Verified Current Inventory Baseline 2026-06-18", "Sealed"),
            CurrentCorrection(415, warehouse, roomByCode["BLUECA06"], gsmt, "3200", 3, verifiedAt, "Wes Verified Current Inventory Baseline 2026-06-18", "Sealed"),
            CurrentCorrection(416, warehouse, roomByCode["BLUECA06"], gsmt, "9450", 26, verifiedAt, "Wes Verified Current Inventory Baseline 2026-06-18", "Sealed"),
            CurrentCorrection(417, warehouse, roomByCode["BLUECA06"], gsmt, "9750", 21, verifiedAt, "Wes Verified Current Inventory Baseline 2026-06-18", "Sealed"));
        var afterCorrection = verifiedAt.AddMinutes(10);
        db.Receipts.AddRange(
            Receipt(110, "LS-EVANCA12-1", "Truck receipt", 700, afterCorrection, warehouse, roomByCode["EVANCA12"], fuji),
            Receipt(111, "DS-EVANCA12-1", "Truck receipt", 800, afterCorrection.AddMinutes(1), warehouse, roomByCode["EVANCA12"], fuji),
            Receipt(112, "EVANCA12-DOOR-TYPE", "Truck receipt", 900, afterCorrection.AddMinutes(2), warehouse, roomByCode["EVANCA12"], fuji),
            Receipt(113, "EVANCA12-LOT-TYPE", "Truck receipt", 1000, afterCorrection.AddMinutes(3), warehouse, roomByCode["EVANCA12"], fuji),
            Receipt(120, "TRUCK-COUNT-1", "Truck receipt", 40, afterCorrection.AddMinutes(4), warehouse, roomByCode["TESTTRUCK"], red));
        db.QcSamples.AddRange(
            Sample(210, 112, doorSample),
            Sample(211, 113, lotSample),
            Sample(220, 120, truckSample),
            Sample(221, 120, truckSample));
        await db.SaveChangesAsync();
    }

    private static void SeedImportMasterData(CropQcDbContext db)
    {
        var warehouse = new Warehouse { Id = 901, Code = "EBS", Name = "Earl Brown and Sons" };
        var room = Room(12, "EVANCA12", "Evans 12", "Evans-12", "EVANCA12", "Evans", warehouse);
        var fuji = new FruitProfile { Id = 902, Name = "Fuji", VarietyCode = "FUJI", FruitType = "Apple", ProductionType = "Conventional" };
        db.Warehouses.Add(warehouse);
        db.Rooms.Add(room);
        db.FruitProfiles.Add(fuji);
        db.SaveChanges();
    }

    private static Room Room(int id, string code, string name, string cropQcRoomName, string compuTechCode, string subLocation, Warehouse warehouse) => new()
    {
        Id = id,
        WarehouseId = warehouse.Id,
        Warehouse = warehouse,
        Code = code,
        Name = name,
        CropQcRoomName = cropQcRoomName,
        CompuTechRoomCode = compuTechCode,
        DisplayName = cropQcRoomName,
        SubLocation = subLocation
    };

    private static RoomInventoryAdjustment CurrentCorrection(long id, Warehouse warehouse, Room room, FruitProfile fruitProfile, string lot, int bins, DateTimeOffset at, string source, string? status = null) => new()
    {
        Id = id,
        CropYear = 2026,
        WarehouseId = warehouse.Id,
        Warehouse = warehouse,
        RoomId = room.Id,
        Room = room,
        FruitProfileId = fruitProfile.Id,
        FruitProfile = fruitProfile,
        GrowerName = "Wes Verified Current Inventory",
        LotNumber = lot,
        VarietyCode = fruitProfile.VarietyCode,
        OldBinCount = null,
        ChangeAmount = bins,
        NewBinCount = bins,
        AdjustmentType = RoomInventoryImportService.StartingInventoryAdjustmentType,
        InventoryStatus = status,
        Source = source,
        Reason = source,
        AdjustmentAt = at,
        CreatedByUserId = 9001,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static Receipt Receipt(long id, string compuTechId, string receiptType, int bins, DateTimeOffset receivedAt, Warehouse warehouse, Room room, FruitProfile fruitProfile) => new()
    {
        Id = id,
        CropYear = 2026,
        ReceivedAt = receivedAt,
        CompuTechReceiptId = compuTechId,
        ReceiptType = receiptType,
        WarehouseId = warehouse.Id,
        Warehouse = warehouse,
        RoomId = room.Id,
        Room = room,
        FruitProfileId = fruitProfile.Id,
        FruitProfile = fruitProfile,
        GrowerName = "Fuji Grower",
        GrowerNumber = "EVANCA12",
        LotCode = "EVANCA12",
        BinCount = bins,
        CreatedAt = receivedAt,
        UpdatedAt = receivedAt
    };

    private static QcSample Sample(long id, long receiptId, SampleType sampleType) =>
        Sample(id, receiptId, sampleType, DateTimeOffset.Parse("2026-06-14T10:00:00-07:00"));

    private static QcSample Sample(long id, long receiptId, SampleType sampleType, DateTimeOffset sampleTakenAt) => new()
    {
        Id = id,
        ReceiptId = receiptId,
        SampleTypeId = sampleType.Id,
        SampleType = sampleType,
        Status = "Complete",
        StarchStatus = "Not Required",
        PhotoStatus = "Photos Complete",
        EmailStatus = "Not Sent",
        ActualSampleSize = 10,
        SampleTakenAt = sampleTakenAt,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static QcFruitReading FruitReading(long id, long sampleId, decimal pressure) => new()
    {
        Id = id,
        QcSampleId = sampleId,
        RowNumber = 1,
        Pressure1Lbs = pressure,
        Pressure2Lbs = pressure,
        SizeStatus = "Not Entered",
        IsCompleted = true,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static DashboardDataService CreateService(CropQcDbContext db)
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.Email, "wes@fruitandland.com"),
                    new Claim(ClaimTypes.Role, "Admin")
                ],
                "TestAuth"))
        };
        var configuration = new ConfigurationBuilder().Build();
        return new DashboardDataService(
            db,
            new FakeFileStorageService(),
            new FileStorageOptions(),
            new EmailOptions { Provider = EmailProviders.GmailUser, QcDefaultRecipients = "qc-recipient@fruitandland.com" },
            new FakeRecipientResolver(),
            new GoogleAuthenticationOptions { AllowedDomains = new HashSet<string>(["fruitandland.com"], StringComparer.OrdinalIgnoreCase) },
            new FakeCredentialStore(),
            new FakeEmailSender(),
            new QcPhotoRequirementPolicy(),
            new StableEmailComposer(),
            new CropYearService(db, configuration),
            new HttpContextAccessor { HttpContext = httpContext },
            configuration,
            NullLogger<DashboardDataService>.Instance);
    }

    private static RoomInventoryImportService CreateImportService(CropQcDbContext db)
    {
        var configuration = new ConfigurationBuilder().Build();
        return new RoomInventoryImportService(
            db,
            new FakeWebHostEnvironment(),
            new CropYearService(db, configuration));
    }

    private sealed class StableEmailComposer : IQcSummaryEmailComposer
    {
        public Task<QcEmailContent> ComposeAsync(QcSample sample, ReadinessViewModel readiness, User? sendingUser, bool isOverride, string? overrideReason, CancellationToken cancellationToken) =>
            Task.FromResult(new QcEmailContent("QC Summary", "<p>Html</p>", "Text", []));
    }

    private sealed class FakeEmailSender : IQcEmailSender
    {
        public Task<QcEmailSendResult> SendAsync(User sender, QcEmailMessage message, CancellationToken cancellationToken) =>
            Task.FromResult(new QcEmailSendResult(true, "message-id", null));
    }

    private sealed class FakeRecipientResolver : IQcEmailRecipientResolver
    {
        public Task<QcEmailRecipientResolution> ResolveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new QcEmailRecipientResolution(["qc-recipient@fruitandland.com"], QcEmailRecipientSources.FallbackConfiguration));
    }

    private sealed class FakeCredentialStore : IGoogleCredentialStore
    {
        public Task SaveFromAuthenticationPropertiesAsync(User user, AuthenticationProperties properties, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<GoogleAccessTokenResult> GetAccessTokenAsync(User user, CancellationToken cancellationToken) => Task.FromResult(GoogleAccessTokenResult.Success("token"));
        public Task<GoogleCredentialDiagnostic> GetDiagnosticAsync(User user, CancellationToken cancellationToken) => Task.FromResult(new GoogleCredentialDiagnostic(true, true));
    }

    private sealed class FakeFileStorageService : IFileStorageService
    {
        public string GenerateTargetPath(FileStorageTargetContext context) => "test/path";
        public Task<FileStorageReference> SaveAsync(FileStorageSaveRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new FileStorageReference(FileStorageProviders.Local, "test-key", request.TargetPath, request.FileName, request.ContentType, request.FileSizeBytes ?? 0));
        public Task<FileStorageReference?> GetMetadataAsync(string storageKey, CancellationToken cancellationToken = default) => Task.FromResult<FileStorageReference?>(null);
        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) => Task.FromResult<Stream?>(null);
        public Task DeleteOrVoidAsync(string storageKey, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class FakeWebHostEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "CropQc.Tests";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = "";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static string FindRepositoryFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file {Path.Combine(pathParts)}.");
    }
}
