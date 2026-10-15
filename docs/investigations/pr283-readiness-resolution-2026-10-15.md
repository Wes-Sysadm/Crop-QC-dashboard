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

The exact candidate and compatible rollback passed the fresh restored rehearsal on 2026-10-15. The previous failed clone remains untouched. Local evidence is retained outside Git in `local-test-runtime/pr283-readiness-fix/`; the previous clone/output remains under `local-test-runtime/pr283-preparation/`.

| Frozen component | Identity |
| --- | --- |
| Candidate code commit | `38733d2633af9f1155dab83a1c468a6914ce20f8` |
| Candidate Linux image | `cropqc-pr283:38733d2`, `sha256:753fade9b5d0dca77d0fe16d8f9e72e4e2b14a5439c64f5fe1027360fbba519a` |
| Compatible rollback commit | `d4a18684957b84178ac14f1227b93d786e3aa4c0` |
| Rollback Linux image | `cropqc-pr283:rollback-d4a1868`, `sha256:74a1a282e166194c900c99e05197748e41ad45a4ca0d53639adb6070dcc52abc` |
| Image archive | `pr283-38733d2-rollback-d4a1868-runtime21.tar`, 166,622,208 bytes |
| Archive SHA-256 | `d1c65ede85b8ab569bd5853e5a07008d520bae0c52c0436ad1695c2d3fff6819` |
| Platform / executed runtime | Linux amd64; .NET and ASP.NET 9.0.21 |
| SDK base | `mcr.microsoft.com/dotnet/sdk@sha256:2f5eb32870b4c0074a3d5b5191a03553bcab7b74463a76a7c0a5f3ef65c90fe5` |
| Runtime base | `mcr.microsoft.com/dotnet/aspnet@sha256:7da398155e0d6439cf0058bc1afa937817a03e320714b876a5f58a7d64f79d50` |

Independent archive inspection verified all 24 content-addressed blobs and both OCI index/platform/config links. Execution outputs record the actual loaded Web/Data/Shared DLL hashes; candidate forward and handback hashes match. The source archives, frozen Docker recipes, build/runtime outputs, drivers and `frozen-artifacts.json` are retained. Documentation-only closeout commits do not alter the frozen `src`, Dockerfile or startup script. This is local retained artifact evidence, not a remote registry publication or a claim that Render's next floating-tag rebuild will produce identical binaries.

Rollback is a local branch/package based on `3002d0774f52ee5e94aaef81dbd0d62c6bed335b`. Its only differences are the three readiness/identity runtime files and their reviewed-source manifest entries. It retains main's older restrictive receipt reader and compensation-aware writer, plus the tested additive schema. It safely executes authorized outside return and exact partial transit return after committed identity corrections and custody acknowledgement/reversal. It preserves database activity; no old backup was restored during rollback and no Down migration was used. The older receipt-reader limitation is a known rollback limitation, not a persistence incompatibility. No separate feature PR was created.

### Restore and schema

Retained backup #201 package: 19,669,968 bytes, SHA-256 `4c37061d61e3ed394cbd904b7e882ce210e493c381e3ec2c6ae204790e7b192d`. The local package was independently reverified for ZIP integrity, all four manifest component checksums and exact decompressed SQL content (337,491,624 bytes), then freshly restored into `pr283_fix_rehearsal_test` on isolated PostgreSQL 18.4. This does not claim a new backup or new remote read-back.

That older backup predates the three custody migrations. The frozen application's migration scripts aligned only the disposable restore with `ReceiptHeldTransferCustody`, `ReceiptCustodyCompensations` and `ReceiptCustodyGuardrails`, retaining the already-present `IsolatedInventoryMovementCohorts` migration. Hash comparison preserved original columns/rows in all 109 original tables. No new migration is introduced by this PR. Baseline strict readiness passed all 444 current identities before the workflow clone was created.

### Forward, rollback and independent proof

The new positive-control clone `pr283_fix_workflow_test` starts the historical target projections at nineteen, not twenty-nine. All application operations use the frozen canonical executor; no trigger, writer fence or application safeguard was disabled. The complete original sequence passed:

- Seven exact bins plus nineteen shared bins enter outside custody; only the seven are identity-corrected. Original movement/ledger evidence stays unchanged. The rollback returns each allocation to its current identity.
- A separate 26-bin loss is committed, the selected seven-bin receipt is corrected while consumed, and the loss is restored as seven plus nineteen. No consumed bins are fabricated before the recorded reversal.
- Nineteen bins are treated before seven later arrivals. The later seven remain untreated, and exact partial returns of 2, 3 and 2 bins preserve the nineteen treated bins in transit.
- A separate one-bin acknowledgement/reversal activates the compensation fence. A writer lacking the capability is rejected. The compatible rollback continues safe reads and canonical writes with that fence intact.
- Identical retries replay; changed-intent retries conflict; stale partial-return requests are rejected without writes. The new reader preserves exact seven-bin ownership and does not allocate the shared nineteen.
- Candidate handback is read-only and observes the same state left by rollback. Final authoritative quantities are **117 receipt origins = 97 room bins + 20 transit bins**. Transit is nineteen treated bins plus the separate one-bin custody fixture; no receipt-held remainder exists.

