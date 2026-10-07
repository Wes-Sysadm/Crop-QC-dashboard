# Active photo references and deleted backup history

## Problem and affected boundary

Backup #185 failed on frozen photo 6997 after three bounded attempts. Snapshot capture included soft-deleted photos, and remote verification required their original and presentation objects. Normal RemoveSamplePhotoAsync/RemoveReceiptPhotoAsync first save IsDeleted, deletion metadata and an audit, then call SafeDeleteStorageAsync for the presentation. Retaining database history while removing that presentation is intentional application behavior.

This PR changes backup snapshot selection, schema-manifest photo counts and package validation only. It does not change photo deletion/upload/presentation code, Google Drive objects, receipts, inventory, audit history, retention, bounded retry/timeouts, worker ownership or PR #266 recovery. No migration or installer change is required.

## Production read-only evidence

At 2026-10-07T19:17:29Z production contained **11,700 active** and **170 deleted** QcPhoto rows. The active count has grown from the supplied 11,680; 50 deleted rows have nonblank presentation keys. None of the active rows has a missing FileId or unsupported storage provider. No individual remote object was tested or repaired.

QcPhoto 6997 belongs to Receipt 1473 / TR109421, type TopOfTruck, filename TR109421_TopOfTruck_2026-09-04_202224.jpg. It is soft-deleted at 2026-09-04T20:40:55.657371Z with reason Removed from sample detail. Add audit 108863 and remove audit 108876 establish legitimate application history. These records and their object references remain unchanged.

The available failure evidence identifies deleted photo 6997, not an active missing object. No active photo is currently **known** missing from that evidence; this is not an exhaustive current Drive-accessibility certification. A new backup must still verify every active snapshot reference. All 170 deleted rows should be excluded from remote requirements while preserved in the database dump.

## Contract and restore

Freeze only QcPhotos where IsDeleted is false, once inside the exported PostgreSQL snapshot. pg_dump remains unfiltered and preserves every photo row, original/presentation key, deletion timestamp/reason and audit. No tombstone duplication or blob restoration is necessary: the application already excludes deleted photos from active receipt/sample views.

New format-v3 manifests explicitly declare ActivePhotos. Schema counts distinguish total, active and deleted photos; total remains the actual database row count. Package validation reconciles the active count with the frozen count and unique accessible manifest rows, and validates total = active + deleted. V1 archives remain readable and v2 archives retain their original all-photo count rule. Older v2 readers reject v3 archives containing deleted rows; use the updated verifier for new archives.

Active missing FileId, unsupported provider, missing original/presentation, and original/presentation size mismatches still fail. The three attempts, per-attempt timeout and cancellation semantics are unchanged. A photo active at capture remains required even if deleted afterward; the frozen snapshot is never relaxed by live re-query.

## Regression and release evidence

The provider regression recreates photo 6997's receipt linkage, type, filename, deletion timestamp/reason, orientation/presentation revision and add/remove audits with fixture storage keys. It tests missing original, missing presentation and both; only the active photo is queried remotely. The actual application pg_dump path and psql restore preserve all table fingerprints and the full deleted row/audits. Restored active selectors exclude it and subsequent backup capture does not resurrect it. Strict active failure and v3 count/scope corruption tests supplement existing v1/v2 compatibility, retry/timeout, snapshot concurrency, worker and batch recovery tests.

Final validation: 2,293 application tests passed, zero failed/skipped, including all 116 scoped backup/restore, photo deletion/staging, orientation/presentation, production readiness and architecture tests (37 BoundedBackupTests cases). The standalone new pg_dump/psql regression also passed. Restore/build, formatting, EF no-pending-model and diff checks passed. The full suite was run once on the final implementation as explicitly requested. Two intermediate failures were fixture setup omissions (required presentation metadata and migration-history table), corrected before the final green run. Only BackupService's reviewed nonphysical-writer fingerprint changed; all inventory architecture gates passed. Production remains unchanged during PR preparation. Do not retry production until the fix is reviewed, merged and deployed; afterward require a fresh fully verified manual backup and then the normal next 1 AM Pacific scheduled execution. Never repair photo 6997 or restore removed blobs to satisfy backup verification.

