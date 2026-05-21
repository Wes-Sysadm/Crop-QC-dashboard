using CropQc.Api.Dtos;
using CropQc.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Api.Services;

public interface IQcPhotoService
{
    Task<(QcPhotoDto? Photo, string? Error)> CreateAsync(CreateQcPhotoRequest request, CancellationToken cancellationToken);
}

public sealed class QcPhotoService(CropQcDbContext dbContext, IAuditService auditService) : IQcPhotoService
{
    public async Task<(QcPhotoDto? Photo, string? Error)> CreateAsync(CreateQcPhotoRequest request, CancellationToken cancellationToken)
    {
        if ((request.ReceiptId is null && request.QcSampleId is null) || (request.ReceiptId is not null && request.QcSampleId is not null))
        {
            return (null, "Photo metadata must attach to exactly one parent: ReceiptId or QcSampleId.");
        }

        if (string.IsNullOrWhiteSpace(request.PhotoType)
            || string.IsNullOrWhiteSpace(request.PhotoSource)
            || string.IsNullOrWhiteSpace(request.FileName)
            || string.IsNullOrWhiteSpace(request.ContentType)
            || string.IsNullOrWhiteSpace(request.SharePointDriveId)
            || string.IsNullOrWhiteSpace(request.SharePointItemId))
        {
            return (null, "PhotoType, PhotoSource, FileName, ContentType, SharePointDriveId, and SharePointItemId are required.");
        }

        var photo = new QcPhoto
        {
            ReceiptId = request.ReceiptId,
            QcSampleId = request.QcSampleId,
            PhotoType = request.PhotoType,
            PhotoSource = request.PhotoSource,
            FileName = request.FileName,
            ContentType = request.ContentType,
            FileSizeBytes = request.FileSizeBytes,
            SharePointDriveId = request.SharePointDriveId,
            SharePointItemId = request.SharePointItemId,
            WebUrl = request.WebUrl,
            CapturedByUserId = request.CapturedByUserId,
            CapturedAt = request.CapturedAt ?? DateTimeOffset.UtcNow
        };

        dbContext.QcPhotos.Add(photo);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditService.RecordAsync("Create", nameof(QcPhoto), photo.Id.ToString(), afterValuesJson: "Photo metadata created; file binary not stored in SQL.", cancellationToken: cancellationToken);
        return (ToDto(photo), null);
    }

    public static QcPhotoDto ToDto(QcPhoto photo) => new(
        photo.Id,
        photo.ReceiptId,
        photo.QcSampleId,
        photo.PhotoType,
        photo.PhotoSource,
        photo.FileName,
        photo.ContentType,
        photo.FileSizeBytes,
        photo.SharePointDriveId,
        photo.SharePointItemId,
        photo.WebUrl,
        photo.CapturedByUserId,
        photo.CapturedAt);
}
