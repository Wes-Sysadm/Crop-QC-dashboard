# Recorded-history reconstruction of the three remaining treatment pools

Date: 2026-10-08 UTC. **Proposal for approval; no production repair, deployment, merge, or backup operation occurred.**

All three blockers are resolvable by audited projection reconstruction under the authoritative recorded-inventory rule. The complete recorded histories establish **202 bins in DH-15/2350, 10 in WP-5/1084, and 170 in WP-8/2350**, uniformly untreated. Exact surviving receipt allocation remains shared/unresolved. No authoritative quantity changes are required.

This supersedes the earlier requirement for external receiving paperwork or onsite counts in [the prior report](historical-projection-reconstruction-2026-10-08.md), section E. The earlier code conflated missing per-receipt allocation with missing treatment proof. No missing or contradictory quantity/treatment event remains for these three pools.

## Evidence boundary and business rule

The evidence is verified backup **#186**, snapshot **2026-10-07T23:53:37.406346+00:00**, deployed main `e58d73a12c8bf0cc5f0f2333e442497e2b91b807`. Package: **17,317,796 bytes**, SHA-256 `0170afc86576e3c57483ebdf1cc3b431555b545bf1cd9e454e7e020bdf22ccc0`. Archive CRC, four component checksums, dump completion, and the 11,768-photo manifest passed. A fresh isolated restore completed at 2026-10-08T05:22:25.376056+00:00. No new production query/download/backup was needed; the earlier read-only production investigation independently reported the same target quantities. These are exact quantities at the evidenced snapshot, not a claim that production has remained unchanged since then.

Every legitimate origin and subsequent movement, correction, void, consumption and loss must appear in the system. We therefore replay signed committed events; we do not hypothesize unrecorded arrivals or re-add bins removed by a committed correction. Correction reasons supply no allocation or treatment proof.

The proof includes 44 ledger events in the three target pools plus two original-source events in DH-4C (room 36), 29 original receipts, three audited receipt revisions, three paired room transfers, eight packing entries and their run/revision records, and nine lineage movements. There are no recorded loss, room-depletion, transfer-reversal, or packing-reversal events in these exact histories. Unsupported event kinds remain explicit blockers for future cases.

## Exact reconstruction proposal

| Pool | Identity (warehouse/room/GL/profile) | Authoritative before → after | Projection before → after | Retire exact rows (bins/version) | False excess |
|---|---|---:|---:|---|---:|
| DH-15 / 2350 DANJ | 2 / 47 / 495 / 18 | 202 → 202 | 598 → 202 | 407:396/v4; 557:202/v3 | 396 |
| WP-5 / 1084 GALA | 4 / 2 / 398 / 2 | 10 → 10 | 130 → 10 | 65:120/v5; 597:10/v3 | 120 |
| WP-8 / 2350 DANJ | 4 / 5 / 495 / 18 | 170 → 170 | 362 → 170 | 552:192/v2; 553:170/v6 | 192 |

For each pool, retain both old rows as Historical, preserve their original identity, receipt and treatment metadata, record retired quantity/time/operation key and increment version exactly once. Create one canonical Current shared row, `ReceiptId=null`, signature `u`, state `Untreated`, no application links, quantity as above, initial version 1. The 708-bin difference is false projection excess, not a physical loss. [The proposal JSON](recorded-history-reconstruction-proposal.json) includes exact original snapshots, versions, plans, evidence references and sequential local fingerprints for all six candidates. It is not executable production authorization.

## Complete transaction histories and both clocks

The tables list recorded commit order; the final column is the balance after that event in effective-time order. Full effective sequences are also listed. Both replays begin at zero, conserve the same quantity and never go negative. We retain every timestamp; backdated business dates do not rewrite commit order. Since no applicable treatment event occurs on these branches, the timestamp inversions do not change treatment eligibility.

### DH-15 / lot 2350

