# Six treatment-lineage blockers and PR #271 release investigation

## A. Executive summary

**NO-GO. No production repair, deployment, migration, merge, or new backup was performed.** The six stored-projection failures remain. Their signed inventory histories reconcile to **1,817 authoritative bins**, while 16 positive segments contain **3,356 projected bins**, an excess of **1,539**. This is not evidence of 1,539 additional physical bins.

All six have the same historical status-alias pattern: blank-status inventory remains positive after an operation materializes a second `Conventional` representation and consumes/transfers from that representation. All six affected pools are recorded as untreated. None has a treatment application in the affected room or an application link on its segments. The historical writer defect is already addressed by status normalization changes on main; those changes did not automatically rewrite these records.

The existing canonical resolver independently proves the **two Evans-5 pools and WP-7** as untreated. It refuses **DH-15, WP-5 and WP-8**, where legacy void/count corrections invalidate its exact-arrival proof. Their audits explain the signed quantity changes, but do not prove the surviving physical allocation per receipt. We retain these limitations and do not treat a correction reason as proof of physical custody.

The draft code change adds evidence explanations to the failed readiness output. **It does not suppress any of the six blockers, change inventory eligibility, alter treatment proof, or execute normalization.** This closes a diagnostic gap; it is not a release-unblocking fix or a production repair tool.

Backup #187 completed successfully during this investigation. Backup #186 passed a fresh independent local restore. Neither fact clears the inventory gate.

## B. Exact findings and classification

| Finding | Warehouse / room ID | GrowerLot / lot / profile / variety | Authority | Explicit | Excess | Classification and resolution |
|---|---|---|---:|---:|---:|---|
| DH-15 | DH 2 / 47 | 495 / 2350 / 18 / DANJ | 202 | 598 | 396 | B/C projection duplication; E exact allocation after void. Conditional audited reconstruction after receipt evidence review. |
| Evans-5 | EBS 1 / 15 | 511 / 3152 / 2 / GALA | 61 | 162 | 101 | B/C; untreated pool proven. Separate approval for derived projection reconstruction; exact receipt custody remains unknown. |
| Evans-5 | EBS 1 / 15 | 642 / 9682 / 2 / GALA | 252 | 536 | 284 | B/C; untreated pool proven. Same bounded reconstruction requirement. |
| WP-5 | WP 4 / 2 | 398 / 1084 / 2 / GALA | 10 | 130 | 120 | B/C; E receipt attribution after void and mixed consumption. Conditional reconciliation. |
| WP-7 | WP 4 / 4 | 448 / 1372 / 17 / BART | 1,122 | 1,568 | 446 | B/C; untreated pool proven, receipt allocation ambiguous. Reconstruct shared pool without assigning 122 consumed bins to a guessed receipt. |
| WP-8 | WP 4 / 5 | 495 / 2350 / 18 / DANJ | 170 | 362 | 192 | B/C; E exact receipt attribution after count correction. Conditional reconciliation. |

B = incorrect historical projection; C = legacy status normalization; E = insufficient historical evidence for exact attribution. No finding is classified D merely because it blocks deployment. No contradictory *treatment application* was found for these six; physical count and receipt allocation are not thereby proven. F applies to the historical shared writer behavior and the current diagnostic gap. The 35 broader unresolved positions below must not be relabeled B without evidence.

The exact failing rule is `TREATMENT_LINEAGE_EXCEEDS_AUTHORITATIVE_INVENTORY`: sum positive stored segments after status-key normalization and compare to positive authoritative inventory. Full readiness job `job-db3gb82j9qps73fhb24g` failed with six findings on deployed main. A read-only run of the amended service against the restored database still returns exactly six blockers.

## C. Chronological reconstruction

The [evidence annex](six-lineage-blockers-evidence-2026-10-08.md) contains a separate table for every finding, every signed ledger event, both effective and recorded timestamps, running balances, segment versions, movement IDs, parent references and audits. Preserve both clocks: WP-7 run 111 was recorded September 21 with an effective September 17 date; sorting only by business date creates a false pre-arrival depletion.

