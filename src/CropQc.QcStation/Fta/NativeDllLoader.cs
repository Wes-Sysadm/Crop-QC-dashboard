using System.Runtime.InteropServices;

namespace CropQc.QcStation.Fta;

public sealed class NativeDllLoader : INativeDllLoader
{
    public DllLoadResult TryLoad(string dllPath)
    {
        try
        {
            var handle = NativeLibrary.Load(dllPath);
            return DllLoadResult.Success(handle);
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or FileLoadException)
        {
            if (ex is BadImageFormatException || ex.Message.Contains("0x8007000B", StringComparison.OrdinalIgnoreCase))
            {
                return DllLoadResult.ArchitectureMismatch(ex.Message);
            }

            return DllLoadResult.Failed(ex.Message);
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
}