| Ledger | Recorded UTC | Effective UTC | Event / exact parent | Delta | Recorded balance | Effective balance |
|---:|---|---|---|---:|---:|---:|
| 2316 | 2026-09-01T22:14:57.259707Z | 2026-09-01T21:57:00Z | ReceiptAdd: receipt 1338 (TR508945) | +64 | 64 | 193 |
| 2364 | 2026-09-02T17:37:27.272133Z | 2026-09-01T17:19:00Z | ReceiptAdd: receipt 1358 (TR508958) | +63 | 127 | 129 |
| 2383 | 2026-09-02T18:16:42.329044Z | 2026-08-31T18:15:00Z | ReceiptAdd: receipt 1373 (TR508944) | +66 | 193 | 66 |
| 2399 | 2026-09-02T20:20:39.347628Z | 2026-09-02T20:20:00Z | ReceiptAdd: receipt 1389 (TR508975) | +60 | 253 | 253 |
| 2413 | 2026-09-02T22:30:05.596319Z | 2026-09-02T22:09:00Z | ReceiptAdd: receipt 1403 (TR508980) | +60 | 313 | 313 |
| 2460 | 2026-09-03T18:46:18.465121Z | 2026-09-03T18:45:00Z | TransferIn: room:335 | +66 | 379 | 379 |
| 2790 | 2026-09-06T01:34:56.754001Z | 2026-09-04T01:34:00Z | ReceiptAdd: receipt 1539 (TR509011) | +23 | 402 | 402 |
| 2806 | 2026-09-06T19:26:56.020700Z | 2026-09-06T19:24:00Z | ReceiptAdd: receipt 1555 (TR509056) | +66 | 468 | 468 |
| 2821 | 2026-09-06T23:22:18.493729Z | 2026-09-06T23:20:00Z | ReceiptAdd: receipt 1568 (TR509071) | +56 | 524 | 524 |
| 2822 | 2026-09-06T23:50:27.206959Z | 2026-09-06T23:49:00Z | ReceiptAdd: receipt 1569 (TR509072) | +64 | 588 | 588 |
| 2972 | 2026-09-09T23:31:49.216256Z | 2026-09-09T23:25:00Z | TransferOut: room:369 | -192 | 396 | 396 |
| 3000 | 2026-09-10T20:13:10.541341Z | 2026-09-10T20:13:10.541341Z | ReceiptAdminOverride: receipt 1373 (TR508944); revision 0f977af1-1376-4fa7-91a4-a56bbf512fb0 | -66 | 330 | 330 |
| 3001 | 2026-09-10T20:15:06.042995Z | 2026-09-10T20:14:00Z | TransferOut: room:370 | -128 | 202 | 202 |

Effective ledger order: 2383, 2364, 2316, 2399, 2413, 2460, 2790, 2806, 2821, 2822, 2972, 3000, 3001.

Original receipt additions: **522**; net committed ledger total: **202**.

### WP-5 / lot 1084

| Ledger | Recorded UTC | Effective UTC | Event / exact parent | Delta | Recorded balance | Effective balance |
|---:|---|---|---|---:|---:|---:|
| 130 | 2026-08-01T22:03:10.768961Z | 2026-08-01T21:56:00Z | ReceiptAdd: receipt 132 (TR508186) | +8 | 8 | 8 |
| 135 | 2026-08-04T00:08:11.018190Z | 2026-08-03T00:07:00Z | BinsRun: entry:39; run 7/revision 7 | -8 | 0 | 0 |
| 196 | 2026-08-06T23:56:04.741576Z | 2026-08-06T23:55:00Z | ReceiptAdd: receipt 169 (TR508223) | +50 | 50 | 50 |
| 221 | 2026-08-07T23:59:01.566453Z | 2026-08-07T23:57:00Z | BinsRun: entry:53; run 14/revision 14 | -34 | 16 | 16 |
| 223 | 2026-08-08T00:51:30.323093Z | 2026-08-08T00:50:00Z | ReceiptAdd: receipt 189 (TR508232) | +66 | 82 | 82 |
| 235 | 2026-08-08T20:02:37.662402Z | 2026-08-08T20:01:00Z | ReceiptAdd: receipt 201 (TR508244) | +59 | 141 | 141 |
| 240 | 2026-08-09T20:47:29.760328Z | 2026-08-09T20:40:00Z | ReceiptAdd: receipt 206 (TR508249) | +8 | 149 | 149 |
| 410 | 2026-08-13T22:04:25.826698Z | 2026-08-13T22:02:00Z | ReceiptAdd: receipt 318 (TR508306) | +39 | 188 | 188 |
| 501 | 2026-08-14T21:27:37.812816Z | 2026-08-14T21:26:00Z | ReceiptAdd: receipt 378 (TR508330) | +33 | 221 | 221 |
| 651 | 2026-08-15T21:40:54.534909Z | 2026-08-15T21:40:00Z | ReceiptAdd: receipt 444 (TR508367) | +53 | 274 | 274 |
| 1015 | 2026-08-18T23:04:21.102970Z | 2026-08-18T23:03:00Z | ReceiptAdd: receipt 633 (TR508489) | +60 | 334 | 334 |
| 1148 | 2026-08-19T23:46:53.116518Z | 2026-08-19T23:46:00Z | ReceiptAdd: receipt 709 (TR508534) | +76 | 410 | 195 |
| 1205 | 2026-08-20T17:24:21.295966Z | 2026-08-19T17:23:00Z | BinsRun: entry:125; run 38/revision 43 | -215 | 195 | 119 |
| 1303 | 2026-08-21T00:33:52.204620Z | 2026-08-21T00:32:00Z | ReceiptAdd: receipt 767 (TR508570) | +65 | 260 | 260 |
| 1870 | 2026-08-27T23:05:29.976871Z | 2026-08-27T23:04:00Z | ReceiptAdd: receipt 1069 (TR508772) | +48 | 308 | 308 |
| 2300 | 2026-09-01T21:02:40.756502Z | 2026-08-31T18:58:00Z | BinsRun: entry:221; run 67/revision 73 | -188 | 120 | 126 |
| 2317 | 2026-09-01T22:15:07.076058Z | 2026-08-28T22:13:00Z | ReceiptAdd: receipt 1339 (TR508908) | +6 | 126 | 314 |
| 2939 | 2026-09-09T17:38:04.998215Z | 2026-09-09T17:38:04.998215Z | ReceiptAdminOverride: receipt 1339 (TR508908); revision 61ff4cac-48ce-4331-8306-8d2b0bdd21a8 | -6 | 120 | 120 |
| 3132 | 2026-09-14T14:54:49.516694Z | 2026-09-11T14:50:00Z | BinsRun: entry:357; run 102/revision 125 | -110 | 10 | 10 |

