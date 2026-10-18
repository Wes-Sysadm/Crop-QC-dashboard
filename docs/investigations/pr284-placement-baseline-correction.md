# PR #284 placement baseline correction

Scope authorized by the owner's “Fix Multi-Allocation Receipt Placement Baseline”
instruction, 2026-10-17: development, synthetic PostgreSQL tests, local frozen
artifacts and focused review only. No production access, protected #207 restore,
merge, deployment or recovery-image publication is authorized by this instruction.

## Rules and scope

Specification 1.2.0, governance source c17bd4deb477b57aa5ac1584175512d49318263b;
main ea112eae6aca92778e9338e60c804ac9edd444fd. INV-001–006, TRT-001/002,
REC-001/002, MOV-001, ROOM-001, AUD-001, OPS-001 and GOV-001 apply.
This is an implementation correction under the existing ADRs, not a policy change.
Recorded origins/custody remain authoritative; treatment is point-in-time evidence;
unknown receipt ancestry remains unknown; immutable history and fences remain.

## Failure and minimum correction

The old PlaceReceiptCustody loop prepared destination evidence only after finding
a position. An empty destination was not registered. The first allocation created
a cohort; the second allocation registered that new cohort as pre-command history.
Later allocations with the same treatment and receipt credited that cohort again.
ValidateDestinationsAsync then rejected its increase because it was neither an
unchanged historical cohort nor exact recorded outgoing consumption:

`Destination cohort changed without exact recorded consumption or preserved incoming allocation evidence.`

The prior frozen candidate/rollback rejected atomically: every table fingerprint
was unchanged and custody remained 648 source + 4 held = 652. Instrumented staged
quantities were 1, 2, 3, 4; the evidence was operable at every stage. A new binary
regression against the exact old Data DLL from ff489a8 reproduced rejection with
four split acknowledgement allocations. Two distinct allocations passed as a
control. Old DLL SHA-256:
ec2483e0a83a085527d11a06b0f10b898eec818d56496219bcb8cade4291d45f.

The placement branch now checks held quantities and captures each affected
destination identity before its first mutation. An absent position is an in-memory
zero observation, not a new inventory row or receipt allocation. Existing
per-allocation custody deductions, movement/ledger credits and final validation
remain. InventoryCommandDestination, CanonicalProjectionFactory and the shared
executor are unchanged. The reviewed writer registry updates only this writer's
LF-normalized content hash; its capability and fence classification do not change.

The identical production-file change is applied to the isolated rollback branch
based on 888368bee33676833da4b27f83409ac0a6f09114. It does not import the full
PR #284 feature. Previous rollback artifacts are immutable and remain distinct.
No schema, migration, permissions, manual-origin or baseline-import changes.

## Focused evidence

ReceiptPlacementBaselineTests has 13 PostgreSQL cases: empty mixed destinations
with two or four allocations; repeated same-cohort credits in empty, occupied
treated and stale destinations; partial placement/later placement/reversal and
replacement; six atomic invalid-input cases; and two independent-connection
concurrency cases (same or different operation key). Exact retries replay;
changed-intent retries conflict; stale selections reject. Invalid operations
preserve all-table fingerprints. These cases are mandatory traceability contracts.

The four-bin fixture proves 648 source + 0 held + 4 destination = 652, with
582 untreated and 70 application-29 bins globally, including exactly two of each
at destination. The 56 bins with shared ancestry retain that uncertainty. Ten
original ReceiptAdd rows stay ten. Placements reconcile to movement and ledger
credits. Occupied treatment segments and membership links retain their complete
scalar state. Stale destination projections remain discrepant by their original
amount; only the incoming recorded delta is applied. Operation-specific proof
does not certify unrelated historical projection cleanup.

Full validation, final source/artifact identities, same-database Linux runtime
rehearsal and independently checked cleanup belong in the final PR handoff. Do
not infer those results from a source commit or a local Windows test alone.

## Required next gates

PR #284 remains draft and production release NO-GO. Focused human review must
cover both new executable identities before publishing or activating recovery.
Then obtain the separately required new #207 restore/write authorization for the
protected same-database rehearsal. All established backup, readiness, runtime,
rollback, CI and independent release verification gates remain mandatory. No
historical RED reconstruction or receipt correction is included.
