using CropQc.Data.Entities;
using CropQc.Shared.Storage;
using CropQc.Web.Models;
using System.Net;
using System.Text;

namespace CropQc.Web.Services;

public interface IQcSummaryEmailComposer
{
    Task<QcEmailContent> ComposeAsync(QcSample sample, ReadinessViewModel readiness, bool isOverride, string? overrideReason, CancellationToken cancellationToken);
}

public sealed record QcEmailContent(string Subject, string HtmlBody, string TextBody, IReadOnlyList<QcEmailInlineImage> InlineImages);

public sealed record QcEmailInlineImage(string ContentId, string FileName, string ContentType, byte[] Bytes, string AltText);

public sealed class QcSummaryEmailComposer(
    IFileStorageService fileStorageService,
    IQcPhotoRequirementPolicy photoRequirementPolicy,
    ILogger<QcSummaryEmailComposer> logger) : IQcSummaryEmailComposer
{
    public async Task<QcEmailContent> ComposeAsync(QcSample sample, ReadinessViewModel readiness, bool isOverride, string? overrideReason, CancellationToken cancellationToken)
    {
        var completedRows = sample.FruitReadings.Where(x => x.IsCompleted).OrderBy(x => x.RowNumber).ToList();
        var requirements = photoRequirementPolicy.GetRequirements(sample.SampleType.Name);
        var photos = SelectEmailPhotos(sample, requirements);
        var inlineImages = new List<QcEmailInlineImage>();
        var imageReferences = new Dictionary<long, string>();

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
                    continue;
                }

                await using var memory = new MemoryStream();
                await stream.CopyToAsync(memory, cancellationToken);
                var contentId = $"cropqc-photo-{photo.Id}@cropqc";
                inlineImages.Add(new QcEmailInlineImage(
                    contentId,
                    string.IsNullOrWhiteSpace(photo.FileName) ? $"photo-{photo.Id}.jpg" : photo.FileName,
                    string.IsNullOrWhiteSpace(photo.ContentType) ? "image/jpeg" : photo.ContentType,
                    memory.ToArray(),
                    FriendlyPhotoName(photo.PhotoType)));
                imageReferences[photo.Id] = contentId;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not load QC email inline photo bytes. PhotoId: {PhotoId}. StorageProvider: {StorageProvider}.", photo.Id, photo.StorageProvider);
            }
        }

        var subject = BuildSubject(sample);
        var html = BuildHtml(sample, readiness, completedRows, requirements, photos, imageReferences, isOverride, overrideReason);
        var text = BuildText(sample, readiness, completedRows, requirements, photos, isOverride, overrideReason);
        return new QcEmailContent(subject, html, text, inlineImages);
    }

    private static IReadOnlyList<QcPhoto> SelectEmailPhotos(QcSample sample, IReadOnlyList<QcPhotoRequirement> requirements)
    {
        var result = new List<QcPhoto>();
        foreach (var requirement in requirements)
        {
            var source = requirement.ReceiptLevel ? sample.Receipt.Photos : sample.Photos;
            result.AddRange(source
                .Where(x => string.Equals(x.PhotoType, requirement.PhotoType, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.CapturedAt));
        }

        return result;
    }

    public static string BuildSubject(QcSample sample)
    {
        var parts = new[]
        {
            sample.Receipt.Warehouse.Code,
            "-",
            sample.Receipt.GrowerName,
            sample.Receipt.LotCode,
            sample.Receipt.FruitProfile.VarietyCode,
            sample.Receipt.Room.Code,
            sample.SampleType.Name,
            "On",
            sample.SampleTakenAt.LocalDateTime.ToString("MM/dd/yyyy")
        };

        return string.Join(' ', parts.Where(x => !string.IsNullOrWhiteSpace(x))).Replace(" - ", " - ");
    }

    private static string BuildHtml(
        QcSample sample,
        ReadinessViewModel readiness,
        IReadOnlyList<QcFruitReading> completedRows,
        IReadOnlyList<QcPhotoRequirement> requirements,
        IReadOnlyList<QcPhoto> photos,
        IReadOnlyDictionary<long, string> imageReferences,
        bool isOverride,
        string? overrideReason)
    {
        var summary = BuildSummary(completedRows);
        var html = new StringBuilder();
        html.AppendLine("<!doctype html><html><body style=\"font-family:Arial,sans-serif;color:#1f2933;\">");
        html.AppendLine($"<h1>{Html(sample.GetDisplayReceiptId())} QC Summary</h1>");
        if (isOverride)
        {
            html.AppendLine($"<p style=\"color:#92400e;\"><strong>Override send:</strong> {Html(overrideReason ?? "")}</p>");
        }

        html.AppendLine("<table cellpadding=\"4\" cellspacing=\"0\" style=\"border-collapse:collapse;\">");
        AddInfoRow(html, "Sample type", sample.SampleType.Name);
        AddInfoRow(html, "Warehouse", sample.Receipt.Warehouse.Code);
        AddInfoRow(html, "Room", sample.Receipt.Room.Code);
        AddInfoRow(html, "Receipt ID", sample.GetDisplayReceiptId());
        AddInfoRow(html, "Grower", sample.Receipt.GrowerName);
        AddInfoRow(html, "Lot", sample.Receipt.LotCode);
        AddInfoRow(html, "Variety", sample.Receipt.FruitProfile.VarietyCode);
        AddInfoRow(html, "Sample date/time", sample.SampleTakenAt.LocalDateTime.ToString("g"));
        AddInfoRow(html, "Inspector", sample.TakenByUser?.DisplayName ?? sample.TakenByUser?.Email ?? "");
        html.AppendLine("</table>");

        html.AppendLine("<h2>Summary</h2>");
        html.AppendLine("<table cellpadding=\"6\" cellspacing=\"0\" style=\"border-collapse:collapse;border:1px solid #cbd5e1;\">");
        AddInfoRow(html, "Sample size", readiness.CompletedFruitCount.ToString());
        AddInfoRow(html, "Average pressure lbs", Format(summary.AveragePressure));
        AddInfoRow(html, "Average starch", Format(summary.AverageStarch));
        AddInfoRow(html, "Average weight grams", Format(summary.AverageWeight));
        AddInfoRow(html, "Grade summary", summary.GradeSummary);
        AddInfoRow(html, "Defect summary", summary.DefectSummary);
        AddInfoRow(html, "Size/status summary", summary.SizeSummary);
        if (!string.IsNullOrWhiteSpace(sample.Notes)) AddInfoRow(html, "Notes", sample.Notes);
        html.AppendLine("</table>");

        html.AppendLine("<h2>Fruit Overview</h2>");
        html.AppendLine("<table cellpadding=\"5\" cellspacing=\"0\" style=\"border-collapse:collapse;border:1px solid #cbd5e1;\">");
        html.AppendLine("<thead><tr><th>Row</th><th>P1 lbs</th><th>P2 lbs</th><th>Avg lbs</th><th>Weight g</th><th>Grade</th><th>Starch</th><th>Size</th><th>Defects</th><th>Notes</th></tr></thead><tbody>");
        foreach (var row in completedRows)
        {
            var defects = row.Defects.Select(x => x.DefectType.Name).OrderBy(x => x).ToList();
            var notes = string.Join("; ", row.Defects.Select(x => x.Notes).Where(x => !string.IsNullOrWhiteSpace(x)));
            html.AppendLine("<tr>" +
                Cell(row.RowNumber.ToString()) +
                Cell(Format(row.Pressure1Lbs)) +
                Cell(Format(row.Pressure2Lbs)) +
                Cell(Format(Average(row.Pressure1Lbs, row.Pressure2Lbs))) +
                Cell(Format(row.WeightGrams)) +
                Cell(row.Grade?.Code ?? "") +
                Cell(row.StarchScaleValue?.Value.ToString("0.0") ?? "") +
                Cell(row.SizeCategory?.ToString() ?? row.SizeStatus) +
                Cell(defects.Count == 0 ? "" : string.Join(", ", defects)) +
                Cell(notes) +
                "</tr>");
        }
        html.AppendLine("</tbody></table>");

        html.AppendLine("<h2>Photos</h2>");
        foreach (var requirement in requirements)
        {
            var group = photos.Where(x => string.Equals(x.PhotoType, requirement.PhotoType, StringComparison.OrdinalIgnoreCase)).ToList();
            if (group.Count == 0)
            {
                continue;
            }

            html.AppendLine($"<h3>{Html(requirement.FriendlyName)}</h3>");
            foreach (var photo in group)
            {
                if (imageReferences.TryGetValue(photo.Id, out var contentId))
                {
                    html.AppendLine($"<p><img src=\"cid:{Html(contentId)}\" alt=\"{Html(requirement.FriendlyName)}\" style=\"max-width:520px;height:auto;\" /></p>");
                }
                else if (!string.IsNullOrWhiteSpace(photo.WebUrl))
                {
                    html.AppendLine($"<p><a href=\"{Html(photo.WebUrl)}\">{Html(photo.FileName)}</a></p>");
                }
            }
        }

        html.AppendLine("</body></html>");
        return html.ToString();
    }

    private static string BuildText(
        QcSample sample,
        ReadinessViewModel readiness,
        IReadOnlyList<QcFruitReading> completedRows,
        IReadOnlyList<QcPhotoRequirement> requirements,
        IReadOnlyList<QcPhoto> photos,
        bool isOverride,
        string? overrideReason)
    {
        var summary = BuildSummary(completedRows);
        var text = new StringBuilder();
        text.AppendLine($"{sample.GetDisplayReceiptId()} QC Summary");
        text.AppendLine($"Sample type: {sample.SampleType.Name}");
        text.AppendLine($"Warehouse/Room: {sample.Receipt.Warehouse.Code} / {sample.Receipt.Room.Code}");
        text.AppendLine($"Grower/Lot/Variety: {sample.Receipt.GrowerName} / {sample.Receipt.LotCode} / {sample.Receipt.FruitProfile.VarietyCode}");
        if (isOverride) text.AppendLine($"Override reason: {overrideReason}");
        text.AppendLine();
        text.AppendLine($"Summary: sample size {readiness.CompletedFruitCount}; avg pressure {Format(summary.AveragePressure)} lbs; avg starch {Format(summary.AverageStarch)}; avg weight {Format(summary.AverageWeight)} g; grades {summary.GradeSummary}; defects {summary.DefectSummary}; size/status {summary.SizeSummary}");
        text.AppendLine();
        text.AppendLine("Fruit Overview");
        foreach (var row in completedRows)
        {
            var defects = string.Join(", ", row.Defects.Select(x => x.DefectType.Name).OrderBy(x => x));
            text.AppendLine($"Row {row.RowNumber}: P1 {Format(row.Pressure1Lbs)} lbs, P2 {Format(row.Pressure2Lbs)} lbs, Avg {Format(Average(row.Pressure1Lbs, row.Pressure2Lbs))} lbs, Weight {Format(row.WeightGrams)} g, Grade {row.Grade?.Code}, Starch {row.StarchScaleValue?.Value:0.0}, Size {row.SizeCategory}, Defects {defects}");
        }
        text.AppendLine();
        text.AppendLine("Photo sections:");
        foreach (var requirement in requirements)
        {
            var count = photos.Count(x => string.Equals(x.PhotoType, requirement.PhotoType, StringComparison.OrdinalIgnoreCase));
            text.AppendLine($"- {requirement.FriendlyName}: {count} photo(s)");
        }
        return text.ToString();
    }

    private static QcSummaryStats BuildSummary(IReadOnlyList<QcFruitReading> rows)
    {
        var pressures = rows.Select(x => Average(x.Pressure1Lbs, x.Pressure2Lbs)).Where(x => x is not null).Select(x => x!.Value).ToList();
        var starch = rows.Select(x => x.StarchScaleValue?.Value).Where(x => x is not null).Select(x => x!.Value).ToList();
        var weights = rows.Select(x => x.WeightGrams).Where(x => x is not null).Select(x => x!.Value).ToList();
        return new QcSummaryStats(
            pressures.Count == 0 ? null : pressures.Average(),
            starch.Count == 0 ? null : starch.Average(),
            weights.Count == 0 ? null : weights.Average(),
            Summarize(rows.Select(x => x.Grade?.Code).Where(x => !string.IsNullOrWhiteSpace(x))!),
            Summarize(rows.SelectMany(x => x.Defects).Select(x => x.DefectType.Name)),
            Summarize(rows.Select(x => x.SizeCategory?.ToString() ?? x.SizeStatus).Where(x => !string.IsNullOrWhiteSpace(x))));
    }

    private static string Summarize(IEnumerable<string> values)
    {
        var summary = values.GroupBy(x => x).OrderBy(x => x.Key).Select(x => $"{x.Key}: {x.Count()}").ToList();
        return summary.Count == 0 ? "None" : string.Join(", ", summary);
    }

    private static void AddInfoRow(StringBuilder builder, string label, string value) =>
        builder.AppendLine($"<tr><th style=\"text-align:left;border:1px solid #cbd5e1;background:#f8fafc;\">{Html(label)}</th><td style=\"border:1px solid #cbd5e1;\">{Html(value)}</td></tr>");

    private static string Cell(string value) => $"<td style=\"border:1px solid #cbd5e1;\">{Html(value)}</td>";
    private static string Html(string? value) => WebUtility.HtmlEncode(value ?? "");
    private static string Format(decimal? value) => value?.ToString("0.##") ?? "";
    private static decimal? Average(decimal? first, decimal? second) => first is null || second is null ? null : decimal.Round((first.Value + second.Value) / 2m, 2);
    private static string FriendlyPhotoName(string photoType) => photoType switch
    {
        "BinTruck" => "Truck photo",
        "SampleBeforeCutting" => "Whole sample",
        "CutFruit" => "Cut apples",
        "FruitAfterStarch" => "Starch apples",
        _ => photoType
    };

    private sealed record QcSummaryStats(decimal? AveragePressure, decimal? AverageStarch, decimal? AverageWeight, string GradeSummary, string DefectSummary, string SizeSummary);
}
