using CropQc.QcStation.Fta;
namespace CropQc.QcStation.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var settingsPath = StationConfiguration.ResolveSettingsPath(args.FirstOrDefault());
        if (!File.Exists(settingsPath))
        {
            MessageBox.Show(
                StationConfiguration.MissingSettingsMessage(settingsPath),
                "QC Station settings missing",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        var configuration = StationConfiguration.Load(settingsPath);
        var stationService = FtaStationServiceFactory.Create(configuration, new WinFormsFtaMessagePump());

        Application.Run(new MainForm(stationService, settingsPath));
    }
}
