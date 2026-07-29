# Packout reconciliation production schema update

This runbook applies the additive PostgreSQL schema required by
`20260729165910_AddPackoutProjectionReconciliation` without running the
historical EF migration chain against production.

The package supports databases whose application objects were originally
created through `EnsureCreated` or earlier compatibility scripts. It does not
insert, update, or delete `__EFMigrationsHistory`.

## Incident baseline

PR #154 merged as commit `9356fa7c2d04aecb251ff7d17bdb6fdae1aad1f9`.
The application reached Render before the corresponding production database
update. PostgreSQL returned `42703` because
`QcSamples.DefectInspectionStatus` did not exist. Read-only catalog inspection
on July 29, 2026 found:

- PostgreSQL 18.4 was reachable.
- `__EFMigrationsHistory` ended at
  `20260727003738_AddGrowerLotProjectionSnapshotsAndPermissionLevels`.
- `RoomInventoryAdjustments` and `BinsRunEntries` already existed.
- All eight additive columns from the migration were absent.
- All six packout reconciliation tables were absent.
- The migration was not partially applied.

The deployment reached production because the web service had no Render
pre-deploy schema gate. Startup diagnostics warned about pending/schema state,
but warnings did not prevent the new container from becoming live.

## Files

- Read-only preflight:
  `scripts/postgresql/preflight-packout-projection-reconciliation.sql`
- Transactional apply:
  `scripts/postgresql/apply-packout-projection-reconciliation-schema.sql`
- Read-only verification:
  `scripts/postgresql/verify-packout-projection-reconciliation.sql`

Run all scripts with `psql -X -v ON_ERROR_STOP=1 -f <script>`. Use a secured
operator environment that supplies the connection through process environment
or a protected secret store. Do not paste a connection string into logs,
tickets, PR descriptions, or shell transcripts.

## Mandatory production order

1. Confirm the production rollback is healthy. Record the active Render commit,
   `ASPNETCORE_ENVIRONMENT`, database provider, `/health`, `/health/db`, login,
   Dashboard, Receipts, QC Sample detail, Rooms, Bins Run, and Run Planner
   results.
2. Disable production autodeploy in Render. Do not start a new deploy.
3. From the active production runtime, run
   `dotnet CropQc.Web.dll --run-backup=predeployment`.
4. Verify the backup command exited zero and record the backup-run ID, UTC
   timestamp, filename, restricted Google Drive location, size, SHA-256,
   deployed commit, and retention category.
5. Read the uploaded backup back from Google Drive and confirm size, SHA-256,
   ZIP readability, manifest, and database dump validation. Stop if any check
   fails.
6. Run the read-only preflight script against production. Save its output in
   the restricted operational record. The output must show the expected
   pre-update objects, defect backfill counts, preservation counts, duplicate
   checks, orphan checks, and an `NOT APPLIED`, `PARTIALLY APPLIED`, or
   `FULLY APPLIED` classification.
7. Review and approve the preflight output. Do not proceed when it reports a
   missing checkpoint object, duplicate key, invalid required value, orphan,
   or an unexplained partial state.
8. Restore the just-verified production backup into a disposable PostgreSQL
   database that is clearly named as a restore/test database.
9. Run the apply script against the restored copy exactly as it would be run
   in production.
10. Run the verification script against the restored copy. Compare its core
    preservation counts with the production preflight counts.
11. Run the application against the restored copy and verify `/health`,
    `/health/db`, login, Dashboard, Receipts, QC Sample details, Rooms,
    Bins Run, Run Planner, projection details, and packout reconciliation
    administration/read paths.
12. Re-run the apply and verification scripts on the restored copy. The second
    apply must succeed without additional rows or schema drift.
13. Obtain explicit written production database authorization. This is the
    production authorization point. Approval of the PR or restored-copy test
    alone is not authorization to change production.
14. Apply
    `scripts/postgresql/apply-packout-projection-reconciliation-schema.sql`
    to production exactly once using `ON_ERROR_STOP`.
15. Run
    `scripts/postgresql/verify-packout-projection-reconciliation.sql`
    immediately. Stop before deployment if it does not exit zero.
16. Deploy the approved latest `main`. The Render pre-deploy command must be:
    `dotnet CropQc.Web.dll --verify-schema=20260729165910_AddPackoutProjectionReconciliation`.
17. Confirm the pre-deploy schema gate passes and record its safe reference ID,
    application version/commit, provider, expected migration, and checked
    object count.
18. Verify the deployed commit, `/health`, `/health/db`, login, Dashboard,
    Receipts, QC Sample details, Rooms, Inventory, Bins Run, Run Planner,
    projection details, and packout reconciliation reads.
19. Confirm receipt, sample, reading, defect, inventory, Bins Run, projection,
    user, photo, email, and audit preservation counts did not decrease.
20. Reenable autodeploy only after the owner approves the completed production
    verification record.

## Apply behavior

The apply script:

- obtains a transaction-scoped advisory lock and table locks;
- verifies required checkpoint tables and columns;
- refuses duplicate keys, ambiguous default configuration, and unsafe partial
  tables;
- creates only missing additive objects;
- completes common partial states such as missing tables, empty partial tables,
  columns, indexes, and foreign keys;
- classifies every existing `QcSample` from actual defect rows;
- makes `DefectInspectionStatus` non-null only after verification;
- inserts only the required `PackoutAnalysisConfigurations` row with ID 1 when
  no configuration exists;
- inserts no sample, receipt, inventory, projection, pack-code, email, photo, or
  audit data;
- verifies the expected objects before committing; and
- rolls the entire transaction back on any error.

Backfill rules are deterministic:

- one or more `QcFruitDefects` related through the sample's fruit readings:
  `Defects found`;
- no related defect rows: `No defects found`.

## Migration-history policy

The compatibility script intentionally does not forge EF migration history.
For an EF-managed restored copy, the normal EF migration remains the supported
path and may record the migration. For a production database whose logical
schema came from `EnsureCreated`/compatibility updates, object verification is
the source of truth. The deployment gate therefore verifies the required
objects and logs the expected migration identifier without requiring a
potentially inaccurate history row.

## Rollback

Before the production apply, rollback means keeping or redeploying the last
compatible application commit. The apply script is additive; after a successful
apply, do not drop the new schema during an application rollback. Keep the
additive objects and redeploy the prior compatible application while the
forward application issue is corrected.

If the apply script fails, PostgreSQL rolls back the transaction. Do not mark
the migration as applied and do not manually recreate individual objects.
Preserve the logs and preflight output, keep the rollback deployment live, and
review the exact failed precondition.
