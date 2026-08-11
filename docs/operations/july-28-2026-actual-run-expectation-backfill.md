# July 28, 2026 Actual Run #1 frozen-expectation backfill

This operation is limited to the reviewed existing July 28, 2026 WP Actual Run #1 and current revision #1. It is not an Actual Run conversion and does not create or change inventory movement. It adds only the missing frozen `RunExpectation`, its source rows, and one correction audit.

The command is a dry run unless `--apply` is supplied. Any preflight issue is a hard stop. Do not change the reviewed constants or weaken a refusal during operations.

## Root cause and reviewed evidence

Actual Run #1 was physically run at `2026-07-29T00:31:00Z` and persisted at `2026-07-31T00:32:42.065870Z`. Commit `f78cfab9f9ffc5a421603d4f5ed1a629a2470350`, which introduced frozen Run Expectations and made current Actual Run creation call `IRunExpectationService.CreateFrozenAsync`, was committed later on July 30 Pacific time. Actual Run #1 therefore predates mandatory frozen expectations and legitimately has none in the run-62 production snapshot.

Verified run-62 evidence:

- Actual Run: ID `1`, Active, revision number `1`, WP
- historical operator: Alexis (`UserId=8`, `alexis@wp-packing.com`)
- revision: ID `1`, Create, operation key `2dc80673fb2a40c8a3a4fbd3a75658b0`, current
- run facility assignment: `ReviewedProductionBackfill:20260804-run40`
- active depletion entry: ID `31`, exactly `184` bins
- linked depletion adjustment: ID `115`, `261 - 184 = 77`
- reviewed source adjustment: ID `114`
- room/crop: WP Room 4 (`WarehouseId=4`, `RoomId=1`), crop year 2026
- identity: grower/lot `1084`, `FruitProfileId=17`, Bartlett `BART`, Conventional, not organic
- existing expectations: `0`
- existing packouts: `0`
- production PackCodeDefinitions: `0`

## Dry-run preflight

Run the built application with the target database connection and no `--apply`:

```text
dotnet CropQc.Web.dll --backfill-july-28-actual-run-expectation
```

Require:

- `success: true`
- `applied: false`
- `preflight.state: Ready`
- `preflight.issues: []`
- exact evidence for run 1, revision 1, entry 31, adjustments 115/114, and 184 bins
- recorded target and protected fingerprints
- recorded adjustment, entry, current inventory, receipt, transfer, grower-lot, physical-run, and Run Reporting integrity values

`AlreadyApplied` is acceptable only when the exact expectation, one source for entry 31, and one audit marker are recognized. A later PackoutRun is acceptable only when it links to that exact expectation. Do not apply again.

## Apply on an authorized database

Apply requires a current successful, verified, retained, unpruned backup whose lease was released. Production also requires `--confirm-production`. Use fingerprints from the immediately preceding reviewed dry run:

```text
dotnet CropQc.Web.dll --backfill-july-28-actual-run-expectation --apply --confirm-production --backup-run-id=<verified-run-id> --requested-by=<active-admin-email> --reason=<approved-correction-reason> --expected-target-fingerprint=<dry-run-target-sha256> --expected-protected-fingerprint=<dry-run-protected-sha256> --authorization-token=APPLY_REVIEWED_JULY_28_ACTUAL_RUN_EXPECTATION_BACKFILL
```

For a disposable copy restored from verified backup run 62, the dump captures its own backup record before upload/read-back, retention, and lease release complete. In an explicitly non-production environment only, attest the exact externally verified package:

```text
--confirm-disposable-restore --backup-run-id=62 --verified-backup-package-sha256=af54589c20c5921681a00f9e01cad801907673fc4bc6f42bfb6d8b81e03603ba
```

Package: `cropqc-production-predeployment-20260811-035656.zip`, 1,889,970 bytes. The restored-copy exception is rejected in production.

## Timestamp and audit semantics

`IRunExpectationService.CreateFrozenAsync` performs the calculation using current authoritative code and configuration. `RunAtSnapshot` remains the historical run time. `CalculatedAt`, audit `CreatedAt`, and the expectation creator truthfully record the backfill execution time and correction administrator. The audit separately preserves Alexis as the historical operator. No timestamp is falsified to July 28.

## Permitted writes

The serializable transaction may create only:

1. one `RunExpectation` for Actual Run #1 revision #1;
2. one `RunExpectationSource` for existing Bins Run entry #31;
3. one `AuditLog` correction marker.

It must not create or change an ActualRun, revision, BinsRunEntry, RoomInventoryAdjustment, Receipt, PackoutRun, PackCodeDefinition, RunProjection, migration-history row, or physical quantity.

## Required verifier

After apply, require `applied: true`, `preflight.state: AlreadyApplied`, expectation total 184, and source count 1. Rerun without `--apply` and require `AlreadyApplied` with zero writes.

Compare before/after:

- protected fingerprint identical;
- adjustment count, quantity, and hash identical;
- Bins Run entry count, quantities, and hash identical;
- current room inventory hash identical;
- receipt, transfer, and grower-lot counts/hashes identical;
- Actual Run physical-bin totals identical;
- All/WP/EBS reporting totals, group count, and grouping hash identical;
- no PackoutRun or PackCodeDefinition created by the backfill;
- `__EFMigrationsHistory` unchanged.

On the disposable restore only, follow the backfill with the authenticated representative PDF upload. Parsing and Packout Review creation must succeed independently of Pack Type configuration. With zero definitions, unknown codes remain Review Required and no weights or categories may be invented.
