using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using CropQc.Data.Entities;

namespace CropQc.Web.Services;

public interface IPackoutFeedbackWorkbookService
{
    byte[] Build(PackoutRun run);
}

public sealed class PackoutFeedbackWorkbookService(
    PackoutProcessingOptions options,
    ILogger<PackoutFeedbackWorkbookService> logger) : IPackoutFeedbackWorkbookService
{
    public byte[] Build(PackoutRun run)
    {
        var stopwatch = Stopwatch.StartNew();
        var startingWorkingSet = Environment.WorkingSet;
        using var stream = new MemoryStream();
        int rowCount;
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "[Content_Types].xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>""");
            Add(archive, "_rels/.rels", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            Add(archive, "xl/workbook.xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Packout Feedback" sheetId="1" r:id="rId1"/></sheets></workbook>""");
            Add(archive, "xl/_rels/workbook.xml.rels", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>""");
            rowCount = AddWorksheet(archive, Rows(run));
        }

        var result = stream.ToArray();
        stopwatch.Stop();
        logger.LogInformation(
            "Packout Excel generation completed. Packout run {PackoutRunId}; Excel row count {ExcelRowCount}; output bytes {OutputBytes}; elapsed ms {ElapsedMilliseconds}; working set delta bytes {WorkingSetDeltaBytes}.",
            run.Id,
            rowCount,
            result.LongLength,
            stopwatch.ElapsedMilliseconds,
            Environment.WorkingSet - startingWorkingSet);
        return result;
    }

    private static IEnumerable<IReadOnlyList<string?>> Rows(PackoutRun run)
    {
        yield return new string?[] { "Crop QC Packout Feedback" };
        yield return new string?[] { "Projection", run.RunProjection.Name };
        yield return new string?[] { "Facility", run.FacilitySnapshot };
        yield return new string?[] { "Packing date", run.PackingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
        yield return new string?[] { "Run number", run.RunNumber.ToString(CultureInfo.InvariantCulture) };
        yield return new string?[] { "Crop year", run.CropYearSnapshot.ToString(CultureInfo.InvariantCulture) };
        yield return new string?[] { "Lot", run.LotNumberSnapshot };
        yield return new string?[] { "Variety", run.VarietySnapshot };
        yield return new string?[] { "Production", run.IsOrganicSnapshot ? "Organic" : "Conventional" };
        yield return new string?[] { "Dumped bins", Format(run.DumpedBins) };
        yield return new string?[] { "Pounds per bin", Format(run.PoundsPerBin) };
        yield return new string?[] { "Dumped pounds", Format(run.DumpedPounds) };
        yield return new string?[] { "Packed-product pounds", Format(run.PackedProductPounds) };
        yield return new string?[] { "Actual packout %", Format(run.ActualPackoutPercent) };
        yield return new string?[] { "Juice pounds", Format(run.JuicePounds) };
        yield return new string?[] { "Peeler/Slicer pounds", Format(run.PeelerSlicerPounds) };
        yield return new string?[] { "Waste pounds", Format(run.WastePounds) };
        yield return new string?[] { "Overall accuracy score", Format(run.OverallAccuracyScore) };
        yield return new string?[] { "Reconciliation difference pounds", Format(run.ReconciliationDifferencePounds) };
        yield return new string?[] { "Reconciliation warning", run.HasReconciliationWarning ? "Yes - exceeds 10% of dumped pounds" : "No" };
        yield return new string?[] { "Calculation version", run.CalculationVersion };
        yield return Array.Empty<string?>();
        yield return new string?[] { "Projection source snapshots" };
        yield return new string?[] { "Source ID", "Grower", "Lot", "Room", "Variety", "Current remaining bins", "Projected bins", "Size fruit", "Grade fruit", "Joint fruit", "Defect %", "Bins Run ID", "Receipt IDs", "Sample IDs" };
        foreach (var source in run.RunProjection.Sources.OrderBy(x => x.SortOrder).ThenBy(x => x.Id))
        {
            yield return new string?[]
            {
                source.Id.ToString(CultureInfo.InvariantCulture),
                source.GrowerSnapshot,
                source.LotSnapshot,
                source.RoomSnapshot,
                source.VarietySnapshot,
                source.AvailableBinsSnapshot?.ToString(CultureInfo.InvariantCulture),
                source.PlannedBins.ToString(CultureInfo.InvariantCulture),
                source.SizeBasisFruitCount.ToString(CultureInfo.InvariantCulture),
                source.GradeBasisFruitCount.ToString(CultureInfo.InvariantCulture),
                source.JointSizeGradeBasisFruitCount.ToString(CultureInfo.InvariantCulture),
                Format(source.TotalDefectPercentageSnapshot),
                source.ActualBinsRunEntryId?.ToString(CultureInfo.InvariantCulture),
                source.ContributingReceiptIdsJson,
                source.ContributingSampleIdsJson
            };
        }
        yield return Array.Empty<string?>();
        yield return new string?[] { "Accuracy components" };
        yield return new string?[] { "Size", Format(run.SizeAccuracyScore) };
        yield return new string?[] { "Grade", Format(run.GradeAccuracyScore) };
        yield return new string?[] { "Packout", Format(run.PackoutAccuracyScore) };
        yield return new string?[] { "Juice", Format(run.JuiceAccuracyScore) };
        yield return new string?[] { "Peeler/Slicer", Format(run.PeelerSlicerAccuracyScore) };
        yield return new string?[] { "Waste", Format(run.WasteAccuracyScore) };
        yield return Array.Empty<string?>();
        yield return new string?[] { "Parsed actual lines" };
        yield return new string?[] { "Source file", "Line", "Raw pack code", "Quantity", "Net lb", "Extended lb", "Size", "Grade", "Category", "Confidence", "Corrected" };
        foreach (var line in run.Lines.OrderBy(x => x.PackoutReportSourceId).ThenBy(x => x.SourceLineNumber))
        {
            yield return new string?[]
            {
                line.PackoutReportSource?.OriginalFileName,
                line.SourceLineNumber.ToString(CultureInfo.InvariantCulture),
                line.RawPackCode,
                Format(line.Quantity),
                Format(line.NetWeightPounds),
                Format(line.ExtendedWeightPounds),
                line.SizeCategory?.ToString(CultureInfo.InvariantCulture),
                line.Grade?.Code,
                line.ProductCategory,
                Format(line.Confidence * 100m),
                line.WasCorrected ? "Yes" : "No"
            };
        }
        yield return Array.Empty<string?>();
        yield return new string?[] { "Source files (original files are not retained after parsing)" };
        yield return new string?[] { "Filename", "SHA-256", "Parser", "Confidence", "Parsed at" };
        foreach (var source in run.Sources.OrderBy(x => x.Id))
        {
            yield return new string?[]
            {
                source.OriginalFileName,
                source.Sha256,
                $"{source.ParserName} {source.ParserVersion}".Trim(),
                Format(source.Confidence is null ? null : source.Confidence * 100m),
                source.ParsedAt.ToString("u", CultureInfo.InvariantCulture)
            };
        }
    }

    private int AddWorksheet(ZipArchive archive, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var entry = archive.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 32 * 1024);
        writer.Write("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");
        var rowIndex = 0;
        foreach (var row in rows)
        {
            rowIndex++;
            if (rowIndex > options.MaximumWorkbookRows)
            {
                throw new InvalidOperationException($"Packout feedback workbooks may contain at most {options.MaximumWorkbookRows:N0} rows.");
            }
            writer.Write($"<row r=\"{rowIndex}\">");
            for (var columnIndex = 0; columnIndex < row.Count; columnIndex++)
            {
                var value = SecurityElement.Escape(row[columnIndex] ?? "");
                writer.Write($"<c r=\"{Column(columnIndex + 1)}{rowIndex}\" t=\"inlineStr\"><is><t>{value}</t></is></c>");
            }
            writer.Write("</row>");
        }
        writer.Write("</sheetData></worksheet>");
        return rowIndex;
    }

    private static string? Format(decimal? value) =>
        value?.ToString("0.####", CultureInfo.InvariantCulture);

    private static string Column(int value)
    {
        var result = "";
        while (value > 0)
        {
            value--;
            result = (char)('A' + value % 26) + result;
            value /= 26;
        }
        return result;
    }

    private static void Add(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }
}