Effective ledger order: 130, 135, 196, 221, 223, 235, 240, 410, 501, 651, 1015, 1205, 1148, 1303, 1870, 2317, 2300, 2939, 3132.

Original receipt additions: **571**; net committed ledger total: **10**.

### WP-8 / lot 2350

| Ledger | Recorded UTC | Effective UTC | Event / exact parent | Delta | Recorded balance | Effective balance |
|---:|---|---|---|---:|---:|---:|
| 2677 | 2026-09-05T23:45:41.262826Z | 2026-09-05T23:22:00Z | ReceiptAdd: receipt 1532 (TR509046) | +60 | 60 | 60 |
| 2825 | 2026-09-07T15:37:45.142663Z | 2026-09-06T15:36:00Z | ReceiptAdd: receipt 1572 (TR509079) | +64 | 124 | 124 |
| 2843 | 2026-09-07T19:50:57.916520Z | 2026-09-06T19:50:00Z | ReceiptAdd: receipt 1587 (TR509078) | +60 | 184 | 184 |
| 2857 | 2026-09-07T21:36:14.672598Z | 2026-09-06T21:26:00Z | ReceiptAdd: receipt 1594 (TR509078) | +60 | 244 | 244 |
| 2901 | 2026-09-08T20:58:35.496301Z | 2026-09-07T20:57:00Z | ReceiptAdd: receipt 1626 (TR509101) | +6 | 250 | 250 |
| 2973 | 2026-09-09T23:31:49.216418Z | 2026-09-09T23:25:00Z | TransferIn: room:369 | +192 | 442 | 442 |
| 2975 | 2026-09-09T23:45:30.203292Z | 2026-09-09T23:43:00Z | BinsRun: entry:313; run 91/revision 110 | -237 | 205 | 205 |
| 3002 | 2026-09-10T20:15:06.043260Z | 2026-09-10T20:14:00Z | TransferIn: room:370 | +128 | 333 | 397 |
| 3138 | 2026-09-14T16:39:57.507951Z | 2026-09-10T16:39:00Z | ReceiptAdd: receipt 1754 (TR509156) | +64 | 397 | 269 |
| 3142 | 2026-09-14T17:13:10.346474Z | 2026-09-14T17:13:10.346474Z | ReceiptAdminOverride: receipt 1754 (TR509156); revision 95eeef85-d8d8-4da4-a2b2-dfbd68e27656 | -64 | 333 | 310 |
| 3374 | 2026-09-21T04:30:03.624568Z | 2026-09-14T04:29:00Z | BinsRun: entry:376; run 110/revision 133 | -23 | 310 | 374 |
| 3380 | 2026-09-21T04:42:41.155551Z | 2026-09-20T04:41:00Z | BinsRun: entry:382; run 115/revision 138 | -140 | 170 | 170 |