### DH-15: 588 → 396 → 330 → 202

Receipts 1338, 1358, 1373, 1389, 1403 contribute 313; transfer 335 adds 66; receipts 1539, 1555, 1568, 1569 add 209, reaching 588. Transfer 369 removes 192 to WP-8 (ledger 2972/2973, movement 506), leaving 396 on segment 407. Receipt 1373/TR508944 is voided for `DOUBLE TICKET` (override `0f977af1-1376-4fa7-91a4-a56bbf512fb0`, ledger 3000 -66, audit 123203), leaving authority 330. Transfer 370 removes 128 (ledger 3001/3002, movement 516) from newly materialized segment 557, leaving 202. Segment 407 still says 396; segment 557 says 202. Audit 123204 records transfer 370, immediately after the void. Do not infer a second 396-bin physical allocation.

### Evans-5 / 3152: 25 + 76 - 40 = 61

Transfers 337 and 346 bring 25 from room 20 and 76 from room 21 (movements 352/361 into segment 414). Transfer 350 sends 40 to room 7 (movement 365 from newly materialized segment 426). Segment 414 retains all 101 arrivals while 426 retains the legitimate 61-bin remainder. Audits 106635, 106644 and 106648 corroborate the parents. Source same-identity receipt candidates are 583, 595, 612, 663, 668, 1275, 1340, 1343, 1372, 1386 and 1391; destination movements do not select exact surviving receipts. These are ancestry candidates, not an allocation assignment.

### Evans-5 / 9682: 104 + 180 - 32 = 252

Transfers 341 and 349 bring 104 and 180 from rooms 20 and 21 (movements 356/364 into 419). Transfer 351 sends 32 to room 7 through alias segment 428 (movement 366). Segment 419 retains 284 and 428 retains 252. Audits 106639, 106647 and 106649 corroborate this sequence. Same-identity source receipt candidates: 582, 589, 602, 607, 617, 664, 1250, 1264, 1270, 1304, 1311, 1322, 1336, 1345, 1353, 1357, 1382, 1392, 1399, 1750, 1756, 1913. No exact surviving receipt split is proven.

### WP-5: earlier empty boundary; 308 - 188 + 6 - 6 - 110 = 10

An initial 8-bin receipt and matching depletion reach zero. The subsequent occupancy is reconstructed in the annex from receipts 169, 189, 201, 206, 318, 378, 444, 633, 709, 767 and 1069 and earlier consumption. Run 67/entry 221 removes 188, leaving 120 on segment 65 (movement 299, audit 99709). Receipt 1339/TR508908 adds 6, then is voided for `Wrong variety` (override `61ff4cac-48ce-4331-8306-8d2b0bdd21a8`, ledger 2939 -6, audit 120803). Run 102/entry 357 removes 110 from newly materialized segment 597 (movement 573, audit 126455), leaving 10 while segment 65 still says 120. Exact physical disposition of the incorrectly ticketed six is not established by the void reason.

### WP-7: 446 - 122 + 798 = 1,122

Inter-crew transfers 16–22 move 446 from McDougall room 66 to WP room 4. Dispatches 584–591 match receives 601–607 and 615. All eight transfers, including later transfer 34, are fully received in recorded workflow; none is partial or in transit. Run 111/revision 134/entry 377 removes 122 (ledger 3375, movement 623, audit 137045) from new alias segment 630, leaving 324 there while the original 446 remains on segments 617/618/619. Transfer 34 then adds 798 through movements 703–705 (dispatches 700–702), reaching authority 1,122. Original segments plus later arrivals hold 1,244; alias 630 adds another 324: total 1,568. The 446 excess comprises 324 duplicate remainder plus 122 consumed historical representation.

Preserve source-room audit 145038: on September 23 segment 160 was explicitly reconciled from 802 to 542 to remove 184+72 duplicate representations and four rematerialized dispatch bins. Transfer 34 subsequently carries exactly 542 from that source plus 72 from segment 154 and 184 from 148. Treating its prior 802 as another current source would repeat an already-audited error.

