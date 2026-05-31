using System.Text.Json;
using System.Text.Json.Serialization;

namespace CropQc.QcStation.Fta;

public sealed class StationConfiguration
{
    public const string InstalledSettingsPath = @"C:\ProgramData\CropQc\QcStation\qcstation.settings.json";

    public string StationName { get; set; } = Environment.MachineName;
    public string WarehouseCode { get; set; } = "WP";
    public FtaMode FtaMode { get; set; } = FtaMode.Mock;
    public string FtaDllPath { get; set; } = ".\\fta";
    public string FtaDllFileName { get; set; } = "FTA_dll.dll";
    public FtaInitializationMode FtaInitializationMode { get; set; } = FtaInitializationMode.FTAInit;
    public string FtaConfigPath { get; set; } = @"C:\Program Files\FTADLL\FTA_DLL.CFG";
    public int FtaReadingTimeoutSeconds { get; set; } = 60;
    public bool FtaManualCaptureSafeMode { get; set; } = true;
    public int FtaManualRearmDelayMs { get; set; } = 2000;
    public string? FtaWorkingDirectory { get; set; }
    public string? ComPort { get; set; }
    public string ApiBaseUrl { get; set; } = "https://localhost:7001";
    public string? QcStationCode { get; set; }
    public string? QcStationApiKey { get; set; }
    public string LocalDataPath { get; set; } = ".\\data";

    public static StationConfiguration Load(string path)
    {
        if (!File.Exists(path))
        {
            return new StationConfiguration();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<StationConfiguration>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        }) ?? new StationConfiguration();
    }

    public static string ResolveSettingsPath(string? commandLinePath, string? baseDirectory = null, string? installedSettingsPath = null)
    {
        var installedPath = installedSettingsPath ?? InstalledSettingsPath;
        if (File.Exists(installedPath))
        {
            return installedPath;
        }

        if (!string.IsNullOrWhiteSpace(commandLinePath))
        {
            return commandLinePath;
        }

        var directory = new DirectoryInfo(baseDirectory ?? AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "CropQc.QcStation", "qcstation.settings.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return Path.Combine(baseDirectory ?? AppContext.BaseDirectory, "qcstation.settings.json");
    }

    public static string MissingSettingsMessage(string path) =>
        $"QC Station settings were not found at '{path}'. Install the Crop QC Station app, then download station config from Admin -> QC Stations and import qcstation.settings.json. Installed stations should use {InstalledSettingsPath}.";

    public void CopyFrom(StationConfiguration source)
    {
        StationName = source.StationName;
        WarehouseCode = source.WarehouseCode;
        FtaMode = source.FtaMode;
        FtaDllPath = source.FtaDllPath;
        FtaDllFileName = source.FtaDllFileName;
        FtaInitializationMode = source.FtaInitializationMode;
        FtaConfigPath = source.FtaConfigPath;
        FtaReadingTimeoutSeconds = source.FtaReadingTimeoutSeconds;
        FtaManualCaptureSafeMode = source.FtaManualCaptureSafeMode;
        FtaManualRearmDelayMs = source.FtaManualRearmDelayMs;
        FtaWorkingDirectory = source.FtaWorkingDirectory;
        ComPort = source.ComPort;
        ApiBaseUrl = source.ApiBaseUrl;
        QcStationCode = source.QcStationCode;
        QcStationApiKey = source.QcStationApiKey;
        LocalDataPath = source.LocalDataPath;
    }
}

public static class StationConfigurationImport
{
    public static StationConfiguration ValidateSource(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            throw new FileNotFoundException("qcstation.settings.json was not found.", sourcePath);
        }

        var configuration = StationConfiguration.Load(sourcePath);
        if (string.IsNullOrWhiteSpace(configuration.QcStationCode)
            || string.IsNullOrWhiteSpace(configuration.QcStationApiKey)
            || string.IsNullOrWhiteSpace(configuration.ApiBaseUrl)
            || string.IsNullOrWhiteSpace(configuration.StationName)
            || string.IsNullOrWhiteSpace(configuration.WarehouseCode))
        {
            throw new InvalidDataException("This does not appear to be a valid Crop QC Station config.");
        }

        return configuration;
    }

    public static string Import(string sourcePath, string? targetPath = null)
    {
        ValidateSource(sourcePath);
        var destination = targetPath ?? StationConfiguration.InstalledSettingsPath;
        var directory = Path.GetDirectoryName(destination);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        if (File.Exists(destination))
        {
            var backupPath = Path.Combine(
                directory ?? AppContext.BaseDirectory,
                $"qcstation.settings.backup-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(destination, backupPath, overwrite: true);
        }

        File.Copy(sourcePath, destination, overwrite: true);
        return destination;
    }
}
