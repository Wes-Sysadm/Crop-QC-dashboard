# PR251 systemic validation and release runbook

The release fixes inventory movement globally. Only the independently reviewed
McDougall Room14 segment160 correction is authorized historical repair.

## Shared implementation

- Inter-crew dispatch and outside-warehouse allocation use one
  `AllocateAcrossSegmentsAsync` implementation. It materializes once, consumes
  the same tracked segments, checks `remaining + allocated = starting`, and
  recognizes a completed operation before attempting any new materialization.
- Individual internal transfers, bulk internal transfers, processor shipments,
  Bins Run and inventory losses use `MoveCoreAsync`. Their enclosing service
  transactions encompass parent, lineage, ledger and audit writes. Processor
  lines re-read the ledger; bulk positions explicitly reduce the prepared
  snapshot by prior allocations. Corrections/reversals use the original movement
  and reverse its exact provenance, rather than treating the original Receipt
  quantity as fresh stock.
- `InventoryStatusIdentity` canonicalizes whitespace/casing and the proven
  legacy production-type-as-status alias. It never changes historical keys.
  Distinct or unknown status/identity values on the same canonical position
  require review; they cannot become a new implicit untreated remainder.
- Selection, materialization, destination lookup, actual-run correction,
  receipt application/true-up, reconciliation and readiness use that shared
  interpretation. Canonical GrowerLot/FruitProfile/year and receipt/application
  provenance remain protected.
- Existing serializable transactions, ordered room locks, segment concurrency
  versions, unique operation keys and inventory deduction invariants remain in
  force. Wrapped PostgreSQL serialization/deadlock conflicts are recognized and
  returned as a refresh/review response for transfer callers.
- The server reloads eligible positions within the transaction. Bulk review
  tokens cover the selection and ledger revision. Excluded positions remain
  visible and unchanged while independent valid positions can move.

## Test scope

Shared status and allocation logic affects internal/inter-crew/outside/processor
transfers, treatment application and provenance, Bins Run corrections, receipt
inventory overrides, inventory losses and ledger/report projections. Focused
tests cover those dependencies. Unrelated email, photos, hardware, station
enrollment and installer features are not changed or recertified.

Generic PostgreSQL tests cover 800-bin bulk movement, replay, old-room rejection,
status alias/casing, distinct-status isolation, stale review, overlapping
80-of-100 requests, conservation, split 4+62 allocations, second-segment failure,
second-position failure and guarded SQL repair/idempotency. The concurrency test
synchronizes before the existing room locks and verifies exactly one transfer
commits, 20 remain at source, 80 arrive, and the loser cannot replay stale stock.

`RoomTransferRestoredPostgreSqlTests` requires an explicitly disposable fresh
production restore through `ROOM_TRANSFER_RESTORED_POSTGRES`. It proves the
1058-vs-798 blocker, fingerprints historical Receipts/ledger/movements/transfers,
executes the exact repair twice, verifies 542/256/798/2520, and exercises the
authenticated transfer GET and antiforgery-protected bulk POST/replay. All
synthetic fruit movement stays in that disposable database.

## Production execution

1. Freeze the reviewed PR head/main and require all configured checks.
2. Run the standard `dotnet CropQc.Web.dll --run-backup=predeployment` in the
   current Render service image. Require durable Drive read-back, checksums,
   manifest/dump validity, retention and lease release.
3. Restore that verified package into a new disposable PostgreSQL database and
   complete the test above. Record final build/model/format/test evidence.
4. Merge with a merge commit, compare its tree with the tested head, and deploy
   only that exact main SHA with Render auto-deploy remaining off.
5. Verify startup/schema and health before any production repair.
6. Recheck the exact current fingerprints. Execute
   `scripts/postgresql/repair-mcd14-lineage-20260923.sql` with `psql -X -v
   ON_ERROR_STOP=1 -v actor_user_id=1 -f ...` only if all guards still pass.
   Its serializable transaction and table locks isolate the bounded write.
   Any changed precondition, including room total, stops the correction.
7. Verify only segment160 and its audit changed, preserve historical fingerprints,
   rerun safely for AlreadyApplied and verify authenticated transfer UI and logs.
8. Preserve the full read-only global scan separately. No other discrepancy is
   authorized for automatic repair.

Rollback application SHA: `90c129f4bca77b6b6df8b259532d2974e79a1e0d`.
There is no schema change. A rollback remains database-compatible but restores
the old allocation defect; suspend affected movement until the fix is restored.
Do not reverse the reviewed correction or restore an old database over newer
business activity to undo an application deployment.

This runbook does not itself certify that deployment or production repair has
occurred. Exact execution results and global findings belong in the release
report accompanying the task.
