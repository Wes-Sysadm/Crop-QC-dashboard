using System.Runtime.InteropServices;
using CropQc.QcStation.Fta;
using CropQc.Shared;

namespace CropQc.QcStation.WinForms;

public sealed class MainForm : Form
{
    private readonly IFtaStationService stationService;
    private readonly string settingsPath;
    private readonly Dictionary<string, Label> valueLabels = [];
    private readonly TextBox logTextBox = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        WordWrap = false
    };
    private int renderedLogCount;

    public MainForm(IFtaStationService stationService, string settingsPath)
    {
        this.stationService = stationService;
        this.settingsPath = settingsPath;

        Text = $"{ProjectInfo.Name} QC Station WinForms FTA Harness";
        Width = 1200;
        Height = 820;
        MinimumSize = new Size(1000, 720);

        BuildLayout();
        RefreshStatusDisplay();
        AppendLog($"Settings: {settingsPath}");
        AppendLog("WinForms harness started on STA thread with a Windows message loop.");
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        root.Controls.Add(BuildStatusPanel(), 0, 0);
        root.Controls.Add(BuildButtonPanel(), 0, 1);
        root.Controls.Add(BuildLogPanel(), 0, 2);
    }

    private Control BuildStatusPanel()
    {
        var group = new GroupBox
        {
            Dock = DockStyle.Top,
            Text = "Status / Config",
            AutoSize = true
        };

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 4,
            Padding = new Padding(10)
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        group.Controls.Add(grid);

        AddStatusRow(grid, "Station name", "StationName", "Warehouse", "WarehouseCode");
        AddStatusRow(grid, "FTA mode", "FtaMode", "DLL path", "FtaDllPath");
        AddStatusRow(grid, "DLL file", "FtaDllFileName", "Initialization mode", "FtaInitializationMode");
        AddStatusRow(grid, "FTA config path", "FtaConfigPath", "Working directory", "FtaWorkingDirectory");
        AddStatusRow(grid, "Current working directory", "CurrentWorkingDirectory", "Process architecture", "ProcessArchitecture");
        AddStatusRow(grid, "OS architecture", "OSArchitecture", "Last pressure reading", "LastPressureReading");

        return group;
    }

    private void AddStatusRow(TableLayoutPanel grid, string label1, string key1, string label2, string key2)
    {
        var row = grid.RowCount++;
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        AddStatusCell(grid, label1, key1, row, 0);
        AddStatusCell(grid, label2, key2, row, 2);
    }

    private void AddStatusCell(TableLayoutPanel grid, string label, string key, int row, int column)
    {
        grid.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 4, 8, 4)
        }, column, row);

        var value = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(360, 0),
            Margin = new Padding(0, 4, 14, 4)
        };
        valueLabels[key] = value;
        grid.Controls.Add(value, column + 1, row);
    }

    private Control BuildButtonPanel()
    {
        var group = new GroupBox
        {
            Dock = DockStyle.Top,
            Text = "FTA Commands",
            AutoSize = true
        };

        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(10),
            WrapContents = true
        };
        group.Controls.Add(flow);

        AddCommand(flow, "Initialize FTA", () => stationService.InitializeAsync());
        AddCommand(flow, "Initialize FTA With Config Path", () => stationService.InitializeWithConfigPathAsync());
        AddCommand(flow, "Open FTA Setup", () => stationService.OpenSetupAsync());
        AddCommand(flow, "FTA Diagnostic Status", () => stationService.DiagnosticStatusAsync());
        AddCommand(flow, "Start Manual/Button Firmness Reading", () => stationService.StartPressureReadingAsync());
        AddReadingCommand(flow, "Start Auto Firmness Reading", () => stationService.StartAutoFirmnessReadingAsync());
        AddReadingCommand(flow, "Start And Wait Manual/Button Reading", () => stationService.StartAndWaitManualFirmnessReadingAsync());
        AddReadingCommand(flow, "Demo-Style Manual/Button Reading", () => stationService.DemoStyleManualButtonReadingAsync());
        AddReadingCommand(flow, "Demo-Style Auto Reading", () => stationService.DemoStyleAutoReadingAsync());
        AddReadingCommand(flow, "Get Latest Reading", () => stationService.GetLatestPressureReadingAsync());
        AddCommand(flow, "Cancel", () => stationService.CancelReadingAsync());
        AddCommand(flow, "Return Probe Home", () => stationService.ReturnProbeHomeAsync());
        AddCommand(flow, "Quit/Disconnect FTA", () => stationService.QuitAsync());

        var clearButton = CreateButton("Clear Log");
        clearButton.Click += (_, _) =>
        {
            stationService.ClearLog();
            renderedLogCount = 0;
            logTextBox.Clear();
            AppendLog("Log cleared.");
        };
        flow.Controls.Add(clearButton);

        return group;
    }

    private Control BuildLogPanel()
    {
        var group = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "Log"
        };
        group.Controls.Add(logTextBox);
        return group;
    }

    private void AddCommand(FlowLayoutPanel flow, string text, Func<Task<FtaDeviceStatus>> command)
    {
        var button = CreateButton(text);
        button.Click += async (_, _) => await RunCommandAsync(text, async () =>
        {
            var status = await command();
            AppendLog($"{text}: {status.StatusMessage}{(string.IsNullOrWhiteSpace(status.ErrorMessage) ? "" : $" Error: {status.ErrorMessage}")}");
        });
        flow.Controls.Add(button);
    }

    private void AddReadingCommand(FlowLayoutPanel flow, string text, Func<Task<PressureReading?>> command)
    {
        var button = CreateButton(text);
        button.Click += async (_, _) => await RunCommandAsync(text, async () =>
        {
            var reading = await command();
            AppendLog(reading is null
                ? $"{text}: no reading returned."
                : $"{text}: {reading.ReadingValueLbs:0.00} lbs ({reading.Source}) at {reading.CapturedAt:yyyy-MM-dd HH:mm:ss}.");
        });
        flow.Controls.Add(button);
    }

    private static Button CreateButton(string text) =>
        new()
        {
            Text = text,
            AutoSize = true,
            Margin = new Padding(4),
            Padding = new Padding(8, 5, 8, 5)
        };

    private async Task RunCommandAsync(string commandName, Func<Task> command)
    {
        UseWaitCursor = true;
        SetButtonsEnabled(false);
        AppendLog($"{commandName} started.");
        try
        {
            await command();
        }
        catch (Exception ex)
        {
            AppendLog($"{commandName} failed: {ex.Message}");
        }
        finally
        {
            RenderNewServiceLogEntries();
            RefreshStatusDisplay();
            SetButtonsEnabled(true);
            UseWaitCursor = false;
        }
    }

    private void SetButtonsEnabled(bool enabled)
    {
        foreach (var button in Controls.OfType<Control>().SelectMany(Descendants).OfType<Button>())
        {
            button.Enabled = enabled;
        }
    }

    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (Control child in control.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private void RenderNewServiceLogEntries()
    {
        foreach (var entry in stationService.LogEntries.Skip(renderedLogCount))
        {
            AppendLog(entry);
        }
        renderedLogCount = stationService.LogEntries.Count;
    }

    private void RefreshStatusDisplay()
    {
        var config = stationService.Configuration;
        SetValue("StationName", config.StationName);
        SetValue("WarehouseCode", config.WarehouseCode);
        SetValue("FtaMode", config.FtaMode.ToString());
        SetValue("FtaDllPath", config.FtaDllPath);
        SetValue("FtaDllFileName", config.FtaDllFileName);
        SetValue("FtaInitializationMode", config.FtaInitializationMode.ToString());
        SetValue("FtaConfigPath", config.FtaConfigPath);
        SetValue("FtaWorkingDirectory", string.IsNullOrWhiteSpace(config.FtaWorkingDirectory) ? "(not configured)" : config.FtaWorkingDirectory);
        SetValue("CurrentWorkingDirectory", Environment.CurrentDirectory);
        SetValue("ProcessArchitecture", RuntimeInformation.ProcessArchitecture.ToString());
        SetValue("OSArchitecture", RuntimeInformation.OSArchitecture.ToString());
        SetValue("LastPressureReading", FormatReading(stationService.LatestReading));
    }

    private void SetValue(string key, string value)
    {
        if (valueLabels.TryGetValue(key, out var label))
        {
            label.Text = value;
        }
    }

    private void AppendLog(string message)
    {
        logTextBox.AppendText($"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
        logTextBox.SelectionStart = logTextBox.TextLength;
        logTextBox.ScrollToCaret();
    }

    private static string FormatReading(PressureReading? reading) =>
        reading is null
            ? "(none)"
            : $"{reading.ReadingValueLbs:0.00} lbs | {reading.Source} | {reading.Status} | {reading.CapturedAt:yyyy-MM-dd HH:mm:ss}";
}