Independent database-enforced read-only SQL, outside the application resolver, compared full-row hash multisets across 21 affected tables. Every original row is preserved: 2,683 receipts, 4,368 ledger rows, 988 segments, 57 application links, 1,103 movements, 26 applications, 37 application sources, 47 outside parents, 42 transit parents, eight losses, 171 actual runs, 179 run revisions, 481 bins-run entries, 185,120 audits, 124 receipt overrides, 71 identity corrections and 276 command records. Original receipt-variety and custody acknowledgement/placement/reversal tables were empty. New fixture and operation rows are accounted for separately. SQL independently confirms 117 origins, room ledger net 97, transit dispatch 27 less returns seven, and one acknowledgement exactly compensated by one reversal.

### Readiness before/after and negative controls

| State / image | Inventory | Treatment / projection | Schema / topology | Outcome |
| --- | --- | --- | --- | --- |
| Preserved old failed clone / previous image | 12 findings across six rows | Two ten-bin excesses | PASS | Prior mandatory NO-GO |
| Same unchanged failed clone / corrected candidate | PASS, zero findings | Same two ten-bin excesses, same identities/36-versus-26 values | PASS | Expected strict rejection; no repair |
| Fresh baseline / corrected candidate | PASS | PASS, 444 identities | PASS | PASS |
| Completed clean 117-bin sequence / candidate | PASS | PASS, 449 identities | PASS | PASS |
| Same completed clean sequence / rollback | PASS | PASS, 449 identities | PASS | PASS |

Candidate and rollback final readiness JSON are identical, SHA-256 `edcbb1844be80eca83eab98c207bd53d8aea051c0a342f608a8636de4646aaa3`. Final topology contains nineteen and one transit bins with zero contradictions. Readiness ran with PostgreSQL `default_transaction_read_only=on`.

The four new PostgreSQL cases cover loss/outside restoration with clean and excess fixtures. Each clean case also checks eight deliberate corruptions within rolled-back disposable transactions: wrong historical parent identity; wrong credit amount; invalid reversal link; missing departure parent; unknown source treatment; missing selected receipt membership; wrong original ledger identity; and invalid committed intent hash. All remain blocking. A later identity correction cannot retroactively change the identity valid at an earlier return. Historical excess remains non-authoritative and still fails strict treatment readiness.

### Change-scoped verification and limitations

- Four new regression cases passed; 79 affected existing cases passed, including the existing receipt-specific and normal identity-restoration workflows. Prior 195-case/14-case evidence remains applicable outside the changed validator dependency; no full unrelated suite was repeated.
- Mandatory contracts: **153 passed, zero failed/skipped**, including the new four cases, architecture scans and existing PostgreSQL race contracts. TRX `artifacts/governance/32c7fb2e0ea94b54a0873166664db675/contracts.trx`.
- Governance: **29 tests passed**; metadata check passed all 20 rules. Reviewed candidate hashes remain enforced.
- Solution restore/build, Linux candidate/rollback builds, EF model consistency, changed-file formatting and diff whitespace checks passed. Build has five existing ImageSharp advisory warnings; no dependency upgrade is included here. A local solution build attempted while tests were active hit a DLL lock; the sequential rerun passed. A local frozen-recipe digest substitution initially retained a tag suffix; the corrected recipe built successfully. Neither event changed application behavior or waived a gate.
- No model, physical writer, permission, WinForms/MSI or hardware change. CI results must be bound to the final PR head; the PR/check links are the source for current status. New code requires the owner's final review; the prior favorable review applied to `565c45b`.

**Recommendation: GO for final human review of this narrow correction once final-head CI is green; no release authorization is claimed.** The exact previous rehearsal blockers are resolved for valid recorded states, while corrupted projections and unsupported restoration evidence remain blocked. No known technical blocker remains in this scoped rehearsal.

A future authorized release must freeze the actual approved merged candidate, verify actual runtime/artifact identity and durable rollback availability, take a fresh independently verified production backup, pass current strict production readiness/schema checks, preserve authoritative fingerprints, and complete authenticated affected-route, health and independent inventory/custody/treatment/audit verification. Retained backup #201 and local images do not substitute for those release-time gates. Keep additive schema; never restore an old database over newer legitimate activity to undo application code.

After #283 reaches approved main, #282 must reconcile its three identity-test conflict regions, preserve both traceability/contract sets and validate its combined candidate separately. Nothing here changes #282, any named production receipt, historical projection cleanup or the 210-bin RED proposal.

Nothing was merged or deployed; no production data was accessed or modified. **PR #283 is not complete until its later authorized merge, deployment and independent production verification.**
