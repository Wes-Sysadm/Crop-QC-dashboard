using System.Net;
using System.Text.RegularExpressions;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class DirectTransferHttpTests
{
    [InventoryPostgresFact]
    public async Task Operator_form_requires_whole_bins_permission_antiforgery_and_fresh_inventory_and_replays_exactly()
    {
        await using var f = await Fixture.Create();
        await using var db = f.CreateDbContext();
        (await db.Users.SingleAsync(x => x.Id == 8000)).Email = ApplicationAreas.OwnerEmail;
        db.Rooms.Add(new() { Id = 9008, WarehouseId = 1, Code = "EBS-DIRECT", Name = "Cross-company target" });
        await db.SaveChangesAsync();
        await using var host = new ReceiptLocationHttpTests.ReceiptHost(f.Connection);
        using var client = host.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        var html = await client.GetStringAsync("/Transfers?sourceRoomId=9002");
        Assert.Contains("EBS-DIRECT", html);
        Assert.DoesNotContain("Truck Receipt", html);
        Assert.Contains("Confirm transfer", html);
        var form = Form(html);
        form["DestinationRoomId"] = "9008";
        form["Selections[0].Quantity"] = "11";
        var before = await f.Snapshot();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/Transfers", new FormUrlEncodedContent([]))).StatusCode);
        using var denied = host.CreateClient(new() { AllowAutoRedirect = false });
        denied.DefaultRequestHeaders.Add("X-Receipt-Test-User", "no-receiving-permission@example.invalid");
        Assert.Equal(HttpStatusCode.Forbidden, (await denied.PostAsync("/Transfers", new FormUrlEncodedContent(form))).StatusCode);
        form["Selections[0].Quantity"] = "1.5";
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Transfers", new FormUrlEncodedContent(form))).StatusCode);
        Assert.Equal(before, await f.Snapshot());
        form["Selections[0].Quantity"] = "11";
        var response = await client.PostAsync("/Transfers", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.StartsWith("/Transfers/", response.Headers.Location!.OriginalString);
        var after = await f.Snapshot();
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Transfers", new FormUrlEncodedContent(form))).StatusCode);
        Assert.Equal(after, await f.Snapshot());
        form["OperationKey"] = Guid.NewGuid().ToString("N");
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Transfers", new FormUrlEncodedContent(form))).StatusCode);
        Assert.Equal(after, await f.Snapshot());
        Assert.Equal(8, await f.Physical());
        Assert.Equal(11, await db.RoomInventoryAdjustments.Where(x => x.RoomId == 9008).SumAsync(x => x.ChangeAmount));
        Assert.Equal(0, await db.InterCrewTransfers.CountAsync());
        Assert.Contains("Correct this transfer", await client.GetStringAsync(response.Headers.Location));
    }

    [InventoryPostgresFact]
    public async Task Candidate_compatible_recovery_profile_candidate_preserves_committed_corrections_and_continued_activity()
    {
        await using var f = await Fixture.Create();
        await using var db = f.CreateDbContext();
        (await db.Users.SingleAsync(x => x.Id == 8000)).Email = ApplicationAreas.OwnerEmail;
        await db.SaveChangesAsync();
        long original;
        await using (var candidate = new ReceiptLocationHttpTests.ReceiptHost(f.Connection))
        {
            using var client = candidate.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
            var form = Form(await client.GetStringAsync("/Transfers?sourceRoomId=9002"));
            form["DestinationRoomId"] = "9003"; form["Selections[0].Quantity"] = "11";
            Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Transfers", new FormUrlEncodedContent(form))).StatusCode);
            original = (await db.RoomTransfers.SingleAsync()).Id;
            var correction = Form(await client.GetStringAsync($"/Transfers/{original}"));
            correction["Quantity"] = "3"; correction["Reason"] = "Destination reports missing bins";
            correction["Evidence"] = "Reported shortage; source not independently checked";
            Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Transfers/Correct", new FormUrlEncodedContent(correction))).StatusCode);
        }
        var assigned = await db.RoomTransfers.AsNoTracking().SingleAsync(x => x.CorrectsRoomTransferId == original);
        var beforeRecovery = await f.Snapshot();
        await using (var rollback = new ReceiptLocationHttpTests.ReceiptHost(f.Connection, directTransfers: false))
        {
            using var client = rollback.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
            var html = await client.GetStringAsync($"/Transfers/{assigned.Id}");
            Assert.Contains("Physical presence there was not independently verified", html);
            Assert.DoesNotContain("Correct this transfer", html);
            var form = Form(await client.GetStringAsync("/Transfers?sourceRoomId=9002"));
            form["DestinationRoomId"] = "9003"; form["Selections[0].Quantity"] = "1";
            Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Transfers", new FormUrlEncodedContent(form))).StatusCode);
            Assert.Equal(beforeRecovery, await f.Snapshot());
            // The compatible recovery runtime still understands the committed
            // movement chain and can perform an unrelated supported inventory exit.
            var depletion = await f.Command(InventoryCommandKind.LegacyDump, 1, room: 9003);
            Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(depletion)).Status);
        }
        await using (var candidate = new ReceiptLocationHttpTests.ReceiptHost(f.Connection))
        {
            using var client = candidate.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
            var form = Form(await client.GetStringAsync($"/Transfers/{assigned.Id}"));
            form["Quantity"] = "3"; form["Reason"] = "Correct verified location"; form["Evidence"] = "Operator found and moved the three bins";
            form["PhysicallyVerified"] = "true"; form["VerifiedDestinationRoomId"] = "9003";
            Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Transfers/Correct", new FormUrlEncodedContent(form))).StatusCode);
        }
        Assert.False((await db.RoomTransfers.AsNoTracking().SingleAsync(x => x.Id == assigned.Id)).PhysicalLocationVerified);
        Assert.True((await db.RoomTransfers.AsNoTracking().SingleAsync(x => x.CorrectsRoomTransferId == assigned.Id)).PhysicalLocationVerified);
        Assert.Equal(8, await f.Physical());
        Assert.Equal(10, await f.Physical(9003));
        Assert.Equal(1, await db.BinsRunEntries.SumAsync(x => x.BinsRun));
    }

    private static Dictionary<string, string> Form(string html) => Regex.Matches(html, "<input[^>]*type=\"hidden\"[^>]*>")
        .Cast<Match>().Select(m => (Name: Regex.Match(m.Value, "name=\"([^\"]+)\"").Groups[1].Value,
            Value: WebUtility.HtmlDecode(Regex.Match(m.Value, "value=\"([^\"]*)\"").Groups[1].Value)))
        .Where(x => x.Name.Length > 0).GroupBy(x => x.Name).ToDictionary(x => x.Key, x => x.First().Value);
}
