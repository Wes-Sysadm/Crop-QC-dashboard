using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Auth;
using CropQc.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using System.Text;

namespace CropQc.Api.Tests;

public sealed class GmailUserEmailTests
{
    [Fact]
    public void EmailOptions_DefaultQcRecipientIsRequiredQcMailbox()
    {
        var options = new EmailOptions();

        Assert.Equal(QcReportEmailDefaults.RequiredRecipient, options.QcRecipientHeader);
        Assert.Equal([QcReportEmailDefaults.RequiredRecipient], options.QcRecipientList);
    }

    [Fact]
    public void EmailOptions_QcReportDefaultDoesNotUseLegacyRecipientLists()
    {
        var options = new EmailOptions
        {
            ToAddress = "QC@fruitandland.com",
            QcDefaultRecipients = "rob@earlbrownandsons.com,wes@fruitandland.com"
        };

        Assert.Equal(QcReportEmailDefaults.RequiredRecipient, options.QcRecipientHeader);
        Assert.DoesNotContain("rob@earlbrownandsons.com", options.QcRecipientHeader);
    }

    [Fact]
    public void QcEmailRecipientParser_AcceptsCommasAndNewLinesAndRemovesDuplicates()
    {
        var result = QcEmailRecipientParser.Parse("""
            rob@earlbrownandsons.com, wes@fruitandland.com
            ROB@earlbrownandsons.com
            user@wp-packingllc.com
            """);

        Assert.Equal(["rob@earlbrownandsons.com", "wes@fruitandland.com", "user@wp-packingllc.com"], result.Recipients);
        Assert.Empty(result.InvalidRecipients);
    }

    [Fact]
    public void QcEmailRecipientParser_ReportsInvalidEmails()
    {
        var result = QcEmailRecipientParser.Parse("rob@earlbrownandsons.com,not-an-email");

        Assert.Equal(["rob@earlbrownandsons.com"], result.Recipients);
        Assert.Equal(["not-an-email"], result.InvalidRecipients);
    }

