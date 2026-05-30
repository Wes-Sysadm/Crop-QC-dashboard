using System.IO.Compression;
using CropQc.Data.Entities;
using CropQc.QcStation.Fta;
using CropQc.Web.Services;

namespace CropQc.Api.Tests.QcStation;

public sealed class QcStationSetupPackageTests
{
    [Fact]
    public void SetupPackage_ContainsConfigInstallScriptAndReadme()
    {
        var station = new CropQc.Data.Entities.QcStation
        {
            Id = 1,
            StationCode = "WP-QC-01",
            StationName = "WP QC Station 1",
            Name = "WP QC Station 1",
            WarehouseCode = "WP"
        };

        var package = QcStationSetupPackageBuilder.Build(station, """{"StationName":"WP QC Station 1"}""");

        using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        Assert.Contains(archive.Entries, x => x.FullName == "qcstation.settings.json");
        Assert.Contains(archive.Entries, x => x.FullName == "Install-CropQcStation.cmd");
        Assert.Contains(archive.Entries, x => x.FullName == "install-qcstation.ps1");
        Assert.Contains(archive.Entries, x => x.FullName == "README.txt");

        var command = ReadEntry(archive, "Install-CropQcStation.cmd");
        Assert.Contains("powershell.exe -NoProfile -ExecutionPolicy Bypass", command);
        Assert.Contains("install-qcstation.ps1", command);

        var script = ReadEntry(archive, "install-qcstation.ps1");
        Assert.Contains(@"C:\Program Files\CropQc\QcStation", script);
        Assert.Contains(@"C:\ProgramData\CropQc\QcStation\qcstation.settings.json", script);
        Assert.Contains("$packageHasApp = $false", script);
        Assert.Contains("$appBackupDirectory = \"$appDirectory.backup-$timestamp\"", script);
        Assert.Contains("qcstation.settings.backup-$timestamp.json", script);
        Assert.Contains("HKCU:\\Software\\Classes\\cropqcstation", script);
        Assert.Contains("URL Protocol", script);
        Assert.Contains(@"C:\Program Files\CropQc\QcStation\CropQc.QcStation.WinForms.exe", script);
        Assert.Contains("shell\\open\\command", script);
        Assert.Contains("%1", script);
        Assert.Contains("Crop QC Station.lnk", script);

        var readme = ReadEntry(archive, "README.txt");
        Assert.Contains("Double-click Install-CropQcStation.cmd", readme);
        Assert.Contains("Config-only setup package", readme);
        Assert.Contains("cropqcstation://", readme);
        Assert.DoesNotContain("Set-ExecutionPolicy -Scope Process Bypass", readme);
    }

