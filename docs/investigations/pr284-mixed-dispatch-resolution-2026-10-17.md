# PR #284 mixed-treatment dispatch investigation

2026-10-17 America/Los_Angeles. Scope: attachment `c7be47ed-3171-454b-a56f-1e198d9fffec`; synthetic investigation and a proven systemic correction on the existing draft PR. No merge, deployment, protected restore, production connection, historical reconstruction or production writes.

## Decision

**Systemic replay defect confirmed (category A). GO for focused code review; NO-GO for release.** A faithful synthetic PostgreSQL reproduction identifies the failed final predicate and passes with a small shared-reader correction. The original protected transaction did not capture its final in-transaction resolver result, so the synthetic result is not relabeled as a newly observed #207 result. A reviewed, newly frozen candidate and outstanding protected mixed-treatment/compensation rehearsal remain necessary.

The accepted 286-bin internal Return/rollback/handback proof is retained. A separate compatibility check now establishes that the old rollback reader cannot interpret the newly supported mixed-dispatch history correctly. Its immutable package is preserved; its compatibility scope cannot be extended to this operation without correction and verification.

## Source and rule assessment

Fetched `origin/main`: `ea112eae6aca92778e9338e60c804ac9edd444fd`. Starting reviewed head: `34963880cc3b02a7978c91cc74d54f0cfa3f67dd`, branch `codex/system-wide-transfer-eligibility`. No incoming main commits. Knowledge comparison reports expected proposal differences in traceability/writer registry and historical reports; existing untracked release reports preserved.

Specification 1.2.0; governance commit `c17bd4deb477b57aa5ac1584175512d49318263b`; ADR-001–008. Applicable INV-001–006, TRT-001/002, REC-001/002, MOV-001, ROOM-001, AUD-001, OPS-001 and GOV-001. Ledger/custody establishes physical quantity; movement allocations establish treatment; receipt ownership remains independently proven or unknown. No foundational rule, writer authorization, concurrency control, final guard or release safeguard changes.

## Exact failing predicate in the reproduction

The same synthetic history runs on disposable PostgreSQL 18.4 using the frozen pre-fix candidate Data/Shared DLLs and the verified production-base Data/Shared DLLs. Both fail with the reported final-reconciliation error. The test instruments the existing observer, retains the executor context through a test-only factory, and resolves evidence inside the same Serializable transaction immediately after each persisted source line. No guard is removed and no diagnostic is registered in either application.

| Stage | Authority | Current projections | Operable before fix | Operable after fix |
|---|---:|---:|---|---|
| Input | 652 | 442 | Yes, recorded replay | Yes, recorded replay |
| After existing recorded-cohort reconstruction | 652 | 652 | Yes | Yes |
| After first two-bin untreated debit | 650 | 650 | Yes | Yes |
| After second two-bin treated debit | 648 | 648 | **No** | **Yes** |

At rejection: final position exists; this is the source, so destination admission is false; independent-source exemption is false; quantity equality is **true**; `IsOperable` is **false**, with `MixedTreatmentAmbiguity`. `UsesRecordedCohorts` and `UsesIndependentCohorts` are false at the final read. The exact final guard in `InventoryCommandOperations.cs` remains unchanged.

Physical writes inside the attempted transaction are correct: two -2 ledger entries and two +2 transit allocations. The source segments contain 580 untreated and 68 treated bins. Rebuilt segments use `r:` plus the existing operation-key hash namespace and retain exact application/receipt links and shared ancestry. No new inventory origin is created.

The reader's `InventoryCohortEvidence.Related` previously matched by parent alone. Both ledger rows have the same inter-crew parent. Consequently, replay associates **both** two-bin departures with **each** two-bin debit: four allocated bins do not match that debit's two bins. It falls back to an unallocated withdrawal, combines treated and untreated survivors into Unknown, and cannot prove the rebuilt projections. This causes the operability failure even though 648 equals 648. The baseline 210-bin gap is not the failing equality predicate.

The persisted canonical keys already distinguish the lines: `command:0:out` pairs with `command:0:s<segment>`, and `command:1:out` pairs with `command:1:s<segment>`. The same load can receive later commands with different keys; those must not be retroactively attached to earlier debits.

## Correction and scope

