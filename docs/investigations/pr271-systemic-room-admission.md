# PR #271 systemic room-admission and safeguard review

Review date: 2026-10-13. Owner requested revisions to draft #271, not release.
Starting head `0b3063070aed448fecaef98d7ef20305b9f6126e`; fetched main/base
`75b8dbe00210d413d35ae41d0b134b312a3ad66e`. No incoming main commits.
Work continues on remote branch `codex/authoritative-partial-custody` through its
existing detached integration worktree; no other worktree was reset.
Specification **1.1.0**, governance source **c21da183961f6d0304a83a1c2eb10fc6e7f9884d**.
AGENTS, specification, decision register, change procedure, inventory phase contracts,
change-scoped testing and overnight release standard were read against fetched main.

## Assessment and review boundary

Applicable: INV-001–006, REC-001–002, MOV-001, TRT-001–002, ROOM-001,
AUD-001, OPS-001, GOV-001; ADR-001–007. This implements existing policy;
no policy version or approval authority changes. Authoritative origins, signed
ledger events and exclusive custody determine stock. Exact receipt ancestry and
treatment membership are separate proofs. A new receipt/correction origin does
not confer treatment merely by sharing a destination.

The review inventories all 32 registered workflows, including physical credits,
their adapters and reversals, dormant typed commands, nonphysical paths and
maintenance writers. It does not claim every historical record is repaired or
every possible operator request succeeds. Full-pool source proof and destination
admission are deliberately different operations. Strict readiness remains strict.

Changed dependencies in this revision: positive receipt quantity correction,
untouched receipt-placement compensation, and the shared finalization of two
typed addition commands. Their ledger/movement/projection relationships, original
applications, receipt history, custody parents, concurrency and audit are the
test boundary. No new database migration or application entrypoint.

## Canonical contract already present

Reuse `InventoryCommandExecutor.ExecuteAsync` (Serializable transaction and
complete-intent journal), `InventoryOriginGuard.ValidateAsync`,
`InventoryEvidenceLoader`, `InventoryAvailabilityResolver`,
`InventoryCohortEvidence`, `InventoryDestinationAdmission.Assess`,
`PrepareDestinationAsync`, `CanonicalProjectionFactory.CurrentAsync` and
`ValidateDestinationsAsync`. There is no new inventory authority or writer.

The adapter authenticates and authorizes the operation. The executor validates
an active actor, intent, transaction watermark/version and operation-specific
source/origin. Source quantity must cover the exact selected custody/treatment
slice; a receipt-specific action additionally proves ancestry. Destination checks
active facility/room, seal and route restrictions in its typed handler, then
checks ledger identity, nonnegative quantity, valid custody and origin chain.
An incoming command creates its own cohort. Commit checks exact signed physical
effects, equal projection delta and retention of prior destination rows, with
only exactly recorded consumption permitted. A discrepancy produces a
`CanonicalDestinationReconciliation` audit; it does not become a repair.

Capacity is stored/displayed as `Rooms.CapacityBins`, but there is no general
hard capacity admission predicate in these canonical handlers. The existing
ROOM-001 traceability gap explicitly says not to invent a new capacity or
commodity-segregation rule. This review adds neither an arbitrary capacity
rejection nor a capacity bypass. Defining/enforcing a new hard capacity policy
requires a separately scoped decision.

## System-wide room-entry workflow matrix

Paths below are repository-relative. Data handler files are under
`src/CropQc.Data/Inventory/`; named Web services/controllers are under their
usual `src/CropQc.Web/Services/` and `Controllers/` directories. Method names
are stable exact code locations. All canonical operations share the transaction,
journal, active-actor, SQL/DbContext writer and final audit safeguards above.
“D” means canonical destination admission, not a whole-pool operability test.

