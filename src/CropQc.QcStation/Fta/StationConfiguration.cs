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
        if (!string.IsNullOrWhiteSpace(commandLinePath))
        {
            return commandLinePath;
        }

        var installedPath = installedSettingsPath ?? InstalledSettingsPath;
        if (File.Exists(installedPath))
        {
            return installedPath;
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
        $"QC Station settings were not found at '{path}'. Install the station setup package from Admin -> QC Stations, or pass a settings path on the command line. Installed stations should use {InstalledSettingsPath}.";
}
