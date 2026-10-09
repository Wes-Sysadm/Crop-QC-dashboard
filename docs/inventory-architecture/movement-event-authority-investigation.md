# Inventory movement and destination projections

Local implementation and isolated rehearsal. This document is not release or production repair approval.

## Rules and scope

Base: `50bdc1a3a0dbbdfde435e2b87e24e43a2fd486a8`; branch:
`codex/inventory-movement-evidence-null-safety`. Specification 1.0.1,
governance commit `566413428f86266c1d3d5015010276ea0090115f`.
Applicable rules: INV-001–006, MOV-001, TRT-001–002, REC-001–002,
ROOM-001, AUD-001, OPS-001; decisions ADR-001–006.

The owner's 2026-10-09 clarification defines one authority chain: authorized
receipt/origin, audited movement/custody, occupancy over time, treatment events,
and current bin state. A disagreeing projection is a reconciliation defect,
not another authority. Recorded aggregate allocations must not be converted
into invented physical bin identifiers or exact receipt ancestry.

The affected area includes the shared resolver and command engine, room/bulk
movement, receiving, cross-company custody completion/return, receipt location
and identity corrections, and consumption/restoration paths sharing those
components. Their selectors and postconditions are part of the same boundary.
Original receipts, ledger, movements, applications, parents, corrections and
audits must remain intact. Readiness and production safeguards remain separate
from operation-specific admission.

## Read-only production observation

Snapshot: **2026-10-09 14:46:28.269371 UTC**, PostgreSQL RepeatableRead with
`transaction_read_only=on`, deployed SHA `50bdc1a3a0dbbdfde435e2b87e24e43a2fd486a8`.
Render workspace `tea-d7uc4ippo60c73ebn4mg` was supplied explicitly.
No production command, repair, backup, deployment or configuration change ran.
The sanitized typed evidence is retained in
`tests/CropQc.Api.Tests/Fixtures/InventoryMovement/mcdougall-20261009.json`.

| Position | Ledger bins | Current projection bins | Observation |
|---|---:|---:|---|
| McDougall MCD-03 / room 55 / lot 1242 / RED / profile 14 | 34 | 34 | Receipt 2472, TR tr509483; ledger 4146; movement 828; segment 765; untreated |
| McDougall MCD-02 / room 73 / lot 1242 / RED / profile 14 | 652 | 442 | Segment 729: 372 shared untreated; segment 730: 70, receipt 2378, application 29 |
| McDougall MCD-02 / room 73 / lot 1242 / ATGL / profile 22 | 63 | 63 | Separate receipt 2533, TR509491; ledger 4210; movement 893; segment 829 |
| McDougall MCD-02 / room 73 / lot 2750 / RED / profile 14 | 257 | 183 | Another incomplete mixed-treatment position; application 28 |

There is no ATGL source position in room 55 in this snapshot. The reported
variety remains unverified; the lot clarification was `1242`. Tests must not
rename the recorded 34 RED bins as ATGL. Arbitrary synthetic variety cases
test that the correction is independent of this identity (synthetic CGAL inventory).

Room 73 / RED / 1242 has nine ReceiptAdd entries totalling 596 bins:
3821/2252/+56, 3835/2266/+56, 3863/2294/+68, 3931/2334/+68,
3978/2343/+68, 4016/2378/+70, 4024/2386/+70, 4066/2419/+70,
4100/2439/+70 (ledger/receipt/quantity). Transfer 411 adds 56 through
ledger 4074 and immutable movement 801. No RED/1242 withdrawal appears
in the room's complete loaded ledger. Transfer 410's 74-bin withdrawal
belongs to ORGS/1128, not this pool.

The 210-bin projection shortfall corresponds to three later 70-bin receipts
(2386, 2419, 2439). Application 29 is receipt-scoped to receipt 2378;
it does not treat later receipts or incoming fruit. A supplementary read-only
observation at 14:56:41.564392 UTC confirmed allocation 40: application 29,
receipt 2378, 70 bins, `u` to `u|a:29`. Allocation 39 similarly records 72 bins
of receipt 2364 under application 28 for lot 2750.

Transfer 411 came from room 69, backed by receipt 2262 / TR509428, origin ledger
3831 (+56), and transfer debit 4073 (-56). Its arrival movement 801 retains a
null receipt ID; the proposal preserves that shared ancestry. It does not assign
the destination's 56 bins to receipt 2262 simply because the upstream quantity
matches. Source receipt 2472 arrived effectively 2026-10-06 19:44 UTC and was
recorded at 19:46:14.288658 UTC. ATGL receipt 2533 is a separate arrival effective
2026-10-06 19:57 UTC, recorded 2026-10-08 22:56:34.475533 UTC.

The [read-only evidence queries](movement-event-authority-evidence.sql) were
syntax- and result-checked against the isolated backup. The immutable typed
observation and supplementary application allocation are regression inputs.

