namespace CropQc.QcStation.Fta;

public interface INativeDllLoader
{
    DllLoadResult TryLoad(string dllPath);
}
