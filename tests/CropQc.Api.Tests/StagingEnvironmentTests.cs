using CropQc.Shared.Storage;
using CropQc.Web.Auth;
using CropQc.Web.Controllers;
using CropQc.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CropQc.Api.Tests;

public sealed class StagingEnvironmentTests
{
    [Fact]
    public void Staging_validator_accepts_isolated_configuration()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["DATABASE_PROVIDER"] = "PostgreSql",
            ["ConnectionStrings:CropQc"] = "Host=staging-db;Database=cropqc_staging;Username=cropqc_staging;Password=secret",
            ["Authentication:AllowedGoogleDomains"] = "fruitandland.com",
            ["Authentication:Google:ClientId"] = "staging-client",
            ["Authentication:Google:ClientSecret"] = "staging-secret",
            ["Staging:AllowedTestUserEmails"] = "tester@fruitandland.com",
            ["Staging:ProductionDatabaseMarkers"] = "crop-qc-dashboard-db,Database=cropqc;",
            ["Staging:ProductionGoogleDriveFolderIds"] = "0ADHRTHdG9u98Uk9PVA",
            ["Email:Provider"] = "None",
            ["FileStorage:Provider"] = "Local",
            ["QcStation:ApiBaseUrl"] = "https://crop-qc-dashboard-staging.onrender.com",
            ["PerformanceDiagnostics:Enabled"] = "true",
            ["PerformanceDiagnostics:RecentRequestLimit"] = "250"
        });

        var errors = StagingEnvironmentValidator.BuildValidationErrors(
            configuration,
            new AppEnvironmentOptions { Kind = AppEnvironmentKinds.Staging, DisplayName = "Crop QC Staging" },
            StagingEnvironmentOptions.FromConfiguration(configuration),
            GoogleAuthenticationOptions.FromConfiguration(configuration),
            EmailOptionsFactory.Create(configuration, isProduction: true, explicitEnvironmentProvider: "None"),
            new FileStorageOptions { Provider = FileStorageProviders.Local, LocalRootPath = "/var/data/cropqc-staging-files" },
            new GoogleDriveStorageOptions(),
            PerformanceDiagnosticsOptions.FromConfiguration(configuration, new FakeHostEnvironment("Production")));

        Assert.Empty(errors);
    }

    [Fact]
    public void Staging_validator_rejects_missing_allowlist_and_external_service_risks()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["DATABASE_PROVIDER"] = "SqlServer",
            ["ConnectionStrings:CropQc"] = "Host=crop-qc-dashboard-db;Database=cropqc;Username=cropqc",
            ["Authentication:AllowedGoogleDomains"] = "fruitandland.com",
            ["Authentication:Google:ClientId"] = "",
            ["Authentication:Google:ClientSecret"] = "",
            ["Staging:AllowedTestUserEmails"] = "",
            ["Staging:ProductionDatabaseMarkers"] = "crop-qc-dashboard-db,Database=cropqc;",
            ["Staging:ProductionGoogleDriveFolderIds"] = "0ADHRTHdG9u98Uk9PVA",
            ["Email:Provider"] = "GmailUser",
            ["FileStorage:Provider"] = "GoogleDrive",
            ["GoogleDrive:RootFolderId"] = "0ADHRTHdG9u98Uk9PVA",
            ["QcStation:ApiBaseUrl"] = "https://crop-qc-dashboard.onrender.com",
            ["PerformanceDiagnostics:Enabled"] = "false"
        });

        var errors = StagingEnvironmentValidator.BuildValidationErrors(
            configuration,
            new AppEnvironmentOptions { Kind = AppEnvironmentKinds.Staging, DisplayName = "Production" },
            StagingEnvironmentOptions.FromConfiguration(configuration),
            GoogleAuthenticationOptions.FromConfiguration(configuration),
            EmailOptionsFactory.Create(configuration, isProduction: true, explicitEnvironmentProvider: "GmailUser"),
            new FileStorageOptions { Provider = FileStorageProviders.GoogleDrive },
            new GoogleDriveStorageOptions { RootFolderId = "0ADHRTHdG9u98Uk9PVA" },
            PerformanceDiagnosticsOptions.FromConfiguration(configuration, new FakeHostEnvironment("Production")));

        Assert.Contains(errors, error => error.Contains("DATABASE_PROVIDER", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("production database", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("Google", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("approved test accounts", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("Email__Provider", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("Google Drive", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("PerformanceDiagnostics", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("QcStation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Staging_allowlist_is_enforced_after_google_domain_check()
    {
        var configuration = BuildConfiguration(new Dictionary<string, string?>
        {
            ["Staging:AllowedTestUserEmails"] = "allowed@fruitandland.com, second@fruitandland.com"
        });
        var options = StagingEnvironmentOptions.FromConfiguration(configuration);

        Assert.True(options.IsAllowedTestUser("ALLOWED@fruitandland.com"));
        Assert.False(options.IsAllowedTestUser("outsider@fruitandland.com"));
    }

    [Fact]
    public void Layout_shows_staging_banner_only_for_staging_environment()
    {
        var layout = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Shared", "_Layout.cshtml"));

        Assert.Contains("AppEnvironment.IsStaging", layout);
        Assert.Contains("STAGING - Non-production data", layout);
        Assert.DoesNotContain("TEST SITE", layout);
    }

    [Fact]
    public void Diagnostics_page_is_staging_only_and_admin_authorized()
    {
        var controllerType = typeof(DiagnosticsController);
        var authorize = controllerType.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
            .Cast<AuthorizeAttribute>()
            .Single();
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "DiagnosticsController.cs"));
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Diagnostics", "Requests.cshtml"));

        Assert.Equal(AccessPolicyNames.ConfigurationAdmin, authorize.Policy);
        Assert.Contains("appEnvironment.IsStaging", controller);
        Assert.Contains("return NotFound()", controller);
        Assert.DoesNotContain("CommandText", view);
        Assert.DoesNotContain("Parameters", view);
    }

    [Fact]
    public void Render_blueprint_defines_isolated_staging_service()
    {
        var render = File.ReadAllText(FindRepositoryFile("render.yaml"));

        Assert.Contains("name: crop-qc-dashboard-staging", render);
        Assert.Contains("name: crop-qc-dashboard-staging-db", render);
        Assert.Contains("databaseName: cropqc_staging", render);
        Assert.Contains("AppEnvironment__Kind", render);
        Assert.Contains("value: Staging", render);
        Assert.Contains("Staging__AllowedTestUserEmails", render);
        Assert.Contains("FileStorage__Provider", render);
        Assert.Contains("value: Local", render);
        Assert.Contains("Email__Provider", render);
        Assert.Contains("PerformanceDiagnostics__Enabled", render);
    }

    [Fact]
    public void Program_does_not_add_development_authentication_bypass()
    {
        var program = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Program.cs"));
        var webRoot = Path.GetDirectoryName(FindRepositoryFile("src", "CropQc.Web", "Program.cs"))!;
        var webSources = Directory.GetFiles(webRoot, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText);

        Assert.Contains("AddGoogle", program);
        Assert.Contains("StagingEnvironmentOptions", program);
        Assert.DoesNotContain("AddScheme<Test", program);
        Assert.DoesNotContain("DevelopmentAuthentication", string.Concat(webSources));
        Assert.DoesNotContain("FakeAuthentication", string.Concat(webSources));
    }

    [Fact]
    public void Staging_docs_cover_setup_and_pr122_validation_use()
    {
        var docs = File.ReadAllText(FindRepositoryFile("docs", "staging-environment.md"));

        Assert.Contains("crop-qc-dashboard-staging", docs);
        Assert.Contains("Staging__AllowedTestUserEmails", docs);
        Assert.Contains("Email__Provider=None", docs);
        Assert.Contains("FileStorage__Provider=Local", docs);
        Assert.Contains("/Admin/Diagnostics/Requests", docs);
        Assert.Contains("PR #122", docs);
    }

    private static IConfiguration BuildConfiguration(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    private static string FindRepositoryFile(params string[] pathParts)
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrWhiteSpace(current))
        {
            var candidate = Path.Combine(new[] { current }.Concat(pathParts).ToArray());
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = Directory.GetParent(current)?.FullName ?? "";
        }

        throw new FileNotFoundException(string.Join(Path.DirectorySeparatorChar, pathParts));
    }

    private sealed class FakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "CropQc.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
