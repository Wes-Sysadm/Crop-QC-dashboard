using System.Net;
using System.Security.Claims;
using System.Text.Json;
using System.Text.RegularExpressions;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Controllers;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class ActualRunCorrectionAvailabilityHttpTests
{
    [InventoryPostgresTheory]
    [InlineData(310, 0, false)]
    [InlineData(378, 0, false)]
    [InlineData(310, 20, false)]
    [InlineData(310, 0, true)]
    public async Task Rendered_correction_credits_only_its_own_run_once_and_preserves_conservation(int originalBins, int interveningBins, bool treated)
    {
        await using var f = await Fixture.Create(receiptAdmin: false);
        await using (var seed = f.CreateDbContext())
        {
            var user = await seed.Users.SingleAsync(x => x.Id == 8000);
            user.IsActive = true; user.EmploymentFacility = EmploymentFacilities.Wp;
            user.EmploymentEffectiveAt = DateTimeOffset.UtcNow.AddDays(-1);
            var role = new Role { Name = "Run correction fixture", NormalizedName = "RUN CORRECTION FIXTURE" };
            role.PageAccesses.Add(new() { AreaKey = ApplicationAreas.ActualRuns, AccessLevel = "Admin" });
            role.PageAccesses.Add(new() { AreaKey = ApplicationAreas.BinsRun, AccessLevel = "Admin" });
            seed.UserRoles.Add(new() { UserId = user.Id, Role = role });
            (await seed.Receipts.SingleAsync()).BinCount = 378;
            var origin = await seed.RoomInventoryAdjustments.SingleAsync(); origin.ChangeAmount = 378; origin.NewBinCount = 378;
            (await seed.TreatmentLineageSegments.SingleAsync()).CurrentBins = 378;
            await seed.SaveChangesAsync();
        }
        var factory = new CanonicalActualRunWorkflowTests.EnabledFactory(f.Connection);
        await using var db = factory.CreateDbContext();
        if (treated)
        {
            var treatment = (await f.Command(InventoryCommandKind.TreatmentAssignment, 378)) with
            { TreatmentChemicalId = await db.TreatmentChemicals.Where(x => x.IsActive && x.ApplicationLevel == "Room").Select(x => x.Id).FirstAsync() };
            var assigned = await new InventoryCommandExecutor(factory).ExecuteAsync(treatment);
            Assert.True(assigned.Status == InventoryCommandStatus.Committed, assigned.Detail);
        }
        var applications = await db.RoomTreatmentApplications.AsNoTracking().OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync();
        var service = new BinsRunService(db, new CanonicalOutsideWorkflowTests.Access(), NullLogger<BinsRunService>.Instance,
            canonicalCommands: new InventoryCommandExecutor(factory, runExpectations: new CanonicalRunExpectationWriter()));
        var principal = CanonicalReceivingWorkflowTests.Operator().HttpContext!.User;
        var runId = await Create(originalBins);
        if (interveningBins > 0) await Create(interveningBins);
        var originalLedger = await db.RoomInventoryAdjustments.AsNoTracking().OrderBy(x => x.Id).ToArrayAsync();
        var receipt = JsonSerializer.Serialize(await db.Receipts.AsNoTracking().SingleAsync());
        await using var host = new RunHost(f.Connection);
        using var client = host.Client();
        var path = $"/BinsRun/ActualRuns/{runId}";
        var before = await f.Snapshot();
        var page = await client.GetAsync($"/BinsRun?Section=Actual&EditActualRunId={runId}");
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        var html = await page.Content.ReadAsStringAsync();
        Assert.Equal(before, await f.Snapshot());
        var form = Fields(Form(html, path));
        var expectedField = Assert.Single(form.Keys, x => x.EndsWith(".ExpectedAvailableBins", StringComparison.Ordinal));
        var binsField = Assert.Single(form.Keys, x => x.EndsWith(".BinsRun", StringComparison.Ordinal));
        var availableForCorrection = 378 - interveningBins;
        Assert.Equal(availableForCorrection.ToString(), form[expectedField]);
        Assert.Contains("Available after reversal", html);
        Assert.Equal(378 - originalBins - interveningBins, await f.Physical());
        form["CorrectionReason"] = "Correct the recorded quantity in disposable PostgreSQL";

        // Displayed/posted availability is not inventory authority. Even the old
        // page's duplicate credit cannot authorize more than the exact reversal.
        form[expectedField] = (availableForCorrection + originalBins).ToString();
        form[binsField] = (availableForCorrection + 1).ToString();
        await RejectUnchanged();
        form[expectedField] = availableForCorrection.ToString();
        form[binsField] = "300";
        var saved = await client.PostAsync(path, new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode); Assert.True(string.IsNullOrEmpty(host.Error(saved)), host.Error(saved));
        Assert.Equal(availableForCorrection - 300, await f.Physical());
        var afterSave = await f.Snapshot();
        var retry = await client.PostAsync(path, new FormUrlEncodedContent(form));
        Assert.Equal(HttpStatusCode.Redirect, retry.StatusCode); Assert.Empty(host.Error(retry));
        Assert.Equal(afterSave, await f.Snapshot());
        var current = await db.ActualRuns.AsNoTracking().SingleAsync(x => x.Id == runId);
        Assert.Equal(2, current.CurrentRevisionNumber);
        var detail = await client.GetStringAsync($"/BinsRun?Section=Actual&EditActualRunId={runId}");
        var cancelPath = path + "/Cancel";
        var cancel = Fields(Form(detail, cancelPath), requireToken: false); cancel["Reason"] = "Restore the corrected disposable run";
        cancel["__RequestVerificationToken"] = Fields(Form(detail, path))["__RequestVerificationToken"];
        var canceled = await client.PostAsync(cancelPath, new FormUrlEncodedContent(cancel));
        Assert.Equal(HttpStatusCode.Redirect, canceled.StatusCode); Assert.Empty(host.Error(canceled));
        Assert.Equal(availableForCorrection, await f.Physical());
        foreach (var row in originalLedger)
            Assert.Equal(JsonSerializer.Serialize(row), JsonSerializer.Serialize(await db.RoomInventoryAdjustments.AsNoTracking().SingleAsync(x => x.Id == row.Id)));
        Assert.Equal(receipt, JsonSerializer.Serialize(await db.Receipts.AsNoTracking().SingleAsync()));
        Assert.Equal(applications, await db.RoomTreatmentApplications.AsNoTracking().OrderBy(x => x.Id).Select(x => x.Id).ToArrayAsync());
        var signature = treated ? "u|a:" + string.Join(',', applications) : "u";
        Assert.All(await db.TreatmentLineageMovements.AsNoTracking().Where(x => x.BinsRunEntryId != null).ToListAsync(), x => Assert.Equal(signature, x.TreatmentSignatureSnapshot));
        var remaining = Assert.Single((await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db))
            .ResolveAsync(new(9001, [9002]), new(), DateTimeOffset.UtcNow)).Positions);
        Assert.Equal(availableForCorrection, remaining.AvailableQuantity);
        Assert.All(remaining.TreatmentSlices, x => Assert.Equal(applications, x.ApplicationIds.ToArray()));
        var conservation = await new InventoryConservationReportService(db, new CropQc.Web.Services.RoomInventoryLedgerQueryService(db), 2026).AnalyzeAsync(default);
        Assert.Equal(0, conservation.Global.Difference);
        var readiness = await new InventoryDeductionInvariantService(db, NullLogger<InventoryDeductionInvariantService>.Instance).VerifyReadinessAsync(default);
        Assert.True(readiness.IsReady, JsonSerializer.Serialize(readiness.Issues));

        async Task<long> Create(int quantity)
        {
            var option = Assert.Single((await service.GetPageAsync(new() { Section = "Actual", WarehouseId = 9001, RoomIds = [9002] }, principal, default)).AvailableInventory);
            var create = new ActualRunForm
            {
                RunFacilityWarehouseId = 9001,
                RunAt = DateTimeOffset.UtcNow,
                SalesDeskId = await db.SalesDesks.Where(x => x.IsActive).Select(x => x.Id).FirstAsync(),
                Lines = [new() { InventoryKey = option.InventoryKey, TreatmentSignature = option.TreatmentSignature,
                    CanonicalFingerprint = option.CanonicalFingerprint, ExpectedAvailableBins = option.CurrentBins, BinsRun = quantity }]
            };
            Assert.Null(await service.CreateActualRunAsync(create, principal, default));
            return (await db.ActualRunRevisions.AsNoTracking().SingleAsync(x => x.OperationKey == create.OperationKey)).ActualRunId;
        }
        async Task RejectUnchanged()
        {
            var rejected = await client.PostAsync(path, new FormUrlEncodedContent(form));
            Assert.Equal(HttpStatusCode.Redirect, rejected.StatusCode); Assert.NotEmpty(host.Error(rejected));
            Assert.Equal(before, await f.Snapshot());
            // Follow the normal redirect so the one-request error message is
            // consumed before checking the subsequent successful submission.
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(rejected.Headers.Location)).StatusCode);
        }
    }

    private static string Form(string html, string action) => Assert.Single(Regex.Matches(html, "<form\\b[^>]*>.*?</form>", RegexOptions.Singleline)
        .Select(x => x.Value), x => x.Contains("action=\"" + action + "\"", StringComparison.Ordinal));

    private static Dictionary<string, string> Fields(string form, bool requireToken = true)
    {
        var fields = new Dictionary<string, string>();
        foreach (Match tag in Regex.Matches(form, "<input\\b[^>]*>"))
        {
            if (tag.Value.Contains("disabled", StringComparison.Ordinal)) continue;
            var name = Regex.Match(tag.Value, "name=\"([^\"]+)\"");
            if (name.Success) fields[WebUtility.HtmlDecode(name.Groups[1].Value)] = WebUtility.HtmlDecode(Regex.Match(tag.Value, "value=\"([^\"]*)\"").Groups[1].Value);
        }
        foreach (Match tag in Regex.Matches(form, "<textarea[^>]*name=\"([^\"]+)\"[^>]*>(.*?)</textarea>", RegexOptions.Singleline))
            fields[tag.Groups[1].Value] = WebUtility.HtmlDecode(tag.Groups[2].Value);
        if (requireToken) Assert.True(fields.TryGetValue("__RequestVerificationToken", out var token) && token.Length > 0);
        return fields;
    }

    private sealed class RunHost(string connection) : WebApplicationFactory<ReceiptsController>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "PostgreSql",
                ["ConnectionStrings:CropQc"] = connection,
                ["Database:EnsureCreatedOnStartup"] = "false",
                ["Database:SeedMasterDataOnStartup"] = "false",
                ["Backups:Enabled"] = "false",
                ["Email:Provider"] = "None"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<CanonicalInventoryMode>(); services.AddSingleton(new CanonicalInventoryMode(true));
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
            });
        }
        public HttpClient Client()
        {
            var client = CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
            var options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get("Cookies");
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Email, "canonical-test@example.invalid")], "Cookies");
            var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), new AuthenticationProperties
            { IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(20) }, "Cookies");
            client.DefaultRequestHeaders.Add("Cookie", options.Cookie.Name + "=" + options.TicketDataFormat.Protect(ticket));
            return client;
        }
        public string Error(HttpResponseMessage response)
        {
            var context = new DefaultHttpContext { RequestServices = Services };
            if (response.Headers.TryGetValues("Set-Cookie", out var cookies)) context.Request.Headers.Cookie = string.Join("; ", cookies.Select(x => x.Split(';')[0]));
            var data = Services.GetRequiredService<ITempDataProvider>().LoadTempData(context);
            return data.TryGetValue("Error", out var error) ? error?.ToString() ?? "" : "";
        }
    }
}