| Workflow / entrypoint | Adapter → command / handler | Authoritative and treatment proof; exact blocking conditions | Room/derived behavior and consistency |
|---|---|---|---|
| Ordinary web receiving; ReceiptsController create | DashboardDataService.CanonicalReceiving → ReceiveStockAsync, InventoryCommandReceiving.cs | Active ordinary Truck receipt intent; positive count; exact active grower/profile; unique receipt number; original arrival; untreated new allocation | Active matching warehouse, unsealed room; D; no old pool normalization. #281 intact |
| API receipt create; CropQc.Api ReceiptService | Same ReceiveStock command | Station/API authorization plus canonical origin; metadata-only API edit cannot change physical identity/count/location | Same D and writer, no API-specific projection authority |
| Activate previously unposted receipt | DashboardDataService.CanonicalReceiptMetadata → ActivateReceiptInventory → ReceiveStockAsync | Exact receipt version/count; absence of existing ledger, movements, treatment, depletion and projection dependencies; no duplicate origin | Same D. A preexisting history block protects against double recognition |
| Room move / same-company warehouse transfer; BinsRunController Transfer | DashboardDataService.Canonical → RoomMove/WarehouseTransfer → InventoryCommandOperations.ApplyAsync | Ledger/custody amount, recursive origin, selected known treatment; shared ancestry allowed; equal debit/credit | Source active/unsealed; target active/unsealed; cross-company route cannot bypass truck receipt; D |
| Reverse room move; BinsRunController Transfer Reverse | ReverseCanonicalRoomTransferAsync → ReverseRoomMove | Exact unreversed parent ledger pair and original destination movements; surviving selected allocations; source for reversal is original destination | Independent source cohorts allowed; original source gets D; actual consumed original allocation still blocks |
| Legacy/internal inter-crew receive; BinsRunController InterCrewTransfers Receive | InterCrewTransferService.Canonical → ReceiveTransfer → InventoryCommandCustody | Exact active dispatch parent, complete current custody quantities and treatment; receipt-required routes validated | D; no second ReceiptAdd; whole parent proof is legitimate |
| Matched Truck Receipt completion; TruckReceiptReconciliationController CompleteTransfer | CompleteCanonicalAsync → ReceiveTransfer | Receipt/transfer versions, route, variety totals, exact load proof, no prior partial acknowledgement history | D; use allocation-specific custody workflow once #271 acknowledgement exists |
| Partial acknowledgement; AcknowledgeTransfer | WriteReceiptCustodyAsync → ChangeReceiptCustodyAsync | Exact dispatch IDs, observed variety count, remaining dispatched amount, matching crop/route; acknowledgment ≤ observed receipt and dispatch | No room credit; seal does not prevent holding. Missing allocation identity cannot be replaced with variety totals |
| Receipt-held placement; PlaceTransferCustody | Same → PlaceReceiptCustody | Valid original physical flow, acknowledgment, held quantity, exact dispatch treatment/application links | Active/unsealed room in receipt warehouse, not dispatch source; D; separate cohort |
| Placement reversal then replacement; ReversePlacement / PlaceTransferCustody | ReverseReceiptCustodyAsync then PlaceReceiptCustody | Exact unreversed placement, untouched destination segment, same identity/ancestry/treatment/apps, no downstream consumption/move/treatment; independent cohort or full valid source; recursive origin | Room debit becomes held custody, not physical return to dispatch; replacement uses D. Fixed whole-pool veto here |
| Reverse acknowledgement | ReverseReceiptCustodyAsync | Only held amount can be compensated; valid reversal evidence/actor/reason; no negative net custody | No room credit; unresolved dispatch reappears exactly once |
| Transit edit / exact partial return; EditTransit | TruckReceiptReconciliationService.Canonical → TransferEdit / ReturnTransitAllocationAsync | Exact original dispatch allocation, transfer version, remaining whole-load conservation; acknowledged history rejects legacy whole-load edit | Return to active/unsealed original source via D; no new origin |
| Inter-crew return of completed legacy receipt / reopen | InterCrewTransferService.Canonical, TruckReceiptReconciliationService.Canonical → Return/ReopenTransfer | Original received cohort must survive; exact parent, receipt/version and paired flow; acknowledgement history requires compensations instead | Received room is a source, not merely destination. Source proof valid; return room uses D |
| Outside warehouse return; BinsRunController OutsideTransfers Reverse | OutsideWarehouseTransferService.Canonical → Return → InventoryCommandCustody | Active original custody parent and exact complete dispatch-minus-reversal slices; treatment retained | D at active/unsealed original room; dispatch source need not still contain consumed projection bins |
| Processor shipment return; ProcessorShipmentsController Reverse | ProcessorShipmentService.Canonical → Return | Exact complete shipment/line custody, original parent, unreversed movements and effective treatment | D; return cannot invent a second shipment origin |
| Loss reversal; RoomInventoryLoss service/controller route | ReverseLossAsync → RestoreConsumptionAsync | Exact loss debit, immutable source movement quantities, no prior restoration, recursive origin including empty history, original treatment/app links | Active/unsealed restoration room; D; depleted room is not an availability requirement |
| Run/depletion reversal; BinsRunController Cancel/Reverse and DashboardDataService depletion | ReverseRunAsync → RestoreConsumptionAsync | Exact original run revision/entry/depletion ledger and movement, unreversed parent; original consumption preserved | D; recorded compensating credit; no rewrite of original dump |
| Actual/legacy run revision | ReviseRunAsync / ReviseLegacyDumpAsync | Exact same-run restoration plus sufficient selected current source, versions and treatment; new consumption conserved | Restoration uses D; subsequent deduction needs source proof. A restored credit is not permission to consume unrelated ambiguous stock |
| Positive quantity correction with surviving receipt bins; ReceiptsController AdminInventoryOverride | ReceiptInventoryOverrideService.Canonical → ChangeReceiptQuantityAsync | Active ordinary receipt, exact current receipt location(s), expected version/fingerprint, allocations total delta, explicit untreated confirmation | Active/unsealed selected room; now D; new untreated current-time cohort, old projections untouched |
| Positive correction after complete consumption | Same → AddDepletedReceiptStockAsync | Recorded ordinary receipt origin and full origin history, no surviving allocation, explicit selected room fingerprint, presence and untreated confirmation | Active/unsealed room in receipt warehouse; D; old consumption unchanged; no assumed location/history |
| Negative correction / void | Same → ChangeReceiptQuantityAsync | Exact receipt-owned current room allocations covering removal, no unexplained custody; void exact receipt confirmation and zero external/held/transit amount | Source proof, not D; selective/recorded reconstruction only if proven needed for exact consumption. No recall of consumed bins |
| Receipt location correction | Same → CorrectReceiptLocationAsync | Exact current receipt slices, expected unchanged identity/quantity, selected source room, all unselected allocations retained | Source operation needs ancestry/treatment; target D; exact debit/credit with before/after audit |
| Receipt identity correction | Same → CorrectReceiptIdentityAsync | Exact receipt allocations, active reviewed replacement, no correction cycle, immutable old identity and valid custody mapping | Source proof; target identity D; external custody is reclassified through recorded identity mapping, never duplicated |
| Manual positive true-up / room fill; BinsRunController TrueUp | DashboardDataService.CanonicalManualStock → AddManualStockAsync | Explicit authorized delta, current ledger identity; new treatment Unknown, no receipt invented | **D follow-on:** adapter/executor still demand whole-pool IsOperable and handler balanced projection; inconsistent with ordinary independent admission |
| Baseline import/replacement; RoomInventoryController | RoomInventoryImportService.Canonical → CanonicalBaselinePreview / ImportBaselineAsync | Reviewed full-room forecast, effective cutoff, no omitted-lot loss, no reduction, exact masters and fingerprint; Unknown added treatment | **D follow-on:** old normalization/whole-pool dependency; baseline eligibility checks active but no explicit seal predicate. Needs separate import contract review |
| Typed ReceiptCorrection / BaselineAdjustment | Engine compatibility intents; no current Web/API adapter found | Selected source slice and signed declared delta; increases restricted to untreated signature; old single-allocation behavior | PR271 endpoint regression fixed. Still older source-style model; consolidate/deprecate in follow-on, not a new exposed correction UI |
| Treatment assignment / reversal | RoomTreatmentService.Canonical, .CanonicalReceiving; treatment command handlers | Point-in-time exact presence, application sources, full occupied room for room application; exact ancestry for receipt application; reversal retains original events | Zero physical quantity delta; cannot bypass true treatment proof. Later arrivals excluded |
| Administrative reconstruction / historical identity maintenance | ProjectionReconstructionCommand, reviewed CLI maintenance registry | Explicit approved exact state/fingerprint, replay, protected-history checks, independent verification | Projection-only; not invoked by ordinary receiving. Requires separate production repair authority |
| QC, Door/Lot/Field samples, fill reports/email, projected runs, packout/repack metadata | Registered nonphysical services | Observations/planning/reporting only; actual packing consumption goes through run commands above | No physical room admission; not a hidden stock source |

