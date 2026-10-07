# Outside Warehouse identity gate: historical names versus stable identity

## Problem and scope

The PR #264 deployment passed schema verification, then stopped on four `OutsideWarehouseTransferIdentityMismatch` issues for adjustments 4128, 4129, 4137 and 4138. The old verifier compared `RoomInventoryAdjustment.GrowerName` directly with `OutsideWarehouseTransfer.GrowerNameSnapshot`.

These records have the same stable identity. Canonical `InventoryCommandExecutor.Ledger` stores `InventoryIdentity.GrowerNumber` in the ledger's legacy `GrowerName` field, while `DispatchAsync` preserves the allocated source segment's `GrowerNameSnapshot` on the Outside Warehouse transfer. The discrepancy existed at creation; it was not caused by a later master-data rename. Changing either historical field to satisfy the string comparison would destroy evidence without correcting physical stock.

Affected area: `InventoryDeductionInvariantService` pre-commit validation and deployment readiness for Outside Warehouse transfers. This change does not alter inventory writers, availability selectors, receipts, transfer snapshots, schema, UI, or quantities. PR #264 remains included in the base.

## Reviewed production accounting

All four records are crop 2026, EBS / LAMB-13 (warehouse 1 / room 6), FruitProfile 3 / GOLD / Conventional, Untreated (`u`), destination Sunfair Outside Warehouse 6. Receipt IDs and source-adjustment IDs are null: these are canonical pooled allocations, with persisted source segment and command provenance.

| Adjustment | Transfer | GrowerLot | Lot / grower number | Deduction | Source before → after | Movement / segment |
|---|---|---|---|---|---|---|
| 4128 | 40 | 658 | 9932 | 48 | 49 → 1 | 811 / 746 |
| 4129 | 41 | 97 | 9285 | 12 | 63 → 51 | 812 / 747 |
| 4137 | 42 | 658 | 9932 | 1 | 1 → 0 | 819 / 746 |
| 4138 | 43 | 97 | 9285 | 47 | 51 → 4 | 820 / 747 |

The command journal agrees with each ledger ID, movement ID, parent ID, stable identity and before/after quantity. The ledger display field holds `9932` or `9285`; each transfer retains the matching grower's historical descriptive name. The four transfers total 108 bins: 49 from lot 9932 and 59 from lot 9285. No identity correction or transfer reversal exists for these records in the reviewed data. No production repair is required for this name discrepancy.

## Correct invariant

- Preserve exact warehouse, room, receipt, crop, GrowerLot, FruitProfile, lot, variety and inventory-status checks between ledger and durable transfer.
- When crop, GrowerLot, FruitProfile and lot provide complete stable ledger identity, do not treat grower display-name equality as an inventory invariant.
- Retain the prior name check for legacy records lacking stable identity; do not grant a blanket exemption to unproven history.
- For canonical (invariant version 3+) dispatches, compare the complete transfer identity—including grower number, production type and Organic flag—with its immutable dispatch movement keys. Require matching quantity, source room/segment, operation key, receipt provenance where specified, and treatment identity.
- Preserve original amount, reversal, balance, parent-link and multiple-parent checks.
- Never use today's master-data grower name to rewrite a transfer, ledger or movement snapshot. The verifier is read-only and does not call SaveChanges.

The canonical movement check prevents removal of the display-name comparison from concealing an actual grower-number or identity mismatch. Legacy movement-key formats are not interpreted as canonical keys.

## Regression and rehearsal

`OutsideWarehouseIdentityGateTests` covers all four production shapes, historical/current names, master-data rename, capitalization/format differences, truly different GrowerLot/grower number/FruitProfile/lot/crop/room/warehouse/receipt/variety/Organic/status, quantity/balance/reversal errors, missing or inconsistent movements, treatment and operation provenance, multiple parents, incomplete legacy identity and read-only snapshot preservation.

The PostgreSQL regression clones the explicitly local, disposable verified restore. It adds the four later transfer shapes only inside that disposable copy. Independent historical source anchors avoid replacing the older backup's current projections. It runs the real deployment deduction-verifier service over the entire restored database and proves all table fingerprints unchanged by verification.

The available local template is Backup #184 (2026-10-05), older than the four 2026-10-06 transfers. This is a production-shaped rehearsal enriched with reviewed records, not a claim of a fresh whole-production restore. The production pre-deploy command must still pass against live data after review/merge. No special backup or new production export was started for this read-only/local task.

Validation: 84 focused tests and the explicitly requested full suite of 2,268 tests passed, with zero failures and zero skips. Restore, build, format, model and diff checks passed, including the reviewed writer-coverage check. No migration or installer change.

## Release sequence

Review this narrow PR against main containing PR #264. After review and authorized merge, deploy compatible main with canonical inventory ON using the unchanged pre-deploy gate. Do not disable the invariant check, rewrite the four historical records or revert #264. Keep TR110044 unchanged until the release is live and authenticated checks pass; then reverify its current state before the operator's genuine 35 → 20 correction.