Receipt references 626, 660, 720, 774 are retained in the transfer lineage, but their present receipt counts are respectively 64, 54, 56, 64, not the pool quantities 72, 250, 184, 134. Legacy transfer `ReceiptId` is therefore not sufficient evidence that each destination pool is the exact original ticket quantity. The resolver correctly reports receipt attribution as ambiguous.

### WP-8: 250 + 192 - 237 + 128 + 64 - 64 - 23 - 140 = 170

Receipts 1532, 1572, 1587, 1594, 1626 contribute 250. DH-15 transfer 369 adds 192 to segment 552. Run 91/entry 313 consumes 237 from alias segment 553 (movement 507, audit 122217). Transfer 370 adds 128 to 553. Receipt 1754/TR509156 adds 64, then is corrected to zero: override `95eeef85-d8d8-4da4-a2b2-dfbd68e27656`, ledger 3142 -64, audit 126585. The recorded reason says it was already received at DH and another WP ticket should not have been made. Runs 110/entry 376 and 115/entry 382 remove 23 and 140 (movements 622/628; audits 137043/137053). Segment 553 now equals authority 170, while 552 still holds its original 192. Do not restore the zeroed ticket or deduct 192 physical bins to make projections balance.

## D. Root causes and evidence limits

The historical source before commit `28a4c9a` uses exact `IdentityKey == IdentityKey(snapshot)` queries in `RoomTreatmentService.MaterializeAsync`/segment lookup. Blank and redundant `Conventional` suffixes can therefore select different rows for the same fruit. Creation times and the exact movement/ledger arithmetic reproduce that mechanism for all six. Commits `28a4c9a` and `5b43962` later normalize status keys and add guarded historical reconciliation. This is source-and-record evidence of the mechanism, not proof of the exact deployed SHA on each September event; historical deployment/log coverage was not established.

The three legacy corrections add another limitation: `InventoryAvailabilityResolver.ProveUntreatedPool` requires each ReceiptAdd to match a current, nondeleted receipt with the same quantity. Deleted receipts 1339/1373 and receipt 1754 now at zero deliberately fail that proof. Removing those checks would be unsafe. Their complete override records, compensating ledger entries, operation keys and audits explain the changes, but a general correction-proof adapter would need independently tested revision chains and treatment/custody safeguards. This PR does not invent one.

## E. Shared systemic causes

Status aliases, legacy lazy materialization, shared pools carrying representative receipt IDs, corrections outside the original lineage update path, and backdated operational clocks interact. They must be separated: quantity authority is the ledger; treatment travels with actual movement allocations; receipt identity alone is not surviving receipt custody; a raw segment balance is a projection.

The generic readiness output previously reported only an excess total. It could not distinguish a proven untreated pool with a stale projection from unsupported treatment/correction evidence. The new evidence assessment explains that difference while keeping the strict stored-projection failure.

## F. Custody and point-in-time treatment implications

Current database custody for these six is exactly the table in B. There is no unresolved inter-crew leg among WP-7 transfers 16–22/34. Room transfers 350/351 send 40/32 onward to room 7; DH-15 transfers 369/370 deliver 192/128 to WP-8. These are confirmed paired ledger/movement records, not room-card inference. This investigation cannot replace an onsite count or establish undocumented lost/misticketed fruit.

No application event occurs in any of the five affected rooms. The recorded lineage is untreated before and after the listed removals/transfers; there is no legitimate basis to add a treatment to these pools simply because another room or the same variety was treated.

Specific counterexample: lot 1372 also has **106 treated bins in McDougall room 65**, a different physical branch. Transfer 121/ledger 953 brings 106 at August 18 19:11 UTC, before application **1** at 20:09 UTC. Application source **4** records those 106 (`u` → `u|a:1`); application 1 totals 1,368 across its sources and audit **57218** records it. Identity correction `b94b079d-8e24-474d-a11b-1fdc79e39880` later transfers the 106 between legacy/canonical identities (ledger 2738/2739, movement 450, segment 19 → 503, audit **112768**) without changing treatment. There is no outgoing lineage from 503 into the WP-7 branch. Do not remove its treatment or propagate it to WP-7.

