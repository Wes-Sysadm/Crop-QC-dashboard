# Actual Run 3 reporting identity correction

This package is narrowly limited to the missing authoritative reporting snapshots on Bins Run entry `33`, belonging to Actual Run `3` / current revision `3`. It does not change the physical run, inventory, quantity, date, Sales Desk, fruit profile, grower lot, or adjustment.

Production execution is not authorized by the development PR. Keep the PR draft until separately reviewed and released.

## Reviewed identity

- Actual Run: `3`, Active, current revision number `1`
- Revision: `3`, current
- Bins Run entry: `33`, 173-bin unreversed depletion
- Room Inventory Adjustment: `117`, `-173`, WP, ORBA, Fruit Profile `19`, Grower Lot `394`, grower number/lot `1080`
- Physical run time: `2026-07-31 00:33 UTC`, which is July 30 Pacific
- Facility / Sales Desk: WP / Domex
- Correction source stored in the 50-character database field: `ReviewedProdCorrection:20260826-run3-reporting-id`

## Read-only preflight

Run the command without `--apply`. It must report `Ready` with zero issues, and its evidence and fingerprints must be retained for review.

```powershell
dotnet CropQc.Web.dll --correct-actual-run-3-reporting-identity
```

The preflight accepts only:

- **State A / Ready:** every target reporting field is null and every reviewed source fact still matches;
- **State B / AlreadyApplied:** every target field is exactly the reviewed value, assignment time is populated, assigned-by remains null, and one matching correction audit exists;
- **State C / Refused:** source evidence changed, values are partial/conflicting, or audit state is inconsistent.

State C is a hard stop. Do not edit the constants or repair the row manually.

## Future authorized apply

Before applying in production, take and verify a fresh standard production backup. Supply the retained backup run ID and exact package SHA-256, the reviewed preflight fingerprints, an active built-in Admin email, reason, production confirmation, and exact authorization token.

```powershell
dotnet CropQc.Web.dll `
  --correct-actual-run-3-reporting-identity `
  --apply `
  --confirm-production `
  --backup-run-id=<fresh-verified-run-id> `
  --verified-backup-package-sha256=<fresh-package-sha256> `
  --requested-by=<active-admin-email> `
  --reason="Restore reviewed authoritative reporting identity for Actual Run #3." `
  --expected-target-fingerprint=<immediate-preflight-target-fingerprint> `
  --expected-protected-fingerprint=<immediate-preflight-protected-fingerprint> `
  --authorization-token=APPLY_REVIEWED_ACTUAL_RUN_3_REPORTING_IDENTITY
```

The apply opens a serializable transaction, reruns and fingerprint-checks the preflight, updates only the eleven reporting assignment/identity fields, writes one detailed audit, and requires an `AlreadyApplied` postflight with an unchanged protected fingerprint before commit.

## Verification and idempotency

Run the dry-run command again. Require `AlreadyApplied`, one audit, the exact reviewed fields, and zero writes. Then verify:

- entry 33 is eligible under `AuthoritativeRunReportingQuery.ApplyValidRules`;
- July 29 WP ORBA / Organic / Domex / grower 1080 / 155 matches Actual Run 2;
- July 30 WP ORBA / Organic / Domex / grower 1080 / 173 matches Actual Run 3;
- no incomplete-identity diagnostic remains for Actual Run 3;
- Bins Run entry 33 and adjustment 117 retain 173 / -173 and all original physical inventory fields.

## Reversal / recovery

There is no generic reversal command because that would weaken the bounded correction. If immediate verification fails, the transaction rolls back automatically. If a problem is discovered after commit, stop reconciliation use for the affected run, retain the correction audit and backup, and prepare a separately reviewed reversal that targets entry 33 and nulls only the exact correction fields after proving they still equal this package’s values. Do not restore or alter physical inventory rows.