## Safeguard decision matrix

This groups repeated predicates by the invariant they enforce rather than treating
each duplicated error string as a different business rule. Locations identify the
actual predicates and their callers in the matrix above.

| Check / exact location | Invariant / evidence | Current result; duplication or derived dependency | Decision and supporting evidence |
|---|---|---|---|
| ExecuteAsync intent, actor, journal hash/JSON, Serializable retry | MOV-001/AUD-001: actor, complete intent, same-key replay, fresh read versions | BLOCK; adapters also authenticate/authorize, deliberately complementary | Keep BLOCK. Existing PostgreSQL concurrency/replay tests; compensation races |
| Controller policies + WriteReceiptCustodyAsync RequireAccessAsync/RequireCrewAsync | Authorized operator and facility route | BLOCK; not inferred by active actor alone | Keep BLOCK; ReceiptCustodyHttpTests |
| ReceiveStockAsync master/duplicate/activation checks | INV-001: one authorized ordinary receipt origin | BLOCK; history query includes projections as a duplicate-risk diagnostic | Keep BLOCK until absence of prior physical origin is proven; no blanket activation bypass |
| InventoryOriginGuard.ValidateAsync | INV-001/003: receipts/openings, correction history, recursive movement/custody origin | BLOCK; independent of balanced segments | Keep BLOCK. Missing/duplicate origin tests; now explicit on placement reversal |
| InventoryDestinationAdmission.Assess negative/identity/custody/cutoff | INV-002/003/ROOM-001 | BLOCK actual negative ledger, invalid custody/identity, changed historical cutoff | Keep BLOCK. Five negative restored positions retained, no clamping |
| Typed room/warehouse/route/seal checks | ROOM-001/MOV-001 | BLOCK; repeated handlers, actual operational restrictions | Keep BLOCK. Live 25 sealed rooms; cross-company and seal regressions |
| InventoryAvailabilityResolver source treatment/quantity | INV-002/003/TRT-002 | BLOCK if selected quantity/history unproven; permits recorded or independent cohorts | Keep BLOCK for unsupported selected stock, not all future arrivals; 20 positive source-proof failures in restored corpus |
| Exact receipt provenance / InventoryReceiptAvailability.ReadAsync | INV-006/REC-001: which receipt owns removed/relocated bins | BLOCK for receipt-specific action, not ordinary shared-pool movement | Keep BLOCK. Shared-pool and TR110044 exact-newer-receipt tests; no proportional ancestry |
| CanonicalTreatmentSelections.MovementSlices and source adapters | INV-006: treatment selection without invented exact receipt | Aggregates compatible treatment slices, no fabricated receipt | Keep; #281 source regression proves incoming isolated stock selectable despite old remainder |
| Raw projection gap / destination diagnostic | INV-004/ROOM-001: projection is not authority | WARN/audit for incoming operation after authority proof; strict readiness unchanged | Keep WARN, possible separate RECONSTRUCT. Runtime tests 0/9/30/negative projection shapes |
| Positive correction NormalizePositionAsync | INV-004/AUD-001/ROOM-001 | Before revision silently reconstructed old destination even though only +delta needed | Remove this coupling; D plus exact delta, no normalization. Regression reproduced old-row retirement before fix |
| ReverseReceiptCustodyAsync whole-position IsOperable | REC-002/TRT-002 | Before revision BLOCK despite independently proven untouched placement | Permit proven exact cohort + origin + unchanged dependency guards. Reproduced 17 authority/9 old projection plus 7 placed; reverse 3/re-place 3 |
| ReverseReceiptCustodyAsync downstream dependencies | TRT-002/REC-002: reversal cannot pretend treated/moved/consumed bins remained untouched | BLOCK any actual downstream dependency; not a destination discrepancy | Keep BLOCK; move/loss/treatment dependency tests |
| Receipt correction allocations and external custody | REC-001/INV-003 | BLOCK absent exact removable amount, cannot net unrelated receipt deltas | Keep BLOCK; down15 is not justified by revised total alone |
| Additional origin confirmation and now-based effective time | TRT-001/REC-001 | BLOCK absent exact location/presence/untreated confirmation; old consumption not required to return | Keep existing correction primitive; unknown treatment never defaults untreated. TR110045 13−13+2 |
| CanonicalProjectionFactory.CurrentAsync exact cohort identity/apps | TRT-002/INV-004 | BLOCK conflicting matches **inside current command cohort**; single emitter of original error | Keep BLOCK; no global catch-to-warning. #281 avoids irrelevant legacy-key collision |
| ValidateDestinationsAsync | INV-003/TRT-002/AUD-001 | BLOCK unequal ledger/projection delta or altered old evidence without exact recorded consumption | Keep; shared by newly consolidated positive correction |
| NormalizePositionAsync / InventoryEventReplay / SelectiveProjectionReconstruction | INV-004/005/006 | RECONSTRUCT only a proven source representation needed by operation, with audited supersession | Keep guarded; neither descriptive candidate nor raw gap authorizes repair |
| InventoryCommandOperations positive endpoint rewrite | AUD-001/TRT-002 | Defect: committed +2 with null destination segment, despite balanced count | Removed redundant rewrite; both typed cases failed before fix. No guard relaxed |
| Manual true-up whole-pool gate | INV-001/004/ROOM-001 | Potential false BLOCK; separate origin still coupled to old treatment projections | Follow-on D: reuse D, preserve Unknown treatment and explicit quantity authorization; no change here |
| CanonicalBaselinePreview / ImportBaselineAsync | INV-003/004 and import cutoff history | BLOCK whole-pool proof; automatic normalization; broader room forecast safety | Follow-on D; do not remove cutoff/no-reduction protections |
| Evidence loader bound, immutable identity contradiction, unsupported backdated application | INV-005/TRT-001 | BLOCK with specific diagnostic; cannot prove selected operation | Keep BLOCK; do not fabricate timing or attribution |
| InventoryDeductionInvariantService / treatment readiness / migration writer fence | OPS-001/AUD-001/INV-003 | Strict readiness, canonical transaction-only writes, compensation-aware old-binary fence | Always BLOCK at respective boundary. Destination warning never changes release readiness |

