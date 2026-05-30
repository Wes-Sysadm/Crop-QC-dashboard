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

        var readme = ReadEntry(archive, "README.txt");
        Assert.Contains("Double-click Install-CropQcStationConfig.cmd", readme);
        Assert.DoesNotContain("Set-ExecutionPolicy -Scope Process Bypass", readme);
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

            var path = StationConfiguration.ResolveSettingsPath(null, tempRoot.FullName);

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
}
