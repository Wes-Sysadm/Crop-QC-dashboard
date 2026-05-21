using CropQc.Api.Dtos;
using CropQc.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Api.Services;

public interface IQcSummaryEmailLogService
{
    Task<(QcSummaryEmailLogDto? Log, string? Error)> CreateAsync(long receiptId, CreateEmailLogRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<QcSummaryEmailLogDto>> GetHistoryAsync(long receiptId, CancellationToken cancellationToken);
}

public sealed class QcSummaryEmailLogService(CropQcDbContext dbContext, IAuditService auditService) : IQcSummaryEmailLogService
{
    public async Task<(QcSummaryEmailLogDto? Log, string? Error)> CreateAsync(long receiptId, CreateEmailLogRequest request, CancellationToken cancellationToken)
    {
        var receipt = await dbContext.Receipts.FindAsync([receiptId], cancellationToken);
        if (receipt is null)
        {
            return (null, "Receipt not found.");
        }

        string? replyTo = request.ReplyToAddress;
        if (string.IsNullOrWhiteSpace(replyTo) && request.QcSampleId is not null)
        {
            var sampleTakerEmail = await dbContext.QcSamples.AsNoTracking()
                .Where(x => x.Id == request.QcSampleId)
                .Select(x => x.TakenByUser == null ? null : x.TakenByUser.Email)
                .SingleOrDefaultAsync(cancellationToken);
            replyTo = sampleTakerEmail;
        }

        var log = new QcSummaryEmailLog
        {
            ReceiptId = receiptId,
            QcSampleId = request.QcSampleId,
            FromAddress = string.IsNullOrWhiteSpace(request.FromAddress) ? "HL@fruitandland.com" : request.FromAddress,
            ToAddress = string.IsNullOrWhiteSpace(request.ToAddress) ? "QC@fruitandland.com" : request.ToAddress,
            ReplyToAddress = replyTo,
            Subject = request.Subject,
            Status = request.Status,
            MessageId = request.MessageId,
            SentByUserId = request.SentByUserId,
            SentAt = request.SentAt,
            IsResend = request.IsResend,
            ResendReason = request.ResendReason,
            EmailBodySnapshot = request.EmailBodySnapshot,
            ReportSnapshotReference = request.ReportSnapshotReference,
            CreatedAt = DateTimeOffset.UtcNow
        };

        dbContext.QcSummaryEmailLogs.Add(log);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync(request.IsResend ? "Resend" : "Send", nameof(QcSummaryEmailLog), log.Id.ToString(), afterValuesJson: "QC Summary email log placeholder created; email not sent.", cancellationToken: cancellationToken);
        return (ToDto(log), null);
    }

    public async Task<IReadOnlyList<QcSummaryEmailLogDto>> GetHistoryAsync(long receiptId, CancellationToken cancellationToken) =>
        await dbContext.QcSummaryEmailLogs.AsNoTracking()
            .Where(x => x.ReceiptId == receiptId)
            .OrderByDescending(x => x.CreatedAt)
            .Select(x => ToDto(x))
            .ToListAsync(cancellationToken);

    private static QcSummaryEmailLogDto ToDto(QcSummaryEmailLog log) => new(
        log.Id,
        log.ReceiptId,
        log.QcSampleId,
        log.FromAddress,
        log.ToAddress,
        log.ReplyToAddress,
        log.Subject,
        log.Status,
        log.MessageId,
        log.SentByUserId,
        log.SentAt,
        log.IsResend,
        log.ResendReason,
        log.EmailBodySnapshot,
        log.ReportSnapshotReference,
        log.CreatedAt);
}
