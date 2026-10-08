# Crop QC business rules

Specification version: **1.0.0**. Established policies reaffirmed by the owner's permanent-knowledge request, 2026-10-08 (America/Los_Angeles). This consolidation awaits PR approval; it does not claim merge or deployment. IDs retain the compatible taxonomy proposed in draft #274 to avoid a competing catalog. Changes follow [the change procedure](CHANGE_PROCEDURE.md). [Traceability](traceability.json) records implementation, executable evidence and gaps, not universal certification.

This is the single normative business-rule catalog. Existing inventory phase documents remain detailed technical contracts and historical evidence, not competing business-policy owners. Their dated implementation/release status is not current production status. A more specific compatible constraint still applies; contradictions require resolution, not a silent override.

**Business rules define the system. Authoritative transactions establish physical truth. Code implements those rules. Tests verify the implementation. Historical projections never redefine authoritative inventory.**

See the [source inventory and precedence](README.md) and [decision register](ARCHITECTURAL_DECISIONS.md).

## INV-001 — Inventory origin

Every physical bin entering tracked inventory needs a recorded, authorized inventory-origin operation: an authoritative receipt or explicitly supported opening balance/addition. A transferred bin retains its prior physical origin; transfer custody proves its location and does not originate more stock. Projections cannot originate stock. Supported manual additions must record their authorized quantity and unknown provenance/treatment where applicable, never invent a receipt.

## INV-002 — Authoritative containers and custody

Ledger, committed transaction and custody evidence determine quantity and location. Account for each quantity exactly once across room-held, in-transit, receipt-held, unresolved, consumed or explicitly disposed states. A transfer evidence receipt is not a second ReceiptAdd. Room deductions into outside/processor/inter-crew custody are not automatically physical disappearance.

## INV-003 — Physical conservation

Debits reconcile to credits, held/transit quantities, or legitimate recorded exits. Corrections need authoritative compensating evidence. No invented bins, silent loss, duplicate deduction or unexplained negative availability. Preserve a negative legacy balance as a blocked diagnostic, not a clamped or invented physical zero. Receiving reconciliation is per current-crop active non-test Truck receipt; opposite discrepancies cannot cancel into PASS. See the detailed [accounting procedure](../receipt-inventory-conservation-gates.md).

## INV-004 — Derived projections

Room projections and treatment segments represent authoritative inventory; they are not independent physical sources. Excess/stale projections never increase available stock. Reconstruct only from proven recorded events, retaining supersession evidence. Never edit authoritative quantities to fit a projection. Read paths cannot normalize or grant repair authorization.

## INV-005 — Recorded replay

Begin investigations with authoritative system transactions. Reconstruct complete recorded receipts, transfers, corrections, reversals, packing and losses before suggesting physical counts or outside paperwork. Preserve recorded/commit chronology separately from effective business time. Neither a correction reason nor a receipt label substitutes for transaction evidence. Revised receipt metadata alone is not the original arrival. When proof is insufficient, identify the exact missing or contradictory event and affected operation; a technical replay limitation does not change policy.

## INV-006 — Shared pools versus receipt ancestry

Proven shared quantity does not imply exact surviving receipt ancestry. Preserve both confidence levels. Do not invent receipt allocations or reject a valid shared-pool operation solely for unavailable exact ancestry. Receipt-scoped operations still require exact provenance; treatment-specific operations require their treatment proof. Name the unsupported operation and missing evidence, not the entire quantity as unknown.

## TRT-001 — Point-in-time treatment

Only fruit present at the recorded application is treated. Treatment does not linger in rooms; later arrivals and newly added correction bins do not inherit it. History follows the affected allocation. Unknown never silently becomes untreated.

## TRT-002 — Treatment conservation

Splits, merges, moves, custody changes, corrections and consumption preserve applicable application/movement lineage. No treatment leaks between unrelated branches. Balanced quantities alone do not prove treatment membership. Reversal preserves original application/source/movement evidence.

