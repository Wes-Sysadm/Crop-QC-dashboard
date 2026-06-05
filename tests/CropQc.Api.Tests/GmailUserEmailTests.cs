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
    public void EmailOptions_DefaultQcRecipientsUseTestingAddresses()
    {
        var options = new EmailOptions();

        Assert.Equal("rob@earlbrownandsons.com, wes@fruitandland.com", options.QcRecipientHeader);
        Assert.Equal(["rob@earlbrownandsons.com", "wes@fruitandland.com"], options.QcRecipientList);
    }

    [Fact]
    public void EmailOptions_ConfiguredQcDefaultRecipientsOverrideLegacyToAddress()
    {
        var options = new EmailOptions
        {
            ToAddress = "QC@fruitandland.com",
            QcDefaultRecipients = "rob@earlbrownandsons.com,wes@fruitandland.com"
        };

        Assert.Equal("rob@earlbrownandsons.com, wes@fruitandland.com", options.QcRecipientHeader);
        Assert.DoesNotContain("QC@fruitandland.com", options.QcRecipientHeader);
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
        Assert.Contains("\"QcDefaultRecipients\": \"rob@earlbrownandsons.com,wes@fruitandland.com\"", productionSettings);
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
        Assert.Contains("Required Photos", details);
        Assert.Contains("Sample type:", details);
        Assert.DoesNotContain("Send QC Summary Placeholder", details);
        Assert.DoesNotContain("Override Placeholder", overrideSend);
        Assert.DoesNotContain("Send Placeholder", dailyQc);
    }

    [Fact]
    public void SamplesController_RestrictsEmailSendRoles()
    {
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "SamplesController.cs"));

        Assert.Contains("[Authorize(Policy = \"RequireQcUserOrHigher\")]", controller);
        Assert.Contains("[Authorize(Policy = \"RequireManagerOrAdmin\")]", controller);
        Assert.Contains("SendQcSummaryAsync", controller);
        Assert.Contains("LogOverrideSendAsync", controller);
    }

    [Fact]
    public void WebEmailConfiguration_UsesTestingQcDefaultRecipients()
    {
        var emailOptions = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "EmailOptions.cs"));
        var productionSettings = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "appsettings.Production.json"));
        var adminManagementService = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "AdminManagementService.cs"));

        Assert.Contains("Email:QcDefaultRecipients", emailOptions);
        Assert.Contains("EmailOptionsFactory", emailOptions);
        Assert.Contains("rob@earlbrownandsons.com,wes@fruitandland.com", productionSettings);
        Assert.Contains("rob@earlbrownandsons.com,wes@fruitandland.com", adminManagementService);
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
            NullLogger<GmailUserEmailSender>.Instance);

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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            SendCount++;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
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
