# Reviewed historical lineage status normalization

## Scope and reproduction

Base: `c014528ebab82de8c9ce0e5ef0f5f5e920b5a8ae` (main, including PR #251).
This fix is independent of Truck Receipt PR #252. No Truck Receipt code,
repair SQL, entity model, migration, or production data is changed.

Affected area identified before testing: reviewed legacy Grower Lot identity
reconciliation, treatment projection replacement, untreated provenance proof,
and the shared destination-lineage guard. Preserve original inventory
adjustments, receipts, transfers, treatment applications, and movements; keep
ordinary movement allocation/status protection intact. The full suite was
explicitly requested in addition to these affected-area checks.

On unchanged main, the isolated test
`Stale_inventory_status_treatment_projection_is_normalized_as_part_of_proven_untreated_backfill`
failed. The related group produced 68 passes and that one failure. Expected:
the reviewed correction conserves 170 authoritative bins, consolidates the
27 source and 68 stale target lineage bins, and backfills exactly 75 bins whose
untreated provenance is proven. Actual: reconciliation rolls back with
`Destination treatment lineage status or identity requires review.`

## Root cause and intended behavior

There are two distinct findings:

1. The fixture appended a tenth field to a nine-field identity key. It now
   replaces the ninth (status) field. With **only that fixture correction**,
   unchanged production code still failed with the same error. A separate
   regression now requires malformed keys to fail closed.
2. PR #251 intentionally made ordinary movements reject conflicting status
   identities. The reviewed reconciliation path still needs to replace a
   proven stale legacy projection, but called the same destination helper
   before retiring that stale source. The new general guard therefore blocked
   its own audited replacement. This is a production-code defect, not an
   obsolete expectation that should be suppressed.

PR #251's production-type alias normalization is correct and unchanged:
blank and redundant production-type statuses represent the same identity.
The regression instead concerns a non-alias historical status, such as
`CONVENTIONAL` on an otherwise Organic identity. This requires explicit
reviewed reconciliation, not automatic aliasing during a movement.

## Bounded change

Only the two existing reviewed stale-projection replacement call sites pass
their exact pending stale segment objects to the private destination helper.
Quantity and treatment-provenance classification still runs first. Each
exception is rechecked against its own key and snapshot, the target identity,
warehouse and room. Only legacy `CONVENTIONAL`/`ORGANIC` display statuses qualify.
Malformed keys, snapshot mismatches, other lots, other warehouses, Hold states,
unrelated conflicts, and negative lineage remain blocked. Ordinary operational
callers supply no exception and retain the PR #251 guard.

The existing correction transaction, compensating inventory adjustments,
segment version increments, audit record, and identity-reclassification
movements are retained. Stale source segments are kept at zero with their
original key/provenance. Treatment state, signature, Receipt attribution, and
application links are carried to the replacement. Untreated gap backfill still
requires exact addition/movement accounting and absence of applicable treatment.

## Production impact assessment

| Risk | Finding and validation |
| --- | --- |
| Incorrect authoritative quantity | The original failure rolls back. The fix conserves total inventory; source identity adjustments net to zero. |
| Overcount or duplicate projection | Replacement retires the same source quantity; the regression proves one current 170-bin projection and exactly 75 bins of backfill. |
| Stale or downgraded treatment | Stale status can keep reconciliation blocked. Confirmed treatment, its signature, Receipt and application links survive normalization; treated/applicable-treatment gaps cannot become untreated. |
| Incorrectly unavailable bins | The defect leaves the position unavailable. Read-only selection assertions prove it becomes eligible only after exact 170-bin reconciliation. |
| Incorrectly eligible bins | Hold, malformed/mismatched identity, negative, and unproven-treatment cases make no persisted changes. |
| Negative inventory or movement errors | Negative-lineage and conservation guards remain; PR #251 split-allocation, concurrent-transfer and rollback tests pass against PostgreSQL. |
| Historical loss | Scalar-field fingerprints of original receipts, adjustments, transfers, movements and applications remain identical. Original source segments and application links remain. |

No production instance was changed or queried for this fix, so this report does
not claim a new live discrepancy count. Deploying this code would not perform
an automatic data repair. Any future operational correction remains a separate
reviewed, authorized action. The Bartlett guarded repair and 26 other known
discrepancies are outside this PR.

## Validation

- Restore: pass.
- Original failure: reproduced alone and in the unchanged-main group.
- Valid nine-field fixture with old production code: same failure reproduced.
- Fixed isolated regression: pass; original expected behavior preserved and
  strengthened with quantity, history, availability, audit and retry assertions.
- Final affected suite: **81 passed, 0 failed, 0 skipped**.
- Full solution suite: **1,855 passed, 0 failed, 2 optional skips; 1,857 total**.
- Two new PostgreSQL cases executed on fresh uniquely named disposable databases:
  commit/idempotent retry and injected final audit failure. The latter proves
  every intermediate database write rolls back. Foreign keys and normal
  serializable correction transactions are active.
- PR #251's PostgreSQL concurrency, split-allocation rollback, bulk rollback,
  and existing guarded-repair regression also executed on disposable databases.
- Build: pass, 0 errors; 57 existing compiler warnings outside the changed code.
- EF pending-model check: no changes since the last migration.
- Formatting: changed C# files pass `dotnet format --verify-no-changes`.
- `git diff --check`: pass.
- No schema changes; no migration or compatibility package needed.
- No WinForms changes; no MSI rebuild needed.

Optional skips remain the pre-existing
`FruitProfileIdentityGuardPostgreSqlTests.PostgreSql_SaveTimeReferences_Concurrency_Cosmetics_AndAuditAtomicity`
and `ReceiptDateBaselineTests.PostgreSql_date_edit_preserves_accounting`, whose
separate opt-in database variables were not set. Other environment-gated restored
production/hardware tests were not enabled; this is not a claim to have run
every optional integration mode.

Reproduce the affected suite using `dotnet test` with filter:

```text
FullyQualifiedName~LegacyGrowerLotReconciliationTests|FullyQualifiedName~RoomTreatmentTrackingTests|FullyQualifiedName~InventoryIdentity|FullyQualifiedName~BulkRoomTransferTests|FullyQualifiedName~TreatmentAwareTransferTests
```

Set `CROPQC_LEGACY_NORMALIZATION_TEST_POSTGRES` and
`ROOM_TRANSFER_TEST_POSTGRES` to a clearly disposable PostgreSQL admin connection
to execute the provider-specific cases. They create and drop their own test
databases. Never use a production connection. Full validation uses
`dotnet test CropQc.sln --no-build` with the same variables.

## Handoff

Do not merge or deploy automatically. PR #252 remains unchanged at
`3c2ca0b9eaae61293b99463d611afa84511f28b0` and stays Draft.
Only after this separate baseline fix is merged may #252 be updated onto main
and receive the requested full release validation, fresh restore/adoption
rehearsal, protected fingerprints and read-only 15-load/630-bin/InTransit checks.
The approved Truck Receipt adoption and rollback rules are unchanged.
