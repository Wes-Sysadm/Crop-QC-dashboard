# PR #284 historical custody closure performance correction

Status: synthetic correction and integrity validation PASS; focused human performance review required. Production release remains NO-GO pending reviewed/frozen binaries and the separately authorized production-shaped and rollback/handback gates. No production access, new protected restore, merge, deployment, migration or repair occurred in this task.

## Authority and scope

Owner Wes authorized “PR #284 — Correct Historical Custody Closure Performance” on 2026-10-16, continuing draft PR [#284](https://github.com/Wes-Sysadm/Crop-QC-dashboard/pull/284). This is implementation and synthetic-validation authority, not release authority. Existing branch `codex/system-wide-transfer-eligibility`; starting preparation head `590db49e3f8745421dcf774b186741938df2d0df`; last reviewed application `377cde9812242d9dec239a9fdb49c0791558cfe5`. Current fetched main/base remains `df977d041dae2d449cf6fe5f8581e1f5cae41947`; no integration update was needed. Implementation commit: `dbb488fabac83014d5ba70ff6c75779b1bbfc6de`. Subsequent report commits do not change application source; the final draft head and its CI links are recorded in the PR/closeout.

Specification 1.2.0 and main governance commit `c17bd4deb477b57aa5ac1584175512d49318263b` apply. INV-001–006, TRT-001–002, REC-001–002, MOV-001, ROOM-001, AUD-001, OPS-001 and GOV-001 / ADR-001–008 retain their established scope. This repairs evidence batching without changing inventory authority, treatment or receipt policy. Knowledge synchronization preserved nine unrelated local reports and reported no incoming main changes.

## Confirmed cause and correction

The failed [production-shaped preparation](pr284-release-preparation-2026-10-16.md) and [retained results](pr284-release-performance-2026-10-16.json) identify `IncludeRecordedCustodyAsync`: requested lots expanded to 25 historical rooms, then `LoadRoomsAsync` called the full canonical aggregate before filtering identity keys. The extra aggregate cost 3,757.942 ms, including 3,685.148 ms JIT / 626 functions; this was CPU compilation, not disk I/O. The synthetic reproduction independently exhibits the same added aggregate and multi-second cost.

Only three application files change:

- `InventoryEvidenceLoader.RecordedCustody.cs` passes its existing requested keys/lots into historical closure. Parent validation, exact paired debit/credit proof, audited relocation, chronology, recorded replay and fingerprint composition are unchanged.
- `RoomInventoryLedgerQueryService.cs` adds an internal scoped entry to the SAME canonical calculation. A normalized lot predicate precedes the expensive grouping. It retains all growers/years sharing candidate lots, nullable identity and original receipt fallback; exact canonical keys select the final proof positions. It does not assume that a lot number alone identifies a grower. Bounded Bins Run edge reads expand connected old display labels before filtering, preserving consumed-stock reconciliation. The graph uses a local queue and visited set; there is no persistent/global cache.
- `InventoryEvidenceLoader.cs` filters raw historical evidence to those lots and selects full relevant segment/movement records through bounded lightweight identity indexes. The existing key normalizer handles spaces/status aliases. Both canonical keys and segment links/identity columns retain contradictory evidence rather than silently dropping it. Ordinary reads remain unchanged.

The SQL shape is now, schematically:

```sql
WHERE adjustment."RoomId" = ANY (@rooms)
  AND upper(btrim(CASE WHEN adjustment."LotNumber" <> ''
      THEN adjustment."LotNumber"
      ELSE coalesce(receipt."GrowerNumber", receipt."LotCode") END))
      = ANY (@normalizedLots)
  AND NOT EXISTS (/* unchanged full-room opening-balance cutoff */)
-- existing normalization, grouping, metadata and Bins Run reconciliation follow
```

Baseline subqueries deliberately read the unfiltered ledger: a baseline for another lot can supersede an earlier receipt. Raw-null identity is not discarded because it may normalize from original linked records. Connected Bins Run labels are retained even when their spelling changes. Exact parent, receipt/correction snapshots and audit checks still distinguish an identity change from a same-identity relocation. Effective timestamps and committed chronology remain separate. Read transactions retain RepeatableRead/Serializable checks and READ ONLY ownership. Row/group bounds and historical-snapshot rejection remain fail-closed.

The scope is conservative, not an assertion that every raw row has exact ancestry: same-lot growers and connected legacy labels can remain normalization candidates. Lightweight segment/movement indexes still scan the bounded related rooms so malformed or contradictory links remain visible. No schema/index migration, planner/JIT change, timeout change, resource increase or new authority was introduced.

