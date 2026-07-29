using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ExcelDataReader;

namespace CropQc.Web.Services;

public sealed record PackoutUploadFile(string FileName, string ContentType, byte[] Bytes);

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

public sealed partial class PackoutReportParser(
    ILogger<PackoutReportParser> logger) : IPackoutReportParser
{
    public const int MaximumFileBytes = 20 * 1024 * 1024;
    public const string ParserVersion = "1.0";
    private static readonly string[] AllowedExtensions = [".pdf", ".xlsx", ".xls", ".csv", ".txt", ".jpg", ".jpeg", ".png", ".tif", ".tiff"];

    public async Task<PackoutParseResult> ParseAsync(PackoutUploadFile file, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Upload a PDF, XLS, XLSX, CSV, TXT, JPG, PNG, or TIFF packout report.");
        }
        if (file.Bytes.Length == 0 || file.Bytes.Length > MaximumFileBytes)
        {
            throw new InvalidOperationException($"Each packout report must be between 1 byte and {MaximumFileBytes / 1024 / 1024} MB.");
        }

        string text;
        string parser;
        if (extension == ".xlsx")
        {
            text = ReadXlsx(file.Bytes);
            parser = "OpenXML";
        }
        else if (extension == ".xls")
        {
            text = ReadXls(file.Bytes);
            parser = "ExcelDataReader";
        }
        else if (extension is ".csv" or ".txt")
        {
            text = DecodeText(file.Bytes);
            parser = "DelimitedText";
        }
        else
        {
            text = await ExtractWithPortableOcrAsync(file, extension, cancellationToken);
            parser = extension == ".pdf" ? "Poppler+Tesseract" : "Tesseract";
        }

        var lines = ParseText(text);
        var confidence = lines.Count == 0 ? 0m : decimal.Round(lines.Average(x => x.Confidence), 5);
        var diagnostic = lines.Count == 0
            ? "No packout detail rows could be identified. The original was not retained; correct the source file and upload it again."
            : lines.Any(x => x.RequiresReview)
                ? $"{lines.Count(x => x.RequiresReview)} parsed row(s) require review before finalization."
                : null;
        return new(
            Path.GetFileName(file.FileName),
            file.ContentType,
            file.Bytes.LongLength,
            Convert.ToHexString(SHA256.HashData(file.Bytes)).ToLowerInvariant(),
            parser,
            ParserVersion,
            confidence,
            lines,
            diagnostic);
    }

    public static IReadOnlyList<ParsedPackoutLine> ParseText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return [];
        var results = new List<ParsedPackoutLine>();
        var sourceLines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        for (var index = 0; index < sourceLines.Length; index++)
        {
            var raw = CollapseWhitespace(sourceLines[index]);
            if (raw.Length == 0 || IsHeaderOrTotal(raw)) continue;
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
            if (packCode is null && quantity is null) continue;
            var requiresReview = packCode is null || quantity is null || quantity < 0m || confidence < 0.85m;
            results.Add(new(index + 1, raw, packCode, quantity, confidence, requiresReview));
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

    private async Task<string> ExtractWithPortableOcrAsync(
        PackoutUploadFile file,
        string extension,
        CancellationToken cancellationToken)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"cropqc-packout-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            var input = Path.Combine(tempRoot, $"input{extension}");
            await File.WriteAllBytesAsync(input, file.Bytes, cancellationToken);
            if (extension == ".pdf")
            {
                var directTextPath = Path.Combine(tempRoot, "direct.txt");
                var direct = await RunProcessAsync("pdftotext", ["-layout", input, directTextPath], cancellationToken);
                if (direct.ExitCode == 0 && File.Exists(directTextPath))
                {
                    var directText = await File.ReadAllTextAsync(directTextPath, cancellationToken);
                    if (directText.Count(char.IsLetterOrDigit) >= 40) return directText;
                }

                var imagePrefix = Path.Combine(tempRoot, "page");
                var raster = await RunProcessAsync("pdftoppm", ["-png", "-r", "300", input, imagePrefix], cancellationToken);
                if (raster.ExitCode != 0)
                {
                    throw new InvalidOperationException("The PDF could not be rasterized for OCR.");
                }
                var pages = Directory.GetFiles(tempRoot, "page-*.png").OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                var pageText = new List<string>();
                foreach (var page in pages)
                {
                    pageText.Add((await RunProcessAsync("tesseract", [page, "stdout", "--psm", "6"], cancellationToken)).StandardOutput);
                }
                return string.Join(Environment.NewLine, pageText);
            }

            return (await RunProcessAsync("tesseract", [input, "stdout", "--psm", "6"], cancellationToken)).StandardOutput;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Packout OCR failed for extension {Extension}. No uploaded file content was retained.", extension);
            throw new InvalidOperationException("The report image could not be read by the configured OCR tools. Check image clarity or upload a spreadsheet/CSV.", exception);
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); }
            catch (IOException exception) { logger.LogWarning(exception, "Temporary packout OCR files could not be removed immediately."); }
            catch (UnauthorizedAccessException exception) { logger.LogWarning(exception, "Temporary packout OCR files could not be removed immediately."); }
        }
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
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return new(process.ExitCode, await stdout, await stderr);
    }

    private static string ReadXlsx(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var document = SpreadsheetDocument.Open(stream, false);
        var workbook = document.WorkbookPart ?? throw new InvalidOperationException("The XLSX workbook is missing its workbook part.");
        var shared = workbook.SharedStringTablePart?.SharedStringTable?.Elements<SharedStringItem>().Select(x => x.InnerText).ToList() ?? [];
        var rows = new List<string>();
        foreach (var worksheetPart in workbook.WorksheetParts)
        {
            foreach (var row in worksheetPart.Worksheet.Descendants<Row>())
            {
                rows.Add(string.Join('\t', row.Elements<Cell>().Select(cell => CellText(cell, shared))));
            }
        }
        return string.Join(Environment.NewLine, rows);
    }

    private static string ReadXls(byte[] bytes)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var stream = new MemoryStream(bytes);
        using var reader = ExcelReaderFactory.CreateReader(stream);
        var lines = new List<string>();
        do
        {
            while (reader.Read())
            {
                var values = Enumerable.Range(0, reader.FieldCount)
                    .Select(reader.GetValue)
                    .Select(x => Convert.ToString(x, CultureInfo.InvariantCulture) ?? "");
                lines.Add(string.Join('\t', values));
            }
        }
        while (reader.NextResult());
        return string.Join(Environment.NewLine, lines);
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

    private static string DecodeText(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
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
