namespace CropQc.QcStation.Fta;

public sealed record DllLoadResult(bool Loaded, string? ErrorMessage)
{
    public static DllLoadResult Success() => new(true, null);
    public static DllLoadResult Failed(string errorMessage) => new(false, errorMessage);
}