## Matched synthetic performance

The fixture contains the 286-bin Frenchman analogue, five original transfers and one audited relocation, plus nineteen depleted historical rooms reached by twenty additional valid hops, returning to the original source before its original departure. Across the 25 historical rooms, 500 unrelated identities have 2,000 independent receipts / 20,000 bins. It contains synthetic data only. The actual production Frenchman ORBA record is not relabelled as this synthetic CGAL/treatment fixture.

Sequential runs compare actual baseline `df977d0`, pre-fix `377cde9` and the revised Linux binaries on the same unchanged PostgreSQL 18.4 database. Runtime is .NET/ASP.NET 9.0.21, Linux x64. Each application and database is limited to two CPUs and 2 GiB; no workload measurements overlap. PostgreSQL JIT remains on, thresholds 100000/500000/500000, random_page_cost 1.1, work_mem 26468kB and max_parallel_workers_per_gather 1. These match retained production planner evidence. Round 0 is the first pass in a new process; only its first selector is fully process-cold, and later selectors can reuse query compilation. No cold disk cache is claimed; rounds 1–2 are warm. No latency SLA is invented.

| Reader (warm ms) | Baseline | Pre-fix PR | Revised PR | SELECTs baseline / before / after |
| --- | ---: | ---: | ---: | ---: |
| single-room | 24.5–24.8 | 3,611.0–3,663.9 | 51.1–56.5 | 10 / 24 / 27 |
| roommove | 40.8–41.4 | 3,637.0–3,839.0 | 64.8–69.1 | 13 / 27 / 30 |
| intercrew | 7,044.4–7,196.8 | 10,416.1–10,765.8 | 6,811.7–6,991.8 | 18 / 32 / 35 |
| outside | 6,993.2–7,178.8 | 10,542.3–10,913.8 | 6,744.5–7,013.8 | 14 / 28 / 31 |
| processor | 7,048.0–7,079.2 | 10,721.3–10,791.4 | 6,913.7–7,261.3 | 16 / 30 / 33 |

The historical aggregate falls from **3,660.026 ms to 5.240 ms**, planning 9.039 → 8.909 ms, estimated cost 835,179.97 → 8,752.90. JIT: 580 functions / 3,620.533 ms → no JIT. Aggregate rows: 545 → 45; shared hits: 50598 → 1842; physical reads and temp writes: zero in both plans.

Single-room proof records: **171 baseline / 4,319 pre-fix / 319 revised**. Pre/post-fix output remains 21 positions / two proven treatment slices. Intermediate metadata/index rows are excluded from this proof-record metric; reader-call counts are reported separately. Three extra bounded index/edge SELECTs trade small round-trip overhead for preserving ambiguous historical evidence. Compared with baseline, the revised room paths retain approximately 25–32 ms of necessary historical-proof overhead, rather than the previous added seconds. Broad-reader warm ranges overlap or closely track baseline; processor maximum is about 182 ms higher in this sample, while its minimum is lower. The plans attribute multi-second broad cost to the two pre-existing whole-selection aggregates, not to historical closure.

Exact cold/warm timings, SQL hashes, plan/JIT/buffer statistics, query counts, output counts, allocations and peak working sets are in [the synthetic evidence](pr284-historical-closure-performance-2026-10-16.json). Raw command/EXPLAIN artifacts remain local under `local-test-runtime/pr284-closure`; no protected data was used or published.

The broad inter-crew/outside/processor routes still execute two existing whole-selection canonical aggregates. Their JIT cost is visible in both baseline and PR measurements. This task removes the third, historical closure aggregate's multi-second overhead; it does not claim to optimize the existing broad-selection aggregates. Peak application working sets were 205.6 MiB baseline, 223.5 MiB pre-fix and 215.6 MiB revised, below the unchanged 2 GiB limit; no OOM, timeout or retry was observed. No all-product performance certification or production latency claim is made. Any remaining baseline difference is reported, not hidden by disabling JIT or changing limits.

## Integrity and regression evidence

The frozen pre-fix and revised runs return byte-equivalent serialized evidence for all 21 positions in the selected room: authoritative quantities, receipt confidence/evidence, treatment/application membership, recorded arrivals, blockers' inputs, versions and fingerprints. All synthetic database table fingerprints remain unchanged by measurement. The Frenchman target retains 286 bins: 220 shared-ancestry and 66 exact receipt-owned; its recorded application 800000 remains attached to exactly its proven cohorts. Unknown ancestry remains unknown.

