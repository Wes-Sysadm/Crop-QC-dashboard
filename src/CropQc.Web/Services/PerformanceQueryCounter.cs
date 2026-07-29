namespace CropQc.Web.Services;

public interface IPerformanceQueryCounter
{
    int Count { get; }
    double ElapsedMilliseconds { get; }
    double SlowestCommandMilliseconds { get; }
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
    private long slowestCommandTicks;
    private int failedCount;

    public int Count => count;
    public double ElapsedMilliseconds => TimeSpan.FromTicks(Interlocked.Read(ref elapsedTicks)).TotalMilliseconds;
    public double SlowestCommandMilliseconds => TimeSpan.FromTicks(Interlocked.Read(ref slowestCommandTicks)).TotalMilliseconds;
    public int FailedCount => failedCount;

    public void Increment() => Interlocked.Increment(ref count);
    public void AddElapsed(TimeSpan duration)
    {
        Interlocked.Add(ref elapsedTicks, duration.Ticks);
        var current = Interlocked.Read(ref slowestCommandTicks);
        while (duration.Ticks > current)
        {
            var observed = Interlocked.CompareExchange(ref slowestCommandTicks, duration.Ticks, current);
            if (observed == current)
            {
                break;
            }

            current = observed;
        }
    }
    public void IncrementFailed() => Interlocked.Increment(ref failedCount);

    public void Reset()
    {
        Interlocked.Exchange(ref count, 0);
        Interlocked.Exchange(ref elapsedTicks, 0);
        Interlocked.Exchange(ref slowestCommandTicks, 0);
        Interlocked.Exchange(ref failedCount, 0);
    }
}