WP-7 ancestry is instead room 58 segment 147 → room 66 segment 148 (movement 89/transfer 211); room 59 segment 153 → 154 (92/214); room 60 segment 155 → 156 (93/215); room 61 via room 68 segment 122 → 159 (74/198 and 95/217); and shared pool 160 from rooms 61, 63 and 57 (96/218, 97/219, 182/281). Source audits are listed in the annex/query set. Source room 21 application **19** is receipt-specific to receipt **990**, lot **9722**, not Evans-5 lots 3152/9682. It does not linger in the source room or attach to every GALA shipment.

TR110059 remains receipt **2492**, 40 bins, EVANS-BKT room **23**, ledger **4167**, arrival movement **850**, current untreated segment **786** (version 2). No subsequent transfer to LAMBCA15 is recorded. The earlier receipt-2161 identity correction remains unchanged (64 bins; ledger 4172/4173, movements 855/856, historical/current segments 791/792).

## G. Additional susceptible records

The annex lists every exact affected identity for the following populations. These populations overlap and must not be added together as missing physical inventory.

- **35 positive canonical positions / 10,265 bins** require reconciliation: 29 UnknownTreatment, three MixedTreatmentAmbiguity, and the three corrected-receipt UnsupportedHistoricalEvidence findings above. The mixed positions are room 18 / GrowerLot 336 / GALA, and room 73 / GrowerLots 430 and 187. These are E or possible A pending evidence, not automatically stale-only.
- **13 zero-authority positions / 644 positive projected bins**, across rooms 7, 8, 15, 16, 20, 21 and 60. These are additional B/C candidates requiring individual replay; the positive-inventory excess gate does not report them.
- **Five negative canonical positions / -127 bins**: room 60 GrowerLots 141 (-40), 477 (-18), 502 (-5); room 61 GrowerLots 476 (-20), 478 (-44). Their historical causes remain unverified; no true-up is proposed.
- **784 segments**: 309 current positive, 470 current zero, five Historical zero, no negative segment quantities. Depleted/historical rows are retained evidence, not fruit to restore.
- **124 legacy overrides**: 55 reclassifications, 50 quantity corrections, 13 voids, six location corrections. The scan found no ReceiptInventoryOverride occurring after an application directly on that same receipt; this does not certify every identity reclassification or shared room pool. There are 71 identity corrections and 63 canonical commands, separately fingerprinted.
- **42 inter-crew transfers**: 22 Received, all with received count equal loaded count; 20 InTransit totaling **844 bins** (IDs 1–15 and 38–42). This is recorded transit custody, not proof of missing fruit. Existing live topology verification had zero invalid custody rows. No production partial receipts exist in these legacy rows, so future exact partial-allocation behavior still requires PR #271's regressions.
- **409 room transfers**, 43 outside-warehouse transfers and eight loss records were included in the operational snapshot. The six relevant transfer chains conserve their signed quantities. The broad extraction does not independently certify every historical branch of every transfer.

Previous live canonical readiness reports 438 positive positions totaling 72,318 bins, of which 403 are operable / 62,053 bins. The restored #186 diagnostic returns 1,024 positions: 438 positive, 581 zero, five negative. It has five fewer receipts/arrivals totaling 230 bins than the current snapshot; the three newer positions are room 7/GrowerLot 632/profile 4, room 23/404/1, and room 1/398/4. All pre-existing operational rows and treatment links compare exactly, including all six findings and all 35 unresolved positions. Do not confuse the raw full ledger total 72,191 with current positive-only 72,318 (difference includes -127).

## H. Required code corrections and draft scope

Changed area: read-only treatment readiness diagnostics and its direct regression fixtures. No shared resolver, writer, migration, UI, WinForms, backup or custody behavior changes.

