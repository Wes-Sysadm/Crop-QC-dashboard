# Receipt-specific evidence in mixed custody

Development investigation, 2026-10-14 America/Los_Angeles. No production access,
repair, reconstruction, migration, merge or deployment is part of this task.

## Scope and rule assessment

Base/main: `3002d0774f52ee5e94aaef81dbd0d62c6bed335b`; branch
`codex/receipt-specific-custody-evidence`. Fetched main matches the starting state.
Specification 1.1.0, governance commit `1c263278f13eb31884a2872c2151257dee099789`.
Tracked knowledge matches main; the pre-existing untracked PR #271 release note
was preserved. Open PRs were inspected before branching. This is independent of
draft #282 (`0e4fd225370337de9cd2e7d633214d8530ce7f8b`), not stacked on it.
Older #268/#269 work concerns receiving timing/treatment correction, not this
selected-membership defect. No Admin UI or permission changes are included.

Applicable rules: INV-001–006, TRT-001/002, REC-001/002, MOV-001, AUD-001,
ROOM-001, OPS-001 and GOV-001; ADR-001–006 remain unchanged. This is an implementation
repair under existing shared-pool/receipt-specific rules, not a policy amendment.
The affected surface is receipt preview/commit evidence, exact selection in the
shared resolver, and identity/quantity/location operations that consume it.
Canonical writers, transaction isolation, readiness, origin validation and
destination admission remain unchanged.

## Confirmed causes and evidence

1. `InventoryCustodyEvidence` constructs residual allocations from original
   dispatch movements minus recorded returns/acknowledgements. It validates parent
   quantity, ledger debits, physical flow, source membership and current identity
   overlays. Its projections are synthetic custody evidence, not source-room
   balances. `InventoryCustodyTreatment` validates the immutable departure's
   receipt, signature, application links and business/recorded chronology.
2. The reproduced parent has 26 bins: seven on an exact receipt departure and
   nineteen on a departure with unassigned receipt ancestry. All 26 have proven
   custody and treatment. Pool-wide `ReceiptProvenance` is correctly Unknown.
   `InventoryReceiptAvailability` incorrectly used that pool-wide confidence as
   a prerequisite for selecting the independently proven seven.
3. Room receipt selection had a related conservative fallback: recorded replay
   rejected every unassigned cohort, including an unrelated older remainder.
4. Room discovery retains old receipt/ledger/movement references. After a complete
   withdrawal or identity correction, those references are historical evidence,
   not proof that the receipt still owns some of the old shared pool. Their global
   ancestry confidence incorrectly blocked later reads of the restored receipt.
5. Three baseline assertions expected current raw projection totals to equal the
   ledger. The fixture deliberately contains 29 projected versus 19 authoritative
   bins before the new seven-bin receipt. PR #281's isolated destination admission
   retains unrelated excess rather than silently repairing it.

## Corrected selection contract

- Require the operation's normal identity, quantity, custody, treatment, cutoff
  and fingerprint checks. Exact receipt requirements remain mandatory.
- Permit selected receipt membership proven by the existing current-flow proof,
  complete recorded replay, or validated immutable custody departures. For custody,
  residual slice totals must reconcile to the authoritative parent and every
  selected slice must pass the existing departure-treatment proof.
- Exact eligibility uses singleton receipt membership; a shared list containing
  the receipt ID is not an exact allocation. In the outside fixture, the scoped
  available quantity is seven while pool ancestry remains Unknown. The other
  nineteen are never allocated to that receipt.
- Recorded replay may preserve an unrelated unknown-ancestry remainder. If its
  evidence includes the selected receipt, selection remains blocked. An unresolved
  treatment on a different proven receipt does not contaminate the selected
  cohort. Selected unknown treatment or unresolved quantity never becomes proof
  of available untreated inventory; the existing PR #281 regression enforces this.
- Ignore historical-only references for current allocation only when conserved,
  resolved replay proves no surviving selected cohort, no shared cohort that may
  contain it, and no positive current projection claiming it. Keep the position
  and fingerprint in the receipt state, so this does not weaken concurrency.
- Include original source membership when discovering custody candidates; a
  missing or contradictory departure receipt ID cannot hide that position from
  the receipt reader.

No history is rewritten by reads. The replay uses recorded chronology while
retaining effective times. Correction reasons are not allocation evidence.

## Four baseline regressions

Untouched main was rerun on disposable PostgreSQL: three passed, four failed,
zero skips. The following evidence replaces the failing assertions:

| Regression | Authoritative result and replacement contract |
|---|---|
| Run cancellation after receipt identity correction | 45 bins: old identity 19, corrected target 26. Corrected receipt owns exactly seven target bins. Source current raw 19; target raw 36, with ten excluded as UnprovenProjection. Original consumption/movements/ledger/audits preserved; replay does not write twice. |
| Loss reversal after receipt identity correction | Same 19 + 26 conservation, seven exact target bins and ten excluded target projection bins. Immutable original loss allocation and retained history remain unchanged. |
| Reclassification into an existing identity | 45 bins remain. Source authority/raw 19/29; target authority/raw 26/36. Both excesses remain non-authoritative. Other receipts and previous movements are unchanged; correction cycle remains rejected. |
| Mixed outside custody, correction and normal return | Source room zero for the dispatched identity; outside custody 26 = seven exact + nineteen unassigned. Correction splits current identities without rewriting the original dispatch. Normal parent return restores nineteen old-identity and seven corrected-identity bins; target already held nineteen, so global room total is 45. |

Historical retired projection quantities are preserved separately from *current*
projection excess. Assertions check authoritative/available quantities, exact
receipt allocation, treatment signatures, excluded excess, and original ledger,
movement, audit and application/source rows. They do not demand erasing old rows.

## Additional regression boundaries

Repeated exact dispatch-allocation returns of 2, 3 and 2 preserve all seven
receipt bins across room/transit custody and leave the other nineteen unchanged.
One case retains unknown ancestry; another treats the older nineteen before the
new receipt arrives, preserving their separately proven receipt/application
membership while all seven later arrivals remain untreated. Original departure
and application-source records are unchanged. Replay is idempotent; changed intent
and stale versions are rejected.

Concurrent receipt identity commands use independent PostgreSQL transactions;
one commits and the other becomes stale, preserving 45 bins and a single
correction. The winning operation replays without writes.

Negative fixtures remove selected membership, contradict treatment, contradict
parent quantity, or remove the allocation movement for a committed withdrawal.
Reads and canonical correction commands must reject these states without writes.
Malformed history is constructed only through an explicitly localhost/test-marked
PostgreSQL fixture connection; no application capability or guard is disabled.

## Architecture and integration

Runtime changes are confined to `InventoryReceiptAvailability`,
`InventoryAvailabilityResolver`, and `InventoryCohortEvidence`. They remain
read-only; no new inventory subsystem, origin, command, writer capability, schema,
wire intent or migration is introduced. The two existing scanner candidates are
reviewed as read-only; their local collection additions are not physical writes.
INV-006 traceability now requires all four repaired regressions and the new exact,
negative, replay and concurrency cases through the existing CI contract runner.

#282 can integrate the resulting main change after this PR is reviewed and merged.
A read-only three-way comparison against #282's reviewed head found three
conflict regions in `CanonicalReceiptIdentityWorkflowTests.cs`: its diagnostic
calls/helper overlap the replacement assertions/helper. This is not a clean
automatic integration. Preserve those diagnostics if useful and retain all the
new authoritative assertions; do not restore raw projection equality.
Traceability and the reviewed writer-candidate manifest combine cleanly. Keep
both traceability additions and update #282's obsolete four-failure gap statement
after combined-candidate validation; retain
#282's separately approved Admin policy/UI changes only in its own lifecycle.
Do not deploy an arbitrary old pre-compensation binary as rollback.

## Verification and release boundary

Provider: disposable PostgreSQL 18.6 on localhost; no production copy.

- Untouched main: identity suite **3 passed, 4 failed, 0 skipped**.
- Final affected workflows: **195 passed, 0 failed, 0 skipped**, including all seven
  identity cases, seven new custody cases, PR #271 custody and PR #281 destination/
  treatment cases. The intermediate run caught an overbroad check on unrelated
  treatment uncertainty; narrowing it to selected evidence restored that existing
  contract. No failing test was excluded.
- Solution restore and Release build pass (final incremental build: five warnings,
  zero errors). EF reports no model changes. Changed-file format verification and
  `git diff --check` pass. Governance validator: 29 tests and 20-rule metadata pass.
- Mandatory contracts: **149 passed, 0 failed, 0 skipped**, including the required
  architecture suites. Final GitHub CI is recorded in the PR closeout.
  The first contract run exposed a missing empty test entry database; local
  fixture setup was corrected without changing the application or excluding tests.

Evidence: `artifacts/receipt-evidence-baseline/baseline.trx`,
`artifacts/receipt-evidence-confirmed/affected.trx`; contract TRX uses the standard
`artifacts/governance/<run>/contracts.trx` path. Generated results are not committed.
Changed shared evidence justifies receipt/custody/identity, treatment, resolver,
destination-admission and affected concurrency tests, plus mandatory architecture
and governance contracts. It does not justify unrelated email, photo or QC tests.

This development evidence is not production runtime or rollback rehearsal.
Release still requires owner authorization, frozen candidate, passing effective
CI, fresh independently verified backup, model/schema preflight, production-shaped
affected-workflow rehearsal, compensation-aware compatible rollback, strict
readiness, protected fingerprints, health checks and independent verification.
No production repair is proposed or authorized by this reader correction.
