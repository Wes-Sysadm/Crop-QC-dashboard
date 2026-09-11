using System.Diagnostics;
using System.Runtime;
using System.Text.Json;
using CropQc.Data;
using CropQc.Shared.Time;

namespace CropQc.Web.Services;

public interface IRequestActivityTracker
{
    IDisposable Track();
    RequestActivitySnapshot SnapshotAndResetPeriod();
}

public sealed record RequestActivitySnapshot(
    int ActiveRequests,
    int PeakActiveRequests,
    long StartedRequests,
    long CompletedRequests);

public sealed class RequestActivityTracker : IRequestActivityTracker
{
    private int activeRequests;
    private int peakActiveRequests;
    private long startedRequests;
    private long completedRequests;

    public IDisposable Track()
    {
        Interlocked.Increment(ref startedRequests);
        var active = Interlocked.Increment(ref activeRequests);
        UpdatePeak(active);
        return new RequestLease(this);
    }

    public RequestActivitySnapshot SnapshotAndResetPeriod()
    {
        var active = Volatile.Read(ref activeRequests);
        var peak = Math.Max(active, Interlocked.Exchange(ref peakActiveRequests, active));
        return new RequestActivitySnapshot(
            active,
            peak,
            Interlocked.Exchange(ref startedRequests, 0),
            Interlocked.Exchange(ref completedRequests, 0));
    }

    private void Complete()
    {
        Interlocked.Decrement(ref activeRequests);
        Interlocked.Increment(ref completedRequests);
    }

    private void UpdatePeak(int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref peakActiveRequests))
            && Interlocked.CompareExchange(ref peakActiveRequests, value, current) != current)
        {
        }
    }

    private sealed class RequestLease(RequestActivityTracker tracker) : IDisposable
    {
        private RequestActivityTracker? owner = tracker;

        public void Dispose() => Interlocked.Exchange(ref owner, null)?.Complete();
    }
}

public enum RuntimeMemoryPressureLevel
{
    Normal,
    Elevated,
    Critical
}

public static class RuntimeMemoryPressureClassifier
{
    public static RuntimeMemoryPressureLevel Classify(long workingSetBytes, long warningBytes, long criticalBytes) =>
        workingSetBytes >= criticalBytes
            ? RuntimeMemoryPressureLevel.Critical
            : workingSetBytes >= warningBytes
                ? RuntimeMemoryPressureLevel.Elevated
                : RuntimeMemoryPressureLevel.Normal;
}