## Reproduced code path

`DashboardDataService.CreateRoomTransferAsync` routes canonical requests through
`CreateCanonicalRoomTransferAsync` to `InventoryCommandExecutor.ExecuteAsync`.
The source resolves to 34 available untreated bins. The destination resolves to
652 authoritative bins, 442 projected bins, no admitted treatment slices,
`TreatmentConfidence=Ambiguous`, and `MixedTreatmentAmbiguity`.
The executor calls the default treatment-strict resolver for the whole
destination, then requires `IsOperable` and emits:
`Destination evidence is not proven; no stock may be merged into it.`

The unchanged command was also executed on an isolated clone of verified backup
#190. It returned the exact reported destination error; every table's content
hash remained unchanged. This reproduces the defect with the recorded RED
identity, not the operator's unverified ATGL selection. The original failed
request's exact quantity, submission time and server log were not recovered.

Deleting this predicate alone is insufficient. Normalization currently only
rebuilds an entirely proven untreated pool; the final affected-room check
again requires the entire position to be operable and balanced. Equivalent
destination checks occur in `InventoryCommandCustody`, `Receiving`,
`ReceiptLocation`, `ReceiptIdentity`, `Restoration`, and `TransitEdits`.
Run revisions, receipt changes and reversals have dependent postconditions.
Actual source, exact receipt, custody-parent, route and application guards must
be distinguished from incoming-destination checks.

## Implemented operation policy

Destination admission must validate the proposed operation against recorded
origins, current custody, identity, eligibility, chronology and conservation.
It must not require preexisting destination projections to establish that
incoming bins exist. Incoming cohorts retain their own treatment and provable
ancestry. Unknown ancestry remains shared; unknown treatment remains unknown.

`InventoryDestinationAdmission` checks room custody, nonnegative ledger quantity,
identity and transaction cutoff. Route/company, active/sealed room, source
availability, exact dispatch allocation, receipt completion and concurrency guards
remain in their existing typed operations. Missing, excessive, negative or
ambiguous **projection** quantities are reconciliation information. A negative
physical ledger or an unfunded/duplicate recorded origin remains a hard stop.

`InventoryOriginGuard` follows recorded receipt origins, committed correction
snapshots, supported authorized additions and debit-funded custody parents.
Current receipt identity is not used to invalidate an earlier corrected identity.
Run restoration follows its exact original consumption rather than treating a
later revision's debit as its origin. Evidence is bounded and read-only.

The projection factory uses the command's `CohortKey` as a derived allocation
boundary. Incoming known treatment and nullable receipt ancestry coexist with
older unresolved projections. This key is not a physical bin identifier or a new
inventory origin. Commit checks exact source/destination ledger and projection
deltas and preserves unrelated cohorts. `CanonicalDestinationReconciliation`
records the old evidence, diagnostics and new allocation whenever reconciliation
is still needed. Ordinary safe untreated normalization remains supported.

`InventoryEventReplay` reconstructs quantities from recorded ledger sequence and
audited movement allocations, retaining effective dates for occupancy and
application eligibility. Complete application-source snapshots determine treatment
membership. A receipt correction reason is never allocation proof. Unallocated
withdrawals preserve shared ancestry; surviving mixed treatment becomes unknown.
An effective receipt arrival crossing an already-recorded application is named
as an occupancy conflict, not silently labelled untreated. Unsupported origin or
allocation events are reported by ledger/movement/application ID.

When the complete recorded sequence conserves quantity and proves every treatment
cohort, the command can retire defective current projections and create replacements
under `CanonicalRecordedCohortReconstruction`. This occurs in the same Serializable
transaction, after origin validation, with exact before versions and retained
historical projection rows. It does not rewrite ledger, receipts, movements or
treatment applications. If older treatment remains unresolved, only independently
proven incoming cohorts are selectable; their remainder is not relabelled untreated.
Whole-room treatment continues requiring all occupied positions. Exact receipt
operations retain their separate ancestry guard.

Room moves, same-company warehouse moves, transfer completion/return/reopen,
ordinary receiving, receipt location/identity correction, loss/run restoration,
and their read selectors now share this distinction. Packing, depletion and loss
selectors can use independently proven current cohorts. The change contains no
room, lot, variety, site or receipt-specific runtime exception. Strict release
readiness and explicit maintenance reconstruction approval are not disabled.

## Isolated #190 rehearsal and reconstruction proposal

The final candidate's reviewed feature migration was applied only to a fresh disposable clone; both commands and all 20 protected-history comparisons were repeated after the destination origin check was added.
The original backup/template was read-only and unchanged.

| Isolated step | MCD-03 RED/1242 | MCD-02 RED/1242 | Combined |
|---|---:|---:|---:|
| Before | 34 | 652 | 686 |
| Move 34 into room 73 | 0 | 686 | 686 |
| Move 50 untreated back, using complete recorded-cohort reconstruction | 50 | 636 | 686 |