    [Fact]
    public void EmailOptionsFactory_ProductionDefaultsToGmailUserWhenProviderMissingOrDefaultNone()
    {
        var missingProvider = EmailOptionsFactory.Create(new ConfigurationBuilder().Build(), isProduction: true, explicitEnvironmentProvider: null);
        var defaultNone = EmailOptionsFactory.Create(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:Provider"] = EmailProviders.None
            })
            .Build(), isProduction: true, explicitEnvironmentProvider: null);

        Assert.Equal(EmailProviders.GmailUser, missingProvider.Provider);
        Assert.Equal(EmailProviders.GmailUser, defaultNone.Provider);
        Assert.True(missingProvider.IsProduction);
    }

    [Fact]
    public void EmailOptionsFactory_DevelopmentDefaultsToNone()
    {
        var options = EmailOptionsFactory.Create(new ConfigurationBuilder().Build(), isProduction: false, explicitEnvironmentProvider: null);

        Assert.Equal(EmailProviders.None, options.Provider);
        Assert.False(options.IsProduction);
    }

    [Fact]
    public void EmailOptionsFactory_ExplicitEnvironmentProviderOverridesProductionDefault()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:Provider"] = EmailProviders.None
            })
            .Build();

        var disabled = EmailOptionsFactory.Create(configuration, isProduction: true, explicitEnvironmentProvider: EmailProviders.None);
        var enabled = EmailOptionsFactory.Create(configuration, isProduction: true, explicitEnvironmentProvider: EmailProviders.GmailUser);

        Assert.Equal(EmailProviders.None, disabled.Provider);
        Assert.Equal(EmailProviders.GmailUser, enabled.Provider);
    }

    [Fact]
    public void GmailRawMessage_UsesLoggedInUserAsFromAndIncludesSubject()
    {
        var recipients = "rob@earlbrownandsons.com, wes@fruitandland.com";
        var raw = GmailUserEmailSender.BuildRawMessage(new QcEmailMessage(
            "wes@fruitandland.com",
            recipients,
            "sample-taker@fruitandland.com",
            "QC Summary - R123",
            "Plain body text",
            "<html><body><p>HTML body text</p><img src=\"cid:test-image@cropqc\" /></body></html>",
            [new QcEmailInlineImage("test-image@cropqc", "photo.jpg", "image/jpeg", [1, 2, 3], "Whole sample")]));

        var decoded = DecodeBase64Url(raw);

        Assert.Contains("From: wes@fruitandland.com", decoded);
        Assert.Contains($"To: {recipients}", decoded);
        Assert.Contains("Reply-To: sample-taker@fruitandland.com", decoded);
        Assert.Contains("Subject: QC Summary - R123", decoded);
        Assert.Contains("multipart/related", decoded);
        Assert.Contains("multipart/alternative", decoded);
        Assert.Contains("Plain body text", decoded);
        Assert.Contains("HTML body text", decoded);
        Assert.Contains("Content-ID: <test-image@cropqc>", decoded);
        Assert.Contains("Content-Disposition: inline", decoded);
        Assert.DoesNotContain("Content-Disposition: attachment", decoded);
        Assert.DoesNotContain("drive.google.com", decoded);
    }

    [Fact]
    public async Task GmailUserEmailSender_SendPreservesInlineMimeParts()
    {
        var httpHandler = new FakeGmailHttpHandler(HttpStatusCode.OK, """{"id":"gmail-message-1"}""");
        var sender = CreateSender(new FakeCredentialStore(GoogleAccessTokenResult.Success("access-token")), httpHandler);
        var message = new QcEmailMessage(
            "wes@fruitandland.com",
            "rob@earlbrownandsons.com",
            null,
            "QC Summary",
            "Photo is inline.",
            "<html><body><h2>Photos</h2><img src=\"cid:photo-1@cropqc\" /></body></html>",
            [new QcEmailInlineImage("photo-1@cropqc", "photo.jpg", "image/jpeg", [1, 2, 3, 4], "Whole sample")]);

        var result = await sender.SendAsync(User("wes@fruitandland.com"), message, CancellationToken.None);

        Assert.True(result.Success);
        using var json = System.Text.Json.JsonDocument.Parse(httpHandler.LastRequestBody);
        var raw = json.RootElement.GetProperty("raw").GetString();
        Assert.NotNull(raw);
        var decoded = DecodeBase64Url(raw!);
        Assert.Contains("Content-Type: multipart/related", decoded);
        Assert.Contains("<h2>Photos</h2><img src=\"cid:photo-1@cropqc\" />", decoded);
        Assert.Contains("Content-ID: <photo-1@cropqc>", decoded);
        Assert.Contains("Content-Disposition: inline; filename=\"photo.jpg\"", decoded);
        Assert.DoesNotContain("Content-Disposition: attachment", decoded);
        Assert.DoesNotContain("drive.google.com", decoded);
    }

    [Fact]
    public void GmailRawMessage_RejectsOversizedInlineImagesBeforeMimeBuild()
    {
        var oversized = new byte[(int)GmailUserEmailSender.MaxInlineImageBytesPerMessage + 1];

        var exception = Assert.Throws<InvalidOperationException>(() => GmailUserEmailSender.BuildRawMessage(new QcEmailMessage(
            "rob@earlbrownandsons.com",
            "wes@fruitandland.com",
            null,
            "QC Summary",
            "Text",
            "<p>Html</p>",
            [new QcEmailInlineImage("photo@cropqc", "photo.jpg", "image/jpeg", oversized, "Photo")])));

        Assert.Contains("embedded photos were too large", exception.Message);
    }

    [Theory]
    [InlineData("wes@fruitandland.com")]
    [InlineData("rob@earlbrownandsons.com")]
    [InlineData("user@wp-packingllc.com")]
    public async Task GmailUserEmailSender_AllowsConfiguredCompanyDomains(string senderEmail)
    {
        var httpHandler = new FakeGmailHttpHandler(HttpStatusCode.OK, """{"id":"gmail-message-1"}""");
        var sender = CreateSender(new FakeCredentialStore(GoogleAccessTokenResult.Success("access-token")), httpHandler);

        var result = await sender.SendAsync(User(senderEmail), Message(senderEmail), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("gmail-message-1", result.MessageId);
        Assert.Equal(1, httpHandler.SendCount);
    }

    [Fact]
    public async Task GmailUserEmailSender_BlocksDisallowedDomainBeforeCredentialLookup()
    {
        var credentialStore = new FakeCredentialStore(GoogleAccessTokenResult.Success("access-token"));
        var httpHandler = new FakeGmailHttpHandler(HttpStatusCode.OK, """{"id":"gmail-message-1"}""");
        var sender = CreateSender(credentialStore, httpHandler);

        var result = await sender.SendAsync(User("outsider@example.com"), Message("outsider@example.com"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("domain is not allowed", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, credentialStore.AccessTokenRequests);
        Assert.Equal(0, httpHandler.SendCount);
    }

    [Theory]
    [InlineData("wes@fruitandland.com")]
    [InlineData("rob@earlbrownandsons.com")]
    [InlineData("user@wp-packingllc.com")]
    public async Task GmailUserEmailSender_MissingGmailPermissionRequiresReconnectForAnyAllowedDomain(string senderEmail)
    {
        var sender = CreateSender(
            new FakeCredentialStore(GoogleAccessTokenResult.Reconnect("Gmail permission is required. Please reconnect Google/Gmail.")),
            new FakeGmailHttpHandler(HttpStatusCode.OK, """{"id":"gmail-message-1"}"""));

        var result = await sender.SendAsync(User(senderEmail), Message(senderEmail), CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.ReconnectRequired);
    }

    [Fact]
    public async Task GmailUserEmailSender_ProviderNoneInProductionShowsRenderSetting()
    {
        var sender = CreateSender(
            new FakeCredentialStore(GoogleAccessTokenResult.Success("access-token")),
            new FakeGmailHttpHandler(HttpStatusCode.OK, """{"id":"gmail-message-1"}"""),
            new EmailOptions { Provider = EmailProviders.None, IsProduction = true });

        var result = await sender.SendAsync(User("wes@fruitandland.com"), Message("wes@fruitandland.com"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(result.ReconnectRequired);
        Assert.Contains("Email__Provider=GmailUser", result.Error);
        Assert.Contains("Production should use", result.Error);
    }

    [Fact]
    public async Task GmailUserEmailSender_GmailUserChecksTokenBeforeSending()
    {
        var credentialStore = new FakeCredentialStore(GoogleAccessTokenResult.Reconnect("Gmail permission is required. Please reconnect Google/Gmail."));
        var sender = CreateSender(credentialStore, new FakeGmailHttpHandler(HttpStatusCode.OK, """{"id":"gmail-message-1"}"""));

        var result = await sender.SendAsync(User("wes@fruitandland.com"), Message("wes@fruitandland.com"), CancellationToken.None);

        Assert.False(result.Success);
        Assert.True(result.ReconnectRequired);
        Assert.Equal(1, credentialStore.AccessTokenRequests);
    }

    [Fact]
    public void GoogleAuthenticationOptions_ReadsAllCompanyDomainsFromConfiguration()
    {
        var options = GoogleAuthenticationOptions.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Authentication:AllowedGoogleDomains"] = "fruitandland.com,earlbrownandsons.com,wp-packingllc.com"
            })
            .Build());

        Assert.True(options.IsAllowedEmail("wes@fruitandland.com"));
        Assert.True(options.IsAllowedEmail("rob@earlbrownandsons.com"));
        Assert.True(options.IsAllowedEmail("user@wp-packingllc.com"));
        Assert.False(options.IsAllowedEmail("outsider@example.com"));
    }

    [Fact]
    public void WebLogin_RequestsGmailSendScopeAndDoesNotKeepTokensInCookie()
    {
        var program = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Program.cs"));

        Assert.Contains("options.Scope.Add(gmailOptions.SendScope)", program);
        Assert.Contains("options.AccessType = \"offline\"", program);
        Assert.Contains("prompt=consent", program);
        Assert.Contains("options.SaveTokens = true", program);
        Assert.Contains("SaveFromAuthenticationPropertiesAsync", program);
        Assert.Contains("StoreTokens(Array.Empty<AuthenticationToken>())", program);
    }

    [Fact]
    public void TokenStorage_EncryptsTokensAndDoesNotLogSecrets()
    {
        var store = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "GoogleCredentialStore.cs"));

        Assert.Contains("CreateProtector(\"CropQc.GoogleOAuthTokens.v1\")", store);
        Assert.Contains("protector.Protect(accessToken)", store);
        Assert.Contains("protector.Protect(refreshToken)", store);
        Assert.Contains("protector.Unprotect", store);
        Assert.Contains("Gmail permission is required. Please reconnect Google/Gmail.", store);
        Assert.DoesNotContain("LogInformation(refreshToken", store);
        Assert.DoesNotContain("LogWarning(refreshToken", store);
        Assert.DoesNotContain("LogError(refreshToken", store);
    }

    [Fact]
    public void ProductionConfig_UsesGmailUserProviderAndConfiguredScope()
    {
        var productionSettings = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "appsettings.Production.json"));

        Assert.Contains("\"Provider\": \"GmailUser\"", productionSettings);
        Assert.Contains("\"SendScope\": \"https://www.googleapis.com/auth/gmail.send\"", productionSettings);
        Assert.Contains("fruitandland.com,earlbrownandsons.com,wp-packingllc.com", productionSettings);
        Assert.Contains("\"QcReportDefaultRecipient\": \"qc@fruitandland.com\"", productionSettings);
    }

    [Fact]
    public void AdminConfiguration_ShowsSafeEmailStatusPanel()
    {
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "ConfigurationController.cs"));
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Configuration", "Index.cshtml"));
        var program = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Program.cs"));

        Assert.Contains("BuildEmailStatusAsync", controller);
        Assert.Contains("GmailCredentialPresent", controller);
        Assert.Contains("GmailSendPermissionGranted", controller);
        Assert.Contains("Email Status", view);
        Assert.Contains("QC Email Recipients", view);
        Assert.Contains("Default QC recipients source", view);
        Assert.Contains("Manage orchard QC recipients", view);
        Assert.Contains("Email__Provider=GmailUser", view);
        Assert.Contains("Reconnect Google/Gmail", view);
        Assert.Contains("Email provider:", program);
        Assert.Contains("Default QC recipients configured:", program);
        Assert.DoesNotContain("AccessToken", view);
        Assert.DoesNotContain("RefreshToken", view);
        Assert.DoesNotContain("ClientSecret", view);
    }

    [Fact]
    public void SampleViews_ShowGmailSenderAndNoPlaceholderSendLanguage()
    {
        var details = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Samples", "Details.cshtml"));
        var overrideSend = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Samples", "OverrideSend.cshtml"));
        var dailyQc = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "DailyQc", "Index.cshtml"));

        Assert.Contains("Sending from:", details);
        Assert.Contains("Send QC Summary", details);
        Assert.Contains("To:", details);
        Assert.Contains("Gmail permission is missing", overrideSend);
        Assert.Contains("Email Diagnostics", overrideSend);
        Assert.Contains("Allowed Google Workspace domains", overrideSend);
        Assert.Contains("configured QC recipients", overrideSend);
        Assert.Contains("Send QC Summary Override", overrideSend);
        Assert.Contains("Send QC Summary", dailyQc);
        Assert.Contains("Photos / Requirements", details);
        Assert.Contains("Sample type:", details);
        Assert.DoesNotContain("Send QC Summary Placeholder", details);
        Assert.DoesNotContain("Override Placeholder", overrideSend);
        Assert.DoesNotContain("Send Placeholder", dailyQc);
    }

    [Fact]
    public void SamplesController_RestrictsEmailSendRoles()
    {
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "SamplesController.cs"));

        Assert.Contains("AccessPolicyNames.DailyQcEdit", controller);
        Assert.Contains("AccessPolicyNames.DailyQcAdmin", controller);
        Assert.Contains("SendQcSummaryAsync", controller);
        Assert.Contains("LogOverrideSendAsync", controller);
    }

    [Fact]
    public void WebEmailConfiguration_UsesRequiredQcRecipientAndOrchardResolver()
    {
        var emailOptions = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "EmailOptions.cs"));
        var productionSettings = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "appsettings.Production.json"));
        var adminManagementService = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "AdminManagementService.cs"));
        var dashboardDataService = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "DashboardDataService.cs"));
        var configurationController = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "ConfigurationController.cs"));

        Assert.Contains("Email:QcReportDefaultRecipient", emailOptions);
        Assert.Contains("EmailOptionsFactory", emailOptions);
        Assert.Contains("QcEmailRecipientResolver", emailOptions);
        Assert.Contains("QcEmailDefaultRecipients", emailOptions);
        Assert.Contains("qc@fruitandland.com", productionSettings);
        Assert.Contains("QcReportEmailDefaults.RequiredRecipient", adminManagementService);
        Assert.Contains("Invalid QC email recipient", adminManagementService);
        Assert.Contains("No QC email recipients are configured. Admins can set them under Admin -> Configuration -> QC Email Recipients.", dashboardDataService);
        Assert.Contains("qcEmailRecipientResolver.ResolveForSampleAsync", dashboardDataService);
        Assert.Contains("AccessPolicyNames.EmailConfigurationAdmin", configurationController);
    }

    private static string DecodeBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }

    private static GmailUserEmailSender CreateSender(FakeCredentialStore credentialStore, FakeGmailHttpHandler httpHandler, EmailOptions? emailOptions = null) =>
        new(
            emailOptions ?? new EmailOptions { Provider = EmailProviders.GmailUser, ToAddress = "rob@earlbrownandsons.com,wes@fruitandland.com" },
            new GoogleAuthenticationOptions
            {
                AllowedDomains = new HashSet<string>(["fruitandland.com", "earlbrownandsons.com", "wp-packingllc.com"], StringComparer.OrdinalIgnoreCase)
            },
            credentialStore,
            new FakeHttpClientFactory(httpHandler),
            NullLogger<GmailUserEmailSender>.Instance,
            new PerformanceExternalCallCounter());

    private static User User(string email) => new()
    {
        Id = Math.Abs(email.GetHashCode()),
        Email = email,
        DisplayName = email,
        Domain = GoogleAuthenticationOptions.GetEmailDomain(email) ?? "",
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static QcEmailMessage Message(string senderEmail) =>
        new(senderEmail, "rob@earlbrownandsons.com,wes@fruitandland.com", null, "QC Summary", "Text", "<p>HTML</p>", []);

    private sealed class FakeCredentialStore(GoogleAccessTokenResult result) : IGoogleCredentialStore
    {
        public int AccessTokenRequests { get; private set; }
        public Task SaveFromAuthenticationPropertiesAsync(User user, Microsoft.AspNetCore.Authentication.AuthenticationProperties properties, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task<GoogleAccessTokenResult> GetAccessTokenAsync(User user, CancellationToken cancellationToken)
        {
            AccessTokenRequests++;
            return Task.FromResult(result);
        }
        public Task<GoogleCredentialDiagnostic> GetDiagnosticAsync(User user, CancellationToken cancellationToken) =>
            Task.FromResult(new GoogleCredentialDiagnostic(result.AccessToken is not null, result.AccessToken is not null));
    }

    private sealed class FakeHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class FakeGmailHttpHandler(HttpStatusCode statusCode, string body) : HttpMessageHandler
    {
        public int SendCount { get; private set; }
        public string LastRequestBody { get; private set; } = "{}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SendCount++;
            LastRequestBody = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }

    private static string FindRepositoryFile(params string[] pathParts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(pathParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not find repository file {Path.Combine(pathParts)}.");
    }
}
