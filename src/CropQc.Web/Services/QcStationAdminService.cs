using System.IO.Compression;
using System.Text;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Security;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public interface IQcStationAdminService
{
    bool AppPayloadAvailable { get; }
    Task<QcStationsPageViewModel> GetStationsAsync(string? search, string? warehouseCode, string activeFilter, CancellationToken cancellationToken);
    Task<(string? Error, QcStationConfigDownload? Download)> CreateAsync(QcStationForm form, string changedByEmail, CancellationToken cancellationToken);
    Task<string?> UpdateAsync(QcStationForm form, string changedByEmail, CancellationToken cancellationToken);
    Task<string?> SetActiveAsync(int id, bool isActive, string changedByEmail, CancellationToken cancellationToken);
    Task<(string? Error, QcStationConfigDownload? Download)> RotateKeyAsync(int id, string changedByEmail, CancellationToken cancellationToken);
}

public sealed record QcStationConfigDownload(string FileName, string Json, string PackageFileName, byte[] PackageBytes, bool AppPayloadAvailable);

public static class QcStationSetupPackageBuilder
{
    public const string InstalledConfigPath = @"C:\ProgramData\CropQc\QcStation\qcstation.settings.json";
    public const string InstalledConfigDirectory = @"C:\ProgramData\CropQc\QcStation";
    public const string InstalledAppDirectory = @"C:\Program Files\CropQc\QcStation";
    public const string StandardWinFormsExePath = @"C:\Program Files\CropQc\QcStation\CropQc.QcStation.WinForms.exe";

    public static byte[] Build(QcStation station, string configJson, string? appPayloadPath = null)
    {
        var appPayloadAvailable = !string.IsNullOrWhiteSpace(appPayloadPath)
            && Directory.Exists(appPayloadPath)
            && File.Exists(Path.Combine(appPayloadPath, "CropQc.QcStation.WinForms.exe"));
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (appPayloadAvailable)
            {
                AddDirectory(archive, appPayloadPath!, "app");
            }

            AddText(archive, "qcstation.settings.json", configJson);
            AddText(archive, "Install-CropQcStation.cmd", BuildCommandInstaller());
            AddText(archive, "install-qcstation.ps1", BuildInstallScript(appPayloadAvailable));
            AddText(archive, "README.txt", BuildReadme(station, appPayloadAvailable));
        }

