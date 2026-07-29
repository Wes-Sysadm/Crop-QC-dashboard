using System.Globalization;
using System.IO.Compression;
using System.Security;
using CropQc.Data;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IReceivingExportService
{
    Task<byte[]> ExportReceivingDataAsync(CancellationToken cancellationToken);
}

public sealed class ReceivingExportService(CropQcDbContext dbContext) : IReceivingExportService
{
    public async Task<byte[]> ExportReceivingDataAsync(CancellationToken cancellationToken)
    {
        var receipts = await dbContext.Receipts.AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.Room)
            .Include(x => x.FruitProfile)
            .Include(x => x.Samples).ThenInclude(x => x.SampleType)
            .Include(x => x.Samples).ThenInclude(x => x.FruitReadings).ThenInclude(x => x.Grade)
            .Include(x => x.Samples).ThenInclude(x => x.FruitReadings).ThenInclude(x => x.StarchScaleValue)
            .Include(x => x.Samples).ThenInclude(x => x.FruitReadings).ThenInclude(x => x.Defects).ThenInclude(x => x.DefectType)
            .OrderByDescending(x => x.ReceivedAt)
            .ToListAsync(cancellationToken);

        var rows = new List<IReadOnlyList<string?>>();
        rows.Add(Header());

        foreach (var receipt in receipts)
        {
            if (receipt.Samples.Count == 0)
            {
                rows.Add(ReceiptRow(receipt, null, null));
                continue;
            }

            foreach (var sample in receipt.Samples.OrderBy(x => x.SampleSequenceNumber))
            {
                var readings = sample.FruitReadings.OrderBy(x => x.RowNumber).ToList();
                if (readings.Count == 0)
                {
                    rows.Add(ReceiptRow(receipt, sample, null));
                    continue;
                }

                foreach (var reading in readings)
                {
                    rows.Add(ReceiptRow(receipt, sample, reading));
                }
            }
        }

        return CreateWorkbook(rows);
    }

    private static IReadOnlyList<string?> Header() =>
    [
        "Receipt ID", "Display Sample ID", "Crop Year", "Received Date/Time", "Warehouse", "Room", "Grower", "Lot",
        "Variety Code", "Variety Description", "Commodity", "Bin Count", "Sample Type", "Sample Status",
        "Starch Status", "Photo Status", "Email Status", "Defect Inspection Status", "Sample Taken At", "Actual Sample Size",
        "Row Number", "Pressure 1", "Pressure 2", "Average Pressure", "Weight Grams", "Calculated Size",
        "Size Status", "Grade", "Starch", "Defects", "Other Defect Notes", "Ready/Missing Status"
    ];

    private static IReadOnlyList<string?> ReceiptRow(CropQc.Data.Entities.Receipt receipt, CropQc.Data.Entities.QcSample? sample, CropQc.Data.Entities.QcFruitReading? reading)
    {
        var displaySampleId = sample is null
            ? null
            : sample.SampleSequenceNumber <= 1 ? receipt.CompuTechReceiptId : $"{receipt.CompuTechReceiptId}({sample.SampleSequenceNumber})";
        var defects = reading is null
            ? null
            : string.Join("; ", reading.Defects.Select(x => x.DefectType.Name).OrderBy(x => x));
        var otherNotes = reading?.Defects.FirstOrDefault(x => x.DefectType.Name == "Other")?.Notes;
        var average = reading?.Pressure1Lbs is null || reading.Pressure2Lbs is null
            ? null
            : decimal.Round((reading.Pressure1Lbs.Value + reading.Pressure2Lbs.Value) / 2m, 2).ToString(CultureInfo.InvariantCulture);
        var readyStatus = sample is null
            ? null
            : sample.EmailStatus == "Sent" ? "Sent" : sample.Status;

        return
        [
            receipt.CompuTechReceiptId,
            displaySampleId,
            receipt.CropYear.ToString(CultureInfo.InvariantCulture),
            receipt.ReceivedAt.ToString("u", CultureInfo.InvariantCulture),
            receipt.Warehouse.Code,
            receipt.Room.Code,
            receipt.GrowerName,
            receipt.LotCode,
            receipt.FruitProfile.VarietyCode,
            receipt.FruitProfile.Name,
            receipt.FruitProfile.FruitType,
            receipt.BinCount.ToString(CultureInfo.InvariantCulture),
            sample?.SampleType.Name,
            sample?.Status,
            sample?.StarchStatus,
            sample?.PhotoStatus,
            sample?.EmailStatus,
            sample?.DefectInspectionStatus,
            sample?.SampleTakenAt.ToString("u", CultureInfo.InvariantCulture),
            sample?.ActualSampleSize?.ToString(CultureInfo.InvariantCulture),
            reading?.RowNumber.ToString(CultureInfo.InvariantCulture),
            reading?.Pressure1Lbs?.ToString(CultureInfo.InvariantCulture),
            reading?.Pressure2Lbs?.ToString(CultureInfo.InvariantCulture),
            average,
            reading?.WeightGrams?.ToString(CultureInfo.InvariantCulture),
            reading?.SizeCategory?.ToString(CultureInfo.InvariantCulture),
            reading?.SizeStatus,
            reading?.Grade?.Code,
            reading?.StarchScaleValue?.Value.ToString("0.0", CultureInfo.InvariantCulture),
            defects,
            otherNotes,
            readyStatus
        ];
    }

    private static byte[] CreateWorkbook(IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            AddEntry(archive, "[Content_Types].xml", ContentTypesXml());
            AddEntry(archive, "_rels/.rels", RootRelsXml());
            AddEntry(archive, "xl/workbook.xml", WorkbookXml());
            AddEntry(archive, "xl/_rels/workbook.xml.rels", WorkbookRelsXml());
            AddEntry(archive, "xl/worksheets/sheet1.xml", WorksheetXml(rows));
            AddEntry(archive, "xl/styles.xml", StylesXml());
        }

        return stream.ToArray();
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var writer = new StreamWriter(entry.Open());
        writer.Write(content);
    }

    private static string WorksheetXml(IReadOnlyList<IReadOnlyList<string?>> rows)
    {
        var sheetRows = rows.Select((row, rowIndex) =>
        {
            var cells = row.Select((value, columnIndex) =>
            {
                var reference = $"{ColumnName(columnIndex + 1)}{rowIndex + 1}";
                return $"""<c r="{reference}" t="inlineStr"><is><t>{SecurityElement.Escape(value ?? string.Empty)}</t></is></c>""";
            });
            return $"""<row r="{rowIndex + 1}">{string.Concat(cells)}</row>""";
        });

        return $"""<?xml version="1.0" encoding="UTF-8" standalone="yes"?><worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetData>{string.Concat(sheetRows)}</sheetData></worksheet>""";
    }

    private static string ColumnName(int columnNumber)
    {
        var name = string.Empty;
        while (columnNumber > 0)
        {
            var modulo = (columnNumber - 1) % 26;
            name = Convert.ToChar('A' + modulo) + name;
            columnNumber = (columnNumber - modulo) / 26;
        }

        return name;
    }

    private static string ContentTypesXml() =>
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>""";

    private static string RootRelsXml() =>
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>""";

    private static string WorkbookXml() =>
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Receiving Data" sheetId="1" r:id="rId1"/></sheets></workbook>""";

    private static string WorkbookRelsXml() =>
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>""";

    private static string StylesXml() =>
        """<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts><fills count="1"><fill><patternFill patternType="none"/></fill></fills><borders count="1"><border/></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs></styleSheet>""";
}
