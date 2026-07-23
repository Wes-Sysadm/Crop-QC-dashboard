using System.Security.Claims;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Storage;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

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
        Assert.Equal("Not Sent", sample.EmailStatus);
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
        Assert.All(rows.Take(3), row => Assert.False(row.IsCompleted));
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

        var current = await db.QcSamples.SingleAsync(x => x.Id == currentId);
        current.ActualSampleSize = 14;
        var prior = await db.QcSamples.SingleAsync(x => x.Id == priorId);
        prior.ActualSampleSize = 10;
        db.QcFruitReadings.AddRange(Enumerable.Range(2, 14).Select(rowNumber => new QcFruitReading
        {
            QcSampleId = priorId,
            RowNumber = rowNumber,
            SizeStatus = "NotCalculated",
            CreatedAt = currentDate.AddDays(-7)
        }));
        await db.SaveChangesAsync();

        var detail = await service.GetDetailAsync(currentId, Owner(), CancellationToken.None);

        Assert.Equal([priorId, currentId], detail.Trend.Select(x => x.SampleId).ToArray());
        Assert.Equal(-1m, detail.CurrentSummary.AveragePressureChangeFromPriorLbs);
        Assert.Equal(priorId, detail.Trend.First().SampleId);
        Assert.Equal(15, detail.Trend.First().TargetSampleSize);
        Assert.Equal(14, detail.Trend.Last().TargetSampleSize);
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
        Assert.Equal(3, detail.CurrentSummary.PressureReadingCount);
        Assert.Equal(12m, detail.CurrentSummary.AveragePressureLbs);
        Assert.Equal(14m, detail.CurrentSummary.PeakPressureLbs);
        Assert.Equal(10m, detail.CurrentSummary.MinimumPressureLbs);
        Assert.Equal(2m, detail.CurrentSummary.PressureStandardDeviationLbs);
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
    public async Task HistoricalExpandedSample_CanSaveWhenStoredSampleSizeIsStale()
    {
        await using var db = CreateDbContext();
        await SeedFieldSampleMasterDataAsync(db);
        var service = CreateService(db);
        var sampleId = await CreateSampleAsync(service, "Historical Block", new DateTimeOffset(2026, 7, 1, 8, 0, 0, TimeSpan.Zero));
        for (var rowNumber = 11; rowNumber <= 15; rowNumber++)
        {
            db.QcFruitReadings.Add(new QcFruitReading
            {
                QcSampleId = sampleId,
                RowNumber = rowNumber,
                WeightGrams = 238m,
                SizeCategory = 80,
                SizeStatus = SizeCalculationService.Sized,
                IsCompleted = true,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        await db.SaveChangesAsync();

        var before = await service.GetDetailAsync(sampleId, Owner(), CancellationToken.None);
        Assert.Equal(15, before.TargetSampleSize);

        var error = await service.SaveRowsAsync(sampleId, new SaveFruitReadingsForm
        {
            SampleId = sampleId,
            TargetSampleSize = 10,
            Rows = [new FruitReadingEditRow { RowNumber = 15, WeightGrams = 266m, OriginalWeightGrams = 238m, SizeCategory = 80, OriginalSizeCategory = 80 }]
        }, Owner(), CancellationToken.None);

        Assert.Null(error);
        var sample = await db.QcSamples.SingleAsync(x => x.Id == sampleId);
        var row = await db.QcFruitReadings.SingleAsync(x => x.QcSampleId == sampleId && x.RowNumber == 15);
        Assert.Equal(15, sample.ActualSampleSize);
        Assert.Equal(266m, row.WeightGrams);
        Assert.Equal(72, row.SizeCategory);
    }

    [Fact]
    public async Task SaveRowsAsync_RecalculatesSizeAndIgnoresClientOverride()
    {
        await using var db = CreateDbContext();
        await SeedFieldSampleMasterDataAsync(db);
        var service = CreateService(db);
        var sampleId = await CreateSampleAsync(service, "Size Block", DateTimeOffset.UtcNow);

        var firstError = await service.SaveRowsAsync(sampleId, new SaveFruitReadingsForm
        {
            SampleId = sampleId,
            TargetSampleSize = 10,
            Rows = [new FruitReadingEditRow { RowNumber = 1, WeightGrams = 266m }]
        }, Owner(), CancellationToken.None);
        Assert.Null(firstError);
        var calculated = await db.QcFruitReadings.SingleAsync(x => x.QcSampleId == sampleId && x.RowNumber == 1);
        Assert.Equal(72, calculated.SizeCategory);
        Assert.Equal(SizeCalculationService.Sized, calculated.SizeStatus);

        var overrideError = await service.SaveRowsAsync(sampleId, new SaveFruitReadingsForm
        {
            SampleId = sampleId,
            TargetSampleSize = 10,
            Rows = [new FruitReadingEditRow
            {
                RowNumber = 1,
                WeightGrams = 266m,
                OriginalWeightGrams = 266m,
                SizeCategory = 80,
                OriginalSizeCategory = 72
            }]
        }, Owner(), CancellationToken.None);
        Assert.Null(overrideError);
        var overridden = await db.QcFruitReadings.SingleAsync(x => x.QcSampleId == sampleId && x.RowNumber == 1);
        Assert.Equal(72, overridden.SizeCategory);
        Assert.Equal(SizeCalculationService.Sized, overridden.SizeStatus);

        Assert.Null(await service.SaveRowsAsync(sampleId, new SaveFruitReadingsForm
        {
            SampleId = sampleId,
            TargetSampleSize = 10,
            Rows = [new FruitReadingEditRow { RowNumber = 1, WeightGrams = null, OriginalWeightGrams = 266m, SizeCategory = 80 }]
        }, Owner(), CancellationToken.None));
        var cleared = await db.QcFruitReadings.SingleAsync(x => x.QcSampleId == sampleId && x.RowNumber == 1);
        Assert.Null(cleared.WeightGrams);
        Assert.Null(cleared.SizeCategory);
        Assert.Equal(SizeCalculationService.NotCalculated, cleared.SizeStatus);
    }

    [Fact]
    public async Task Autosave_SavesOnlyChangedFieldsAndPreservesNewerStationPressure()
    {
        await using var db = CreateDbContext();
        await SeedFieldSampleMasterDataAsync(db);
        var service = CreateService(db);
        var sampleId = await CreateSampleAsync(service, "Autosave Block", DateTimeOffset.UtcNow);
        var reading = await db.QcFruitReadings.SingleAsync(x => x.QcSampleId == sampleId && x.RowNumber == 1);
        reading.Pressure1Lbs = 13.25m;
        reading.Pressure1Source = "FTA";
        reading.FieldVersion++;
        await db.SaveChangesAsync();

        var result = await service.AutosaveAsync(sampleId, new FieldSampleAutosaveRequest
        {
            ChangeId = "weight-only",
            Source = "Browser",
            RowChanges =
            [
                new FieldSampleAutosaveRowChange
                {
                    RowNumber = 1,
                    Changes = [new FieldSampleAutosaveFieldChange { Field = "WeightGrams", OriginalValue = null, Value = "266" }]
                }
            ]
        }, Owner(), CancellationToken.None);

        Assert.True(result.Saved);
        reading = await db.QcFruitReadings.SingleAsync(x => x.QcSampleId == sampleId && x.RowNumber == 1);
        Assert.Equal(13.25m, reading.Pressure1Lbs);
        Assert.Equal("FTA", reading.Pressure1Source);
        Assert.Equal(266m, reading.WeightGrams);
        Assert.Equal(72, reading.SizeCategory);
    }

    [Fact]
    public async Task Autosave_SameFieldStationChangeProducesResolvableConflict()
    {
        await using var db = CreateDbContext();
        await SeedFieldSampleMasterDataAsync(db);
        var service = CreateService(db);
        var sampleId = await CreateSampleAsync(service, "Conflict Block", DateTimeOffset.UtcNow);
        var reading = await db.QcFruitReadings.SingleAsync(x => x.QcSampleId == sampleId && x.RowNumber == 1);
        reading.Pressure1Lbs = 14m;
        reading.Pressure1Source = "FTA";
        await db.SaveChangesAsync();

        var conflict = await service.AutosaveAsync(sampleId, new FieldSampleAutosaveRequest
        {
            ChangeId = "pressure-conflict",
            RowChanges =
            [
                new FieldSampleAutosaveRowChange
                {
                    RowNumber = 1,
                    Changes = [new FieldSampleAutosaveFieldChange { Field = "Pressure1Lbs", OriginalValue = "12", Value = "13" }]
                }
            ]
        }, Owner(), CancellationToken.None);

        Assert.False(conflict.Saved);
        var item = Assert.Single(conflict.Conflicts);
        Assert.Equal("14", item.ServerValue);
        Assert.Contains("QC Station", item.Message);
        Assert.Equal(14m, (await db.QcFruitReadings.SingleAsync(x => x.QcSampleId == sampleId && x.RowNumber == 1)).Pressure1Lbs);
        Assert.Contains(await db.AuditLogs.ToListAsync(), x => x.Action == "autosave-conflict");
    }

    [Fact]
    public async Task Autosave_RetryAfterCommittedResponseIsIdempotentAndReturnsNormalizedMetadata()
    {
        await using var db = CreateDbContext();
        await SeedFieldSampleMasterDataAsync(db);
        var service = CreateService(db);
        var sampleId = await CreateSampleAsync(service, "Retry Block", DateTimeOffset.UtcNow);
        var request = new FieldSampleAutosaveRequest
        {
            ChangeId = "lost-response-retry",
            MetadataChanges = [new FieldSampleAutosaveFieldChange { Field = "Notes", OriginalValue = null, Value = "  queued note  " }],
            RowChanges =
            [
                new FieldSampleAutosaveRowChange
                {
                    RowNumber = 1,
                    Changes = [new FieldSampleAutosaveFieldChange { Field = "WeightGrams", OriginalValue = null, Value = "266" }]
                }
            ]
        };

        var first = await service.AutosaveAsync(sampleId, request, Owner(), CancellationToken.None);
        var versionAfterFirst = (await db.QcSamples.SingleAsync(x => x.Id == sampleId)).FieldSampleAutosaveVersion;
        var second = await service.AutosaveAsync(sampleId, request, Owner(), CancellationToken.None);
        var versionAfterRetry = (await db.QcSamples.SingleAsync(x => x.Id == sampleId)).FieldSampleAutosaveVersion;

        Assert.True(first.Saved);
        Assert.True(second.Saved);
        Assert.Empty(second.Conflicts);
        Assert.Equal(versionAfterFirst, versionAfterRetry);
        Assert.Equal("queued note", second.MetadataValues["Notes"]);
        Assert.Equal(72, second.Rows.Single(x => x.RowNumber == 1).SizeCategory);
    }

    [Fact]
    public async Task Autosave_ExplicitWeightClearClearsCalculatedSize()
    {
        await using var db = CreateDbContext();
        await SeedFieldSampleMasterDataAsync(db);
        var service = CreateService(db);
        var sampleId = await CreateSampleAsync(service, "Clear Block", DateTimeOffset.UtcNow);
        Assert.True((await service.AutosaveAsync(sampleId, new FieldSampleAutosaveRequest
        {
            ChangeId = "set-weight",
            RowChanges = [new FieldSampleAutosaveRowChange { RowNumber = 1, Changes = [new FieldSampleAutosaveFieldChange { Field = "WeightGrams", Value = "266" }] }]
        }, Owner(), CancellationToken.None)).Saved);

        var cleared = await service.AutosaveAsync(sampleId, new FieldSampleAutosaveRequest
        {
            ChangeId = "clear-weight",
            RowChanges = [new FieldSampleAutosaveRowChange { RowNumber = 1, Changes = [new FieldSampleAutosaveFieldChange { Field = "WeightGrams", OriginalValue = "266", Value = null }] }]
        }, Owner(), CancellationToken.None);

        Assert.True(cleared.Saved);
        var row = cleared.Rows.Single(x => x.RowNumber == 1);
        Assert.Null(row.WeightGrams);
        Assert.Null(row.SizeCategory);
    }

    [Fact]
    public async Task Autosave_MissingSizeThresholdPreservesWeightWithoutCalculatedSize()
    {
        await using var db = CreateDbContext();
        await SeedFieldSampleMasterDataAsync(db);
        foreach (var threshold in await db.FruitSizeConversionThresholds.Where(x => x.FruitType == "Apple").ToListAsync()) threshold.IsActive = false;
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var sampleId = await CreateSampleAsync(service, "No Threshold Block", DateTimeOffset.UtcNow);

        var result = await service.AutosaveAsync(sampleId, new FieldSampleAutosaveRequest
        {
            ChangeId = "weight-without-threshold",
            RowChanges = [new FieldSampleAutosaveRowChange { RowNumber = 1, Changes = [new FieldSampleAutosaveFieldChange { Field = "WeightGrams", Value = "266" }] }]
        }, Owner(), CancellationToken.None);

        Assert.True(result.Saved);
        var row = await db.QcFruitReadings.SingleAsync(x => x.QcSampleId == sampleId && x.RowNumber == 1);
        Assert.Equal(266m, row.WeightGrams);
        Assert.Null(row.SizeCategory);
        Assert.Equal(SizeCalculationService.Undersized, row.SizeStatus);
    }

    [Fact]
    public async Task Autosave_RejectsNewlySelectedInactiveDefect()
    {
        await using var db = CreateDbContext();
        await SeedFieldSampleMasterDataAsync(db);
        (await db.DefectTypes.SingleAsync(x => x.Id == 2)).IsActive = false;
        await db.SaveChangesAsync();
        var service = CreateService(db);
        var sampleId = await CreateSampleAsync(service, "Inactive Defect Block", DateTimeOffset.UtcNow);

        var result = await service.AutosaveAsync(sampleId, new FieldSampleAutosaveRequest
        {
            ChangeId = "inactive-defect",
            RowChanges = [new FieldSampleAutosaveRowChange { RowNumber = 1, Changes = [new FieldSampleAutosaveFieldChange { Field = "DefectTypeIds", Value = "2" }] }]
        }, Owner(), CancellationToken.None);

        Assert.False(result.Saved);
        Assert.Contains(result.ValidationErrors, x => x.Field == "DefectTypeIds" && x.Message.Contains("inactive", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(await db.QcFruitDefects.Where(x => x.QcFruitReading.QcSampleId == sampleId).ToListAsync());
    }

    [Fact]
    public async Task FieldSampleDefects_DistinguishNotInspectedNoneAndMultipleDefects()
    {
        await using var db = CreateDbContext();
        await SeedFieldSampleMasterDataAsync(db);
        var service = CreateService(db);
        var sampleId = await CreateSampleAsync(service, "Defect Block", DateTimeOffset.UtcNow);

        var result = await service.AutosaveAsync(sampleId, new FieldSampleAutosaveRequest
        {
            ChangeId = "defects",
            RowChanges =
            [
                new FieldSampleAutosaveRowChange
                {
                    RowNumber = 1,
                    Changes =
                    [
                        new FieldSampleAutosaveFieldChange { Field = "DefectsInspected", OriginalValue = "false", Value = "true" },
                        new FieldSampleAutosaveFieldChange { Field = "DefectTypeIds", OriginalValue = null, Value = "1,2" }
                    ]
                },
                new FieldSampleAutosaveRowChange
                {
                    RowNumber = 2,
                    Changes = [new FieldSampleAutosaveFieldChange { Field = "DefectsInspected", OriginalValue = "false", Value = "true" }]
                }
            ]
        }, Owner(), CancellationToken.None);

        Assert.True(result.Saved);
        var detail = await service.GetDetailAsync(sampleId, Owner(), CancellationToken.None);
        Assert.Equal(2, detail.CurrentSummary.DefectInspectedFruitCount);
        Assert.Equal(1, detail.CurrentSummary.DefectAffectedFruitCount);
        Assert.Equal(50m, detail.CurrentSummary.DefectAffectedPercentage);
        Assert.Equal(["Bruise", "Other"], detail.CurrentSummary.DefectDistribution.Select(x => x.Defect).ToArray());
        Assert.False(detail.FruitRows.Single(x => x.RowNumber == 3).DefectsInspected);
        Assert.True(detail.FruitRows.Single(x => x.RowNumber == 2).DefectsInspected);
        Assert.Empty(detail.FruitRows.Single(x => x.RowNumber == 2).Defects);
    }

    [Theory]
    [InlineData("Apple", "Whole Apple Sample", "Cut Apple")]
    [InlineData("Pear", "Whole Pear Sample", "Cut Pear")]
    [InlineData("", "Whole Fruit Sample", "Cut Fruit")]
    public void CommodityTerminology_IsCentralized(string fruitType, string whole, string cut)
    {
        var result = FieldSampleCommodityTerminologyService.ForFruitType(fruitType);
        Assert.Equal(whole, result.WholeSampleLabel);
        Assert.Equal(cut, result.CutFruitLabel);
    }

    [Fact]
    public async Task CompleteFieldSample_RemainsEditableAndChangesAfterSendRequireResend()
    {
        await using var db = CreateDbContext();
        await SeedFieldSampleMasterDataAsync(db);
        var service = CreateService(db);
        var sampleId = await CreateSampleAsync(service, "Lifecycle Block", DateTimeOffset.UtcNow);
        await SavePressureAsync(service, sampleId, 12m, 13m);

        Assert.Null(await service.MarkCompleteAsync(sampleId, Owner(), CancellationToken.None));
        var completed = await db.QcSamples.SingleAsync(x => x.Id == sampleId);
        Assert.Equal("Complete", completed.Status);
        Assert.Equal("Not Sent", completed.EmailStatus);

        completed.Status = "Sent";
        completed.EmailStatus = "Sent";
        db.QcSummaryEmailLogs.Add(new QcSummaryEmailLog
        {
            QcSampleId = sampleId,
            FromAddress = ApplicationAreas.OwnerEmail,
            ToAddress = "qc@fruitandland.com",
            Subject = "Prior Field Sample report",
            Status = "Sent",
            SentAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var editError = await service.SaveRowsAsync(sampleId, new SaveFruitReadingsForm
        {
            SampleId = sampleId,
            TargetSampleSize = 10,
            Rows = [new FruitReadingEditRow
            {
                RowNumber = 1,
                Pressure1Lbs = 11m,
                OriginalPressure1Lbs = 12m,
                Pressure2Lbs = 13m,
                OriginalPressure2Lbs = 13m
            }]
        }, Owner(), CancellationToken.None);

        Assert.Null(editError);
        var changed = await db.QcSamples.SingleAsync(x => x.Id == sampleId);
        Assert.Equal("Changed Since Last Send", changed.Status);
        Assert.Equal("Needs Resend", changed.EmailStatus);
        Assert.Single(await db.QcSummaryEmailLogs.Where(x => x.QcSampleId == sampleId).ToListAsync());
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
        Assert.DoesNotContain("Weight Trend", detail);
        Assert.DoesNotContain("Starch Trend", detail);
        Assert.DoesNotContain("Pressure Trend", detail);
        Assert.DoesNotContain("Size Trend", detail);
        Assert.Contains("_BlockTrendCard", index);
        Assert.Contains("Save Now", detail);
        Assert.Contains("Open in QC Station", detail);
        Assert.Contains("Html.PartialAsync(\"_DeviceCapturePanel\"", detail);
        Assert.Contains("ShowScale: true", detail);
        Assert.Contains("id=\"sample-photos\"", detail);
        Assert.Contains("AllowMultiple = true", detail);
        Assert.Contains("Preview Report", detail);
        Assert.Contains("sizeThresholdJson", detail);
        Assert.Contains("class=\"fruit-row\"", detail);
        Assert.Contains("data-add-field-row", detail);
        Assert.Contains("field-sample-autosave.js", detail);
        Assert.Contains("data-size-category", detail);
        Assert.DoesNotContain("name=\"Rows[@i].SizeCategory\"", detail);
        var autosave = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "wwwroot", "js", "field-sample-autosave.js"));
        Assert.Contains("localStorage.setItem", autosave);
        Assert.Contains("debounceMilliseconds: 1000", detail);
        Assert.Contains("Conflict detected", autosave);
        Assert.Contains("sameSubmittedChange", autosave);
        Assert.Contains("current.originalValue = normalize(serverValue)", autosave);
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

    [Fact]
    public void FieldSamplePhotoAdd_UsesSampleTypeSpecificEditPermission()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var start = service.IndexOf("public async Task<string?> AddSamplePhotoMetadataAsync", StringComparison.Ordinal);
        var end = service.IndexOf("public async Task<string?> RemoveSamplePhotoAsync", start, StringComparison.Ordinal);
        var method = service[start..end];

        Assert.Contains("ApplicationAreas.FieldSamples, PageAccessLevel.Edit", method, StringComparison.Ordinal);
        Assert.Contains("CanEditSamplesAsync(cancellationToken)", method, StringComparison.Ordinal);
        Assert.Contains("You do not have permission to add photos.", method, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FieldSampleReport_PreviewsConfirmedOrchardRecipientsAndPreservesSendHistory()
    {
        await using var db = CreateDbContext();
        await SeedFieldSampleMasterDataAsync(db);
        db.Users.Add(new User
        {
            Id = 1,
            Email = ApplicationAreas.OwnerEmail,
            DisplayName = "Test Owner",
            Domain = "fruitandland.com",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var fieldSampleService = CreateService(db);
        var create = await fieldSampleService.CreateAsync(new FieldSampleCreateForm
        {
            OrchardName = "WP ORCHARD",
            GrowerNumber = "1080",
            BlockName = "Report Block",
            FruitProfileId = 1,
            ConfirmCreateNewBlock = true,
            SampleTakenAt = new DateTimeOffset(2026, 7, 22, 8, 30, 0, TimeSpan.Zero)
        }, Owner(), CancellationToken.None);
        Assert.Null(create.Error);
        var sampleId = create.SampleId!.Value;
        await SavePressureAsync(fieldSampleService, sampleId, 12m, 14m);
        Assert.Null(await fieldSampleService.MarkCompleteAsync(sampleId, Owner(), CancellationToken.None));

        var orchardId = await db.CanonicalOrchardBlocks
            .Where(x => x.FieldSamples.Any(sample => sample.Id == sampleId))
            .Select(x => x.CanonicalOrchardId)
            .SingleAsync();
        db.OrchardReportRecipients.Add(new OrchardReportRecipient
        {
            CanonicalOrchardId = orchardId,
            EmailAddress = "manager@example.com",
            NormalizedEmailAddress = "manager@example.com",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        db.QcPhotos.Add(new QcPhoto
        {
            QcSampleId = sampleId,
            PhotoType = "SampleBeforeCutting",
            PhotoSource = "Test",
            FileName = "field-sample.png",
            ContentType = "image/png",
            FileSizeBytes = FieldSamplePng.Length,
            StorageProvider = "Test",
            FileId = "field-photo-1",
            SharePointDriveId = "",
            SharePointItemId = "field-photo-1",
            CapturedAt = DateTimeOffset.UtcNow,
            UploadedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var access = new UserAccessService(db, new ConfigurationBuilder().Build());
        var resolver = new QcEmailRecipientResolver(db, new EmailOptions(), NullLogger<QcEmailRecipientResolver>.Instance);
        var sender = new CapturingFieldSampleEmailSender();
        var reportService = new FieldSampleReportService(
            db,
            fieldSampleService,
            access,
            resolver,
            sender,
            new FieldSamplePhotoStorageService(),
            NullLogger<FieldSampleReportService>.Instance);

        var (preview, previewError) = await reportService.PreviewAsync(sampleId, Owner(), CancellationToken.None);
        Assert.Null(previewError);
        Assert.NotNull(preview);
        Assert.Equal("qc@fruitandland.com, manager@example.com", preview!.Recipients);
        Assert.Contains("WP ORCHARD", preview.Subject);
        Assert.Contains("Report Block", preview.Subject);
        Assert.Contains("Grower number</th><td>1080", preview.HtmlBody);
        Assert.Contains("Same-Block Trends", preview.HtmlBody);
        Assert.Contains("data:image/jpeg;base64,", preview.HtmlBody);

        Assert.Null(await reportService.SendAsync(sampleId, Owner(), CancellationToken.None));
        Assert.NotNull(sender.Message);
        Assert.Equal("qc@fruitandland.com, manager@example.com", sender.Message!.To);
        Assert.Single(sender.Message.InlineImages);
        var sentSample = await db.QcSamples.SingleAsync(x => x.Id == sampleId);
        Assert.Equal("Sent", sentSample.Status);
        Assert.Equal("Sent", sentSample.EmailStatus);
        var history = await db.QcSummaryEmailLogs.SingleAsync(x => x.QcSampleId == sampleId);
        Assert.Null(history.ReceiptId);
        Assert.Equal("gmail-field-1", history.MessageId);
        Assert.False(history.IsResend);
        Assert.Equal("This Field Sample report was already sent and the sample has not changed.",
            await reportService.SendAsync(sampleId, Owner(), CancellationToken.None));

        Assert.Null(await fieldSampleService.SaveRowsAsync(sampleId, new SaveFruitReadingsForm
        {
            SampleId = sampleId,
            TargetSampleSize = 10,
            Rows = [new FruitReadingEditRow
            {
                RowNumber = 1,
                Pressure1Lbs = 11.5m,
                OriginalPressure1Lbs = 12m,
                Pressure2Lbs = 14m,
                OriginalPressure2Lbs = 14m
            }]
        }, Owner(), CancellationToken.None));
        var changed = await db.QcSamples.SingleAsync(x => x.Id == sampleId);
        Assert.Equal("Needs Resend", changed.EmailStatus);

        Assert.Null(await reportService.SendAsync(sampleId, Owner(), CancellationToken.None));
        var sendHistory = await db.QcSummaryEmailLogs.Where(x => x.QcSampleId == sampleId).OrderBy(x => x.Id).ToListAsync();
        Assert.Equal(2, sendHistory.Count);
        Assert.False(sendHistory[0].IsResend);
        Assert.True(sendHistory[1].IsResend);
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
        db.FruitProfiles.Add(new FruitProfile
        {
            Id = 2,
            Name = "Bartlett Pear",
            VarietyCode = "BART",
            FruitType = "Pear",
            ProductionType = "Conventional"
        });
        db.Grades.Add(new Grade { Id = 1, Code = "US1", Name = "US No. 1", IsActive = true });
        db.DefectTypes.AddRange(
            new DefectType { Id = 1, Name = "Bruise", IsActive = true },
            new DefectType { Id = 2, Name = "Other", IsActive = true },
            new DefectType { Id = 3, Name = "Inactive Defect", IsActive = false });
        db.StarchScales.Add(new StarchScale { Id = 1, Name = "Apple Starch", FruitType = "Apple" });
        db.StarchScaleValues.AddRange(
            new StarchScaleValue { Id = 1, StarchScaleId = 1, Value = 3m, SortOrder = 1 },
            new StarchScaleValue { Id = 2, StarchScaleId = 1, Value = 4m, SortOrder = 2 });
        db.FruitSizeConversionThresholds.AddRange(
            new FruitSizeConversionThreshold { Id = 1, FruitType = "Apple", SizeCategory = 72, MinimumWeightGrams = 264m },
            new FruitSizeConversionThreshold { Id = 2, FruitType = "Apple", SizeCategory = 80, MinimumWeightGrams = 238m });
        db.FruitSizeConversionThresholds.Add(new FruitSizeConversionThreshold { Id = 3, FruitType = "Pear", SizeCategory = 90, MinimumWeightGrams = 200m });
        await db.SaveChangesAsync();
    }

    private static ClaimsPrincipal Owner() =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Email, ApplicationAreas.OwnerEmail)], "Test"));

    private sealed class CapturingFieldSampleEmailSender : IQcEmailSender
    {
        public QcEmailMessage? Message { get; private set; }

        public Task<QcEmailSendResult> SendAsync(User sender, QcEmailMessage message, CancellationToken cancellationToken)
        {
            Message = message;
            return Task.FromResult(QcEmailSendResult.Sent("gmail-field-1"));
        }
    }

    private static readonly byte[] FieldSamplePng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private sealed class FieldSamplePhotoStorageService : IFileStorageService
    {
        public string GenerateTargetPath(FileStorageTargetContext context) => "unused";
        public Task<FileStorageReference> SaveAsync(FileStorageSaveRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public Task<FileStorageReference?> GetMetadataAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<FileStorageReference?>(null);
        public Task<Stream?> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(storageKey == "field-photo-1" ? new MemoryStream(FieldSamplePng, writable: false) : null);
        public Task DeleteOrVoidAsync(string storageKey, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

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
