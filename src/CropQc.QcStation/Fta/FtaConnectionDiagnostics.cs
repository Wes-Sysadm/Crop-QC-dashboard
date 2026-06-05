using System.Runtime.InteropServices;
using System.Text;
using System.Diagnostics;

namespace CropQc.QcStation.Fta;

public sealed record FtaConnectionCandidate(
    string Type,
    string Identifier,
    string FriendlyName,
    string Status,
    bool IsLikelyFta,
    string RecommendedAction);

public sealed record FtaConnectionDiagnosticReport(
    IReadOnlyList<FtaConnectionCandidate> Candidates,
    string RecommendedMode,
    string Conclusion,
    string ReportText);

public static class FtaConnectionDiagnostics
{
    public const string KnownFtaVendorId = "VID_6017";
    public const string KnownFtaProductId = "PID_3430";

    public static FtaConnectionDiagnosticReport BuildReport(
        StationConfiguration configuration,
        string loadedConfigPath,
        IFtaEnvironmentDiagnostics environmentDiagnostics,
        FtaDeviceStatus? ftaStatus = null,
        string? lastError = null)
    {
        var config = environmentDiagnostics.ReadConfigFile(ResolveConfigPath(configuration));
        var candidates = DiscoverCandidates(configuration, environmentDiagnostics, config);
        var recommendedMode = RecommendMode(configuration, candidates, config);
        var conclusion = BuildConclusion(configuration, candidates, ftaStatus, lastError);
        var text = BuildReportText(configuration, loadedConfigPath, config, candidates, recommendedMode, conclusion, ftaStatus, lastError);
        return new FtaConnectionDiagnosticReport(candidates, recommendedMode, conclusion, text);
    }

