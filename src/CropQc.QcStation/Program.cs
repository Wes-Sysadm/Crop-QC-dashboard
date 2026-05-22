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
            await stationService.OpenSetupAsync();
            break;
        case "3":
            await stationService.DiagnosticStatusAsync();
            break;
        case "4":
            await stationService.CheckStatusAsync();
            break;
        case "5":
            await stationService.StartPressureReadingAsync();
            break;
        case "6":
            await stationService.GetLatestPressureReadingAsync();
            break;
        case "7":
            await stationService.CancelReadingAsync();
            break;
        case "8":
            await stationService.ReturnProbeHomeAsync();
            break;
        case "9":
            await stationService.QuitAsync();
            break;
        case "10":
            Console.Write("Manual mock pressure lbs, blank for generated test value: ");
            var input = Console.ReadLine();
            stationService.UseMockReading(decimal.TryParse(input, out var manualValue) ? manualValue : null);
            break;
        case "11":
            stationService.ClearLog();
            break;
        case "0":
            await stationService.QuitAsync();
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
    Console.WriteLine($"DLL file: {stationService.Configuration.FtaDllFileName}");
    Console.WriteLine($"COM port: {stationService.Configuration.ComPort ?? "(not configured)"}");
    Console.WriteLine($"API base URL: {stationService.Configuration.ApiBaseUrl}");
    Console.WriteLine($"Local data path: {stationService.Configuration.LocalDataPath}");
    Console.WriteLine($"Last pressure reading: {FormatReading(stationService.LatestReading)}");
    Console.WriteLine();
    Console.WriteLine("Commands");
    Console.WriteLine("1. Initialize FTA");
    Console.WriteLine("2. Open FTA Setup");
    Console.WriteLine("3. FTA Diagnostic Status");
    Console.WriteLine("4. Check Status");
    Console.WriteLine("5. Start Pressure Reading");
    Console.WriteLine("6. Get Latest Reading");
    Console.WriteLine("7. Cancel");
    Console.WriteLine("8. Return Probe Home");
    Console.WriteLine("9. Quit/Disconnect FTA");
    Console.WriteLine("10. Use Mock Reading");
    Console.WriteLine("11. Clear Log");
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
