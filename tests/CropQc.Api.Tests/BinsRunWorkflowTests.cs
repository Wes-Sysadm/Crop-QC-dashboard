using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Storage;
using CropQc.Web.Auth;
using CropQc.Web.Controllers;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Data.Common;

namespace CropQc.Api.Tests;

public sealed class BinsRunWorkflowTests
{
    [Fact]
    public void BinsRun_IsTopLevelPermissionedNavigation()
    {
        var layout = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_Layout.cshtml"));
        var access = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "UserAccessService.cs"));
        var program = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Program.cs"));
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "BinsRun", "Index.cshtml"));
        var projectionMath = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "ProjectionDistributionMath.cs"));

        Assert.Contains("ApplicationAreas.BinsRun", access);
        Assert.Contains("AccessPolicyNames.BinsRunView", program);
        Assert.Contains("AccessPolicyNames.BinsRunEdit", program);
        Assert.Contains("AccessPolicyNames.BinsRunAdmin", program);
        Assert.Contains("canAccessBinsRun", layout);
        Assert.Contains("<a asp-controller=\"BinsRun\" asp-action=\"Index\" asp-route-facility=\"@facilityRouteValue\">Bins Run &amp; Transfers</a>", layout);
        Assert.DoesNotContain("/BinsRun@facilityQuery", layout);
        Assert.Contains("Select Room", view);
        Assert.Contains("Run Planner", view);
        Assert.Contains("Record Actual Run", view);
        Assert.Contains("Transfer Bins", view);
        Assert.Contains("True Up Inventory", view);
        Assert.Contains("Projected Fruit Sizing by Calculated Fruit Size", view);
        Assert.Contains("Projected Packed Boxes by Pack", view);
        Assert.Contains("32, 36, 40, 48, 56, 64, 72, 80, 88, 100, 113, 125, 138, 150, 163, 175, 198, 216", projectionMath);
        AssertActionPolicy<BinsRunController>(nameof(BinsRunController.Index), AccessPolicyNames.BinsRunView);
        AssertActionPolicy<BinsRunController>(nameof(BinsRunController.Projection), AccessPolicyNames.BinsRunView);
        AssertActionPolicy<BinsRunController>(nameof(BinsRunController.Create), AccessPolicyNames.BinsRunEdit);
        AssertActionPolicy<BinsRunController>(nameof(BinsRunController.Edit), AccessPolicyNames.BinsRunEdit);
        AssertActionPolicy<BinsRunController>(nameof(BinsRunController.Reverse), AccessPolicyNames.BinsRunAdmin);
        AssertActionPolicy<BinsRunController>(nameof(BinsRunController.CreateActualRun), AccessPolicyNames.BinsRunEdit);
        AssertActionPolicy<BinsRunController>(nameof(BinsRunController.UpdateActualRun), AccessPolicyNames.BinsRunEdit);
        AssertActionPolicy<BinsRunController>(nameof(BinsRunController.CancelActualRun), AccessPolicyNames.BinsRunAdmin);
        AssertActionPolicy<BinsRunController>(nameof(BinsRunController.ApproveActualRunOverride), AccessPolicyNames.BinsRunAdmin);
        AssertActionPolicy<BinsRunController>(nameof(BinsRunController.ReverseTransfer), AccessPolicyNames.TransfersAdmin);
        AssertActionPolicy<RoomInventoryController>(
            nameof(RoomInventoryController.Reconciliation),
            AccessPolicyNames.CurrentLotsAdmin);
    }

    [Fact]
    public async Task ViewOnlyUser_CanReviewButCannotCreate()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var viewOnly = Principal("viewer@fruitandland.com");

        var page = await service.GetPageAsync(new BinsRunFilterForm(), viewOnly, CancellationToken.None);
        var error = await service.CreateAsync(new BinsRunForm
        {
            InventoryKey = page.AvailableInventory[0].InventoryKey,
            BinsRun = 5,
            ExpectedAvailableBins = page.AvailableInventory[0].CurrentBins,
            RunAt = DateTimeOffset.UtcNow
        }, viewOnly, CancellationToken.None);

        Assert.False(page.CanRecord);
        Assert.NotNull((await service.GetPageAsync(new BinsRunFilterForm { RoomId = 1001 }, viewOnly, CancellationToken.None)).RoomSummary);
        Assert.Equal("Bins Run Edit access is required to record bins run.", error);
        Assert.Empty(await db.BinsRunEntries.ToListAsync());
    }

    [Fact]
    public async Task RoomSummaryAndLotSubmenu_UseOnlyCurrentAvailableInventoryForSelectedRoom()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var page = await CreateService(db).GetPageAsync(new BinsRunFilterForm { RoomId = 1001 }, Principal("manager@fruitandland.com"), CancellationToken.None);

        Assert.NotNull(page.RoomSummary);
        Assert.Equal("Evans-12", page.RoomSummary!.RoomName);
        Assert.Equal("EBS", page.RoomSummary.Facility);
        Assert.Equal(190, page.RoomSummary.TotalAvailableBins);
        Assert.Equal(3, page.RoomSummary.ActiveLotCount);
        Assert.All(page.AvailableInventory, x => Assert.Equal(1001, x.RoomId));
        Assert.Contains(page.AvailableInventory, x => x.Lot == "LOT-120" && x.CurrentBins == 120);
        Assert.Contains(page.AvailableInventory, x => x.Lot == "LOT-30" && x.CurrentBins == 30);
        Assert.Contains(page.AvailableInventory, x => x.Lot == "HISTORY" && x.CurrentBins == 40);
        Assert.DoesNotContain(page.AvailableInventory, x => x.Lot == "LOT-ZERO");
        Assert.DoesNotContain(page.AvailableInventory, x => x.RoomId == 1002);
    }

    [Fact]
    public async Task RoomSummary_WeightsSizingAndGradeByCurrentAvailableBins()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var summary = (await CreateService(db).GetPageAsync(new BinsRunFilterForm { RoomId = 1001 }, Principal("manager@fruitandland.com"), CancellationToken.None)).RoomSummary!;

        Assert.Equal(32, summary.SizeDistribution.First().Size);
        Assert.Equal(216, summary.SizeDistribution.Last().Size);
        Assert.Equal(40m, summary.SizeDistribution.Single(x => x.Size == 80).Percentage);
        Assert.Equal(60m, summary.SizeDistribution.Single(x => x.Size == 100).Percentage);
        Assert.Equal(100m, summary.SizeDistribution.Sum(x => x.Percentage));
        Assert.Equal(60m, summary.GradeSummary.Single(x => x.Grade == "W1").EstimatedBins);
        Assert.Equal(90m, summary.GradeSummary.Single(x => x.Grade == "W2").EstimatedBins);
        Assert.Equal(2, summary.SizeDataLotCount);
        Assert.Equal(2, summary.GradeDataLotCount);
        Assert.Equal(190, summary.Projection.AvailableBins);
        Assert.Equal(150, summary.Projection.SizeRepresentedBins);
        Assert.Equal(40, summary.Projection.SizeMissingBins);
        Assert.Equal(150, summary.Projection.GradeRepresentedBins);
        Assert.Equal(40, summary.Projection.GradeMissingBins);
    }

    [Theory]
    [InlineData(10, 4, 40)]
    [InlineData(25, 10, 40)]
    [InlineData(50, 20, 40)]
    public void SizeProjection_ConvertsSupportedSampleSizesToPercentages(int sampleSize, int size80Count, decimal expectedSize80Percent)
    {
        var readings = Enumerable.Range(1, sampleSize)
            .Select(row => new QcFruitReading
            {
                RowNumber = row,
                SizeCategory = row <= size80Count ? 80 : 100,
                SizeStatus = "Sized"
            })
            .ToList();

        var distribution = ProjectionDistributionMath.BuildSizePercentages(readings);

        Assert.Equal(sampleSize, distribution.DenominatorFruitCount);
        Assert.Equal(expectedSize80Percent / 100m, distribution.Percentages[80]);
        Assert.Equal(100m, decimal.Round(distribution.Percentages.Values.Sum() * 100m, 2));
    }

    [Fact]
    public void SizeProjection_UsesEnteredFruitRowsAsDenominatorForIncompleteSamples()
    {
        var readings = Enumerable.Range(1, 10)
            .Select(row => new QcFruitReading
            {
                RowNumber = row,
                SizeCategory = row <= 6 ? 80 : row <= 8 ? 100 : null,
                WeightGrams = row > 8 ? 180 : null,
                SizeStatus = row <= 8 ? "Sized" : "NotCalculated"
            })
            .ToList();

        var distribution = ProjectionDistributionMath.BuildSizePercentages(readings);

        Assert.Equal(10, distribution.DenominatorFruitCount);
        Assert.Equal(8, distribution.ClassifiedFruitCount);
        Assert.Equal(2, distribution.UnclassifiedFruitCount);
        Assert.Equal(60m, distribution.Percentages[80] * 100m);
        Assert.Equal(20m, decimal.Round(distribution.UnclassifiedPercentage * 100m, 2));
    }

    [Fact]
    public void SizeProjection_CombinesLotsUsingCurrentBinWeightedPercentages()
    {
        var lots = new[]
        {
            new { Key = "A", Bins = 300 },
            new { Key = "B", Bins = 100 }
        };
        var sampleData = new Dictionary<string, SizeSampleDistribution>
        {
            ["A"] = new(new Dictionary<int, decimal> { [72] = 0.8m, [80] = 0.2m }, 10, 10, 0),
            ["B"] = new(new Dictionary<int, decimal> { [72] = 0.2m, [80] = 0.8m }, 50, 50, 0)
        };

        var points = ProjectionDistributionMath.CombineWeightedSizePercentages(lots, sampleData, lot => lot.Key, lot => lot.Bins);

        Assert.Equal(65m, points.Single(x => x.Size == 72).Percentage);
        Assert.Equal(35m, points.Single(x => x.Size == 80).Percentage);
        Assert.Equal(100m, points.Sum(x => x.Percentage));
    }

    [Fact]
    public void SizeProjection_DifferingSampleSizesDoNotReceiveExtraWeight()
    {
        var lots = new[]
        {
            new { Key = "TEN", Bins = 100 },
            new { Key = "FIFTY", Bins = 100 }
        };
        var sampleData = new Dictionary<string, SizeSampleDistribution>
        {
            ["TEN"] = ProjectionDistributionMath.BuildSizePercentages(Enumerable.Range(1, 10).Select(row => new QcFruitReading { RowNumber = row, SizeCategory = row <= 8 ? 72 : 80, SizeStatus = "Sized" })),
            ["FIFTY"] = ProjectionDistributionMath.BuildSizePercentages(Enumerable.Range(1, 50).Select(row => new QcFruitReading { RowNumber = row, SizeCategory = row <= 10 ? 72 : 80, SizeStatus = "Sized" }))
        };

        var points = ProjectionDistributionMath.CombineWeightedSizePercentages(lots, sampleData, lot => lot.Key, lot => lot.Bins);

        Assert.Equal(50m, points.Single(x => x.Size == 72).Percentage);
        Assert.Equal(50m, points.Single(x => x.Size == 80).Percentage);
    }

    [Fact]
    public async Task Projection_DefaultsToWholeRoomWhenNoLotsAreSelected()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var projection = await CreateService(db).GetProjectionAsync(new BinsRunProjectionRequest { RoomId = 1001 }, Principal("viewer@fruitandland.com"), CancellationToken.None);

        Assert.False(projection.IsSelection);
        Assert.Equal("Room summary", projection.Label);
        Assert.Equal(3, projection.LotCount);
        Assert.Equal(190, projection.AvailableBins);
        Assert.Equal(40m, projection.SizeDistribution.Single(x => x.Size == 80).Percentage);
        Assert.Equal(60m, projection.SizeDistribution.Single(x => x.Size == 100).Percentage);
        Assert.Equal(60m, projection.GradeSummary.Single(x => x.Grade == "W1").EstimatedBins);
        Assert.Equal(90m, projection.GradeSummary.Single(x => x.Grade == "W2").EstimatedBins);
    }

    [Fact]
    public async Task Projection_SelectingOneLotReturnsOnlyThatLot()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var lot = (await service.GetPageAsync(new BinsRunFilterForm { RoomId = 1001 }, Principal("viewer@fruitandland.com"), CancellationToken.None))
            .AvailableInventory.Single(x => x.Lot == "LOT-120");

        var projection = await service.GetProjectionAsync(new BinsRunProjectionRequest { RoomId = 1001, InventoryKeys = [lot.InventoryKey] }, Principal("viewer@fruitandland.com"), CancellationToken.None);

        Assert.True(projection.IsSelection);
        Assert.Equal(1, projection.LotCount);
        Assert.Equal(120, projection.AvailableBins);
        Assert.Equal(32, projection.SizeDistribution.First().Size);
        Assert.Equal(216, projection.SizeDistribution.Last().Size);
        Assert.Equal(50m, projection.SizeDistribution.Single(x => x.Size == 80).Percentage);
        Assert.Equal(50m, projection.SizeDistribution.Single(x => x.Size == 100).Percentage);
        Assert.Equal(60m, projection.GradeSummary.Single(x => x.Grade == "W1").EstimatedBins);
        Assert.Equal(60m, projection.GradeSummary.Single(x => x.Grade == "W2").EstimatedBins);
        Assert.Equal(120, projection.SizeRepresentedBins);
        Assert.Equal(0, projection.SizeMissingBins);
    }

    [Fact]
    public async Task Projection_SelectingMultipleLotsCombinesWeightedSelectedLotsOnly()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var page = await service.GetPageAsync(new BinsRunFilterForm { RoomId = 1001 }, Principal("viewer@fruitandland.com"), CancellationToken.None);
        var lot120 = page.AvailableInventory.Single(x => x.Lot == "LOT-120");
        var lot30 = page.AvailableInventory.Single(x => x.Lot == "LOT-30");
        var unselectedHistory = page.AvailableInventory.Single(x => x.Lot == "HISTORY");

        var projection = await service.GetProjectionAsync(new BinsRunProjectionRequest
        {
            RoomId = 1001,
            InventoryKeys = [lot120.InventoryKey, lot30.InventoryKey]
        }, Principal("viewer@fruitandland.com"), CancellationToken.None);

        Assert.Equal(2, projection.LotCount);
        Assert.Equal(150, projection.AvailableBins);
        Assert.Equal(40m, projection.SizeDistribution.Single(x => x.Size == 80).Percentage);
        Assert.Equal(60m, projection.SizeDistribution.Single(x => x.Size == 100).Percentage);
        Assert.Equal(60m, projection.GradeSummary.Single(x => x.Grade == "W1").EstimatedBins);
        Assert.Equal(90m, projection.GradeSummary.Single(x => x.Grade == "W2").EstimatedBins);
        Assert.DoesNotContain(page.AvailableInventory.Where(x => x.InventoryKey != unselectedHistory.InventoryKey), x => x.InventoryKey == unselectedHistory.InventoryKey);
        Assert.Equal(150, projection.SizeRepresentedBins);
        Assert.Equal(0, projection.SizeMissingBins);
        Assert.Empty(await db.BinsRunEntries.ToListAsync());
        Assert.DoesNotContain(await db.RoomInventoryAdjustments.ToListAsync(), x => x.AdjustmentType == BinsRunService.AdjustmentType);
    }

    [Fact]
    public async Task Projection_ReportsMissingSizingAndGradeBinsSeparately()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var page = await service.GetPageAsync(new BinsRunFilterForm { RoomId = 1001 }, Principal("viewer@fruitandland.com"), CancellationToken.None);
        var lot120 = page.AvailableInventory.Single(x => x.Lot == "LOT-120");
        var history = page.AvailableInventory.Single(x => x.Lot == "HISTORY");

        var projection = await service.GetProjectionAsync(new BinsRunProjectionRequest
        {
            RoomId = 1001,
            InventoryKeys = [lot120.InventoryKey, history.InventoryKey]
        }, Principal("viewer@fruitandland.com"), CancellationToken.None);

        Assert.Equal(160, projection.AvailableBins);
        Assert.Equal(120, projection.SizeRepresentedBins);
        Assert.Equal(40, projection.SizeMissingBins);
        Assert.Equal(75m, projection.SizeCoveragePercent);
        Assert.Equal(120, projection.GradeRepresentedBins);
        Assert.Equal(40, projection.GradeMissingBins);
        Assert.DoesNotContain(projection.SizeDistribution, x => x.Size == 80 && x.Percentage == 0);
    }

    [Fact]
    public async Task Projection_ApiShapeDoesNotExposeRawSizeCounts()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);

        var projection = await CreateService(db).GetProjectionAsync(new BinsRunProjectionRequest { RoomId = 1001 }, Principal("viewer@fruitandland.com"), CancellationToken.None);
        var json = JsonSerializer.Serialize(projection, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        Assert.Contains("\"percentage\"", json);
        Assert.DoesNotContain("\"estimatedBins\"", JsonSerializer.Serialize(projection.SizeDistribution, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    [Fact]
    public async Task Projection_RejectsOtherRoomAndDepletedInventory()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var otherRoomLot = (await service.GetPageAsync(new BinsRunFilterForm { RoomId = 1002 }, Principal("viewer@fruitandland.com"), CancellationToken.None))
            .AvailableInventory.Single(x => x.Lot == "LOT-OTHER");

        var otherRoomError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetProjectionAsync(new BinsRunProjectionRequest { RoomId = 1001, InventoryKeys = [otherRoomLot.InventoryKey] }, Principal("viewer@fruitandland.com"), CancellationToken.None));
        var depletedError = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetProjectionAsync(new BinsRunProjectionRequest { RoomId = 1001, InventoryKeys = ["A:8002:1001|LOT-ZERO|FUJI"] }, Principal("viewer@fruitandland.com"), CancellationToken.None));

        Assert.Equal("Selected inventory is not available in this room.", otherRoomError.Message);
        Assert.Equal("Selected inventory is not available in this room.", depletedError.Message);
    }

    [Fact]
    public async Task MissingSizingAndGradeData_ProduceEmptySummaryStates()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        db.QcFruitReadings.RemoveRange(db.QcFruitReadings);
        await db.SaveChangesAsync();

        var summary = (await CreateService(db).GetPageAsync(new BinsRunFilterForm { RoomId = 1001 }, Principal("viewer@fruitandland.com"), CancellationToken.None)).RoomSummary!;

        Assert.Empty(summary.SizeDistribution);
        Assert.Empty(summary.GradeSummary);
        Assert.Equal(0, summary.SizeDataLotCount);
        Assert.Equal(0, summary.GradeDataLotCount);
    }

    [Fact]
    public async Task CreatingBinsRun_ReducesAvailableQuantityAndAuditsWithoutChangingHistory()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var user = Principal("manager@fruitandland.com");
        var option = (await service.GetPageAsync(new BinsRunFilterForm { RoomId = 1001 }, user, CancellationToken.None))
            .AvailableInventory.Single(x => x.Lot == "LOT-120");
        var projection = ProjectionForActual(option, 1000);
        db.RunProjections.Add(projection);
        await db.SaveChangesAsync();
        var form = ActualRunForm(option, projection);
        form.BinsRun = 30;
        form.RunAt = DateTimeOffset.Parse("2026-07-10T08:00:00-07:00");
        form.Notes = "Packing line run";

        var error = await service.CreateAsync(form, user, CancellationToken.None);

        Assert.Null(error);
        var refreshed = await service.GetPageAsync(new BinsRunFilterForm { RoomId = 1001 }, user, CancellationToken.None);
        Assert.Equal(90, refreshed.AvailableInventory.Single(x => x.Lot == "LOT-120").CurrentBins);
        Assert.Equal(160, refreshed.RoomSummary!.TotalAvailableBins);
        var entry = Assert.Single(await db.BinsRunEntries.ToListAsync());
        Assert.Equal(120, entry.PreviousAvailableBins);
        Assert.Equal(30, entry.BinsRun);
        Assert.Equal(90, entry.NewAvailableBins);
        Assert.Contains(await db.AuditLogs.ToListAsync(), x => x.Action == "Create" && x.EntityName == nameof(BinsRunEntry));
        Assert.Equal(40, (await db.Receipts.SingleAsync(x => x.Id == 7001)).BinCount);
        Assert.Equal(1, await db.QcSamples.CountAsync(x => x.ReceiptId == 7001));
    }

    [Fact]
    public async Task BinsRun_CannotExceedAvailableOrUseStaleExpectedQuantity()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var user = Principal("manager@fruitandland.com");
        var option = (await service.GetPageAsync(new BinsRunFilterForm { RoomId = 1001 }, user, CancellationToken.None))
            .AvailableInventory.Single(x => x.Lot == "LOT-120");
        var firstProjection = ProjectionForActual(option, 1000);
        var staleProjection = ProjectionForActual(option, 1000);
        db.RunProjections.AddRange(firstProjection, staleProjection);
        await db.SaveChangesAsync();
        var tooManyForm = ActualRunForm(option, firstProjection);
        tooManyForm.BinsRun = 121;
        var firstForm = ActualRunForm(option, firstProjection);
        var staleForm = ActualRunForm(option, staleProjection);

        var tooMany = await service.CreateAsync(tooManyForm, user, CancellationToken.None);
        var first = await service.CreateAsync(firstForm, user, CancellationToken.None);
        var stale = await service.CreateAsync(staleForm, user, CancellationToken.None);

        Assert.Contains("only 120 bins", tooMany);
        Assert.Null(first);
        Assert.Contains("Available quantity changed before save", stale);
        Assert.DoesNotContain(await db.RoomInventoryAdjustments.ToListAsync(), x => x.NewBinCount < 0);
    }

    [Fact]
    public async Task DeletedProjection_CannotCreateActualRunOrMutateInventory()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var user = Principal("manager@fruitandland.com");
        var option = (await service.GetPageAsync(new BinsRunFilterForm { RoomId = 1001 }, user, CancellationToken.None))
            .AvailableInventory.Single(x => x.Lot == "LOT-120");
        var projection = ProjectionForActual(option, 1000);
        projection.IsDeleted = true;
        db.RunProjections.Add(projection);
        await db.SaveChangesAsync();
        var baselineAdjustments = await db.RoomInventoryAdjustments.CountAsync();

        var error = await service.CreateAsync(ActualRunForm(option, projection), user, CancellationToken.None);

        Assert.Contains("deleted projection", error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(baselineAdjustments, await db.RoomInventoryAdjustments.CountAsync());
        Assert.Empty(await db.BinsRunEntries.ToListAsync());
    }

    [Fact]
    public async Task ProjectionFacilityMustMatchActualRunInventory()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var user = Principal("manager@fruitandland.com");
        var option = (await service.GetPageAsync(new BinsRunFilterForm { RoomId = 1001 }, user, CancellationToken.None))
            .AvailableInventory.Single(x => x.Lot == "LOT-120");
        var projection = ProjectionForActual(option, 4);
        db.RunProjections.Add(projection);
        await db.SaveChangesAsync();
        var baselineAdjustments = await db.RoomInventoryAdjustments.CountAsync();

        var error = await service.CreateAsync(ActualRunForm(option, projection), user, CancellationToken.None);

        Assert.Contains("assigned WP or EBS facility", error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(baselineAdjustments, await db.RoomInventoryAdjustments.CountAsync());
        Assert.Empty(await db.BinsRunEntries.ToListAsync());
    }

    [Fact]
    public async Task EditingAndReversing_AdjustsAndRestoresInventory()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var user = Principal("manager@fruitandland.com");
        var admin = Principal("admin@fruitandland.com");
        var option = (await service.GetPageAsync(new BinsRunFilterForm { RoomId = 1001 }, user, CancellationToken.None))
            .AvailableInventory.Single(x => x.Lot == "LOT-120");
        var projection = ProjectionForActual(option, 1000);
        db.RunProjections.Add(projection);
        await db.SaveChangesAsync();
        var createForm = ActualRunForm(option, projection);
        createForm.BinsRun = 30;
        await service.CreateAsync(createForm, user, CancellationToken.None);
        var entry = await db.BinsRunEntries.SingleAsync();
        var currentOption = (await service.GetPageAsync(new BinsRunFilterForm { RoomId = 1001 }, user, CancellationToken.None))
            .AvailableInventory.Single(x => x.Lot == "LOT-120");

        var editError = await service.UpdateAsync(entry.Id, new BinsRunForm
        {
            InventoryKey = currentOption.InventoryKey,
            BinsRun = 45,
            RunAt = DateTimeOffset.UtcNow
        }, user, CancellationToken.None);
        var afterEdit = await service.GetPageAsync(new BinsRunFilterForm { RoomId = 1001 }, user, CancellationToken.None);
        var replacement = await db.BinsRunEntries
            .SingleAsync(x => x.Id != entry.Id
                && x.TransactionType != ActualRunTransactionTypes.Reversal
                && !x.IsReversed);
        var reversalStartedAt = DateTimeOffset.UtcNow;
        var reverseError = await service.ReverseAsync(new ReverseBinsRunForm { Id = replacement.Id, Reason = "Correction" }, admin, CancellationToken.None);
        var afterReverse = await service.GetPageAsync(new BinsRunFilterForm { RoomId = 1001 }, user, CancellationToken.None);

        Assert.Null(editError);
        Assert.Equal(75, afterEdit.AvailableInventory.Single(x => x.Lot == "LOT-120").CurrentBins);
        Assert.Equal(145, afterEdit.RoomSummary!.TotalAvailableBins);
        Assert.Null(reverseError);
        Assert.Equal(120, afterReverse.AvailableInventory.Single(x => x.Lot == "LOT-120").CurrentBins);
        Assert.Equal(190, afterReverse.RoomSummary!.TotalAvailableBins);
        Assert.Contains(await db.AuditLogs.ToListAsync(), x => x.Action == "Update" && x.EntityName == nameof(BinsRunEntry));
        Assert.Contains(await db.AuditLogs.ToListAsync(), x => x.Action == "Reverse" && x.EntityName == nameof(BinsRunEntry));
        Assert.True((await db.BinsRunEntries.SingleAsync(x => x.Id == entry.Id)).IsReversed);
        Assert.True((await db.BinsRunEntries.SingleAsync(x => x.Id == replacement.Id)).IsReversed);
        Assert.Equal(2, await db.BinsRunEntries.CountAsync(x => x.TransactionType == ActualRunTransactionTypes.Reversal));
        var directReversal = await db.BinsRunEntries
            .Include(x => x.InventoryAdjustment)
            .SingleAsync(x => x.ReversesBinsRunEntryId == replacement.Id);
        Assert.True(directReversal.InventoryAdjustment.AdjustmentAt >= reversalStartedAt);
    }

    [Fact]
    public void ActualRunMigration_IsAdditiveAndProviderCompatible()
    {
        var migration = File.ReadAllText(FindRepositoryFile(
            "src",
            "CropQc.Data",
            "Migrations",
            "20260729230451_AddActualRunRoomInventoryLedger.cs"));

        Assert.Contains("MigrationProviderTypes.StoreType", migration);
        Assert.Contains("NpgsqlValueGenerationStrategy.IdentityByDefaultColumn", migration);
        Assert.DoesNotContain("migrationBuilder.DropTable", migration[..migration.IndexOf("protected override void Down", StringComparison.Ordinal)]);
        Assert.DoesNotContain("migrationBuilder.DropColumn", migration[..migration.IndexOf("protected override void Down", StringComparison.Ordinal)]);
    }

    [Fact]
    public async Task ActualRun_MultipleRoomsLotsAndSameLot_DepletesLedgerAtomicallyAndIsIdempotent()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var user = Principal("manager@fruitandland.com");
        var page = await service.GetPageAsync(new BinsRunFilterForm
        {
            Section = "Actual",
            RoomIds = [1001, 1002]
        }, user, CancellationToken.None);
        var roomOneLot120 = page.AvailableInventory.Single(x => x.RoomId == 1001 && x.Lot == "LOT-120");
        var roomTwoLot120 = page.AvailableInventory.Single(x => x.RoomId == 1002 && x.Lot == "LOT-120");
        var roomOneLot30 = page.AvailableInventory.Single(x => x.RoomId == 1001 && x.Lot == "LOT-30");
        var receiptCount = await db.Receipts.CountAsync();
        var form = GroupForm(
            (roomOneLot120, 40),
            (roomTwoLot120, 25),
            (roomOneLot30, 10));

        var error = await service.CreateActualRunAsync(form, user, CancellationToken.None);
        var firstEntryCount = await db.BinsRunEntries.CountAsync();
        var duplicateError = await service.CreateActualRunAsync(form, user, CancellationToken.None);

        Assert.Null(error);
        Assert.Null(duplicateError);
        var run = await db.ActualRuns.SingleAsync();
        Assert.Equal(ActualRunStatuses.Active, run.Status);
        Assert.Equal(1, run.CurrentRevisionNumber);
        Assert.Equal(3, firstEntryCount);
        Assert.Equal(firstEntryCount, await db.BinsRunEntries.CountAsync());
        var entries = await db.BinsRunEntries.OrderBy(x => x.RoomId).ThenBy(x => x.LotNumber).ToListAsync();
        Assert.All(entries, x =>
        {
            Assert.Null(x.ReceiptId);
            Assert.Equal(ActualRunTransactionTypes.Depletion, x.TransactionType);
            Assert.Equal(run.Id, x.ActualRunId);
        });
        Assert.Equal(3, await db.RoomInventoryAdjustments.CountAsync(x => x.ActualRunId == run.Id));
        var ledgerAfterSave = await new RoomInventoryLedgerQueryService(db)
            .GetSnapshotsAsync(1000, [1001, 1002], CancellationToken.None);
        Assert.Equal(80, ledgerAfterSave.Single(x => x.RoomId == 1001 && x.Lot == "LOT-120").CurrentBins);
        Assert.Equal(-40, ledgerAfterSave.Single(x => x.RoomId == 1001 && x.Lot == "LOT-120").NegativeBins);
        var refreshed = await service.GetPageAsync(new BinsRunFilterForm { Section = "Actual", RoomIds = [1001, 1002] }, user, CancellationToken.None);
        Assert.Equal(80, refreshed.AvailableInventory.Single(x => x.RoomId == 1001 && x.Lot == "LOT-120").CurrentBins);
        Assert.Equal(0, refreshed.AvailableInventory.Single(x => x.RoomId == 1002 && x.Lot == "LOT-120").CurrentBins);
        Assert.Equal(20, refreshed.AvailableInventory.Single(x => x.RoomId == 1001 && x.Lot == "LOT-30").CurrentBins);
        Assert.Equal(receiptCount, await db.Receipts.CountAsync());
    }

    [Fact]
    public async Task ActualRun_EditUsesReversalsThenCancelRestoresEveryRoom()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var manager = Principal("manager@fruitandland.com");
        var admin = Principal("admin@fruitandland.com");
        var initialPage = await service.GetPageAsync(new BinsRunFilterForm { Section = "Actual", RoomIds = [1001, 1002] }, manager, CancellationToken.None);
        var roomOne = initialPage.AvailableInventory.Single(x => x.RoomId == 1001 && x.Lot == "LOT-120");
        var roomTwo = initialPage.AvailableInventory.Single(x => x.RoomId == 1002 && x.Lot == "LOT-120");
        await service.CreateActualRunAsync(GroupForm((roomOne, 40)), manager, CancellationToken.None);
        var run = await db.ActualRuns.SingleAsync();
        var originalEntry = await db.BinsRunEntries.SingleAsync();
        var edit = GroupForm((roomOne, 30), (roomTwo, 10));
        edit.Id = run.Id;
        edit.ConcurrencyVersion = run.ConcurrencyVersion;

        var editError = await service.UpdateActualRunAsync(run.Id, edit, manager, CancellationToken.None);

        Assert.Null(editError);
        await db.Entry(run).ReloadAsync();
        Assert.Equal(2, run.CurrentRevisionNumber);
        Assert.Equal(2, run.ConcurrencyVersion);
        Assert.True((await db.BinsRunEntries.SingleAsync(x => x.Id == originalEntry.Id)).IsReversed);
        var reversal = await db.BinsRunEntries.SingleAsync(x => x.ReversesBinsRunEntryId == originalEntry.Id);
        Assert.Equal(ActualRunTransactionTypes.Reversal, reversal.TransactionType);
        Assert.Equal(2, await db.BinsRunEntries.CountAsync(x => x.ActualRunId == run.Id && x.TransactionType == ActualRunTransactionTypes.Depletion && !x.IsReversed));
        var afterEdit = await service.GetPageAsync(new BinsRunFilterForm { Section = "Actual", RoomIds = [1001, 1002] }, manager, CancellationToken.None);
        Assert.Equal(90, afterEdit.AvailableInventory.Single(x => x.RoomId == 1001 && x.Lot == "LOT-120").CurrentBins);
        Assert.Equal(15, afterEdit.AvailableInventory.Single(x => x.RoomId == 1002 && x.Lot == "LOT-120").CurrentBins);

        var stale = GroupForm((roomOne, 20));
        stale.Id = run.Id;
        stale.ConcurrencyVersion = 1;
        Assert.Contains("Conflict detected", await service.UpdateActualRunAsync(run.Id, stale, manager, CancellationToken.None));

        var cancel = new CancelActualRunForm
        {
            Id = run.Id,
            ConcurrencyVersion = run.ConcurrencyVersion,
            OperationKey = Guid.NewGuid().ToString("N"),
            Reason = "Run canceled before packout"
        };
        var cancelError = await service.CancelActualRunAsync(cancel, admin, CancellationToken.None);
        var entriesAfterCancel = await db.BinsRunEntries.CountAsync();
        var repeatedCancel = await service.CancelActualRunAsync(cancel, admin, CancellationToken.None);

        Assert.Null(cancelError);
        Assert.Null(repeatedCancel);
        await db.Entry(run).ReloadAsync();
        Assert.Equal(ActualRunStatuses.Canceled, run.Status);
        Assert.Equal("Run canceled before packout", run.CancellationReason);
        Assert.Equal(entriesAfterCancel, await db.BinsRunEntries.CountAsync());
        var restored = await service.GetPageAsync(new BinsRunFilterForm { Section = "Actual", RoomIds = [1001, 1002] }, manager, CancellationToken.None);
        Assert.Equal(120, restored.AvailableInventory.Single(x => x.RoomId == 1001 && x.Lot == "LOT-120").CurrentBins);
        Assert.Equal(25, restored.AvailableInventory.Single(x => x.RoomId == 1002 && x.Lot == "LOT-120").CurrentBins);
        Assert.Contains(await db.AuditLogs.ToListAsync(), x => x.Action == "Cancel" && x.EntityName == nameof(ActualRun));
    }

    [Fact]
    public async Task ActualRun_OverdrawRequiresDifferentAdministratorAndPersistsOverrideAudit()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var manager = Principal("manager@fruitandland.com");
        var admin = Principal("admin@fruitandland.com");
        var option = (await service.GetPageAsync(new BinsRunFilterForm { Section = "Actual", RoomIds = [1001] }, manager, CancellationToken.None))
            .AvailableInventory.Single(x => x.Lot == "LOT-30");
        var form = GroupForm((option, 35));
        var baselineAdjustmentCount = await db.RoomInventoryAdjustments.CountAsync();

        var requestMessage = await service.CreateActualRunAsync(form, manager, CancellationToken.None);
        var request = await db.ActualRunOverrideRequests.Include(x => x.Lines).SingleAsync();

        Assert.Contains("pending approval", requestMessage);
        Assert.Empty(await db.ActualRuns.ToListAsync());
        Assert.Equal(baselineAdjustmentCount, await db.RoomInventoryAdjustments.CountAsync());
        Assert.Equal(30, request.Lines.Single().AvailableBins);
        Assert.Equal(35, request.Lines.Single().RequestedBins);
        Assert.Equal(5, request.Lines.Single().ShortageBins);
        Assert.Contains("Admin access", await service.ApproveActualRunOverrideAsync(new ApproveActualRunOverrideForm { RequestId = request.Id, Reason = "Not authorized" }, manager, CancellationToken.None));

        var approvalError = await service.ApproveActualRunOverrideAsync(
            new ApproveActualRunOverrideForm { RequestId = request.Id, Reason = "Approved for verified physical pull" },
            admin,
            CancellationToken.None);

        Assert.Null(approvalError);
        var entry = await db.BinsRunEntries.SingleAsync();
        Assert.True(entry.IsOverdrawOverride);
        Assert.Equal(30, entry.OverrideAvailableBins);
        Assert.Equal(35, entry.OverrideRequestedBins);
        Assert.Equal(5, entry.OverrideShortageBins);
        Assert.Equal("Approved for verified physical pull", entry.OverrideReason);
        Assert.Equal(1000, entry.OverrideApprovedByUserId);
        Assert.Equal(-5, entry.NewAvailableBins);
        Assert.Equal(ActualRunOverrideStatuses.Approved, (await db.ActualRunOverrideRequests.SingleAsync()).Status);
        Assert.Contains(await db.AuditLogs.ToListAsync(), x => x.Action == "OverdrawAttempt");
    }

    [Fact]
    public async Task ActualRun_RequesterCannotApproveOwnOverdraw()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var admin = Principal("admin@fruitandland.com");
        var option = (await service.GetPageAsync(new BinsRunFilterForm { Section = "Actual", RoomIds = [1001] }, admin, CancellationToken.None))
            .AvailableInventory.Single(x => x.Lot == "LOT-30");
        await service.CreateActualRunAsync(GroupForm((option, 31)), admin, CancellationToken.None);
        var request = await db.ActualRunOverrideRequests.SingleAsync();

        var error = await service.ApproveActualRunOverrideAsync(
            new ApproveActualRunOverrideForm { RequestId = request.Id, Reason = "Self approval" },
            admin,
            CancellationToken.None);

        Assert.Contains("cannot approve their own", error);
        Assert.Empty(await db.ActualRuns.ToListAsync());
    }

    [Fact]
    public async Task ActualRun_ReceiptOnlyLotIsNotInventoryAndInvalidRowDoesNotPartiallySave()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var warehouse = await db.Warehouses.SingleAsync(x => x.Id == 1000);
        var room = await db.Rooms.SingleAsync(x => x.Id == 1001);
        var fruit = await db.FruitProfiles.SingleAsync(x => x.Id == 1000);
        db.Receipts.Add(new Receipt
        {
            Id = 7999,
            CropYear = 2026,
            ReceivedAt = DateTimeOffset.UtcNow,
            CompuTechReceiptId = "RECEIPT-ONLY",
            ReceiptType = "Truck receipt",
            Warehouse = warehouse,
            Room = room,
            FruitProfile = fruit,
            GrowerName = "Receipt Only Grower",
            LotCode = "RECEIPT-ONLY",
            BinCount = 999,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var user = Principal("manager@fruitandland.com");
        var page = await service.GetPageAsync(new BinsRunFilterForm { Section = "Actual", RoomIds = [1001] }, user, CancellationToken.None);
        Assert.DoesNotContain(page.AvailableInventory, x => x.Lot == "RECEIPT-ONLY");
        var valid = page.AvailableInventory.Single(x => x.Lot == "LOT-120");
        var form = GroupForm((valid, 10));
        form.Lines.Add(new ActualRunLineForm { InventoryKey = "R:7999", BinsRun = 5, ExpectedAvailableBins = 999 });
        var adjustmentCount = await db.RoomInventoryAdjustments.CountAsync();

        var error = await service.CreateActualRunAsync(form, user, CancellationToken.None);

        Assert.Contains("not room-ledger inventory", error);
        Assert.Empty(await db.ActualRuns.ToListAsync());
        Assert.Empty(await db.BinsRunEntries.ToListAsync());
        Assert.Equal(adjustmentCount, await db.RoomInventoryAdjustments.CountAsync());
        Assert.Equal(999, (await db.Receipts.SingleAsync(x => x.Id == 7999)).BinCount);
    }

    [Fact]
    public async Task ActualRun_ViewOnlyUserCannotCreateAndLegacyHistoryRemainsReadable()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var viewer = Principal("viewer@fruitandland.com");
        var option = (await service.GetPageAsync(new BinsRunFilterForm { Section = "Actual", RoomIds = [1001] }, viewer, CancellationToken.None))
            .AvailableInventory.Single(x => x.Lot == "LOT-120");

        var error = await service.CreateActualRunAsync(GroupForm((option, 1)), viewer, CancellationToken.None);

        Assert.Contains("Create access", error);
        Assert.Empty(await db.ActualRuns.ToListAsync());
        Assert.NotEmpty((await service.GetPageAsync(new BinsRunFilterForm { RoomId = 1001 }, viewer, CancellationToken.None)).AvailableInventory);
    }

    [Fact]
    public async Task RoomTransfer_ReversalRestoresInventoryExactlyOnce()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var managerService = CreateDashboardService(db, Principal("manager@fruitandland.com"));
        var sourceLot = (await managerService.GetRoomDetailAsync(1001, CancellationToken.None))
            .TransferLotOptions.Single(x => x.Label.Contains("LOT-120", StringComparison.OrdinalIgnoreCase));
        var transferForm = new RoomTransferForm
        {
            OperationKey = Guid.NewGuid().ToString("N"),
            FromRoomId = 1001,
            ToRoomId = 1002,
            SourceLotKey = sourceLot.LotKey,
            BinCount = 10,
            TransferAt = DateTimeOffset.UtcNow,
            Reason = "Unit-test transfer"
        };

        Assert.Null(await managerService.CreateRoomTransferAsync(transferForm, CancellationToken.None));
        Assert.Null(await managerService.CreateRoomTransferAsync(transferForm, CancellationToken.None));
        var original = await db.RoomTransfers.SingleAsync(x => x.ReversesRoomTransferId == null);
        Assert.Equal(110, await LedgerBalanceAsync(db, 1001, "LOT-120"));
        Assert.Equal(35, await LedgerBalanceAsync(db, 1002, "LOT-120"));

        var reverseForm = new ReverseRoomTransferForm
        {
            Id = original.Id,
            OperationKey = Guid.NewGuid().ToString("N"),
            Reason = "Unit-test reversal"
        };
        var adminService = CreateDashboardService(db, Principal("admin@fruitandland.com"));
        Assert.Null(await adminService.ReverseRoomTransferAsync(reverseForm, CancellationToken.None));
        var transferCountAfterReverse = await db.RoomTransfers.CountAsync();
        Assert.Null(await adminService.ReverseRoomTransferAsync(reverseForm, CancellationToken.None));

        Assert.Equal(transferCountAfterReverse, await db.RoomTransfers.CountAsync());
        Assert.Equal(120, await LedgerBalanceAsync(db, 1001, "LOT-120"));
        Assert.Equal(25, await LedgerBalanceAsync(db, 1002, "LOT-120"));
        Assert.True(original.IsReversed);
        Assert.Single(await db.RoomTransfers.Where(x => x.ReversesRoomTransferId == original.Id).ToListAsync());
        Assert.Equal(4, await db.RoomInventoryAdjustments.CountAsync(x => x.RoomTransferId != null));
        var readiness = await new InventoryDeductionInvariantService(
            db,
            NullLogger<InventoryDeductionInvariantService>.Instance).VerifyReadinessAsync(CancellationToken.None);
        Assert.True(readiness.IsReady, string.Join("; ", readiness.Issues.Select(x => x.Code)));
    }

    [Fact]
    public async Task PostgreSql_BinsRunActualRunTransferReversalAndReadinessWorkflow_WhenConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROPQC_TEST_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connectionString);
        var interceptor = new RoomLedgerCommandInterceptor();
        var optionsBuilder = new DbContextOptionsBuilder<CropQcDbContext>();
        CropQcDatabase.Configure(optionsBuilder, DatabaseProviders.PostgreSql, connectionString);
        optionsBuilder.AddInterceptors(interceptor);
        var options = optionsBuilder.Options;
        await using var db = new CropQcDbContext(options);
        Assert.True(
            await db.Database.EnsureCreatedAsync(),
            "The configured disposable PostgreSQL workflow database must start empty.");
        await SeedActualRunLedgerOnlyAsync(db);
        var service = CreateService(db);
        var manager = Principal("manager@fruitandland.com");

        interceptor.Reset();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var page = await service.GetPageAsync(
            new BinsRunFilterForm { Section = "Actual", RoomIds = [1001, 1002] },
            manager,
            CancellationToken.None);
        stopwatch.Stop();
        Console.WriteLine(
            $"Actual Run PostgreSQL room selection: {interceptor.RoomLedgerQueryCount} room-ledger queries, " +
            $"{page.AvailableInventory.Count} rows, {stopwatch.Elapsed.TotalMilliseconds:0.0} ms.");
        var lot120Room1 = page.AvailableInventory.Single(x => x.RoomId == 1001 && x.Lot == "LOT-120");
        var lot120Room2 = page.AvailableInventory.Single(x => x.RoomId == 1002 && x.Lot == "LOT-120");

        Assert.Equal(2, interceptor.RoomLedgerQueryCount);
        Assert.True(page.AvailableInventory.Count <= 2000);
        var create = GroupForm((lot120Room1, 10), (lot120Room2, 5));
        Assert.Null(await service.CreateActualRunAsync(create, manager, CancellationToken.None));
        Assert.Null(await service.CreateActualRunAsync(create, manager, CancellationToken.None));
        Assert.Equal(110, await LedgerBalanceAsync(db, 1001, "LOT-120"));
        Assert.Equal(20, await LedgerBalanceAsync(db, 1002, "LOT-120"));
        Assert.Equal(2, await db.BinsRunEntries.CountAsync(x => x.ActualRunId != null && x.TransactionType == ActualRunTransactionTypes.Depletion));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5), $"Room selection took {stopwatch.Elapsed.TotalMilliseconds:0} ms.");

        var run = await db.ActualRuns.SingleAsync();
        var editPage = await service.GetPageAsync(
            new BinsRunFilterForm { Section = "Actual", RoomIds = [1001, 1002] },
            manager,
            CancellationToken.None);
        var edit = GroupForm(
            (editPage.AvailableInventory.Single(x => x.RoomId == 1001 && x.Lot == "LOT-120"), 8),
            (editPage.AvailableInventory.Single(x => x.RoomId == 1002 && x.Lot == "LOT-120"), 7));
        edit.Id = run.Id;
        edit.ConcurrencyVersion = run.ConcurrencyVersion;
        Assert.Null(await service.UpdateActualRunAsync(run.Id, edit, manager, CancellationToken.None));
        await db.Entry(run).ReloadAsync();
        var cancel = new CancelActualRunForm
        {
            Id = run.Id,
            ConcurrencyVersion = run.ConcurrencyVersion,
            OperationKey = Guid.NewGuid().ToString("N"),
            Reason = "Disposable PostgreSQL workflow verification"
        };
        var admin = Principal("admin@fruitandland.com");
        Assert.Null(await service.CancelActualRunAsync(cancel, admin, CancellationToken.None));
        var afterCancelEntryCount = await db.BinsRunEntries.CountAsync();
        Assert.Null(await service.CancelActualRunAsync(cancel, admin, CancellationToken.None));
        Assert.Equal(afterCancelEntryCount, await db.BinsRunEntries.CountAsync());
        Assert.Equal(120, await LedgerBalanceAsync(db, 1001, "LOT-120"));
        Assert.Equal(25, await LedgerBalanceAsync(db, 1002, "LOT-120"));

        var legacyPage = await service.GetPageAsync(
            new BinsRunFilterForm { Section = "Actual", RoomId = 1001 },
            manager,
            CancellationToken.None);
        var legacyOption = legacyPage.AvailableInventory.Single(x => x.Lot == "LOT-30");
        var legacyProjection = ProjectionForActual(legacyOption, 1000);
        db.RunProjections.Add(legacyProjection);
        await db.SaveChangesAsync();
        var legacyForm = ActualRunForm(legacyOption, legacyProjection);
        legacyForm.OperationKey = Guid.NewGuid().ToString("N");
        legacyForm.BinsRun = 5;
        Assert.Null(await service.CreateAsync(legacyForm, manager, CancellationToken.None));
        var legacyEntry = await db.BinsRunEntries.SingleAsync(
            x => x.ActualRunId == null && x.TransactionType == ActualRunTransactionTypes.Legacy && !x.IsReversed);
        Assert.Equal(25, await LedgerBalanceAsync(db, 1001, "LOT-30"));
        Assert.Null(await service.ReverseAsync(
            new ReverseBinsRunForm { Id = legacyEntry.Id, Reason = "Disposable workflow reversal" },
            admin,
            CancellationToken.None));
        Assert.Equal(30, await LedgerBalanceAsync(db, 1001, "LOT-30"));

        var dashboard = CreateDashboardService(db, manager);
        var sourceLot = (await dashboard.GetRoomDetailAsync(1001, CancellationToken.None))
            .TransferLotOptions.Single(x => x.Label.Contains("LOT-120", StringComparison.OrdinalIgnoreCase));
        var transferForm = new RoomTransferForm
        {
            OperationKey = Guid.NewGuid().ToString("N"),
            FromRoomId = 1001,
            ToRoomId = 1002,
            SourceLotKey = sourceLot.LotKey,
            BinCount = 10,
            TransferAt = DateTimeOffset.UtcNow,
            Reason = "Disposable PostgreSQL transfer verification"
        };
        Assert.Null(await dashboard.CreateRoomTransferAsync(transferForm, CancellationToken.None));
        Assert.Null(await dashboard.CreateRoomTransferAsync(transferForm, CancellationToken.None));
        var transfer = await db.RoomTransfers.SingleAsync(x => x.ReversesRoomTransferId == null);
        Assert.Equal(2, await db.RoomInventoryAdjustments.CountAsync(x => x.RoomTransferId == transfer.Id));
        Assert.Equal(110, await LedgerBalanceAsync(db, 1001, "LOT-120"));
        Assert.Equal(35, await LedgerBalanceAsync(db, 1002, "LOT-120"));

        dashboard = CreateDashboardService(db, admin);
        var reverseTransfer = new ReverseRoomTransferForm
        {
            Id = transfer.Id,
            OperationKey = Guid.NewGuid().ToString("N"),
            Reason = "Disposable PostgreSQL transfer reversal"
        };
        Assert.Null(await dashboard.ReverseRoomTransferAsync(reverseTransfer, CancellationToken.None));
        var transferCountAfterReverse = await db.RoomTransfers.CountAsync();
        Assert.Null(await dashboard.ReverseRoomTransferAsync(reverseTransfer, CancellationToken.None));
        Assert.Equal(transferCountAfterReverse, await db.RoomTransfers.CountAsync());
        Assert.Equal(120, await LedgerBalanceAsync(db, 1001, "LOT-120"));
        Assert.Equal(25, await LedgerBalanceAsync(db, 1002, "LOT-120"));

        var invariant = new InventoryDeductionInvariantService(
            db,
            NullLogger<InventoryDeductionInvariantService>.Instance);
        var readiness = await invariant.VerifyReadinessAsync(CancellationToken.None);
        Assert.True(readiness.IsReady, string.Join("; ", readiness.Issues.Select(x => $"{x.Code}:{x.AdjustmentId}")));
        var reconciliation = await new RoomInventoryReconciliationService(
            db,
            new RoomInventoryLedgerQueryService(db),
            invariant).GetPageAsync(new RoomInventoryReconciliationFilter { WarehouseId = 1000 }, CancellationToken.None);
        Assert.NotEmpty(reconciliation.NegativeAdjustments);
    }

    [Fact]
    public async Task RoomLedger_UsesReceiptMetadataForLegacyBlankAdjustments_AndCountsDepletionOnce()
    {
        using var db = CreateDbContext();
        await SeedProductionLikeBartlettLedgerAsync(db);
        var query = new RoomInventoryLedgerQueryService(db);

        var snapshots = await query.GetSnapshotsAsync(2000, [2001], CancellationToken.None);

        Assert.Equal(2, snapshots.Count);
        var conventional = snapshots.Single(x => x.ProductionType == "Conventional");
        Assert.Equal(2026, conventional.CropYear);
        Assert.Equal("1084", conventional.Lot);
        Assert.Equal("BART", conventional.Variety);
        Assert.Equal(325, conventional.PositiveBins);
        Assert.Equal(-184, conventional.NegativeBins);
        Assert.Equal(184, conventional.LegacyBinsRunDepletionBins);
        Assert.Equal(141, conventional.CurrentBins);
        Assert.Equal(2, conventional.TransactionCount);
        var organic = snapshots.Single(x => x.ProductionType == "Organic");
        Assert.Equal(310, organic.CurrentBins);
        Assert.NotEqual(conventional.FruitProfileId, organic.FruitProfileId);

        var page = await CreateService(db).GetPageAsync(
            new BinsRunFilterForm { Section = "Actual", RoomIds = [2001] },
            Principal("manager@fruitandland.com"),
            CancellationToken.None);
        Assert.Equal(141, page.AvailableInventory.Single(x => x.FruitProfileId == conventional.FruitProfileId).CurrentBins);
        Assert.Equal(310, page.AvailableInventory.Single(x => x.FruitProfileId == organic.FruitProfileId).CurrentBins);
    }

    [Fact]
    public async Task Reconciliation_IsReadOnly_AndReportsUnledgeredReceiptOrigin()
    {
        using var db = CreateDbContext();
        await SeedProductionLikeBartlettLedgerAsync(db);
        var beforeAdjustments = await db.RoomInventoryAdjustments.CountAsync();
        var beforeEntries = await db.BinsRunEntries.CountAsync();
        var service = new RoomInventoryReconciliationService(db, new RoomInventoryLedgerQueryService(db));

        var page = await service.GetPageAsync(
            new RoomInventoryReconciliationFilter { WarehouseId = 2000, RoomId = 2001 },
            CancellationToken.None);

        Assert.Equal(636, page.InboundReceiptBins);
        Assert.Equal(1, page.UnledgeredInboundBins);
        Assert.Equal(451, page.LedgerBalance);
        Assert.Contains(page.Rows, x =>
            x.ProductionType == "Conventional"
            && x.UnledgeredInboundBins == 1
            && x.Warnings.Any(warning => warning.Contains("no room-ledger origin", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(beforeAdjustments, await db.RoomInventoryAdjustments.CountAsync());
        Assert.Equal(beforeEntries, await db.BinsRunEntries.CountAsync());
        Assert.Empty(await db.ActualRuns.ToListAsync());
    }

    [Fact]
    public async Task Reconciliation_BaselineForAnotherLotDoesNotSupersedeDeduction()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var binsRun = CreateService(db);
        var user = Principal("manager@fruitandland.com");
        var option = (await binsRun.GetPageAsync(
            new BinsRunFilterForm { RoomId = 1001 },
            user,
            CancellationToken.None)).AvailableInventory.Single(x => x.Lot == "LOT-120");
        var projection = ProjectionForActual(option, 1000);
        db.RunProjections.Add(projection);
        await db.SaveChangesAsync();
        Assert.Null(await binsRun.CreateAsync(ActualRunForm(option, projection), user, CancellationToken.None));

        var warehouse = await db.Warehouses.SingleAsync(x => x.Id == 1000);
        var room = await db.Rooms.SingleAsync(x => x.Id == 1001);
        var fruit = await db.FruitProfiles.SingleAsync(x => x.Id == 1000);
        var unrelatedBaseline = Adjustment(8999, warehouse, room, fruit, "OTHER-BASELINE", 10);
        unrelatedBaseline.AdjustmentAt = DateTimeOffset.UtcNow.AddDays(1);
        db.RoomInventoryAdjustments.Add(unrelatedBaseline);
        await db.SaveChangesAsync();

        var reconciliation = await new RoomInventoryReconciliationService(
            db,
            new RoomInventoryLedgerQueryService(db),
            new InventoryDeductionInvariantService(
                db,
                NullLogger<InventoryDeductionInvariantService>.Instance))
            .GetPageAsync(
                new RoomInventoryReconciliationFilter { WarehouseId = 1000, RoomId = 1001 },
                CancellationToken.None);

        Assert.True(reconciliation.NegativeAdjustments.Single().CurrentlyAffectsInventory);
    }

    [Fact]
    public async Task RoomLedger_CurrentInventoryBaselineResetsEarlierActivity()
    {
        using var db = CreateDbContext();
        var warehouse = new Warehouse { Id = 3000, Code = "EBS", Name = "EBS", IsActive = true };
        var room = new Room { Id = 3001, Warehouse = warehouse, Code = "ROOM", Name = "Room", IsActive = true };
        var fruit = new FruitProfile
        {
            Id = 3000,
            Name = "Fuji",
            VarietyCode = "FUJI",
            FruitType = "Apple",
            ProductionType = "Conventional",
            IsActive = true
        };
        var receipt = ProductionReceipt(9301, 100, "1570", fruit, warehouse, room);
        receipt.ReceivedAt = DateTimeOffset.Parse("2026-06-01T12:00:00Z");
        var receiptAdd = ProductionReceiptAdjustment(9302, receipt, 100);
        var baselineAt = DateTimeOffset.Parse("2026-06-18T12:00:00Z");
        var firstBaseline = Adjustment(9303, warehouse, room, fruit, "1570", 50);
        firstBaseline.AdjustmentAt = baselineAt;
        firstBaseline.CreatedAt = baselineAt;
        firstBaseline.NewBinCount = 50;
        var replacementBaseline = Adjustment(9304, warehouse, room, fruit, "1570", -5);
        replacementBaseline.AdjustmentAt = baselineAt;
        replacementBaseline.CreatedAt = baselineAt.AddMinutes(1);
        replacementBaseline.OldBinCount = 50;
        replacementBaseline.NewBinCount = 45;
        var laterDepletion = new RoomInventoryAdjustment
        {
            Id = 9305,
            CropYear = 2026,
            Warehouse = warehouse,
            Room = room,
            FruitProfile = fruit,
            GrowerName = "Grower",
            LotNumber = "1570",
            VarietyCode = "FUJI",
            ChangeAmount = -10,
            NewBinCount = 35,
            AdjustmentType = BinsRunService.AdjustmentType,
            AdjustmentAt = baselineAt.AddDays(1),
            CreatedAt = baselineAt.AddDays(1)
        };
        db.AddRange(warehouse, room, fruit, receipt, receiptAdd, firstBaseline, replacementBaseline, laterDepletion);
        await db.SaveChangesAsync();

        var snapshot = Assert.Single(await new RoomInventoryLedgerQueryService(db)
            .GetSnapshotsAsync(3000, [3001], CancellationToken.None));

        Assert.Equal(35, snapshot.CurrentBins);
        Assert.Equal(45, snapshot.PositiveBins);
        Assert.Equal(-10, snapshot.NegativeBins);
        Assert.Equal(2, snapshot.TransactionCount);
    }

    [Fact]
    public void ProductionDatabaseSafety_RejectsStartupMutationAndNonDisposableTestDatabase()
    {
        Assert.Throws<InvalidOperationException>(() =>
            ProductionDatabaseSafety.RejectProductionStartupMutation(
                isProduction: true,
                ensureCreatedOnStartup: true,
                seedMasterDataOnStartup: false));
        Assert.Throws<InvalidOperationException>(() =>
            ProductionDatabaseSafety.RejectProductionStartupMutation(
                isProduction: true,
                ensureCreatedOnStartup: false,
                seedMasterDataOnStartup: true));
        Assert.Throws<InvalidOperationException>(() =>
            ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(
                "Host=example;Database=crop_qc_db;Username=readonly"));

        ProductionDatabaseSafety.RejectProductionStartupMutation(
            isProduction: false,
            ensureCreatedOnStartup: true,
            seedMasterDataOnStartup: true);
        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(
            "Host=localhost;Database=crop_qc_disposable_test;Username=tester");
    }

    private static CropQcDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new CropQcDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }

    private static async Task SeedInventoryAsync(CropQcDbContext db)
    {
        var warehouse = new Warehouse { Id = 1000, Code = "EBS", Name = "EBS", IsActive = true };
        var room = new Room { Id = 1001, WarehouseId = warehouse.Id, Warehouse = warehouse, Code = "EVANCA12", Name = "Evans 12", CropQcRoomName = "Evans-12", IsActive = true };
        var otherRoom = new Room { Id = 1002, WarehouseId = warehouse.Id, Warehouse = warehouse, Code = "LAMBCA17", Name = "Lamb 17", CropQcRoomName = "Lamb-17", IsActive = true };
        var fruit = new FruitProfile { Id = 1000, Name = "Fuji", VarietyCode = "FUJI", FruitType = "Apple", ProductionType = "Conventional", IsActive = true };
        var sampleType = new SampleType { Id = 1000, Name = "Receiving Sample", IsActive = true };
        var doorSampleType = new SampleType { Id = 1001, Name = "Door Sample", IsActive = true };
        var grade1 = new Grade { Id = 1000, Code = "W1", Name = "W1", IsActive = true };
        var grade2 = new Grade { Id = 1001, Code = "W2", Name = "W2", IsActive = true };
        db.Warehouses.Add(warehouse);
        db.Rooms.AddRange(room, otherRoom);
        db.FruitProfiles.Add(fruit);
        db.SampleTypes.AddRange(sampleType, doorSampleType);
        db.Grades.AddRange(grade1, grade2);
        db.Users.AddRange(
            User(1000, "admin@fruitandland.com", PageAccessLevel.Admin),
            User(1001, "manager@fruitandland.com", PageAccessLevel.Edit),
            User(1002, "viewer@fruitandland.com", PageAccessLevel.View));
        db.RoomInventoryAdjustments.AddRange(
            Adjustment(8001, warehouse, room, fruit, "LOT-120", 120),
            Adjustment(8004, warehouse, room, fruit, "LOT-30", 30),
            Adjustment(8005, warehouse, room, fruit, "HISTORY", 40),
            Adjustment(8002, warehouse, room, fruit, "LOT-ZERO", 0),
            Adjustment(8003, warehouse, otherRoom, fruit, "LOT-OTHER", 60),
            Adjustment(8006, warehouse, otherRoom, fruit, "LOT-120", 25));
        db.Receipts.AddRange(new Receipt
        {
            Id = 7001,
            CropYear = 2026,
            ReceivedAt = DateTimeOffset.UtcNow,
            CompuTechReceiptId = "TRUCK-HISTORY",
            ReceiptType = "Truck receipt",
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            RoomId = room.Id,
            Room = room,
            FruitProfileId = fruit.Id,
            FruitProfile = fruit,
            GrowerName = "History Grower",
            LotCode = "HISTORY",
            BinCount = 40,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        },
        SampleReceipt(7002, "QC-LOT-120", "LOT-120", warehouse, room, fruit),
        SampleReceipt(7003, "QC-LOT-30", "LOT-30", warehouse, room, fruit));
        db.QcSamples.Add(new QcSample
        {
            Id = 7101,
            ReceiptId = 7001,
            SampleTypeId = sampleType.Id,
            SampleType = sampleType,
            Status = "Complete",
            StarchStatus = "Complete",
            PhotoStatus = "Complete",
            EmailStatus = "Not Sent",
            SampleTakenAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        });
        db.QcSamples.AddRange(
            Sample(7102, 7002, doorSampleType, DateTimeOffset.Parse("2026-07-09T08:00:00-07:00")),
            Sample(7103, 7003, doorSampleType, DateTimeOffset.Parse("2026-07-09T09:00:00-07:00")));
        db.QcFruitReadings.AddRange(
            FruitReading(7201, 7102, 1, 80, grade1),
            FruitReading(7202, 7102, 2, 100, grade2),
            FruitReading(7203, 7103, 1, 100, grade2),
            FruitReading(7204, 7103, 2, 100, grade2));
        await db.SaveChangesAsync();
    }

    private static async Task SeedActualRunLedgerOnlyAsync(CropQcDbContext db)
    {
        var warehouse = new Warehouse { Id = 1000, Code = "ART", Name = "Actual Run Test", IsActive = true };
        var room = new Room { Id = 1001, WarehouseId = warehouse.Id, Warehouse = warehouse, Code = "EVANCA12", Name = "Evans 12", CropQcRoomName = "Evans-12", IsActive = true };
        var otherRoom = new Room { Id = 1002, WarehouseId = warehouse.Id, Warehouse = warehouse, Code = "LAMBCA17", Name = "Lamb 17", CropQcRoomName = "Lamb-17", IsActive = true };
        var fruit = new FruitProfile { Id = 1000, Name = "Actual Run Test Apple", VarietyCode = "ACTUALRUNTEST", FruitType = "Apple", ProductionType = "Conventional", IsActive = true };
        db.Warehouses.Add(warehouse);
        db.Rooms.AddRange(room, otherRoom);
        db.FruitProfiles.Add(fruit);
        db.Users.AddRange(
            User(1000, "admin@fruitandland.com", PageAccessLevel.Admin),
            User(1001, "manager@fruitandland.com", PageAccessLevel.Edit));
        var adjustments = new[]
        {
            Adjustment(8001, warehouse, room, fruit, "LOT-120", 120),
            Adjustment(8004, warehouse, room, fruit, "LOT-30", 30),
            Adjustment(8003, warehouse, otherRoom, fruit, "LOT-OTHER", 60),
            Adjustment(8006, warehouse, otherRoom, fruit, "LOT-120", 25)
        };
        foreach (var adjustment in adjustments)
        {
            adjustment.AdjustmentAt = adjustment.AdjustmentAt.ToUniversalTime();
        }
        db.RoomInventoryAdjustments.AddRange(adjustments);
        await db.SaveChangesAsync();
    }

    private static async Task SeedProductionLikeBartlettLedgerAsync(CropQcDbContext db)
    {
        var warehouse = new Warehouse { Id = 2000, Code = "WP", Name = "Windy Point", IsActive = true };
        var room = new Room
        {
            Id = 2001,
            WarehouseId = warehouse.Id,
            Warehouse = warehouse,
            Code = "WP-4",
            Name = "Room 4",
            CropQcRoomName = "WP-4",
            IsActive = true
        };
        var conventional = new FruitProfile
        {
            Id = 2000,
            Name = "Bartlett",
            VarietyCode = "BART",
            FruitType = "Pear",
            ProductionType = "Conventional",
            IsActive = true
        };
        var organic = new FruitProfile
        {
            Id = 2001,
            Name = "Organic Bartlett",
            VarietyCode = "BART",
            FruitType = "Pear",
            ProductionType = "Organic",
            IsOrganic = true,
            IsActive = true
        };
        db.Warehouses.Add(warehouse);
        db.Rooms.Add(room);
        db.FruitProfiles.AddRange(conventional, organic);
        db.Users.Add(User(1001, "manager@fruitandland.com", PageAccessLevel.Edit));

        var conventionalReceipt = ProductionReceipt(9001, 325, "1084", conventional, warehouse, room);
        var organicReceipt = ProductionReceipt(9002, 310, "1080", organic, warehouse, room);
        var unledgeredReceipt = ProductionReceipt(9003, 1, "1084", conventional, warehouse, room);
        db.Receipts.AddRange(conventionalReceipt, organicReceipt, unledgeredReceipt);
        var conventionalAdd = ProductionReceiptAdjustment(9101, conventionalReceipt, 325);
        var organicAdd = ProductionReceiptAdjustment(9102, organicReceipt, 310);
        var depletion = new RoomInventoryAdjustment
        {
            Id = 9103,
            CropYear = null,
            Receipt = conventionalReceipt,
            Warehouse = warehouse,
            Room = room,
            FruitProfileId = null,
            FruitProfile = null,
            GrowerName = "WP ORCHARD",
            LotNumber = "1084",
            VarietyCode = null,
            ChangeAmount = -184,
            NewBinCount = 141,
            AdjustmentType = BinsRunService.AdjustmentType,
            Source = "Bins Run",
            AdjustmentAt = DateTimeOffset.Parse("2026-07-28T05:11:00Z"),
            CreatedAt = DateTimeOffset.Parse("2026-07-28T05:11:00Z")
        };
        db.RoomInventoryAdjustments.AddRange(conventionalAdd, organicAdd, depletion);
        db.BinsRunEntries.Add(new BinsRunEntry
        {
            Id = 9201,
            InventoryAdjustment = depletion,
            Warehouse = warehouse,
            Room = room,
            FruitProfile = conventional,
            GrowerName = "WP ORCHARD",
            LotNumber = "1084",
            VarietyCode = "BART",
            PreviousAvailableBins = 325,
            BinsRun = 184,
            NewAvailableBins = 141,
            RunAt = DateTimeOffset.Parse("2026-07-28T05:11:00Z"),
            CreatedAt = DateTimeOffset.Parse("2026-07-28T05:11:00Z"),
            TransactionType = ActualRunTransactionTypes.Legacy
        });
        await db.SaveChangesAsync();
    }

    private static Receipt ProductionReceipt(
        long id,
        int bins,
        string lot,
        FruitProfile fruit,
        Warehouse warehouse,
        Room room) => new()
        {
            Id = id,
            CropYear = 2026,
            ReceivedAt = DateTimeOffset.Parse("2026-07-21T17:56:00Z"),
            CompuTechReceiptId = $"WP-{id}",
            ReceiptType = "Truck receipt",
            Warehouse = warehouse,
            Room = room,
            FruitProfile = fruit,
            GrowerName = "WP ORCHARD",
            GrowerNumber = lot,
            LotCode = lot,
            BinCount = bins,
            CreatedAt = DateTimeOffset.Parse("2026-07-21T17:56:00Z"),
            UpdatedAt = DateTimeOffset.Parse("2026-07-21T17:56:00Z")
        };

    private static RoomInventoryAdjustment ProductionReceiptAdjustment(long id, Receipt receipt, int bins) => new()
    {
        Id = id,
        CropYear = null,
        Receipt = receipt,
        Warehouse = receipt.Warehouse,
        Room = receipt.Room,
        FruitProfileId = null,
        FruitProfile = null,
        GrowerName = receipt.GrowerName,
        LotNumber = receipt.GrowerNumber ?? receipt.LotCode,
        VarietyCode = null,
        OldBinCount = 0,
        ChangeAmount = bins,
        NewBinCount = bins,
        AdjustmentType = "ReceiptAdd",
        Source = "Receiving inventory added",
        AdjustmentAt = receipt.ReceivedAt,
        CreatedAt = receipt.ReceivedAt
    };

    private static Receipt SampleReceipt(long id, string receiptId, string lot, Warehouse warehouse, Room room, FruitProfile fruit) => new()
    {
        Id = id,
        CropYear = 2026,
        ReceivedAt = DateTimeOffset.Parse("2026-07-09T07:00:00-07:00"),
        CompuTechReceiptId = receiptId,
        ReceiptType = "Door sample",
        WarehouseId = warehouse.Id,
        Warehouse = warehouse,
        RoomId = room.Id,
        Room = room,
        FruitProfileId = fruit.Id,
        FruitProfile = fruit,
        GrowerName = "QC Grower",
        GrowerNumber = lot,
        LotCode = lot,
        BinCount = 999,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static RunProjection ProjectionForActual(BinsRunInventoryOptionViewModel option, int facilityWarehouseId)
    {
        var projection = new RunProjection
        {
            PlannedRunDate = new(2026, 7, 24),
            Name = "Actual conversion",
            Status = RunProjectionStatuses.Ready,
            ProjectionMode = RunProjectionModes.Inventory,
            FacilityWarehouseId = facilityWarehouseId,
            FacilityCodeSnapshot = facilityWarehouseId == 1000 ? "EBS" : "WP",
            CropYear = 2026,
            ApplePoundsPerBin = 880,
            PearPoundsPerBin = 920,
            StandardBoxWeightPounds = 40,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        projection.Sources.Add(new RunProjectionSource
        {
            SourceType = RunProjectionSourceTypes.Inventory,
            InventoryKey = option.InventoryKey,
            WarehouseId = option.WarehouseId,
            RoomId = option.RoomId,
            FruitProfileId = option.FruitProfileId ?? 0,
            PlannedBins = 5,
            SelectedQcSourceType = RunProjectionQcSourceTypes.None,
            Commodity = "Apple",
            SourceLabelSnapshot = option.Label,
            FacilitySnapshot = "EBS",
            RoomSnapshot = option.Room,
            LotSnapshot = option.Lot,
            VarietySnapshot = option.Variety,
            CalculationVersion = RunProjectionCalculationService.CurrentCalculationVersion,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        return projection;
    }

    private static BinsRunForm ActualRunForm(BinsRunInventoryOptionViewModel option, RunProjection projection) =>
        new()
        {
            WarehouseId = option.WarehouseId,
            RoomId = option.RoomId,
            InventoryKey = option.InventoryKey,
            BinsRun = 5,
            ExpectedAvailableBins = option.CurrentBins,
            RunProjectionId = projection.Id,
            RunProjectionSourceId = projection.Sources.Single().Id,
            RunAt = DateTimeOffset.UtcNow
        };

    private static ActualRunForm GroupForm(params (BinsRunInventoryOptionViewModel Option, int Bins)[] rows) =>
        new()
        {
            OperationKey = Guid.NewGuid().ToString("N"),
            RunAt = DateTimeOffset.UtcNow,
            Lines = rows.Select(x => new ActualRunLineForm
            {
                InventoryKey = x.Option.InventoryKey,
                BinsRun = x.Bins,
                ExpectedAvailableBins = x.Option.CurrentBins
            }).ToList()
        };

    private static User User(int id, string email, PageAccessLevel binsRunLevel) => new()
    {
        Id = id,
        Email = email,
        DisplayName = email,
        Domain = "fruitandland.com",
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
        PageAccesses =
        {
            new UserPageAccess { AreaKey = ApplicationAreas.BinsRun, AccessLevel = binsRunLevel.ToString(), UpdatedAt = DateTimeOffset.UtcNow },
            new UserPageAccess { AreaKey = ApplicationAreas.RoomTransactions, AccessLevel = binsRunLevel.ToString(), UpdatedAt = DateTimeOffset.UtcNow },
            new UserPageAccess { AreaKey = ApplicationAreas.Transfers, AccessLevel = binsRunLevel.ToString(), UpdatedAt = DateTimeOffset.UtcNow }
        }
    };

    private static RoomInventoryAdjustment Adjustment(long id, Warehouse warehouse, Room room, FruitProfile fruit, string lot, int bins) => new()
    {
        Id = id,
        CropYear = 2026,
        WarehouseId = warehouse.Id,
        Warehouse = warehouse,
        RoomId = room.Id,
        Room = room,
        FruitProfileId = fruit.Id,
        FruitProfile = fruit,
        GrowerName = "Wes Verified Current Inventory",
        LotNumber = lot,
        VarietyCode = fruit.VarietyCode,
        OldBinCount = null,
        ChangeAmount = bins,
        NewBinCount = bins,
        AdjustmentType = RoomInventoryImportService.StartingInventoryAdjustmentType,
        Source = "Current Inventory Baseline",
        Reason = "Test seed",
        AdjustmentAt = DateTimeOffset.Parse("2026-06-18T00:00:00-07:00"),
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static QcSample Sample(long id, long receiptId, SampleType sampleType, DateTimeOffset sampleTakenAt) => new()
    {
        Id = id,
        ReceiptId = receiptId,
        SampleTypeId = sampleType.Id,
        SampleType = sampleType,
        Status = "Complete",
        StarchStatus = "Complete",
        PhotoStatus = "Complete",
        EmailStatus = "Not Sent",
        SampleTakenAt = sampleTakenAt,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static QcFruitReading FruitReading(long id, long sampleId, int row, int size, Grade grade) => new()
    {
        Id = id,
        QcSampleId = sampleId,
        RowNumber = row,
        GradeId = grade.Id,
        Grade = grade,
        SizeCategory = size,
        SizeStatus = "Sized",
        IsCompleted = true,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static BinsRunService CreateService(CropQcDbContext db) =>
        new(db, new UserAccessService(db, new ConfigurationBuilder().Build()), NullLogger<BinsRunService>.Instance);

    private static DashboardDataService CreateDashboardService(CropQcDbContext db, ClaimsPrincipal principal)
    {
        var configuration = new ConfigurationBuilder().Build();
        return new DashboardDataService(
            db,
            null!,
            new FileStorageOptions(),
            new EmailOptions(),
            null!,
            new GoogleAuthenticationOptions(),
            null!,
            null!,
            new QcPhotoRequirementPolicy(),
            null!,
            new CropYearService(db, configuration),
            new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } },
            configuration,
            NullLogger<DashboardDataService>.Instance,
            new UserAccessService(db, configuration));
    }

    private static Task<int> LedgerBalanceAsync(CropQcDbContext db, int roomId, string lot) =>
        db.RoomInventoryAdjustments
            .Where(x => x.RoomId == roomId && x.LotNumber == lot)
            .SumAsync(x => x.ChangeAmount);

    private static ClaimsPrincipal Principal(string email) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Email, email)], "TestAuth"));

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

    private sealed class RoomLedgerCommandInterceptor : DbCommandInterceptor
    {
        public int RoomLedgerQueryCount { get; private set; }

        public void Reset() => RoomLedgerQueryCount = 0;

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("RoomInventoryAdjustments", StringComparison.OrdinalIgnoreCase))
            {
                RoomLedgerQueryCount++;
            }

            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
