# PR #284 — approved restore: remaining broad-closure performance blocker

**NO-GO.** The single-room historical closure regression is corrected, but the restored broad selectors still incur approximately four extra seconds. Preparation stopped at that performance safeguard before synthetic workflow mutations, authenticated-route rehearsal, rollback or handback. This report supersedes earlier conditional GO wording; it does not discard the earlier evidence for its identified executable.

## Scope, authority and source

- PR [#284](https://github.com/Wes-Sysadm/Crop-QC-dashboard/pull/284) remains draft on `codex/system-wide-transfer-eligibility`.
- Application source: `efe895913bc54080a52746897a9dea19e12bcf52`; base/current fetched main: `df977d041dae2d449cf6fe5f8581e1f5cae41947`. No integration update was needed. Subsequent reporting commits do not change application binaries.
- Owner reply on 2026-10-16: **“Approve one isolated #204 restore.”** This explicitly covered affected performance, strict readiness and forward/rollback/handback, without network, published ports, production credentials or a production connection, followed by deletion. That one-time authorization was consumed once. No merge/deployment/repair authority was inferred.
- Specification 1.2.0, main governance commit `c17bd4deb477b57aa5ac1584175512d49318263b`; applicable INV-001–006, TRT-001–002, REC-001–002, MOV-001, ROOM-001, AUD-001, OPS-001 and GOV-001. ADR-001–008 retain their scope. Ledger/origin/custody evidence establishes quantity; projections cannot authorize stock. Point-in-time treatment, unknown ancestry, baseline semantics, immutable history, concurrency and writer fences remain mandatory.

## Final receipt-origin dependency correction

Final review reproduced an additional scope defect: another historical identity's ReceiptAdd for the **same selected receipt** can trigger original-receipt snapshot validation. Filtering that row out changed original quantity/identity evidence. The PostgreSQL regression failed before the fix and passes afterward.

`InventoryEvidenceLoader.cs` now retains the original room query for a bounded distinct SELECT of selected receipt IDs whose quantity/identity differs from their ReceiptAdd. It retains the existing mismatch predicate and original snapshot interpretation. It does not fetch unrelated receipt entities, invent provenance, change ordinary reads, or restore full-identity aggregation. The read-only capability hash was updated after explicit review. One mandatory regression was added; no writer, model, migration, policy or readiness rule changed.

All seven historical-scope cases pass. The subsequent affected/mandatory run passed **198 cases, zero failures/skips**, including **195 mandatory cases**, every **82 required method** and structural suites. Earlier 347-case evidence remains applicable outside this specifically rerun dependency; these counts are overlapping runs, not additive coverage. Governance tests pass 29/29 and metadata passes all 20 rules. Exact Linux application build, changed-file formatting and whitespace checks pass. The prior model-consistency result remains valid because no model/migration dependency changed; actual restored schema readiness also passes. All four GitHub job types passed on `efe8959`.

The final matched synthetic fixture retained identical pre-fix/revised serialized evidence for all 21 positions, including the 286-bin target with 220 shared-ancestry and 66 exact receipt-owned bins. All 113 synthetic table fingerprints were unchanged. Warm single-room reads fell from 3,506–3,583 ms to 50–51 ms; room selection from 3,516–3,615 ms to 66–77 ms. The historical aggregate fell from 3,571 ms to 5.187 ms with no JIT. This synthetic result did **not** predict the broader restored scope below.

## Exact preparation artifacts

These are retained local preparation artifacts, not an owner-approved release package:

| Artifact | Identity |
| --- | --- |
| Candidate source | `efe895913bc54080a52746897a9dea19e12bcf52` |
| Candidate image index | `sha256:e0ff5814d3ff0fc7dcd5fd7c4b23911f697f558ebb2027719a595bba9c366e97` |
| Candidate archive SHA-256 | `1a6833c0355db2b6e673b115abe9d172330248f4aec03bc7846d6de4edb53ae3` |
| Archive bytes | 150,424,064 |
| Candidate Web DLL SHA-256 | `59b9f1493cd923fb2dc1ff210743003899d636072ebe0b30d5515611945dd356` |
| Candidate Data DLL SHA-256 | `27165441559771f70328b68fc65c5db6b61054e87cfb33b69c28db435133278f` |
| Candidate Shared DLL SHA-256 | `e45767523a88d1b4a52d381a66afe20fcd51eb8d8ccae8e768b0be9133eeab21` |
| Baseline/rollback source | `df977d041dae2d449cf6fe5f8581e1f5cae41947` |
| Baseline/rollback image index | `sha256:598c26de196ebc3ecfb160fa07534de7d2400ba57c2376e74457b10aa2c028ec` |
| Verified backup #204 dump SHA-256 | `c62e035f97b2b330921272342a9d23bae4803e56d681025df4adca0040ba997a` |

The exact candidate was built from a canonical Git archive with pinned SDK/runtime inputs. Actual runtime: Linux amd64, .NET and ASP.NET 9.0.21; PostgreSQL 18.4. Package/manifests and raw local evidence are under `local-test-runtime/pr284-closure-restore`. The previous `377cde9` package is not this candidate. No cloud image publication or deployment occurred.

## Restored readiness and integrity

Both baseline/rollback and candidate passed `--verify-release-readiness` and `--verify-inventory-conservation`, exit 0, through read-only connections with startup schema/seed writes disabled:

- Schema: 34 applied migrations unchanged, including isolated cohorts and the custody migrations; 1,007 schema objects verified.
- Inventory deductions: 1,139 negative adjustments (93 historical, 1,046 new format), zero blockers.
- Treatment: 452 current identities, zero blocking issues.
- Custody/topology: zero reported issues.
- Current authoritative room quantity and accounting: 77,642 bins each; difference zero.
- Receiving: 2,627 receipts, 109,559 receipt bins and 109,559 ledger bins, no mismatches.
- Transit 844; outside custody 1,409; receipt-held zero. Treatment identity debits/credits both 6,088.

Independent comparison after all reads and EXPLAIN runs confirms **all 269,834 original rows in all 113 tables unchanged**, zero added rows and unchanged migration history. No correction, synthetic workflow operation, reconstruction, normalization or migration was executed on this restore.

## Matched restored performance and exact remaining query

Sequential baseline/candidate runs used the same database, 2 CPU limits, 2 GiB application / 3 GiB database limits, and retained production planner settings: JIT on; thresholds 100000/500000/500000; random_page_cost 1.1; work_mem 26468 kB; max_parallel_workers_per_gather 1. No configuration workaround was applied. Warm ranges are rounds 1–2; round 0 is a process first pass, not a disk-cold claim.

| Selector | Baseline warm ms | Candidate warm ms | SELECTs baseline → candidate |
| --- | ---: | ---: | ---: |
| Direct single-room | 23.1–23.7 | 64.4–75.9 | 10 → 29 |
| Room transfer | 43.9–54.1 | 84.2–87.4 | 13 → 32 |
| Inter-crew | 7,767.5–7,843.3 | 11,742.4–11,836.2 | 31 → 50 |
| Outside warehouse | 7,836.2–7,893.5 | 11,700.7–11,911.0 | 15 → 34 |
| Processor | 7,879.5–7,923.7 | 11,945.0–12,039.0 | 17 → 36 |

Single-room proof rows: baseline 89, old candidate 3,869 (historical rehearsal), revised candidate 875. Its closure now filters nine lot labels across 25 rooms: aggregate execution **23.574 ms**, planning 9.55 ms, estimated cost 45,850.96, 110 rows and no JIT. The multi-second single-room defect is eliminated; approximately 40–53 ms of proof overhead remains against baseline.

The broad reader still passes the union of all requested lots/rooms from `IncludeRecordedCustodyAsync` into `LoadRoomsAsync` and `GetHistorySnapshotsAsync`. Filtering before grouping now excludes unrelated lots, but the combined relevant batch is still large enough to trigger expensive compilation in the existing correlated canonical aggregate. Read-only EXPLAIN identifies the **third historical aggregate** specifically:

- 59 rooms, 42 normalized lot labels, 492 aggregate rows.
- Estimated cost **583,901.09**; planning **8.566 ms**; execution **3,715.784 ms**.
- **621 JIT functions**, **3,628.790 ms** compilation (2,065.562 optimization; 1,520.811 emission).
- 173,409 shared-buffer hits; no physical reads or temporary writes.
- The two pre-existing broad aggregates separately cost 3,685.586 and 3,797.374 ms. They explain baseline cost; the additional historical aggregate explains the remaining regression.

This is a confirmed broad batching/query-plan limitation, not an origin-revision correctness failure or a reason to disable JIT, loosen proof or raise timeouts. Peak application working sets were approximately 243 MiB baseline and 256 MiB candidate; no OOM or timeout. The two samples per warm range support a repeatable material difference, not a new absolute latency SLA.

Full samples, SQL hashes, parameter cardinalities, plans, runtime identities, readiness, integrity and cleanup are in [sanitized evidence](pr284-approved-restore-performance-2026-10-16.json). `rowsRead` counts reader calls including terminal reads; it is not exact SQL row cardinality. The room-selection wrapper is not enumerable, so its instrumentation `outputRows=-1` is an unavailable count. Initial benchmark argument parsing measured only the fallback single-room branch; those samples were excluded from the five-selector comparison. A synthetic fingerprint assertion initially compared unordered table lines; table-keyed comparisons proved all hashes/counts unchanged and the corrected verifier uses stable ordering.

## Stop, cleanup and next scope

Preparation stopped before forward/rollback/handback and authenticated routes. The retained compensation-aware baseline passes readiness on the unchanged restore, but continued safe operation after new #284 writes remains **unproven**. Prepared local workflow instrumentation was not executed and is not passing evidence. The eight historical proof gains / 56 separate origin failures were not fully re-audited after the performance stop; the outside selector did retain 459 options versus baseline 439, which alone does not certify their custody or origin.

At **2026-10-16 16:36:20 UTC (09:36 PDT)**, restored container `15aacdd29633831151957e79967da5466876a45aaa2042ac8367f2e122c52df6`, volume `187971d2ea83d1af862832e559946fcb587b68e45a60ad35e33b039b45c5b799` and generated uncompressed restore SQL were deleted; absence was independently verified. The separate task-owned synthetic container and volume were also deleted. Original verified backup packages and unrelated containers were retained. Incremental cloud charges: $0.

The next implementation scope is the **broad historical aggregate only**: evaluate bounded connected-lot component batching or an equivalent bounded canonical query shape, preserving cross-label Bins Run reconciliation, unfiltered room baselines, nullable identity semantics, full correction snapshots, complete parent/custody/treatment proof, global row bounds and scope-identical fingerprints. Reproduce this 42-lot/59-room shape synthetically before another protected rehearsal. This is a proposal, not an implemented or verified solution.

Keep PR #284 draft. **NO-GO for performance acceptance or release.** The current one-time restore is finished and deleted; a future protected restore requires fresh scoped authority. Human review of the next correction, exact candidate validation, completed same-database forward/rollback/handback, activatable rollback, authenticated routes, explicit release authority, fresh verified release backup and live readiness remain mandatory. Nothing was merged, deployed or changed in production.
