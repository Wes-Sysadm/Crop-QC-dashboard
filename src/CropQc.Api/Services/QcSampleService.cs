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
        var receipt = await dbContext.Receipts.SingleOrDefaultAsync(x => x.Id == receiptId && !x.IsDeleted, cancellationToken);
        if (receipt is null)
        {
            return (null, "Receipt not found.");
        }

        if (request.SampleTypeId <= 0)
        {
            return (null, "SampleTypeId is required.");
        }

        var existingCount = await dbContext.QcSamples.CountAsync(x => x.ReceiptId == receiptId && !x.IsDeleted, cancellationToken);
        var sequence = existingCount + 1;
        var sample = new QcSample
        {
            ReceiptId = receiptId,
            SampleTypeId = request.SampleTypeId,
            SampleSequenceNumber = sequence,
            Status = sequence > 1 ? "Needs Review" : "Data Entry In Progress",
            StarchStatus = "Starch Pending",
            PhotoStatus = "Photo Pending",
            EmailStatus = "Not Sent",
            TakenByUserId = request.TakenByUserId,
            QcStationId = request.QcStationId,
            ActualSampleSize = request.ActualSampleSize,
            Notes = request.Notes,
            SampleTakenAt = request.SampleTakenAt ?? DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.QcSamples.Add(sample);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync("Create", nameof(QcSample), sample.Id.ToString(), afterValuesJson: sequence > 1 ? "Duplicate receiving sample marked Needs Review." : "QC sample created.", cancellationToken: cancellationToken);
        return (ToDto(sample, receipt.CompuTechReceiptId), null);
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
        sample.Notes = request.Notes;
        sample.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync("Edit", nameof(QcSample), sample.Id.ToString(), afterValuesJson: "Sample statuses updated.", cancellationToken: cancellationToken);
        return (ToDto(sample, sample.Receipt.CompuTechReceiptId), null);
    }

    public static QcSampleDto ToDto(QcSample sample, string compuTechReceiptId) => new(
        sample.Id,
        sample.ReceiptId,
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
