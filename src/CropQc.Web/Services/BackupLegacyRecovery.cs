using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public sealed record BackupLegacyRecoveryTarget(long Id, DateTimeOffset ExpectedStart, string TerminationEvidence);

public static class BackupLegacyRecovery
{
    // Legacy workers never held the session lock: each target needs independent host termination evidence.
    public static Task<string> AbandonAsync(CropQcDbContext db, long id, DateTimeOffset expectedStart,
        string actor, string terminationEvidence, bool workerStopped, bool apply, CancellationToken ct) =>
        AbandonBatchAsync(db, [new(id, expectedStart, terminationEvidence)], actor, workerStopped, apply, ct);

    public static async Task<string> AbandonBatchAsync(CropQcDbContext db,
        IReadOnlyList<BackupLegacyRecoveryTarget> targets, string actor, bool workerStopped, bool apply, CancellationToken ct)
    {
        if (!workerStopped || string.IsNullOrWhiteSpace(actor) || targets.Count == 0
            || targets.Any(x => x.Id <= 0 || x.ExpectedStart == default || string.IsNullOrWhiteSpace(x.TerminationEvidence)
                || x.TerminationEvidence.Trim().Length < 20)
            || targets.Select(x => x.Id).Distinct().Count() != targets.Count)
            throw new InvalidOperationException("Exact unique targets, stopped-worker confirmation, administrator identity and concrete termination evidence for EACH target are required; elapsed time is not evidence.");

        await using var owner = await BackupWorkerSession.TryOpenAsync(db.Database.GetConnectionString()!, ct)
            ?? throw new InvalidOperationException("A participating backup worker is still active.");
        // The transaction uses the lock-owning connection: losing that session cannot leave a writer running.
        await using var recoveryDb = owner.CreateRecoveryContext();
        await using var tx = await recoveryDb.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
        var ids = targets.Select(x => x.Id).ToArray();
        var runs = await recoveryDb.BackupRunRecords.Where(x => ids.Contains(x.Id)).OrderBy(x => x.Id).ToListAsync(ct);
        if (runs.Count != targets.Count || runs.Any(run => run.Status != BackupRunStatuses.Running || run.WorkerId != null
                || run.StartedAt != targets.Single(x => x.Id == run.Id).ExpectedStart)
            || await recoveryDb.BackupRunRecords.AnyAsync(x => x.Status == BackupRunStatuses.Running && !ids.Contains(x.Id), ct))
            throw new InvalidOperationException("Recovery guard changed or already recovered; expected exactly the reviewed legacy Running set, with no unreviewed or modern Running attempt.");

        var lease = await recoveryDb.BackupOperationLeases.SingleAsync(x => x.Id == 1, ct);
        foreach (var run in runs.Where(x => x.ScheduledPacificDate != null))
        {
            var guard = await recoveryDb.BackupNightlyRunGuards.SingleOrDefaultAsync(x => x.PacificDate == run.ScheduledPacificDate, ct);
            if (guard is null || guard.Result != BackupRunStatuses.Running || (guard.BackupRunId is { } id && id != run.Id)
                || runs.Count(x => x.ScheduledPacificDate == run.ScheduledPacificDate) != 1)
                throw new InvalidOperationException("Nightly backup recovery guard changed; review the exact batch before recovery.");
        }
        var reviewed = runs.Select(run => new
        {
            run.Id,
            run.StartedAt,
            run.Status,
            run.WorkerId,
            run.PackageFileName,
            run.VerifiedAt,
            terminationEvidence = targets.Single(x => x.Id == run.Id).TerminationEvidence
        }).ToArray();
        if (!apply)
            return JsonSerializer.Serialize(new
            {
                mode = "Preflight",
                changed = 0,
                actor,
                reviewed,
                lease = new { lease.LeaseId, lease.ExpiresAt },
                message = "No state changed. Explicit --apply is required."
            });

        var completedAt = DateTimeOffset.UtcNow;
        var audits = new List<AuditLog>();
        foreach (var run in runs)
        {
            var before = JsonSerializer.Serialize(run);
            run.Status = BackupRunStatuses.Abandoned;
            run.CompletedAt = completedAt;
            run.DurationMilliseconds = (long)(completedAt - run.StartedAt).TotalMilliseconds;
            run.LeaseReleasedAt = completedAt;
            // Package, checksum, verification and existing failure/history fields remain untouched.
            await BackupWorkerSession.CompleteAbandonedScheduleAsync(recoveryDb, run, ct);
            var audit = new AuditLog
            {
                Action = "BackupAbandoned",
                EntityName = "Backup",
                EntityKey = run.Id.ToString(),
                BeforeValuesJson = before,
                AfterValuesJson = JsonSerializer.Serialize(new
                {
                    run,
                    actor,
                    reviewedIds = ids,
                    terminationEvidence = targets.Single(x => x.Id == run.Id).TerminationEvidence,
                    leaseReleased = true,
                    packageCertified = false
                }),
                SourceApplication = "CropQc.Web",
                CreatedAt = completedAt
            };
            recoveryDb.AuditLogs.Add(audit);
            audits.Add(audit);
        }
        lease.LeaseId = null;
        lease.ExpiresAt = null;
        await recoveryDb.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return JsonSerializer.Serialize(new
        {
            mode = "Applied",
            changed = runs.Count,
            completedAt,
            actor,
            reviewed,
            status = BackupRunStatuses.Abandoned,
            auditIds = audits.Select(x => x.Id),
            packageCertified = false
        });
    }
}
