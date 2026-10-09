# Architectural decision register

Register version: 1.1.0. ADR-001–006 retain their historical approval records. ADR-007 proposes the explicit owner-requested approval workflow, 2026-10-09 America/Los_Angeles; it is not effective until adopted under current safeguards. No independent approval or release is claimed by this branch.

## Decision taxonomy

- **Established business fact**: settled policy; implement and verify, do not ask the owner to rediscover it.
- **Technical implementation detail**: redesignable while preserving policy and evidence.
- **Genuine missing evidence**: name the missing/contradictory records and the specific unsupported operation.
- **New policy question**: owner decision required, distinct from a code limitation.

Never request external counts/paperwork by default when recorded history already answers the question. Where receipt ancestry or treatment remains unproven, isolate that limitation from proven aggregate quantity. Dates in historical reports (including UTC dates one day ahead of local time) are evidence timestamps, not fresh production observations.

| Decision | Business question / approved answer | Rationale / affected workflows | Rules | Approval / supersedes |
|---|---|---|---|---|
| ADR-001 | What creates quantity? Existing authorized origin + ledger/custody evidence, not projections. | Avoid duplicate materialization; all inventory producers/readers. | INV-001, INV-002, INV-003, INV-004 | Established policy reaffirmed by owner request, 2026-10-08; consolidation pending PR approval; supersedes obsolete receipt-only current-state prose in architecture.md, not historical records. |
| ADR-002 | Does shared quantity require exact receipt ancestry? Only receipt-specific actions require exact ancestry; other actions still require their own quantity/treatment proof. | Shared pools cannot be fabricated into receipt allocations; movement, packing, receipt correction. | INV-005, INV-006, ROOM-001 | Established policy reaffirmed by owner request, 2026-10-08; consolidation pending PR approval; no prior decision superseded. |
| ADR-003 | Does treatment remain in a room? No; it follows fruit present at application time. | Prevent later arrivals/corrections inheriting treatment; receiving/movement/packing/reversal. | TRT-001, TRT-002, REC-001 | Established policy reaffirmed by owner request, 2026-10-08; consolidation pending PR approval; no prior decision superseded. |
| ADR-004 | Can 49 received settle 50 dispatched? No; retain the unresolved one and distinguish held/placed. | Conservation and explicit compensation; partial receipts and transfers. | REC-002, MOV-001, INV-002 | Established policy reaffirmed by owner request, 2026-10-08; consolidation pending PR approval; #271 implementation pending; no previous record rewritten. |
| ADR-005 | Can correction/rollback erase history? No; use reviewed compensation/supersession and data-compatible recovery. | Auditability and current-custody safety; all corrections/releases. | AUD-001, REC-001, OPS-001 | Established policy reaffirmed by owner request, 2026-10-08; consolidation pending PR approval; no prior decision superseded. |
| ADR-006 | Can code/tests, ordinary task overrides or local knowledge redefine foundational rules? No; GitHub reviewed main distributes the approved rules, and genuine policy changes require explicit documented business-owner approval of old/new behavior and affected workflows, versioned decisions and tests. Ordinary task exceptions affect workflow preferences only and never silently waive policy or production safeguards. | Prevent drift; all tasks and reviews. | GOV-001 | Owner request "Finalize PR #276 Governance", item 3, 2026-10-08, explicitly clarifies the task-override boundary; consolidation pending PR approval; no prior decision superseded. |
| ADR-007 | Does explicit owner release approval need a second human or duplicate GitHub action? No. Once effective, one auditable owner instruction authorizes the identified PR's normal merge-and-release lifecycle, subject to every technical safeguard and any explicit extra review condition. | Avoid duplicate approval while preserving code review, independent technical verification, exact scope, revocation and fail-closed release gates. | GOV-001, OPS-001, AUD-001 | Owner Wes, "Crop QC Dashboard — Single Owner Release Authorization", 2026-10-09, in task 01a117ce-8381-7602-b396-3c44cc35402e. Requested policy approved for proposal; adoption/merge not authorized by this task. Supersedes CHANGE_PROCEDURE's blanket independent-human requirement and the proposed independent/code-owner settings only after effective adoption; preserves ADR-006's foundational-policy boundary. See [record and transition](RELEASE_AUTHORIZATION.md). |

Implementation detail examples: isolation/lock strategy, evidence batching and index shapes. They must continue satisfying their existing technical contracts until a reviewed compatible replacement is proven.

Missing evidence examples: absent movement link or contradictory treatment membership; a changed receipt reason is not its replacement. New-policy examples: new room capacity/commodity segregation policy, not an excuse to repeat settled conservation decisions.

Add decisions with ID, question, explicit answer, rationale, workflows, date/approval reference, rule IDs and superseded decision IDs. Mark proposed decisions as proposed until human approval; do not self-approve a foundational change.

## Supporting evidence and applicable components

| Decision | Supporting repository evidence | Components |
|---|---|---|
| ADR-001 | [Phase 1](../inventory-architecture/phase1-read-contract.md), [Phase 2](../inventory-architecture/phase2-command-engine.md) | InventoryEvidenceLoader, RoomInventoryLedgerQueryService, InventoryCustodyEvidence, InventoryCommandExecutor |
| ADR-002 | [Receipt correction proof](../receipt-bin-count-correction.md), [shared lineage](../proven-lineage-eligibility.md) | InventoryAvailabilityResolver, InventoryReceiptAvailability, movement/packing selectors |
| ADR-003 | [Phase 3](../inventory-architecture/phase3-workflow-cutover.md), [outstanding treatment gap](OUTSTANDING_PRS.md) | RoomTreatmentService, canonical receiving/corrections, treatment movements |
| ADR-004 | [Conservation gates](../receipt-inventory-conservation-gates.md), [PR 271 review](OUTSTANDING_PRS.md) | InterCrewTransfer, TruckReceiptReconciliation, receipt custody (draft implementation) |
| ADR-005 | [Historical integrity](../change-scoped-testing-standard.md), [release policy](../overnight-release-standard.md) | Corrections, reversals, audit/journal, release and rollback procedures |
| ADR-006 | [Root instructions](../../AGENTS.md), [change procedure](CHANGE_PROCEDURE.md), [Windows setup](WINDOWS_SETUP.md) | All Codex sessions, governance checks, Sync-CropQcKnowledge.ps1 |

The original [technology decisions](../decisions.md) remain authoritative for their
compatible scope. That historical file contains two entries labelled ADR-0006;
refer to their titles (Admin Cleanup and Downloads, or Offline QC Station Sync)
when citing them. They are not silently renumbered or superseded. The three-digit
IDs in this register are distinct. Original approval dates were not recorded in
that file; do not invent them. This register records the owner's present
reaffirmation, not a retrospective claim of approval for an open PR.
