using CropQc.Api.Dtos;
using CropQc.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Api.Services;

public interface IQcFruitReadingService
{
    Task<(QcFruitReadingDto? Reading, string? Error)> UpsertAsync(long sampleId, int rowNumber, UpsertQcFruitReadingRequest request, CancellationToken cancellationToken);
    Task<(QcFruitDefectDto? Defect, string? Error)> AddDefectAsync(long readingId, CreateQcFruitDefectRequest request, CancellationToken cancellationToken);
    Task<bool> RemoveDefectAsync(long readingId, long defectId, CancellationToken cancellationToken);
}

public sealed class QcFruitReadingService(CropQcDbContext dbContext, IAuditService auditService) : IQcFruitReadingService
{
    public async Task<(QcFruitReadingDto? Reading, string? Error)> UpsertAsync(long sampleId, int rowNumber, UpsertQcFruitReadingRequest request, CancellationToken cancellationToken)
    {
        if (rowNumber is < 1 or > 25)
        {
            return (null, "RowNumber must be between 1 and 25.");
        }

        if (request.IsCompleted && (request.Pressure1Lbs is null || request.Pressure2Lbs is null || request.WeightGrams is null || request.GradeId is null))
        {
            return (null, "Completed rows require Pressure1Lbs, Pressure2Lbs, WeightGrams, and GradeId.");
        }

        var sample = await dbContext.QcSamples.Include(x => x.Receipt).ThenInclude(x => x.FruitProfile)
            .SingleOrDefaultAsync(x => x.Id == sampleId, cancellationToken);
        if (sample is null)
        {
            return (null, "QC sample not found.");
        }

        var isBlank = request.Pressure1Lbs is null
            && request.Pressure2Lbs is null
            && request.WeightGrams is null
            && request.GradeId is null
            && request.StarchScaleValueId is null;

        var reading = await dbContext.QcFruitReadings.SingleOrDefaultAsync(x => x.QcSampleId == sampleId && x.RowNumber == rowNumber, cancellationToken);
        if (reading is null)
        {
            reading = new QcFruitReading
            {
                QcSampleId = sampleId,
                RowNumber = rowNumber,
                SizeStatus = SizeCalculationService.NotCalculated,
                CreatedAt = DateTimeOffset.UtcNow
            };
            dbContext.QcFruitReadings.Add(reading);
        }

        var thresholds = await dbContext.FruitSizeConversionThresholds.AsNoTracking()
            .Where(x => x.FruitType == sample.Receipt.FruitProfile.FruitType)
            .ToListAsync(cancellationToken);
        var size = SizeCalculationService.Calculate(request.WeightGrams, thresholds);

        reading.Pressure1Lbs = request.Pressure1Lbs;
        reading.Pressure1Source = request.Pressure1Source;
        reading.Pressure2Lbs = request.Pressure2Lbs;
        reading.Pressure2Source = request.Pressure2Source;
        reading.WeightGrams = request.WeightGrams;
        reading.GradeId = request.GradeId;
        reading.StarchScaleValueId = request.StarchScaleValueId;
        reading.SizeCategory = size.SizeCategory;
        reading.SizeStatus = size.SizeStatus;
        reading.IsCompleted = request.IsCompleted && !isBlank;
        reading.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync("Edit", nameof(QcFruitReading), reading.Id.ToString(), afterValuesJson: "Fruit reading upserted.", cancellationToken: cancellationToken);
        return (ToDto(reading), null);
    }

    public async Task<(QcFruitDefectDto? Defect, string? Error)> AddDefectAsync(long readingId, CreateQcFruitDefectRequest request, CancellationToken cancellationToken)
    {
        if (!await dbContext.QcFruitReadings.AnyAsync(x => x.Id == readingId, cancellationToken))
        {
            return (null, "Fruit reading not found.");
        }

        var defect = new QcFruitDefect
        {
            QcFruitReadingId = readingId,
            DefectTypeId = request.DefectTypeId,
            Notes = request.Notes
        };

        dbContext.QcFruitDefects.Add(defect);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync("Create", nameof(QcFruitDefect), defect.Id.ToString(), afterValuesJson: "Fruit defect added.", cancellationToken: cancellationToken);
        return (new QcFruitDefectDto(defect.Id, defect.QcFruitReadingId, defect.DefectTypeId, defect.Notes), null);
    }

    public async Task<bool> RemoveDefectAsync(long readingId, long defectId, CancellationToken cancellationToken)
    {
        var defect = await dbContext.QcFruitDefects.SingleOrDefaultAsync(x => x.Id == defectId && x.QcFruitReadingId == readingId, cancellationToken);
        if (defect is null)
        {
            return false;
        }

        dbContext.QcFruitDefects.Remove(defect);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync("Delete", nameof(QcFruitDefect), defectId.ToString(), afterValuesJson: "Fruit defect removed.", cancellationToken: cancellationToken);
        return true;
    }

    public static QcFruitReadingDto ToDto(QcFruitReading reading) => new(
        reading.Id,
        reading.QcSampleId,
        reading.RowNumber,
        reading.Pressure1Lbs,
        reading.Pressure1Source,
        reading.Pressure2Lbs,
        reading.Pressure2Source,
        SizeCalculationService.CalculatePressureAverage(reading.Pressure1Lbs, reading.Pressure2Lbs),
        reading.WeightGrams,
        reading.GradeId,
        reading.StarchScaleValueId,
        reading.SizeCategory,
        reading.SizeStatus,
        reading.IsCompleted);
}