    public static IReadOnlyList<FtaConnectionCandidate> DiscoverCandidates(
        StationConfiguration configuration,
        IFtaEnvironmentDiagnostics environmentDiagnostics,
        FtaConfigFileDiagnostics? config = null)
    {
        var candidates = new List<FtaConnectionCandidate>();
        foreach (var device in environmentDiagnostics.GetUsbHidDevices())
        {
            var likelyFta = IsKnownFtaUsbHid(device.InstanceId);
            candidates.Add(new FtaConnectionCandidate(
                "USB HID",
                device.InstanceId,
                device.FriendlyName,
                device.Status,
                likelyFta,
                likelyFta
                    ? "Likely direct USB FTA. If the DLL does not respond, open Calibration/FTA Setup or FTAWin and confirm the interface setting."
                    : "USB HID device. Usually not the FTA unless vendor/device ID matches VID_6017/PID_3430."));
        }

        var comPorts = environmentDiagnostics.GetComPortDiagnostics();
        foreach (var port in comPorts)
        {
            var isUsbSerial = IsUsbToSerialCandidate(port);
            candidates.Add(new FtaConnectionCandidate(
                isUsbSerial ? "USB-to-Serial" : "Serial COM",
                port.PortName,
                string.IsNullOrWhiteSpace(port.FriendlyName) ? port.PnpDeviceId : port.FriendlyName,
                port.Status,
                isUsbSerial,
                isUsbSerial
                    ? "Possible USB-to-serial FTA adapter. Confirm this COM port in FTA Setup/Calibration and verify the adapter driver."
                    : "Native serial COM port. Use FTA Setup/Calibration to select this port if the FTA is connected here."));
        }

        config ??= environmentDiagnostics.ReadConfigFile(ResolveConfigPath(configuration));
        foreach (var port in config.VisibleComPorts)
        {
            var exists = comPorts.Any(x => string.Equals(x.PortName, port, StringComparison.OrdinalIgnoreCase));
            candidates.Add(new FtaConnectionCandidate(
                "Configured COM",
                port,
                config.Path,
                exists ? "Available" : "Not currently available",
                exists,
                exists
                    ? "FTA_DLL.CFG references this COM port and Windows reports it. This is a good serial candidate."
                    : "FTA_DLL.CFG references this COM port, but Windows does not currently report it. Check cable, adapter, driver, or update FTA Setup."));
        }

        return candidates
            .OrderByDescending(x => x.IsLikelyFta)
            .ThenBy(x => x.Type, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.Identifier, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool IsKnownFtaUsbHid(string value) =>
        value.Contains(KnownFtaVendorId, StringComparison.OrdinalIgnoreCase);

    public static bool IsUsbToSerialCandidate(FtaComPortDiagnostics port)
    {
        var value = string.Join(" ", port.PortName, port.FriendlyName, port.PnpDeviceId, port.Manufacturer);
        string[] markers =
        [
            "FTDI",
            "Prolific",
            "Silicon Labs",
            "CP210",
            "CH340",
            "CH341",
            "USB Serial Device",
            "Communications Port",
            "USB-to-Serial",
            "USB Serial",
            "Serial Converter"
        ];
        return markers.Any(marker => value.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    public static string RecommendMode(
        StationConfiguration configuration,
        IReadOnlyList<FtaConnectionCandidate> candidates,
        FtaConfigFileDiagnostics config)
    {
        if (configuration.FtaConnectionMode != FtaConnectionMode.Auto)
        {
            return $"{configuration.FtaConnectionMode} (manual override from config)";
        }

        if (candidates.Any(x => x.Type == "USB HID" && x.IsLikelyFta))
        {
            return "UsbHid: Windows sees known FTA USB HID VID_6017/PID_3430.";
        }

        var availableConfiguredCom = candidates.Where(x => x.Type == "Configured COM" && x.IsLikelyFta).ToArray();
        if (availableConfiguredCom.Length == 1)
        {
            return $"Serial: {availableConfiguredCom[0].Identifier} is referenced in FTA_DLL.CFG and available.";
        }

        var usbSerial = candidates.Where(x => x.Type == "USB-to-Serial" && x.IsLikelyFta).ToArray();
        return usbSerial.Length switch
        {
            0 => "Auto: no known USB HID or serial candidate found.",
            1 => $"UsbSerial: {usbSerial[0].Identifier} appears to be a USB-to-serial adapter.",
            _ => "Auto: multiple USB-to-serial candidates found. Do not auto-select; choose the correct port in FTA Setup/Calibration."
        };
    }

    public static string BuildConclusion(
        StationConfiguration configuration,
        IReadOnlyList<FtaConnectionCandidate> candidates,
        FtaDeviceStatus? ftaStatus,
        string? lastError)
    {
        var configuredDll = PeFileInspector.Inspect(Path.Combine(ResolveDllFolder(configuration), ResolveDllFileName(configuration)));
        var configuredBorland = PeFileInspector.Inspect(Path.Combine(ResolveDllFolder(configuration), FtaDllPressureReader.BorlandMemoryManagerFileName));
        var borlandCandidates = InspectBorlandCandidates(configuration);
        var hasIncorrectFormatError = !string.IsNullOrWhiteSpace(lastError)
            && (lastError.Contains("incorrect format", StringComparison.OrdinalIgnoreCase)
                || lastError.Contains("0x8007000B", StringComparison.OrdinalIgnoreCase));

        if (!File.Exists(Path.Combine(ResolveDllFolder(configuration), ResolveDllFileName(configuration))))
        {
            return "FTA DLL missing. Install FTADLL.exe from Admin -> Downloads, then rerun diagnostics.";
        }

        if (RuntimeInformation.ProcessArchitecture != Architecture.X86)
        {
            return "FTA_DLL.dll requires x86. Install/run the x86 QC Station app.";
        }

        if (ftaStatus?.IsConnected == true)
        {
            return "FTA ready.";
        }

        if (hasIncorrectFormatError && RuntimeInformation.ProcessArchitecture == Architecture.X86)
        {
            return BuildIncorrectFormatGuidance(RuntimeInformation.ProcessArchitecture, configuredDll, configuredBorland, candidates.Any(x => x.Type == "Configured COM" && x.IsLikelyFta), borlandCandidates);
        }

        if (hasIncorrectFormatError)
        {
            return "FTA DLL failed to load. This is likely x86/x64 mismatch or missing dependency.";
        }

        if (!string.IsNullOrWhiteSpace(lastError) && candidates.Any(x => x.Type == "Configured COM" && x.IsLikelyFta))
        {
            return "Connection candidate looks valid: configured COM port is present. DLL load must be fixed before FTAInit can test the FTA.";
        }

        if (candidates.Any(x => x.Type == "USB HID" && x.IsLikelyFta))
        {
            return "FTA USB device detected, but DLL did not respond. Open FTA Setup / Calibration or FTAWin and confirm the interface setting.";
        }

        if (candidates.Any(x => x.Type is "USB-to-Serial" or "Serial COM" or "Configured COM"))
        {
            return "COM port detected, but FTA DLL did not respond. Confirm the correct COM port in FTA Setup / Calibration and verify adapter drivers.";
        }

        return "No USB HID or COM connection was detected. Check cable, power, Device Manager, and FTADLL/driver installation.";
    }

    public static string BuildReportText(
        StationConfiguration configuration,
        string loadedConfigPath,
        FtaConfigFileDiagnostics config,
        IReadOnlyList<FtaConnectionCandidate> candidates,
        string recommendedMode,
        string conclusion,
        FtaDeviceStatus? ftaStatus,
        string? lastError)
    {
        var builder = new StringBuilder();
        var appBaseDirectory = AppContext.BaseDirectory;
        var processMainModulePath = GetProcessMainModulePath();
        builder.AppendLine("Crop QC Station FTA Hardware Diagnostic Report");
        builder.AppendLine();
        builder.AppendLine("Section 1: App/runtime");
        builder.AppendLine($"Process architecture: {RuntimeInformation.ProcessArchitecture}");
        builder.AppendLine($"OS architecture: {RuntimeInformation.OSArchitecture}");
        builder.AppendLine($"App install path: {appBaseDirectory}");
        builder.AppendLine($"AppContext.BaseDirectory: {appBaseDirectory}");
        builder.AppendLine($"Process main module path: {FormatOptional(processMainModulePath)}");
        builder.AppendLine($"Loaded config path: {loadedConfigPath}");
        builder.AppendLine($"Running from Program Files: {YesNo(IsProgramFilesPath(appBaseDirectory) || IsProgramFilesPath(processMainModulePath))}");
        builder.AppendLine($".NET runtime: {RuntimeInformation.FrameworkDescription}");
        builder.AppendLine();
        builder.AppendLine("Section 2: Station config");
        builder.AppendLine($"StationName: {configuration.StationName}");
        builder.AppendLine($"QcStationCode: {configuration.QcStationCode}");
        builder.AppendLine($"WarehouseCode: {configuration.WarehouseCode}");
        builder.AppendLine($"ApiBaseUrl: {configuration.ApiBaseUrl}");
        builder.AppendLine($"FtaMode: {configuration.FtaMode}");
        builder.AppendLine($"FtaConnectionMode: {configuration.FtaConnectionMode}");
        builder.AppendLine($"FtaDllPath: {configuration.FtaDllPath}");
        builder.AppendLine($"FtaDllFileName: {configuration.FtaDllFileName}");
        builder.AppendLine($"FtaInitializationMode: {configuration.FtaInitializationMode}");
        builder.AppendLine($"FtaConfigPath: {configuration.FtaConfigPath}");
        builder.AppendLine($"FtaWorkingDirectory: {configuration.FtaWorkingDirectory}");
        builder.AppendLine($"FtaFirmnessUnit: {configuration.FtaFirmnessUnit}");
        builder.AppendLine($"FtaSerialPort: {configuration.FtaSerialPort}");
        builder.AppendLine($"FtaSerialBaudRate: {configuration.FtaSerialBaudRate}");
        builder.AppendLine($"FtaSerialDataBits: {configuration.FtaSerialDataBits}");
        builder.AppendLine($"FtaSerialParity: {configuration.FtaSerialParity}");
        builder.AppendLine($"FtaSerialStopBits: {configuration.FtaSerialStopBits}");
        builder.AppendLine();
        builder.AppendLine("Section 3: Required files");
        var configuredDllFolder = ResolveDllFolder(configuration);
        var configuredMainDllPath = Path.Combine(configuredDllFolder, ResolveDllFileName(configuration));
        var configuredBorlandPath = Path.Combine(configuredDllFolder, FtaDllPressureReader.BorlandMemoryManagerFileName);
        var sysWow64MainDllPath = @"C:\Windows\SysWOW64\FTA_DLL.dll";
        var sysWow64BorlandPath = @"C:\Windows\SysWOW64\borlndmm.dll";
        var alternateInstalledMainDllPath = @"C:\Program Files\FTADLL\FTA_DLL.dll";
        var alternateInstalledBorlandPath = @"C:\Program Files\FTADLL\borlndmm.dll";
        AddFileLine(builder, sysWow64MainDllPath);
        AddFileLine(builder, sysWow64BorlandPath);
        AddDirectoryLine(builder, ResolveDllFolder(configuration), "Configured DLL folder");
        AddFileLine(builder, configuredMainDllPath, "Configured main DLL");
        AddFileLine(builder, ResolveConfigPath(configuration), "Configured FTA config file");
        AddFileLine(builder, alternateInstalledMainDllPath);
        AddFileLine(builder, @"C:\Program Files\FTADLL\FTA_DLL.CFG");
        AddDirectoryLine(builder, @"C:\Program Files (x86)\FTAWin", "FTAWin working folder");
        builder.AppendLine();
        builder.AppendLine("Section 3b: DLL PE architecture/details");
        AddPeLine(builder, configuredMainDllPath, "Configured main DLL");
        AddPeLine(builder, configuredBorlandPath, "Configured borlndmm.dll");
        AddPeLine(builder, sysWow64MainDllPath, "SysWOW64 FTA_DLL.dll");
        AddPeLine(builder, sysWow64BorlandPath, "SysWOW64 borlndmm.dll");
        AddPeLine(builder, alternateInstalledMainDllPath, "Program Files FTADLL FTA_DLL.dll");
        AddPeLine(builder, alternateInstalledBorlandPath, "Program Files FTADLL borlndmm.dll");
        foreach (var vendorDll in EnumerateVendorDlls(configuration).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            AddPeLine(builder, vendorDll, "Vendor DLL");
        }
        builder.AppendLine(CompareDllCopies(sysWow64MainDllPath, alternateInstalledMainDllPath));
        builder.AppendLine(BuildBorlandDependencyGuidance(configuredMainDllPath, configuredBorlandPath, InspectBorlandCandidates(configuration)));
        builder.AppendLine();
        builder.AppendLine("Section 4: DLL load/status check");
        builder.AppendLine(ftaStatus is null ? "Not run yet." : ftaStatus.StatusMessage);
        builder.AppendLine($"Last error: {FormatOptional(lastError ?? ftaStatus?.ErrorMessage)}");
        builder.AppendLine("On 64-bit Windows, C:\\Windows\\SysWOW64 is the 32-bit system folder. C:\\Windows\\System32 is the 64-bit system folder.");
        builder.AppendLine("If an x86 QC Station loads a dependency from System32, check FtaDllPath and the vendor installer folders.");
        builder.AppendLine();
        builder.AppendLine("Section 5/6: USB HID, serial, USB-to-serial, and configured COM candidates");
        foreach (var candidate in candidates)
        {
            builder.AppendLine($"{candidate.Type} | {candidate.Identifier} | {candidate.FriendlyName} | {candidate.Status} | Likely FTA: {YesNo(candidate.IsLikelyFta)} | {candidate.RecommendedAction}");
        }
        if (candidates.Count == 0)
        {
            builder.AppendLine("(none)");
        }
        builder.AppendLine($"FTA_DLL.CFG path: {config.Path}");
        builder.AppendLine($"FTA_DLL.CFG exists: {YesNo(config.Exists)}");
        builder.AppendLine($"FTA_DLL.CFG visible COM strings: {FormatList(config.VisibleComPorts)}");
        builder.AppendLine();
        builder.AppendLine("Section 7: FTA response/status");
        builder.AppendLine(ftaStatus is null ? "Run Full FTA Diagnostic or Initialize to read FTAStatus/FTABitStatus." : ftaStatus.StatusMessage);
        builder.AppendLine();
        builder.AppendLine("Section 8: Plain-English conclusion");
        builder.AppendLine($"Recommended connection mode: {recommendedMode}");
        builder.AppendLine(conclusion);
        return builder.ToString();
    }

    public static string BuildIncorrectFormatGuidance(
        Architecture processArchitecture,
        PeFileInspection configuredDll,
        PeFileInspection configuredBorland,
        bool hasAvailableConfiguredCom,
        IReadOnlyList<PeFileInspection>? borlandCandidates = null)
    {
        if (processArchitecture != Architecture.X86)
        {
            return "FTA DLL failed to load. This is likely x86/x64 mismatch or missing dependency.";
        }

        var guidance = new List<string>
        {
            "QC Station is already running x86. The DLL load failure is likely caused by the FTA DLL file or one of its dependencies being the wrong architecture, invalid, or loaded from the wrong folder."
        };

        if (configuredDll.Exists && configuredDll.Architecture != PeArchitecture.X86)
        {
            guidance.Add("Configured FTA_DLL.dll is not x86. Use the 32-bit vendor FTA_DLL.dll.");
        }
        else
        {
            guidance.Add("Confirm FTA_DLL.dll PE architecture, borlndmm.dll PE architecture, dependency search path, and which vendor DLL copy is correct.");
        }

        if (configuredDll.Architecture == PeArchitecture.X86 && configuredBorland.Exists && configuredBorland.Architecture == PeArchitecture.X64)
        {
            guidance.Add("FTA_DLL.dll is 32-bit, but borlndmm.dll is 64-bit. Install/copy the vendor 32-bit borlndmm.dll next to FTA_DLL.dll.");
        }
        else if (configuredBorland.Exists && configuredBorland.Architecture != PeArchitecture.X86)
        {
            guidance.Add("borlndmm.dll is not x86 or is invalid. Reinstall FTADLL.exe or replace with the vendor 32-bit borlndmm.dll.");
        }

        var x86Borland = borlandCandidates?.FirstOrDefault(candidate => candidate.Architecture == PeArchitecture.X86);
        if (x86Borland is not null && !string.Equals(Path.GetDirectoryName(x86Borland.Path), Path.GetDirectoryName(configuredDll.Path), StringComparison.OrdinalIgnoreCase))
        {
            guidance.Add($"An x86 borlndmm.dll was found at {x86Borland.Path}. Copy it next to {configuredDll.Path}.");
        }

        if (configuredDll.Architecture == PeArchitecture.X86 && !configuredBorland.Exists)
        {
            guidance.Add($"FTA_DLL.dll is 32-bit, but borlndmm.dll is missing next to it. Install/copy the vendor 32-bit borlndmm.dll next to {configuredDll.Path}.");
        }

        if (configuredDll.Architecture == PeArchitecture.Invalid || configuredBorland.Architecture == PeArchitecture.Invalid)
        {
            guidance.Add("One vendor file exists but does not look like a valid Windows DLL.");
        }

        if (hasAvailableConfiguredCom)
        {
            guidance.Add("Connection candidate looks valid: configured COM port is present. DLL load must be fixed before FTAInit can test the FTA.");
        }

        return string.Join(" ", guidance);
    }

    private static string ResolveDllFolder(StationConfiguration configuration) =>
        string.IsNullOrWhiteSpace(configuration.FtaDllPath)
            ? AppContext.BaseDirectory
            : Path.GetFullPath(configuration.FtaDllPath);

    private static string ResolveDllFileName(StationConfiguration configuration) =>
        string.IsNullOrWhiteSpace(configuration.FtaDllFileName) ? FtaDllPressureReader.DefaultFtaDllFileName : configuration.FtaDllFileName.Trim();

    private static string ResolveConfigPath(StationConfiguration configuration) =>
        string.IsNullOrWhiteSpace(configuration.FtaConfigPath)
            ? Path.Combine(ResolveDllFolder(configuration), FtaEnvironmentDiagnostics.FtaConfigFileName)
            : Path.GetFullPath(configuration.FtaConfigPath);

    private static void AddFileLine(StringBuilder builder, string path, string? label = null) =>
        builder.AppendLine($"{label ?? path}: {(File.Exists(path) ? "Yes" : "No")} ({path})");

    private static void AddDirectoryLine(StringBuilder builder, string path, string label) =>
        builder.AppendLine($"{label}: {(Directory.Exists(path) ? "Yes" : "No")} ({path})");

    public static bool IsProgramFilesPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        return StartsWith(path, programFiles)
            || StartsWith(path, programFilesX86)
            || StartsWith(path, @"C:\Program Files")
            || StartsWith(path, @"C:\Program Files (x86)");
    }

    private static bool StartsWith(string path, string directory) =>
        !string.IsNullOrWhiteSpace(directory)
        && Path.GetFullPath(path).StartsWith(Path.GetFullPath(directory), StringComparison.OrdinalIgnoreCase);

    private static string YesNo(bool value) => value ? "Yes" : "No";

    private static string FormatOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "(none)" : value;

    private static string FormatList(IReadOnlyList<string> values) =>
        values.Count == 0 ? "(none)" : string.Join(", ", values);

    private static void AddPeLine(StringBuilder builder, string path, string label)
    {
        var inspection = PeFileInspector.Inspect(path);
        builder.AppendLine($"{label}: {PeFileInspector.Format(inspection)}");
        if (inspection.Exists && inspection.Architecture == PeArchitecture.Invalid)
        {
            builder.AppendLine($"  Guidance: {inspection.ErrorMessage ?? "File exists but does not look like a valid Windows DLL."}");
        }
        else if (label.Contains("FTA_DLL", StringComparison.OrdinalIgnoreCase) || label.Contains("main DLL", StringComparison.OrdinalIgnoreCase))
        {
            if (inspection.Exists && inspection.Architecture != PeArchitecture.X86)
            {
                builder.AppendLine("  Guidance: Configured FTA_DLL.dll is not x86. Use the 32-bit vendor FTA_DLL.dll.");
            }
        }
        else if (label.Contains("borlndmm", StringComparison.OrdinalIgnoreCase))
        {
            if (inspection.Exists && inspection.Architecture != PeArchitecture.X86)
            {
                builder.AppendLine("  Guidance: borlndmm.dll is not x86 or is invalid. Reinstall FTADLL.exe or replace with the vendor 32-bit borlndmm.dll.");
            }
        }
    }

    private static IReadOnlyList<PeFileInspection> InspectBorlandCandidates(StationConfiguration configuration)
    {
        var paths = new[]
        {
            Path.Combine(ResolveDllFolder(configuration), FtaDllPressureReader.BorlandMemoryManagerFileName),
            @"C:\Windows\SysWOW64\borlndmm.dll",
            @"C:\Program Files\FTADLL\borlndmm.dll",
            @"C:\Program Files (x86)\FTAWin\borlndmm.dll"
        };

        return paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(PeFileInspector.Inspect)
            .ToArray();
    }

    private static string BuildBorlandDependencyGuidance(string mainDllPath, string borlandPath, IReadOnlyList<PeFileInspection> borlandCandidates)
    {
        var mainDll = PeFileInspector.Inspect(mainDllPath);
        var configuredBorland = PeFileInspector.Inspect(borlandPath);
        var x86Borland = borlandCandidates.FirstOrDefault(candidate => candidate.Architecture == PeArchitecture.X86);

        if (mainDll.Architecture == PeArchitecture.X86 && configuredBorland.Architecture == PeArchitecture.X64)
        {
            var guidance = "Dependency guidance: FTA_DLL.dll is 32-bit, but borlndmm.dll is 64-bit. Install/copy the vendor 32-bit borlndmm.dll next to FTA_DLL.dll.";
            if (x86Borland is not null)
            {
                guidance += $" x86 borlndmm.dll candidate found at {x86Borland.Path}; copy it next to {mainDllPath}.";
            }
            return guidance;
        }

        if (mainDll.Architecture == PeArchitecture.X86 && !configuredBorland.Exists)
        {
            var guidance = $"Dependency guidance: FTA_DLL.dll is 32-bit, but borlndmm.dll is missing next to it. Install/copy the vendor 32-bit borlndmm.dll next to {mainDllPath}.";
            if (x86Borland is not null)
            {
                guidance += $" x86 borlndmm.dll candidate found at {x86Borland.Path}.";
            }
            return guidance;
        }

        if (x86Borland is not null)
        {
            return $"Dependency guidance: x86 borlndmm.dll candidate found at {x86Borland.Path}. If DLL loading still fails, copy it next to {mainDllPath}.";
        }

        return "Dependency guidance: no x86 borlndmm.dll candidate was found in the configured DLL folder, SysWOW64, Program Files\\FTADLL, or FTAWin folder.";
    }

    private static IEnumerable<string> EnumerateVendorDlls(StationConfiguration configuration)
    {
        var folders = new[]
        {
            ResolveDllFolder(configuration),
            @"C:\Windows\SysWOW64",
            @"C:\Program Files\FTADLL",
            @"C:\Program Files (x86)\FTAWin"
        };

        foreach (var folder in folders.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(folder, "*.dll", SearchOption.TopDirectoryOnly)
                    .Where(path => Path.GetFileName(path).Contains("FTA", StringComparison.OrdinalIgnoreCase)
                        || Path.GetFileName(path).Equals(FtaDllPressureReader.BorlandMemoryManagerFileName, StringComparison.OrdinalIgnoreCase));
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }
        }
    }

    private static string CompareDllCopies(string sysWow64Path, string programFilesPath)
    {
        var sysWow64 = PeFileInspector.Inspect(sysWow64Path);
        var programFiles = PeFileInspector.Inspect(programFilesPath);
        var builder = new StringBuilder("DLL copy comparison: ");
        builder.Append($"SysWOW64 architecture {PeFileInspector.FormatArchitecture(sysWow64.Architecture)}, size {sysWow64.FileSizeBytes?.ToString() ?? "(missing)"}, modified {FormatDate(sysWow64.LastModifiedAt)}; ");
        builder.Append($"Program Files FTADLL architecture {PeFileInspector.FormatArchitecture(programFiles.Architecture)}, size {programFiles.FileSizeBytes?.ToString() ?? "(missing)"}, modified {FormatDate(programFiles.LastModifiedAt)}.");

        if (sysWow64.Architecture == PeArchitecture.X86 && programFiles.Exists && programFiles.Architecture != PeArchitecture.X86)
        {
            builder.Append(" Recommendation: use C:\\Windows\\SysWOW64 as FtaDllPath because that copy appears to be the valid x86 DLL.");
        }
        else if (programFiles.Architecture == PeArchitecture.X86 && sysWow64.Exists && sysWow64.Architecture != PeArchitecture.X86)
        {
            builder.Append(" Recommendation: switch FtaDllPath to C:\\Program Files\\FTADLL because that copy appears to be the valid x86 DLL.");
        }

        return builder.ToString();
    }

    private static string FormatDate(DateTimeOffset? value) =>
        value is null ? "(missing)" : value.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz");

    private static string? GetProcessMainModulePath()
    {
        try
        {
            return Process.GetCurrentProcess().MainModule?.FileName;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
