# PR #284 — receipt-placement compensation conservation

## Scope and authority

Owner instruction: “Fix Receipt Placement Reversal Conservation Gate,” supplied
2026-10-17; execution continued 2026-10-18. Continue PR #284 and its compatible
rollback branch only. No production access, protected restore, merge, deployment,
rollback publication or activation is authorized by this correction task.

Specification 1.2.0, governance commit
`c17bd4deb477b57aa5ac1584175512d49318263b`, applies. INV-001–006,
TRT-001/002, REC-001/002, MOV-001, ROOM-001, AUD-001, OPS-001 and GOV-001
remain unchanged. ADR-001–006 inventory authority and ADR-008 receipt-edit
authorization remain effective; this is a reporting defect correction, not a
business-policy amendment. Main was `ea112eae6aca92778e9338e60c804ac9edd444fd`;
candidate began at `6fc9fe056a647d2ec23b14d222634bcf613cec16`, rollback V3 at
`690292f1bf04097800ccf6a634c42b1ce11c95b1`. Neither required a new main integration.

## Confirmed defect and correction

The canonical reversal writer already emits a negative room ledger adjustment,
an exact reversing treatment movement and an immutable custody compensation.
The previous conservation report omitted `ReceiptPlacementReversal`, producing
exit 2 even when every quantity difference was zero and strict readiness passed.
The standalone SQL diagnostic had the same omission. A fresh canonical synthetic
reproduction placed seven of nineteen acknowledged bins, then reversed two:
five room + fourteen receipt-held = nineteen; the old report still failed solely
on the unknown classification. Its read-only checks changed no table contents.

The report now recognizes this as an inter-crew custody room movement only with
complete raw compensation proof: reversal/placement/acknowledgement/transfer,
unique original credit and reversing debit/movement, identity/room/source,
opposite quantities, operation keys, durable command intent and result, audit,
treatment application membership, chronological custody totals and limits.
Explicit invalid-compensation identifiers prevent `IsReady` when any required
proof is missing or contradictory. Raw records are validated independently of
the historical opening-baseline filter. Unknown types still fail.

`IsKnownMovement()` is deliberately unchanged: its other caller guards historical
receipt editing. Recognition here does not grant permission to edit receipts.
No physical writer, origin rule, schema, migration, treatment application or
inventory readiness gate changed. Writer-registry hashes describe read-only
collection construction; they grant no new physical write capability.

The SQL diagnostic independently applies the same proof and exposes invalid
parent/orphan-ledger/orphan-movement IDs plus `receipt_compensations_ready`.
Its displayed transit quantity now subtracts acknowledged bins; receipt-held
quantity is acknowledgement minus acknowledgement reversals minus placements
plus placement reversals, matching the application report.

Immutable intent timestamps retain 100ns ticks whereas Npgsql persists
microseconds. The proof compares at storage precision; neither stored JSON nor
committed timestamp is rewritten. Treatment is checked against the original
dispatch and placement evidence, including application timing and reversals;
unknown treatment is never interpreted as untreated.

## Change-scoped evidence

The PostgreSQL 18 regression fixture uses synthetic data only. Positive cases
cover full/partial/successive compensation, re-placement, mixed treatment,
occupied destinations and stale projections. Negative cases cover orphan ledger,
missing placement or movement, wrong acknowledgement/transfer, debit mismatch,
wrong reversing movement/source/identity/treatment, duplicate or excessive
compensation, inconsistent held accounting, missing audit, malformed intent,
unknown type and a bad debit superseded by a later opening baseline.

Both report outputs must agree and every table fingerprint must remain unchanged.
For three malformed shapes the test first proves that the existing database check
constraint rejects them, then removes that check **only in the disposable negative
fixture** to exercise the diagnostic's independent rejection. No application or
migration constraint is removed. The valid mixed case preserves 652 bins,
582 untreated / 70 application-29 members, ten original ReceiptAdd rows and
56 bins with explicitly unknown ancestry. This is synthetic evidence, not a claim
about exact ownership of the protected historical shared pool.

The same reporting/SQL/test correction is applied to rollback V3 without importing
the PR #284 feature. Accepted Return-history, mixed-dispatch, destination-baseline,
antiforgery and custody safeguards remain intact. Final build, affected tests,
mandatory contracts, governance, model/format checks, exact artifact identities,
CI and the interrupted same-database Linux handback are recorded in the PR handoff
and local runtime report after execution; this document does not pre-certify them.

## Remaining release gates

Keep PR #284 draft. Exact changed candidate and rollback V4 need focused review.
New local artifacts are distinct from V3 and must not be published or activated
under this task. Following review, a newly scoped protected #207 restore/rehearsal
authorization is required; consumed grants cannot be reused. Production-shaped
readiness, recovery compatibility, fresh independently verified production backup,
safe release and independent production verification remain mandatory. No RED
reconstruction or historical receipt correction is included.
