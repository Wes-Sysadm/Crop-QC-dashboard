# Receiving placement investigation — 2026-10-07

## A. Executive summary

**Partial investigation and bounded application repairs; the system-wide definition of done is not met.** Two reproduced defects can block otherwise valid receipts and room transfers: incorrect treatment timing for exact legacy receipts, and failed cleanup masking commit-time serialization errors. The repairs prove each exact legacy receipt's arrival and preserve PostgreSQL retry handling. They do not bypass an evidence blocker, change quantities, or repair production data.

This is **not a confirmed explanation of the TR110059 incident**. Production access is pending the Render connector's required workspace confirmation. Separate, higher-risk gaps remain in partial receiving, legacy variance handling, positive receipt correction, and placement policy. Those are identified below, not represented as fixed by this change.

Repository baseline and current fetched `origin/main`: `43abc4e9f22970776145f178100a2a7634426841` (PR #266). Branch: `codex/receiving-placement-integrity`. No merge, deployment, production configuration change, backup operation, or production data write has been performed.

## B. TR110059 investigation and evidence

The operator report supplies receipt TR110059, lot 1033, FUJI, 40 bins, failed destination LAMBCA15, and successful workaround destination EVAN - BKT. An administrative attempt reportedly failed too. These are user-reported facts, not independently queried database evidence.

The screenshot and its exact error are unavailable. Render `list_services` and `list_postgres_instances` returned `no workspace selected`. `list_workspaces` returned Wes's workspace. Confirmation was requested through the connector workflow; no workspace was selected and no production SQL or log request was subsequently made.

Consequently all of the following remain unverified: deployed SHA and feature flags; original failure time/request ID/message; whether the failed attempt left records; retries; successful receipt row ID; subsequent transfer or reversal; current room/custody; current treatment history; and backup #185/frozen photo 6997. Absence of queried evidence must not be interpreted as absence of a transfer or partial write.

Read-only GitHub inspection found [PR #267](https://github.com/Wes-Sysadm/Crop-QC-dashboard/pull/267) open at `f22bd5d4b0f9f5a0d050a403ff656eff4525ae09`. Its author reports that #185's photo 6997 failure concerns a deleted photo still included in required remote verification. This is secondary PR evidence, not this investigation's production verification. Its backup changes and test results are not included in this branch and do not establish a current verified backup.

The mandatory remaining read-only investigation is:

1. Identify the web/API services and database, record live deployed commit and deployment timeline, and inspect receiving-time application/request logs.
2. Find every receipt with normalized `CompuTechReceiptId = TR110059`, including deleted/superseded records, and its ordinary/transfer classification, revisions, audits, and command journal.
3. Resolve LAMBCA15 and EVAN - BKT from actual room IDs and aliases; compare warehouse, activity, seal timestamps, capacity, and configuration.
4. Follow receipt-linked ledger entries, lineage movements/segments, transfer parents, inter-company receiving evidence, correction records, and reversals. Do not infer custody from `Receipts.RoomId`, which is historical after movement.
5. Load canonical evidence for both rooms and the exact grower/profile/status identity under one consistent read-only snapshot. Preserve raw projection quantities and blockers alongside authoritative ledger/custody balances.
6. Inspect applicable treatment applications and immutable source snapshots against each arrival's actual/effective timing; compare the failed and successful paths on the deployed code.
7. Inspect backup #185 and any later successful run; verification requires durable package read-back, checksum, archive/manifest/dump validation. A database status alone does not prove backup health.

Do not recreate TR110059, reverse the workaround, or run a repair while collecting this evidence.

## C. Why one destination failed and the other succeeded

**Not verified for production.** A destination-dependent software failure is reproduced locally: an occupied room has 19 treated bins and 7 later untreated bins, the latter with exact ordinary receipt/ledger/projection evidence but no legacy arrival movement. The old resolver uses the oldest room arrival for that later receipt, declares `MixedTreatmentAmbiguity`, and the destination gate blocks another receipt or transfer. An empty destination has no such existing pool and passes.

This demonstrates a failure class consistent with a destination-dependent report. It does not establish that either production room has this shape.

## D. Root causes identified

### Verified application defect addressed here

`InventoryAvailabilityResolver.ValidTreatment` recognizes an exact incoming lineage movement's own arrival time, but lacked equivalent evidence for a legacy receipt with no incoming movement. It fell back to the oldest positive ledger event for the entire identity/room. That incorrectly made an earlier room treatment appear relevant to later fruit.

The before-fix reproduction failed with `MixedTreatmentAmbiguity`; the four ambiguous/invalid-evidence cases still correctly failed closed. The repair adds `ExactLegacyReceiptArrival`: one exact ordinary `ReceiptAdd`, unchanged receipt quantity, one surviving receipt-scoped current projection, and complete, individually parented outgoing movement/ledger evidence for any intervening consumption. No unexplained negative ledger event, transfer-evidence receipt, extra positive correction, ambiguous receipt, or guessed projection quantity is accepted.

### Verified commit-time concurrency defect addressed here

A repeated PostgreSQL receipt race raised `This NpgsqlTransaction has completed; it is no longer usable` from the executor's catch-block rollback. The local server log independently recorded a serialization cancellation during `COMMIT`. The original retry handler was never reached because the transaction had already ended and explicit rollback threw a second exception.

Cleanup now disposes the transaction before classifying the original exception. Npgsql disposal rolls back pending work and safely handles completed transactions ([provider contract](https://www.npgsql.org/doc/api/Npgsql.NpgsqlTransaction.html)). The original SQLSTATE still controls the existing bounded full-intent retry. Deterministic tests reproduce an already-aborted commit, verify all rows are back at their original fingerprint before retry, preserve a nonretryable server error, and simulate a lost response after a real successful commit followed by journal replay. This is a narrow cleanup correction to the existing transaction owner, not a change to isolation or idempotency policy.

### Independent findings not fixed by this PR

* `src/CropQc.Data/Inventory/InventoryCommandReceiptChanges.cs:83`, `ChangeReceiptQuantityAsync`, credits the selected signature and application IDs when quantity increases. `src/CropQc.Web/Services/RoomTreatmentService.cs:2274`, `AddReceiptTrueUpAsync`, similarly copies legacy treatment links. This can extend historical treatment membership without proving additional bins were present at the event.
* `src/CropQc.Data/Inventory/InventoryCommandCustody.cs:155`, `CompleteCustodyAsync`, requires the entire dispatch and complete matching receiving evidence. `src/CropQc.Data/Inventory/InventoryCustodyEvidence.cs:25` treats open inter-crew custody as `InTransit` with `BinsReceived == null`. There is no canonical receipt-held partial-arrival state for 49 received / 1 unresolved.
* `src/CropQc.Web/Services/InterCrewTransferService.cs:270`, legacy `ReceiveAsync`, credits `BinsReceived` and marks mismatches `ReceivedNeedsReview`; `ReviewAsync` at `:311` can settle status without a physical shortage disposition. An overage can be credited beyond dispatch. This is a separate custody-model defect, not evidence of TR110059 corruption.
* `src/CropQc.Data/Entities/MasterDataModels.cs:26` stores `Room.CapacityBins`, but receiving/movement placement checks do not enforce it. There are no per-room commodity, lot/variety, or organic/conventional restriction fields. Segregation requirements cannot be inferred from organic identity alone.
* Canonical room checks use raw `IsSealed`; `src/CropQc.Web/Services/RoomSealingService.cs:230`, `IsEffectivelySealed`, considers effective time. Scheduled seals and historical movement timing remain inconsistent.

## E. Affected workflows and path inventory

| Path | Evidence and current behavior |
|---|---|
| Web ordinary receiving | `DashboardDataService.CreateReceiptAsync` -> `CanonicalReceivingService.ReceiveAsync` -> executor -> `ReceiveStockAsync`; existing destination gate uses the corrected shared resolver. |
| API ordinary receiving | `ReceiptService.CreateAsync` uses the same canonical service, active operator/permission checks, and command journal. |
| Receipt activation | Executor's `ActivateReceiptInventory` uses the same receiving handler and existing-history exclusion. |
| Room/warehouse transfer, partial transfer, treated-fruit movement | Executor resolves source and destination with the same resolver, then `ApplyAsync` writes conserved ledger pairs and selected movements. |
| Receipt location correction | `CorrectReceiptLocationAsync` uses shared source/destination proof. Existing exact receipt ownership and route requirements remain. |
| Cross-company receipt completion/reopen | `TruckReceiptReconciliationService` -> command executor; exact manifest/receipt matching remains mandatory. No new partial-receipt capability is provided. |
| Receipt quantity/identity correction, reversals, losses, dumps, processor/Outside Warehouse movements | Consumers of canonical evidence can benefit from the corrected proof. Their separate operation policies remain in effect. MCP is not identified as a separate physical authority; external callers still must use these application boundaries. |
| Legacy Web receiving/movements | Retain older services and seal/identity/invariant gates. The canonical write guard rejects nonmigrated physical writes once canonical operation history exists, even if configuration is subsequently disabled. This PR does not replace the legacy engine. |
| Legacy API receiving | Existing noncanonical branch saves a receipt and audit without the canonical command contract. Its parity is not certified here. |
| Background/startup operations | Prior architectural registry and runtime guards were inspected. No ordinary background inventory rebalance was invoked. Maintenance entry points are not a production-repair authorization. |

## F. Authoritative inventory compliance

The new proof is read-only. It never credits a ledger, creates a bin, changes custody, normalizes a segment, or edits receipt/treatment/audit history. The existing command executor remains the only canonical physical writer and retains PostgreSQL Serializable transactions, fresh-context retry, operation-key intent comparison, conservation checks, audit/journal writes, and post-write projection checks.

New PostgreSQL tests assert quantities, exact receipt origins, one receipt/movement, current projections, treatment source snapshots, durable replay, full-table rollback fingerprints, and sum of room balances. Unrelated unknown projections remain unchanged. Negative authority and unproven treatment remain blocked, including for an actor assigned the Admin role.

This is aggregate receipt/segment custody proof, not a newly introduced unique physical-bin identifier. No claim of a complete production invariant audit is made.

## G. Treatment lineage compliance

The fixture begins with 19 treated + 7 later untreated bins. Adding/moving 40 yields 66 total: 19 treated and 47 untreated. The original application-source quantity remains 19. Later exact partial moves preserve both treatment classes and total quantity. Incoming canonical receipts retain their own untreated movement and no historical application links.

The helper uses the legacy receipt's ledger arrival only when its surviving quantity is independently defensible. A later applicable room treatment still blocks an untreated projection. Unknown treatment, unassigned consumption, wrong receipt count, and transfer-evidence receipts are rejected. Receipt correction cannot use this helper to disguise an additional positive ledger entry as the original arrival.

The separate positive-correction treatment-copy defect remains a release risk and requires its own regression-led change. Additional quantity needs explicit treatment evidence or an unresolved treatment classification, without silently changing old application sources or declaring it historically treated.

## H. Room eligibility and placement

Current destination activation, warehouse matching, sealing, canonical identity, custody, route, and treatment checks remain. A room's historical treatment alone no longer blocks the newly proven later receipt shape. Unrelated identity discrepancies are not repaired or consumed by a different receipt.

This PR does **not** claim centralized full room eligibility, new capacity enforcement, or organic segregation policy. A follow-up must define which capacity values are enforceable limits versus planning values and represent actual room restrictions, then enforce them at all credit/relocation boundaries with concurrent total-room checks. Blanket organic/conventional mixing bans without configured business rules would invent a restriction.

Blocked canonical receiving, transfer destinations, and receipt-location correction now expose operation reference, destination, blocker codes, and an administrative evidence-review action. These messages cover inventory discrepancies; a comprehensive typed operational/application-error contract remains outside this bounded fix.

## I. Receipt and transfer reconciliation

Ordinary receiving still creates one origin. Transfer-evidence receipts still do not independently credit physical inventory. Exact cross-company completion/reopen safeguards are unchanged. Return-to-source remains a real physical return and cannot serve as bookkeeping for a missing bin.

The requested 50 expected / 49 received / 1 unresolved state is **not implemented**. It requires a distinct receipt-held custody representation with immutable allocation-level received quantities, remaining transit/unresolved quantities, per-receipt idempotency, and explicit later reconciliation/reversal. Completion must be separate from physical partial receipt. Every custody reader, report, selector, review/reopen operation, and legacy route must participate; simply allowing a mismatch in `CompleteCustodyAsync` would be unsafe.

## J. Historical data risks and prior fixes

Reviewed local history includes #251 (`c014528`, current-room/status alias and split-allocation changes), #252 (`6cb17eb`, Truck Receipt reconciliation), #254 (`0f4456e`, schema/index verification), #255 (`3520c4f`, independently proven lineage normalization), #258/#259/#260 (canonical reads/commands/cutover), and subsequent #263/#264 receipt location/quantity corrections. The fetched main also includes #265/#266.

Existing projection normalization remains evidence-based and cannot add ledger authority. This change does not edit its algorithm or historical normalization records. Historical unassigned pools, malformed status keys, negative balances, projection overcounts, ambiguous treatment/receipt ownership, and missing parents remain reviewable discrepancies. The new helper deliberately refuses ambiguous source consumption.

Historical occurrences of correction treatment inheritance or legacy variance require separate record-specific analysis. No affected production record list is yet available, and no automatic repair is proposed.

## K. Implementation and review scope

* `src/CropQc.Shared/Inventory/InventoryAvailabilityResolver.cs:260`, `ValidTreatment`; `:291`, `ExactLegacyReceiptArrival`: exact legacy arrival/consumption timing proof.
* `src/CropQc.Shared/Inventory/CanonicalInventoryMessages.cs:5`, `PlacementBlocker`: contextual inventory-discrepancy error.
* `src/CropQc.Data/Inventory/InventoryCommandReceiving.cs:58`, `ReceiveStockAsync`; `src/CropQc.Data/Inventory/InventoryCommandExecutor.cs:97`, destination loop; `src/CropQc.Data/Inventory/InventoryCommandReceiptLocation.cs:55`, `CorrectReceiptLocationAsync`: shared discrepancy message; existing `IsOperable` gates remain.
* `src/CropQc.Data/Inventory/InventoryCommandExecutor.cs:154`, catch cleanup: dispose before evaluating the original error/retry class.
* `tests/CropQc.Api.Tests/ReceivingPlacementEvidenceTests.cs:8`, `:24`; `tests/CropQc.Api.Tests/ReceivingPlacementWorkflowTests.cs:17`, `:62`, `:100`, `:153`; `tests/CropQc.Api.Tests/InventoryCommitFailureTests.cs:15`, `:48`: pure evidence, disposable PostgreSQL state/race, and commit-failure regressions.
* `docs/inventory-architecture/phase3-reviewed-write-candidates.json`: refresh the three changed command files and read-only resolver's normalized source hashes after review; no new writer, capability assignment, or allowlist exemption.

No schema, migration, WinForms, QC Station, installer, backup, low-level device, email, or production configuration changes. Diff review specifically checked preservation of the canonical write guard, source availability gates, destination evidence gates, treatment application links, immutable histories, and command transaction ownership.

## L. Verification

Final full-suite result: **2,209 passed, 0 failed, 33 skipped, 2,242 total**. The broad run is justified by the shared availability resolver and transaction owner, both used throughout canonical inventory workflows. Restore passed; solution build passed with zero errors; EF reported no pending model changes; scoped whitespace-format verification and `git diff --check` passed. The initial unchanged-code receiving/correction/concurrency baseline passed 14 tests. The new before-fix evidence reproduction was 1 expected failure and 4 passing rejection cases. The repaired evidence/resolver run passed 41 tests. Initial PostgreSQL placement/race expansion passed 14 tests before final administrative-blocker additions.

The first broader run was 2,201 passed, 1 failed, 33 skipped. Its failure was the reviewed-source hash gate. A later focused run caught the commit-time cleanup defect described in D, alongside the resolver's remaining fingerprint update. After correction, 28 placement/commit/concurrency tests passed with zero failures/skips. Skipped broader tests require separately configured restored-production/provider fixtures; they are not passed checks. A subsequent broad run exposed a race-test assumption that the first of two competing receipt numbers must win; the assertion now accepts either winner while still requiring exactly one committed receipt and one conflict. The final rebuilt broad run passed as reported above.

Mandatory scenario coverage must not be overstated: new tests cover 40 FUJI/1033, empty/occupied/multiple-segment/historically treated destinations, unrelated discrepancies, treated and untreated transfers, later partial consumption, duplicate prevention, rollback/retry, concurrent receipts/movement, audit retention, and admin integrity rejection. Existing suites cover normalization, status aliases, ordinary cross-company completion, corrections and reversals. True partial receipts, receipt-held custody, new capacity/segregation restrictions, and repaired positive-correction treatment attribution remain unmet requirements.

## M. Concurrency and transaction verification

New races coordinate two independent PostgreSQL transactions at the executor's `Resolved` stage: two distinct receipts, same receipt number with different operation keys, identical intent, and receipt versus movement. They assert one defensible origin per receipt and conservation across rooms. Existing double-consumer/treatment/move/return/reopen races are part of the broader run.

Tests use a disposable local PostgreSQL 18.6 instance bound to loopback. No tests connect to production. Whole-database row fingerprints verify failure/retry atomicity. They do not certify production isolation, deployment flags, restored production history, partial-receipt races, or concurrent reconciliation of a shortage.

## N. Production repair requirements

None authorized or executed. No record-specific repair can responsibly be specified without production evidence. If needed, a separate plan must enumerate exact record IDs and immutable before fingerprints; ledger/custody/treatment totals; proposed correction/supersession and explicit provenance; expected after-state; audit actor/reason; rollback/compensating action; and read-only verification queries. It requires explicit approval and a fresh verified backup before any mutation.

## O. Remaining risks and unresolved findings

1. TR110059 exact cause, failed-write/duplicate behavior, current custody, and destination comparison remain unverified pending production workspace confirmation.
2. Partial/receipt-held custody and legacy variance conservation require a separately scoped implementation, not a relaxed receipt-match gate.
3. Positive correction treatment attribution remains unsafe for treated selected allocations and needs a separate repair.
4. Full centralized capacity/segregation/availability policy and effective seal-time parity remain incomplete.
5. Strict destination proof can still reject genuinely ambiguous same-identity history; this PR only handles independently proven legacy receipts. It does not make every historical discrepancy safe.
6. Production backup #185, photo 6997, package recoverability, live deployed SHA, and feature activation have not been verified.
7. No production-shaped restore or live UI test was performed. The system-wide definition of done is not satisfied and production must not be declared fixed.

## P. Delivery and next steps

The focused change is prepared as a **draft PR** for review, without merge/deployment authorization. Commit, PR link, GitHub check status, and final `origin/main` comparison are supplied in the delivery record. Local verification is recorded in section L. There is no MSI change or artifact requirement.

Next: confirm the Render workspace; complete the read-only incident/backup evidence capture; compare the actual failure against the reproduced class; finish the independent custody, correction, and policy work in appropriately scoped nonstacked PRs. Production data repairs and deployment require separate explicit authorization and the repository's verified-backup/release gates.
