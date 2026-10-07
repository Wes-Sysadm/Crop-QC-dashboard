# Bounded production backups

## Scope and release boundary

This change concerns backup capture, remote file-reference verification, worker ownership/progress, recovery, and the Admin Backups history display. It does not change inventory, receiving/QC writes, photo upload/storage architecture, Truck Receipt behavior or canonical commands. Prior packages remain immutable. Tests target this backup boundary and its schema/history compatibility; the completed Phase 3 suite is not repeated.

PR #260 remains Ready for Review at `cad53039e041403a35a75393f1a23d4e1bc22b6b`. Its engineering acceptance is unchanged. Backup #177 is **Running / abandoned for release purposes**, not an application failure and not a verified backup. Use the next naturally completed, independently verified backup (preferably overnight) for the remaining isolated Phase 3 rehearsal. Do not wait specifically for #177 or start competing daytime attempts. Fresh release backup/final restore verification remains required before production activation.

## What the deployed code actually does

The backup implementation at deployed `71ff57dd7fe7009b8cc56199a46bd4ead43859c2` is identical to the implementation in base main `4d3cd4b57cb317e335dec77ecf1617182819d17d`.

| Stage | Before this change | Finding |
|---|---|---|
| Identity/lease | Save Running, take an unrenewed two-hour lease | No worker identity, heartbeat or persisted stage |
| Database | `pg_dump --serializable-deferrable`, gzip stdout | One consistent dump, but its safe-snapshot acquisition can wait under concurrent writes |
| Schema/counts | Separate sequential live EF queries after dump | Not the dump's snapshot; counts can disagree with it |
| Photo discovery | One `OrderBy(Id).ToListAsync()` after those queries | Finite list; later inserts cannot append to it |
| Remote metadata | Serial `GetMetadataAsync` for each listed FileId | Slow network work; no backup-specific attempt bound; missing object recorded as inaccessible without failing |
| Package | Serialize frozen in-memory components into a ZIP | No re-enumeration or live-state restart |
| Checksums/upload | Hash components/package, upload, read back; upload/read back sidecar | No production-idle check; package is not rebuilt for new uploads |
| Completion | Persist verified references, retention, Succeeded, release lease | Abrupt worker loss bypasses catch/finally and can leave Running forever |

There is **no OFFSET/LIMIT enumeration, growing work queue, repeat-until-empty loop, upload-triggered restart, or requirement for production to become idle** in photo manifest creation. Rows committed after backup start but before its later photo SELECT can enter the old manifest even though they are absent from the earlier dump. That is a consistency gap, not an endless enumeration algorithm.

PostgreSQL documents that `--serializable-deferrable` can delay dump startup indefinitely while read/write transactions are active. This is a possible delay mechanism, not a proven diagnosis of #177. The last observed console photo query indicates its dump had already advanced past startup at that observation. See [pg_dump](https://www.postgresql.org/docs/current/app-pgdump.html) and [snapshot synchronization](https://www.postgresql.org/docs/current/functions-admin.html#FUNCTIONS-SNAPSHOT-SYNCHRONIZATION).

## Backup #177 read-only findings

- Authoritative record: Running; started 2026-10-01 22:06:13.539320 UTC; completion, failure stage/error, package size/hash and verification remain null. No success/failure terminal evidence is established.
- Lease expiration: 2026-10-02 00:06:13.539320 UTC; it is unrenewed. Expiration alone does not establish failure or authorize recovery.
- Existing schema has no heartbeat, stage, worker identity or frozen/completed object count. The last persisted milestone is BackupStarted. The earlier attached console reached photo reference querying, but exact later progress is unknowable from durable records.
- Current original web instance `qphds` showed only PID 1 web application (uptime over six days), no backup CLI or pg_dump process. Read-only database activity inspection likewise found no matching dump/start-time session. This is bounded observation, not proof of a terminal backup failure or of all possible workers elsewhere.
- A focused application-log search returned no terminal event for #177. Shell command output is not guaranteed to be retained in service application logs.
- Current photo rows: 11,218; 77 UploadedAt values after #177 started; latest upload 2026-10-02 01:55:47.075469 UTC. There is no recorded queue count to correlate growth. Code proves that inserts after its one list query cannot extend that list.
- There is no durable resume checkpoint for the in-memory package/manifest algorithm. A worker that is truly gone cannot resume #177 from its Running row. A live worker can finish a finite list if its external operations return; old network calls and snapshot acquisition lack a backup-specific bound.
- No metadata/status/lease was changed; no worker was killed, no backup was started, and no package was deleted or certified during this investigation.

