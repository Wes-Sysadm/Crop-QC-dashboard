using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Storage;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace CropQc.Web.Services;

public interface IFieldSampleReportService
{
    Task<(FieldSampleReportPreviewViewModel? Preview, string? Error)> PreviewAsync(long sampleId, ClaimsPrincipal user, CancellationToken cancellationToken);
    Task<string?> SendAsync(long sampleId, ClaimsPrincipal user, CancellationToken cancellationToken);
}

public sealed class FieldSampleReportService(
    CropQcDbContext dbContext,
    IFieldSampleService fieldSampleService,
    IUserAccessService userAccessService,
    IQcEmailRecipientResolver recipientResolver,
    IQcEmailSender emailSender,
    IFileStorageService fileStorageService,
    ILogger<FieldSampleReportService> logger) : IFieldSampleReportService
{
    private const string FieldSampleTypeName = "Field Sample";
    private const int MaxInlineImageBytes = 1_500_000;
    private const int MaxTotalInlineImageBytes = 12_000_000;
    private const int MaxSourceImageBytes = 25_000_000;
    private static readonly IBusinessTimeService ReportTime = new PacificBusinessTimeService(new CropQc.Shared.Time.SystemClock());

    public async Task<(FieldSampleReportPreviewViewModel? Preview, string? Error)> PreviewAsync(long sampleId, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!await userAccessService.HasAccessAsync(user, ApplicationAreas.FieldSamples, PageAccessLevel.View, cancellationToken))
        {
            return (null, "Field Samples access is required.");
        }

        var prepared = await PrepareAsync(sampleId, user, preview: true, cancellationToken);
        if (prepared.Error is not null || prepared.Content is null || prepared.Detail is null || prepared.Recipients is null)
        {
            return (null, prepared.Error ?? "Field Sample report could not be prepared.");
        }

        return (new FieldSampleReportPreviewViewModel
        {
            SampleId = sampleId,
            Subject = prepared.Content.Subject,
            Recipients = prepared.Recipients.Header,
            HtmlBody = ReplaceCidImagesWithDataUrls(prepared.Content.HtmlBody, prepared.Content.InlineImages),
            CanSend = prepared.Detail.CanSend,
            IsResend = prepared.Detail.SendHistory.Any(x => string.Equals(x.Status, "Sent", StringComparison.OrdinalIgnoreCase)),
            ChangedSinceLastSend = prepared.Detail.ChangedSinceLastSend,
            MissingItems = prepared.Detail.CompletionMissingItems
        }, null);
    }

    public async Task<string?> SendAsync(long sampleId, ClaimsPrincipal user, CancellationToken cancellationToken)
    {
        if (!await userAccessService.HasAccessAsync(user, ApplicationAreas.FieldSamples, PageAccessLevel.Edit, cancellationToken))
        {
            return "Field Samples Edit access is required.";
        }

        var prepared = await PrepareAsync(sampleId, user, preview: false, cancellationToken);
        if (prepared.Error is not null || prepared.Sample is null || prepared.Content is null || prepared.Detail is null || prepared.Recipients is null)
        {
            return prepared.Error ?? "Field Sample report could not be prepared.";
        }

        if (prepared.Detail.CompletionMissingItems.Count > 0)
        {
            return $"Field Sample report cannot be sent: {string.Join("; ", prepared.Detail.CompletionMissingItems)}";
        }
        if (!prepared.Detail.CanSend)
        {
            return string.Equals(prepared.Detail.LifecycleStatus, "Sent", StringComparison.OrdinalIgnoreCase)
                ? "This Field Sample report was already sent and the sample has not changed."
                : "Mark the Field Sample complete before sending its report.";
        }

        var sender = await FindUserAsync(user, cancellationToken);
        if (sender is null)
        {
            return "A logged-in active user is required to send the Field Sample report.";
        }

        var previousStatus = prepared.Sample.Status;
        var previousEmailStatus = prepared.Sample.EmailStatus;
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-10);
        int claimed;
        if (dbContext.Database.IsRelational())
        {
            claimed = await dbContext.QcSamples
                .Where(x => x.Id == sampleId && (x.EmailStatus != "Sending" || x.UpdatedAt < cutoff))
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(x => x.EmailStatus, "Sending")
                    .SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow), cancellationToken);
        }
        else
        {
            var tracked = await dbContext.QcSamples.SingleAsync(x => x.Id == sampleId, cancellationToken);
            if (string.Equals(tracked.EmailStatus, "Sending", StringComparison.OrdinalIgnoreCase) && tracked.UpdatedAt >= cutoff)
            {
                claimed = 0;
            }
            else
            {
                tracked.EmailStatus = "Sending";
                tracked.UpdatedAt = DateTimeOffset.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
                claimed = 1;
            }
        }
        if (claimed == 0)
        {
            return "A Field Sample report send is already in progress. Wait for it to finish before retrying.";
        }

        dbContext.ChangeTracker.Clear();
        var isResend = await dbContext.QcSummaryEmailLogs.AsNoTracking()
            .AnyAsync(x => x.QcSampleId == sampleId && x.Status == "Sent", cancellationToken);
        var message = new QcEmailMessage(
            sender.Email,
            prepared.Recipients.Header,
            prepared.Sample.TakenByUser?.Email,
            prepared.Content.Subject,
            prepared.Content.TextBody,
            prepared.Content.HtmlBody,
            prepared.Content.InlineImages);
        QcEmailSendResult sendResult;
        try
        {
            sendResult = await emailSender.SendAsync(sender, message, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Field Sample report sender failed unexpectedly. SampleId: {SampleId}; SenderUserId: {SenderUserId}.", sampleId, sender.Id);
            sendResult = QcEmailSendResult.Failed("The email provider did not complete the request. Retry after checking Gmail connectivity.");
        }
        var now = DateTimeOffset.UtcNow;
        var trackedSample = await dbContext.QcSamples.SingleAsync(x => x.Id == sampleId, cancellationToken);
        trackedSample.Status = sendResult.Success ? "Sent" : previousStatus;
        trackedSample.EmailStatus = sendResult.Success ? "Sent" : previousEmailStatus;
        trackedSample.UpdatedAt = now;

        dbContext.QcSummaryEmailLogs.Add(new QcSummaryEmailLog
        {
            ReceiptId = null,
            QcSampleId = sampleId,
            FromAddress = sender.Email,
            ToAddress = prepared.Recipients.Header,
            ReplyToAddress = prepared.Sample.TakenByUser?.Email,
            Subject = prepared.Content.Subject,
            Status = sendResult.Success ? "Sent" : "Failed",
            MessageId = sendResult.MessageId,
            SentByUserId = sender.Id,
            SentAt = sendResult.Success ? now : null,
            IsResend = isResend,
            MissingItemsSnapshot = string.Join(Environment.NewLine, prepared.Detail.CompletionMissingItems),
            ReportSnapshotReference = sendResult.Success
                ? $"Gmail message id: {sendResult.MessageId ?? "(not returned)"}; inline images: {prepared.Content.InlineImages.Count}; 30-day block trend"
                : $"Send failed: {sendResult.Error}",
            CreatedAt = now
        });
        await AddAuditAsync(
            sendResult.Success ? (isResend ? "resend" : "send") : "send-failed",
            sampleId,
            sender,
            new
            {
                Status = sendResult.Success ? "Sent" : "Failed",
                Recipients = prepared.Recipients.Recipients,
                prepared.Recipients.ResolvedOrchardId,
                prepared.Recipients.OrchardCouldNotBeResolved,
                prepared.Recipients.OrchardHadNoConfiguredManager,
                SkippedInvalidRecipientCount = prepared.Recipients.SkippedInvalidAddresses.Count,
                InlineImageCount = prepared.Content.InlineImages.Count,
                IsResend = isResend,
                Failure = sendResult.Success ? null : sendResult.Error
            }, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        if (sendResult.Success)
        {
            return null;
        }
        return sendResult.ReconnectRequired
            ? "Gmail permission is required. Reconnect Google/Gmail, then retry; the Field Sample remains unchanged."
            : $"Field Sample report failed: {sendResult.Error}";
    }

    private async Task<PreparedReport> PrepareAsync(long sampleId, ClaimsPrincipal user, bool preview, CancellationToken cancellationToken)
    {
        var sample = await dbContext.QcSamples.AsNoTracking()
            .Include(x => x.SampleType)
            .Include(x => x.FieldSampleFruitProfile)
            .Include(x => x.CanonicalOrchardBlock).ThenInclude(x => x!.CanonicalOrchard)
            .Include(x => x.TakenByUser)
            .Include(x => x.QcStation).ThenInclude(x => x!.Warehouse)
            .Include(x => x.FruitReadings).ThenInclude(x => x.Grade)
            .Include(x => x.FruitReadings).ThenInclude(x => x.StarchScaleValue)
            .Include(x => x.FruitReadings).ThenInclude(x => x.Defects).ThenInclude(x => x.DefectType)
            .Include(x => x.Photos)
            .SingleOrDefaultAsync(x => x.Id == sampleId && !x.IsDeleted && x.SampleType.Name == FieldSampleTypeName, cancellationToken);
        if (sample is null)
        {
            return new PreparedReport(null, null, null, null, "Field Sample not found.");
        }

        var detail = await fieldSampleService.GetDetailAsync(sampleId, user, cancellationToken);
        if (detail.SampleId == 0)
        {
            return new PreparedReport(sample, detail, null, null, detail.DataWarning ?? "Field Sample could not be loaded.");
        }
        var recipients = await recipientResolver.ResolveForSampleAsync(sampleId, null, cancellationToken);
        var sender = await FindUserAsync(user, cancellationToken);
        var content = await ComposeAsync(sample, detail, sender, preview, cancellationToken);
        return new PreparedReport(sample, detail, recipients, content, null);
    }

    private async Task<QcEmailContent> ComposeAsync(QcSample sample, FieldSampleDetailViewModel detail, User? sender, bool preview, CancellationToken cancellationToken)
    {
        var photos = sample.Photos.Where(x => !x.IsDeleted).OrderBy(x => x.CapturedAt).ToList();
        var inlineImages = new List<QcEmailInlineImage>();
        var imageIds = new Dictionary<long, string>();
        var totalBytes = 0;
        foreach (var photo in photos)
        {
            var key = photo.FileId ?? photo.SharePointItemId;
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }
            try
            {
                await using var stream = await fileStorageService.OpenReadAsync(key, cancellationToken);
                if (stream is null)
                {
                    logger.LogWarning("Field Sample report skipped missing photo content. SampleId: {SampleId}; PhotoId: {PhotoId}.", sample.Id, photo.Id);
                    continue;
                }
                var prepared = await PrepareInlineImageAsync(photo, stream, Math.Min(MaxInlineImageBytes, MaxTotalInlineImageBytes - totalBytes), cancellationToken);
                if (prepared is null)
                {
                    logger.LogWarning("Field Sample report skipped an unreadable or oversized photo. SampleId: {SampleId}; PhotoId: {PhotoId}.", sample.Id, photo.Id);
                    continue;
                }
                var cid = $"field-sample-photo-{photo.Id}@cropqc";
                inlineImages.Add(new QcEmailInlineImage(cid, prepared.FileName, prepared.ContentType, prepared.Bytes, $"Field Sample photo {inlineImages.Count + 1}"));
                imageIds[photo.Id] = cid;
                totalBytes += prepared.Bytes.Length;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Field Sample report could not load photo content. SampleId: {SampleId}; PhotoId: {PhotoId}; StorageProvider: {StorageProvider}.", sample.Id, photo.Id, photo.StorageProvider);
            }
        }

        var orchard = sample.CanonicalOrchardBlock?.CanonicalOrchard?.OrchardName
            ?? sample.CanonicalOrchardBlock?.OrchardName
            ?? sample.FieldSampleGrowerName
            ?? "Unknown Orchard";
        var block = sample.CanonicalOrchardBlock?.CanonicalBlockName ?? sample.FieldSampleOriginalBlockName ?? "Unknown Block";
        var variety = sample.FieldSampleFruitProfile?.Name ?? "Unknown Variety";
        var terminology = FieldSampleCommodityTerminologyService.ForFruitType(sample.FieldSampleFruitProfile?.FruitType);
        var subject = $"Field Sample QC – {orchard} – {block} – {variety} – {ReportTime.FormatPacific(sample.SampleTakenAt, "MMMM d, yyyy", includeZone: false)}";
        var html = BuildHtml(sample, detail, photos, imageIds, orchard, block, variety, terminology, sender, preview);
        var text = BuildText(sample, detail, photos, orchard, block, variety, terminology, sender, preview);
        return new QcEmailContent(subject, html, text, inlineImages);
    }

    private static string BuildHtml(QcSample sample, FieldSampleDetailViewModel detail, IReadOnlyList<QcPhoto> photos, IReadOnlyDictionary<long, string> imageIds, string orchard, string block, string variety, FieldSampleCommodityTerminology terminology, User? sender, bool preview)
    {
        var rows = sample.FruitReadings.Where(HasEnteredData).OrderBy(x => x.RowNumber).ToList();
        var html = new StringBuilder();
        html.AppendLine("<!doctype html><html><body style=\"font-family:Arial,sans-serif;color:#1f2933;\">");
        html.AppendLine("<h1>Field Sample QC Report</h1>");
        if (detail.ChangedSinceLastSend) html.AppendLine("<p style=\"color:#92400e;\"><strong>Corrected report:</strong> this sample changed after its most recent send.</p>");
        html.AppendLine("<h2>Identification</h2><table cellpadding=\"4\" cellspacing=\"0\" style=\"border-collapse:collapse;\">");
        AddInfo(html, "Orchard", orchard);
        AddInfo(html, "Grower", sample.FieldSampleGrowerName ?? orchard);
        AddInfo(html, "Grower number", sample.FieldSampleGrowerNumber ?? "");
        AddInfo(html, "Canonical block", block);
        if (!string.Equals(block, sample.FieldSampleOriginalBlockName, StringComparison.OrdinalIgnoreCase)) AddInfo(html, "Original block entry", sample.FieldSampleOriginalBlockName ?? "");
        AddInfo(html, "Variety", variety);
        AddInfo(html, "Sample date/time", ReportTime.FormatPacific(sample.SampleTakenAt));
        AddInfo(html, "Sampling location", sample.QcStation?.Warehouse?.Code ?? "Field");
        AddInfo(html, "Collector/creator", sample.TakenByUser?.DisplayName ?? sample.TakenByUser?.Email ?? "");
        AddInfo(html, "Completion status", detail.LifecycleStatus);
        AddInfo(html, preview ? "Report previewed by" : "Report sent by", sender?.DisplayName ?? sender?.Email ?? "");
        AddInfo(html, preview ? "Preview time" : "Report sent time", ReportTime.FormatPacific(ReportTime.UtcNow));
        html.AppendLine("</table>");

        html.AppendLine("<h2>Fruit Detail</h2><table cellpadding=\"5\" cellspacing=\"0\" style=\"border-collapse:collapse;border:1px solid #cbd5e1;width:100%;\"><thead><tr><th>Fruit</th><th>Weight g</th><th>Size</th><th>P1 lb</th><th>P2 lb</th><th>Avg lb</th><th>Starch</th><th>Grade</th><th>Defects</th></tr></thead><tbody>");
        foreach (var row in rows)
        {
            html.Append("<tr>");
            foreach (var value in new[] { row.RowNumber.ToString(), Format(row.WeightGrams), row.SizeCategory?.ToString() ?? "", Format(row.Pressure1Lbs), Format(row.Pressure2Lbs), Format(Average(row.Pressure1Lbs, row.Pressure2Lbs)), StarchText(sample, row, photos), row.Grade?.Code ?? "", FruitDefects(row) })
            {
                html.Append($"<td style=\"border:1px solid #cbd5e1;white-space:nowrap;\">{Html(value)}</td>");
            }
            html.AppendLine("</tr>");
        }
        html.AppendLine("</tbody></table>");

        html.AppendLine("<h2>Sample Photos</h2>");
        if (photos.Count == 0) html.AppendLine("<p>No photos were attached.</p>");
        var photoNumber = 0;
        foreach (var photo in photos)
        {
            photoNumber++;
            if (imageIds.TryGetValue(photo.Id, out var cid))
            {
                html.AppendLine($"<figure style=\"display:inline-block;margin:8px;vertical-align:top;\"><img src=\"cid:{Html(cid)}\" alt=\"Field Sample photo {photoNumber}\" style=\"max-width:320px;max-height:240px;width:auto;height:auto;\"/><figcaption>Photo {photoNumber}: {Html(FriendlyPhotoType(photo.PhotoType, terminology))}</figcaption></figure>");
            }
            else
            {
                html.AppendLine($"<p>Photo {photoNumber}: {Html(FriendlyPhotoType(photo.PhotoType, terminology))} (image unavailable)</p>");
            }
        }

        AppendTrend(html, detail);
        if (!string.IsNullOrWhiteSpace(sample.Notes)) html.AppendLine($"<p><strong>Notes:</strong> {Html(sample.Notes)}</p>");
        AppendCurrentSummary(html, sample, detail, photos);
        html.AppendLine("</body></html>");
        return html.ToString();
    }

    private static string BuildText(QcSample sample, FieldSampleDetailViewModel detail, IReadOnlyList<QcPhoto> photos, string orchard, string block, string variety, FieldSampleCommodityTerminology terminology, User? sender, bool preview)
    {
        var text = new StringBuilder();
        text.AppendLine("Field Sample QC Report");
        text.AppendLine($"Orchard: {orchard}");
        text.AppendLine($"Grower: {sample.FieldSampleGrowerName ?? orchard}");
        text.AppendLine($"Grower number: {sample.FieldSampleGrowerNumber}");
        text.AppendLine($"Canonical block: {block}");
        text.AppendLine($"Variety: {variety}");
        text.AppendLine($"Sample date/time: {ReportTime.FormatPacific(sample.SampleTakenAt)}");
        text.AppendLine($"Status: {detail.LifecycleStatus}");
        text.AppendLine($"{(preview ? "Previewed" : "Sent")} by: {sender?.DisplayName ?? sender?.Email}");
        text.AppendLine();
        text.AppendLine("Fruit Detail");
        foreach (var row in sample.FruitReadings.Where(HasEnteredData).OrderBy(x => x.RowNumber))
        {
            text.AppendLine($"Fruit {row.RowNumber}: Weight {Format(row.WeightGrams)} g; Size {row.SizeCategory}; P1 {Format(row.Pressure1Lbs)} lb; P2 {Format(row.Pressure2Lbs)} lb; Avg {Format(Average(row.Pressure1Lbs, row.Pressure2Lbs))} lb; Starch {StarchText(sample, row, photos)}; Grade {row.Grade?.Code}; Defects {FruitDefects(row)}");
        }
        text.AppendLine();
        text.AppendLine("Same-block 30-day trend");
        var trend = detail.BlockTrend?.Points ?? detail.Trend;
        if (trend.Count <= 1) text.AppendLine("This is the first available sample for the confirmed block in the last 30 days.");
        foreach (var point in trend.OrderBy(x => x.SampleTakenAt).ThenBy(x => x.SampleId))
        {
            text.AppendLine($"{ReportTime.FormatPacific(point.SampleTakenAt)}{(point.SampleId == sample.Id ? " (current)" : "")}: Weight {Format(point.Summary.AverageWeightGrams)} g; Size {AverageSize(point.SizeDistribution)}; Average Pressure {Format(point.Summary.AveragePressureLbs)} lb; Starch {Format(point.Summary.AverageStarch)}; Grades {Distribution(point.Summary.GradeDistribution)}; Defects {DefectSummary(point.Summary)}; {DefectDistribution(point.Summary)}");
        }
        text.AppendLine();
        text.AppendLine("Final Sample Summary");
        text.AppendLine($"Meaningful fruit rows: {detail.CurrentSummary.EnteredFruitCount}");
        text.AppendLine($"Average weight: {Format(detail.CurrentSummary.AverageWeightGrams, " g")}");
        text.AppendLine($"Average Pressure: {Format(detail.CurrentSummary.AveragePressureLbs, " lb")}");
        text.AppendLine($"Average starch: {StarchSummaryText(sample, detail.CurrentSummary.AverageStarch, photos)}");
        text.AppendLine($"Defect inspection: {DefectSummary(detail.CurrentSummary)}");
        text.AppendLine($"Defect distribution: {DefectDistribution(detail.CurrentSummary)}");
        text.AppendLine($"Photos: {photos.Count}");
        return text.ToString();
    }

    private static void AppendCurrentSummary(StringBuilder html, QcSample sample, FieldSampleDetailViewModel detail, IReadOnlyList<QcPhoto> photos)
    {
        html.AppendLine("<h2>Final Sample Summary</h2><table cellpadding=\"4\" cellspacing=\"0\" style=\"border-collapse:collapse;\">");
        AddInfo(html, "Meaningful fruit rows", detail.CurrentSummary.EnteredFruitCount.ToString());
        AddInfo(html, "Average weight", Format(detail.CurrentSummary.AverageWeightGrams, " g"));
        AddInfo(html, "Average / representative size", AverageSize(detail.SizeDistribution));
        AddInfo(html, "Size distribution", Distribution(detail.SizeDistribution.Select(x => new FieldSampleDistributionPoint(x.Size.ToString(), x.Percentage))));
        AddInfo(html, "Average Pressure", Format(detail.CurrentSummary.AveragePressureLbs, " lb"));
        AddInfo(html, "Average starch", StarchSummaryText(sample, detail.CurrentSummary.AverageStarch, photos));
        AddInfo(html, "Starch distribution", Distribution(detail.CurrentSummary.StarchDistribution));
        AddInfo(html, "Grade distribution", Distribution(detail.CurrentSummary.GradeDistribution));
        AddInfo(html, "Defect inspection", DefectSummary(detail.CurrentSummary));
        AddInfo(html, "Defect distribution", DefectDistribution(detail.CurrentSummary));
        html.AppendLine("</table>");
    }

    private static void AppendTrend(StringBuilder html, FieldSampleDetailViewModel detail)
    {
        var trend = detail.BlockTrend?.Points ?? detail.Trend;
        html.AppendLine("<h2>Same-Block Trends — Last 30 Days</h2>");
        if (trend.Count <= 1) html.AppendLine("<p>This is the first available sample for the confirmed block in the last 30 days.</p>");
        html.AppendLine("<table cellpadding=\"5\" cellspacing=\"0\" style=\"border-collapse:collapse;border:1px solid #cbd5e1;width:100%;\"><thead><tr><th>Date</th><th>Fruit</th><th>Avg weight</th><th>Size</th><th>Average Pressure</th><th>Avg starch</th><th>Grades</th><th>Defects</th></tr></thead><tbody>");
        foreach (var point in trend.OrderBy(x => x.SampleTakenAt).ThenBy(x => x.SampleId))
        {
            var values = new[]
            {
                ReportTime.FormatPacific(point.SampleTakenAt) + (point.SampleId == detail.SampleId ? " (current)" : ""),
                point.Summary.EnteredFruitCount.ToString(),
                Format(point.Summary.AverageWeightGrams, " g"),
                AverageSize(point.SizeDistribution),
                Format(point.Summary.AveragePressureLbs, " lb"),
                Format(point.Summary.AverageStarch),
                Distribution(point.Summary.GradeDistribution),
                $"{DefectSummary(point.Summary)}; {DefectDistribution(point.Summary)}"
            };
            html.Append("<tr>");
            foreach (var value in values) html.Append($"<td style=\"border:1px solid #cbd5e1;\">{Html(value)}</td>");
            html.AppendLine("</tr>");
        }
        html.AppendLine("</tbody></table>");
    }

    private static async Task<PreparedInlineImage?> PrepareInlineImageAsync(QcPhoto photo, Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        if (maxBytes <= 0) return null;
        if (stream.CanSeek && stream.Length > MaxSourceImageBytes) return null;
        await using var source = new MemoryStream();
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0) break;
            if (source.Length + read > MaxSourceImageBytes) return null;
            await source.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        source.Position = 0;
        using var image = await Image.LoadAsync(source, cancellationToken);
        image.Mutate(x => x.AutoOrient().Resize(new ResizeOptions { Mode = ResizeMode.Max, Size = new Size(900, 700) }));
        image.Metadata.ExifProfile = null;
        image.Metadata.IccProfile = null;
        foreach (var quality in new[] { 78, 65, 50 })
        {
            await using var result = new MemoryStream();
            await image.SaveAsJpegAsync(result, new JpegEncoder { Quality = quality }, cancellationToken);
            if (result.Length <= maxBytes)
            {
                return new PreparedInlineImage($"{Path.GetFileNameWithoutExtension(photo.FileName)}.jpg", "image/jpeg", result.ToArray());
            }
        }
        return null;
    }

    private async Task<User?> FindUserAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var email = principal.FindFirstValue(ClaimTypes.Email)?.Trim();
        return string.IsNullOrWhiteSpace(email)
            ? null
            : await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(x => x.IsActive && x.Email == email, cancellationToken);
    }

    private async Task AddAuditAsync(string action, long sampleId, User sender, object after, CancellationToken cancellationToken)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityName = "field-sample-report",
            EntityKey = sampleId.ToString(),
            UserId = sender.Id,
            AfterValuesJson = JsonSerializer.Serialize(after),
            SourceApplication = "CropQc.Web",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await Task.CompletedTask;
    }

    private static string ReplaceCidImagesWithDataUrls(string html, IReadOnlyList<QcEmailInlineImage> images)
    {
        foreach (var image in images)
        {
            html = html.Replace($"cid:{image.ContentId}", $"data:{image.ContentType};base64,{Convert.ToBase64String(image.Bytes)}", StringComparison.Ordinal);
        }
        return html;
    }

    private static void AddInfo(StringBuilder html, string label, string value) =>
        html.AppendLine($"<tr><th align=\"left\" style=\"padding-right:18px;\">{Html(label)}</th><td>{Html(value)}</td></tr>");
    private static string Html(string? value) => WebUtility.HtmlEncode(value ?? "");
    private static string Format(decimal? value, string suffix = "") => value is null ? "" : $"{value:0.##}{suffix}";
    private static decimal? Average(decimal? first, decimal? second) => first is null && second is null ? null : first is null ? second : second is null ? first : decimal.Round((first.Value + second.Value) / 2m, 2);
    private static string Distribution(IEnumerable<FieldSampleDistributionPoint> points) => string.Join(", ", points.Select(x => $"{x.Label} {x.Percentage:0.#}%"));
    private static string AverageSize(IReadOnlyList<FieldSampleSizePoint> points)
    {
        if (points.Count == 0) return "";
        return (points.Sum(x => x.Size * x.Percentage) / points.Sum(x => x.Percentage)).ToString("0.#");
    }
    private static bool HasEnteredData(QcFruitReading row) => row.Pressure1Lbs is not null || row.Pressure2Lbs is not null || row.WeightGrams is not null || row.StarchScaleValueId is not null || row.SizeCategory is not null || row.GradeId is not null || row.DefectsInspected || row.Defects.Count > 0;

    private static string StarchText(QcSample sample, QcFruitReading row, IReadOnlyCollection<QcPhoto> photos) =>
        row.StarchScaleValue?.Value.ToString("0.0")
        ?? (IsPearWithStarchPhoto(sample, photos) ? "Not entered — see photo" : "");

    private static string StarchSummaryText(QcSample sample, decimal? value, IReadOnlyCollection<QcPhoto> photos) =>
        value?.ToString("0.##")
        ?? (IsPearWithStarchPhoto(sample, photos) ? "Not entered — see photo" : "");

    private static bool IsPearWithStarchPhoto(QcSample sample, IReadOnlyCollection<QcPhoto> photos) =>
        string.Equals(sample.FieldSampleFruitProfile?.FruitType, "Pear", StringComparison.OrdinalIgnoreCase)
        && photos.Any(x => !x.IsDeleted
            && QcPhotoRequirementPolicy.NormalizePhotoType(x.PhotoType)
                .Equals("FruitAfterStarch", StringComparison.OrdinalIgnoreCase));
    private static string FruitDefects(QcFruitReading row) => !row.DefectsInspected
        ? "Not inspected"
        : row.Defects.Count == 0
            ? "Inspected — none"
            : string.Join(", ", row.Defects.OrderBy(x => x.DefectType.Name).Select(x => string.IsNullOrWhiteSpace(x.Notes) ? x.DefectType.Name : $"{x.DefectType.Name}: {x.Notes}"));
    private static string DefectSummary(FieldSampleMetricSummary summary) => summary.DefectAffectedPercentage is null
        ? "Not inspected"
        : $"{summary.DefectAffectedFruitCount} of {summary.DefectInspectedFruitCount} inspected fruit affected ({summary.DefectAffectedPercentage:0.#}%)";
    private static string DefectDistribution(FieldSampleMetricSummary summary) => summary.DefectInspectedFruitCount == 0
        ? "Not inspected"
        : summary.DefectDistribution.Count == 0
            ? "No defects found"
            : string.Join(", ", summary.DefectDistribution.Select(x => $"{x.Defect} {x.FruitCount} ({x.PercentageOfInspectedFruit:0.#}%)"));
    private static string FriendlyPhotoType(string value, FieldSampleCommodityTerminology terminology) => QcPhotoRequirementPolicy.NormalizePhotoType(value) switch
    {
        "SampleBeforeCutting" => terminology.WholeSampleLabel,
        "CutFruit" => terminology.CutFruitLabel,
        "FruitAfterStarch" => $"Starch {terminology.Commodity}",
        _ => value
    };

    private sealed record PreparedReport(QcSample? Sample, FieldSampleDetailViewModel? Detail, QcEmailRecipientResolution? Recipients, QcEmailContent? Content, string? Error);
    private sealed record PreparedInlineImage(string FileName, string ContentType, byte[] Bytes);
}
