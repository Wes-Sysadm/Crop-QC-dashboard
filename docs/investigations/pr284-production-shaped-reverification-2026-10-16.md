# PR #284 — production-shaped performance reverification

**NO-GO for advancing to forward/rollback/handback or release.** Performance
acceptance passes, but the required cross-scope source fingerprint check fails
for WP-7 / lot 1372 / profile 17. The same defect exists in baseline, pre-fix and
optimized readers. This report records the exact failed safeguard without changing
the reviewed implementation or treating a pre-existing defect as permission to pass.

## Authority, source and compliance

Owner Wes's “PR #284 — Production-Shaped Performance Reverification” instruction
records favorable human review of application head
`512940de3714eb4dec7eb26dcab282d1d6289acc`. The subsequent explicit reply on
2026-10-16, **“Approve one read-only #204 restore,”** authorized one local disposable
restore, limited to read-only performance, readiness and integrity checks. Prior
restore approvals were consumed; this was a new approval and was used once.
Application/custody writes, migrations, reconstruction, corrections, production
access, merge/deployment and forward/rollback/handback writes were excluded.

PR [#284](https://github.com/Wes-Sysadm/Crop-QC-dashboard/pull/284) remains draft on
`codex/system-wide-transfer-eligibility`. Fetched main/base remains
`df977d041dae2d449cf6fe5f8581e1f5cae41947`; no incoming integration changes or
unreviewed application changes were found. Four CI job types were successful at
the reviewed head: governance, contracts, Windows pwsh and Windows PowerShell
([latest reviewed-head run](https://github.com/Wes-Sysadm/Crop-QC-dashboard/actions/runs/38942267565)).
GitHub main protection returned “Branch not protected” (404), and effective branch
rules returned `[]`. No settings were changed; documented safeguards still bind.
The earlier deployment `dep-db8qpk3l550s73escjmg` is a historical reference, not a
fresh production observation in this task.

AGENTS, canonical rules, ADRs, change procedure, read/command architecture,
change-scoped testing, release standard and relevant PR reports were refreshed.
Specification 1.2.0 / governance commit
`c17bd4deb477b57aa5ac1584175512d49318263b` applies. INV-001–006, TRT-001/002,
REC-001/002, MOV-001, ROOM-001, AUD-001, OPS-001 and GOV-001 require authoritative
origins/quantity/custody, point-in-time treatment, honest receipt ancestry,
conservation, immutable history and unchanged operation/release guards.
No business rule, writer, model, migration, permission or application code changed.
Nine unrelated untracked investigation reports were preserved.

## Backup, isolation and frozen artifacts

The existing local #204 package was independently rechecked before restoration:

- Package: `cropqc-production-predeployment-20261016-033344.zip`, 20,177,849 bytes;
  SHA-256 `7030fed357e9166ac0da8747cad2c77800950d0d7a2bd71e3a82390646f3b4c2`.
- Compressed dump: SHA-256
  `c62e035f97b2b330921272342a9d23bae4803e56d681025df4adca0040ba997a`.
- Manifest: SHA-256
  `b54a5ce92245bbcc2970f4c2273bd9ff747147f69d735143deee3ef591c734a0`;
  archive and extracted manifest bytes agree. ZIP CRC and every component's size
  and SHA-256 pass, including sanitized configuration, schema and photo manifest.
- Expanded SQL: 345,808,559 bytes; SHA-256
  `c2f8b08833420c257c75b3c18984e2e908057d494b99c6fd5bdf08b8195d5300`.
  It was deleted immediately after restoration. Prior independent remote read-back
  remains recorded evidence; no fresh download or production backup was run.

One PostgreSQL 18.4/Linux amd64 container used `--network none`, no published ports,
no production credentials, no production connection and a dedicated disposable
volume/database. Applications shared only that isolated network namespace.
Every application connection enforced `default_transaction_read_only=on`;
startup schema creation and seeding were disabled. Backup/email execution was
disabled for verification commands. No schema migration ran.

| Version | Source | Frozen image index |
| --- | --- | --- |
| Baseline / retained rollback | `df977d041dae2d449cf6fe5f8581e1f5cae41947` | `sha256:598c26de196ebc3ecfb160fa07534de7d2400ba57c2376e74457b10aa2c028ec` |
| Pre-fix | `efe895913bc54080a52746897a9dea19e12bcf52` | `sha256:e0ff5814d3ff0fc7dcd5fd7c4b23911f697f558ebb2027719a595bba9c366e97` |
| Optimized | `512940de3714eb4dec7eb26dcab282d1d6289acc` | `sha256:b77fa7cd22791b965368a4b9dc6aec3580f826484ab69b6de9e4324ef86a2f9e` |

The reviewed candidate was built from canonical Git LF source archive SHA-256
`4268b823eadb033fe3038a2550072cc4778228965d1d059e5f1fbd6d030d25c7`, using
the previously pinned SDK/runtime inputs. Its local image archive is 150,427,136
bytes, SHA-256 `f78a72de8c6d9199b616d77c514e19fc8ccc82cc89ebd6e27976549461399fad`.
The package is retained locally, not published or approved for deployment.

| Optimized assembly | SHA-256 |
| --- | --- |
| CropQc.Web.dll | `dd1177320187a04fb6ee972e39222da46e351937ad1a38db9e98d3ab418fa2cc` |
| CropQc.Data.dll | `9e8b6a9708d13a6dea9493598862bf23e03a43e8dafc2dd560d610aabfdcf293` |
| CropQc.Shared.dll | `e45767523a88d1b4a52d381a66afe20fcd51eb8d8ccae8e768b0be9133eeab21` |

Archive checksums, Linux architecture, .NET/ASP.NET 9.0.21 and actual DLL identities
were checked before each measured version. Baseline rollback archive SHA-256 is
`78a4576fa9b014bde5bc4eade343a7e9196463a30f78b0fbb71ab7009f714f55`;
pre-fix archive SHA-256 is
`1a6833c0355db2b6e673b115abe9d172330248f4aec03bc7846d6de4edb53ae3`.
These identify usable local binaries, not proven post-#284-write rollback safety.

## Matched performance — PASS

All three versions ran sequentially on the same restored data, with two application
CPUs / 2 GiB and two database CPUs / 3 GiB. No build or test overlapped timing.
JIT remained on at thresholds 100000/500000/500000. Each diagnostic connection used
the same previously recorded planner settings: random_page_cost 1.1,
work_mem 26468 kB, max_parallel_workers_per_gather 1. Production configuration was
not accessed or changed. No JIT disable, timeout increase or resource workaround.

Warm ranges are rounds 1–2; round 0 is a process first pass, not disk-cold latency.
The same room 67 and actual selector methods were used for each version.

| Workflow | Baseline warm ms | Pre-fix warm ms | Optimized warm ms | SELECTs baseline / pre-fix / optimized |
| --- | ---: | ---: | ---: | ---: |
| Direct single-room | 25.9–27.3 | 64.9–66.2 | 72.9–75.3 | 10 / 29 / 30 |
| Room transfer | 43.3–47.2 | 85.0–88.6 | 89.6–92.4 | 13 / 32 / 33 |
| Inter-crew | 8,114.4–8,521.6 | 11,795.2–12,452.1 | 7,982.9–8,113.9 | 31 / 50 / 50 |
| Outside warehouse | 7,891.5–8,043.7 | 11,897.3–12,455.8 | 7,937.2–8,084.1 | 15 / 34 / 34 |
| Processor | 8,255.4–8,292.2 | 11,996.1–12,569.4 | 7,971.9–8,407.1 | 17 / 36 / 36 |

The pre-fix third aggregate covered **59 rooms / 42 labels / 492 groups**:
estimated cost 583,901.09, execution **4,014.419 ms**, JIT **3,928.610 ms /
621 functions**. Optimized broad reads execute only the two baseline aggregates;
the redundant third is absent. Their diagnostic executions still cost 4,077.910
and 4,041.223 ms: this PR does not remove the pre-existing broad baseline cost.
The bounded retained-group lookup replaces the third aggregate, so broad SELECT
count need not decrease. Required missing-room loading for the narrow scope uses
24 missing rooms, 25-room baseline context, nine labels and 95 groups: cost
12,708.77, **22.245 ms, no JIT**. History remains complete on the measured scope.

Remaining warm overhead versus baseline is approximately 46–49 ms for direct room
reads and 42–49 ms for room transfer; compared with pre-fix, about 8–9 ms and 4–5 ms
respectively. Broad ranges overlap baseline variation; there is no remaining
additional multi-second aggregate. These local samples are not a Render latency SLA.
Peak application working sets were 266.4 / 299.1 / 303.8 MiB. No OOM or timeout.
Single-room proof rows remain 875 pre-fix/optimized; all-room proof rows remain
15,618. Full samples, CPU/resource observations, query counts and plan summaries
are in the [sanitized evidence](pr284-production-shaped-reverification-2026-10-16.json).

## Independent inventory proof and exact deviations

All **1,066 canonical positions** have identical identities, locations,
authoritative and committed quantities across all three versions. There are 452
positive positions / 77,769 bins and five pre-existing negative positions totaling
-127, yielding signed room accounting of 77,642. The negative positions remain
explicit blocked diagnostics; no negative balance was clamped or repaired.

Complete serialized outputs match **exactly** between pre-fix and optimized builds
for the five workflows and all-room evidence. This includes original receipts,
movement/parent/debit/credit evidence, application memberships and reversals,
baseline/correction semantics, shared or unknown receipt ancestry and fingerprints.
Offline independent resolver/selector comparison covers 459 outside options, 459
processor options, nine inter-crew options and nine room-transfer entries.

The eight known treatment-proof gains remain 1,928 bins; the two sealed positions
(rooms 57 and 63, lot 2710 / profile 7; 323 and 426 bins) retain zero
`TreatmentSelectionBins`. The six unsealed gains remain exactly **1,179 bins**:
room 11 lots 1565/9380 (140/147), room 15 lot 9342 (77), and room 67 lots
1110/1290/2825 (286/5/524). All eight pass the existing origin guard.
The 20 previously hidden positive positions / 5,974 bins are represented, including
disabled unresolved entries; no projection excess becomes authoritative stock.

The **same 56 origin failures / 10,437 bins**, with the same position IDs and error
messages, persist across baseline, pre-fix and optimized builds. Treatment selector
availability is not final command-origin authorization. The source guard remains
mandatory in `InventoryCommandExecutor`, and no command was submitted in this scope.
Sealed-room treatment-selection quantity is zero even when the model retains an
`AvailableBins` quantity and treatment-level `IsAvailable` flag; submission still
uses the seal guard. An initial offline checker incorrectly equated those retained
fields with movement permission and was corrected to the actual model contract.
No application safeguard was changed to satisfy that checker.

Fifty-five fingerprints intentionally differ from baseline because newly included
`RecordedArrivals` are now dependencies. Every such position has nonempty recorded
arrivals; other baseline evidence fields remain unchanged. Pre-fix and optimized
fingerprints agree for all 1,066 positions. This expected dependency addition is
separate from the failed scope comparison below.

## Failed safeguard — WP-7 original receipt snapshots depend on read scope

The independent comparison of broad retained-group reads against conservative
warehouse reads matched 421 positive positions, then failed on the 422nd. The
remaining 30 positive positions were not certified after the stop.

Exact position: warehouse 4 / room 4 **WP-7**, grower-lot ID 448, lot **1372**,
profile **17**, Conventional BART, crop 2026, **501 bins**.

| Receipt evidence | Broad read | Warehouse and single-room reads |
| --- | --- | --- |
| Receipt 626 current quantity | 64 | 64 |
| Receipt 626 original quantity / identity verified | 56 / true | null / null |
| Receipt 720 current quantity | 56 | 56 |
| Receipt 720 original quantity / identity verified | 56 / false | null / null |

Broad fingerprint:
`8E86A6221D3FED98FCE03626390D38836303C981306F095B174068EE42AB9D1D`.
Warehouse/single-room fingerprint:
`032D2821921684B4BADFB8150FE74529EF31A2B8DAF3B4702D8ACCE4392E9A72`.
Quantity, ledger, projections, movements, applications, receipt ancestry and entity
version arrays otherwise agree. Exact ancestry remains ambiguous across receipts
626, 660, 720 and 774; no allocation was invented.

Targeted read-only reproduction shows the identical mismatch on **all three frozen
versions**, including current main. `InventoryEvidenceLoader` discovers corrected
receipt origins from `ReceiptAdd` rows already restricted to the requested rooms
or warehouse. Receipts 626 and 720 are relevant to WP-7 through retained lineage,
but their triggering original rows are outside that scope:

- Ledger **997**: receipt 626, warehouse 3, room 59, 56 bins, profile 17;
  effective 2026-08-18 22:21:00Z, committed 22:21:57.753488Z.
- Ledger **1185**: receipt 720, warehouse 3, room 58, 56 bins, profile 7;
  effective 2026-08-20 03:31:00Z, committed 03:35:27.306543Z.

The all-room reader sees the original quantity/identity mismatch and loads the
receipt override snapshot; narrower reads do not. The historical-lot optimization's
extra lookup still preserves the original *room* restriction and does not resolve
this cross-room dependency. This is a pre-existing scope-dependent evidence defect,
not a lost aggregate group introduced by reuse. It can produce inconsistent
selector/command fingerprints, so it is not accepted as a harmless difference.
WP-7 also retains the separate existing origin rejection at ledger 1187:
`receipt-change:45678062-5b38-470b-ab7e-58f1db8e0415` credits more than recorded debits.
Resolving fingerprint consistency must not remove that origin rejection or invent
receipt ownership.

The appropriate next development scope is narrowly bounded receipt-origin revision
dependency closure for already-relevant receipt IDs across original rooms, with
broad/warehouse/single-room fingerprint regression proof. Preserve the 56 origin
findings, original chronology and query bounds; do not broaden to all unrelated
receipts or remove snapshot fields merely to force equality. **No implementation
change or repair is made under this preparation-only task.**

## Readiness, integrity and cleanup

Baseline and optimized `--verify-release-readiness` and
`--verify-inventory-conservation` each returned **exit 0**:

- Schema: 1,007 expected objects; all 34 applied migrations unchanged.
- Inventory deductions: 1,139 classified negative adjustments; zero blockers.
- Treatment lineage: 452 current identities; zero blocking issues.
- Custody/topology: zero reported issues.
- Receiving: 2,627 receipts; 109,559 receipt bins = 109,559 ledger bins; zero mismatches.
- Room accounting: 77,642 = 77,642, zero difference; transit 844, outside custody
  1,409, receipt-held zero. Treatment identity debits and credits both 6,088.

Strict readiness does not detect the source-scope discrepancy and is not substituted
for that failed gate. Independent whole-table fingerprints used SHA-256 of canonical
JSON row hashes, sorted as a multiset, plus row counts. All **269,834 original rows
across 113 tables**, including every original audit row and migration history,
remained identical after measurements and targeted failure diagnosis. No new rows,
changed rows, deletes or schema migration occurred.

At **2026-10-16 18:22:48 UTC**, task-owned container
`eee3ca7a993f51f55db1b3c16a034f015a9d6f4bcb94659d0d2c377fc7bdd54f`
and volume `94b2860f543dd58866faa8c71c6b461659f8c35b56dfd9d358c987dba5fa787d`
were deleted; both were independently verified absent. Expanded SQL was already
deleted. Original verified backups and unrelated local resources were preserved.
No cloud resources were created; incremental cloud charges were $0. Sanitized
measurements, plans, failure details and artifact hashes remain; raw exported
inventory/receipt/audit evidence is removed after report compilation.

## Validation retained and remaining gates

No completed broad application tests were repeated. The prior 211 affected cases
(198 mandatory cases), final focused case, 29 governance tests, model/format checks
and successful reviewed-head CI remain evidence for unchanged source. This task
adds exact Linux packaging, read-only restored measurements/readiness and independent
data comparisons; it does not relabel them as new application test counts.
Documentation-only closeout validation and CI are recorded in the PR closeout.

**NO-GO for advancement.** Resolve and review the specific fingerprint dependency
defect, then obtain fresh scope approval for any further protected restore: this
one-time approval has been consumed and its database deleted. Only after the
read-only proof gate passes should a separately authorized one-database
forward/rollback/handback rehearsal test exact compatible application behavior
after new custody operations, without resetting newer business activity. That
rehearsal and authenticated routes remain unexecuted. No destructive Down migration
or old-backup overwrite is an acceptable rollback.

Owner release authorization, current live preflight, fresh independently verified
release backup, activatable compatible rollback and all release health/readiness
checks remain separate prerequisites. Nothing was merged, deployed or changed in
production. No historical correction or RED reconstruction was performed.
