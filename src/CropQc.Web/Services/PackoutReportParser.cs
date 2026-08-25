using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ExcelDataReader;

namespace CropQc.Web.Services;

public sealed record PackoutUploadFile(
    string FileName,
    string ContentType,
    string TemporaryPath,
    long Length);

public sealed record ParsedPackoutLine(
    int SourceLineNumber,
    string RawText,
    string? RawPackCode,
    decimal? Quantity,
    decimal Confidence,
    bool RequiresReview);

public sealed record PackoutParseResult(
    string FileName,
    string ContentType,
    long FileSizeBytes,
    string Sha256,
    string ParserName,
    string ParserVersion,
    decimal Confidence,
    IReadOnlyList<ParsedPackoutLine> Lines,
    string? SafeDiagnostic);

public interface IPackoutReportParser
{
    Task<PackoutParseResult> ParseAsync(PackoutUploadFile file, CancellationToken cancellationToken);
}

public sealed partial class PackoutReportParser : IPackoutReportParser
{
    public const string ParserVersion = "1.3";
    private static readonly string[] AllowedExtensions = [".pdf", ".xlsx", ".xls", ".csv", ".txt", ".jpg", ".jpeg", ".png", ".tif", ".tiff"];
    private readonly PackoutProcessingOptions options;
    private readonly ILogger<PackoutReportParser> logger;

    public PackoutReportParser(
        PackoutProcessingOptions options,
        ILogger<PackoutReportParser> logger)
    {
        this.options = options;
        this.logger = logger;
    }

    public PackoutReportParser(ILogger<PackoutReportParser> logger)
        : this(new PackoutProcessingOptions(), logger)
    {
    }

