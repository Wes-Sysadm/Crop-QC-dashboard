using System.Net;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Api.Tests;

public sealed class ReceiptQuantityHttpTests
{
    [InventoryPostgresTheory]
    [InlineData(20)]
    [InlineData(40)]
    public async Task Reviewed_admin_post_corrects_only_selected_receipt_and_requires_addition_treatment_confirmation(int quantity)
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
        Assert.Contains("Save Receipt", html); Assert.DoesNotContain("Review bin count correction", html);
        Assert.Contains("Removed from selected current inventory", html); Assert.Contains("35 bins", html);
        Assert.Contains("Untreated", html); Assert.Contains("Bins to correct", html);
        Assert.Contains("ConfirmAdditionalBinsUntreated", html);
        Assert.DoesNotContain("ownership cannot be proven", html);
        var hidden = ReceiptCorrectionRenderedForm.Read(html);
        ReceiptCorrectionRenderedForm.Review(hidden, quantity);
        hidden["Reason"] = quantity < 35 ? "Incorrect original count: 15 buffer bins included" : "Five verified untreated bins omitted from count";
        hidden["ConfirmInventoryChange"] = "true";
        var missingToken = new Dictionary<string, string>(hidden); missingToken.Remove("__RequestVerificationToken");
        var before = await f.Snapshot();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync($"/Receipts/{id}/Edit", new FormUrlEncodedContent(missingToken))).StatusCode);
        Assert.Equal(before, await f.Snapshot());
        if (quantity > 35)
        {
            var unconfirmed = await client.PostAsync($"/Receipts/{id}/Edit", new FormUrlEncodedContent(hidden));
            Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
            Assert.Equal(before, await f.Snapshot());
            var rejectedPage = await unconfirmed.Content.ReadAsStringAsync();
            Assert.Contains("Confirm that the additional bins are untreated", rejectedPage);
            hidden["ConfirmAdditionalBinsUntreated"] = "true";
        }
        response = await client.PostAsync($"/Receipts/{id}/Edit", new FormUrlEncodedContent(hidden));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var audit = await client.GetAsync(response.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, audit.StatusCode);
        Assert.Equal(hidden["Reason"], await db.ReceiptInventoryOverrides.Select(x => x.Reason).SingleAsync());
        Assert.Equal(quantity, (await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == id)).BinCount);
        Assert.Equal(534 + quantity, await f.Physical());
    }
}