## Fixed snapshot and file-reference contract

`BackupSnapshot` opens a read-only PostgreSQL REPEATABLE READ transaction and exports its snapshot. That acquisition is the logical capture cutoff; the earlier run StartedAt is separately recorded. Database revision, capture timestamp, application SHA, schema migration list and frozen photo count are recorded.

The exact same snapshot supplies schema counts and one fully materialized ordered **active** photo list (`!IsDeleted`), and is imported by `pg_dump --snapshot`. The dump preserves **all** QcPhoto rows and audits, including deleted metadata. The exporter remains open until dump completion, then closes before slow remote metadata work. Writes continue normally; rows committed after snapshot acquisition belong to a later backup. No paging over mutable production data occurs.

For each frozen photo, verify the original and any distinct presentation object reference. Each metadata request has a 30-second deadline, at most three attempts and bounded inter-attempt delays. Missing/unsupported references, unavailable objects or size mismatches fail clearly; no silent omission or new discovery. Progress total never expands. Cancellation terminates the child pg_dump process and does not retry objects.

This preserves the existing reference-manifest architecture: photo binaries remain in their existing Google Drive storage. The manifest records remote size/checksum/time observed during verification; it is not a historical binary snapshot of an object manually overwritten in Drive. New uploads cannot add references to this package. An unavailable **active** object's original or required presentation still prevents a verified backup. Soft-deleted photos are historical database evidence: the application's deletion workflow may intentionally remove their presentation objects, so those rows do not require remote verification and are not resurrected during restore.

New packages use format version 3 and `photoReferenceScope: ActivePhotos`. Schema `rowCounts.photos` remains the total database count; `activePhotos` and `deletedPhotos` partition that total. The verifier requires nonnegative counts, total = active + deleted, and frozenPhotoCount = activePhotos = unique accessible remote-manifest rows. FrozenObjectCount counts active photo references (a photo may reference both original and presentation objects), not deleted rows or individual metadata requests. The reader retains the existing v1 behavior and v2 all-photo count contract; earlier v2 readers cannot validate a v3 package containing deleted history. Use this version or newer to verify new packages. Existing packages are not rewritten. No database schema change is required.

Format v2 adds cutoff/revision/count metadata while retaining the same five-entry ZIP, four component names, component hashes and sidecar layout. Existing readers ignore the new fields; the new verifier accepts legacy packages and additionally validates v2 frozen-count consistency, unique references, accessibility and the complete gzip/dump footer. No prior archive is rewritten or reclassified.

## Worker ownership and recovery

A non-pooled PostgreSQL session advisory lock owns the whole attempt. A second participating worker cannot acquire it, even when an attempt is old or its lease timestamp expired. Stage checkpoints persist immediately; every 20 seconds the owning connection records heartbeat/progress and renews the compatibility lease. The UI shows stage, heartbeat, frozen/completed photos and referenced bytes verified.

After an owning session ends, a successor holding that same exclusive lock may mark its orphaned Running record Abandoned, with a BackupAbandoned before/after audit. It atomically closes the associated nightly guard as Abandoned while retaining the date uniqueness guard; recovery never schedules a second attempt. It does not certify a package. Run status and nightly result are optimistic concurrency tokens so an old process cannot overwrite successor recovery. Heartbeat connection failure cancels ongoing work; there is no age-only failure rule. An orphan is recoverable on the next supervised attempt; merely viewing history performs no mutation.

