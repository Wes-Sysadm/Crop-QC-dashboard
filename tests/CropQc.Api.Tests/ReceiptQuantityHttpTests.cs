using System.Net;
using System.Text.RegularExpressions;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Api.Tests;

public sealed class ReceiptQuantityHttpTests
{
    [InventoryPostgresFact]
    public async Task Buffer_review_and_normal_admin_post_correct_only_the_selected_receipt()
    {
        await using var f = await ReceiptQuantityCorrectionTests.Create(true);
        await using var db = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection).CreateDbContext();
        (await db.Users.SingleAsync(x => x.Id == 8000)).Email = ApplicationAreas.OwnerEmail;
        await db.SaveChangesAsync();
        var id = await ReceiptQuantityCorrectionTests.Target(db);
        var r = await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == id);
        await using var host = new ReceiptLocationHttpTests.ReceiptHost(f.Connection);
        using var client = host.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        var response = await client.GetAsync($"/Receipts/{id}/Edit");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.Contains("Review Bin Count Override", html); Assert.Contains("buffer bins included", html);
        Assert.Contains("Removed from selected current inventory", html); Assert.Contains("35 bins", html);
        Assert.Contains("Untreated", html); Assert.Contains("Bins to correct", html);
        Assert.DoesNotContain("ownership cannot be proven", html);
        var hidden = Regex.Matches(html, "<input[^>]*type=\"hidden\"[^>]*>").Select(x => x.Value)
            .Select(x => new { Name = Regex.Match(x, "name=\"([^\"]*)\"").Groups[1].Value, Value = WebUtility.HtmlDecode(Regex.Match(x, "value=\"([^\"]*)\"").Groups[1].Value) })
            .Where(x => x.Name.Length > 0).GroupBy(x => x.Name).ToDictionary(x => x.Key, x => x.First().Value);
        hidden["CropYear"] = "2026"; hidden["ConfirmCropYear"] = "true"; hidden["CompuTechReceiptId"] = r.CompuTechReceiptId;
        hidden["ReceiptType"] = r.ReceiptType; hidden["WarehouseId"] = "9001"; hidden["RoomId"] = "9002";
        hidden["FruitProfileId"] = "9004"; hidden["GrowerLotId"] = "100000"; hidden["GrowerNumber"] = r.GrowerNumber!;
        hidden["GrowerName"] = r.GrowerName; hidden["LotCode"] = r.LotCode; hidden["BinCount"] = "20";
        hidden["ReceivedAt"] = r.ReceivedAt.ToString("O"); hidden.Remove("CorrectionSourceRoomId");
        hidden["Reason"] = "Incorrect original count: 15 buffer bins included"; hidden["ConfirmInventoryChange"] = "true";
        var missingToken = new Dictionary<string, string>(hidden); missingToken.Remove("__RequestVerificationToken");
        var before = await f.Snapshot();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/Receipts/{id}/AdminInventoryOverride", new FormUrlEncodedContent(missingToken))).StatusCode);
        Assert.Equal(before, await f.Snapshot());
        response = await client.PostAsync($"/Receipts/{id}/AdminInventoryOverride", new FormUrlEncodedContent(hidden));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var audit = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, audit.StatusCode);
        Assert.Contains("15 buffer bins included", await audit.Content.ReadAsStringAsync());
        Assert.Equal(20, (await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == id)).BinCount);
        Assert.Equal(554, await f.Physical());
    }
}