Effective ledger order: 2677, 2825, 2843, 2857, 2901, 2973, 2975, 3138, 3002, 3374, 3142, 3380.

Original receipt additions: **314**; net committed ledger total: **170**.

### Original transfer source and quantity reconciliation

DH-4C (warehouse 2, room 36) received **TR508926 / receipt 1288 / 66 bins**, ledger 2196, recorded **2026-09-01T01:07:02.207571Z**, effective **2026-09-01T01:05:00Z**. Transfer **335** debited all 66 through ledger **2459** and credited DH-15 through **2460**, effective **2026-09-03T18:45:00Z**; movement **342**, segment **406→407**, retains `u/Untreated`. Source balance is zero. This is recorded ancestry, independent of receipt 1373’s correction reason.

- DH-15: **522 original receipt bins + 66 transferred in − 192 transferred out − 66 void correction − 128 transferred out = 202**. Transfer 369 pairs ledger 2972/2973 and movement 506 (407→552); transfer 370 pairs ledger 3001/3002 and movement 516 (557→553). Both are committed room transfers; no quantity remains in transit.
- WP-5: **571 original receipt bins − 555 packed − 6 void correction = 10**. Packing quantities are 8, 34, 215, 188 and 110. The first occupancy closes at zero (ledger 130/135); the proof includes it instead of silently dropping it. Entry 39/run 7/revision 7 and entry 53/run 14/revision 14 substantiate the early −8/−34 debits. Their treatment snapshots are null and no lineage movements exist, but the recorded arrivals and absence of any applicable treatment prove a uniform pool; the debit itself is independently recorded and conserved. Later entries 125/221/357 have movements 31/299/573.
- WP-8: **314 original receipt bins + 320 transferred in − 64 quantity correction − 400 packed = 170**. Packing is 237+23+140, entries 313/376/382, revisions 110/133/138, movements 507/622/628. Receipts 1587 and 1594 both bear TR509078 and each records +60; ticket text alone does not authorize discarding either committed arrival.

## Receipt revisions and allocation ambiguity

Receipt 1373 retains historical stored count 66 but is voided; override `0f977af1-1376-4fa7-91a4-a56bbf512fb0`, ledger 3000 −66 and audit 123203 establish effective quantity zero. Receipt 1339 similarly retains stored count 6 but is voided; override `61ff4cac-48ce-4331-8306-8d2b0bdd21a8`, ledger 2939 −6, audit 120803. Receipt 1754 remains non-deleted with corrected count zero; override `95eeef85-d8d8-4da4-a2b2-dfbd68e27656`, ledger 3142 −64, audit 126585. All three before/after identity, quantity, deletion/version transitions, unique operation keys, complete adjustment counts, audit snapshots and chronology validate.

The source events and total treatment class are proven. **No recorded allocation establishes which individual original receipt contributed each surviving bin after shared packing/transfers/corrections.** That is the specific missing allocation detail; it does not prevent shared quantity/treatment reconstruction. No FIFO, proportional split, fabricated duplicate-ticket link, or receipt reassignment is proposed. Exact-receipt operations remain blocked until an actual recorded allocation proves them.

## Treatment proof and code boundary

There are zero room treatment applications in rooms 2, 5, 36 and 47, zero receipt applications on the involved receipts, and zero application links on their segments. Incoming source history is recursively traced to original receipts: WP-8 → DH-15 → DH-4C. All nine recorded lineage movements have exact identities and `u/Untreated` snapshots. Both clocks and all source/destination quantities are validated. Untreated eligibility therefore follows complete recorded origins and treatment history, not current room location, administrative assertions, or a missing quantity gap.

The new read-only `RecordedInventoryHistoryReconstruction` proof is invoked only by the explicit maintenance preview/approval/execution path when the ordinary proof cannot establish a plan. It checks detached full histories, both signed replays, original receipts, committed negative revisions, audited snapshots, exact transfer pairs, packing parents/revisions and all lineage movements. It also searches operational parents independently of ledger links: unlinked receipts, packing entries or room transfers fail; loss, room-depletion, inter-crew, outside-warehouse and processor events require their own supported proof and cannot be silently omitted. Source ancestry is bounded to 32 rooms; cycles, positive receipt revisions, unsupported event types, mixed/unknown treatment, missing parents and timestamp contradictions remain blocked with specific event identifiers. Earlier room treatments do not attach to later arrivals.

