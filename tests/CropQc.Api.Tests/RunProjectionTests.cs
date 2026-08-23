using System.Security.Claims;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CropQc.Api.Tests;

public sealed class RunProjectionTests
{
    [Theory]
    [InlineData("Apple", 1, 22)]
    [InlineData("Pear", 1, 23)]
    [InlineData("Apple", 10, 220)]
    [InlineData("Pear", 10, 230)]
    public void GrossBoxFormula_UsesCommoditySpecificPoundsPerBin(string commodity, int bins, int expectedBoxes)
    {
        var result = Calculate(commodity, bins, 80, 90);

        Assert.Equal(expectedBoxes, result.ProjectedBoxes);
        Assert.Equal(expectedBoxes, result.RoundedProjectedBoxes);
        Assert.Equal(bins * (commodity == "Apple" ? 880m : 920m), result.ProjectedPounds);
    }

    [Fact]
    public void MixedCommodityTotals_AreCalculatedSourceBySource()
    {
        var apple = Calculate("Apple", 2, 80, 90);
        var pear = Calculate("Pear", 3, 80, 90);

        Assert.Equal(5, apple.PlannedBins + pear.PlannedBins);
        Assert.Equal(4520m, apple.ProjectedPounds + pear.ProjectedPounds);
        Assert.Equal(113m, apple.ProjectedBoxes + pear.ProjectedBoxes);
    }

    [Fact]
    public void WholeBoxCategories_AreFlooredIndependentlyWithoutRoundingUp()
    {
        var result = RunProjectionCalculationService.Calculate(
            "Apple",
            1,
            880m,
            920m,
            40m,
            [new(80), new(90), new(100)]);

        Assert.Equal(22, result.RoundedProjectedBoxes);
        Assert.Equal(21, result.SizeAllocations.Sum(x => x.RoundedProjectedBoxes));
        Assert.Equal(7, result.SizeAllocations.Single(x => x.SizeCategory == 80).RoundedProjectedBoxes);
        Assert.Equal(7, result.SizeAllocations.Single(x => x.SizeCategory == 90).RoundedProjectedBoxes);
        Assert.Equal(7, result.SizeAllocations.Single(x => x.SizeCategory == 100).RoundedProjectedBoxes);
    }

    [Fact]
    public void SizeDistribution_UsesOnlySuppliedMeaningfulCalculatedSizes()
    {
        var result = RunProjectionCalculationService.Calculate(
            "Apple",
            1,
            880m,
            920m,
            40m,
            [new(80), new(80), new(100)]);

        Assert.Equal(3, result.SizeAllocations.Sum(x => x.SampleCount));
        Assert.Equal(66.6667m, result.SizeAllocations.Single(x => x.SizeCategory == 80).Percentage);
        Assert.Equal(100m, result.SizeAllocations.Sum(x => x.Percentage));
    }

    [Fact]
    public void SparseAndMissingSampleData_ReturnClearWarnings()
    {
        var sparse = Calculate("Apple", 1, 80);
        var missing = RunProjectionCalculationService.Calculate("Apple", 1, 880m, 920m, 40m, []);

        Assert.Contains("Sparse sample", sparse.Warning);
        Assert.Empty(missing.SizeAllocations);
        Assert.Contains("No meaningful calculated size data", missing.Warning);
        Assert.Equal(22m, missing.ProjectedBoxes);
    }

    [Fact]
    public void UnknownCommodity_DoesNotSilentlyAssumeApple()
    {
        var result = Calculate("Stone fruit", 5, 80);

        Assert.Equal("Unknown", result.Commodity);
        Assert.Equal(0m, result.PoundsPerBin);
        Assert.Equal(0m, result.ProjectedBoxes);
        Assert.Contains("Resolve the fruit profile", result.Warning);
    }

    [Theory]
    [InlineData(100, 22, 0)]
    [InlineData(90, 19, 2)]
    [InlineData(85, 18, 3)]
    [InlineData(0, 0, 22)]
    public void ExpectedPackout_DerivesComplementaryCullAndFloorsWholeBoxes(
        decimal packout,
        int expectedPacked,
        int expectedCull)
    {
        var result = CalculateWithPackout(packout, [80, 90, 100], ["US #1", "Fancy", "C Grade"]);

        Assert.Equal(packout, result.ExpectedPackoutPercent);
        Assert.Equal(100m - packout, result.ExpectedCullPercent);
        Assert.Equal(expectedPacked, result.RoundedPackedProjectedBoxes);
        Assert.Equal(expectedCull, result.RoundedCullProjectedBoxes);
        Assert.All(result.SizeAllocations, x => Assert.Equal(decimal.Floor(x.PackedProjectedBoxes), x.RoundedPackedProjectedBoxes));
        Assert.All(result.SizeAllocations, x => Assert.Equal(decimal.Floor(x.CullProjectedBoxes), x.RoundedCullProjectedBoxes));
        Assert.All(result.GradeAllocations, x => Assert.Equal(decimal.Floor(x.PackedBoxes), x.RoundedPackedBoxes));
        Assert.All(result.GradeAllocations, x => Assert.Equal(decimal.Floor(x.CullBoxes), x.RoundedCullBoxes));
    }

    [Fact]
    public void EightyFivePercentPackout_RemovesFifteenPercentNotEightyFive()
    {
        var result = CalculateWithPackout(85m, [80, 90], ["US #1", "Fancy"]);

        Assert.Equal(18.7m, result.PackedProjectedBoxes);
        Assert.Equal(3.3m, result.CullProjectedBoxes);
        Assert.Equal(85m, result.PackedProjectedBoxes / result.ProjectedBoxes * 100m);
    }

