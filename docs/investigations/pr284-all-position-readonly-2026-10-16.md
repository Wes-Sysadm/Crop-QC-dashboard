# PR #284 — all-position read-only verification: recorded-custody scope failure

**NO-GO for forward/rollback/handback advancement or release.** The approved
receipt-origin fix passes WP-7, but complete verification finds **18 other
positive positions / 2,932 bins** with scope-dependent recorded-arrival proof
and fingerprints. All **452 positive positions** were examined; none was
excluded or left unreached. No application fix was attempted at this safeguard.

## Authority and exact source

Owner Wes approved one additional read-only restore with the reply **“Approve one
read-only #204 restore”** on 2026-10-16 America/Los_Angeles, question
`call_f3389c20d0884289b46485b76c500fac`. Attachment
`b61baa1e-84b0-4086-8596-99a4857a2c87` defines the permitted all-452-position,
performance, origin, readiness and integrity checks. This one-use authority is
now consumed. No workflow writes, migration, transfer, forward/rollback/handback,
production access, correction, reconstruction, merge or deployment was authorized
or executed. No cloud resource was created; incremental cloud cost is $0.

- Reviewed/executed source: `8047d1a3202f4493b67f36cb70601e0ee9046205`.
- Prior optimized comparator: `512940de3714eb4dec7eb26dcab282d1d6289acc`.
- Fetched main/base and production-source comparator: `df977d041dae2d449cf6fe5f8581e1f5cae41947`.
- Branch: `codex/system-wide-transfer-eligibility`; [PR #284](https://github.com/Wes-Sysadm/Crop-QC-dashboard/pull/284) stays draft. Main had no incoming change; no integration was required.
- Specification 1.2.0; governance `c17bd4deb477b57aa5ac1584175512d49318263b`.
  INV-001–006, TRT-001/002, REC-001/002, MOV-001, ROOM-001, AUD-001,
  OPS-001, GOV-001 and applicable ADR-001–008 remain unchanged. Authority,
  quantity, receipt ancestry, treatment and projections remain distinct.
- AGENTS, canonical rules, decisions, change procedure, phase 1/2 contracts,
  release standard and change-scoped testing were read; knowledge synchronization
  was report-only. Nine pre-existing untracked release reports were preserved.

## Backup, runtime and isolation

Backup **#204** was independently rechecked locally before restoration: ZIP CRC,
manifest equality, every component size/SHA-256 and decompressed SQL checksum.
The durable remote read-back belongs to the earlier recorded backup verification;
no new production/remote backup access is claimed.

| Artifact | Identity |
|---|---|
| Package | 20,177,849 bytes; `7030fed357e9166ac0da8747cad2c77800950d0d7a2bd71e3a82390646f3b4c2` |
| Compressed SQL | 19,929,864 bytes; `c62e035f97b2b330921272342a9d23bae4803e56d681025df4adca0040ba997a` |
| Expanded SQL | 345,808,559 bytes; `c2f8b08833420c257c75b3c18984e2e908057d494b99c6fd5bdf08b8195d5300` |
| Candidate canonical LF source archive | `992c5f5d0f2fab439165b0802e788d31f764c1da0eba16d1d9fc47d9638b4507` |
| Candidate local image | `sha256:6e02e9ea0c134524b524d573b0b1ce7e097580b2b58f80eacdb46eda4089e223` |
| Frozen candidate image archive | 150,429,696 bytes; `9212369a4b04d3261fef65304c281e75abf29849a5952cccc30823053997bf90` |
| CropQc.Web.dll | `12b94f8f43fb41a8fc73aa4a767cefcc664d23034b94942fdbde4f1f3c3bcf92` |
| CropQc.Data.dll | `41af2196fef64b6c3294b16d7a75b1867bfb5fedb63740970876f472b34686e0` |
| CropQc.Shared.dll | `e45767523a88d1b4a52d381a66afe20fcd51eb8d8ccae8e768b0be9133eeab21` |

The exact commit was archived with canonical LF content and newly built using
the established pinned SDK/runtime Dockerfile. Actual runtime: Linux x64,
.NET/ASP.NET **9.0.21**; PostgreSQL **18.4 Debian**. All three application images,
archives and loaded DLL identities were verified. Prior synthetic build hashes
were not substituted for the newly frozen executable. The companion evidence
retains comparator identities and Dockerfile hashes. The pinned SDK and runtime
are respectively `sha256:2f5eb32870b4c0074a3d5b5191a03553bcab7b74463a76a7c0a5f3ef65c90fe5`
and `sha256:7da398155e0d6439cf0058bc1afa937817a03e320714b876a5f58a7d64f79d50`.

One PostgreSQL container used `network=none`, no published ports, only local
throwaway authentication and no production environment or connection. Application
probes shared only that isolated network namespace. Database sessions enforced
`default_transaction_read_only=on`; startup schema/seed writes, emails and backup
jobs were disabled. Application/database limits were 2 CPUs / 2 GiB and 2 CPUs /
3 GiB. The expanded SQL was deleted immediately after the single successful restore.

## Complete proof comparison and confirmed failure

As-of point: **2026-10-17T01:20:47.667344Z**, held constant for direct evidence
reads on unchanged data. Complete position evidence and resolved results were
compared, including identity/location, quantity/custody, ledger, receipt proof,
versions, treatment/application memberships, confidence, blockers and watermark.
No fingerprint field was omitted to make a scope comparison pass.

| Broad comparison | Positions checked | Exact matches | Failures |
|---|---:|---:|---:|
| Warehouse | 452 | 452 | 0 |
| Single room | 452 | 434 | 18 |
| Command source (warehouse + room) | 452 | 434 | 18 |
| Total | 1,356 | 1,320 | 36 |

The 36 failed comparisons concern the same 18 positions. Differing evidence
fields are **RecordedArrivals and Watermark**; resolved differences are **Proof
and Watermark**. Quantity, identity, location, current custody, local ledger,
receipt evidence, selected treatment slices, availability and blocker results
are otherwise equal. All position IDs, broad/narrow fingerprints, missing arrival
cohorts, parent events and added version dependencies are retained in the
[sanitized evidence](pr284-all-position-readonly-2026-10-16.json).

| Room (ID) | Lot | GrowerLotId | ProfileId | Bins | Broad-only recorded arrival ledger IDs |
|---|---|---:|---:|---:|---|
| Lamb Street 16 (9) | 9541 | 623 | 10 | 1 | 3430 |
| Evans Street 5 (15) | 1011 | 399 | 7 | 350 | 2489 |
| Evans Street 5 (15) | 3152 | 511 | 2 | 61 | 2491, 2509 |
| Evans Street 5 (15) | 3162 | 513 | 2 | 67 | 2493 |
| Evans Street 5 (15) | 9092 | 559 | 2 | 126 | 2503, 2511 |
| Evans Street 5 (15) | 9362 | 581 | 2 | 196 | 2505 |
| Evans Street 5 (15) | 9372 | 583 | 2 | 136 | 2495, 2513 |
| Evans Street 5 (15) | 9682 | 642 | 2 | 252 | 2499, 2515 |
| Evans Street 7 (17) | 3152 | 511 | 2 | 193 | 1158, 1162, 1172 |
| Evans Street 7 (17) | 9362 | 581 | 2 | 37 | 1265 |
| Evans Street 8 (18) | 3032 | 505 | 2 | 55 | 2264 |
| Evans Street 8 (18) | 9092 | 559 | 2 | 184 | 1682 |
| Evans Street 8 (18) | 9332 | 574 | 7 | 696 | 2262 |
| Evans Street 8 (18) | 9362 | 581 | 2 | 66 | 1708 |
| Evans Street 8 (18) | 9380 | 331 | 2 | 29 | 1688, 2252 |
| Evans Street 8 (18) | 9722 | 648 | 2 | 160 | 2258 |
| Room 11 (63) | 2200 | 57 | 7 | 296 | 1739, 1745, 1782, 1801 |
| Room 11 (63) | 2910 | 184 | 7 | 27 | 1741, 1747 |

**Confirmed code mechanism:** `InventoryEvidenceLoader.RecordedCustody.cs`
selects `requested` positions with a positive unmatched TransferIn or
ReceiptAdminOverride. It gathers those identities' historical closure and calls
`InventoryRecordedCustodyReplay.Enrich`. The final loop then applies that
replayed evidence to **every original position** with an enriched peer result,
including positions that were not in `requested`. A broad/warehouse request can
therefore trigger enrichment for a position because another room of the same
identity needs closure. Its narrow request does not trigger that work.

The frozen candidate's exact predicate and resolver were also evaluated offline
against the captured evidence, after the restored database was deleted. For all
18 failures, `localRequested=false`; each has a qualifying peer elsewhere. Every
local triggering-kind credit already has a matching incoming movement. Examples:
Lamb Street 16 / 9541 gets its broad enrichment because room 21 ledger 2852
requests closure; Evans Street 5 / 3152 and Evans Street 7 / 3152 inherit the
closure triggered by room 11 ledgers 547/680. The 1-bin Lamb Street 16 position
has broad-only arrival ledger 3430, representing a historical 30-bin transfer
(parent RoomTransfer 385, debit 3429), not 30 new current bins.

For that Lamb Street 16 example the broad fingerprint is
`A631356E11FD9CA32989F885757D84A297C00E5146F473A45782E49FF1A31CE9`;
its room/command fingerprint is
`578B8FC761B60DF351AD3AFF1A1C012376F61F59300978E79D4C32D750D17B49`.
A broad source key therefore cannot reliably match the command's narrow stale-state
proof even with no business activity. No command was submitted to demonstrate a
write failure. This is a loader dependency defect, not evidence of changed
physical stock or a request to repair the underlying records.

This recorded-custody file is unchanged between optimized `512940d` and reviewed
`8047d1a`; this is a newly exposed coverage gap, not a demonstrated regression
introduced by the receipt-origin patch. Earlier verification covered warehouse
scopes only and stopped at WP-7. This run completed both narrower scopes for all
452 positions before failing with diagnostic application exit **134**, wrapper
exit **1**. The failure was not suppressed or relabeled as PASS.

## WP-7, origin protection and transfer objective

WP-7 / room 4 / GrowerLot 448 / lot 1372 / profile 17 retains **501 bins**.
Original ledger 997 establishes receipt 626's original 56 bins / identity **true**
in warehouse 3, room 59. Ledger 1185 establishes receipt 720's original 56 bins /
identity **false** in warehouse 3, room 58. Receipts 660/774 retain their prior
uncorrected evidence. No exact shared ownership was invented.

All-room, WP warehouse, WP-7 single-room, command-source and applicable outside
selector agree on fingerprint
`AA2A5DDCF1E53298955D717E9A20508C752CF362ED74E30338CB45D1AF8E44D7`.
The complete WP-7 evidence, versions and resolver results agree across scopes.
Receipt 720 was not promoted to true.

The independent origin guard retains **all 56 exact failing position IDs/error
messages, totaling 10,437 bins**, in baseline, optimized and candidate runtimes.
In particular it still rejects:

> Ledger 1187: custody parent receipt-change:45678062-5b38-470b-ab7e-58f1db8e0415 credits more inventory than its recorded debits.

This is an independent operation-specific block. WP-7's selector still presents
501 treatment-selectable bins; this is not final origin authorization. We do
**not** claim all 56 origin-failing options are hidden/disabled in selectors.
Their existing command-origin rejection is unchanged. Unknown treatment and
unsupported selection remainders remain disabled; sealed rooms have zero
selectable treatment bins. Nothing in this check authorizes those origin failures.

The intended treatment-selection improvements remain: six unsealed positions
recover **1,179 bins** of selection evidence (room 11 / 1565 = 140, room 11 / 9380
= 147, room 15 / 9342 = 77, room 67 / 1110 = 286, /1290 = 5, /2825 = 524).
All six pass the origin guard. Two additional sealed positions retain recovered
evidence but remain non-dispatchable. All **20 formerly hidden positive
positions / 5,974 bins** are visible with unsupported remainders blocked.

All 459 outside and 459 processor options, plus nine inter-crew and nine
room-transfer options in the scoped probe, reconcile to canonical quantities,
selected proven treatment membership, source-key fingerprints and honest receipt
confidence. These are read-only service probes, not authenticated HTTP or command
execution. Shared unknown receipt ancestry stays unresolved. Optimized and current
outputs match for all five workflows and all-room evidence except deliberately
strengthened fingerprints/source-key suffixes. Those expected **cross-version**
changes are distinct from the **same-version cross-scope** failure above.

## Matched performance

Sequential matched measurements used the same data, pinned runtime, resource
limits and session planner settings: JIT on, thresholds 100000/500000/500000,
random_page_cost 1.1, work_mem 26468kB, max_parallel_workers_per_gather 1.
No JIT disabling, global tuning, timeout extension or competing build/test was
used. These are two warm samples after process warmup, not a Render latency SLA.

| Workflow | Optimized warm ms | Candidate warm ms | SELECT count | Reader ReadCount |
|---|---:|---:|---:|---:|
| Single room | 74.2–74.6 | 74.5–79.7 | 30 → 31 | 2212 → 2347 |
| Room transfer | 90.8–95.1 | 94.6–95.3 | 33 → 34 | 2253 → 2388 |
| Inter-crew | 7695.9–8047.2 | 7781.2–8052.1 | 50 → 51 | 25143 → 28665 |
| Outside warehouse | 7800.0–7928.5 | 7859.4–7888.7 | 34 → 35 | 24862 → 28384 |
| Processor | 7819.3–8003.3 | 7873.5–8068.8 | 36 → 37 | 25318 → 28840 |

The candidate adds one batched query per measured workflow. Its origin plans
return 2,640 rows in 0.880 ms and a 932-row history subset in 0.426 ms; narrow
plans return 23 rows in 0.060 ms and 119 in 0.126 ms. No origin plan has JIT.
The previously eliminated additional broad historical aggregate remains absent;
existing broad aggregate/JIT costs remain. Narrow evidence RowsLoaded increases
875 → 1,017; all-room evidence 15,618 → 19,194. Added dependency rows are not
physical inventory. ReadCount includes terminal reads and is not a bin count.

Peak application working sets were 302.1 MiB baseline, 310.5 MiB optimized and
304.4 MiB candidate. CPU, allocation and database cgroup observations and sanitized
SQL/plan evidence are retained. No material new performance regression was found.

## Strict readiness, conservation and independent integrity

Both candidate commands exit **0**:
`--verify-release-readiness` and `--verify-inventory-conservation`.
Full release readiness verifies **1,007 schema objects**, inventory deduction,
treatment and inter-crew/outside custody topology. There are **34 actual applied
migration records**. The readiness report's expectedMigration marker names its
schema contract target; it is not substituted for the actual migration list.
There is no migration or model change in this continuation.

- Signed authoritative/accounted room bins: **77,642**, difference zero.
- Positive positions: **452 / 77,769 bins**; five negative positions total **-127** remain diagnostic, not clamped.
- In transit **844**; outside custody **1,409**; receipt-held **0**.
- Receiving: **2,627** current-crop active non-test Truck receipts; receipt and ledger totals **109,559**, zero per-receipt mismatches.
- Treatment identity debit/credit: **6,088 / 6,088**, zero difference and zero invalid movements.
- Treatment readiness: **452 identities**, zero blockers. Inventory and topology blockers: zero.

Independent SQL multiset SHA-256 fingerprints and row counts for **113 tables /
269,834 original rows** match before, between and after the checks. All original
transactions, custody records, receipts/revisions, treatment applications, audits
and migration history are unchanged; no added or removed table/row is detected.
Manifest SHA-256:
`1e90aa5ab248e18b83b34ff7023ebfeafc4ad560949f138e58d765deb89a73a5`.
Readiness success does not override the failed source-proof consistency gate.

## Cleanup and next gate

At **2026-10-17T01:29:43Z**, task container
`a2c0a6f46f5211ff8bb4bed49ab16a8707570d354a94373661052cc2abed46eb`
and its exclusive volume
`d9b191088298f0fe455cc282981fccb3c50f0646bf0f2de43103e819d554fb6f`
were deleted after final independent integrity verification. Both were separately
verified absent. Expanded SQL was absent. **41 raw protected diagnostic exports**
were then deleted; sanitized findings, SQL-plan summaries and hashes remain.
Original backup #204 and unrelated resources/files were preserved.

The new frozen Linux build passed. Reviewed-head four CI checks remain successful;
existing 270 distinct affected/contract cases (including 202 mandatory cases), 29
governance tests, model and formatting evidence are retained, not relabeled as
newly executed. No unrelated broad tests were repeated. This continuation changes
only documentation and sanitized evidence; final documentation-head CI is recorded
in the PR Checks. It does not fix the 18-position defect.

**NO-GO.** The next work is a narrowly scoped correction and regression proof for
recorded-custody dependency selection: the same position must have the same
replay/enrichment dependencies whether loaded alone or alongside another room
that requests closure. Preserve required source history, fingerprints, guards
and bounded aggregate reuse. Do not merely remove fingerprint fields or skip
these positions. A design must prove whether filtering incidental enrichment or
loading a stable per-position dependency closure preserves the actual treatment
contract; no implementation choice is approved by this report.

After that correction and human review, any new protected restore requires fresh
scoped approval. Only a passing complete read-only gate permits preparation of
the separately authorized same-database forward/rollback/handback rehearsal.
That stage, authenticated route checks, rollback activation, explicit release
approval, fresh independently verified production backup and strict live readiness
remain outstanding. No rollback/handback operation, merge or deployment occurred.
