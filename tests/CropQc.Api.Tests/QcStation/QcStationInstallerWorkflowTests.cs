using CropQc.QcStation.Fta;

namespace CropQc.Api.Tests.QcStation;

public sealed class QcStationInstallerWorkflowTests
{
    [Fact]
    public void Dockerfile_BuildsOnlyWebDashboard()
    {
        var dockerfile = File.ReadAllText(FindRepositoryFile("Dockerfile"));

        Assert.Contains("dotnet publish src/CropQc.Web/CropQc.Web.csproj", dockerfile);
        Assert.Contains("test -f /app/publish/CropQc.Web.dll", dockerfile);
        Assert.Contains("test -f /app/publish/CropQc.Shared.dll", dockerfile);
        Assert.Contains("test -f /app/publish/CropQc.Data.dll", dockerfile);
        Assert.DoesNotContain("CropQc.QcStation.WinForms.csproj", dockerfile);
        Assert.DoesNotContain("App_Data/QcStationWinForms", dockerfile);
    }

    [Fact]
    public void WebProject_NoLongerPublishesWinFormsPayload()
    {
        var project = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "CropQc.Web.csproj"));

        Assert.DoesNotContain(@"App_Data\QcStationWinForms\**\*", project);
    }

    [Fact]
    public void BuildInstallerScript_PublishesWinFormsAndSupportsSigning()
    {
        var script = File.ReadAllText(FindRepositoryFile("scripts", "build-qcstation-installer.ps1"));

        Assert.Contains("CropQc.QcStation.WinForms.csproj", script);
        Assert.Contains("-r win-x86", script);
        Assert.Contains("-p:PlatformTarget=x86", script);
        Assert.Contains("CropQc.QcStation.Installer.wixproj", script);
        Assert.Contains("CropQcStationSetup.msi", script);
        Assert.Contains("SIGN_INSTALLER", script);
        Assert.Contains("SIGNING_MODE", script);
        Assert.Contains("SIGN_CERT_PATH", script);
        Assert.Contains("Installer is unsigned and may trigger SmartScreen/Defender", script);
    }

    [Fact]
    public void WixInstaller_RegistersProtocolAndContainsNoStationSecrets()
    {
        var wxs = File.ReadAllText(FindRepositoryFile("installers", "CropQc.QcStation.Installer", "Package.wxs"));

        Assert.Contains("ProgramFiles64Folder", wxs);
        Assert.Contains("INSTALLFOLDER", wxs);
        Assert.Contains("CropQc.QcStation.WinForms.exe", wxs);
        Assert.Contains("cropqcstation", wxs);
        Assert.Contains("URL Protocol", wxs);
        Assert.Contains("StartMenuShortcut", wxs);
        Assert.Contains("DesktopShortcut", wxs);
        Assert.DoesNotContain("QcStationApiKey", wxs);
        Assert.DoesNotContain("qcstation.settings.json", wxs);
    }

    [Fact]
    public void AdminDownloads_UsesMsiInstallerAndNoScriptZipDefault()
    {
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "AdminController.cs"));
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Admin", "Downloads.cshtml"));

        Assert.Contains("CropQcStationSetup.msi", controller);
        Assert.Contains("DownloadQcStationInstaller", controller);
        Assert.Contains("QC Station installer has not been deployed yet", controller);
        Assert.Contains("Download MSI", controller);
        Assert.Contains("Not deployed", view);
        Assert.DoesNotContain("Install-CropQcStation.cmd", controller);
    }

    [Fact]
    public void AdminQcStations_DownloadsJsonOnly()
    {
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Admin", "QcStations.cshtml"));
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "QcStationAdminService.cs"));

        Assert.Contains("Add Station and Download Config JSON", view);
        Assert.Contains("Rotate Key and Download Config JSON", view);
        Assert.Contains("The MSI contains no station secrets", view);
        Assert.Contains("QcStationConfigDownload(string FileName, string Json)", service);
        Assert.DoesNotContain("ZipArchive", service);
        Assert.DoesNotContain("PackageBytes", service);
    }

    [Fact]
    public void ProtocolLaunch_ParsesValidSampleUrl()
    {
        var launch = QcStationProtocolLaunch.Parse("cropqcstation://sample/123");

        Assert.NotNull(launch);
        Assert.Equal(123, launch.SampleId);
        Assert.Null(launch.ReceiptId);
    }

    [Theory]
    [InlineData("cropqcstation://sample/not-a-number")]
    [InlineData("cropqcstation://sample/0")]
    [InlineData("https://example.com/sample/123")]
    [InlineData("cropqcstation://unknown/123")]
    public void ProtocolLaunch_RejectsInvalidUrls(string value)
    {
        Assert.Null(QcStationProtocolLaunch.Parse(value));
    }

    [Fact]
    public void SampleDetailView_UsesCropQcStationProtocolLinkAndInstallerGuidance()
    {
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Samples", "Details.cshtml"));

        Assert.Contains("href=\"cropqcstation://sample/@Model.Sample.Id\"", view);
        Assert.Contains("QC Station App Installer from Admin -> Downloads", view);
        Assert.Contains("download station config from Admin -> QC Stations and import it", view);
    }

    [Fact]
    public void ResolveSettingsPath_PrefersCommandLinePath()
    {
        var path = StationConfiguration.ResolveSettingsPath(@"C:\custom\qcstation.settings.json", baseDirectory: @"C:\unused");

        Assert.Equal(@"C:\custom\qcstation.settings.json", path);
    }

    [Fact]
    public void ResolveSettingsPath_UsesDevelopmentFallback()
    {
        var tempRoot = Directory.CreateTempSubdirectory("cropqc-station-config-test");
        try
        {
            var repoConfigDirectory = Path.Combine(tempRoot.FullName, "src", "CropQc.QcStation");
            Directory.CreateDirectory(repoConfigDirectory);
            var repoConfigPath = Path.Combine(repoConfigDirectory, "qcstation.settings.json");
            File.WriteAllText(repoConfigPath, "{}");

            var path = StationConfiguration.ResolveSettingsPath(null, tempRoot.FullName, Path.Combine(tempRoot.FullName, "missing-programdata-config.json"));

            Assert.Equal(repoConfigPath, path);
        }
        finally
        {
            tempRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public void StationConfigImport_RequiresStationCodeAndApiKey()
    {
        var tempRoot = Directory.CreateTempSubdirectory("cropqc-station-import-test");
        try
        {
            var invalidPath = Path.Combine(tempRoot.FullName, "invalid.json");
            File.WriteAllText(invalidPath, """{"StationName":"WP QC Station 1"}""");

            Assert.Throws<InvalidDataException>(() => StationConfigurationImport.ValidateSource(invalidPath));

            var validPath = Path.Combine(tempRoot.FullName, "qcstation.settings.json");
            File.WriteAllText(validPath, """{"StationName":"WP QC Station 1","QcStationCode":"WP-QC-01","QcStationApiKey":"secret"}""");
            var targetPath = Path.Combine(tempRoot.FullName, "ProgramData", "qcstation.settings.json");

            var installedPath = StationConfigurationImport.Import(validPath, targetPath);

            Assert.Equal(targetPath, installedPath);
            Assert.True(File.Exists(targetPath));
        }
        finally
        {
            tempRoot.Delete(recursive: true);
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