        return stream.ToArray();
    }

    public static string BuildCommandInstaller() =>
        """
        @echo off
        setlocal
        title Crop QC Station Installer

        echo Installing Crop QC Station...
        echo.

        powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0install-qcstation.ps1"
        set EXITCODE=%ERRORLEVEL%

        echo.
        if "%EXITCODE%"=="0" (
            echo Install completed. You can close this window.
        ) else (
            echo Install failed with exit code %EXITCODE%.
            echo If Windows blocked the install, right-click this file and choose Run as administrator.
        )
        echo.
        pause
        exit /b %EXITCODE%
        """;

    public static string BuildInstallScript(bool appPayloadAvailable = false) =>
        $$"""
        $ErrorActionPreference = 'Stop'

        $configDirectory = '{{InstalledConfigDirectory}}'
        $configPath = '{{InstalledConfigPath}}'
        $appDirectory = '{{InstalledAppDirectory}}'
        $appExePath = '{{StandardWinFormsExePath}}'
        $sourceConfigPath = Join-Path $PSScriptRoot 'qcstation.settings.json'
        $sourceAppDirectory = Join-Path $PSScriptRoot 'app'
        $packageHasApp = {{(appPayloadAvailable ? "$true" : "$false")}}

        try {
            $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
            $isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
            if (-not $isAdmin) {
                Write-Host "Administrator permission is required to install the QC Station app under Program Files."
                Write-Host "Requesting administrator permission..."
                Start-Process powershell.exe -Verb RunAs -Wait -ArgumentList "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
                Write-Host "Elevated installer process finished."
                exit 0
            }

            if (-not (Test-Path -LiteralPath $sourceConfigPath)) {
                throw "qcstation.settings.json was not found next to this installer script."
            }

            if ($packageHasApp) {
                if (-not (Test-Path -LiteralPath $sourceAppDirectory)) {
                    throw "The setup package says it contains the QC Station app, but the app folder was not found."
                }

                if (Test-Path -LiteralPath $appDirectory) {
                    $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
                    $appBackupDirectory = "$appDirectory.backup-$timestamp"
                    Copy-Item -LiteralPath $appDirectory -Destination $appBackupDirectory -Recurse -Force
                    Write-Host "Existing QC Station app folder backed up to $appBackupDirectory"
                }

                if (-not (Test-Path -LiteralPath $appDirectory)) {
                    New-Item -ItemType Directory -Path $appDirectory -Force | Out-Null
                }

                Copy-Item -Path (Join-Path $sourceAppDirectory '*') -Destination $appDirectory -Recurse -Force
                Write-Host "QC Station app installed to $appDirectory"
            }
            else {
                Write-Warning "This is a config-only setup package. The QC Station app payload was not included in the web deployment."
                Write-Warning "Install or copy the QC Station app to $appDirectory, then rerun this installer to register cropqcstation:// links."
            }

            if (-not (Test-Path -LiteralPath $configDirectory)) {
                New-Item -ItemType Directory -Path $configDirectory -Force | Out-Null
            }

            if (Test-Path -LiteralPath $configPath) {
                $timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
                $backupPath = Join-Path $configDirectory "qcstation.settings.backup-$timestamp.json"
                Copy-Item -LiteralPath $configPath -Destination $backupPath -Force
                Write-Host "Existing QC Station configuration backed up to $backupPath"
            }

            Copy-Item -LiteralPath $sourceConfigPath -Destination $configPath -Force
            Write-Host "QC Station configuration installed successfully."
            Write-Host "Installed path: $configPath"
            Write-Host ""

            if (Test-Path -LiteralPath $appExePath) {
                $protocolRoot = 'HKCU:\Software\Classes\cropqcstation'
                New-Item -Path $protocolRoot -Force | Out-Null
                Set-Item -Path $protocolRoot -Value 'URL:Crop QC Station'
                New-ItemProperty -Path $protocolRoot -Name 'URL Protocol' -Value '' -PropertyType String -Force | Out-Null
                New-Item -Path "$protocolRoot\DefaultIcon" -Force | Out-Null
                Set-Item -Path "$protocolRoot\DefaultIcon" -Value "`"$appExePath`",0"
                New-Item -Path "$protocolRoot\shell\open\command" -Force | Out-Null
                Set-Item -Path "$protocolRoot\shell\open\command" -Value "`"$appExePath`" `"%1`""
                Write-Host "cropqcstation:// protocol handler registered for this Windows user."

                try {
                    $desktopPath = [Environment]::GetFolderPath('DesktopDirectory')
                    if (-not [string]::IsNullOrWhiteSpace($desktopPath)) {
                        $shortcutPath = Join-Path $desktopPath 'Crop QC Station.lnk'
                        $shell = New-Object -ComObject WScript.Shell
                        $shortcut = $shell.CreateShortcut($shortcutPath)
                        $shortcut.TargetPath = $appExePath
                        $shortcut.WorkingDirectory = $appDirectory
                        $shortcut.IconLocation = "$appExePath,0"
                        $shortcut.Save()
                        Write-Host "Desktop shortcut created: $shortcutPath"
                    }
                }
                catch {
                    Write-Warning "Desktop shortcut could not be created: $($_.Exception.Message)"
                }
            }
            else {
                Write-Warning "QC Station app executable was not found. Protocol handler was not registered. Install or copy the QC Station app to $appExePath, then rerun this installer."
            }

            Write-Host ""
            Write-Host "Next steps:"
            Write-Host "1. Launch Crop QC Station."
            Write-Host "2. Install FTADLL.exe from Admin Downloads if this computer is connected to an FTA."
            Write-Host "3. Confirm station code and warehouse in the app."
            Write-Host "4. Test Open in QC Station from a dashboard sample page."
        }
        catch {
            Write-Error "Crop QC Station install failed: $($_.Exception.Message)"
            exit 1
        }
        """;

    public static string BuildReadme(QcStation station, bool appPayloadAvailable = false)
    {
        var stationName = string.IsNullOrWhiteSpace(station.StationName) ? station.Name : station.StationName;
        var packageType = appPayloadAvailable
            ? "Full setup package: installs the QC Station app, station config, and cropqcstation:// protocol handler."
            : "Config-only setup package: the QC Station app payload was not deployed with the website.";
        return $$"""
        Crop QC Station Setup Package
        =============================

        Station name: {{stationName}}
        Station code: {{station.StationCode}}
        Warehouse: {{station.WarehouseCode ?? ""}}

        {{packageType}}

        Install steps:
        1. Extract this ZIP on the QC Station computer.
        2. Double-click Install-CropQcStation.cmd.
        3. Approve the Windows administrator prompt if shown.
        4. Confirm it says the installation completed successfully.
        5. Install FTADLL.exe from Admin Downloads if this computer is connected to an FTA.
        6. Launch Crop QC Station.

        App install path:
        {{InstalledAppDirectory}}

        Config install path:
        {{InstalledConfigPath}}

        Protocol link setup:
        - The installer registers cropqcstation:// links to:
          {{StandardWinFormsExePath}}
        - If this package is config-only, publish/deploy the WinForms app payload, download a new setup package, or copy the QC Station app to the app install path and rerun Install-CropQcStation.cmd.
        - Test by opening a dashboard sample page and clicking Open in QC Station.

        Keep this package private because it contains the station API key.
        Anyone with this package can act as this QC Station until the key is rotated or the station is deactivated.

        If this package is lost or exposed, rotate the station key in Admin -> QC Stations and download a new package.
        """;
    }

    private static void AddText(ZipArchive archive, string entryName, string content)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static void AddDirectory(ZipArchive archive, string sourceDirectory, string entryRoot)
    {
        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
            archive.CreateEntryFromFile(file, $"{entryRoot}/{relative}", CompressionLevel.Optimal);
        }
    }
}

