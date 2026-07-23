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
            new RunProjectionCreateForm { PlannedRunDate = new(2026, 7, 24), Name = "Day shift" },
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
            new RunProjectionCreateForm { PlannedRunDate = new(2026, 7, 24), Name = "Availability check" },
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
    }

    [Fact]
    public void PlannerUi_ProvidesHistoryDateAccessAndSpreadsheetSafeExport()
    {
        var view = ReadRepositoryFile("src", "CropQc.Web", "Views", "BinsRun", "Index.cshtml");
        var controller = ReadRepositoryFile("src", "CropQc.Web", "Controllers", "BinsRunController.cs");

        Assert.Contains("Open date", view);
        Assert.Contains("Converted to Actual Run", view);
        Assert.Contains("safe[0] is '=' or '+' or '-' or '@'", controller);
    }

    private static RunProjectionLineCalculation Calculate(string commodity, int bins, params int[] sizes) =>
        RunProjectionCalculationService.Calculate(
            commodity,
            bins,
            RunProjectionCalculationService.DefaultApplePoundsPerBin,
            RunProjectionCalculationService.DefaultPearPoundsPerBin,
            RunProjectionCalculationService.DefaultStandardBoxWeightPounds,
            sizes.Select(x => new RunProjectionSizeObservation(x)));

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
