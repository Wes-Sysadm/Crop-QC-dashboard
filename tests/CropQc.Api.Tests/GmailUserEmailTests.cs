using CropQc.Web.Services;
using System.Text;

namespace CropQc.Api.Tests;

public sealed class GmailUserEmailTests
{
    [Fact]
    public void GmailRawMessage_UsesLoggedInUserAsFromAndIncludesSubject()
    {
        var raw = GmailUserEmailSender.BuildRawMessage(new QcEmailMessage(
            "wes@fruitandland.com",
            "QC@fruitandland.com",
            "sample-taker@fruitandland.com",
            "QC Summary - R123",
            "Plain body text",
            "<html><body><p>HTML body text</p><img src=\"cid:test-image@cropqc\" /></body></html>",
            [new QcEmailInlineImage("test-image@cropqc", "photo.jpg", "image/jpeg", [1, 2, 3], "Whole sample")]));

        var decoded = DecodeBase64Url(raw);

        Assert.Contains("From: wes@fruitandland.com", decoded);
        Assert.Contains("To: QC@fruitandland.com", decoded);
        Assert.Contains("Reply-To: sample-taker@fruitandland.com", decoded);
        Assert.Contains("Subject: QC Summary - R123", decoded);
        Assert.Contains("multipart/related", decoded);
        Assert.Contains("multipart/alternative", decoded);
        Assert.Contains("Plain body text", decoded);
        Assert.Contains("HTML body text", decoded);
        Assert.Contains("Content-ID: <test-image@cropqc>", decoded);
        Assert.Contains("Content-Disposition: inline", decoded);
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
    }

    [Fact]
    public void SampleViews_ShowGmailSenderAndNoPlaceholderSendLanguage()
    {
        var details = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Samples", "Details.cshtml"));
        var overrideSend = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Samples", "OverrideSend.cshtml"));
        var dailyQc = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "DailyQc", "Index.cshtml"));

        Assert.Contains("Sending from:", details);
        Assert.Contains("Send QC Summary", details);
        Assert.Contains("Gmail permission is missing", overrideSend);
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

    private static string DecodeBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
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
