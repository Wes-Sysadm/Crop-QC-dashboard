using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public static class BackupLegacyRecovery
{
    // Explicit administrator attestation is essential: legacy workers never held the new session lock.
    // This command does not discover/terminate workers, delete artifacts, or certify a package.
    public static async Task<string> AbandonAsync(CropQcDbContext db, long id, DateTimeOffset expectedStart,
        string actor, string terminationEvidence, bool workerStopped, bool apply, CancellationToken ct)
    {
        if (!workerStopped || string.IsNullOrWhiteSpace(actor) || terminationEvidence.Trim().Length < 20)
            throw new InvalidOperationException("Explicit stopped-worker confirmation, administrator identity and concrete termination evidence are required; elapsed time is not evidence.");
        await using var owner = await BackupWorkerSession.TryOpenAsync(db.Database.GetConnectionString()!, ct)
            ?? throw new InvalidOperationException("A participating backup worker is still active.");
        await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var run = await db.BackupRunRecords.SingleAsync(x => x.Id == id, ct);
        if (run.StartedAt != expectedStart || run.Status != BackupRunStatuses.Running || run.WorkerId != null
            || await db.BackupRunRecords.AnyAsync(x => x.Id != id && x.Status == BackupRunStatuses.Running, ct))
            throw new InvalidOperationException("Recovery guard changed; expected exactly the reviewed legacy Running attempt.");
        if (!apply) return "Recovery preflight passed; no state changed. Apply requires separately reviewed termination evidence.";
        var before = JsonSerializer.Serialize(new { run.Id, run.Status, run.StartedAt, run.CompletedAt });
        run.Status = BackupRunStatuses.Abandoned;
        run.CompletedAt = DateTimeOffset.UtcNow;
        run.DurationMilliseconds = (long)(run.CompletedAt.Value - run.StartedAt).TotalMilliseconds;
        run.ErrorSummary = "Administrator confirmed legacy worker termination. Abandoned for release purposes; no package certified.";
        var lease = await db.BackupOperationLeases.SingleAsync(x => x.Id == 1, ct);
        lease.LeaseId = null;
        lease.ExpiresAt = null;
        db.AuditLogs.Add(new AuditLog
        {
            Action = "BackupAbandoned",
            EntityName = "Backup",
            EntityKey = id.ToString(),
            BeforeValuesJson = before,
            AfterValuesJson = JsonSerializer.Serialize(new { run.Status, actor, terminationEvidence, leaseReleased = true }),
            SourceApplication = "CropQc.Web",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return $"Backup {id} recorded as Abandoned with audit evidence; no package or historical evidence deleted.";
    }
}
