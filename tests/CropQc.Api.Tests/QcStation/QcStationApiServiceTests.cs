using CropQc.Api.Dtos;
using CropQc.Api.Services;
using CropQc.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Api.Tests.QcStation;

public sealed class QcStationApiServiceTests
{
    [Fact]
    public void ApplyPressureOnlyUpdate_DoesNotClearWeightGradeStarchSizeOrCompletionData()
    {
        var reading = new QcFruitReading
        {
            Id = 100,
            QcSampleId = 5,
            RowNumber = 1,
            Pressure1Lbs = 10m,
            Pressure1Source = "Manual",
            Pressure2Lbs = 11m,
            Pressure2Source = "Manual",
            WeightGrams = 185m,
            GradeId = 2,
            StarchScaleValueId = 4,
            SizeCategory = 100,
            SizeStatus = "Calculated",
            IsCompleted = true,
            CreatedAt = DateTimeOffset.UtcNow
        };

        QcStationApiService.ApplyPressureOnlyUpdate(reading, new UpdateQcStationPressureRowRequest(1, 12.25m, 13.75m));

        Assert.Equal(12.25m, reading.Pressure1Lbs);
        Assert.Equal("FTA", reading.Pressure1Source);
        Assert.Equal(13.75m, reading.Pressure2Lbs);
        Assert.Equal("FTA", reading.Pressure2Source);
        Assert.Equal(185m, reading.WeightGrams);
        Assert.Equal(2, reading.GradeId);
        Assert.Equal(4, reading.StarchScaleValueId);
        Assert.Equal(100, reading.SizeCategory);
        Assert.Equal("Calculated", reading.SizeStatus);
        Assert.True(reading.IsCompleted);
    }

    [Fact]
    public void ApplyPressureOnlyUpdate_KeepsPressureOnlyRowsIncompleteWhenWeightAndGradeAreMissing()
    {
        var reading = new QcFruitReading
        {
            QcSampleId = 5,
            RowNumber = 1,
            SizeStatus = "NotCalculated",
            IsCompleted = false,
            CreatedAt = DateTimeOffset.UtcNow
        };

        QcStationApiService.ApplyPressureOnlyUpdate(reading, new UpdateQcStationPressureRowRequest(1, 12.25m, 13.75m));

        Assert.Equal(12.25m, reading.Pressure1Lbs);
        Assert.Equal(13.75m, reading.Pressure2Lbs);
        Assert.False(reading.IsCompleted);
        Assert.Null(reading.WeightGrams);
        Assert.Null(reading.GradeId);
    }