The exact original error remains emitted only by
`CanonicalProjectionFactory.CurrentAsync`. Searches also traced “No exact
evidence-backed supersession plan”, “Exact current receipt inventory and treatment
ownership cannot be proven”, “Destination inventory cannot be proven for return”,
“Received inventory cannot be proven for reopening”, “Proven current projections
cannot cover”, and “Placement has subsequent ... dependencies” to the predicates
above. “Destination” in a reversal error often means the room being **debited**
(the forward operation's destination); it is not an admission veto.

## Confirmed corrections in this revision

1. **C: existing receipt-positive correction inconsistency.** A disposable ordinary
   receipt proves 19 bins, while its projection incorrectly shows 9. Adding 2
   previously retired/replaced the old projection. The corrected operation leaves
   that exact old row unchanged, creates 2 untreated bins in its own cohort,
   posts +2, reports 21 authoritative/11 projected, audits reconciliation, and
   replays once. A discrepancy remains visible; no repair is silently bundled.
2. **B: partial-custody reversal inconsistency introduced by #271.** Into a room
   holding authoritative 17 but projected 9, place 7 proven held bins. Reversing
   3 untouched placed bins falsely failed the whole-pool gate. It now validates
   the independent cohort and recursive origin, preserves exact identity/apps and
   downstream dependency guards, debits 3 into held custody and can re-place 3.
   Both treated and untreated dispatch variants are covered.
3. **B: typed addition lineage regression introduced by #271.** ReceiptCorrection
   and BaselineAdjustment correctly constructed arrivals but a later shared
   endpoint-swap assigned their already-null source as destination. Both
   committed quantities with null destination segments in failing reproductions.
   Removing this redundant swap retains the exact arrival link.

No exception is generically downgraded to a warning. No change to source replay,
origin algorithms, custody arithmetic, treatment application rules, permissions,
readiness, schema, protected-history checks or release standard.

## Existing PR scope / overlap and separate follow-on

**A — retained:** #269's exact quantity/location correction integration, receiving
operator permission path, partial allocation acknowledgement/placement,
compensations, exclusive held/transit accounting, arrival-time proof, canonical
writer guards, additive schema, audit snapshots, consumed-receipt addition UI and
#281 destination isolation/source selection. The PR's generated migration
designers account for most lines; they are schema snapshots, not separate runtime
features. Existing test changes that seed canonical origins replace invalid
projection-only fixtures rather than loosening assertions.

**B/C — this revision:** the three defects above; targeted writer registry review
and behavior contracts. No new independent PR is stacked on #269/#271.
Current #271 already contains #269 functionality; do not merge that older draft
independently without reconciling overlap. Earlier #268 receiving overlap and
historical diagnostics/reconstruction drafts are not runtime dependencies.

**D — separately scoped follow-on recommendation:** “Canonical manual-origin and
baseline admission consistency.” Exact paths:
`DashboardDataService.CanonicalManualStock.cs`,
`InventoryCommandExecutor.cs` manual-add source resolution,
`InventoryCommandManualStock.cs`, `CanonicalBaselinePreview.cs`,
`InventoryCommandBaseline.cs`, and dormant positive
`ReceiptCorrection/BaselineAdjustment` intents in
`InventoryCommandOperations.cs`. Consolidate onto existing destination
admission; retain explicit Unknown attribution for manual origins, full baseline
forecast and no hidden ledger loss; decide/verify baseline seal behavior; remove
or reconcile unused duplicate typed positive entry contracts. This is broader
than receiving/held custody and is not silently implemented here. No claim that
the entire application's manual/import paths now satisfy universal admission.

The old phase2 command-engine prose allowing destination normalization is
superseded by #281's isolated admission contract. Current code, this assessment
and the canonical rules control; historical verification prose does not approve
today's production repair.

## Production and restored evidence

Live Render SELECT-only census used explicit workspace
`tea-d7uc4ippo60c73ebn4mg`, database `dpg-d8cru6reo5us73a2bgt0-a`,
captured **2026-10-13T20:53:38.240426Z**.
[Query](pr271-room-admission-census.sql) and [complete results](pr271-room-admission-census.json).
4,344 ledger events: EBS 2,263 across 25 rooms; DH 362/21; McDougall
1,053/15; WP 666/5. This is an event census, not a sum of physical bins.
It includes receipts, corrections, paired room transfers, outside/inter-crew
custody, dumps and reversals, losses, depletions and opening imports.

55 depleted status-alias segments occur in **20 rooms**, across EBS/WP/McDougall;
every exact segment, lot and profile is in the JSON. Lamb15 includes Fuji
segments 591 (1033) and 629 (3202). Their zero quantities are not additional stock.
25 rooms are sealed (exact IDs in JSON); no unauthorized unsealing was performed.

A separate SELECT-only RepeatableRead analysis used the **unchanged local backup
#198 restore**, canonical #281 reader code at c21da183 (same implementation as
main 75b8dbe), and every room's canonical evidence. This is a backup-time
observation, not fresh production readiness. It loaded 1,048 positions including
historical/zero/identity remnants. [Complete position evidence](pr271-backup198-room-evidence.csv)
records exact identity, ledger, segment, movement and application IDs.