`TreatmentLineageReadinessService` now performs one bounded canonical evidence load for the reported failed rooms and adds segment IDs, treatment/receipt confidence, canonical blocker codes and historical projection explanations. Mismatched quantities between the initial scan and the later evidence read yield `EvidenceChanged`; missing positions yield `EvidenceUnavailable`. Both remain blocked. `ProvenPoolRequiresProjectionReview` explicitly does not mean repaired or release-ready.

The six production-shaped fixtures preserve ledger/movement IDs, quantities, clocks and statuses, with a synthetic watermark and no actor credentials. Tests cover all six, missing arrivals, treatment conflicts, treatment during occupancy, treatment before later arrival, negative projections, unavailable/changed evidence, conservation, immutable evidence and the fact that a proven pool still fails the persisted projection gate.

The historical alias writer fix is already on main. A future reviewed reconstruction command must supersede only proven projections, use concurrency/version/fingerprint guards, preserve audit/movement/application history, support idempotency, and refuse ambiguous treatment. A future receipt-revision proof adapter must validate exact before/after correction chains; a reason string or administrator role alone cannot prove allocation identity. Neither is smuggled into this diagnostic PR.

Source references: `src/CropQc.Web/Services/TreatmentLineage144CorrectionService.cs` (`VerifyAsync`, `Assess`); `src/CropQc.Shared/Inventory/InventoryAvailabilityResolver.cs` (`Replay`, `ProveUntreatedPool`, `ValidTreatment`, `IsAliasDuplicate`); `src/CropQc.Data/Inventory/InventoryEvidenceLoader.cs`; `src/CropQc.Web/Services/RoomTreatmentService.cs` (`MaterializeAsync`); existing `InventoryAvailabilityTests.Wp7_accounts_for_duplicate_and_consumed_history_without_inventing_receipt_allocation`.

## I. Separate production repair proposal — NOT APPROVED OR EXECUTED

See [exact before images and proposed after fields](six-lineage-blockers-proposed-repair-2026-10-08.json). This is a non-executable approval package, not SQL to run. IDs allocated at execution, approval-bound command keys and execution timestamps cannot be truthfully preassigned.

| Pool | Existing segment IDs to supersede | Proposed new shared quantity | Preconditions beyond exact before images |
|---|---|---:|---|
| DH-15 / 2350 | 407, 557 | 202 | Review void 1373 and paired transfers; resolve physical/receipt uncertainty or explicitly retain it without fabricating exact custody. |
| Evans-5 / 3152 | 414, 426 | 61 | Revalidate exact arrivals 25+76, transfer-out 40, no treatment. |
| Evans-5 / 9682 | 419, 428 | 252 | Revalidate 104+180-32, no treatment. |
| WP-5 / 1084 | 65, 597 | 10 | Review void 1339 and all depletion parents; physical misticket disposition remains unknown. |
| WP-7 / 1372 | 617, 618, 619, 630, 669, 670 | 1,122 | Preserve source correction 145038; acknowledge unknown surviving per-receipt split, including 122 consumed. |
| WP-8 / 2350 | 552, 553 | 170 | Review receipt 1754 correction chain and all DH/WP movement parents. |

Proposal: retain each old row with `CurrentBins=0`, `Disposition=Historical`, `RetiredQuantity=before.CurrentBins`, approved timestamp/command, incremented concurrency version; create one canonical **shared** Untreated/u pool per approved identity, `ReceiptId=null`, no application links. Preserve raw old keys and historical receipt references. This intentionally does not create a claim of exact per-receipt custody. Do not change any authoritative ledger, receipt, transfer, run, movement, application, source or application link. Projection arithmetic becomes 1,817; authoritative quantity delta is zero. Merely deleting alias rows or decrementing selected receipt slices is not an acceptable substitute.

For three corrected-receipt cases, this remains **conditional**: current canonical proof is insufficient, so there is no approved/executable exact physical repair. If further evidence contradicts untreated custody, preserve the block and obtain manual treatment/custody reconciliation rather than force this proposal.

