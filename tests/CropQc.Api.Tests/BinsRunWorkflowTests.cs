using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Storage;
using CropQc.Shared.Time;
using CropQc.Web.Auth;
using CropQc.Web.Controllers;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
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
        var navigation = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "SiteNavigationService.cs"));
        var access = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "UserAccessService.cs"));
        var program = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Program.cs"));
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "BinsRun", "Index.cshtml"));
        var projectionMath = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "ProjectionDistributionMath.cs"));

        Assert.Contains("ApplicationAreas.BinsRun", access);
        Assert.Contains("AccessPolicyNames.BinsRunView", program);
        Assert.Contains("AccessPolicyNames.BinsRunEdit", program);
        Assert.Contains("AccessPolicyNames.BinsRunAdmin", program);
        Assert.Contains("ApplicationAreas.BinsRun", navigation);
        Assert.Contains("\"run-planner\"", navigation);
        Assert.Contains("\"actual-runs\"", navigation);
        Assert.Contains("\"room-transfers\"", navigation);
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
        AssertActionPolicy<BinsRunController>(nameof(BinsRunController.CreateActualRun), AccessPolicyNames.ActualRunsCreate);
        AssertActionPolicy<BinsRunController>(nameof(BinsRunController.UpdateActualRun), AccessPolicyNames.ActualRunsAdmin);
        AssertActionPolicy<BinsRunController>(nameof(BinsRunController.CancelActualRun), AccessPolicyNames.ActualRunsAdmin);
        AssertActionPolicy<BinsRunController>(nameof(BinsRunController.ApproveActualRunOverride), AccessPolicyNames.ActualRunsAdmin);
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

        var error = await service.CreateAsync(ProjectionConversionForm(option, projection), user, CancellationToken.None);

        Assert.Contains("cannot be converted", error, StringComparison.OrdinalIgnoreCase);
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

        var error = await service.CreateAsync(ProjectionConversionForm(option, projection), user, CancellationToken.None);

        Assert.Contains("cannot be converted", error, StringComparison.OrdinalIgnoreCase);
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

    [Theory]
    [InlineData("Conventional")]
    [InlineData("Organic")]
    [InlineData(null)]
    public async Task StandaloneReversal_UsesOriginalTransactionInventoryStatus_WhenCurrentSnapshotIsBlank(string? originalStatus)
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var manager = Principal("manager@fruitandland.com");
        var option = (await service.GetPageAsync(new BinsRunFilterForm { RoomId = 1001 }, manager, default))
            .AvailableInventory.Single(x => x.Lot == "LOT-120");
        var projection = ProjectionForActual(option, 1000);
        db.RunProjections.Add(projection);
        await db.SaveChangesAsync();
        var form = ActualRunForm(option, projection);
        form.BinsRun = 8;
        Assert.Null(await service.CreateAsync(form, manager, default));
        var original = await db.BinsRunEntries.SingleAsync();
        original.InventoryStatus = originalStatus;
        original.InventoryAdjustment.InventoryStatus = originalStatus;
        AddBlankCurrentMetadataAdjustment(db, original, 8991);
        await db.SaveChangesAsync();

        Assert.Null(await service.ReverseAsync(
            new ReverseBinsRunForm { Id = original.Id, Reason = "Production-shaped identity regression" },
            Principal("admin@fruitandland.com"),
            default));

        var reversal = await db.BinsRunEntries.Include(x => x.InventoryAdjustment)
            .SingleAsync(x => x.ReversesBinsRunEntryId == original.Id);
        Assert.Equal(originalStatus ?? "", reversal.InventoryStatus ?? "");
        Assert.Equal(originalStatus ?? "", reversal.InventoryAdjustment.InventoryStatus ?? "");
        var readiness = await new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance)
            .VerifyReadinessAsync(default);
        Assert.DoesNotContain(readiness.Issues, x => x.Code == "OrganicStatusMismatch");
    }

    [Fact]
    public async Task StandaloneRevision_UsesOriginalTransactionInventoryStatus_WhenCurrentSnapshotIsBlank()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var manager = Principal("manager@fruitandland.com");
        var option = (await service.GetPageAsync(new BinsRunFilterForm { RoomId = 1001 }, manager, default))
            .AvailableInventory.Single(x => x.Lot == "LOT-120");
        var projection = ProjectionForActual(option, 1000);
        db.RunProjections.Add(projection);
        await db.SaveChangesAsync();
        var create = ActualRunForm(option, projection);
        create.BinsRun = 8;
        Assert.Null(await service.CreateAsync(create, manager, default));
        var original = await db.BinsRunEntries.SingleAsync();
        original.InventoryStatus = "Conventional";
        original.InventoryAdjustment.InventoryStatus = "Conventional";
        AddBlankCurrentMetadataAdjustment(db, original, 8992);
        await db.SaveChangesAsync();
        var current = (await service.GetPageAsync(new BinsRunFilterForm { RoomId = 1001 }, manager, default))
            .AvailableInventory.Single(x => x.Lot == "LOT-120");

        Assert.Null(await service.UpdateAsync(original.Id, new BinsRunForm
        {
            InventoryKey = current.InventoryKey,
            BinsRun = 7,
            RunAt = DateTimeOffset.UtcNow
        }, manager, default));

        var reversal = await db.BinsRunEntries.Include(x => x.InventoryAdjustment)
            .SingleAsync(x => x.ReversesBinsRunEntryId == original.Id);
        Assert.Equal("Conventional", reversal.InventoryStatus);
        Assert.Equal("Conventional", reversal.InventoryAdjustment.InventoryStatus);
    }

    [Fact]
    public async Task ActualRunCancellation_UsesOriginalTransactionInventoryStatus_WhenCurrentSnapshotIsBlank()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var manager = Principal("manager@fruitandland.com");
        var option = (await service.GetPageAsync(new BinsRunFilterForm { Section = "Actual", RoomIds = [1001] }, manager, default))
            .AvailableInventory.Single(x => x.Lot == "LOT-120");
        Assert.Null(await service.CreateActualRunAsync(GroupForm((option, 8)), manager, default));
        var run = await db.ActualRuns.SingleAsync();
        var original = await db.BinsRunEntries.SingleAsync();
        original.InventoryStatus = "Conventional";
        original.InventoryAdjustment.InventoryStatus = "Conventional";
        AddBlankCurrentMetadataAdjustment(db, original, 8993);
        await db.SaveChangesAsync();

        Assert.Null(await service.CancelActualRunAsync(new CancelActualRunForm
        {
            Id = run.Id,
            ConcurrencyVersion = run.ConcurrencyVersion,
            OperationKey = Guid.NewGuid().ToString("N"),
            Reason = "Production-shaped cancellation identity regression"
        }, Principal("admin@fruitandland.com"), default));

        var reversal = await db.BinsRunEntries.Include(x => x.InventoryAdjustment)
            .SingleAsync(x => x.ReversesBinsRunEntryId == original.Id);
        Assert.Equal("Conventional", reversal.InventoryStatus);
        Assert.Equal("Conventional", reversal.InventoryAdjustment.InventoryStatus);
        var readiness = await new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance)
            .VerifyReadinessAsync(default);
        Assert.DoesNotContain(readiness.Issues, x => x.Code == "OrganicStatusMismatch");
    }

    private static void AddBlankCurrentMetadataAdjustment(CropQcDbContext db, BinsRunEntry entry, long id)
    {
        db.RoomInventoryAdjustments.Add(new RoomInventoryAdjustment
        {
            Id = id,
            CropYear = entry.CropYear,
            WarehouseId = entry.WarehouseId,
            RoomId = entry.RoomId,
            GrowerLotId = entry.GrowerLotId,
            FruitProfileId = entry.FruitProfileId,
            GrowerName = entry.GrowerName,
            LotNumber = entry.LotNumber,
            PoolStart = entry.PoolStart,
            VarietyCode = entry.VarietyCode,
            OldBinCount = entry.NewAvailableBins,
            ChangeAmount = 0,
            NewBinCount = entry.NewAvailableBins,
            AdjustmentType = "ManualTrueUp",
            Source = "Historical aggregate metadata regression fixture",
            InventoryStatus = null,
            AdjustmentAt = entry.RunAt.AddMinutes(1),
            CreatedAt = entry.RunAt.AddMinutes(1),
            InventoryInvariantVersion = 0
        });
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
        Assert.Equal(EmploymentFacilities.Ebs, run.RunFacilityCodeSnapshot);
        Assert.Equal(RunFacilityAssignmentSources.Employment, run.RunFacilityAssignmentSource);
        Assert.Equal(1, run.CurrentRevisionNumber);
        Assert.Equal(3, firstEntryCount);
        Assert.Equal(firstEntryCount, await db.BinsRunEntries.CountAsync());
        var entries = await db.BinsRunEntries.OrderBy(x => x.RoomId).ThenBy(x => x.LotNumber).ToListAsync();
        Assert.All(entries, x =>
        {
            Assert.Null(x.ReceiptId);
            Assert.Equal(ActualRunTransactionTypes.Depletion, x.TransactionType);
            Assert.Equal(run.Id, x.ActualRunId);
            Assert.Equal(EmploymentFacilities.Ebs, x.ReportingFacilityCodeSnapshot);
            Assert.Equal(2026, x.ReportingCropYearSnapshot);
            Assert.Equal("FUJI", x.ReportingVarietyCodeSnapshot);
            Assert.Equal("Conventional", x.ProductionTypeSnapshot);
            Assert.False(x.IsOrganicSnapshot);
        });
        Assert.Equal(3, await db.RoomInventoryAdjustments.CountAsync(x => x.ActualRunId == run.Id));
        var expectation = await db.RunExpectations.Include(x => x.Sources).SingleAsync();
        Assert.Equal(run.Id, expectation.ActualRunId);
        Assert.Equal(1, expectation.RevisionNumber);
        Assert.Equal(75, expectation.TotalBins);
        Assert.Equal(3, expectation.Sources.Count);
        Assert.Null(run.RunProjectionId);
        Assert.Equal(1, await db.RunExpectations.CountAsync());
        var ledgerAfterSave = await new RoomInventoryLedgerQueryService(db)
            .GetSnapshotsAsync(1000, [1001, 1002], CancellationToken.None);
        Assert.Equal(80, ledgerAfterSave.Single(x => x.RoomId == 1001 && x.Lot == "LOT-120").CurrentBins);
        Assert.Equal(-40, ledgerAfterSave.Single(x => x.RoomId == 1001 && x.Lot == "LOT-120").NegativeBins);
        var refreshed = await service.GetPageAsync(new BinsRunFilterForm { Section = "Actual", RoomIds = [1001, 1002] }, user, CancellationToken.None);
        Assert.Equal(80, refreshed.AvailableInventory.Single(x => x.RoomId == 1001 && x.Lot == "LOT-120").CurrentBins);
        Assert.DoesNotContain(refreshed.AvailableInventory, x => x.RoomId == 1002 && x.Lot == "LOT-120");
        Assert.Equal(20, refreshed.AvailableInventory.Single(x => x.RoomId == 1001 && x.Lot == "LOT-30").CurrentBins);
        Assert.Equal(receiptCount, await db.Receipts.CountAsync());
    }

    [Fact]
    public async Task Stale_BinsRun_and_multi_source_ActualRun_fail_atomically_after_room_is_sealed()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var user = Principal("manager@fruitandland.com");
        var page = await service.GetPageAsync(new BinsRunFilterForm
        {
            Section = "Actual",
            RoomIds = [1001, 1002]
        }, user, default);
        var roomOne = page.AvailableInventory.Single(x => x.RoomId == 1001 && x.Lot == "LOT-120");
        var roomTwo = page.AvailableInventory.Single(x => x.RoomId == 1002 && x.Lot == "LOT-120");
        var baselineAdjustments = await db.RoomInventoryAdjustments.CountAsync();

        var scheduledRoomTwo = await db.Rooms.SingleAsync(x => x.Id == 1002);
        scheduledRoomTwo.IsSealed = true;
        scheduledRoomTwo.SealedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        scheduledRoomTwo.SealRecordedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        await db.SaveChangesAsync();
        var actualRunError = await service.CreateActualRunAsync(GroupForm((roomOne, 5), (roomTwo, 5)), user, default);

        Assert.Contains("sealed", actualRunError, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Lamb-17", actualRunError, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.ActualRuns.ToListAsync());
        Assert.Empty(await db.BinsRunEntries.ToListAsync());
        Assert.Equal(baselineAdjustments, await db.RoomInventoryAdjustments.CountAsync());

        var scheduledRoomOne = await db.Rooms.SingleAsync(x => x.Id == 1001);
        scheduledRoomOne.IsSealed = true;
        scheduledRoomOne.SealedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        scheduledRoomOne.SealRecordedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        await db.SaveChangesAsync();
        var binsRunError = await service.CreateAsync(new BinsRunForm
        {
            InventoryKey = roomOne.InventoryKey,
            BinsRun = 5,
            ExpectedAvailableBins = roomOne.CurrentBins,
            RunAt = DateTimeOffset.UtcNow
        }, user, default);

        Assert.Contains("sealed", binsRunError, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.BinsRunEntries.ToListAsync());
        Assert.Equal(baselineAdjustments, await db.RoomInventoryAdjustments.CountAsync());
    }

    [Fact]
    public async Task ActualRun_HeterogeneousSourceLinesPersistIndependentCanonicalIdentities()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        db.FruitProfiles.AddRange(
            new FruitProfile { Id = 1003, Name = "Organic Bartlett", VarietyCode = "BART", FruitType = "Pear", ProductionType = "Organic", IsOrganic = true },
            new FruitProfile { Id = 1004, Name = "Gala", VarietyCode = "GALA", FruitType = "Apple", ProductionType = "Conventional" });
        await db.SaveChangesAsync();
        var snapshots = new RoomInventoryLedgerSnapshot[]
        {
            new(1000, "EBS", 1001, "Evans-12", "", 2026, 501, 1000, "Grower A", "1084", "LOT-A", null, "FUJI", "FUJI", "Fuji", "Apple", "Conventional", false, "Packable", 10, 0, 0, 0, 0, 0, 0, 0, 10, 10, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 8001),
            new(1000, "EBS", 1001, "Evans-12", "", 2026, 502, 1003, "Grower B", "1511", "LOT-B", null, "BART", "BART", "Bartlett", "Pear", "Organic", true, "Organic packable", 20, 0, 0, 0, 0, 0, 0, 0, 20, 20, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 8004),
            new(1000, "EBS", 1001, "Evans-12", "", 2026, 503, 1004, "Grower C", "9350", "LOT-C", null, "GALA", "GALA", "Gala", "Apple", "Conventional", false, "CA storage", 30, 0, 0, 0, 0, 0, 0, 0, 30, 30, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 8005)
        };
        var service = CreateService(db, roomInventoryLedgerQueryService: new StaticRoomInventoryLedgerQueryService(snapshots));
        var user = Principal("manager@fruitandland.com");
        var page = await service.GetPageAsync(
            new BinsRunFilterForm { Section = "Actual", WarehouseId = 1000, RoomIds = [1001] },
            user,
            CancellationToken.None);
        var rows = page.AvailableInventory.OrderBy(x => x.Lot).ToArray();

        var error = await service.CreateActualRunAsync(GroupForm((rows[0], 1), (rows[1], 2), (rows[2], 3)), user, CancellationToken.None);

        Assert.Null(error);
        var entries = await db.BinsRunEntries.OrderBy(x => x.LotNumber).ToListAsync();
        Assert.Equal(3, entries.Count);
        Assert.Equal(new[] { "1084", "1511", "9350" }, entries.Select(x => x.GrowerNumberSnapshot));
        Assert.Equal(new[] { "FUJI", "BART", "GALA" }, entries.Select(x => x.VarietyCode));
        Assert.Equal(new[] { "Conventional", "Organic", "Conventional" }, entries.Select(x => x.ProductionTypeSnapshot));
        Assert.Equal(new bool?[] { false, true, false }, entries.Select(x => x.IsOrganicSnapshot));
        Assert.Equal(new[] { 1, 2, 3 }, entries.Select(x => x.BinsRun));
        Assert.Equal(new[] { -1, -2, -3 }, await db.RoomInventoryAdjustments
            .Where(x => x.ActualRunId != null)
            .OrderBy(x => x.LotNumber)
            .Select(x => x.ChangeAmount)
            .ToArrayAsync());
        var expectation = await db.RunExpectations.Include(x => x.Sources).SingleAsync();
        Assert.Equal(6, expectation.TotalBins);
        Assert.Equal(3, expectation.Sources.Count);
        var detail = await service.GetActualRunDetailAsync(await db.ActualRuns.Select(x => x.Id).SingleAsync(), user, CancellationToken.None);
        Assert.Equal(3, detail!.Contributions.Count);
        Assert.Equal(new[] { "1084", "1511", "9350" }, detail.Contributions.Select(x => x.GrowerNumber));
        Assert.Equal(new[] { "Packable", "Organic packable", "CA storage" }, detail.Contributions.Select(x => x.InventoryStatus));
    }

    [Fact]
    public async Task ActualRun_UniqueReviewedGrowerAliasResolvesOnlyTheDeficientSourceLine()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        AddMappedGrower(db, "1511", "Porky Pears", "Porky Pears ORG CHIL");
        await db.SaveChangesAsync();
        var snapshots = new[]
        {
            new RoomInventoryLedgerSnapshot(
                1000, "EBS", 1001, "Evans-12", "", 2026, null, 1000,
                "Porky Pears ORG CHIL", null, "1511", null, "FUJI", "FUJI", "Fuji", "Apple", "Conventional", false, "Packable",
                20, 0, 0, 0, 0, 0, 0, 0, 20, 20, 1, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 8001)
        };
        var service = CreateService(db, roomInventoryLedgerQueryService: new StaticRoomInventoryLedgerQueryService(snapshots));
        var user = Principal("manager@fruitandland.com");
        var option = (await service.GetPageAsync(
            new BinsRunFilterForm { Section = "Actual", WarehouseId = 1000, RoomIds = [1001] }, user, CancellationToken.None))
            .AvailableInventory.Single();

        Assert.Equal("1511", option.GrowerNumber);
        Assert.Null(await service.CreateActualRunAsync(GroupForm((option, 2)), user, CancellationToken.None));
        Assert.Equal("1511", (await db.BinsRunEntries.SingleAsync()).GrowerNumberSnapshot);
    }

    [Fact]
    public async Task ActualRun_TreatmentSegmentsOfSameInventoryDeductExactlyOncePerLine()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var treatments = new SegmentedRoomTreatmentService();
        var service = CreateService(db, roomTreatmentService: treatments);
        var user = Principal("manager@fruitandland.com");
        var options = (await service.GetPageAsync(
                new BinsRunFilterForm { Section = "Actual", WarehouseId = 1000, RoomIds = [1001] }, user, CancellationToken.None))
            .AvailableInventory.Where(x => x.Lot == "LOT-120").OrderBy(x => x.TreatmentSignature).ToArray();

        Assert.Null(await service.CreateActualRunAsync(GroupForm((options[0], 2), (options[1], 3)), user, CancellationToken.None));

        var entries = await db.BinsRunEntries.OrderBy(x => x.TreatmentSignatureSnapshot).ToListAsync();
        Assert.Equal(new[] { "segment-a", "segment-b" }, entries.Select(x => x.TreatmentSignatureSnapshot));
        Assert.Equal(new[] { 120, 118 }, entries.Select(x => x.PreviousAvailableBins));
        Assert.Equal(new[] { 118, 115 }, entries.Select(x => x.NewAvailableBins));
        Assert.Equal(new[] { ("segment-a", 8101L, 2), ("segment-b", 8102L, 3) }, treatments.Moves);
        Assert.Equal(new[] { 120, 118 }, treatments.AuthoritativeBalances);
        var adjustments = await db.RoomInventoryAdjustments.Where(x => x.ActualRunId != null).OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(new[] { -2, -3 }, adjustments.Select(x => x.ChangeAmount));
        Assert.Equal(new int?[] { 120, 118 }, adjustments.Select(x => x.OldBinCount));
        Assert.Equal(new[] { 118, 115 }, adjustments.Select(x => x.NewBinCount));
        Assert.Equal(2, adjustments.Select(x => x.InventoryOperationKey).Distinct().Count());
    }

    [Fact]
    public async Task ActualRun_ProductionShapeSequentialTreatmentLines_DoNotRematerializeConsumedUntreatedBins()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var snapshot = new RoomInventoryLedgerSnapshot(
            1000, "EBS", 1001, "LAMB-15", "Lamb", 2026, 98, 1000,
            "DL & JJ FARMS-HOME CONV", "9100", "9100", null, "GALA", "GALA", "Gala", "Apple",
            "Conventional", false, "Conventional", 225, 0, 0, 0, 0, 0, 0, 0, 225, 225, 1,
            DateTimeOffset.Parse("2026-08-28T00:47:00Z"), DateTimeOffset.Parse("2026-08-28T00:47:00Z"), 1874);
        var treatments = new ProductionShapeSequentialTreatmentService();
        var service = CreateService(
            db,
            roomInventoryLedgerQueryService: new StaticRoomInventoryLedgerQueryService([snapshot]),
            roomTreatmentService: treatments);
        var user = Principal("manager@fruitandland.com");
        var options = (await service.GetPageAsync(
                new BinsRunFilterForm { Section = "Actual", WarehouseId = 1000, RoomIds = [1001] }, user, default))
            .AvailableInventory.ToArray();
        var untreated = Assert.Single(options, x => x.TreatmentSegmentId == 144);
        var mcpReceiptD = Assert.Single(options, x => x.TreatmentSegmentId == 184);

        var error = await service.CreateActualRunAsync(GroupForm((untreated, 132), (mcpReceiptD, 1)), user, default);

        Assert.Null(error);
        Assert.Equal(new[] { 225, 93 }, treatments.AuthoritativeBalances);
        Assert.Equal(new[] { (144L, 132), (184L, 1) }, treatments.Moves);
        Assert.Equal(0, treatments.Bins(144));
        Assert.Equal(24, treatments.Bins(175));
        Assert.Equal(24, treatments.Bins(176));
        Assert.Equal(24, treatments.Bins(180));
        Assert.Equal(20, treatments.Bins(184));
        Assert.Equal(92, treatments.TotalBins);
        Assert.Equal(new[] { 225, 93 }, await db.BinsRunEntries.OrderBy(x => x.Id).Select(x => x.PreviousAvailableBins).ToArrayAsync());
        Assert.Equal(new[] { 93, 92 }, await db.BinsRunEntries.OrderBy(x => x.Id).Select(x => x.NewAvailableBins).ToArrayAsync());
    }

    [Fact]
    public async Task ActualRun_ImplicitUntreatedAndPersistedUntreated_DoesNotThrow()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var treatments = new RecordingTreatmentSelectionService(
        [
            new("u", TreatmentLineageStates.Untreated, 40, "Receipt-specific untreated", 7901, 123),
            new("u", TreatmentLineageStates.Untreated, 80, "Implicit untreated remainder", null, null)
        ]);
        var service = CreateService(db, roomTreatmentService: treatments);
        var user = Principal("manager@fruitandland.com");
        var option = Assert.Single((await service.GetPageAsync(
                new BinsRunFilterForm { Section = "Actual", WarehouseId = 1000, RoomIds = [1001] }, user, default))
            .AvailableInventory, x => x.Lot == "LOT-120" && x.TreatmentSegmentId is null);

        var error = await service.CreateActualRunAsync(GroupForm((option, 5)), user, default);

        Assert.Null(error);
        var move = Assert.Single(treatments.Moves);
        Assert.Equal(("u", (long?)null, (long?)null, 5), move);
        Assert.Equal(115, await LedgerBalanceAsync(db, 1001, "LOT-120"));
        Assert.Single(await db.ActualRuns.ToListAsync());
        Assert.Single(await db.ActualRunRevisions.ToListAsync());
        Assert.Single(await db.BinsRunEntries.ToListAsync());
    }

    [Fact]
    public async Task ActualRun_PersistedTreatmentSegment_UsesExactSegmentIdAndReceipt()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var treatments = new RecordingTreatmentSelectionService(
        [
            new("u", TreatmentLineageStates.Untreated, 40, "Receipt A untreated", 7901, 123),
            new("u", TreatmentLineageStates.Untreated, 80, "Receipt B untreated", 7902, 456)
        ]);
        var service = CreateService(db, roomTreatmentService: treatments);
        var user = Principal("manager@fruitandland.com");
        var option = Assert.Single((await service.GetPageAsync(
                new BinsRunFilterForm { Section = "Actual", WarehouseId = 1000, RoomIds = [1001] }, user, default))
            .AvailableInventory, x => x.Lot == "LOT-120" && x.TreatmentSegmentId == 123);
        var form = GroupForm((option, 5));
        form.Lines[0].TreatmentSegmentId = option.TreatmentSegmentId;

        var error = await service.CreateActualRunAsync(form, user, default);

        Assert.Null(error);
        Assert.Equal(("u", (long?)123, (long?)7901, 5), Assert.Single(treatments.Moves));
    }

    [Fact]
    public async Task ActualRun_LineCorrection_ReversesOldTreatmentAndUsesExactCorrectedSegment()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var treatments = new SegmentedRoomTreatmentService();
        var service = CreateService(db, roomTreatmentService: treatments);
        var manager = Principal("manager@fruitandland.com");
        var admin = Principal("admin@fruitandland.com");
        var page = await service.GetPageAsync(
            new BinsRunFilterForm { Section = "Actual", WarehouseId = 1000, RoomIds = [1001] }, manager, default);
        var segmentA = page.AvailableInventory.Single(x => x.Lot == "LOT-120" && x.TreatmentSegmentId == 8101);
        var create = GroupForm((segmentA, 10));
        create.Lines[0].TreatmentSegmentId = 8101;
        Assert.Null(await service.CreateActualRunAsync(create, manager, default));
        var run = await db.ActualRuns.SingleAsync();
        var editPage = await service.GetPageAsync(
            new BinsRunFilterForm { Section = "Actual", EditActualRunId = run.Id }, admin, default);
        var segmentB = editPage.AvailableInventory.Single(x => x.Lot == "LOT-120" && x.TreatmentSegmentId == 8102);
        var edit = GroupForm((segmentB, 10));
        edit.Lines[0].TreatmentSegmentId = 8102;
        edit.Id = run.Id;
        edit.ConcurrencyVersion = run.ConcurrencyVersion;
        edit.CorrectionReason = "Corrected treatment segment for the selected grower source";

        Assert.Null(await service.UpdateActualRunAsync(run.Id, edit, admin, default));

        Assert.Equal(1, treatments.Reversals);
        Assert.Equal(("segment-b", 8102, 10), treatments.Moves.Last());
        Assert.Equal("segment-b", await db.BinsRunEntries
            .Where(x => x.ActualRunId == run.Id && x.TransactionType == ActualRunTransactionTypes.Depletion && !x.IsReversed)
            .Select(x => x.TreatmentSignatureSnapshot)
            .SingleAsync());
    }

    [Fact]
    public async Task ActualRun_ZeroBalanceCorrectionPageAndPostUseExactPostReversalTreatmentState()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var ledger = new RoomInventoryLedgerQueryService(db);
        var configuration = new ConfigurationBuilder().Build();
        var principal = Principal("manager@fruitandland.com");
        var context = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };
        var treatments = new RoomTreatmentService(
            db,
            ledger,
            new UserAccessService(db, configuration),
            context,
            new PacificBusinessTimeService(new CropQc.Shared.Time.SystemClock()),
            NullLogger<RoomTreatmentService>.Instance);
        var service = CreateService(db, roomInventoryLedgerQueryService: ledger, roomTreatmentService: treatments);
        var createPage = await service.GetPageAsync(
            new BinsRunFilterForm { Section = "Actual", WarehouseId = 1000, RoomIds = [1001] }, principal, default);
        var original = createPage.AvailableInventory.Single(x => x.Lot == "LOT-120");
        Assert.Equal(120, original.CurrentBins);
        Assert.Null(await service.CreateActualRunAsync(GroupForm((original, 120)), principal, default));
        var run = await db.ActualRuns.SingleAsync();
        var originalEntry = await db.BinsRunEntries.SingleAsync(x => !x.IsReversed);
        var originalMovement = await db.TreatmentLineageMovements.SingleAsync(x =>
            x.BinsRunEntryId == originalEntry.Id && x.ReversesTreatmentLineageMovementId == null);
        Assert.Equal(0, await LedgerBalanceAsync(db, 1001, "LOT-120"));

        var beforeGet = new
        {
            ActualRuns = await db.ActualRuns.CountAsync(),
            ActualRunRevisions = await db.ActualRunRevisions.CountAsync(),
            BinsRunEntries = await db.BinsRunEntries.CountAsync(),
            Adjustments = await db.RoomInventoryAdjustments.CountAsync(),
            Segments = await db.TreatmentLineageSegments.CountAsync(),
            Movements = await db.TreatmentLineageMovements.CountAsync(),
            Audits = await db.AuditLogs.CountAsync()
        };

        var editPage = await service.GetPageAsync(
            new BinsRunFilterForm { Section = "Actual", EditActualRunId = run.Id },
            Principal("admin@fruitandland.com"),
            default);

        var correctedSource = Assert.Single(editPage.AvailableInventory, x =>
            x.Lot == "LOT-120" && x.TreatmentSegmentId == originalMovement.SourceSegmentId);
        Assert.Equal(120, correctedSource.CurrentBins);
        Assert.Equal(originalEntry.TreatmentSignatureSnapshot, correctedSource.TreatmentSignature);
        Assert.Equal(originalMovement.TreatmentSignatureSnapshot, correctedSource.TreatmentSignature);
        Assert.Equal(beforeGet.ActualRuns, await db.ActualRuns.CountAsync());
        Assert.Equal(beforeGet.ActualRunRevisions, await db.ActualRunRevisions.CountAsync());
        Assert.Equal(beforeGet.BinsRunEntries, await db.BinsRunEntries.CountAsync());
        Assert.Equal(beforeGet.Adjustments, await db.RoomInventoryAdjustments.CountAsync());
        Assert.Equal(beforeGet.Segments, await db.TreatmentLineageSegments.CountAsync());
        Assert.Equal(beforeGet.Movements, await db.TreatmentLineageMovements.CountAsync());
        Assert.Equal(beforeGet.Audits, await db.AuditLogs.CountAsync());

        var correction = GroupForm((correctedSource, 110));
        correction.Id = run.Id;
        correction.ConcurrencyVersion = run.ConcurrencyVersion;
        correction.CorrectionReason = "Production-shaped zero-balance correction proof";
        Assert.Null(await service.UpdateActualRunAsync(
            run.Id, correction, Principal("admin@fruitandland.com"), default));

        db.ChangeTracker.Clear();
        Assert.Equal(10, await LedgerBalanceAsync(db, 1001, "LOT-120"));
        Assert.Equal(2, await db.ActualRunRevisions.CountAsync());
        Assert.True((await db.BinsRunEntries.SingleAsync(x => x.Id == originalEntry.Id)).IsReversed);
        var activeEntry = await db.BinsRunEntries.SingleAsync(x =>
            x.ActualRunId == run.Id
            && x.TransactionType == ActualRunTransactionTypes.Depletion
            && !x.IsReversed);
        Assert.Equal(110, activeEntry.BinsRun);
        Assert.Equal(originalEntry.TreatmentSignatureSnapshot, activeEntry.TreatmentSignatureSnapshot);
        Assert.Equal(3, await db.TreatmentLineageMovements.CountAsync(x =>
            x.BinsRunEntryId == originalEntry.Id || x.BinsRunEntryId == activeEntry.Id));
        var currentSnapshot = Assert.Single(await ledger.GetSnapshotsAsync(1000, [1001], default), x => x.Lot == "LOT-120");
        var currentTreatment = Assert.Single(await treatments.GetSelectionsAsync(currentSnapshot, default));
        Assert.Equal(10, currentTreatment.CurrentBins);
        Assert.Equal(originalEntry.TreatmentSignatureSnapshot, currentTreatment.TreatmentSignature);
    }

    [Fact]
    public async Task ActualRun_AmbiguousNullSegment_ReturnsValidationAndWritesNothing()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var treatments = new RecordingTreatmentSelectionService(
        [
            new("u", TreatmentLineageStates.Untreated, 40, "Implicit untreated A", null, null),
            new("u", TreatmentLineageStates.Untreated, 80, "Implicit untreated B", null, null)
        ]);
        var service = CreateService(db, roomTreatmentService: treatments);
        var user = Principal("manager@fruitandland.com");
        var option = (await service.GetPageAsync(
                new BinsRunFilterForm { Section = "Actual", WarehouseId = 1000, RoomIds = [1001] }, user, default))
            .AvailableInventory.First(x => x.Lot == "LOT-120");
        var adjustmentCount = await db.RoomInventoryAdjustments.CountAsync();
        var auditCount = await db.AuditLogs.CountAsync();

        var error = await service.CreateActualRunAsync(GroupForm((option, 5)), user, default);

        Assert.Contains("multiple treatment histories", error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.ActualRuns.ToListAsync());
        Assert.Empty(await db.ActualRunRevisions.ToListAsync());
        Assert.Empty(await db.BinsRunEntries.ToListAsync());
        Assert.Empty(await db.TreatmentLineageMovements.ToListAsync());
        Assert.Equal(adjustmentCount, await db.RoomInventoryAdjustments.CountAsync());
        Assert.Equal(auditCount, await db.AuditLogs.CountAsync());
        Assert.Empty(treatments.Moves);
    }

    [Fact]
    public async Task ActualRun_StaleTreatmentSelection_ReturnsValidationAndWritesNothing()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var treatments = new RecordingTreatmentSelectionService(
        [
            new("u", TreatmentLineageStates.Untreated, 40, "Receipt-specific untreated", 7901, 123),
            new("u", TreatmentLineageStates.Untreated, 80, "Implicit untreated remainder", null, null)
        ]);
        var service = CreateService(db, roomTreatmentService: treatments);
        var user = Principal("manager@fruitandland.com");
        var option = Assert.Single((await service.GetPageAsync(
                new BinsRunFilterForm { Section = "Actual", WarehouseId = 1000, RoomIds = [1001] }, user, default))
            .AvailableInventory, x => x.Lot == "LOT-120" && x.TreatmentSegmentId is null);
        var form = GroupForm((option, 5));
        form.Lines[0].TreatmentSegmentId = 999;
        var adjustmentCount = await db.RoomInventoryAdjustments.CountAsync();

        var error = await service.CreateActualRunAsync(form, user, default);

        Assert.Contains("multiple treatment histories", error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.ActualRuns.ToListAsync());
        Assert.Empty(await db.ActualRunRevisions.ToListAsync());
        Assert.Empty(await db.BinsRunEntries.ToListAsync());
        Assert.Equal(adjustmentCount, await db.RoomInventoryAdjustments.CountAsync());
        Assert.Empty(treatments.Moves);
    }

    [Fact]
    public async Task ActualRun_EditPage_DuplicateSignatureSelections_DoesNotThrow()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var treatments = new RecordingTreatmentSelectionService(
        [
            new("u", TreatmentLineageStates.Untreated, 40, "Receipt-specific untreated", 7901, 123),
            new("u", TreatmentLineageStates.Untreated, 80, "Implicit untreated remainder", null, null)
        ]);
        var service = CreateService(db, roomTreatmentService: treatments);
        var user = Principal("manager@fruitandland.com");
        var option = Assert.Single((await service.GetPageAsync(
                new BinsRunFilterForm { Section = "Actual", WarehouseId = 1000, RoomIds = [1001] }, user, default))
            .AvailableInventory, x => x.Lot == "LOT-120" && x.TreatmentSegmentId is null);
        Assert.Null(await service.CreateActualRunAsync(GroupForm((option, 5)), user, default));
        var runId = await db.ActualRuns.Select(x => x.Id).SingleAsync();

        var editPage = await service.GetPageAsync(new BinsRunFilterForm
        {
            Section = "Actual",
            WarehouseId = 1000,
            RoomIds = [1001],
            EditActualRunId = runId
        }, user, default);

        var editLine = Assert.Single(editPage.ActualRunForm.Lines);
        Assert.Equal("u", editLine.TreatmentSignature);
        Assert.Null(editLine.TreatmentSegmentId);
        Assert.Equal(85, editLine.ExpectedAvailableBins);
    }

    [Fact]
    public async Task ActualRun_SharedUserSelectsReportingFacilityWithoutRestrictingSourceInventory()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var employee = await db.Users.SingleAsync(x => x.Email == "manager@fruitandland.com");
        employee.EmploymentFacility = EmploymentFacilities.Shared;
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var principal = Principal(employee.Email);
        var option = (await service.GetPageAsync(new BinsRunFilterForm { Section = "Actual", RoomIds = [1001] }, principal, CancellationToken.None))
            .AvailableInventory.Single(x => x.Lot == "LOT-120");
        var wp = await db.Warehouses.Where(x => x.Code == EmploymentFacilities.Wp).OrderByDescending(x => x.Id).FirstAsync();
        var form = GroupForm((option, 10));
        form.RunFacilityWarehouseId = wp.Id;
        form.SalesDeskId = 1;

        Assert.Null(await service.CreateActualRunAsync(form, principal, CancellationToken.None));

        var run = await db.ActualRuns.SingleAsync();
        var line = await db.BinsRunEntries.SingleAsync();
        Assert.Equal(EmploymentFacilities.Wp, run.RunFacilityCodeSnapshot);
        Assert.Equal(RunFacilityAssignmentSources.SharedSelection, run.RunFacilityAssignmentSource);
        Assert.Equal(EmploymentFacilities.Ebs, line.Warehouse.Code);
        Assert.Equal(EmploymentFacilities.Wp, line.ReportingFacilityCodeSnapshot);

        employee.EmploymentFacility = EmploymentFacilities.Ebs;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(EmploymentFacilities.Wp, (await db.ActualRuns.SingleAsync()).RunFacilityCodeSnapshot);
    }

    [Fact]
    public async Task ActualRun_NewWpRunRequiresActiveSalesDeskAndPersistsSnapshot()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var employee = await db.Users.SingleAsync(x => x.Email == "manager@fruitandland.com");
        employee.EmploymentFacility = EmploymentFacilities.Wp;
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var principal = Principal(employee.Email);
        var option = (await service.GetPageAsync(new BinsRunFilterForm { Section = "Actual", RoomIds = [1001] }, principal, default))
            .AvailableInventory.Single(x => x.Lot == "LOT-120");
        var missing = GroupForm((option, 10));

        Assert.Equal("Select a Sales Desk for this WP Actual Run.", await service.CreateActualRunAsync(missing, principal, default));
        Assert.Empty(db.ActualRuns);

        var valid = GroupForm((option, 10));
        valid.SalesDeskId = 1;
        Assert.Null(await service.CreateActualRunAsync(valid, principal, default));
        var run = await db.ActualRuns.SingleAsync();
        Assert.Equal(1, run.SalesDeskId);
        Assert.Equal("Domex", run.SalesDeskNameSnapshot);
        Assert.Equal(10, await db.BinsRunEntries.SumAsync(x => x.BinsRun));
        Assert.Contains(await db.AuditLogs.ToListAsync(), x => x.Action == "ActualRunSalesDeskAssigned");

        var inactiveDesk = await db.SalesDesks.SingleAsync(x => x.Id == 2);
        inactiveDesk.IsActive = false;
        await db.SaveChangesAsync();
        var inactive = GroupForm((option, 1));
        inactive.SalesDeskId = inactiveDesk.Id;
        Assert.Contains("active Sales Desk", await service.CreateActualRunAsync(inactive, principal, default));
    }

    [Fact]
    public async Task ActualRun_EbsRunRejectsTamperedSalesDesk()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var principal = Principal("manager@fruitandland.com");
        var option = (await service.GetPageAsync(new BinsRunFilterForm { Section = "Actual", RoomIds = [1001] }, principal, default))
            .AvailableInventory.Single(x => x.Lot == "LOT-120");
        var form = GroupForm((option, 10));
        form.SalesDeskId = 1;

        Assert.Equal("Sales Desk attribution is not valid for EBS Actual Runs.", await service.CreateActualRunAsync(form, principal, default));
        Assert.Empty(db.ActualRuns);
        Assert.Empty(db.BinsRunEntries);
    }

    [Fact]
    public async Task ActualRun_SalesDeskCorrectionIsAdminOnlyIdempotentAndInventoryNeutral()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var managerRecord = await db.Users.SingleAsync(x => x.Email == "manager@fruitandland.com");
        managerRecord.EmploymentFacility = EmploymentFacilities.Wp;
        var adminRecord = await db.Users.SingleAsync(x => x.Email == "admin@fruitandland.com");
        adminRecord.EmploymentFacility = EmploymentFacilities.Wp;
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var manager = Principal(managerRecord.Email);
        var admin = Principal(adminRecord.Email);
        var option = (await service.GetPageAsync(new BinsRunFilterForm { Section = "Actual", RoomIds = [1001] }, manager, default))
            .AvailableInventory.Single(x => x.Lot == "LOT-120");
        var create = GroupForm((option, 10));
        create.SalesDeskId = 1;
        Assert.Null(await service.CreateActualRunAsync(create, manager, default));
        var run = await db.ActualRuns.SingleAsync();
        var adjustmentFingerprint = await db.RoomInventoryAdjustments.OrderBy(x => x.Id).Select(x => new { x.Id, x.ChangeAmount, x.OldBinCount, x.NewBinCount }).ToListAsync();
        var entryFingerprint = await db.BinsRunEntries.OrderBy(x => x.Id).Select(x => new { x.Id, x.BinsRun, x.IsReversed }).ToListAsync();
        var expectationFingerprint = await db.RunExpectations.OrderBy(x => x.Id).Select(x => new { x.Id, x.TotalBins }).ToListAsync();
        var operationKey = Guid.NewGuid().ToString("N");
        var correction = new CorrectActualRunSalesDeskForm { Id = run.Id, ConcurrencyVersion = run.ConcurrencyVersion, OperationKey = operationKey, SalesDeskId = 2, Reason = "Reviewed WP sales attribution" };

        Assert.Contains("Admin access", await service.CorrectActualRunSalesDeskAsync(correction, manager, default));
        Assert.Null(await service.CorrectActualRunSalesDeskAsync(correction, admin, default));
        Assert.Null(await service.CorrectActualRunSalesDeskAsync(correction, admin, default));

        db.ChangeTracker.Clear();
        run = await db.ActualRuns.SingleAsync();
        Assert.Equal(2, run.SalesDeskId);
        Assert.Equal("Honey Bear", run.SalesDeskNameSnapshot);
        var history = await db.ActualRunSalesDeskCorrections.SingleAsync();
        Assert.Equal("Domex", history.PreviousSalesDeskNameSnapshot);
        Assert.Equal("Honey Bear", history.NewSalesDeskNameSnapshot);
        Assert.Equal("Reviewed WP sales attribution", history.Reason);
        Assert.Equal(adjustmentFingerprint, await db.RoomInventoryAdjustments.OrderBy(x => x.Id).Select(x => new { x.Id, x.ChangeAmount, x.OldBinCount, x.NewBinCount }).ToListAsync());
        Assert.Equal(entryFingerprint, await db.BinsRunEntries.OrderBy(x => x.Id).Select(x => new { x.Id, x.BinsRun, x.IsReversed }).ToListAsync());
        Assert.Equal(expectationFingerprint, await db.RunExpectations.OrderBy(x => x.Id).Select(x => new { x.Id, x.TotalBins }).ToListAsync());
        Assert.Contains(await db.AuditLogs.ToListAsync(), x => x.Action == "ActualRunSalesDeskCorrected" && x.AfterValuesJson!.Contains("RunQuantityDelta"));
    }

    [Fact]
    public async Task ActualRun_HistoricalSalesDeskAssignmentIsAuditedAndOperationallyNeutral()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var run = new ActualRun
        {
            Status = ActualRunStatuses.Active,
            CurrentRevisionNumber = 1,
            ConcurrencyVersion = 3,
            RunAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            RunFacilityCodeSnapshot = EmploymentFacilities.Wp
        };
        db.ActualRuns.Add(run);
        await db.SaveChangesAsync();
        var operationalRowsBefore = await db.RoomInventoryAdjustments.CountAsync() + await db.BinsRunEntries.CountAsync();

        Assert.Null(await CreateService(db).CorrectActualRunSalesDeskAsync(new CorrectActualRunSalesDeskForm
        {
            Id = run.Id,
            ConcurrencyVersion = run.ConcurrencyVersion,
            OperationKey = Guid.NewGuid().ToString("N"),
            SalesDeskId = 3,
            Reason = "Historical sales review"
        }, Principal("admin@fruitandland.com"), default));

        Assert.Equal(3, run.SalesDeskId);
        Assert.Equal("Viva Tierra", run.SalesDeskNameSnapshot);
        Assert.Equal(operationalRowsBefore, await db.RoomInventoryAdjustments.CountAsync() + await db.BinsRunEntries.CountAsync());
        Assert.Contains(await db.AuditLogs.ToListAsync(), x => x.Action == "ActualRunSalesDeskAssigned" && x.AfterValuesJson!.Contains("RunQuantityDelta"));
    }

    [Fact]
    public async Task ActualRun_DetailCorrectionIsAdminOnlyIdempotentAndOperationallyNeutral()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var run = new ActualRun
        {
            Status = ActualRunStatuses.Active,
            CurrentRevisionNumber = 1,
            ConcurrencyVersion = 4,
            RunAt = DateTimeOffset.Parse("2026-09-02T18:30:00Z"),
            Notes = "Entered Tuesday",
            CreatedAt = DateTimeOffset.Parse("2026-09-02T18:31:00Z"),
            RunFacilityCodeSnapshot = EmploymentFacilities.Wp,
            SalesDeskId = 1,
            SalesDeskNameSnapshot = "Domex"
        };
        db.ActualRuns.Add(run);
        await db.SaveChangesAsync();
        var protectedCounts = new
        {
            Entries = await db.BinsRunEntries.CountAsync(),
            Adjustments = await db.RoomInventoryAdjustments.CountAsync(),
            Revisions = await db.ActualRunRevisions.CountAsync(),
            Expectations = await db.RunExpectations.CountAsync(),
            Packouts = await db.PackoutRuns.CountAsync(),
            Treatments = await db.TreatmentLineageMovements.CountAsync()
        };
        var operationKey = Guid.NewGuid().ToString("N");
        var form = new CorrectActualRunDetailsForm
        {
            Id = run.Id,
            ConcurrencyVersion = 4,
            OperationKey = operationKey,
            RunAt = DateTimeOffset.Parse("2026-09-01T18:30:00Z"),
            Notes = "Physical run was Monday",
            Reason = "Reviewed production record"
        };
        var service = CreateService(db);

        Assert.Contains("Admin access", (await service.CorrectActualRunDetailsAsync(form, Principal("manager@fruitandland.com"), default)).Error);
        var applied = await service.CorrectActualRunDetailsAsync(form, Principal("admin@fruitandland.com"), default);
        var duplicate = await service.CorrectActualRunDetailsAsync(form, Principal("admin@fruitandland.com"), default);
        var mismatchedReuse = await service.CorrectActualRunDetailsAsync(new CorrectActualRunDetailsForm
        {
            Id = form.Id,
            ConcurrencyVersion = form.ConcurrencyVersion,
            OperationKey = form.OperationKey,
            RunAt = form.RunAt,
            Notes = "Different payload",
            Reason = form.Reason
        }, Principal("admin@fruitandland.com"), default);

        Assert.Null(applied.Error);
        Assert.False(applied.AlreadyApplied);
        Assert.Null(duplicate.Error);
        Assert.True(duplicate.AlreadyApplied);
        Assert.Contains("different run detail correction", mismatchedReuse.Error);
        db.ChangeTracker.Clear();
        var corrected = await db.ActualRuns.SingleAsync(x => x.Id == run.Id);
        Assert.Equal(form.RunAt, corrected.RunAt);
        Assert.Equal(form.Notes, corrected.Notes);
        Assert.Equal(5, corrected.ConcurrencyVersion);
        Assert.Equal(1, corrected.CurrentRevisionNumber);
        Assert.Equal(1, corrected.SalesDeskId);
        Assert.Equal(EmploymentFacilities.Wp, corrected.RunFacilityCodeSnapshot);
        var history = Assert.Single(await db.ActualRunDetailCorrections.ToListAsync());
        Assert.Equal(DateTimeOffset.Parse("2026-09-02T18:30:00Z"), history.PreviousRunAt);
        Assert.Equal(form.RunAt, history.NewRunAt);
        Assert.Equal("Reviewed production record", history.Reason);
        Assert.Single(await db.AuditLogs.Where(x => x.Action == "ActualRunDetailsCorrected").ToListAsync());
        Assert.Equal(protectedCounts.Entries, await db.BinsRunEntries.CountAsync());
        Assert.Equal(protectedCounts.Adjustments, await db.RoomInventoryAdjustments.CountAsync());
        Assert.Equal(protectedCounts.Revisions, await db.ActualRunRevisions.CountAsync());
        Assert.Equal(protectedCounts.Expectations, await db.RunExpectations.CountAsync());
        Assert.Equal(protectedCounts.Packouts, await db.PackoutRuns.CountAsync());
        Assert.Equal(protectedCounts.Treatments, await db.TreatmentLineageMovements.CountAsync());
    }

    [Fact]
    public async Task ActualRun_DetailCorrectionRejectsNoChangeStaleCanceledFutureAndOperationKeyMismatch()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var run = new ActualRun
        {
            Status = ActualRunStatuses.Active,
            CurrentRevisionNumber = 1,
            ConcurrencyVersion = 3,
            RunAt = DateTimeOffset.Parse("2026-08-01T18:30:00Z"),
            Notes = "Same",
            CreatedAt = DateTimeOffset.Parse("2026-08-01T18:31:00Z")
        };
        db.ActualRuns.Add(run);
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var admin = Principal("admin@fruitandland.com");

        var noChange = await service.CorrectActualRunDetailsAsync(new CorrectActualRunDetailsForm
        {
            Id = run.Id,
            ConcurrencyVersion = 3,
            RunAt = run.RunAt,
            Notes = run.Notes,
            Reason = "Reviewed"
        }, admin, default);
        Assert.Equal("No run details changed.", noChange.Error);
        Assert.Empty(db.ActualRunDetailCorrections);

        var stale = await service.CorrectActualRunDetailsAsync(new CorrectActualRunDetailsForm
        {
            Id = run.Id,
            ConcurrencyVersion = 2,
            RunAt = run.RunAt.AddDays(-1),
            Reason = "Stale"
        }, admin, default);
        Assert.Contains("Conflict detected", stale.Error);

        var future = await service.CorrectActualRunDetailsAsync(new CorrectActualRunDetailsForm
        {
            Id = run.Id,
            ConcurrencyVersion = 3,
            RunAt = DateTimeOffset.UtcNow.AddHours(1),
            Reason = "Future"
        }, admin, default);
        Assert.Contains("future", future.Error, StringComparison.OrdinalIgnoreCase);

        run.Status = ActualRunStatuses.Canceled;
        await db.SaveChangesAsync();
        var canceled = await service.CorrectActualRunDetailsAsync(new CorrectActualRunDetailsForm
        {
            Id = run.Id,
            ConcurrencyVersion = 3,
            RunAt = run.RunAt.AddDays(-1),
            Reason = "Canceled"
        }, admin, default);
        Assert.Contains("active", canceled.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(db.ActualRunDetailCorrections);
    }

    [Fact]
    public void ActualRunDetailCorrectionUiEndpointAndCompatibilityPackageExposeSafetyControls()
    {
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "BinsRun", "ActualRunDetail.cshtml"));
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "BinsRunController.cs"));
        var preflight = File.ReadAllText(FindRepositoryFile("scripts", "postgresql", "preflight-actual-run-detail-corrections.sql"));
        var apply = File.ReadAllText(FindRepositoryFile("scripts", "postgresql", "apply-actual-run-detail-corrections-schema.sql"));
        var verify = File.ReadAllText(FindRepositoryFile("scripts", "postgresql", "verify-actual-run-detail-corrections.sql"));

        Assert.Contains("Edit Run Details", view);
        Assert.Contains("Apply Run Detail Correction", view);
        Assert.Contains("datetime-local", view);
        Assert.Contains("The run date was corrected after this Run Expectation was frozen", view);
        Assert.Contains("[HttpPost(\"ActualRuns/{id:long}/Details\")]", controller);
        Assert.Contains("AccessPolicyNames.ActualRunsAdmin", controller);
        Assert.Contains("[ValidateAntiForgeryToken]", controller);
        Assert.Contains("State C", preflight);
        Assert.Contains("pg_advisory_xact_lock", apply);
        Assert.Contains("cropqc.test_force_actual_run_detail_failure", apply);
        Assert.Contains("__EFMigrationsHistory", verify);
    }

    [Fact]
    public async Task SalesDeskMasterLifecycleIsDynamicAndAudited()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var admin = new AdminManagementService(db, new VarietyColorService(db));
        const string actor = "admin@fruitandland.com";

        Assert.Null(await admin.SaveMasterDataAsync(new MasterDataEditForm
        {
            Type = "sales-desks",
            Name = "Fourth Desk",
            DisplayOrder = 40,
            IsActive = true
        }, actor, default));
        var desk = await db.SalesDesks.SingleAsync(x => x.Name == "Fourth Desk");
        Assert.Null(await admin.SaveMasterDataAsync(new MasterDataEditForm
        {
            Type = "sales-desks",
            Id = desk.Id,
            Name = "Fourth Desk Updated",
            DisplayOrder = 35,
            IsActive = true
        }, actor, default));
        Assert.Null(await admin.DeactivateAsync("sales-desks", desk.Id, actor, default));
        Assert.False(desk.IsActive);
        Assert.Null(await admin.SaveMasterDataAsync(new MasterDataEditForm
        {
            Type = "sales-desks",
            Id = desk.Id,
            Name = desk.Name,
            DisplayOrder = desk.DisplayOrder,
            IsActive = true
        }, actor, default));
        Assert.True(desk.IsActive);

        var actions = await db.AuditLogs.Where(x => x.EntityName == nameof(SalesDesk)).Select(x => x.Action).ToListAsync();
        Assert.Contains("SalesDeskCreated", actions);
        Assert.Contains("SalesDeskUpdated", actions);
        Assert.Contains("SalesDeskDeactivated", actions);
        Assert.Contains("SalesDeskReactivated", actions);
    }

    [Fact]
    public async Task ActualRun_SalesDeskCorrectionFailsClosedOnStaleRun()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var run = new ActualRun
        {
            Status = ActualRunStatuses.Active,
            CurrentRevisionNumber = 1,
            ConcurrencyVersion = 5,
            RunAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            RunFacilityCodeSnapshot = EmploymentFacilities.Wp,
            SalesDeskId = 1,
            SalesDeskNameSnapshot = "Domex"
        };
        db.ActualRuns.Add(run);
        await db.SaveChangesAsync();

        var error = await CreateService(db).CorrectActualRunSalesDeskAsync(new CorrectActualRunSalesDeskForm
        {
            Id = run.Id,
            ConcurrencyVersion = 4,
            SalesDeskId = 2,
            Reason = "Stale attempt"
        }, Principal("admin@fruitandland.com"), default);

        Assert.Contains("Conflict detected", error);
        Assert.Empty(db.ActualRunSalesDeskCorrections);
        Assert.Equal(1, (await db.ActualRuns.SingleAsync()).SalesDeskId);
    }

    [Fact]
    public void ActualRunSalesDeskUiAndSchemaPackageExposeRequiredSafetyControls()
    {
        var index = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "BinsRun", "Index.cshtml"));
        var detail = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "BinsRun", "ActualRunDetail.cshtml"));
        var master = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "MasterData", "Index.cshtml"));
        var preflight = File.ReadAllText(FindRepositoryFile("scripts", "postgresql", "preflight-actual-run-sales-desk-attribution.sql"));
        var apply = File.ReadAllText(FindRepositoryFile("scripts", "postgresql", "apply-actual-run-sales-desk-attribution-schema.sql"));
        var verify = File.ReadAllText(FindRepositoryFile("scripts", "postgresql", "verify-actual-run-sales-desk-attribution.sql"));

        Assert.Contains("Select Sales Desk", index);
        Assert.Contains("N/A for EBS", index);
        Assert.Contains("WP Total reconciles exactly", index);
        Assert.Contains("Unassigned historical runs", index);
        Assert.Contains("Apply Sales Desk Correction", detail);
        Assert.Contains("Inventory, treatment lineage, run quantities, and frozen expectations are unchanged", detail);
        Assert.Contains("Sales Desks", master);
        Assert.Contains("State C", preflight);
        Assert.Contains("pg_advisory_xact_lock", apply);
        Assert.Contains("cropqc.test_force_sales_desk_failure", apply);
        Assert.Contains("unassigned_wp_runs", verify);
        Assert.Contains("__EFMigrationsHistory", verify);
    }

    [Fact]
    public async Task ActualRun_UnassignedUserIsBlockedAndEmploymentUserCannotChooseOtherFacility()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var employee = await db.Users.SingleAsync(x => x.Email == "manager@fruitandland.com");
        var service = CreateService(db);
        var principal = Principal(employee.Email);
        var option = (await service.GetPageAsync(new BinsRunFilterForm { Section = "Actual", RoomIds = [1001] }, principal, CancellationToken.None))
            .AvailableInventory.Single(x => x.Lot == "LOT-120");
        var wp = await db.Warehouses.Where(x => x.Code == EmploymentFacilities.Wp).OrderByDescending(x => x.Id).FirstAsync();
        var wrongFacility = GroupForm((option, 10));
        wrongFacility.RunFacilityWarehouseId = wp.Id;

        Assert.Contains("requires this run to be credited to EBS", await service.CreateActualRunAsync(wrongFacility, principal, CancellationToken.None));

        employee.EmploymentFacility = EmploymentFacilities.Unassigned;
        await db.SaveChangesAsync();
        Assert.Contains("assign your Employment Facility", await service.CreateActualRunAsync(GroupForm((option, 10)), principal, CancellationToken.None));
        Assert.Empty(await db.ActualRuns.ToListAsync());
    }

    [Fact]
    public async Task ActualRunDetail_LoadsCreatedRunExpectationAndEmptySupportingDocumentArea()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var user = Principal("manager@fruitandland.com");
        var option = (await service.GetPageAsync(
            new BinsRunFilterForm { Section = "Actual", RoomIds = [1001] },
            user,
            CancellationToken.None))
            .AvailableInventory.Single(x => x.Lot == "LOT-120");
        Assert.Null(await service.CreateActualRunAsync(GroupForm((option, 10)), user, CancellationToken.None));
        var runId = await db.ActualRuns.Select(x => x.Id).SingleAsync();

        var detail = await service.GetActualRunDetailAsync(runId, user, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal(runId, detail!.Id);
        Assert.NotNull(detail.CurrentExpectation);
        Assert.Null(detail.Packout);
        Assert.True(detail.OptionalDetailAvailable);
        Assert.Null(detail.DetailWarning);
    }

    [Fact]
    public async Task ActualRunDetail_LoadsLegacyRunWithoutExpectationOrPackout()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var user = Principal("manager@fruitandland.com");
        var option = (await service.GetPageAsync(
            new BinsRunFilterForm { Section = "Actual", RoomIds = [1001] },
            user,
            CancellationToken.None))
            .AvailableInventory.Single(x => x.Lot == "LOT-120");
        Assert.Null(await service.CreateActualRunAsync(GroupForm((option, 10)), user, CancellationToken.None));
        var runId = await db.ActualRuns.Select(x => x.Id).SingleAsync();
        db.RunExpectationSources.RemoveRange(db.RunExpectationSources);
        db.RunExpectations.RemoveRange(db.RunExpectations);
        await db.SaveChangesAsync();

        var detail = await service.GetActualRunDetailAsync(runId, user, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Empty(detail!.Expectations);
        Assert.Null(detail.CurrentExpectation);
        Assert.Null(detail.Packout);
        Assert.True(detail.OptionalDetailAvailable);
        Assert.NotEmpty(detail.Contributions);
    }

    [Fact]
    public void ActualRunHistory_LinkTargetsDetailRouteAndDetailViewExposesSupportingDocuments()
    {
        var index = File.ReadAllText(FindRepositoryFile(
            "src", "CropQc.Web", "Views", "BinsRun", "Index.cshtml"));
        var detail = File.ReadAllText(FindRepositoryFile(
            "src", "CropQc.Web", "Views", "BinsRun", "ActualRunDetail.cshtml"));
        var controller = File.ReadAllText(FindRepositoryFile(
            "src", "CropQc.Web", "Controllers", "BinsRunController.cs"));

        Assert.Contains("href=\"/BinsRun/ActualRuns/@run.Id\"", index);
        Assert.Contains("[HttpGet(\"ActualRuns/{id:long}\")]", controller);
        Assert.Contains("Packout Result and supporting documents", detail);
        Assert.Contains("No Packout Result has been uploaded", detail);
        Assert.Contains("<dt>Source lines</dt>", detail);
        Assert.Contains("<th>Grower number</th>", detail);
        Assert.Contains("<th>Organic / Conventional</th>", detail);
        Assert.Contains("<th>Status</th>", detail);
        Assert.DoesNotContain("The dashboard could not complete the request", detail);
    }

    [Fact]
    public void TransferCustody_IsDefaultOff_HidesNewModes_AndServerBlocksEveryMutation()
    {
        var controller = File.ReadAllText(FindRepositoryFile(
            "src", "CropQc.Web", "Controllers", "BinsRunController.cs"));
        var view = File.ReadAllText(FindRepositoryFile(
            "src", "CropQc.Web", "Views", "BinsRun", "Index.cshtml"));
        var settings = File.ReadAllText(FindRepositoryFile(
            "src", "CropQc.Web", "appsettings.json"));

        Assert.Contains("TransferCustody:Enabled", controller);
        Assert.Contains("if (Model.TransferCustodyEnabled)", view);
        Assert.Contains("\"Enabled\": false", settings);
        Assert.True(controller.Split("if (!TransferCustodyEnabled) return NotFound();", StringSplitOptions.None).Length - 1 >= 8);
        Assert.Contains("TransferType = \"Internal\"", controller);
    }

    [Fact]
    public void ReleaseReadinessCommand_IsMachineEnforcedAndIncludesAllRequiredGates()
    {
        var program = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Program.cs"));

        Assert.Contains("--verify-release-readiness", File.ReadAllText(FindRepositoryFile(
            "src", "CropQc.Web", "Services", "TreatmentLineage144CorrectionService.cs")));
        Assert.Contains("20260904030132_ReintroduceQcPhotoOrientation", program);
        Assert.Contains("expectedSchemaObjects = 909", program);
        Assert.Contains("VerifyReadinessAsync", program);
        Assert.Contains("topology", program);
        Assert.Contains("Environment.ExitCode = releaseReady ? 0 : 1", program);
    }

    [Fact]
    public void ActualRunPackoutUpload_PreservesEachSelectedReportAsADistinctSource()
    {
        var service = File.ReadAllText(FindRepositoryFile(
            "src", "CropQc.Web", "Services", "PackoutReconciliationService.cs"));
        var model = File.ReadAllText(FindRepositoryFile(
            "src", "CropQc.Data", "Entities", "PackoutReconciliationModels.cs"));

        Assert.Contains("for (var index = 0; index < form.Files.Count; index++)", service);
        Assert.Contains("run.Sources.Add(source);", service);
        Assert.Contains("PackoutReportSource = source", service);
        Assert.Contains("ICollection<PackoutReportSource> Sources", model);
    }

    [Fact]
    public async Task ActualRun_RunExpectationFailure_RollsBackRunLedgerAndInventoryAtomically()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CropQcDbContext>()
            .UseSqlite(connection)
            .Options;
        await using var db = new CropQcDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await SeedActualRunLedgerOnlyAsync(db);
        var baselineAdjustments = await db.RoomInventoryAdjustments.CountAsync();
        var service = CreateService(
            db,
            new ThrowingRunExpectationService(),
            new StaticRoomInventoryLedgerQueryService(
            [
                new(
                    1000, "ART", 1001, "Evans-12", "", 2026, null, 1000,
                    "Test Grower", "1084", "LOT-120", null, "ACTUALRUNTEST", "ACTUALRUNTEST",
                    "Actual Run Test Apple", "Apple", "Conventional", false, "",
                    120, 0, 0, 0, 0, 0, 0, 0, 120, 120, 1,
                    DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 8001)
            ]));
        var user = Principal("manager@fruitandland.com");
        var option = new BinsRunInventoryOptionViewModel(
            "L:1000:1001:2026:LOT-120:ACTUALRUNTEST:1000",
            null,
            8001,
            1000,
            1001,
            "Evans-12 / LOT-120",
            "Test Grower",
            "LOT-120",
            "ACTUALRUNTEST",
            "Evans-12",
            120,
            "",
            null,
            1000,
            "Apple",
            null,
            2026,
            "Conventional");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateActualRunAsync(GroupForm((option, 10)), user, CancellationToken.None));

        db.ChangeTracker.Clear();
        Assert.Empty(await db.ActualRuns.AsNoTracking().ToListAsync());
        Assert.Empty(await db.ActualRunRevisions.AsNoTracking().ToListAsync());
        Assert.Empty(await db.BinsRunEntries.AsNoTracking().ToListAsync());
        Assert.Empty(await db.RunExpectations.AsNoTracking().ToListAsync());
        Assert.Equal(baselineAdjustments, await db.RoomInventoryAdjustments.AsNoTracking().CountAsync());
    }

    [Fact]
    public async Task ActualRun_UpdateWithUnchangedLines_UsesMetadataCorrectionWithoutInventoryRevision()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var manager = Principal("manager@fruitandland.com");
        var admin = Principal("admin@fruitandland.com");
        var option = (await service.GetPageAsync(
            new BinsRunFilterForm { Section = "Actual", RoomIds = [1001] }, manager, default))
            .AvailableInventory.Single(x => x.Lot == "LOT-120");
        Assert.Null(await service.CreateActualRunAsync(GroupForm((option, 64)), manager, default));
        var run = await db.ActualRuns.SingleAsync();
        var originalEntry = await db.BinsRunEntries.SingleAsync();
        var adjustmentCount = await db.RoomInventoryAdjustments.CountAsync();
        var revisedAt = run.RunAt.AddDays(-1);
        var edit = GroupForm((option, 64));
        edit.Id = run.Id;
        edit.ConcurrencyVersion = run.ConcurrencyVersion;
        edit.RunAt = revisedAt;
        edit.Notes = "Corrected date only";
        edit.CorrectionReason = "Reviewed production paperwork";

        Assert.Null(await service.UpdateActualRunAsync(run.Id, edit, admin, default));

        db.ChangeTracker.Clear();
        var corrected = await db.ActualRuns.SingleAsync();
        Assert.Equal(revisedAt, corrected.RunAt);
        Assert.Equal("Corrected date only", corrected.Notes);
        Assert.Single(await db.ActualRunRevisions.ToListAsync());
        Assert.Single(await db.ActualRunDetailCorrections.ToListAsync());
        Assert.Equal(adjustmentCount, await db.RoomInventoryAdjustments.CountAsync());
        Assert.False((await db.BinsRunEntries.SingleAsync(x => x.Id == originalEntry.Id)).IsReversed);
        Assert.Equal(56, await LedgerBalanceAsync(db, 1001, "LOT-120"));
    }

    [Fact]
    public async Task ActualRun_BinDecrease_UsesRevisionAndRestoresExactDifference()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var manager = Principal("manager@fruitandland.com");
        var admin = Principal("admin@fruitandland.com");
        var option = (await service.GetPageAsync(
            new BinsRunFilterForm { Section = "Actual", RoomIds = [1001] }, manager, default))
            .AvailableInventory.Single(x => x.Lot == "LOT-120");
        Assert.Null(await service.CreateActualRunAsync(GroupForm((option, 64)), manager, default));
        var run = await db.ActualRuns.SingleAsync();
        var editPage = await service.GetPageAsync(
            new BinsRunFilterForm { Section = "Actual", EditActualRunId = run.Id }, admin, default);
        var current = editPage.AvailableInventory.Single(x => x.Lot == "LOT-120");
        var edit = GroupForm((current, 58));
        edit.Id = run.Id;
        edit.ConcurrencyVersion = run.ConcurrencyVersion;
        Assert.Contains("reason is required", await service.UpdateActualRunAsync(run.Id, edit, admin, default), StringComparison.OrdinalIgnoreCase);
        Assert.Single(await db.ActualRunRevisions.ToListAsync());
        Assert.Equal(56, await LedgerBalanceAsync(db, 1001, "LOT-120"));
        edit.CorrectionReason = "Operator entered 64 bins; actual count was 58";

        Assert.Null(await service.UpdateActualRunAsync(run.Id, edit, admin, default));

        db.ChangeTracker.Clear();
        Assert.Equal(62, await LedgerBalanceAsync(db, 1001, "LOT-120"));
        Assert.Equal(58, await db.BinsRunEntries
            .Where(x => x.ActualRunId == run.Id && x.TransactionType == ActualRunTransactionTypes.Depletion && !x.IsReversed)
            .SumAsync(x => x.BinsRun));
        var revision = await db.ActualRunRevisions.SingleAsync(x => x.IsCurrent);
        Assert.Equal("Operator entered 64 bins; actual count was 58", revision.Reason);
        Assert.Equal(2, revision.RevisionNumber);
    }

    [Fact]
    public async Task ActualRun_GrowerAndBinCorrection_MovesExactInventoryAndIsIdempotent()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var targetBaseline = await db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 8004);
        targetBaseline.ChangeAmount = 100;
        targetBaseline.NewBinCount = 100;
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var manager = Principal("manager@fruitandland.com");
        var admin = Principal("admin@fruitandland.com");
        var createPage = await service.GetPageAsync(
            new BinsRunFilterForm { Section = "Actual", RoomIds = [1001] }, manager, default);
        var wrongGrower = createPage.AvailableInventory.Single(x => x.Lot == "LOT-120");
        Assert.Null(await service.CreateActualRunAsync(GroupForm((wrongGrower, 120)), manager, default));
        Assert.Equal(0, await LedgerBalanceAsync(db, 1001, "LOT-120"));
        var run = await db.ActualRuns.SingleAsync();
        var correctedAt = run.RunAt.AddDays(-1);
        var editPage = await service.GetPageAsync(
            new BinsRunFilterForm { Section = "Actual", EditActualRunId = run.Id }, admin, default);
        var correctGrower = editPage.AvailableInventory.Single(x => x.Lot == "LOT-30");
        var edit = GroupForm((correctGrower, 58));
        edit.Id = run.Id;
        edit.ConcurrencyVersion = run.ConcurrencyVersion;
        edit.RunAt = correctedAt;
        edit.CorrectionReason = "Run entered under wrong grower and wrong bin count";

        Assert.Contains("Admin access", await service.UpdateActualRunAsync(run.Id, edit, manager, default));
        Assert.Null(await service.UpdateActualRunAsync(run.Id, edit, admin, default));
        var entryCount = await db.BinsRunEntries.CountAsync();
        var adjustmentCount = await db.RoomInventoryAdjustments.CountAsync();
        Assert.Null(await service.UpdateActualRunAsync(run.Id, edit, admin, default));

        db.ChangeTracker.Clear();
        var corrected = await db.ActualRuns.SingleAsync();
        Assert.Equal(correctedAt, corrected.RunAt);
        Assert.Equal(120, await LedgerBalanceAsync(db, 1001, "LOT-120"));
        Assert.Equal(42, await LedgerBalanceAsync(db, 1001, "LOT-30"));
        Assert.Equal(entryCount, await db.BinsRunEntries.CountAsync());
        Assert.Equal(adjustmentCount, await db.RoomInventoryAdjustments.CountAsync());
        Assert.Equal(2, await db.ActualRunRevisions.CountAsync());
        var active = await db.BinsRunEntries.SingleAsync(x =>
            x.ActualRunId == run.Id && x.TransactionType == ActualRunTransactionTypes.Depletion && !x.IsReversed);
        Assert.Equal("LOT-30", active.LotNumber);
        Assert.Equal(58, active.BinsRun);
        Assert.Equal(1011, active.GrowerLotId);
        var detail = await service.GetActualRunDetailAsync(run.Id, admin, default);
        Assert.NotNull(detail);
        Assert.Equal(58, detail.TotalBins);
        Assert.Equal(new[] { 1, 2 }, detail.RunRevisions.Select(x => x.RevisionNumber));
        Assert.Equal("LOT-120", Assert.Single(detail.RunRevisions[0].Lines).Lot);
        Assert.Equal("LOT-30", Assert.Single(detail.RunRevisions[1].Lines).Lot);
        Assert.True(detail.RunRevisions[1].IsCurrent);
        var reportingConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RunReporting:AuthoritativeStartCropYear"] = "2026"
        }).Build();
        var reporting = new RunReportingService(
            db,
            new PacificBusinessTimeService(new CropQc.Shared.Time.SystemClock()),
            new UserAccessService(db, reportingConfiguration),
            reportingConfiguration,
            new VarietyColorService(db));
        var totals = Assert.IsType<RunTotalsDetailViewModel>((await reporting.GetAsync(new BinsRunFilterForm
        {
            Section = "RunTotals",
            ReportFacility = EmploymentFacilities.Ebs,
            ReportCropYear = 2026
        }, admin, default)).Detail);
        Assert.Equal(58, totals.TotalBins);
        var selectedVariety = Assert.Single(totals.Varieties, x => x.Bins == 58);
        var growerTotals = Assert.IsType<RunTotalsDetailViewModel>((await reporting.GetAsync(new BinsRunFilterForm
        {
            Section = "RunTotals",
            ReportFacility = EmploymentFacilities.Ebs,
            ReportCropYear = 2026,
            ReportVarietyKey = selectedVariety.VarietyKey
        }, admin, default)).Detail);
        Assert.Equal(58, Assert.Single(growerTotals.Weeks).Bins);
        var correctedGrower = Assert.Single(Assert.Single(growerTotals.Weeks).Growers);
        Assert.Equal("LOT-30", correctedGrower.GrowerNumber);
        Assert.Equal(58, correctedGrower.Bins);
        var readiness = await new InventoryDeductionInvariantService(
            db,
            NullLogger<InventoryDeductionInvariantService>.Instance).VerifyReadinessAsync(default);
        Assert.True(readiness.IsReady, string.Join("; ", readiness.Issues.Select(x => x.Code)));
    }

    [Fact]
    public async Task ActualRun_BinIncrease_UsesRevisionAndEnforcesAvailableInventory()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var manager = Principal("manager@fruitandland.com");
        var admin = Principal("admin@fruitandland.com");
        var option = (await service.GetPageAsync(
            new BinsRunFilterForm { Section = "Actual", RoomIds = [1001] }, manager, default))
            .AvailableInventory.Single(x => x.Lot == "LOT-120");
        Assert.Null(await service.CreateActualRunAsync(GroupForm((option, 58)), manager, default));
        var run = await db.ActualRuns.SingleAsync();
        var editPage = await service.GetPageAsync(
            new BinsRunFilterForm { Section = "Actual", EditActualRunId = run.Id }, admin, default);
        var current = editPage.AvailableInventory.Single(x => x.Lot == "LOT-120");
        var edit = GroupForm((current, 64));
        edit.Id = run.Id;
        edit.ConcurrencyVersion = run.ConcurrencyVersion;
        edit.CorrectionReason = "Physical count confirmed";

        Assert.Null(await service.UpdateActualRunAsync(run.Id, edit, admin, default));

        Assert.Equal(56, await LedgerBalanceAsync(db, 1001, "LOT-120"));
        Assert.Equal(64, await db.BinsRunEntries
            .Where(x => x.ActualRunId == run.Id && x.TransactionType == ActualRunTransactionTypes.Depletion && !x.IsReversed)
            .SumAsync(x => x.BinsRun));
    }

    [Fact]
    public async Task ActualRun_LineCorrectionFailsClosedWhenPackoutIsFinalized()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var manager = Principal("manager@fruitandland.com");
        var admin = Principal("admin@fruitandland.com");
        var option = (await service.GetPageAsync(
            new BinsRunFilterForm { Section = "Actual", RoomIds = [1001] }, manager, default))
            .AvailableInventory.Single(x => x.Lot == "LOT-120");
        Assert.Null(await service.CreateActualRunAsync(GroupForm((option, 20)), manager, default));
        var run = await db.ActualRuns.SingleAsync();
        var entry = await db.BinsRunEntries.SingleAsync();
        entry.IsReconciled = true;
        await db.SaveChangesAsync();
        var baselineEntries = await db.BinsRunEntries.CountAsync();
        var baselineAdjustments = await db.RoomInventoryAdjustments.CountAsync();
        var edit = GroupForm((option, 19));
        edit.Id = run.Id;
        edit.ConcurrencyVersion = run.ConcurrencyVersion;
        edit.CorrectionReason = "Physical count reviewed";

        var error = await service.UpdateActualRunAsync(run.Id, edit, admin, default);

        Assert.Contains("finalized packout reconciliation", error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(baselineEntries, await db.BinsRunEntries.CountAsync());
        Assert.Equal(baselineAdjustments, await db.RoomInventoryAdjustments.CountAsync());
        Assert.Single(await db.ActualRunRevisions.ToListAsync());
    }

    [Fact]
    public async Task ActualRun_MultiLineCorrection_PreservesUnchangedLineExactlyOnce()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var manager = Principal("manager@fruitandland.com");
        var admin = Principal("admin@fruitandland.com");
        var page = await service.GetPageAsync(
            new BinsRunFilterForm { Section = "Actual", RoomIds = [1001, 1002] }, manager, default);
        var changedSource = page.AvailableInventory.Single(x => x.RoomId == 1001 && x.Lot == "LOT-120");
        var unchangedSource = page.AvailableInventory.Single(x => x.RoomId == 1002 && x.Lot == "LOT-OTHER");
        Assert.Null(await service.CreateActualRunAsync(
            GroupForm((changedSource, 40), (unchangedSource, 30)), manager, default));
        var run = await db.ActualRuns.SingleAsync();
        var editPage = await service.GetPageAsync(
            new BinsRunFilterForm { Section = "Actual", EditActualRunId = run.Id }, admin, default);
        var changedCurrent = editPage.AvailableInventory.Single(x => x.RoomId == 1001 && x.Lot == "LOT-120");
        var unchangedCurrent = editPage.AvailableInventory.Single(x => x.RoomId == 1002 && x.Lot == "LOT-OTHER");
        var edit = GroupForm((changedCurrent, 35), (unchangedCurrent, 30));
        edit.Id = run.Id;
        edit.ConcurrencyVersion = run.ConcurrencyVersion;
        edit.CorrectionReason = "First grower line was five bins too high";

        Assert.Null(await service.UpdateActualRunAsync(run.Id, edit, admin, default));

        var currentLines = await db.BinsRunEntries
            .Where(x => x.ActualRunId == run.Id
                && x.TransactionType == ActualRunTransactionTypes.Depletion
                && !x.IsReversed)
            .OrderBy(x => x.RoomId)
            .Select(x => new { x.RoomId, x.LotNumber, x.BinsRun })
            .ToListAsync();
        Assert.Equal(2, currentLines.Count);
        Assert.Contains(currentLines, x => x.RoomId == 1001 && x.LotNumber == "LOT-120" && x.BinsRun == 35);
        Assert.Single(currentLines, x => x.RoomId == 1002 && x.LotNumber == "LOT-OTHER" && x.BinsRun == 30);
        Assert.Equal(85, await LedgerBalanceAsync(db, 1001, "LOT-120"));
        Assert.Equal(30, await LedgerBalanceAsync(db, 1002, "LOT-OTHER"));
        Assert.Equal(65, currentLines.Sum(x => x.BinsRun));
        Assert.Equal(2, await db.ActualRunRevisions.CountAsync());
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

        edit.CorrectionReason = "Correct source allocation";
        var editError = await service.UpdateActualRunAsync(run.Id, edit, admin, CancellationToken.None);

        Assert.Null(editError);
        await db.Entry(run).ReloadAsync();
        Assert.Equal(2, run.CurrentRevisionNumber);
        Assert.Equal(2, run.ConcurrencyVersion);
        Assert.Equal(2, await db.RunExpectations.CountAsync(x => x.ActualRunId == run.Id));
        Assert.Equal(
            new[] { 1, 2 },
            await db.RunExpectations
                .Where(x => x.ActualRunId == run.Id)
                .OrderBy(x => x.RevisionNumber)
                .Select(x => x.RevisionNumber)
                .ToArrayAsync());
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
        stale.CorrectionReason = "Stale correction";
        var entriesBeforeStale = await db.BinsRunEntries.CountAsync();
        var adjustmentsBeforeStale = await db.RoomInventoryAdjustments.CountAsync();
        var revisionsBeforeStale = await db.ActualRunRevisions.CountAsync();
        Assert.Contains("Conflict detected", await service.UpdateActualRunAsync(run.Id, stale, admin, CancellationToken.None));
        Assert.Equal(entriesBeforeStale, await db.BinsRunEntries.CountAsync());
        Assert.Equal(adjustmentsBeforeStale, await db.RoomInventoryAdjustments.CountAsync());
        Assert.Equal(revisionsBeforeStale, await db.ActualRunRevisions.CountAsync());

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
        Assert.Equal(EmploymentFacilities.Ebs, run.RunFacilityCodeSnapshot);
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
            DestinationWarehouseId = 1000,
            DestinationRoomId = 1002,
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
    public async Task Stale_transfer_source_destination_and_physical_reversal_honor_room_seal()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var managerService = CreateDashboardService(db, Principal("manager@fruitandland.com"));
        var sourceLot = (await managerService.GetRoomDetailAsync(1001, default))
            .TransferLotOptions.Single(x => x.Label.Contains("LOT-120", StringComparison.OrdinalIgnoreCase));
        var baselineAdjustments = await db.RoomInventoryAdjustments.CountAsync();
        RoomTransferForm Form(string key) => new()
        {
            OperationKey = key,
            FromRoomId = 1001,
            DestinationWarehouseId = 1000,
            DestinationRoomId = 1002,
            SourceLotKey = sourceLot.LotKey,
            TreatmentSignature = sourceLot.TreatmentSignature,
            BinCount = 10,
            TransferAt = DateTimeOffset.UtcNow,
            Reason = "Room seal regression"
        };

        var sourceRoom = await db.Rooms.SingleAsync(x => x.Id == 1001);
        var destinationRoom = await db.Rooms.SingleAsync(x => x.Id == 1002);
        sourceRoom.IsSealed = true;
        await db.SaveChangesAsync();
        Assert.Contains("sealed", await managerService.CreateRoomTransferAsync(Form("sealed-source"), default), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.RoomTransfers.ToListAsync());
        Assert.Equal(baselineAdjustments, await db.RoomInventoryAdjustments.CountAsync());

        sourceRoom.IsSealed = false;
        destinationRoom.IsSealed = true;
        await db.SaveChangesAsync();
        Assert.Contains("sealed", await managerService.CreateRoomTransferAsync(Form("sealed-destination"), default), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.RoomTransfers.ToListAsync());
        Assert.Equal(baselineAdjustments, await db.RoomInventoryAdjustments.CountAsync());

        destinationRoom.IsSealed = false;
        await db.SaveChangesAsync();
        Assert.Null(await managerService.CreateRoomTransferAsync(Form("unsealed-transfer"), default));
        var original = await db.RoomTransfers.SingleAsync();
        var postTransferAdjustments = await db.RoomInventoryAdjustments.CountAsync();

        sourceRoom.IsSealed = true;
        await db.SaveChangesAsync();
        var adminService = CreateDashboardService(db, Principal("admin@fruitandland.com"));
        var reversalError = await adminService.ReverseRoomTransferAsync(new ReverseRoomTransferForm
        {
            Id = original.Id,
            OperationKey = "sealed-transfer-reversal",
            Reason = "Physical return"
        }, default);

        Assert.Contains("sealed", reversalError, StringComparison.OrdinalIgnoreCase);
        Assert.False((await db.RoomTransfers.SingleAsync(x => x.Id == original.Id)).IsReversed);
        Assert.Single(await db.RoomTransfers.ToListAsync());
        Assert.Equal(postTransferAdjustments, await db.RoomInventoryAdjustments.CountAsync());
    }

    [Fact]
    public async Task Future_scheduled_seal_allows_transfer_before_effective_time_then_stale_destination_fails_atomically()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateDashboardService(db, Principal("manager@fruitandland.com"));
        var sourceLot = (await service.GetRoomDetailAsync(1001, default))
            .TransferLotOptions.Single(x => x.Label.Contains("LOT-120", StringComparison.OrdinalIgnoreCase));
        RoomTransferForm Form(string key) => new()
        {
            OperationKey = key,
            FromRoomId = 1001,
            DestinationWarehouseId = 1000,
            DestinationRoomId = 1002,
            SourceLotKey = sourceLot.LotKey,
            TreatmentSignature = sourceLot.TreatmentSignature,
            BinCount = 5,
            TransferAt = DateTimeOffset.UtcNow,
            Reason = "Scheduled seal boundary regression"
        };

        var source = await db.Rooms.SingleAsync(x => x.Id == 1001);
        source.IsSealed = true;
        source.SealedAt = DateTimeOffset.UtcNow.AddMinutes(30);
        source.SealRecordedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync();
        Assert.Null(await service.CreateRoomTransferAsync(Form("before-scheduled-seal"), default));
        Assert.Single(await db.RoomTransfers.ToListAsync());

        var destination = await db.Rooms.SingleAsync(x => x.Id == 1002);
        destination.IsSealed = true;
        destination.SealedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        destination.SealRecordedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        await db.SaveChangesAsync();
        var transfers = await db.RoomTransfers.CountAsync();
        var adjustments = await db.RoomInventoryAdjustments.CountAsync();
        var treatments = await db.TreatmentLineageMovements.CountAsync();

        Assert.Contains("sealed", await service.CreateRoomTransferAsync(Form("after-destination-seal"), default), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(transfers, await db.RoomTransfers.CountAsync());
        Assert.Equal(adjustments, await db.RoomInventoryAdjustments.CountAsync());
        Assert.Equal(treatments, await db.TreatmentLineageMovements.CountAsync());
    }

    [Fact]
    public async Task RoomTransfer_DestinationFacilitiesAreLimitedToTheSameReceivingCrew()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        foreach (var existing in await db.Rooms.Where(x => x.Id != 1001 && x.Id != 1002).ToListAsync()) existing.IsActive = false;
        var wp = await db.Warehouses.SingleAsync(x => x.Code == "WP");
        var mcd = await db.Warehouses.SingleAsync(x => x.Code == "McDougall");
        var dh = await db.Warehouses.SingleAsync(x => x.Code == "DH");
        db.Rooms.AddRange(
            new Room { Id = 1010, Warehouse = wp, WarehouseId = wp.Id, Code = "WP-02", Name = "WP 02", SortOrder = 2, IsActive = true },
            new Room { Id = 1011, Warehouse = wp, WarehouseId = wp.Id, Code = "WP-01", Name = "WP 01", SortOrder = 1, IsActive = true },
            new Room { Id = 1012, Warehouse = wp, WarehouseId = wp.Id, Code = "WP-OFF", Name = "WP inactive", SortOrder = 0, IsActive = false },
            new Room { Id = 1020, Warehouse = mcd, WarehouseId = mcd.Id, Code = "MCD-03", Name = "MCD 03", SortOrder = 1, IsActive = true },
            new Room { Id = 1030, Warehouse = dh, WarehouseId = dh.Id, Code = "DH-01", Name = "DH 01", SortOrder = 1, IsActive = true });
        await db.SaveChangesAsync();

        var page = await CreateDashboardService(db, Principal("manager@fruitandland.com"))
            .GetRoomDetailAsync(1001, CancellationToken.None);

        Assert.Equal(["EBS"], page.TransferDestinationFacilities.Select(x => x.Label));
        Assert.Equal(1000, page.TransferDestinationFacilities.Single(x => x.Label == "EBS").WarehouseId);
        Assert.Equal("McDougall", (await db.Warehouses.AsNoTracking().SingleAsync(x => x.Id == mcd.Id)).Code);
        Assert.Equal(1000, page.TransferForm.DestinationWarehouseId);
        Assert.DoesNotContain(page.TransferDestinationOptions, x => x.RoomId is 1001 or 1012);
        Assert.Equal([1002], page.TransferDestinationOptions.Select(x => x.RoomId));

        var wpPage = await CreateDashboardService(db, Principal("manager@fruitandland.com"))
            .GetRoomDetailAsync(1011, CancellationToken.None);
        Assert.Equal(["WP", "DH"], wpPage.TransferDestinationFacilities.Select(x => x.Label));
        Assert.Equal([1010, 1030], wpPage.TransferDestinationOptions.Select(x => x.RoomId));

        var mcdPage = await CreateDashboardService(db, Principal("manager@fruitandland.com"))
            .GetRoomDetailAsync(1020, CancellationToken.None);
        Assert.Equal(["MCD"], mcdPage.TransferDestinationFacilities.Select(x => x.Label));
        Assert.Empty(mcdPage.TransferDestinationOptions);
        var activeRoomIds = await db.Rooms.AsNoTracking().Where(x => x.IsActive).Select(x => x.Id).ToHashSetAsync();
        Assert.All(page.TransferDestinationOptions, x => Assert.Contains(x.RoomId, activeRoomIds));
    }

    [Fact]
    public async Task RoomTransfer_ServerRejectsTamperedFacilityRoomInactiveAndSourceDestinationCombinations()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var mcd = await db.Warehouses.SingleAsync(x => x.Code == "McDougall");
        var dh = await db.Warehouses.SingleAsync(x => x.Code == "DH");
        var mcdRoom = new Room { Id = 1020, Warehouse = mcd, WarehouseId = mcd.Id, Code = "MCD-03", Name = "MCD 03", IsActive = true };
        var inactive = new Room { Id = 1021, Warehouse = mcd, WarehouseId = mcd.Id, Code = "MCD-OFF", Name = "MCD inactive", IsActive = false };
        db.Rooms.AddRange(mcdRoom, inactive);
        await db.SaveChangesAsync();
        var service = CreateDashboardService(db, Principal("manager@fruitandland.com"));
        var source = (await service.GetRoomDetailAsync(1001, CancellationToken.None)).TransferLotOptions.First(x => x.Label.Contains("LOT-120"));
        var beforeAdjustments = await db.RoomInventoryAdjustments.CountAsync();
        RoomTransferForm Form(int warehouseId, int roomId, string key) => new()
        {
            OperationKey = key,
            FromRoomId = 1001,
            DestinationWarehouseId = warehouseId,
            DestinationRoomId = roomId,
            SourceLotKey = source.LotKey,
            TreatmentSignature = source.TreatmentSignature,
            BinCount = 5,
            TransferAt = DateTimeOffset.UtcNow,
            Reason = "Destination validation"
        };

        Assert.Contains("does not belong", await service.CreateRoomTransferAsync(Form(dh.Id, mcdRoom.Id, "mismatch"), default));
        Assert.Contains("inactive", await service.CreateRoomTransferAsync(Form(mcd.Id, inactive.Id, "inactive"), default));
        Assert.Contains("different", await service.CreateRoomTransferAsync(Form(1000, 1001, "same-room"), default));
        Assert.Empty(await db.RoomTransfers.ToListAsync());
        Assert.Equal(beforeAdjustments, await db.RoomInventoryAdjustments.CountAsync());
    }

    [Fact]
    public async Task RoomTransfer_CrossCrewDestinationFailsClosedAndRequiresCustodyWorkflow()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var mcd = await db.Warehouses.SingleAsync(x => x.Code == "McDougall");
        var destination = new Room { Id = 1020, Warehouse = mcd, WarehouseId = mcd.Id, Code = "MCD-03", Name = "MCD 03", CropQcRoomName = "MCD-03", IsActive = true };
        db.Rooms.Add(destination);
        await db.SaveChangesAsync();
        var service = CreateDashboardService(db, Principal("manager@fruitandland.com"));
        var source = (await service.GetRoomDetailAsync(1001, default)).TransferLotOptions.First(x => x.Label.Contains("LOT-120"));

        var error = await service.CreateRoomTransferAsync(new RoomTransferForm
        {
            OperationKey = "cross-facility-partial",
            FromRoomId = 1001,
            DestinationWarehouseId = mcd.Id,
            DestinationRoomId = destination.Id,
            SourceLotKey = source.LotKey,
            TreatmentSignature = source.TreatmentSignature,
            BinCount = 10,
            TransferAt = DateTimeOffset.UtcNow,
            Reason = "Cross-facility regression"
        }, default);

        Assert.Contains("Transfer to Another Crew", error);
        Assert.Empty(await db.RoomTransfers.ToListAsync());
        Assert.Equal(120, await LedgerBalanceAsync(db, 1001, "LOT-120"));
        Assert.Equal(0, await LedgerBalanceAsync(db, destination.Id, "LOT-120"));
        Assert.Equal("McDougall", (await db.Warehouses.AsNoTracking().SingleAsync(x => x.Id == mcd.Id)).Code);
    }

    [Fact]
    public void RoomTransfer_ViewProvidesFacilityFilteringReviewAndIPhoneLayout()
    {
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "BinsRun", "Index.cshtml"));
        var css = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "wwwroot", "css", "site.css"));

        Assert.Contains("Destination Facility", view);
        Assert.Contains("name=\"DestinationWarehouseId\"", view);
        Assert.Contains("name=\"DestinationRoomId\"", view);
        Assert.Contains("data-warehouse-id", view);
        Assert.Contains("option.disabled = !visible", view);
        Assert.Contains("room.value = ''", view);
        Assert.Contains("Fruit to transfer", view);
        Assert.Contains("Current Room Inventory", view);
        Assert.Contains("Available in transfer selections", view);
        Assert.Contains("TransferInventoryReconciles", view);
        Assert.Contains("TransferInventoryError", view);
        Assert.Contains("data-treatment-signature", view);
        Assert.Contains("Review Transfer", view);
        Assert.Contains("FirstOrDefault(x => x.WarehouseId == Model.RoomSummary?.WarehouseId)?.Label", view);
        Assert.Contains("if (!reviewReady)", view);
        Assert.Contains("data-review-source-facility", view);
        Assert.Contains("data-review-destination-facility", view);
        Assert.Contains("FROM", view);
        Assert.Contains("TO", view);
        Assert.Contains("@media (max-width: 430px)", css);
        Assert.Contains(".transfer-workflow", css);
        Assert.Contains("overflow: hidden", css);
        Assert.Contains("grid-template-columns: minmax(0, 1fr)", css);
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
        edit.CorrectionReason = "Disposable PostgreSQL workflow verification";
        var admin = Principal("admin@fruitandland.com");
        Assert.Null(await service.UpdateActualRunAsync(run.Id, edit, admin, CancellationToken.None));
        await db.Entry(run).ReloadAsync();
        var cancel = new CancelActualRunForm
        {
            Id = run.Id,
            ConcurrencyVersion = run.ConcurrencyVersion,
            OperationKey = Guid.NewGuid().ToString("N"),
            Reason = "Disposable PostgreSQL workflow verification"
        };
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
        var legacyForm = ActualRunForm(legacyOption);
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
            DestinationWarehouseId = 1000,
            DestinationRoomId = 1002,
            SourceLotKey = sourceLot.LotKey,
            BinCount = 10,
            TransferAt = DateTimeOffset.Parse("2026-08-19T19:00:00Z"),
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
    public async Task PostgreSql_ActualRunDetailCorrectionIsNeutralAndCanonicalReportingTranslates_WhenConfigured()
    {
        var connectionString = Environment.GetEnvironmentVariable("CROPQC_ACTUAL_RUN_DETAIL_POSTGRES");
        if (string.IsNullOrWhiteSpace(connectionString)) return;

        ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(connectionString);
        var optionsBuilder = new DbContextOptionsBuilder<CropQcDbContext>();
        CropQcDatabase.Configure(optionsBuilder, DatabaseProviders.PostgreSql, connectionString);
        await using var db = new CropQcDbContext(optionsBuilder.Options);
        Assert.True(await db.Database.EnsureCreatedAsync(), "The configured Actual Run detail PostgreSQL database must start empty.");
        await db.FruitProfiles.ExecuteDeleteAsync();
        await db.SampleTypes.ExecuteDeleteAsync();
        await db.Grades.ExecuteDeleteAsync();
        await SeedInventoryAsync(db, useSeededEbsWarehouse: true);
        var service = CreateService(db);
        var manager = Principal("manager@fruitandland.com");
        var admin = Principal("admin@fruitandland.com");
        var option = (await service.GetPageAsync(new BinsRunFilterForm { Section = "Actual", RoomIds = [1001] }, manager, default))
            .AvailableInventory.Single(x => x.Lot == "LOT-120");
        var create = GroupForm((option, 10));
        create.RunAt = DateTimeOffset.Parse("2026-08-03T18:30:00Z");
        Assert.Null(await service.CreateActualRunAsync(create, manager, default));
        var run = await db.ActualRuns.SingleAsync();
        var entryFingerprint = await db.BinsRunEntries.Where(x => x.ActualRunId == run.Id)
            .Select(x => new { x.Id, x.RunAt, x.BinsRun, x.InventoryAdjustmentId, x.ActualRunRevisionId }).SingleAsync();
        var adjustmentFingerprint = await db.RoomInventoryAdjustments.Where(x => x.ActualRunId == run.Id)
            .Select(x => new { x.Id, x.AdjustmentAt, x.ChangeAmount, x.OldBinCount, x.NewBinCount }).SingleAsync();
        var expectationFingerprint = await db.RunExpectations.Where(x => x.ActualRunId == run.Id)
            .Select(x => new { x.Id, x.RunAtSnapshot, x.TotalBins }).SingleAsync();

        var result = await service.CorrectActualRunDetailsAsync(new CorrectActualRunDetailsForm
        {
            Id = run.Id,
            ConcurrencyVersion = run.ConcurrencyVersion,
            OperationKey = Guid.NewGuid().ToString("N"),
            RunAt = DateTimeOffset.Parse("2026-08-01T18:30:00Z"),
            Notes = "PostgreSQL correction proof",
            Reason = "Physical run date reviewed"
        }, admin, default);
        Assert.Null(result.Error);

        db.ChangeTracker.Clear();
        Assert.Equal(entryFingerprint, await db.BinsRunEntries.Where(x => x.ActualRunId == run.Id)
            .Select(x => new { x.Id, x.RunAt, x.BinsRun, x.InventoryAdjustmentId, x.ActualRunRevisionId }).SingleAsync());
        Assert.Equal(adjustmentFingerprint, await db.RoomInventoryAdjustments.Where(x => x.ActualRunId == run.Id)
            .Select(x => new { x.Id, x.AdjustmentAt, x.ChangeAmount, x.OldBinCount, x.NewBinCount }).SingleAsync());
        Assert.Equal(expectationFingerprint, await db.RunExpectations.Where(x => x.ActualRunId == run.Id)
            .Select(x => new { x.Id, x.RunAtSnapshot, x.TotalBins }).SingleAsync());
        Assert.Single(await db.ActualRunDetailCorrections.ToListAsync());
        Assert.Single(await db.AuditLogs.Where(x => x.Action == "ActualRunDetailsCorrected").ToListAsync());

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RunReporting:AuthoritativeStartCropYear"] = "2026"
        }).Build();
        var reporting = new RunReportingService(
            db,
            new PacificBusinessTimeService(new CropQc.Shared.Time.SystemClock()),
            new UserAccessService(db, configuration),
            configuration,
            new VarietyColorService(db));
        var page = await reporting.GetAsync(new BinsRunFilterForm
        {
            Section = "RunTotals",
            ReportFacility = EmploymentFacilities.Ebs,
            ReportCropYear = 2026
        }, manager, default);
        Assert.Equal(10, Assert.IsType<RunTotalsDetailViewModel>(page.Detail).TotalBins);
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
    public async Task MCD09_CompatibleReceiptSourcesReconcileToOneExactTransferChoice()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var warehouse = await db.Warehouses.SingleAsync(x => x.Id == 1000);
        var room = await db.Rooms.SingleAsync(x => x.Id == 1001);
        var fruit = await db.FruitProfiles.SingleAsync(x => x.Id == 1000);
        var linkedLot = new GrowerLot
        {
            Id = 448,
            Grower = "Production-shaped grower",
            LotNumber = "1372",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var unlinkedReceipt = new Receipt
        {
            Id = 99001,
            CropYear = 2026,
            ReceivedAt = DateTimeOffset.UtcNow.AddDays(-2),
            CompuTechReceiptId = "MCD09-A",
            ReceiptType = "Truck receipt",
            Warehouse = warehouse,
            Room = room,
            FruitProfile = fruit,
            GrowerName = "Production-shaped grower",
            GrowerNumber = "1372",
            LotCode = "1372",
            BinCount = 158,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var linkedReceipt = new Receipt
        {
            Id = 99002,
            CropYear = 2026,
            ReceivedAt = DateTimeOffset.UtcNow.AddDays(-1),
            CompuTechReceiptId = "MCD09-B",
            ReceiptType = "Truck receipt",
            Warehouse = warehouse,
            Room = room,
            FruitProfile = fruit,
            GrowerLot = linkedLot,
            GrowerLotId = linkedLot.Id,
            GrowerName = "Production-shaped grower",
            GrowerNumber = "1372",
            LotCode = "1372",
            BinCount = 128,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.AddRange(linkedLot, unlinkedReceipt, linkedReceipt);
        db.RoomInventoryAdjustments.AddRange(
            SourceAdjustment(99001, unlinkedReceipt, null, 158),
            SourceAdjustment(99002, linkedReceipt, linkedLot.Id, 128));
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var ledger = new RoomInventoryLedgerQueryService(db);
        var canonical = Assert.Single(
            await ledger.GetSnapshotsAsync(warehouse.Id, [room.Id], default),
            x => x.Lot == "1372");
        Assert.Equal(286, canonical.CurrentBins);
        Assert.Equal(448, canonical.GrowerLotId);
        Assert.Equal(2, canonical.TransactionCount);

        var principal = Principal("manager@fruitandland.com");
        var configuration = new ConfigurationBuilder().Build();
        var context = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };
        var access = new UserAccessService(db, configuration);
        var treatments = new RoomTreatmentService(
            db,
            ledger,
            access,
            context,
            new PacificBusinessTimeService(new CropQc.Shared.Time.SystemClock()),
            NullLogger<RoomTreatmentService>.Instance);
        var detail = await CreateDashboardService(db, principal, ledger, treatments)
            .GetRoomDetailAsync(room.Id, default);
        var option = Assert.Single(
            detail.TransferLotOptions,
            x => x.Label.Contains("1372", StringComparison.Ordinal));
        Assert.Equal(286, option.CurrentBins);
        Assert.True(detail.TransferInventoryReconciles);
        Assert.Equal(detail.CurrentLots.Sum(x => x.CurrentBins), detail.TransferCurrentRoomBins);
        Assert.Equal(detail.TransferCurrentRoomBins, detail.TransferAvailableBins);
        Assert.Equal(detail.TransferCurrentRoomBins, detail.TransferLotOptions.Sum(x => x.CurrentBins));
        Assert.Null(detail.TransferInventoryError);
        Assert.Null(detail.DataWarning);

        RoomInventoryAdjustment SourceAdjustment(long id, Receipt receipt, int? growerLotId, int bins) => new()
        {
            Id = id,
            CropYear = 2026,
            Receipt = receipt,
            ReceiptId = receipt.Id,
            Warehouse = warehouse,
            WarehouseId = warehouse.Id,
            Room = room,
            RoomId = room.Id,
            GrowerLotId = growerLotId,
            FruitProfile = fruit,
            FruitProfileId = fruit.Id,
            GrowerName = receipt.GrowerName,
            LotNumber = receipt.LotCode,
            VarietyCode = fruit.VarietyCode,
            ChangeAmount = bins,
            NewBinCount = bins,
            AdjustmentType = "ReceiptAdd",
            AdjustmentAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            InventoryInvariantVersion = 2
        };
    }

    [Fact]
    public async Task RoomDetail_PreservesCurrentInventoryWhenTreatmentProjectionFails()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var roomId = await db.Rooms.Select(x => x.Id).FirstAsync();

        var detail = await CreateDashboardService(
                db,
                Principal("manager@fruitandland.com"),
                new RoomInventoryLedgerQueryService(db),
                new ThrowingRoomTreatmentService())
            .GetRoomDetailAsync(roomId, default);

        Assert.NotEmpty(detail.CurrentLots);
        Assert.Empty(detail.TransferLotOptions);
        Assert.NotNull(detail.DataWarning);
        Assert.Contains("Current inventory is shown", detail.DataWarning, StringComparison.Ordinal);
        Assert.False(detail.TransferInventoryReconciles);
        Assert.Equal(detail.CurrentLots.Sum(x => x.CurrentBins), detail.TransferCurrentRoomBins);
        Assert.Equal(0, detail.TransferAvailableBins);
        Assert.Contains("does not reconcile", detail.TransferInventoryError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RoomTransfer_TreatmentProjectionMismatchFailsClosedBeforeAnyWrite()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var principal = Principal("manager@fruitandland.com");
        var safeDetail = await CreateDashboardService(db, principal).GetRoomDetailAsync(1001, default);
        var source = safeDetail.TransferLotOptions.First(x => x.Label.Contains("LOT-120", StringComparison.Ordinal));
        var beforeTransfers = await db.RoomTransfers.CountAsync();
        var beforeAdjustments = await db.RoomInventoryAdjustments.CountAsync();
        var beforeAudits = await db.AuditLogs.CountAsync();
        var service = CreateDashboardService(
            db,
            principal,
            new RoomInventoryLedgerQueryService(db),
            new ShortTreatmentProjectionService());

        var detail = await service.GetRoomDetailAsync(1001, default);
        var error = await service.CreateRoomTransferAsync(new RoomTransferForm
        {
            OperationKey = "projection-mismatch",
            FromRoomId = 1001,
            DestinationWarehouseId = 1000,
            DestinationRoomId = 1002,
            SourceLotKey = source.LotKey,
            TreatmentSignature = source.TreatmentSignature,
            BinCount = 1,
            TransferAt = DateTimeOffset.UtcNow,
            Reason = "Must fail before writing"
        }, default);

        Assert.False(detail.TransferInventoryReconciles);
        Assert.NotEqual(detail.TransferCurrentRoomBins, detail.TransferAvailableBins);
        Assert.Empty(detail.TransferLotOptions);
        Assert.Equal("Transfer inventory does not reconcile with the Room's current inventory. No transfer was recorded. Refresh or review inventory reconciliation.", error);
        Assert.Equal(beforeTransfers, await db.RoomTransfers.CountAsync());
        Assert.Equal(beforeAdjustments, await db.RoomInventoryAdjustments.CountAsync());
        Assert.Equal(beforeAudits, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task RoomTransfer_ExactTreatmentSegmentCannotBeOverdrawnEvenWithLegacyOverrideFlag()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var principal = Principal("manager@fruitandland.com");
        var service = CreateDashboardService(db, principal);
        var detail = await service.GetRoomDetailAsync(1001, default);
        var source = detail.TransferLotOptions.First(x => x.Label.Contains("LOT-120", StringComparison.Ordinal));
        var beforeTransfers = await db.RoomTransfers.CountAsync();
        var beforeAdjustments = await db.RoomInventoryAdjustments.CountAsync();

        var error = await service.CreateRoomTransferAsync(new RoomTransferForm
        {
            OperationKey = "exact-segment-overdraw",
            FromRoomId = 1001,
            DestinationWarehouseId = 1000,
            DestinationRoomId = 1002,
            SourceLotKey = source.LotKey,
            TreatmentSignature = source.TreatmentSignature,
            BinCount = source.CurrentBins + 1,
            ConfirmOverTransfer = true,
            TransferAt = DateTimeOffset.UtcNow,
            Reason = "Must fail closed"
        }, default);

        Assert.Contains("exact selected treatment segment", error, StringComparison.Ordinal);
        Assert.Equal(beforeTransfers, await db.RoomTransfers.CountAsync());
        Assert.Equal(beforeAdjustments, await db.RoomInventoryAdjustments.CountAsync());
    }

    [Fact]
    public async Task AllActiveRooms_CardDetailCurrentInventoryAndTransferUseOneAuthoritativeTotal()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var configuration = new ConfigurationBuilder().Build();
        var ledger = new RoomInventoryLedgerQueryService(db);
        var dashboard = CreateDashboardService(db, Principal("manager@fruitandland.com"), ledger);
        var currentInventory = new RoomInventoryImportService(
            db,
            null!,
            new CropYearService(db, configuration),
            ledger);
        var cards = await dashboard.GetRoomsAsync(
            new RoomSummaryFilterForm { Facility = "All", EbsLocation = "All EBS", RoomStatus = "All" },
            default);
        var currentPage = await currentInventory.GetPageAsync(new RoomInventoryImportForm { Facility = "All" }, default);
        var activeRoomIds = await db.Rooms.AsNoTracking().Where(x => x.IsActive).Select(x => x.Id).ToListAsync();

        foreach (var roomId in activeRoomIds)
        {
            var authoritative = (await ledger.GetSnapshotsAsync(null, [roomId], default))
                .Where(x => x.CurrentBins > 0)
                .Sum(x => x.CurrentBins);
            var detail = await dashboard.GetRoomDetailAsync(roomId, default);

            Assert.Equal(authoritative, cards.Rooms.Single(x => x.RoomId == roomId).CurrentBinsCount ?? 0);
            Assert.Equal(authoritative, detail.CurrentLots.Sum(x => x.CurrentBins));
            Assert.Equal(authoritative, currentPage.CurrentLots.Where(x => x.RoomId == roomId).Sum(x => x.CurrentBins));
            Assert.Equal(authoritative, detail.TransferCurrentRoomBins);
            Assert.Equal(authoritative, detail.TransferAvailableBins);
            Assert.Equal(authoritative, detail.TransferLotOptions.Sum(x => x.CurrentBins));
            Assert.True(detail.TransferInventoryReconciles);
        }
    }

    [Fact]
    public async Task RoomLedger_ChainedLegacyBinsRunWithoutCropYear_CanonicalizesToZeroAndIsNotSelectable()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var warehouse = await db.Warehouses.SingleAsync(x => x.Id == 1000);
        var room = await db.Rooms.SingleAsync(x => x.Id == 1001);
        var fruit = await db.FruitProfiles.SingleAsync(x => x.Id == 1000);
        var baseline = await db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 8001);
        var first = LegacyDepletion(8501, warehouse, room, fruit, "LOT-120", 120, 50, 70);
        var second = LegacyDepletion(8502, warehouse, room, fruit, "LOT-120", 70, 70, 0);
        db.RoomInventoryAdjustments.AddRange(first, second);
        db.BinsRunEntries.AddRange(
            LegacyEntry(8601, warehouse, room, fruit, baseline, first, "LOT-120", 120, 50, 70),
            LegacyEntry(8602, warehouse, room, fruit, first, second, "LOT-120", 70, 70, 0));
        await db.SaveChangesAsync();

        var snapshots = await new RoomInventoryLedgerQueryService(db)
            .GetSnapshotsAsync(1000, [1001], CancellationToken.None);
        var canonical = snapshots.Single(x => x.Lot == "LOT-120");
        Assert.Equal(2026, canonical.CropYear);
        Assert.Equal(0, canonical.CurrentBins);
        Assert.Equal(3, canonical.TransactionCount);

        var page = await CreateService(db).GetPageAsync(
            new BinsRunFilterForm { Section = "Actual", WarehouseId = 1000, RoomIds = [1001] },
            Principal("manager@fruitandland.com"),
            CancellationToken.None);
        Assert.DoesNotContain(page.AvailableInventory, x => x.Lot == "LOT-120");
    }

    [Fact]
    public async Task ActualRunInventory_ByRoomAndByVariety_ReturnSamePositiveStableRowsWithoutWrites()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var service = CreateService(db);
        var user = Principal("manager@fruitandland.com");
        var before = new
        {
            Adjustments = await db.RoomInventoryAdjustments.CountAsync(),
            Entries = await db.BinsRunEntries.CountAsync(),
            Receipts = await db.Receipts.CountAsync(),
            Audits = await db.AuditLogs.CountAsync()
        };

        var byRoom = await service.GetPageAsync(
            new BinsRunFilterForm
            {
                Section = "Actual",
                WarehouseId = 1000,
                SelectionMode = ActualRunSelectionModes.ByRoom,
                RoomIds = [1001, 1002]
            },
            user,
            CancellationToken.None);
        var byVariety = await service.GetPageAsync(
            new BinsRunFilterForm
            {
                Section = "Actual",
                WarehouseId = 1000,
                SelectionMode = ActualRunSelectionModes.ByVariety,
                FruitProfileId = 1000
            },
            user,
            CancellationToken.None);
        var oneRoom = await service.GetPageAsync(
            new BinsRunFilterForm
            {
                Section = "Actual",
                WarehouseId = 1000,
                SelectionMode = ActualRunSelectionModes.ByRoom,
                RoomIds = [1001]
            },
            user,
            CancellationToken.None);
        var emptyVariety = await service.GetPageAsync(
            new BinsRunFilterForm
            {
                Section = "Actual",
                WarehouseId = 1000,
                SelectionMode = ActualRunSelectionModes.ByVariety,
                FruitProfileId = 9999
            },
            user,
            CancellationToken.None);

        Assert.NotEmpty(byRoom.AvailableInventory);
        Assert.All(byRoom.AvailableInventory, x => Assert.True(x.CurrentBins > 0));
        Assert.Equal(
            byRoom.AvailableInventory.Select(x => x.InventoryKey).OrderBy(x => x),
            byVariety.AvailableInventory.Select(x => x.InventoryKey).OrderBy(x => x));
        Assert.Equal(new[] { 1001, 1002 }, byVariety.AvailableInventory.Select(x => x.RoomId).Distinct().OrderBy(x => x));
        Assert.All(oneRoom.AvailableInventory, x => Assert.Equal(1001, x.RoomId));
        Assert.Equal(
            byRoom.AvailableInventory.Where(x => x.RoomId == 1001).Select(x => x.InventoryKey).OrderBy(x => x),
            oneRoom.AvailableInventory.Select(x => x.InventoryKey).OrderBy(x => x));
        Assert.Equal(byRoom.AvailableInventory.Count, byRoom.AvailableInventory.Select(x => x.InventoryKey).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Empty(emptyVariety.AvailableInventory);
        Assert.Contains("no positive current inventory", emptyVariety.InventorySelectionMessage, StringComparison.OrdinalIgnoreCase);
        Assert.All(byVariety.AvailableInventory, x => Assert.Equal(1000, x.FruitProfileId));
        Assert.All(byVariety.AvailableInventory, x =>
        {
            Assert.Equal("EBS", x.Facility);
            Assert.False(string.IsNullOrWhiteSpace(x.RoomName));
            Assert.False(string.IsNullOrWhiteSpace(x.SourceReference));
        });
        Assert.Equal(before.Adjustments, await db.RoomInventoryAdjustments.CountAsync());
        Assert.Equal(before.Entries, await db.BinsRunEntries.CountAsync());
        Assert.Equal(before.Receipts, await db.Receipts.CountAsync());
        Assert.Equal(before.Audits, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task RoomLedger_KeepsRoomGrowerLotAndCropYearIdentitiesSeparate()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var warehouse = await db.Warehouses.SingleAsync(x => x.Id == 1000);
        var roomOne = await db.Rooms.SingleAsync(x => x.Id == 1001);
        var roomTwo = await db.Rooms.SingleAsync(x => x.Id == 1002);
        var fruit = await db.FruitProfiles.SingleAsync(x => x.Id == 1000);
        var rows = new[]
        {
            Adjustment(8701, warehouse, roomOne, fruit, "IDENTITY", 5),
            Adjustment(8702, warehouse, roomOne, fruit, "IDENTITY", 6),
            Adjustment(8703, warehouse, roomOne, fruit, "IDENTITY", 7),
            Adjustment(8704, warehouse, roomTwo, fruit, "IDENTITY", 8)
        };
        rows[0].CropYear = 2025;
        rows[0].GrowerLotId = 11;
        rows[1].CropYear = 2026;
        rows[1].GrowerLotId = 11;
        rows[2].CropYear = 2026;
        rows[2].GrowerLotId = 12;
        rows[3].CropYear = 2026;
        rows[3].GrowerLotId = 11;
        db.RoomInventoryAdjustments.AddRange(rows);
        await db.SaveChangesAsync();

        var snapshots = (await new RoomInventoryLedgerQueryService(db)
                .GetSnapshotsAsync(1000, [1001, 1002], 1000, CancellationToken.None))
            .Where(x => x.Lot == "IDENTITY")
            .ToList();

        Assert.Equal(4, snapshots.Count);
        Assert.Equal(new[] { 5, 6, 7, 8 }, snapshots.Select(x => x.CurrentBins).OrderBy(x => x));
        Assert.Equal(4, snapshots.Select(x => (x.RoomId, x.CropYear, x.GrowerLotId)).Distinct().Count());
    }

    [Fact]
    public async Task RoomLedger_NeverTreatsGrowerLotLotNumberAsGrowerNumber()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        var warehouse = await db.Warehouses.SingleAsync(x => x.Id == 1000);
        var room = await db.Rooms.SingleAsync(x => x.Id == 1001);
        var fruit = await db.FruitProfiles.SingleAsync(x => x.Id == 1000);
        var growerLot = new GrowerLot
        {
            Id = 9900,
            Grower = "Lot-only grower",
            LotNumber = "7777",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var adjustment = Adjustment(9900, warehouse, room, fruit, "LOT-ONLY", 9);
        adjustment.GrowerLot = growerLot;
        adjustment.GrowerLotId = growerLot.Id;
        db.AddRange(growerLot, adjustment);
        await db.SaveChangesAsync();

        var snapshot = (await new RoomInventoryLedgerQueryService(db)
                .GetSnapshotsAsync(warehouse.Id, [room.Id], fruit.Id, CancellationToken.None))
            .Single(x => x.Lot == "LOT-ONLY");

        Assert.Null(snapshot.GrowerNumber);
        Assert.Equal("7777", growerLot.LotNumber);
        var service = CreateService(db);
        var option = (await service.GetPageAsync(
                new BinsRunFilterForm { Section = "Actual", WarehouseId = warehouse.Id, RoomIds = [room.Id] },
                Principal("manager@fruitandland.com"),
                CancellationToken.None))
            .AvailableInventory.Single(x => x.Lot == "LOT-ONLY");
        Assert.Contains("authoritative Grower Number", await service.CreateActualRunAsync(
            GroupForm((option, 1)),
            Principal("manager@fruitandland.com"),
            CancellationToken.None));
    }

    [Fact]
    public async Task ActualRun_DuplicateWpOrEbsWarehouseConfigurationFailsClosed()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        db.Warehouses.Add(new Warehouse { Id = 9901, Code = EmploymentFacilities.Ebs, Name = "Duplicate EBS", IsActive = true });
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var principal = Principal("manager@fruitandland.com");
        var option = (await service.GetPageAsync(
                new BinsRunFilterForm { Section = "Actual", WarehouseId = 1000, RoomIds = [1001] },
                principal,
                CancellationToken.None))
            .AvailableInventory.Single(x => x.Lot == "LOT-120");

        var error = await service.CreateActualRunAsync(GroupForm((option, 1)), principal, CancellationToken.None);

        Assert.Contains("Exactly one active WP warehouse and one active EBS warehouse", error);
        Assert.Empty(await db.ActualRuns.ToListAsync());
    }

    [Fact]
    public async Task ActualRun_MissingWpOrEbsWarehouseConfigurationFailsClosed()
    {
        using var db = CreateDbContext();
        await SeedInventoryAsync(db);
        foreach (var wp in await db.Warehouses.Where(x => x.Code == EmploymentFacilities.Wp).ToListAsync())
        {
            wp.IsActive = false;
        }
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var principal = Principal("manager@fruitandland.com");
        var option = (await service.GetPageAsync(
                new BinsRunFilterForm { Section = "Actual", WarehouseId = 1000, RoomIds = [1001] },
                principal,
                CancellationToken.None))
            .AvailableInventory.Single(x => x.Lot == "LOT-120");

        var error = await service.CreateActualRunAsync(GroupForm((option, 1)), principal, CancellationToken.None);

        Assert.Contains("Exactly one active WP warehouse and one active EBS warehouse", error);
        Assert.Empty(await db.ActualRuns.ToListAsync());
    }

    [Fact]
    public async Task ActualRun_AuthoritativeWritesRequireCompleteReportingIdentity()
    {
        using var db = CreateDbContext();
        await SeedActualRunLedgerOnlyAsync(db);
        var complete = new RoomInventoryLedgerSnapshot(
            1000, "ART", 1001, "Evans-12", "", 2026, null, 1000,
            "Test Grower", "1084", "LOT-120", null, "ACTUALRUNTEST", "ACTUALRUNTEST",
            "Actual Run Test Apple", "Apple", "Conventional", false, "",
            120, 0, 0, 0, 0, 0, 0, 0, 120, 120, 1,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 8001);
        var cases = new[]
        {
            ("crop year", complete with { CropYear = null }, "missing crop year"),
            ("fruit profile", complete with { FruitProfileId = null }, "canonical variety/profile"),
            ("variety", complete with { Variety = "" }, "canonical variety/profile"),
            ("production type", complete with { ProductionType = "" }, "production type"),
            ("organic status", complete with { IsOrganic = null }, "Organic/Conventional status"),
            ("grower number", complete with { GrowerNumber = null }, "authoritative Grower Number")
        };
        var principal = Principal("manager@fruitandland.com");
        foreach (var (field, snapshot, expectedError) in cases)
        {
            var service = CreateService(
                db,
                roomInventoryLedgerQueryService: new StaticRoomInventoryLedgerQueryService([snapshot]));
            var option = (await service.GetPageAsync(
                    new BinsRunFilterForm { Section = "Actual", WarehouseId = 1000, RoomIds = [1001] },
                    principal,
                    CancellationToken.None))
                .AvailableInventory.Single();

            var error = await service.CreateActualRunAsync(GroupForm((option, 1)), principal, CancellationToken.None);

            Assert.NotNull(error);
            Assert.True(error.Contains(expectedError, StringComparison.OrdinalIgnoreCase), $"{field}: {error}");
            Assert.Contains("Source line ART / Evans-12", error);
            Assert.Empty(await db.ActualRuns.ToListAsync());
        }
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

    private static async Task SeedInventoryAsync(CropQcDbContext db, bool useSeededEbsWarehouse = false)
    {
        var seededEbsWarehouses = await db.Warehouses
            .Where(x => x.Code == EmploymentFacilities.Ebs)
            .ToListAsync();
        foreach (var seededEbs in seededEbsWarehouses)
        {
            seededEbs.IsActive = false;
        }
        var warehouse = useSeededEbsWarehouse
            ? seededEbsWarehouses.Single()
            : new Warehouse { Id = 1000, Code = "EBS", Name = "EBS", IsActive = true };
        warehouse.IsActive = true;
        var room = new Room { Id = 1001, WarehouseId = warehouse.Id, Warehouse = warehouse, Code = "EVANCA12", Name = "Evans 12", CropQcRoomName = "Evans-12", IsActive = true };
        var otherRoom = new Room { Id = 1002, WarehouseId = warehouse.Id, Warehouse = warehouse, Code = "LAMBCA17", Name = "Lamb 17", CropQcRoomName = "Lamb-17", IsActive = true };
        var fruit = new FruitProfile { Id = 1000, Name = "Fuji", VarietyCode = "FUJI", FruitType = "Apple", ProductionType = "Conventional", IsActive = true };
        var sampleType = new SampleType { Id = 1000, Name = "Receiving Sample", IsActive = true };
        var doorSampleType = new SampleType { Id = 1001, Name = "Door Sample", IsActive = true };
        var grade1 = new Grade { Id = 1000, Code = "W1", Name = "W1", IsActive = true };
        var grade2 = new Grade { Id = 1001, Code = "W2", Name = "W2", IsActive = true };
        var growerLots = new[]
        {
            GrowerLot(1010, "LOT-120"),
            GrowerLot(1011, "LOT-30"),
            GrowerLot(1012, "HISTORY"),
            GrowerLot(1013, "LOT-ZERO"),
            GrowerLot(1014, "LOT-OTHER")
        };
        if (!useSeededEbsWarehouse)
        {
            db.Warehouses.Add(warehouse);
        }
        db.Rooms.AddRange(room, otherRoom);
        db.FruitProfiles.Add(fruit);
        db.GrowerLots.AddRange(growerLots);
        db.SampleTypes.AddRange(sampleType, doorSampleType);
        db.Grades.AddRange(grade1, grade2);
        db.Users.AddRange(
            User(1000, "admin@fruitandland.com", PageAccessLevel.Admin),
            User(1001, "manager@fruitandland.com", PageAccessLevel.Edit),
            User(1002, "viewer@fruitandland.com", PageAccessLevel.View));
        var inventoryAdjustments = new[]
        {
            Adjustment(8001, warehouse, room, fruit, "LOT-120", 120),
            Adjustment(8004, warehouse, room, fruit, "LOT-30", 30),
            Adjustment(8005, warehouse, room, fruit, "HISTORY", 40),
            Adjustment(8002, warehouse, room, fruit, "LOT-ZERO", 0),
            Adjustment(8003, warehouse, otherRoom, fruit, "LOT-OTHER", 60),
            Adjustment(8006, warehouse, otherRoom, fruit, "LOT-120", 25)
        };
        foreach (var adjustment in inventoryAdjustments)
        {
            adjustment.GrowerLot = growerLots.Single(x => x.LotNumber == adjustment.LotNumber);
            adjustment.GrowerLotId = adjustment.GrowerLot.Id;
        }
        db.RoomInventoryAdjustments.AddRange(inventoryAdjustments);
        var receipts = new Receipt[] { new Receipt
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
        SampleReceipt(7003, "QC-LOT-30", "LOT-30", warehouse, room, fruit),
        SampleReceipt(7004, "QC-OTHER-LOT-120", "LOT-120", warehouse, otherRoom, fruit),
        SampleReceipt(7005, "QC-LOT-OTHER", "LOT-OTHER", warehouse, otherRoom, fruit),
        SampleReceipt(7006, "QC-HISTORY", "HISTORY", warehouse, room, fruit) };
        foreach (var receipt in receipts)
        {
            receipt.GrowerLot = growerLots.Single(x => x.LotNumber == receipt.LotCode);
            receipt.GrowerLotId = receipt.GrowerLot.Id;
        }
        db.Receipts.AddRange(receipts);
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
        if (useSeededEbsWarehouse)
        {
            foreach (var entry in db.ChangeTracker.Entries<QcFruitReading>().ToList())
            {
                entry.State = EntityState.Detached;
            }
            foreach (var entry in db.ChangeTracker.Entries<QcSample>().ToList())
            {
                entry.State = EntityState.Detached;
            }
            foreach (var property in db.ChangeTracker.Entries().SelectMany(x => x.Properties))
            {
                if (property.Metadata.ClrType == typeof(DateTimeOffset)
                    && property.CurrentValue is DateTimeOffset value
                    && value.Offset != TimeSpan.Zero)
                {
                    property.CurrentValue = value.ToUniversalTime();
                }
            }
        }
        await db.SaveChangesAsync();

        static GrowerLot GrowerLot(int id, string lot) => new()
        {
            Id = id,
            Grower = "Test Grower",
            LotNumber = lot,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
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
        db.Receipts.AddRange(
            ProductionReceipt(7901, 120, "LOT-120", fruit, warehouse, room),
            ProductionReceipt(7902, 30, "LOT-30", fruit, warehouse, room),
            ProductionReceipt(7903, 60, "LOT-OTHER", fruit, warehouse, otherRoom),
            ProductionReceipt(7904, 25, "LOT-120", fruit, warehouse, otherRoom));
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
        foreach (var seededWp in await db.Warehouses.Where(x => x.Code == EmploymentFacilities.Wp).ToListAsync())
        {
            seededWp.IsActive = false;
        }
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
        db.Users.Add(User(1001, "manager@fruitandland.com", PageAccessLevel.Edit, EmploymentFacilities.Wp));

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

    private static BinsRunForm ActualRunForm(
        BinsRunInventoryOptionViewModel option,
        RunProjection? projection = null) =>
        new()
        {
            WarehouseId = option.WarehouseId,
            RoomId = option.RoomId,
            InventoryKey = option.InventoryKey,
            BinsRun = 5,
            ExpectedAvailableBins = option.CurrentBins,
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
                TreatmentSignature = x.Option.TreatmentSignature,
                BinsRun = x.Bins,
                ExpectedAvailableBins = x.Option.CurrentBins
            }).ToList()
        };

    private static BinsRunForm ProjectionConversionForm(BinsRunInventoryOptionViewModel option, RunProjection projection)
    {
        var form = ActualRunForm(option, projection);
        form.RunProjectionId = projection.Id;
        form.RunProjectionSourceId = projection.Sources.Single().Id;
        return form;
    }

    private static User User(int id, string email, PageAccessLevel binsRunLevel, string employmentFacility = EmploymentFacilities.Ebs)
    {
        var role = new Role
        {
            Name = $"Bins Run test role {id}",
            NormalizedName = $"BINS RUN TEST ROLE {id}",
            IsActive = true
        };
        foreach (var area in ApplicationAreas.All)
        {
            role.PageAccesses.Add(new RolePageAccess
            {
                AreaKey = area.Key,
                AccessLevel = binsRunLevel.ToString(),
                UpdatedAt = DateTimeOffset.UtcNow
            });
        }

        var user = new User
        {
            Id = id,
            Email = email,
            DisplayName = email,
            Domain = "fruitandland.com",
            IsActive = true,
            EmploymentFacility = employmentFacility,
            CreatedAt = DateTimeOffset.UtcNow
        };
        user.UserRoles.Add(new UserRole { User = user, Role = role });
        return user;
    }

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

    private static RoomInventoryAdjustment LegacyDepletion(
        long id,
        Warehouse warehouse,
        Room room,
        FruitProfile fruit,
        string lot,
        int previous,
        int bins,
        int next) => new()
        {
            Id = id,
            CropYear = null,
            Warehouse = warehouse,
            Room = room,
            FruitProfile = fruit,
            GrowerName = "Wes Verified Current Inventory",
            LotNumber = lot,
            VarietyCode = fruit.VarietyCode,
            OldBinCount = previous,
            ChangeAmount = -bins,
            NewBinCount = next,
            AdjustmentType = BinsRunService.AdjustmentType,
            Source = "Bins Run",
            AdjustmentAt = DateTimeOffset.Parse("2026-07-20T00:00:00Z").AddMinutes(id),
            CreatedAt = DateTimeOffset.Parse("2026-07-20T00:00:00Z").AddMinutes(id)
        };

    private static BinsRunEntry LegacyEntry(
        long id,
        Warehouse warehouse,
        Room room,
        FruitProfile fruit,
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
            CropYear = null,
            FruitProfile = fruit,
            GrowerName = "Wes Verified Current Inventory",
            LotNumber = lot,
            VarietyCode = fruit.VarietyCode,
            PreviousAvailableBins = previous,
            BinsRun = bins,
            NewAvailableBins = next,
            RunAt = adjustment.AdjustmentAt,
            CreatedAt = adjustment.CreatedAt,
            TransactionType = ActualRunTransactionTypes.Legacy
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

    private static BinsRunService CreateService(
        CropQcDbContext db,
        IRunExpectationService? runExpectationService = null,
        IRoomInventoryLedgerQueryService? roomInventoryLedgerQueryService = null,
        IRoomTreatmentService? roomTreatmentService = null) =>
        new(
            db,
            new UserAccessService(db, new ConfigurationBuilder().Build()),
            NullLogger<BinsRunService>.Instance,
            roomInventoryLedgerQueryService: roomInventoryLedgerQueryService,
            runExpectationService: runExpectationService,
            roomTreatmentService: roomTreatmentService);

    private static void AddMappedGrower(CropQcDbContext db, string number, string displayName, params string[] aliases)
    {
        var now = DateTimeOffset.Parse("2026-08-01T00:00:00Z");
        var grower = new CanonicalGrower
        {
            DisplayName = displayName,
            NormalizedKey = $"REVIEWED_GROWER_NUMBER_{number}",
            CreatedAt = now,
            UpdatedAt = now
        };
        grower.GrowerNumbers.Add(new CanonicalGrowerNumber
        {
            GrowerNumber = number,
            NormalizedGrowerNumber = number,
            CreatedAt = now,
            UpdatedAt = now
        });
        foreach (var alias in aliases.Prepend(displayName))
        {
            grower.Aliases.Add(new CanonicalGrowerAlias
            {
                AliasName = alias,
                NormalizedAliasKey = CanonicalGrowerService.NormalizeGrowerKey(alias),
                CreatedAt = now,
                UpdatedAt = now
            });
        }
        db.CanonicalGrowers.Add(grower);
    }

    private sealed class ThrowingRunExpectationService : IRunExpectationService
    {
        public Task<RunExpectation> CreateFrozenAsync(
            ActualRun actualRun,
            ActualRunRevision revision,
            IReadOnlyList<BinsRunEntry> activeEntries,
            int userId,
            DateTimeOffset calculatedAt,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated missing Run Expectation schema.");
    }

    private sealed class StaticRoomInventoryLedgerQueryService(
        IReadOnlyList<RoomInventoryLedgerSnapshot> snapshots) : IRoomInventoryLedgerQueryService
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
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RoomInventoryLedgerSnapshot>>(
                snapshots.Where(x =>
                    (warehouseId is null || x.WarehouseId == warehouseId)
                    && (roomIds is null || roomIds.Count == 0 || roomIds.Contains(x.RoomId))
                    && (fruitProfileId is null || x.FruitProfileId == fruitProfileId))
                .ToList());
    }

    private sealed class ThrowingRoomTreatmentService : IRoomTreatmentService
    {
        public Task<RoomTreatmentData> GetRoomDataAsync(int roomId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Simulated treatment projection failure.");

        public Task<RoomTreatmentApplyPageViewModel> GetApplyPageAsync(RoomTreatmentApplyForm form, bool review, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(string? Error, long? ApplicationId)> ApplyAsync(RoomTreatmentApplyForm form, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> ReverseAsync(ReverseRoomTreatmentApplicationForm form, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<TreatmentSegmentSelection>> GetSelectionsAsync(RoomInventoryLedgerSnapshot snapshot, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<string, IReadOnlyList<TreatmentSegmentSelection>>> GetSelectionsAsync(IReadOnlyList<RoomInventoryLedgerSnapshot> snapshots, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TreatmentLineageWriteResult> MoveAsync(RoomInventoryLedgerSnapshot snapshot, string? treatmentSignature, int bins, int? destinationWarehouseId, int? destinationRoomId, string operationKey, string movementType, long? roomTransferId, long? roomInventoryLossId, long? binsRunEntryId, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TreatmentLineageWriteResult> ReverseMovementsAsync(string operationKeyPrefix, string movementType, long? roomTransferId, long? roomInventoryLossId, long? binsRunEntryId, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TreatmentLineageWriteResult> AddUnknownAsync(RoomInventoryLedgerSnapshot snapshot, int bins, string operationKey, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ShortTreatmentProjectionService : IRoomTreatmentService
    {
        public Task<RoomTreatmentData> GetRoomDataAsync(int roomId, CancellationToken cancellationToken) =>
            Task.FromResult(new RoomTreatmentData([], [], false, false));

        public Task<IReadOnlyDictionary<string, IReadOnlyList<TreatmentSegmentSelection>>> GetSelectionsAsync(
            IReadOnlyList<RoomInventoryLedgerSnapshot> snapshots,
            CancellationToken cancellationToken)
        {
            var result = snapshots.ToDictionary(
                RoomTreatmentService.SelectionLookupKey,
                x => (IReadOnlyList<TreatmentSegmentSelection>)[new(
                    RoomTreatmentService.IdentityKey(x),
                    "u",
                    TreatmentLineageStates.Untreated,
                    Math.Max(0, x.CurrentBins - 1),
                    "Untreated")],
                StringComparer.OrdinalIgnoreCase);
            return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<TreatmentSegmentSelection>>>(result);
        }

        public Task<RoomTreatmentApplyPageViewModel> GetApplyPageAsync(RoomTreatmentApplyForm form, bool review, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(string? Error, long? ApplicationId)> ApplyAsync(RoomTreatmentApplyForm form, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> ReverseAsync(ReverseRoomTreatmentApplicationForm form, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<TreatmentSegmentSelection>> GetSelectionsAsync(RoomInventoryLedgerSnapshot snapshot, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TreatmentLineageWriteResult> MoveAsync(RoomInventoryLedgerSnapshot snapshot, string? treatmentSignature, int bins, int? destinationWarehouseId, int? destinationRoomId, string operationKey, string movementType, long? roomTransferId, long? roomInventoryLossId, long? binsRunEntryId, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken) => throw new InvalidOperationException("Move must not run when inventory does not reconcile.");
        public Task<TreatmentLineageWriteResult> ReverseMovementsAsync(string operationKeyPrefix, string movementType, long? roomTransferId, long? roomInventoryLossId, long? binsRunEntryId, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TreatmentLineageWriteResult> AddUnknownAsync(RoomInventoryLedgerSnapshot snapshot, int bins, string operationKey, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class SegmentedRoomTreatmentService : IRoomTreatmentService
    {
        public List<(string Signature, long SegmentId, int Bins)> Moves { get; } = [];
        public List<int> AuthoritativeBalances { get; } = [];
        public int Reversals { get; private set; }

        public Task<IReadOnlyDictionary<string, IReadOnlyList<TreatmentSegmentSelection>>> GetSelectionsAsync(
            IReadOnlyList<RoomInventoryLedgerSnapshot> snapshots,
            CancellationToken cancellationToken)
        {
            var result = snapshots.ToDictionary(
                RoomTreatmentService.SelectionLookupKey,
                snapshot => (IReadOnlyList<TreatmentSegmentSelection>)
                [
                    new(RoomTreatmentService.IdentityKey(snapshot), "segment-a", TreatmentLineageStates.Confirmed, 50, "Treatment A", SegmentId: 8101),
                    new(RoomTreatmentService.IdentityKey(snapshot), "segment-b", TreatmentLineageStates.Confirmed, 70, "Treatment B", SegmentId: 8102)
                ],
                StringComparer.OrdinalIgnoreCase);
            return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<TreatmentSegmentSelection>>>(result);
        }

        public Task<TreatmentLineageWriteResult> MoveAsync(
            RoomInventoryLedgerSnapshot snapshot,
            string? treatmentSignature,
            int bins,
            int? destinationWarehouseId,
            int? destinationRoomId,
            string operationKey,
            string movementType,
            long? roomTransferId,
            long? roomInventoryLossId,
            long? binsRunEntryId,
            DateTimeOffset occurredAt,
            int? actorUserId,
            CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("Actual Runs must move the exact selected treatment segment.");
        }

        public Task<TreatmentLineageWriteResult> MoveSelectedAsync(
            RoomInventoryLedgerSnapshot snapshot,
            string? treatmentSignature,
            long? treatmentSegmentId,
            long? treatmentReceiptId,
            int bins,
            int? destinationWarehouseId,
            int? destinationRoomId,
            string operationKey,
            string movementType,
            long? roomTransferId,
            long? roomInventoryLossId,
            long? binsRunEntryId,
            DateTimeOffset occurredAt,
            int? actorUserId,
            CancellationToken cancellationToken)
        {
            AuthoritativeBalances.Add(snapshot.CurrentBins);
            Moves.Add((treatmentSignature ?? "", treatmentSegmentId ?? 0, bins));
            return Task.FromResult(new TreatmentLineageWriteResult(true, null));
        }

        public Task<RoomTreatmentData> GetRoomDataAsync(int roomId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RoomTreatmentApplyPageViewModel> GetApplyPageAsync(RoomTreatmentApplyForm form, bool review, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(string? Error, long? ApplicationId)> ApplyAsync(RoomTreatmentApplyForm form, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> ReverseAsync(ReverseRoomTreatmentApplicationForm form, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<TreatmentSegmentSelection>> GetSelectionsAsync(RoomInventoryLedgerSnapshot snapshot, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TreatmentLineageWriteResult> ReverseMovementsAsync(string operationKeyPrefix, string movementType, long? roomTransferId, long? roomInventoryLossId, long? binsRunEntryId, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken)
        {
            Reversals++;
            return Task.FromResult(new TreatmentLineageWriteResult(true, null));
        }
        public Task<TreatmentLineageWriteResult> AddUnknownAsync(RoomInventoryLedgerSnapshot snapshot, int bins, string operationKey, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class ProductionShapeSequentialTreatmentService : IRoomTreatmentService
    {
        private readonly Dictionary<long, MutableSegment> segments = new()
        {
            [144] = new(144, "u", TreatmentLineageStates.Untreated, 132, null),
            [175] = new(175, "u|a:6", TreatmentLineageStates.Confirmed, 24, 930),
            [176] = new(176, "u|a:7", TreatmentLineageStates.Confirmed, 24, 927),
            [180] = new(180, "u|a:10", TreatmentLineageStates.Confirmed, 24, 938),
            [184] = new(184, "u|a:12", TreatmentLineageStates.Confirmed, 21, 944)
        };

        public List<int> AuthoritativeBalances { get; } = [];
        public List<(long SegmentId, int Bins)> Moves { get; } = [];
        public int TotalBins => segments.Values.Sum(x => x.CurrentBins);
        public int Bins(long id) => segments[id].CurrentBins;

        public Task<IReadOnlyDictionary<string, IReadOnlyList<TreatmentSegmentSelection>>> GetSelectionsAsync(
            IReadOnlyList<RoomInventoryLedgerSnapshot> snapshots,
            CancellationToken cancellationToken)
        {
            var result = snapshots.ToDictionary(
                RoomTreatmentService.SelectionLookupKey,
                snapshot => (IReadOnlyList<TreatmentSegmentSelection>)segments.Values.Select(x => new TreatmentSegmentSelection(
                    RoomTreatmentService.IdentityKey(snapshot), x.Signature, x.State, x.CurrentBins,
                    x.State == TreatmentLineageStates.Untreated ? "Untreated" : "MCP", x.ReceiptId, x.Id)).ToList(),
                StringComparer.OrdinalIgnoreCase);
            return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<TreatmentSegmentSelection>>>(result);
        }

        public Task<TreatmentLineageWriteResult> MoveSelectedAsync(
            RoomInventoryLedgerSnapshot snapshot,
            string? treatmentSignature,
            long? treatmentSegmentId,
            long? treatmentReceiptId,
            int bins,
            int? destinationWarehouseId,
            int? destinationRoomId,
            string operationKey,
            string movementType,
            long? roomTransferId,
            long? roomInventoryLossId,
            long? binsRunEntryId,
            DateTimeOffset occurredAt,
            int? actorUserId,
            CancellationToken cancellationToken)
        {
            AuthoritativeBalances.Add(snapshot.CurrentBins);
            if (TotalBins > snapshot.CurrentBins)
                throw new InvalidOperationException("Treatment lineage exceeds authoritative inventory.");
            if (TotalBins < snapshot.CurrentBins)
                segments[144].CurrentBins += snapshot.CurrentBins - TotalBins;
            if (treatmentSegmentId is null || !segments.TryGetValue(treatmentSegmentId.Value, out var segment)
                || segment.Signature != treatmentSignature || segment.ReceiptId != treatmentReceiptId || segment.CurrentBins < bins)
                return Task.FromResult(new TreatmentLineageWriteResult(false, "The exact treatment segment is unavailable."));
            segment.CurrentBins -= bins;
            Moves.Add((segment.Id, bins));
            return Task.FromResult(new TreatmentLineageWriteResult(true, null));
        }

        public Task<RoomTreatmentData> GetRoomDataAsync(int roomId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RoomTreatmentApplyPageViewModel> GetApplyPageAsync(RoomTreatmentApplyForm form, bool review, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(string? Error, long? ApplicationId)> ApplyAsync(RoomTreatmentApplyForm form, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> ReverseAsync(ReverseRoomTreatmentApplicationForm form, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<TreatmentSegmentSelection>> GetSelectionsAsync(RoomInventoryLedgerSnapshot snapshot, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TreatmentLineageWriteResult> MoveAsync(RoomInventoryLedgerSnapshot snapshot, string? treatmentSignature, int bins, int? destinationWarehouseId, int? destinationRoomId, string operationKey, string movementType, long? roomTransferId, long? roomInventoryLossId, long? binsRunEntryId, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TreatmentLineageWriteResult> ReverseMovementsAsync(string operationKeyPrefix, string movementType, long? roomTransferId, long? roomInventoryLossId, long? binsRunEntryId, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TreatmentLineageWriteResult> AddUnknownAsync(RoomInventoryLedgerSnapshot snapshot, int bins, string operationKey, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();

        private sealed record MutableSegment(long Id, string Signature, string State, int InitialBins, long? ReceiptId)
        {
            public int CurrentBins { get; set; } = InitialBins;
        }
    }

    private sealed record TreatmentSelectionTemplate(
        string Signature,
        string State,
        int Bins,
        string Label,
        long? ReceiptId,
        long? SegmentId);

    private sealed class RecordingTreatmentSelectionService(
        IReadOnlyList<TreatmentSelectionTemplate> templates) : IRoomTreatmentService
    {
        public List<(string Signature, long? SegmentId, long? ReceiptId, int Bins)> Moves { get; } = [];

        public Task<IReadOnlyDictionary<string, IReadOnlyList<TreatmentSegmentSelection>>> GetSelectionsAsync(
            IReadOnlyList<RoomInventoryLedgerSnapshot> snapshots,
            CancellationToken cancellationToken)
        {
            var result = snapshots.ToDictionary(
                RoomTreatmentService.SelectionLookupKey,
                snapshot => (IReadOnlyList<TreatmentSegmentSelection>)templates.Select(x => new TreatmentSegmentSelection(
                    RoomTreatmentService.IdentityKey(snapshot), x.Signature, x.State, x.Bins, x.Label, x.ReceiptId, x.SegmentId)).ToList(),
                StringComparer.OrdinalIgnoreCase);
            return Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<TreatmentSegmentSelection>>>(result);
        }

        public Task<IReadOnlyList<TreatmentSegmentSelection>> GetSelectionsAsync(
            RoomInventoryLedgerSnapshot snapshot,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TreatmentSegmentSelection>>(templates.Select(x => new TreatmentSegmentSelection(
                RoomTreatmentService.IdentityKey(snapshot), x.Signature, x.State, x.Bins, x.Label, x.ReceiptId, x.SegmentId)).ToList());

        public Task<TreatmentLineageWriteResult> MoveSelectedAsync(
            RoomInventoryLedgerSnapshot snapshot,
            string? treatmentSignature,
            long? treatmentSegmentId,
            long? treatmentReceiptId,
            int bins,
            int? destinationWarehouseId,
            int? destinationRoomId,
            string operationKey,
            string movementType,
            long? roomTransferId,
            long? roomInventoryLossId,
            long? binsRunEntryId,
            DateTimeOffset occurredAt,
            int? actorUserId,
            CancellationToken cancellationToken)
        {
            Moves.Add((treatmentSignature ?? "", treatmentSegmentId, treatmentReceiptId, bins));
            return Task.FromResult(new TreatmentLineageWriteResult(true, null));
        }

        public Task<RoomTreatmentData> GetRoomDataAsync(int roomId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<RoomTreatmentApplyPageViewModel> GetApplyPageAsync(RoomTreatmentApplyForm form, bool review, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<(string? Error, long? ApplicationId)> ApplyAsync(RoomTreatmentApplyForm form, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<string?> ReverseAsync(ReverseRoomTreatmentApplicationForm form, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TreatmentLineageWriteResult> MoveAsync(RoomInventoryLedgerSnapshot snapshot, string? treatmentSignature, int bins, int? destinationWarehouseId, int? destinationRoomId, string operationKey, string movementType, long? roomTransferId, long? roomInventoryLossId, long? binsRunEntryId, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken) => throw new InvalidOperationException("The exact selected treatment segment must be used.");
        public Task<TreatmentLineageWriteResult> ReverseMovementsAsync(string operationKeyPrefix, string movementType, long? roomTransferId, long? roomInventoryLossId, long? binsRunEntryId, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<TreatmentLineageWriteResult> AddUnknownAsync(RoomInventoryLedgerSnapshot snapshot, int bins, string operationKey, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static DashboardDataService CreateDashboardService(
        CropQcDbContext db,
        ClaimsPrincipal principal,
        IRoomInventoryLedgerQueryService? ledger = null,
        IRoomTreatmentService? treatments = null)
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
            new UserAccessService(db, configuration),
            roomInventoryLedgerQueryService: ledger,
            roomTreatmentService: treatments);
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
