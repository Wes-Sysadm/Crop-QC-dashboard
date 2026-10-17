# PR #284 — reuse canonical groups for broad historical closure

Status: synthetic correctness and comparative performance **PASS**; final-head
CI is recorded in the PR closeout and remains required for focused human review.
Production release remains **NO-GO**. This report continues the failed
[production-shaped performance gate](pr284-approved-restore-performance-2026-10-16.md);
it does not claim a new protected restore, rollback or handback.

## Authority and compliance

Owner Wes's “PR #284 — Eliminate Broad Historical Aggregation Regression” instruction
authorizes this narrow synthetic-only continuation of draft
[#284](https://github.com/Wes-Sysadm/Crop-QC-dashboard/pull/284), on
`codex/system-wide-transfer-eligibility`. Starting head is
`7367a9d5d497111f2df200404ccf271d7b839dab`, preceding application source
`efe895913bc54080a52746897a9dea19e12bcf52`. Fetched main/base remains
`df977d041dae2d449cf6fe5f8581e1f5cae41947`; no integration update was needed.
Nine unrelated local investigation reports were preserved.

Specification 1.2.0, governance commit
`c17bd4deb477b57aa5ac1584175512d49318263b`, ADR-001–008 and INV-001–006,
TRT-001/002, REC-001/002, MOV-001, ROOM-001, AUD-001, OPS-001 and GOV-001 apply.
Authoritative origins, ledger and custody establish quantity. Point-in-time
treatment and recorded receipt ancestry remain evidence requirements. Projections
cannot create stock or establish treatment. No policy, permission, physical writer,
model, migration, command fence, readiness rule or release safeguard changes.

## Confirmed cause and selected correction

The prior restored read demonstrated three canonical aggregates on broad selectors.
The first two already existed on production main. The third came from
`IncludeRecordedCustodyAsync`, which constructed a new ledger reader for historical
closure even though its parent read had already aggregated many of those rooms.
Its 59-room/42-label query returned 492 groups and cost 3,715.784 ms, including
3,628.790 ms JIT. This task does not optimize the two baseline queries.

Both proposed approaches were evaluated:

| Approach | Correctness and performance assessment |
| --- | --- |
| A — reuse already-loaded canonical groups | Selected. Retains raw persisted groups, including depleted positions, before canonical-year inference and Bins Run reconciliation. Complete evidence and reconciliation run again for the closure. Fully covered broad scopes eliminate the third aggregate; uncovered rooms use one existing bounded aggregate. No per-identity query loop. |
| B — batch independent connected components | Not implemented. It would require another dependency partitioner covering Bins Run aliases, repeated visits, receipt corrections and room-wide baselines. A complete large component could still cross the JIT threshold, while small batches repeat canonical work and require extra global accounting. A removes proven duplicate work without choosing an arbitrary batch size or replacing the existing dependency graph. No comparative implementation timing is claimed for B. |

Only three application files change:

1. `InventoryEvidenceLoader.cs` creates one ledger reader per read and passes it
   into closure. Its original receipt-revision predicate and full evidence loading
   remain unchanged.
2. `InventoryEvidenceLoader.RecordedCustody.cs` threads that reader through the
   existing closure. Requested keys, related rooms, exact debit/credit pairs,
   parent/audit validation, replay and fingerprint construction are unchanged.
3. `RoomInventoryLedgerQueryService.cs` retains at most 2,000 persisted groups
   within that read. It reuses only complete room coverage with the same transaction
   ID and effective cutoff, without a warehouse/profile/overlay restriction.
   A warehouse-filtered read cannot certify complete room coverage and takes the
   existing fresh path. Missing rooms use the same canonical SQL arithmetic and
   unfiltered room-baseline subqueries. One bounded provider-side latest-ID lookup
   applies the exact effective-label predicate to retained groups; normalization is
   not replaced with an in-memory approximation. The combined group count is checked
   before inference/reconciliation, so separate valid batches cannot evade the cap.

This is neither a cache of finished availability results nor a cross-request cache.
The same RepeatableRead/Serializable evidence snapshot remains mandatory. Capture
does not omit zero balances. Historical source rows, connected Bins Run labels,
nullable fields, correction-trigger dependencies, treatment applications/reversals,
and unknown ancestry are still queried and replayed. No application SQL writes
were added. The three registry hashes were updated only after the
[read-capability review](transfer-eligibility-capability-review-2026-10-15.md).

## Synthetic correctness

`HistoricalCustodyScopeTests.BroadHistory` extends the existing 286-bin Frenchman
case with 41 independent ten-hop chains, 34 additional rooms (59 relevant rooms),
42 historical labels plus one connected old Bins Run label, similar grower labels,
nullable historical Bins Run identity, later receipts and unrelated active occupants.
The 500 neighboring identities have 6,000 synthetic receipts, reproducing the
correlated origin-lookup cost without changing the selected historical scope.
It retains the original repeated depleted-room visits and audited location correction.
Two additional applications include a reversal between hops; later arrivals remain
separate. Frenchman remains 286 bins: 220 with shared ancestry and 66 with exact
receipt ownership, with its proven application membership intact.

Three new regression methods cover the changed assumptions:

- Full broad evidence equals a fresh warehouse-scoped reference. Every positive
  position equals its single-room source evidence, including fingerprint. Every
  depleted position retains exact ledger rows and quantity. Mutating a relevant
  receipt, parent, correction or application invalidates the affected fingerprint
  and agrees with warehouse-filtered command resolution; an unrelated identity is
  unchanged. The read-only phase preserves all fixture table fingerprints.
- A retained aggregate cannot be used at another effective timestamp or in a new
  transaction after a committed change.
- Provider whitespace/identity behavior agrees with a fresh read, and the combined
  cached/missing result rejects more than 2,000 groups even when the missing query
  alone returns exactly 2,000. Evidence is never truncated as complete.

Depleted-only reads intentionally do not request historical closure. A broad read
may enrich such a position as context for another room's positive stock. This
pre-existing difference in non-selectable historical context is not reported as
source fingerprint equality; positive selectable positions must agree exactly.
Complete pre-fix/optimized broad output equality independently includes depleted
positions. No stock is inferred from that context.

## Validation and measurement record

The local selected run passed **211 cases, zero failures/skips**. An independent
TRX audit verified all **85 required methods / 198 mandatory cases**, including both
architecture suites. This includes the historical custody/identity, treatment,
receipt, origin/seal, actual concurrency/idempotency and internal/cross-company
route dependencies. Three new methods are required under INV-005. Earlier 198/195
and 275-case reports remain evidence only for their recorded checkpoints; their
counts are not added to this run. The protected all-facilities restore test and the
optional separately configured identity concurrency body were not selected.
After strengthening the fixture, its final focused case passed again, including
explicit 19-bin treatment/reversal and seven-bin later-arrival assertions. This
is overlapping evidence, not an additional unique test count.

Governance: 29 tests passed; all 20-rule metadata checks passed. Solution restore
and Release build passed (74 existing warnings, including existing NuGet advisory
warnings). Linux affected-test build passed. EF reports no pending model change;
no migration is required. Focused formatting and whitespace verification passed
after formatting the new test initializers. Final-head GitHub checks are recorded
in the PR closeout; a local pass is not a CI claim.

The first pilot had 242 historical aggregate groups and a 269 ms third query.
A second reached 526 groups but only 333.769 ms, because correlated lookup estimates
remained below the expensive JIT threshold. Both preserved complete outputs but
are excluded from performance acceptance. The final fixture retains 526 groups and
adds neighboring receipt history. No JIT, planner threshold, timeout or resource
workaround was applied.

### Final matched benchmark

Sequential baseline, `efe8959`, then optimized application; pinned Linux x64
.NET/ASP.NET 9.0.21 and PostgreSQL 18.4. Each application and the database had two
CPUs and 2 GiB. JIT remained on with 100000/500000/500000 thresholds;
random_page_cost 1.1, work_mem 26468 kB, max_parallel_workers_per_gather 1.
Reads used default_transaction_read_only=on. No builds/tests ran during timing.
Warm ranges are rounds 1–2; round 0 is reported separately as process first pass,
not a disk-cold or production latency claim.

| Workflow | Baseline warm ms | Pre-fix warm ms | Optimized warm ms | Reader SELECTs baseline / pre-fix / optimized |
| --- | ---: | ---: | ---: | ---: |
| Single-room | 291.2–294.6 | 331.0–341.1 | 337.5–338.7 | 10 / 28 / 29 |
| Room transfer | 546.5–558.7 | 572.9–604.8 | 589.4–596.0 | 13 / 31 / 32 |
| Inter-crew | 7,483.6–7,564.8 | 11,434.1–11,599.9 | 7,639.6–7,751.2 | 18 / 36 / 36 |
| Outside warehouse | 7,444.5–7,463.6 | 11,099.8–11,530.6 | 7,666.9–7,861.2 | 14 / 32 / 32 |
| Processor | 7,621.5–7,659.1 | 11,285.0–11,675.9 | 7,752.8–7,956.9 | 16 / 34 / 34 |

The third historical aggregate reproduced **3,744.764 ms** execution,
**3,684.020 ms JIT / 591 functions**, cost 3,148,845.03, 59 rooms / 43 connected
labels / 526 rows, 102,944 shared hits and zero physical reads. It is absent from
the optimized broad plans. The two unchanged baseline aggregates remain
(optimized plan executions 3,515.922 and 3,758.145 ms). The additional key lookup
replaces one aggregate, so broad SELECT count stays the same; narrower reads add
one lookup while still loading missing rooms. Optimized single-room closure
aggregated 44 missing-room groups in **15.465 ms**, cost 81,318.06, with no JIT.
Necessary proof loading remains roughly 0.2–0.4 seconds over baseline on these
heavy broad samples, not the prior additional 3–4 seconds. Single-room/room-transfer
warm ranges remain comparable to `efe8959`; their existing baseline SQL is slower
in this deliberately denser synthetic dataset than in the earlier restore.

Inter-crew process first passes were 9,534.0 / 13,577.5 / 9,774.8 ms. All first-pass
samples, allocations, process CPU, database cgroup resource snapshots, query counts,
plan/JIT/buffer metrics and runtime/DLL hashes are in the
[measurement record](pr284-broad-aggregate-reuse-2026-10-16.json). Peak application
working sets were 275.1 / 308.6 / 293.9 MiB. No OOM, timeout or temporary plan writes.
Reader counts exclude transaction control. `rowsRead` includes terminal reads;
`RowsLoaded` is proof-record accounting, not every SQL row. Single-room proof rows
remain 639 pre/post-fix; all-room proof rows remain 15,678. Full raw SQL/EXPLAIN and
outputs are retained locally, with artifact hashes in the measurement record.

Complete pre-fix/optimized outputs match for all five workflows and the additional
all-room read: **1,005 evidence positions / 61,425 authoritative bins**. All **112
table counts/hashes** are unchanged across measurements. Independent offline replay
then checked **577 outside, 577 processor, 21 inter-crew and 21 room-transfer
options** against canonical quantity, treatment membership and fingerprints.
Existing disabled room entries without command fingerprints were matched by exact
identity and required to remain blocked. No unknown treatment was authorized as
untreated, and shared receipt ancestry was not assigned to an invented receipt.

This synthetic result supports focused human performance review after final CI.
It does not certify the actual restored coverage. Large missing-room closures and
warehouse-filtered reads still take the conservative fresh path; the next protected
performance gate must verify actual requested-room coverage and query plans.

### Cleanup

At 2026-10-16 17:38:43 UTC, task-owned synthetic container
`437f993004bf4bb6c4951bfb8b69067690d4907818fc727bd1ee7c7cbb56d08f` and volume
`bbfee6e273af4a2cfe18f5a8c835c6d411b70a6a8a6cd74cc63e1dbb0bd6e673` were deleted.
Both were independently verified absent. Unrelated `pr281-pg`, original backups,
retained application images and benchmark evidence were preserved. No Render or
other cloud resources were created; incremental cloud charges were $0.

## Remaining protected rehearsal and release gates

No live production queries or mutations, protected-data restore, merge, deployment,
production migration, receipt correction or RED reconstruction occurred. The prior
one-time #204 authorization was consumed and its database deleted. The historical
1,179-bin treatment-selection improvement, 20 previously hidden positions and 56
separate origin failures have not been freshly measured against protected data in
this task; their predicates remain unchanged, and a new rehearsal must verify them.

After focused human review, the proposed **new scoped authorization** is one restore
of the already-local independently verified backup #204 (dump SHA-256
`c62e035f97b2b330921272342a9d23bae4803e56d681025df4adca0040ba997a`) into one
disposable local PostgreSQL 18 container and volume. It must have no external
network, published ports, production credentials or production connection. Recheck
the retained archive/manifest/dump checksums before use. Permit matched read-only
baseline/pre-fix/reviewed-candidate measurements, full per-position comparisons,
strict readiness, and independent table counts/hashes. Retain only sanitized
evidence; delete the database, volume and expanded SQL immediately after completion
or at a failed safeguard. This scope proposes **no workflow mutations**. A subsequent
forward/rollback/handback rehearsal needs explicit authority for its exact disposable
operations; it is not covered by this read-only proposal. No new restore is started
or presumed approved by this report.

Human review, production-shaped performance/readiness acceptance, frozen compatible
forward/rollback artifacts, completed forward/rollback/handback, authenticated route
verification, explicit release authority and the fresh verified release backup
remain required. Do not merge, deploy or claim rollback safety from this correction.