    public async Task<PackoutParseResult> ParseAsync(PackoutUploadFile file, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var startingWorkingSet = Environment.WorkingSet;
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        var validationError = ValidateUpload(file.FileName, file.Length, options);
        if (validationError is not null)
        {
            throw new InvalidOperationException(validationError);
        }

        var actualLength = new FileInfo(file.TemporaryPath).Length;
        if (actualLength != file.Length || actualLength > options.MaximumFileBytes)
        {
            throw new InvalidOperationException("The uploaded report size changed while it was being staged. Upload it again.");
        }

        IReadOnlyList<ParsedPackoutLine> lines;
        string parserName;
        string? formatDiagnostic = null;
        int? pageCount = null;
        if (extension == ".xlsx")
        {
            lines = ReadXlsx(file.TemporaryPath);
            parserName = "OpenXML";
        }
        else if (extension == ".xls")
        {
            lines = ReadXls(file.TemporaryPath);
            parserName = "ExcelDataReader";
        }
        else if (extension == ".txt")
        {
            var text = await File.ReadAllTextAsync(file.TemporaryPath, cancellationToken);
            if (IsSummaryReportByGrower(text))
            {
                lines = ParseSummaryReportByGrower(text, options.MaximumParsedRows);
                (lines, formatDiagnostic) = ValidateSummaryReportTotal(text, lines);
                parserName = "WP Summary Report By Grower";
            }
            else
            {
                lines = IsGrowerSummary(text)
                    ? ParseText(text, options.MaximumParsedRows)
                    : await ReadDelimitedAsync(file.TemporaryPath, cancellationToken);
                parserName = "DelimitedText";
            }
        }
        else if (extension == ".csv")
        {
            lines = await ReadDelimitedAsync(file.TemporaryPath, cancellationToken);
            parserName = "DelimitedText";
        }
        else
        {
            if (extension == ".pdf")
            {
                pageCount = await GetPdfPageCountAsync(file.TemporaryPath, cancellationToken);
                var pageLimitError = PackoutUploadLimits.ValidatePdfPageCount(pageCount.Value, options);
                if (pageLimitError is not null)
                {
                    throw new InvalidOperationException(pageLimitError);
                }
            }
            else
            {
                var dimensions = await ReadImageDimensionsAsync(file.TemporaryPath, extension, cancellationToken);
                if (dimensions is null || dimensions.Value.Width <= 0 || dimensions.Value.Height <= 0)
                {
                    throw new InvalidOperationException("The report image dimensions could not be validated.");
                }
                if ((long)dimensions.Value.Width * dimensions.Value.Height > options.MaximumImagePixels)
                {
                    throw new InvalidOperationException($"Report images may contain at most {options.MaximumImagePixels:N0} pixels.");
                }
            }

            var extracted = await ExtractWithPortableOcrAsync(file.TemporaryPath, extension, pageCount, cancellationToken);
            lines = ParseText(extracted.Text, options.MaximumParsedRows);
            if (IsSummaryReportByGrower(extracted.Text))
            {
                (lines, formatDiagnostic) = ValidateSummaryReportTotal(extracted.Text, lines);
                parserName = $"{(extension == ".pdf" ? extracted.UsedOcr ? "Poppler+Tesseract" : "PopplerText" : "Tesseract")} / WP Summary Report By Grower";
            }
            else
            {
                parserName = extension == ".pdf"
                    ? extracted.UsedOcr ? "Poppler+Tesseract" : "PopplerText"
                    : "Tesseract";
            }
        }

        var confidence = lines.Count == 0 ? 0m : decimal.Round(lines.Average(x => x.Confidence), 5);
        var diagnostic = formatDiagnostic ?? (lines.Count == 0
            ? "No packout detail rows could be identified. The original document remains available for review and reprocessing."
            : lines.Any(x => x.RequiresReview)
                ? $"{lines.Count(x => x.RequiresReview)} parsed row(s) require review before finalization."
                : null);
        await using var hashStream = new FileStream(
            file.TemporaryPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(hashStream, cancellationToken);
        stopwatch.Stop();
        logger.LogInformation(
            "Packout report parsing completed. Extension {Extension}; uploaded bytes {UploadedBytes}; page count {PageCount}; parsed row count {ParsedRowCount}; elapsed ms {ElapsedMilliseconds}; working set delta bytes {WorkingSetDeltaBytes}.",
            extension,
            actualLength,
            pageCount,
            lines.Count,
            stopwatch.ElapsedMilliseconds,
            Environment.WorkingSet - startingWorkingSet);
        return new(
            Path.GetFileName(file.FileName),
            file.ContentType,
            actualLength,
            Convert.ToHexString(hash).ToLowerInvariant(),
            parserName,
            ParserVersion,
            confidence,
            lines,
            diagnostic);
    }

    public static IReadOnlyList<ParsedPackoutLine> ParseText(string text, int maximumRows = 25_000)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        if (IsSummaryReportByGrower(text)) return ParseSummaryReportByGrower(text, maximumRows);
        if (IsGrowerSummary(text)) return ParseGrowerSummary(text, maximumRows);
        using var reader = new StringReader(text);
        var results = new List<ParsedPackoutLine>();
        var lineNumber = 0;
        string? sourceLine;
        while ((sourceLine = reader.ReadLine()) is not null)
        {
            lineNumber++;
            ParseSourceLine(sourceLine, lineNumber, results);
            if (results.Count > maximumRows)
            {
                throw new InvalidOperationException($"A report may contain at most {maximumRows:N0} parsed rows.");
            }
        }
        return results;
    }

