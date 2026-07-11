namespace CropQc.Web.Services;

public interface IPerformanceExternalCallCounter
{
    int TotalCount { get; }
    IReadOnlyDictionary<string, int> ProviderCounts { get; }
    void Increment(string provider);
    void Reset();
}

public sealed class PerformanceExternalCallCounter : IPerformanceExternalCallCounter
{
    private static readonly AsyncLocal<Dictionary<string, int>?> Counts = new();

    public int TotalCount => ProviderCounts.Values.Sum();

    public IReadOnlyDictionary<string, int> ProviderCounts =>
        Counts.Value is null
            ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, int>(Counts.Value, StringComparer.OrdinalIgnoreCase);

    public void Increment(string provider)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            provider = "External";
        }

        var counts = Counts.Value ??= new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        counts[provider] = counts.TryGetValue(provider, out var current)
            ? current + 1
            : 1;
    }

    public void Reset() => Counts.Value = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
}