After the second command, current projections matched 50 and 636 respectively.
Application 29 still belongs to its original 70 bins; the separate ATGL/1242
position remained 63. Room 73 RED/2750 remained 257 authoritative / 183 projected;
the read-only event projection offers 257 without changing that stored state.

Independent SQL comparisons against the untouched template passed 20 checks:
original ledger through ID 4218, movements through ID 909, transfers through ID
414, audits through ID 176861, original application links, every unrelated
projection's original columns, all receipts, receipt overrides, identity
corrections, applications and application sources, runs/revisions/consumption,
losses/depletions, intercompany/processor/outside custody, and global ledger sum.
Only the commanded new records and permitted target projection lifecycle changed.

For a future authorized reconstruction, refresh evidence and fingerprints first:

* Room 73 / RED / 1242: **652 = 582 untreated + 70 application 29**. Within
  untreated stock, retain the transfer's **56 shared bins** without an invented
  receipt assignment. Original receipt-backed cohorts remain distinguishable.
* Room 73 / RED / 2750: **257 = 185 untreated + 72 application 28**, subject to
  the same complete origin and fresh-state command checks. Its 74-bin gap is
  receipt 2384; the observed projection has 111 untreated and 72 treated.

These are evidence-bound proposals, not production repairs. No other room count
is asserted from this scoped investigation. Any site can encounter the same old
whole-destination predicate when projections are incomplete or conflicting.

## Schema and release limits

Migration `20261009150448_IsolatedInventoryMovementCohorts` adds a non-null empty
default `CohortKey` and includes it in the two current-cohort unique indexes.
No origin, ledger, custody or treatment-application column changes. Existing
columns and quantities are preserved. SQL Server and PostgreSQL use the existing
provider-specific migration helper. There is no WinForms change or MSI requirement.

The SQL Server design-time snapshot passes the EF pending-model check. Production's
PostgreSQL configuration already accounts for provider-specific snapshot differences.
Backup #190 also has a documented historical migration-history mismatch: running
the complete EF chain tries to add existing `TotalDefectPercentageSnapshot`.
The rehearsal follows the existing [feature-migration guidance](../truck-receipt-release-review.md)
and generates only the reviewed migration range from
`20261002031709_BoundedBackupSnapshotProgress`. Do not replay the full chain or
rewrite history as part of this fix.

After cohorts are used, destructive migration Down deliberately refuses. This
was verified on the populated clone with an identical whole-database hash before
and after refusal. A pre-use schema roundtrip is covered by a PostgreSQL regression.
An application predating cohort support cannot be assumed to interpret multiple
same-signature cohorts correctly. **A production release needs a separately frozen
and rehearsed compatible rollback build that retains cohort-aware reads/writes
and the additive schema. Redeploying arbitrary old main after use is not an
approved rollback plan.** This draft does not authorize release or waive that gate.

Release preparation must use a fresh verified backup, current full readiness,
exact candidate rehearsal, maintenance/auto-deploy safeguards, bounded affected
workflow smoke checks, and independent production verification. This task did not
run a backup, alter production, merge, deploy, or reopen PR #275.

## Verification

The focused risk coverage includes empty and defective occupied destinations,
negative/excess/missing projections, partial movement, origin absence/duplication,
overdraw, stale double custody, Truck Receipt completion and retry, exact reversal,
source reconstruction, shared ancestry, later receiving treatment and movement,
effective/recorded chronology, schema roundtrip/refused downgrade, and architecture
contracts preventing the generic destination veto. Existing correction, packing,
loss, treatment and concurrency tests cover the shared engine's affected callers.

Final test counts, CI status and frozen commit are recorded in the draft PR and
completion report. Earlier failed development runs were fixed and rerun; an
unbounded test attempt exhausted local PostgreSQL lock memory and is not counted
as a pass. Subsequent runs use the repository's four-thread PostgreSQL settings.
The historical 68+104 backup fixture requires a different specific snapshot;
its skipped development invocation is not claimed as verified by backup #190.

Solution restore and Release build passed (69 existing warnings, including
ImageSharp advisories). Governance metadata and 22 Node tests passed. No pending
EF model changes. Final scoped verification: **120 passed, zero failed/skipped** on PostgreSQL 18, including restored-data concurrency. Mandatory governance and architecture contracts: **83 passed, zero failed/skipped**, with required-member execution verified. Changed-file formatting and diff checks passed. GitHub CI and the frozen commit are recorded in the PR. No browser/onsite operation or production release readiness
is certified here.

The first mandatory-contract attempt found the named disposable base database missing; it passed after that local fixture was created. No production service was used as a substitute. The final solution build had zero errors and five existing package advisories (the clean build also reported existing compiler warnings).
