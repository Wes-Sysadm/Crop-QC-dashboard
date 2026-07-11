namespace CropQc.Web.Services;

public interface IPerformanceQueryCounter
{
    int Count { get; }
    double ElapsedMilliseconds { get; }
    int FailedCount { get; }
    void Increment();
    void AddElapsed(TimeSpan duration);
    void IncrementFailed();
    void Reset();
}

public sealed class PerformanceQueryCounter : IPerformanceQueryCounter
{
    private int count;
    private long elapsedTicks;
    private int failedCount;

    public int Count => count;
    public double ElapsedMilliseconds => TimeSpan.FromTicks(Interlocked.Read(ref elapsedTicks)).TotalMilliseconds;
    public int FailedCount => failedCount;

    public void Increment() => Interlocked.Increment(ref count);
    public void AddElapsed(TimeSpan duration) => Interlocked.Add(ref elapsedTicks, duration.Ticks);
    public void IncrementFailed() => Interlocked.Increment(ref failedCount);

    public void Reset()
    {
        Interlocked.Exchange(ref count, 0);
        Interlocked.Exchange(ref elapsedTicks, 0);
        Interlocked.Exchange(ref failedCount, 0);
    }
}
