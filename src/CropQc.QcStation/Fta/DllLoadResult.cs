namespace CropQc.QcStation.Fta;

public sealed record DllLoadResult(bool Loaded, string? ErrorMessage, bool IsArchitectureMismatch = false, IntPtr NativeLibraryHandle = default)
{
    public static DllLoadResult Success(IntPtr nativeLibraryHandle = default) => new(true, null, false, nativeLibraryHandle);
    public static DllLoadResult Failed(string errorMessage) => new(false, errorMessage);
    public static DllLoadResult ArchitectureMismatch(string errorMessage) => new(false, errorMessage, true);
}
