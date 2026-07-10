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
        Assert.Contains("ReceiptStorageExclusionReason", service);
        Assert.Contains("only Truck Receipt records add storage bins", service);
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
    public void DepletionCommands_UseRoomTransactionMatrixAccess()
    {
        AssertActionPolicy<HomeController>(nameof(HomeController.DepleteRoom), AccessPolicyNames.RoomTransactionsEdit);
        AssertActionPolicy<HomeController>(nameof(HomeController.VoidRoomDepletion), AccessPolicyNames.RoomTransactionsAdmin);
        AssertActionPolicy<HomeController>(nameof(HomeController.InventoryTrueUp), AccessPolicyNames.RoomTransactionsAdmin);
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
        Assert.Contains("Room Transaction History", room);
        Assert.Contains("True Up Current Lot Bins", room);
        Assert.Contains("Weakest lot", home);
        Assert.Contains("Weakest lot signal", partial);
        Assert.Contains("FindWeakestLot", service);
    }

    [Fact]
    public void RoomsTab_ShowsInventoryFillManagement()
    {
        var layout = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_Layout.cshtml"));
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "HomeController.cs"));
        var model = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Models", "DashboardViewModels.cs"));
        var rooms = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "Rooms.cshtml"));
        var room = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "Room.cshtml"));

        Assert.Contains("<a href=\"/Rooms\">Rooms</a>", layout);
        Assert.Contains("[HttpGet(\"/Rooms\")]", controller);
        Assert.Contains("[HttpGet(\"/Rooms/{roomId:int}\")]", controller);
        Assert.Contains("RoomsPageViewModel", model);
        Assert.Contains("StartingSeasonBins", model);
        Assert.Contains("NetChangeBins", model);
        Assert.Contains("CompuTechCode", model);
        Assert.Contains("LastActivityAt", model);
        Assert.Contains("Current Bins", rooms);
        Assert.Contains("Starting Season", rooms);
        Assert.Contains("Linked Receipts", room);
        Assert.Contains("Room Transaction History", room);
    }

    [Fact]
    public void RoomTransfers_AreAuditedAndPermissioned()
    {
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "HomeController.cs"));
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var model = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Models", "DashboardViewModels.cs"));
        var room = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "Room.cshtml"));

        Assert.Contains("[HttpPost(\"/Dashboard/Rooms/{roomId:int}/Transfer\")]", controller);
        Assert.Contains("AccessPolicyNames.RoomTransactionsEdit", controller);
        Assert.Contains("CreateRoomTransferAsync", service);
        Assert.Contains("\"TransferOut\"", service);
        Assert.Contains("\"TransferIn\"", service);
        Assert.Contains("AddAuditAsync(\"Transfer\", nameof(RoomInventoryAdjustment)", service);
        Assert.Contains("RoomTransferForm", model);
        Assert.Contains("TransferLotOptions", model);
        Assert.Contains("Record Transfer", room);
        Assert.Contains("Bins in", room);
        Assert.Contains("Bins out", room);
    }

    [Fact]
    public void EbsCurrentInventoryBaseline_IncludesCompuTechSeedFileAndExpectedRoomTotals()
    {
        var csvPath = FindRepositoryFile("src", "CropQc.Web", "Data", "Seed", "ebs-starting-room-inventory.csv");
        var csv = File.ReadAllText(csvPath);
        var rows = File.ReadLines(csvPath).Skip(1)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(line => line.Split(','))
            .ToList();

        Assert.Equal(18, rows.Count);
        Assert.StartsWith("CropYear,Warehouse,RoomCode,Grower,Lot,Variety,Bins,Status,EffectiveDate,Notes", csv);
        Assert.Equal(0, SumBins(rows, "evanca05"));
        Assert.Equal(1022, SumBins(rows, "EVANCA12"));
        Assert.Equal(0, SumBins(rows, "BLUECA04"));
        Assert.Equal(1178, SumBins(rows, "BLUECA01"));
        Assert.Equal(1201, SumBins(rows, "EVANCA01"));
        Assert.Equal(1918, SumBins(rows, "LAMBCA17"));
        Assert.Equal(514, SumBins(rows, "BLUECA06"));
        Assert.Contains("2026,EBS,EVANCA12,,1570,FUJI,819,Sealed,2026-06-18", csv);
        Assert.Contains("2026,EBS,EVANCA01,,9660,RED,1039,,2026-06-18", csv);
        Assert.Contains("2026,EBS,LAMBCA17,,1020,PINK,559,Sealed,2026-06-18", csv);
        Assert.Contains("2026,EBS,BLUECA01,,9560,RED,608,Sealed,2026-06-18", csv);
        Assert.Contains("2026,EBS,BLUECA06,,1290,GSMT,281,Sealed,2026-06-18", csv);
        Assert.Contains("Wes Verified Current Inventory Baseline 2026-06-18", csv);
    }

    [Fact]
    public void EbsCurrentInventoryBaseline_MapsShortCodesAndAuditsBinChanges()
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
        Assert.Contains("CurrentInventoryBaselineType", service);
        Assert.Contains("ParseCropYear", service);
        Assert.Contains("ParseEffectiveDate", service);
        Assert.Contains("GetCsvTemplate", service);
        Assert.Contains("GetCsvExample", service);
        Assert.Contains("BuildRoomTotalPreview", service);
        Assert.Contains("ConfirmReplaceExistingBatch", service);
        Assert.Contains("BinCountChange", service);
        Assert.Contains("public int? CropYear", qcEntity);
        Assert.Contains("public string? CropQcRoomName", masterEntity);
        Assert.Contains("public string? CompuTechRoomCode", masterEntity);
        Assert.Contains("public string? Source", qcEntity);
        Assert.Contains("public string? SourceRoomCode", qcEntity);
        Assert.Contains("public string? InventoryStatus", qcEntity);
        Assert.Contains("EnsureRoomMetadataSchemaAsync", program);
        Assert.Contains("ADD COLUMN IF NOT EXISTS \"CropYear\"", program);
        Assert.Contains("ADD COLUMN IF NOT EXISTS \"InventoryStatus\"", program);
        Assert.Contains("ADD COLUMN IF NOT EXISTS \"SourceRoomCode\"", program);
        Assert.Contains("ADD COLUMN IF NOT EXISTS \"CropQcRoomName\"", program);
        Assert.Contains("AccessPolicyNames.CurrentLotsView", controller);
        Assert.Contains("[HttpGet(\"Template\")]", controller);
        Assert.Contains("Import EBS Current Baseline", view);
        Assert.Contains("Download CSV Template", view);
        Assert.Contains("Show Example", view);
        Assert.Contains("Room Totals Preview", view);
        Assert.Contains("Confirm replacement of existing baseline batch rows", view);
        Assert.Contains("Current Inventory Baseline", view);
        Assert.Contains("Effective", view);
        Assert.Contains("Status", view);
        Assert.Contains("Import Preview", view);
        Assert.Contains("Crop QC Room", view);
        Assert.Contains("Compu-Tech Code", view);
        Assert.Contains("CanApplyEbsCorrectionSeed", controller);
        Assert.Contains("ApplicationAreas.CurrentLots", controller);
        var accessService = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "UserAccessService.cs"));
        Assert.Contains("IsOwner", accessService);
        Assert.Contains("wes@fruitandland.com", accessService);
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
        Assert.Contains("ReceiptStorageExclusionReason", service);
        Assert.Contains("if (receipt.IsDeleted", service);
        Assert.Contains("HasStorageExcludedIdentifierPrefix(receipt.CompuTechReceiptId, \"LS\")", service);
        Assert.Contains("HasStorageExcludedIdentifierPrefix(receipt.CompuTechReceiptId, \"DS\")", service);
        Assert.Contains("!string.Equals(receipt.ReceiptType, \"Truck receipt\"", service);
        Assert.Contains("HasStorageExcludedIdentifierPrefix", service);
        Assert.Contains("Excluded: LS prefix.", service);
        Assert.Contains("Excluded: DS prefix.", service);
        Assert.Contains("Excluded: Door Sample.", service);
        Assert.Contains("Excluded: Lot Sample.", service);
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

        Assert.Contains("AccessPolicyNames.ReceiptEditEdit", controller);
        Assert.Contains("AccessPolicyNames.ReceiptDeleteAdmin", controller);
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

        Assert.Equal(1201, SumBins(rows, "EVANCA01"));
        Assert.Equal(1022, SumBins(rows, "EVANCA12"));
        Assert.Equal(1918, SumBins(rows, "LAMBCA17"));
        Assert.Equal(1178, SumBins(rows, "BLUECA01"));
        Assert.Equal(0, SumBins(rows, "BLUECA04"));
        Assert.Equal(514, SumBins(rows, "BLUECA06"));
    }

    [Fact]
    public void DashboardRoomSummary_IncludesAdjustmentOnlyCurrentInventoryBaseline()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var model = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Models", "DashboardViewModels.cs"));
        var partial = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_RoomLotCard.cshtml"));
        var layout = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_Layout.cshtml"));

        Assert.Contains("BuildAdjustmentOnlyLotSummariesAsync", service);
        Assert.Contains("ReceiptId == null", service);
        Assert.Contains("RoomInventoryImportService.StartingInventoryAdjustmentType", service);
        Assert.Contains("BreakdownSourceType", service);
        Assert.Contains("Current Inventory Baseline", service);
        Assert.Contains("InventoryAdjustmentId", model);
        Assert.Contains("InventoryStatus", model);
        Assert.Contains("Current inventory baseline; no receipt history yet.", partial);
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

    [Fact]
    public void DashboardRoomDetail_ReusesRoomProjectionSummariesAndPreservesExistingSections()
    {
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "HomeController.cs"));
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var binsRunService = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "BinsRunService.cs"));
        var projectionMath = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "ProjectionDistributionMath.cs"));
        var model = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Models", "DashboardViewModels.cs"));
        var room = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Home", "Room.cshtml"));
        var css = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "wwwroot", "css", "site.css"));

        Assert.Contains("[HttpPost(\"/Dashboard/Rooms/{roomId:int}/Projection\")]", controller);
        Assert.Contains("AccessPolicyNames.RoomsView", controller);
        Assert.Contains("GetRoomProjectionAsync", controller);
        Assert.Contains("BaselineProjection", model);
        Assert.Contains("ProjectionLots", model);
        Assert.Contains("SampleTimeline", model);
        Assert.Contains("RoomProjectionRequest", model);
        Assert.Contains("BinsRunProjectionViewModel", model);
        Assert.Contains("SizeDisplayOrder = [32, 36, 40, 48, 56, 64, 72, 80, 88, 100, 113, 125, 138, 150, 163, 175, 198, 216]", projectionMath);
        Assert.Contains("BuildRoomProjection(activeLots, sampleDistributions, isSelection: false)", service);
        Assert.Contains("BuildRoomProjection(lots, sampleDistributions, isSelection)", service);
        Assert.Contains("ProjectionDistributionMath.CombineWeightedSizePercentages", service);
        Assert.Contains("ProjectionDistributionMath.CombineWeightedSizePercentages", binsRunService);
        Assert.Contains("currentBins(lot) * percentage", projectionMath);
        Assert.Contains("lot.CurrentBins * grade.Value", service);
        Assert.Contains("Percentages use entered fruit rows as the denominator", room);
        Assert.Contains("SizeRepresentedBins", service);
        Assert.Contains("SizeMissingBins", service);
        Assert.Contains("GradeRepresentedBins", service);
        Assert.Contains("GradeMissingBins", service);
        Assert.Contains("Missing sizing", service);
        Assert.Contains("Missing grade", service);
        Assert.Contains("Sample is", service);
        Assert.Contains("Room Insights", room);
        Assert.Contains("Baseline Size Distribution", room);
        Assert.Contains("Baseline Expected Grade", room);
        Assert.Contains("Packout Projections", room);
        Assert.Contains("Projection A", room);
        Assert.Contains("Projection B", room);
        Assert.Contains("Select flagged lots", room);
        Assert.Contains("Exclude flagged lots", room);
        Assert.Contains("Change Over Time", room);
        Assert.Contains("Projections are read-only and do not change inventory.", room);
        Assert.Contains("Current Lots", room);
        Assert.Contains("Linked Receipts", room);
        Assert.Contains("Room Transaction History", room);
        Assert.Contains("Depletion History", room);
        Assert.Contains("Depleted / Historical Lots", room);
        Assert.Contains("AbortController", room);
        Assert.Contains("data-room-project-lot", room);
        Assert.Contains("room-projection-grid", css);
        Assert.Contains("room-projection-lot", css);
        Assert.Contains("projection-comparison", css);
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
