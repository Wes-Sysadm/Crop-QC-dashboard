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

public sealed class PackoutFeedbackWorkbookService : IPackoutFeedbackWorkbookService
{
    public byte[] Build(PackoutRun run)
    {
        var rows = new List<IReadOnlyList<string?>>
        {
            new string?[] { "Crop QC Packout Feedback" },
            new string?[] { "Projection", run.RunProjection.Name },
            new string?[] { "Facility", run.FacilitySnapshot },
            new string?[] { "Packing date", run.PackingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) },
            new string?[] { "Run number", run.RunNumber.ToString(CultureInfo.InvariantCulture) },
            new string?[] { "Crop year", run.CropYearSnapshot.ToString(CultureInfo.InvariantCulture) },
            new string?[] { "Lot", run.LotNumberSnapshot },
            new string?[] { "Variety", run.VarietySnapshot },
            new string?[] { "Production", run.IsOrganicSnapshot ? "Organic" : "Conventional" },
            new string?[] { "Dumped bins", Format(run.DumpedBins) },
            new string?[] { "Pounds per bin", Format(run.PoundsPerBin) },
            new string?[] { "Dumped pounds", Format(run.DumpedPounds) },
            new string?[] { "Packed-product pounds", Format(run.PackedProductPounds) },
            new string?[] { "Actual packout %", Format(run.ActualPackoutPercent) },
            new string?[] { "Juice pounds", Format(run.JuicePounds) },
            new string?[] { "Peeler/Slicer pounds", Format(run.PeelerSlicerPounds) },
            new string?[] { "Waste pounds", Format(run.WastePounds) },
            new string?[] { "Overall accuracy score", Format(run.OverallAccuracyScore) },
            new string?[] { "Reconciliation difference pounds", Format(run.ReconciliationDifferencePounds) },
            new string?[] { "Reconciliation warning", run.HasReconciliationWarning ? "Yes - exceeds 10% of dumped pounds" : "No" },
            new string?[] { "Calculation version", run.CalculationVersion },
            Array.Empty<string?>(),
            new string?[] { "Projection source snapshots" },
            new string?[] { "Source ID", "Grower", "Lot", "Room", "Variety", "Current remaining bins", "Projected bins", "Size fruit", "Grade fruit", "Joint fruit", "Defect %", "Bins Run ID", "Receipt IDs", "Sample IDs" }
        };
        foreach (var source in run.RunProjection.Sources.OrderBy(x => x.SortOrder).ThenBy(x => x.Id))
        {
            rows.Add(new string?[]
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
            });
        }
        rows.Add(Array.Empty<string?>());
        rows.Add(new string?[] { "Accuracy components" });
        rows.Add(new string?[] { "Size", Format(run.SizeAccuracyScore) });
        rows.Add(new string?[] { "Grade", Format(run.GradeAccuracyScore) });
        rows.Add(new string?[] { "Packout", Format(run.PackoutAccuracyScore) });
        rows.Add(new string?[] { "Juice", Format(run.JuiceAccuracyScore) });
        rows.Add(new string?[] { "Peeler/Slicer", Format(run.PeelerSlicerAccuracyScore) });
        rows.Add(new string?[] { "Waste", Format(run.WasteAccuracyScore) });
        rows.Add(Array.Empty<string?>());
        rows.AddRange(new IReadOnlyList<string?>[]
        {
            new string?[] { "Parsed actual lines" },
            new string?[] { "Source file", "Line", "Raw pack code", "Quantity", "Net lb", "Extended lb", "Size", "Grade", "Category", "Confidence", "Corrected" }
        });

        foreach (var line in run.Lines.OrderBy(x => x.PackoutReportSourceId).ThenBy(x => x.SourceLineNumber))
        {
            rows.Add(new string?[]
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
            });
        }

        rows.Add(Array.Empty<string?>());
        rows.Add(new string?[] { "Source files (original files are not retained after parsing)" });
        rows.Add(new string?[] { "Filename", "SHA-256", "Parser", "Confidence", "Parsed at" });
        foreach (var source in run.Sources.OrderBy(x => x.Id))
        {
            rows.Add(new string?[]
            {
                source.OriginalFileName,
                source.Sha256,
                $"{source.ParserName} {source.ParserVersion}".Trim(),
                Format(source.Confidence is null ? null : source.Confidence * 100m),
                source.ParsedAt.ToString("u", CultureInfo.InvariantCulture)
            });
        }

        return Workbook(rows);
    }

    private static string? Format(decimal? value) =>
        value?.ToString("0.####", CultureInfo.InvariantCulture);

    private static byte[] Workbook(IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(archive, "[Content_Types].xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>""");
            Add(archive, "_rels/.rels", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""");
            Add(archive, "xl/workbook.xml", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Packout Feedback" sheetId="1" r:id="rId1"/></sheets></workbook>""");
            Add(archive, "xl/_rels/workbook.xml.rels", """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/></Relationships>""");
            Add(archive, "xl/worksheets/sheet1.xml", Worksheet(rows));
        }

        return stream.ToArray();
    }

    private static string Worksheet(IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        var xml = new StringBuilder("""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>""");
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            xml.Append($"<row r=\"{rowIndex + 1}\">");
            for (var columnIndex = 0; columnIndex < rows[rowIndex].Count; columnIndex++)
            {
                var value = SecurityElement.Escape(rows[rowIndex][columnIndex] ?? "");
                xml.Append($"<c r=\"{Column(columnIndex + 1)}{rowIndex + 1}\" t=\"inlineStr\"><is><t>{value}</t></is></c>");
            }

            xml.Append("</row>");
        }

        xml.Append("</sheetData></worksheet>");
        return xml.ToString();
    }

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
        using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
        writer.Write(content);
    }
}