public sealed class QcStationAdminService(CropQcDbContext dbContext, IConfiguration configuration) : IQcStationAdminService
{
    private string AppPayloadPath =>
        configuration["QcStation:WinFormsPayloadPath"]
        ?? Path.Combine(AppContext.BaseDirectory, "App_Data", "QcStationWinForms");

    public bool AppPayloadAvailable =>
        Directory.Exists(AppPayloadPath)
        && File.Exists(Path.Combine(AppPayloadPath, "CropQc.QcStation.WinForms.exe"));

    public async Task<QcStationsPageViewModel> GetStationsAsync(string? search, string? warehouseCode, string activeFilter, CancellationToken cancellationToken)
    {
        await EnsureQcStationColumnsAsync(cancellationToken);
        var query = dbContext.QcStations.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(x => x.StationCode.Contains(term) || x.StationName.Contains(term) || x.Name.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(warehouseCode))
        {
            query = query.Where(x => x.WarehouseCode == warehouseCode || (x.Warehouse != null && x.Warehouse.Code == warehouseCode));
        }

        query = activeFilter switch
        {
            "Inactive" => query.Where(x => !x.IsActive),
            "All" => query,
            _ => query.Where(x => x.IsActive)
        };

        var stations = await query
            .OrderBy(x => x.WarehouseCode)
            .ThenBy(x => x.StationCode)
            .Select(x => new QcStationListItemViewModel(
                x.Id,
                x.StationName == "" ? x.Name : x.StationName,
                x.StationCode,
                x.WarehouseCode ?? (x.Warehouse == null ? "" : x.Warehouse.Code),
                x.Description,
                x.IsActive,
                x.ApiKeyLastFour,
                x.ApiKeyCreatedAt,
                x.ApiKeyRotatedAt,
                x.LastSeenAt,
                x.LastSeenIp,
                x.LastSyncAt))
            .ToListAsync(cancellationToken);

        return new QcStationsPageViewModel
        {
            Stations = stations,
            Warehouses = await dbContext.Warehouses.AsNoTracking().OrderBy(x => x.Code).ToListAsync(cancellationToken),
            Search = search,
            WarehouseCode = warehouseCode,
            ActiveFilter = string.IsNullOrWhiteSpace(activeFilter) ? "Active" : activeFilter,
            AppPayloadAvailable = AppPayloadAvailable,
            AppPayloadPath = AppPayloadPath
        };
    }