| Warehouse | All positions | Positive positions | Projection quantity mismatches | Positive source-proof blocks | Negative positions |
|---|---:|---:|---:|---:|---:|
| EBS | 417 | 133 | 65 | 10 | 0 |
| DH | 130 | 124 | 120 | 2 | 0 |
| McDougall | 345 | 122 | 58 | 8 | 5 |
| WP | 156 | 58 | 24 | 0 | 0 |

These 267 mismatches are **not 267 corrupted authoritative pools**. Many are
legitimate historical representations or reconstructable source projections.
There are 189 source diagnostics over all positive/zero/negative positions;
167 destination assessments fail mostly on historical incomplete identity, not
167 rejected live receipt requests. Assessment permission alone omits adapter
permissions/seals and recursive origin validation; it is not write authorization.

The 20 positive source-proof blocks (all retain destination diagnostic admission;
sealed-room blocks apply separately) are:

- EBS: EVANS-01 1565/profile2 (140), 9380/2 (147); EVANS-2 1011/7
  (1,153), 9332/7 (180); EVANS-05 1011/7 (350), 9342/2 (77);
  EVANS-7 3032/2 (113); EVANS-8 3032/2 (55), 3050/2 (275);
  EVANS-11 9541/10 (378).
- DH: DH-19 1451/7 (253); DH-22 1081/9 (403), both sealed.
- McDougall: MCD-05 2200/7 (280), 2710/7 (323), 2910/7 (69);
  MCD-06 2825/21 (537); MCD-11 2710/7 (426);
  MCD-15 1110/19 (286), 1290/19 (5), 2825/19 (524), all sealed.