    [Fact]
    public void SetupPackage_IncludesAppFolderWhenPayloadExists()
    {
        var station = new CropQc.Data.Entities.QcStation
        {
            Id = 1,
            StationCode = "WP-QC-01",
            StationName = "WP QC Station 1",
            Name = "WP QC Station 1",
            WarehouseCode = "WP"
        };
        var payloadRoot = Directory.CreateTempSubdirectory("cropqc-station-payload-test");
        try
        {
            File.WriteAllText(Path.Combine(payloadRoot.FullName, "CropQc.QcStation.WinForms.exe"), "fake exe");
            File.WriteAllText(Path.Combine(payloadRoot.FullName, "CropQc.QcStation.dll"), "fake dll");

            var package = QcStationSetupPackageBuilder.Build(station, """{"StationName":"WP QC Station 1"}""", payloadRoot.FullName);

            using var archive = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
            Assert.Contains(archive.Entries, x => x.FullName == "app/CropQc.QcStation.WinForms.exe");
            Assert.Contains(archive.Entries, x => x.FullName == "app/CropQc.QcStation.dll");
            Assert.Contains("$packageHasApp = $true", ReadEntry(archive, "install-qcstation.ps1"));
            Assert.Contains("Copy-Item -Path (Join-Path $sourceAppDirectory '*') -Destination $appDirectory", ReadEntry(archive, "install-qcstation.ps1"));
            Assert.Contains("Full setup package", ReadEntry(archive, "README.txt"));
        }
        finally
        {
            payloadRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public void PublishScript_StagesPayloadDirectlyInWebAppData()
    {
        var script = File.ReadAllText(FindRepositoryFile("scripts", "publish-qcstation-winforms-x86.ps1"));

        Assert.Contains("src/CropQc.Web/App_Data/QcStationWinForms", script);
        Assert.Contains("-p:TargetFramework=net9.0-windows", script);
        Assert.Contains("-p:PlatformTarget=x86", script);
        Assert.Contains("-p:EnableWindowsTargeting=true", script);
        Assert.Contains("-p:RuntimeIdentifier=win-x86", script);
        Assert.Contains("CropQc.QcStation.WinForms.exe was not found", script);
        Assert.DoesNotContain("[switch]$CopyToWebPayload", script);
    }

    [Fact]
    public void WebProject_PublishesQcStationPayloadWhenPresent()
    {
        var project = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "CropQc.Web.csproj"));

        Assert.Contains(@"App_Data\QcStationWinForms\**\*", project);
        Assert.Contains("<CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>", project);
    }

    [Fact]
    public void Dockerfile_PublishesWinFormsPayloadBeforeWebPublish()
    {
        var dockerfile = File.ReadAllText(FindRepositoryFile("Dockerfile"));

        Assert.Contains("src/CropQc.QcStation.WinForms/CropQc.QcStation.WinForms.csproj", dockerfile);
        Assert.Contains("-r win-x86", dockerfile);
        Assert.Contains("-p:TargetFramework=net9.0-windows", dockerfile);
        Assert.Contains("-p:EnableWindowsTargeting=true", dockerfile);
        Assert.Contains("-p:Platform=x86", dockerfile);
        Assert.Contains("Publishing QC Station WinForms payload", dockerfile);
        Assert.Contains("-o src/CropQc.Web/App_Data/QcStationWinForms", dockerfile);
        Assert.Contains("test -f src/CropQc.Web/App_Data/QcStationWinForms/CropQc.QcStation.WinForms.exe", dockerfile);
        Assert.Contains("dotnet publish src/CropQc.Web/CropQc.Web.csproj", dockerfile);
    }

    [Fact]
    public void AdminQcStationsView_BlocksFullSetupWhenPayloadMissing()
    {
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Admin", "QcStations.cshtml"));

        Assert.Contains("QC Station app payload is missing", view);
        Assert.Contains("Full Setup Package Unavailable", view);
        Assert.Contains("disabled title=\"Deploy the WinForms payload before generating setup packages.\"", view);
        Assert.Contains("Add Station and Download Full Setup Package", view);
    }

    [Fact]
    public void AdminController_BlocksPackageCreateAndRotateWhenPayloadMissing()
    {
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "AdminController.cs"));

        Assert.Contains("RequestsSetupPackage(downloadType) && !qcStationAdminService.AppPayloadAvailable", controller);
        Assert.Contains("Full setup packages cannot be generated", controller);
        Assert.Contains("private static bool RequestsSetupPackage", controller);
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
    public void SampleDetailView_UsesCropQcStationProtocolLink()
    {
        var viewPath = FindRepositoryFile("src", "CropQc.Web", "Views", "Samples", "Details.cshtml");
        var view = File.ReadAllText(viewPath);

        Assert.Contains("href=\"cropqcstation://sample/@Model.Sample.Id\"", view);
        Assert.Contains("Requires the QC Station setup package", view);
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

    private static string ReadEntry(ZipArchive archive, string entryName)
    {
        var entry = archive.GetEntry(entryName) ?? throw new InvalidOperationException($"Entry {entryName} was not found.");
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
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