    public async Task<(string? Error, QcStationConfigDownload? Download)> CreateAsync(QcStationForm form, string changedByEmail, CancellationToken cancellationToken)
    {
        await EnsureQcStationColumnsAsync(cancellationToken);
        var error = await ValidateFormAsync(form, isCreate: true, cancellationToken);
        if (error is not null)
        {
            return (error, null);
        }

        var rawKey = QcStationApiKeyValidator.GenerateApiKey();
        var now = DateTimeOffset.UtcNow;
        var station = new QcStation
        {
            StationName = form.StationName.Trim(),
            Name = form.StationName.Trim(),
            StationCode = form.StationCode.Trim(),
            WarehouseCode = form.WarehouseCode.Trim(),
            WarehouseId = await FindWarehouseIdAsync(form.WarehouseCode, cancellationToken),
            Description = form.Description?.Trim(),
            Notes = form.Notes?.Trim(),
            IsActive = form.IsActive,
            ApiKeyHash = QcStationApiKeyValidator.HashApiKey(rawKey),
            ApiKeyLastFour = rawKey[^4..],
            ApiKeyCreatedAt = now,
            CreatedAt = now,
            CreatedByUserId = await FindUserIdAsync(changedByEmail, cancellationToken)
        };

        dbContext.QcStations.Add(station);
        await dbContext.SaveChangesAsync(cancellationToken);
        await AddAuditAsync("create", station, changedByEmail, null, "QC Station created and API key generated.", cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (null, BuildConfigDownload(station, rawKey));
    }

    public async Task<string?> UpdateAsync(QcStationForm form, string changedByEmail, CancellationToken cancellationToken)
    {
        await EnsureQcStationColumnsAsync(cancellationToken);
        if (form.Id is null)
        {
            return "Station id is required.";
        }

        var error = await ValidateFormAsync(form, isCreate: false, cancellationToken);
        if (error is not null)
        {
            return error;
        }

        var station = await dbContext.QcStations.FindAsync([form.Id.Value], cancellationToken);
        if (station is null)
        {
            return "Station not found.";
        }

        var before = JsonSerializer.Serialize(ToAuditSnapshot(station));
        station.StationName = form.StationName.Trim();
        station.Name = station.StationName;
        station.StationCode = form.StationCode.Trim();
        station.WarehouseCode = form.WarehouseCode.Trim();
        station.WarehouseId = await FindWarehouseIdAsync(form.WarehouseCode, cancellationToken);
        station.Description = form.Description?.Trim();
        station.Notes = form.Notes?.Trim();
        station.IsActive = form.IsActive;
        station.UpdatedAt = DateTimeOffset.UtcNow;

        await AddAuditAsync("update", station, changedByEmail, before, JsonSerializer.Serialize(ToAuditSnapshot(station)), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> SetActiveAsync(int id, bool isActive, string changedByEmail, CancellationToken cancellationToken)
    {
        await EnsureQcStationColumnsAsync(cancellationToken);
        var station = await dbContext.QcStations.FindAsync([id], cancellationToken);
        if (station is null)
        {
            return "Station not found.";
        }

        var before = JsonSerializer.Serialize(ToAuditSnapshot(station));
        station.IsActive = isActive;
        station.UpdatedAt = DateTimeOffset.UtcNow;
        await AddAuditAsync(isActive ? "reactivate" : "deactivate", station, changedByEmail, before, JsonSerializer.Serialize(ToAuditSnapshot(station)), cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<(string? Error, QcStationConfigDownload? Download)> RotateKeyAsync(int id, string changedByEmail, CancellationToken cancellationToken)
    {
        await EnsureQcStationColumnsAsync(cancellationToken);
        var station = await dbContext.QcStations.FindAsync([id], cancellationToken);
        if (station is null)
        {
            return ("Station not found.", null);
        }

        var rawKey = QcStationApiKeyValidator.GenerateApiKey();
        var before = JsonSerializer.Serialize(ToAuditSnapshot(station));
        station.ApiKeyHash = QcStationApiKeyValidator.HashApiKey(rawKey);
        station.ApiKeyLastFour = rawKey[^4..];
        station.ApiKeyRotatedAt = DateTimeOffset.UtcNow;
        station.UpdatedAt = DateTimeOffset.UtcNow;
        await AddAuditAsync("rotate-key", station, changedByEmail, before, "QC Station API key rotated. Raw key not stored.", cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return (null, BuildConfigDownload(station, rawKey));
    }

    private async Task<string?> ValidateFormAsync(QcStationForm form, bool isCreate, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(form.StationName) || string.IsNullOrWhiteSpace(form.StationCode) || string.IsNullOrWhiteSpace(form.WarehouseCode))
        {
            return "Station name, station code, and warehouse are required.";
        }

        var id = form.Id ?? 0;
        if (await dbContext.QcStations.AnyAsync(x => x.StationCode == form.StationCode.Trim() && x.Id != id, cancellationToken))
        {
            return "Station code must be unique.";
        }

        if (!await dbContext.Warehouses.AnyAsync(x => x.Code == form.WarehouseCode.Trim(), cancellationToken))
        {
            return "Warehouse code is not valid.";
        }

        if (!isCreate && form.Id is null)
        {
            return "Station id is required.";
        }

        return null;
    }

    private QcStationConfigDownload BuildConfigDownload(QcStation station, string rawKey)
    {
        var config = new
        {
            StationName = station.StationName == "" ? station.Name : station.StationName,
            WarehouseCode = station.WarehouseCode ?? "",
            FtaMode = "RealDll",
            FtaDllPath = @"C:\Windows\SysWOW64",
            FtaDllFileName = "FTA_DLL.dll",
            FtaInitializationMode = "FTAInit",
            FtaConfigPath = @"C:\Program Files\FTADLL\FTA_DLL.CFG",
            FtaReadingTimeoutSeconds = 60,
            FtaWorkingDirectory = @"C:\Program Files (x86)\FTAWin",
            ComPort = (string?)null,
            ApiBaseUrl = configuration["QcStation:ApiBaseUrl"] ?? "https://crop-qc-dashboard.onrender.com",
            QcStationCode = station.StationCode,
            QcStationApiKey = rawKey,
            LocalDataPath = "local-data"
        };

        var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
        var packageFileName = $"CropQcStation-{SanitizeFileName(station.StationCode)}-Setup.zip";
        return new QcStationConfigDownload(
            $"{station.StationCode}-qcstation.settings.json",
            json,
            packageFileName,
            QcStationSetupPackageBuilder.Build(station, json, AppPayloadPath),
            AppPayloadAvailable);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "QC-Station" : safe;
    }

    private async Task<int?> FindWarehouseIdAsync(string warehouseCode, CancellationToken cancellationToken) =>
        await dbContext.Warehouses.Where(x => x.Code == warehouseCode.Trim()).Select(x => (int?)x.Id).SingleOrDefaultAsync(cancellationToken);

    private async Task<int?> FindUserIdAsync(string email, CancellationToken cancellationToken) =>
        await dbContext.Users.Where(x => x.Email == email).Select(x => (int?)x.Id).SingleOrDefaultAsync(cancellationToken);

    private async Task AddAuditAsync(string action, QcStation station, string by, string? before, string? after, CancellationToken cancellationToken)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = action,
            EntityName = nameof(QcStation),
            EntityKey = station.Id.ToString(),
            UserId = await FindUserIdAsync(by, cancellationToken),
            BeforeValuesJson = before,
            AfterValuesJson = after,
            SourceApplication = "CropQc.Web",
            CreatedAt = DateTimeOffset.UtcNow
        });
    }

    private static object ToAuditSnapshot(QcStation station) => new
    {
        station.Id,
        station.StationName,
        station.StationCode,
        station.WarehouseCode,
        station.Description,
        station.IsActive,
        station.ApiKeyLastFour,
        station.LastSeenAt,
        station.LastSyncAt
    };

    private async Task EnsureQcStationColumnsAsync(CancellationToken cancellationToken)
    {
        var provider = dbContext.Database.ProviderName ?? "";
        if (provider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""
                ALTER TABLE "QcStations" ADD COLUMN IF NOT EXISTS "StationName" character varying(150) NOT NULL DEFAULT '';
                ALTER TABLE "QcStations" ADD COLUMN IF NOT EXISTS "WarehouseCode" character varying(25) NULL;
                ALTER TABLE "QcStations" ADD COLUMN IF NOT EXISTS "Description" character varying(500) NULL;
                ALTER TABLE "QcStations" ADD COLUMN IF NOT EXISTS "ApiKeyHash" character varying(200) NULL;
                ALTER TABLE "QcStations" ADD COLUMN IF NOT EXISTS "ApiKeyLastFour" character varying(12) NULL;
                ALTER TABLE "QcStations" ADD COLUMN IF NOT EXISTS "ApiKeyCreatedAt" timestamp with time zone NULL;
                ALTER TABLE "QcStations" ADD COLUMN IF NOT EXISTS "ApiKeyRotatedAt" timestamp with time zone NULL;
                ALTER TABLE "QcStations" ADD COLUMN IF NOT EXISTS "LastSeenAt" timestamp with time zone NULL;
                ALTER TABLE "QcStations" ADD COLUMN IF NOT EXISTS "LastSeenIp" character varying(100) NULL;
                ALTER TABLE "QcStations" ADD COLUMN IF NOT EXISTS "LastSyncAt" timestamp with time zone NULL;
                ALTER TABLE "QcStations" ADD COLUMN IF NOT EXISTS "CreatedAt" timestamp with time zone NOT NULL DEFAULT '2026-01-01 00:00:00+00';
                ALTER TABLE "QcStations" ADD COLUMN IF NOT EXISTS "UpdatedAt" timestamp with time zone NULL;
                ALTER TABLE "QcStations" ADD COLUMN IF NOT EXISTS "CreatedByUserId" integer NULL;
                ALTER TABLE "QcStations" ADD COLUMN IF NOT EXISTS "Notes" character varying(1000) NULL;
                UPDATE "QcStations" SET "StationName" = "Name" WHERE "StationName" = '';
                """, cancellationToken);
        }
        else if (provider.Contains("SqlServer", StringComparison.OrdinalIgnoreCase))
        {
            await dbContext.Database.ExecuteSqlRawAsync("""
                IF COL_LENGTH('QcStations', 'StationName') IS NULL ALTER TABLE [QcStations] ADD [StationName] nvarchar(150) NOT NULL CONSTRAINT [DF_QcStations_StationName] DEFAULT N'';
                IF COL_LENGTH('QcStations', 'WarehouseCode') IS NULL ALTER TABLE [QcStations] ADD [WarehouseCode] nvarchar(25) NULL;
                IF COL_LENGTH('QcStations', 'Description') IS NULL ALTER TABLE [QcStations] ADD [Description] nvarchar(500) NULL;
                IF COL_LENGTH('QcStations', 'ApiKeyHash') IS NULL ALTER TABLE [QcStations] ADD [ApiKeyHash] nvarchar(200) NULL;
                IF COL_LENGTH('QcStations', 'ApiKeyLastFour') IS NULL ALTER TABLE [QcStations] ADD [ApiKeyLastFour] nvarchar(12) NULL;
                IF COL_LENGTH('QcStations', 'ApiKeyCreatedAt') IS NULL ALTER TABLE [QcStations] ADD [ApiKeyCreatedAt] datetimeoffset NULL;
                IF COL_LENGTH('QcStations', 'ApiKeyRotatedAt') IS NULL ALTER TABLE [QcStations] ADD [ApiKeyRotatedAt] datetimeoffset NULL;
                IF COL_LENGTH('QcStations', 'LastSeenAt') IS NULL ALTER TABLE [QcStations] ADD [LastSeenAt] datetimeoffset NULL;
                IF COL_LENGTH('QcStations', 'LastSeenIp') IS NULL ALTER TABLE [QcStations] ADD [LastSeenIp] nvarchar(100) NULL;
                IF COL_LENGTH('QcStations', 'LastSyncAt') IS NULL ALTER TABLE [QcStations] ADD [LastSyncAt] datetimeoffset NULL;
                IF COL_LENGTH('QcStations', 'CreatedAt') IS NULL ALTER TABLE [QcStations] ADD [CreatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_QcStations_CreatedAt] DEFAULT '2026-01-01T00:00:00+00:00';
                IF COL_LENGTH('QcStations', 'UpdatedAt') IS NULL ALTER TABLE [QcStations] ADD [UpdatedAt] datetimeoffset NULL;
                IF COL_LENGTH('QcStations', 'CreatedByUserId') IS NULL ALTER TABLE [QcStations] ADD [CreatedByUserId] int NULL;
                IF COL_LENGTH('QcStations', 'Notes') IS NULL ALTER TABLE [QcStations] ADD [Notes] nvarchar(1000) NULL;
                UPDATE [QcStations] SET [StationName] = [Name] WHERE [StationName] = N'';
                """, cancellationToken);
        }
    }
}
