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
        Assert.Contains(archive.Entries, x => x.FullName == "Install-CropQcStationConfig.cmd");
        Assert.Contains(archive.Entries, x => x.FullName == "install-qcstation-config.ps1");
        Assert.Contains(archive.Entries, x => x.FullName == "README.txt");

        var command = ReadEntry(archive, "Install-CropQcStationConfig.cmd");
        Assert.Contains("powershell.exe -NoProfile -ExecutionPolicy Bypass", command);
        Assert.Contains("install-qcstation-config.ps1", command);

        var script = ReadEntry(archive, "install-qcstation-config.ps1");
        Assert.Contains(@"C:\ProgramData\CropQc\QcStation\qcstation.settings.json", script);
        Assert.Contains("qcstation.settings.backup-$timestamp.json", script);
        Assert.Contains("HKCU:\\Software\\Classes\\cropqcstation", script);
        Assert.Contains("URL Protocol", script);
        Assert.Contains(@"C:\Program Files\CropQc\QcStation\CropQc.QcStation.WinForms.exe", script);
        Assert.Contains("shell\\open\\command", script);
        Assert.Contains("%1", script);

        var readme = ReadEntry(archive, "README.txt");
        Assert.Contains("Double-click Install-CropQcStationConfig.cmd", readme);
        Assert.Contains("cropqcstation://", readme);
        Assert.DoesNotContain("Set-ExecutionPolicy -Scope Process Bypass", readme);
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