Legacy records such as #177 have no session-lock participation. They are **never automatically abandoned** by this proof. A new worker refuses to pass such a Running record until an administrator independently verifies worker termination and uses the exact guarded recovery command below. Do not deploy new and old backup runners concurrently: old binaries do not honor the new lock. Keep the additive migration during application rollback and coordinate worker shutdown before changing runner versions.

### Reviewed legacy recovery

Use the exact run id and StartedAt from the authoritative record. First inspect the hosting job/process identity and verify it has terminated and cannot resume. A stale heartbeat, elapsed time, or missing interactive shell alone is insufficient evidence. The command does not terminate workers.

```text
dotnet CropQc.Web.dll --abandon-backup=<id> --expected-start=<exact-UTC-start> --requested-by=<administrator> --termination-evidence=<specific-host-job-termination-evidence> --worker-stopped --confirm-production
```

This single-run syntax remains compatible. For multiple independently reviewed legacy attempts, use an exact JSON set; do not recover them one at a time or edit status with SQL:

```bash
dotnet CropQc.Web.dll --abandon-backups-json='[{"Id":35,"ExpectedStart":"2026-08-01T22:44:47.388930Z","TerminationEvidence":"<reviewed host termination evidence for 35>"},{"Id":177,"ExpectedStart":"2026-10-01T22:06:13.539320Z","TerminationEvidence":"<reviewed host termination evidence for 177>"}]' --requested-by=<administrator> --worker-stopped --confirm-production
```

Both forms default to read-only preflight, reporting the exact records, per-record evidence and lease state. After reviewing that output under the authorized runbook, add `--apply`. The command first acquires the exclusive worker advisory lock, then uses that same session for a PostgreSQL Serializable transaction. Every target must still be Running with its exact StartedAt and null WorkerId. Any missing/changed target, modern worker, unreviewed Running row or inconsistent nightly guard fails the entire batch. Empty/duplicate targets, missing actor, evidence or stopped-worker confirmation are rejected.

Apply records every target as Abandoned with a shared completion time, duration, lease-release timestamp and a separate BackupAbandoned before/after audit containing the actor and that target's termination evidence. Package/checksum/verification and original diagnostic fields are preserved; nothing is certified or deleted. Compatibility lease state is cleared and any associated nightly guard is closed in the same transaction. Audit persistence failure rolls back all changes. A retry clearly reports changed/already-recovered state without adding audits. Evidence must never contain credentials. These examples do not authorize production mutation by themselves.

## Schema, rollout and validation

`20261002031709_BoundedBackupSnapshotProgress` adds eight nullable fields to BackupRunRecords. No operational rows are backfilled or changed; old applications ignore the columns. Down refuses when worker evidence exists. Keep a compatible worker/application pair and apply the additive schema before using the new runner. Merge ordering with #260 requires the normal migration/model-snapshot reconciliation; this PR does not include #260's workflow cutover.

Focused validation: 50 passed / 0 failed / 0 skipped, including real PostgreSQL 10,000+100 concurrent inserts, captured-reference edits, pg_dump/psql restore, bounded retries/timeouts/cancellation, presentation checks, long healthy worker/lease renewal, exclusive ownership, orphan recovery/audit/idempotency, stale-worker write fencing, guarded legacy recovery, v1 package compatibility (including the unchanged verified Backup #176 archive), v2 manifest/footer checks and existing backup/retention/notification/release-safety checks. The additive migration was applied to a disposable clone of verified Backup #176: all original table fingerprints (excluding migration history and new nullable metadata) remained identical, and old backup history remained readable. No production connection is permitted by the fixture guards.

Reproduce with `BACKUP_TEST_POSTGRES` pointing to a disposable localhost PostgreSQL database, pg_dump/psql on PATH, and `BACKUP_RESTORE_TEST_POSTGRES` pointing to a separately verified local restore with the Phase 2 additive schema. Run the BoundedBackupTests, ProductionReadinessTests and BusinessTimeAndReceiptPurgeTests filter. Restore/package availability is not inferred from these local tests.