public sealed class RuntimeMemoryTelemetryHostedService(
    PerformanceDiagnosticsOptions options,
    IRequestActivityTracker requestActivity,
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<RuntimeMemoryTelemetryHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunEvans11RepairIfRequestedAsync(stoppingToken);

        if (!options.Enabled || !options.RuntimeMemoryTelemetryEnabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.RuntimeMemoryTelemetryIntervalSeconds));
        while (!stoppingToken.IsCancellationRequested)
        {
            LogSnapshot();
            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken))
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task RunEvans11RepairIfRequestedAsync(CancellationToken cancellationToken)
    {
        var mode = configuration["CROPQC_EVANS11_3152_REPAIR_MODE"]?.Trim();
        if (!string.Equals(mode, "prepare", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(mode, "apply", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var requestedBy = configuration["CROPQC_EVANS11_3152_REQUESTED_BY"]?.Trim();
        if (string.IsNullOrWhiteSpace(requestedBy)) requestedBy = "wes@fruitandland.com";
        var backupRunId = long.TryParse(configuration["CROPQC_EVANS11_3152_BACKUP_RUN_ID"], out var parsedBackupRunId)
            ? parsedBackupRunId
            : (long?)null;
        var backupSha256 = configuration["CROPQC_EVANS11_3152_BACKUP_SHA256"]?.Trim();
        var apply = string.Equals(mode, "apply", StringComparison.OrdinalIgnoreCase);

        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var repair = new Evans11Grower3152RepairService(
            services.GetRequiredService<CropQcDbContext>(),
            services.GetRequiredService<IRoomInventoryLedgerQueryService>(),
            services.GetRequiredService<IRoomTreatmentService>(),
            services.GetRequiredService<IInventoryDeductionInvariantService>(),
            services.GetRequiredService<IBackupService>(),
            services.GetRequiredService<IBusinessTimeService>(),
            configuration,
            services.GetRequiredService<ILogger<Evans11Grower3152RepairService>>());
        var result = await repair.RunAsync(
            apply,
            createBackup: !apply,
            requestedBy,
            backupRunId,
            backupSha256,
            cancellationToken);
        var serialized = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (result.Success)
        {
            logger.LogInformation("Evans Street 11 grower repair mode {Mode} completed: {Result}", mode, serialized);
        }
        else
        {
            logger.LogCritical("Evans Street 11 grower repair mode {Mode} failed closed: {Result}", mode, serialized);
        }
    }

    private void LogSnapshot()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        var gc = GC.GetGCMemoryInfo();
        var requests = requestActivity.SnapshotAndResetPeriod();
        ThreadPool.GetAvailableThreads(out var availableWorkerThreads, out var availableCompletionPortThreads);
        ThreadPool.GetMaxThreads(out var maximumWorkerThreads, out var maximumCompletionPortThreads);
        var level = RuntimeMemoryPressureClassifier.Classify(
            process.WorkingSet64,
            options.RuntimeMemoryWarningWorkingSetBytes,
            options.RuntimeMemoryCriticalWorkingSetBytes);

        var values = new object?[]
        {
            level,
            process.WorkingSet64,
            process.PrivateMemorySize64,
            gc.HeapSizeBytes,
            gc.FragmentedBytes,
            gc.MemoryLoadBytes,
            gc.HighMemoryLoadThresholdBytes,
            gc.TotalAvailableMemoryBytes,
            GC.GetTotalAllocatedBytes(precise: false),
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            requests.ActiveRequests,
            requests.PeakActiveRequests,
            requests.StartedRequests,
            requests.CompletedRequests,
            process.Threads.Count,
            maximumWorkerThreads - availableWorkerThreads,
            maximumCompletionPortThreads - availableCompletionPortThreads,
            GCSettings.IsServerGC,
            GCSettings.LatencyMode,
            Environment.Version.ToString(),
            Environment.ProcessorCount,
            (DateTimeOffset.UtcNow - process.StartTime.ToUniversalTime()).TotalSeconds
        };
        const string message = "Runtime memory telemetry {MemoryPressureLevel}: working set {WorkingSetBytes} bytes; private memory {PrivateMemoryBytes} bytes; GC heap {GcHeapBytes} bytes; fragmented {GcFragmentedBytes} bytes; GC memory load {GcMemoryLoadBytes}/{GcHighMemoryLoadThresholdBytes} bytes; total available {GcTotalAvailableMemoryBytes} bytes; total allocated {GcTotalAllocatedBytes} bytes; collections gen0/gen1/gen2 {Gen0Collections}/{Gen1Collections}/{Gen2Collections}; requests active/peak/started/completed {ActiveRequests}/{PeakActiveRequests}/{StartedRequests}/{CompletedRequests}; process threads {ProcessThreads}; ThreadPool worker/IO in use {ThreadPoolWorkersInUse}/{ThreadPoolIoInUse}; server GC {ServerGc}; GC latency {GcLatencyMode}; runtime {RuntimeVersion}; processors {ProcessorCount}; uptime {UptimeSeconds} seconds.";

        if (level == RuntimeMemoryPressureLevel.Critical)
        {
            logger.LogCritical(message, values);
        }
        else if (level == RuntimeMemoryPressureLevel.Elevated)
        {
            logger.LogWarning(message, values);
        }
        else
        {
            logger.LogInformation(message, values);
        }
    }
}
