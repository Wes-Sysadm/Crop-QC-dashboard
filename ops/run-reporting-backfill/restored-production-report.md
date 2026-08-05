# Restored production report — backup run 41

## Backup evidence

- Run ID: 41 (`PreDeployment`, `Succeeded`)
- Production SHA: `2b840ef23b505300a7c0be0ca2b011f5355a0e98`
- Started: `2026-08-05 02:48:04.057047+00`
- Completed and lease released: `2026-08-05 02:50:46.223512+00`
- Package: `cropqc-production-predeployment-20260805-024804.zip`
- Size: 1,235,296 bytes
- SHA-256: `2fc81bc3255870a439c298acabad8fcb2b28d5e99e90d1a0e59f15a04786a1ae`
- Package storage reference: `1log7Z8DFHhTmYfIyENFt5nJwQQ_y3OVE`
- Sidecar: `cropqc-production-predeployment-20260805-024804.manifest.json`
- Sidecar storage reference: `1h85koqAHgwbbkZ1FePuXdUu_4AxvT2WL`
- Upload read-back verified: `2026-08-05 02:50:46.015766+00`
- Retention completed: `2026-08-05 02:50:46.104255+00`
- Independent checks: exact package size/hash; readable ZIP; readable nonempty PostgreSQL dump; readable configuration, schema, and photo-manifest JSON; exact component sizes/hashes; sidecar/run-record agreement
- Notification: the existing background worker sent its standard success notification separately after the backup; no manual email dispatch or Gmail permission was requested

The package contains a 1,241,564-byte compressed PostgreSQL dump, an 817-byte configuration export, a 1,836-byte schema export, and a 539,155-byte photo manifest. Their SHA-256 values agree with the internal manifest.

## Restored baseline

The exact package restored without errors into localhost-only PostgreSQL 18 database `cropqc_run41_baseline_untouched`. Key counts were: Users 10, Warehouses 4, Receipts 142, Grower Lots 398, Actual Runs 7, Actual Run revisions 7, Bins Run entries 39, room inventory adjustments 154, transfers 0, Run Expectations 4, Packout Runs 0, Run Projections 15, QC samples 166, QC photos 716, QC summary email logs 146, and audit logs 14,895.

The database had 24 migration-history rows through `20260727003738_AddGrowerLotProjectionSnapshotsAndPermissionLevels`. Required later application objects through `20260731014107_SeparatePlanningProjectionsFromActualRuns` were present even though that migration-history row was absent. Neither rehearsal fabricated a missing historical row. They inserted only the two reviewed additive migrations after exact object-state checks.

## Attribution and combined-upgrade result

- WP crop 2026: 9 included lines, 1,100 bins
- EBS crop 2026: 2 included lines, 388 bins
- Excluded Needs Review: entry 33, 173 bins, missing authoritative grower number
- Pre-2026: 27 lines untouched and excluded from reporting/review
- New Actual Runs since run 40: 0
- New Bins Run lines since run 40: 0

The exact clean sequence passed twice from untouched clones: object-state preflight, semantic attribution preflight, reporting schema apply/verify, attribution apply/verify, zero-change second attribution apply, receipt-override schema apply/verify, 195-object application gate, and inventory-deduction readiness. The first attribution changed 2 users, 7 Actual Runs, and 11 Bins Run lines; the second changed `0 / 0 / 0`. Readiness inspected 45 negative adjustments, retaining six known version-0 nonblocking legacy issues and finding zero blocking issues.

The disposable receipt workflow passed quantity reduction, compensating increase, acknowledged negative inventory, duplicate operation-key idempotency, stale-concurrency rejection, void after transfer, paired identity reclassification, unresolved-lineage rollback, reconciliation, and exact-parent invariant verification. Rejected operations left no partial writes. No disposable workflow record is part of the clean-upgrade database or production package.

## Semantic guard result

The guard ignores unrelated Users and authentication/session/provisioning fields. Fifteen disposable PostgreSQL scenarios passed. `LastLoginAt`, login-driven generic `UpdatedAt`, an unrelated new user, and an unrelated login passed. Changes to either target email or active state, a conflicting target facility, a reviewed quantity, a run recorder, a new authoritative line, or a protected relationship failed. Reapplying the already-applied package remained idempotent.

## Matched production-data memory result

Three Release samples per build used the same run-41 dataset and additive schema state. Rooms and Current Inventory response byte counts were identical across all nine samples.

| Phase | Deployed median bytes/request | Main median | Candidate median | Candidate max peak |
| --- | ---: | ---: | ---: | ---: |
| Rooms | 28,104,326 | 12,911,327 | 12,925,479 | 263.04 MiB |
| Current Inventory | 27,921,446 | 12,727,298 | 12,741,907 | 257.40 MiB |
| Mixed concurrency 8 | 6,338,386 | 3,708,990 | 3,713,868 | 275.58 MiB |

Candidate allocations differed from latest main by approximately 0.11% for Rooms and 0.12% for Current Inventory, within the repeated-run noise bound, and were more than 53% below old production. A stable 83 KiB candidate LOH delta was below the explicit 1 MiB materiality floor while total heap and working-set behavior remained bounded. All five-batch candidate plateau samples declined after idle (−8.12, −17.23, and −8.66 MiB from first to last batch), remained below 384 MiB, and showed no monotonic retained-memory growth.
