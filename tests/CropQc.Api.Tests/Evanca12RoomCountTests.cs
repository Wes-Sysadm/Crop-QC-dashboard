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
using Microsoft.Extensions.Logging;
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
        Assert.Equal("Fuji: 1022 bins", detail.Summary.VarietyStatusSummary);
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
        var futureReceipt = Receipt(130, "EVANCA12-FUTURE-TRUCK", "Truck receipt", 10, receivedAt, warehouse, room, fuji);
        db.Receipts.Add(futureReceipt);
        db.RoomInventoryAdjustments.Add(ReceiptInventoryAdjustment(430, futureReceipt));
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
        Assert.Equal("Fuji: 1022 bins", current.Summary?.VarietyStatusSummary);
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
        Assert.Contains(page.Lots, x => x.Lot == "1570" && x.Variety == "Fuji" && x.CurrentBins == 819);
        Assert.DoesNotContain(page.Lots, x => x.CurrentBins is 700 or 800);
    }

    [Fact]
    public async Task LotAndDoorSamplesUpdateCurrentConditionWithoutAddingBins()
    {
        await using var db = CreateDbContext();
        await SeedVerifiedEbsInventoryAsync(db);
        var warehouse = await db.Warehouses.FirstAsync(x => x.Code == "EBS");
        var room = await db.Rooms.FirstAsync(x => x.Code == "EVANCA12");
        var fuji = await db.FruitProfiles.SingleAsync(x => x.Id == 902);
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
        Assert.Contains(detail.CurrentLots, x => x.LotCode == "1020" && x.CurrentBins == 559);
        Assert.Single(detail.CurrentLots, x => x.LotCode == "1020");
        Assert.Contains(page.Lots, x => x.Lot == "1020" && x.CurrentBins == 559 && x.Grower == "");
        Assert.Single(page.Lots, x => x.Lot == "1020");
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

    [Fact]
    public async Task DashboardRoomsAndCurrentInventory_UseCanonicalLedgerIdentityWithoutWriting()
    {
        await using var db = CreateDbContext();
        await SeedCanonicalInventorySourceRegressionAsync(db);
        var dashboardService = CreateService(db);
        var importService = CreateImportService(db);
        var adjustmentCountBefore = await db.RoomInventoryAdjustments.CountAsync();
        var adjustmentBinsBefore = await db.RoomInventoryAdjustments.SumAsync(x => x.ChangeAmount);

        var dashboard = await dashboardService.GetHomeDashboardAsync(new RoomSummaryFilterForm { Facility = "All" }, CancellationToken.None);
        var rooms = await dashboardService.GetRoomsAsync(new RoomSummaryFilterForm { Facility = "All" }, CancellationToken.None);
        var currentInventory = await importService.GetPageAsync(new RoomInventoryImportForm { Facility = "All" }, CancellationToken.None);

        Assert.Null(dashboard.DataWarning);
        Assert.Null(rooms.DataWarning);
        Assert.DoesNotContain(dashboard.RoomSummaries, x => x.RoomCode == "EBS-TEST");
        Assert.DoesNotContain(dashboard.RoomSummaries, x => x.RoomCode == "Evans-12");
        Assert.Equal(1, dashboard.RoomSummaries.Single(x => x.RoomCode == "DH-1").CurrentBinsCount);
        Assert.Equal(0, rooms.Rooms.Where(x => x.RoomCode == "EBS-TEST").Sum(x => x.CurrentBinsCount ?? 0));
        Assert.Equal(0, rooms.Rooms.Where(x => x.RoomCode == "Evans-12").Sum(x => x.CurrentBinsCount ?? 0));
        Assert.Equal(1, rooms.Rooms.Single(x => x.RoomCode == "DH-1").CurrentBinsCount);
        Assert.DoesNotContain(currentInventory.CurrentLots, x => x.RoomCode == "EBS-TEST");
        Assert.DoesNotContain(currentInventory.CurrentLots, x => x.RoomCode == "Evans-12" || x.Variety == "ATGL");

        var dashboardTotals = dashboard.StorageByFacility.ToDictionary(x => x.Facility, x => x.CurrentBins);
        var roomTotals = rooms.Rooms.GroupBy(x => x.Facility).ToDictionary(x => x.Key, x => x.Sum(y => y.CurrentBinsCount ?? 0));
        var currentInventoryTotals = currentInventory.CurrentLots.GroupBy(x => x.Facility).ToDictionary(x => x.Key, x => x.Sum(y => y.CurrentBins));
        Assert.Equal(new Dictionary<string, int> { ["DH"] = 1, ["EBS"] = 388, ["WP"] = 565 }, dashboardTotals);
        Assert.Equal(dashboardTotals, roomTotals);
        Assert.Equal(dashboardTotals, currentInventoryTotals);
        Assert.Equal(388, dashboard.RoomSummaries.Single(x => x.RoomCode == "Evans Street 7").CurrentBinsCount);

        Assert.Equal(adjustmentCountBefore, await db.RoomInventoryAdjustments.CountAsync());
        Assert.Equal(adjustmentBinsBefore, await db.RoomInventoryAdjustments.SumAsync(x => x.ChangeAmount));

        var evansSeven = await db.Rooms.SingleAsync(x => x.Code == "EVANS7");
        var gala = await db.FruitProfiles.SingleAsync(x => x.Id == 9902);
        db.RoomInventoryAdjustments.Add(InventoryAdjustment(9906, evansSeven.Warehouse, evansSeven, gala, "EVANS-GALA", 12, 2026, "ReceiptAdd"));
        await db.SaveChangesAsync();

        var refreshed = await dashboardService.GetHomeDashboardAsync(new RoomSummaryFilterForm { Facility = "EBS" }, CancellationToken.None);
        Assert.Equal(400, refreshed.StorageByFacility.Single().CurrentBins);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncidentBdb7aeaf_ZeroLegacyAndCanonicalGrowerLotReconcileToOneExactIdentity(bool reverseReceiptOrder)
    {
        await using var db = CreateEmptyDbContext();
        var warehouse = new Warehouse { Id = 1, Code = "WP", Name = "WP Packing" };
        var room = new Room { Id = 1, Warehouse = warehouse, Code = "WP-1", Name = "WP-1", CropQcRoomName = "WP-1", CapacityBins = 500 };
        var profile = new FruitProfile { Id = 17, Name = "Bartlett", VarietyCode = "BART", FruitType = "Pear", ProductionType = "Conventional" };
        var growerLot = new GrowerLot
        {
            Id = 398,
            Grower = "Grower 1084",
            LotNumber = "1084",
            CreatedAt = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-07-01T00:00:00Z")
        };
        var legacyReceipt = IncidentReceipt(reverseReceiptOrder ? 93 : 92, 64, warehouse, room, profile, null);
        var canonicalReceipt = IncidentReceipt(reverseReceiptOrder ? 92 : 93, 145, warehouse, room, profile, growerLot);
        var sampleType = new SampleType { Id = 71, Name = "Truck Sample", IsActive = true };
        var sample = Sample(7101, canonicalReceipt.Id, sampleType, DateTimeOffset.Parse("2026-08-01T18:00:00Z"));
        var entities = reverseReceiptOrder
            ? new object[] { warehouse, room, profile, growerLot, sampleType, canonicalReceipt, legacyReceipt, sample, FruitReading(71001, sample.Id, 15m) }
            : new object[] { warehouse, room, profile, growerLot, sampleType, legacyReceipt, canonicalReceipt, sample, FruitReading(71001, sample.Id, 15m) };
        db.AddRange(entities);
        await db.SaveChangesAsync();
        var adjustmentsBefore = await db.RoomInventoryAdjustments.CountAsync();
        var logger = new ListLogger<DashboardDataService>();
        var service = CreateService(db, new FixedLedgerQuery([
            IncidentSnapshot(null, 0, 89),
            IncidentSnapshot(398, 145, 138)
        ]), logger);

        var home = await service.GetHomeDashboardAsync(new RoomSummaryFilterForm { Facility = "All" }, CancellationToken.None);
        var rooms = await service.GetRoomsAsync(new RoomSummaryFilterForm { Facility = "All" }, CancellationToken.None);
        var detail = await service.GetRoomDetailAsync(room.Id, CancellationToken.None);
        var lots = await service.GetCurrentGrowerLotsAsync(new CurrentGrowerLotsFilterForm { Facility = "All", CropYear = 2026 }, CancellationToken.None);
        var wpLots = await service.GetCurrentGrowerLotsAsync(new CurrentGrowerLotsFilterForm { Facility = "WP", CropYear = 2026 }, CancellationToken.None);
        var ebsLots = await service.GetCurrentGrowerLotsAsync(new CurrentGrowerLotsFilterForm { Facility = "EBS", CropYear = 2026 }, CancellationToken.None);
        var growerLots = await service.GetCurrentGrowerLotsAsync(new CurrentGrowerLotsFilterForm { Facility = "All", CropYear = 2026, Grower = "1084" }, CancellationToken.None);
        var searchLots = await service.GetCurrentGrowerLotsAsync(new CurrentGrowerLotsFilterForm { Facility = "All", CropYear = 2026, Search = "1084" }, CancellationToken.None);
        var canonicalVariety = await service.GetCurrentGrowerLotsAsync(new CurrentGrowerLotsFilterForm { Facility = "All", CropYear = 2026, Variety = "Bartlett" }, CancellationToken.None);
        var rawVarietyCode = await service.GetCurrentGrowerLotsAsync(new CurrentGrowerLotsFilterForm { Facility = "All", CropYear = 2026, Variety = "BART" }, CancellationToken.None);
        var unrelated = await service.GetCurrentGrowerLotsAsync(new CurrentGrowerLotsFilterForm { Facility = "All", CropYear = 2026, Variety = "Gala" }, CancellationToken.None);
        var unrelatedGrower = await service.GetCurrentGrowerLotsAsync(new CurrentGrowerLotsFilterForm { Facility = "All", CropYear = 2026, Grower = "9999" }, CancellationToken.None);
        var unrelatedSearch = await service.GetCurrentGrowerLotsAsync(new CurrentGrowerLotsFilterForm { Facility = "All", CropYear = 2026, Search = "NO-SUCH-LOT" }, CancellationToken.None);
        var unrelatedYear = await service.GetCurrentGrowerLotsAsync(new CurrentGrowerLotsFilterForm { Facility = "All", CropYear = 2025 }, CancellationToken.None);
        var unrelatedWarehouse = await service.GetCurrentGrowerLotsAsync(new CurrentGrowerLotsFilterForm { Facility = "All", CropYear = 2026, WarehouseId = 9999 }, CancellationToken.None);

        Assert.Null(home.DataWarning);
        Assert.Null(rooms.DataWarning);
        Assert.Null(detail.DataWarning);
        Assert.Null(lots.DataWarning);
        Assert.Equal(145, home.StorageByFacility.Single().CurrentBins);
        Assert.Equal(1, home.StorageByFacility.Single().CurrentGrowerLots);
        Assert.Equal(145, rooms.Rooms.Single().CurrentBinsCount);
        Assert.Equal(1, rooms.Rooms.Single().CurrentLotsCount);
        var current = Assert.Single(detail.CurrentLots);
        Assert.Equal(398, current.GrowerLotId);
        Assert.Equal(145, current.CurrentBins);
        Assert.Equal(2, current.ReceiptEvidenceCount);
        Assert.Contains(current.ReceiptEvidence, x => x.ReceiptId == legacyReceipt.Id);
        Assert.Contains(current.ReceiptEvidence, x => x.ReceiptId == canonicalReceipt.Id);
        var grower = Assert.Single(detail.CurrentGrowers);
        Assert.Equal(145, grower.CurrentBins);
        Assert.Equal(1, grower.CurrentLotCount);
        Assert.Equal(145, grower.PressureRepresentedBins);
        Assert.Equal(15m, grower.WeightedPressureLbs);
        var displayedLot = Assert.Single(lots.Lots);
        Assert.Equal(398, displayedLot.GrowerLotId);
        Assert.Equal(145, displayedLot.CurrentBins);
        var storedGrower = Assert.Single(lots.Growers);
        Assert.Equal(1, storedGrower.CurrentLotCount);
        Assert.Equal(145, storedGrower.CurrentBins);
        Assert.Equal(145, storedGrower.PressureRepresentedBins);
        Assert.Equal(15m, storedGrower.WeightedPressureLbs);
        Assert.Equal(145, wpLots.TotalCurrentBins);
        Assert.Equal(0, ebsLots.TotalCurrentBins);
        Assert.Equal(wpLots.TotalCurrentBins + ebsLots.TotalCurrentBins, lots.TotalCurrentBins);
        Assert.Equal(145, growerLots.TotalCurrentBins);
        Assert.Equal(145, searchLots.TotalCurrentBins);
        Assert.Equal(145, canonicalVariety.TotalCurrentBins);
        Assert.Equal(canonicalVariety.TotalCurrentBins, rawVarietyCode.TotalCurrentBins);
        Assert.Equal(0, unrelated.TotalCurrentBins);
        Assert.Equal(0, unrelatedGrower.TotalCurrentBins);
        Assert.Equal(0, unrelatedSearch.TotalCurrentBins);
        Assert.Equal(0, unrelatedYear.TotalCurrentBins);
        Assert.Equal(0, unrelatedWarehouse.TotalCurrentBins);
        Assert.Equal(lots.TotalCurrentBins, lots.Lots.Sum(x => x.CurrentBins));
        Assert.Equal(lots.TotalCurrentBins, lots.Growers.Sum(x => x.CurrentBins));
        Assert.Equal(lots.TotalCurrentBins, lots.Growers.SelectMany(x => x.Varieties).Sum(x => x.BinCount));
        Assert.Contains(logger.Messages, x => x.Contains("1|2026|1084|BART|17", StringComparison.Ordinal));
        Assert.Equal(adjustmentsBefore, await db.RoomInventoryAdjustments.CountAsync());
        Assert.Equal(2, await db.Receipts.CountAsync());
    }

    [Fact]
    public async Task CurrentStorageVarietyFilter_ResolvesCanonicalAliasesAcrossReceiptsAndAdjustmentOnlyRows()
    {
        await using var db = CreateEmptyDbContext();
        var warehouse = new Warehouse { Id = 31, Code = "WP", Name = "WP Packing" };
        var room = new Room { Id = 32, Warehouse = warehouse, Code = "WP-ALIAS", Name = "WP Alias", CropQcRoomName = "WP Alias", CapacityBins = 500 };
        var gsmt = new FruitProfile { Id = 41, Name = "GSMT", VarietyCode = "GSMT", FruitType = "Apple", ProductionType = "Conventional", IsOrganic = false };
        var grannysmith = new FruitProfile { Id = 42, Name = "Grannysmith", VarietyCode = "GRANNYSMITH", FruitType = "Apple", ProductionType = "Conventional", IsOrganic = false };
        var canonicalOrganic = new FruitProfile { Id = 43, Name = "Organic Granny Smith", VarietyCode = "GRANNY SMITH", FruitType = "Apple", ProductionType = "Organic", IsOrganic = true };
        var gala = new FruitProfile { Id = 44, Name = "Gala", VarietyCode = "GALA", FruitType = "Apple", ProductionType = "Conventional", IsOrganic = false };
        var receipts = new[]
        {
            AliasReceipt(4101, "ALIAS-GSMT", "1084", "A-1", 10, warehouse, room, gsmt),
            AliasReceipt(4102, "ALIAS-GRANNYSMITH", "1084", "A-2", 20, warehouse, room, grannysmith),
            AliasReceipt(4103, "ALIAS-CANONICAL", "1084", "A-3", 30, warehouse, room, canonicalOrganic),
            AliasReceipt(4104, "UNRELATED-GALA", "1084", "G-1", 50, warehouse, room, gala)
        };
        var adjustmentOnly = new RoomInventoryAdjustment
        {
            Id = 4199,
            CropYear = 2026,
            Warehouse = warehouse,
            Room = room,
            GrowerName = "Grower 1084",
            LotNumber = "A-4",
            VarietyCode = "Grannysmith",
            ChangeAmount = 40,
            NewBinCount = 40,
            AdjustmentType = RoomInventoryImportService.StartingInventoryAdjustmentType,
            Source = "Alias adjustment fixture",
            AdjustmentAt = DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
            CreatedAt = DateTimeOffset.Parse("2026-08-01T00:00:00Z")
        };
        db.AddRange(warehouse, room, gsmt, grannysmith, canonicalOrganic, gala);
        db.Receipts.AddRange(receipts);
        db.RoomInventoryAdjustments.Add(adjustmentOnly);
        await db.SaveChangesAsync();
        var adjustmentCount = await db.RoomInventoryAdjustments.CountAsync();
        var service = CreateService(db, new FixedLedgerQuery([
            StorageSnapshot(warehouse.Id, room.Id, gsmt.Id, "A-1", "GSMT", "GSMT", "Conventional", false, 10),
            StorageSnapshot(warehouse.Id, room.Id, grannysmith.Id, "A-2", "GRANNYSMITH", "Grannysmith", "Conventional", false, 20),
            StorageSnapshot(warehouse.Id, room.Id, canonicalOrganic.Id, "A-3", "GRANNY SMITH", "Organic Granny Smith", "Organic", true, 30),
            StorageSnapshot(warehouse.Id, room.Id, null, "A-4", "Grannysmith", "Grannysmith", "", null, 40),
            StorageSnapshot(warehouse.Id, room.Id, gala.Id, "G-1", "GALA", "Gala", "Conventional", false, 50)
        ]));

        var canonical = await service.GetCurrentGrowerLotsAsync(new CurrentGrowerLotsFilterForm { Facility = "All", CropYear = 2026, Variety = "Granny Smith" }, CancellationToken.None);
        var rawGsmt = await service.GetCurrentGrowerLotsAsync(new CurrentGrowerLotsFilterForm { Facility = "All", CropYear = 2026, Variety = "GSMT" }, CancellationToken.None);
        var rawGrannysmith = await service.GetCurrentGrowerLotsAsync(new CurrentGrowerLotsFilterForm { Facility = "All", CropYear = 2026, Variety = "grAnNySmItH" }, CancellationToken.None);
        var unrelated = await service.GetCurrentGrowerLotsAsync(new CurrentGrowerLotsFilterForm { Facility = "All", CropYear = 2026, Variety = "Gala" }, CancellationToken.None);

        Assert.Null(canonical.DataWarning);
        Assert.Equal(100, canonical.TotalCurrentBins);
        Assert.Equal(canonical.TotalCurrentBins, rawGsmt.TotalCurrentBins);
        Assert.Equal(canonical.TotalCurrentBins, rawGrannysmith.TotalCurrentBins);
        Assert.Equal(4, canonical.Lots.Count);
        Assert.Contains(canonical.Lots, x => x.Lot == "A-4" && x.CurrentBins == 40);
        Assert.DoesNotContain(canonical.Lots, x => x.Lot == "G-1");
        Assert.Equal(50, unrelated.TotalCurrentBins);
        Assert.All(canonical.Lots, x => Assert.Equal("Granny Smith", x.Variety));
        var identities = canonical.Growers.SelectMany(x => x.Varieties).ToList();
        Assert.Contains(identities, x => x.IsOrganic == true && x.ProductionType == "Organic");
        Assert.Contains(identities, x => x.IsOrganic == false && x.ProductionType == "Conventional");
        Assert.Equal(adjustmentCount, await db.RoomInventoryAdjustments.CountAsync());
        Assert.Equal(4, await db.Receipts.CountAsync());
    }

    [Fact]
    public async Task PostgreSql_CurrentStorageCanonicalAliasFiltering_UsesAuthoritativeLedger_WhenConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROPQC_TEST_CURRENT_STORAGE_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connectionString);
        var options = new DbContextOptionsBuilder<CropQcDbContext>();
        CropQcDatabase.Configure(options, DatabaseProviders.PostgreSql, connectionString);
        await using var db = new CropQcDbContext(options.Options);
        Assert.True(await db.Database.EnsureCreatedAsync(), "The configured current-storage PostgreSQL database must start empty.");
        var warehouse = await db.Warehouses.SingleAsync(x => x.Code == "WP");
        var room = new Room { Id = 31002, Warehouse = warehouse, Code = "WP-ALIAS", Name = "WP Alias", CropQcRoomName = "WP Alias", CapacityBins = 500 };
        var gsmt = new FruitProfile { Id = 31011, Name = "GSMT", VarietyCode = "PR174_GSMT", FruitType = "Apple", ProductionType = "Conventional", IsOrganic = false };
        var grannysmith = new FruitProfile { Id = 31012, Name = "Grannysmith", VarietyCode = "PR174_GRANNYSMITH", FruitType = "Apple", ProductionType = "Conventional", IsOrganic = false };
        var organic = new FruitProfile { Id = 31013, Name = "Organic Granny Smith", VarietyCode = "PR174_GRANNY_SMITH", FruitType = "Apple", ProductionType = "Organic", IsOrganic = true };
        var gala = new FruitProfile { Id = 31014, Name = "Gala", VarietyCode = "PR174_GALA", FruitType = "Apple", ProductionType = "Conventional", IsOrganic = false };
        var receipts = new[]
        {
            AliasReceipt(31101, "PG-GSMT", "1084", "A-1", 10, warehouse, room, gsmt),
            AliasReceipt(31102, "PG-GRANNYSMITH", "1084", "A-2", 20, warehouse, room, grannysmith),
            AliasReceipt(31103, "PG-CANONICAL", "1084", "A-3", 30, warehouse, room, organic),
            AliasReceipt(31104, "PG-GALA", "1084", "G-1", 50, warehouse, room, gala)
        };
        db.AddRange(room, gsmt, grannysmith, organic, gala);
        db.Receipts.AddRange(receipts);
        db.RoomInventoryAdjustments.AddRange(
            RealReceiptAdjustment(31201, receipts[0]),
            RealReceiptAdjustment(31202, receipts[1]),
            RealReceiptAdjustment(31203, receipts[2]),
            RealReceiptAdjustment(31204, receipts[3]),
            new RoomInventoryAdjustment
            {
                Id = 31205,
                CropYear = 2026,
                Warehouse = warehouse,
                Room = room,
                GrowerName = "Grower 1084",
                LotNumber = "A-4",
                VarietyCode = "Grannysmith",
                ChangeAmount = 40,
                NewBinCount = 40,
                AdjustmentType = RoomInventoryImportService.StartingInventoryAdjustmentType,
                Source = "PostgreSQL alias fixture",
                AdjustmentAt = DateTimeOffset.Parse("2026-08-01T00:00:00Z"),
                CreatedAt = DateTimeOffset.Parse("2026-08-01T00:00:00Z")
            });
        await db.SaveChangesAsync();
        var adjustmentsBefore = await db.RoomInventoryAdjustments.CountAsync();
        var service = CreateService(db);

        var canonical = await service.GetCurrentGrowerLotsAsync(new CurrentGrowerLotsFilterForm { Facility = "All", CropYear = 2026, Variety = "Granny Smith" }, CancellationToken.None);
        var raw = await service.GetCurrentGrowerLotsAsync(new CurrentGrowerLotsFilterForm { Facility = "All", CropYear = 2026, Variety = "GSMT" }, CancellationToken.None);
        var unrelated = await service.GetCurrentGrowerLotsAsync(new CurrentGrowerLotsFilterForm { Facility = "All", CropYear = 2026, Variety = "Gala" }, CancellationToken.None);

        Assert.Null(canonical.DataWarning);
        Assert.Equal(100, canonical.TotalCurrentBins);
        Assert.Equal(canonical.TotalCurrentBins, raw.TotalCurrentBins);
        Assert.Equal(50, unrelated.TotalCurrentBins);
        Assert.Contains(canonical.Lots, x => x.Lot == "A-4" && x.CurrentBins == 40);
        Assert.All(canonical.Lots, x => Assert.Equal("Granny Smith", x.Variety));
        Assert.Equal(adjustmentsBefore, await db.RoomInventoryAdjustments.CountAsync());
    }

    [Fact]
    public async Task ConflictingNonNullGrowerLotSnapshotsFailClosedWithBoundedDiagnostic()
    {
        await using var db = CreateEmptyDbContext();
        var warehouse = new Warehouse { Id = 1, Code = "WP", Name = "WP Packing" };
        var room = new Room { Id = 1, Warehouse = warehouse, Code = "WP-1", Name = "WP-1", CropQcRoomName = "WP-1", CapacityBins = 500 };
        var profile = new FruitProfile { Id = 17, Name = "Bartlett", VarietyCode = "BART", FruitType = "Pear", ProductionType = "Conventional" };
        db.AddRange(warehouse, room, profile, IncidentReceipt(92, 100, warehouse, room, profile, null));
        await db.SaveChangesAsync();
        var logger = new ListLogger<DashboardDataService>();
        var service = CreateService(db, new FixedLedgerQuery([
            IncidentSnapshot(398, 60, 138),
            IncidentSnapshot(399, 40, 139)
        ]), logger);

        var rooms = await service.GetRoomsAsync(new RoomSummaryFilterForm { Facility = "All" }, CancellationToken.None);

        Assert.NotNull(rooms.DataWarning);
        Assert.Empty(rooms.Rooms);
        Assert.Contains(logger.Messages, x => x.Contains("growerLotIds=398,399", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, x => x.Contains("quantities were not selected or discarded", StringComparison.Ordinal));
        Assert.Empty(db.RoomInventoryAdjustments);
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

    private static CropQcDbContext CreateEmptyDbContext()
    {
        var options = new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new CropQcDbContext(options);
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

    private static async Task SeedCanonicalInventorySourceRegressionAsync(CropQcDbContext db)
    {
        var ebs = new Warehouse { Id = 9901, Code = "EBS", Name = "Earl Brown Storage" };
        var wp = new Warehouse { Id = 9902, Code = "WP", Name = "Windy Point" };
        var dh = new Warehouse { Id = 9903, Code = "DH", Name = "DH" };
        var ebsTest = new Room { Id = 9901, Warehouse = ebs, Code = "EBS-TEST", Name = "EBS Test", CropQcRoomName = "EBS-TEST", IsActive = true };
        var evansSeven = new Room { Id = 9902, Warehouse = ebs, Code = "EVANS7", Name = "Evans Street 7", CropQcRoomName = "Evans Street 7", IsActive = true };
        var wpRoom = new Room { Id = 9903, Warehouse = wp, Code = "WP-4", Name = "WP-4", CropQcRoomName = "WP-4", IsActive = true };
        var dhRoom = new Room { Id = 9904, Warehouse = dh, Code = "DH-1", Name = "DH-1", CropQcRoomName = "DH-1", IsActive = true };
        var evansTwelve = new Room { Id = 9905, Warehouse = ebs, Code = "EVANS-12", Name = "Evans Street 12", CropQcRoomName = "Evans-12", IsActive = true };
        var red = new FruitProfile { Id = 9901, Name = "Red Delicious", VarietyCode = "RED", FruitType = "Apple", ProductionType = "Conventional" };
        var gala = new FruitProfile { Id = 9902, Name = "Gala", VarietyCode = "GALA", FruitType = "Apple", ProductionType = "Conventional" };
        var bartlett = new FruitProfile { Id = 9903, Name = "Bartlett", VarietyCode = "BART", FruitType = "Pear", ProductionType = "Conventional" };
        var fuji = new FruitProfile { Id = 9904, Name = "Fuji", VarietyCode = "FUJI", FruitType = "Apple", ProductionType = "Conventional" };
        var autumnGlory = new FruitProfile { Id = 9905, Name = "Autumn Glory", VarietyCode = "ATGL", FruitType = "Apple", ProductionType = "Conventional" };
        db.Warehouses.AddRange(ebs, wp, dh);
        db.Rooms.AddRange(ebsTest, evansSeven, wpRoom, dhRoom, evansTwelve);
        db.FruitProfiles.AddRange(red, gala, bartlett, fuji, autumnGlory);

        var source = InventoryAdjustment(9901, ebs, ebsTest, red, "TEST-LOT", 100, 2025, RoomInventoryImportService.StartingInventoryAdjustmentType);
        var depletion = InventoryAdjustment(9902, ebs, ebsTest, red, "TEST-LOT", -100, null, BinsRunService.AdjustmentType);
        var fujiSource = InventoryAdjustment(9910, ebs, evansTwelve, fuji, "1570", 120, 2025, RoomInventoryImportService.StartingInventoryAdjustmentType);
        var fujiFirst = InventoryAdjustment(9911, ebs, evansTwelve, fuji, "1570", -50, null, BinsRunService.AdjustmentType);
        var fujiSecond = InventoryAdjustment(9912, ebs, evansTwelve, fuji, "1570", -70, null, BinsRunService.AdjustmentType);
        db.RoomInventoryAdjustments.AddRange(
            source,
            depletion,
            InventoryAdjustment(9903, ebs, evansSeven, gala, "EVANS-GALA", 388, 2026, RoomInventoryImportService.StartingInventoryAdjustmentType),
            InventoryAdjustment(9904, wp, wpRoom, bartlett, "WP-BART", 565, 2026, "ReceiptAdd"),
            InventoryAdjustment(9905, dh, dhRoom, gala, "DH-GALA", 1, 2026, "ReceiptAdd"),
            fujiSource,
            fujiFirst,
            fujiSecond,
            InventoryAdjustment(9913, dh, dhRoom, autumnGlory, "1030", 0, 2025, "ReceiptAdd"));
        db.BinsRunEntries.Add(new BinsRunEntry
        {
            Id = 9901,
            SourceInventoryAdjustment = source,
            InventoryAdjustment = depletion,
            Warehouse = ebs,
            Room = ebsTest,
            CropYear = null,
            FruitProfile = red,
            GrowerName = "Test",
            LotNumber = "TEST-LOT",
            VarietyCode = "RED",
            PreviousAvailableBins = 100,
            BinsRun = 100,
            NewAvailableBins = 0,
            RunAt = DateTimeOffset.Parse("2026-07-01T00:00:00Z"),
            CreatedAt = DateTimeOffset.Parse("2026-07-01T00:00:00Z")
        });
        db.BinsRunEntries.AddRange(
            LegacyBinsRunEntry(9910, ebs, evansTwelve, fuji, fujiSource, fujiFirst, "1570", 120, 50, 70),
            LegacyBinsRunEntry(9911, ebs, evansTwelve, fuji, fujiFirst, fujiSecond, "1570", 70, 70, 0));
        await db.SaveChangesAsync();
    }

    private static BinsRunEntry LegacyBinsRunEntry(
        long id,
        Warehouse warehouse,
        Room room,
        FruitProfile profile,
        RoomInventoryAdjustment source,
        RoomInventoryAdjustment adjustment,
        string lot,
        int previous,
        int bins,
        int next) => new()
        {
            Id = id,
            SourceInventoryAdjustment = source,
            InventoryAdjustment = adjustment,
            Warehouse = warehouse,
            Room = room,
            FruitProfile = profile,
            GrowerName = "Test",
            LotNumber = lot,
            VarietyCode = profile.VarietyCode,
            PreviousAvailableBins = previous,
            BinsRun = bins,
            NewAvailableBins = next,
            RunAt = adjustment.AdjustmentAt,
            CreatedAt = adjustment.CreatedAt,
            TransactionType = ActualRunTransactionTypes.Legacy
        };

    private static RoomInventoryAdjustment InventoryAdjustment(
        long id,
        Warehouse warehouse,
        Room room,
        FruitProfile profile,
        string lot,
        int change,
        int? cropYear,
        string adjustmentType) => new()
        {
            Id = id,
            CropYear = cropYear,
            Warehouse = warehouse,
            Room = room,
            FruitProfile = profile,
            GrowerName = "Test",
            LotNumber = lot,
            VarietyCode = profile.VarietyCode,
            ChangeAmount = change,
            NewBinCount = Math.Max(0, change),
            AdjustmentType = adjustmentType,
            Source = "Regression fixture",
            AdjustmentAt = DateTimeOffset.Parse("2026-06-01T00:00:00Z").AddMinutes(id - 9900),
            CreatedAt = DateTimeOffset.Parse("2026-06-01T00:00:00Z").AddMinutes(id - 9900)
        };

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
        var truckReceipt = await db.Receipts.SingleAsync(x => x.Id == 120);
        db.RoomInventoryAdjustments.Add(ReceiptInventoryAdjustment(420, truckReceipt));
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

    private static RoomInventoryAdjustment ReceiptInventoryAdjustment(long id, Receipt receipt) => new()
    {
        Id = id,
        CropYear = receipt.CropYear,
        Receipt = receipt,
        Warehouse = receipt.Warehouse,
        Room = receipt.Room,
        FruitProfile = receipt.FruitProfile,
        GrowerName = receipt.GrowerName,
        LotNumber = receipt.GrowerNumber ?? receipt.LotCode,
        VarietyCode = receipt.FruitProfile.VarietyCode,
        OldBinCount = 0,
        ChangeAmount = receipt.BinCount,
        NewBinCount = receipt.BinCount,
        AdjustmentType = "ReceiptAdd",
        Source = "Receiving inventory added",
        AdjustmentAt = receipt.ReceivedAt,
        CreatedAt = receipt.ReceivedAt
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

    private static DashboardDataService CreateService(
        CropQcDbContext db,
        IRoomInventoryLedgerQueryService? ledgerQuery = null,
        ILogger<DashboardDataService>? logger = null)
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
            logger ?? NullLogger<DashboardDataService>.Instance,
            roomInventoryLedgerQueryService: ledgerQuery);
    }

    private static Receipt IncidentReceipt(
        long id,
        int bins,
        Warehouse warehouse,
        Room room,
        FruitProfile profile,
        GrowerLot? growerLot) => new()
        {
            Id = id,
            CropYear = 2026,
            ReceivedAt = DateTimeOffset.Parse("2026-07-26T19:34:00Z").AddMinutes(id - 92),
            CompuTechReceiptId = $"INC-{id}",
            Warehouse = warehouse,
            Room = room,
            FruitProfile = profile,
            GrowerLot = growerLot,
            GrowerNumber = "1084",
            GrowerName = "Grower 1084",
            LotCode = "1084",
            BinCount = bins,
            CreatedAt = DateTimeOffset.Parse("2026-07-26T19:35:00Z").AddMinutes(id - 92),
            UpdatedAt = DateTimeOffset.Parse("2026-07-26T19:35:00Z").AddMinutes(id - 92)
        };

    private static Receipt AliasReceipt(
        long id,
        string receiptNumber,
        string growerNumber,
        string lot,
        int bins,
        Warehouse warehouse,
        Room room,
        FruitProfile profile) => new()
        {
            Id = id,
            CropYear = 2026,
            ReceivedAt = DateTimeOffset.Parse("2026-08-01T17:00:00Z").AddMinutes(id - 4101),
            CompuTechReceiptId = receiptNumber,
            ReceiptType = "Truck receipt",
            Warehouse = warehouse,
            Room = room,
            FruitProfile = profile,
            GrowerNumber = growerNumber,
            GrowerName = "Grower 1084",
            LotCode = lot,
            BinCount = bins,
            CreatedAt = DateTimeOffset.Parse("2026-08-01T17:00:00Z").AddMinutes(id - 4101),
            UpdatedAt = DateTimeOffset.Parse("2026-08-01T17:00:00Z").AddMinutes(id - 4101)
        };

    private static RoomInventoryAdjustment RealReceiptAdjustment(long id, Receipt receipt) => new()
    {
        Id = id,
        CropYear = receipt.CropYear,
        GrowerLotId = receipt.GrowerLotId,
        Receipt = receipt,
        Warehouse = receipt.Warehouse,
        Room = receipt.Room,
        FruitProfile = receipt.FruitProfile,
        GrowerName = receipt.GrowerName,
        LotNumber = receipt.LotCode,
        VarietyCode = receipt.FruitProfile.VarietyCode,
        OldBinCount = 0,
        ChangeAmount = receipt.BinCount,
        NewBinCount = receipt.BinCount,
        AdjustmentType = "ReceiptAdd",
        Source = "Receiving inventory added",
        AdjustmentAt = receipt.ReceivedAt,
        CreatedAt = receipt.ReceivedAt
    };

    private static RoomInventoryLedgerSnapshot IncidentSnapshot(int? growerLotId, int currentBins, long latestAdjustmentId) => new(
        1,
        "WP",
        1,
        "WP-1",
        "",
        2026,
        growerLotId,
        17,
        "Grower 1084",
        "1084",
        "1084",
        null,
        "BART",
        "BART",
        "Bartlett",
        "Pear",
        "Conventional",
        false,
        "",
        Math.Max(0, currentBins),
        Math.Min(0, currentBins),
        0,
        0,
        0,
        0,
        0,
        0,
        0,
        currentBins,
        1,
        DateTimeOffset.Parse("2026-07-26T19:34:00Z"),
        DateTimeOffset.Parse("2026-08-04T01:28:00Z"),
        latestAdjustmentId,
        growerLotId is null ? "Legacy receipt inventory" : $"Grower Lot {growerLotId}");

    private static RoomInventoryLedgerSnapshot StorageSnapshot(
        int warehouseId,
        int roomId,
        int? fruitProfileId,
        string lot,
        string variety,
        string varietyName,
        string productionType,
        bool? isOrganic,
        int currentBins) => new(
            warehouseId,
            "WP",
            roomId,
            "WP Alias",
            "",
            2026,
            null,
            fruitProfileId,
            "Grower 1084",
            "1084",
            lot,
            null,
            variety,
            variety,
            varietyName,
            "Apple",
            productionType,
            isOrganic,
            "",
            currentBins,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            currentBins,
            1,
            DateTimeOffset.Parse("2026-08-01T17:00:00Z"),
            DateTimeOffset.Parse("2026-08-01T17:00:00Z"),
            9000 + currentBins,
            "Alias test evidence");

    private sealed class FixedLedgerQuery(IReadOnlyList<RoomInventoryLedgerSnapshot> snapshots) : IRoomInventoryLedgerQueryService
    {
        public Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(
            int? warehouseId,
            IReadOnlyCollection<int>? roomIds,
            CancellationToken cancellationToken) =>
            GetSnapshotsAsync(warehouseId, roomIds, null, cancellationToken);

        public Task<IReadOnlyList<RoomInventoryLedgerSnapshot>> GetSnapshotsAsync(
            int? warehouseId,
            IReadOnlyCollection<int>? roomIds,
            int? fruitProfileId,
            CancellationToken cancellationToken)
        {
            var filtered = snapshots
                .Where(x => warehouseId is null || x.WarehouseId == warehouseId)
                .Where(x => roomIds is not { Count: > 0 } || roomIds.Contains(x.RoomId))
                .Where(x => fruitProfileId is null || x.FruitProfileId == fruitProfileId)
                .ToList();
            return Task.FromResult<IReadOnlyList<RoomInventoryLedgerSnapshot>>(filtered);
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add($"{formatter(state, exception)} {exception}");
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