The canonical resolver, treatment guards, ordinary receiving permissions, receipt-increase workflow and exact allocation requirements are unchanged. The existing serializable locks, stale evidence checks, separate approval, verified backup gate, idempotency, rollback and independent verification remain mandatory. No migration or production repair SQL was added.

## Isolated rehearsal and conservation

A fresh backup #186 restore passed all six reconstructions, immediate independent verification and identical-key replay. The first three already-proven pools remain 61, 252 and 1,122 bins. The newly resolved pools remain 202, 10 and 170. Total authoritative quantity across all six stays **1,817**; total false projection excess removed is **1,539** (831 previously proven + 708 newly resolved). Sixteen original projection rows were retained as Historical and six shared replacements were added.

Local replacement IDs are **806–811**; the new three are 809/810/811. Six approval audits and six repair audits were appended, with repair audit IDs **174804, 174806, 174808, 174810, 174812, 174814**, plus six idempotency records. These generated IDs are rehearsal results, not prescribed production IDs.

Full-row before/after hashes match for all 26 protected/unrelated table projections: ledger, receipts and variety lines, overrides, transfers and outside/processor custody, runs/revisions/entries, losses/depletions, identity corrections, treatments/application sources/links, immutable movements, existing audits/commands, master identities and unrelated segments. Only the exact 16 retirement rows, six new projections and explicit maintenance audit/command records are excluded from that equality comparison. The retirement verifier separately validates every preserved field of the 16 rows. The separate treated lot 1372 branch in McDougall room 65, including segment 503 and its 106 bins, remains unchanged.

Full readiness before: **6 treatment blockers**. After: **PASS**, schema **979 objects**, inventory invariants ready, **438 current identities / 0 treatment blockers**, custody topology ready. Readiness ran through `--verify-release-readiness` with a PostgreSQL read-only connection. No readiness check was weakened.

[Machine-readable conservation and readiness evidence](recorded-history-reconstruction-evidence.json) retains exact plans, before/after positions and hashes. [Read-only verification SQL](recorded-history-reconstruction-verification.sql) executed successfully on the isolated restore. The SQL returned all 46 ledger events, all historical/current segments, parent records and **zero applicable treatment applications**.

Reconstruction also rehearsed successfully on a separate isolated #186 clone with **PR #271 + #272 integration code and PR #271 additive schema**. All six repairs committed, verified and replayed, protected hashes matched, and combined full readiness passed with zero treatment blockers. After adding the final independent operational-parent completeness checks, the final code reran the proof read-only against both original restores: all three pools remain proven and no unsupported/orphan parent was found. The mutation path and resulting projection plan did not change. The prior implementation task’s full application suite remains historical; this follow-up does not claim that suite was rerun.

## Tests, limitations and approval boundary

Final focused tests: **135 passed, 0 failed, 0 skipped** with local PostgreSQL enabled. This includes **36 new historical/negative regression cases** covering all three full timelines, upstream ancestry, both clocks, valid revisions, legacy packing, missing/contradictory events, orphan operational parents, treatment conflicts, pre-arrival applications, and unchanged unresolved receipt provenance. Existing reconstruction approval/concurrency/rollback/replay, availability, proof and architecture tests also pass.

Package restore and solution build passed (12 existing warnings, zero errors). Changed-file formatting and `git diff --check` passed. Default-provider model consistency passed. Explicit PostgreSQL EF model checking still reports the pre-existing provider-specific pending-model difference, reproduced on the unchanged baseline worktree; no entity/model/migration files changed. This warning is not represented as a clean PostgreSQL migration check. No WinForms code changed; no MSI was required.

A passing release-readiness result is not a claim that every historical diagnostic is repaired: the earlier broader unknown/mixed, depleted and negative populations were not auto-corrected. The three newly resolved positions are a subset of the previously reported 35 unresolved positives; broader counts are historical and were not rescanned with this new maintenance proof.

There is no remaining missing quantity/treatment event for these three pools at the evidenced snapshot. Exact surviving receipt allocation remains explicitly unresolved. Fresh production evidence and separate approval are required before any repair; the report requests review of a concrete projection-only proposal, not a physical count or external paperwork. If state has changed, fingerprints/versions require a new preview rather than forcing this plan.

The new draft PR is based directly on current `origin/main` (`e58d73a12c8bf0cc5f0f2333e442497e2b91b807`) on `codex/recorded-history-reconstruction`. It includes the prior draft framework and this proof extension without stacking on PR #273. PR #273 is not modified or closed. Nothing was merged, deployed or written to production.
