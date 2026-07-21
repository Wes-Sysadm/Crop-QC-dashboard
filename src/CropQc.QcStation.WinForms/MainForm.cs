using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using CropQc.QcStation.Api;
using CropQc.QcStation.Fta;
using CropQc.Shared;

namespace CropQc.QcStation.WinForms;

public sealed class MainForm : Form
{
    private readonly IFtaStationService stationService;
    private string settingsPath;
    private readonly TestFruitPressureCapture testFruitCapture = new();
    private QcStationApiClient? apiClient;
    private QcStationSampleDetail? selectedSample;
    private bool hasUnsavedPressureChanges;
    private readonly HashSet<int> dirtyPressureRows = [];
    private readonly Dictionary<string, Label> valueLabels = [];
    private readonly TextBox apiBaseUrlTextBox = new() { Width = 260 };
    private readonly TextBox warehouseFilterTextBox = new() { Width = 80 };
    private readonly TextBox apiStatusTextBox = CreateReadOnlyTextBox(260);
    private readonly TextBox selectedSampleTextBox = CreateReadOnlyTextBox(260);
    private readonly TextBox sampleContextTextBox = CreateReadOnlyTextBox(520);
    private readonly TextBox unsavedChangesTextBox = CreateReadOnlyTextBox(120);
    private readonly TextBox lastSaveResultTextBox = CreateReadOnlyTextBox(260);
    private readonly CheckBox autoSaveCompletedFruitCheckBox = new() { Text = "Auto-save after each captured reading", AutoSize = true, Checked = true };
    private readonly SemaphoreSlim pressureSaveLock = new(1, 1);
    private readonly ListView sampleListView = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
        HeaderStyle = ColumnHeaderStyle.Nonclickable,
        Height = 115
    };
    private readonly NumericUpDown fruitNumberInput = new()
    {
        Minimum = 1,
        Maximum = TestFruitPressureCapture.MaxFruitCount,
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
    private readonly TextBox currentFruitTextBox = CreateReadOnlyTextBox();
    private readonly TextBox currentTargetTextBox = CreateReadOnlyTextBox();
    private readonly TextBox continuousStatusTextBox = CreateReadOnlyTextBox();
    private readonly ListView fruitPressureGrid = new()
    {
        Dock = DockStyle.Fill,
        View = View.Details,
        FullRowSelect = true,
        GridLines = true,
        HeaderStyle = ColumnHeaderStyle.Nonclickable
    };
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
    private readonly IFtaEnvironmentDiagnostics ftaEnvironmentDiagnostics = new FtaEnvironmentDiagnostics();
    private readonly TextBox ftaDiagnosticsTextBox = new()
    {
        Dock = DockStyle.Fill,
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Both,
        WordWrap = false
    };
    private readonly ComboBox ftaConnectionModeComboBox = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 150
    };
    private readonly ComboBox ftaSerialPortComboBox = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 150
    };
    private int renderedLogCount;
    private CancellationTokenSource? continuousCaptureCts;
    private bool isContinuousCaptureRunning;
    private bool shutdownComplete;
    private bool shutdownInProgress;

    private readonly QcStationProtocolLaunch? launchRequest;

    public MainForm(IFtaStationService stationService, string settingsPath, QcStationProtocolLaunch? launchRequest = null)
    {
        this.stationService = stationService;
        this.settingsPath = settingsPath;
        this.launchRequest = launchRequest;

        Text = $"{ProjectInfo.Name} QC Station WinForms FTA Harness";
        Width = 1180;
        Height = 820;
        MinimumSize = new Size(900, 640);
        apiBaseUrlTextBox.Text = stationService.Configuration.ApiBaseUrl;
        warehouseFilterTextBox.Text = stationService.Configuration.WarehouseCode;

        BuildLayout();
        RefreshStatusDisplay();
        RefreshSampleStatusDisplay();
        RefreshCaptureDisplay();
        AppendLog($"Settings: {settingsPath}");
        AppendLog("WinForms harness started on STA thread with a Windows message loop.");
        Shown += async (_, _) => await HandleLaunchRequestAsync();
    }

    private void BuildLayout()
    {
        var menu = new MenuStrip();
        var setupMenu = new ToolStripMenuItem("Station Setup");
        setupMenu.DropDownItems.Add("Import / Replace Station Config", null, async (_, _) => await ImportStationConfigAsync());
        setupMenu.DropDownItems.Add("Open Config Folder", null, async (_, _) => await OpenConfigFolderAsync());
        setupMenu.DropDownItems.Add("Forget Current Config", null, async (_, _) => await ForgetCurrentConfigAsync());
        setupMenu.DropDownItems.Add("Test Dashboard Connection", null, async (_, _) => await TestDashboardConnectionAsync());
        menu.Items.Add(setupMenu);
        MainMenuStrip = menu;
        Controls.Add(menu);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(10, menu.Height + 6, 10, 10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(BuildScrollableTab("Setup / Connection", BuildStackedPanel(BuildStationSetupPanel(), BuildStatusPanel())));
        tabs.TabPages.Add(BuildScrollableTab("Sample Selection", BuildSampleSelectionPanel()));
        tabs.TabPages.Add(BuildScrollableTab("FTA Capture", BuildStackedPanel(BuildGuidancePanel(), BuildButtonPanel(), BuildCaptureFieldsPanel())));
        tabs.TabPages.Add(BuildTab("Pressure Grid", BuildPressureTablesPanel()));
        tabs.TabPages.Add(BuildTab("FTA Diagnostics", BuildFtaDiagnosticsPanel()));
        tabs.TabPages.Add(BuildTab("Logs / Diagnostics", BuildLogPanel()));
        root.Controls.Add(tabs, 0, 0);
    }

    private static TabPage BuildScrollableTab(string title, Control content)
    {
        var tab = new TabPage(title) { AutoScroll = true };
        content.Dock = DockStyle.Top;
        tab.Controls.Add(content);
        return tab;
    }

    private static TabPage BuildTab(string title, Control content)
    {
        var tab = new TabPage(title);
        content.Dock = DockStyle.Fill;
        tab.Controls.Add(content);
        return tab;
    }

    private static Control BuildStackedPanel(params Control[] controls)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 1,
            RowCount = controls.Length,
            Padding = new Padding(8)
        };

        for (var index = 0; index < controls.Length; index++)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            controls[index].Dock = DockStyle.Top;
            controls[index].Margin = new Padding(0, 0, 0, 10);
            panel.Controls.Add(controls[index], 0, index);
        }

        return panel;
    }

    private Control BuildStationSetupPanel()
    {
        var group = new GroupBox
        {
            Dock = DockStyle.Top,
            Text = "Station Setup",
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
        AddSampleButton(flow, "Import / Replace Station Config", ImportStationConfigAsync);
        AddSampleButton(flow, "Open Config Folder", OpenConfigFolderAsync);
        AddSampleButton(flow, "Forget Current Config", ForgetCurrentConfigAsync);
        AddSampleButton(flow, "Test Dashboard Connection", TestDashboardConnectionAsync);
        flow.Controls.Add(new Label
        {
            Text = "Import downloaded qcstation.settings.json after station key rotation. The app installs it to ProgramData, reloads station identity immediately, and never displays the API key.",
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            Margin = new Padding(8, 8, 4, 4)
        });
        return group;
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

        AddStatusRow(grid, "Loaded config path", "LoadedConfigPath", "Station name", "StationName");
        AddStatusRow(grid, "Station code", "QcStationCode", "Warehouse", "WarehouseCode");
        AddStatusRow(grid, "FTA mode", "FtaMode", "DLL path", "FtaDllPath");
        AddStatusRow(grid, "DLL file", "FtaDllFileName", "Initialization mode", "FtaInitializationMode");
        AddStatusRow(grid, "FTA config path", "FtaConfigPath", "Working directory", "FtaWorkingDirectory");
        AddStatusRow(grid, "Current working directory", "CurrentWorkingDirectory", "Process architecture", "ProcessArchitecture");
        AddStatusRow(grid, "OS architecture", "OSArchitecture", "Last pressure reading", "LastPressureReading");
        AddStatusRow(grid, "API base URL", "ApiBaseUrl", "Safe capture mode", "FtaManualCaptureSafeMode");
        AddStatusRow(grid, "FTA firmness unit", "FtaFirmnessUnit", "Re-arm timing", "FtaRearmTiming");
        AddStatusRow(grid, "FTA connection mode", "FtaConnectionMode", "FTA serial port", "FtaSerialPort");

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
            Text = "Use Manual/Button mode only. Press and hold the physical FTA button for each test. If the probe travels too far or behaves unexpectedly, click Stop Capture and use Calibration before continuing."
        };

    private Control BuildSampleSelectionPanel()
    {
        var group = new GroupBox
        {
            Dock = DockStyle.Top,
            Text = "Dashboard Sample Selection",
            AutoSize = true
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(10),
            AutoSize = true
        };
        group.Controls.Add(root);

        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true
        };
        controls.Controls.Add(new Label { Text = "ApiBaseUrl", AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Margin = new Padding(0, 8, 4, 4) });
        controls.Controls.Add(apiBaseUrlTextBox);
        controls.Controls.Add(new Label { Text = "Warehouse", AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Margin = new Padding(8, 8, 4, 4) });
        controls.Controls.Add(warehouseFilterTextBox);
        AddSampleButton(controls, "Refresh Today's Samples", RefreshTodaySamplesAsync);
        AddSampleButton(controls, "Select Sample", SelectCurrentSampleAsync);
        AddSampleButton(controls, "Save / Retry Unsaved Pressures", SavePressuresToDashboardAsync);
        controls.Controls.Add(autoSaveCompletedFruitCheckBox);
        root.Controls.Add(controls, 0, 0);

        var status = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true
        };
        AddLabeledControl(status, "API status", apiStatusTextBox);
        AddLabeledControl(status, "Selected sample", selectedSampleTextBox);
        AddLabeledControl(status, "Unsaved", unsavedChangesTextBox);
        AddLabeledControl(status, "Last save", lastSaveResultTextBox);
        AddLabeledControl(status, "Context", sampleContextTextBox);
        root.Controls.Add(status, 0, 1);

        sampleListView.Columns.Add("Display ID", 105);
        sampleListView.Columns.Add("Warehouse", 80);
        sampleListView.Columns.Add("Room", 80);
        sampleListView.Columns.Add("Grower", 150);
        sampleListView.Columns.Add("Lot", 100);
        sampleListView.Columns.Add("Variety", 80);
        sampleListView.Columns.Add("Status", 130);
        sampleListView.Columns.Add("Starch", 105);
        sampleListView.Columns.Add("Email", 90);
        sampleListView.Columns.Add("P Rows", 70);
        root.Controls.Add(sampleListView, 0, 2);

        sampleListView.DoubleClick += async (_, _) => await SelectCurrentSampleAsync();
        apiStatusTextBox.Text = "Not connected";
        selectedSampleTextBox.Text = "(none)";
        unsavedChangesTextBox.Text = "No";
        lastSaveResultTextBox.Text = "(none)";
        sampleContextTextBox.Text = "(none)";

        return group;
    }

    private static void AddLabeledControl(FlowLayoutPanel panel, string label, Control control)
    {
        panel.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            Margin = new Padding(8, 8, 4, 4)
        });
        panel.Controls.Add(control);
    }

    private Control BuildButtonPanel()
    {
        var group = new GroupBox
        {
            Dock = DockStyle.Top,
            Text = "FTA Capture - Manual/Button Mode",
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

        AddFtaCommand(flow, "Initialize", () => stationService.InitializeAsync());
        AddFtaCommand(flow, "Calibration", () => stationService.OpenSetupAsync());
        AddFtaCaptureButton(flow, "Diagnostics", RunFullFtaDiagnosticAsync);
        AddFtaContinuousButton(flow, "Start Manual Capture", StartContinuousManualCapture);
        AddFtaContinuousButton(flow, "Stop Capture", StopContinuousCapture);
        AddFtaCaptureButton(flow, "Quit", QuitDisconnectFtaAsync);

        return group;
    }

    private Control BuildFtaDiagnosticsPanel()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var controls = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true
        };
        ftaConnectionModeComboBox.Items.AddRange(Enum.GetNames<FtaConnectionMode>());
        ftaConnectionModeComboBox.SelectedItem = stationService.Configuration.FtaConnectionMode.ToString();
        controls.Controls.Add(new Label { Text = "Connection mode", AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Margin = new Padding(4, 8, 4, 4) });
        controls.Controls.Add(ftaConnectionModeComboBox);
        controls.Controls.Add(new Label { Text = "COM port", AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold), Margin = new Padding(8, 8, 4, 4) });
        controls.Controls.Add(ftaSerialPortComboBox);
        AddSampleButton(controls, "Refresh Devices", RefreshFtaDevicesAsync);
        AddSampleButton(controls, "Run Full FTA Diagnostic", RunFullFtaDiagnosticAsync);
        AddSampleButton(controls, "Copy Diagnostic Report", CopyFtaDiagnosticReportAsync);
        AddSampleButton(controls, "Open FTA Setup / Calibration", OpenFtaSetupAndRefreshDiagnosticsAsync);
        AddSampleButton(controls, "Save FTA Connection Settings", SaveFtaConnectionSettingsAsync);
        controls.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(760, 0),
            Margin = new Padding(8, 8, 4, 4),
            Text = "Serial settings must match FTA Setup / FTA_DLL.CFG. Auto mode prefers known USB HID VID_6017/PID_3430, then configured COM ports, and never silently chooses among multiple USB-to-serial adapters."
        });
        root.Controls.Add(controls, 0, 0);
        root.Controls.Add(ftaDiagnosticsTextBox, 0, 1);

        RefreshFtaDeviceSelections();
        ftaDiagnosticsTextBox.Text = "Click Run Full FTA Diagnostic to check app architecture, DLL files, USB HID devices, COM ports, FTA_DLL.CFG, and FTAStatus/FTABitStatus response.";
        return root;
    }

    private Control BuildPressureCapturePanel()
    {
        var group = new GroupBox
        {
            Dock = DockStyle.Fill,
            Text = "QC Sample Pressure Capture"
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(10)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 400));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        group.Controls.Add(root);

        root.Controls.Add(BuildCaptureFieldsPanel(), 0, 0);
        root.Controls.Add(BuildFruitPressureGridPanel(), 1, 0);
        root.Controls.Add(BuildReadingHistoryPanel(), 2, 0);

        return group;
    }

    private Control BuildPressureTablesPanel()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(10)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        root.Controls.Add(BuildFruitPressureGridPanel(), 0, 0);
        root.Controls.Add(BuildReadingHistoryPanel(), 1, 0);
        return root;
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
        AddCaptureRow(panel, "Current fruit", currentFruitTextBox, 5);
        AddCaptureRow(panel, "Current target", currentTargetTextBox, 6);
        AddCaptureRow(panel, "Continuous status", continuousStatusTextBox, 7);

        var targetPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = true
        };
        targetPanel.Controls.Add(pressure1TargetRadio);
        targetPanel.Controls.Add(pressure2TargetRadio);
        targetPanel.Controls.Add(autoAdvanceTargetRadio);
        AddCaptureRow(panel, "Capture target", targetPanel, 8);

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
            var clearedFruit = testFruitCapture.FruitNumber;
            testFruitCapture.ClearCurrentFruit();
            dirtyPressureRows.Add(clearedFruit);
            hasUnsavedPressureChanges = true;
            RefreshCaptureDisplay();
            AppendLog("Local test fruit cleared.");
        };
        buttons.Controls.Add(clearButton);
        AddCaptureRow(panel, "", buttons, 9);

        var note = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(390, 0),
            Text = "Auto-save is on by default. Save / Retry Unsaved Pressures updates only Pressure 1 and Pressure 2 on the selected sample."
        };
        AddCaptureRow(panel, "", note, 10);

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

    private Control BuildFruitPressureGridPanel()
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
            Text = "Pressure Grid",
            AutoSize = true,
            Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 6)
        }, 0, 0);

        fruitPressureGrid.Columns.Add("Fruit", 55);
        fruitPressureGrid.Columns.Add("Pressure 1", 85);
        fruitPressureGrid.Columns.Add("Pressure 2", 85);
        fruitPressureGrid.Columns.Add("Average", 75);
        fruitPressureGrid.Columns.Add("Status", 90);
        panel.Controls.Add(fruitPressureGrid, 0, 1);

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

    private void AddFtaCommand(FlowLayoutPanel flow, string text, Func<Task<FtaDeviceStatus>> command)
    {
        var button = CreateFtaCommandButton(text);
        button.Click += async (_, _) => await RunCommandAsync(text, async () =>
        {
            var status = await command();
            AppendLog($"{text}: {status.StatusMessage}{(string.IsNullOrWhiteSpace(status.ErrorMessage) ? "" : $" Error: {status.ErrorMessage}")}");
        });
        flow.Controls.Add(button);
    }

    private void AddFtaCaptureButton(FlowLayoutPanel flow, string text, Func<Task> command)
    {
        var button = CreateFtaCommandButton(text);
        button.Click += async (_, _) => await RunCommandAsync(text, command);
        flow.Controls.Add(button);
    }

    private void AddFtaContinuousButton(FlowLayoutPanel flow, string text, Action command)
    {
        var button = CreateFtaCommandButton(text);
        button.Click += (_, _) => command();
        flow.Controls.Add(button);
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

    private void AddContinuousButton(FlowLayoutPanel flow, string text, Action command)
    {
        var button = CreateButton(text);
        button.Click += (_, _) => command();
        flow.Controls.Add(button);
    }

    private void AddSampleButton(FlowLayoutPanel flow, string text, Func<Task> command)
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

    private static Button CreateFtaCommandButton(string text) =>
        new()
        {
            Text = text,
            AutoSize = false,
            Width = 178,
            Height = 48,
            Margin = new Padding(6),
            Padding = new Padding(8, 6, 8, 6),
            TextAlign = ContentAlignment.MiddleCenter
        };

    private static TextBox CreateReadOnlyTextBox(int width = 220) =>
        new()
        {
            ReadOnly = true,
            Width = width
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

    private async Task RefreshTodaySamplesAsync()
    {
        if (!StationConfiguration.IsConfigurationValid(stationService.Configuration))
        {
            apiStatusTextBox.Text = "Config missing/invalid";
            AppendLog("Refresh Today's Samples skipped: import a valid station config first.");
            return;
        }

        apiClient = CreateApiClient();
        apiStatusTextBox.Text = "Loading...";
        sampleListView.Items.Clear();
        try
        {
            var samples = await apiClient.GetTodaySamplesAsync(warehouseFilterTextBox.Text);
            foreach (var sample in samples)
            {
                var item = new ListViewItem(sample.DisplayReceiptId) { Tag = sample };
                item.SubItems.Add(sample.WarehouseCode);
                item.SubItems.Add(sample.RoomCode);
                item.SubItems.Add(sample.GrowerName);
                item.SubItems.Add(sample.LotCode);
                item.SubItems.Add(sample.VarietyCode);
                item.SubItems.Add(sample.Status);
                item.SubItems.Add(sample.StarchStatus);
                item.SubItems.Add(sample.EmailStatus);
                item.SubItems.Add(sample.CompletedPressureRows.ToString());
                sampleListView.Items.Add(item);
            }

            apiStatusTextBox.Text = $"Loaded {samples.Count} samples";
            AppendLog($"Loaded {samples.Count} samples from dashboard API.");
        }
        catch (Exception ex)
        {
            HandleApiException("Refresh today's samples", ex);
        }
    }

    private async Task SelectCurrentSampleAsync()
    {
        if (sampleListView.SelectedItems.Count == 0 || sampleListView.SelectedItems[0].Tag is not QcStationSampleListItem sample)
        {
            AppendLog("Select Sample requested, but no sample is selected.");
            return;
        }

        try
        {
            apiClient ??= CreateApiClient();
            selectedSample = await apiClient.GetSampleDetailAsync(sample.SampleId);
            if (selectedSample is null)
            {
                apiStatusTextBox.Text = "Sample not found";
                AppendLog($"Sample {sample.SampleId} was not found by the API.");
                return;
            }

            LoadSelectedSampleIntoCaptureGrid();
            dirtyPressureRows.Clear();
            hasUnsavedPressureChanges = false;
            RefreshSampleStatusDisplay();
            RefreshCaptureDisplay();
            AppendLog($"Selected sample {selectedSample.DisplayReceiptId} from dashboard API.");
        }
        catch (Exception ex)
        {
            HandleApiException("Select sample", ex);
        }
    }

    private async Task LoadSampleByIdAsync(long sampleId)
    {
        try
        {
            apiClient ??= CreateApiClient();
            selectedSample = await apiClient.GetSampleDetailAsync(sampleId);
            if (selectedSample is null)
            {
                apiStatusTextBox.Text = "Sample not found";
                AppendLog($"Protocol launch sample {sampleId} was not found by the API.");
                return;
            }

            LoadSelectedSampleIntoCaptureGrid();
            dirtyPressureRows.Clear();
            hasUnsavedPressureChanges = false;
            RefreshSampleStatusDisplay();
            RefreshCaptureDisplay();
            apiStatusTextBox.Text = $"Loaded sample {selectedSample.DisplayReceiptId}";
            AppendLog($"Loaded sample {selectedSample.DisplayReceiptId} from protocol launch.");
        }
        catch (Exception ex)
        {
            HandleApiException($"Load sample {sampleId}", ex);
        }
    }

    private async Task HandleLaunchRequestAsync()
    {
        if (launchRequest?.SampleId is long sampleId)
        {
            AppendLog($"Protocol launch requested sample {sampleId}.");
            await LoadSampleByIdAsync(sampleId);
        }
        else if (launchRequest?.ReceiptId is long receiptId)
        {
            AppendLog($"Protocol launch requested receipt {receiptId}; receipt launch is not implemented yet.");
        }
    }

    private void LoadSelectedSampleIntoCaptureGrid()
    {
        testFruitCapture.LoadRows(selectedSample!.FruitReadings.Select(row => new FruitPressureCaptureRow(
            row.RowNumber,
            row.Pressure1Lbs,
            row.Pressure2Lbs,
            row.PressureAverageLbs,
            row.Pressure1Lbs is null ? "Missing P1" : row.Pressure2Lbs is null ? "Missing P2" : "Complete")),
            selectedSample.TargetSampleSize);
        fruitNumberInput.Maximum = testFruitCapture.TargetFruitCount;
    }

    private Task SavePressuresToDashboardAsync() =>
        SavePressuresToDashboardAsync(autoSave: false, capture: null);

    private async Task SavePressuresToDashboardAsync(bool autoSave, CapturedPressureHistoryEntry? capture)
    {
        await pressureSaveLock.WaitAsync();
        try
        {
            await SavePressuresToDashboardCoreAsync(autoSave, capture);
        }
        finally
        {
            pressureSaveLock.Release();
        }
    }

    private async Task SavePressuresToDashboardCoreAsync(bool autoSave, CapturedPressureHistoryEntry? capture)
    {
        var actionName = autoSave ? "Auto-save pressures" : "Save pressures";
        if (selectedSample is null)
        {
            lastSaveResultTextBox.Text = "Select a sample before saving pressures.";
            AppendLog($"{actionName}: select a sample before saving pressures.");
            return;
        }

        var config = stationService.Configuration;
        if (string.IsNullOrWhiteSpace(config.ApiBaseUrl)
            || string.IsNullOrWhiteSpace(config.QcStationCode)
            || string.IsNullOrWhiteSpace(config.QcStationApiKey))
        {
            lastSaveResultTextBox.Text = "Station config missing.";
            AppendLog($"{actionName}: station config is missing ApiBaseUrl, QcStationCode, or QcStationApiKey.");
            return;
        }

        apiClient ??= CreateApiClient();
        var rows = testFruitCapture.Rows
            .Where(row => dirtyPressureRows.Contains(row.FruitNumber))
            .Select(row => new QcStationPressureRowUpdate(row.FruitNumber, row.Pressure1Lbs, row.Pressure2Lbs))
            .ToList();
        if (rows.Count == 0)
        {
            lastSaveResultTextBox.Text = "No pressure values to save.";
            AppendLog($"{actionName}: there are no pressure values to send.");
            return;
        }

        try
        {
            if (autoSave && capture is not null)
            {
                lastSaveResultTextBox.Text = $"Saving Fruit {capture.FruitNumber} {capture.TargetSlot}...";
                continuousStatusTextBox.Text = "Saving to dashboard...";
            }

            AppendLog($"{actionName} started. SampleId: {selectedSample.SampleId}; StationCode: {config.QcStationCode}; RowCount: {rows.Count}.");
            foreach (var row in rows)
            {
                AppendLog($"{actionName} row {row.RowNumber}: P1={FormatPressure(row.Pressure1Lbs)}, P2={FormatPressure(row.Pressure2Lbs)}.");
            }

            selectedSample = await apiClient.SavePressuresAsync(selectedSample.SampleId, rows);
            dirtyPressureRows.ExceptWith(rows.Select(row => row.RowNumber));
            hasUnsavedPressureChanges = dirtyPressureRows.Count > 0;
            lastSaveResultTextBox.Text = autoSave && capture is not null
                ? $"Saved Fruit {capture.FruitNumber} {capture.TargetSlot}."
                : "Saved pressures to dashboard.";
            RefreshSampleStatusDisplay();
            RefreshCaptureDisplay();
            AppendLog(autoSave && capture is not null
                ? $"Auto-save succeeded for Fruit {capture.FruitNumber} {capture.TargetSlot}. Sample: {selectedSample?.DisplayReceiptId}."
                : $"Saved pressures to dashboard. Rows: {rows.Count}; Sample: {selectedSample?.DisplayReceiptId}.");
        }
        catch (Exception ex)
        {
            hasUnsavedPressureChanges = true;
            lastSaveResultTextBox.Text = autoSave
                ? "Auto-save failed. Reading kept locally. Use Save / Retry Unsaved Pressures."
                : "Save failed.";
            RefreshSampleStatusDisplay();
            HandleApiException(actionName, ex);
            if (autoSave)
            {
                lastSaveResultTextBox.Text = "Auto-save failed. Reading kept locally. Use Save / Retry Unsaved Pressures.";
                AppendLog("Auto-save failed. Reading kept locally. Use Save / Retry Unsaved Pressures to retry.");
                if (ex is QcStationAuthorizationException or QcStationApiException)
                {
                    AppendLog("Continuous capture stopped because dashboard save failed with an authorization or server/API error.");
                    continuousCaptureCts?.Cancel();
                }
            }
        }
    }

    private QcStationApiClient CreateApiClient() =>
        QcStationApiClient.Create(
            apiBaseUrlTextBox.Text,
            stationService.Configuration.QcStationCode,
            stationService.Configuration.QcStationApiKey,
            stationService.Configuration.StationName);

    private void HandleApiException(string action, Exception ex)
    {
        if (ex is QcStationAuthorizationException)
        {
            var config = stationService.Configuration;
            var message = $"Station authorization failed for StationCode: {config.QcStationCode ?? "(missing)"}. The app may be using an old config, the station may be inactive, or the key may have been rotated. Import the latest station config from Admin -> QC Stations.";
            apiStatusTextBox.Text = "Not authorized";
            lastSaveResultTextBox.Text = "Not authorized";
            AppendLog($"{action} failed: {message}");
            AppendLog($"StationName: {config.StationName}; QcStationCode: {config.QcStationCode ?? "(missing)"}; ApiBaseUrl: {config.ApiBaseUrl}");
            MessageBox.Show(
                $"{message}{Environment.NewLine}{Environment.NewLine}StationName: {config.StationName}{Environment.NewLine}QcStationCode: {config.QcStationCode ?? "(missing)"}{Environment.NewLine}ApiBaseUrl: {config.ApiBaseUrl}",
                "QC Station not authorized",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        if (ex is QcStationApiException apiException)
        {
            var safeBody = string.IsNullOrWhiteSpace(apiException.ResponseBody) ? "(no response body)" : apiException.ResponseBody;
            apiStatusTextBox.Text = "API error";
            lastSaveResultTextBox.Text = $"Save failed: HTTP {(int?)apiException.StatusCode} {apiException.StatusCode}";
            AppendLog($"{action} failed: HTTP {(int?)apiException.StatusCode} {apiException.StatusCode}. Response: {safeBody}");
            return;
        }

        apiStatusTextBox.Text = "API error";
        lastSaveResultTextBox.Text = $"Save failed: {ex.Message}";
        AppendLog($"{action} failed: {ex.Message}");
    }

    private async Task ImportStationConfigAsync()
    {
        using var importForm = new ConfigImportForm(StationConfiguration.InstalledSettingsPath, launchRequest);
        if (importForm.ShowDialog(this) != DialogResult.OK || !File.Exists(StationConfiguration.InstalledSettingsPath))
        {
            AppendLog("Station config import was cancelled or did not install a config.");
            return;
        }

        settingsPath = StationConfiguration.InstalledSettingsPath;
        var importedConfiguration = StationConfiguration.Load(settingsPath);
        stationService.Configuration.CopyFrom(importedConfiguration);
        apiBaseUrlTextBox.Text = importedConfiguration.ApiBaseUrl;
        warehouseFilterTextBox.Text = importedConfiguration.WarehouseCode;
        apiClient = null;
        RefreshStatusDisplay();
        AppendLog($"Station config imported. StationName: {importedConfiguration.StationName}; QcStationCode: {importedConfiguration.QcStationCode}; WarehouseCode: {importedConfiguration.WarehouseCode}; ApiBaseUrl: {importedConfiguration.ApiBaseUrl}.");
        await TestDashboardConnectionAsync();
        if (launchRequest?.SampleId is long sampleId && selectedSample is null)
        {
            AppendLog($"Retrying pending protocol sample {sampleId} after config import.");
            await LoadSampleByIdAsync(sampleId);
        }
    }

    private Task OpenConfigFolderAsync()
    {
        var folder = Path.GetDirectoryName(StationConfiguration.InstalledSettingsPath)!;
        try
        {
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true
            });
            AppendLog($"Opened config folder: {folder}");
        }
        catch (Exception ex)
        {
            AppendLog($"Open Config Folder failed: {ex.Message}");
            MessageBox.Show(
                $"Could not open config folder:{Environment.NewLine}{folder}{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Open Config Folder failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        return Task.CompletedTask;
    }

    private async Task ForgetCurrentConfigAsync()
    {
        var result = MessageBox.Show(
            "Forget the current station config? The active config will be backed up and removed. API calls will stop until a valid config is imported.",
            "Forget Current Config",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);
        if (result != DialogResult.Yes)
        {
            AppendLog("Forget Current Config cancelled.");
            return;
        }

        try
        {
            var path = StationConfiguration.InstalledSettingsPath;
            if (File.Exists(path))
            {
                var backupPath = StationConfiguration.BackupPathFor(path);
                File.Move(path, backupPath, overwrite: true);
                AppendLog($"Current station config backed up to {backupPath}.");
            }
            else
            {
                AppendLog("No installed station config was present to forget.");
            }

            stationService.Configuration.CopyFrom(new StationConfiguration
            {
                StationName = "(missing)",
                WarehouseCode = "",
                ApiBaseUrl = "",
                QcStationCode = null,
                QcStationApiKey = null
            });
            settingsPath = StationConfiguration.InstalledSettingsPath;
            apiClient = null;
            selectedSample = null;
            sampleListView.Items.Clear();
            dirtyPressureRows.Clear();
            hasUnsavedPressureChanges = false;
            apiStatusTextBox.Text = "Config missing";
            selectedSampleTextBox.Text = "(none)";
            sampleContextTextBox.Text = "(none)";
            RefreshStatusDisplay();
            RefreshSampleStatusDisplay();
            AppendLog("Station config forgotten. Import / Replace Station Config before using dashboard API calls.");
            MessageBox.Show(
                "Station configuration was forgotten. Use Import / Replace Station Config to install a valid qcstation.settings.json.",
                "Station config forgotten",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            await ImportStationConfigAsync();
        }
        catch (Exception ex)
        {
            AppendLog($"Forget Current Config failed: {ex.Message}");
            MessageBox.Show(
                $"Could not forget current config. Run Crop QC Station as Administrator once if Windows blocks access.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                "Forget Current Config failed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private async Task TestDashboardConnectionAsync()
    {
        if (!StationConfiguration.IsConfigurationValid(stationService.Configuration))
        {
            apiStatusTextBox.Text = "Dashboard connection failed. Config missing/invalid.";
            apiStatusTextBox.BackColor = Color.MistyRose;
            AppendLog("Test Dashboard Connection skipped: config is missing or invalid. Import station config first.");
            return;
        }

        try
        {
            apiClient = CreateApiClient();
            AppendLog($"Testing dashboard connection for StationName: {stationService.Configuration.StationName}; QcStationCode: {stationService.Configuration.QcStationCode}; ApiBaseUrl: {stationService.Configuration.ApiBaseUrl}.");
            var samples = await apiClient.GetTodaySamplesAsync(warehouseFilterTextBox.Text);
            apiStatusTextBox.BackColor = Color.Honeydew;
            apiStatusTextBox.Text = $"Dashboard connection successful. Loaded {samples.Count} samples.";
            AppendLog($"Dashboard connection successful. ApiBaseUrl: {stationService.Configuration.ApiBaseUrl}; StationName: {stationService.Configuration.StationName}; QcStationCode: {stationService.Configuration.QcStationCode}; WarehouseCode: {stationService.Configuration.WarehouseCode}; Loaded {samples.Count} samples.");
        }
        catch (Exception ex)
        {
            apiStatusTextBox.BackColor = Color.MistyRose;
            var statusText = ex switch
            {
                QcStationAuthorizationException authEx when authEx.StatusCode == System.Net.HttpStatusCode.Forbidden => "Dashboard connection failed. Station inactive or forbidden.",
                QcStationAuthorizationException => "Dashboard connection failed. Unauthorized / invalid key.",
                QcStationApiException apiEx => $"Dashboard connection failed. HTTP {(int?)apiEx.StatusCode} {apiEx.StatusCode}.",
                HttpRequestException => "Dashboard connection failed. Server unavailable or network error.",
                TaskCanceledException => "Dashboard connection failed. Server unavailable or timed out.",
                _ => "Dashboard connection failed. Unexpected response."
            };
            apiStatusTextBox.Text = statusText;
            HandleApiException("Test dashboard connection", ex);
            apiStatusTextBox.BackColor = Color.MistyRose;
            apiStatusTextBox.Text = statusText;
        }
    }

    private void StartContinuousManualCapture()
    {
        if (isContinuousCaptureRunning)
        {
            AppendLog("Continuous capture is already running.");
            return;
        }

        if (testFruitCapture.IsSampleComplete)
        {
            AppendLog($"Continuous capture was not started because the local {testFruitCapture.TargetFruitCount}-fruit sample is already complete.");
            return;
        }

        continuousCaptureCts = new CancellationTokenSource();
        isContinuousCaptureRunning = true;
        continuousStatusTextBox.Text = "Armed";
        AppendLog("Continuous capture started.");
        AppendLog($"Current target: Fruit {testFruitCapture.FruitNumber} {testFruitCapture.CurrentTargetSlot}.");
        AppendLog("Continuous capture armed. Press and hold the green FTA button for each test.");
        _ = RunContinuousManualCaptureAsync(continuousCaptureCts.Token);
        RefreshCaptureDisplay();
    }

    private void StopContinuousCapture()
    {
        if (!isContinuousCaptureRunning)
        {
            AppendLog("Continuous capture is not running.");
            return;
        }

        AppendLog("Continuous capture stop requested.");
        _ = Task.Run(async () =>
        {
            try
            {
                await stationService.CancelReadingAsync();
            }
            catch (Exception ex)
            {
                BeginInvoke(() => AppendLog($"Stop Continuous Capture: Cancel FTA Action failed: {ex.Message}"));
            }
        });
        continuousCaptureCts?.Cancel();
    }

    private async Task StopContinuousCaptureForShutdownAsync(string reason)
    {
        if (!isContinuousCaptureRunning)
        {
            AppendLog($"{reason}: continuous capture is not running.");
            return;
        }

        AppendLog($"{reason}: stopping continuous capture.");
        continuousCaptureCts?.Cancel();

        var timeoutAt = DateTimeOffset.UtcNow.AddSeconds(3);
        while (isContinuousCaptureRunning && DateTimeOffset.UtcNow < timeoutAt)
        {
            await Task.Delay(100);
            Application.DoEvents();
        }

        AppendLog(isContinuousCaptureRunning
            ? $"{reason}: continuous capture stop requested; continuing with disconnect cleanup."
            : $"{reason}: continuous capture stopped.");
    }

    private async Task QuitDisconnectFtaAsync()
    {
        AppendLog("Quit/Disconnect FTA requested.");
        await ShutdownFtaAsync("Quit/Disconnect FTA", closeAfterShutdown: false);
    }

    private async Task ShutdownFtaAsync(string reason, bool closeAfterShutdown)
    {
        if (shutdownInProgress)
        {
            AppendLog($"{reason}: shutdown is already in progress.");
            return;
        }

        shutdownInProgress = true;
        try
        {
            await StopContinuousCaptureForShutdownAsync(reason);

            AppendLog($"{reason}: calling FTACancel then FTAQuit.");
            var status = await stationService.QuitAsync();
            RenderNewServiceLogEntries();
            AppendLog($"{reason}: {status.StatusMessage}{(string.IsNullOrWhiteSpace(status.ErrorMessage) ? "" : $" Error: {status.ErrorMessage}")}");

            continuousStatusTextBox.Text = "Disconnected - initialize FTA before capture";
            AppendLog($"{reason}: local status set to disconnected/not initialized.");
        }
        catch (Exception ex)
        {
            AppendLog($"{reason}: disconnect cleanup failed: {ex.Message}");
            continuousStatusTextBox.Text = "Disconnect error - initialize or restart before capture";
        }
        finally
        {
            shutdownInProgress = false;
            RefreshStatusDisplay();
            RefreshCaptureDisplay();

            if (closeAfterShutdown)
            {
                shutdownComplete = true;
                Close();
            }
        }
    }

    private async Task RunContinuousManualCaptureAsync(CancellationToken cancellationToken)
    {
        try
        {
            await ArmContinuousManualReadingAsync(cancellationToken);
            while (!cancellationToken.IsCancellationRequested)
            {
                var reading = await stationService.PollLatestPressureReadingAsync(cancellationToken);
                RenderNewServiceLogEntries();

                if (reading is not null && testFruitCapture.ShouldCaptureReading(reading))
                {
                    AppendLog("Continuous capture reading detected.");
                    var capture = CaptureReading(reading, PressureCaptureTarget.AutoAdvance, syncFruitFromInput: false);
                    AppendLog($"Auto-advanced target: Fruit {testFruitCapture.FruitNumber} {testFruitCapture.CurrentTargetSlot}.");

                    if (testFruitCapture.IsSampleComplete)
                    {
                        AppendLog($"Continuous capture completed Fruit {testFruitCapture.TargetFruitCount} Pressure 2. Local sample capture is complete.");
                        break;
                    }

                    await WaitForReadingResetAsync(cancellationToken);
                    await ArmContinuousManualReadingAsync(cancellationToken);
                }

                await Task.Delay(500, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            AppendLog("Continuous capture stopped.");
        }
        catch (TimeoutException ex)
        {
            continuousStatusTextBox.Text = "FTA home timeout";
            AppendLog(ex.Message);
        }
        catch (Exception ex)
        {
            AppendLog($"Continuous capture stopped because of an error: {ex.Message}");
        }
        finally
        {
            isContinuousCaptureRunning = false;
            continuousCaptureCts?.Dispose();
            continuousCaptureCts = null;
            continuousStatusTextBox.Text = testFruitCapture.IsSampleComplete ? "Sample complete" : "Stopped";
            RefreshStatusDisplay();
            RefreshCaptureDisplay();
        }
    }

    private async Task ArmContinuousManualReadingAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await WaitForManualRearmReadyAsync(cancellationToken);
        var status = await stationService.StartPressureReadingAsync(cancellationToken);
        RenderNewServiceLogEntries();
        if (!status.IsInitialized)
        {
            throw new InvalidOperationException(status.ErrorMessage ?? status.StatusMessage);
        }

        continuousStatusTextBox.Text = "Ready for next FTA button press.";
        AppendLog($"Re-armed for next test. Current target: Fruit {testFruitCapture.FruitNumber} {testFruitCapture.CurrentTargetSlot}.");
    }

    private async Task WaitForReadingResetAsync(CancellationToken cancellationToken)
    {
        // FTAReadMaxFirmness resets the new-reading bit per SDK. Give the DLL/UI
        // message loop a short breath before accepting another reading event.
        await Task.Delay(500, cancellationToken);
    }

    private async Task WaitForManualRearmReadyAsync(CancellationToken cancellationToken)
    {
        var config = stationService.Configuration;
        var delayMs = config.FtaManualRearmDelayMs > 0 ? config.FtaManualRearmDelayMs : 250;
        var pollIntervalMs = config.FtaHomePollIntervalMs > 0 ? config.FtaHomePollIntervalMs : 100;
        var maxHomeWaitMs = config.FtaMaxHomeWaitMs > 0 ? config.FtaMaxHomeWaitMs : 5000;
        if (!config.FtaManualCaptureSafeMode)
        {
            await Task.Delay(delayMs, cancellationToken);
            return;
        }

        continuousStatusTextBox.Text = "Waiting for probe home...";
        AppendLog($"Waiting for FTA to return home before next test. Polling every {pollIntervalMs} ms; max wait {maxHomeWaitMs} ms.");
        var startedAt = DateTimeOffset.UtcNow;
        var deadline = startedAt.AddMilliseconds(maxHomeWaitMs);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = await stationService.DiagnosticStatusAsync(cancellationToken);
            RenderNewServiceLogEntries();
            if (!status.IsConnected && config.FtaMode == FtaMode.RealDll)
            {
                throw new InvalidOperationException(status.ErrorMessage ?? "FTA connection was lost while waiting to re-arm.");
            }

            if (StatusIndicatesProbeAtTop(status.StatusMessage))
            {
                var elapsedMs = (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds;
                AppendLog($"FTA home detected after {elapsedMs:0} ms. Debouncing {delayMs} ms before re-arm.");
                await Task.Delay(delayMs, cancellationToken);
                continuousStatusTextBox.Text = "Ready for next FTA button press.";
                AppendLog($"FTA ready for next button press after {(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds:0} ms.");
                return;
            }

            if (!StatusIndicatesProbeAtBottom(status.StatusMessage)
                && DateTimeOffset.UtcNow - startedAt >= TimeSpan.FromMilliseconds(delayMs))
            {
                AppendLog($"Probe-at-top was not confirmed, but fallback safe delay of {delayMs} ms elapsed without probe-at-bottom status. Total wait {(DateTimeOffset.UtcNow - startedAt).TotalMilliseconds:0} ms.");
                continuousStatusTextBox.Text = "Ready for next FTA button press.";
                return;
            }

            await Task.Delay(pollIntervalMs, cancellationToken);
        }

        continuousStatusTextBox.Text = "FTA home timeout";
        throw new TimeoutException("FTA did not report home position. Capture stopped. Check FTA and use Calibration/Diagnostics before continuing.");
    }

    private static bool StatusIndicatesProbeAtTop(string statusMessage)
    {
        return StatusBitSegmentContains(statusMessage, "FTABitStatus(5) probe at top", "Yes");
    }

    private static bool StatusIndicatesProbeAtBottom(string statusMessage)
    {
        return StatusBitSegmentContains(statusMessage, "FTABitStatus(6) probe at bottom", "Yes");
    }

    private static bool StatusBitSegmentContains(string statusMessage, string marker, string expectedValue)
    {
        var index = statusMessage.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return false;
        }

        var end = statusMessage.IndexOf('|', index);
        var segment = end < 0 ? statusMessage[index..] : statusMessage[index..end];
        return segment.Contains(expectedValue, StringComparison.OrdinalIgnoreCase);
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

    private CapturedPressureHistoryEntry CaptureReading(PressureReading reading, PressureCaptureTarget target, bool syncFruitFromInput = true)
    {
        if (syncFruitFromInput)
        {
            testFruitCapture.FruitNumber = (int)fruitNumberInput.Value;
        }

        var capturedFruit = testFruitCapture.FruitNumber;
        var slot = testFruitCapture.Capture(reading, target);
        dirtyPressureRows.Add(capturedFruit);
        hasUnsavedPressureChanges = true;
        continuousStatusTextBox.Text = "Reading captured.";
        AppendLog(FormatCapturedReadingLog(reading, capturedFruit, slot));
        var capture = new CapturedPressureHistoryEntry(reading.CapturedAt, reading.ReadingValueLbs, reading.Source, capturedFruit, slot);
        RefreshCaptureDisplay();
        RefreshSampleStatusDisplay();
        QueueAutoSave(capture);
        return capture;
    }

    private static string FormatCapturedReadingLog(PressureReading reading, int fruitNumber, string slot)
    {
        if (reading.RawReadingValue is decimal rawValue && !string.IsNullOrWhiteSpace(reading.RawReadingUnit))
        {
            return $"Captured raw {rawValue:0.00} {reading.RawReadingUnit} = {reading.ReadingValueLbs:0.00} lbs into Fruit {fruitNumber} {slot}.";
        }

        return $"Captured {reading.ReadingValueLbs:0.00} lbs into Fruit {fruitNumber} {slot}.";
    }

    private void QueueAutoSave(CapturedPressureHistoryEntry capture)
    {
        if (!autoSaveCompletedFruitCheckBox.Checked)
        {
            return;
        }

        AppendLog($"Auto-save queued for Fruit {capture.FruitNumber} {capture.TargetSlot}.");
        _ = SavePressuresToDashboardAsync(autoSave: true, capture);
    }

    private Task RefreshFtaDevicesAsync()
    {
        RefreshFtaDeviceSelections();
        var report = BuildFtaDiagnosticReport(null, null);
        ftaDiagnosticsTextBox.Text = report.ReportText;
        AppendLog("FTA device discovery refreshed.");
        return Task.CompletedTask;
    }

    private async Task RunFullFtaDiagnosticAsync()
    {
        AppendLog("Run Full FTA Diagnostic started.");
        RefreshFtaDeviceSelections();
        FtaDeviceStatus? status = null;
        string? error = null;
        try
        {
            AppendLog($"FTA diagnostic: mode {stationService.Configuration.FtaConnectionMode}; DLL {stationService.Configuration.FtaDllPath}\\{stationService.Configuration.FtaDllFileName}; working directory {FormatOptional(stationService.Configuration.FtaWorkingDirectory)}.");
            status = await stationService.InitializeAsync();
            RenderNewServiceLogEntries();
            if (!status.IsInitialized || !status.IsConnected)
            {
                error = status.ErrorMessage ?? status.StatusMessage;
            }
        }
        catch (Exception ex)
        {
            error = ex.Message;
            AppendLog($"Run Full FTA Diagnostic initialize failed: {ex.Message}");
        }

        var report = BuildFtaDiagnosticReport(status, error);
        ftaDiagnosticsTextBox.Text = report.ReportText;
        AppendLog($"Run Full FTA Diagnostic conclusion: {report.Conclusion}");
    }

    private Task CopyFtaDiagnosticReportAsync()
    {
        var reportText = string.IsNullOrWhiteSpace(ftaDiagnosticsTextBox.Text)
            ? BuildFtaDiagnosticReport(null, null).ReportText
            : ftaDiagnosticsTextBox.Text;
        Clipboard.SetText(reportText);
        AppendLog("FTA diagnostic report copied to clipboard.");
        return Task.CompletedTask;
    }

    private async Task OpenFtaSetupAndRefreshDiagnosticsAsync()
    {
        var status = await stationService.OpenSetupAsync();
        RenderNewServiceLogEntries();
        RefreshFtaDeviceSelections();
        var report = BuildFtaDiagnosticReport(status, status.ErrorMessage);
        ftaDiagnosticsTextBox.Text = report.ReportText;
        AppendLog("FTA Setup / Calibration closed or returned; diagnostics refreshed.");
    }

    private Task SaveFtaConnectionSettingsAsync()
    {
        var config = stationService.Configuration;
        if (Enum.TryParse<FtaConnectionMode>(ftaConnectionModeComboBox.SelectedItem?.ToString(), ignoreCase: true, out var mode))
        {
            config.FtaConnectionMode = mode;
        }

        config.FtaSerialPort = string.IsNullOrWhiteSpace(ftaSerialPortComboBox.SelectedItem?.ToString())
            ? null
            : ftaSerialPortComboBox.SelectedItem.ToString();
        config.ComPort = config.FtaSerialPort;

        var path = File.Exists(settingsPath) ? settingsPath : StationConfiguration.InstalledSettingsPath;
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(config, new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            }));
            settingsPath = path;
            RefreshStatusDisplay();
            AppendLog($"FTA connection settings saved to {path}. Serial settings must match FTA Setup / FTA_DLL.CFG.");
        }
        catch (Exception ex)
        {
            AppendLog($"FTA connection settings could not be saved: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private void RefreshFtaDeviceSelections()
    {
        var selectedPort = stationService.Configuration.FtaSerialPort ?? stationService.Configuration.ComPort;
        ftaConnectionModeComboBox.SelectedItem = stationService.Configuration.FtaConnectionMode.ToString();
        ftaSerialPortComboBox.Items.Clear();
        ftaSerialPortComboBox.Items.Add("");
        foreach (var port in ftaEnvironmentDiagnostics.GetComPortDiagnostics().Where(port => !string.IsNullOrWhiteSpace(port.PortName)).Select(port => port.PortName).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            ftaSerialPortComboBox.Items.Add(port);
        }

        ftaSerialPortComboBox.SelectedItem = selectedPort is not null && ftaSerialPortComboBox.Items.Contains(selectedPort)
            ? selectedPort
            : "";
    }

    private FtaConnectionDiagnosticReport BuildFtaDiagnosticReport(FtaDeviceStatus? status, string? error) =>
        FtaConnectionDiagnostics.BuildReport(stationService.Configuration, settingsPath, ftaEnvironmentDiagnostics, status, error);

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
            button.Enabled = enabled && !string.Equals(button.Tag as string, "AlwaysDisabled", StringComparison.Ordinal);
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
        SetValue("LoadedConfigPath", settingsPath);
        SetValue("StationName", config.StationName);
        SetValue("QcStationCode", config.QcStationCode ?? "");
        SetValue("WarehouseCode", config.WarehouseCode);
        SetValue("ApiBaseUrl", config.ApiBaseUrl);
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
        SetValue("FtaManualCaptureSafeMode", config.FtaManualCaptureSafeMode ? "Enabled" : "Disabled");
        SetValue("FtaFirmnessUnit", config.FtaFirmnessUnit.ToString());
        SetValue("FtaRearmTiming", $"poll {config.FtaHomePollIntervalMs} ms, delay {config.FtaManualRearmDelayMs} ms, max {config.FtaMaxHomeWaitMs} ms");
        SetValue("FtaConnectionMode", config.FtaConnectionMode.ToString());
        SetValue("FtaSerialPort", string.IsNullOrWhiteSpace(config.FtaSerialPort) ? "(not configured)" : config.FtaSerialPort);
    }

    private void RefreshSampleStatusDisplay()
    {
        selectedSampleTextBox.Text = selectedSample is null ? "(none)" : selectedSample.DisplayReceiptId;
        unsavedChangesTextBox.Text = hasUnsavedPressureChanges ? "Yes" : "No";
        sampleContextTextBox.Text = selectedSample is null
            ? "(none)"
            : $"{selectedSample.WarehouseCode} {selectedSample.RoomCode} | {selectedSample.GrowerName} | Lot {selectedSample.LotCode} | {selectedSample.VarietyCode} | Target {selectedSample.TargetSampleSize}";
    }

    private void RefreshCaptureDisplay()
    {
        if ((int)fruitNumberInput.Value != testFruitCapture.FruitNumber)
        {
            fruitNumberInput.Value = testFruitCapture.FruitNumber;
        }

        pressure1TextBox.Text = FormatPressure(testFruitCapture.Pressure1Lbs);
        pressure2TextBox.Text = FormatPressure(testFruitCapture.Pressure2Lbs);
        averagePressureTextBox.Text = FormatPressure(testFruitCapture.AveragePressureLbs);
        lastCapturedTextBox.Text = testFruitCapture.LastCapturedReading is null
            ? "(none)"
            : $"{testFruitCapture.LastCapturedReading.ReadingValueLbs:0.00} lbs ({testFruitCapture.LastCapturedReading.Source})";
        currentFruitTextBox.Text = testFruitCapture.FruitNumber.ToString();
        currentTargetTextBox.Text = testFruitCapture.CurrentTargetSlot;
        var capturedCount = testFruitCapture.Rows.Count(row => row.Pressure1Lbs is not null || row.Pressure2Lbs is not null);
        continuousStatusTextBox.Text = isContinuousCaptureRunning
            ? $"Continuous capture armed - captured {capturedCount} / {testFruitCapture.TargetFruitCount}"
            : testFruitCapture.IsSampleComplete
                ? "Sample complete"
                : string.IsNullOrWhiteSpace(continuousStatusTextBox.Text) ? $"Stopped - captured {capturedCount} / {testFruitCapture.TargetFruitCount}" : continuousStatusTextBox.Text;

        fruitPressureGrid.BeginUpdate();
        fruitPressureGrid.Items.Clear();
        foreach (var row in testFruitCapture.Rows)
        {
            var item = new ListViewItem(row.FruitNumber.ToString());
            item.SubItems.Add(FormatPressure(row.Pressure1Lbs));
            item.SubItems.Add(FormatPressure(row.Pressure2Lbs));
            item.SubItems.Add(FormatPressure(row.AveragePressureLbs));
            item.SubItems.Add(row.Status);
            if (row.FruitNumber == testFruitCapture.FruitNumber)
            {
                item.BackColor = Color.LightYellow;
            }
            fruitPressureGrid.Items.Add(item);
        }
        fruitPressureGrid.EndUpdate();

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

    private static string FormatOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "(not configured)" : value;

    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (shutdownComplete)
        {
            base.OnFormClosing(e);
            return;
        }

        e.Cancel = true;
        AppendLog("Application closing: attempting FTA cleanup.");
        try
        {
            await ShutdownFtaAsync("Application closing", closeAfterShutdown: true);
        }
        catch (Exception ex)
        {
            AppendLog($"Application closing: cleanup error ignored: {ex.Message}");
            shutdownComplete = true;
            Close();
        }
    }
}