    [Fact]
    public void DecimalPackout_IsAppliedProportionallyToEverySizeAndGrade()
    {
        var result = CalculateWithPackout(
            82.5m,
            [80, 80, 90, 100],
            ["US #1", "US #1", "Fancy", "C Grade"]);

        Assert.All(result.SizeAllocations, x => Assert.Equal(x.UnroundedProjectedBoxes * 0.825m, x.PackedProjectedBoxes));
        Assert.All(result.GradeAllocations, x => Assert.Equal(x.GrossBoxes * 0.825m, x.PackedBoxes));
        Assert.Equal(
            result.GradeAllocations.Select(x => x.Percentage),
            result.GradeAllocations.Select(x => decimal.Round(x.PackedBoxes / result.PackedProjectedBoxes * 100m, 4)));
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100.01)]
    public void OutOfRangePackout_IsRejected(decimal value) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => CalculateWithPackout(value, [80], ["US #1"]));

    [Fact]
    public void BlankPackout_PreservesGrossAndLeavesPackedOutputUncalculated()
    {
        var result = RunProjectionCalculationService.Calculate(
            "Apple", 1, 880m, 920m, 40m, null,
            [new(80), new(90)],
            [new("US #1")]);

        Assert.Equal(22m, result.ProjectedBoxes);
        Assert.Null(result.ExpectedPackoutPercent);
        Assert.Equal(0m, result.PackedProjectedBoxes);
        Assert.Contains("required", result.Warning);
    }

    [Fact]
    public void MissingGradeData_DoesNotInventGradeDistribution()
    {
        var result = CalculateWithPackout(85m, [80, 90], []);

        Assert.Empty(result.GradeAllocations);
        Assert.Contains("Grade breakdown is unavailable", result.Warning);
    }

    [Fact]
    public void DistributionDenominators_AreIndependent()
    {
        var result = RunProjectionCalculationService.Calculate(
            "Apple", 1, 880m, 920m, 40m, 90m,
            [new(80), new(90), new(100)],
            [new("US #1"), new("Fancy")],
            1);

        Assert.Equal(3, result.SizeBasisFruitCount);
        Assert.Equal(2, result.GradeBasisFruitCount);
        Assert.Equal(1, result.JointSizeGradeBasisFruitCount);
    }

    [Fact]
    public void CombinedPackout_IsNotPresentedAsCompleteWhenAnySourceIsMissingItsAssumption()
    {
        var model = new RunProjectionDetailViewModel
        {
            TotalProjectedPounds = 1600m,
            TotalPackedProjectedPounds = 680m,
            Sources =
            [
                new() { ExpectedPackoutPercent = 85m },
                new() { ExpectedPackoutPercent = null }
            ]
        };

        Assert.Null(model.EffectivePackoutPercent);
        model.Sources = [new() { ExpectedPackoutPercent = 85m }];
        model.TotalProjectedPounds = 800m;
        Assert.Equal(85m, model.EffectivePackoutPercent);
    }

    [Theory]
    [InlineData(" apple ", "Apple")]
    [InlineData("PEAR", "Pear")]
    [InlineData("", "Unknown")]
    public void CommodityClassification_UsesCentralizedFruitTypeMetadata(string value, string expected) =>
        Assert.Equal(expected, RunProjectionCalculationService.NormalizeCommodity(value));

    [Fact]
    public void Rooms_RemovesMovedOperationalFormsAndLinksToCombinedArea()
    {
        var room = ReadRepositoryFile("src", "CropQc.Web", "Views", "Home", "Room.cshtml");
        var card = ReadRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_RoomLotCard.cshtml");

        Assert.DoesNotContain("Transfer Bins", room);
        Assert.DoesNotContain("True Up Current Lot Bins", room);
        Assert.DoesNotContain("Packout Projections", room);
        Assert.Contains("Open in Bins Run &amp; Transfers", room);
        Assert.Contains("SourceKey=", card);
        Assert.Contains("Plan Run / Manage Inventory", card);
    }

    [Fact]
    public void CombinedArea_ContainsGuidedSectionsAndPlanningSafeguards()
    {
        var view = ReadRepositoryFile("src", "CropQc.Web", "Views", "BinsRun", "Index.cshtml");
        var service = ReadRepositoryFile("src", "CropQc.Web", "Services", "RunProjectionService.cs");
        var binsRun = ReadRepositoryFile("src", "CropQc.Web", "Services", "BinsRunService.cs");

        Assert.Contains("Run Planner", view);
        Assert.Contains("Record Actual Run", view);
        Assert.Contains("Transfer Bins", view);
        Assert.Contains("True Up Inventory", view);
        Assert.Contains("Recent Activity", view);
        Assert.Contains("receipt-backed samples for the grower lot", view);
        Assert.Contains("Commercial Packs are not yet available", view);
        Assert.Contains("whole 40-pound boxes", view);
        Assert.Contains("planning estimate", view);
        Assert.Contains("FieldSampleBlockResolution != \"Suggested\"", service);
        Assert.Contains("Available quantity changed before save", binsRun);
        Assert.Contains("must be mapped to real inventory", binsRun);
        Assert.Contains("projection cannot be converted to an actual run", binsRun);
        Assert.Contains("ConvertSourceToActualRun", binsRun);
        Assert.Contains("A Preharvest projection cannot create an actual Bins Run", binsRun);
        Assert.Contains("ProjectionMode", view);
        Assert.Contains("Preharvest", view);
        Assert.Contains("Inventory", view);
        Assert.Contains("FieldSamples", view);
        Assert.Contains("Create Inventory Projection", view);
    }

    [Fact]
    public void ProjectionModel_IsNormalizedAndStoresHistoricalAssumptions()
    {
        var model = ReadRepositoryFile("src", "CropQc.Data", "Entities", "RunProjectionModels.cs");
        var migration = Directory.GetFiles(RepositoryDirectory("src", "CropQc.Data", "Migrations"), "*AddRunProjections.cs").Single();
        var migrationText = File.ReadAllText(migration);

        Assert.Contains("class RunProjection", model);
        Assert.Contains("class RunProjectionSource", model);
        Assert.Contains("class RunProjectionSizeResult", model);
        Assert.Contains("ApplePoundsPerBin", model);
        Assert.Contains("PearPoundsPerBin", model);
        Assert.Contains("StandardBoxWeightPounds", model);
        Assert.Contains("CreateTable(", migrationText);
        Assert.DoesNotContain("DropColumn(", migrationText);
        Assert.DoesNotContain("DropTable(", migrationText[..migrationText.IndexOf("protected override void Down", StringComparison.Ordinal)]);
    }

    [Fact]
    public async Task DraftProjection_SupportsMultipleIndependentSourcesWithoutInventoryMutation()
    {
        await using var db = CreateDbContext();
        var bins = new PlanningBinsRunService(
        [
            Inventory("R:1", 1, 20, "Apple"),
            Inventory("R:2", 2, 30, "Pear")
        ]);
        var service = CreateProjectionService(db, bins);
        var created = await service.CreateAsync(
            new RunProjectionCreateForm
            {
                PlannedRunDate = new(2026, 7, 24),
                Name = "Day shift",
                ProjectionMode = RunProjectionModes.Inventory,
                FacilityWarehouseId = 1
            },
            Owner(),
            CancellationToken.None);

        Assert.Null(created.Error);
        Assert.Null(await service.AddSourceAsync(new RunProjectionAddSourceForm
        {
            ProjectionId = created.Id!.Value,
            SourceKey = "R:1",
            PlannedBins = 4,
            SelectedQcSource = RunProjectionQcSourceTypes.None,
            ConcurrencyVersion = 1
        }, Owner(), CancellationToken.None));
        Assert.Null(await service.AddSourceAsync(new RunProjectionAddSourceForm
        {
            ProjectionId = created.Id.Value,
            SourceKey = "R:2",
            PlannedBins = 6,
            SelectedQcSource = RunProjectionQcSourceTypes.None,
            ConcurrencyVersion = 2
        }, Owner(), CancellationToken.None));

        var projection = await db.RunProjections.Include(x => x.Sources).SingleAsync();
        Assert.Equal(10, projection.TotalPlannedBins);
        Assert.Equal([4, 6], projection.Sources.OrderBy(x => x.SortOrder).Select(x => x.PlannedBins));
        Assert.Empty(await db.BinsRunEntries.ToListAsync());
        Assert.Empty(await db.RoomInventoryAdjustments.ToListAsync());
    }

    [Fact]
    public async Task AvailabilityOverrideAndConcurrency_AreExplicit()
    {
        await using var db = CreateDbContext();
        var service = CreateProjectionService(db, new PlanningBinsRunService([Inventory("R:1", 1, 5, "Apple")]));
        var created = await service.CreateAsync(
            new RunProjectionCreateForm
            {
                PlannedRunDate = new(2026, 7, 24),
                Name = "Availability check",
                ProjectionMode = RunProjectionModes.Inventory,
                FacilityWarehouseId = 1
            },
            Owner(),
            CancellationToken.None);
        var form = new RunProjectionAddSourceForm
        {
            ProjectionId = created.Id!.Value,
            SourceKey = "R:1",
            PlannedBins = 6,
            SelectedQcSource = RunProjectionQcSourceTypes.None,
            ConcurrencyVersion = 1
        };

        Assert.Contains("exceed", await service.AddSourceAsync(form, Owner(), CancellationToken.None), StringComparison.OrdinalIgnoreCase);
        form.AvailabilityOverrideAcknowledged = true;
        Assert.Null(await service.AddSourceAsync(form, Owner(), CancellationToken.None));
        Assert.Contains("changed after the page loaded", await service.UpdateHeaderAsync(new RunProjectionHeaderForm
        {
            Id = created.Id.Value,
            Name = "Stale update",
            PlannedRunDate = new(2026, 7, 25),
            FacilityWarehouseId = 1,
            ConcurrencyVersion = 1
        }, Owner(), CancellationToken.None), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApplyToAllPackout_UpdatesExistingSourcesAndKeepsLinesIndependentlyEditable()
    {
        await using var db = CreateDbContext();
        var service = CreateProjectionService(db, new PlanningBinsRunService(
        [
            Inventory("R:1", 1, 5, "Apple"),
            Inventory("R:2", 2, 5, "Pear")
        ]));
        var created = await service.CreateAsync(
            new RunProjectionCreateForm
            {
                PlannedRunDate = new(2026, 7, 24),
                Name = "Packout",
                ProjectionMode = RunProjectionModes.Inventory,
                FacilityWarehouseId = 1
            },
            Owner(),
            CancellationToken.None);
        foreach (var item in new[] { ("R:1", 1L), ("R:2", 2L) })
        {
            Assert.Null(await service.AddSourceAsync(new RunProjectionAddSourceForm
            {
                ProjectionId = created.Id!.Value,
                SourceKey = item.Item1,
                PlannedBins = 1,
                SelectedQcSource = RunProjectionQcSourceTypes.None,
                ConcurrencyVersion = item.Item2
            }, Owner(), CancellationToken.None));
        }

        Assert.Null(await service.ApplyPackoutToAllAsync(new RunProjectionApplyPackoutForm
        {
            ProjectionId = created.Id!.Value,
            ExpectedPackoutPercent = 75m,
            ConcurrencyVersion = 3
        }, Owner(), CancellationToken.None));
        Assert.All((await db.RunProjectionSources.ToListAsync()), x => Assert.Equal(75m, x.ExpectedPackoutPercent));

        var first = await db.RunProjectionSources.OrderBy(x => x.Id).FirstAsync();
        Assert.Null(await service.UpdateSourceAsync(new RunProjectionUpdateSourceForm
        {
            ProjectionId = created.Id.Value,
            SourceId = first.Id,
            PlannedBins = 1,
            SelectedQcSource = RunProjectionQcSourceTypes.None,
            ExpectedPackoutPercent = 90m,
            SortOrder = first.SortOrder,
            ConcurrencyVersion = 4
        }, Owner(), CancellationToken.None));
        Assert.Equal([90m, 75m], await db.RunProjectionSources.OrderBy(x => x.Id).Select(x => x.ExpectedPackoutPercent!.Value).ToListAsync());
    }

    [Fact]
    public async Task PreharvestProjection_UsesExplicitConfirmedBlockAndSpecificFieldSampleWithoutInventory()
    {
        await using var db = CreateDbContext();
        var seeded = await SeedPreharvestFieldSamplesAsync(db);
        var service = CreateProjectionService(db, new PlanningBinsRunService([]));
        var created = await service.CreateAsync(
            new RunProjectionCreateForm
            {
                PlannedRunDate = new(2026, 7, 24),
                Name = "Preharvest Bartlett",
                ProjectionMode = RunProjectionModes.Preharvest,
                FacilityWarehouseId = 1
            },
            Owner(),
            CancellationToken.None);

        Assert.Null(created.Error);
        var candidates = await service.SearchSourcesAsync(
            "WP ORCHARD",
            null,
            null,
            RunProjectionModes.Preharvest,
            Owner(),
            CancellationToken.None);
        var candidate = Assert.Single(candidates, x => x.CanonicalOrchardBlockId == seeded.NorthBlockId);
        Assert.Equal($"B:{seeded.NorthBlockId}:{seeded.ProfileId}", candidate.SourceKey);
        Assert.Equal(seeded.NewestNorthSampleId, candidate.DefaultFieldSampleId);
        Assert.Null(candidate.AvailableBins);

        var choices = await service.GetFieldSampleChoicesAsync(
            created.Id!.Value,
            seeded.NorthBlockId,
            seeded.ProfileId,
            Owner(),
            CancellationToken.None);
        Assert.Equal([seeded.NewestNorthSampleId, seeded.OlderNorthSampleId], choices.Select(x => x.SampleId!.Value));
        Assert.DoesNotContain(choices, x => x.SampleId == seeded.DeletedNorthSampleId);
        Assert.DoesNotContain(choices, x => x.SampleId == seeded.SuggestedNorthSampleId);

        Assert.Null(await service.AddSourceAsync(new RunProjectionAddSourceForm
        {
            ProjectionId = created.Id.Value,
            SourceKey = candidate.SourceKey,
            PlannedBins = 12,
            SelectedQcSource = $"FieldSample:{seeded.OlderNorthSampleId}",
            ConcurrencyVersion = 1
        }, Owner(), CancellationToken.None));

        var projection = await db.RunProjections.Include(x => x.Sources).SingleAsync();
        var source = Assert.Single(projection.Sources);
        Assert.Equal(RunProjectionModes.Preharvest, projection.ProjectionMode);
        Assert.Equal(RunProjectionSourceTypes.FieldSample, source.SourceType);
        Assert.Equal(seeded.OlderNorthSampleId, source.FieldSampleId);
        Assert.Equal(seeded.OlderNorthSampleId, source.SelectedQcSampleId);
        Assert.Equal(12, source.PlannedBins);
        Assert.True(source.ExpectedPackoutUsedDefault);
        Assert.Null(source.InventoryKey);
        Assert.Null(source.ReceiptId);
        Assert.Null(source.WarehouseId);
        Assert.Null(source.RoomId);
        Assert.Empty(await db.BinsRunEntries.ToListAsync());
        Assert.Empty(await db.RoomInventoryAdjustments.ToListAsync());

        Assert.Null(await service.UpdateSourceAsync(new RunProjectionUpdateSourceForm
        {
            ProjectionId = projection.Id,
            SourceId = source.Id,
            PlannedBins = source.PlannedBins,
            SelectedQcSource = $"FieldSample:{seeded.NewestNorthSampleId}",
            ExpectedPackoutPercent = source.ExpectedPackoutPercent,
            SortOrder = source.SortOrder,
            ConcurrencyVersion = 2
        }, Owner(), CancellationToken.None));
        Assert.Equal(seeded.NewestNorthSampleId, (await db.RunProjectionSources.SingleAsync()).FieldSampleId);
    }

    [Fact]
    public async Task PreharvestProjection_CombinesBlocksAndUsesWeightBasedEffectivePackout()
    {
        await using var db = CreateDbContext();
        var seeded = await SeedPreharvestFieldSamplesAsync(db);
        var service = CreateProjectionService(db, new PlanningBinsRunService([]));
        var created = await service.CreateAsync(
            new RunProjectionCreateForm
            {
                PlannedRunDate = new(2026, 7, 24),
                Name = "Two blocks",
                ProjectionMode = RunProjectionModes.Preharvest,
                FacilityWarehouseId = 1
            },
            Owner(),
            CancellationToken.None);

        Assert.Null(await service.AddSourceAsync(new RunProjectionAddSourceForm
        {
            ProjectionId = created.Id!.Value,
            SourceKey = $"B:{seeded.NorthBlockId}:{seeded.ProfileId}",
            PlannedBins = 1,
            SelectedQcSource = $"FieldSample:{seeded.NewestNorthSampleId}",
            ExpectedPackoutPercent = 80,
            ConcurrencyVersion = 1
        }, Owner(), CancellationToken.None));
        Assert.Null(await service.AddSourceAsync(new RunProjectionAddSourceForm
        {
            ProjectionId = created.Id.Value,
            SourceKey = $"B:{seeded.SouthBlockId}:{seeded.ProfileId}",
            PlannedBins = 3,
            SelectedQcSource = $"FieldSample:{seeded.SouthSampleId}",
            ExpectedPackoutPercent = 60,
            ConcurrencyVersion = 2
        }, Owner(), CancellationToken.None));

        var planner = await service.GetPlannerAsync(
            new(2026, 7, 24),
            created.Id,
            "All",
            "Active",
            "Facility",
            Owner(),
            CancellationToken.None);
        var detail = Assert.IsType<RunProjectionDetailViewModel>(planner.SelectedProjection);
        Assert.Equal(4, detail.TotalPlannedBins);
        Assert.Equal(2, detail.Sources.Count);
        Assert.Equal(65m, detail.EffectivePackoutPercent);
        Assert.Equal(detail.Sources.Sum(x => x.PackedProjectedBoxes), detail.TotalPackedProjectedBoxes);
        Assert.Equal(detail.Sources.Sum(x => x.CullProjectedBoxes), detail.TotalCullProjectedBoxes);
    }

    [Fact]
    public async Task PreharvestProjection_CannotUseInventoryOrBecomeActualWithoutExplicitMapping()
    {
        await using var db = CreateDbContext();
        var seeded = await SeedPreharvestFieldSamplesAsync(db);
        var inventory = Inventory("R:1", 1, 25, "Pear") with
        {
            CanonicalOrchardBlockId = seeded.NorthBlockId,
            FruitProfileId = seeded.ProfileId
        };
        var service = CreateProjectionService(db, new PlanningBinsRunService([inventory]));
        var created = await service.CreateAsync(
            new RunProjectionCreateForm
            {
                PlannedRunDate = new(2026, 7, 24),
                Name = "Preharvest to inventory",
                ProjectionMode = RunProjectionModes.Preharvest,
                FacilityWarehouseId = 1
            },
            Owner(),
            CancellationToken.None);

        Assert.Contains("Field Sample", await service.AddSourceAsync(new RunProjectionAddSourceForm
        {
            ProjectionId = created.Id!.Value,
            SourceKey = inventory.InventoryKey,
            PlannedBins = 2,
            SelectedQcSource = RunProjectionQcSourceTypes.None,
            ConcurrencyVersion = 1
        }, Owner(), CancellationToken.None), StringComparison.OrdinalIgnoreCase);
        Assert.Null(await service.AddSourceAsync(new RunProjectionAddSourceForm
        {
            ProjectionId = created.Id.Value,
            SourceKey = $"B:{seeded.NorthBlockId}:{seeded.ProfileId}",
            PlannedBins = 2,
            SelectedQcSource = $"FieldSample:{seeded.NewestNorthSampleId}",
            ConcurrencyVersion = 1
        }, Owner(), CancellationToken.None));

        var source = await db.RunProjectionSources.SingleAsync();
        var transition = await service.CreateInventoryFromPreharvestAsync(new RunProjectionCreateInventoryForm
        {
            Id = created.Id.Value,
            Name = "Mapped inventory plan",
            PlannedRunDate = new(2026, 7, 25),
            FacilityWarehouseId = 1,
            ConcurrencyVersion = 2,
            Mappings =
            [
                new RunProjectionInventoryMappingForm
                {
                    PreharvestSourceId = source.Id,
                    InventoryKey = inventory.InventoryKey
                }
            ]
        }, Owner(), CancellationToken.None);

        Assert.Null(transition.Error);
        var projections = await db.RunProjections.Include(x => x.Sources).OrderBy(x => x.Id).ToListAsync();
        Assert.Equal([RunProjectionModes.Preharvest, RunProjectionModes.Inventory], projections.Select(x => x.ProjectionMode));
        Assert.Equal(RunProjectionStatuses.Superseded, projections[0].Status);
        Assert.Equal(RunProjectionStatuses.Draft, projections[1].Status);
        Assert.Equal(projections[0].Id, projections[1].SourceProjectionId);
        Assert.Equal(source.Id, Assert.Single(projections[1].Sources).SourceProjectionSourceId);
        Assert.Empty(await db.BinsRunEntries.ToListAsync());
        Assert.Empty(await db.RoomInventoryAdjustments.ToListAsync());
    }

    [Fact]
    public async Task PreharvestReady_RejectsMissingBinsFieldSampleAndPackout()
    {
        await using var db = CreateDbContext();
        var seeded = await SeedPreharvestFieldSamplesAsync(db);
        var service = CreateProjectionService(db, new PlanningBinsRunService([]));
        var created = await service.CreateAsync(
            new RunProjectionCreateForm
            {
                PlannedRunDate = new(2026, 7, 24),
                Name = "Readiness",
                ProjectionMode = RunProjectionModes.Preharvest,
                FacilityWarehouseId = 1
            },
            Owner(),
            CancellationToken.None);
        Assert.Null(await service.AddSourceAsync(new RunProjectionAddSourceForm
        {
            ProjectionId = created.Id!.Value,
            SourceKey = $"B:{seeded.NorthBlockId}:{seeded.ProfileId}",
            PlannedBins = 3,
            SelectedQcSource = $"FieldSample:{seeded.NewestNorthSampleId}",
            ExpectedPackoutPercent = 85,
            ConcurrencyVersion = 1
        }, Owner(), CancellationToken.None));
        var source = await db.RunProjectionSources.SingleAsync();

        source.PlannedBins = 0;
        await db.SaveChangesAsync();
        Assert.Contains("bin quantity", await service.MarkReadyAsync(
            new RunProjectionStatusForm { Id = created.Id.Value, ConcurrencyVersion = 2 },
            Owner(),
            CancellationToken.None), StringComparison.OrdinalIgnoreCase);

        source.PlannedBins = 3;
        source.SelectedQcSampleId = null;
        await db.SaveChangesAsync();
        Assert.Contains("Field Sample", await service.MarkReadyAsync(
            new RunProjectionStatusForm { Id = created.Id.Value, ConcurrencyVersion = 2 },
            Owner(),
            CancellationToken.None), StringComparison.OrdinalIgnoreCase);

        source.SelectedQcSampleId = seeded.NewestNorthSampleId;
        source.ExpectedPackoutPercent = null;
        await db.SaveChangesAsync();
        Assert.Contains("packout", await service.MarkReadyAsync(
            new RunProjectionStatusForm { Id = created.Id.Value, ConcurrencyVersion = 2 },
            Owner(),
            CancellationToken.None), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PreharvestMigration_IsAdditiveProviderCompatibleAndDefaultsExistingPlansToInventory()
    {
        var migration = Directory.GetFiles(
            RepositoryDirectory("src", "CropQc.Data", "Migrations"),
            "*AddPreharvestRunProjectionMode.cs").Single();
        var text = File.ReadAllText(migration);
        var up = text[..text.IndexOf("protected override void Down", StringComparison.Ordinal)];

        Assert.Contains("defaultValue: \"Inventory\"", up);
        Assert.Contains("MigrationProviderTypes.StoreType(migrationBuilder, \"bit\", \"boolean\")", up);
        Assert.Contains("MigrationProviderTypes.StoreType(migrationBuilder, \"nvarchar(25)\", \"character varying(25)\")", up);
        Assert.DoesNotContain("DropColumn(", up);
        Assert.DoesNotContain("DropTable(", up);
        Assert.DoesNotContain("DeleteData(", up);
        Assert.DoesNotContain("UpdateData(", up);
    }

    [Fact]
    public async Task FinalizedProjection_CannotBeCancelledOrEdited()
    {
        await using var db = CreateDbContext();
        db.RunProjections.Add(new RunProjection
        {
            PlannedRunDate = new(2026, 7, 24),
            Name = "Converted plan",
            Status = RunProjectionStatuses.Converted,
            CropYear = 2026,
            ApplePoundsPerBin = 880,
            PearPoundsPerBin = 920,
            StandardBoxWeightPounds = 40,
            CreatedAt = TestNow,
            UpdatedAt = TestNow
        });
        await db.SaveChangesAsync();
        var projection = await db.RunProjections.SingleAsync();
        var service = CreateProjectionService(db, new PlanningBinsRunService([]));

        var error = await service.CancelAsync(new RunProjectionStatusForm
        {
            Id = projection.Id,
            ConcurrencyVersion = projection.ConcurrencyVersion,
            Reason = "Should not be accepted"
        }, Owner(), CancellationToken.None);

        Assert.Contains("cannot be cancelled", error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(RunProjectionStatuses.Converted, (await db.RunProjections.SingleAsync()).Status);
    }

    [Fact]
    public void PlannerQueries_RequireCompletedCurrentCropYearQcAndConfirmedFieldSamples()
    {
        var service = ReadRepositoryFile("src", "CropQc.Web", "Services", "RunProjectionService.cs");

        Assert.Contains("x.Receipt!.CropYear == cropYear", service);
        Assert.Contains("x.Status == \"Ready to Send\"", service);
        Assert.Contains("x.Status == \"Sent\"", service);
        Assert.Contains("x.FieldSampleBlockResolution != \"Suggested\"", service);
        Assert.Contains("x.CanonicalOrchardBlockId == blockId && x.FieldSampleFruitProfileId == source.FruitProfileId", service);
        Assert.Contains("FieldSampleCropWindow(cropYear)", service);
        Assert.Contains("if (useAutomaticPriority && source.ReceiptId is long receiptId)", service);
        Assert.Contains("x.SampleType.IsActive", service);
        Assert.Contains("x.SampleType.Name != FieldSampleTypeName", service);
        Assert.Contains("x.FruitReadings.Any(row => row.SizeCategory != null)", service);
        Assert.Contains("x.FruitReadings.Any(row => row.GradeId != null)", service);
    }

    [Fact]
    public void PlannerUi_ProvidesHistoryDateAccessAndSpreadsheetSafeExport()
    {
        var view = ReadRepositoryFile("src", "CropQc.Web", "Views", "BinsRun", "Index.cshtml");
        var controller = ReadRepositoryFile("src", "CropQc.Web", "Controllers", "BinsRunController.cs");

        Assert.Contains("Open date", view);
        Assert.Contains("Converted to Actual Run", view);
        Assert.Contains("safe[0] is '=' or '+' or '-' or '@'", controller);
        Assert.Contains("Expected Packout %", view);
        Assert.Contains("Expected Cull/Loss %", view);
        Assert.Contains("Apply to All", view);
        Assert.Contains("Refresh From Current QC Data", view);
        Assert.Contains("data-projection-source-form", view);
        Assert.Contains("X-Projection-Autosave", view);
        Assert.Contains("Conflict — reload before saving", view);
        Assert.Contains("X-Projection-Autosave", controller);
        Assert.Contains("return BadRequest(new { error });", controller);
    }

    [Fact]
    public async Task ProjectionCreation_RequiresWpOrEbsFacility()
    {
        await using var db = CreateDbContext();
        var service = CreateProjectionService(db, new PlanningBinsRunService([]));
        var result = await service.CreateAsync(new RunProjectionCreateForm
        {
            Name = "No facility",
            PlannedRunDate = new(2026, 7, 24),
            ProjectionMode = RunProjectionModes.Preharvest
        }, Owner(), CancellationToken.None);

        Assert.Null(result.Id);
        Assert.Contains("WP or EBS", result.Error);
        Assert.Empty(await db.RunProjections.ToListAsync());
    }

    [Theory]
    [InlineData(1, "WP")]
    [InlineData(4, "EBS")]
    public async Task ProjectionCreation_PersistsOperationalFacility(int facilityId, string expectedCode)
    {
        await using var db = CreateDbContext();
        var service = CreateProjectionService(db, new PlanningBinsRunService([]));
        var result = await service.CreateAsync(new RunProjectionCreateForm
        {
            Name = $"{expectedCode} run",
            PlannedRunDate = new(2026, 7, 24),
            ProjectionMode = RunProjectionModes.Preharvest,
            FacilityWarehouseId = facilityId
        }, Owner(), CancellationToken.None);

        Assert.Null(result.Error);
        var projection = await db.RunProjections.SingleAsync();
        Assert.Equal(facilityId, projection.FacilityWarehouseId);
        Assert.Equal(expectedCode, projection.FacilityCodeSnapshot);
    }

    [Fact]
    public async Task InventorySource_CannotCrossProjectionFacility()
    {
        await using var db = CreateDbContext();
        var ebsInventory = Inventory("EBS:1", 1, 10, "Apple") with { WarehouseId = 4, Facility = "EBS" };
        var service = CreateProjectionService(db, new PlanningBinsRunService([ebsInventory]));
        var created = await service.CreateAsync(new RunProjectionCreateForm
        {
            Name = "WP run",
            PlannedRunDate = new(2026, 7, 24),
            ProjectionMode = RunProjectionModes.Inventory,
            FacilityWarehouseId = 1
        }, Owner(), CancellationToken.None);

        var error = await service.AddSourceAsync(new RunProjectionAddSourceForm
        {
            ProjectionId = created.Id!.Value,
            SourceKey = ebsInventory.InventoryKey,
            PlannedBins = 1,
            SelectedQcSource = RunProjectionQcSourceTypes.None,
            ConcurrencyVersion = 1
        }, Owner(), CancellationToken.None);

        Assert.Contains("not the projection", error, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(await db.RunProjectionSources.ToListAsync());
    }

    [Fact]
    public async Task InventorySearch_IsServerFilteredByFacility()
    {
        await using var db = CreateDbContext();
        var profile = new FruitProfile
        {
            Name = "Bartlett",
            VarietyCode = "BART",
            FruitType = "Pear",
            ProductionType = "Conventional"
        };
        var room = new Room { WarehouseId = 4, Code = "EBS-1", Name = "EBS Room 1" };
        db.AddRange(profile, room);
        await db.SaveChangesAsync();
        db.Receipts.Add(new Receipt
        {
            CropYear = 2026,
            ReceivedAt = TestNow,
            CompuTechReceiptId = "EBS-REC-1",
            ReceiptType = "Truck receipt",
            WarehouseId = 4,
            RoomId = room.Id,
            FruitProfileId = profile.Id,
            GrowerName = "EBS GROWER",
            LotCode = "1080-01",
            BinCount = 10,
            CreatedAt = TestNow,
            UpdatedAt = TestNow
        });
        await db.SaveChangesAsync();
        var service = CreateProjectionService(db, new PlanningBinsRunService([]));

        var rows = await service.SearchSourcesAsync("", 4, null, RunProjectionModes.Inventory, Owner(), CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal("EBS", row.Facility);
        Assert.StartsWith("G:2026:", row.SourceKey);
        Assert.Contains("1080-01", row.Label);
    }

    [Fact]
    public async Task GrowerLotProjection_CombinesReceiptsByBinWeightAndRefreshPreservesTotalProjectedBins()
    {
        await using var db = CreateDbContext();
        var profile = new FruitProfile
        {
            Name = "Gala",
            VarietyCode = "GALA",
            FruitType = "Apple",
            ProductionType = "Conventional"
        };
        var room = new Room { WarehouseId = 1, Code = "WP-1", Name = "WP Room 1" };
        var sampleType = new SampleType { Name = "Receiving Sample" };
        db.AddRange(profile, room, sampleType);
        await db.SaveChangesAsync();

        Receipt SeedReceipt(string reference, int bins, int size, DateTimeOffset receivedAt)
        {
            var receipt = new Receipt
            {
                CropYear = 2026,
                ReceivedAt = receivedAt,
                CompuTechReceiptId = reference,
                ReceiptType = "Truck receipt",
                WarehouseId = 1,
                RoomId = room.Id,
                FruitProfileId = profile.Id,
                GrowerName = "WP ORCHARD",
                GrowerNumber = "1080",
                LotCode = "1080-LOT-7",
                BinCount = bins,
                CreatedAt = receivedAt,
                UpdatedAt = receivedAt
            };
            receipt.Samples.Add(new QcSample
            {
                SampleType = sampleType,
                Status = "Complete",
                StarchStatus = "Complete",
                PhotoStatus = "Complete",
                EmailStatus = "Not Sent",
                SampleTakenAt = receivedAt,
                CreatedAt = receivedAt,
                FruitReadings =
                {
                    new QcFruitReading
                    {
                        RowNumber = 1,
                        SizeCategory = size,
                        SizeStatus = "Calculated",
                        WeightGrams = 180m,
                        CreatedAt = receivedAt
                    }
                }
            });
            db.Receipts.Add(receipt);
            return receipt;
        }

        SeedReceipt("REC-10", 10, 80, TestNow.AddHours(-2));
        SeedReceipt("REC-30", 30, 100, TestNow.AddHours(-1));
        await db.SaveChangesAsync();
        var service = CreateProjectionService(db, new PlanningBinsRunService([]));
        var created = await service.CreateAsync(new RunProjectionCreateForm
        {
            Name = "Grower lot projection",
            PlannedRunDate = new(2026, 7, 24),
            ProjectionMode = RunProjectionModes.Inventory,
            FacilityWarehouseId = 1
        }, Owner(), CancellationToken.None);
        var candidate = Assert.Single(await service.SearchSourcesAsync(
            "1080-LOT-7", 1, null, RunProjectionModes.Inventory, Owner(), CancellationToken.None));
        var addError = await service.AddSourceAsync(new RunProjectionAddSourceForm
        {
            ProjectionId = created.Id!.Value,
            SourceKey = candidate.SourceKey,
            PlannedBins = 60,
            ExpectedPackoutPercent = 100m,
            ConcurrencyVersion = 1
        }, Owner(), CancellationToken.None);

        Assert.Null(addError);
        var source = await db.RunProjectionSources
            .Include(x => x.SizeResults)
            .SingleAsync();
        Assert.Equal(RunProjectionSourceTypes.GrowerLot, source.SourceType);
        Assert.Equal(40, source.ReceivedBinsSnapshot);
        Assert.Equal(20, source.AdditionalExpectedBinsSnapshot);
        Assert.Equal(25m, source.SizeResults.Single(x => x.SizeCategory == 80).Percentage);
        Assert.Equal(75m, source.SizeResults.Single(x => x.SizeCategory == 100).Percentage);

        SeedReceipt("REC-20", 20, 90, TestNow);
        await db.SaveChangesAsync();
        var projection = await db.RunProjections.SingleAsync();
        var refused = await service.RefreshSourceAsync(
            projection.Id, source.Id, projection.ConcurrencyVersion, false, Owner(), CancellationToken.None);
        Assert.Contains("Confirm refresh", refused);
        var refreshError = await service.RefreshSourceAsync(
            projection.Id, source.Id, projection.ConcurrencyVersion, true, Owner(), CancellationToken.None);

        Assert.Null(refreshError);
        await db.Entry(source).ReloadAsync();
        Assert.Equal(60, source.PlannedBins);
        Assert.Equal(60, source.ReceivedBinsSnapshot);
        Assert.Equal(0, source.AdditionalExpectedBinsSnapshot);
        Assert.NotNull(source.RefreshHistoryJson);
        Assert.Equal(3, JsonDocument.Parse(source.ContributingReceiptIdsJson!).RootElement.GetArrayLength());
    }

    [Fact]
    public async Task PlannerFilters_SeparateWpAndEbsAndComputeCalendarCounts()
    {
        await using var db = CreateDbContext();
        var service = CreateProjectionService(db, new PlanningBinsRunService([]));
        foreach (var facilityId in new[] { 1, 4 })
        {
            await service.CreateAsync(new RunProjectionCreateForm
            {
                Name = facilityId == 1 ? "WP run" : "EBS run",
                PlannedRunDate = new(2026, 7, 24),
                ProjectionMode = RunProjectionModes.Preharvest,
                FacilityWarehouseId = facilityId
            }, Owner(), CancellationToken.None);
        }

        var wp = await service.GetPlannerAsync(new(2026, 7, 24), null, "WP", "Active", "Facility", Owner(), CancellationToken.None);
        var ebs = await service.GetPlannerAsync(new(2026, 7, 24), null, "EBS", "Active", "Facility", Owner(), CancellationToken.None);
        var all = await service.GetPlannerAsync(new(2026, 7, 24), null, "All", "Active", "Facility", Owner(), CancellationToken.None);

        Assert.Single(wp.Projections);
        Assert.Equal("WP", wp.Projections[0].FacilityCode);
        Assert.Single(ebs.Projections);
        Assert.Equal("EBS", ebs.Projections[0].FacilityCode);
        Assert.Equal(2, all.Projections.Count);
        Assert.Equal(["WP", "EBS"], all.FacilityTotals.Select(x => x.FacilityCode));
        var day = all.CalendarDays.Single(x => x.Date == new DateOnly(2026, 7, 24));
        Assert.Equal(1, day.WpProjectionCount);
        Assert.Equal(1, day.EbsProjectionCount);
    }

    [Fact]
    public async Task FreshPlannerLoad_UsesPacificTodayWhileKeepingHistoricalAndLaterDatesAccessible()
    {
        await using var db = CreateDbContext();
        var service = CreateProjectionService(db, new PlanningBinsRunService([]));
        await CreateProjectionAsync(service, new DateOnly(2026, 6, 12), "Historical June run");
        await CreateProjectionAsync(service, new DateOnly(2026, 7, 29), "Upcoming run");
        await CreateProjectionAsync(service, new DateOnly(2026, 9, 2), "Later scheduled run");

        var planner = await service.GetPlannerAsync(
            null, null, "All", "Active", "Facility", Owner(), CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 7, 23), planner.PacificToday);
        Assert.Equal(planner.PacificToday, planner.SelectedDate);
        Assert.True(planner.CalendarDays.Single(x => x.Date == planner.PacificToday).IsToday);
        Assert.Contains(planner.CalendarDays, x => x.Date > planner.PacificToday);
        Assert.Contains(planner.HistoricalProjectionDates, x => x.Date == new DateOnly(2026, 6, 12));
        Assert.Contains(planner.LaterProjectionDates, x => x.Date == new DateOnly(2026, 9, 2));
        Assert.True(planner.HasUpcomingProjections);
    }

    [Fact]
    public async Task PlannerWithOnlyHistoricalProjections_StillOpensOnTodayAndShowsFutureEmptyState()
    {
        await using var db = CreateDbContext();
        var service = CreateProjectionService(db, new PlanningBinsRunService([]));
        await CreateProjectionAsync(service, new DateOnly(2026, 6, 12), "Historical June run");

        var planner = await service.GetPlannerAsync(
            null, null, "WP", "Active", "Facility", Owner(), CancellationToken.None);

        Assert.Equal(planner.PacificToday, planner.SelectedDate);
        Assert.False(planner.HasUpcomingProjections);
        Assert.Contains(planner.HistoricalProjectionDates, x => x.Date == new DateOnly(2026, 6, 12));
    }

    [Fact]
    public async Task ExplicitHistoricalProjection_RemainsSelectableWithoutChangingPacificToday()
    {
        await using var db = CreateDbContext();
        var service = CreateProjectionService(db, new PlanningBinsRunService([]));
        var projectionId = await CreateProjectionAsync(service, new DateOnly(2026, 6, 12), "Historical June run");

        var planner = await service.GetPlannerAsync(
            new DateOnly(2026, 6, 12),
            projectionId,
            "WP",
            "Active",
            "Facility",
            Owner(),
            CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 6, 12), planner.SelectedDate);
        Assert.Equal(new DateOnly(2026, 7, 23), planner.PacificToday);
        Assert.True(planner.IsDirectProjectionOpen);
        Assert.Contains(planner.CalendarDays, x => x.Date == planner.SelectedDate && x.IsSelected);
        Assert.Equal("WP", planner.SelectedFacility);
    }

    [Theory]
    [InlineData("2026-07-24T06:30:00Z", 2026, 7, 23)]
    [InlineData("2026-03-08T09:30:00Z", 2026, 3, 8)]
    [InlineData("2026-11-01T08:30:00Z", 2026, 11, 1)]
    public async Task PlannerToday_UsesAuthoritativePacificDateAcrossUtcAndDstBoundaries(
        string utc,
        int year,
        int month,
        int day)
    {
        await using var db = CreateDbContext();
        var service = CreateProjectionService(
            db,
            new PlanningBinsRunService([]),
            DateTimeOffset.Parse(utc));

        var planner = await service.GetPlannerAsync(
            null, null, "All", "Active", "Facility", Owner(), CancellationToken.None);

        Assert.Equal(new DateOnly(year, month, day), planner.PacificToday);
        Assert.Equal(planner.PacificToday, planner.SelectedDate);
    }

    [Fact]
    public void PlannerView_AnchorsFreshLoadsOnServerPacificTodayWithoutUsingBrowserLocalTime()
    {
        var view = ReadRepositoryFile("src", "CropQc.Web", "Views", "BinsRun", "Index.cshtml");
        var css = ReadRepositoryFile("src", "CropQc.Web", "wwwroot", "css", "site.css");
        var controller = ReadRepositoryFile("src", "CropQc.Web", "Controllers", "BinsRunController.cs");

        Assert.Contains("data-pacific-today", view);
        Assert.Contains("data-run-calendar-date", view);
        Assert.Contains("scrollIntoView({ block: \"nearest\", inline: \"start\" })", view);
        Assert.Contains("run-planner-preserve-date-once", view);
        Assert.Contains("window.location.replace(todayUrl)", view);
        Assert.Contains("data-run-planner-today-action", view);
        Assert.Contains("No projections are scheduled from today forward.", view);
        Assert.Contains("Earlier saved projection date", view);
        Assert.Contains("Later scheduled:", view);
        Assert.DoesNotContain("localStorage", view);
        Assert.DoesNotContain("new Date().", view);
        Assert.Contains("businessTime.PacificDate(businessTime.UtcNow)", controller);
        Assert.Contains("run-calendar-today-label", css);
        Assert.Contains("content: \"●\"", css);
    }

    [Fact]
    public async Task ReadyProjection_RejectsLegacyUnassignedRecord()
    {
        await using var db = CreateDbContext();
        var projection = LegacyProjection("Legacy", RunProjectionStatuses.Draft);
        db.RunProjections.Add(projection);
        await db.SaveChangesAsync();
        var service = CreateProjectionService(db, new PlanningBinsRunService([]));

        var error = await service.MarkReadyAsync(
            new RunProjectionStatusForm { Id = projection.Id, ConcurrencyVersion = projection.ConcurrencyVersion },
            Owner(),
            CancellationToken.None);

        Assert.Contains("WP or EBS", error);
    }

    [Fact]
    public async Task SoftDelete_HidesActiveAndRetainsProjectionSourcesAndAudit()
    {
        await using var db = CreateDbContext();
        var projection = LegacyProjection("Remove me", RunProjectionStatuses.Draft, 1, "WP");
        projection.Sources.Add(TestProjectionSource());
        db.RunProjections.Add(projection);
        await db.SaveChangesAsync();
        var sourceId = projection.Sources.Single().Id;
        var service = CreateProjectionService(db, new PlanningBinsRunService([]));
        var token = Guid.NewGuid();

        Assert.Null(await service.DeleteAsync(DeleteForm(projection, token), Owner(), CancellationToken.None));
        var saved = await db.RunProjections.SingleAsync(x => x.Id == projection.Id);
        Assert.True(saved.IsDeleted);
        Assert.Equal("Draft", saved.DeletedFromStatus);
        Assert.Equal(token, saved.DeletionOperationId);
        Assert.True(await db.RunProjectionSources.AnyAsync(x => x.Id == sourceId));
        Assert.True(await db.AuditLogs.AnyAsync(x => x.EntityName == nameof(RunProjection) && x.Action == "Delete"));
        var active = await service.GetPlannerAsync(projection.PlannedRunDate, null, "WP", "Active", "Facility", Owner(), CancellationToken.None);
        Assert.Empty(active.Projections);
        var deleted = await service.GetPlannerAsync(projection.PlannedRunDate, projection.Id, "WP", "Deleted", "Facility", Owner(), CancellationToken.None);
        Assert.Single(deleted.Projections);
        Assert.True(deleted.SelectedProjection!.IsDeleted);
        Assert.False(deleted.SelectedProjection.CanEditRecord);
        Assert.True(await db.AuditLogs.AnyAsync(x => x.EntityName == nameof(RunProjection)
            && x.EntityKey == projection.Id.ToString()
            && x.Action == "InspectDeleted"));
    }

    [Fact]
    public async Task DeletedProjection_CannotBeCancelledByModifiedRequest()
    {
        await using var db = CreateDbContext();
        var projection = LegacyProjection("Deleted draft", RunProjectionStatuses.Draft, 1, "WP");
        projection.IsDeleted = true;
        projection.DeletionOperationId = Guid.NewGuid();
        db.RunProjections.Add(projection);
        await db.SaveChangesAsync();
        var service = CreateProjectionService(db, new PlanningBinsRunService([]));

        var error = await service.CancelAsync(
            new RunProjectionStatusForm
            {
                Id = projection.Id,
                ConcurrencyVersion = projection.ConcurrencyVersion,
                Reason = "Modified request"
            },
            Owner(),
            CancellationToken.None);

        Assert.Contains("read-only", error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(RunProjectionStatuses.Draft, (await db.RunProjections.SingleAsync()).Status);
    }

    [Fact]
    public async Task SoftDelete_RetryWithSameOperationTokenIsIdempotent()
    {
        await using var db = CreateDbContext();
        var projection = LegacyProjection("Idempotent", RunProjectionStatuses.Draft, 1, "WP");
        db.RunProjections.Add(projection);
        await db.SaveChangesAsync();
        var service = CreateProjectionService(db, new PlanningBinsRunService([]));
        var form = DeleteForm(projection, Guid.NewGuid());

        Assert.Null(await service.DeleteAsync(form, Owner(), CancellationToken.None));
        Assert.Null(await service.DeleteAsync(form, Owner(), CancellationToken.None));
        Assert.Single(await db.AuditLogs.Where(x => x.Action == "Delete").ToListAsync());
    }

    [Theory]
    [InlineData("", "detailed reason")]
    [InlineData("wrong", "short")]
    public async Task SoftDelete_RequiresExactConfirmationAndDetailedReason(string confirmation, string reason)
    {
        await using var db = CreateDbContext();
        var projection = LegacyProjection("Confirm this", RunProjectionStatuses.Draft, 1, "WP");
        db.RunProjections.Add(projection);
        await db.SaveChangesAsync();
        var service = CreateProjectionService(db, new PlanningBinsRunService([]));
        var form = DeleteForm(projection, Guid.NewGuid());
        form.ConfirmationValue = confirmation;
        form.Reason = reason;

        Assert.NotNull(await service.DeleteAsync(form, Owner(), CancellationToken.None));
        Assert.False((await db.RunProjections.SingleAsync()).IsDeleted);
    }

    [Fact]
    public async Task SoftDelete_BlocksProjectionLinkedToActualRunAndAuditsAttempt()
    {
        await using var db = CreateDbContext();
        var projection = LegacyProjection("Linked", RunProjectionStatuses.Ready, 1, "WP");
        var source = TestProjectionSource();
        source.ActualBinsRunEntryId = 99;
        projection.Sources.Add(source);
        db.RunProjections.Add(projection);
        await db.SaveChangesAsync();
        var service = CreateProjectionService(db, new PlanningBinsRunService([]));

        var error = await service.DeleteAsync(DeleteForm(projection, Guid.NewGuid()), Owner(), CancellationToken.None);

        Assert.Contains("actual Bins Run", error);
        Assert.False((await db.RunProjections.SingleAsync()).IsDeleted);
        Assert.True(await db.AuditLogs.AnyAsync(x => x.Action == "DeleteBlockedActualRun"));
    }

    [Fact]
    public async Task DuplicateAcrossFacilities_OmitsInventoryAndReferencesOriginalProjection()
    {
        await using var db = CreateDbContext();
        var source = LegacyProjection("WP inventory", RunProjectionStatuses.Draft, 1, "WP");
        source.Sources.Add(TestProjectionSource());
        db.RunProjections.Add(source);
        await db.SaveChangesAsync();
        var service = CreateProjectionService(db, new PlanningBinsRunService([]));

        var result = await service.DuplicateAsync(new RunProjectionDuplicateForm
        {
            Id = source.Id,
            PlannedRunDate = source.PlannedRunDate.AddDays(1),
            FacilityWarehouseId = 4
        }, Owner(), CancellationToken.None);

        Assert.Null(result.Error);
        var clone = await db.RunProjections.Include(x => x.Sources).SingleAsync(x => x.Id == result.Id);
        Assert.Equal(4, clone.FacilityWarehouseId);
        Assert.Equal(source.Id, clone.SourceProjectionId);
        Assert.Empty(clone.Sources);
        Assert.Equal(0, clone.TotalPlannedBins);
        Assert.Contains("OmittedInventorySourceIds", (await db.AuditLogs.SingleAsync(x => x.Action == "Duplicate")).AfterValuesJson);
    }

    [Fact]
    public async Task DuplicateWithinFacility_RetainsSourcesButNeverActualRunLink()
    {
        await using var db = CreateDbContext();
        var source = LegacyProjection("Same facility", RunProjectionStatuses.Draft, 1, "WP");
        var sourceLine = TestProjectionSource();
        sourceLine.ActualBinsRunEntryId = 42;
        source.Sources.Add(sourceLine);
        db.RunProjections.Add(source);
        await db.SaveChangesAsync();
        var service = CreateProjectionService(db, new PlanningBinsRunService([]));

        var result = await service.DuplicateAsync(new RunProjectionDuplicateForm
        {
            Id = source.Id,
            PlannedRunDate = source.PlannedRunDate.AddDays(1),
            FacilityWarehouseId = 1
        }, Owner(), CancellationToken.None);

        var clone = await db.RunProjections.Include(x => x.Sources).SingleAsync(x => x.Id == result.Id);
        Assert.Single(clone.Sources);
        Assert.Null(clone.Sources.Single().ActualBinsRunEntryId);
    }

    [Fact]
    public void PlannerUi_ExposesFacilityDeletionAndDurableNavigationControls()
    {
        var view = ReadRepositoryFile("src", "CropQc.Web", "Views", "BinsRun", "Index.cshtml");
        var room = ReadRepositoryFile("src", "CropQc.Web", "Views", "Home", "Room.cshtml");
        var card = ReadRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_RoomLotCard.cshtml");
        var delete = ReadRepositoryFile("src", "CropQc.Web", "Views", "BinsRun", "DeleteProjection.cshtml");

        Assert.Contains("Planning Facility", view);
        Assert.Contains("Which facility is this run for?", view);
        Assert.Contains("ProjectionVisibility", view);
        Assert.Contains("Facility Totals", view);
        Assert.Contains("Delete Projection", view);
        Assert.Contains("data-facility-warehouse-id", view);
        Assert.Contains("Facility=@Model.Summary.Warehouse", room);
        Assert.Contains("Facility=@Model.Warehouse", card);
        Assert.Contains("OperationToken", delete);
        Assert.Contains("ConfirmDeletion", delete);
    }

    [Fact]
    public void FacilitySoftDeleteMigration_IsAdditiveProviderCompatibleAndConservative()
    {
        var migration = Directory.GetFiles(
            RepositoryDirectory("src", "CropQc.Data", "Migrations"),
            "*AddRunProjectionFacilityAndSoftDelete.cs").Single();
        var text = File.ReadAllText(migration);
        var up = text[..text.IndexOf("protected override void Down", StringComparison.Ordinal)];

        Assert.Contains("MigrationProviderTypes.StoreType", up);
        Assert.Contains("timestamp with time zone", up);
        Assert.Contains("COUNT(DISTINCT", up, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"Code\" IN ('WP', 'EBS')", up);
        Assert.DoesNotContain("DropColumn(", up);
        Assert.DoesNotContain("DropTable(", up);
        Assert.DoesNotContain("DeleteData(", up);
        Assert.DoesNotContain("UpdateData(", up);
    }

    private static RunProjection LegacyProjection(
        string name,
        string status,
        int? facilityId = null,
        string? facilityCode = null) =>
        new()
        {
            PlannedRunDate = new(2026, 7, 24),
            Name = name,
            Status = status,
            ProjectionMode = RunProjectionModes.Inventory,
            FacilityWarehouseId = facilityId,
            FacilityCodeSnapshot = facilityCode,
            CropYear = 2026,
            ApplePoundsPerBin = 880,
            PearPoundsPerBin = 920,
            StandardBoxWeightPounds = 40,
            CreatedAt = TestNow,
            UpdatedAt = TestNow
        };

    private static RunProjectionSource TestProjectionSource() =>
        new()
        {
            SourceType = RunProjectionSourceTypes.Inventory,
            InventoryKey = "WP:1",
            WarehouseId = 1,
            FruitProfileId = 1,
            PlannedBins = 5,
            SelectedQcSourceType = RunProjectionQcSourceTypes.None,
            Commodity = "Apple",
            SourceLabelSnapshot = "WP inventory",
            FacilitySnapshot = "WP",
            VarietySnapshot = "Gala",
            CalculationVersion = RunProjectionCalculationService.CurrentCalculationVersion,
            CreatedAt = TestNow,
            UpdatedAt = TestNow
        };

    private static DeleteRunProjectionForm DeleteForm(RunProjection projection, Guid operationId) =>
        new()
        {
            Id = projection.Id,
            ConcurrencyVersion = projection.ConcurrencyVersion,
            Reason = "Duplicate planning record",
            ConfirmationValue = projection.Id.ToString(),
            ConfirmDeletion = true,
            OperationToken = operationId.ToString("D")
        };

    private static RunProjectionLineCalculation Calculate(string commodity, int bins, params int[] sizes) =>
        RunProjectionCalculationService.Calculate(
            commodity,
            bins,
            RunProjectionCalculationService.DefaultApplePoundsPerBin,
            RunProjectionCalculationService.DefaultPearPoundsPerBin,
            RunProjectionCalculationService.DefaultStandardBoxWeightPounds,
            sizes.Select(x => new RunProjectionSizeObservation(x)));

    private static RunProjectionLineCalculation CalculateWithPackout(
        decimal? packout,
        IReadOnlyList<int> sizes,
        IReadOnlyList<string> grades) =>
        RunProjectionCalculationService.Calculate(
            "Apple",
            1,
            880m,
            920m,
            40m,
            packout,
            sizes.Select(x => new RunProjectionSizeObservation(x)),
            grades.Select(x => new RunProjectionGradeObservation(x)));

    private static readonly DateTimeOffset TestNow = DateTimeOffset.Parse("2026-07-23T18:00:00Z");

    private static CropQcDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new CropQcDbContext(options);
        db.Warehouses.AddRange(
            new Warehouse { Id = 1, Code = "WP", Name = "Windy Point" },
            new Warehouse { Id = 4, Code = "EBS", Name = "Earl Brown" });
        db.SaveChanges();
        return db;
    }

    private static RunProjectionService CreateProjectionService(
        CropQcDbContext db,
        IBinsRunService bins,
        DateTimeOffset? now = null) =>
        new(
            db,
            bins,
            new UserAccessService(db, new ConfigurationBuilder().Build()),
            new CropYearService(db, new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["CropYear:ActiveYear"] = "2026" }).Build()),
            new PacificBusinessTimeService(new FixedClock(now ?? TestNow)));

    private static async Task<long> CreateProjectionAsync(
        RunProjectionService service,
        DateOnly plannedDate,
        string name)
    {
        var result = await service.CreateAsync(
            new RunProjectionCreateForm
            {
                PlannedRunDate = plannedDate,
                Name = name,
                ProjectionMode = RunProjectionModes.Preharvest,
                FacilityWarehouseId = 1
            },
            Owner(),
            CancellationToken.None);
        Assert.Null(result.Error);
        return Assert.IsType<long>(result.Id);
    }

    private static ClaimsPrincipal Owner() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Email, ApplicationAreas.OwnerEmail)], "Test"));

    private static RunProjectionInventorySource Inventory(string key, long receiptId, int bins, string commodity) =>
        new(
            key,
            receiptId,
            $"REC-{receiptId}",
            null,
            1,
            1,
            "WP",
            "Room 1",
            1,
            commodity,
            null,
            "WP ORCHARD",
            "1080",
            "1080",
            "Bartlett",
            bins,
            TestNow);

    private static async Task<PreharvestSeed> SeedPreharvestFieldSamplesAsync(CropQcDbContext db)
    {
        var sampleType = new SampleType { Name = "Field Sample" };
        var profile = new FruitProfile
        {
            Name = "Bartlett",
            VarietyCode = "BART",
            FruitType = "Pear",
            ProductionType = "Conventional"
        };
        var orchard = new CanonicalOrchard
        {
            OrchardName = "WP ORCHARD",
            NormalizedOrchardKey = "WP ORCHARD",
            CreatedAt = TestNow,
            UpdatedAt = TestNow
        };
        var north = new CanonicalOrchardBlock
        {
            CanonicalOrchard = orchard,
            OrchardName = orchard.OrchardName,
            CanonicalBlockName = "North",
            NormalizedOrchardKey = orchard.NormalizedOrchardKey,
            NormalizedBlockKey = "NORTH",
            CreatedAt = TestNow,
            UpdatedAt = TestNow
        };
        var south = new CanonicalOrchardBlock
        {
            CanonicalOrchard = orchard,
            OrchardName = orchard.OrchardName,
            CanonicalBlockName = "South",
            NormalizedOrchardKey = orchard.NormalizedOrchardKey,
            NormalizedBlockKey = "SOUTH",
            CreatedAt = TestNow,
            UpdatedAt = TestNow
        };
        db.AddRange(sampleType, profile, orchard, north, south);
        await db.SaveChangesAsync();

        QcSample FieldSample(CanonicalOrchardBlock block, DateTimeOffset takenAt, int size) =>
            new()
            {
                SampleType = sampleType,
                Status = "Complete",
                StarchStatus = "Not Started",
                PhotoStatus = "Not Started",
                EmailStatus = "Not Sent",
                FieldSampleFruitProfile = profile,
                CanonicalOrchardBlock = block,
                FieldSampleGrowerName = orchard.OrchardName,
                FieldSampleGrowerNumber = "1080",
                FieldSampleOriginalBlockName = block.CanonicalBlockName,
                FieldSampleBlockResolution = "Confirmed",
                SampleTakenAt = takenAt,
                CreatedAt = takenAt,
                FruitReadings =
                {
                    new QcFruitReading
                    {
                        RowNumber = 1,
                        SizeCategory = size,
                        SizeStatus = "Calculated",
                        CreatedAt = takenAt
                    },
                    new QcFruitReading
                    {
                        RowNumber = 2,
                        SizeCategory = size + 10,
                        SizeStatus = "Calculated",
                        CreatedAt = takenAt
                    }
                }
            };

        var olderNorth = FieldSample(north, TestNow.AddDays(-7), 80);
        var newestNorth = FieldSample(north, TestNow.AddDays(-2), 90);
        var southSample = FieldSample(south, TestNow.AddDays(-3), 100);
        var deletedNorth = FieldSample(north, TestNow.AddDays(-1), 110);
        deletedNorth.IsDeleted = true;
        deletedNorth.DeletedAt = TestNow;
        var suggestedNorth = FieldSample(north, TestNow, 120);
        suggestedNorth.FieldSampleBlockResolution = "Suggested";
        db.QcSamples.AddRange(olderNorth, newestNorth, southSample, deletedNorth, suggestedNorth);
        await db.SaveChangesAsync();
        return new PreharvestSeed(
            profile.Id,
            north.Id,
            south.Id,
            olderNorth.Id,
            newestNorth.Id,
            southSample.Id,
            deletedNorth.Id,
            suggestedNorth.Id);
    }

    private sealed record PreharvestSeed(
        int ProfileId,
        int NorthBlockId,
        int SouthBlockId,
        long OlderNorthSampleId,
        long NewestNorthSampleId,
        long SouthSampleId,
        long DeletedNorthSampleId,
        long SuggestedNorthSampleId);

    private sealed class FixedClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow { get; } = now;
    }

    private sealed class PlanningBinsRunService(IReadOnlyList<RunProjectionInventorySource> sources) : IBinsRunService
    {
        public Task<IReadOnlyList<RunProjectionInventorySource>> SearchPlanningInventoryAsync(
            string? query,
            int? warehouseId,
            int? roomId,
            int take,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RunProjectionInventorySource>>(
                sources.Where(x => warehouseId is null || x.WarehouseId == warehouseId).Take(take).ToList());

        public Task<RunProjectionInventorySource?> GetPlanningInventoryAsync(string inventoryKey, CancellationToken cancellationToken) =>
            Task.FromResult(sources.SingleOrDefault(x => x.InventoryKey == inventoryKey));

        public Task<BinsRunPageViewModel> GetPageAsync(BinsRunFilterForm filter, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ActualRunDetailViewModel?> GetActualRunDetailAsync(long id, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<BinsRunProjectionViewModel> GetProjectionAsync(BinsRunProjectionRequest request, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> CreateAsync(BinsRunForm form, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> UpdateAsync(long id, BinsRunForm form, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> ReverseAsync(ReverseBinsRunForm form, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> CreateActualRunAsync(ActualRunForm form, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> UpdateActualRunAsync(long id, ActualRunForm form, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> CancelActualRunAsync(CancelActualRunForm form, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> CorrectActualRunSalesDeskAsync(CorrectActualRunSalesDeskForm form, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> ApproveActualRunOverrideAsync(ApproveActualRunOverrideForm form, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static string ReadRepositoryFile(params string[] parts) => File.ReadAllText(RepositoryFile(parts));

    private static string RepositoryFile(params string[] parts)
    {
        var path = Path.Combine(RepositoryDirectory(parts.Take(parts.Length - 1).ToArray()), parts[^1]);
        if (!File.Exists(path)) throw new FileNotFoundException(path);
        return path;
    }

    private static string RepositoryDirectory(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (Directory.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException(Path.Combine(parts));
    }
}