    [Fact]
    public void QcStationDetailPayload_IncludesTargetSampleSizeAndAvoidsFixedTwentyFiveGrid()
    {
        var apiDto = File.ReadAllText(FindRepositoryFile("src", "CropQc.Api", "Dtos", "QcDtos.cs"));
        var stationDto = File.ReadAllText(FindRepositoryFile("src", "CropQc.QcStation", "Api", "QcStationApiDtos.cs"));
        var webController = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "QcStationController.cs"));
        var apiService = File.ReadAllText(FindRepositoryFile("src", "CropQc.Api", "Services", "QcStationApiService.cs"));

        Assert.Contains("int TargetSampleSize", apiDto, StringComparison.Ordinal);
        Assert.Contains("int TargetSampleSize", stationDto, StringComparison.Ordinal);
        Assert.DoesNotContain("Enumerable.Range(1, 25)", webController, StringComparison.Ordinal);
        Assert.DoesNotContain("Enumerable.Range(1, 25)", apiService, StringComparison.Ordinal);
        Assert.Contains("Math.Max(targetSampleSize", webController, StringComparison.Ordinal);
        Assert.Contains("Math.Max(targetSampleSize", apiService, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QcStationApi_LoadsReceiptlessFieldSampleBySampleId()
    {
        await using var db = CreateDbContext();
        var sample = await SeedFieldSampleAsync(db, actualSampleSize: 20);
        var service = new QcStationApiService(db, new AuditService(db));

        var detail = await service.GetSampleDetailAsync(sample.Id, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Null(detail.ReceiptId);
        Assert.Equal($"Field Sample #{sample.Id}", detail.DisplayReceiptId);
        Assert.Equal("WP Orchard", detail.GrowerName);
        Assert.Equal("North Block 12", detail.LotCode);
        Assert.Equal("GALA", detail.VarietyCode);
        Assert.Equal(20, detail.TargetSampleSize);
        Assert.Equal(20, detail.FruitReadings.Count);
    }

    [Fact]
    public async Task QcStationApi_SavesFtaPressuresToExpandedFieldSampleRowsWithoutReceiptOrInventory()
    {
        await using var db = CreateDbContext();
        var sample = await SeedFieldSampleAsync(db, actualSampleSize: 50);
        var station = new CropQc.Data.Entities.QcStation
        {
            Id = 1,
            StationCode = "FIELD-FTA-01",
            Name = "Field FTA",
            StationName = "Field FTA",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.QcStations.Add(station);
        await db.SaveChangesAsync();
        var service = new QcStationApiService(db, new AuditService(db));

        var (updated, error) = await service.UpdatePressuresAsync(
            sample.Id,
            new UpdateQcStationPressuresRequest([
                new UpdateQcStationPressureRowRequest(11, 12.25m, null),
                new UpdateQcStationPressureRowRequest(20, 13.5m, 14.5m),
                new UpdateQcStationPressureRowRequest(50, 15.25m, 15.75m)
            ]),
            station,
            CancellationToken.None);

        Assert.Null(error);
        Assert.NotNull(updated);
        Assert.Equal(50, updated.TargetSampleSize);
        var row11 = await db.QcFruitReadings.SingleAsync(x => x.QcSampleId == sample.Id && x.RowNumber == 11);
        var row20 = await db.QcFruitReadings.SingleAsync(x => x.QcSampleId == sample.Id && x.RowNumber == 20);
        var row50 = await db.QcFruitReadings.SingleAsync(x => x.QcSampleId == sample.Id && x.RowNumber == 50);
        Assert.Equal(12.25m, row11.Pressure1Lbs);
        Assert.Equal("FTA", row11.Pressure1Source);
        Assert.Null(row11.Pressure2Lbs);
        Assert.Equal(13.5m, row20.Pressure1Lbs);
        Assert.Equal(14.5m, row20.Pressure2Lbs);
        Assert.Equal(15.25m, row50.Pressure1Lbs);
        Assert.Equal(15.75m, row50.Pressure2Lbs);
        Assert.Equal("FTA", row50.Pressure1Source);
        Assert.Equal("FTA", row50.Pressure2Source);
        Assert.Null((await db.QcSamples.SingleAsync(x => x.Id == sample.Id)).ReceiptId);
        Assert.Empty(await db.RoomInventoryAdjustments.ToListAsync());
        Assert.Empty(await db.BinsRunEntries.ToListAsync());
        Assert.Contains(await db.AuditLogs.ToListAsync(), x => x.SourceApplication == "CropQc.QcStation:Field FTA");
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

    private static CropQcDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;
        return new CropQcDbContext(options);
    }

    private static async Task<QcSample> SeedFieldSampleAsync(CropQcDbContext db, int actualSampleSize)
    {
        var sampleType = new SampleType { Id = 5, Name = "Field Sample" };
        var fruitProfile = new FruitProfile
        {
            Id = 1,
            Name = "Gala Apple",
            VarietyCode = "GALA",
            FruitType = "Apple",
            ProductionType = "Conventional"
        };
        var block = new CanonicalOrchardBlock
        {
            Id = 1,
            OrchardName = "WP Orchard",
            CanonicalBlockName = "North Block 12",
            NormalizedOrchardKey = "WPORCHARD",
            NormalizedBlockKey = "NORTHBLOCK12",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var sample = new QcSample
        {
            Id = 100,
            ReceiptId = null,
            SampleTypeId = sampleType.Id,
            SampleType = sampleType,
            Status = "Data Entry In Progress",
            StarchStatus = "Starch Pending",
            PhotoStatus = "Not Required",
            EmailStatus = "Not Applicable",
            ActualSampleSize = actualSampleSize,
            FieldSampleFruitProfileId = fruitProfile.Id,
            FieldSampleFruitProfile = fruitProfile,
            CanonicalOrchardBlockId = block.Id,
            CanonicalOrchardBlock = block,
            FieldSampleGrowerName = "WP Orchard",
            FieldSampleOriginalBlockName = "North Block 12",
            SampleTakenAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.SampleTypes.Add(sampleType);
        db.FruitProfiles.Add(fruitProfile);
        db.CanonicalOrchardBlocks.Add(block);
        db.QcSamples.Add(sample);
        await db.SaveChangesAsync();
        return sample;
    }
}
