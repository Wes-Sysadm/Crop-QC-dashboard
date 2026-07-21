using System.Security.Claims;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace CropQc.Api.Tests;

public sealed class FieldSampleWorkflowTests
{
    [Fact]
    public void FieldSamples_ArePermissionedAndHaveDedicatedNavigation()
    {
        var layout = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_Layout.cshtml"));
        var access = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "UserAccessService.cs"));
        var program = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Program.cs"));
        var photoPolicy = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "QcPhotoRequirementPolicy.cs"));

        Assert.Contains("ApplicationAreas.FieldSamples", access);
        Assert.Contains("AccessPolicyNames.FieldSamplesView", program);
        Assert.Contains("AccessPolicyNames.FieldSamplesEdit", program);
        Assert.Contains("canAccessFieldSamples", layout);
        Assert.Contains("<a href=\"/FieldSamples\">Field Samples</a>", layout);
        Assert.Contains("Contains(\"field\"", photoPolicy);

        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "FieldSamplesController.cs"));
        Assert.Contains("[HttpGet(\"\")]", controller);
        Assert.Contains("[HttpGet(\"Create\")]", controller);
        Assert.Contains("[HttpPost(\"Create\")]", controller);
        Assert.Contains("[HttpGet(\"{id:long}\")]", controller);
        Assert.Contains("[HttpGet(\"{id:long}/Edit\")]", controller);
        Assert.Contains("[HttpPost(\"{id:long}/rows\")]", controller);
        Assert.Contains("[HttpGet(\"Suggestions\")]", controller);
    }

    [Theory]
    [InlineData("Block 12", "block 12")]
    [InlineData("Block-12", "BLOCK 12")]
    [InlineData(" Block   12 ", "Block 12")]
    public void OrchardBlockMatcher_NormalizesHarmlessBlockDifferences(string left, string right)
    {
        Assert.Equal(OrchardBlockMatcher.Normalize(left), OrchardBlockMatcher.Normalize(right));
    }

    [Fact]
    public void OrchardBlockMatcher_DoesNotAutoMatchDifferentBlockNumbers()
    {
        Assert.False(OrchardBlockMatcher.IsAutomaticMatch("WP Orchard", "Block 12", "WP Orchard", "Block 21", uniqueCandidate: true));
    }

    [Fact]
    public async Task CreateAsync_DefaultsToTenFruitAndDoesNotCreateInventory()
    {
        await using var db = CreateDbContext();
        await SeedFieldSampleMasterDataAsync(db);
        var service = CreateService(db);

        var create = await service.CreateAsync(new FieldSampleCreateForm
        {
            OrchardName = "WP Orchard",
            BlockName = "North Block 12",
            FruitProfileId = 1,
            ConfirmCreateNewBlock = true,
            SampleTakenAt = new DateTimeOffset(2026, 7, 17, 8, 0, 0, TimeSpan.Zero)
        }, Owner(), CancellationToken.None);
        Assert.Null(create.Error);
        var sampleId = create.SampleId!.Value;

        var sample = await db.QcSamples.Include(x => x.CanonicalOrchardBlock).SingleAsync(x => x.Id == sampleId);
        Assert.Null(sample.ReceiptId);
        Assert.Equal(10, sample.ActualSampleSize);
        Assert.Equal("Field Sample", (await db.SampleTypes.SingleAsync(x => x.Id == sample.SampleTypeId)).Name);
        Assert.Equal("Not Required", sample.PhotoStatus);
        Assert.Equal("Not Applicable", sample.EmailStatus);
        Assert.Equal("North Block 12", sample.FieldSampleOriginalBlockName);
        Assert.Equal("North Block 12", sample.CanonicalOrchardBlock!.CanonicalBlockName);
        Assert.Equal(10, await db.QcFruitReadings.CountAsync(x => x.QcSampleId == sampleId));
        Assert.All(await db.QcFruitReadings.Where(x => x.QcSampleId == sampleId).ToListAsync(), row =>
        {
            Assert.Null(row.WeightGrams);
            Assert.Null(row.Pressure1Lbs);
            Assert.Null(row.Pressure2Lbs);
            Assert.Null(row.StarchScaleValueId);
            Assert.False(row.IsCompleted);
        });
        Assert.Empty(await db.RoomInventoryAdjustments.ToListAsync());
        Assert.Empty(await db.BinsRunEntries.ToListAsync());
    }

    [Fact]
    public async Task CreateAsync_DoesNotAutomaticallyApplyFuzzyBlockSuggestion()
    {
        await using var db = CreateDbContext();
        await SeedFieldSampleMasterDataAsync(db);
        var service = CreateService(db);
        await CreateSampleAsync(service, "North Block 12", new DateTimeOffset(2026, 7, 10, 8, 0, 0, TimeSpan.Zero));

        var result = await service.CreateAsync(new FieldSampleCreateForm
        {
            OrchardName = "WP Orchard",
            BlockName = "Nort Block 12",
            FruitProfileId = 1,
            SampleTakenAt = new DateTimeOffset(2026, 7, 17, 8, 0, 0, TimeSpan.Zero)
        }, Owner(), CancellationToken.None);

        Assert.Null(result.SampleId);
        Assert.Equal("Select an existing block or confirm that this is a new canonical block.", result.Error);
        Assert.Empty(await db.OrchardBlockAliases.Where(x => x.AliasName == "Nort Block 12").ToListAsync());
    }

    [Fact]
    public async Task SaveRowsAsync_IsPartialFriendlyAndDoesNotRequireWeightOrGrade()
    {
        await using var db = CreateDbContext();
        await SeedFieldSampleMasterDataAsync(db);
        var service = CreateService(db);
        var sampleId = await CreateSampleAsync(service, "North Block 12", new DateTimeOffset(2026, 7, 17, 8, 0, 0, TimeSpan.Zero));

        var error = await service.SaveRowsAsync(sampleId, new SaveFruitReadingsForm
        {
            SampleId = sampleId,
            TargetSampleSize = 10,
            Rows =
            [
                new FruitReadingEditRow { RowNumber = 1, Pressure1Lbs = 15.2m, Pressure2Lbs = 15.6m },
                new FruitReadingEditRow { RowNumber = 2, StarchScaleValueId = 1 },
                new FruitReadingEditRow { RowNumber = 3, WeightGrams = 266m }
            ]
        }, Owner(), CancellationToken.None);

        Assert.Null(error);
        var rows = await db.QcFruitReadings.Where(x => x.QcSampleId == sampleId).OrderBy(x => x.RowNumber).ToListAsync();
        Assert.Equal(10, rows.Count);
        Assert.Equal(15.2m, rows[0].Pressure1Lbs);
        Assert.Null(rows[0].WeightGrams);
        Assert.Null(rows[0].GradeId);
        Assert.Equal(1, rows[1].StarchScaleValueId);
        Assert.Equal(72, rows[2].SizeCategory);
        Assert.All(rows.Skip(3), row =>
        {
            Assert.Null(row.WeightGrams);
            Assert.Null(row.Pressure1Lbs);
            Assert.Null(row.Pressure2Lbs);
            Assert.Null(row.StarchScaleValueId);
        });
    }

    [Fact]
    public async Task DetailTrend_UsesSameCanonicalBlockAndPriorThirtyDaysOnly()
    {
        await using var db = CreateDbContext();
        await SeedFieldSampleMasterDataAsync(db);
        var service = CreateService(db);
        var currentDate = new DateTimeOffset(2026, 7, 17, 8, 0, 0, TimeSpan.Zero);
        var currentId = await CreateSampleAsync(service, "North Block 12", currentDate);
        var priorId = await CreateSampleAsync(service, "North-Block 12", currentDate.AddDays(-7));
        var olderId = await CreateSampleAsync(service, "North Block 12", currentDate.AddDays(-31));
        var otherBlockId = await CreateSampleAsync(service, "North Block 21", currentDate.AddDays(-4));

        await SavePressureAsync(service, priorId, 16m, 16m);
        await SavePressureAsync(service, currentId, 15m, 15m);
        await SavePressureAsync(service, olderId, 20m, 20m);
        await SavePressureAsync(service, otherBlockId, 10m, 10m);

        var detail = await service.GetDetailAsync(currentId, Owner(), CancellationToken.None);

        Assert.Equal([priorId, currentId], detail.Trend.Select(x => x.SampleId).ToArray());
        Assert.Equal(-1m, detail.CurrentSummary.AveragePressureChangeFromPriorLbs);
        Assert.Equal(priorId, detail.Trend.First().SampleId);
        Assert.DoesNotContain(detail.Trend, x => x.SampleId == olderId);
        Assert.DoesNotContain(detail.Trend, x => x.SampleId == otherBlockId);
    }

    [Fact]
    public async Task DetailSummary_UsesRowPressureAveragesAndValidEnteredValuesOnly()
    {
        await using var db = CreateDbContext();
        await SeedFieldSampleMasterDataAsync(db);
        var service = CreateService(db);
        var sampleId = await CreateSampleAsync(service, "North Block 12", new DateTimeOffset(2026, 7, 17, 8, 0, 0, TimeSpan.Zero));

        var error = await service.SaveRowsAsync(sampleId, new SaveFruitReadingsForm
        {
            SampleId = sampleId,
            TargetSampleSize = 10,
            Rows =
            [
                new FruitReadingEditRow { RowNumber = 1, Pressure1Lbs = 10m, Pressure2Lbs = 12m, WeightGrams = 266m, StarchScaleValueId = 1 },
                new FruitReadingEditRow { RowNumber = 2, Pressure1Lbs = 14m, WeightGrams = 238m, StarchScaleValueId = 2 },
                new FruitReadingEditRow { RowNumber = 3, WeightGrams = 100m }
            ]
        }, Owner(), CancellationToken.None);
        Assert.Null(error);

        var detail = await service.GetDetailAsync(sampleId, Owner(), CancellationToken.None);

        Assert.Equal(3, detail.CurrentSummary.EnteredFruitCount);
        Assert.Equal(201.33m, detail.CurrentSummary.AverageWeightGrams);
        Assert.Equal(266m, detail.CurrentSummary.PeakWeightGrams);
        Assert.Equal(100m, detail.CurrentSummary.MinimumWeightGrams);
        Assert.Equal(2, detail.CurrentSummary.StarchRepresentedFruitCount);
        Assert.Equal(3.5m, detail.CurrentSummary.AverageStarch);
        Assert.Equal(2, detail.CurrentSummary.PressureReadingCount);
        Assert.Equal(12.5m, detail.CurrentSummary.AveragePressureLbs);
        Assert.Equal(14m, detail.CurrentSummary.PeakPressureLbs);
        Assert.Equal(11m, detail.CurrentSummary.MinimumPressureLbs);
        Assert.Equal(2.12m, detail.CurrentSummary.PressureStandardDeviationLbs);
    }

    [Fact]
    public async Task DetailSizeDistribution_UsesEnteredRowsAsDenominatorAndBusinessOrder()
    {
        await using var db = CreateDbContext();
        await SeedFieldSampleMasterDataAsync(db);
        var service = CreateService(db);
        var sampleId = await CreateSampleAsync(service, "North Block 12", new DateTimeOffset(2026, 7, 17, 8, 0, 0, TimeSpan.Zero));

        var error = await service.SaveRowsAsync(sampleId, new SaveFruitReadingsForm
        {
            SampleId = sampleId,
            TargetSampleSize = 10,
            Rows =
            [
                new FruitReadingEditRow { RowNumber = 1, WeightGrams = 266m },
                new FruitReadingEditRow { RowNumber = 2, WeightGrams = 238m },
                new FruitReadingEditRow { RowNumber = 3, Pressure1Lbs = 12m }
            ]
        }, Owner(), CancellationToken.None);
        Assert.Null(error);

        var detail = await service.GetDetailAsync(sampleId, Owner(), CancellationToken.None);

        Assert.Equal([72, 80], detail.SizeDistribution.Select(x => x.Size).ToArray());
        Assert.Equal([50m, 50m], detail.SizeDistribution.Select(x => x.Percentage).ToArray());
    }

    [Fact]
    public async Task SaveRowsAsync_AllowsExpandedRowsForDeviceCapturedMeasurements()
    {
        await using var db = CreateDbContext();
        await SeedFieldSampleMasterDataAsync(db);
        var service = CreateService(db);
        var sampleId = await CreateSampleAsync(service, "North Block 12", new DateTimeOffset(2026, 7, 17, 8, 0, 0, TimeSpan.Zero));

        var error = await service.SaveRowsAsync(sampleId, new SaveFruitReadingsForm
        {
            SampleId = sampleId,
            TargetSampleSize = 50,
            Rows =
            [
                new FruitReadingEditRow { RowNumber = 11, WeightGrams = 266m },
                new FruitReadingEditRow { RowNumber = 20, Pressure1Lbs = 12.5m, Pressure2Lbs = 13.5m },
                new FruitReadingEditRow { RowNumber = 50, WeightGrams = 312m }
            ]
        }, Owner(), CancellationToken.None);

        Assert.Null(error);
        var sample = await db.QcSamples.SingleAsync(x => x.Id == sampleId);
        Assert.Equal(50, sample.ActualSampleSize);
        var detail = await service.GetDetailAsync(sampleId, Owner(), CancellationToken.None);
        Assert.Equal(50, detail.TargetSampleSize);
        Assert.Equal(50, detail.FruitRows.Count);
        Assert.Equal(266m, detail.FruitRows.Single(x => x.RowNumber == 11).WeightGrams);
        Assert.Equal(13m, detail.FruitRows.Single(x => x.RowNumber == 20).PressureAverageLbs);
        Assert.Equal(312m, detail.FruitRows.Single(x => x.RowNumber == 50).WeightGrams);
    }

    [Fact]
    public async Task UpdateMetadataAsync_ReassignsBlockWithoutReceiptFields()
    {
        await using var db = CreateDbContext();
        await SeedFieldSampleMasterDataAsync(db);
        var service = CreateService(db);
        var sampleId = await CreateSampleAsync(service, "North Block 12", new DateTimeOffset(2026, 7, 17, 8, 0, 0, TimeSpan.Zero));

        var error = await service.UpdateMetadataAsync(sampleId, new FieldSampleMetadataForm
        {
            SampleId = sampleId,
            OrchardName = "WP Orchard",
            BlockName = "River Bottom",
            FruitProfileId = 1,
            ConfirmCreateNewBlock = true,
            SampleTakenAt = new DateTimeOffset(2026, 7, 18, 9, 0, 0, TimeSpan.Zero),
            Notes = "Checked after cool morning"
        }, Owner(), CancellationToken.None);

        Assert.Null(error);
        var sample = await db.QcSamples.Include(x => x.CanonicalOrchardBlock).SingleAsync(x => x.Id == sampleId);
        Assert.Null(sample.ReceiptId);
        Assert.Equal("River Bottom", sample.FieldSampleOriginalBlockName);
        Assert.Equal("River Bottom", sample.CanonicalOrchardBlock!.CanonicalBlockName);
        Assert.Equal("Checked after cool morning", sample.Notes);
    }

    [Fact]
    public void DashboardInitialLoad_ExcludesReceiptlessFieldSamplesFromReceiptBackedProjection()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var start = service.IndexOf("private async Task<IReadOnlyList<SampleListItemViewModel>> BuildTodayDashboardSamplesAsync", StringComparison.Ordinal);
        var end = service.IndexOf("private ReadinessViewModel BuildCompactReadiness", StringComparison.Ordinal);
        var method = service[start..end];

        Assert.Contains("&& x.ReceiptId != null", method);
        Assert.Contains("x.ReceiptId!.Value", method);
    }

    [Fact]
    public async Task SearchByAlias_ReturnsSamplesUnderCanonicalBlock()
    {
        await using var db = CreateDbContext();
        await SeedFieldSampleMasterDataAsync(db);
        var service = CreateService(db);
        var sampleId = await CreateSampleAsync(service, "North Block 12", new DateTimeOffset(2026, 7, 17, 8, 0, 0, TimeSpan.Zero));
        var block = await db.CanonicalOrchardBlocks.SingleAsync();
        db.OrchardBlockAliases.Add(new OrchardBlockAlias
        {
            CanonicalOrchardBlockId = block.Id,
            AliasName = "NB12",
            NormalizedAliasKey = OrchardBlockMatcher.Normalize("NB12"),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var page = await service.GetIndexAsync(new FieldSampleSearchForm { Search = "NB12" }, Owner(), CancellationToken.None);

        Assert.Contains(page.Samples, x => x.Id == sampleId);
    }

    [Fact]
    public void FieldSampleViews_AreDedicatedAndDoNotExposeReceiptWorkflowFields()
    {
        var index = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "FieldSamples", "Index.cshtml"));
        var create = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "FieldSamples", "Create.cshtml"));
        var detail = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "FieldSamples", "Details.cshtml"));

        Assert.Contains("New Field Sample", index);
        Assert.Contains("Avg starch", index);
        Assert.Contains("Completion", index);
        Assert.Contains("Create 10-fruit Field Sample", create);
        Assert.Contains("Suggested block:", create);
        Assert.DoesNotContain("confidence", create, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Weight Trend", detail);
        Assert.Contains("Starch Trend", detail);
        Assert.Contains("Pressure Trend", detail);
        Assert.Contains("Size Trend", detail);
        Assert.Contains("Save Field Sample", detail);
        Assert.Contains("Open in QC Station", detail);
        Assert.Contains("Html.PartialAsync(\"_DeviceCapturePanel\"", detail);
        Assert.Contains("ShowScale: true", detail);
        Assert.Contains("class=\"fruit-row\"", detail);
        Assert.Contains("data-add-field-row", detail);
        Assert.DoesNotContain("ReceiptId", index + create);
        Assert.DoesNotContain("Truck", index + create + detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SaveRowsAsync_PreservesNewerQcStationPressureWhenOnlyOtherFieldsWereEdited()
    {
        await using var db = CreateDbContext();
        await SeedFieldSampleMasterDataAsync(db);
        var service = CreateService(db);
        var sampleId = await CreateSampleAsync(service, "Concurrency Block", DateTimeOffset.UtcNow);
        var captured = await db.QcFruitReadings.SingleAsync(x => x.QcSampleId == sampleId && x.RowNumber == 1);
        captured.Pressure1Lbs = 12.5m;
        captured.Pressure1Source = "FTA";
        captured.Pressure2Lbs = 13.5m;
        captured.Pressure2Source = "FTA";
        await db.SaveChangesAsync();

        var error = await service.SaveRowsAsync(sampleId, new SaveFruitReadingsForm
        {
            SampleId = sampleId,
            TargetSampleSize = 10,
            Rows = [new FruitReadingEditRow
            {
                RowNumber = 1,
                Pressure1Lbs = null,
                Pressure2Lbs = null,
                OriginalPressure1Lbs = null,
                OriginalPressure2Lbs = null,
                WeightGrams = 180m
            }]
        }, Owner(), CancellationToken.None);

        Assert.Null(error);
        var row = await db.QcFruitReadings.SingleAsync(x => x.QcSampleId == sampleId && x.RowNumber == 1);
        Assert.Equal(12.5m, row.Pressure1Lbs);
        Assert.Equal(13.5m, row.Pressure2Lbs);
        Assert.Equal("FTA", row.Pressure1Source);
        Assert.Equal("FTA", row.Pressure2Source);
        Assert.Equal(180m, row.WeightGrams);
    }

    private static async Task<long> CreateSampleAsync(IFieldSampleService service, string blockName, DateTimeOffset sampleDate)
    {
        var create = await service.CreateAsync(new FieldSampleCreateForm
        {
            OrchardName = "WP Orchard",
            BlockName = blockName,
            FruitProfileId = 1,
            ConfirmCreateNewBlock = true,
            SampleTakenAt = sampleDate
        }, Owner(), CancellationToken.None);
        Assert.Null(create.Error);
        return create.SampleId!.Value;
    }

    private static async Task SavePressureAsync(IFieldSampleService service, long sampleId, decimal p1, decimal p2)
    {
        var error = await service.SaveRowsAsync(sampleId, new SaveFruitReadingsForm
        {
            SampleId = sampleId,
            TargetSampleSize = 10,
            Rows = [new FruitReadingEditRow { RowNumber = 1, Pressure1Lbs = p1, Pressure2Lbs = p2 }]
        }, Owner(), CancellationToken.None);
        Assert.Null(error);
    }

    private static FieldSampleService CreateService(CropQcDbContext db)
    {
        var configuration = new ConfigurationBuilder().Build();
        return new(db, new UserAccessService(db, configuration), configuration);
    }

    private static CropQcDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new CropQcDbContext(options);
    }

    private static async Task SeedFieldSampleMasterDataAsync(CropQcDbContext db)
    {
        db.SampleTypes.Add(new SampleType { Id = 5, Name = "Field Sample" });
        db.FruitProfiles.Add(new FruitProfile
        {
            Id = 1,
            Name = "Gala Apple",
            VarietyCode = "GALA",
            FruitType = "Apple",
            ProductionType = "Conventional"
        });
        db.StarchScales.Add(new StarchScale { Id = 1, Name = "Apple Starch", FruitType = "Apple" });
        db.StarchScaleValues.AddRange(
            new StarchScaleValue { Id = 1, StarchScaleId = 1, Value = 3m, SortOrder = 1 },
            new StarchScaleValue { Id = 2, StarchScaleId = 1, Value = 4m, SortOrder = 2 });
        db.FruitSizeConversionThresholds.AddRange(
            new FruitSizeConversionThreshold { Id = 1, FruitType = "Apple", SizeCategory = 72, MinimumWeightGrams = 264m },
            new FruitSizeConversionThreshold { Id = 2, FruitType = "Apple", SizeCategory = 80, MinimumWeightGrams = 238m });
        await db.SaveChangesAsync();
    }

    private static ClaimsPrincipal Owner() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Email, ApplicationAreas.OwnerEmail)], "Test"));

    private static string FindRepositoryFile(params string[] parts)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            var candidate = Path.Combine(new[] { current }.Concat(parts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = Directory.GetParent(current)?.FullName;
        }

        throw new FileNotFoundException("Could not find repository file.", Path.Combine(parts));
    }
}
