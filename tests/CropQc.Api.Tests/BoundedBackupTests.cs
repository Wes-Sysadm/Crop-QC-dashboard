using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Storage;
using CropQc.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CropQc.Api.Tests;

public sealed class BoundedBackupTests
{
    [BackupArchiveFact]
    public void Existing_verified_archive_remains_readable_without_rewriting_it()
    {
        var path = Environment.GetEnvironmentVariable("BACKUP_VERIFIED_PACKAGE")!;
        var bytes = File.ReadAllBytes(path);
        var before = System.Security.Cryptography.SHA256.HashData(bytes);
        BackupService.VerifyPackage(bytes);
        Assert.Equal(before, System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
    }
    [BackupRestoreFact]
    public async Task Additive_migration_preserves_verified_restore_and_old_backup_history()
    {
        await using var f = await Database.Create(initialize: false, template: Environment.GetEnvironmentVariable("BACKUP_RESTORE_TEST_POSTGRES"));
        await using var db = f.Context();
        var before = await Fingerprints(f.Connection);
        var migrator = db.GetService<IMigrator>();
        var script = migrator.GenerateScript("20261001144023_CanonicalInventoryCommands", "20261002031709_BoundedBackupSnapshotProgress");
        await db.Database.ExecuteSqlRawAsync(script);
        Assert.Equal(before, await Fingerprints(f.Connection));
        Assert.False(await db.BackupRunRecords.AnyAsync(x => x.WorkerId != null || x.SnapshotCapturedAt != null || x.HeartbeatAt != null));
        var service = new BackupService(db, null!, null!, null!, null!, new CropQc.Shared.Time.PacificBusinessTimeService(new Clock()), null!, null!);
        Assert.NotNull(await (Task<object>)typeof(BackupService).GetMethod("BuildSchemaManifestAsync", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(service, [CancellationToken.None])!);
        db.BackupRunRecords.Add(Run(Guid.NewGuid())); await db.SaveChangesAsync();
        var rollback = migrator.GenerateScript("20261002031709_BoundedBackupSnapshotProgress", "20261001144023_CanonicalInventoryCommands");
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync(rollback));
    }

    private static async Task<SortedDictionary<string, string>> Fingerprints(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString); await connection.OpenAsync();
        var names = new List<string>();
        await using (var list = new NpgsqlCommand("SELECT tablename FROM pg_tables WHERE schemaname='public' AND tablename <> '__EFMigrationsHistory' ORDER BY tablename", connection))
        await using (var reader = await list.ExecuteReaderAsync())
            while (await reader.ReadAsync()) names.Add(reader.GetString(0));
        var result = new SortedDictionary<string, string>();
        foreach (var name in names)
        {
            var projection = name == "BackupRunRecords" ? "to_jsonb(t) - ARRAY['WorkerId','CurrentStage','HeartbeatAt','SnapshotCapturedAt','SnapshotRevision','FrozenObjectCount','ObjectsCompleted','BytesProcessed']" : "to_jsonb(t)";
            await using var hash = new NpgsqlCommand($"SELECT COALESCE(md5(string_agg(v::text,'' ORDER BY v::text)),'') FROM (SELECT {projection} AS v FROM \"{name.Replace("\"", "\"\"")}\" t) q", connection);
            result[name] = (string)(await hash.ExecuteScalarAsync())!;
        }
        return result;
    }

    private sealed class Clock : CropQc.Shared.Time.IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
    [BackupPostgresFact]
    public async Task Snapshot_freezes_10000_references_and_dump_while_100_new_uploads_commit()
    {
        await using var f = await Database.Create();
        await using var db = f.Context();
        db.QcSamples.Add(new QcSample
        {
            Id = 9999,
            SampleType = new SampleType { Id = 900001, Name = "Snapshot test" },
            Status = "In Progress",
            StarchStatus = "Pending",
            PhotoStatus = "Pending",
            EmailStatus = "Not Sent",
            SampleTakenAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        db.QcPhotos.AddRange(Enumerable.Range(1, 10000).Select(Photo));
        await db.SaveChangesAsync();
        byte[] dump;
        await using (var snapshot = await BackupSnapshot.OpenAsync(f.Connection, default))
        {
            Assert.NotEmpty(snapshot.Revision);
            var frozen = await snapshot.FreezePhotosAsync(default);
            Assert.Equal(10000, frozen.Count);
            db.QcPhotos.AddRange(Enumerable.Range(10001, 100).Select(Photo));
            await db.SaveChangesAsync();
            // Concurrent metadata edit must not shift ordering or change a captured object reference.
            await db.QcPhotos.Where(x => x.Id == 1).ExecuteUpdateAsync(x => x.SetProperty(p => p.FileId, "new-upload"));
            Assert.Equal(10000, await snapshot.Database.QcPhotos.CountAsync());
            Assert.Equal("1", frozen[0].FileId);
            var storage = new MetadataStorage();
            var progress = new List<int>();
            var manifest = await BackupPhotoManifest.BuildAsync(frozen, storage, (done, _) => progress.Add(done), default);
            Assert.Equal(10000, manifest.Count);
            Assert.Equal(Enumerable.Range(1, 10000).Select(x => x.ToString()), storage.Keys);
            Assert.Equal(Enumerable.Range(1, 10000), progress);
            // Invoke the actual pg_dump path, importing the same snapshot used by the manifest.
            var service = new BackupService(db, null!, null!, null!, null!, null!, null!, null!);
            dump = await (Task<byte[]>)typeof(BackupService).GetMethod("CreateDatabaseDumpAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(service, [snapshot.SnapshotId, CancellationToken.None])!;
        }
        await using var next = await BackupSnapshot.OpenAsync(f.Connection, default);
        Assert.Equal(10100, (await next.FreezePhotosAsync(default)).Count);
        await using var restored = await Database.Create(initialize: false);
        using var compressed = new MemoryStream(dump);
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var sql = new StreamReader(gzip);
        var info = new ProcessStartInfo("psql") { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        var target = new NpgsqlConnectionStringBuilder(restored.Connection);
        foreach (var arg in new[] { "-X", "--single-transaction", "-v", "ON_ERROR_STOP=1", "-h", target.Host!, "-p", target.Port.ToString(), "-U", target.Username!, "-d", target.Database! }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        await process.StandardInput.WriteAsync(await sql.ReadToEndAsync());
        process.StandardInput.Close();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, await errors);
        await output;
        await using var restoredDb = restored.Context();
        Assert.Equal(10000, await restoredDb.QcPhotos.CountAsync());
        Assert.Equal("1", (await restoredDb.QcPhotos.FindAsync(1L))!.FileId);
        Assert.Empty(await restoredDb.InventoryCommands.ToListAsync());
    }

    [Fact]
    public async Task Missing_frozen_object_retries_exactly_three_times_and_fails_without_omission()
    {
        var storage = new MetadataStorage { Missing = true };
        var completed = 0;
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => BackupPhotoManifest.BuildAsync([Photo(1), Photo(2)], storage,
            (done, _) => completed = done, default, retryDelay: TimeSpan.Zero));
        Assert.Contains("3 bounded attempts", error.Message);
        Assert.Equal(new[] { "1", "1", "1" }, storage.Keys);
        Assert.Equal(0, completed);
    }

    [Fact]
    public async Task Nonresponding_object_has_bounded_attempts_and_cancellation_is_not_retried()
    {
        var storage = new MetadataStorage { Hang = true };
        await Assert.ThrowsAsync<InvalidDataException>(() => BackupPhotoManifest.BuildAsync([Photo(1)], storage, (_, _) => { }, default,
            TimeSpan.FromMilliseconds(20), TimeSpan.Zero));
        Assert.Equal(3, storage.Keys.Count);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => BackupPhotoManifest.BuildAsync([Photo(1)], storage, (_, _) => { }, canceled.Token));
        Assert.Equal(3, storage.Keys.Count);
    }

    [Fact]
    public async Task Referenced_presentation_is_verified_and_size_mismatch_fails()
    {
        var photo = Photo(1); photo.PresentationStorageKey = "presentation"; photo.PresentationFileSizeBytes = 4;
        var storage = new MetadataStorage();
        Assert.Single(await BackupPhotoManifest.BuildAsync([photo], storage, (_, _) => { }, default));
        Assert.Equal(new[] { "1", "presentation" }, storage.Keys);
        photo.FileSizeBytes = 100;
        await Assert.ThrowsAsync<InvalidDataException>(() => BackupPhotoManifest.BuildAsync([photo], storage, (_, _) => { }, default));
    }

    [BackupPostgresFact]
    public async Task Healthy_long_worker_renews_lease_and_orphan_recovers_only_after_session_ends()
    {
        await using var f = await Database.Create();
        var owner = (await BackupWorkerSession.TryOpenAsync(f.Connection, default))!;
        await using var db = f.Context();
        var run = Run(owner.WorkerId);
        run.ScheduledPacificDate = "2026-10-01";
        db.BackupNightlyRunGuards.Add(new BackupNightlyRunGuard
        {
            PacificDate = run.ScheduledPacificDate,
            CreatedAt = run.StartedAt,
            Result = BackupRunStatuses.Running
        });
        db.BackupRunRecords.Add(run);
        var lease = await db.BackupOperationLeases.SingleAsync(); lease.LeaseId = owner.WorkerId; lease.ExpiresAt = DateTimeOffset.UtcNow.AddDays(-1);
        await db.SaveChangesAsync();
        owner.Report("Photo metadata", 3, 10000, 12);
        await owner.StartAsync(run.Id, default, TimeSpan.FromMilliseconds(25));
        Assert.Null(await BackupWorkerSession.TryOpenAsync(f.Connection, default));
        await Task.Delay(75);
        await db.Entry(run).ReloadAsync(); await db.Entry(lease).ReloadAsync();
        Assert.Equal(BackupRunStatuses.Running, run.Status);
        Assert.NotNull(run.HeartbeatAt); Assert.Equal(10000, run.FrozenObjectCount); Assert.Equal(3, run.ObjectsCompleted);
        Assert.True(lease.ExpiresAt > DateTimeOffset.UtcNow);
        owner.Report("Packaging");
        await owner.FinishAsync();
        await db.Entry(run).ReloadAsync();
        Assert.Equal(10000, run.FrozenObjectCount); Assert.Equal(3, run.ObjectsCompleted);
        await owner.DisposeAsync(); // No terminal record: simulates an exited worker's orphan.
        await using var successor = (await BackupWorkerSession.TryOpenAsync(f.Connection, default))!;
        await successor.RecoverOrphansAsync(default);
        run.Status = BackupRunStatuses.Failed; // An old process must not overwrite successor recovery evidence.
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => db.SaveChangesAsync());
        await db.Entry(run).ReloadAsync();
        Assert.Equal(BackupRunStatuses.Abandoned, run.Status);
        var nightly = await db.BackupNightlyRunGuards.AsNoTracking().SingleAsync();
        Assert.Equal(BackupRunStatuses.Abandoned, nightly.Result);
        Assert.Equal(run.Id, nightly.BackupRunId);
        Assert.Equal(run.CompletedAt, nightly.CompletedAt);
        Assert.Null(run.VerifiedAt);
        Assert.Single(await db.AuditLogs.Where(x => x.Action == "BackupAbandoned").ToListAsync());
        await successor.RecoverOrphansAsync(default); // Idempotent; evidence retained.
        Assert.Single(await db.AuditLogs.Where(x => x.Action == "BackupAbandoned").ToListAsync());
    }

    [BackupPostgresFact]
    public async Task Legacy_running_record_requires_explicit_termination_evidence_and_exact_guards()
    {
        await using var f = await Database.Create();
        await using var db = f.Context();
        var run = Run(null); db.BackupRunRecords.Add(run); await db.SaveChangesAsync();
        await using (var owner = (await BackupWorkerSession.TryOpenAsync(f.Connection, default))!)
            await Assert.ThrowsAsync<InvalidOperationException>(() => owner.RecoverOrphansAsync(default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => BackupLegacyRecovery.AbandonAsync(db, run.Id, run.StartedAt, "admin", "Confirmed job termination in host event", false, true, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => BackupLegacyRecovery.AbandonAsync(db, run.Id, run.StartedAt.AddSeconds(1), "admin", "Confirmed job termination in host event", true, true, default));
        await BackupLegacyRecovery.AbandonAsync(db, run.Id, run.StartedAt, "admin", "Confirmed job termination in host event", true, false, default);
        Assert.Equal(BackupRunStatuses.Running, run.Status);
        Assert.Empty(await db.AuditLogs.ToListAsync());
        await BackupLegacyRecovery.AbandonAsync(db, run.Id, run.StartedAt, "admin", "Confirmed job termination in host event", true, true, default);
        await db.Entry(run).ReloadAsync();
        Assert.Equal(BackupRunStatuses.Abandoned, run.Status);
        Assert.Single(await db.AuditLogs.Where(x => x.Action == "BackupAbandoned").ToListAsync());
        Assert.Null(run.VerifiedAt);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void Version2_package_checks_manifest_counts_accessibility_and_dump_completion(bool wrongCount, bool inaccessible, bool incompleteDump)
    {
        var files = new Dictionary<string, byte[]>();
        using (var compressed = new MemoryStream())
        {
            using (var gzip = new GZipStream(compressed, CompressionMode.Compress, true))
            using (var text = new StreamWriter(gzip))
                text.Write("-- PostgreSQL database dump\nSELECT 1;\n" + (incompleteDump ? "" : "-- PostgreSQL database dump complete\n"));
            files.Add("db.sql.gz", compressed.ToArray());
        }
        files.Add("cropqc-config-test.json", JsonSerializer.SerializeToUtf8Bytes(new { }));
        files.Add("cropqc-schema-test.json", JsonSerializer.SerializeToUtf8Bytes(new { rowCounts = new { photos = 1 } }));
        files.Add("cropqc-photo-manifest-test.json", JsonSerializer.SerializeToUtf8Bytes(new[] { new { photoId = 1L, objectAccessible = !inaccessible } }));
        var components = files.Select(x => new { name = x.Key, sizeBytes = x.Value.Length, sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(x.Value)).ToLowerInvariant() }).ToArray();
        files.Add("backup-manifest.json", JsonSerializer.SerializeToUtf8Bytes(new { formatVersion = 2, frozenPhotoCount = wrongCount ? 2 : 1, components }));
        using var package = new MemoryStream();
        using (var zip = new ZipArchive(package, ZipArchiveMode.Create, true))
            foreach (var file in files) { using var entry = zip.CreateEntry(file.Key).Open(); entry.Write(file.Value); }
        if (wrongCount || inaccessible || incompleteDump)
            Assert.Throws<InvalidDataException>(() => BackupService.VerifyPackage(package.ToArray()));
        else BackupService.VerifyPackage(package.ToArray());
    }

    [BackupPostgresFact]
    public async Task Batch_abandons_two_exact_targets_with_one_audit_each_and_preserves_success_history()
    {
        await using var f = await Database.Create();
        await using var db = f.Context();
        var first = Run(null); var second = Run(null); var success = Run(null);
        first.ErrorSummary = "Original historical diagnostic";
        first.PackageFileName = "incomplete-original.zip"; first.Sha256 = "original-hash";
        second.ScheduledPacificDate = "2026-10-01";
        success.Status = BackupRunStatuses.Succeeded; success.VerifiedAt = success.StartedAt;
        success.PackageFileName = "verified.zip"; success.Sha256 = "verified-hash";
        db.BackupRunRecords.AddRange(first, second, success);
        await db.SaveChangesAsync();
        db.BackupNightlyRunGuards.Add(new BackupNightlyRunGuard
        {
            PacificDate = second.ScheduledPacificDate,
            BackupRunId = second.Id,
            Result = BackupRunStatuses.Running,
            CreatedAt = second.StartedAt
        });
        var lease = await db.BackupOperationLeases.SingleAsync();
        lease.LeaseId = Guid.NewGuid(); lease.ExpiresAt = DateTimeOffset.UtcNow.AddDays(1);
        await db.SaveChangesAsync();
        var successBefore = JsonSerializer.Serialize(success);
        var targets = Reviewed(first, second);
        var before = await RecoveryFingerprint(db);
        var preview = await BackupLegacyRecovery.AbandonBatchAsync(db, targets, "reviewing-admin", true, false, default);
        Assert.Contains("Preflight", preview); Assert.Contains("changed\":0", preview);
        Assert.Equal(before, await RecoveryFingerprint(db));

        var result = await BackupLegacyRecovery.AbandonBatchAsync(db, targets, "reviewing-admin", true, true, default);
        Assert.Contains("changed\":2", result);
        db.ChangeTracker.Clear();
        var recovered = await db.BackupRunRecords.Where(x => x.Id != success.Id).ToListAsync();
        Assert.All(recovered, x =>
        {
            Assert.Equal(BackupRunStatuses.Abandoned, x.Status); Assert.NotNull(x.CompletedAt);
            Assert.NotNull(x.LeaseReleasedAt); Assert.True(x.DurationMilliseconds > 0); Assert.Null(x.VerifiedAt);
        });
        Assert.Equal(recovered[0].CompletedAt, recovered[1].CompletedAt);
        Assert.Equal("incomplete-original.zip", recovered.Single(x => x.Id == first.Id).PackageFileName);
        Assert.Equal("original-hash", recovered.Single(x => x.Id == first.Id).Sha256);
        Assert.Equal("Original historical diagnostic", recovered.Single(x => x.Id == first.Id).ErrorSummary);
        Assert.Equal(successBefore, JsonSerializer.Serialize(await db.BackupRunRecords.SingleAsync(x => x.Id == success.Id)));
        var audits = await db.AuditLogs.OrderBy(x => x.EntityKey).ToListAsync();
        Assert.Equal(2, audits.Count);
        foreach (var target in targets)
        {
            var audit = Assert.Single(audits, x => x.EntityKey == target.Id.ToString());
            Assert.Equal("BackupAbandoned", audit.Action);
            Assert.Contains("Running", audit.BeforeValuesJson);
            Assert.Contains("Abandoned", audit.AfterValuesJson);
            Assert.Contains("reviewing-admin", audit.AfterValuesJson);
            Assert.Contains(target.TerminationEvidence, audit.AfterValuesJson);
        }
        lease = await db.BackupOperationLeases.SingleAsync();
        Assert.Null(lease.LeaseId); Assert.Null(lease.ExpiresAt);
        var guard = await db.BackupNightlyRunGuards.SingleAsync();
        Assert.Equal(BackupRunStatuses.Abandoned, guard.Result); Assert.Equal(second.Id, guard.BackupRunId);
        var after = await RecoveryFingerprint(db);
        var retry = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BackupLegacyRecovery.AbandonBatchAsync(db, targets, "reviewing-admin", true, true, default));
        Assert.Contains("already recovered", retry.Message);
        Assert.Equal(after, await RecoveryFingerprint(db));
        // The same worker admission path that previously rejected legacy rows now accepts a nightly worker.
        await using var worker = (await BackupWorkerSession.TryOpenAsync(f.Connection, default))!;
        await worker.RecoverOrphansAsync(default);
        var nightly = Run(worker.WorkerId); nightly.BackupType = BackupRunTypes.Daily;
        lease = await db.BackupOperationLeases.SingleAsync();
        db.BackupRunRecords.Add(nightly); lease.LeaseId = worker.WorkerId; await db.SaveChangesAsync();
        await worker.StartAsync(nightly.Id, default);
        await worker.FinishAsync();
        Assert.Equal(2, await db.AuditLogs.CountAsync(x => x.Action == "BackupAbandoned"));
    }

    [BackupPostgresFact]
    public async Task Batch_changed_start_or_status_or_missing_target_changes_nothing()
    {
        foreach (var change in new[] { "start", "status", "missing" })
        {
            await using var f = await Database.Create();
            await using var db = f.Context();
            var a = Run(null); var b = Run(null); db.BackupRunRecords.AddRange(a, b); await db.SaveChangesAsync();
            var targets = Reviewed(a, b);
            if (change == "start") targets[1] = targets[1] with { ExpectedStart = b.StartedAt.AddSeconds(1) };
            if (change == "status") { b.Status = BackupRunStatuses.Succeeded; await db.SaveChangesAsync(); }
            if (change == "missing") targets[1] = targets[1] with { Id = long.MaxValue };
            var before = await RecoveryFingerprint(db);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                BackupLegacyRecovery.AbandonBatchAsync(db, targets, "admin", true, true, default));
            Assert.Equal(before, await RecoveryFingerprint(db));
        }
    }

    [BackupPostgresFact]
    public async Task Batch_modern_target_or_unreviewed_running_attempt_blocks_every_change()
    {
        foreach (var change in new[] { "modern-target", "unreviewed-legacy", "unreviewed-modern" })
        {
            await using var f = await Database.Create();
            await using var db = f.Context();
            var a = Run(null); var b = Run(change == "modern-target" ? Guid.NewGuid() : null);
            db.BackupRunRecords.AddRange(a, b);
            if (change != "modern-target") db.BackupRunRecords.Add(Run(change == "unreviewed-modern" ? Guid.NewGuid() : null));
            await db.SaveChangesAsync();
            var before = await RecoveryFingerprint(db);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                BackupLegacyRecovery.AbandonBatchAsync(db, Reviewed(a, b), "admin", true, true, default));
            Assert.Equal(before, await RecoveryFingerprint(db));
        }
    }

    [BackupPostgresFact]
    public async Task Batch_live_worker_session_blocks_even_without_a_modern_record()
    {
        await using var f = await Database.Create();
        await using var db = f.Context();
        var a = Run(null); var b = Run(null); db.BackupRunRecords.AddRange(a, b); await db.SaveChangesAsync();
        var before = await RecoveryFingerprint(db);
        await using var worker = (await BackupWorkerSession.TryOpenAsync(f.Connection, default))!;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            BackupLegacyRecovery.AbandonBatchAsync(db, Reviewed(a, b), "admin", true, true, default));
        Assert.Equal(before, await RecoveryFingerprint(db));
    }

    [BackupPostgresFact]
    public async Task Batch_requires_each_evidence_actor_stopped_confirmation_and_unique_targets()
    {
        await using var f = await Database.Create();
        await using var db = f.Context();
        var a = Run(null); var b = Run(null); db.BackupRunRecords.AddRange(a, b); await db.SaveChangesAsync();
        var before = await RecoveryFingerprint(db);
        foreach (var change in new[] { "evidence", "actor", "confirmation", "duplicate", "empty" })
        {
            var targets = Reviewed(a, b);
            if (change == "evidence") targets[1] = targets[1] with { TerminationEvidence = "" };
            if (change == "duplicate") targets[1] = targets[0];
            if (change == "empty") targets = [];
            await Assert.ThrowsAsync<InvalidOperationException>(() => BackupLegacyRecovery.AbandonBatchAsync(db,
                targets, change == "actor" ? "" : "admin", change != "confirmation", true, default));
            Assert.Equal(before, await RecoveryFingerprint(db));
        }
    }

    [BackupPostgresFact]
    public async Task Batch_invalid_nightly_guard_fails_in_preflight_and_apply_without_partial_writes()
    {
        await using var f = await Database.Create();
        await using var db = f.Context();
        var a = Run(null); var b = Run(null); b.ScheduledPacificDate = "2026-10-01";
        db.BackupRunRecords.AddRange(a, b); await db.SaveChangesAsync();
        var before = await RecoveryFingerprint(db);
        foreach (var apply in new[] { false, true })
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                BackupLegacyRecovery.AbandonBatchAsync(db, Reviewed(a, b), "admin", true, apply, default));
        Assert.Equal(before, await RecoveryFingerprint(db));
    }

    [BackupPostgresFact]
    public async Task Batch_audit_write_failure_rolls_back_both_runs_and_lease()
    {
        await using var f = await Database.Create();
        await using var db = f.Context();
        var a = Run(null); var b = Run(null); db.BackupRunRecords.AddRange(a, b); await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_recovery_audit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Injected audit persistence failure'; END $$;
            CREATE TRIGGER reject_recovery_audit BEFORE INSERT ON "AuditLogs"
            FOR EACH ROW EXECUTE FUNCTION reject_recovery_audit();
            """);
        var before = await RecoveryFingerprint(db);
        await Assert.ThrowsAsync<DbUpdateException>(() =>
            BackupLegacyRecovery.AbandonBatchAsync(db, Reviewed(a, b), "admin", true, true, default));
        Assert.Equal(before, await RecoveryFingerprint(db));
        await using var nextWorker = await BackupWorkerSession.TryOpenAsync(f.Connection, default);
        Assert.NotNull(nextWorker); // Failed recovery must release its session lock.
    }

    private static BackupLegacyRecoveryTarget[] Reviewed(params BackupRunRecord[] runs) =>
        runs.Select(run => new BackupLegacyRecoveryTarget(run.Id, run.StartedAt,
            $"Host deployment for backup {run.Id} was deactivated; matching job terminated.")).ToArray();

    private static async Task<string> RecoveryFingerprint(CropQcDbContext db)
    {
        db.ChangeTracker.Clear();
        return JsonSerializer.Serialize(new
        {
            runs = await db.BackupRunRecords.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
            leases = await db.BackupOperationLeases.AsNoTracking().OrderBy(x => x.Id).ToListAsync(),
            guards = await db.BackupNightlyRunGuards.AsNoTracking().OrderBy(x => x.PacificDate).ToListAsync(),
            audits = await db.AuditLogs.AsNoTracking().OrderBy(x => x.Id).ToListAsync()
        });
    }

    private static QcPhoto Photo(int id) => new()
    {
        Id = id,
        QcSampleId = 9999,
        PhotoType = "QC",
        PhotoSource = "Test",
        FileName = $"{id}.jpg",
        ContentType = "image/jpeg",
        StorageProvider = FileStorageProviders.GoogleDrive,
        FileId = id.ToString(),
        FileSizeBytes = 4,
        SharePointDriveId = "test-drive",
        SharePointItemId = id.ToString(),
        CapturedAt = DateTimeOffset.UtcNow
    };
    private static BackupRunRecord Run(Guid? worker) => new()
    {
        BackupType = "Manual",
        Status = BackupRunStatuses.Running,
        EnvironmentName = "Test",
        DatabaseProvider = "PostgreSQL",
        RetentionCategory = "Manual",
        StartedAt = new DateTimeOffset(DateTimeOffset.UtcNow.AddDays(-2).Ticks / 10 * 10, TimeSpan.Zero),
        WorkerId = worker
    };

    private sealed class MetadataStorage : IFileStorageService
    {
        public List<string> Keys { get; } = [];
        public bool Missing { get; init; }
        public bool Hang { get; init; }
        public async Task<FileStorageReference?> GetMetadataAsync(string key, CancellationToken cancellationToken = default)
        {
            Keys.Add(key);
            if (Hang) await Task.Delay(Timeout.Infinite, cancellationToken);
            return Missing ? null : new(FileStorageProviders.GoogleDrive, key, "", key + ".jpg", "image/jpeg", 4, Checksum: "captured-md5");
        }
        public string GenerateTargetPath(FileStorageTargetContext context) => throw new NotSupportedException();
        public Task<FileStorageReference> SaveAsync(FileStorageSaveRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task DeleteOrVoidAsync(string key, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Database(string connection) : IAsyncDisposable
    {
        public string Connection => connection;
        public CropQcDbContext Context() => new(new DbContextOptionsBuilder<CropQcDbContext>().UseNpgsql(connection).Options);
        public static async Task<Database> Create(bool initialize = true, string? template = null)
        {
            var source = Environment.GetEnvironmentVariable("BACKUP_TEST_POSTGRES")!;
            ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(source);
            var builder = new NpgsqlConnectionStringBuilder(source);
            Assert.Contains(builder.Host, new[] { "127.0.0.1", "localhost" });
            var name = "backup_" + Guid.NewGuid().ToString("N") + "_test";
            await using var admin = new NpgsqlConnection(source); await admin.OpenAsync();
            var templateClause = "";
            if (template is not null)
            {
                ProductionDatabaseSafety.RequireClearlyDisposableTestDatabase(template);
                var original = new NpgsqlConnectionStringBuilder(template);
                Assert.Equal(builder.Host, original.Host); Assert.Equal(builder.Port, original.Port);
                templateClause = $" TEMPLATE \"{original.Database!.Replace("\"", "\"\"")}\"";
            }
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\"{templateClause}", admin); await command.ExecuteNonQueryAsync();
            builder.Database = name;
            var result = new Database(builder.ConnectionString);
            if (initialize) { await using var db = result.Context(); await db.Database.EnsureCreatedAsync(); }
            return result;
        }
        public async ValueTask DisposeAsync()
        {
            NpgsqlConnection.ClearAllPools();
            await using var db = Context(); await db.Database.EnsureDeletedAsync();
        }
    }
}

public sealed class BackupPostgresFactAttribute : FactAttribute
{
    public BackupPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BACKUP_TEST_POSTGRES")))
            Skip = "Set BACKUP_TEST_POSTGRES to a disposable local PostgreSQL database; pg_dump and psql must be on PATH.";
    }
}

public sealed class BackupRestoreFactAttribute : FactAttribute
{
    public BackupRestoreFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BACKUP_RESTORE_TEST_POSTGRES"))
            || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BACKUP_TEST_POSTGRES")))
            Skip = "Set BACKUP_RESTORE_TEST_POSTGRES to a verified local restore containing the Phase 2 additive schema.";
    }
}

public sealed class BackupArchiveFactAttribute : FactAttribute
{
    public BackupArchiveFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("BACKUP_VERIFIED_PACKAGE")))
            Skip = "Set BACKUP_VERIFIED_PACKAGE to an existing independently verified local archive for compatibility validation.";
    }
}