## REC-001 — Receipt correction history

Corrections, voids and reversals preserve original evidence, actors and timestamps through authorized audited operations. Quantity changes use the established correction mechanism, not ordinary metadata edits; no metadata-only correction while current relevant custody remains without proof. Corrections must not attach historical treatment to new bins. Exact operator permissions remain workflow-specific; the general rule does not grant a role additional authority.

## REC-002 — Partial receipt custody

Dispatched, acknowledged, held, placed and unresolved quantities stay distinct. A 50-bin load acknowledged as 49 retains one unresolved bin. No manufactured stock, discarded shortage or falsely completed reconciliation. Compensation changes the recorded custody state, not an assumed physical return. **Main does not yet implement all partial-acknowledgement transitions; draft #271 is tracked as a gap, not silently incorporated here.**

## MOV-001 — Transfer integrity

Preserve source/destination custody, company boundaries, supported exact reconciliation and treatment lineage. Transfers never create bins. Retries bind the same complete intent, conflicting keys fail, and concurrent operations commit a valid serial order or roll back. Internal movement nets to zero globally. Existing route restrictions remain enforced.

## ROOM-001 — Consistent room eligibility

Valid fruit can enter eligible open rooms under the same supported evidence/eligibility contract. Enforce actual permission, facility, seal and operation constraints. An unrelated historical projection defect must not arbitrarily block a valid operation whose required authority/treatment evidence is sufficient. This does not waive strict release readiness or authorize bypassing an unresolved operation-specific proof.

## AUD-001 — Historical evidence

Retain original receipts, transfers, ledger entries, treatment applications, corrections, movement links, actors, timestamps and audits. Use documented compensation or audited supersession, not erasure to make balances appear correct. Preserve immutable source evidence and record new before/after effects atomically with the operation. Separately authorized retention/purge procedures are not ordinary corrections.

## OPS-001 — Production safety

Authorization defines scope, never a waiver of verified backup, strict readiness, schema/rollback safeguards, canonical writer guards or immutable evidence. No unauthorized repair or synthetic production mutation. Execute the existing [release standard](../overnight-release-standard.md) and root backup procedure. After custody/schema use, rollback must be compatible with committed data, including compensation-aware readers/writers where required; preserving columns alone is insufficient. Do not restore old backups over newer business activity. A green CI check is not release approval.

## QC-001 — QC sampling

Preserve partial fruit saves and all supported fruit counts, including 10/25/50-fruit workflows. QC observation does not create physical inventory. Receipt-backed QC follows supported canonical fruit identity without recreation; preserve sample ownership and original readings. Do not invent readiness or email requirements for optional photos.

## FIELD-001 — Field Sample separation

Field Samples are receiptless preharvest observations, not Receiving inventory, Bins Run, room-card stock or Receiving email. Preserve dedicated list/create/edit/detail workflows, orchard/grower and canonical block, 30-day same-block size/starch/weight/pressure trends, partial supported fruit counts, manual entry, browser scale and station FTA pressure capture, and confirmation of suggested fuzzy matches. Reuse existing photo storage/audit patterns.

## DEV-001 — Station and device boundaries

Preserve verified station credentials, enrollment, pressure-only writes, unit conversion, FTA/scale protocols and installer/config separation. A station reading must not overwrite unrelated fruit/pressure fields. Secrets never enter installers, logs or source. Hardware behavior requires actual onsite proof, not a mock test claim. See [FTA contract](../qc-station-fta.md); its original POC status is historical.

## SYNC-001 — Stale-write and retry safety

Browser/device retries and failures retain entered readings and reject stale overwrites of newer readings. Persist originating station where supported. The [offline design](../offline-sync-design.md) is future work, not evidence of a deployed durable offline queue: pending offline work must not be erased before acknowledged sync if that design is implemented. Never sync test data into production.

