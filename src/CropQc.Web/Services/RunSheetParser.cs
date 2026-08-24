using System.Globalization;
using System.Text;
using CropQc.Data.Entities;

namespace CropQc.Web.Services;

public sealed class RunSheetConfigurationException(string message) : InvalidOperationException(message);

public static class RunSheetParser
{
    public const string OrganicProductionType = "Organic";
    public const string ConventionalProductionType = "Conventional";
    private static readonly CultureInfo SheetCulture = CultureInfo.GetCultureInfo("en-US");

    public static IReadOnlyList<ExternalPhysicalRun> ParseWorksheet(
        string facility,
        IReadOnlyList<IReadOnlyList<object?>> values,
        RunSheetReconciliationOptions options)
    {
        if (facility is not (EmploymentFacilities.Ebs or EmploymentFacilities.Wp))
        {
            throw new ArgumentOutOfRangeException(nameof(facility), "Only WP and EBS run sheets are supported.");
        }

        var headerIndex = FindHeaderRow(values, facility, options.BoundedHeaderSearchRows);
        if (headerIndex < 0)
        {
            throw new RunSheetConfigurationException($"The {facility} worksheet does not contain the required run headers in the first {options.BoundedHeaderSearchRows} rows.");
        }

        var columns = BuildColumnMap(values[headerIndex]);
        Require(columns, "DATE", facility);
        Require(columns, "BINSDUMPED", facility);
        Require(columns, "GROWERNUMBER", facility);
        Require(columns, "VARIETY", facility);
        Require(columns, "CATEGORY", facility);
        if (facility == EmploymentFacilities.Wp)
        {
            Require(columns, "SALES", facility);
        }

        var parsed = new List<ExternalRunSheetRow>();
        foreach (var row in values.Skip(headerIndex + 1).Take(options.BoundedMaximumRows - headerIndex - 1))
        {
            if (!TryDate(Value(row, columns["DATE"]), options.CropYear, out var date)
                || date.Year != options.CropYear
                || !TryPositiveBins(Value(row, columns["BINSDUMPED"]), out var bins))
            {
                continue;
            }

            var growerNumber = NormalizeGrowerNumber(Value(row, columns["GROWERNUMBER"]));
            var variety = NormalizeCode(Value(row, columns["VARIETY"]));
            if (string.IsNullOrWhiteSpace(growerNumber) || string.IsNullOrWhiteSpace(variety))
            {
                continue;
            }

            var category = NormalizeCode(Value(row, columns["CATEGORY"]));
            var productionType = NormalizeProductionType(category);
            var salesCode = facility == EmploymentFacilities.Wp
                ? NormalizeCode(Value(row, columns["SALES"]))
                : "";
            var salesDesk = facility == EmploymentFacilities.Wp
                ? ResolveSalesDesk(salesCode, options)
                : null;
            var unknownSalesCode = facility == EmploymentFacilities.Wp && salesDesk is null
                ? (string.IsNullOrWhiteSpace(salesCode) ? "(blank)" : salesCode)
                : null;

            parsed.Add(new ExternalRunSheetRow(
                facility,
                date,
                bins,
                growerNumber,
                columns.TryGetValue("GROWERNAME", out var growerNameColumn) ? Text(Value(row, growerNameColumn)) : "",
                variety,
                productionType,
                salesDesk,
                unknownSalesCode,
                columns.TryGetValue("POOL", out var poolColumn) ? Text(Value(row, poolColumn)) : ""));
        }

        return parsed
            .GroupBy(x => new
            {
                x.Facility,
                x.Date,
                x.Variety,
                x.ProductionType,
                SalesDeskKey = x.SalesDesk ?? $"UNKNOWN:{x.UnknownSalesDeskCode}"
            })
            .Select(group => new ExternalPhysicalRun(
                group.Key.Facility,
                group.Key.Date,
                group.Key.Variety,
                group.Key.ProductionType,
                group.First().SalesDesk,
                group.First().UnknownSalesDeskCode,
                group.Sum(x => x.Bins),
                group.GroupBy(x => x.GrowerNumber, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(x => x.Key, x => x.Sum(y => y.Bins), StringComparer.OrdinalIgnoreCase)))
            .OrderBy(x => x.Facility, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Date)
            .ThenBy(x => x.Variety, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.ProductionType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SalesDesk ?? x.UnknownSalesDeskCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string NormalizeProductionType(string? category)
    {
        var normalized = NormalizeCode(category);
        return normalized.Contains("ORG", StringComparison.OrdinalIgnoreCase)
            ? OrganicProductionType
            : ConventionalProductionType;
    }

    public static string NormalizeGrowerNumber(object? value)
    {
        var text = Text(value).Replace(",", "", StringComparison.Ordinal).Trim();
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number)
            && number == decimal.Truncate(number))
        {
            return number.ToString("0", CultureInfo.InvariantCulture);
        }

        return NormalizeCode(text);
    }

    public static string NormalizeCode(object? value) =>
        string.Join(' ', Text(value)
            .Trim()
            .ToUpperInvariant()
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static int FindHeaderRow(
        IReadOnlyList<IReadOnlyList<object?>> values,
        string facility,
        int maximumRows)
    {
        var required = facility == EmploymentFacilities.Wp
            ? new[] { "DATE", "BINSDUMPED", "GROWERNUMBER", "VARIETY", "CATEGORY", "SALES" }
            : new[] { "DATE", "BINSDUMPED", "GROWERNUMBER", "VARIETY", "CATEGORY" };
        for (var index = 0; index < Math.Min(values.Count, maximumRows); index++)
        {
            var columns = BuildColumnMap(values[index]);
            if (required.All(columns.ContainsKey))
            {
                return index;
            }
        }

        return -1;
    }

    private static Dictionary<string, int> BuildColumnMap(IReadOnlyList<object?> row)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < row.Count; index++)
        {
            var header = NormalizeHeader(row[index]);
            if (!string.IsNullOrWhiteSpace(header))
            {
                result.TryAdd(header, index);
            }
        }

        return result;
    }

