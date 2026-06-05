using CropQc.QcStation.Fta;
using CropQc.Web.Controllers;
using CropQc.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace CropQc.Api.Tests.QcStation;

public sealed class QcStationInstallerWorkflowTests
{
    private const string QcStationInstallerGoogleDriveUrl = "https://drive.google.com/file/d/1NQzoomWfDQpP2a3q-N_g9_lgIHGD37nt/view?usp=drive_link";
    private const string FtaBorlndmmGoogleDriveUrl = "https://drive.google.com/file/d/example-borlndmm/view?usp=drive_link";
    private const string MasterFolderGoogleDriveUrl = "https://drive.google.com/drive/folders/example-master-folder";

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
        Assert.Contains("Upload this installer to Google Drive and set Downloads__QcStationInstallerUrl in Render.", script);
    }

    [Fact]
    public void WixInstaller_InstallsAppCreatesConfigFolderAndRegistersProtocol()
    {
        var wxs = File.ReadAllText(FindRepositoryFile("installers", "CropQc.QcStation.Installer", "Package.wxs"));

        Assert.Contains("ProgramFiles64Folder", wxs);
        Assert.Contains("INSTALLFOLDER", wxs);
        Assert.Contains("CommonAppDataFolder", wxs);
        Assert.Contains("ProgramDataStationFolder", wxs);
        Assert.Contains("ProgramDataConfigFolder", wxs);
        Assert.Contains("<CreateFolder />", wxs);
        Assert.Contains("CropQc.QcStation.WinForms.exe", wxs);
        Assert.Contains("cropqcstation", wxs);
        Assert.Contains("URL Protocol", wxs);
        Assert.Contains("StartMenuShortcut", wxs);
        Assert.Contains("DesktopShortcut", wxs);
        Assert.Contains("MajorUpgrade", wxs);
        Assert.Contains("UpgradeCode", wxs);
        Assert.DoesNotContain("QcStationApiKey", wxs);
        Assert.DoesNotContain("qcstation.settings.json", wxs);
    }

    [Fact]
    public void AdminDownloads_UsesMsiInstallerAndNoScriptZipDefault()
    {
        var controller = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Controllers", "AdminController.cs"));
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Admin", "Downloads.cshtml"));

        Assert.Contains("CropQcStationSetup.msi", controller);
        Assert.Contains("Downloads:QcStationInstallerUrl", controller);
        Assert.Contains("QC Station installer link is not configured", controller);
        Assert.Contains("Upload CropQcStationSetup.msi to Google Drive", controller);
        Assert.Contains("Crop QC Station App Installer", controller);
        Assert.Contains("Hosted Files Folder", controller);
        Assert.Contains("Downloads:MasterFolderUrl", controller);
        Assert.Contains("Downloads folder link is not configured. Set Downloads__MasterFolderUrl in Render.", controller);
        Assert.Contains("Open Google Drive Folder", controller);
        Assert.Contains("Open Google Drive Download", controller);
        Assert.Contains("FTA DLL Installer", controller);
        Assert.Contains("FTA borlndmm.dll Dependency", controller);
        Assert.Contains("Downloads:FtaBorlndmmUrl", controller);
        Assert.Contains("Downloads__FtaBorlndmmUrl", controller);
        Assert.Contains("Not deployed", view);
        Assert.Contains("Manage QC Stations", view);
        Assert.Contains("Station configs are generated and downloaded from Admin", view);
        Assert.Contains("Install Crop QC Station App Installer from this page", view);
        Assert.Contains("Download the station config JSON from Admin", view);
        Assert.DoesNotContain("\"QC Station Configs\"", controller);
        Assert.DoesNotContain("qcstation.settings.json", controller);
        Assert.DoesNotContain("Add Station and Download Config JSON", view);
        Assert.DoesNotContain("Rotate Key and Download Config JSON", view);
        Assert.DoesNotContain("Download Config</button>", view);
        Assert.DoesNotContain("action=\"/Admin/QcStations/Create\"", view);
        Assert.DoesNotContain("action=\"/Admin/QcStations/RotateKey\"", view);
        Assert.DoesNotContain("Install-CropQcStation.cmd", controller);
        Assert.DoesNotContain("PhysicalFile", controller);
    }

    [Fact]
    public void AdminDownloads_ShowsGoogleDriveMsiLinkWhenConfigured()
    {
        var controller = CreateAdminController(new Dictionary<string, string?>
        {
            ["Downloads:QcStationInstallerUrl"] = QcStationInstallerGoogleDriveUrl
        });

        var result = Assert.IsType<ViewResult>(controller.Downloads());
        var model = Assert.IsType<AdminDownloadsViewModel>(result.Model);
        var installer = Assert.Single(model.Downloads, item => item.Name == "Crop QC Station App Installer");

        Assert.True(installer.IsAvailable);
        Assert.True(installer.OpensInNewTab);
        Assert.Equal("Open Google Drive Download", installer.ActionText);
        Assert.Equal(QcStationInstallerGoogleDriveUrl, installer.Url);
        Assert.DoesNotContain("not configured", installer.Notes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AdminDownloads_ShowsMasterGoogleDriveFolderWhenConfigured()
    {
        var controller = CreateAdminController(new Dictionary<string, string?>
        {
            ["Downloads:MasterFolderUrl"] = MasterFolderGoogleDriveUrl
        });

        var result = Assert.IsType<ViewResult>(controller.Downloads());
        var model = Assert.IsType<AdminDownloadsViewModel>(result.Model);
        var folder = Assert.Single(model.Downloads, item => item.Name == "Hosted Files Folder");

        Assert.True(folder.IsAvailable);
        Assert.True(folder.OpensInNewTab);
        Assert.Equal("Open Google Drive Folder", folder.ActionText);
        Assert.Equal(MasterFolderGoogleDriveUrl, folder.Url);
    }

    [Fact]
    public void AdminDownloads_ShowsMissingMasterFolderGuidanceWhenUrlIsMissing()
    {
        var controller = CreateAdminController([]);

        var result = Assert.IsType<ViewResult>(controller.Downloads());
        var model = Assert.IsType<AdminDownloadsViewModel>(result.Model);
        var folder = Assert.Single(model.Downloads, item => item.Name == "Hosted Files Folder");

        Assert.False(folder.IsAvailable);
        Assert.False(folder.OpensInNewTab);
        Assert.Equal("", folder.Url);
        Assert.Contains("Downloads folder link is not configured", folder.Notes);
        Assert.Contains("Downloads__MasterFolderUrl", folder.Notes);
    }

    [Fact]
    public void AdminDownloads_ShowsBorlndmmDependencyLinkWhenConfigured()
    {
        var controller = CreateAdminController(new Dictionary<string, string?>
        {
            ["Downloads:FtaBorlndmmUrl"] = FtaBorlndmmGoogleDriveUrl
        });

        var result = Assert.IsType<ViewResult>(controller.Downloads());
        var model = Assert.IsType<AdminDownloadsViewModel>(result.Model);
        var dependency = Assert.Single(model.Downloads, item => item.Name == "FTA borlndmm.dll Dependency");

        Assert.True(dependency.IsAvailable);
        Assert.True(dependency.OpensInNewTab);
        Assert.Equal("Open Google Drive Download", dependency.ActionText);
        Assert.Equal(FtaBorlndmmGoogleDriveUrl, dependency.Url);
        Assert.Contains("Back up the existing borlndmm.dll", dependency.Notes);
    }

    [Fact]
    public void AdminDownloads_ShowsMissingBorlndmmGuidanceWhenUrlIsMissing()
    {
        var controller = CreateAdminController([]);

        var result = Assert.IsType<ViewResult>(controller.Downloads());
        var model = Assert.IsType<AdminDownloadsViewModel>(result.Model);
        var dependency = Assert.Single(model.Downloads, item => item.Name == "FTA borlndmm.dll Dependency");

        Assert.False(dependency.IsAvailable);
        Assert.False(dependency.OpensInNewTab);
        Assert.Equal("", dependency.Url);
        Assert.Contains("FTA borlndmm.dll dependency link is not configured", dependency.Notes);
        Assert.Contains("Downloads__FtaBorlndmmUrl", dependency.Notes);
    }

    [Fact]
    public void AdminDownloads_ShowsMissingMsiGuidanceWhenUrlIsMissing()
    {
        var controller = CreateAdminController([]);

        var result = Assert.IsType<ViewResult>(controller.Downloads());
        var model = Assert.IsType<AdminDownloadsViewModel>(result.Model);
        var installer = Assert.Single(model.Downloads, item => item.Name == "Crop QC Station App Installer");

        Assert.False(installer.IsAvailable);
        Assert.False(installer.OpensInNewTab);
        Assert.Equal("", installer.Url);
        Assert.Contains("QC Station installer link is not configured", installer.Notes);
        Assert.Contains("Downloads__QcStationInstallerUrl", installer.Notes);
    }

    [Fact]
    public void ProductionSettings_IncludeGoogleDriveMsiInstallerUrl()
    {
        var productionSettings = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "appsettings.Production.json"));

        Assert.Contains("\"Downloads\"", productionSettings);
        Assert.Contains("\"MasterFolderUrl\"", productionSettings);
        Assert.Contains("\"QcStationInstallerUrl\"", productionSettings);
        Assert.Contains(QcStationInstallerGoogleDriveUrl, productionSettings);
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
        Assert.Contains("FtaManualCaptureSafeMode = true", service);
        Assert.Contains("FtaConnectionMode = \"Auto\"", service);
        Assert.Contains("FtaManualRearmDelayMs = 250", service);
        Assert.Contains("FtaHomePollIntervalMs = 100", service);
        Assert.Contains("FtaMaxHomeWaitMs = 5000", service);
        Assert.Contains("FtaFirmnessUnit = \"Kilograms\"", service);
        Assert.Contains("FtaSerialPort = (string?)null", service);
        Assert.Contains("FtaSerialDataBits = 8", service);
        Assert.Contains("FtaSerialParity = \"None\"", service);
        Assert.Contains("FtaSerialStopBits = \"One\"", service);
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
        Assert.Contains("<h2>FTA Pressure Capture</h2>", view);
        Assert.Contains("Continue entering weight and grade on this page as needed", view);
        Assert.Contains("rotate/download station config from Admin -> QC Stations and import it", view);
    }

    [Fact]
    public void ReceiptDetailView_OffersOpenInQcStationForLinkedSamplesOnly()
    {
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Receipts", "Details.cshtml"));

        Assert.Contains("@foreach (var sample in Model.Samples)", view);
        Assert.Contains("href=\"cropqcstation://sample/@sample.Id\"", view);
        Assert.Contains("Open in QC Station", view);
        Assert.Contains("Use Open in QC Station to capture FTA pressures for a sample", view);
        Assert.True(view.IndexOf("cropqcstation://sample/@sample.Id", StringComparison.Ordinal) > view.IndexOf("@foreach (var sample in Model.Samples)", StringComparison.Ordinal));
    }

    [Fact]
    public void DailyQcView_OffersOpenInQcStationForEachSample()
    {
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "DailyQc", "Index.cshtml"));

        Assert.Contains("@foreach (var sample in Model.Samples)", view);
        Assert.Contains("href=\"cropqcstation://sample/@sample.Id\"", view);
        Assert.Contains("Open in QC Station", view);
    }

    [Fact]
    public void ResolveSettingsPath_UsesCommandLinePathWhenProgramDataConfigIsMissing()
    {
        var path = StationConfiguration.ResolveSettingsPath(@"C:\custom\qcstation.settings.json", baseDirectory: @"C:\unused", installedSettingsPath: @"C:\unused\missing-programdata-config.json");

        Assert.Equal(@"C:\custom\qcstation.settings.json", path);
    }

    [Fact]
    public void ResolveSettingsPath_PrefersProgramDataPathOverCommandLinePath()
    {
        var tempRoot = Directory.CreateTempSubdirectory("cropqc-station-programdata-test");
        try
        {
            var installedPath = Path.Combine(tempRoot.FullName, "ProgramData", "qcstation.settings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(installedPath)!);
            File.WriteAllText(installedPath, "{}");

            var path = StationConfiguration.ResolveSettingsPath(@"C:\custom\qcstation.settings.json", tempRoot.FullName, installedPath);

            Assert.Equal(installedPath, path);
        }
        finally
        {
            tempRoot.Delete(recursive: true);
        }
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
    public void StationConfigImport_RequiresStationCodeApiKeyAndApiBaseUrl()
    {
        var tempRoot = Directory.CreateTempSubdirectory("cropqc-station-import-test");
        try
        {
            var invalidPath = Path.Combine(tempRoot.FullName, "invalid.json");
            File.WriteAllText(invalidPath, """{"StationName":"WP QC Station 1"}""");

            Assert.Throws<InvalidDataException>(() => StationConfigurationImport.ValidateSource(invalidPath));

            var validPath = Path.Combine(tempRoot.FullName, "qcstation.settings.json");
            File.WriteAllText(validPath, """{"StationName":"WP QC Station 1","WarehouseCode":"WP","QcStationCode":"WP-QC-01","QcStationApiKey":"secret","ApiBaseUrl":"https://crop-qc-dashboard.onrender.com"}""");
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

    [Fact]
    public void ResolveSettingsPath_DoesNotUseDevelopmentFallbackFromProgramFilesInstall()
    {
        var installPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "CropQc",
            "QcStation");

        var path = StationConfiguration.ResolveSettingsPath(
            null,
            installPath,
            Path.Combine(installPath, "missing-programdata-config.json"));

        Assert.Equal(Path.Combine(installPath, "qcstation.settings.json"), path);
    }

    [Fact]
    public void StationConfigImport_BacksUpExistingConfig()
    {
        var tempRoot = Directory.CreateTempSubdirectory("cropqc-station-import-backup-test");
        try
        {
            var sourcePath = Path.Combine(tempRoot.FullName, "qcstation.settings.json");
            File.WriteAllText(sourcePath, """{"StationName":"WP QC Station 1","WarehouseCode":"WP","QcStationCode":"WP-QC-01","QcStationApiKey":"secret","ApiBaseUrl":"https://crop-qc-dashboard.onrender.com"}""");
            var targetPath = Path.Combine(tempRoot.FullName, "ProgramData", "qcstation.settings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            File.WriteAllText(targetPath, """{"old":true}""");

            StationConfigurationImport.Import(sourcePath, targetPath);

            Assert.True(File.Exists(targetPath));
            Assert.Single(Directory.GetFiles(Path.GetDirectoryName(targetPath)!, "qcstation.settings.backup-*.json"));
        }
        finally
        {
            tempRoot.Delete(recursive: true);
        }
    }

    [Fact]
    public void WinFormsUi_WiresFtaSetupCalibrationCommand()
    {
        var mainForm = File.ReadAllText(FindRepositoryFile("src", "CropQc.QcStation.WinForms", "MainForm.cs"));
        var program = File.ReadAllText(FindRepositoryFile("src", "CropQc.QcStation.WinForms", "Program.cs"));
        var ftaButtonPanel = ExtractBetween(mainForm, "private Control BuildButtonPanel()", "private Control BuildPressureCapturePanel()");

        Assert.Contains("AddFtaCommand(flow, \"Initialize\"", ftaButtonPanel);
        Assert.Contains("AddFtaCommand(flow, \"Calibration\"", ftaButtonPanel);
        Assert.Contains("AddFtaCaptureButton(flow, \"Diagnostics\", RunFullFtaDiagnosticAsync)", ftaButtonPanel);
        Assert.Contains("AddFtaContinuousButton(flow, \"Start Manual Capture\"", ftaButtonPanel);
        Assert.Contains("AddFtaContinuousButton(flow, \"Stop Capture\"", ftaButtonPanel);
        Assert.Contains("AddFtaCaptureButton(flow, \"Quit\"", ftaButtonPanel);
        Assert.Contains("Use Manual/Button mode only. Press and hold the physical FTA button for each test.", mainForm);
        Assert.Contains("stationService.OpenSetupAsync()", mainForm);
        Assert.Contains("Auto-save after each captured reading", mainForm);
        Assert.Contains("Checked = true", mainForm);
        Assert.Contains("Save / Retry Unsaved Pressures", mainForm);
        Assert.Contains("QueueAutoSave(capture)", mainForm);
        Assert.Contains("FtaHomePollIntervalMs", mainForm);
        Assert.Contains("FtaMaxHomeWaitMs", mainForm);
        Assert.Contains("Ready for next FTA button press.", mainForm);
        Assert.Contains("Dashboard connection successful. Loaded", mainForm);
        Assert.Contains("Dashboard connection failed. Unauthorized / invalid key.", mainForm);
        Assert.DoesNotContain("Start Auto", ftaButtonPanel);
        Assert.DoesNotContain("Auto Firmness", ftaButtonPanel);
        Assert.DoesNotContain("Demo-Style", ftaButtonPanel);
        Assert.DoesNotContain("Get Latest", ftaButtonPanel);
        Assert.DoesNotContain("Start And Wait", ftaButtonPanel);
        Assert.DoesNotContain("Manual/Button Firmness", ftaButtonPanel);
        Assert.DoesNotContain("Return Probe", ftaButtonPanel);
        Assert.DoesNotContain("Cancel FTA", ftaButtonPanel);
        Assert.DoesNotContain("Clear Log", ftaButtonPanel);
        Assert.Contains("Import / Replace Station Config", mainForm);
        Assert.Contains("Open Config Folder", mainForm);
        Assert.Contains("Forget Current Config", mainForm);
        Assert.Contains("LoadedConfigPath", mainForm);
        Assert.Contains("apiClient = null", mainForm);
        Assert.Contains("WaitForManualRearmReadyAsync", mainForm);
        Assert.Contains("FtaManualCaptureSafeMode", mainForm);
        Assert.Contains("BuildFtaDiagnosticsPanel", mainForm);
        Assert.Contains("Run Full FTA Diagnostic", mainForm);
        Assert.Contains("Copy Diagnostic Report", mainForm);
        Assert.Contains("Save FTA Connection Settings", mainForm);
        Assert.Contains("FtaConnectionMode", mainForm);
        Assert.Contains("FtaSerialPort", mainForm);
        Assert.Contains("RequiresImport", program);
        Assert.Contains("StationConfigurationImport.ValidateSource(settingsPath)", program);
    }

    [Fact]
    public void ConfigImportDialog_DefaultsToDownloadsAndJsonFiles()
    {
        var form = File.ReadAllText(FindRepositoryFile("src", "CropQc.QcStation.WinForms", "ConfigImportForm.cs"));

        Assert.Contains("InitialDirectory = ResolveInitialDirectory()", form);
        Assert.Contains("Station config JSON (*.json)|*.json|All files (*.*)|*.*", form);
        Assert.Contains("FilterIndex = 1", form);
        Assert.Contains("Downloads", form);
        Assert.Contains("Validating required fields: StationName, WarehouseCode, ApiBaseUrl, QcStationCode, QcStationApiKey", form);
    }

    [Fact]
    public void AdminDownloadsAndQcStations_ExplainConfigImportFlow()
    {
        var downloads = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Admin", "Downloads.cshtml"));
        var qcStations = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Admin", "QcStations.cshtml"));

        Assert.Contains("Import / Replace Station Config", downloads);
        Assert.Contains("Import / Replace Station Config", qcStations);
        Assert.Contains("Confirm the Station Name and Station Code shown in the app match this station", qcStations);
        Assert.Contains("Install FTADLL.exe only on FTA-connected computers", downloads);
        Assert.Contains("Downloads is only for shared installer/support files", downloads);
    }

    [Fact]
    public void QcStations_AllowsDuplicateFriendlyNamesButRequiresUniqueCodes()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "QcStationAdminService.cs"));
        var validateForm = ExtractBetween(service, "private async Task<string?> ValidateFormAsync", "private QcStationConfigDownload BuildConfigDownload");

        Assert.Contains("Station code must be unique.", validateForm);
        Assert.Contains("QcStationApiKeyValidator.IsStationCodeSafe", validateForm);
        Assert.DoesNotContain("Station name must be unique", validateForm, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("x.StationName ==", validateForm);
        Assert.DoesNotContain("x.Name ==", validateForm);
    }

    [Fact]
    public void QcStations_ViewShowsDeactivateReactivateAndDuplicateNameGuidance()
    {
        var view = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Views", "Admin", "QcStations.cshtml"));

        Assert.Contains("Station Name is a friendly label and can be reused", view);
        Assert.Contains("Station Code must be unique", view);
        Assert.Contains("/Admin/QcStations/Deactivate", view);
        Assert.Contains("/Admin/QcStations/Reactivate", view);
        Assert.Contains("If this is the same computer, reactivate it.", view);
        Assert.Contains("create a new Station Code or rotate the key", view);
    }

    [Fact]
    public void QcStations_ReactivateDoesNotRotateOrDeleteApiKey()
    {
        var service = File.ReadAllText(FindRepositoryFile("src", "CropQc.Web", "Services", "QcStationAdminService.cs"));
        var setActive = ExtractBetween(service, "public async Task<string?> SetActiveAsync", "public async Task<(string? Error, QcStationConfigDownload? Download)> RotateKeyAsync");

        Assert.Contains("station.IsActive = isActive", setActive);
        Assert.Contains("reactivate", setActive);
        Assert.Contains("deactivate", setActive);
        Assert.DoesNotContain("ApiKeyHash", setActive);
        Assert.DoesNotContain("GenerateApiKey", setActive);
        Assert.DoesNotContain("Remove(", setActive);
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

    private static AdminController CreateAdminController(Dictionary<string, string?> values)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
        return new AdminController(null!, null!, null!, null!, configuration);
    }

    private static string ExtractBetween(string value, string start, string end)
    {
        var startIndex = value.IndexOf(start, StringComparison.Ordinal);
        Assert.True(startIndex >= 0, $"Could not find start marker '{start}'.");
        var endIndex = value.IndexOf(end, startIndex, StringComparison.Ordinal);
        Assert.True(endIndex > startIndex, $"Could not find end marker '{end}'.");
        return value[startIndex..endIndex];
    }
}
