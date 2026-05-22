using CropQc.QcStation.Fta;
namespace CropQc.QcStation.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var settingsPath = args.FirstOrDefault() ?? ResolveDefaultSettingsPath();
        var configuration = StationConfiguration.Load(settingsPath);
        var stationService = FtaStationServiceFactory.Create(configuration, new WinFormsFtaMessagePump());

        Application.Run(new MainForm(stationService, settingsPath));
    }

    private static string ResolveDefaultSettingsPath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "CropQc.QcStation", "qcstation.settings.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return Path.Combine(AppContext.BaseDirectory, "qcstation.settings.json");
    }
}
