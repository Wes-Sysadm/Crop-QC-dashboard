using System.Net;
using System.Security.Claims;
using System.Text.RegularExpressions;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Controllers;
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
using Microsoft.Extensions.Options;
using static CropQc.Api.Tests.InventoryCommandTests;

namespace CropQc.Api.Tests;

public sealed class InventoryTransferAntiforgeryHttpTests
{
    // One isolated PostgreSQL database per action. Real cookie authentication,
    // database permissions, Razor rendering, MVC antiforgery and canonical writers.
    [InventoryPostgresFact]
    public async Task Transfer_family_rejects_missing_invalid_and_foreign_session_tokens_before_any_write()
    {
        foreach (var action in new[] { "room", "room-reverse", "outside", "outside-reverse", "dispatch", "receive", "review", "crew-reverse", "true-up" })
        {
            await using var f = await Fixture.Create();
            await SeedOperator(f);
            var setup = await Prepare(f, action);
            await using var host = new TransferHost(f.Connection);
            using var client = host.Client(ApplicationAreas.OwnerEmail);
            var html = await client.GetStringAsync(setup.Page);
            var formHtml = Form(html, setup.RenderedAction);
            var token = Token(formHtml);
            Assert.NotEmpty(token);
            var payload = new Dictionary<string, string>(setup.Payload);
            if (action == "room") payload["SourceLotKey"] = Option(formHtml, "SourceLotKey");
            if (action is "outside" or "dispatch") payload["SourceKey"] = Option(formHtml, "SourceKey");
            // Every negative request has a complete operation-shaped payload, not an empty form.
            Assert.True(payload.Count >= 3);
            var before = await f.Snapshot(); var commands = host.Commands.Calls;
            foreach (var invalid in new string?[] { null, "not-a-valid-token" })
            {
                payload.Remove("__RequestVerificationToken");
                if (invalid != null) payload["__RequestVerificationToken"] = invalid;
                Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(setup.Path, new FormUrlEncodedContent(payload))).StatusCode);
                Assert.Equal(commands, host.Commands.Calls);
                Assert.Equal(before, await f.Snapshot());
            }
            using var otherSession = host.Client(ApplicationAreas.OwnerEmail);
            var otherToken = Token(Form(await otherSession.GetStringAsync(setup.Page), setup.RenderedAction));
            payload["__RequestVerificationToken"] = otherToken;
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(setup.Path, new FormUrlEncodedContent(payload))).StatusCode);
            Assert.Equal(commands, host.Commands.Calls);
            Assert.Equal(before, await f.Snapshot());

            using var anonymous = host.Client(null);
            var anonymousResponse = await anonymous.PostAsync(setup.Path, new FormUrlEncodedContent(payload));
            Assert.Equal(HttpStatusCode.Redirect, anonymousResponse.StatusCode);
            Assert.StartsWith("http://localhost/Login", anonymousResponse.Headers.Location!.ToString());
            using var denied = host.Client("transfer-viewer@example.invalid");
            var deniedResponse = await denied.PostAsync(setup.Path, new FormUrlEncodedContent(payload));
            Assert.Equal(HttpStatusCode.Redirect, deniedResponse.StatusCode);
            Assert.StartsWith("http://localhost/AccessDenied", deniedResponse.Headers.Location!.ToString());
            Assert.Equal(commands, host.Commands.Calls);
            Assert.Equal(before, await f.Snapshot());

            // A matching token reaches existing business validation: an explicit
            // stale/nonexistent source/parent must still be rejected without writes.
            payload["__RequestVerificationToken"] = token;
            var invalidPath = setup.Path;
            if (action is "room-reverse" or "outside-reverse" or "receive" or "crew-reverse")
                invalidPath = setup.Path.Replace(setup.Parent.ToString(), "999999999", StringComparison.Ordinal);
            else if (action == "room") payload["SourceLotKey"] = "stale-source-selection";
            else if (action == "true-up") payload["ReceiptId"] = "999999999";
            else payload["SourceKey"] = "stale-source-selection";
            var rejected = await client.PostAsync(invalidPath, new FormUrlEncodedContent(payload));
            Assert.Equal(HttpStatusCode.Redirect, rejected.StatusCode);
            Assert.DoesNotContain("Login", rejected.Headers.Location!.ToString());
            Assert.DoesNotContain("AccessDenied", rejected.Headers.Location!.ToString());
            Assert.Equal(before, await f.Snapshot());
            if (action == "review")
                Assert.Contains("A review note cannot resolve missing or excess inventory", host.Error(rejected));
        }
    }

    [InventoryPostgresFact]
    public async Task Valid_cookie_and_rendered_tokens_preserve_room_outside_and_intercrew_workflows()
    {
        foreach (var action in new[] { "room", "outside", "dispatch", "receive", "room-reverse", "outside-reverse", "crew-reverse" })
        {
            await using var f = await Fixture.Create();
            await SeedOperator(f);
            if (action is "room" or "outside")
            {
                await using var treatmentDb = f.CreateDbContext();
                var chemical = await treatmentDb.TreatmentChemicals.FirstAsync(x => x.IsActive && x.ApplicationLevel == "Room");
                await Commit(f, (await f.Command(InventoryCommandKind.TreatmentAssignment, 19)) with { TreatmentChemicalId = chemical.Id, EffectiveAt = DateTimeOffset.UtcNow.AddMinutes(-1) });
            }
            var setup = await Prepare(f, action);
            await using var host = new TransferHost(f.Connection);
            using var client = host.Client(ApplicationAreas.OwnerEmail);
            var rendered = Form(await client.GetStringAsync(setup.Page), setup.RenderedAction);
            var payload = new Dictionary<string, string>(setup.Payload) { ["__RequestVerificationToken"] = Token(rendered) };
            if (action == "room")
            {
                payload["SourceLotKey"] = Option(rendered, "SourceLotKey");
                payload["TreatmentSignature"] = WebUtility.HtmlDecode(Regex.Match(rendered, "data-treatment-signature=\"([^\"]+)\"").Groups[1].Value);
            }
            if (action is "outside" or "dispatch") payload["SourceKey"] = Option(rendered, "SourceKey");
            await using var db = f.CreateDbContext();
            var originalReceipt = await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == 100000);
            var history = await db.RoomInventoryAdjustments.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
            var applications = System.Text.Json.JsonSerializer.Serialize(await db.RoomTreatmentApplications.AsNoTracking().OrderBy(x => x.Id).ToListAsync());
            var response = await client.PostAsync(setup.Path, new FormUrlEncodedContent(payload));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.True(host.Commands.Calls > 0, action + " did not invoke the canonical writer");
            var expectedSource = action.EndsWith("reverse", StringComparison.Ordinal) ? 19 : 14;
            Assert.True(expectedSource == await f.Physical(), action + ": " + host.Error(response) + " " + host.Commands.LastResult);
            if (action == "room") Assert.Equal(5, await f.Physical(9003));
            if (action == "dispatch")
            {
                var parent = await db.InterCrewTransfers.AsNoTracking().SingleAsync();
                Assert.True(parent.RequiresTruckReceipt);
                Assert.Equal(5, parent.BinsLoaded);
                Assert.Null(parent.ReceivingReceiptId);
            }
            if (action == "receive")
            {
                var parent = await db.InterCrewTransfers.AsNoTracking().SingleAsync(x => x.Id == setup.Parent);
                Assert.Equal(5, parent.BinsReceived);
                Assert.False(parent.RequiresTruckReceipt); // supported pre-activation legacy load
                Assert.Equal(5, await db.RoomInventoryAdjustments.Where(x => x.RoomId == 9007).SumAsync(x => x.ChangeAmount));
            }
            if (action == "outside") Assert.Equal(5, (await db.OutsideWarehouseTransfers.AsNoTracking().SingleAsync()).BinCount);
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(originalReceipt), System.Text.Json.JsonSerializer.Serialize(await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == 100000)));
            foreach (var old in history)
                Assert.Equal(System.Text.Json.JsonSerializer.Serialize(old), System.Text.Json.JsonSerializer.Serialize(await db.RoomInventoryAdjustments.AsNoTracking().SingleAsync(x => x.Id == old.Id)));
            Assert.False(await db.TreatmentLineageSegments.AnyAsync(x => x.CurrentBins < 0));
            Assert.Equal(19, await db.RoomInventoryAdjustments.SumAsync(x => x.ChangeAmount)
                + await db.InterCrewTransfers.Where(x => x.Status == InterCrewTransferStatuses.InTransit).SumAsync(x => x.BinsLoaded)
                + await db.OutsideWarehouseTransfers.Where(x => !x.IsReversed).SumAsync(x => x.BinCount));
            Assert.Equal(applications, System.Text.Json.JsonSerializer.Serialize(await db.RoomTreatmentApplications.AsNoTracking().OrderBy(x => x.Id).ToListAsync()));
            Assert.False(await db.TreatmentLineageSegments.AnyAsync(x => x.ReceiptId != null && x.ReceiptId != 100000));
            if (action is "room" or "outside")
            {
                var applicationId = await db.RoomTreatmentApplications.Select(x => x.Id).SingleAsync();
                var movements = await db.TreatmentLineageMovements.AsNoTracking()
                    .Where(x => x.MovementType == (action == "room" ? TreatmentLineageMovementTypes.Transfer : TreatmentLineageMovementTypes.OutsideWarehouseTransfer)).ToListAsync();
                Assert.NotEmpty(movements);
                foreach (var movement in movements)
                {
                    var segment = await db.TreatmentLineageSegments.AsNoTracking().Include(x => x.Applications)
                        .SingleAsync(x => x.Id == movement.SourceSegmentId);
                    Assert.Equal(segment.TreatmentSignature, movement.TreatmentSignatureSnapshot);
                    Assert.Contains(segment.Applications, x => x.RoomTreatmentApplicationId == applicationId);
                    if (movement.DestinationSegmentId is long destinationId)
                    {
                        var destination = await db.TreatmentLineageSegments.AsNoTracking().Include(x => x.Applications).SingleAsync(x => x.Id == destinationId);
                        Assert.Equal(segment.TreatmentSignature, destination.TreatmentSignature);
                        Assert.Contains(destination.Applications, x => x.RoomTreatmentApplicationId == applicationId);
                    }
                }
            }
            var after = await f.Snapshot();
            Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync(setup.Path, new FormUrlEncodedContent(payload))).StatusCode);
            Assert.Equal(after, await f.Snapshot());
            payload["Reason"] += " changed"; payload["Note"] = "Changed retry";
            if (payload.ContainsKey("BinCount")) payload["BinCount"] = "4";
            if (payload.ContainsKey("BinsLoaded")) payload["BinsLoaded"] = "4";
            if (payload.ContainsKey("BinsReceived")) payload["BinsReceived"] = "4";
            Assert.Equal(HttpStatusCode.Redirect, (await client.PostAsync(setup.Path, new FormUrlEncodedContent(payload))).StatusCode);
            Assert.Equal(after, await f.Snapshot());
        }
    }

    private static async Task SeedOperator(Fixture f)
    {
        await using var db = f.CreateDbContext();
        (await db.Users.SingleAsync(x => x.Id == 8000)).Email = ApplicationAreas.OwnerEmail;
        db.Users.Add(new() { Id = 8001, Email = "transfer-viewer@example.invalid", DisplayName = "Denied synthetic operator", IsActive = true });
        db.Rooms.Add(new() { Id = 9007, WarehouseId = 1, Code = "HTTP-EBS", Name = "Synthetic EBS receiving room" });
        db.OutsideWarehouses.Add(new() { Id = 9901, Code = "HTTP-OUT", Name = "Synthetic outside storage", IsActive = true });
        await db.SaveChangesAsync();
    }

    private static async Task<Scenario> Prepare(Fixture f, string action)
    {
        var payload = new Dictionary<string, string> { ["OperationKey"] = Guid.NewGuid().ToString("N"), ["Reason"] = "Security HTTP rehearsal" };
        var page = "/BinsRun?Section=Transfer&WarehouseId=9001&RoomId=9002";
        string path; long parent = 0;
        if (action == "room" || action == "room-reverse")
        {
            path = "/BinsRun/Transfer";
            payload["FromRoomId"] = "9002"; payload["DestinationWarehouseId"] = "9001"; payload["DestinationRoomId"] = "9003";
            payload["BinCount"] = "5"; payload["TreatmentSignature"] = "u"; payload["TransferAt"] = DateTimeOffset.UtcNow.ToString("O");
            if (action == "room-reverse") { parent = (await Commit(f, await f.Command(InventoryCommandKind.RoomMove, 5))).Effects[0].ParentId!.Value; path += $"/{parent}/Reverse"; }
            return new(path, page, "/BinsRun/Transfer", parent, payload);
        }
        if (action == "true-up")
        {
            payload["RoomId"] = "9002"; payload["ReceiptId"] = "100000"; payload["NewBinCount"] = "18";
            payload["CanonicalFingerprint"] = (await f.Command(InventoryCommandKind.RoomMove, 1)).Lines[0].Source.ExpectedFingerprint;
            payload["AdjustmentAt"] = DateTimeOffset.UtcNow.ToString("O");
            return new("/BinsRun/TrueUp", "/BinsRun?Section=TrueUp&WarehouseId=9001&RoomId=9002", "/BinsRun/TrueUp", 0, payload);
        }
        payload["ExpectedAvailableBins"] = "19"; payload["ConfirmedReview"] = "true";
        if (action.StartsWith("outside", StringComparison.Ordinal))
        {
            path = "/BinsRun/OutsideTransfers"; page += "&TransferType=Outside";
            payload["OutsideWarehouseId"] = "9901"; payload["BinCount"] = "5"; payload["TransferredAt"] = new CropQc.Shared.Time.PacificBusinessTimeService(new CropQc.Shared.Time.SystemClock()).NowPacific.ToString("yyyy-MM-ddTHH:mm:ss.fffffff");
            if (action == "outside-reverse")
            {
                parent = (await Commit(f, (await f.Command(InventoryCommandKind.OutsideWarehouseTransfer, 5)) with { CounterpartyId = 9901 })).Effects[0].ParentId!.Value;
                page = path + "/" + parent; path = page + "/Reverse"; payload["TransferId"] = parent.ToString();
            }
            return new(path, page, path, parent, payload);
        }
        path = "/BinsRun/InterCrewTransfers"; page += "&TransferType=InterCrew";
        payload["SourceWarehouseId"] = "9001"; payload["SourceRoomId"] = "9002"; payload["DestinationCustodyGroup"] = "EBS";
        payload["BinsLoaded"] = "5"; payload["LoadedAt"] = new CropQc.Shared.Time.PacificBusinessTimeService(new CropQc.Shared.Time.SystemClock()).NowPacific.ToString("yyyy-MM-ddTHH:mm:ss.fffffff");
        if (action != "dispatch")
        {
            if (action == "review")
            {
                // Historical variance diagnostic only. A note must not settle a
                // genuine shortage; this fixture is never used as stock evidence.
                await using var db = f.CreateDbContext();
                var variance = new InterCrewTransfer
                {
                    OperationKey = "synthetic-variance",
                    SourceWarehouseId = 9001,
                    SourceRoomId = 9002,
                    GrowerNameSnapshot = "Synthetic variance",
                    LotNumberSnapshot = "VARIANCE",
                    VarietyCodeSnapshot = "CGAL",
                    ProductionTypeSnapshot = "Conventional",
                    TreatmentStateSnapshot = "Unknown",
                    TreatmentSignatureSnapshot = "unknown",
                    TreatmentSummarySnapshot = "Unknown",
                    DestinationCustodyGroup = "EBS",
                    DestinationWarehouseId = 1,
                    DestinationRoomId = 9007,
                    BinsLoaded = 5,
                    BinsReceived = 4,
                    VarianceBins = -1,
                    Status = InterCrewTransferStatuses.ReceivedNeedsReview,
                    LoadedByUserId = 8000,
                    ReceivedByUserId = 8000,
                    LoadedAt = DateTimeOffset.UtcNow,
                    ReceivedAt = DateTimeOffset.UtcNow,
                    RequiresTruckReceipt = false
                };
                db.InterCrewTransfers.Add(variance); await db.SaveChangesAsync();
                parent = variance.Id; page = path + "/" + parent; path = page + "/Review";
                payload["TransferId"] = parent.ToString(); payload["Note"] = "Security HTTP rehearsal";
                return new(path, page, path, parent, payload);
            }
            // Existing legacy loads remain receivable with their original policy.
            parent = (await Commit(f, (await f.Command(InventoryCommandKind.InterCompanyDispatch, 5)) with { CustodyGroup = "EBS", Dispatch = new(null, null, false) })).Effects[0].ParentId!.Value;
            page = path + "/" + parent;
            payload["TransferId"] = parent.ToString(); payload["Note"] = "Security HTTP rehearsal";
            payload["DestinationRoomId"] = "9007"; payload["BinsReceived"] = "5"; payload["ReceivedAt"] = new CropQc.Shared.Time.PacificBusinessTimeService(new CropQc.Shared.Time.SystemClock()).NowPacific.ToString("yyyy-MM-ddTHH:mm:ss.fffffff");
            path = page + (action == "receive" ? "/Receive" : action == "review" ? "/Review" : "/Reverse");
        }
        return new(path, page, path, parent, payload);
    }

    private static async Task<InventoryCommandResult> Commit(Fixture f, InventoryCommand command)
    {
        var result = await f.Execute(command);
        Assert.True(result.Status == InventoryCommandStatus.Committed, result.Detail);
        return result;
    }
    private static string Form(string html, string action)
    {
        var form = Regex.Matches(html, "<form\\b[^>]*>.*?</form>", RegexOptions.Singleline | RegexOptions.IgnoreCase)
            .Select(x => x.Value).SingleOrDefault(x => x.Contains("action=\"" + action + "\"", StringComparison.Ordinal));
        Assert.True(form != null, "Missing rendered form " + action);
        return form!;
    }
    private static string Token(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        Assert.True(match.Success, "Rendered form lacks antiforgery token");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
    private static string Option(string html, string name)
    {
        var option = Regex.Matches(html, "<input[^>]*name=\"" + name + "\"[^>]*>")
            .Select(x => x.Value).FirstOrDefault(x => !x.Contains("disabled", StringComparison.Ordinal)
                && (name != "SourceLotKey" || x.Contains("data-treatment-signature", StringComparison.Ordinal)));
        Assert.True(option != null, "No enabled rendered source option for " + name);
        return WebUtility.HtmlDecode(Regex.Match(option!, "value=\"([^\"]+)\"").Groups[1].Value);
    }
    private sealed record Scenario(string Path, string Page, string RenderedAction, long Parent, Dictionary<string, string> Payload);

    private sealed class TransferHost(string connection) : WebApplicationFactory<ReceiptsController>
    {
        public CommandCounter Commands { get; } = new();
        public string Error(HttpResponseMessage response)
        {
            var context = new DefaultHttpContext { RequestServices = Services };
            if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
                context.Request.Headers.Cookie = string.Join("; ", cookies.Select(x => x.Split(';')[0]));
            var data = Services.GetRequiredService<ITempDataProvider>().LoadTempData(context);
            return data.TryGetValue("Error", out var error) ? error?.ToString() ?? "" : "";
        }
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
                ["EbsDailyBinsEmail:Enabled"] = "false",
                ["Email:Provider"] = "None",
                ["TransferCustody:Enabled"] = "true",
                ["TruckReceiptReconciliation:Enabled"] = "true"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<CanonicalInventoryMode>(); services.AddSingleton(new CanonicalInventoryMode(true));
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services.RemoveAll<IInventoryCommandExecutor>();
                services.AddScoped<IInventoryCommandExecutor>(sp => new CountingExecutor(sp.GetRequiredService<InventoryCommandExecutor>(), Commands));
            });
        }
        public HttpClient Client(string? email)
        {
            var client = CreateClient(new() { AllowAutoRedirect = false, HandleCookies = true });
            if (email != null)
            {
                // Locally issued synthetic session, actual production cookie handler
                // and active-operator/role validation. No authentication replacement.
                var options = Services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get("Cookies");
                var identity = new ClaimsIdentity([new Claim(ClaimTypes.Email, email)], "Cookies");
                var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), new AuthenticationProperties
                { IssuedUtc = DateTimeOffset.UtcNow, ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(20) }, "Cookies");
                client.DefaultRequestHeaders.Add("Cookie", options.Cookie.Name + "=" + options.TicketDataFormat.Protect(ticket));
            }
            return client;
        }
    }
    private sealed class CommandCounter { public int Calls; public string? LastResult; }
    private sealed class CountingExecutor(InventoryCommandExecutor inner, CommandCounter counter) : IInventoryCommandExecutor
    {
        public async Task<InventoryCommandResult> ExecuteAsync(InventoryCommand command, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref counter.Calls);
            var result = await inner.ExecuteAsync(command, cancellationToken);
            counter.LastResult = System.Text.Json.JsonSerializer.Serialize(result);
            return result;
        }
    }
}
