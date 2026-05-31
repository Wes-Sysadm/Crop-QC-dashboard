using CropQc.QcStation.Api;
using CropQc.QcStation.Fta;

namespace CropQc.QcStation.WinForms;

public sealed class ConfigImportForm : Form
{
    private readonly string targetPath;
    private readonly TextBox selectedFileTextBox = new() { Width = 520, ReadOnly = true };
    private readonly Label statusLabel = new()
    {
        AutoSize = true,
        MaximumSize = new Size(620, 0)
    };

    public ConfigImportForm(string targetPath, QcStationProtocolLaunch? launchRequest)
    {
        this.targetPath = targetPath;
        Text = "Crop QC Station Setup";
        Width = 720;
        Height = 360;
        MinimumSize = new Size(620, 320);
        StartPosition = FormStartPosition.CenterScreen;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18),
            ColumnCount = 1,
            RowCount = 6
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Text = "Station configuration is missing.",
            AutoSize = true,
            Font = new Font(SystemFonts.DefaultFont.FontFamily, 14, FontStyle.Bold)
        }, 0, 0);

        var launchText = launchRequest?.SampleId is long sampleId
            ? $" The app was opened for sample {sampleId}; import station config before that sample can be loaded."
            : "";
        root.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(640, 0),
            Text = "Download qcstation.settings.json from Admin -> QC Stations, then import it here. The config contains this computer's station code and API key." + launchText
        }, 0, 1);

        root.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(640, 0),
            Text = $"Installed config path: {targetPath}"
        }, 0, 2);

        var picker = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Top };
        picker.Controls.Add(selectedFileTextBox);
        var browseButton = new Button { Text = "Browse for qcstation.settings.json", AutoSize = true };
        browseButton.Click += async (_, _) => await BrowseAndImportAsync();
        picker.Controls.Add(browseButton);
        root.Controls.Add(picker, 0, 3);

        root.Controls.Add(statusLabel, 0, 4);

        var closeButton = new Button { Text = "Close", AutoSize = true, Anchor = AnchorStyles.Right };
        closeButton.Click += (_, _) => Close();
        root.Controls.Add(closeButton, 0, 5);
    }

    private async Task BrowseAndImportAsync()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Select qcstation.settings.json",
            Filter = "QC Station settings (qcstation.settings.json)|qcstation.settings.json|JSON files (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        selectedFileTextBox.Text = dialog.FileName;
        try
        {
            var sourceConfiguration = StationConfigurationImport.ValidateSource(dialog.FileName);
            var installedPath = StationConfigurationImport.Import(dialog.FileName, targetPath);
            var configuration = StationConfiguration.Load(installedPath);
            var connectionStatus = await TestConnectionAsync(configuration);
            statusLabel.ForeColor = Color.DarkGreen;
            statusLabel.Text = $"Config installed successfully. StationName: {configuration.StationName}; QcStationCode: {configuration.QcStationCode}; WarehouseCode: {configuration.WarehouseCode}; ApiBaseUrl: {configuration.ApiBaseUrl}. {connectionStatus}";
            MessageBox.Show(
                $"Config installed successfully.{Environment.NewLine}{Environment.NewLine}StationName: {sourceConfiguration.StationName}{Environment.NewLine}QcStationCode: {sourceConfiguration.QcStationCode}{Environment.NewLine}WarehouseCode: {sourceConfiguration.WarehouseCode}{Environment.NewLine}ApiBaseUrl: {sourceConfiguration.ApiBaseUrl}{Environment.NewLine}{Environment.NewLine}{connectionStatus}",
                "Station config installed",
                MessageBoxButtons.OK,
                connectionStatus.StartsWith("Connected", StringComparison.OrdinalIgnoreCase) ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            statusLabel.ForeColor = Color.DarkRed;
            statusLabel.Text = $"Configuration import failed: {ex.Message}. If Windows blocks the copy, run Crop QC Station as administrator and try again.";
        }
    }

    private static async Task<string> TestConnectionAsync(StationConfiguration configuration)
    {
        try
        {
            var client = QcStationApiClient.Create(
                configuration.ApiBaseUrl,
                configuration.QcStationCode,
                configuration.QcStationApiKey,
                configuration.StationName);
            await client.GetTodaySamplesAsync(configuration.WarehouseCode);
            return "Connected successfully.";
        }
        catch (QcStationAuthorizationException ex)
        {
            return ex.StatusCode == System.Net.HttpStatusCode.Forbidden
                ? "Station inactive or not authorized. Confirm this station is active in Admin -> QC Stations."
                : "Station not authorized. The key may be invalid or rotated; download/import the latest station config.";
        }
        catch (HttpRequestException ex)
        {
            return $"Server unavailable or connection failed: {ex.Message}";
        }
        catch (TaskCanceledException)
        {
            return "Server unavailable or connection timed out.";
        }
    }
}
