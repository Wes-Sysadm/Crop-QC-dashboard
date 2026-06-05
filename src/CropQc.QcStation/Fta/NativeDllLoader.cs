using System.Runtime.InteropServices;

namespace CropQc.QcStation.Fta;

public sealed class NativeDllLoader : INativeDllLoader
{
    private const uint LoadWithAlteredSearchPath = 0x00000008;

    public DllLoadResult TryLoad(string dllPath)
    {
        var fullPath = Path.GetFullPath(dllPath);
        var dllSearchDirectory = Path.GetDirectoryName(fullPath);
        var currentDirectoryBeforeLoad = Environment.CurrentDirectory;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (!string.IsNullOrWhiteSpace(dllSearchDirectory))
                {
                    SetDllDirectory(dllSearchDirectory);
                }

                var handle = LoadLibraryEx(fullPath, IntPtr.Zero, LoadWithAlteredSearchPath);
                if (handle == IntPtr.Zero)
                {
                    var error = Marshal.GetLastWin32Error();
                    var message = Marshal.GetPInvokeErrorMessage(error);
                    return DllLoadResult.Failed(
                        $"{message} (Win32 error {error})",
                        "Win32 LoadLibraryEx",
                        error,
                        currentDirectoryBeforeLoad,
                        Environment.CurrentDirectory,
                        dllSearchDirectory,
                        fullPath);
                }

                return DllLoadResult.Success(handle, currentDirectoryBeforeLoad, Environment.CurrentDirectory, dllSearchDirectory, fullPath);
            }

            var nativeHandle = NativeLibrary.Load(fullPath);
            return DllLoadResult.Success(nativeHandle, currentDirectoryBeforeLoad, Environment.CurrentDirectory, dllSearchDirectory, fullPath);
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or FileLoadException)
        {
            if (ex is BadImageFormatException || ex.Message.Contains("0x8007000B", StringComparison.OrdinalIgnoreCase))
            {
                return DllLoadResult.ArchitectureMismatch(
                    ex.Message,
                    ex.GetType().Name,
                    ex.HResult,
                    currentDirectoryBeforeLoad,
                    Environment.CurrentDirectory,
                    dllSearchDirectory,
                    fullPath);
            }

            return DllLoadResult.Failed(
                ex.Message,
                ex.GetType().Name,
                ex.HResult,
                currentDirectoryBeforeLoad,
                Environment.CurrentDirectory,
                dllSearchDirectory,
                fullPath);
        }
    }

    public bool TryGetExport(IntPtr nativeLibraryHandle, string exportName, Type delegateType, out Delegate? nativeDelegate, out string? errorMessage)
    {
        try
        {
            var export = NativeLibrary.GetExport(nativeLibraryHandle, exportName);
            nativeDelegate = Marshal.GetDelegateForFunctionPointer(export, delegateType);
            errorMessage = null;
            return true;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException or ArgumentException)
        {
            nativeDelegate = null;
            errorMessage = ex.Message;
            return false;
        }
    }

    public void Free(IntPtr nativeLibraryHandle)
    {
        if (nativeLibraryHandle != IntPtr.Zero)
        {
            NativeLibrary.Free(nativeLibraryHandle);
        }
    }

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetDllDirectory(string? lpPathName);

    [DllImport("kernel32", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibraryEx(string lpLibFileName, IntPtr hFile, uint dwFlags);
}
