using System.IO.Compression;
using System.Xml.Linq;
using CropQc.Web.Services;

namespace CropQc.Api.Tests;

public sealed class TruckReceiptReceivingReadTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Export_uses_each_varietys_quantity_and_room_provenance_excludes_receiving_evidence(bool completed)
    {
        await using var f = await TruckReceiptReconciliationTests.Fixture.CreateAsync();
        var load = await f.DispatchAsync(70); await f.AddSecondVarietyAsync(load.Id, 30);
        var receipt = await f.CreateReceiptAsync(100);
        var form = await f.FormAsync(receipt.Id, load.Id);
        form.Lines = [new() { FruitProfileId = f.First.Id, BinCount = 70 }, new() { FruitProfileId = f.Second.Id, BinCount = 30 }];
        Assert.Null(await f.Service.EditReceiptAsync(form, default));
        Assert.Null(await f.Service.MatchAsync(await f.FormAsync(receipt.Id, load.Id), default));
        if (completed) Assert.Null(await f.Service.CompleteAsync(await f.FormAsync(receipt.Id, load.Id), default));
        var bytes = await new ReceivingExportService(f.Db).ExportReceivingDataAsync(default);
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        using var sheet = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var xml = XDocument.Load(sheet);
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var rows = xml.Descendants(ns + "row").Select(row => row.Elements(ns + "c")
            .Select(cell => cell.Descendants(ns + "t").FirstOrDefault()?.Value ?? "").ToArray()).ToList();
        var received = rows.Where(row => row[0] == receipt.CompuTechReceiptId).ToList();
        Assert.Equal(2, received.Count);
        Assert.Equal("70", Assert.Single(received, row => row[8] == f.First.VarietyCode)[11]);
        Assert.Equal("30", Assert.Single(received, row => row[8] == f.Second.VarietyCode)[11]);
        Assert.All(received, row => Assert.Equal(completed ? "Completed transfer reconciliation" : "Pending transfer reconciliation", row[32]));
        Assert.Contains(rows, row => row[8] == f.First.VarietyCode && row[11] == "300" && row[32] == "");
        var room = await f.Dashboard.GetRoomDetailAsync(f.Destination.Id, default);
        Assert.DoesNotContain(room.LikelySourceReceipts, x => x.ReceiptId == receipt.Id);
        Assert.Equal(completed ? 70 : 0, await f.BalanceAsync(f.Destination.Id));
    }
}
