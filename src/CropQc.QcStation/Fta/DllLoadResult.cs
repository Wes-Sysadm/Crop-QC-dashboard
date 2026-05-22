namespace CropQc.QcStation.Fta;

public sealed record DllLoadResult(bool Loaded, string? ErrorMessage, bool IsArchitectureMismatch = false)
{
    public static DllLoadResult Success() => new(true, null);
    public static DllLoadResult Failed(string errorMessage) => new(false, errorMessage);
    public static DllLoadResult ArchitectureMismatch(string errorMessage) => new(false, errorMessage, true);
}
