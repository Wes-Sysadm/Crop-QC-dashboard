# Lamb15 Conventional Fuji: projection identity admission defect

Investigation: 2026-10-13. Development base and observed web release:
`2f66134938f768a1d467074037cc3dbcf308d0e5`. No production mutation, backup
trigger, merge or deployment was performed. Production access was read-only.

## Rule compliance and scope

Specification **1.1.0**, governance source
`76fbf429cb342c89ea1a5eae3e43ac507cd7c79f`; current-main knowledge synchronization passed.
Applicable: INV-001–006, TRT-001–002, REC-001–002, MOV-001, ROOM-001,
AUD-001, OPS-001 and GOV-001; ADR-001–006 and existing ADR-007 lifecycle authority.
Transactions establish quantities and custody; projections represent that evidence.
Treatment requires point-in-time physical presence, not a room label. Receipt
ancestry requires exact evidence; shared stock remains shared. No business rule changes.

The changed dependency is destination preparation plus source normalization and
receipt-scoped cohort selection. Receiving, room moves, truck receipt completion,
receipt corrections/voids/treatment, source consumption, and reconstruction share
these components. Historical receipts, ledger entries, movements, applications,
application sources and audit history must remain intact. New commands still record
their own authorized deltas and audit evidence atomically. No new writer or origin.

## Confirmed defect and limits of attribution

There is one emitter of the exact exception, in
`CanonicalProjectionFactory.CurrentAsync`. It selects current rows by room,
operation cohort, treatment signature, receipt and normalized identity, then rejects
multiple matches or a mismatching raw identity, warehouse, treatment state or
application-ID set. That guard remains unchanged.

On baseline main, `ReceiveStockAsync` calls `PrepareDestinationAsync`, which
opportunistically normalizes an otherwise operable untreated destination. The old
normalization plan retired only positive rows, then requested an empty-cohort
replacement. A **zero-bin current legacy status alias remained in that same key**.
The factory found its normalized identity but rejected its raw spelling. A derived
zero-bin row thereby vetoed a legitimate new receipt before stock creation.

This exact exception was reproduced on a disposable restore of backup **196** for
Lamb15 Fuji lot **1033**, segment **591**, with the production/base implementation.
The corrected implementation accepts new receipts for both lot 1033 and lot **3202**
(segment **629**) while preserving the original rows byte-for-byte.

The user's precise failed request has not been correlated: its lot/receipt ID,
request timestamp and request/correlation identity were not supplied. An exact-message
Render log search over October 6–13 returned no entries. The executor converts this
InvalidOperationException to a blocked command result, so lack of an exception log
does not prove no failure occurred. The restore establishes a real matching defect,
not the identity or outcome of an unobserved operator request. No physical count or
external paperwork is needed to establish this software defect.

## Authoritative Lamb15 evidence

Consistent canonical evidence was captured in a verified read-only RepeatableRead
transaction at **2026-10-13T16:10:04.080432Z**, covering 586 positions in 21 rooms.
Queries used Render workspace `tea-d7uc4ippo60c73ebn4mg` explicitly, PostgreSQL
`dpg-d8cru6reo5us73a2bgt0-a`. Supplemental read-only SQL reconfirmed the balances.

Room **8** is `LAMB-15` / `LAMBCA15`, Lamb Street 15, warehouse **1 / EBS**,
active, unsealed, capacity 1,859. Comparison room **23**, `EVANS-BKT` / `KTSTORED`,
is also active/unsealed in EBS, capacity 5,000. Fuji profile **1** is Conventional,
non-organic. The reproduced exception is neither a seal nor capacity rejection.

| Lot / GrowerLotId | Recorded net quantity calculation | Current authority / projections |
|---|---|---|
| 1033 / 404 | 743 receipt bins + 52 transfer-in − 2 committed receipt correction − 793 consumed | 0 / 0 |
| 3202 / 519 | 9 committed correction + 6 receipt bins − 15 consumed | 0 / 0 |
| 9333 / 575 | 14 receipt bins − 14 consumed | 0 / 0 |
| 1022 / 402 | Receipt 2610: +4 | 4 / 4 |
| 9335 / 577 | Receipts 2566, 2591, 2599, 2624, 2635: 2+12+8+5+2 | 29 / 29 |

All five positions have verified room custody in the canonical evidence, operable
results and matching authoritative/projection quantities. Thus Lamb15 contains
**33 recorded Conventional Fuji bins**, all in lots 1022 and 9335, not in 1033/3202.
This is a room/identity reconciliation, not a claim about all Fuji custody elsewhere.
The committed +9 correction for 3202 is retained as a ledger effect, not rewritten
as a new receipt or interpreted from its reason text as physical allocation proof.