    public static string? ValidateUpload(string fileName, long length, PackoutProcessingOptions options)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return "Upload a PDF, XLS, XLSX, CSV, TXT, JPG, PNG, or TIFF packout report.";
        return length <= 0 || length > options.MaximumFileBytes
            ? $"Each packout report must be between 1 byte and {options.MaximumFileBytes / 1024 / 1024} MB."
            : null;
    }

    public static bool IsSummaryReportByGrower(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return text.Contains("Summary Report By Grower", StringComparison.OrdinalIgnoreCase)
            && text.Contains("Date Type:", StringComparison.OrdinalIgnoreCase)
            && text.Contains("Quantity", StringComparison.OrdinalIgnoreCase)
            && text.Contains("Grd %", StringComparison.OrdinalIgnoreCase)
            && text.Contains("Var %", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<ParsedPackoutLine> ParseSummaryReportByGrower(string text, int maximumRows)
    {
        using var reader = new StringReader(text);
        var results = new List<ParsedPackoutLine>();
        var inDetails = false;
        var lineNumber = 0;
        while (reader.ReadLine() is { } sourceLine)
        {
            lineNumber++;
            var raw = sourceLine.Trim();
            if (raw.Contains("Quantity", StringComparison.OrdinalIgnoreCase)
                && raw.Contains("Grd %", StringComparison.OrdinalIgnoreCase)
                && raw.Contains("Var %", StringComparison.OrdinalIgnoreCase))
            {
                inDetails = true;
                continue;
            }
            if (!inDetails || string.IsNullOrWhiteSpace(raw)
                || raw.Contains(" Total", StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith("Total", StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith("Grand Total", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var columns = Regex.Split(raw, @"\t+|\s{2,}")
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToList();
            string? packCode = null;
            string? quantityText = null;
            string? gradePercent = null;
            string? varietyPercent = null;
            if (columns.Count >= 13)
            {
                packCode = columns[3];
                quantityText = columns[^3];
                gradePercent = columns[^2];
                varietyPercent = columns[^1];
            }
            else
            {
                var detail = Regex.Match(raw,
                    @"^(?<stor>\S+)\s+(?<var>\S+)\s+(?<grd>\S+)\s+(?<pack>\S+)\s+(?<size>\S+)\s+(?<brand>\S+)\s+(?<spec>\S+)\s+(?<loc>\S+)\s+(?<lot>\S+)\s+(?<run>\S+)\s+(?<quantity>[\d,]+(?:\.\d+)?)\s+(?<gradePct>\d+(?:\.\d+)?%)\s+(?<varPct>\d+(?:\.\d+)?%)$",
                    RegexOptions.IgnoreCase);
                if (detail.Success)
                {
                    packCode = detail.Groups["pack"].Value;
                    quantityText = detail.Groups["quantity"].Value;
                    gradePercent = detail.Groups["gradePct"].Value;
                    varietyPercent = detail.Groups["varPct"].Value;
                }
            }
            if (string.IsNullOrWhiteSpace(packCode)
                || gradePercent?.EndsWith('%') != true
                || varietyPercent?.EndsWith('%') != true
                || ParseDecimal(quantityText) is not decimal quantity)
            {
                continue;
            }
            results.Add(new(lineNumber, sourceLine, packCode, quantity, 0.98m, false));
            if (results.Count > maximumRows)
                throw new InvalidOperationException($"A report may contain at most {maximumRows:N0} parsed rows.");
        }
        return results;
    }

    private static (IReadOnlyList<ParsedPackoutLine> Lines, string? Diagnostic) ValidateSummaryReportTotal(
        string text,
        IReadOnlyList<ParsedPackoutLine> lines)
    {
        var totalMatch = Regex.Match(text, @"(?im)^\s*Grand\s+Total\D+(?<total>[\d,]+(?:\.\d+)?)\b");
        var declared = totalMatch.Success ? ParseDecimal(totalMatch.Groups["total"].Value) : null;
        var detailTotal = lines.Sum(x => x.Quantity ?? 0m);
        if (declared is not null && declared.Value == detailTotal) return (lines, null);
        var reason = declared is null
            ? $"The report detail total is {detailTotal:0.####}, but the Grand Total could not be verified. Review the parsed rows."
            : $"The report detail total is {detailTotal:0.####}, but the stated Grand Total is {declared:0.####}. Review the parsed rows.";
        return (lines.Select(x => x with { RequiresReview = true }).ToList(), reason);
    }

    public static bool IsGrowerSummary(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        var markers = new[]
        {
            "Grower Summary", "From Date:", "To Date:", "Run #:", "Grower:",
            "Variety:", "Pack Type:", "Lid Label", "End of Variety", "End of Run"
        };
        return markers.Count(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase)) >= 7
            && text.Contains("Grower Summary", StringComparison.OrdinalIgnoreCase)
            && text.Contains("Pack Type:", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<ParsedPackoutLine> ParseGrowerSummary(string text, int maximumRows)
    {
        using var reader = new StringReader(text);
        var results = new List<ParsedPackoutLine>();
        string? packType = null;
        var lineNumber = 0;
        while (reader.ReadLine() is { } sourceLine)
        {
            lineNumber++;
            var raw = CollapseWhitespace(sourceLine);
            var section = Regex.Match(raw, @"^Pack Type:\s*(?<pack>.+?)\s+Color:\s*$", RegexOptions.IgnoreCase);
            if (section.Success)
            {
                packType = section.Groups["pack"].Value.Trim();
                continue;
            }
            if (packType is null || raw.StartsWith("Total:", StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith("End of ", StringComparison.OrdinalIgnoreCase)
                || raw.StartsWith("Lid Label", StringComparison.OrdinalIgnoreCase)) continue;

            var detail = Regex.Match(raw,
                @"^(?<lid>[A-Z0-9-]+)\s+(?<grade>Wa Fancy|US No\.\s*1B?|[^\d]+?)\s+(?<size>\d+(?:\s+\d+/\d+)?)\s+(?<boxes>[\d,]+)\s+(?<percent>\d+(?:\.\d+)?%)\s+lbs$",
                RegexOptions.IgnoreCase);
            if (!detail.Success) continue;
            var boxes = ParseDecimal(detail.Groups["boxes"].Value);
            if (boxes is null) continue;
            results.Add(new(
                lineNumber,
                $"Pack Type: {packType} | Lid Label: {detail.Groups["lid"].Value} | Grade: {detail.Groups["grade"].Value.Trim()} | Size: {detail.Groups["size"].Value} | Box: {boxes:0} | Source: {raw}",
                packType,
                boxes,
                0.98m,
                false));
            if (results.Count > maximumRows)
                throw new InvalidOperationException($"A report may contain at most {maximumRows:N0} parsed rows.");
        }
        return results;
    }

    public static (string NormalizedCode, string? ProductCategory, decimal? NetWeightPounds) ClassifyPackCode(string? rawCode)
    {
        var normalized = NormalizePackCode(rawCode);
        var liquid = LiquidCodeRegex().Match(normalized);
        if (liquid.Success && decimal.TryParse(liquid.Groups["pounds"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var pounds))
        {
            return (normalized, CropQc.Data.Entities.PackoutProductCategories.Juice, pounds);
        }
        return (normalized, null, null);
    }

    public static string NormalizePackCode(string? value) =>
        Regex.Replace(value?.Trim().ToUpperInvariant() ?? "", @"[^A-Z0-9]+", "");

    private async Task<IReadOnlyList<ParsedPackoutLine>> ReadDelimitedAsync(
        string path,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var results = new List<ParsedPackoutLine>();
        var sourceLineNumber = 0;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            sourceLineNumber++;
            if (sourceLineNumber > options.MaximumSpreadsheetRows)
            {
                throw new InvalidOperationException($"Delimited reports may contain at most {options.MaximumSpreadsheetRows:N0} rows.");
            }
            ParseSourceLine(line, sourceLineNumber, results);
            EnsureParsedRowLimit(results.Count);
        }
        return results;
    }

    private IReadOnlyList<ParsedPackoutLine> ReadXlsx(string path)
    {
        using var document = SpreadsheetDocument.Open(path, false);
        var workbook = document.WorkbookPart ?? throw new InvalidOperationException("The XLSX workbook is missing its workbook part.");
        var shared = workbook.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().Select(x => x.InnerText).ToList() ?? [];
        var results = new List<ParsedPackoutLine>();
        var sourceLineNumber = 0;
        foreach (var worksheetPart in workbook.WorksheetParts)
        {
            using var reader = DocumentFormat.OpenXml.OpenXmlReader.Create(worksheetPart);
            while (reader.Read())
            {
                if (reader.ElementType != typeof(Row) || !reader.IsStartElement) continue;
                if (reader.LoadCurrentElement() is not Row row) continue;
                sourceLineNumber++;
                EnsureSpreadsheetRowLimit(sourceLineNumber);
                ParseSourceLine(string.Join('\t', row.Elements<Cell>().Select(cell => CellText(cell, shared))), sourceLineNumber, results);
                EnsureParsedRowLimit(results.Count);
            }
        }
        return results;
    }

    private IReadOnlyList<ParsedPackoutLine> ReadXls(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var results = new List<ParsedPackoutLine>();
        var sourceLineNumber = 0;
        do
        {
            while (reader.Read())
            {
                sourceLineNumber++;
                EnsureSpreadsheetRowLimit(sourceLineNumber);
                var values = Enumerable.Range(0, reader.FieldCount)
                    .Select(reader.GetValue)
                    .Select(x => Convert.ToString(x, CultureInfo.InvariantCulture) ?? "");
                ParseSourceLine(string.Join('\t', values), sourceLineNumber, results);
                EnsureParsedRowLimit(results.Count);
            }
        }
        while (reader.NextResult());
        return results;
    }

    private async Task<ExtractedText> ExtractWithPortableOcrAsync(
        string input,
        string extension,
        int? pageCount,
        CancellationToken cancellationToken)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"cropqc-packout-ocr-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            if (extension == ".pdf")
            {
                var directTextPath = Path.Combine(tempRoot, "direct.txt");
                var direct = await RunProcessAsync("pdftotext", ["-layout", input, directTextPath], cancellationToken);
                if (direct.ExitCode == 0 && File.Exists(directTextPath))
                {
                    var directText = await File.ReadAllTextAsync(directTextPath, cancellationToken);
                    if (directText.Count(char.IsLetterOrDigit) >= 40) return new(directText, false);
                }

                var output = new StringBuilder();
                for (var pageNumber = 1; pageNumber <= pageCount.GetValueOrDefault(1); pageNumber++)
                {
                    var imagePrefix = Path.Combine(tempRoot, "page");
                    var image = $"{imagePrefix}.png";
                    try
                    {
                        var raster = await RunProcessAsync(
                            "pdftoppm",
                            ["-f", pageNumber.ToString(CultureInfo.InvariantCulture), "-l", pageNumber.ToString(CultureInfo.InvariantCulture), "-singlefile", "-png", "-r", "200", input, imagePrefix],
                            cancellationToken);
                        if (raster.ExitCode != 0 || !File.Exists(image))
                        {
                            throw new InvalidOperationException($"PDF page {pageNumber} could not be rasterized for OCR.");
                        }
                        var dimensions = await ReadImageDimensionsAsync(image, ".png", cancellationToken);
                        if (dimensions is null || dimensions.Value.Width <= 0 || dimensions.Value.Height <= 0)
                        {
                            throw new InvalidOperationException($"Rasterized PDF page {pageNumber} dimensions could not be validated.");
                        }
                        if ((long)dimensions.Value.Width * dimensions.Value.Height > options.MaximumImagePixels)
                        {
                            throw new InvalidOperationException($"Rasterized PDF pages may contain at most {options.MaximumImagePixels:N0} pixels.");
                        }
                        output.AppendLine((await RunProcessAsync("tesseract", [image, "stdout", "--psm", "6"], cancellationToken)).StandardOutput);
                    }
                    finally
                    {
                        TryDeleteFile(image);
                    }
                }
                return new(output.ToString(), true);
            }

            return new((await RunProcessAsync("tesseract", [input, "stdout", "--psm", "6"], cancellationToken)).StandardOutput, true);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Packout OCR failed for extension {Extension}. The upload service retains the original document independently of this parse result.", extension);
            throw new InvalidOperationException("The report image could not be read by the configured OCR tools. Check image clarity or upload a spreadsheet/CSV.", exception);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch (IOException exception) { logger.LogWarning(exception, "Temporary packout OCR files could not be removed immediately."); }
            catch (UnauthorizedAccessException exception) { logger.LogWarning(exception, "Temporary packout OCR files could not be removed immediately."); }
        }
    }

    private sealed record ExtractedText(string Text, bool UsedOcr);

    private async Task<int> GetPdfPageCountAsync(string path, CancellationToken cancellationToken)
    {
        var result = await RunProcessAsync("pdfinfo", [path], cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException("The PDF page count could not be validated.");
        }
        var match = Regex.Match(result.StandardOutput, @"(?im)^Pages:\s*(?<count>\d+)\s*$");
        if (!match.Success || !int.TryParse(match.Groups["count"].Value, out var pageCount) || pageCount < 1)
        {
            throw new InvalidOperationException("The PDF does not contain a valid page count.");
        }
        return pageCount;
    }

    private static async Task<(int Width, int Height)?> ReadImageDimensionsAsync(
        string path,
        string extension,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (extension == ".png")
        {
            var header = new byte[24];
            if (await stream.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken) < header.Length) return null;
            return (BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4)), BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4)));
        }
        if (extension is ".jpg" or ".jpeg")
        {
            return await ReadJpegDimensionsAsync(stream, cancellationToken);
        }
        if (extension is ".tif" or ".tiff")
        {
            return await ReadTiffDimensionsAsync(stream, cancellationToken);
        }
        return null;
    }

    private static async Task<(int Width, int Height)?> ReadJpegDimensionsAsync(Stream stream, CancellationToken cancellationToken)
    {
        var two = new byte[2];
        if (await stream.ReadAtLeastAsync(two, 2, false, cancellationToken) < 2 || two[0] != 0xff || two[1] != 0xd8) return null;
        while (await stream.ReadAtLeastAsync(two, 2, false, cancellationToken) == 2)
        {
            if (two[0] != 0xff) return null;
            var marker = two[1];
            if (marker is 0xd8 or 0xd9) continue;
            if (await stream.ReadAtLeastAsync(two, 2, false, cancellationToken) < 2) return null;
            var length = BinaryPrimitives.ReadUInt16BigEndian(two);
            if (length < 2) return null;
            if (marker is >= 0xc0 and <= 0xc3 or >= 0xc5 and <= 0xc7 or >= 0xc9 and <= 0xcb or >= 0xcd and <= 0xcf)
            {
                var size = new byte[5];
                if (await stream.ReadAtLeastAsync(size, size.Length, false, cancellationToken) < size.Length) return null;
                return (BinaryPrimitives.ReadUInt16BigEndian(size.AsSpan(3, 2)), BinaryPrimitives.ReadUInt16BigEndian(size.AsSpan(1, 2)));
            }
            stream.Seek(length - 2, SeekOrigin.Current);
        }
        return null;
    }

    private static async Task<(int Width, int Height)?> ReadTiffDimensionsAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[8];
        if (await stream.ReadAtLeastAsync(header, 8, false, cancellationToken) < 8) return null;
        var littleEndian = header[0] == (byte)'I' && header[1] == (byte)'I';
        if (!littleEndian && !(header[0] == (byte)'M' && header[1] == (byte)'M')) return null;
        ushort U16(ReadOnlySpan<byte> value) => littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(value) : BinaryPrimitives.ReadUInt16BigEndian(value);
        uint U32(ReadOnlySpan<byte> value) => littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(value) : BinaryPrimitives.ReadUInt32BigEndian(value);
        stream.Seek(U32(header.AsSpan(4, 4)), SeekOrigin.Begin);
        var countBytes = new byte[2];
        if (await stream.ReadAtLeastAsync(countBytes, 2, false, cancellationToken) < 2) return null;
        var entryCount = U16(countBytes);
        int? width = null;
        int? height = null;
        var entry = new byte[12];
        for (var index = 0; index < entryCount; index++)
        {
            if (await stream.ReadAtLeastAsync(entry, 12, false, cancellationToken) < 12) return null;
            var tag = U16(entry.AsSpan(0, 2));
            if (tag is not (256 or 257)) continue;
            var type = U16(entry.AsSpan(2, 2));
            var value = type == 3 ? U16(entry.AsSpan(8, 2)) : checked((int)U32(entry.AsSpan(8, 4)));
            if (tag == 256) width = value;
            else height = value;
            if (width is not null && height is not null) return (width.Value, height.Value);
        }
        return null;
    }

    private static async Task<ProcessResult> RunProcessAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException($"{fileName} could not be started.");
        try
        {
            var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            return new(process.ExitCode, await stdout, await stderr);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            throw;
        }
    }

    private static void ParseSourceLine(string sourceLine, int sourceLineNumber, List<ParsedPackoutLine> results)
    {
        var raw = CollapseWhitespace(sourceLine);
        if (raw.Length == 0 || IsHeaderOrTotal(raw)) return;
        var structured = StructuredRowRegex().Match(raw);
        string? packCode = null;
        decimal? quantity = null;
        var confidence = 0.45m;
        if (structured.Success)
        {
            packCode = structured.Groups["code"].Value;
            quantity = ParseDecimal(structured.Groups["quantity"].Value);
            confidence = 0.92m;
        }
        else
        {
            var tokens = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            packCode = tokens.FirstOrDefault(IsLikelyPackCode);
            quantity = tokens.Reverse().Select(ParseDecimal).FirstOrDefault(x => x is not null and not 0m);
            if (packCode is not null && quantity is not null) confidence = 0.72m;
        }
        if (packCode is null && quantity is null) return;
        var requiresReview = packCode is null || quantity is null || quantity < 0m || confidence < 0.85m;
        results.Add(new(sourceLineNumber, raw, packCode, quantity, confidence, requiresReview));
    }

    private void EnsureSpreadsheetRowLimit(int count)
    {
        if (count > options.MaximumSpreadsheetRows)
        {
            throw new InvalidOperationException($"Spreadsheet reports may contain at most {options.MaximumSpreadsheetRows:N0} rows.");
        }
    }

    private void EnsureParsedRowLimit(int count)
    {
        if (count > options.MaximumParsedRows)
        {
            throw new InvalidOperationException($"A report may contain at most {options.MaximumParsedRows:N0} parsed rows.");
        }
    }

    private static string CellText(Cell cell, IReadOnlyList<string> shared)
    {
        var value = cell.CellValue?.InnerText ?? cell.InnerText;
        if (cell.DataType?.Value == CellValues.SharedString
            && int.TryParse(value, out var index)
            && index >= 0
            && index < shared.Count)
        {
            return shared[index];
        }
        return value;
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static bool IsHeaderOrTotal(string line) =>
        line.Contains("SUMMARY REPORT", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("TOTAL", StringComparison.OrdinalIgnoreCase)
        || line.Contains("PACKING, LLC", StringComparison.OrdinalIgnoreCase)
        || line.StartsWith("PAGE ", StringComparison.OrdinalIgnoreCase);

    private static bool IsLikelyPackCode(string token)
    {
        var normalized = NormalizePackCode(token);
        return normalized.Length is >= 2 and <= 15
            && normalized.Any(char.IsDigit)
            && (normalized.Any(char.IsLetter) || normalized.All(char.IsDigit));
    }

    private static decimal? ParseDecimal(string? value) =>
        decimal.TryParse(value?.Replace(",", "", StringComparison.Ordinal), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    private static string CollapseWhitespace(string value) =>
        Regex.Replace(value.Trim(), @"\s+", " ");

    [GeneratedRegex(@"^(?:(?:[A-Z][A-Z0-9'./-]*)(?:\s+)){1,8}(?<code>[A-Z0-9][A-Z0-9./-]{1,14})\s+(?:[A-Z][A-Z0-9'./-]*\s+){0,8}(?<quantity>-?\d+(?:\.\d+)?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex StructuredRowRegex();

    [GeneratedRegex(@"^(?<pounds>\d+(?:\.\d+)?)L$", RegexOptions.IgnoreCase)]
    private static partial Regex LiquidCodeRegex();

    private sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError);
}
