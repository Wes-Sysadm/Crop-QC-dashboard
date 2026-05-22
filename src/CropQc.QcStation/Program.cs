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
            await stationService.InitializeWithConfigPathAsync();
            break;
        case "3":
            await stationService.OpenSetupAsync();
            break;
        case "4":
            await stationService.DiagnosticStatusAsync();
            break;
        case "5":
            await stationService.CheckStatusAsync();
            break;
        case "6":
            await stationService.StartPressureReadingAsync();
            break;
        case "7":
            await stationService.StartAutoFirmnessReadingAsync();
            break;
        case "8":
            Console.WriteLine("Press the FTA front/init button or run the physical firmness test when prompted by the FTA.");
            await stationService.StartAndWaitManualFirmnessReadingAsync();
            break;
        case "9":
            await stationService.DemoStylePollReadingAsync();
            break;
        case "10":
            await stationService.DemoStyleAutoReadingAsync();
            break;
        case "11":
            Console.WriteLine("Press the FTA front/init button when prompted by the FTA.");
            await stationService.DemoStyleManualButtonReadingAsync();
            break;
        case "12":
            await stationService.GetLatestPressureReadingAsync();
            break;
        case "13":
            await stationService.CancelReadingAsync();
            break;
        case "14":
            await stationService.ReturnProbeHomeAsync();
            break;
        case "15":
            await stationService.QuitAsync();
            break;
        case "16":
            Console.Write("Manual mock pressure lbs, blank for generated test value: ");
            var input = Console.ReadLine();
            stationService.UseMockReading(decimal.TryParse(input, out var manualValue) ? manualValue : null);
            break;
        case "17":
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
    Console.WriteLine($"Initialization mode: {stationService.Configuration.FtaInitializationMode}");
    Console.WriteLine($"FTA config path: {stationService.Configuration.FtaConfigPath}");
    Console.WriteLine($"Reading timeout seconds: {stationService.Configuration.FtaReadingTimeoutSeconds}");
    Console.WriteLine($"COM port: {stationService.Configuration.ComPort ?? "(not configured)"}");
    Console.WriteLine($"API base URL: {stationService.Configuration.ApiBaseUrl}");
    Console.WriteLine($"Local data path: {stationService.Configuration.LocalDataPath}");
    Console.WriteLine($"Last pressure reading: {FormatReading(stationService.LatestReading)}");
    Console.WriteLine();
    Console.WriteLine("Commands");
    Console.WriteLine("1. Initialize FTA");
    Console.WriteLine("2. Initialize FTA With Config Path");
    Console.WriteLine("3. Open FTA Setup");
    Console.WriteLine("4. FTA Diagnostic Status");
    Console.WriteLine("5. Check Status");
    Console.WriteLine("6. Start Manual/Button Firmness Reading");
    Console.WriteLine("7. Start Auto Firmness Reading");
    Console.WriteLine("8. Start And Wait Manual/Button Reading");
    Console.WriteLine("9. Demo-Style Poll Reading");
    Console.WriteLine("10. Demo-Style Auto Reading");
    Console.WriteLine("11. Demo-Style Manual/Button Reading");
    Console.WriteLine("12. Get Latest Reading");
    Console.WriteLine("13. Cancel");
    Console.WriteLine("14. Return Probe Home");
    Console.WriteLine("15. Quit/Disconnect FTA");
    Console.WriteLine("16. Use Mock Reading");
    Console.WriteLine("17. Clear Log");
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