    private static string NormalizeHeader(object? value)
    {
        var text = Text(value).Trim().ToUpperInvariant().Replace("#", "NUMBER", StringComparison.Ordinal);
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString() switch
        {
            "CATIGORY" => "CATEGORY",
            "POOLCODE" => "POOL",
            "GROWER" => "GROWERNAME",
            _ => builder.ToString()
        };
    }

    private static void Require(IReadOnlyDictionary<string, int> columns, string key, string facility)
    {
        if (!columns.ContainsKey(key))
        {
            throw new RunSheetConfigurationException($"The {facility} worksheet is missing required header {key}.");
        }
    }

    private static object? Value(IReadOnlyList<object?> row, int index) => index < row.Count ? row[index] : null;

    private static string Text(object? value) => Convert.ToString(value, SheetCulture) ?? "";

    private static bool TryPositiveBins(object? value, out int bins)
    {
        var text = Text(value).Trim();
        if (decimal.TryParse(text, NumberStyles.Number, SheetCulture, out var parsed)
            && parsed > 0
            && parsed == decimal.Truncate(parsed)
            && parsed <= int.MaxValue)
        {
            bins = decimal.ToInt32(parsed);
            return true;
        }

        bins = 0;
        return false;
    }

    private static bool TryDate(object? value, int cropYear, out DateOnly date)
    {
        if (value is DateTime dateTime)
        {
            date = DateOnly.FromDateTime(dateTime);
            return true;
        }
        if (value is DateTimeOffset offset)
        {
            date = DateOnly.FromDateTime(offset.DateTime);
            return true;
        }

        var text = Text(value).Trim();
        foreach (var format in new[] { "M/d", "MM/dd" })
        {
            if (DateTime.TryParseExact(text, format, SheetCulture, DateTimeStyles.None, out var monthDay))
            {
                date = new DateOnly(cropYear, monthDay.Month, monthDay.Day);
                return true;
            }
        }
        if (DateOnly.TryParse(text, SheetCulture, DateTimeStyles.AllowWhiteSpaces, out date))
        {
            return true;
        }

        date = default;
        return false;
    }

    private static string? ResolveSalesDesk(string salesCode, RunSheetReconciliationOptions options) =>
        options.SalesDeskMappings.TryGetValue(salesCode, out var desk) && !string.IsNullOrWhiteSpace(desk)
            ? desk.Trim()
            : null;
}
