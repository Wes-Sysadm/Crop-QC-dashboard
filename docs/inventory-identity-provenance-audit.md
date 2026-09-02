# Inventory identity and provenance audit

This audit records the final architecture for the systemic inventory identity correction work. It deliberately separates current inventory identity from historical provenance.

## Canonical concepts

- **Current inventory identity** is the ledger identity for a physical position: crop year, grower lot, fruit profile, normalized grower/lot text, variety, production/organic state, inventory status, facility, room, and treatment signature where treatment allocation matters.
- **Historical provenance** is the immutable chain of receipt, source adjustment, movement parent, treatment segment/movement, and audit records that explains how bins reached the current position.
- `FruitProfileId` is authoritative for variety, production type, and organic status. Persisted snapshots must agree with the referenced profile.
- `GrowerLotId` is not sufficient by itself. A grower lot may legitimately contain multiple fruit profiles.
- `ReceiptId` is provenance. It is neither a complete current-location key nor a safe current-identity resolver.

## Final workflow classification

| Workflow | Source of current identity | Source of provenance | Post-correction behavior | Final classification |
|---|---|---|---|---|
| Receiving | Submitted Receipt and FruitProfile | Receipt | Superseded identities are rejected before mutation | **FIXED — explicit canonical write guard** |
| Receipt quantity override | Exact Receipt-attributable treatment/source allocation; legacy Receipt rows only before movement | Receipt-linked adjustments and treatment allocation | Nullable-`ReceiptId` movement is rejected unless exact Receipt allocation survives | **FAILS CLOSED — no exact Receipt allocation** |
| Receipt identity override | Every authoritative current source position | Receipt, override audit, durable correction parent | Reclassifies current positions in place and preserves historical rows | **FIXED — durable identity correction** |
| Receipt void | Exact Receipt-attributable treatment/source allocation | Receipt and override audit | Rejects stale Receipt balance inference | **FAILS CLOSED — no exact Receipt allocation** |
| Receipt room correction | Exact Receipt position before movement; Receipt receiving provenance after movement | Receipt and location override audit | Moves exact fruit only before movement; never teleports fruit after movement | **FIXED — distinct location correction** |
| Manual True-Up from Receipt | Unique canonical current ledger snapshot | Receipt is provenance only | Superseded, ambiguous, or incomplete identities are rejected | **FAILS CLOSED — no unique canonical position** |
| Current Room Inventory / Room Detail / selection lists | Aggregated RoomInventoryAdjustment ledger identity | Latest adjustment plus optional Receipt/source parent | Correct if compensating rows exist | Safe current-state source; legacy text inference remains bounded |
| Internal Room Transfer | Complete canonical current ledger snapshot only | RoomTransfer, adjustment parent, treatment segment Receipt | Receipt/FruitProfile mutation fallback removed; superseded writes rejected centrally | **FIXED — canonical snapshot plus invariant** |
| Inter-Crew dispatch/receive | Current ledger snapshot; final canonical chain on receive | Immutable transfer snapshot and treatment movements | Current queue/receive show/use final canonical identity; history retains dispatch identity | **FIXED — canonical custody projection** |
| Outside Warehouse transfer | Current ledger snapshot copied into transfer snapshot | Source adjustment and treatment movement | Terminal external custody remains immutable; superseded reversal is rejected | **BOUNDED LEGACY DISPLAY ONLY — terminal provenance cannot mutate** |
| Bins Run / Actual Run | Current ledger snapshot and exact source adjustment | BinsRunEntry source adjustment and treatment movement | New writes are centrally guarded; obsolete historical replay is rejected | **FIXED — canonical write invariant** |
| Room inventory loss | Current ledger snapshot | Optional reviewed Receipt, loss parent, treatment movement | New writes are centrally guarded; obsolete reversals are rejected | **FIXED — canonical write invariant** |
| Processor Shipment | Current ledger snapshot | Source adjustment, shipment line, treatment movement | New writes are centrally guarded; obsolete reversals are rejected | **FIXED — canonical write invariant** |
| Treatment application | Current/as-of ledger snapshot or exact Receipt-attributable active segment | Treatment application and segment application link | Receipt-level application rejects ambiguous moved fruit and obsolete identity | **FAILS CLOSED — no exact current Receipt position** |
| Treatment movement | Selected current ledger snapshot and exact active treatment segment | Segment Receipt and immutable movement chain | Correction creates an explicit conserved treatment reclassification movement | **FIXED — linked treatment correction** |
| Run Planner / reporting / QC attribution | Ledger snapshots, immutable reporting snapshots, Receipt/QC provenance | Source adjustment, Receipt, reporting snapshots | Historical reporting remains historical | Presentation may need canonical resolution without rewriting snapshots |
| Inventory reconciliation/readiness | Ledger identities, durable correction map, and parent invariants | Adjustment parents | Detects positive obsolete balances, chains/conflicts, and post-correction obsolete writes | **FIXED — permanent readiness diagnostic** |

