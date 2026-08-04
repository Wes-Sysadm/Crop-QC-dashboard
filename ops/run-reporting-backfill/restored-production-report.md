# Restored production report — backup run 39

## Backup evidence

- Run ID: 39 (`PreDeployment`, `Succeeded`)
- Production SHA: `2b840ef23b505300a7c0be0ca2b011f5355a0e98`
- Started: `2026-08-04 17:41:35.399718+00`
- Completed: `2026-08-04 17:44:01.001141+00`
- Package: `cropqc-production-predeployment-20260804-174135.zip`
- Size: 1,137,016 bytes
- SHA-256: `ee635396b27edde3cc3d4b183caea588827b92047732efe6ba3bd3ae2c17232d`
- Sidecar: `cropqc-production-predeployment-20260804-174135.manifest.json`
- Package storage reference: `1sfVeSnUWN8SMchK8NmbFbg2AxjiesewL`
- Sidecar storage reference: `1k74DFfUEx1z9rg4OnUDmuEWQyDsvabVo`
- Independent checks: exact size/hash, readable ZIP, readable nonempty PostgreSQL dump, four readable JSON components, component sizes/hashes, sidecar/run-record agreement
- Notification: failed separately because Gmail permission was unavailable; no email was dispatched and backup success was unaffected

## Restored baseline

The exact package was restored without errors to localhost-only PostgreSQL 18 database `cropqc_prod_run39_restore_20260804`. Key baseline counts were: Users 10, Warehouses 4, Receipts 136, Grower Lots 398, Actual Runs 7, Actual Run revisions 7, Bins Run entries 39, Room inventory adjustments 142, transfers 0, Run Expectations 4, Packout Runs 0, Run Projections 14, QC samples 156, QC fruit readings 4,122, QC photos 662, and audit logs 13,806.

The database had 24 migration-history rows through `20260727003738_AddGrowerLotProjectionSnapshotsAndPermissionLevels`. Required later application objects through `20260731014107_SeparatePlanningProjectionsFromActualRuns` were present even though that migration-history row was absent. The rehearsal did not fabricate the missing row. It applied and recorded only `20260804052104_AddFacilityRunReporting` after exact object-state verification.

A second restore, `cropqc_prod_run39_sequence_20260804`, rehearsed the exact production sequence from the untouched dump: read-only schema preflight, read-only attribution preflight, bounded compatibility schema apply, object verification, first attribution apply, verification, second attribution apply, and final verification. Every step passed; the second apply changed zero rows.

## Attribution result

- WP crop 2026: 9 included lines, 1,100 bins
- EBS crop 2026: 2 included lines, 388 bins
- Excluded Needs Review: entry 33, 173 bins, missing authoritative grower number
- Pre-2026: 27 lines untouched and excluded from reporting/review

First application: 2 users, 7 Actual Runs, 11 Bins Run lines. Second application: 0 users, 0 Actual Runs, 0 Bins Run lines. Verification after both applications reproduced the same totals and exclusion.

Protected counts and deterministic row fingerprints remained identical for receipts, Grower Lots, warehouses, rooms, inventory adjustments, depletions, transfers, Actual Run revisions, override requests, Run Expectations, projections, packout records, and QC records. Operational-field fingerprints also remained identical for Users, Actual Runs, and Bins Run entries; all 13,806 pre-existing audit rows remained unchanged.

## Restored-production performance

The Release read-only benchmark completed every request successfully against the second restore. Over 100 sequential requests each, Run Reporting summary allocated 694,538 bytes/request, detail allocated 2,626,718 bytes/request, and Needs Review allocated 2,655,826 bytes/request. The mixed route matrix allocated 3,089,675 bytes/request at concurrency 8 and peaked at 255.3 MiB, below the established 384 MiB PR #168 warning threshold. No automatic refresh loop was introduced.
