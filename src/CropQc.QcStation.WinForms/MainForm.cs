using System.Runtime.InteropServices;
using CropQc.QcStation.Fta;
using CropQc.Shared;

namespace CropQc.QcStation.WinForms;

public sealed class MainForm : Form
{
    private readonly IFtaStationService stationService;
    private readonly string settingsPath;
    private readonly TestFruitPressureCapture testFruitCapture = new();
    private readonly Dictionary<string, Label> valueLabels = [];
    private readonly NumericUpDown fruitNumberInput = new()
    {
        Minimum = 1,
        Maximum = 25,
        Value = 1,
        Width = 70
    };
    private readonly TextBox pressure1TextBox = CreateReadOnlyTextBox();
    private readonly TextBox pressure2TextBox = CreateReadOnlyTextBox();
    private readonly TextBox averagePressureTextBox = CreateReadOnlyTextBox();
    private readonly TextBox lastCapturedTextBox = CreateReadOnlyTextBox();
    private readonly RadioButton pressure1TargetRadio = new() { Text = "Pressure 1", AutoSize = true };
    private readonly RadioButton pressure2TargetRadio = new() { Text = "Pressure 2", AutoSize = true };
    private readonly RadioButton autoAdvanceTargetRadio = new() { Text = "Auto-advance", AutoSize = true, Checked = true };
    private readonly ListView readingHistoryList = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
        HeaderStyle = ColumnHeaderStyle.Nonclickable
    };
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
        Width = 1260;
        Height = 920;
        MinimumSize = new Size(1040, 760);

        BuildLayout();
        RefreshStatusDisplay();
        RefreshCaptureDisplay();
        AppendLog($"Settings: {settingsPath}");
        AppendLog("WinForms harness started on STA thread with a Windows message loop.");
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        Controls.Add(root);

        root.Controls.Add(BuildStatusPanel(), 0, 0);
        root.Controls.Add(BuildGuidancePanel(), 0, 1);
        root.Controls.Add(BuildButtonPanel(), 0, 2);
        root.Controls.Add(BuildPressureCapturePanel(), 0, 3);
        root.Controls.Add(BuildLogPanel(), 0, 4);
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

    private static Control BuildGuidancePanel() =>
        new Label
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(4, 8, 4, 4),
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            Text = "Recommended workflow: Click Start Manual Reading, then press and hold the green FTA button until the probe completes the test. Auto firmness reading is experimental and is not supported on the current unit."
        };

    private Control BuildButtonPanel()
    {
        var group = new GroupBox
        {
            Dock = DockStyle.Top,
            Text = "FTA Commands - Manual/Button Reading Recommended",
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
        AddCommand(flow, "Start Manual/Button Firmness Reading - Recommended", () => stationService.StartPressureReadingAsync());
        AddReadingCommand(flow, "Start Auto Firmness Reading - Experimental", () => stationService.StartAutoFirmnessReadingAsync());
        AddReadingCommand(flow, "Start And Wait Manual/Button Reading - Recommended", () => stationService.StartAndWaitManualFirmnessReadingAsync());
        AddReadingCommand(flow, "Demo-Style Manual/Button Reading", () => stationService.DemoStyleManualButtonReadingAsync());
        AddReadingCommand(flow, "Demo-Style Auto Reading - Experimental", () => stationService.DemoStyleAutoReadingAsync());
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

    private Control BuildPressureCapturePanel()
    {
        var group = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "Local Two-Pressure Capture Test - Not Saved"
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(10)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 430));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        group.Controls.Add(root);

        root.Controls.Add(BuildCaptureFieldsPanel(), 0, 0);
        root.Controls.Add(BuildReadingHistoryPanel(), 1, 0);

        return group;
    }

    private Control BuildCaptureFieldsPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 8,
            AutoSize = true
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        AddCaptureRow(panel, "Fruit number", fruitNumberInput, 0);
        AddCaptureRow(panel, "Pressure 1", pressure1TextBox, 1);
        AddCaptureRow(panel, "Pressure 2", pressure2TextBox, 2);
        AddCaptureRow(panel, "Average pressure", averagePressureTextBox, 3);
        AddCaptureRow(panel, "Last captured", lastCapturedTextBox, 4);

        var targetPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true
        };
        targetPanel.Controls.Add(pressure1TargetRadio);
        targetPanel.Controls.Add(pressure2TargetRadio);
        targetPanel.Controls.Add(autoAdvanceTargetRadio);
        AddCaptureRow(panel, "Capture target", targetPanel, 5);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true
        };
        AddCaptureButton(buttons, "Capture Pressure 1", () => CaptureLatestReadingAsync(PressureCaptureTarget.Pressure1));
        AddCaptureButton(buttons, "Capture Pressure 2", () => CaptureLatestReadingAsync(PressureCaptureTarget.Pressure2));
        AddCaptureButton(buttons, "Start Manual Reading and Capture", StartManualReadingAndCaptureAsync);

        var clearButton = CreateButton("Clear Test Fruit");
        clearButton.Click += (_, _) =>
        {
            testFruitCapture.Clear();
            RefreshCaptureDisplay();
            AppendLog("Local test fruit cleared.");
        };
        buttons.Controls.Add(clearButton);
        AddCaptureRow(panel, "", buttons, 6);

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(390, 0),
            Text = "This panel is local-only. It prepares the operator flow for mapping FTA readings into QC sample rows later, but it does not save to Azure SQL or the web workflow."
        };
        AddCaptureRow(panel, "", note, 7);

        fruitNumberInput.ValueChanged += (_, _) => testFruitCapture.FruitNumber = (int)fruitNumberInput.Value;

        return panel;
    }

    private static void AddCaptureRow(TableLayoutPanel panel, string labelText, Control control, int row)
    {
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(new Label
        {
            Text = labelText,
            AutoSize = true,
            Font = labelText.Length == 0 ? SystemFonts.DefaultFont : new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            Margin = new Padding(0, 5, 10, 5)
        }, 0, row);
        control.Margin = new Padding(0, 3, 0, 3);
        panel.Controls.Add(control, 1, row);
    }

    private Control BuildReadingHistoryPanel()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1
        };
        panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        panel.Controls.Add(new Label
        {
            Text = "Reading History",
            AutoSize = true,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 6)
        }, 0, 0);

        readingHistoryList.Columns.Add("Captured At", 170);
        readingHistoryList.Columns.Add("Pressure", 90);
        readingHistoryList.Columns.Add("Source", 80);
        readingHistoryList.Columns.Add("Fruit", 60);
        readingHistoryList.Columns.Add("Target Slot", 110);
        panel.Controls.Add(readingHistoryList, 0, 1);

        return panel;
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

    private void AddCaptureButton(FlowLayoutPanel flow, string text, Func<Task> command)
    {
        var button = CreateButton(text);
        button.Click += async (_, _) => await RunCommandAsync(text, command);
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

    private static TextBox CreateReadOnlyTextBox() =>
        new()
        {
            ReadOnly = true,
            Width = 220
        };

    private async Task StartManualReadingAndCaptureAsync()
    {
        AppendLog("Start Manual Reading and Capture: press and hold the green FTA button until the probe completes the test.");
        var reading = await stationService.StartAndWaitManualFirmnessReadingAsync();
        if (reading is null)
        {
            AppendLog("Start Manual Reading and Capture: no reading returned.");
            return;
        }

        CaptureReading(reading, GetSelectedCaptureTarget());
    }

    private Task CaptureLatestReadingAsync(PressureCaptureTarget target)
    {
        var reading = stationService.LatestReading;
        if (reading is null)
        {
            AppendLog("Capture requested, but there is no latest pressure reading yet.");
            return Task.CompletedTask;
        }

        CaptureReading(reading, target);
        return Task.CompletedTask;
    }

    private void CaptureReading(PressureReading reading, PressureCaptureTarget target)
    {
        testFruitCapture.FruitNumber = (int)fruitNumberInput.Value;
        var slot = testFruitCapture.Capture(reading, target);
        AppendLog($"Captured {reading.ReadingValueLbs:0.00} lbs into Fruit {testFruitCapture.FruitNumber} {slot}.");
        RefreshCaptureDisplay();
    }

    private PressureCaptureTarget GetSelectedCaptureTarget()
    {
        if (pressure1TargetRadio.Checked)
        {
            return PressureCaptureTarget.Pressure1;
        }

        if (pressure2TargetRadio.Checked)
        {
            return PressureCaptureTarget.Pressure2;
        }

        return PressureCaptureTarget.AutoAdvance;
    }

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
            RefreshCaptureDisplay();
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

    private void RefreshCaptureDisplay()
    {
        pressure1TextBox.Text = FormatPressure(testFruitCapture.Pressure1Lbs);
        pressure2TextBox.Text = FormatPressure(testFruitCapture.Pressure2Lbs);
        averagePressureTextBox.Text = FormatPressure(testFruitCapture.AveragePressureLbs);
        lastCapturedTextBox.Text = testFruitCapture.LastCapturedReading is null
            ? "(none)"
            : $"{testFruitCapture.LastCapturedReading.ReadingValueLbs:0.00} lbs ({testFruitCapture.LastCapturedReading.Source})";

        readingHistoryList.BeginUpdate();
        readingHistoryList.Items.Clear();
        foreach (var entry in testFruitCapture.History)
        {
            var item = new ListViewItem(entry.CapturedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));
            item.SubItems.Add($"{entry.PressureValueLbs:0.00} lbs");
            item.SubItems.Add(entry.Source.ToString());
            item.SubItems.Add(entry.FruitNumber.ToString());
            item.SubItems.Add(entry.TargetSlot);
            readingHistoryList.Items.Add(item);
        }
        readingHistoryList.EndUpdate();
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

    private static string FormatPressure(decimal? pressure) =>
        pressure is null ? "" : $"{pressure:0.00} lbs";
}
