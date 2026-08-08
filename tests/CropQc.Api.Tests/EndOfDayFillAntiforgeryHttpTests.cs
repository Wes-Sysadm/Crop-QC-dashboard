using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CropQc.Api.Tests;

public sealed class EndOfDayFillAntiforgeryHttpTests
{
    [Fact]
    public void PersistentDataProtectionKeyRing_AllowsASecondInstanceToUnprotectData()
    {
        var keyDirectory = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            $"cropqc-data-protection-{Guid.NewGuid():N}"));
        try
        {
            var firstInstance = DataProtectionProvider.Create(
                keyDirectory,
                options => options.SetApplicationName("CropQcDashboard"));
            var protectedValue = firstInstance.CreateProtector("EndOfDayFillAntiforgeryTest")
                .Protect("compatible-across-deployments");

            var secondInstance = DataProtectionProvider.Create(
                keyDirectory,
                options => options.SetApplicationName("CropQcDashboard"));

            Assert.Equal(
                "compatible-across-deployments",
                secondInstance.CreateProtector("EndOfDayFillAntiforgeryTest").Unprotect(protectedValue));
            Assert.NotEmpty(keyDirectory.GetFiles());
        }
        finally
        {
            keyDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task EveryRenderedEndOfDayFillPostForm_IncludesAnAntiforgeryToken()
    {
        await using var factory = new EndOfDayFillWebApplicationFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();

        var reportHtml = await GetRequiredHtmlAsync(client, "/EndOfDayFill?groupId=1");
        AssertPostFormsHaveTokens(reportHtml, "/EndOfDayFill/Send", expectedCount: 1);

        var configurationHtml = await GetRequiredHtmlAsync(client, "/MasterData/end-of-day-fill-groups");
        AssertPostFormsHaveTokens(configurationHtml, "/MasterData/end-of-day-fill-groups/", expectedCount: 7);

        var usersHtml = await GetRequiredHtmlAsync(client, "/Admin/Users");
        AssertPostFormsHaveTokens(usersHtml, "/Admin/Users/EndOfDayFillGroups", expectedCount: 1);
    }

    [Fact]
    public async Task RenderedForm_WithMatchingAntiforgeryToken_SendsOnceAndFinalizesHistory()
    {
        await using var factory = new EndOfDayFillWebApplicationFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();

        var previewResponse = await client.GetAsync("/EndOfDayFill?groupId=1");
        var previewHtml = await previewResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, previewResponse.StatusCode);
        Assert.Contains(previewResponse.Headers.GetValues("Set-Cookie"), value =>
            value.Contains(".AspNetCore.Antiforgery.", StringComparison.Ordinal));
        var antiforgeryToken = HiddenValue(previewHtml, "__RequestVerificationToken");
        var previewToken = HiddenValue(previewHtml, "PreviewToken");

        var sendResponse = await client.PostAsync("/EndOfDayFill/Send", Form(
            antiforgeryToken,
            previewToken));

        Assert.Equal(HttpStatusCode.Redirect, sendResponse.StatusCode);
        Assert.Matches("^/EndOfDayFill/History/\\d+$", sendResponse.Headers.Location?.OriginalString);
        Assert.Single(factory.EmailSender.Messages);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
        var send = Assert.Single(await db.EndOfDayFillReportSends.AsNoTracking().ToListAsync());
        Assert.Equal(EndOfDayFillSendStatuses.Succeeded, send.Status);
        Assert.True(send.PhysicalCountConfirmed);
        Assert.Equal("http-antiforgery-fake-gmail-id", send.GmailMessageId);
        Assert.Empty(await db.EndOfDayFillSendReservations.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task MissingAntiforgeryToken_IsRejectedBeforeSendOrReservation()
    {
        await using var factory = new EndOfDayFillWebApplicationFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();
        var previewResponse = await client.GetAsync("/EndOfDayFill?groupId=1");
        var previewHtml = await previewResponse.Content.ReadAsStringAsync();

        var sendResponse = await client.PostAsync("/EndOfDayFill/Send", Form(
            antiforgeryToken: null,
            HiddenValue(previewHtml, "PreviewToken")));

        await AssertRejectedWithoutSendAsync(factory, sendResponse);
    }

    [Fact]
    public async Task MismatchedAntiforgeryToken_IsRejectedBeforeSendOrReservation()
    {
        await using var factory = new EndOfDayFillWebApplicationFactory();
        using var client = await factory.CreateAuthenticatedClientAsync();
        var previewResponse = await client.GetAsync("/EndOfDayFill?groupId=1");
        var previewHtml = await previewResponse.Content.ReadAsStringAsync();

        var sendResponse = await client.PostAsync("/EndOfDayFill/Send", Form(
            antiforgeryToken: "invalid-antiforgery-token",
            HiddenValue(previewHtml, "PreviewToken")));

        await AssertRejectedWithoutSendAsync(factory, sendResponse);
    }

    private static async Task AssertRejectedWithoutSendAsync(
        EndOfDayFillWebApplicationFactory factory,
        HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.EmailSender.Messages);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
        Assert.Empty(await db.EndOfDayFillReportSends.AsNoTracking().ToListAsync());
        Assert.Empty(await db.EndOfDayFillSendReservations.AsNoTracking().ToListAsync());
    }

    private static async Task<string> GetRequiredHtmlAsync(HttpClient client, string path)
    {
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadAsStringAsync();
    }

    private static void AssertPostFormsHaveTokens(string html, string actionPrefix, int expectedCount)
    {
        var forms = Regex.Matches(
                html,
                "<form\\b[^>]*method=\"post\"[^>]*>.*?</form>",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase | RegexOptions.Singleline)
            .Select(x => x.Value)
            .Where(x => x.Contains($"action=\"{actionPrefix}", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.Equal(expectedCount, forms.Count);
        Assert.All(forms, form => Assert.Contains("name=\"__RequestVerificationToken\"", form, StringComparison.Ordinal));
    }

    private static FormUrlEncodedContent Form(string? antiforgeryToken, string previewToken)
    {
        var values = new List<KeyValuePair<string, string>>
        {
            new("GroupId", "1"),
            new("PreviewToken", previewToken),
            new("PhysicalCountConfirmed", "true")
        };
        if (antiforgeryToken is not null)
        {
            values.Add(new("__RequestVerificationToken", antiforgeryToken));
        }
        return new FormUrlEncodedContent(values);
    }

    private static string HiddenValue(string html, string name)
    {
        var match = Regex.Match(
            html,
            $"<input[^>]*name=\"{Regex.Escape(name)}\"[^>]*value=\"(?<value>[^\"]*)\"[^>]*>",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        Assert.True(match.Success, $"The rendered form did not contain {name}.");
        return WebUtility.HtmlDecode(match.Groups["value"].Value);
    }

    private sealed class EndOfDayFillWebApplicationFactory : WebApplicationFactory<Program>
    {
        private const string SenderEmail = ApplicationAreas.OwnerEmail;
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        public RecordingEmailSender EmailSender { get; } = new();
        public FixedInventorySource InventorySource { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            connection.Open();
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:EnsureCreatedOnStartup"] = "true",
                    ["Database:SeedMasterDataOnStartup"] = "false",
                    ["Backups:Enabled"] = "false",
                    ["EbsDailyBinsEmail:Enabled"] = "false",
                    ["Email:Provider"] = EmailProviders.GmailUser,
                    ["DataProtection:PersistKeysToFileSystem"] = "false",
                    ["RENDER_EXTERNAL_HOSTNAME"] = "integration-test.local"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<CropQcDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<CropQcDbContext>>();
                services.RemoveAll<CropQcDbContext>();
                services.RemoveAll<IHostedService>();
                services.RemoveAll<IQcEmailSender>();
                services.RemoveAll<IEndOfDayFillInventorySource>();
                services.RemoveAll<IClock>();
                services.RemoveAll<EmailOptions>();
                services.AddDbContext<CropQcDbContext>(options => options.UseSqlite(connection));
                services.AddSingleton<IQcEmailSender>(EmailSender);
                services.AddSingleton<IEndOfDayFillInventorySource>(InventorySource);
                services.AddSingleton<IClock>(new FixedClock(new DateTimeOffset(2026, 8, 7, 4, 22, 0, TimeSpan.Zero)));
                services.AddSingleton(new EmailOptions { Provider = EmailProviders.GmailUser });
                services.AddDataProtection().UseEphemeralDataProtectionProvider();
                services
                    .AddAuthentication(options =>
                    {
                        options.DefaultAuthenticateScheme = TestAuthenticationHandler.SchemeName;
                        options.DefaultChallengeScheme = TestAuthenticationHandler.SchemeName;
                    })
                    .AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(
                        TestAuthenticationHandler.SchemeName,
                        _ => { });
            });
        }

        public async Task<HttpClient> CreateAuthenticatedClientAsync()
        {
            var client = CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true
            });
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(TestAuthenticationHandler.SchemeName);

            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CropQcDbContext>();
            var sender = new User
            {
                Email = SenderEmail,
                DisplayName = "HTTP Integration Sender",
                Domain = "fruitandland.com",
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.Users.Add(sender);
            await db.SaveChangesAsync();
            var warehouse = await db.Warehouses.SingleAsync(x => x.Code == "WP");
            db.Rooms.Add(new Room
            {
                Id = FixedInventorySource.RoomId,
                WarehouseId = warehouse.Id,
                Code = "DH-1",
                Name = "DH Room 1",
                DisplayName = "DH-1",
                CapacityBins = 900,
                IsActive = true,
                EndOfDayFillReportGroupId = 1
            });
            db.EndOfDayFillUserGroupAssignments.Add(new EndOfDayFillUserGroupAssignment
            {
                UserId = sender.Id,
                ReportGroupId = 1,
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedByUserId = sender.Id
            });
            db.UserGoogleCredentials.Add(new UserGoogleCredential
            {
                UserId = sender.Id,
                Provider = "Google",
                RefreshTokenEncrypted = "test-only",
                Scope = GmailScopes.Send,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
            return client;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                connection.Dispose();
            }
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "EndOfDayFillHttpIntegration";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.Email, ApplicationAreas.OwnerEmail)],
                SchemeName);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    private sealed class FixedInventorySource : IEndOfDayFillInventorySource
    {
        public const int RoomId = 910;

        public Task<IReadOnlyList<RoomLotSummaryViewModel>> GetCurrentLotsAsync(
            IReadOnlyCollection<int> roomIds,
            CancellationToken cancellationToken)
        {
            IReadOnlyList<RoomLotSummaryViewModel> lots = roomIds.Contains(RoomId)
                ?
                [
                    new RoomLotSummaryViewModel
                    {
                        RoomId = RoomId,
                        RoomCode = "DH-1",
                        CurrentBins = 145,
                        GrowerNumber = "1084",
                        GrowerName = "Smith Orchards",
                        CanonicalVarietyKey = "gala",
                        CanonicalVarietyName = "Gala",
                        ProductionType = "Fresh",
                        IsOrganic = false,
                        VarietyHexColor = "#c62828",
                        InventoryKey = "canonical-398",
                        GrowerLotId = 398
                    }
                ]
                : [];
            return Task.FromResult(lots);
        }
    }

    private sealed class RecordingEmailSender : IQcEmailSender
    {
        public ConcurrentQueue<(User Sender, QcEmailMessage Message)> Messages { get; } = new();

        public Task<QcEmailSendResult> SendAsync(
            User sender,
            QcEmailMessage message,
            CancellationToken cancellationToken)
        {
            Messages.Enqueue((sender, message));
            return Task.FromResult(QcEmailSendResult.Sent("http-antiforgery-fake-gmail-id"));
        }
    }

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }
}
