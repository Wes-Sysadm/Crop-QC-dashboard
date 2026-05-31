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
        if (!File.Exists(settingsPath) || RequiresImport(settingsPath, settingsArg))
        {
            using var importForm = new ConfigImportForm(StationConfiguration.InstalledSettingsPath, launch);
            if (importForm.ShowDialog() != DialogResult.OK || !File.Exists(StationConfiguration.InstalledSettingsPath))
            {
                MessageBox.Show(
                    StationConfiguration.MissingSettingsMessage(settingsPath),
                    "QC Station settings missing",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            settingsPath = StationConfiguration.InstalledSettingsPath;
        }

        var configuration = StationConfiguration.Load(settingsPath);
        var stationService = FtaStationServiceFactory.Create(configuration, new WinFormsFtaMessagePump());

        Application.Run(new MainForm(stationService, settingsPath, launch));
    }

    private static bool RequiresImport(string settingsPath, string? settingsArg)
    {
        var shouldValidate = string.Equals(settingsPath, StationConfiguration.InstalledSettingsPath, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(settingsArg);
        if (!shouldValidate)
        {
            return false;
        }

        try
        {
            StationConfigurationImport.ValidateSource(settingsPath);
            return false;
        }
        catch
        {
            return true;
        }
    }
}
