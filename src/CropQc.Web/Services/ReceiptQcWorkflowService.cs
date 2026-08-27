using System.Security.Claims;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IReceiptQcWorkflowService
{
    Task<ReceiptQcSampleOpenResult> OpenAsync(
        long receiptId,
        bool allowCreate,
        ClaimsPrincipal user,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<HistoricalReceiptQcSampleAudit>> GetHistoricalDuplicateAuditAsync(
        CancellationToken cancellationToken);
}

public sealed class ReceiptQcWorkflowService(
    CropQcDbContext dbContext) : IReceiptQcWorkflowService
{
    public async Task<ReceiptQcSampleOpenResult> OpenAsync(
        long receiptId,
        bool allowCreate,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var email = user.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();
        var userId = string.IsNullOrWhiteSpace(email)
            ? null
            : await dbContext.Users.AsNoTracking()
                .Where(x => x.Email == email && x.IsActive)
                .Select(x => (int?)x.Id)
                .SingleOrDefaultAsync(cancellationToken);

        var result = await ReceiptQcSampleCoordinator.OpenOrCreateAsync(
            dbContext,
            receiptId,
            allowCreate,
            requestedSampleTypeId: null,
            takenByUserId: userId,
            qcStationId: null,
            actualSampleSize: 10,
            sampleTakenAt: null,
            notes: null,
            cancellationToken);

        if (result.Created && result.Sample is not null && result.Receipt is not null)
        {
            dbContext.AuditLogs.Add(new AuditLog
            {
                Action = "Create",
                EntityName = nameof(QcSample),
                EntityKey = result.Sample.Id.ToString(),
                UserId = userId,
                AfterValuesJson = JsonSerializer.Serialize(new
                {
                    result.Sample.Id,
                    result.Sample.ReceiptId,
                    result.Sample.SampleTypeId,
                    result.Sample.SampleSequenceNumber,
                    Receipt = result.Receipt.CompuTechReceiptId,
                    Source = "Open Receiving"
                }),
                SourceApplication = "CropQc.Web",
                CreatedAt = DateTimeOffset.UtcNow
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    public Task<IReadOnlyList<HistoricalReceiptQcSampleAudit>> GetHistoricalDuplicateAuditAsync(
        CancellationToken cancellationToken) =>
        ReceiptQcSampleCoordinator.GetHistoricalDuplicateAuditAsync(dbContext, cancellationToken);
}
