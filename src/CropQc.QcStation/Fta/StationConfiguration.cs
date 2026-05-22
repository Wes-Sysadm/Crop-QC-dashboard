using System.Text.Json;
using System.Text.Json.Serialization;

namespace CropQc.QcStation.Fta;

public sealed class StationConfiguration
{
    public string StationName { get; set; } = Environment.MachineName;
    public string WarehouseCode { get; set; } = "WP";
    public FtaMode FtaMode { get; set; } = FtaMode.Mock;
    public string FtaDllPath { get; set; } = ".\\fta";
    public string FtaDllFileName { get; set; } = "FTA_dll.dll";
    public string? ComPort { get; set; }
    public string ApiBaseUrl { get; set; } = "https://localhost:7001";
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
}