The [complete bounded Fuji ledger](lamb15-fuji-ledger.csv) records every returned
ledger ID, amount, receipt/parent, effective timestamp and recorded timestamp.
For 1033, the first 329 receipts less the two-bin correction and 327 consumed reached
zero on September 12. A later 52-bin transfer (RoomTransfer **390**, ledger **3646**)
and 414 more receipt bins were consumed by ledger entries **3716, 3717, 3728**.
For 3202, ledger **3360** (+9) and **3633** (+6) were exhausted by **3726/3727**.
No quantity is inferred from the displayed room balance.

Segment 591's raw key ends `FUJI|CONVENTIONAL|False|CONVENTIONAL` rather than
canonical `FUJI|CONVENTIONAL|False|`. It has zero bins, null receipt, empty CohortKey,
Untreated/u, no application links, version 8. Segment 629 has the equivalent
lot-3202 defect, version 3. Both are still Current. Zero-bin segment **592** for
9333 already has canonical spelling. The successful current 1022/9335 receipts
also use canonical spelling; 1033 Fuji receipts have succeeded in EVANS-BKT,
including receipt **2492 / TR110059**. These comparisons establish that the same
variety is not universally ineligible and the room is not universally closed.

Other current Lamb15 fruit includes GSMT lots 1042 (27), 3140 (221), 9602 (524).
Those successful populations do not make a conflicting zero-bin Fuji key valid.

Lamb15's eight historical applications (**4, 6–12**, August 21/25) are receiving
applications tied to other receipts (**790, 930, 927, 936, 937, 938, 942, 944**).
The five Fuji evidence positions contain no applicable treatment events. The
1033/3202 zero-bin segments have no application memberships. New fruit must not
inherit those older room-associated receipt treatments. No treatment application
was edited or assigned to historical Fuji.

## Blast radius and classification

The production-wide SQL shape scan found **55** depleted current redundant-status
alias rows in **20 rooms**. Exact segment, room, lot, variety, receipt and cohort
identifiers are in [the alias census](lamb15-projection-aliases.csv).

The captured canonical evidence also contains 103 positions with some zero-bin
raw-key mismatch. Applying the old destination normalization predicates and
replacement-receipt match identifies **40 static collision candidates in 17 rooms**;
39 are depleted, and EVANS-7 / lot 9682 / GALA retains 387 authoritative bins.
See [the bounded baseline scan](lamb15-baseline-candidate-scan.csv). This scan is a
susceptibility assessment, not 40 failed HTTP requests: independent origin proofs,
room eligibility, and other guards can stop an operation earlier. It does not claim
all possible projection defects across the database were enumerated.

Confirmed classification for Lamb15 1033/3202: **stale derived identity**, with
conserved authoritative quantities and no implicated treatment links. No missing
custody, duplicated physical Fuji, application contamination or concurrency race
was established for those pools. Other scan entries remain susceptibility findings;
their unrelated blockers are retained in the CSV, not relabeled as safe inventory.
Concurrent timing is not required to reproduce the confirmed exception.

## Systemic correction

1. Destination preparation validates admission and recorded origin/custody, then
   preserves the baseline. It no longer normalizes or reconstructs the destination
   as a prerequisite for incoming fruit. Existing operation-key cohorts isolate
   new arrivals. Exact ledger/projection deltas and unchanged historical destination
   rows remain mandatory; reconciliation diagnostics/audits remain active.
2. A zero-bin alias alone no longer forces a proven balanced mixed-treatment source
   through untreated-pool replacement. If a whole-pool normalization is actually
   independently proven and necessary, its audited plan includes depleted rows as
   well as positive rows, preserving their keys, receipt links and application links
   in retirement history. No physical quantity is added by retirement.
3. Exact independently proven incoming receipt cohorts remain selectable even when
   an older untreated pool has stale projections. Treatment certainty for the old
   pool does not erase the newer receipt's proven ownership. Existing receipt-flow
   proofs, unexplained-withdrawal rejection and authoritative caps are unchanged.
4. A later source normalization reuses the existing selective reconstruction proof
   to retain proven incoming receipt populations and reconstruct only the proven
   residual. It cannot collapse those receipts into an unassigned pool. Ambiguous
   old ancestry stays shared; correction allocations across rooms remain explicit.

No exception is broadly swallowed or converted to a warning. The strict factory,
source eligibility, sealed/inactive/wrong-warehouse restrictions, origin validation,
negative balance, duplicate/exclusive custody, stale state, serializable retries,
receipt ownership, application membership and reconstruction approval guards remain.

### Emitter/caller review

The factory is reached by receiving; room moves and adjustments; truck receipt and
custody returns; transit returns; receipt quantity/location/identity corrections;
stock/baseline operations; treatment assignment/reversal; compensating movement/loss/
run restoration; source normalization; recorded cohort reconstruction; and explicitly
approved projection reconstruction. Incoming command paths use their operation cohort.
Recorded reconstruction uses its separate `r:` namespace and retires every exact
current row after event replay and origin proof. Explicit maintenance reconstruction
retains preview/approval/fingerprint/concurrency guards. The empty-cohort source
normalizer was the specific caller leaving depleted aliases behind.