`InventoryCohortEvidence.cs` now requires canonical inter-crew parent matches to also match the persisted command line and recorded commit timestamp. Direction suffixes `:out`/`:in` are normalized; the delimiter prevents line 1 matching line 10. Existing movement suffixes, including reversal references, remain supported. Missing canonical line/commit evidence fails matching. Legacy invariant versions retain their prior parent proof contract; no fictional keys or timestamp equality are imposed on old transfer legs.

This is a reader pairing correction. No ledger, projection writer, command journal, retry implementation, readiness check, migration or database fence changes. The final source reconciliation guard is byte-unchanged. No lot, room, variety or fixed quantity is special-cased. Other parent types and parentless correction matching are unchanged.

The new PostgreSQL regression asserts both treatment selections, every intermediate quantity, custody conservation, exact application membership, preservation of the shared 56-bin cohort, unchanged original receipts/ledger/transfer/application/source/movement evidence, retirement evidence for 442 historical projected bins, retries, conflicting intent, stale selection and a later allocation appended to the same parent. Focused matching cases reject wrong line, command, parent, missing key and different commit time while preserving legacy behavior. Traceability adds the regression to mandatory INV-003, TRT-002 and MOV-001 evidence without removing any contract.

## Authoritative RED evidence and the separate projection gap

The retained read-only [RED proposal](../inventory-architecture/pr278-red1242-reconstruction-proposal.md) and [investigation](../inventory-architecture/movement-event-authority-investigation.md) establish the following historical observation, not a refreshed live query:

| Ledger event | Receipt / custody | Bins | Treatment |
|---|---|---:|---|
| 3821 | Receipt 2252 | 56 | Untreated |
| 3835 | Receipt 2266 | 56 | Untreated |
| 3863 | Receipt 2294 | 68 | Untreated |
| 3931 | Receipt 2334 | 68 | Untreated |
| 3978 | Receipt 2343 | 68 | Untreated |
| 4016 | Receipt 2378; application 29/source 40 | 70 | Application 29 |
| 4024 | Receipt 2386 | 70 | Untreated |
| 4066 | Receipt 2419 | 70 | Untreated |
| 4100 | Receipt 2439 | 70 | Untreated |
| 4074 | Transfer 411; movement 801 | 56 | Untreated, shared receipt ancestry |
| **Total** | | **652** | **582 untreated + 70 treated** |

The observed projections were segment 729: 372 shared untreated, segment 730: 70 treated. The absent representation is the three later 70-bin receipts (210). Upstream transfer funding includes receipt 2262 / origin 3831 / debit 4073, but movement 801 does not assign exact receipt ownership to its 56 bins. Neither matching quantities nor the room supplies that attribution. The treatment application does not attach to later arrivals.

The synthetic fixture uses different generic IDs and synthetic timestamps, nine receipt origins plus one 56-bin transfer, with the same 652/442 and 582/70 relationships. It copies no protected rows. Initial legacy history uses established test-fixture proof structures; the tested dispatch and later allocation edit run through the canonical executor. Its on-command, audited reconstruction is the already-existing normalization path, not a newly introduced repair operation. No production projection was repaired and the separate 210-bin RED reconstruction remains excluded.

## The 66-bin ancestry claim

