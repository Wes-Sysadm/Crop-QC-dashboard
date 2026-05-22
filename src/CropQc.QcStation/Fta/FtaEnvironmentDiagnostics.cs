using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CropQc.QcStation.Fta;

public interface IFtaEnvironmentDiagnostics
{
    FtaConfigFileDiagnostics ReadConfigFile(string dllFolder);
    IReadOnlyList<string> GetAvailableComPorts();
    IReadOnlyList<string> GetHidDeviceIdsByVendorId(string vendorId);
}

public sealed record FtaConfigFileDiagnostics(
    string Path,
    bool Exists,
    DateTimeOffset? LastWriteTime,
    long? Length,
    IReadOnlyList<string> VisibleComPorts);

public sealed class FtaEnvironmentDiagnostics : IFtaEnvironmentDiagnostics
{
    public const string FtaConfigFileName = "FTA_DLL.CFG";

    public FtaConfigFileDiagnostics ReadConfigFile(string dllFolder)
    {
        var configPath = Path.Combine(dllFolder, FtaConfigFileName);
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
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var ports = new List<string>();
        for (var portNumber = 1; portNumber <= 256; portNumber++)
        {
            var portName = $"COM{portNumber}";
            var targetPath = new StringBuilder(512);
            if (QueryDosDevice(portName, targetPath, targetPath.Capacity) != 0)
            {
                ports.Add(portName);
            }
        }

        return ports;
    }

    public IReadOnlyList<string> GetHidDeviceIdsByVendorId(string vendorId)
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
                .Where(name => name.Contains(vendorId, StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            return [$"Unable to read HID registry entries: {ex.Message}"];
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int QueryDosDevice(string lpDeviceName, StringBuilder lpTargetPath, int ucchMax);
}
