# PR #284 — position-deterministic recorded-custody evidence

This is a synthetic-only development continuation of existing draft
[PR #284](https://github.com/Wes-Sysadm/Crop-QC-dashboard/pull/284), on
`codex/system-wide-transfer-eligibility`. It does not authorize release, a protected
restore, production access, repairs, or forward/rollback/handback operations.

## Baseline, authority and compliance

- Starting documentation head: `efb340f6a81214d29e9ac1ba9bff9c69cbcc6213`.
- Starting application: `8047d1a3202f4493b67f36cb70601e0ee9046205`.
- Fetched main/base: `df977d041dae2d449cf6fe5f8581e1f5cae41947`; no incoming integration change.
- Specification 1.2.0; governance commit `c17bd4deb477b57aa5ac1584175512d49318263b`.
  AGENTS, canonical rules, ADR-001–008, applicable inventory architecture,
  change-scoped testing and release procedures govern this change.
- INV-001–006, TRT-001/002, REC-001/002, MOV-001, ROOM-001, AUD-001,
  OPS-001 and GOV-001 remain unchanged. Ledger quantity, exclusive custody,
  exact/unknown receipt ownership, point-in-time treatment, immutable history,
  operator permissions, concurrency, readiness and release safeguards remain separate.
- Owner instruction: “Correct Recorded-Custody Scope-Dependent Fingerprints,”
  attachment `f7a20236-f082-431f-927f-0cf0028ecf04`, synthetic development only.
  The prior #204 approval is consumed. No new protected restore was requested or performed.

## Confirmed defect and scope

The [previous all-position report](pr284-all-position-readonly-2026-10-16.md)
established 18 unique positions / 2,932 bins with 36 failed room/command
comparisons. Warehouse comparisons passed. Quantities, treatment and receipt
evidence agreed; broad-only `RecordedArrivals`, watermark dependencies and
resolver proof did not. These are not missing bins and no actual failing
production command was submitted to establish a write failure.

`IncludeRecordedCustodyAsync` correctly identifies positive positions with an
unmatched historical `TransferIn` or `ReceiptAdminOverride`. It loads complete
history for their identities and validates parents, paired rows, chronology and
audited corrections. Its final loop previously attached replay output to *every*
original position found in that closure, even when that position had complete
modern incoming evidence and did not request replay. A same-identity peer in
another room therefore changed its proof and fingerprint. All 18 reported
positions had `localRequested=false`.

## Selected proof contract

The loader now retains an in-memory set of the originally requesting
`(RoomId, Identity.Key)` positions. Only those positions receive replay arrivals,
associated applications and historical watermark dependencies. The original
request predicate is unchanged. Full intermediate history is still loaded and
replayed, including modern-backed peers, depleted sources, reversals, corrections
and prior hops needed to prove a requesting position.

This distinguishes proof required by a position from context used to prove
another position. It does not discard any ledger, movement, receipt, projection,
application or existing local watermark input. Modern movement evidence remains
primary in `InventoryEventReplay`; related contradictory modern evidence still
prevents legacy fallback. Exact parent and audited correction checks, replay,
fingerprint serialization, SQL, row bounds and writer fences are unchanged.

Only `InventoryEvidenceLoader.RecordedCustody.cs` changes application behavior
(six added lines and one replaced line). Its reviewed normalized LF SHA-256 is
`8cd8ca9223f5c652c8f4c9603b400073d6867227d5e22e4671b99e03bbd97e7e`.
The [capability review](transfer-eligibility-capability-review-2026-10-15.md)
preceded the read-only content-lock update. No writer, migration, model,
configuration, runtime or policy change is included.

## Regression evidence

The new PostgreSQL theory uses all 18 report references as quantity/arrival-count
shapes, with synthetic identities and canonical moves. References are test labels,
never application special cases. The one-bin case has a historical 30-bin arrival
and 29 committed consumed bins; it remains one bin. Other cases have one to four
modern arrivals, a second modern peer, a depleted original source and a positive
legacy peer. Treated and untreated variants retain their own recorded membership.

| Report reference: room / lot | Current bins | Arrival-count shape |
|---|---:|---:|
| 9 / 9541 | 1 | 1 |
| 15 / 1011 | 350 | 1 |
| 15 / 3152 | 61 | 2 |
| 15 / 3162 | 67 | 1 |
| 15 / 9092 | 126 | 2 |
| 15 / 9362 | 196 | 1 |
| 15 / 9372 | 136 | 2 |
| 15 / 9682 | 252 | 2 |
| 17 / 3152 | 193 | 3 |
| 17 / 9362 | 37 | 1 |
| 18 / 3032 | 55 | 1 |
| 18 / 9092 | 184 | 1 |
| 18 / 9332 | 696 | 1 |
| 18 / 9362 | 66 | 1 |
| 18 / 9380 | 29 | 2 |
| 18 / 9722 | 160 | 1 |
| 63 / 2200 | 296 | 4 |
| 63 / 2910 | 27 | 2 |

All **18 fail before the application fix**, at the complete serialized-evidence
comparison. All **18 pass afterward**, including broad/warehouse/room/command
evidence and resolver equality, successful use of a broad selection by the narrow
writer, durable idempotent retry, stale submission rejection, exact remaining
quantities and unchanged full-table snapshots during reads. These are shapes of
the reported failures, not claimed reproductions of every protected transaction.

The existing historical-scope test no longer exempts depleted positions from full
evidence equality. The initial post-fix run passed all 28 new-pattern and existing
historical-scope cases, zero skips. The expanded mixed fixture adds modern peers
to the 59-room historical corpus: mixed applications, arrivals after treatment,
reversal between legacy hops, partial shared ancestry, exact receipt cohorts,
audited relocation, repeated/depleted rooms and unrelated identities. Full
evidence and resolver comparisons include every position in each room. Real
canonical movement invalidates an older selection; unrelated proof is unchanged.
Contradictory membership is injected into detached evidence only, preserving the
persisted writer fence. Sealed sources and exact-receipt requests on unknown
ancestry remain blocked.

Final combined validation, matched SQL measurements and cleanup are recorded in
the companion evidence file and results below. Earlier unrelated evidence is
retained rather than rerunning an unrelated full suite.

## Final validation and matched synthetic performance

**GO for focused human review only. NO-GO for release advancement.**

- 249 distinct scoped cases pass, including all 221 mandatory cases / 91 required
  methods and structural suites, with zero final failures or skipped bodies.
  The combined run passed 248 cases; the new mixed fixture initially asserted a
  blanket resolver block instead of event-replay rejection. Its test-only correction
  checks replay and actual command seal enforcement at their existing boundaries;
  its final rerun passed, including explicit five-bin application membership and
  reversal assertions. Final review also added explicit sealed-destination
  rejection with a full-table no-write assertion to that same mixed contract;
  its targeted final rerun passed on a new synthetic-only instance, subsequently
  deleted. Both source and destination seal checks are executed. No application
  guard changed to satisfy the test, and the case count remains 249.
- Existing recorded-custody, receipt-origin, treatment/time/reversal, origin guard,
  room/inter-crew route, cross-company receipt, return/audit, concurrency,
  idempotency, strict readiness/conservation and architecture contracts passed.
  The synthetic 626/720 original-proof and unfunded receipt-change rejection remain
  covered; protected positions were not accessed again.
- 29 Node governance tests and metadata checks pass. Solution restore, final
  Windows and Linux builds, model check, changed-source formatting and whitespace
  checks pass. Existing build/workspace warnings are retained. No migration or MSI
  is needed. Required final-head GitHub CI is recorded in the PR Checks; local
  success alone is not CI or release authorization.
- Main was refetched and remained unchanged. Report-only knowledge sync reports
  the intentional uncommitted changes and preserved unrelated untracked files;
  it does not integrate or overwrite them. Specification/ADR source remains current.
  A raw Windows PR-event check flags CRLF versus Git LF as a policy change;
  the specification's tracked blob is identical to main
  (`999647ab311aa9127a84b84d9afc8669c6571481`). Policy evolution is checked against
  canonical Git content and again by unchanged Linux CI, without changing the
  governance checker or waiving required contracts.

Matched measurements use the previously frozen `8047d1a` Linux image
`sha256:6e02e9ea0c134524b524d573b0b1ce7e097580b2b58f80eacdb46eda4089e223`
and the candidate build against the same synthetic 61-room corpus. Linux x64,
.NET 9.0.21 and PostgreSQL 18.4; application 2 CPU / 2 GiB, database 2 CPU / 3 GiB.
One cold and two warm iterations per workflow, without concurrent builds/tests
during timing. Sessions are read-only. `random_page_cost=1.1`, `work_mem=26468kB`,
parallel workers per gather 1, JIT on with thresholds 100000/500000/500000.
No timeout, planner or production configuration change hides query cost.

| Read | Baseline warm ms | Candidate warm ms | SELECTs / rows, both | Aggregates, both |
|---|---:|---:|---:|---:|
| Legacy room evidence | 217.3–233.1 | 223.3–237.7 | 30 / 1,053 | 2 |
| Modern room evidence | 27.8–30.3 | 32.8–36.6 | 11 / 19 | 1 |
| Room-move selector | 287.9–332.6 | 265.0–289.8 | 33 / 1,098 | 3 |
| Inter-crew selector | 3,747.5–3,813.2 | 3,282.9–4,026.8 | 37 / 27,152 | 2 |
| Outside selector | 3,567.2–3,725.7 | 3,320.1–3,404.0 | 33 / 27,147 | 2 |
| Processor selector | 3,491.8–3,889.7 | 3,153.6–3,433.2 | 35 / 27,728 | 2 |
| All-room evidence | 2,339.7–2,766.4 | 2,337.5–2,364.8 | 30 / 25,167 | 1 |

Every SQL statement hash, SELECT count, row count, aggregate count and output
position count matches. Evidence rows remain 899 legacy-room, five modern-room
and 21,980 all-room. No extra broad aggregate returns. Millisecond-scale variation
in the unchanged modern-room path is not an added query; two warm samples are not
an SLA or production-capacity certification. Peak application working set is
304.3 versus 312.4 MiB. Per-round CPU, allocations, plans, execution times and
hashes are in the [companion evidence](pr284-recorded-custody-scope-2026-10-16.json).
Captured narrow aggregates execute in 72.454/127.989 versus 79.761/133.024 ms;
outside aggregates in 1,560.754/2,517.305 versus 1,450.113/2,480.733 ms. Captured
origin lookups remain 0.031–2.241 ms. Captured plans do not invoke JIT at the
unchanged enabled thresholds. Full raw SQL/EXPLAIN artifacts remain locally
under `local-test-runtime/pr284-recorded-scope`, with paths and hashes in evidence.

Exact output comparison covers 1,008 synthetic positions and all seven workflows.
Only redundant arrival proof, its dependencies and resulting fingerprints differ:
two positive modern positions and 363 depleted positions. Ten depleted positions
also lose *imported reader application context* that existed solely through the
removed arrivals. Each removal is bound to an application ID in those exact
arrivals; retained application arrays remain subsets and current quantity is zero.
Every positive position's application membership is unchanged. Actual stored
applications are unchanged, and required intermediate applications remain in
historical closure for requesting positions. No other evidence, quantity,
receipt ancestry or operative selector field differs. This explicitly explains
the initial comparison stop at the depleted application arrays; it is not an
exception for unexplained treatment loss.

All 112 synthetic database tables remain byte-content equivalent by ordered
row-count/hash snapshots before and after each application. Combined fingerprint:
`6dca695f1c88f08b35aa6221b0ca888139950d5d2322de6df9e4d34784a6cf19`.
Cleanup identities and confirmation are recorded in the companion evidence.
No production resource, protected backup, migration or business data was accessed.

## Release limits and next gates

The six unsealed historical gains / 1,179 bins, twenty previously hidden positive
positions, 56 independent origin failures and WP-7 ledger 1187 rejection are
protected historical findings. This task verifies their synthetic guard contracts;
it does not claim a new protected-data run. The synthetic 626/720 equivalents and
their original-receipt dependencies remain mandatory contracts. No origin failure
is repaired or waived, and selector availability is not final origin authorization.

After focused human review, obtain separate explicit approval for **one new
read-only #204 restore**. Recheck every one of the 452 positive positions in all
scopes, including all 18 previous failures, with exact treatment membership and
all 56 origin failures preserved. Only after that gate passes may a separately
authorized forward/rollback/handback rehearsal proceed. These stages are not
combined. Fresh verified production backup, strict readiness, compatible rollback,
authenticated affected routes and independent verification remain release gates.

PR #284 stays draft, unmerged and undeployed. No production data, projection,
receipt or treatment repair is proposed by this read-only correction.