EVANS-8/3050 is MixedTreatmentAmbiguity despite equal 275/275 quantities;
the other 19 have UnknownTreatment. Do not mark them untreated or authorize a
source movement from destination admission. Each requires replay of the linked
application/arrival/withdrawal events; this census identifies the actual
operation-specific limitation but does not certify individual repairs.

Five negative positions remain blocked:
MCD-08 1270/profile21 **−40**, ledger 1633/1750/1907;
1538/29 **−18**, 1669/1756/1910; 2822/18 **−5**, 1885/2128/2295;
MCD-09 1537/18 **−20**, 2035/2064/2082; 1539/18 **−44**, 2034/2068/2080.
They require full correction/identity history investigation, not clamping or
classification as harmless projection differences.

The broad raw SQL grouping experiment was not used as authoritative reconciliation:
it omits canonical nullable-identity fallback/corrections and baseline semantics.
The canonical backup reader above supplies the classifications. This prevents
false conclusions from superficially matching room/lot aggregates.

### Three receiving examples retained, not the scope boundary

Complete original histories, SQL, effective/recorded timestamps and failed-request
limits remain in [the receipt investigation](receiving-corrections-2026-10-13.md).

| Receipt | Proven before | Conditional correction | Required evidence / history preserved |
|---|---|---|---|
| TR110045 / 2476, RED 9502, room30 | 13 receipt −13 run168/revision193 = 0 current | +2 gives revised15, current2 | Explicit current room/presence/untreated confirmation; no assumption that +2 were dumped or historically treated. Original ledger4150/4374 and movement832/1071 unchanged. Effective dump Oct7 Pacific; committed Oct13 |
| TR110044 / 2475, Organic ORRD 9501, room30 | 35 exact owned bins; pool569 including unrelated490+44 | −15 gives receipt20/pool554 | Exact selected current ownership; no unrelated lot netting. Existing baseline operation succeeded on restore; original failed actor/payload/time not known |
| TR110057 / 2490, Conventional GSMT 9333, room7 | 27 exact owned bins; pool561 | +4 gives receipt31/pool565 | Operator-supported new origin in selected current location, no old room treatment inherited. Baseline operation succeeded on restore; original failed request not correlated |

