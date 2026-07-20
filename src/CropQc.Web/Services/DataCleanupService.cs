using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IDataCleanupService
{
    Task<DataCleanupViewModel> BuildPageAsync(DataCleanupFilterForm filter, CancellationToken cancellationToken);
    Task<(DataCleanupPreviewViewModel Preview, string? Error)> ExecuteAsync(DataCleanupFilterForm filter, string changedByEmail, CancellationToken cancellationToken);
}

public sealed class DataCleanupService(
    CropQcDbContext dbContext,
    ICropYearService cropYearService,
    IConfiguration configuration,
    IWebHostEnvironment environment) : IDataCleanupService
{
    private const string RequiredConfirmation = "DELETE TEST DATA";

    public async Task<DataCleanupViewModel> BuildPageAsync(DataCleanupFilterForm filter, CancellationToken cancellationToken)
    {
        filter.CropYear ??= cropYearService.GetCurrentCropYear(DateTimeOffset.Now);
        return new DataCleanupViewModel
        {
            Filter = filter,
            Preview = await PreviewAsync(filter, cancellationToken),
            AvailableCropYears = await cropYearService.GetAvailableCropYearsAsync(cancellationToken),
            Warehouses = await dbContext.Warehouses.AsNoTracking().OrderBy(x => x.Code).ToListAsync(cancellationToken),
            SampleTypes = await dbContext.SampleTypes.AsNoTracking().OrderBy(x => x.Name).ToListAsync(cancellationToken),
            EnvironmentName = environment.EnvironmentName,
            DatabaseProvider = configuration["DATABASE_PROVIDER"] ?? configuration["Database:Provider"] ?? "Default"
        };
    }

    public async Task<(DataCleanupPreviewViewModel Preview, string? Error)> ExecuteAsync(DataCleanupFilterForm filter, string changedByEmail, CancellationToken cancellationToken)
    {
        var preview = await PreviewAsync(filter, cancellationToken);
        if (!string.Equals(filter.ConfirmationText?.Trim(), RequiredConfirmation, StringComparison.Ordinal))
        {
            return (preview, $"Type {RequiredConfirmation} to confirm cleanup.");
        }

        if (preview.SamplesAffected == 0 && preview.ReceiptsAffected == 0)
        {
            return (preview, "No matching data was found for the selected filters.");
        }

        var selectedSampleIdsQuery = BuildSampleQuery(filter, false).Select(sample => sample.Id);
        if (string.Equals(filter.CleanupMode, "Hard", StringComparison.OrdinalIgnoreCase)
            && !filter.IncludePhotoMetadata
            && await dbContext.QcPhotos.AsNoTracking().AnyAsync(x => x.QcSampleId != null && selectedSampleIdsQuery.Contains(x.QcSampleId.Value), cancellationToken))
        {
            return (preview, "Hard purge found sample photo metadata. Select Count/delete photo metadata, or use Soft cleanup.");
        }

        var sampleIds = await BuildSampleQuery(filter, tracking: false).Select(x => x.Id).ToListAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var userId = await dbContext.Users.Where(x => x.Email == changedByEmail).Select(x => (int?)x.Id).SingleOrDefaultAsync(cancellationToken);

        if (string.Equals(filter.CleanupMode, "Hard", StringComparison.OrdinalIgnoreCase))
        {
            await HardPurgeAsync(sampleIds, changedByEmail, cancellationToken);
        }
        else
        {
            var samples = await dbContext.QcSamples.Where(x => sampleIds.Contains(x.Id)).ToListAsync(cancellationToken);
            foreach (var sample in samples)
            {
                sample.IsDeleted = true;
                sample.DeletedAt = now;
                sample.DeletedByUserId = userId;
                sample.DeleteReason = string.IsNullOrWhiteSpace(filter.Reason) ? "Admin data cleanup" : filter.Reason.Trim();
                sample.UpdatedAt = now;
            }
        }

        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = string.Equals(filter.CleanupMode, "Hard", StringComparison.OrdinalIgnoreCase) ? "hard-purge" : "soft-cleanup",
            EntityName = "test-data-cleanup",
            EntityKey = filter.CropYear?.ToString() ?? "all-crop-years",
            UserId = userId,
            BeforeValuesJson = null,
            AfterValuesJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                filter.CropYear,
                filter.AllCropYears,
                filter.FromDate,
                filter.ToDate,
                filter.WarehouseId,
                filter.SampleTypeId,
                filter.ReceiptId,
                filter.CleanupMode,
                preview.ReceiptsAffected,
                preview.SamplesAffected,
                preview.FruitRowsAffected,
                preview.PhotoRecordsAffected,
                preview.EmailLogsAffected
            }),
            SourceApplication = "Web",
            CreatedAt = now
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return (preview, null);
    }

    private async Task<DataCleanupPreviewViewModel> PreviewAsync(DataCleanupFilterForm filter, CancellationToken cancellationToken)
    {
        var sampleIds = await BuildSampleQuery(filter, tracking: false).Select(x => x.Id).ToListAsync(cancellationToken);
        var receiptIds = await BuildReceiptQuery(filter, tracking: false).Select(x => x.Id).ToListAsync(cancellationToken);
        var fruitRows = await dbContext.QcFruitReadings.AsNoTracking().CountAsync(x => sampleIds.Contains(x.QcSampleId), cancellationToken);
        var photoRecords = filter.IncludePhotoMetadata
            ? await dbContext.QcPhotos.AsNoTracking().CountAsync(x => x.QcSampleId != null && sampleIds.Contains(x.QcSampleId.Value), cancellationToken)
            : 0;
        var emailLogs = await dbContext.QcSummaryEmailLogs.AsNoTracking().CountAsync(x => x.QcSampleId != null && sampleIds.Contains(x.QcSampleId.Value), cancellationToken);

        return new DataCleanupPreviewViewModel
        {
            ReceiptsAffected = receiptIds.Count,
            SamplesAffected = sampleIds.Count,
            FruitRowsAffected = fruitRows,
            PhotoRecordsAffected = photoRecords,
            EmailLogsAffected = emailLogs,
            DriveFilesAffected = 0,
            IsProduction = environment.IsProduction(),
            IsAllCropYears = filter.AllCropYears
        };
    }

    private IQueryable<Receipt> BuildReceiptQuery(DataCleanupFilterForm filter, bool tracking)
    {
        var query = tracking ? dbContext.Receipts.AsQueryable() : dbContext.Receipts.AsNoTracking();
        query = query.Where(x => !x.IsDeleted);
        if (!filter.AllCropYears && filter.CropYear is not null) query = query.Where(x => x.CropYear == filter.CropYear);
        if (filter.FromDate is not null) query = query.Where(x => x.ReceivedAt >= new DateTimeOffset(filter.FromDate.Value));
        if (filter.ToDate is not null) query = query.Where(x => x.ReceivedAt <= new DateTimeOffset(filter.ToDate.Value.Date.AddDays(1).AddTicks(-1)));
        if (filter.WarehouseId is not null) query = query.Where(x => x.WarehouseId == filter.WarehouseId);
        if (!string.IsNullOrWhiteSpace(filter.ReceiptId)) query = query.Where(x => x.CompuTechReceiptId.Contains(filter.ReceiptId.Trim()));
        return query;
    }

    private IQueryable<QcSample> BuildSampleQuery(DataCleanupFilterForm filter, bool tracking)
    {
        var receiptQuery = BuildReceiptQuery(filter, tracking: false).Select(x => x.Id);
        var query = tracking ? dbContext.QcSamples.AsQueryable() : dbContext.QcSamples.AsNoTracking();
        query = query.Where(x => x.ReceiptId != null && receiptQuery.Contains(x.ReceiptId.Value));
        if (!filter.IncludeDeletedSamples) query = query.Where(x => !x.IsDeleted);
        if (filter.SampleTypeId is not null) query = query.Where(x => x.SampleTypeId == filter.SampleTypeId);
        if (!filter.IncludeEmailedSamples) query = query.Where(x => x.EmailStatus != "Sent");
        return query;
    }

    private async Task HardPurgeAsync(IReadOnlyList<long> sampleIds, string changedByEmail, CancellationToken cancellationToken)
    {
        var readingIds = await dbContext.QcFruitReadings.Where(x => sampleIds.Contains(x.QcSampleId)).Select(x => x.Id).ToListAsync(cancellationToken);
        dbContext.QcFruitDefects.RemoveRange(dbContext.QcFruitDefects.Where(x => readingIds.Contains(x.QcFruitReadingId)));
        dbContext.QcFruitReadings.RemoveRange(dbContext.QcFruitReadings.Where(x => sampleIds.Contains(x.QcSampleId)));
        dbContext.QcPhotos.RemoveRange(dbContext.QcPhotos.Where(x => x.QcSampleId != null && sampleIds.Contains(x.QcSampleId.Value)));
        dbContext.QcSummaryEmailLogs.RemoveRange(dbContext.QcSummaryEmailLogs.Where(x => x.QcSampleId != null && sampleIds.Contains(x.QcSampleId.Value)));
        dbContext.QcSamples.RemoveRange(dbContext.QcSamples.Where(x => sampleIds.Contains(x.Id)));
        await Task.CompletedTask;
    }
}