Audit requirements: named approver and executor, reason, immutable before/after JSON, all segment versions, correction/ledger/movement/audit references, proof limitations, source and target fingerprints, verified backup ID/SHA, idempotent operation key, zero ledger/receipt/movement/application quantity deltas. Update and audit atomically under locks; any changed version, added treatment, new movement or changed quantity aborts the whole approved scope.

Rollback: before any subsequent operational use, a separately authorized compensating correction may supersede the new pool and restore the exact before projections from the audit, with new audit/version increments. Never delete the correction audit or replay old movements. Once new operations use the reconstructed pool, do not blindly restore old projections or a whole database backup; trace descendants and perform a reviewed forward correction. Retain additive schema during application rollback.

Independent verification: compare protected row counts/hashes before and after; re-run canonical and full release readiness; validate each six-pool quantity and treatment, original treatment 1/segment 503, all transfer pairs, no invented exact receipt allocation, unchanged TR110059/2161, idempotent retry and stale/concurrent refusal on a disposable restore. Obtain onsite count/paperwork where needed. No blanket repair of the 35 unknown or 13 depleted populations is proposed.

## J. Automated regression and evidence results

Base/current main: `e58d73a12c8bf0cc5f0f2333e442497e2b91b807`; branch `codex/lineage-blocker-investigation`. PR #271 remains separate at `58e6d29edc9d81326274a3d7ff73c56dde20fcc5`.

Restore and solution build passed (64 existing warnings, zero errors at first build; final incremental build seven existing warnings, zero errors). **59 focused tests passed, zero failed/skipped**: `TreatmentLineageReadinessEvidenceTests`, `TreatmentLineage144CorrectionTests`, `InventoryAvailabilityTests`. No full-suite or browser/hardware certification is claimed; the changed code only reads readiness evidence. No MSI is required.

EF pending-model check passed: no model changes. Formatting passed using repository precedent `dotnet format ... --verify-no-changes` for all three changed C# files; `git diff --check` passed. PostgreSQL rehearsal uses a newly restored disposable #186 database with `default_transaction_read_only=on`, including the amended readiness service: all six blockers remain and annotations split three proven/three unresolved. The fixture/diagnostic does not normalize or save entities.

Final production integrity check at **03:36:18.520923 UTC**: all 15 operational table hashes/counts and signed ledger total 72,191 match the 03:10:00.288293 baseline. All **175,132 pre-existing audits** retain hash `7bb07e345996703fcdf3ae0ed8910712`. Only audits **175138–175140** were added: backup187 completion, queued notification and sent notification from the already-running authorized worker. No inventory audit or prior record changed. See [before/after integrity evidence](six-lineage-blockers-integrity-2026-10-08.json). No production write was issued by this task.

Evidence inputs: production repeatable SELECT snapshot at **2026-10-08 03:11:31.590739 UTC**, read_only=on; targeted production audits; current GitHub/Render reads; backup records/logs; fresh local restore. Source SQL is included in `six-lineage-blockers-operational-evidence.sql`, `six-lineage-blockers-audits.sql`, `six-lineage-blockers-fingerprints.sql`, and `six-lineage-blockers-backup-status.sql`. Run only with read-only credentials/session and UTC; no backup command is present. Raw operational exports remain outside source control; the annex and fixture retain the relevant identifiers and results.

## K. Backup and production state

Read at **2026-10-08 03:27:03.502522 UTC**: #187 **Succeeded**, completed **03:25:05.439933 UTC**, verified **03:25:05.298488 UTC**, all **11,795/11,795** objects. Render log at 03:25:05.565758277 confirms verified completion for job `job-db3frh59fdbs73dk3v9g`.

- Package: `cropqc-production-predeployment-20261008-022001.zip`, **17,363,625 bytes**.
- SHA-256: `abd3ca8519383ad8b378c1b5eb623e14bcbe78a229fbfb4d4951673ef02a8cb2`.
- Drive package ID `1zxfJ_cPenfgdEziVVsxNhOE0jqkgW5a4`; sidecar `11ZssH4b2BGGpJeMEJOvKhWj9TtRu1EWL`.
- Lease released; singleton lease row has null LeaseId and ExpiresAt; no stale lease cleanup needed.
- These are worker-recorded/read-back verification and log evidence. #187 was not independently downloaded/restored in this investigation; future release must meet its then-current independent recovery policy.