Lamb15 #281 was reproduced historically for 1033/3202 aliases and is exercised
again by the affected regression set. Original ATGL Room3→Room2 remains
unverified; the recorded 34-bin RED/1242 example is not ATGL.
MCD-02/RED1242 retains canonical652/projected442 in backup198; its separate
**210-bin RED reconstruction remains excluded and requires its own authorization**.

## Validation, architecture and release limitations

Before-fix evidence: two focused tests failed for the whole-pool reversal veto and
unexpected positive-correction projection retirement; two typed-addition cases
failed with null destination segment. The first two passed immediately after
the narrowed fixes. Final validation results are recorded below after completion.

Regression coverage maps to the requested scenarios through existing meaningful
tests, expanded only where changed behavior requires it:

- #281 Lamb15 restored histories, depleted aliases, mixed treatment, later arrival,
  repeated partial movement and independent-session concurrent arrivals:
  InventoryDestinationAliasTests / InventoryMovementDestinationTests.
- Consumed +2, exact downward correction, ordinary +4, location/presence/treatment
  confirmation and stale/concurrent replay: DepletedReceiptCorrectionTests,
  ReceiptQuantityCorrectionTests, ReceiptCorrectionTreatmentTests, backup198 clone.
- Exact held/partial multi-lot and treatment allocation, sealed placement,
  repeated compensation and unsafe downstream dependencies:
  ReceiptCustodyWorkflowTests / ReceiptCustodyCorrectionTests / HTTP tests.
