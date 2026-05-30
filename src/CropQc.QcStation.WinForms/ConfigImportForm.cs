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
        browseButton.Click += (_, _) => BrowseAndImport();
        picker.Controls.Add(browseButton);
        root.Controls.Add(picker, 0, 3);

        root.Controls.Add(statusLabel, 0, 4);

        var closeButton = new Button { Text = "Close", AutoSize = true, Anchor = AnchorStyles.Right };
        closeButton.Click += (_, _) => Close();
        root.Controls.Add(closeButton, 0, 5);
    }

    private void BrowseAndImport()
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
            var installedPath = StationConfigurationImport.Import(dialog.FileName, targetPath);
            var configuration = StationConfiguration.Load(installedPath);
            statusLabel.ForeColor = Color.DarkGreen;
            statusLabel.Text = $"Station configuration imported successfully. Station: {configuration.StationName} ({configuration.QcStationCode}), Warehouse: {configuration.WarehouseCode}. Restarting station screen...";
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            statusLabel.ForeColor = Color.DarkRed;
            statusLabel.Text = $"Configuration import failed: {ex.Message}. If Windows blocks the copy, run Crop QC Station as administrator and try again.";
        }
    }
}