#186 remains retained and recoverable: `cropqc-production-manual-20261007-235333.zip`, 17,317,796 bytes, SHA `0170afc86576e3c57483ebdf1cc3b431555b545bf1cd9e454e7e020bdf22ccc0`, package `1_PQkedyFJGHV7MM7JadEQobplgAWcmaa`, sidecar `18npYQ_Qm0oRTDCG2h52RkBxOf5Bj3bwf`. Fresh local verification at **03:09:53.250387 UTC** matched size/hash, safe archive paths/CRC, four component checksums, 311,151,169-byte SQL dump, and 11,768-photo manifest, then restored PostgreSQL successfully. Snapshot is **2026-10-07 23:53:37.406346 UTC**; it predates five later receipts and must not replace newer production activity.

Web is still PR #270 SHA `e58d73a12c8bf0cc5f0f2333e442497e2b91b807`, live deployment `dep-db3dkn7avr4c739bcuq0`. Backup cron is separately deployed PR #267 SHA `95518dec131d44b01e2dc8fcfcbb82f508a8f399`, deployment `dep-db3ath142hec73955je0`; do not assume matching SHAs. Confirmed workspace `tea-d7uc4ippo60c73ebn4mg` was explicit in every Render request. No custody migration tables exist from #271; newest migration remains `20261002031709_BoundedBackupSnapshotProgress`.

Last verified nightly remains #184 (October 5); October 6/7 nightly guards failed. The next October 8 scheduled 08:00 UTC attempt and 09:00 retry were still in the future at the check, so successful nightly recovery is **unverified**. Existing read-only monitor `verify-recovered-nightly-backup` remains the monitoring path; no automation was created or changed here. #185 remains failed/incomplete; #187 success does not retrospectively make it successful.

## L. PR #271 integration requirements

Keep #271 draft/unmerged. This PR starts from current main, not #271, and changes no #271 code. Merge/release order requires a fresh review after any main changes; no merge is authorized here. Retain #271's exact dispatch-allocation selection, receiver count correction permissions, compensating custody reversals and point-in-time legacy receipt-arrival proof. A shared untreated pool cannot be advertised as exact per-receipt allocation for partial receiving.

After any separately approved repairs, rebase/rehearse the combined frozen candidate on a fresh restore and run focused receipt correction, exact partial receiving, reversal/idempotency, treatment-before/after-arrival and readiness regressions. The independent `Assess` helper must continue to consume the same canonical resolver; do not add room IDs or record exemptions to production logic. This task does not claim that a future merged candidate has already passed those integration tests.

## M. Remaining risks and unavailable evidence

Onsite bin counts, original duplicate-ticket paperwork and exact surviving receipt allocation remain unavailable. The current data proves database custody and recorded untreated movement snapshots; it cannot prove absence of an unrecorded physical treatment. Historical deployment SHA/logs for the September alias events were not established. Legacy effective-time/recorded-time discrepancies remain intact. Three corrected-receipt pools lack automated exact-arrival proof; 35 positive positions, 13 depleted projection candidates and five negative positions require the stated broader review. No generic repair or legacy-treatment bypass is justified.

The current diagnostic uses a separate consistent canonical read after its initial scan; changed quantities are flagged. Its annotations are explanatory, not authorization for a later write. Any repair must acquire fresh locks/version checks and revalidate all evidence, even when an earlier diagnostic says Proven.

## N. Release decision

**NO-GO for PR #271.** All six are explained and have bounded resolution proposals, but none was repaired, the full stored-projection gate still has six blockers, and genuine unknown lineage/custody questions remain. Resume only after separately approved repairs/reconciliation, zero full live readiness blockers, preserved point-in-time treatment and audit history, a current verified recovery point, and successful combined #271 integration rehearsal. A green diagnostic test or successful backup alone is insufficient.
