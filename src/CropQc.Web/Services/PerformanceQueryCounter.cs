namespace CropQc.Web.Services;

public interface IPerformanceQueryCounter
{
    int Count { get; }
    void Increment();
    void Reset();
}

public sealed class PerformanceQueryCounter : IPerformanceQueryCounter
{
    private int count;

    public int Count => count;

    public void Increment() => Interlocked.Increment(ref count);

    public void Reset() => Interlocked.Exchange(ref count, 0);
}
