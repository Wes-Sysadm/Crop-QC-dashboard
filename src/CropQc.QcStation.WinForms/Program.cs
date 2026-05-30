using CropQc.QcStation.Fta;
namespace CropQc.QcStation.WinForms;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        var launch = args.Select(QcStationProtocolLaunch.Parse).FirstOrDefault(x => x is not null);
        var settingsArg = args.FirstOrDefault(x => !QcStationProtocolLaunch.IsProtocolArgument(x));
        var settingsPath = StationConfiguration.ResolveSettingsPath(settingsArg);
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

        Application.Run(new MainForm(stationService, settingsPath, launch));
    }
}
