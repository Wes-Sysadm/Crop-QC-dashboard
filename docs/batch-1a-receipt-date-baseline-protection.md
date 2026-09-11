# Batch 1A — Receipt date/baseline protection

## Scope and rule

Normal Receipt metadata editing must not change whether existing Receipt-linked
inventory ledger rows contribute to current accounting. This batch adds a guard;
it does not change inventory aggregation, corrections, custody, or historical data.

The existing opening-baseline rule is room-wide. A receiptless
`StartingInventoryImport` at or after a Receipt's `ReceivedAt` supersedes linked
rows in that room, regardless of crop or identity. Equality is superseded.
For current accounting, the latest baseline per room is therefore sufficient.

The guard examines **every room containing Receipt-linked history**, including
destination rooms and zero-net source history. It compares old/new eligibility
against each room's latest cutoff. It does not infer eligibility from the
Receipt's original room, current balance, or treatment segment ReceiptId.
Receiptless descendants do not themselves use the mutable Receipt date, but
consolidation does not exempt the original linked history from this check.

- Same-side date fixes and edits with no applicable baseline remain allowed.
- Crossing an effective boundary in either direction fails before Receipt or
  audit mutation, with an explanation that opening inventory accounting is
  affected and a controlled accounting review is required.
- Crossing an older, shadowed baseline is allowed if eligibility is unchanged.
- Date checks and successful saves use the existing serializable transaction
  helper; the Receipt concurrency token is retained.
- No compensation movements, migration, backfill, or automatic correction.

`RoomInventoryLedgerQueryService` and the independent
`InventoryConservationReportService` arithmetic are unchanged. The legacy
Dashboard room cutoff helper was also inspected and uses the same latest
room-baseline boundary. No aggregation was replaced by a shared implementation.

## Reproduction and validation

Base: `dc334428ca336f32a8873f132e6b0ec49870ac91`.

Before changing production code, the 22-case regression matrix produced 13
expected failures and 9 passes. An ordinary before-to-after edit silently changed
current inventory **100 → 120**; reversing the date changed **120 → 100**.
A zero-net case changed effective row IDs even though its total stayed 100.

After the guard:

- Batch 1A: **22/22** EF InMemory cases pass.
- Batch 1A: **22/22** PostgreSQL **18.6** cases actually executed; **0 skipped**.
  Each case created a new uniquely named localhost database, used the real
  operational query and independent conservation service, then dropped it.
- Direct regression set: ReceiptInventoryOverrideTests, RoomSummaryDepletionTests,
  BinsRunWorkflowTests, excluding four existing environment-gated PostgreSQL
  methods that otherwise return early. These are not PostgreSQL proof.
  **197/197** directly affected regressions pass; with the 22 InMemory Batch 1A
  cases, the final focused command reports **219/219**, zero skips.
- Coverage includes both directions, exact cutoff, multiple baselines,
  transferred/consolidated fruit, prior crop and prior-crop baseline,
  quantity/identity-corrected Receipts, and zero-net history.
- Rejected edits preserve Receipt scalar fingerprint and audit count. All cases
  preserve ledger/correction/transfer/treatment/custody fingerprints, effective
  row IDs, current inventory positions, and per-receipt/facility/global conservation.
- Existing all-current-crop and opposing per-receipt discrepancy tests remain
  part of ReceiptInventoryOverrideTests; neither their assertions nor production
  reconciliation rules were changed.
- Restore/build, EF pending-model check, formatting, and diff validation pass.

The repository's change-scoped standard applies: only Receipt editing and its
direct inventory/baseline/conservation dependencies are certified. No full-suite,
production restore, UI/browser, or unrelated product certification is claimed.
The local PostgreSQL server was stopped and all case databases removed.

## Release boundary

Migration: **None**. Production changes: **None**.
Evans 11 and the 3162/3152 operational correction are explicitly outside scope.
This is development evidence, not authorization to merge or deploy.
