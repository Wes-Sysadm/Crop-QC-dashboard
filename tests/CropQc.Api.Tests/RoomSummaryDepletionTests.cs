using CropQc.Web.Controllers;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using System.Reflection;

namespace CropQc.Api.Tests;

public sealed class RoomSummaryDepletionTests
{
    [Fact]
    public void Dashboard_RoomSummaryUsesAllRoomsAndShowsEmptyRooms()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "Index.cshtml"));

        Assert.Contains("BuildRoomSummariesAsync", service);
        Assert.Contains("dbContext.Rooms.AsNoTracking()", service);
        Assert.Contains("Status = status", service);
        Assert.Contains("roomLots.Count == 0 ? \"Empty\"", service);
        Assert.Contains("Room Summary", view);
        Assert.Contains("Model.RoomSummaries", view);
        Assert.Contains("/Dashboard/Rooms/@room.RoomId", view);
        Assert.Contains("Rooms with fruit", view);
        Assert.Contains("Empty rooms", view);
        Assert.Contains("All rooms", view);
    }

    [Fact]
    public void RoomSummary_ExcludesVoidedDepletionsAndUsesCurrentBins()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));

        Assert.Contains("RoomDepletions.AsNoTracking()", service);
        Assert.Contains("!x.IsVoided", service);
        Assert.Contains("latestAdjustment.NewBinCount", service);
        Assert.Contains("receipt.BinCount - depleted", service);
        Assert.Contains("CurrentBins > 0", service);
        Assert.Contains("GroupBy(x => x.RoomId)", service);
        Assert.Contains("var roomLots = currentLotsByRoom.GetValueOrDefault(room.Id", service);
    }

    [Fact]
    public void Dashboard_RoomSummaryHasFacilityAndEbsLocationFilters()
    {
        var model = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Models", "DashboardViewModels.cs"));
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "HomeController.cs"));
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "Index.cshtml"));

        Assert.Contains("RoomSummaryFilterForm", model);
        Assert.Contains("\"All\", \"MCD\", \"WP\", \"EBS\", \"DH\"", model);
        Assert.Contains("\"All EBS\", \"Evans\", \"Lamb\", \"BM\"", model);
        Assert.Contains("[FromQuery] RoomSummaryFilterForm", controller);
        Assert.Contains("RoomStatus { get; set; } = \"WithFruit\"", model);
        Assert.Contains("FacilityCode", service);
        Assert.Contains("RoomLocationGroup", service);
        Assert.Contains("Evans", view);
        Assert.Contains("Lamb", view);
        Assert.Contains("BM", view);
    }

    [Fact]
    public void RoomInventory_SeparatesReceivingInventoryFromObservationalSamples()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var dashboard = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "Index.cshtml"));
        var docs = File.ReadAllText(FindRepositoryFile("docs", "architecture.md"));

        Assert.Contains("Receipts/receiving add current inventory", dashboard);
        Assert.Contains("Door and Lot samples are observational", dashboard);
        Assert.Contains("Door and Lot samples do not create room inventory", docs);
        Assert.Contains("latestAdjustment.NewBinCount", service);
        Assert.Contains("var lotSamples = samplesByReceipt.GetValueOrDefault(receipt.Id", service);
    }

    [Fact]
    public void MasterData_GrowerLotsExposeGrowerLotTerminology()
    {
        var admin = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "AdminManagementService.cs"));
        var masterData = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "MasterData", "Index.cshtml"));
        var fields = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "MasterData", "_MasterDataFields.cshtml"));

        Assert.Contains("\"grower-lots\" => await GrowerLotsPage", admin);
        Assert.Contains("Grower Lots", admin);
        Assert.Contains("Lot #", admin);
        Assert.Contains("Pool Start", admin);
        Assert.Contains("INACTIVE", admin);
        Assert.Contains("Pool Start", fields);
        Assert.Contains("Lot #", fields);
        Assert.Contains("/MasterData/grower-lots", masterData);
    }

    [Fact]
    public void Receipts_RemovePoolStartAndReceiptLotAndUseEditableReceiptType()
    {
        var receiptModel = File.ReadAllText(FindRepositoryFile("src", "CropQc.Data", "Entities", "QcModels.cs"));
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var receiptView = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Receipts", "Index.cshtml"));
        var detailsView = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Receipts", "Details.cshtml"));

        Assert.Contains("public string ReceiptType", receiptModel);
        Assert.Contains("ReceiptTypeOptions = [\"Truck receipt\", \"Door sample\", \"Lot sample\"]", service);
        Assert.Contains("name=\"ReceiptType\"", receiptView);
        Assert.Contains("Truck receipt", receiptView);
        Assert.Contains("Door sample", receiptView);
        Assert.Contains("Lot sample", receiptView);
        Assert.Contains("BinCount = Math.Max(0, form.BinCount)", service);
        Assert.Contains(".Where(x => !x.IsDeleted && x.ReceiptType == \"Truck receipt\")", service);
        Assert.Contains("PoolStart = null", service);
        Assert.Contains("public string? GrowerNumber", receiptModel);
        Assert.Contains("public string? PoolStart", receiptModel);
        Assert.Contains("public int? GrowerLotId", receiptModel);
        Assert.Contains("name=\"GrowerNumber\"", receiptView);
        Assert.Contains("@lot.Grower - @lot.LotNumber", receiptView);
        Assert.Contains("growerLotOptions", receiptView);
        Assert.Contains("Lot # not found in Master Data", receiptView);
        Assert.DoesNotContain("name=\"PoolStart\"", receiptView);
        Assert.DoesNotContain("name=\"LotCode\"", receiptView);
        Assert.DoesNotContain("Receipt Lot", receiptView);
        Assert.DoesNotContain("Pool Start", receiptView);
        Assert.DoesNotContain("Pool Start", detailsView);
    }

    [Fact]
    public void GrowerLots_SupportMassImportPreviewAndApply()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "AdminManagementService.cs"));
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "MasterDataController.cs"));
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "MasterData", "Index.cshtml"));

        Assert.Contains("PreviewGrowerLotImportAsync", service);
        Assert.Contains("ApplyGrowerLotImportAsync", service);
        Assert.Contains("Grower Number", service);
        Assert.Contains("POOL Starts", service);
        Assert.Contains("Duplicate Lot #", service);
        Assert.Contains("never deletes production rows", view);
        Assert.Contains("enctype=\"multipart/form-data\"", view);
        Assert.Contains("ImportPreview", controller);
        Assert.Contains("ImportApply", controller);
    }

    [Fact]
    public void DepletionCommands_AreManagerOrAdminOnly()
    {
        AssertActionPolicy<HomeController>(nameof(HomeController.DepleteRoom), "RequireManagerOrAdmin");
        AssertActionPolicy<HomeController>(nameof(HomeController.VoidRoomDepletion), "RequireManagerOrAdmin");
        AssertActionPolicy<HomeController>(nameof(HomeController.InventoryTrueUp), "RequireManagerOrAdmin");
    }

    [Fact]
    public void Depletion_IsAdditiveAuditedAndVoidable()
    {
        var entity = File.ReadAllText(FindRepositoryFile("src", "CropQc.Data", "Entities", "QcModels.cs"));
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var program = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Program.cs"));

        Assert.Contains("public sealed class RoomDepletion", entity);
        Assert.Contains("public sealed class RoomInventoryAdjustment", entity);
        Assert.Contains("IsVoided", entity);
        Assert.Contains("VoidReason", entity);
        Assert.Contains("AddAuditAsync(\"Create\", nameof(RoomDepletion)", service);
        Assert.Contains("AddAuditAsync(\"Void\", nameof(RoomDepletion)", service);
        Assert.Contains("AddAuditAsync(\"BinCountChange\"", service);
        Assert.Contains("EnsureRoomDepletionSchemaAsync", program);
        Assert.Contains("EnsureRoomInventoryAdjustmentSchemaAsync", program);
        Assert.DoesNotContain("Remove(depletion)", service, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RoomInventoryTrueUp_RecordsAdjustmentHistoryAndWeakestLots()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var room = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "Room.cshtml"));
        var home = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "Index.cshtml"));
        var partial = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_RoomLotCard.cshtml"));

        Assert.Contains("CreateRoomInventoryTrueUpAsync", service);
        Assert.Contains("ManualTrueUp", service);
        Assert.Contains("ReceiptAdd", service);
        Assert.Contains("Depletion", service);
        Assert.Contains("Void/Reversal", service);
        Assert.Contains("Bin Count History", room);
        Assert.Contains("True Up Current Lot Bins", room);
        Assert.Contains("Weakest lot", home);
        Assert.Contains("Weakest lot signal", partial);
        Assert.Contains("FindWeakestLot", service);
    }

    [Fact]
    public void EbsStartingInventoryImport_IncludesCompuTechSeedFileAndExpectedRoomTotals()
    {
        var csvPath = FindRepositoryFile("src", "CropQc.Web", "Data", "Seed", "ebs-starting-room-inventory.csv");
        var csv = File.ReadAllText(csvPath);
        var rows = File.ReadLines(csvPath).Skip(1)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Split(','))
            .ToList();

        Assert.Equal(6, rows.Count);
        Assert.StartsWith("Facility,SubLocation,CropQcRoomName,CompuTechRoomCode", csv);
        Assert.Equal(0, SumBins(rows, "evans-5"));
        Assert.Equal(1469, SumBins(rows, "Evans-12"));
        Assert.Equal(186, SumBins(rows, "BM-4"));
        Assert.Equal(1178, SumBins(rows, "BM-1"));
        Assert.Equal(1462, SumBins(rows, "Evans-01"));
        Assert.Equal(1918, SumBins(rows, "Lamb-17"));
        Assert.Equal(786, SumBins(rows, "BM-6"));
        Assert.Contains("EBS,Evans,Evans-12,evanca12", csv);
        Assert.Contains("EBS,BM,BM-1,blueca01", csv);
        Assert.Contains("EBS,BM,BM-6,Blueca06,GSMT,Sealed,786", csv);
        Assert.Contains("Wes Corrected Current Inventory 2026-06-15", csv);
    }

    [Fact]
    public void EbsStartingInventoryImport_MapsShortCodesAndAuditsBinChanges()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "RoomInventoryImportService.cs"));
        var qcEntity = File.ReadAllText(FindRepositoryFile("src", "CropQc.Data", "Entities", "QcModels.cs"));
        var masterEntity = File.ReadAllText(FindRepositoryFile("src", "CropQc.Data", "Entities", "MasterDataModels.cs"));
        var program = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Program.cs"));
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "RoomInventoryController.cs"));
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "RoomInventory", "Index.cshtml"));

        Assert.Contains("CropQcRoomNameForCompuTechCode", service);
        Assert.Contains("\"EVANCA05\" => \"Evans-5\"", service);
        Assert.Contains("\"EVANCA12\" => \"Evans-12\"", service);
        Assert.Contains("\"BLUECA04\" => \"BM-4\"", service);
        Assert.Contains("\"BLUECA06\" => \"BM-6\"", service);
        Assert.Contains("\"BLUECA01\" => \"BM-1\"", service);
        Assert.Contains("\"EVANCA01\" => \"Evans-01\"", service);
        Assert.Contains("\"LAMBCA17\" => \"Lamb-17\"", service);
        Assert.Contains("DetermineEbsSubLocation", service);
        Assert.Contains("Grower not found in Master Data", service);
        Assert.Contains("Duplicate inventory row", service);
        Assert.Contains("StartingInventoryImport", service);
        Assert.Contains("BinCountChange", service);
        Assert.Contains("public string? CropQcRoomName", masterEntity);
        Assert.Contains("public string? CompuTechRoomCode", masterEntity);
        Assert.Contains("public string? Source", qcEntity);
        Assert.Contains("public string? SourceRoomCode", qcEntity);
        Assert.Contains("EnsureRoomMetadataSchemaAsync", program);
        Assert.Contains("ADD COLUMN IF NOT EXISTS \"SourceRoomCode\"", program);
        Assert.Contains("ADD COLUMN IF NOT EXISTS \"CropQcRoomName\"", program);
        Assert.Contains("[Authorize(Policy = \"RequireManagerOrAdmin\")]", controller);
        Assert.Contains("Import EBS Starting Inventory", view);
        Assert.Contains("Import Preview", view);
        Assert.Contains("Crop QC Room", view);
        Assert.Contains("Compu-Tech Code", view);
        Assert.Contains("CanApplyEbsCorrectionSeed", controller);
        Assert.Contains("wes@fruitandland.com", controller);
        Assert.Contains("return Forbid()", controller);
    }

    [Fact]
    public void RoomCompuTechCode_IsEditableAndUsedForImportMatching()
    {
        var model = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Models", "DashboardViewModels.cs"));
        var admin = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "AdminManagementService.cs"));
        var fields = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "MasterData", "_MasterDataFields.cshtml"));
        var import = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "RoomInventoryImportService.cs"));

        Assert.Contains("public string? CompuTechCode", model);
        Assert.Contains("CompuTechCode = x.CompuTechRoomCode", admin);
        Assert.Contains("entity.CompuTechRoomCode = Blank(form.CompuTechCode)", admin);
        Assert.Contains("name=\"CompuTechCode\"", fields);
        Assert.Contains("x.CompuTechRoomCode", import);
        Assert.Contains("NormalizeCode(x.CompuTechRoomCode)", import);
    }

    [Fact]
    public void CurrentStorageCounts_DeduplicateInventoryLots()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var import = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "RoomInventoryImportService.cs"));

        Assert.Contains("CurrentLotKey", service);
        Assert.Contains("currentLots.Select(CurrentLotKey).Distinct", service);
        Assert.Contains("BuildAdjustmentOnlyLotSummariesAsync", service);
        Assert.Contains("RoomInventoryImportService.CurrentStorageLotKey(x.RoomId, x.LotNumber, x.VarietyCode ?? \"\")", service);
        Assert.DoesNotContain("x.Source ?? x.Reason ?? \"\").Trim().ToUpperInvariant()", service);
        Assert.Contains("CurrentStorageLotKey(adjustment.RoomId, adjustment.LotNumber, adjustment.VarietyCode ?? \"\")", import);
        Assert.DoesNotContain("StartingInventoryKey(adjustment.RoomId, adjustment.LotNumber, adjustment.VarietyCode ?? \"\", adjustment.Source", import);
        Assert.Equal(
            RoomInventoryImportService.CurrentStorageLotKey(12, "EBS-ROOM-12-LOT", "GALA"),
            RoomInventoryImportService.CurrentStorageLotKey(12, " ebs-room-12-lot ", "gala"));
    }

    [Fact]
    public void EbsStorageImport_MapsBlueMountainRoom4Aliases()
    {
        Assert.Equal("BM-4", RoomInventoryImportService.NormalizeCropQcRoomName("Blue Mountain Room 4"));
        Assert.Equal("BM-4", RoomInventoryImportService.NormalizeCropQcRoomName("Blue Mountain 4"));
        Assert.Equal("BM-4", RoomInventoryImportService.MasterRoomCodeFor("Blue Mountain Room 4", ""));
        Assert.Equal("BM", RoomInventoryImportService.DetermineEbsSubLocation("Blue Mountain Room 4"));
        Assert.Equal(
            RoomInventoryImportService.CurrentStorageLotKey(4, "1001", "PINK"),
            RoomInventoryImportService.CurrentStorageLotKey(4, "1001", "pink"));
    }

    [Fact]
    public void EbsStorageCounts_DoNotMultiplyByChildJoins()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));

        Assert.Contains("var samples = await QuerySamples()", service);
        Assert.Contains("var samplesByReceipt = samples.GroupBy(x => x.ReceiptId)", service);
        Assert.Contains("var lotSamples = samplesByReceipt.GetValueOrDefault(receipt.Id", service);
        Assert.Contains("receipt.BinCount - depleted", service);
        Assert.DoesNotContain("receiptsQuery.Include(x => x.Samples)", service);
        Assert.DoesNotContain("receiptsQuery.Include(x => x.Photos)", service);
        Assert.DoesNotContain("receiptsQuery.Include(x => x.FruitReadings)", service);
    }

    [Fact]
    public void EbsDailyBinsEmail_IsConfigurableAndUsesDedupedDashboardCounts()
    {
        var emailService = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "EbsDailyBinsEmailService.cs"));
        var admin = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "AdminManagementService.cs"));
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "ConfigurationController.cs"));
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Configuration", "Index.cshtml"));
        var program = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Program.cs"));

        Assert.Contains("EbsDailyBinsEmailSettings.RecipientsKey", admin);
        Assert.Contains("EbsDailyBinsEmailSettings.EnabledKey", admin);
        Assert.Contains("EbsDailyBinsEmailSettings.SendHourLocalKey", admin);
        Assert.Contains("EbsDailyBinsEmailSettings.SenderEmailKey", admin);
        Assert.Contains("GetHomeDashboardAsync", emailService);
        Assert.Contains("new RoomSummaryFilterForm { Facility = \"EBS\"", emailService);
        Assert.Contains("Total bins currently in EBS storage", emailService);
        Assert.Contains("SendEbsDailyBinsNow", controller);
        Assert.Contains("SendEbsDailyBinsTest", controller);
        Assert.Contains("EBS Daily Bin Availability", view);
        Assert.Contains("EbsDailyBins/SendNow", view);
        Assert.Contains("EbsDailyBins/Test", view);
        Assert.Contains("AddHostedService<EbsDailyBinsEmailHostedService>", program);
    }

    [Fact]
    public void ReceiptBinCounts_OnlyTruckReceiptsContributeToStorage()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));

        Assert.Contains("BinCount = Math.Max(0, form.BinCount)", service);
        Assert.Contains(".Where(x => !x.IsDeleted && x.ReceiptType == \"Truck receipt\")", service);
        Assert.Contains("if (receipt.IsDeleted || !string.Equals(receipt.ReceiptType, \"Truck receipt\"", service);
        Assert.Contains("!x.IsDeleted", service);
        Assert.Contains("Truck receipt", service);
        Assert.Contains("Door sample", service);
        Assert.Contains("Lot sample", service);
    }

    [Fact]
    public void AdminCanEditAndSoftDeleteReceipts()
    {
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "ReceiptsController.cs"));
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var model = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Models", "DashboardViewModels.cs"));
        var detail = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Receipts", "Details.cshtml"));
        var edit = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Receipts", "Edit.cshtml"));

        Assert.Contains("[Authorize(Policy = \"RequireAdmin\")]", controller);
        Assert.Contains("UpdateReceiptAsync", service);
        Assert.Contains("SoftDeleteReceiptAsync", service);
        Assert.Contains("receipt.IsDeleted = true", service);
        Assert.Contains("receipt.DeletedAt", service);
        Assert.Contains("receipt.DeletedByUserId", service);
        Assert.Contains("AddAuditAsync(\"Delete\", nameof(Receipt)", service);
        Assert.Contains("UpdateReceiptForm", model);
        Assert.Contains("DeleteReceiptForm", model);
        Assert.Contains("/Receipts/@Model.Receipt.Id/Edit", detail);
        Assert.Contains("Delete Receipt", edit);
        Assert.Contains("name=\"BinCount\"", edit);
    }

    [Fact]
    public void EbsCorrectedSeedTotals_MatchWesCurrentRoomTotals()
    {
        var csvPath = FindRepositoryFile("src", "CropQc.Web", "Data", "Seed", "ebs-starting-room-inventory.csv");
        var rows = File.ReadLines(csvPath).Skip(1)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Split(','))
            .ToList();

        Assert.Equal(1462, SumBins(rows, "Evans-01"));
        Assert.Equal(1469, SumBins(rows, "Evans-12"));
        Assert.Equal(1918, SumBins(rows, "Lamb-17"));
        Assert.Equal(1178, SumBins(rows, "BM-1"));
        Assert.Equal(186, SumBins(rows, "BM-4"));
        Assert.Equal(786, SumBins(rows, "BM-6"));
    }

    [Fact]
    public void DashboardRoomSummary_IncludesAdjustmentOnlyStartingInventory()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var model = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Models", "DashboardViewModels.cs"));
        var partial = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_RoomLotCard.cshtml"));
        var layout = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_Layout.cshtml"));

        Assert.Contains("BuildAdjustmentOnlyLotSummariesAsync", service);
        Assert.Contains("ReceiptId == null", service);
        Assert.Contains("RoomInventoryImportService.StartingInventoryAdjustmentType", service);
        Assert.Contains("InventoryAdjustmentId", model);
        Assert.Contains("Starting inventory; no receipt history yet.", partial);
        Assert.Contains("/Admin/RoomInventory", layout);
        Assert.Contains("Current Lots", layout);
    }

    [Fact]
    public void DashboardAndGrowerLots_ShowCurrentStorage()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var model = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Models", "DashboardViewModels.cs"));
        var dashboard = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "Index.cshtml"));
        var growerLots = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "GrowerLots.cshtml"));
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "HomeController.cs"));

        Assert.Contains("Total Bins In Storage", service);
        Assert.Contains("Grower Lots In Storage", service);
        Assert.Contains("StorageByFacility", model);
        Assert.Contains("CurrentGrowerLotsPageViewModel", model);
        Assert.Contains("/GrowerLots/Current", dashboard);
        Assert.Contains("Bins Currently In Storage", growerLots);
        Assert.Contains("Last QC Sample", growerLots);
        Assert.Contains("Latest Avg Pressure", growerLots);
        Assert.Contains("GetCurrentGrowerLotsAsync", controller);
    }

    [Fact]
    public void CropYearReview_IsWesOnlyAndShowsPressureLoss()
    {
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "HomeController.cs"));
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var model = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Models", "DashboardViewModels.cs"));
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "CropYearReview.cshtml"));
        var layout = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_Layout.cshtml"));

        Assert.Contains("[HttpGet(\"/CropYearReview\")]", controller);
        Assert.Contains("wes@fruitandland.com", controller);
        Assert.Contains("return Forbid()", controller);
        Assert.Contains("GetCropYearReviewAsync", service);
        Assert.Contains("PressureLossPerWeek", model);
        Assert.Contains("Pressure Loss/Week", view);
        Assert.Contains("Days Between Samples", view);
        Assert.Contains("canAccessCropYearReview", layout);
    }

    [Fact]
    public void AdminUsers_RemoveDomainAndKeepActionsResponsive()
    {
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Admin", "Users.cshtml"));
        var css = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "wwwroot", "css", "site.css"));

        Assert.DoesNotContain("<th>Domain</th>", view);
        Assert.DoesNotContain("@user.Domain", view);
        Assert.Contains("responsive-admin-table", view);
        Assert.Contains("responsive-admin-table", css);
        Assert.Contains("display: block; width: 100% !important", css);
    }

    [Fact]
    public void RoomViews_UseResponsiveCardsAndVisibleActions()
    {
        var home = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "Index.cshtml"));
        var room = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "Room.cshtml"));
        var partial = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_RoomLotCard.cshtml"));
        var css = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "wwwroot", "css", "site.css"));

        Assert.Contains("room-card-grid", home);
        Assert.Contains("responsive-card-grid", room);
        Assert.Contains("action-bar", partial);
        Assert.Contains("Open QC Station", partial);
        Assert.Contains("View Receipt", partial);
        Assert.Contains("overflow-x: hidden", css);
        Assert.Contains("word-break: normal", css);
    }

    private static void AssertActionPolicy<TController>(string actionName, string policy)
    {
        var method = typeof(TController).GetMethod(actionName);
        Assert.NotNull(method);
        var attributes = method!.GetCustomAttributes<AuthorizeAttribute>().ToList();
        Assert.Contains(attributes, x => x.Policy == policy);
    }

    private static int SumBins(IEnumerable<string[]> rows, string cropQcRoomName) =>
        rows.Where(x => x.Length >= 7 && x[2].Equals(cropQcRoomName, StringComparison.OrdinalIgnoreCase)).Sum(x => int.Parse(x[6]));

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