The retained [original investigation](system-wide-transfer-eligibility-2026-10-15.md#frenchman-1110-is-one-regression-not-a-special-case) records MCD-15 / lot 1110 / profile 19 effects `+32 +64 +64 +60 -64 +66 +64 = 286`, ledger 361/369/376/380/429/481/664 and transfer parents 6/10/15/22/56. Correction 429/430, revision `bb3504ec-722c-4986-9fd6-66775be96350`, audit 30341 relocates receipt 290's recorded custody from 67 to 68; transfer 56 subsequently returns 64. This proves quantity/custody, not a chosen surviving allocation.

The actual #207 baseline replay was 156/66/64 with null ReceiptId throughout. Transfer 22 / debit-credit 480–481 supports the 66-bin arrival. The retained evidence does **not** establish an exact mapping from that departure to a surviving receipt allocation through its upstream origin/correction history. Those raw allocation records were not retained after protected cleanup, and this task does not restore #207. No claim is made that a missing mapping definitively never existed in the system.

The earlier 220 shared + 66 exact split belongs to `HistoricalInterCrewDispatchTests` and its synthetic descendants, including their synthetic treatment application. It is not actual #207 ownership or treatment history. Actual ownership remains unknown. The already accepted protected internal movement of all 286 conserved bins demonstrates that shared quantity can support a non-receipt-specific operation; exact receipt-specific corrections still require separate proof. This reader correction does not invent that proof.

## Baseline comparison and rollback consequence

Verified production-base DLLs: Data `594a1bcb0d9263ad75097f817cbd3320530bf6b0b7f7a78499ceef743fc5153e`, Shared `627cd2ced0250c4d2a9cd513f04b03afe8752f81f0fef881fde8f63a9edb98e4`. Pre-fix #284: Data `ec2483e0a83a085527d11a06b0f10b898eec818d56496219bcb8cade4291d45f`, Shared `e45767523a88d1b4a52d381a66afe20fcd51eb8d8ccae8e768b0be9133eeab21`. Both run the identical final synthetic fixture and fail the same predicate. This is **not** characterized as a newly introduced #284 regression.

The old Shared DLL also resolves the captured post-fix committed synthetic evidence as 648 authority / 648 projections / `IsOperable=false`, `MixedTreatmentAmbiguity`. This is a direct reader compatibility check, not a new full Linux/Render rollback rehearsal. The patched rollback `8bf615c` carries that same old Shared DLL. Its accepted seven-field receiving-history preservation is unaffected, but its reader compatibility does not cover newly committed mixed-dispatch activity. Keep its published digest immutable. A narrowly reviewed compatible reader backport/package and affected mixed-workflow compatibility proof are required before release; do not disable readiness or roll back the database.

## Validation and handoff

Validation: five new focused cases PASS; affected set initially 100 PASS / one HTTP host fixture failure, then that HTTP case PASS after the clean rebuild; 222 mandatory contracts PASS / zero skips; 29 governance cases PASS. Solution restore/Release rebuild PASS (74 warnings, zero errors), model unchanged, changed-file formatting and whitespace PASS. Final two additional integrity assertions were verified by rerunning the five focused cases. No full application suite or MSI change. Final source identity and GitHub CI belong to the PR handoff.

The [sanitized proof summary](pr284-mixed-dispatch-proof-2026-10-17.json) records the comparisons and cleanup. Local evidence: `local-test-runtime/pr284-mixed-synthetic` contains synthetic-only TRX, same-transaction snapshots, `predicate-comparison.json`, `comparison.json`, assembly hashes and `rollback-reader.log`. These managed-assembly comparisons used a local Windows .NET 9 test host and PostgreSQL 18.4, not a newly frozen Linux release image. Original protected evidence is limited to the already sanitized historical reports; no new protected export was created.

Completed protected internal rollback/handback, 452-position scope comparison and prior proofs are preserved. No broad recertification was requested. Current affected tests cover the shared inter-crew matching dependency and applicable mandatory contracts. A test-environment-only stale Linux web-assets manifest initially broke one HTTP host startup; a clean local solution rebuild and the affected test rerun correct that fixture issue without changing application behavior.

Required next steps: focused human review of this correction; freeze the changed application; resolve rollback reader compatibility with an immutable reviewed package; obtain a fresh specifically scoped protected rehearsal grant after review; capture the real final predicates and complete the remaining mixed-treatment/cross-company/partial-compensation gate. A passing synthetic case is not substituted for that protected release gate. Production backup, strict live readiness, deployment and independent verification remain later, separately authorized release steps. PR remains draft; PR #285 remains closed.

Cleanup independently verified at 2026-10-18T01:44:24Z (October 17 local): all synthetic fixture databases deleted; container `897f81d8f23ca20007ebb262fafaf36c02e56185e7c6d3ecb0a71c08b79fe3d6` and volume `3475753f8a40f3a6161f0087b97419a381c89ecd4d25d69cd8d87a93912e9e52` absent. The synthetic cluster used a loopback-only ephemeral port, no production credentials or integrations. No protected backup opened, no production connection, no cloud resources/charges. Synthetic diagnostic files and frozen immutable prior packages retained; unrelated resources preserved.
