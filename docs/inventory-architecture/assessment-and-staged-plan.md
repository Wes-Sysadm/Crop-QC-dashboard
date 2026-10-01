# Canonical inventory architecture: assessment and staged implementation plan

Status: **architecture review and read-only production investigation complete enough to select the staged path; implementation is not complete and this is not release clearance.** No runtime code, schema, production inventory or historical repairs changed. This follows the request to stop and propose explicit phases if the existing architecture cannot safely support the complete correction in one PR.

Reviewed `origin/main`: `71ff57dd7fe7009b8cc56199a46bd4ead43859c2`. New branch: `codex/canonical-inventory-architecture`. No newer main delta required rebasing. The scan was captured at **2026-10-01T03:12:10.44379+00:00**, against production using an explicit repeatable-read/read-only transaction plus a read-only connection default. The local auditor was rebuilt against current main. Production data was not downloaded as a full database or restored during this architecture-only stage.

## Decision and affected area

Do not ship a WP-7 selection patch or widen `explicit > authoritative` into a trimming rule. The remaining work crosses receipt production, authoritative ledger/custody reads, treatment projection lifecycle, all consuming commands, corrections/reversals, API receiving, and admin diagnostics. These are real shared dependencies; QC calculations, email layout, device protocols and photo storage are outside scope.

One large behavior-changing PR would combine a domain extraction, removal of existing negative-inventory policies, API/web convergence, custody semantics, lifecycle/schema decisions and historical-evidence reconstruction. These must have separate verifiable gates. The key obstacles are concrete:

* Ordinary API receipts bypass the web ledger-producing path and audit after saving. `CropQcDbContext.SaveChanges` synchronizes QC defect status, not inventory. Runtime exposure of this separate API must be inventoried; its code cannot be omitted from a universal invariant.
* Legacy room depletion and receipt negative correction/void do not call the shared treatment consumption lifecycle. A correct row-parent invariant therefore does not guarantee projection retirement.
* Actual Run overdraw approval, receipt negative acknowledgment, and legacy `ConfirmOverDepletion` contradict the requested nonnegative inventory invariant. Their replacement is a business-policy change, explicitly required by this request.
* Truck Receipt completion/return/reopen directly manipulates projections with its own exact raw-key matching. Room treatment apply/reversal uses weaker default transaction isolation than the consuming Serializable flows.
* The #255 proof is intentionally limited to shared untreated rows and selected arrival types. Receipt-specific/inter-crew cases need additional evidence, not fewer guards.

Historical evidence to preserve: original receipts, immutable ledger adjustments, treatment applications/links, movements/reversals, Actual Run revisions, receipt identity correction records, original actors/timestamps and audits. No production normalization or mass repair is proposed for execution in this PR.

## Authoritative model today

**Rooms:** `RoomInventoryAdjustments`, resolved by `RoomInventoryLedgerQueryService`, are the existing physical authority. The query applies opening-baseline precedence, sums signed adjustments by persisted identity, resolves supported legacy identity evidence, and returns current snapshots. `NewBinCount` is historical transaction context; summing that field is not current inventory. Receipt totals and `TreatmentLineageSegment.CurrentBins` are not alternative physical ledgers.

**Custody outside rooms:** dispatched inter-crew inventory is represented by the transfer parent and dispatch/return/receive evidence; Truck Receipt validates that evidence against ledger deductions. Outside Warehouse / processor shipments retain their own custody/history after room deduction. A canonical model must include these existing adapters; pretending every location is a room would duplicate inventory when receiving a load.

**Treatment today:** positive `CurrentBins` plus raw/normalized identity keys decides whether a projection participates. `TreatmentState` is only Untreated/Confirmed/Unknown; it is not a current/historical lifecycle. Zero-balance historical rows and application/movement links remain. Read projection (`ProjectSelectionsBatchAsync`) and write materialization (`MaterializeAsync`) use related but separate calculations. Write materialization can synthesize a missing untreated amount; missing coverage alone is not evidence of untreated status.

