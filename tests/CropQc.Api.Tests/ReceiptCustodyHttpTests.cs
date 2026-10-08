using System.Net;
using System.Text.RegularExpressions;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class ReceiptCustodyHttpTests
{
    [InventoryPostgresFact]
    public async Task Exact_allocation_form_acknowledges_then_places_with_antiforgery_permission_and_replay_protection()
    {
        await using var f = await Fixture.Create();
        var original = await f.ReceiveCommand(treated: true);
        var transferId = original.Lines[0].Source.Location.CustodyRecordId!.Value;
        var receiptId = original.ReceivingEvidence!.ReceiptId;
        await using var db = f.CreateDbContext();
        (await db.Users.SingleAsync(x => x.Id == 8000)).Email = ApplicationAreas.OwnerEmail;
        await db.SaveChangesAsync();
        await using var host = new ReceiptLocationHttpTests.ReceiptHost(f.Connection, truckCustody: true);
        using var client = host.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        var url = $"/Receipts/{receiptId}";
        var html = await client.GetStringAsync(url + "/MatchTransfer");
        Assert.Contains("Acknowledge arrived allocations", html);
        var dispatch = await db.TreatmentLineageMovements.SingleAsync(x => x.InterCrewTransferId == transferId && x.MovementType == "InterCrewDispatch");
        Assert.Contains("Allocations[0].Id", html);
        Assert.Contains((await db.RoomTreatmentApplications.SingleAsync()).ProductNameSnapshot, html);
        async Task<Dictionary<string, string>> Form(long allocation, string page) => new()
        {
            ["__RequestVerificationToken"] = WebUtility.HtmlDecode(Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value),
            ["TransferId"] = transferId.ToString(),
            ["TransferVersion"] = (await db.InterCrewTransfers.AsNoTracking().Where(x => x.Id == transferId).Select(x => x.ConcurrencyVersion).SingleAsync()).ToString(),
            ["ReceiptVersion"] = (await db.Receipts.AsNoTracking().Where(x => x.Id == receiptId).Select(x => x.ConcurrencyVersion).SingleAsync()).ToString(),
            ["Allocations[0].Id"] = allocation.ToString(),
            ["Allocations[0].Quantity"] = "18",
            ["Reason"] = "Observed these exact arrived bins"
        };
        var before = await f.Snapshot();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(url + "/AcknowledgeTransfer", new FormUrlEncodedContent([]))).StatusCode);
        using var denied = host.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        denied.DefaultRequestHeaders.Add("X-Receipt-Test-User", "no-receiving-permission@example.invalid");
        Assert.Equal(HttpStatusCode.Forbidden, (await denied.PostAsync(url + "/AcknowledgeTransfer", new FormUrlEncodedContent(await Form(dispatch.Id, html)))).StatusCode);
        Assert.Equal(before, await f.Snapshot());
        var acknowledge = await Form(dispatch.Id, html);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync(url + "/AcknowledgeTransfer", new FormUrlEncodedContent(acknowledge))).StatusCode);
        Assert.Equal(18, await db.ReceiptCustodyAcknowledgments.SumAsync(x => x.Quantity));
        Assert.Equal(0, await db.RoomInventoryAdjustments.Where(x => x.RoomId == 9007).SumAsync(x => x.ChangeAmount));
        html = await client.GetStringAsync(url + "/MatchTransfer");
        Assert.Contains("18 receipt-held, 1 unresolved", html);
        Assert.Contains("Place receipt-held bins", html);
        Assert.DoesNotContain("Return selected bins to source", html);
        var placement = await Form((await db.ReceiptCustodyAcknowledgments.SingleAsync()).Id, html);
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync(url + "/PlaceTransferCustody", new FormUrlEncodedContent(placement))).StatusCode);
        var after = await f.Snapshot();
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync(url + "/PlaceTransferCustody", new FormUrlEncodedContent(placement))).StatusCode);
        Assert.Equal(after, await f.Snapshot());
        Assert.Equal(18, await db.RoomInventoryAdjustments.Where(x => x.RoomId == 9007).SumAsync(x => x.ChangeAmount));
        Assert.Contains("0 receipt-held, 1 unresolved", await client.GetStringAsync(url + "/MatchTransfer"));
        Assert.Null(await db.Receipts.Where(x => x.Id == receiptId).Select(x => x.TransferCompletedAt).SingleAsync());
        html = await client.GetStringAsync(url + "/MatchTransfer");
        Assert.Contains("Custody and corrections", html);
        var compensation = await Form((await db.ReceiptCustodyPlacements.SingleAsync()).Id, html);
        compensation["Allocations[0].Quantity"] = "1";
        before = await f.Snapshot();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(url + "/ReversePlacement", new FormUrlEncodedContent([]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await denied.PostAsync(url + "/ReversePlacement", new FormUrlEncodedContent(compensation))).StatusCode);
        Assert.Equal(before, await f.Snapshot());
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync(url + "/ReversePlacement", new FormUrlEncodedContent(compensation))).StatusCode);
        Assert.Equal(17, await db.RoomInventoryAdjustments.Where(x => x.RoomId == 9007).SumAsync(x => x.ChangeAmount));
        html = await client.GetStringAsync(url + "/MatchTransfer");
        Assert.Contains("1 receipt-held, 1 unresolved", html);
        var reverseAck = await Form((await db.ReceiptCustodyAcknowledgments.SingleAsync()).Id, html);
        reverseAck["Allocations[0].Quantity"] = "1";
        before = await f.Snapshot();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(url + "/ReverseAcknowledgment", new FormUrlEncodedContent([]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await denied.PostAsync(url + "/ReverseAcknowledgment", new FormUrlEncodedContent(reverseAck))).StatusCode);
        Assert.Equal(before, await f.Snapshot());
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync(url + "/ReverseAcknowledgment", new FormUrlEncodedContent(reverseAck))).StatusCode);
        after = await f.Snapshot();
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync(url + "/ReverseAcknowledgment", new FormUrlEncodedContent(reverseAck))).StatusCode);
        Assert.Equal(after, await f.Snapshot());
        Assert.Contains("0 receipt-held, 2 unresolved", await client.GetStringAsync(url + "/MatchTransfer"));
        Assert.Equal(2, await db.ReceiptCustodyReversals.CountAsync());
    }
}
