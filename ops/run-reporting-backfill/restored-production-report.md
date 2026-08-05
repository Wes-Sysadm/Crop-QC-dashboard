# Restored production report — backup run 40

## Backup evidence

- Run ID: 40 (`PreDeployment`, `Succeeded`)
- Production SHA: `2b840ef23b505300a7c0be0ca2b011f5355a0e98`
- Started: `2026-08-04 20:58:59.256411+00`
- Completed: `2026-08-04 21:01:30.459091+00`
- Package: `cropqc-production-predeployment-20260804-205859.zip`
- Size: 1,188,705 bytes
- SHA-256: `4083417f42030141cb75b7634add1e899477c73d404e4ee679938772dfc577b7`
- Package storage reference: `1cu4ujncHNNrFlXr4GJDyCcCfjtupcj9j`
- Sidecar: `cropqc-production-predeployment-20260804-205859.manifest.json`
- Sidecar storage reference: `1oQkYthEQ9pAifbl8T9ur1-ie-Cehuv2s`
- Upload verification: `2026-08-04 21:01:30.221708+00`
- Retention completed: `2026-08-04 21:01:30.272728+00`
- Lease released: `2026-08-04 21:01:30.459091+00`
- Independent checks: exact size/hash; readable ZIP; readable nonempty 38,515,807-character PostgreSQL dump; readable configuration, schema, and photo-manifest JSON; exact component sizes/hashes; sidecar/run-record agreement
- Notification: failed separately because Gmail permission was unavailable; no email was dispatched and backup success was unaffected

## Restored baseline

The exact package restored without errors to localhost-only PostgreSQL 18 database `cropqc_prod_run40_restore_20260804`. Key baseline counts were: Users 40, Warehouses 4, Receipts 137, Grower Lots 398, Actual Runs 7, Actual Run revisions 7, Bins Run entries 39, room inventory adjustments 145, transfers 0, Run Expectations 4, Packout Runs 0, Run Projections 14, QC samples 162, QC fruit readings 4,243, QC photos 687, and audit logs 14,415.

The database had 24 migration-history rows through `20260727003738_AddGrowerLotProjectionSnapshotsAndPermissionLevels`. Required later application objects through `20260731014107_SeparatePlanningProjectionsFromActualRuns` were present even though that migration-history row was absent. The rehearsal did not fabricate the missing row. It applied and recorded only `20260804052104_AddFacilityRunReporting` after exact object-state verification.

## Attribution result

- WP crop 2026: 9 included lines, 1,100 bins
- EBS crop 2026: 2 included lines, 388 bins
- Excluded Needs Review: entry 33, 173 bins, missing authoritative grower number
- Pre-2026: 27 lines untouched and excluded from reporting/review
- New Actual Runs since run 39: 0
- New Bins Run lines since run 39: 0

The exact sequence began on the untouched restore: protected fingerprints, schema preflight, attribution preflight, additive schema apply, object verification, attribution apply, totals verification, second attribution apply, and final verification. First application changed 2 users, 7 Actual Runs, and 11 Bins Run lines. Second application changed 0 users, 0 Actual Runs, and 0 lines. Protected operational fingerprints remained identical.

## Semantic guard result

The complete Users-table fingerprint was removed. No authentication/session/provisioning field participates in a deployment blocker. Exact semantic checks remain for Alexis and Robert, every reviewed recording user and facility, every reviewed Bins Run and Actual Run identity, the authoritative classification set, WP/EBS warehouses, audit prefix, migration state, and protected operational tables.

Fifteen disposable PostgreSQL scenarios passed. Changes only to `LastLoginAt`, generic `UpdatedAt`, an unrelated new user, or an unrelated login passed. Changes to either target email, either target active state, either target Employment Facility, a reviewed quantity, a run recorder, a new crop-2026 line, or a protected relationship failed. Reapplying the already-applied package remained idempotent.

## Release-blocking performance result

The Release PR #168 production-restore benchmark was run twice against the run-40 restore. Every request succeeded, reporting summary/detail/Needs Review remained bounded, and mixed-concurrency-8 peaked at 257.5–257.6 MiB, below the 384 MiB warning threshold. The Rooms phase nevertheless exceeded its unchanged 12 MiB allocation guard twice:

- first run: 12,587,325 bytes/request (4,413 bytes over);
- second run: 12,686,571 bytes/request (103,659 bytes over).

The same code against run 39 had measured 12,417,694 bytes/request. The follow-up branch changes no application code, but the repeated current-data benchmark failure remains unexplained and was treated as a meaningful fail-closed condition. The production release stopped before PR readiness/merge, maintenance mode, schema application, attribution, or deployment.