- Shared stock unknown ancestry, source origin rejection and returns/restorations:
  required governance contracts and existing canonical historical/truck workflows.
- Architecture contracts retain the sole writer and destination delta/audit
  boundary; added source checks anchor positive correction and cohort reversal.
  Runtime tests prove behavior; source string checks are not presented as proof.
- Strict inventory/treatment readiness code is unchanged. No release PASS is
  inferred from successful incoming commands into known mismatches.

Existing #271 migrations remain:
`20261007212154_ReceiptHeldTransferCustody`,
`20261007235352_ReceiptCustodyCompensations`,
`20261007235822_ReceiptCustodyGuardrails`.
They predate but must coexist with applied CohortKey migration
`20261009150448_IsolatedInventoryMovementCohorts`. Use the reviewed bounded
missing-migration sequence; do not blindly replay the provider's old history.
The disposable restore fixture verifies original columns/data unchanged and new
custody tables empty before rehearsal.

**Rollback release blocker:** after the first acknowledgement, prior main is not
a safe rollback binary. It lacks compensation-aware readers/writers, and the
database fence rejects its physical writes without
`cropqc.receipt_custody_writer=compensations-v1`. Additive columns alone do not
make it compatible. Freeze and rehearse a compensation-aware rollback application
against committed ack/place/reverse/re-place data before release. Destructive
Down, disabled fences, or an older backup over new business activity are not
rollback options. No compatible production rollback artifact is certified here.

No fresh production backup was triggered or required for this local/read-only
task. Backup198 is development rehearsal evidence only. A later authorized
release still needs a fresh independently verified backup, exact frozen runtime,
bounded migration preflight, strict readiness, compatible rollback, affected
authenticated-route checks and independent production verification.
No merge, ready-state change, deployment, migration, production correction or
administrative reconstruction is authorized or performed by this review.


## Final local validation for this revision

- Solution restore/build: PASS, zero build errors. Existing ImageSharp package
  advisories and unrelated nullable/analyzer warnings remain; no dependency change.
- PostgreSQL 18.4 / Linux .NET 9 affected and mandatory contract selection:
  160 unique cases passed across final runs, zero skips. The broad run passed
  158/160; the two new typed-addition assertions incorrectly pinned a pre-normalized
  segment ID. After changing the assertions to verify the actual recorded current
  destination, both cases passed. No application change followed the broad run.
  Artifacts: `local-test-runtime/receiving-corrections/results/systemic-final.trx`
  and `typed-final.trx` in the parent workspace (ignored local evidence).
- The selection includes restored backup198 scenarios, #281 destination aliases,
  custody/correction HTTP paths, concurrency, exact treatment and conservation,
  writer architecture/hash contracts and strict readiness contracts. This is
  change-scoped coverage, not a claim that the full application suite ran.
- Governance metadata: PASS, 20 rules; Node governance tooling: 29 passed.
  Mandatory traceability adds the three new/expanded methods, removes none.
- EF model consistency: PASS, no pending model changes. Disposable post-CohortKey
  restore migration compatibility passed in the affected tests; this is not a
  production migration or a certified rollback rehearsal.
- Changed-file formatting and `git diff --check`: PASS. No WinForms/MSI changes.
- Final remote SHA and GitHub Actions results are recorded in the PR after push;
  checks at the starting head are not presented as checks of this revision.

No production data, configuration, schema or backup was changed. Live evidence
was obtained only by SELECT. The backup198 census ran in an explicit read-only
transaction on an isolated restored database. No production correction or
historical reconstruction is proposed for automatic execution.

Subsequent rollback preparation stopped at a newly reproduced mixed-treatment
transit proof failure. See [the NO-GO rollback report](pr271-compensation-rollback.md).
No compatible post-custody rollback is certified by that work.
