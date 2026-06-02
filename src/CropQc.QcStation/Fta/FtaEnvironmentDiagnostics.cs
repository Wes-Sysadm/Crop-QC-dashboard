using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace CropQc.QcStation.Fta;

public interface IFtaEnvironmentDiagnostics
{
    FtaConfigFileDiagnostics ReadConfigFile(string dllFolder);
    IReadOnlyList<string> GetAvailableComPorts();
    IReadOnlyList<string> GetHidDeviceIdsByVendorId(string vendorId);
    IReadOnlyList<FtaUsbDeviceDiagnostics> GetUsbHidDevices();
    IReadOnlyList<FtaComPortDiagnostics> GetComPortDiagnostics();
}

public sealed record FtaConfigFileDiagnostics(
    string Path,
    bool Exists,
    DateTimeOffset? LastWriteTime,
    long? Length,
    IReadOnlyList<string> VisibleComPorts);

public sealed record FtaUsbDeviceDiagnostics(
    string InstanceId,
    string FriendlyName,
    string Status,
    string Manufacturer);

public sealed record FtaComPortDiagnostics(
    string PortName,
    string FriendlyName,
    string PnpDeviceId,
    string Manufacturer,
    string Status);

public sealed class FtaEnvironmentDiagnostics : IFtaEnvironmentDiagnostics
{
    public const string FtaConfigFileName = "FTA_DLL.CFG";

    public FtaConfigFileDiagnostics ReadConfigFile(string configPathOrFolder)
    {
        var configPath = string.Equals(Path.GetFileName(configPathOrFolder), FtaConfigFileName, StringComparison.OrdinalIgnoreCase)
            ? configPathOrFolder
            : Path.Combine(configPathOrFolder, FtaConfigFileName);
        var fileInfo = new FileInfo(configPath);
        if (!fileInfo.Exists)
        {
            return new FtaConfigFileDiagnostics(configPath, false, null, null, []);
        }

        var bytes = File.ReadAllBytes(configPath);
        var visibleText = string.Join(Environment.NewLine, Encoding.Latin1.GetString(bytes), Encoding.Unicode.GetString(bytes));
        var comPorts = Regex.Matches(visibleText, @"\bCOM\d+\b", RegexOptions.IgnoreCase)
            .Select(match => match.Value.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new FtaConfigFileDiagnostics(
            configPath,
            true,
            fileInfo.LastWriteTime,
            fileInfo.Length,
            comPorts);
    }

    public IReadOnlyList<string> GetAvailableComPorts()
    {
        return GetComPortDiagnostics().Select(port => port.PortName).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public IReadOnlyList<string> GetHidDeviceIdsByVendorId(string vendorId)
    {
        return GetUsbHidDevices()
            .Where(device => device.InstanceId.Contains(vendorId, StringComparison.OrdinalIgnoreCase))
            .Select(device => device.InstanceId)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public IReadOnlyList<FtaUsbDeviceDiagnostics> GetUsbHidDevices()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        try
        {
            using var hidRoot = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\HID");
            if (hidRoot is null)
            {
                return [];
            }

            return hidRoot.GetSubKeyNames()
                .SelectMany(ReadHidDeviceInstances)
                .OrderBy(device => device.InstanceId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return [new FtaUsbDeviceDiagnostics($"Unable to read HID registry entries: {ex.Message}", "", "Error", "")];
        }
    }

    public IReadOnlyList<FtaComPortDiagnostics> GetComPortDiagnostics()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var ports = new SortedDictionary<string, FtaComPortDiagnostics>(StringComparer.OrdinalIgnoreCase);
        for (var portNumber = 1; portNumber <= 256; portNumber++)
        {
            var portName = $"COM{portNumber}";
            var targetPath = new StringBuilder(512);
            if (QueryDosDevice(portName, targetPath, targetPath.Capacity) != 0)
            {
                ports[portName] = new FtaComPortDiagnostics(portName, portName, targetPath.ToString(), "", "Present");
            }
        }

        try
        {
            using var serialRoot = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            if (serialRoot is not null)
            {
                foreach (var valueName in serialRoot.GetValueNames())
                {
                    if (serialRoot.GetValue(valueName) is not string portName || string.IsNullOrWhiteSpace(portName))
                    {
                        continue;
                    }

                    var friendlyName = valueName.Replace("\\Device\\", "", StringComparison.OrdinalIgnoreCase);
                    ports[portName] = new FtaComPortDiagnostics(portName, friendlyName, valueName, InferManufacturer(valueName), "Present");
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            ports[$"ERROR-{ports.Count + 1}"] = new FtaComPortDiagnostics("", "Unable to read serial registry entries", ex.Message, "", "Error");
        }

        return ports.Values.ToArray();
    }

    [SupportedOSPlatform("windows")]
    private static IEnumerable<FtaUsbDeviceDiagnostics> ReadHidDeviceInstances(string deviceKeyName)
    {
        using var deviceKey = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\HID\{deviceKeyName}");
        if (deviceKey is null)
        {
            yield break;
        }

        foreach (var instanceName in deviceKey.GetSubKeyNames())
        {
            using var instanceKey = deviceKey.OpenSubKey(instanceName);
            var instanceId = $@"HID\{deviceKeyName}\{instanceName}";
            var friendlyName = ReadStringValue(instanceKey, "FriendlyName")
                ?? ReadStringValue(instanceKey, "DeviceDesc")
                ?? deviceKeyName;
            yield return new FtaUsbDeviceDiagnostics(
                instanceId,
                friendlyName,
                ReadStringValue(instanceKey, "Status") ?? "Present",
                ReadStringValue(instanceKey, "Mfg") ?? "");
        }
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadStringValue(RegistryKey? key, string valueName) =>
        key?.GetValue(valueName) as string;

    private static string InferManufacturer(string value)
    {
        if (value.Contains("FTDI", StringComparison.OrdinalIgnoreCase))
        {
            return "FTDI";
        }

        if (value.Contains("Prolific", StringComparison.OrdinalIgnoreCase))
        {
            return "Prolific";
        }

        if (value.Contains("Silab", StringComparison.OrdinalIgnoreCase) || value.Contains("CP210", StringComparison.OrdinalIgnoreCase))
        {
            return "Silicon Labs";
        }

        if (value.Contains("CH340", StringComparison.OrdinalIgnoreCase) || value.Contains("CH341", StringComparison.OrdinalIgnoreCase))
        {
            return "WCH CH340/CH341";
        }

        if (value.Contains("USB", StringComparison.OrdinalIgnoreCase))
        {
            return "USB serial";
        }

        return "";
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int QueryDosDevice(string lpDeviceName, StringBuilder lpTargetPath, int ucchMax);
}
