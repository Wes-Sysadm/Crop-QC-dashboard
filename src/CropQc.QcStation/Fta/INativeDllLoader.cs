namespace CropQc.QcStation.Fta;

public interface INativeDllLoader
{
    DllLoadResult TryLoad(string dllPath);
    bool TryGetExport(IntPtr nativeLibraryHandle, string exportName, Type delegateType, out Delegate? nativeDelegate, out string? errorMessage);
    void Free(IntPtr nativeLibraryHandle);
}