Primary anchors: [RoomInventoryLedgerQueryService:57](https://github.com/Wes-Sysadm/Crop-QC-dashboard/blob/71ff57dd7fe7009b8cc56199a46bd4ead43859c2/src/CropQc.Web/Services/RoomInventoryLedgerQueryService.cs#L57), [RoomTreatmentService:2394](https://github.com/Wes-Sysadm/Crop-QC-dashboard/blob/71ff57dd7fe7009b8cc56199a46bd4ead43859c2/src/CropQc.Web/Services/RoomTreatmentService.cs#L2394), [RoomTreatmentService:2458](https://github.com/Wes-Sysadm/Crop-QC-dashboard/blob/71ff57dd7fe7009b8cc56199a46bd4ead43859c2/src/CropQc.Web/Services/RoomTreatmentService.cs#L2458), [ProvenLineageReconciliation:16](https://github.com/Wes-Sysadm/Crop-QC-dashboard/blob/71ff57dd7fe7009b8cc56199a46bd4ead43859c2/src/CropQc.Web/Services/ProvenLineageReconciliation.cs#L16), [TruckReceiptReconciliationService:328](https://github.com/Wes-Sysadm/Crop-QC-dashboard/blob/71ff57dd7fe7009b8cc56199a46bd4ead43859c2/src/CropQc.Web/Services/TruckReceiptReconciliationService.cs#L328).

## Architecture matrix

Legend: **L** = authoritative room ledger snapshots; **T** = shared RoomTreatmentService selection/materialization; **N** = evidence proof from ProvenLineageReconciliation. Matrix entries describe current code, not guarantees of the proposed design. A Serializable helper alone does not prove all reads occur inside its transaction. Optional-null service fallbacks used in legacy/testing code must also be removed or explicitly quarantined from operational DI.

The matrix has **32 workflow/path families**. Static direct `RoomInventoryAdjustments.Add/AddRange/Remove` or `TreatmentLineageSegments.Add/Remove` search identifies **13 service files**, including specialized repairs. That count is a reproducible direct-call surface count, not a claim of 13 independent physical ledgers; raw SQL/CLI repair paths are a separate quarantine surface. There is one principal ledger query, one shared selection projector, one materializer, separate reclassification proofs, and caller-specific source/custody/correction policies. The problem is partial centralization and multiple write authorities, not an independent full reconciliation algorithm in every screen.

See the accompanying CSV for all requested dimensions. The compact matrix below preserves quantity, treatment and transaction distinctions.

| Path | Quantity/location | Treatment/eligibility | Normalization | Transaction/concurrency |
|---|---|---|---|---|
| Room move / same-company warehouse move | L; source and destination room | T; explicit segment or signature; Room transfer projection + identity/seal/quantity checks | T during write | Serializable outer; bulk joins outer; Segment versions; expected quantities; operation keys |
| Room transfer reversal | L + original paired adjustments | Original lineage movement / reversal; Reversal-specific destination availability | Restores original source; reverses destination | Serializable; Segment versions and reversal keys |
| Cross-company / inter-crew dispatch | L -> In Transit custody | Outside transfer source options + T; Source option checks + dispatch provenance | T during dispatch | Serializable; Transfer and segment versions; operation key |
| Legacy/internal inter-crew receive | Dispatched parent/custody -> destination L | Dispatch movements and source application links; Parent status; received-count/variance policy | No general destination reconciliation | Serializable caller; T joins; Transfer + segment versions; receive key |
| Truck Receipt match / variety edit | No new physical inventory; transit parent | Transit evidence; Receipt and transfer versions; route; variety quantities | None | Serializable / caller savepoint; Receipt and transfer versions |
| Truck Receipt completion | Transit dispatch-minus-returns + L destination | ActiveAllocationsAsync / DestinationSegmentAsync; ValidateTransitAsync and exact received lines | No generic normalization | Serializable / caller savepoint; Receipt/transfer/segment versions |
| Truck Receipt transfer edit / return | Transit commitments; L for added source; return restores L | Existing source segment or outside-transfer option; ValidateTransitAsync; exact return allocation; new source check | Only shared dispatch branch can N | Serializable / caller savepoint; Transfer/receipt/segment versions |
| Truck Receipt admin reopen | Reverse exact destination L into transit | Original receive movements and destination segments; Version, reversal, dependent-movement checks | None | Serializable / caller savepoint; Receipt/transfer/segment versions |
| Processor Sale | L; external processor custody/history | T segment options; Independent source identity, treatment and running balances | T during write | Serializable; Shipment/segment versions; operation keys |
| Processor Sale reversal | Original shipment + L restoration | Original processor lineage movement; Shipment reversal rules | Restoration, not generic N | Serializable; Shipment/segment versions |
| Tree Top / Outside Warehouse | L deducted; OutsideWarehouseTransfer custody, not receiving inventory | T grouped by treatment; Own source option and exact balance checks | T during write | Serializable; Transfer/segment versions; operation keys |
| Outside Warehouse reversal | Original outside custody + L restoration | Original lineage movement; Original transfer not previously reversed | No generic N | Serializable; Transfer/segment versions |
| Actual Run / dump / packline consumption | L; only exact current revision may credit its reversed consumption | T via correction treatment plan; Correction plan, selected segment, source identity, shortage approval | T inside outer write | Serializable; Run + segment versions; revision operation key |
| Legacy Bins Run create/edit/reverse | L + original entry credit | T and original reversal movements; Legacy source matching / effective available | T during write | Serializable; Operation keys / segment versions; not one shared position token |
| Packout result / finalization / reopen | Already consumed ActualRun revision / expectation | Stored run provenance; Result reconciliation/locking; does not consume bins a second time | None | Own reporting transactions; physical consumption belongs to BinsRun; Packout version / operation coordinator |
| Repack | Uses normal run if represented as another ActualRun | Same as ActualRun; No separate repack eligibility implementation found | See ActualRun | See ActualRun; packed-product reintroduction is not proven by this repository; See ActualRun |
| Ordinary receiving (web) | ReceiptAdd ledger entry; receipt identity and room | Implicit untreated unless receiving treatment is applied; Receipt validation / identity guard | No historical normalization | Inventory path Serializable; Receipt version and database identity rules |
| Ordinary receiving edits (web) | L / receipt-specific balance for permitted inventory addition; saved quantity/established identity changes require admin override | Implicit receipt lineage; no treatment delta in ordinary update; Quantity/identity edits rejected; received-date baseline guard | None | Serializable inventory path; receipt balance read inside transaction; Receipt version changes; transaction for inventory addition or date change |
| Receiving API create/edit | Receipt row only in this service; no ledger adapter | None; DTO/same-day validation; key change marks QC review | None | No explicit operation transaction; SaveChanges then audit; No shared inventory version/revalidation |
| Room fill / manual positive true-up | L delta; rejects negative true-up | AddUnknownAsync for added bins; Exact snapshot match / superseded identity guard | Unknown addition | Serializable starts after snapshot read; Operation key; no unified position version |
| Room fill report / email | Reporting of room state; does not add inventory | Presentation only; No consuming eligibility | None | No inventory consumption transaction; Reporting/send coordination |
| Dropped bins | L exact identity | T selected segment; Expected balance, quantity, seal, lineage | T during loss | Serializable; can join correction transaction; Loss/segment versions and operation keys |
| Legacy room depletion / reversal | Receipt-attributed ledger; Depletion/DepletionVoid | No treatment consumption call in inspected methods; Exact receipt provenance + ConfirmOverDepletion | None | Serializable; Parent IDs/operation keys; no unified position version |
| Treatment assignment (room / receipt) | L/as-of physical inventory; no quantity creation intended | T materialization/application splits; Room snapshot proof / separate exact receipt attribution | Materialize can N | Room apply uses default isolation; receipt apply Serializable; Segment versions + application operation key |
| Treatment reversal | Current room snapshots and affected segments | Remove application from active signature; historical links retained; Own reversal/current-segment rules | No shared N plan | Default isolation transaction; Segment versions / application reverse state |
| Receipt admin quantity/void/location/identity | Ledger + receipt provenance and custody | T for positive true-up/identity/location; not negative quantity/void; Separate receipt provenance, scope and negative acknowledgment | Positive true-up / reclassification only | Serializable; Receipt, state fingerprint, segment versions, operation key |
| Identity correction / legacy identity reconciliation | L + active completed identity correction mapping | T ReclassifyIdentityAsync + its special proofs; Independent readiness/current target matching | Audited reclassification / stale target retirement | Correction-specific; must enlist in canonical coordinator; Correction key, segment versions, transaction checks |
| Baseline import / replacement | Opening InventoryImport baseline, supersedes earlier ledger rows | No projection epoch retirement; Preview validation / reject established reductions | None | No explicit transaction around preview/read/write; one final SaveChanges; No shared source version |
| Inventory summaries / room dashboard | L, projected for presentation | T on detail; room history separately; Read-only display; not command authorization | Read-only only | No mutation; No write token required |
| Traceback / receipt provenance | Ledger/receipt/movement evidence | Receipt and movement provenance; Exact vs blended / historical / custody classifications | None | No mutation; Read-only |
| Admin reconciliation / readiness / conservation | L + separate receipt/parent checks | Only some readiness checks call T; Separate diagnostics; no common structured availability result | Read-only | No mutation; Read-only |
| Background/API/one-time writers | Direct adjustments / parent modifications in specialized commands | Some directly update lineage or invoke T; Guarded one-time paths, not general eligibility | Command-specific audited repair | Per-command; not a universal coordinator; Per-command guards |

## Mechanisms that create or retain stale projections

1. **Status-key alias rematerialization.** Before #251, materialization loaded only exact raw `IdentityKey` matches. A null status and redundant Conventional/Organic suffix could hide already represented bins; `missing = authoritative - matchingExplicit` then created another untreated projection. Current normalized lookups prevent that particular ordinary materialization path, but do not retire all legacy aliases, and Truck Receipt still owns a separate exact-key destination creator. Do not equate “no raw-key duplicate index violation” with “no duplicate physical representation.”
2. **Ledger-only quantity reductions / lifecycle bypass.** Receipt negative quantity adjustments and voids reduce physical ledger quantity without a corresponding shared treatment reduction. Legacy room depletion similarly has no T call. This can leave positive projection rows after depletion. The existing `InventoryDeductionInvariantService` validates parent/amount relationships and permits acknowledged negatives; it is not a universal availability or projection-conservation gate.
3. **Receipt identity corrections after movement.** The five current negative cases all contain a later receipt correction against a source identity whose inventory was already transferred. The signed ledger then becomes negative in that old identity; some replacement identities were separately transferred. Current guards address portions of provenance, but historical deficits must not be silently clamped or reclassified by the new service.
4. **Multiple custody/projection implementations and incomplete operation boundaries.** Truck Receipt copies raw projection keys in its destination factory. Treatment apply/reversal has default-isolation transactions. Manual true-up resolves its snapshot before starting its transaction. API receipt mutation is separate from both web ledger and audit transaction. These are future-risk surfaces even when the present scan has no duplicate operation keys.

Other sequence mechanisms were traced to their current owners: partial/split movements now share tracked materialization in T; returns/reversals restore original segments and carry original movement links; receipt true-ups create receipt-specific projections; identity corrections have separate stale-target/gap proofs; imports replace opening baselines without a projection occupancy-epoch transition. There is no generic receipt replacement operation: ordinary edit, override, identity/location correction, void and Truck reopen must each be migrated. No dedicated repack inventory command was found. Do not call the broad class fixed until each of these surfaces is tested through the same contract.

## WP-7 Bartlett 1372: exact accounting

Confirmed identity: WP room **4**, crop **2026**, GrowerLot **448**, FruitProfile **17**, Bartlett/BART, conventional. Room 14 is room **66** and is not used as an inventory-total validation target.

| Evidence | Bins |
|---|---:|
| Initial inter-crew receipts #22/#21/#20/#19/#18/#17/#16; ledger #3225,3227–3231,3287 | 66+64+66+66+64+62+58 = **446** |
| ActualRun **111**, BinsRunEntry **377**, ledger **3375**, movement **623** | **−122** |
| Later transfer **34**, ledger **3624**, receives **703/704/705** | 72+184+542 = **798** |
| Current physical room position | **446−122+798 = 1,122** |

Current raw projections:

| Segment | Receipt provenance | Current bins | Accounting |
|---|---|---:|---|
| 617 | 774 | 134 | movements 601/603/604: 4+64+66 |
| 618 | shared | 604 | movements 602/705: 62+542 |
| 619 | 660 | 250 | movements 605/606/607/615: 66+64+62+58 |
| 630 | shared, redundant Conventional status key | 324 | materialized 446, then movement 623 consumes 122 |
| 669 | 626 | 72 | movement 703 |
| 670 | 720 | 184 | movement 704 |
| Total | | **1,568** | 134+604+250+324+72+184 |

Segment 630 was created **2026-09-21 04:33:20.682528 UTC** (09/20 21:33 PDT), at the same save as ActualRun 111 (audit **137045**). The run's business date is earlier; occurrence and creation timestamps must not be conflated. Its initial 446 is independently reconstructed as current 324 + only 122-bin outward movement. At creation, the older 617/618/619 projections already represented 446 under the blank status key. The pre-#251 exact-key materializer explains the second representation. This is strong record/code evidence for alias rematerialization, not a new 446-bin receipt.

**All 446 excess bins are accounted for:** 324 is the surviving duplicate projection, and 122 is the portion already physically consumed from that duplicate while the older inbound projections remain undiminished. `324+122=446`; `1568−446=1122`. Simply zeroing 630 removes only 324 and leaves a 122-bin discrepancy. Arbitrarily subtracting the remaining 122 from receipt 774 would invent receipt-specific consumption. Treatment is uniformly untreated in this set, but exact surviving per-receipt allocation must remain explicitly unresolved unless additional evidence proves it. A future pooled treatment projection may preserve the complete receipt-evidence set without pretending a specific receipt supplied those 122 bins.

The later Room 14 transfer is balanced: segments **154/148/160** dispatched **72/184/542**, total **798**, received as **669/670/618**. Its source room now has zero Bartlett 1372 stock in this scan. There is no evidence here that the completed Bartlett repair was undone or that this 798-bin transfer created the 446 excess. No Bartlett repair or audit was altered.

WP-4, room **1**, independently has **68 eligible** Bartlett 1372 bins. WP-7's shared treatment selection is blocked. The operator clarified that the **172-bin run is awaiting entry because of the locked bins**; it is not an existing saved run to locate. The regression scenario is therefore an intended 172-bin entry using **68 from WP-4 plus 104 from WP-7**. The 172-bin requirement is operator-confirmed; the database independently confirms the 68 eligible WP-4 bins and the 1,122 physical WP-7 bins. No production run was created to test this.

The #255 proof rejects this WP-7 state for concrete reasons: non-null receipt lineage is outside its proof contract, and its allowed positive arrivals do not include InterCrewTransferReceive. Dump already uses that same shared projector. Widening only the dump UI would leave the underlying ambiguity/projection lifecycle unchanged.

## Refreshed discrepancy classification

The scan examined **993 ledger positions, 684 segments and 747 movements**: **24 findings = 6 existing-proof successes + 13 not proven by that algorithm + 5 negative ledger positions**. All six proof successes are now zero physical positions; they are retained historical projections, not available bins to consume. There were zero duplicate operation keys, zero duplicate raw stored segment keys and zero suspicious repeated parent/source consumption groups under the scan's specific definitions. Those zeroes do not exclude normalized-key duplication.

The old 8-proven / 13-unresolved / 5-negative total was 26. Two formerly proven positions no longer appear: EVANS-3/9722 and LAMB-14/9682. Production already contains normalization audits **153820** and **153822**, created by business operations on 09/26. The WP-7 case is within the 13 unresolved cases, not an additional 27th discrepancy. The EVANS-05/9682 discrepancy is a different room/position from the resolved LAMB-14/9682 case.

The following table distinguishes a supported mechanism from permission to normalize. Passing a quantity/treatment replay is not proof of which old code event originally created a stale row. Unresolved labels remain unresolved; no false certainty is used to unlock inventory.

| Room / lot | Physical / explicit | Root cause or supported mechanism | Proof/limit |
|---|---:|---|---|
| WP-5 / 1084 (profile 2) | 10 / 130 | Status-key duplicate + receipt correction. 120 stale in #65; #597 carries current 10. Receipt override also prevents broad replay proof. | Alias structure and ledger arithmetic established; full normalization proof absent |
| EVANS-10 / 3050 (profile 2) | 0 / 48 | Historical depleted/shared untreated projection. Existing #255 occupancy/arrival proof succeeds; original cause of projection survival not uniquely established by proof. | Current treatment proof passed; exact producer defect not asserted |
| LAMB-14 / 9040 (profile 2) | 0 / 228 | Unretired depleted projection; missing historical movement evidence. 228 in #123 remains after complete ledger depletion; five BinsRun deductions total 272. No movements attached to this segment. Exact producer attribution remains unresolved. | Evidence-backed mechanism; unresolved exact attribution |
| LAMB-14 / 9100 (profile 2) | 0 / 6 | Historical depleted/shared untreated projection. Existing #255 occupancy/arrival proof succeeds; original cause of projection survival not uniquely established by proof. | Current treatment proof passed; exact producer defect not asserted |
| LAMB-15 / 9380 (profile 2) | 0 / 40 | Unretired depleted projection; missing consumption evidence. 40 in #177 remains after three 20-bin BinsRun deductions exhausted the later 60-bin occupancy. No segment movements; exact creation/consumption link unresolved. | Evidence-backed mechanism; unresolved exact attribution |
| MCD-08 / 1600 (profile 21) | 0 / 252 | Historical depleted/shared untreated projection. Existing #255 occupancy/arrival proof succeeds; original cause of projection survival not uniquely established by proof. | Current treatment proof passed; exact producer defect not asserted |
| LAMB-14 / 9380 (profile 2) | 0 / 3 | Receipt reduction not reflected in projection; unresolved replay. 3 in #351 versus zero ledger; final -2 receipt correction and -1 run explain residual shape. Must prove exact receipt attribution before normalization. | Evidence-backed mechanism; unresolved exact attribution |
| DH-15 / 2350 (profile 18) | 202 / 598 | Status-key duplicate + receipt reclassification. 396 stale in #407; #557 carries 202. Ledger includes correction #3000; replay of corrected receipt history required. | Alias structure and ledger arithmetic established; full normalization proof absent |
| EVANS-05 / 3152 (profile 2) | 61 / 162 | Status-key duplicate; incomplete immutable identity. 101 in #414 duplicates prior inbound; canonical #426=61 after 40 out. Historical TransferIn provenance requires canonical identity proof. | Alias structure and ledger arithmetic established; full normalization proof absent |
| EVANS-05 / 9682 (profile 2) | 252 / 536 | Status-key duplicate; incomplete immutable identity. 284 in #419 duplicates inbound; #428=252 after 32 out. This is not the resolved LAMB-14 lot 9682 position. | Alias structure and ledger arithmetic established; full normalization proof absent |
| EVANS-11 / 9092 (profile 2) | 0 / 10 | Historical depleted/shared untreated projection. Existing #255 occupancy/arrival proof succeeds; original cause of projection survival not uniquely established by proof. | Current treatment proof passed; exact producer defect not asserted |
| EVANS-05 / 9401 (profile 10) | 0 / 8 | Unretired depleted transfer projection; incomplete identity evidence. +8 TransferIn #2525 then -8 TransferOut #2526; segment #432 retains 8. Current strict replay does not prove immutable identity. | Evidence-backed mechanism; unresolved exact attribution |
| EVANS-6 / 9691 (profile 10) | 0 / 1 | Historical depleted/shared untreated projection. Existing #255 occupancy/arrival proof succeeds; original cause of projection survival not uniquely established by proof. | Current treatment proof passed; exact producer defect not asserted |
| EVANS-10 / 9541 (profile 10) | 0 / 4 | Receipt true-up projection survived depletion/reclassification. Receipt-specific #532=4 from ManualTrueUp; room ledger is zero after receipt corrections and dispatch. Exact corrected receipt attribution unresolved. | Evidence-backed mechanism; unresolved exact attribution |
| EVANS-10 / 3290 (profile 10) | 0 / 35 | Receipt true-up projection survived transfer. Receipt-specific #533=35 from ManualTrueUp; later -205 and -35 transfers exhaust 240. Current proof excludes receipt-scoped projections; do not infer an automatic repair. | Evidence-backed mechanism; unresolved exact attribution |
| EVANS-10 / 9414 (profile 10) | 0 / 6 | Historical depleted/shared untreated projection. Existing #255 occupancy/arrival proof succeeds; original cause of projection survival not uniquely established by proof. | Current treatment proof passed; exact producer defect not asserted |
| WP-8 / 2350 (profile 18) | 170 / 362 | Status-key duplicate + corrected receipt history. 192 stale in #552; #553=170; mixed transfer/receipt and reversal/correction evidence exceeds #255 proof scope. | Alias structure and ledger arithmetic established; full normalization proof absent |
| EVANS-10 / 9820 (profile 10) | 0 / 3 | Corrected receipt history + unretired depleted projection. Segment #564=3 survives final -3 transfer #3187; earlier -1 correction changes receipt replay. Exact historical reconstruction remains required. | Evidence-backed mechanism; unresolved exact attribution |
| WP-7 / 1372 (profile 17) | 1122 / 1568 | Status-key materialization duplicate, receipt-linked inbound. Initial 446 duplicated by #630 at ActualRun 111 creation; 122 consumed from that duplicate; later 798 arrival is balanced. Exact accounting below. | Alias structure and ledger arithmetic established; full normalization proof absent |
| MCD-08 / 1270 (profile 21) | -40 / n/a | Historical receipt correction after stock left source. Profile 21: +40 reclassification #1633 -40 transfer #1750 -40 later correction #1907 = -40. Replacement +40 under profile 26 was subsequently transferred too. Source-based receipt reclassification after depletion. | Ledger sequence proven; corrective allocation not authorized |
| MCD-08 / 1538 (profile 29) | -18 / n/a | Historical receipt correction after stock left source. Profile 29: +41 receipt #1669 -41 transfer #1756 -18 correction #1910 = -18. Receipt reduction after original stock was already transferred. | Ledger sequence proven; corrective allocation not authorized |
| MCD-08 / 2822 (profile 18) | -5 / n/a | Historical receipt correction after stock left source. Profile 18: +5 receipt #1885 -5 transfer #2128 -5 correction #2295 = -5. Replacement +5 profile 29 then transferred. Source reclassification after depletion. | Ledger sequence proven; corrective allocation not authorized |
| MCD-09 / 1537 (profile 18) | -20 / n/a | Historical receipt correction after stock left source. Profile 18: +20 receipt #2035 -20 transfer #2064 -20 correction #2082 = -20. Replacement +20 profile 29 then transferred. Source reclassification after depletion. | Ledger sequence proven; corrective allocation not authorized |
| MCD-09 / 1539 (profile 18) | -44 / n/a | Historical receipt correction after stock left source. Profile 18: +44 receipt #2034 -44 transfer #2068 -44 correction #2080 = -44. Replacement +44 profile 29 then transferred. Source reclassification after depletion. | Ledger sequence proven; corrective allocation not authorized |

The five negative positions total **−127 bins**. They share a concrete receipt-correction-after-depletion pattern; they are not explained by summing treatment history. Their historical source/custody corrections require a separate reviewed plan, not automatic repair during this project. The canonical command boundary must reject future deductions from exhausted identities, including administrator paths.

## Proposed canonical contract (not implemented)

Create a domain/application inventory module usable by **both Web and API**, with EF adapters behind interfaces. Do not put the new authority in a controller or make API reference Web. Extract the existing ledger/custody rules rather than introducing another physical balance table.

`InventoryAvailability.ResolveAsync(scope, operationRequirements, asOf)` returns a structured immutable result:

- canonical identity (crop, GrowerLot, FruitProfile, lot/grower identity, organic/production attributes), location/custody and occupancy epoch;
- authoritative physical quantity and its ledger/custody evidence watermark;
- committed quantity with explicit evidence (distinguish already deducted dispatch from an additional reservation); available quantity bounded by physical stock;
- treatment slices, each with signature, application history, confidence, receipt provenance scope and available quantity;
- historical/superseded/depleted projection references excluded from physical availability;
- a typed proof plan, proposed projection deltas, evidence IDs/hash, algorithm version and normalization-required flag;
- separate quantity, treatment and receipt-provenance confidence; operation-specific blocking reason;
- expected versions for affected receipts, transfers, runs, segments and the ledger read-set fingerprint. A watermark is a stale-input detector, not a database lock.

Read methods must use no-tracking queries and never SaveChanges, update timestamps, acknowledge diagnostics or emit normalization audits. Do not let callers reconstruct treatment slices or add their own “missing untreated” quantity. Room/group reads batch all positions and related evidence; callers consume the same result.

`InventoryCommandExecutor.ExecuteAsync(command)` owns the outer transaction. It resolves the same result **again inside the transaction**, checks the requested scope and quantity, applies only proof-backed projection changes, records the proof audit, performs authoritative movement/consumption plus parent/history changes, validates conservation and commits. Callers supply operation intent and business-specific requirements (seal, custody route, receipt scope), not alternative inventory arithmetic.

Quantity rules: `0 <= available <= authoritative` for operable positions; negative legacy positions return a blocked diagnostic, never an invented zero authoritative quantity. Draft form selections do not reserve bins. Already-dispatched bins are removed from room availability and held in transit, so they must not be deducted twice as reservations. No new reservation subsystem is presently justified. Pending ActualRun overrides are not reservations and cannot grant overdraw permission under the requested invariant.

Treatment and receipt identity are different questions. A fully proven single-treatment pool may be eligible for an ordinary move/dump despite unresolved per-receipt allocation; a receipt-scoped correction must still block without exact receipt attribution. A mixed treatment pool requires an explicit proven slice. Never label a quantity gap Untreated solely because no projection covers it.

## Transaction and concurrency design

Use PostgreSQL Serializable transactions across every authoritative read and write, preserve existing EF version tokens, and retain unique operation keys. Retry only the complete idempotent command after serialization/deadlock failure, with a bounded retry policy or a refresh response. Validate operation-key reuse against the entire command payload. All mutation paths, including API and administrative overrides, must participate; serializable consumers cannot protect against an outside writer using stale pretransaction reads.

Keep ledger, projection updates, movement history, revisions and normalization/operation audits in the same transaction. A normalization helper must not commit an independent transaction. An outer caller needs a savepoint only when it intentionally owns a larger unit; failures clear tracked state after rollback. No production email/network side effect belongs inside the physical command transaction; existing post-commit delivery/outbox patterns remain separate.

Race tests must use two actual PostgreSQL connections with controlled barriers: dump/dump, transfer/dump, treatment/transfer, receipt correction/transfer edit, return/reopen, and baseline import/move. Expected outcome is one valid serial order or a complete retryable rollback, never partial normalization or double consumption. Test implicit/unmaterialized lineage as well as existing segments, because segment versions alone cannot protect a missing row or a ledger predicate.

Backdated operations need an explicit epoch/event-time rule: do not compare a present-day mutable receipt against an old arrival and silently rewrite history. Application timestamps determine who recorded the event; effective movement timestamps determine the business chronology. Both remain in the proof.

## Projection lifecycle and constraints

Proposed minimal durable distinction is **Current vs Historical projection disposition**, separate from Untreated/Confirmed/Unknown treatment. Historical retirement records carry a reason and normalization audit/evidence reference. Do not add five overlapping states as substitutes for evidence. New current projection creation must go through one canonical factory; existing historical rows cannot be revived just because their key matches. Reversals create/credit a current projection using original movement evidence and current occupancy, preserving the original retired row/history.

Schema decision is a Phase 1/2 gate, not a migration in this documentation PR. Prefer the smallest additive disposition/evidence reference if current zero-balance rows plus audit cannot distinguish legitimate reversal from stale-row resurrection. Prototype this against return/reversal and mixed-receipt fixtures before fixing DDL. Existing `CurrentBins` stays a treatment projection cache constrained by the physical authority, never a new ledger. No blind backfill from `CurrentBins > 0` to Current is safe.

Keep existing movement/ledger operation-key uniqueness and segment versioning. Add nonnegative **current projection** checks only after classification; allow historical evidence to remain intact. Evaluate current-only uniqueness over canonical position + treatment signature + receipt provenance scope/epoch, with explicit NULL semantics. Reject “one active projection per lot”: multiple receipts and treatments are legitimate. Raw string uniqueness is insufficient to prevent alias duplicates.

Do not add `ChangeAmount >= 0` to the ledger: deductions are valid. Do not rely on `NewBinCount >= 0` as proof of aggregate balance; old clamping already demonstrates the distinction. A cross-row nonnegative balance cannot be enforced by an ordinary CHECK. Initially enforce it at the canonical Serializable command boundary and prohibit bypass writers; consider a database-level predicate/locking mechanism only if provider concurrency tests prove that boundary insufficient. No second mutable balance ledger is justified solely to obtain a row lock.

Backfill, if needed: read-only classify first; exact identity/epoch/version/fingerprint guards; proof and audit per group; all-or-nothing reviewed batch on a fresh disposable restore; compare protected historical fingerprints; leave all unresolved groups untouched. No backfill is authorized for production by this plan.

## Explicit staged plan and exit gates

| Phase | Deliverable | Exit gate / production behavior |
|---|---|---|
| 0 — assessment (this PR) | Path matrix, source anchors, current discrepancy root-cause table, WP-7 accounting, policy/transaction gaps | Documentation only; no application behavior or database changes |
| 1 — canonical read contract and evidence corpus | Shared non-Web module, existing ledger/custody adapters, batch availability result, separate treatment/receipt confidence, fixtures for every listed class, admin shadow diagnostic | No automatic normalization; old/new differences explained for every position; no per-row proof queries; no operational cutover |
| 2 — transactional command and lifecycle | One command executor, canonical projection factory, disposition decision, proof/audit plan, absolute nonnegative policy, removal of implicit untreated synthesis | Audit-failure/retry/idempotency/concurrency tests pass; no producer/consumer can commit independent normalization; additive migration only if justified |
| 3 — migrate every producer and consumer | Room moves, BinsRun/dump/packline, processor/outside, inter-crew/Truck receive/edit/reopen, receipt Web/API/override/void, losses, treatments/reversal, baseline import; remove duplicated wrappers and fallback arithmetic | Static writer/call-site coverage test; cross-workflow matrix identical; no activation while any ordinary writer bypasses the executor |
| 4 — fresh restore, full rehearsal and release candidate | Fresh verified production backup/isolated restore, discrepancy replay, randomized state machine, full suite, concurrency/provider, query-count/load evidence, protected fingerprints | Every acceptance invariant proven; unproven classes remain blocked; one complete reviewed activation strategy; no production repair bundled |
| 5 — separately authorized controlled rollout | Backup gate, exact commit/schema compatibility, all-workflow cutover, read-only smoke and next genuine operations | No fake production operations; fail at any gate; rollback application compatible with additive schema, never restore over later real activity |

Use successive reviewable PRs based on then-current main, not stacked PRs. Earlier phases are extraction/shadow work, not a claim that the architectural fix has shipped. Activate all ordinary paths together only after the coverage gate; do not deploy one workflow's new eligibility while leaving other writers on old mutation rules. One master acceptance checklist tracks every phase and the 23 requested deliverables.

## Tests and performance required before implementation merge/release

Reusable corpus: original Room 14 Bartlett, 9722, LAMB-14/9682, WP-7/1372, recreated source, status aliases, receipt replacement/correction, split transfer, partial depletion, mixed treatments, balanced lineage, genuine ambiguity and negative attempt. Production IDs may label fixtures; domain decisions must never branch on them.

Property/state-machine runner generates receive/treat/split/move/return/dump/edit/reverse/replacement/processor sequences. After each event assert nonnegative physical availability, conservation including custody and consumption, projection totals never increasing physical inventory, no duplicate consumption, no historical revival, immutable outside-scope history, correct ambiguity and idempotent audit counts. Store seed and shrink failing traces. Generate both canonical and legacy alias/receipt-specific shapes.

Cross-workflow tests reuse one seeded state for room move, transfer, processor sale, dump/ActualRun, and receiving custody; compare the same availability slices with equal operation requirements. Add audit-save failure and concurrency injection to each adapter, not just the domain unit test. Invalid normalization proof must fail before any ledger update; failed movement must roll back normalization and audit.

Fresh-restore gate cannot use September 25 Backup #170 as evidence of September 30 production state. Obtain and independently verify a current standard backup only when implementing the rehearsal phase. Record capture age, application SHA, package hash, production changes since capture and historical fingerprints. Run the complete consistency scan before/after, both with no operations (reads must not mutate) and with isolated real service commands. Validate all 24 current cases plus any new cases in that fresh copy. This stage has not been run and is not represented as passed.

Performance gate: query-count tests for 1, 10, 100 and 1,000 positions; evidence rows, not bin count, drive cost. Fixed/bounded batch queries per requested room/custody scope, no history scan per displayed row, cancellation and explicit pagination/limits. `ProvenLineageReconciliation.ProveAsync` currently loads room ledger/movements/applications per candidate from inside the batch projector; processor/outside option builders also call selection per snapshot. Extract a batched evidence loader and memoize only within a consistent request/transaction. Measure provider execution plans, memory and p50/p95 against the current baseline. The ~7-second whole-production diagnostic scan is not a UI benchmark; no performance result for a new service is claimed.

## Admin and operational behavior

Admin diagnostic consumes the exact canonical result: physical balance, custody, available/committed amounts, treatment slices, receipt evidence sets, excluded historical rows, proof candidate deltas, blocker codes, transaction watermark and parent links. It is read-only and must not acknowledge or normalize anything on page load.

Normal operators see available bins and treatment choice. A proved stale-history case is selectable with normalization deferred to their authorized write. A blocker explains the missing business evidence, not routine internal arithmetic. After this change, legitimate Needs Review categories remain: incomplete/contradictory physical identity; negative historical ledger; unknown treatment application timing; indistinguishable mixed treatment consumption; unsupported or missing movement/reversal evidence; receipt-specific action with unresolved receipt allocation; inconsistent custody; stale/concurrent state requiring reload. Raw overcount alone is not one of these reasons.

## Completion ledger for the 23 requested deliverables

| Deliverable | Status in this PR |
|---|---|
| 1–4 architecture, duplicate causes, physical authority, treatment design | Documented here and in full matrix |
| 5 shared service implemented | **Not implemented; Phase 1–3** |
| 6 old duplicated code removed | Removal list mapped; **nothing removed yet** |
| 7 lifecycle producer defects fixed | Specific bypass/key/transaction defects identified; **not fixed yet** |
| 8 constraints | Existing constraints reviewed; proposals/rejections stated; no DDL |
| 9 production root causes | Fresh read-only classification, confidence limits retained |
| 10 WP-7 accounting | All 446 excess accounted; 68 eligible confirmed; operator clarified intended unsaved run of 172, requiring 104 from WP-7 |
| 11 all workflows canonical | **Not yet; mandatory Phase 3 exit gate** |
| 12–13 concurrency/transactions | Design and concrete current gaps documented |
| 14 admin diagnostics | Contract and shadow/cutover design; not implemented |
| 15 migration/backfill | Strategy proposed; no production update or automatic classification |
| 16–18 invariant/random/full tests | Test plan only; no new implementation to certify |
| 19 fresh restore | Deferred to implementation phase; not run |
| 20 performance/query results | Current N+1 risks identified; benchmark gate defined; no new-service result |
| 21–22 ambiguity / future Needs Review | Explicit supported blocker classes above |
| 23 PR/commit | Documentation-only draft PR; exact number/commit in completion message |

Verification actually performed: solution restore passed; solution build passed with 63 existing warnings and zero errors; current-main local audit harness build passed with zero warnings/errors; EF pending-model check found no changes. Fresh production scan and bounded supporting queries were read-only; report arithmetic and classification checks passed; documentation diff checked. Focused application tests: zero run, because this PR changes documentation only; full suite and fresh database restore were not run and cannot certify an unimplemented architecture. No C# formatting or MSI build applies. Markdown/CSV structure and whitespace are checked. Prior #256 test counts are deliberately not reused as evidence for this task. No merge, deployment, production normalization, negative-position correction or Bartlett repair was performed.
