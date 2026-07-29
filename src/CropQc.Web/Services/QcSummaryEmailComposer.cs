using CropQc.Data.Entities;
using CropQc.Shared.Storage;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using System.Net;
using System.Text;

namespace CropQc.Web.Services;

public interface IQcSummaryEmailComposer
{
    Task<QcEmailContent> ComposeAsync(QcSample sample, ReadinessViewModel readiness, User? sendingUser, bool isOverride, string? overrideReason, CancellationToken cancellationToken);
}

public sealed record QcEmailContent(string Subject, string HtmlBody, string TextBody, IReadOnlyList<QcEmailInlineImage> InlineImages);

public sealed record QcEmailInlineImage(string ContentId, string FileName, string ContentType, byte[] Bytes, string AltText);

public sealed class QcSummaryEmailComposer(
    IFileStorageService fileStorageService,
    IQcPhotoRequirementPolicy photoRequirementPolicy,
    ILogger<QcSummaryEmailComposer> logger) : IQcSummaryEmailComposer
{
    public const int MaxInlineImageBytes = 1_500_000;
    public const int MaxTotalInlineImageBytes = 12_000_000;
    private const int MaxSourceImageBytes = 25_000_000;
    private const int InlineImageMaxWidth = 1200;
    private const int InlineImageMaxHeight = 900;
    private static readonly IBusinessTimeService ReportTime = new PacificBusinessTimeService(new CropQc.Shared.Time.SystemClock());

    public static string BuildBrowserPreviewHtml(QcEmailContent content)
    {
        var html = content.HtmlBody;
        foreach (var image in content.InlineImages)
        {
            var dataUrl = $"data:{image.ContentType};base64,{Convert.ToBase64String(image.Bytes)}";
            html = html.Replace($"cid:{image.ContentId}", dataUrl, StringComparison.Ordinal);
        }

        return html;
    }

    public async Task<QcEmailContent> ComposeAsync(QcSample sample, ReadinessViewModel readiness, User? sendingUser, bool isOverride, string? overrideReason, CancellationToken cancellationToken)
    {
        var enteredRows = sample.FruitReadings.Where(HasEnteredData).OrderBy(x => x.RowNumber).ToList();
        var requirements = photoRequirementPolicy.GetAvailablePhotoTypes(sample.SampleType.Name, sample.Receipt.FruitProfile.FruitType);
        var photos = SelectEmailPhotos(sample, requirements);
        var inlineImages = new List<QcEmailInlineImage>();
        var imageReferences = new Dictionary<long, string>();
        var linkedPhotoNotes = new Dictionary<long, string>();
        var totalInlineBytes = 0;

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

                var remainingInlineBytes = MaxTotalInlineImageBytes - totalInlineBytes;
                if (remainingInlineBytes <= 0)
                {
                    linkedPhotoNotes[photo.Id] = TooLargePhotoNote;
                    continue;
                }

                var inlineImage = await PrepareInlineImageAsync(photo, stream, Math.Min(MaxInlineImageBytes, remainingInlineBytes), cancellationToken);
                if (inlineImage is null)
                {
                    linkedPhotoNotes[photo.Id] = TooLargePhotoNote;
                    continue;
                }

                var contentId = $"cropqc-photo-{photo.Id}@cropqc";
                inlineImages.Add(new QcEmailInlineImage(
                    contentId,
                    inlineImage.FileName,
                    inlineImage.ContentType,
                    inlineImage.Bytes,
                    FriendlyPhotoName(photo.PhotoType)));
                imageReferences[photo.Id] = contentId;
                totalInlineBytes += inlineImage.Bytes.Length;
            }
            catch (OutOfMemoryException ex)
            {
                linkedPhotoNotes[photo.Id] = TooLargePhotoNote;
                logger.LogWarning(ex, "QC email photo was too large to embed and will be linked instead. PhotoId: {PhotoId}.", photo.Id);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not load QC email inline photo bytes. PhotoId: {PhotoId}. StorageProvider: {StorageProvider}.", photo.Id, photo.StorageProvider);
            }
        }

        var subject = BuildSubject(sample);
        var inspector = FormatInspector(sample.TakenByUser) ?? FormatInspector(sendingUser) ?? "";
        var html = BuildHtml(sample, readiness, enteredRows, requirements, photos, imageReferences, linkedPhotoNotes, inspector, isOverride, overrideReason);
        var text = BuildText(sample, readiness, enteredRows, requirements, photos, linkedPhotoNotes, inspector, isOverride, overrideReason);
        return new QcEmailContent(subject, html, text, inlineImages);
    }

    private static IReadOnlyList<QcPhoto> SelectEmailPhotos(QcSample sample, IReadOnlyList<QcPhotoRequirement> requirements)
    {
        var result = new List<QcPhoto>();
        foreach (var requirement in requirements)
        {
            var source = requirement.ReceiptLevel ? sample.Receipt.Photos : sample.Photos;
            result.AddRange(source
                .Where(x => !x.IsDeleted)
                .Where(x => string.Equals(QcPhotoRequirementPolicy.NormalizePhotoType(x.PhotoType), requirement.PhotoType, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(x => x.CapturedAt));
        }

        return result;
    }

    private static async Task<PreparedInlineImage?> PrepareInlineImageAsync(QcPhoto photo, Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        if (maxBytes <= 0)
        {
            return null;
        }

        var sourceBytes = await ReadSourceImageBytesAsync(stream, MaxSourceImageBytes, cancellationToken);
        if (sourceBytes is null)
        {
            return null;
        }

        await using var source = new MemoryStream(sourceBytes);
        using var image = await Image.LoadAsync(source, cancellationToken);
        image.Metadata.ExifProfile = null;
        image.Metadata.IccProfile = null;
        image.Mutate(x => x.AutoOrient());

        if (image.Width > InlineImageMaxWidth || image.Height > InlineImageMaxHeight)
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(InlineImageMaxWidth, InlineImageMaxHeight)
            }));
        }

        foreach (var attempt in CompressionAttempts)
        {
            if (image.Width > attempt.MaxWidth || image.Height > attempt.MaxHeight)
            {
                image.Mutate(x => x.Resize(new ResizeOptions
                {
                    Mode = ResizeMode.Max,
                    Size = new Size(attempt.MaxWidth, attempt.MaxHeight)
                }));
            }

            await using var compressed = new MemoryStream();
            await image.SaveAsJpegAsync(compressed, new JpegEncoder { Quality = attempt.Quality }, cancellationToken);
            if (compressed.Length <= maxBytes)
            {
                return new PreparedInlineImage(BuildInlineImageFileName(photo), "image/jpeg", compressed.ToArray());
            }
        }

        return null;
    }

    private static async Task<byte[]?> ReadSourceImageBytesAsync(Stream stream, int maxBytes, CancellationToken cancellationToken)
    {
        if (stream.CanSeek && stream.Length > maxBytes)
        {
            return null;
        }

        await using var memory = new MemoryStream(capacity: Math.Min(maxBytes, 64 * 1024));
        var buffer = new byte[64 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (memory.Length + read > maxBytes)
            {
                return null;
            }

            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return memory.ToArray();
    }

    private static string BuildInlineImageFileName(QcPhoto photo)
    {
        var name = string.IsNullOrWhiteSpace(photo.FileName)
            ? $"photo-{photo.Id}"
            : Path.GetFileNameWithoutExtension(photo.FileName);

        return $"{(string.IsNullOrWhiteSpace(name) ? $"photo-{photo.Id}" : name)}.jpg";
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
            ReportTime.FormatPacific(sample.SampleTakenAt, "MM/dd/yyyy", includeZone: false)
        };

        return string.Join(' ', parts.Where(x => !string.IsNullOrWhiteSpace(x))).Replace(" - ", " - ");
    }

    private static string BuildHtml(
        QcSample sample,
        ReadinessViewModel readiness,
        IReadOnlyList<QcFruitReading> enteredRows,
        IReadOnlyList<QcPhotoRequirement> requirements,
        IReadOnlyList<QcPhoto> photos,
        IReadOnlyDictionary<long, string> imageReferences,
        IReadOnlyDictionary<long, string> linkedPhotoNotes,
        string inspector,
        bool isOverride,
        string? overrideReason)
    {
        var summary = BuildSummary(enteredRows);
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
        AddInfoRow(html, "Receipt type", sample.Receipt.ReceiptType);
        AddInfoRow(html, "Received", ReportTime.FormatPacific(sample.Receipt.ReceivedAt));
        AddInfoRow(html, "Grower", sample.Receipt.GrowerName);
        AddInfoRow(html, "Grower number", sample.Receipt.GrowerNumber ?? "");
        AddInfoRow(html, "Orchard", sample.Receipt.CanonicalOrchardBlock?.CanonicalOrchard?.OrchardName ?? sample.Receipt.CanonicalOrchardBlock?.OrchardName ?? "Not confirmed");
        AddInfoRow(html, "Block", sample.Receipt.CanonicalOrchardBlock?.CanonicalBlockName ?? "Not confirmed");
        AddInfoRow(html, "Lot", sample.Receipt.LotCode);
        AddInfoRow(html, "Bins received", sample.Receipt.BinCount.ToString());
        AddInfoRow(html, "Variety", sample.Receipt.FruitProfile.VarietyCode);
        AddInfoRow(html, "Sample date/time", ReportTime.FormatPacific(sample.SampleTakenAt));
        AddInfoRow(html, "Inspector", inspector);
        AddInfoRow(html, "Target sample size", sample.ActualSampleSize?.ToString() ?? "");
        html.AppendLine("</table>");

        if (!readiness.IsReady)
        {
            html.AppendLine("<p style=\"color:#92400e;\"><strong>Sample is incomplete;</strong> summary includes entered data only.</p>");
        }

        html.AppendLine("<h2>Fruit Overview</h2>");
        html.AppendLine("<table cellpadding=\"5\" cellspacing=\"0\" style=\"border-collapse:collapse;border:1px solid #cbd5e1;table-layout:auto;width:100%;\">");
        html.AppendLine("<thead><tr><th style=\"white-space:nowrap;\">Row</th><th style=\"white-space:nowrap;\">P1 lbs</th><th style=\"white-space:nowrap;\">P2 lbs</th><th style=\"white-space:nowrap;\">Avg lbs</th><th style=\"white-space:nowrap;\">Weight g</th><th style=\"white-space:nowrap;\">Size</th><th style=\"white-space:nowrap;\">Grade</th><th style=\"white-space:nowrap;\">Starch</th><th>Defects</th><th>Notes</th></tr></thead><tbody>");
        foreach (var row in enteredRows)
        {
            var defects = DefectDisplay(row);
            var notes = DefectNotes(row);
            html.AppendLine("<tr>" +
                Cell(row.RowNumber.ToString(), NumberCellStyle) +
                Cell(Format(row.Pressure1Lbs), NumberCellStyle) +
                Cell(Format(row.Pressure2Lbs), NumberCellStyle) +
                Cell(Format(Average(row.Pressure1Lbs, row.Pressure2Lbs)), NumberCellStyle) +
                Cell(Format(row.WeightGrams), NumberCellStyle) +
                Cell(SizeText(row), NumberCellStyle) +
                Cell(row.Grade?.Code ?? "", NumberCellStyle) +
                Cell(StarchText(sample, row), NumberCellStyle) +
                Cell(defects, WrapCellStyle) +
                Cell(notes, WrapCellStyle) +
                "</tr>");
        }
        html.AppendLine("</tbody></table>");

        AppendPhotoSections(html, requirements, photos, imageReferences, linkedPhotoNotes);

        html.AppendLine("<h2>Summary</h2>");
        html.AppendLine("<table cellpadding=\"6\" cellspacing=\"0\" style=\"border-collapse:collapse;border:1px solid #cbd5e1;\">");
        AddInfoRow(html, "Target sample size", sample.ActualSampleSize?.ToString() ?? "");
        AddInfoRow(html, "Entered fruit count", summary.SampleSize.ToString());
        AddInfoRow(html, "Average Pressure", Format(summary.AveragePressure));
        AddInfoRow(html, "Pressure std dev lbs", Format(summary.PressureStandardDeviation));
        AddInfoRow(html, "Average starch", StarchSummaryText(sample, summary.AverageStarch));
        AddInfoRow(html, "Average weight grams", Format(summary.AverageWeight));
        AddInfoRow(html, "Grade summary", summary.GradeSummary);
        AddInfoRow(html, "Defect summary", summary.DefectSummary);
        AddInfoRow(html, "Size/status summary", summary.SizeSummary);
        if (!string.IsNullOrWhiteSpace(sample.Notes)) AddInfoRow(html, "Notes", sample.Notes);
        html.AppendLine("</table>");

        html.AppendLine("</body></html>");
        return html.ToString();
    }

    private static void AppendPhotoSections(StringBuilder html, IReadOnlyList<QcPhotoRequirement> requirements, IReadOnlyList<QcPhoto> photos, IReadOnlyDictionary<long, string> imageReferences, IReadOnlyDictionary<long, string> linkedPhotoNotes)
    {
        html.AppendLine("<h2>Photos</h2>");
        foreach (var requirement in requirements)
        {
            var group = photos.Where(x => string.Equals(QcPhotoRequirementPolicy.NormalizePhotoType(x.PhotoType), requirement.PhotoType, StringComparison.OrdinalIgnoreCase)).ToList();
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
                    if (linkedPhotoNotes.TryGetValue(photo.Id, out var note))
                    {
                        html.AppendLine($"<p style=\"color:#92400e;\">{Html(note)}</p>");
                    }
                }
                else if (linkedPhotoNotes.TryGetValue(photo.Id, out var note))
                {
                    html.AppendLine($"<p style=\"color:#92400e;\">{Html(photo.FileName)}: {Html(note)}</p>");
                }
            }
        }
    }

    private static string BuildText(
        QcSample sample,
        ReadinessViewModel readiness,
        IReadOnlyList<QcFruitReading> enteredRows,
        IReadOnlyList<QcPhotoRequirement> requirements,
        IReadOnlyList<QcPhoto> photos,
        IReadOnlyDictionary<long, string> linkedPhotoNotes,
        string inspector,
        bool isOverride,
        string? overrideReason)
    {
        var summary = BuildSummary(enteredRows);
        var text = new StringBuilder();
        text.AppendLine($"{sample.GetDisplayReceiptId()} QC Summary");
        text.AppendLine($"Sample type: {sample.SampleType.Name}");
        text.AppendLine($"Warehouse/Room: {sample.Receipt.Warehouse.Code} / {sample.Receipt.Room.Code}");
        text.AppendLine($"Receipt ID: {sample.GetDisplayReceiptId()}");
        text.AppendLine($"Receipt type / received: {sample.Receipt.ReceiptType} / {ReportTime.FormatPacific(sample.Receipt.ReceivedAt)}");
        text.AppendLine($"Grower/Lot/Variety: {sample.Receipt.GrowerName} / {sample.Receipt.LotCode} / {sample.Receipt.FruitProfile.VarietyCode}");
        text.AppendLine($"Grower number: {sample.Receipt.GrowerNumber}");
        text.AppendLine($"Orchard/Block: {sample.Receipt.CanonicalOrchardBlock?.CanonicalOrchard?.OrchardName ?? sample.Receipt.CanonicalOrchardBlock?.OrchardName ?? "Not confirmed"} / {sample.Receipt.CanonicalOrchardBlock?.CanonicalBlockName ?? "Not confirmed"}");
        text.AppendLine($"Bins received: {sample.Receipt.BinCount}");
        text.AppendLine($"Sample date/time: {ReportTime.FormatPacific(sample.SampleTakenAt)}");
        text.AppendLine($"Inspector: {inspector}");
        text.AppendLine($"Target sample size: {sample.ActualSampleSize?.ToString() ?? ""}");
        if (isOverride) text.AppendLine($"Override reason: {overrideReason}");
        if (!readiness.IsReady) text.AppendLine("Sample is incomplete; summary includes entered data only.");
        text.AppendLine();
        text.AppendLine("Fruit Overview");
        foreach (var row in enteredRows)
        {
            var defects = DefectDisplay(row);
            var notes = DefectNotes(row);
            text.AppendLine($"Row {row.RowNumber}: P1 {Format(row.Pressure1Lbs)} lbs, P2 {Format(row.Pressure2Lbs)} lbs, Avg {Format(Average(row.Pressure1Lbs, row.Pressure2Lbs))} lbs, Weight {Format(row.WeightGrams)} g, Size {SizeText(row)}, Grade {row.Grade?.Code}, Starch {StarchText(sample, row)}, Defects {defects}, Notes {notes}");
        }
        text.AppendLine();
        text.AppendLine("Photo sections:");
        foreach (var requirement in requirements)
        {
            var group = photos.Where(x => string.Equals(QcPhotoRequirementPolicy.NormalizePhotoType(x.PhotoType), requirement.PhotoType, StringComparison.OrdinalIgnoreCase)).ToList();
            var count = group.Count;
            if (count > 0)
            {
                text.AppendLine($"- {requirement.FriendlyName}: {count} photo(s)");
                foreach (var photo in group.Where(x => linkedPhotoNotes.ContainsKey(x.Id)))
                {
                    text.AppendLine($"  {photo.FileName}: {linkedPhotoNotes[photo.Id]} {photo.WebUrl}");
                }
            }
        }
        text.AppendLine();
        text.AppendLine("Summary");
        text.AppendLine($"Target sample size: {sample.ActualSampleSize?.ToString() ?? ""}");
        text.AppendLine($"Entered fruit count: {summary.SampleSize}");
        text.AppendLine($"Average Pressure: {Format(summary.AveragePressure)} lbs");
        text.AppendLine($"Pressure std dev lbs: {Format(summary.PressureStandardDeviation)}");
        text.AppendLine($"Average starch: {StarchSummaryText(sample, summary.AverageStarch)}");
        text.AppendLine($"Average weight grams: {Format(summary.AverageWeight)}");
        text.AppendLine($"Grade summary: {summary.GradeSummary}");
        text.AppendLine($"Defect summary: {summary.DefectSummary}");
        text.AppendLine($"Size/status summary: {summary.SizeSummary}");
        if (!string.IsNullOrWhiteSpace(sample.Notes)) text.AppendLine($"Notes: {sample.Notes}");
        return text.ToString();
    }

    private static QcSummaryStats BuildSummary(IReadOnlyList<QcFruitReading> rows)
    {
        var pressures = CropQc.Data.PressureCalculationService.ValidSideReadings(
            rows.Select(x => (x.Pressure1Lbs, x.Pressure2Lbs)));
        var starch = rows.Select(x => x.StarchScaleValue?.Value).Where(x => x is not null).Select(x => x!.Value).ToList();
        var weights = rows.Select(x => x.WeightGrams).Where(x => x is not null).Select(x => x!.Value).ToList();
        return new QcSummaryStats(
            rows.Count,
            pressures.Count == 0 ? null : pressures.Average(),
            StandardDeviation(pressures),
            starch.Count == 0 ? null : starch.Average(),
            weights.Count == 0 ? null : weights.Average(),
            Summarize(rows.Select(x => x.Grade?.Code).Where(x => !string.IsNullOrWhiteSpace(x))!),
            SummarizeDefects(rows),
            SummarizeSizes(rows.Select(SizeText).Where(x => !string.IsNullOrWhiteSpace(x))));
    }

    private static string Summarize(IEnumerable<string> values)
    {
        var summary = values.GroupBy(x => x).OrderBy(x => x.Key).Select(x => $"{x.Key}: {x.Count()}").ToList();
        return summary.Count == 0 ? "None" : string.Join(", ", summary);
    }

    private static string SummarizeDefects(IReadOnlyList<QcFruitReading> rows)
    {
        var inspected = rows.Where(HasEnteredData).ToList();
        if (inspected.Count == 0) return "No defects found";
        var affected = inspected.Count(x => x.Defects.Count > 0);
        var distribution = Summarize(inspected.SelectMany(x => x.Defects).Select(x => x.DefectType.Name));
        return affected == 0
            ? $"No defects found ({inspected.Count} fruit inspected)"
            : $"{affected} of {inspected.Count} inspected fruit affected; {distribution}";
    }

    private static string SummarizeSizes(IEnumerable<string> values)
    {
        var groups = values
            .GroupBy(x => x)
            .OrderByDescending(x => x.Count())
            .ThenBy(x => AppleSizeSort(x.Key))
            .ThenBy(x => x.Key)
            .Select(x => $"{x.Count()} size {x.Key}")
            .ToList();
        return groups.Count == 0 ? "None" : string.Join(", ", groups);
    }

    private static int AppleSizeSort(string value)
    {
        var order = new[] { 56, 64, 72, 80, 88, 100, 113, 120, 125, 138, 150, 163, 175, 198 };
        return int.TryParse(value, out var size)
            ? Array.IndexOf(order, size) is var index && index >= 0 ? index : order.Length + size
            : int.MaxValue;
    }

    private static decimal? StandardDeviation(IReadOnlyList<decimal> values)
    {
        if (values.Count < 2)
        {
            return null;
        }

        var average = (double)values.Average();
        var sum = values.Sum(x => Math.Pow((double)x - average, 2));
        return decimal.Round((decimal)Math.Sqrt(sum / (values.Count - 1)), 2);
    }

    private static bool HasEnteredData(QcFruitReading row) =>
        row.Pressure1Lbs is not null ||
        row.Pressure2Lbs is not null ||
        row.WeightGrams is not null ||
        row.GradeId is not null ||
        row.Grade is not null ||
        row.StarchScaleValueId is not null ||
        row.StarchScaleValue is not null ||
        row.SizeCategory is not null ||
        row.DefectsInspected ||
        row.Defects.Count > 0;

    private static IEnumerable<string> DefectNames(QcFruitReading row) =>
        row.Defects.Select(x => x.DefectType.Name).Where(x => !string.IsNullOrWhiteSpace(x)).OrderBy(x => x);

    private static string DefectDisplay(QcFruitReading row)
    {
        var names = DefectNames(row).ToList();
        if (names.Count > 0) return string.Join(", ", names);
        return "No defects found";
    }

    private static string DefectNotes(QcFruitReading row) =>
        string.Join("; ", row.Defects.Select(x => x.Notes).Where(x => !string.IsNullOrWhiteSpace(x)));

    private static void AddInfoRow(StringBuilder builder, string label, string value) =>
        builder.AppendLine($"<tr><th style=\"text-align:left;border:1px solid #cbd5e1;background:#f8fafc;\">{Html(label)}</th><td style=\"border:1px solid #cbd5e1;\">{Html(value)}</td></tr>");

    private const string NumberCellStyle = "border:1px solid #cbd5e1;white-space:nowrap;text-align:right;min-width:54px;";
    private const string WrapCellStyle = "border:1px solid #cbd5e1;white-space:normal;overflow-wrap:break-word;word-break:normal;min-width:110px;";
    private const string TooLargePhotoNote = "Photo was too large to embed and is linked instead.";

    private static string Cell(string value, string? style = null) => $"<td style=\"{style ?? "border:1px solid #cbd5e1;"}\">{Html(value)}</td>";
    private static string Html(string? value) => WebUtility.HtmlEncode(value ?? "");
    private static string Format(decimal? value) => value?.ToString("0.##") ?? "";
    private static string StarchText(QcSample sample, QcFruitReading row) =>
        row.StarchScaleValue?.Value.ToString("0.0")
        ?? (IsPearWithStarchPhoto(sample) ? "Not entered — see photo" : "");

    private static string StarchSummaryText(QcSample sample, decimal? value) =>
        value?.ToString("0.##")
        ?? (IsPearWithStarchPhoto(sample) ? "Not entered — see photo" : "");

    private static bool IsPearWithStarchPhoto(QcSample sample) =>
        string.Equals(sample.Receipt.FruitProfile.FruitType, "Pear", StringComparison.OrdinalIgnoreCase)
        && sample.Photos.Any(x => !x.IsDeleted
            && QcPhotoRequirementPolicy.NormalizePhotoType(x.PhotoType)
                .Equals("FruitAfterStarch", StringComparison.OrdinalIgnoreCase));

    private static decimal? Average(decimal? first, decimal? second) => (first, second) switch
    {
        (decimal a, decimal b) => decimal.Round((a + b) / 2m, 2),
        (decimal a, null) => a,
        (null, decimal b) => b,
        _ => null
    };

    private static string SizeText(QcFruitReading row) => row.SizeCategory?.ToString() ?? "";

    private static string? FormatInspector(User? user)
    {
        if (user is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(user.DisplayName) && !string.IsNullOrWhiteSpace(user.Email))
        {
            return $"{user.DisplayName} ({user.Email})";
        }

        return string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email : user.DisplayName;
    }

    private static string FriendlyPhotoName(string photoType) => QcPhotoRequirementPolicy.NormalizePhotoType(photoType) switch
    {
        "BinTruck" => "Truck photo",
        "TopOfTruck" => "Top of truck",
        "Hectre" => "Hectre",
        "SampleBeforeCutting" => "Whole sample",
        "CutFruit" => "Cut apples",
        "FruitAfterStarch" => "Starch apples",
        _ => photoType
    };

    private static readonly IReadOnlyList<InlineImageCompressionAttempt> CompressionAttempts =
    [
        new(InlineImageMaxWidth, InlineImageMaxHeight, 74),
        new(1000, 750, 68),
        new(800, 600, 62),
        new(640, 480, 56)
    ];

    private sealed record PreparedInlineImage(string FileName, string ContentType, byte[] Bytes);
    private sealed record InlineImageCompressionAttempt(int MaxWidth, int MaxHeight, int Quality);
    private sealed record QcSummaryStats(int SampleSize, decimal? AveragePressure, decimal? PressureStandardDeviation, decimal? AverageStarch, decimal? AverageWeight, string GradeSummary, string DefectSummary, string SizeSummary);
}
