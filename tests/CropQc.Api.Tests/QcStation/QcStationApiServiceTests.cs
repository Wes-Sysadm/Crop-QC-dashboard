using CropQc.Api.Dtos;
using CropQc.Api.Services;
using CropQc.Data.Entities;

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
}
