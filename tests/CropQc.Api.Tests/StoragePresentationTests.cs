using System.Reflection;
using CropQc.Web.Models;
using CropQc.Web.Services;

namespace CropQc.Api.Tests;

public sealed class StoragePresentationTests
{
    [Fact]
    public void RoomVarietyIdentity_DeduplicatesProductionTypeAndExplainsUnknownStatus()
    {
        Assert.Equal("Organic", new RoomVarietyColorSegmentViewModel { ProductionType = "organic", IsOrganic = true }.IdentityLabel);
        Assert.Equal("Conventional", new RoomVarietyColorSegmentViewModel { ProductionType = "Conventional", IsOrganic = false }.IdentityLabel);
        Assert.Equal("Fresh - Organic", new RoomVarietyColorSegmentViewModel { ProductionType = "Fresh", IsOrganic = true }.IdentityLabel);
        Assert.Equal("Organic status unknown", new RoomVarietyColorSegmentViewModel { IsOrganic = null }.IdentityLabel);
    }

    [Fact]
    public void ReceiptVarietyControl_IsCodeFirstSearchableAndQuickAddIsPermissionGated()
    {
        var view = ReadRepositoryFile("src", "CropQc.Web", "Views", "Receipts", "Index.cshtml");
        var controller = ReadRepositoryFile("src", "CropQc.Web", "Controllers", "ReceiptsController.cs");

        Assert.Contains("receiptVarietySearch", view);
        Assert.Contains("item.VarietyCode", view);
        Assert.Contains("item.ProductionType", view);
        Assert.Contains("Model.CanQuickAddVariety", view);
        Assert.Contains("[ValidateAntiForgeryToken]", controller);
        Assert.Contains("ApplicationAreas.Varieties, PageAccessLevel.Create", controller);
        Assert.Contains("SaveMasterDataAsync", controller);
    }

