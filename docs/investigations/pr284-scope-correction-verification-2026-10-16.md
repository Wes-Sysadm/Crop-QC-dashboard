# PR #284 — recorded-custody scope correction: read-only verification

**GO FOR SEPARATELY AUTHORIZED FORWARD / ROLLBACK / HANDBACK REHEARSAL.**
Production release remains unauthorized. PR #284 stays draft. No application code, business rule, schema or writer changed in this continuation.

## Authorization, source and compliance

Owner Wes explicitly replied **“Approve one read-only #204 restore”** to question `call_cdbf22fbed9a42668c387798c4e8459c` on 2026-10-16 America/Los_Angeles (recorded 2026-10-17 UTC). Attachment `7ee0176a-62cf-460e-94e8-7820445f95d3` defines this scope. Exactly one restore consumed this approval. It excludes application writes, migrations, corrections, production access, forward/rollback/handback, merge and deployment.

- Human-reviewed and executed candidate: `3e3eae16c8abddedc734f637d65115ac154a954f`.
- Current main/base: `df977d041dae2d449cf6fe5f8581e1f5cae41947`; no incoming main changes or integration needed.
- Matched pre-correction executable: `8047d1a3202f4493b67f36cb70601e0ee9046205`.
- Existing branch `codex/system-wide-transfer-eligibility`; [PR #284](https://github.com/Wes-Sysadm/Crop-QC-dashboard/pull/284).
- Specification **1.2.0**, governance commit `c17bd4deb477b57aa5ac1584175512d49318263b`. AGENTS, canonical specification, ADRs, inventory read/command architecture, change procedure, change-scoped testing and release standard were read. Report-only synchronization found only existing PR evidence/contract differences and nine unrelated untracked reports, all preserved.
- INV-001–006, TRT-001/002, REC-001/002, MOV-001, ROOM-001, AUD-001, OPS-001, GOV-001 and applicable ADR-001–008 remain unchanged. Quantities/custody come from transactions; shared quantity does not invent exact ancestry; treatments remain point-in-time; immutable history, seals, origin guards and release safeguards remain mandatory.

## Frozen executable and backup

The reviewed Git source was archived with canonical LF content and built into a new immutable Linux image using the established Dockerfile with pinned SDK/runtime base digests. This is the newly built application, not the previous candidate DLL identity. Runtime commands independently read Linux x64 and .NET/ASP.NET **9.0.21**; database reported **PostgreSQL 18.4**.

| Artifact | Verified identity |
|---|---|
| Source archive SHA-256 | `87cefa9431d7966514a19d346525a7758438661101464310907fa5d61d21fe4c` |
| Local image digest | `sha256:dd49d9225096f560ee44f594fb585a2ee4b4cdc7bc4d4e73119ec328a9d2dccb` |
| Frozen image archive | 150,429,184 bytes; `e15da58617e48bbace2b154a8f157e680f29ce809114e71766ed72874504ac0b` |
| CropQc.Web.dll | `6b87861b2f25f7d4c813aa39804348592a944881ca2dad3c50fc65f96978d4f5` |
| CropQc.Data.dll | `ec2483e0a83a085527d11a06b0f10b898eec818d56496219bcb8cade4291d45f` |
| CropQc.Shared.dll | `e45767523a88d1b4a52d381a66afe20fcd51eb8d8ccae8e768b0be9133eeab21` |
| Backup #204 ZIP | 20,177,849 bytes; `7030fed357e9166ac0da8747cad2c77800950d0d7a2bd71e3a82390646f3b4c2` |
| Compressed database dump | 19,929,864 bytes; `c62e035f97b2b330921272342a9d23bae4803e56d681025df4adca0040ba997a` |
| Expanded database dump | 345,808,559 bytes; `c2f8b08833420c257c75b3c18984e2e908057d494b99c6fd5bdf08b8195d5300` |

Package size/hash, ZIP CRC, manifest bytes, every component size/hash, and decompressed SQL hash passed again before restoration. Prior remote read-back was already independently recorded; no new production backup or download occurred. The local frozen archive and manifest remain under `local-test-runtime/pr284-scope-verification`.

One task-owned database used `network=none`, no published ports, no production connection/credentials and no inherited production environment. Diagnostic application containers shared only that isolated network namespace. Startup creation/seeding and external effects were disabled. All diagnostic database sessions set `default_transaction_read_only=on`; actual session checks confirmed both transaction and default read-only. App/database limits were 2 CPU / 2 GiB and 2 CPU / 3 GiB. Expanded SQL was deleted immediately after restoration.

## Complete scope and exact historical cases

A single as-of timestamp `2026-10-17T05:09:37.611858+00:00` and unchanged database were used for all scope comparisons. **452/452 positive positions pass all 1,356 comparisons**: all-room against warehouse, single-room and canonical command-source scopes. Nothing was excluded or left unreached.

The comparison serialized every source-evidence field and the complete resolver result: identity, physical/committed quantities, current custody, ledger/movements, original receipt evidence/revisions, known/unknown ownership, applications/reversals, current treatment membership, RecordedArrivals, versions, blockers and fingerprints. Selector source keys were independently matched to the canonical proof: 459 outside, 459 processor, nine inter-crew and nine room-move options. This is not fingerprint-only verification.

**All 18 prior failures / 2,932 bins pass all three comparisons:**

| Room | Lot | Current bins | Result |
|---|---|---:|---|
| Lamb Street 16 (9) | 9541 | 1 | PASS |
| Evans Street 5 (15) | 1011 | 350 | PASS |
| Evans Street 5 (15) | 3152 | 61 | PASS |
| Evans Street 5 (15) | 3162 | 67 | PASS |
| Evans Street 5 (15) | 9092 | 126 | PASS |
| Evans Street 5 (15) | 9362 | 196 | PASS |
| Evans Street 5 (15) | 9372 | 136 | PASS |
| Evans Street 5 (15) | 9682 | 252 | PASS |
| Evans Street 7 (17) | 3152 | 193 | PASS |
| Evans Street 7 (17) | 9362 | 37 | PASS |
| Evans Street 8 (18) | 3032 | 55 | PASS |
| Evans Street 8 (18) | 9092 | 184 | PASS |
| Evans Street 8 (18) | 9332 | 696 | PASS |
| Evans Street 8 (18) | 9362 | 66 | PASS |
| Evans Street 8 (18) | 9380 | 29 | PASS |
| Evans Street 8 (18) | 9722 | 160 | PASS |
| Room 11 (63) | 2200 | 296 | PASS |
| Room 11 (63) | 2910 | 27 | PASS |

Lamb Street 16 / lot 9541 remains **one current bin**. Historical RoomTransfer **385** remains 30 bins from room 27 to room 9; ledger **3429/3430** remains −30/+30, and modern movement **637** retains that parent and treatment evidence. The historic 30-bin transfer is not additional available stock. Effective time remains `2026-09-21T23:55:00Z`, separately from its committed timestamps. The redundant RecordedArrival is absent consistently in every scope, while the modern ledger/movement proof remains.

Across the full 1,066-position read, 18 positive and 18 depleted positions lose only redundant RecordedArrivals and their extra dependency versions; fingerprints change accordingly. Every other serialized evidence/operative output field is identical before/after. **No application-membership field changed**, including depleted positions. The same guard used for the synthetic comparison would require explicit evidence for any depleted imported-context removal; none occurred here.

WP-7 / lot 1372 / profile 17 remains **501 bins**. Receipt **626** retains original quantity **56**, original identity verified **true** (current revised quantity 64). Receipt **720** retains original quantity **56**, original identity verified **false**. Complete proof and fingerprint agree across scopes and selector:

`AA2A5DDCF1E53298955D717E9A20508C752CF362ED74E30338CB45D1AF8E44D7`

Its independent origin rejection remains exactly: `Ledger 1187: custody parent receipt-change:45678062-5b38-470b-ab7e-58f1db8e0415 credits more inventory than its recorded debits.`

All **56 existing origin rejections / 10,437 bins** match their prior authoritative reasons exactly in both frozen applications. No unsupported source becomes authorized. The six unsealed improvements totaling **1,179 bins** remain available under their proven treatment-selection evidence. The twenty formerly hidden positive positions (**5,974 bins**) remain visible. Sealed selector entries retain zero dispatchable treatment quantity; exact receipt IDs are supplied only for proven membership. Unknown ancestry and treatment remain unknown. Read-only selector/origin evidence does not substitute for a new mutation rehearsal; existing synthetic seal/writer tests were not rerun.

## Matched performance

One cold-process round and two warm rounds used identical runtime, database, CPU/memory limits and planner settings: `random_page_cost=1.1`, `work_mem=26468kB`, parallel workers/gather 1; JIT on with unchanged 100000/500000/500000 thresholds. No tests/builds or competing diagnostic queries ran during timed measurements. Historical baseline performance was not reopened.

| Workflow | Before warm ms | Candidate warm ms | SELECTs / rows, both | Aggregates, both |
|---|---:|---:|---:|---:|
| single-room | 79.2–80.0 | 82.2–89.6 | 31 / 2,347 | 2 |
| roommove | 101.8–112.8 | 102.2–109.9 | 34 / 2,388 | 3 |
| intercrew | 8,669.8–8,933.5 | 8,769.2–9,059.8 | 51 / 28,665 | 2 |
| outside | 8,865.2–8,927.9 | 8,865.8–8,999.5 | 35 / 28,384 | 2 |
| processor | 8,834.1–9,188.4 | 8,799.8–9,393.2 | 37 / 28,840 | 2 |
| all-evidence | 4,507.8–4,916.5 | 4,543.0–4,921.4 | 32 / 25,883 | 1 |

SQL statement hashes, SELECT/row/aggregate counts and output counts are identical in every matched round. Single-room materialized proof remains 1,017 rows; all-room remains 19,194. No extra historical aggregate or material new latency regression was observed. The roughly 10 ms single-room spread has unchanged SQL/rows; two warm samples are not an SLA. Existing broad plans invoke JIT in both applications; no JIT/planner workaround was used. Sanitized plan roots, timings and hashes are retained; raw protected plans were deleted.

## Strict readiness and independent integrity

- Strict release readiness: **exit 0**, success; actual 34 applied migrations; required 1,007 schema objects pass the schema contract. No migration ran.
- Inventory deduction readiness: no blocking issues. Treatment readiness: 452 identities, zero blockers. Custody/topology readiness: no invalid inter-crew/outside master or topology findings.
- Conservation: **exit 0**, 77,642 signed room bins, 844 in-transit, 1,409 outside custody and zero receipt-held. Positive inventory is 77,769 bins; five existing negatives total −127 and remain blocked diagnostics.
- Receiving: 2,627 receipts reconcile individually; receipt and ledger totals both 109,559, zero mismatch receipts. Treatment identity debits/credits both 6,088; difference zero.
- All **269,834 original rows across 113 tables** retain identical independent ordered-row SHA-256 hashes/counts before/after each application and final cleanup. Original snapshot also equals the previously recorded #204 fingerprint set. No new, deleted or changed row; audit and migration history preserved.

Final table-manifest fingerprint: `1e90aa5ab248e18b83b34ff7023ebfeafc4ad560949f138e58d765deb89a73a5`.

Strict global readiness passing does not waive the 56 operation-specific origin rejections. Both results are intentionally retained.

## Cleanup, validation and remaining gate

Task-owned container `20759cbd7f376807ce92614100213c797d34ab38849f84bc0ffab8739da7edc0` and volume `fcff784f1801e302a9bfd53482bbcb703f8944769a380d63bd36fcb9159ad4ae` were deleted at `2026-10-17T05:18:01.535976+00:00` and independently verified absent. Expanded SQL is absent; **36 raw protected diagnostic files** were deleted and verified absent. Sanitized findings, artifact hashes and the original verified backup remain. Unrelated local resources were preserved; no cloud resource or incremental cloud charge ($0).

The new exact Linux candidate build succeeded. No application/model change occurred in this continuation, so completed 249 affected cases, 221 mandatory cases/91 methods, 29 governance tests, model and formatting evidence were not redundantly rerun. All four reviewed-head GitHub checks were successful at source verification. Documentation validation and final-head CI are recorded in the PR/closeout; prior test counts are not claimed as newly executed.

This evidence resolves the read-only recorded-custody scope gate. It does **not** prove same-database forward/rollback/handback compatibility after new transactions. That is the next separately authorized rehearsal, followed by applicable owner release authority, fresh independently verified production backup, live strict readiness, rollback availability, affected-route checks and independent deployment verification. No historical reconstruction, production correction, merge or deployment occurred.

Full sanitized measurements, exact 452-position comparisons, 18-case disposition, 56 origin errors, hashes and cleanup: [pr284-scope-correction-verification-2026-10-16.json](pr284-scope-correction-verification-2026-10-16.json).
