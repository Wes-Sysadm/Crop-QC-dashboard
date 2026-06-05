namespace CropQc.QcStation.Fta;

public sealed record DllLoadResult(
    bool Loaded,
    string? ErrorMessage,
    bool IsArchitectureMismatch = false,
    IntPtr NativeLibraryHandle = default,
    string? ExceptionType = null,
    int? HResult = null,
    string? CurrentDirectoryBeforeLoad = null,
    string? CurrentDirectoryAtLoad = null,
    string? DllSearchDirectory = null,
    string? LoadedPath = null)
{
    public static DllLoadResult Success(
        IntPtr nativeLibraryHandle = default,
        string? currentDirectoryBeforeLoad = null,
        string? currentDirectoryAtLoad = null,
        string? dllSearchDirectory = null,
        string? loadedPath = null) =>
        new(true, null, false, nativeLibraryHandle, null, null, currentDirectoryBeforeLoad, currentDirectoryAtLoad, dllSearchDirectory, loadedPath);

    public static DllLoadResult Failed(
        string errorMessage,
        string? exceptionType = null,
        int? hResult = null,
        string? currentDirectoryBeforeLoad = null,
        string? currentDirectoryAtLoad = null,
        string? dllSearchDirectory = null,
        string? loadedPath = null) =>
        new(false, errorMessage, false, default, exceptionType, hResult, currentDirectoryBeforeLoad, currentDirectoryAtLoad, dllSearchDirectory, loadedPath);

    public static DllLoadResult ArchitectureMismatch(
        string errorMessage,
        string? exceptionType = null,
        int? hResult = null,
        string? currentDirectoryBeforeLoad = null,
        string? currentDirectoryAtLoad = null,
        string? dllSearchDirectory = null,
        string? loadedPath = null) =>
        new(false, errorMessage, true, default, exceptionType, hResult, currentDirectoryBeforeLoad, currentDirectoryAtLoad, dllSearchDirectory, loadedPath);
}
