# EBS 2026 season-opening correction runbook

## Scope and authorization gate

This package reconciles only the 79 production EBS room-ledger rows outside persisted Evans 7 and before the first legitimate 2026 EBS receipt. Merely merging or deploying this repository does not execute it.

Production execution requires a new, explicit authorization naming this correction. Before that authorization, run only the read-only preflight. Never substitute a guessed date or a manually entered balance.

Protected state:

- Evans 7 (persisted room identity) must remain row-for-row unchanged and at 388 bins.
- WP and every non-EBS facility must remain row-for-row unchanged.
- Zero-net EBS rooms BM-1, BM-6, Evans-12, Evans-5, and Lamb-17 must remain unchanged.
- Receipts, Grower Lots, Bins Runs, Actual Runs, transfers, and their audit history must remain intact.

## Files

- `preflight-ebs-2026-season-opening-correction.sql`: read-only classification and fail-closed fingerprint.
- `apply-ebs-2026-season-opening-correction.sql`: transactional, explicitly gated, idempotent six-row correction.
- `verify-ebs-2026-season-opening-correction.sql`: read-only state and protection verification.
- `ebs-2026-season-opening-production-evidence.csv`: exact read-only production evidence captured before implementation.
- `ebs-2026-season-opening-classification.csv`: every candidate row and reviewed disposition.

## Required backup and disposable restore drill

1. Record the deployed commit, `Production` environment, and PostgreSQL provider.
2. Run `dotnet CropQc.Web.dll --run-backup=predeployment` in the production runtime.
3. Verify the uploaded package exists in the restricted backup destination.
4. Read it back and verify its filename, UTC timestamp, byte size, SHA-256, archive contents, manifest, and database dump.
5. Restore that package into a disposable PostgreSQL database.
6. Run the preflight, apply, apply again, and verify scripts against the disposable restore.
7. Confirm the second apply changes no adjustment and creates no second audit record.
8. Stop if any backup, restore, or verification step fails.

Do not expose the production connection string or backup credentials in terminal captures, logs, or the PR.

## Production preflight

Run with `psql` using the production connection supplied securely by the runtime:

```powershell
psql $env:DATABASE_URL -v ON_ERROR_STOP=1 -f scripts/postgresql/preflight-ebs-2026-season-opening-correction.sql
```

Review all 79 rows. The preflight must report:

- exactly one persisted EBS warehouse and one persisted Evans 7 room;
- boundary receipt 99 at 2026-07-28 08:36 Pacific;
- 79 candidate rows with net 583 bins;
- no unclear classification;
- exact correction targets 1, 8, 22, 23, 25, and 26;
- Evans 7 at 388 bins;
- zero current balance in the already-neutralized rooms.

Stop if production differs materially. Do not edit the apply script to force a changed fingerprint through.

## Separately authorized apply

After explicit authorization, set the required variables and run:

```powershell
psql $env:DATABASE_URL `
  -v ON_ERROR_STOP=1 `
  -v correction_authorization=APPLY_EBS_2026_SEASON_OPENING_CORRECTION `
  -v expected_boundary_receipt_id=99 `
  -v operator_email='<authorized administrator>' `
  -f scripts/postgresql/apply-ebs-2026-season-opening-correction.sql
```

The apply script takes an advisory lock and table locks, repeats the fingerprint checks, snapshots protected rows, makes only these six direct changes, writes one audit record, verifies the final state, and commits only if every assertion passes:

| Adjustment | Before | After | Purpose |
| ---: | ---: | ---: | --- |
| 1 | +34 | 0 | End prior-season BM-4 carry. |
| 8 | +1,039 | 0 | Remove duplicate Evans-01 carry. |
| 22 | 0 | +144 | Restore linked Lamb-14 source quantity. |
| 23 | 0 | +144 | Restore linked Lamb-14 source quantity. |
| 25 | 0 | +101 | Restore linked Lamb-14 source quantity. |
| 26 | 0 | +101 | Restore linked Lamb-14 source quantity. |

The script does not insert a Bins Run, Actual Run, receipt, transfer, or room adjustment. It does not delete any row.

## Verification

```powershell
psql $env:DATABASE_URL -v ON_ERROR_STOP=1 -f scripts/postgresql/verify-ebs-2026-season-opening-correction.sql
```

Then rerun the apply script with the same approved parameters and rerun verification. The repeated apply must report zero updates and exactly one audit marker.

Expected final condition:

- EBS outside Evans 7: every room balance is zero.
- Evans 7: 388 bins and row-for-row unchanged.
- WP and other facilities: row-for-row unchanged.
- Receipts and Grower Lots: unchanged.
- Bins Runs and Actual Runs: unchanged.

## Rollback and restore

If the transaction fails, PostgreSQL rolls it back automatically. If post-commit verification fails, stop application writes, capture the failure evidence, and restore the verified predeployment backup according to the normal repository procedure. Do not improvise compensating inventory adjustments or create fake Bins Runs.

The reviewed direct reversal of this package, if separately approved, is to restore the six captured before-values and remove only the unique correction audit marker in one transaction after the same protected-state checks. The verified backup remains the authoritative recovery path.
