using System.Diagnostics;

namespace CropQc.QcStation.Fta;

public enum PeArchitecture
{
    X86,
    X64,
    Arm64,
    AnyCpu,
    Unknown,
    Invalid,
    Missing
}

public sealed record PeFileInspection(
    string Path,
    bool Exists,
    PeArchitecture Architecture,
    long? FileSizeBytes,
    DateTimeOffset? LastModifiedAt,
    string? Version,
    string? ErrorMessage)
{
    public bool IsValidWindowsDll => Exists && Architecture is PeArchitecture.X86 or PeArchitecture.X64 or PeArchitecture.Arm64 or PeArchitecture.AnyCpu or PeArchitecture.Unknown;
}

public static class PeFileInspector
{
    private const ushort ImageFileMachineI386 = 0x014c;
    private const ushort ImageFileMachineAmd64 = 0x8664;
    private const ushort ImageFileMachineArm64 = 0xaa64;
    private const ushort ImageNtOptionalHdr32Magic = 0x10b;
    private const ushort ImageNtOptionalHdr64Magic = 0x20b;
    private const ushort ImageDllCharacteristicsDynamicBase = 0x0040;
    private const uint ComDescriptorDirectoryIndex = 14;

    public static PeFileInspection Inspect(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new PeFileInspection(path, false, PeArchitecture.Missing, null, null, null, null);
            }

            var info = new FileInfo(path);
            var version = FileVersionInfo.GetVersionInfo(path).FileVersion;
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            if (stream.Length < 0x40 || reader.ReadUInt16() != 0x5a4d)
            {
                return Invalid(path, info, version, "File exists but does not look like a valid Windows DLL.");
            }

            stream.Position = 0x3c;
            var peOffset = reader.ReadInt32();
            if (peOffset <= 0 || peOffset > stream.Length - 24)
            {
                return Invalid(path, info, version, "File exists but does not look like a valid Windows DLL.");
            }

            stream.Position = peOffset;
            if (reader.ReadUInt32() != 0x00004550)
            {
                return Invalid(path, info, version, "File exists but does not look like a valid Windows DLL.");
            }

            var machine = reader.ReadUInt16();
            _ = reader.ReadUInt16(); // section count
            _ = reader.ReadUInt32(); // timestamp
            _ = reader.ReadUInt32(); // symbol table
            _ = reader.ReadUInt32(); // symbols
            var optionalHeaderSize = reader.ReadUInt16();
            _ = reader.ReadUInt16(); // characteristics

            var optionalHeaderStart = stream.Position;
            if (optionalHeaderSize < 2 || optionalHeaderStart + optionalHeaderSize > stream.Length)
            {
                return new PeFileInspection(path, true, MapMachine(machine), info.Length, info.LastWriteTimeUtc, version, "PE header is present but optional header could not be read.");
            }

            var magic = reader.ReadUInt16();
            var architecture = MapMachine(machine);
            if (architecture == PeArchitecture.X86 && IsManagedAnyCpu(stream, reader, optionalHeaderStart, optionalHeaderSize, magic))
            {
                architecture = PeArchitecture.AnyCpu;
            }
            else if (architecture == PeArchitecture.Unknown && magic == ImageNtOptionalHdr64Magic)
            {
                architecture = PeArchitecture.X64;
            }

            return new PeFileInspection(path, true, architecture, info.Length, info.LastWriteTimeUtc, version, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new PeFileInspection(path, File.Exists(path), PeArchitecture.Invalid, null, null, null, ex.Message);
        }
    }

    public static string Format(PeFileInspection inspection)
    {
        if (!inspection.Exists)
        {
            return $"{inspection.Path}: exists No";
        }

        var modified = inspection.LastModifiedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "(unknown)";
        var version = string.IsNullOrWhiteSpace(inspection.Version) ? "(none)" : inspection.Version;
        var error = string.IsNullOrWhiteSpace(inspection.ErrorMessage) ? "" : $" | {inspection.ErrorMessage}";
        return $"{inspection.Path}: exists Yes | architecture {FormatArchitecture(inspection.Architecture)} | size {inspection.FileSizeBytes?.ToString() ?? "(unknown)"} bytes | modified {modified} | version {version}{error}";
    }

    public static string FormatArchitecture(PeArchitecture architecture) =>
        architecture switch
        {
            PeArchitecture.X86 => "x86",
            PeArchitecture.X64 => "x64",
            PeArchitecture.Arm64 => "arm64",
            PeArchitecture.AnyCpu => "AnyCPU",
            PeArchitecture.Invalid => "invalid",
            PeArchitecture.Missing => "missing",
            _ => "unknown"
        };

    private static PeFileInspection Invalid(string path, FileInfo info, string? version, string message) =>
        new(path, true, PeArchitecture.Invalid, info.Length, info.LastWriteTimeUtc, version, message);

    private static PeArchitecture MapMachine(ushort machine) =>
        machine switch
        {
            ImageFileMachineI386 => PeArchitecture.X86,
            ImageFileMachineAmd64 => PeArchitecture.X64,
            ImageFileMachineArm64 => PeArchitecture.Arm64,
            _ => PeArchitecture.Unknown
        };

    private static bool IsManagedAnyCpu(Stream stream, BinaryReader reader, long optionalHeaderStart, ushort optionalHeaderSize, ushort magic)
    {
        if (magic != ImageNtOptionalHdr32Magic || optionalHeaderSize < 224)
        {
            return false;
        }

        stream.Position = optionalHeaderStart + 66;
        var dllCharacteristics = reader.ReadUInt16();
        stream.Position = optionalHeaderStart + 92 + (ComDescriptorDirectoryIndex * 8);
        var comDescriptorRva = reader.ReadUInt32();

        // For the diagnostic report, this only distinguishes managed AnyCPU files from native x86.
        return comDescriptorRva != 0 && (dllCharacteristics & ImageDllCharacteristicsDynamicBase) != 0;
    }
}