A new architecture assertion prevents destination preparation from invoking
normalization, recorded reconstruction or the projection factory. Runtime tests
prove the authority boundary; the source-text assertion alone is not a proof.

## Validation and release status

Final local verification: **194/194 affected tests**, **94/94 mandatory governance/
architecture contracts**, **29/29 Node governance tests**, zero skipped or failed.
The contract run includes all 28 receipt-override readiness cases: valid identity
correction passes and 27 deliberately corrupted evidence variants remain blocked.
Solution restore/build passed (0 errors; existing warnings), EF reports no pending
model changes, changed-C# formatting and diff whitespace checks passed. No full
unrelated application suite was run. Final PR/commit and CI are recorded in the PR
closeout. The affected
suite includes receiving/API parity, mixed treatment and later arrivals, repeated
partial moves, exact partial truck receipt following a recorded return, mismatched
receipt rejection, source/custody guards, receipt corrections/voids, reconstruction,
conservation, replay and concurrency. New mandatory governance contract entries
retain these behaviors in CI. Existing tests that assumed receiving repaired an
unrelated destination were changed to assert preserved excess plus exact authority.
Reconstruction tests now protect proven cohorts rather than labeling every old
current row authoritative, and select exact correction allocations after movements.

The initial wider run inadvertently used backup 196 for an older fixed-quantity
ActualRun restore test expecting 68/1,122 bins; its current restored selector returned
501. That old snapshot-specific test is not claimed as passing. It was excluded from
the final affected set; the directly targeted Lamb15 backup-196 regression is run
explicitly. No production dependency was substituted for a disposable fixture.

No schema/model/migration or installer change is required. No QC Station/MSI build
or hardware verification is implicated. Linux production-runtime rehearsal,
authenticated affected-route checks and final production readiness remain release
gates for the next authorized lifecycle; local results are not deployment clearance.

### Backup and rollback protection

Production backup history reports nightly **197** succeeded, verified
2026-10-13T09:11:14.262283Z, 19,334,319 bytes, SHA-256
`e226b9d106359f7ae4d27d0ab39d66a11c02a1d5a784e5ab5c5b523024fc8c97`.
Its worker deployed commit is `95518dec131d44b01e2dc8fcfcbb82f508a8f399`, distinct
from the web release. Lease row 1 has null LeaseId/ExpiresAt. These are read-only
status observations, not an independent download of backup 197 in this task.

The isolated rehearsal used previously independently downloaded and verified backup
**196**, `cropqc-production-predeployment-20261013-041822.zip`, 19,328,730 bytes,
SHA-256 `1c3563f6270ea26a5370e234ac723b5d194f4f0bc2f7771976048288b665bb31`.
Its compressed SQL component SHA-256 is
`f1676db67bea3dd3787957f676cb0602c4d7fcfe11cc192e31b819ccb143134f`.
Archive/manifest/dump checks were recorded by the previous release; this task
successfully restored it again locally with stop-on-error and a single transaction.

On a separate backup-196 clone, the candidate recorded 7 Fuji bins, applied a
receipt treatment to those 7, received 5 later untreated bins, and moved 3 untreated
bins to EVANS-BKT. Unchanged main **2f66134938f768a1d467074037cc3dbcf308d0e5**
then read the mixed Lamb15 cohorts and moved 1 untreated bin back. Total authority
was **74,429 → 74,441 → 74,441**, treatment remained exactly 7 bins, and ten
protected preexisting transaction/treatment/audit tables retained their hashes in
each phase. Binary hashes and operations are recorded in
[the rollback evidence](lamb15-rollback-evidence.json). No Down migration or schema rollback was used. This verifies local
application/data compatibility, not an exact frozen Linux deployment package.
Rollback reintroduces the original depleted-alias admission defect; it is recovery
protection, not an alternative fix for that workflow. Auto-deploy is still disabled.

Before release: freeze the merged candidate and compatible application rollback,
rehearse the exact runtime, obtain a fresh independently verified production backup,
capture authoritative/treatment fingerprints, pass mandatory readiness, and stop on
any failed safeguard. Nothing in this development PR authorizes those production writes.

## Separate repair proposals and uncertainties

No Lamb15 quantity repair is indicated: 1033/3202 already reconcile at zero. Retiring
their harmless depleted alias rows is optional historical projection maintenance,
not required for incoming receipts after this code fix. A proposal would preserve
their zero quantity, raw identity, receipt/application links and audit before/after
state; require fresh proof/preview, explicit owner repair authorization, verified
backup, concurrency checks and independent verification. No repair was executed.

Occupied or treatment-ambiguous pools in the census require their own complete
evidence-bound assessment; the shape scan is not repair authorization. The separate
210-bin historical RED proposal is untouched. The original ATGL report is not
reinterpreted as RED or Fuji. Missing operator request identity/time remains the
specific limit on attribution of the reported Lamb15 attempt.
