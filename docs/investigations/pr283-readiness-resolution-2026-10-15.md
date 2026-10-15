# PR #283: split-restoration readiness resolution

## Scope and authority

Preparation only, under the owner's 2026-10-15 “Resolve Inventory and Projection Readiness Failures” instruction. No merge, deployment, production access, production migration, correction or reconstruction is authorized by this work. PR #282 and the separate RED reconstruction are excluded.

Reviewed starting head: `565c45bf917cfd51268b0de35bbd85e76447ef2f`; fetched main: `3002d0774f52ee5e94aaef81dbd0d62c6bed335b`. Specification 1.1.0 and approved governance commit `1c263278f13eb31884a2872c2151257dee099789` apply. INV-001–006, REC-001/002, TRT-001/002, MOV-001, ROOM-001, AUD-001, OPS-001 and GOV-001 require authorized origins, conservation, exclusive custody, exact selected receipt evidence, point-in-time treatment, immutable history and strict release safeguards. This is a validator correction, not a business-rule change.

## Exact failed evidence and root cause

The preserved prior disposable clone is `pr283_workflow_test5`, restored from backup #201. Its full failed readiness JSON SHA-256 is `d812999c661c9a3d97934f5e73576256154fc076c6c058692c2705393e0d21c1`. All identifiers below belong to that synthetic rehearsal, not asserted production defects.

| Adjustment | Parent / committed effect | Lot | Delta | Effective UTC | Committed UTC | Original readiness findings |
| --- | --- | --- | ---: | --- | --- | --- |
| 4421 | OutsideWarehouseTransfer 48; `p283-outside:0:out` | 990283 | -26 | 2026-10-15 06:42:32.017424 | 06:42:32.033607 | OutsideWarehouseTransferReversalMismatch; OutsideWarehouseTransferIdentityMismatch |
| 4434 | OutsideWarehouseTransfer 48; `p283-rollback-return:0:in` | 990284 | +7 | 2026-10-15 06:44:12.313498 | 06:44:12.328659 | Same two outside-transfer codes |
| 4435 | OutsideWarehouseTransfer 48; `p283-rollback-return:1:in` | 990283 | +19 | 2026-10-15 06:44:12.313498 | 06:44:12.328659 | Same two outside-transfer codes |
| 4423 | RoomInventoryLoss 9; `p283-loss:0:out` | 990285 | -26 | 2026-10-15 06:42:42.042597 | 06:42:42.047283 | RoomInventoryLossReversalMismatch; RoomInventoryLossIdentityMismatch |
| 4424 | RoomInventoryLoss 9; `p283-reverse-loss:restore:4423:0` | 990286 | +7 | 2026-10-15 06:42:44.083269 | 06:42:44.087189 | Same two loss codes |
| 4425 | RoomInventoryLoss 9; `p283-reverse-loss:restore:4423:1` | 990285 | +19 | 2026-10-15 06:42:44.083269 | 06:42:44.087189 | Same two loss codes |

All six rows use canonical invariant version 3. Each historical parent describes its original 26-bin departure; its ReceiptId is null because the allocation includes seven proven receipt-owned bins and nineteen with unknown exact ancestry. Source warehouse is 4. Outside parent room is 990283; loss parent room is 990285. Both parents are fully reversed by the recorded credits.

Outside movements 1123/1124 departed with seven bins of receipt 2689 and nineteen unassigned bins, source segments 1015/1016. Correction `790ca088-ac15-4df6-9602-13447e7154cf`, committed at 06:42:34.180367 UTC, changed only receipt 2689 from lot 990283 to 990284. Returns 1138/1139 reverse 1123/1124 into segments 1027/1028, respectively: seven at corrected identity and nineteen at original identity.

Loss movements 1126/1127 departed with seven bins of receipt 2690 and nineteen unassigned bins, source segments 1017/1018. Correction `2161c296-e94e-446f-8fe7-9e458a311fe1`, committed at 06:42:43.987906 UTC, changed only receipt 2690 from lot 990285 to 990286. Returns 1128/1129 reverse 1126/1127 into segments 1019/1020. Original departure and treatment snapshots remain intact. These allocations are recorded untreated (`u`), not inferred from their destination room.

Confirmed validator defect: `ValidateOutsideWarehouseTransfer` and `ValidateRoomInventoryLoss` require one reversal credit equal to the parent's 26 bins and compare every ledger side with the original parent identity. The canonical writer legitimately records two credits, 7 + 19, after an audited receipt-specific identity correction. Parent validation repeats for each of three ledger rows, producing twelve messages from two incompatible assumptions on two parents. There is no evidence here of a contradictory canonical writer or missing compensation. Rewriting the parents would destroy the original identity evidence.

## Projection findings remain strict failures

| Room / lot | Pre-existing segment | Before operation | Newly restored segment | Final raw / authority |
| --- | --- | --- | --- | --- |
| 990283 / 990284 | 990284, no ReceiptId, empty CohortKey | 29 projected / 19 authoritative | 1027, receipt 2689, seven bins | 36 / 26 |
| 990285 / 990286 | 990286, no ReceiptId, empty CohortKey | 29 projected / 19 authoritative | 1019, receipt 2690, seven bins | 36 / 26 |

Both ten-bin excesses were explicitly seeded before the first operation. Each restoration added exactly its seven-bin incoming cohort; it did not create either historical excess. Neither excess contributes available inventory. The preserved negative clone is not cleaned up or excluded: its two `TREATMENT_LINEAGE_EXCEEDS_AUTHORITATIVE_INVENTORY` findings must remain. A separate fresh clean fixture starts each historical segment at its recorded nineteen bins; this is a distinct positive control, not repair of the negative clone.

## Narrow implementation boundary

`CanonicalRestorationReadiness` verifies canonical reversed loss/outside parents using the original debit, aggregate positive credits, unique exact reversal movements, receipt membership, original treatment evidence, identity corrections committed by reversal time, and matching committed command intent/results. Missing or contradictory evidence remains blocking. Legacy parent validation is unchanged. `CanonicalIdentityMap.Resolve` adds an optional committed-time cutoff used by this reader; existing writers retain the existing default behavior. The Web readiness service dispatches only canonical reversed parents to that proof.

There is no physical writer, migration, projection repair, quantity authority, readiness exemption or writer-fence change. Reviewed-source hashes and OPS-001 traceability are updated to reflect the inspected code. The old compensation-aware rollback has the same validator defect, so its narrowly scoped compatibility package must backport this readiness correction while retaining the old receipt reader.

## Validation and frozen rehearsal

Evidence is being completed before the final readiness recommendation. The immutable failed clone and its original outputs are retained. Final package identities, positive and negative readiness results, independent fingerprints and CI will be recorded below before this preparation is considered proven.
