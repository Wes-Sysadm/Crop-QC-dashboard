using System.Security.Claims;
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
    public void LargestRemainder_RoundedSizesAddBackToRoundedTotal()
    {
        var result = RunProjectionCalculationService.Calculate(
            "Apple",
            1,
            880m,
            920m,
            40m,
            [new(80), new(90), new(100)]);

        Assert.Equal(22, result.RoundedProjectedBoxes);
        Assert.Equal(22, result.SizeAllocations.Sum(x => x.RoundedProjectedBoxes));
        Assert.Equal(8, result.SizeAllocations.Single(x => x.SizeCategory == 80).RoundedProjectedBoxes);
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
    [InlineData(90, 20, 2)]
    [InlineData(85, 19, 3)]
    [InlineData(0, 0, 22)]
    public void ExpectedPackout_DerivesComplementaryCullAndReconciles(
        decimal packout,
        int expectedPacked,
        int expectedCull)
    {
        var result = CalculateWithPackout(packout, [80, 90, 100], ["US #1", "Fancy", "C Grade"]);

        Assert.Equal(packout, result.ExpectedPackoutPercent);
        Assert.Equal(100m - packout, result.ExpectedCullPercent);
        Assert.Equal(expectedPacked, result.RoundedPackedProjectedBoxes);
        Assert.Equal(expectedCull, result.RoundedCullProjectedBoxes);
        Assert.Equal(result.RoundedProjectedBoxes, result.RoundedPackedProjectedBoxes + result.RoundedCullProjectedBoxes);
        Assert.Equal(result.RoundedPackedProjectedBoxes, result.SizeAllocations.Sum(x => x.RoundedPackedProjectedBoxes));
        Assert.Equal(result.RoundedCullProjectedBoxes, result.SizeAllocations.Sum(x => x.RoundedCullProjectedBoxes));
        Assert.Equal(result.RoundedPackedProjectedBoxes, result.GradeAllocations.Sum(x => x.RoundedPackedBoxes));
        Assert.Equal(result.RoundedCullProjectedBoxes, result.GradeAllocations.Sum(x => x.RoundedCullBoxes));
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
        Assert.Contains("receipt QC first", view);
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
                ProjectionMode = RunProjectionModes.Inventory
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
                ProjectionMode = RunProjectionModes.Inventory
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
                ProjectionMode = RunProjectionModes.Inventory
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
                ProjectionMode = RunProjectionModes.Preharvest
            },
            Owner(),
            CancellationToken.None);

        Assert.Null(created.Error);
        var candidates = await service.SearchSourcesAsync(
            "WP ORCHARD",
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
                ProjectionMode = RunProjectionModes.Preharvest
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
                ProjectionMode = RunProjectionModes.Preharvest
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
                ProjectionMode = RunProjectionModes.Preharvest
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
        return new CropQcDbContext(options);
    }

    private static RunProjectionService CreateProjectionService(CropQcDbContext db, IBinsRunService bins) =>
        new(
            db,
            bins,
            new UserAccessService(db, new ConfigurationBuilder().Build()),
            new CropYearService(db, new ConfigurationBuilder().AddInMemoryCollection(
                new Dictionary<string, string?> { ["CropYear:ActiveYear"] = "2026" }).Build()),
            new PacificBusinessTimeService(new FixedClock(TestNow)));

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
            int? roomId,
            int take,
            CancellationToken cancellationToken) =>
            Task.FromResult(sources);

        public Task<RunProjectionInventorySource?> GetPlanningInventoryAsync(string inventoryKey, CancellationToken cancellationToken) =>
            Task.FromResult(sources.SingleOrDefault(x => x.InventoryKey == inventoryKey));

        public Task<BinsRunPageViewModel> GetPageAsync(BinsRunFilterForm filter, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<BinsRunProjectionViewModel> GetProjectionAsync(BinsRunProjectionRequest request, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> CreateAsync(BinsRunForm form, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> UpdateAsync(long id, BinsRunForm form, ClaimsPrincipal user, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string?> ReverseAsync(ReverseBinsRunForm form, ClaimsPrincipal user, CancellationToken cancellationToken) =>
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
