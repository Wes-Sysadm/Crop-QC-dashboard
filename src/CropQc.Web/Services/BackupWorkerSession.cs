using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace CropQc.Web.Services;

/// <summary>The non-pooled session lock is ownership evidence, not an elapsed-time heuristic.</summary>
public sealed class BackupWorkerSession : IAsyncDisposable
{
    private const long LockKey = 48501871927001;
    private readonly NpgsqlConnection connection;
    private readonly CancellationTokenSource lifetime;
    private readonly CancellationTokenSource stop = new();
    private readonly SemaphoreSlim io = new(1, 1);
    private Task? heartbeat;
    private long runId;
    private Progress progress = new("Initialization", 0, null, 0);
    public Guid WorkerId { get; } = Guid.NewGuid();
    public CancellationToken Token => lifetime.Token;

    private BackupWorkerSession(NpgsqlConnection connection, CancellationToken ct)
    { this.connection = connection; lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct); }

    public static async Task<BackupWorkerSession?> TryOpenAsync(string connectionString, CancellationToken ct)
    {
        // Dispose must close the PostgreSQL session, never return a locked session to a pool.
        var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false, KeepAlive = 15 }.ConnectionString);
        try
        {
            await connection.OpenAsync(ct);
            await using var command = new NpgsqlCommand("SELECT pg_try_advisory_lock(@key)", connection);
            command.Parameters.AddWithValue("key", LockKey);
            if ((bool)(await command.ExecuteScalarAsync(ct))!) return new(connection, ct);
            await connection.DisposeAsync();
            return null;
        }
        catch { await connection.DisposeAsync(); throw; }
    }

    public async Task RecoverOrphansAsync(CancellationToken ct)
    {
        await using var db = new CropQcDbContext(new DbContextOptionsBuilder<CropQcDbContext>().UseNpgsql(connection).Options);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var runs = await db.BackupRunRecords.Where(x => x.Status == BackupRunStatuses.Running).ToListAsync(ct);
        // Legacy workers did not take this lock. Its availability says nothing about their liveness.
        if (runs.Any(x => x.WorkerId == null))
            throw new InvalidOperationException("Legacy Running backup requires reviewed worker-termination evidence before recovery; no automatic timeout recovery is permitted.");
        foreach (var run in runs)
        {
            run.Status = BackupRunStatuses.Abandoned;
            run.CompletedAt = DateTimeOffset.UtcNow;
            run.DurationMilliseconds = (long)(run.CompletedAt.Value - run.StartedAt).TotalMilliseconds;
            run.ErrorSummary = "Owning PostgreSQL worker session ended; a successor acquired the exclusive backup lock. No completed package was certified.";
            await CompleteAbandonedScheduleAsync(db, run, ct);
            db.AuditLogs.Add(new AuditLog
            {
                Action = "BackupAbandoned",
                EntityName = "Backup",
                EntityKey = run.Id.ToString(),
                BeforeValuesJson = JsonSerializer.Serialize(new { Status = BackupRunStatuses.Running, run.WorkerId }),
                AfterValuesJson = JsonSerializer.Serialize(new { run.Status, run.ErrorSummary, successor = WorkerId }),
                SourceApplication = "CropQc.Web",
                CreatedAt = DateTimeOffset.UtcNow
            });
        }
        if (runs.Count > 0)
        {
            var lease = await db.BackupOperationLeases.SingleAsync(x => x.Id == 1, ct);
            // The lock proves that no participating worker retains ownership; preserve all run evidence.
            lease.LeaseId = null;
            lease.ExpiresAt = null;
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    internal static async Task CompleteAbandonedScheduleAsync(CropQcDbContext db, BackupRunRecord run, CancellationToken ct)
    {
        if (run.ScheduledPacificDate is not { } date) return;
        var guard = await db.BackupNightlyRunGuards.SingleOrDefaultAsync(x => x.PacificDate == date, ct);
        if (guard is null || guard.Result != BackupRunStatuses.Running || (guard.BackupRunId is { } id && id != run.Id))
            throw new InvalidOperationException("Nightly backup recovery guard changed; review the attempt before recovery.");
        guard.BackupRunId = run.Id;
        guard.Result = BackupRunStatuses.Abandoned;
        guard.CompletedAt = run.CompletedAt;
        // Keep the date uniqueness guard: recovery never silently schedules a second attempt.
    }

    public async Task StartAsync(long id, CancellationToken ct, TimeSpan? interval = null)
    {
        runId = id;
        await PulseAsync(ct);
        heartbeat = HeartbeatAsync(interval ?? TimeSpan.FromSeconds(20));
    }

    public void Report(string stage, int? completed = null, int? total = null, long? bytes = null)
    {
        var previous = Volatile.Read(ref progress);
        Volatile.Write(ref progress, new(stage, completed ?? previous.Completed, total ?? previous.Total, bytes ?? previous.Bytes));
    }

    public async Task CheckpointAsync(string stage, int? total = null)
    {
        Report(stage, total: total);
        await PulseAsync(Token);
    }

    private async Task PulseAsync(CancellationToken ct)
    {
        await io.WaitAsync(ct);
        try
        {
            var current = Volatile.Read(ref progress);
            await using var tx = await connection.BeginTransactionAsync(ct);
            await using var command = new NpgsqlCommand("""
            UPDATE "BackupOperationLeases" SET "ExpiresAt" = now() + interval '2 hours'
            WHERE "Id"=1 AND "LeaseId"=@worker;
            UPDATE "BackupRunRecords" SET "HeartbeatAt"=now(), "CurrentStage"=@stage,
                "ObjectsCompleted"=@completed, "FrozenObjectCount"=COALESCE("FrozenObjectCount",@total),
                "BytesProcessed"=@bytes
            WHERE "Id"=@id AND "WorkerId"=@worker AND "Status"='Running';
            """, connection, tx);
            command.Parameters.AddWithValue("worker", WorkerId);
            command.Parameters.AddWithValue("stage", current.Stage);
            command.Parameters.AddWithValue("completed", current.Completed);
            command.Parameters.AddWithValue("total", NpgsqlTypes.NpgsqlDbType.Integer, (object?)current.Total ?? DBNull.Value);
            command.Parameters.AddWithValue("bytes", current.Bytes);
            command.Parameters.AddWithValue("id", runId);
            if (await command.ExecuteNonQueryAsync(ct) != 2) throw new InvalidOperationException("Backup worker ownership was lost.");
            await tx.CommitAsync(ct);
        }
        finally { io.Release(); }
    }

    private async Task HeartbeatAsync(TimeSpan interval)
    {
        try
        {
            while (true)
            {
                await Task.Delay(interval, stop.Token);
                await PulseAsync(stop.Token);
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        catch { await lifetime.CancelAsync(); throw; }
    }

    public async Task FinishAsync()
    {
        await stop.CancelAsync();
        if (heartbeat is not null) await heartbeat;
        // Flush the final stage/progress and assert ownership before the caller records success.
        await PulseAsync(Token);
    }

    public async ValueTask DisposeAsync()
    {
        await stop.CancelAsync();
        try { if (heartbeat is not null) await heartbeat; }
        catch { /* RunBackupAsync observes cancellation and records failure; always close ownership. */ }
        await connection.DisposeAsync();
        stop.Dispose();
        lifetime.Dispose();
        io.Dispose();
    }

    private sealed record Progress(string Stage, int Completed, int? Total, long Bytes);
}
