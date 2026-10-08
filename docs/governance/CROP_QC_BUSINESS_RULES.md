# Crop QC business rules

Specification version: **1.0.0**. Adopted for review from the owner's engineering-constitution request, 2026-10-07 (America/Los_Angeles). Merge approval is separate. IDs are permanent; changes follow [the change procedure](CHANGE_PROCEDURE.md). [Traceability](traceability.json) records implementation, executable evidence and gaps, not a claim of universal certification.

This is the single normative business-rule catalog. Existing inventory phase documents remain detailed technical contracts and historical evidence, not competing business-policy owners. Their dated implementation/release status is not current production status. A more specific compatible constraint still applies; contradictions require resolution, not a silent override.

## INV-001 — Inventory origin

Every physical bin entering tracked inventory needs a recorded, authorized origin: authoritative receipt, transfer custody, or an explicitly supported opening balance/addition. Internal transfers relocate existing bins; projections cannot originate stock. Supported manual additions must record their authorized quantity and unknown provenance/treatment where applicable, never invent a receipt.

## INV-002 — Authoritative containers and custody

Ledger, committed transaction and custody evidence determine quantity and location. Account for each quantity exactly once across room-held, in-transit, receipt-held, unresolved, consumed or explicitly disposed states. A transfer evidence receipt is not a second ReceiptAdd. Room deductions into outside/processor/inter-crew custody are not automatically physical disappearance.

## INV-003 — Physical conservation

Debits reconcile to credits, held/transit quantities, or legitimate recorded exits. Corrections need authoritative compensating evidence. No invented bins, silent loss, duplicate deduction or unexplained negative availability. Preserve a negative legacy balance as a blocked diagnostic, not a clamped or invented physical zero. Receiving reconciliation is per current-crop active non-test Truck receipt; opposite discrepancies cannot cancel into PASS. See the detailed [accounting procedure](../receipt-inventory-conservation-gates.md).

## INV-004 — Derived projections

Room projections and treatment segments represent authoritative inventory; they are not independent physical sources. Excess/stale projections never increase available stock. Reconstruct only from proven recorded events, retaining supersession evidence. Never edit authoritative quantities to fit a projection. Read paths cannot normalize or grant repair authorization.

## INV-005 — Recorded replay

Replay receipts, transfers, corrections, reversals, packing and losses through their committed effects. Preserve recorded/commit chronology separately from effective business time. Neither a correction reason nor a receipt label substitutes for transaction evidence. Revised receipt metadata alone is not the original arrival.

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
