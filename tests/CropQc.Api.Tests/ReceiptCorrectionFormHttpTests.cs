using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class ReceiptCorrectionFormHttpTests(ITestOutputHelper output)
{
    [InventoryPostgresFact]
    public async Task Rendered_quantity_post_preserves_identity_and_replays_for_admin()
    {
        await using var f = await Fixture.Create();
        await using var db = f.CreateDbContext();
        await Actor(db, false);
        // Minute-aligned, unmapped identity isolates the missing LotCode defect.
        (await db.Receipts.SingleAsync(x => x.Id == 100000)).ReceivedAt = DateTimeOffset.Parse("2026-10-01T12:00:00Z");
        await db.SaveChangesAsync();
        await using var host = new ReceiptLocationHttpTests.ReceiptHost(f.Connection);
        using var client = Client(host, false);
        var form = ReceiptCorrectionRenderedForm.Read(await client.GetStringAsync("/Receipts/100000/Edit"));
        output.WriteLine("Rendered fields: " + string.Join(", ", form.Keys.Order()));
        ReceiptCorrectionRenderedForm.Review(form, 17);
        var saved = await Identity(db);
        var result = await client.PostAsync("/Receipts/100000/Edit", new FormUrlEncodedContent(form));
        var page = await client.GetStringAsync(result.Headers.Location);
        output.WriteLine(string.Join("\n", Regex.Matches(page, "<p class=\"notice warning\">([\\s\\S]*?)</p>").Select(x => WebUtility.HtmlDecode(x.Groups[1].Value))));
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        Assert.DoesNotContain("/Edit", result.Headers.Location!.ToString());
        Assert.Equal(17, await f.Physical());
        Assert.Equal(saved, await Identity(db));
        var after = await f.Snapshot();
        var retry = await client.PostAsync("/Receipts/100000/Edit", new FormUrlEncodedContent(form));
        Assert.Equal(result.Headers.Location, retry.Headers.Location);
        Assert.Equal(after, await f.Snapshot());
        form["OperationKey"] = Guid.NewGuid().ToString("N");
        form["BinCount"] = "16";
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsync("/Receipts/100000/Edit", new FormUrlEncodedContent(form))).StatusCode);
        Assert.Equal(after, await f.Snapshot());
    }

    [InventoryPostgresFact]
    public async Task Rendered_post_after_thirteen_consumed_adds_only_two_with_separate_untreated_origin()
    {
        await using var f = await Fixture.Create();
        await using var db = f.CreateDbContext();
        await Actor(db, false);
        var receipt = await db.Receipts.SingleAsync(x => x.Id == 100000);
        receipt.BinCount = 13;
        receipt.GrowerNumber = null;
        receipt.ReceivedAt = DateTimeOffset.Parse("2026-10-01T12:34:56.123456Z");
        var segment = await db.TreatmentLineageSegments.SingleAsync(x => x.Id == 100000);
        segment.CurrentBins = 13;
        var origin = await db.RoomInventoryAdjustments.SingleAsync(x => x.Id == 100000);
        origin.ChangeAmount = origin.NewBinCount = 13;
        await db.SaveChangesAsync();
        var chemical = await db.TreatmentChemicals.FirstAsync(x => x.IsActive && x.ApplicationLevel == "Room");
        var treatment = await f.Execute((await f.Command(InventoryCommandKind.TreatmentAssignment, 13)) with { TreatmentChemicalId = chemical.Id, EffectiveAt = DateTimeOffset.Parse("2026-10-06T19:00:00Z") });
        Assert.Equal(InventoryCommandStatus.Committed, treatment.Status);
        var dump = await f.Command(InventoryCommandKind.Dump, 13);
        Assert.Equal(InventoryCommandStatus.Committed, (await f.Execute(dump with
        { EffectiveAt = DateTimeOffset.Parse("2026-10-07T19:00:00Z"), Lines = [dump.Lines[0] with { TreatmentSignature = $"u|a:{treatment.Effects[0].ParentId}" }] })).Status);
        var lastAuditId = await db.AuditLogs.MaxAsync(x => x.Id);
        var history = await History(db, lastAuditId);
        var identity = await Identity(db);
        await using var host = new ReceiptLocationHttpTests.ReceiptHost(f.Connection);
        using var client = Client(host, false);
        var html = await client.GetStringAsync("/Receipts/100000/Edit");
        var form = ReceiptCorrectionRenderedForm.Read(html);
        Assert.Equal(receipt.LotCode, form["GrowerNumber"]);
        ReceiptCorrectionRenderedForm.Review(form, 15);
        var before = await f.Snapshot();
        var missing = await client.PostAsync("/Receipts/100000/Edit", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        Assert.Equal(before, await f.Snapshot());
        var targets = Regex.Match(html, "<select name=\"AdditionalStockTarget\">([\\s\\S]*?)</select>").Value;
        form["AdditionalStockTarget"] = Regex.Matches(targets, "<option[^>]*>").Select(x => ReceiptCorrectionRenderedForm.Attribute(x.Value, "value")).First(x => x.StartsWith("9002:"));
        form["ConfirmAdditionalStockPresent"] = "true";
        form["ConfirmAdditionalBinsUntreated"] = "true";
        var result = await client.PostAsync("/Receipts/100000/Edit", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, result.StatusCode);
        Assert.DoesNotContain("/Edit", result.Headers.Location!.ToString());
        Assert.Equal(2, await f.Physical());
        Assert.Equal(15, (await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == 100000)).BinCount);
        Assert.Equal(history, await History(db, lastAuditId));
        Assert.Equal(identity, await Identity(db));
        var added = await db.TreatmentLineageSegments.AsNoTracking().Include(x => x.Applications).SingleAsync(x => x.CohortKey == form["OperationKey"]);
        Assert.Equal(2, added.CurrentBins); Assert.Equal("u", added.TreatmentSignature); Assert.Empty(added.Applications);
        Assert.Equal(100000, added.ReceiptId);
        var audit = Assert.Single(await db.ReceiptInventoryOverrides.AsNoTracking().ToArrayAsync());
        Assert.Equal(13, audit.OldReceiptBinCount); Assert.Equal(15, audit.NewReceiptBinCount); Assert.Equal(2, audit.InventoryDelta);
    }

    [InventoryPostgresFact]
    public async Task Legacy_display_alias_type_and_ambiguous_minute_roundtrip_do_not_rewrite_saved_metadata()
    {
        await using var f = await Fixture.Create();
        await using var db = f.CreateDbContext();
        await Actor(db, false);
        var receipt = await db.Receipts.SingleAsync(x => x.Id == 100000);
        receipt.ReceiptType = "truck receipt";
        receipt.ReceivedAt = DateTimeOffset.Parse("2025-11-02T09:30:45.123456Z"); // second Pacific 01:30
        var grower = new CanonicalGrower { DisplayName = "Canonical display differs", NormalizedKey = "CORRECTION-ALIAS" };
        grower.GrowerNumbers.Add(new() { GrowerNumber = receipt.GrowerNumber!, NormalizedGrowerNumber = receipt.GrowerNumber! });
        db.CanonicalGrowers.Add(grower);
        await db.SaveChangesAsync();
        Assert.Equal(grower.DisplayName, (await new CanonicalGrowerService(db).LoadResolutionSetAsync(default)).DisplayName(receipt.GrowerName, receipt.GrowerNumber));
        await using var host = new ReceiptLocationHttpTests.ReceiptHost(f.Connection);
        using var client = Client(host, false);
        var form = ReceiptCorrectionRenderedForm.Read(await client.GetStringAsync("/Receipts/100000/Edit"));
        Assert.Equal(receipt.GrowerName, form["GrowerName"]);
        Assert.Equal(receipt.ReceiptType, form["ReceiptType"]);
        Assert.Equal("2025-11-02T01:30", form["ReceivedAt"]);
        ReceiptCorrectionRenderedForm.Review(form, 17);
        var before = await f.Snapshot();
        var response = await client.PostAsync("/Receipts/100000/Edit", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Only ordinary inventory receipts can change inventory quantity", await response.Content.ReadAsStringAsync());
        Assert.Equal(before, await f.Snapshot()); // Do not silently normalize a nonstandard stored receipt type.
        receipt.ReceiptType = "Truck receipt";
        await db.SaveChangesAsync(); // Local fixture setup for the supported receipt kind.
        form = ReceiptCorrectionRenderedForm.Read(await client.GetStringAsync("/Receipts/100000/Edit"));
        ReceiptCorrectionRenderedForm.Review(form, 17);
        var identity = await Identity(db);
        response = await client.PostAsync("/Receipts/100000/Edit", new FormUrlEncodedContent(form));
        Assert.DoesNotContain("/Edit", response.Headers.Location!.ToString());
        Assert.Equal(17, await f.Physical());
        Assert.Equal(identity, await Identity(db));
    }

    [InventoryPostgresFact]
    public async Task Tampered_identity_invalid_binding_and_unauthorized_actor_cannot_write()
    {
        await using var f = await Fixture.Create();
        await using var db = f.CreateDbContext();
        await Actor(db, false);
        await using var host = new ReceiptLocationHttpTests.ReceiptHost(f.Connection);
        using var client = Client(host, false);
        var form = ReceiptCorrectionRenderedForm.Read(await client.GetStringAsync("/Receipts/100000/Edit"));
        ReceiptCorrectionRenderedForm.Review(form, 17);
        var before = await f.Snapshot();
        foreach (var (field, value) in new[] { ("LotCode", "OTHER"), ("LotCode", ""), ("GrowerNumber", "OTHER"),
            ("GrowerNumber", ""), ("GrowerName", "OTHER"), ("GrowerLotId", ""), ("GrowerLotId", "invalid"),
            ("FruitProfileId", "9005"), ("ReceivedAt", "2026-01-01T00:00"), ("ReceiptType", "Door sample") })
        {
            var altered = new Dictionary<string, string>(form) { [field] = value, ["LotCodeOmitted"] = "true" };
            var response = await client.PostAsync("/Receipts/100000/Edit", new FormUrlEncodedContent(altered));
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, field);
            Assert.Equal(before, await f.Snapshot());
        }
        foreach (var field in new[] { "GrowerNumber", "GrowerLotId", "FruitProfileId", "ReceivedAt", "OperationKey", "ReceiptVersion", "CropYear", "BinCount" })
        {
            var omitted = new Dictionary<string, string>(form);
            omitted.Remove(field);
            var rejected = await client.PostAsync("/Receipts/100000/Edit", new FormUrlEncodedContent(omitted));
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Equal(before, await f.Snapshot());
        }
        var query = await client.PostAsync("/Receipts/100000/Edit?LotCode=OTHER", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.BadRequest, query.StatusCode);
        var prefixed = form.ToDictionary(x => x.Key == "__RequestVerificationToken" ? x.Key : "form." + x.Key, x => x.Value);
        prefixed["form.LotCode"] = "OTHER";
        var prefix = await client.PostAsync("/Receipts/100000/Edit", new FormUrlEncodedContent(prefixed));
        Assert.Equal(HttpStatusCode.BadRequest, prefix.StatusCode);
        Assert.Equal(before, await f.Snapshot());
        using var unauthorized = host.CreateClient(new() { AllowAutoRedirect = false });
        unauthorized.DefaultRequestHeaders.Add("X-Receipt-Test-User", "unauthorized@example.invalid");
        Assert.Equal(HttpStatusCode.Forbidden, (await unauthorized.PostAsync("/Receipts/100000/Edit", new FormUrlEncodedContent(form))).StatusCode);
        Assert.Equal(before, await f.Snapshot());
    }

    private static HttpClient Client(ReceiptLocationHttpTests.ReceiptHost host, bool receiver)
    {
        var client = host.CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
        if (receiver) client.DefaultRequestHeaders.Add("X-Receipt-Test-User", "receiver@example.invalid");
        return client;
    }

    private static async Task Actor(CropQcDbContext db, bool receiver)
    {
        var user = await db.Users.SingleAsync(x => x.Id == 8000);
        user.Email = receiver ? "receiver@example.invalid" : ApplicationAreas.OwnerEmail;
        if (receiver)
        {
            db.UserRoles.RemoveRange(await db.UserRoles.Where(x => x.UserId == 8000).ToListAsync());
            var role = new Role { Name = "Receipt form receiver", NormalizedName = "RECEIPT FORM RECEIVER" };
            role.PageAccesses.Add(new() { AreaKey = ApplicationAreas.Receipts, AccessLevel = "Create" });
            role.PageAccesses.Add(new() { AreaKey = ApplicationAreas.ReceiptEdit, AccessLevel = "Create" });
            db.UserRoles.Add(new() { UserId = 8000, Role = role });
        }
        await db.SaveChangesAsync();
    }

    private static async Task<string> Identity(CropQcDbContext db) => JsonSerializer.Serialize(await db.Receipts.AsNoTracking()
        .Where(x => x.Id == 100000).Select(x => new
        {
            x.CropYear,
            x.WarehouseId,
            x.RoomId,
            x.FruitProfileId,
            x.GrowerLotId,
            x.GrowerNumber,
            x.GrowerName,
            x.LotCode,
            x.CompuTechReceiptId,
            x.ReceiptType,
            x.ReceivedAt,
            x.CreatedAt
        }).SingleAsync());

    private static async Task<string> History(CropQcDbContext db, long lastAuditId) => JsonSerializer.Serialize(new
    {
        Dumps = await db.BinsRunEntries.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Losses = await db.RoomInventoryLosses.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Applications = await db.RoomTreatmentApplications.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Sources = await db.RoomTreatmentApplicationSources.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync(),
        Ledger = await db.RoomInventoryAdjustments.AsNoTracking().Where(x => x.ReceiptInventoryOverrideId == null).OrderBy(x => x.Id).ToArrayAsync(),
        Movements = await db.TreatmentLineageMovements.AsNoTracking().Where(x => x.MovementType != "ReceiptQuantityCorrection").OrderBy(x => x.Id).ToArrayAsync(),
        Audits = await db.AuditLogs.AsNoTracking().Where(x => x.Id <= lastAuditId).OrderBy(x => x.Id).ToArrayAsync()
    });
}
