using CropQc.Api.Dtos;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Api.Services;

public interface IQcSampleService
{
    Task<(QcSampleDto? Sample, string? Error)> CreateAsync(long receiptId, CreateQcSampleRequest request, CancellationToken cancellationToken);
    Task<QcSampleDto?> GetAsync(long id, CancellationToken cancellationToken);
    Task<IReadOnlyList<QcSampleDto>> GetForReceiptAsync(long receiptId, CancellationToken cancellationToken);
    Task<IReadOnlyList<QcSampleDto>> GetTodayByWarehouseAsync(int warehouseId, CancellationToken cancellationToken);
    Task<(QcSampleDto? Sample, string? Error)> UpdateStatusesAsync(long id, UpdateQcSampleStatusesRequest request, CancellationToken cancellationToken);
}

public sealed class QcSampleService(CropQcDbContext dbContext, IAuditService auditService) : IQcSampleService
{
    public async Task<(QcSampleDto? Sample, string? Error)> CreateAsync(long receiptId, CreateQcSampleRequest request, CancellationToken cancellationToken)
    {
        if (request.SampleTypeId <= 0)
        {
            return (null, "SampleTypeId is required.");
        }

        var result = await ReceiptQcSampleCoordinator.OpenOrCreateAsync(
            dbContext,
            receiptId,
            allowCreate: true,
            request.SampleTypeId,
            request.TakenByUserId,
            request.QcStationId,
            request.ActualSampleSize,
            request.SampleTakenAt,
            request.Notes,
            cancellationToken);
        if (result.Sample is null || result.Receipt is null)
        {
            return (null, result.Error);
        }

        if (result.Created)
        {
            await auditService.RecordAsync(
                "Create",
                nameof(QcSample),
                result.Sample.Id.ToString(),
                afterValuesJson: $"Single receipt QC sample created as {result.Sample.SampleTypeId}.",
                cancellationToken: cancellationToken);
        }

        return (ToDto(result.Sample, result.Receipt.CompuTechReceiptId), null);
    }

    public async Task<QcSampleDto?> GetAsync(long id, CancellationToken cancellationToken) =>
        await dbContext.QcSamples.AsNoTracking().Include(x => x.Receipt)
            .Where(x => x.Id == id && !x.IsDeleted)
            .Select(x => ToDto(x, x.Receipt.CompuTechReceiptId))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<QcSampleDto>> GetForReceiptAsync(long receiptId, CancellationToken cancellationToken) =>
        await dbContext.QcSamples.AsNoTracking().Include(x => x.Receipt)
            .Where(x => x.ReceiptId == receiptId && !x.IsDeleted)
            .OrderBy(x => x.SampleSequenceNumber)
            .Select(x => ToDto(x, x.Receipt.CompuTechReceiptId))
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<QcSampleDto>> GetTodayByWarehouseAsync(int warehouseId, CancellationToken cancellationToken)
    {
        var todayRange = UtcDayRange.ForUtcDay(DateTimeOffset.UtcNow);
        return await dbContext.QcSamples.AsNoTracking().Include(x => x.Receipt)
            .Where(x => !x.IsDeleted && x.Receipt.WarehouseId == warehouseId && x.SampleTakenAt >= todayRange.Start && x.SampleTakenAt < todayRange.End)
            .OrderByDescending(x => x.SampleTakenAt)
            .Select(x => ToDto(x, x.Receipt.CompuTechReceiptId))
            .ToListAsync(cancellationToken);
    }

    public async Task<(QcSampleDto? Sample, string? Error)> UpdateStatusesAsync(long id, UpdateQcSampleStatusesRequest request, CancellationToken cancellationToken)
    {
        var sample = await dbContext.QcSamples.Include(x => x.Receipt).SingleOrDefaultAsync(x => x.Id == id && !x.IsDeleted, cancellationToken);
        if (sample is null)
        {
            return (null, "QC sample not found.");
        }

        sample.Status = request.Status;
        sample.StarchStatus = request.StarchStatus;
        sample.PhotoStatus = request.PhotoStatus;
        sample.EmailStatus = request.EmailStatus;
        if (sample.Receipt is null || sample.ReceiptId is null)
        {
            return (null, "Receipt-backed QC sample not found.");
        }

        sample.Notes = request.Notes;
        sample.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync("Edit", nameof(QcSample), sample.Id.ToString(), afterValuesJson: "Sample statuses updated.", cancellationToken: cancellationToken);
        return (ToDto(sample, sample.Receipt.CompuTechReceiptId), null);
    }

    public static QcSampleDto ToDto(QcSample sample, string compuTechReceiptId) => new(
        sample.Id,
        sample.ReceiptId!.Value,
        sample.SampleTypeId,
        sample.SampleSequenceNumber,
        sample.SampleSequenceNumber <= 1 ? compuTechReceiptId : $"{compuTechReceiptId}({sample.SampleSequenceNumber})",
        sample.Status,
        sample.StarchStatus,
        sample.PhotoStatus,
        sample.EmailStatus,
        sample.TakenByUserId,
        sample.QcStationId,
        sample.ActualSampleSize,
        sample.Notes,
        sample.SampleTakenAt);
}
