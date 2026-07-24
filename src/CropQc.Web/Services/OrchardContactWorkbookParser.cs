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

    public async Task<ParsedOrchardContactWorkbook> ParseAsync(Stream source, string fileName, CancellationToken cancellationToken)
    {
        if (!string.Equals(Path.GetExtension(fileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("Upload an XLSX workbook. Legacy XLS files are not accepted by this reviewed import.");
        }

        await using var copy = new MemoryStream();
        await source.CopyToAsync(copy, cancellationToken);
        if (copy.Length == 0) throw new InvalidDataException("The uploaded workbook is empty.");
        if (copy.Length > 25 * 1024 * 1024) throw new InvalidDataException("The workbook exceeds the 25 MB import limit.");

        var bytes = copy.ToArray();
        var checksum = Convert.ToHexString(SHA256.HashData(bytes));
        using var workbook = SpreadsheetDocument.Open(new MemoryStream(bytes, writable: false), false);
        var workbookPart = workbook.WorkbookPart ?? throw new InvalidDataException("The workbook package is missing its workbook definition.");
        var sheet = workbookPart.Workbook.Sheets?.Elements<Sheet>()
            .SingleOrDefault(x => string.Equals(x.Name?.Value, AuthoritativeWorksheet, StringComparison.Ordinal));
        if (sheet?.Id?.Value is null)
        {
            throw new InvalidDataException($"Worksheet '{AuthoritativeWorksheet}' was not found.");
        }

        var worksheetPart = (WorksheetPart)workbookPart.GetPartById(sheet.Id.Value);
        var rows = worksheetPart.Worksheet.GetFirstChild<SheetData>()?.Elements<Row>().ToList() ?? [];
        if (rows.Count == 0) throw new InvalidDataException($"Worksheet '{AuthoritativeWorksheet}' is empty.");

        var sharedStrings = workbookPart.SharedStringTablePart?.SharedStringTable;
        var header = ReadRow(rows[0], sharedStrings);
        for (var column = 0; column < RequiredHeaders.Length; column++)
        {
            if (!string.Equals(Clean(header.GetValueOrDefault(column)), RequiredHeaders[column], StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"Worksheet '{AuthoritativeWorksheet}' column {ColumnName(column + 1)} must be '{RequiredHeaders[column]}'.");
            }
        }

        var tokens = new List<ParsedOrchardManagerToken>();
        var sourceRowCount = 0;
        foreach (var row in rows.Skip(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cells = ReadRow(row, sharedStrings);
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

        return new ParsedOrchardContactWorkbook(
            Path.GetFileName(fileName),
            checksum,
            AuthoritativeWorksheet,
            sourceRowCount,
            tokens);
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