| Required risk | Executed evidence |
| --- | --- |
| 286 bins, repeated visits, depleted sources, audited relocation, mixed exact/shared ancestry | Existing full internal dispatch/receive/return lifecycle plus the 25-room PostgreSQL scope test. 286/0/0 → 246/40/0 → 246/0/40 → 286/0/0 remains conserved. |
| Treated multi-hop, reversal between hops, later arrivals | New PostgreSQL theory: 19 affected bins cross another legacy hop; intermediate reversal is retained, and seven later bins stay untreated. Pure recorded replay regressions remain unchanged. |
| Nullable identity, room baseline, status/case/space aliases | New scoped-vs-full canonical comparison retains receipt fallback and the other-lot room baseline; source remains -32 and blocked. Alias/contradictory-link comparisons also preserve historical cutoffs. |
| Similar lot/different grower, connected old labels | New Bins Run regression retains a changed display label and keeps the depleted original at zero while the unrelated grower's five bins remain separate. |
| Unrelated occupants, scope/fingerprint consistency | The wide test compares single-room, warehouse and broad evidence byte-for-byte; changing a neighbouring receipt does not invalidate the target, changing its origin does. Existing real command tests exercise narrow write-time validation. |
| Missing/contradictory parents and origins | Existing recorded-custody, legacy selector, origin and movement destination regressions reject unsupported proof. No new warning replaces a block. |
| Stale selection, independent sessions, retry/idempotency | Existing competing 160-bin PostgreSQL dispatches from 286 allow one commit, leaving 126 room / 160 transit; retry, changed-intent and stale-fingerprint cases remain enforced. |
| Historical transactions, applications and audit | Full fixture snapshots unchanged by reads; existing lifecycle/compensation tests retain original evidence and exact lineage. |

Validation: 345 affected cases passed, no failures, in the combined Linux run; two definitions initially skipped. The legacy PostgreSQL definition was rerun with its separate synthetic setting: two cases passed. Thus **347 executed passing cases** are supported across these final runs. The protected-data Lamb15 regression remains unexecuted and is not counted as a pass. No new protected restore occurred. An independent TRX audit confirms all **194 mandatory cases**, all 81 required methods and all structural suites executed and passed. Six new scope cases are mandatory through INV-005 traceability. Governance tests: 29 passed; metadata: 20 rules PASS.

Solution restore and Windows Release build pass (existing package/analyzer warnings remain); actual Linux build passes. Model consistency reports no changes since the last migration. Changed-file whitespace verification and `git diff --check` pass after formatting the new tests. No WinForms/MSI change or hardware validation applies. The [capability review](transfer-eligibility-capability-review-2026-10-15.md) precedes updates to the three read-only content hashes; no writer fence, capability or readiness guard changed.

Earlier production-shaped findings—eight positions / 1,928 bins gaining historical proof, six unsealed / 1,179 bins selectable, two sealed / 749 bins blocked, twenty formerly hidden positives and 56 separate origin blockers—remain historical evidence. The relevant source/guard behavior is unchanged and its affected synthetic regressions pass. These exact production counts have NOT been reverified against a new protected restore in this task.

## Release status and next gate

GO for focused human performance review once final-head CI passes. NO-GO for production release. The old `377cde9` frozen executable is historical evidence and cannot represent the changed source. A new reviewed/frozen candidate is required in later preparation.

A fresh protected production-shaped restore IS required to revalidate actual closure cardinality/latency, the documented eight positions and 56 origin failures, strict readiness and forward → compatible rollback → handback on one database. The prior one-time #204 restore was deleted and its authorization is not reused. Proposed scope: one new local Docker PostgreSQL database restored from already-local independently verified #204; network none, no published ports, no production credentials/connection; only bounded affected readers and synthetic rehearsal transactions, protected-row fingerprints, then verified database/volume deletion. Await a new explicit owner approval before accessing the protected dump. Backup #204 is a rehearsal input, never the future fresh release backup.

No production configuration, seal, correction, reconstruction or authoritative quantity changes are authorized here. Live readiness, fresh independently verified release backup, schema compatibility, new exact binaries, compensation-aware activatable rollback, authenticated affected routes and independent production verification remain required after human review and explicit owner release authority. The separate RED reconstruction and historical receipt repairs remain excluded.

Cleanup verified at **2026-10-16T15:45:43.4538292+00:00**: task-owned synthetic container `743f5b28fb3615a3fc7bb8193f43749ad4f6c48166a4374cf93d6f258590e0f5` and volume `1d678ce925083aac4acee3abec583819b03fed474e6a7b1fa369b0fd9a62a5e7` deleted; both absent. Unrelated existing containers and protected backup packages were preserved. No cloud resources or incremental cloud charges were created.
