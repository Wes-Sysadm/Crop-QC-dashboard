# Inventory identity and provenance audit

This audit documents the pre-fix architecture reviewed for the systemic inventory identity correction work. It deliberately separates current inventory identity from historical provenance.

## Canonical concepts

- **Current inventory identity** is the ledger identity for a physical position: crop year, grower lot, fruit profile, normalized grower/lot text, variety, production/organic state, inventory status, facility, room, and treatment signature where treatment allocation matters.
- **Historical provenance** is the immutable chain of receipt, source adjustment, movement parent, treatment segment/movement, and audit records that explains how bins reached the current position.
- `FruitProfileId` is authoritative for variety, production type, and organic status. Persisted snapshots must agree with the referenced profile.
- `GrowerLotId` is not sufficient by itself. A grower lot may legitimately contain multiple fruit profiles.
- `ReceiptId` is provenance. It is neither a complete current-location key nor a safe current-identity resolver.

## Workflow map before the fix

| Workflow | Source of current identity | Source of provenance | Behavior after a correction before this fix | Risk |
|---|---|---|---|---|
| Receiving | Submitted Receipt and FruitProfile | Receipt | A later Receipt edit changes the receipt, but there is no durable obsolete-to-canonical mapping | Obsolete identity can be received again |
| Receipt quantity override | Adjustments filtered by `ReceiptId` with Receipt fallback | Receipt-linked adjustments | Transfers with null `ReceiptId` disappear from the reconstructed state | Wrong location or ambiguous quantity correction |
| Receipt identity override | Adjustments filtered by `ReceiptId`; target room taken from edit form/Receipt | Receipt and override audit | Reclassification can be posted in the original room after fruit moved | Confirmed TR508901 defect |
| Receipt void | Adjustments filtered by `ReceiptId` | Receipt and override audit | Moved inventory can be missed | Can void the wrong attributable balance |
| Manual True-Up from Receipt | Receipt room and Receipt identity | Receipt | Uses receipt-local identity/location | Can write to stale room/identity |
| Current Room Inventory / Room Detail / selection lists | Aggregated RoomInventoryAdjustment ledger identity | Latest adjustment plus optional Receipt/source parent | Correct if compensating rows exist | Safe current-state source; legacy text inference remains bounded |
| Internal Room Transfer | Current ledger snapshot, then optional Receipt/FruitProfile fallbacks | RoomTransfer, adjustment parent, treatment segment Receipt | Usually retains ledger identity; fallback can reintroduce Receipt metadata when snapshot fields are incomplete | Unsafe fallback; reversal replays historical identity |
| Inter-Crew dispatch/receive | Current ledger snapshot copied into transfer snapshot | Source adjustment and treatment movements | Dispatch is current-state based; receive replays transfer snapshot | Safe for post-correction dispatch; pre-correction reversal/receive requires guard |
| Outside Warehouse transfer | Current ledger snapshot copied into transfer snapshot | Source adjustment and treatment movement | New transfer is current-state based | Reversal replays historical identity |
| Bins Run / Actual Run | Current ledger snapshot and exact source adjustment | BinsRunEntry source adjustment and treatment movement | New run is current-state based | Reversal/revision replays historical identity |
| Room inventory loss | Current ledger snapshot | Optional reviewed Receipt, loss parent, treatment movement | New loss is current-state based | Reversal replays historical identity |
| Processor Shipment | Current ledger snapshot | Source adjustment, shipment line, treatment movement | New shipment is current-state based | Reversal replays historical identity |
| Treatment application | Current/as-of ledger snapshot | Treatment application and segment application link | Current-room application is ledger-based | Receipt-level treatment reconstruction uses receipt-local adjustments |
| Treatment movement | Selected current ledger snapshot and exact active treatment segment | Segment Receipt and immutable movement chain | No identity-reclassification movement existed | Active treatment segment can remain obsolete |
| Run Planner / reporting / QC attribution | Ledger snapshots, immutable reporting snapshots, Receipt/QC provenance | Source adjustment, Receipt, reporting snapshots | Historical reporting remains historical | Presentation may need canonical resolution without rewriting snapshots |
| Inventory reconciliation/readiness | Ledger identities and parent invariants | Adjustment parents | Could detect structural parents but not durable identity supersession | Obsolete identity recreation was invisible |

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

### Unsafe current-identity fallbacks to fix

- `ReceiptInventoryOverrideService.GetInventoryStateAsync`: `RoomInventoryAdjustments.Where(x => x.ReceiptId == receipt.Id)`.
- `ReceiptInventoryOverrideService.GetOperationalCountsAsync`: Receipt-linked Bins Run/Actual Run/transfer counts.
- `ReceiptInventoryOverrideService.AddReclassificationAdjustments`: every target position uses the Receipt edit form's warehouse/room.
- `DashboardDataService` Receipt manual true-up and legacy depletion paths that reconstruct identity/location directly from Receipt.
- `DashboardDataService.CreateRoomTransferAsync`: `sourceLot` fields fall back to `sourceReceipt` for grower lot and fruit profile.
- Receipt-level treatment application reconstruction based only on Receipt-linked adjustments.
- Transfer, Bins Run/Actual Run, loss, Inter-Crew, Outside Warehouse, and Processor Shipment reversal paths that replay the historical parent identity without checking supersession.
- Any new Receipt write that accepts a superseded source identity.

### Bounded legacy fallbacks retained

- Ledger normalization that fills missing fields for historical rows from their directly linked Receipt or BinsRun source adjustment.
- The unique matching Receipt heuristic used only to recover a display grower number for legacy rows. It must not choose a FruitProfile or drive a mutating workflow.
- Historical reporting snapshots remain immutable; canonical presentation may resolve through the correction map without updating historical rows.

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