## PHOTO-001 — Storage and backup history

Use existing file-storage abstraction and stable metadata references; do not introduce a parallel storage implementation. Preserve approved original/presentation/deletion/audit behavior. The [active-photo backup contract](../active-photo-backup-contract.md) freezes active remote references while retaining deleted database history; a missing required active object fails verification. Do not restore removed blobs to satisfy obsolete manifest assumptions.

## GOV-001 — Rule ownership and verification

Every task identifies relevant rules, evidence, tests, gaps and production implications. Policy changes require explicit human approval and versioned decisions/tests. Structural checks are not runtime proof; concurrency requires real independent PostgreSQL sessions. Test the affected blast radius under [change-scoped testing](../change-scoped-testing-standard.md), not unrelated domains. No test omission, early return, skip or historical run may be reported as executed current coverage.

GitHub's approved main is the shared knowledge authority. Every clone fetches and compares it before relying on local instructions. Identify repository, branch, local/fetched commits and specification version; report discrepancies and integrate safely. Never let local/global configuration or prior conversations silently supersede these rules. See [Windows synchronization](WINDOWS_SETUP.md).

### Foundational requirement index

These 27 stable requirement numbers preserve the owner's complete approved intent.
Rule IDs above are the canonical references used in code, decisions and tests.
The [test matrix](traceability.json) maps each rule to exact tests and explicit gaps;
this index does not imply every implementation path already satisfies the policy.

| Requirement | Established behavior | Rule IDs |
|---|---|---|
| 1 | Every physical bin originates through an authorized recorded inventory-origin operation. | INV-001 |
| 2 | Transfers move existing bins and do not create inventory. | MOV-001, INV-001 |
| 3 | Authoritative transactions, ledgers and custody records establish actual inventory. | INV-002 |
| 4 | Every bin must have defensible custody. | INV-002 |
| 5 | Inventory cannot be duplicated or silently lost. | INV-003 |
| 6 | Room inventory projections are derived from authoritative evidence. | INV-004 |
| 7 | Reconstruct incorrect projections instead of changing authoritative inventory to match. | INV-004 |
| 8 | Exact receipt ancestry and authoritative physical quantity are separate concepts. | INV-006 |
| 9 | Preserve ambiguous exact ancestry when shared inventory is proven. | INV-006 |
| 10 | Do not fabricate receipt allocations. | INV-006 |
| 11 | Do not unnecessarily block an otherwise proven shared pool. | INV-006, ROOM-001 |
| 12 | Operations genuinely requiring exact provenance must prove it. | INV-006 |
| 13 | Treatments apply only to fruit physically present at treatment time. | TRT-001 |
| 14 | Treatments do not linger in rooms. | TRT-001 |
| 15 | Later arrivals do not inherit earlier treatments. | TRT-001 |
| 16 | Treatment history follows affected fruit through movements. | TRT-002 |
| 17 | Unknown treatment history must not automatically become untreated. | TRT-001 |
| 18 | Receipt corrections preserve original records and audit history. | REC-001, AUD-001 |
| 19 | Corrections reconcile through authorized inventory operations. | REC-001, INV-003 |
| 20 | Partial receipts preserve acknowledged, placed and unresolved quantities. | REC-002 |
| 21 | Unresolved bins cannot be silently discarded or manufactured. | REC-002, INV-003 |
| 22 | Reversals and compensations preserve current custody and historical evidence. | REC-001, REC-002, AUD-001 |
| 23 | Begin investigations with authoritative system transactions. | INV-005 |
| 24 | Reconstruct complete recorded histories before suggesting counts or outside paperwork. | INV-005 |
| 25 | Distinguish quantity proof from exact receipt provenance. | INV-006 |
| 26 | Identify the exact missing or contradictory event when evidence is insufficient. | INV-005 |
| 27 | Historical projections never override authoritative physical inventory. | INV-004 |
