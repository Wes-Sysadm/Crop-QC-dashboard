using System.Net;
using System.Text.Json;
using CropQc.Api.Dtos;
using CropQc.Data.Authentication;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class AdminReceiptEditHttpTests
{
    [InventoryPostgresFact]
    public async Task Historical_date_edit_preserves_treatment_membership_and_rejects_crossing_an_application()
    {
        await using var f = await Fixture.Create();
        await using var db = f.CreateDbContext();
        (await db.Users.SingleAsync(x => x.Id == 8000)).Email = ApplicationAreas.OwnerEmail;
        await db.SaveChangesAsync();
        var chemical = await db.TreatmentChemicals.FirstAsync(x => x.IsActive && x.ApplicationLevel == "Room");
        var treatment = await f.Execute((await f.Command(InventoryCommandKind.TreatmentAssignment, 19)) with { TreatmentChemicalId = chemical.Id });
        Assert.Equal(InventoryCommandStatus.Committed, treatment.Status);
        await using var host = new ReceiptLocationHttpTests.ReceiptHost(f.Connection);
        using var client = host.CreateClient(new() { AllowAutoRedirect = false });
        var form = ReceiptCorrectionRenderedForm.Read(await client.GetStringAsync("/Receipts/100000/Edit"));
        form["Reason"] = "Correct historical receiving date without changing membership";
        form["ReceivedAt"] = "2026-01-02T12:00";
        var stock = await f.Snapshot(new Dictionary<string, string> { ["Receipts"] = "false", ["AuditLogs"] = "false", ["InventoryCommands"] = "false" });
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Receipts/100000/Edit", new FormUrlEncodedContent(form))).StatusCode);
        Assert.Equal(stock, await f.Snapshot(new Dictionary<string, string> { ["Receipts"] = "false", ["AuditLogs"] = "false", ["InventoryCommands"] = "false" }));
        form = ReceiptCorrectionRenderedForm.Read(await client.GetStringAsync("/Receipts/100000/Edit"));
        form["Reason"] = "Attempt to move receiving after its treatment";
        form["ReceivedAt"] = "2030-01-01T12:00";
        var before = await f.Snapshot();
        var rejected = await client.PostAsync("/Receipts/100000/Edit", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Contains("crosses a recorded treatment", await rejected.Content.ReadAsStringAsync());
        var retained = ReceiptCorrectionRenderedForm.Read(await rejected.Content.ReadAsStringAsync());
        Assert.Equal(form["ReceivedAt"], retained["ReceivedAt"]); Assert.Equal(form["Reason"], retained["Reason"]);
        Assert.Equal(before, await f.Snapshot());
    }

    [InventoryPostgresFact]
    public async Task Historical_metadata_one_save_requires_reason_preserves_input_and_inventory_and_rejects_stale_replay()
    {
        await using var f = await Fixture.Create();
        await using var db = f.CreateDbContext();
        // Fixture grants only Receipts Admin, not global Admin or owner.
        var receipt = await db.Receipts.SingleAsync(x => x.Id == 100000);
        receipt.ReceivedAt = DateTimeOffset.Parse("2025-11-02T09:30:45.123456Z");
        await db.SaveChangesAsync();
        await using var host = new ReceiptLocationHttpTests.ReceiptHost(f.Connection);
        using var client = host.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Receipt-Test-User", "canonical-test@example.invalid");
        var html = await client.GetStringAsync("/Receipts/100000/Edit");
        Assert.DoesNotContain("Apply Receipt Correction", html); Assert.DoesNotContain("Review bin count correction", html);
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(System.Text.RegularExpressions.Regex.Match(html, "<form class=\"form-grid\"[\\s\\S]*?</form>").Value, "type=\"submit\""));
        var form = ReceiptCorrectionRenderedForm.Read(html);
        form["CompuTechReceiptId"] = "HISTORICAL-ADMIN-EDIT";
        var before = await f.Snapshot();
        var rejected = await client.PostAsync("/Receipts/100000/Edit", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var retained = ReceiptCorrectionRenderedForm.Read(await rejected.Content.ReadAsStringAsync());
        Assert.Equal(form["CompuTechReceiptId"], retained["CompuTechReceiptId"]);
        Assert.Equal(form["OperationKey"], retained["OperationKey"]);
        Assert.Equal(before, await f.Snapshot());
        form["Reason"] = "Correct historical receipt number after review";
        var stock = await f.Snapshot(new Dictionary<string, string>() { ["Receipts"] = "false", ["AuditLogs"] = "false", ["InventoryCommands"] = "false" });
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Receipts/100000/Edit", new FormUrlEncodedContent(form))).StatusCode);
        var saved = await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == 100000);
        Assert.Equal(receipt.ReceivedAt, saved.ReceivedAt); Assert.Equal(receipt.CreatedAt, saved.CreatedAt);
        Assert.Equal("HISTORICAL-ADMIN-EDIT", saved.CompuTechReceiptId);
        Assert.Equal(stock, await f.Snapshot(new Dictionary<string, string>() { ["Receipts"] = "false", ["AuditLogs"] = "false", ["InventoryCommands"] = "false" }));
        var command = JsonSerializer.Deserialize<InventoryCommand>((await db.InventoryCommands.SingleAsync()).IntentJson, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(InventoryCommandKind.UpdateReceiptMetadata, command.Kind); Assert.Equal(form["Reason"], command.Reason);
        var after = await f.Snapshot();
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Receipts/100000/Edit", new FormUrlEncodedContent(form))).StatusCode);
        form["OperationKey"] = Guid.NewGuid().ToString("N"); form["CompuTechReceiptId"] = "STALE";
        var stale = await client.PostAsync("/Receipts/100000/Edit", new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        retained = ReceiptCorrectionRenderedForm.Read(await stale.Content.ReadAsStringAsync());
        Assert.Equal(form["Reason"], retained["Reason"]); Assert.Equal(form["ReceiptVersion"], retained["ReceiptVersion"]);
        Assert.Equal(after, await f.Snapshot());
    }

    [InventoryPostgresTheory]
    [InlineData("Create")]
    [InlineData("Edit")]
    public async Task Nonadmins_cannot_edit_through_web_API_service_or_canonical_command_but_keep_receiving_permission(string level)
    {
        await using var f = await Fixture.Create(receiptAdmin: false);
        await using var db = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection).CreateDbContext();
        await CanonicalOperatorSessionTests.GrantReceivingAsync(db, level == "Edit" ? "View" : level);
        if (level == "Edit")
        {
            // The persisted level is Create (Edit is its enum alias). Exercise the
            // legacy receipt-edit-only grant without granting Receipts Admin.
            var role = await db.UserRoles.Include(x => x.Role).SingleAsync(x => x.UserId == 8000);
            role.Role.PageAccesses.Add(new() { AreaKey = ApplicationAreas.ReceiptEdit, AccessLevel = "Create" });
            await db.SaveChangesAsync();
        }
        var principal = CanonicalReceivingWorkflowTests.Operator().HttpContext!.User;
        Assert.Equal(level == "Create", await OperatorSession.CanReceiveAsync(db, principal, default));
        Assert.False(await OperatorSession.CanEditReceiptAsync(db, principal, default));
        var receipt = await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == 100000);
        await using var host = new ReceiptLocationHttpTests.ReceiptHost(f.Connection);
        using var client = host.CreateClient(new() { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Receipt-Test-User", "canonical-test@example.invalid");
        var before = await f.Snapshot();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/Receipts/100000/Edit")).StatusCode);
        foreach (var path in new[] { "Edit", "AdminInventoryOverride" })
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync("/Receipts/100000/" + path, new FormUrlEncodedContent(new Dictionary<string, string>()))).StatusCode);
        var executor = new InventoryCommandExecutor(new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection));
        var api = new CropQc.Api.Services.ReceiptService(db, new CropQc.Api.Services.AuditService(db), httpContext: CanonicalReceivingWorkflowTests.Operator(), canonicalCommands: executor);
        Assert.Contains("Admin", (await api.UpdateSameDayAsync(receipt.Id, new UpdateReceiptRequest(receipt.CropYear, receipt.ReceivedAt,
            receipt.WarehouseId, receipt.RoomId, receipt.FruitProfileId, "Changed", receipt.LotCode, receipt.BinCount, "Reason", Guid.NewGuid().ToString("N"), receipt.ConcurrencyVersion), default)).Error);
        foreach (var kind in new[] { InventoryCommandKind.UpdateReceiptMetadata, InventoryCommandKind.CorrectReceiptQuantity,
            InventoryCommandKind.CorrectReceiptIdentity, InventoryCommandKind.CorrectReceiptLocation, InventoryCommandKind.ActivateReceiptInventory })
        {
            var denied = await executor.ExecuteAsync(new(Guid.NewGuid().ToString("N"), kind, 8000, DateTimeOffset.UtcNow, "Attempted nonadmin edit", []));
            Assert.Equal(InventoryCommandStatus.Blocked, denied.Status); Assert.Contains("Admin", denied.Detail);
        }
        Assert.Equal(before, await f.Snapshot());
    }

    [InventoryPostgresFact]
    public async Task One_save_identity_uses_audited_command_and_combined_changes_leave_everything_unchanged()
    {
        await using var f = await Fixture.Create();
        await using var db = f.CreateDbContext();
        (await db.Users.SingleAsync(x => x.Id == 8000)).Email = ApplicationAreas.OwnerEmail;
        db.GrowerLots.Add(new() { Id = 100001, Grower = "Corrected grower", LotNumber = "CORRECTED-LOT" });
        await db.SaveChangesAsync();
        await using var host = new ReceiptLocationHttpTests.ReceiptHost(f.Connection);
        using var client = host.CreateClient(new() { AllowAutoRedirect = false });
        var form = ReceiptCorrectionRenderedForm.Read(await client.GetStringAsync("/Receipts/100000/Edit"));
        form["GrowerLotId"] = "100001"; form["GrowerNumber"] = "CORRECTED-LOT"; form["GrowerName"] = "Corrected grower";
        form["Reason"] = "Reviewed exact original grower identity";
        var before = await f.Snapshot();
        foreach (var (name, value) in new[] { ("BinCount", "18"), ("RoomId", "9003"), ("CompuTechReceiptId", "COMBINED") })
        {
            var combined = new Dictionary<string, string>(form) { [name] = value };
            var rejected = await client.PostAsync("/Receipts/100000/Edit", new FormUrlEncodedContent(combined));
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Contains("composite command", await rejected.Content.ReadAsStringAsync());
            Assert.Equal(before, await f.Snapshot());
        }
        var historical = JsonSerializer.Serialize(await db.RoomInventoryAdjustments.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync());
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Receipts/100000/Edit", new FormUrlEncodedContent(form))).StatusCode);
        Assert.Equal(19, await f.Physical());
        Assert.Equal(100001, (await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == 100000)).GrowerLotId);
        Assert.Equal(historical, JsonSerializer.Serialize(await db.RoomInventoryAdjustments.AsNoTracking().Where(x => x.InventoryIdentityCorrectionId == null).OrderBy(x => x.Id).ToArrayAsync()));
        Assert.Single(await db.InventoryIdentityCorrections.Where(x => x.IsComplete).ToArrayAsync());
        var after = await f.Snapshot();
        Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync("/Receipts/100000/Edit", new FormUrlEncodedContent(form))).StatusCode);
        Assert.Equal(after, await f.Snapshot());
    }
}
