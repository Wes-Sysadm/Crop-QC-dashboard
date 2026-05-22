using System.Runtime.InteropServices;

namespace CropQc.QcStation.Fta;

public sealed class NativeDllLoader : INativeDllLoader
{
    public DllLoadResult TryLoad(string dllPath)
    {
        try
        {
            var handle = NativeLibrary.Load(dllPath);
            NativeLibrary.Free(handle);
            return DllLoadResult.Success();
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
}
