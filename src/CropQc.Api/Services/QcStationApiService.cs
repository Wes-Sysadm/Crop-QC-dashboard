using CropQc.Api.Dtos;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Api.Services;

public interface IQcStationApiService
{
    Task<IReadOnlyList<QcStationSampleListItemDto>> GetTodaySamplesAsync(string? warehouseCode, CancellationToken cancellationToken);
    Task<QcStationSampleDetailDto?> GetSampleDetailAsync(long sampleId, CancellationToken cancellationToken);
    Task<(QcStationSampleDetailDto? Sample, string? Error)> UpdatePressuresAsync(long sampleId, UpdateQcStationPressuresRequest request, QcStation station, CancellationToken cancellationToken);
}

public sealed class QcStationApiService(CropQcDbContext dbContext, IAuditService auditService) : IQcStationApiService
{
    public async Task<IReadOnlyList<QcStationSampleListItemDto>> GetTodaySamplesAsync(string? warehouseCode, CancellationToken cancellationToken)
    {
        var todayRange = UtcDayRange.ForUtcDay(DateTimeOffset.UtcNow);
        var query = dbContext.QcSamples.AsNoTracking()
            .Include(x => x.SampleType)
            .Include(x => x.Receipt!).ThenInclude(x => x.Warehouse)
            .Include(x => x.Receipt!).ThenInclude(x => x.Room)
            .Include(x => x.Receipt!).ThenInclude(x => x.FruitProfile)
            .Include(x => x.FieldSampleFruitProfile)
            .Include(x => x.CanonicalOrchardBlock)
            .Include(x => x.FruitReadings)
            .Where(x => !x.IsDeleted
                && (x.ReceiptId != null || x.SampleType.Name == "Field Sample")
                && x.SampleTakenAt >= todayRange.Start
                && x.SampleTakenAt < todayRange.End);

        if (!string.IsNullOrWhiteSpace(warehouseCode))
        {
            query = query.Where(x => x.ReceiptId == null || x.Receipt!.Warehouse.Code == warehouseCode);
        }

        return await query
            .OrderByDescending(x => x.SampleTakenAt)
            .Select(x => new QcStationSampleListItemDto(
                x.Id,
                x.ReceiptId,
                x.ReceiptId == null ? "Field Sample #" + x.Id : x.SampleSequenceNumber <= 1 ? x.Receipt!.CompuTechReceiptId : x.Receipt!.CompuTechReceiptId + "(" + x.SampleSequenceNumber + ")",
                x.ReceiptId == null ? "FIELD" : x.Receipt!.Warehouse.Code,
                x.ReceiptId == null ? "Field" : x.Receipt!.Room.Code,
                x.ReceiptId == null ? x.FieldSampleGrowerName ?? (x.CanonicalOrchardBlock == null ? "" : x.CanonicalOrchardBlock.OrchardName) : x.Receipt!.GrowerName,
                x.ReceiptId == null ? (x.CanonicalOrchardBlock == null ? x.FieldSampleOriginalBlockName ?? "" : x.CanonicalOrchardBlock.CanonicalBlockName) : x.Receipt!.LotCode,
                x.ReceiptId == null ? (x.FieldSampleFruitProfile == null ? "" : x.FieldSampleFruitProfile.VarietyCode) : x.Receipt!.FruitProfile.VarietyCode,
                x.Status,
                x.StarchStatus,
                x.EmailStatus,
                x.FruitReadings.Count(row => row.Pressure1Lbs != null && row.Pressure2Lbs != null),
                x.SampleTakenAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<QcStationSampleDetailDto?> GetSampleDetailAsync(long sampleId, CancellationToken cancellationToken)
    {
        var sample = await dbContext.QcSamples.AsNoTracking()
            .Include(x => x.Receipt!).ThenInclude(x => x.Warehouse)
            .Include(x => x.Receipt!).ThenInclude(x => x.Room)
            .Include(x => x.Receipt!).ThenInclude(x => x.FruitProfile)
            .Include(x => x.FieldSampleFruitProfile)
            .Include(x => x.CanonicalOrchardBlock)
            .Include(x => x.FruitReadings).ThenInclude(x => x.Grade)
            .Include(x => x.FruitReadings).ThenInclude(x => x.StarchScaleValue)
            .Include(x => x.FruitReadings).ThenInclude(x => x.Defects).ThenInclude(x => x.DefectType)
            .SingleOrDefaultAsync(x => x.Id == sampleId && !x.IsDeleted, cancellationToken);

        return sample is null ? null : ToDetailDto(sample);
    }

    public async Task<(QcStationSampleDetailDto? Sample, string? Error)> UpdatePressuresAsync(long sampleId, UpdateQcStationPressuresRequest request, QcStation station, CancellationToken cancellationToken)
    {
        var sample = await dbContext.QcSamples
            .Include(x => x.Receipt!).ThenInclude(x => x.Warehouse)
            .Include(x => x.Receipt!).ThenInclude(x => x.Room)
            .Include(x => x.Receipt!).ThenInclude(x => x.FruitProfile)
            .Include(x => x.FieldSampleFruitProfile)
            .Include(x => x.CanonicalOrchardBlock)
            .Include(x => x.FruitReadings).ThenInclude(x => x.Grade)
            .Include(x => x.FruitReadings).ThenInclude(x => x.StarchScaleValue)
            .Include(x => x.FruitReadings).ThenInclude(x => x.Defects).ThenInclude(x => x.DefectType)
            .SingleOrDefaultAsync(x => x.Id == sampleId && !x.IsDeleted, cancellationToken);

        if (sample is null)
        {
            return (null, "QC sample not found.");
        }

        if (request.Rows is null || request.Rows.Count == 0)
        {
            return (null, "At least one pressure row is required.");
        }

        var before = sample.FruitReadings
            .OrderBy(x => x.RowNumber)
            .Select(x => new { x.RowNumber, x.Pressure1Lbs, x.Pressure2Lbs })
            .ToList();
        var existingRows = sample.FruitReadings.ToDictionary(x => x.RowNumber);
        var targetSampleSize = ResolveTargetSampleSize(sample);
        foreach (var row in request.Rows)
        {
            if (row.RowNumber < 1 || row.RowNumber > targetSampleSize)
            {
                return (null, $"RowNumber {row.RowNumber} must be between 1 and {targetSampleSize}.");
            }

            if (!existingRows.TryGetValue(row.RowNumber, out var reading))
            {
                reading = new QcFruitReading
                {
                    QcSampleId = sampleId,
                    RowNumber = row.RowNumber,
                    SizeStatus = SizeCalculationService.NotCalculated,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                dbContext.QcFruitReadings.Add(reading);
                sample.FruitReadings.Add(reading);
                existingRows[row.RowNumber] = reading;
            }

            ApplyPressureOnlyUpdate(reading, row);
        }

        sample.UpdatedAt = DateTimeOffset.UtcNow;
        if (sample.ReceiptId is null)
        {
            sample.FieldSampleAutosaveVersion++;
        }
        sample.QcStationId = station.Id;
        station.LastSyncAt = DateTimeOffset.UtcNow;
        station.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(
            "Edit",
            nameof(QcFruitReading),
            sampleId.ToString(),
            beforeValuesJson: System.Text.Json.JsonSerializer.Serialize(before),
            afterValuesJson: System.Text.Json.JsonSerializer.Serialize(new
            {
                StationId = station.Id,
                station.StationCode,
                StationName = string.IsNullOrWhiteSpace(station.StationName) ? station.Name : station.StationName,
                SampleId = sampleId,
                Rows = request.Rows.Select(x => new { x.RowNumber, x.Pressure1Lbs, x.Pressure2Lbs })
            }),
            sourceApplication: $"CropQc.QcStation:{(string.IsNullOrWhiteSpace(station.StationName) ? station.Name : station.StationName)}",
            cancellationToken: cancellationToken);
        return (ToDetailDto(sample), null);
    }

    public static void ApplyPressureOnlyUpdate(QcFruitReading reading, UpdateQcStationPressureRowRequest row)
    {
        reading.Pressure1Lbs = row.Pressure1Lbs;
        reading.Pressure1Source = row.Pressure1Lbs is null ? null : "FTA";
        reading.Pressure2Lbs = row.Pressure2Lbs;
        reading.Pressure2Source = row.Pressure2Lbs is null ? null : "FTA";
        reading.IsCompleted = reading.Pressure1Lbs is not null
            && reading.Pressure2Lbs is not null
            && reading.WeightGrams is not null
            && reading.GradeId is not null;
        reading.UpdatedAt = DateTimeOffset.UtcNow;
        reading.FieldVersion++;
    }

    private static QcStationSampleDetailDto ToDetailDto(QcSample sample)
    {
        var targetSampleSize = ResolveTargetSampleSize(sample);
        var rowCount = Math.Max(targetSampleSize, sample.FruitReadings.Count == 0 ? 0 : sample.FruitReadings.Max(x => x.RowNumber));
        var receipt = sample.Receipt;
        var fieldBlock = sample.CanonicalOrchardBlock;
        var displayId = receipt is null ? $"Field Sample #{sample.Id}" : sample.GetDisplayReceiptId();
        var originalId = receipt?.CompuTechReceiptId ?? sample.FieldSampleOriginalBlockName ?? displayId;
        var readingsByRow = sample.FruitReadings
            .GroupBy(x => x.RowNumber)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(row => row.Id).First());

        return new(
            sample.Id,
            sample.ReceiptId,
            displayId,
            originalId,
            receipt?.GrowerName ?? sample.FieldSampleGrowerName ?? fieldBlock?.OrchardName ?? "",
            receipt?.LotCode ?? fieldBlock?.CanonicalBlockName ?? sample.FieldSampleOriginalBlockName ?? "",
            receipt?.FruitProfile.Name ?? sample.FieldSampleFruitProfile?.Name ?? "",
            receipt?.FruitProfile.VarietyCode ?? sample.FieldSampleFruitProfile?.VarietyCode ?? "",
            receipt?.Warehouse.Code ?? "FIELD",
            receipt?.Room.Code ?? "Field",
            sample.Status,
            sample.StarchStatus,
            sample.EmailStatus,
            sample.SampleTakenAt,
            targetSampleSize,
            Enumerable.Range(1, rowCount)
                .Select(rowNumber => ToStationFruitReadingDto(rowNumber, readingsByRow.GetValueOrDefault(rowNumber)))
                .ToList());
    }

    private static int ResolveTargetSampleSize(QcSample sample) =>
        Math.Clamp(sample.ActualSampleSize ?? 10, 1, 50);

    private static QcStationFruitReadingDto ToStationFruitReadingDto(int rowNumber, QcFruitReading? reading)
    {
        if (reading is null)
        {
            return new QcStationFruitReadingDto(
                0,
                0,
                rowNumber,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                SizeCalculationService.NotCalculated,
                false,
                []);
        }

        return new QcStationFruitReadingDto(
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
            reading.Grade?.Code,
            reading.StarchScaleValueId,
            reading.StarchScaleValue?.Value.ToString("0.####"),
            reading.SizeCategory,
            reading.SizeStatus,
            reading.IsCompleted,
            reading.Defects.Select(x => string.IsNullOrWhiteSpace(x.Notes) ? x.DefectType.Name : $"{x.DefectType.Name}: {x.Notes}").ToList());
    }
}
