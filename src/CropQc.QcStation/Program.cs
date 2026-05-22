using CropQc.QcStation.Fta;
using CropQc.Shared;

var settingsPath = args.FirstOrDefault() ?? Path.Combine(AppContext.BaseDirectory, "qcstation.settings.json");
var configuration = StationConfiguration.Load(settingsPath);
var stationService = FtaStationServiceFactory.Create(configuration);

Console.Title = $"{ProjectInfo.Name} QC Station";
Console.WriteLine($"{ProjectInfo.Name} QC Station FTA proof-of-concept");
Console.WriteLine($"Settings: {settingsPath}");

var exit = false;
while (!exit)
{
    RenderStatus(stationService);
    Console.Write("Select command: ");
    var command = Console.ReadLine()?.Trim();
    Console.WriteLine();

    switch (command)
    {
        case "1":
            await stationService.InitializeAsync();
            break;
        case "2":
            await stationService.CheckStatusAsync();
            break;
        case "3":
            await stationService.StartPressureReadingAsync();
            break;
        case "4":
            await stationService.GetLatestPressureReadingAsync();
            break;
        case "5":
            await stationService.CancelReadingAsync();
            break;
        case "6":
            Console.Write("Manual mock pressure lbs, blank for generated test value: ");
            var input = Console.ReadLine();
            stationService.UseMockReading(decimal.TryParse(input, out var manualValue) ? manualValue : null);
            break;
        case "7":
            await stationService.ReturnProbeHomeAsync();
            break;
        case "8":
            stationService.ClearLog();
            break;
        case "0":
            exit = true;
            break;
        default:
            Console.WriteLine("Unknown command.");
            break;
    }
}

static void RenderStatus(IFtaStationService stationService)
{
    Console.WriteLine();
    Console.WriteLine("Station");
    Console.WriteLine("-------");
    Console.WriteLine($"Name: {stationService.Configuration.StationName}");
    Console.WriteLine($"Warehouse: {stationService.Configuration.WarehouseCode}");
    Console.WriteLine($"FTA mode: {stationService.Configuration.FtaMode}");
    Console.WriteLine($"DLL path: {stationService.Configuration.FtaDllPath}");
    Console.WriteLine($"COM port: {stationService.Configuration.ComPort ?? "(not configured)"}");
    Console.WriteLine($"API base URL: {stationService.Configuration.ApiBaseUrl}");
    Console.WriteLine($"Local data path: {stationService.Configuration.LocalDataPath}");
    Console.WriteLine($"Last pressure reading: {FormatReading(stationService.LatestReading)}");
    Console.WriteLine();
    Console.WriteLine("Commands");
    Console.WriteLine("1. Initialize FTA");
    Console.WriteLine("2. Check Status");
    Console.WriteLine("3. Start Pressure Reading");
    Console.WriteLine("4. Get Latest Reading");
    Console.WriteLine("5. Cancel");
    Console.WriteLine("6. Use Mock Reading");
    Console.WriteLine("7. Return Probe Home");
    Console.WriteLine("8. Clear Log");
    Console.WriteLine("0. Exit");
    Console.WriteLine();
    Console.WriteLine("Log");
    Console.WriteLine("---");
    foreach (var entry in stationService.LogEntries.TakeLast(10))
    {
        Console.WriteLine(entry);
    }
    Console.WriteLine();
}

static string FormatReading(PressureReading? reading) =>
    reading is null
        ? "(none)"
        : $"{reading.ReadingValueLbs:0.00} lbs | {reading.Source} | {reading.Status} | {reading.CapturedAt:yyyy-MM-dd HH:mm:ss}";
