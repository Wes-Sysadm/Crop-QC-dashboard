using CropQc.Web.Controllers;
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
        var receiptModel = File.ReadAllText(FindRepositoryFile("src", "CropQc.Data", "Entities", "QcModels.cs"));
        var receiptView = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Receipts", "Index.cshtml"));

        Assert.Contains("\"grower-lots\" => await GrowerLotsPage", admin);
        Assert.Contains("Grower Lots", admin);
        Assert.Contains("Lot #", admin);
        Assert.Contains("Pool Start", admin);
        Assert.Contains("INACTIVE", admin);
        Assert.Contains("Pool Start", fields);
        Assert.Contains("Lot #", fields);
        Assert.Contains("public string? GrowerNumber", receiptModel);
        Assert.Contains("public string? PoolStart", receiptModel);
        Assert.Contains("public int? GrowerLotId", receiptModel);
        Assert.Contains("name=\"GrowerNumber\"", receiptView);
        Assert.Contains("name=\"PoolStart\"", receiptView);
        Assert.Contains("@lot.Grower - @lot.LotNumber", receiptView);
        Assert.Contains("data-pool", receiptView);
        Assert.Contains("growerLotOptions", receiptView);
        Assert.Contains("Lot # not found in Master Data", receiptView);
        Assert.Contains("/MasterData/grower-lots", masterData);
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
