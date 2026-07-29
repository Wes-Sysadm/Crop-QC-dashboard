using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using CropQc.Web.Models;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace CropQc.Web.Services;

public interface IOrchardContactWorkbookParser
{
    Task<ParsedOrchardContactWorkbook> ParseAsync(Stream source, string fileName, CancellationToken cancellationToken);
}

public sealed partial class OrchardContactWorkbookParser : IOrchardContactWorkbookParser
{
    public const string AuthoritativeWorksheet = "Summary";
    private static readonly string[] RequiredHeaders =
        ["Type", "Orchard", "Physical Address", "Name", "Phone", "Email", "Communication Notes"];
    private readonly PackoutProcessingOptions options;
    private readonly ILogger<OrchardContactWorkbookParser>? logger;

    public OrchardContactWorkbookParser()
        : this(new PackoutProcessingOptions(), null)
    {
    }

    public OrchardContactWorkbookParser(
        PackoutProcessingOptions options,
        ILogger<OrchardContactWorkbookParser>? logger)
    {
        this.options = options;
        this.logger = logger;
    }

    public async Task<ParsedOrchardContactWorkbook> ParseAsync(Stream source, string fileName, CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.GetExtension(fileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Upload an XLSX workbook. Legacy XLS files are not accepted by this reviewed import.");
        }

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var startingWorkingSet = Environment.WorkingSet;
        var tempPath = Path.Combine(Path.GetTempPath(), $"cropqc-orchard-import-{Guid.NewGuid():N}.xlsx");
        long uploadedBytes = 0;
        string checksum;
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var staged = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                var buffer = new byte[64 * 1024];
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken);
                    if (read == 0) break;
                    uploadedBytes += read;
                    if (uploadedBytes > options.MaximumFileBytes)
                    {
                        throw new InvalidDataException($"The workbook exceeds the {options.MaximumFileBytes / 1024 / 1024} MB import limit.");
                    }
                    hash.AppendData(buffer, 0, read);
                    await staged.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }
            if (uploadedBytes == 0) throw new InvalidDataException("The uploaded workbook is empty.");
            checksum = Convert.ToHexString(hash.GetHashAndReset());

            using var workbook = SpreadsheetDocument.Open(tempPath, false);
            var workbookPart = workbook.WorkbookPart ?? throw new InvalidDataException("The workbook package is missing its workbook definition.");
            var sheet = workbookPart.Workbook.Sheets?.Elements<Sheet>()
                .SingleOrDefault(x => string.Equals(x.Name?.Value, AuthoritativeWorksheet, StringComparison.Ordinal));
            if (sheet?.Id?.Value is null)
            {
                throw new InvalidDataException($"Worksheet '{AuthoritativeWorksheet}' was not found.");
            }

            var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id.Value);
            var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
            using var rowReader = DocumentFormat.OpenXml.OpenXmlReader.Create(worksheetPart);
            var tokens = new List<ParsedOrchardManagerToken>();
            var sourceRowCount = 0;
            var workbookRowCount = 0;
            var headerRead = false;
            while (rowReader.Read())
            {
                if (rowReader.ElementType != typeof(Row) || !rowReader.IsStartElement) continue;
                if (rowReader.LoadCurrentElement() is not Row row) continue;
                workbookRowCount++;
                if (workbookRowCount > options.MaximumSpreadsheetRows)
                {
                    throw new InvalidDataException($"The workbook exceeds the {options.MaximumSpreadsheetRows:N0}-row import limit.");
                }
                var cells = ReadRow(row, sharedStrings);
                if (!headerRead)
                {
                    for (var column = 0; column < RequiredHeaders.Length; column++)
                    {
                        if (!string.Equals(Clean(cells.GetValueOrDefault(column)), RequiredHeaders[column], StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException(
                                $"Worksheet '{AuthoritativeWorksheet}' column {ColumnName(column + 1)} must be '{RequiredHeaders[column]}'.");
                        }
                    }
                    headerRead = true;
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                var type = Clean(cells.GetValueOrDefault(0));
                if (!string.Equals(type, "Orchard Manager", StringComparison.OrdinalIgnoreCase)) continue;

                sourceRowCount++;
                var orchardCell = Clean(cells.GetValueOrDefault(1));
                var managerName = Clean(cells.GetValueOrDefault(3));
                var emailSource = Clean(cells.GetValueOrDefault(5));
                var email = NormalizeEmail(emailSource);
                var parsedEmail = QcEmailRecipientParser.Parse(email ?? "");
                var emailIsValid = email is not null
                    && parsedEmail.Recipients.Count == 1
                    && parsedEmail.InvalidRecipients.Count == 0;
                var normalizedEmail = emailIsValid ? parsedEmail.Recipients[0].ToUpperInvariant() : null;
                var phoneSource = Clean(cells.GetValueOrDefault(4));
                var (phone, normalizedPhone) = OrchardContactNormalization.NormalizePhone(phoneSource);
                var rowNumber = checked((int)(row.RowIndex?.Value ?? 0));
                foreach (var token in SplitOrchards(orchardCell))
                {
                    tokens.Add(new ParsedOrchardManagerToken(
                        rowNumber,
                        orchardCell,
                        token,
                        managerName,
                        OrchardContactNormalization.NormalizePersonName(managerName),
                        email,
                        normalizedEmail,
                        emailIsValid,
                        phone,
                        normalizedPhone,
                        NullIfEmpty(Clean(cells.GetValueOrDefault(2))),
                        NullIfEmpty(Clean(cells.GetValueOrDefault(6))),
                        NullIfEmpty(Clean(cells.GetValueOrDefault(7)))));
                }
            }
            if (!headerRead) throw new InvalidDataException($"Worksheet '{AuthoritativeWorksheet}' is empty.");
            stopwatch.Stop();
            logger?.LogInformation(
                "Orchard contact workbook parsing completed. Uploaded bytes {UploadedBytes}; spreadsheet row count {SpreadsheetRowCount}; parsed row count {ParsedRowCount}; elapsed ms {ElapsedMilliseconds}; working set delta bytes {WorkingSetDeltaBytes}.",
                uploadedBytes,
                workbookRowCount,
                sourceRowCount,
                stopwatch.ElapsedMilliseconds,
                Environment.WorkingSet - startingWorkingSet);
            return new ParsedOrchardContactWorkbook(
                Path.GetFileName(fileName),
                checksum,
                AuthoritativeWorksheet,
                sourceRowCount,
                tokens);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch (IOException exception)
            {
                logger?.LogWarning(exception, "Temporary orchard-contact workbook could not be removed immediately.");
            }
            catch (UnauthorizedAccessException exception)
            {
                logger?.LogWarning(exception, "Temporary orchard-contact workbook could not be removed immediately.");
            }
        }
    }

    public static IReadOnlyList<string> SplitOrchards(string? value) =>
        (value ?? "")
            .Split([',', '/'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .ToArray();

    private static Dictionary<int, string> ReadRow(Row row, SharedStringTable? sharedStrings)
    {
        var result = new Dictionary<int, string>();
        foreach (var cell in row.Elements<Cell>())
        {
            var reference = cell.CellReference?.Value;
            if (string.IsNullOrWhiteSpace(reference)) continue;
            result[ColumnIndex(reference)] = ReadCell(cell, sharedStrings);
        }

        return result;
    }

    private static string ReadCell(Cell cell, SharedStringTable? sharedStrings)
    {
        if (cell.DataType?.Value == CellValues.SharedString
            && int.TryParse(cell.CellValue?.InnerText, out var sharedIndex))
        {
            return sharedStrings?.Elements<SharedStringItem>().ElementAtOrDefault(sharedIndex)?.InnerText ?? "";
        }

        if (cell.DataType?.Value == CellValues.InlineString) return cell.InlineString?.InnerText ?? "";
        return cell.CellValue?.InnerText ?? cell.InnerText ?? "";
    }

    private static int ColumnIndex(string cellReference)
    {
        var index = 0;
        foreach (var ch in cellReference)
        {
            if (!char.IsLetter(ch)) break;
            index = index * 26 + char.ToUpperInvariant(ch) - 'A' + 1;
        }

        return index - 1;
    }

    private static string ColumnName(int index)
    {
        var result = new StringBuilder();
        while (index > 0)
        {
            index--;
            result.Insert(0, (char)('A' + index % 26));
            index /= 26;
        }

        return result.ToString();
    }

    private static string Clean(string? value) => RepeatedWhitespace().Replace(value?.Trim() ?? "", " ");
    private static string? NormalizeEmail(string value) => NullIfEmpty(value)?.ToLowerInvariant();
    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    [GeneratedRegex("\\s+")]
    private static partial Regex RepeatedWhitespace();
}

public static partial class OrchardContactNormalization
{
    public static string NormalizeOrchardIdentity(string? value)
    {
        var normalized = OrchardBlockMatcher.Normalize(
            value?.Replace('’', '\'').Replace('–', '-').Replace('—', '-'));
        return PossessiveSpacing().Replace(normalized, "$1S");
    }

    public static string NormalizePersonName(string? value) =>
        RepeatedWhitespace().Replace(value?.Trim().ToUpperInvariant() ?? "", " ");

    public static (string? Display, string? Normalized) NormalizePhone(string? value)
    {
        var source = value?.Trim();
        if (string.IsNullOrWhiteSpace(source)) return (null, null);
        var digits = new string(source.Where(char.IsDigit).ToArray());
        if (digits.Length == 11 && digits[0] == '1') digits = digits[1..];
        if (digits.Length != 10) return (source, digits.Length == 0 ? null : digits);
        return ($"({digits[..3]}) {digits[3..6]}-{digits[6..]}", digits);
    }

    public static string WithoutOrchardWord(string normalized) =>
        RepeatedWhitespace().Replace(OrchardWord().Replace(normalized, " "), " ").Trim();

    public static string? ParentheticalAddressEvidence(string? physicalAddress)
    {
        var match = Parenthetical().Match(physicalAddress ?? "");
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    [GeneratedRegex("\\s+")]
    private static partial Regex RepeatedWhitespace();

    [GeneratedRegex("\\bORCHARD\\b", RegexOptions.CultureInvariant)]
    private static partial Regex OrchardWord();

    [GeneratedRegex("\\(([^()]*)\\)\\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex Parenthetical();

    [GeneratedRegex("\\b([A-Z]+) S\\b", RegexOptions.CultureInvariant)]
    private static partial Regex PossessiveSpacing();
}