    [Fact]
    public void DedicatedRooms_UsesCardsDominantColorAndAccessibleNavigation()
    {
        var view = ReadRepositoryFile("src", "CropQc.Web", "Views", "Home", "Rooms.cshtml");

        Assert.Contains("dedicated-room-grid", view);
        Assert.DoesNotContain("<table>", view, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("room.VarietyColorSegments.FirstOrDefault()", view);
        Assert.Contains("card-stretched-link", view);
        Assert.Contains("Count Breakdown", view);
        Assert.Contains("room.VarietyColorSegments.Take(4)", view);
        Assert.Contains("+ @(room.VarietyColorSegments.Count - 4) more varieties", view);
        Assert.Contains("Majority organic", view);
    }

    [Fact]
    public void RoomVarietyIdentity_TieOrderIsStableAndIncludesProductionIdentity()
    {
        var service = ReadRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs");

        Assert.Contains(".OrderByDescending(x => x.CurrentBins)", service);
        Assert.Contains(".ThenBy(x => x.VarietyName)", service);
        Assert.Contains(".ThenBy(x => x.ProductionType)", service);
        Assert.Contains(".ThenBy(x => x.VarietyKey)", service);
        Assert.Contains("x.IsOrganic?.ToString()", service);
    }

    [Fact]
    public void RoomGrowerWeighting_UsesCurrentBinsAndMetricSpecificDenominators()
    {
        var rows = new[]
        {
            Lot("1084", "Smith", "Gala", 300, pressure: 15m, starch: 4m),
            Lot("1084", "Smith", "Honeycrisp", 100, pressure: 11m, starch: null)
        };

        var summaries = InvokeStatic<IReadOnlyList<RoomGrowerSummaryViewModel>>(
            typeof(DashboardDataService),
            "BuildRoomGrowerSummaries",
            (object)rows);

        var grower = Assert.Single(summaries);
        Assert.Equal(400, grower.CurrentBins);
        Assert.Equal(14m, grower.WeightedPressureLbs);
        Assert.Equal(400, grower.PressureRepresentedBins);
        Assert.Equal(4m, grower.WeightedStarch);
        Assert.Equal(300, grower.StarchRepresentedBins);
        Assert.Equal(grower.CurrentBins, grower.Varieties.Sum(x => x.BinCount));
    }

    [Fact]
    public void RoomGrowers_GroupByNumberRatherThanMatchingName()
    {
        var summaries = InvokeStatic<IReadOnlyList<RoomGrowerSummaryViewModel>>(
            typeof(DashboardDataService),
            "BuildRoomGrowerSummaries",
            (object)new[]
            {
                Lot("1084", "Same Orchard", "Gala", 10, 15m, 4m),
                Lot("1099", "Same Orchard", "Gala", 20, 15m, 4m)
            });

        Assert.Equal(2, summaries.Count);
        Assert.Equal(new[] { "1084", "1099" }, summaries.Select(x => x.GrowerNumber));
    }

    [Fact]
    public void RoomAndStorageViews_ShowCoverageAndUnavailableInsteadOfZero()
    {
        var room = ReadRepositoryFile("src", "CropQc.Web", "Views", "Home", "Room.cshtml");
        var storage = ReadRepositoryFile("src", "CropQc.Web", "Views", "Home", "GrowerLots.cshtml");

        Assert.Contains("Coverage:", room);
        Assert.Contains("Unavailable", room);
        Assert.Contains("Coverage:", storage);
        Assert.Contains("Current fruit by grower", room);
        Assert.Contains("storage-grower-card", storage);
    }

    [Fact]
    public void BinsInStorageColumn_UsesDedicatedCenteredClass()
    {
        var view = ReadRepositoryFile("src", "CropQc.Web", "Views", "Home", "GrowerLots.cshtml");
        var css = ReadRepositoryFile("src", "CropQc.Web", "wwwroot", "css", "site.css");

        Assert.Contains("<th class=\"bins-in-storage-column\">", view);
        Assert.Contains("<td class=\"bins-in-storage-column\">", view);
        Assert.Contains(".bins-in-storage-column", css);
        Assert.Contains("text-align: center !important", css);
    }

    [Fact]
    public void ReceiptResults_UseBatchResolvedCanonicalVarietyColors()
    {
        var service = ReadRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs");
        var view = ReadRepositoryFile("src", "CropQc.Web", "Views", "Receipts", "Index.cshtml");

        Assert.Contains("GetResolvedColorsReadOnlyAsync(receiptVarietyKeys", service);
        Assert.Contains("ReceiptListItem(receipt, sampleSummaries.GetValueOrDefault(receipt.Id), receiptColors, growerResolver)", service);
        Assert.Contains("receipt-card-colored", view);
        Assert.Contains("BuildVarietyBandBackground", view);
        Assert.Contains("OrganicLabel", view);
        Assert.Contains("linear-gradient(110deg", view);
        Assert.Contains("variety.BinCount) / (decimal)total", view);
    }

    [Fact]
    public void SiteWideVarietySurfaces_UseSharedConfiguredColorsAndOneOrganicStripe()
    {
        var css = ReadRepositoryFile("src", "CropQc.Web", "wwwroot", "css", "site.css");
        var dashboardService = ReadRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs");
        var inventoryService = ReadRepositoryFile("src", "CropQc.Web", "Services", "InventoryByVarietyService.cs");
        var reportingService = ReadRepositoryFile("src", "CropQc.Web", "Services", "RunReportingService.cs");
        var rooms = ReadRepositoryFile("src", "CropQc.Web", "Views", "Home", "Rooms.cshtml");
        var receipts = ReadRepositoryFile("src", "CropQc.Web", "Views", "Receipts", "Index.cshtml");
        var endOfDay = ReadRepositoryFile("src", "CropQc.Web", "Views", "EndOfDayFill", "Index.cshtml");
        var runTotals = ReadRepositoryFile("src", "CropQc.Web", "Views", "BinsRun", "Index.cshtml");

        Assert.Contains("resolver.GetResolvedColorsReadOnlyAsync(keys", dashboardService);
        Assert.Contains("GetResolvedColorsReadOnlyAsync", inventoryService);
        Assert.Contains("GetResolvedColorsReadOnlyAsync", reportingService);
        Assert.Contains("--room-variety-bg: @cardColor", rooms);
        Assert.Contains("--receipt-variety-bg: @BuildVarietyBandBackground", receipts);
        Assert.Contains("--variety-color: @variety.HexColor", endOfDay);
        Assert.Contains("--variety-bg: @variety.ColorHex", runTotals);

        Assert.Contains("--variety-organic-stripe", css);
        Assert.Equal(1, CountOccurrences(css, "repeating-linear-gradient("));
        Assert.True(CountOccurrences(css, "var(--variety-organic-stripe)") >= 8);
        Assert.DoesNotContain("rgba(22,101,52", css);
    }

    [Fact]
    public void VarietyPresentation_PreservesReadableContrastNeutralFallbackAndStatusColors()
    {
        var css = ReadRepositoryFile("src", "CropQc.Web", "wwwroot", "css", "site.css");

        Assert.Equal("#17212B", VarietyColorService.TextColor("#F5E66A"));
        Assert.Equal("#FFFFFF", VarietyColorService.TextColor("#123456"));
        Assert.Equal(VarietyColorService.NeutralFallbackColor, VarietyColorService.FallbackColor("UNKNOWN"));
        Assert.Contains(".status.ready, .status.sent", css);
        Assert.Contains(".status.missing, .status.pending", css);
        Assert.Contains(".notice.warning", css);
        Assert.Contains(".notice.error", css);
    }

    [Fact]
    public void CurrentStorageFilters_ConstrainSafeSourcesAndApplyIdentityFiltersAfterReconciliation()
    {
        var service = ReadRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs");

        Assert.Contains("var scopedRoomIds = await scopedRoomQuery.Select(x => x.Id).ToListAsync", service);
        Assert.Contains("receiptsQuery = receiptsQuery.Where(x => allowedRoomIds.Contains(x.RoomId))", service);
        Assert.Contains("receiptsQuery = receiptsQuery.Where(x => x.CropYear == cropYear)", service);
        Assert.Contains("var canonicalVarietyFilter = await BuildCanonicalVarietyFilterAsync", service);
        Assert.Contains("varietyFilter.FruitProfileIds.Contains(x.FruitProfileId)", service);
        Assert.Contains("CurrentLotMatchesFilter(x, filter, canonicalVarietyFilter)", service);
        Assert.Contains("query = query.Where(x => allowedRoomIds.Contains(x.RoomId))", service);
        Assert.Contains("allowedRoomIds ?? (roomId is null ? null : [roomId.Value])", service);
    }

    [Fact]
    public void OrchardRecipientPages_AreInCentralPermissionAwareNavigation()
    {
        var recipients = ReadRepositoryFile("src", "CropQc.Web", "Views", "OrchardRecipients", "Index.cshtml");
        var import = ReadRepositoryFile("src", "CropQc.Web", "Views", "OrchardRecipientImports", "Index.cshtml");
        var details = ReadRepositoryFile("src", "CropQc.Web", "Views", "OrchardRecipientImports", "Details.cshtml");
        var navigation = ReadRepositoryFile("src", "CropQc.Web", "Services", "SiteNavigationService.cs");

        Assert.DoesNotContain("_MasterDataNavigation", recipients);
        Assert.Contains("Back to Master Data", recipients);
        Assert.DoesNotContain("_MasterDataNavigation", import);
        Assert.DoesNotContain("_MasterDataNavigation", details);
        Assert.Contains("ApplicationAreas.OrchardManagers", navigation);
        Assert.Contains("QC Recipients", navigation);
        Assert.Contains("Unmatched Identities", navigation);
    }

    [Fact]
    public void EbsHistoricalCleanup_IsAdminOnlyAndReadOnly()
    {
        var controller = ReadRepositoryFile("src", "CropQc.Web", "Controllers", "EbsInventoryCleanupController.cs");
        var view = ReadRepositoryFile("src", "CropQc.Web", "Views", "EbsInventoryCleanup", "Index.cshtml");
        var layout = ReadRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_Layout.cshtml");

        Assert.Contains("HistoricalInventoryCleanupAdmin", controller);
        Assert.Contains("[HttpGet(\"\")]", controller);
        Assert.DoesNotContain("[HttpPost", controller);
        Assert.DoesNotContain("EBS Historical Cleanup", layout);
        Assert.Contains("Historical, read-only review", view);
        Assert.DoesNotContain("<form", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Apply cleanup", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PresentationPages_DoNotExposeMutationForms()
    {
        var rooms = ReadRepositoryFile("src", "CropQc.Web", "Views", "Home", "Rooms.cshtml");
        var storage = ReadRepositoryFile("src", "CropQc.Web", "Views", "Home", "GrowerLots.cshtml");

        Assert.DoesNotContain("method=\"post\"", rooms, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("method=\"post\"", storage, StringComparison.OrdinalIgnoreCase);
    }

    private static RoomLotSummaryViewModel Lot(
        string growerNumber,
        string growerName,
        string variety,
        int bins,
        decimal? pressure,
        decimal? starch) =>
        new()
        {
            RoomId = 1,
            GrowerNumber = growerNumber,
            GrowerName = growerName,
            LotCode = $"{growerNumber}-{variety}",
            CanonicalVarietyKey = VarietyColorService.NormalizeIdentity(variety, variety).Key,
            CanonicalVarietyName = VarietyColorService.NormalizeIdentity(variety, variety).Name,
            ProductionType = "Fresh",
            IsOrganic = false,
            VarietyHexColor = VarietyColorService.FallbackColor(variety),
            CurrentBins = bins,
            AveragePressureLbs = pressure,
            AverageStarch = starch
        };

    private static T InvokeStatic<T>(Type type, string methodName, params object[] args)
    {
        var method = type.GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return Assert.IsAssignableFrom<T>(method.Invoke(null, args));
    }

    private static string ReadRepositoryFile(params string[] path) =>
        File.ReadAllText(FindRepositoryFile(path));

    private static int CountOccurrences(string value, string search)
    {
        var count = 0;
        var offset = 0;
        while ((offset = value.IndexOf(search, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += search.Length;
        }

        return count;
    }

    private static string FindRepositoryFile(params string[] path)
    {
        var current = AppContext.BaseDirectory;
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current }.Concat(path).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, path));
    }
}