## Code-search classification

### Safe provenance use

- Receipt-to-QC/photo/email queries using `ReceiptId` only to find records belonging to a Receipt.
- Audit/history queries using `ReceiptId` to show immutable source evidence.
- Bins Run, Processor Shipment, Outside Warehouse, and Inter-Crew records retaining `ReceiptId` or `SourceInventoryAdjustmentId` as provenance while copying a complete current ledger snapshot.
- Treatment segments retaining `ReceiptId` to preserve receipt allocation inside a combined ledger identity.

### Safe current-state sources

- `RoomInventoryLedgerQueryService.GetSnapshotsAsync` aggregation of RoomInventoryAdjustments.
- Inventory selection in Bins Run, Actual Run, losses, Inter-Crew, Outside Warehouse, Processor Shipments, and room transfers when all identity fields come from the selected ledger snapshot.
- Treatment selection by exact current segment and treatment signature.

### Final mutation-boundary classifications

- **FAILS CLOSED — Receipt quantity and void:** exact Receipt allocation is required once operational movement exists; structurally valid same-identity transfers are not Receipt attribution.
- **FIXED — room-only Receipt correction:** before movement it uses an exact conserved room pair; after movement it changes receiving provenance without teleporting current fruit and never creates `InventoryIdentityCorrection`.
- **FIXED — Receipt identity reclassification:** follows every authoritative current source position and cannot also change receiving room in the same operation.
- **FAILS CLOSED — Manual True-Up and bounded legacy depletion:** reject superseded, incomplete, or ambiguous current identity before mutation.
- **FIXED — Internal Room Transfer:** no `sourceReceipt` or text-matched FruitProfile mutation fallback remains.
- **FAILS CLOSED — Receipt-level treatment:** requires exact active Receipt treatment allocation and rejects ambiguous post-movement reconstruction.
- **FIXED — all new operational ledger writes:** the centralized pre-commit invariant rejects superseded identity except the correction parent's explicit compensating source removal.
- **FAILS CLOSED — historical reversal/revision:** Transfer, Bins Run/Actual Run, loss, Inter-Crew, Outside Warehouse, and Processor Shipment cannot replay a superseded historical identity.

### Bounded legacy display-only fallbacks retained

- **BOUNDED LEGACY DISPLAY ONLY — ledger normalization:** fills missing display fields for historical rows from a directly linked Receipt or Bins Run source adjustment and is not a mutation source.
- **BOUNDED LEGACY DISPLAY ONLY — matching Receipt heuristic:** recovers only a display grower number and cannot choose a FruitProfile or drive a write.
- **BOUNDED LEGACY DISPLAY ONLY — reporting snapshots:** immutable historical reporting evidence may be canonically labeled without rewriting persisted snapshots.
- **BOUNDED LEGACY DISPLAY ONLY — Outside Warehouse:** terminal external custody is historical provenance, not current room/Inter-Crew queue inventory; any reversal is guarded.

## Provenance sufficiency decision

The existing model already carries durable provenance through:

- Receipt-linked initial adjustments;
- source adjustment IDs on Bins Run, Inter-Crew, Outside Warehouse, and Processor Shipment rows;
- explicit movement parents and invariant operation keys;
- receipt-scoped treatment segments and immutable treatment movements.

A second general-purpose provenance graph is not required for this correction. The missing durable concept is identity supersession. The fix adds an auditable correction map and parent links for compensating ledger/treatment movements. Operations that cannot resolve exact provenance must fail closed rather than infer a Receipt.

## Required systemic changes

1. Add a durable, immutable correction mapping keyed by crop year + source grower lot + source fruit profile, targeting a canonical grower lot + fruit profile.
2. Resolve correction chains deterministically and reject self-maps, cycles, and conflicting active mappings.
3. Derive variety, production type, and organic state from the canonical FruitProfile at every corrected write boundary.
4. Reclassify every positive current room position in place, preserving quantities and locations.
5. Reclassify active treatment segments with compensating movements, preserving treatment applications, signatures, quantities, rooms, and receipt provenance.
6. Block obsolete identity at Receiving and direct POST boundaries.
7. Guard historical reversals: if their stored identity is now superseded, fail closed instead of resurrecting it. Transactions created after correction use canonical identity and reverse normally.
8. Add release-readiness and historical diagnostics for stale-room artifacts, positive obsolete balances, treatment mismatch, conservation, cycles/conflicts, and post-correction obsolete writes.
9. Keep all historical Receipt, transfer, run, treatment movement, and audit rows immutable.

## Partial consumption and historical reporting decision

Identity correction reclassifies only current remaining inventory. Already consumed bins and immutable Bins Run/Actual Run reporting snapshots remain historical evidence. Reporting may display a canonical label through the correction resolver, but the underlying snapshots are not rewritten. A reversal of a pre-correction historical transaction fails closed because replaying it would recreate the obsolete identity.
