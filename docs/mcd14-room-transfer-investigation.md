# McDougall Room 14 inventory transfer investigation

Read-only production investigation: 2026-09-23. Room ID **66**, warehouse ID **3**.
Code base: `90c129f4bca77b6b6df8b259532d2974e79a1e0d`.

## Finding and correct quantity

**798 is the correct recorded current quantity for Bartlett lot 1372, not the
entire room. The room contains 2,520 bins across all lots.** This is ledger
verification, not a physical bin count.

There are no individual bin IDs in this model. These are aggregate quantities
on inventory positions and treatment segments. The four-bin difference is a
software-created excess quantity, not four independently identified bin records.

The reported 802/798 warning also concealed an older 256-bin status-key mismatch:
all active lineage for this lot actually totals **1,058**, including the two
segments the old lookup missed. Do not repair this by simply subtracting four
from segment 160 or increasing inventory to 802.

| Evidence | Recorded quantity / meaning |
|---|---|
| Lot 1372, crop year 2026, Grower Lot 448, Fruit Profile 17 | 1,244 additions minus 446 dispatches = **798** |
| Segment 148, Receipt 720 | 184 untreated bins, key ends in `CONVENTIONAL` |
| Segment 154, Receipt 626 | 72 untreated bins, key ends in `CONVENTIONAL` |
| Segment 160, shared provenance | 802 untreated bins, key ends in an empty status |
| Segment 159, Receipt 774 | Now zero; supplied the four-bin part of dispatch 22 |
| Other room inventory | 1,722 bins; total room quantity **2,520** |

The production check found no starting-inventory baseline in this room and no
treatment application links on segments 148, 154, or 160. Receiving and transfer
records account for the quantities; no evidence supports adding four real bins.

## Root causes and exact records

1. `DispatchAsync` allocates one load across multiple segments. Transfer **22**
   consumed **4** bins from segment **159**, then **62** from segment **160**.
   Each allocation called `MoveCoreAsync`, which called `MaterializeAsync` with
   the original **864-bin** snapshot. After consuming the first four bins, the
   second call interpreted the difference as missing untreated lineage and added
   four bins back before consuming 62. The caller wrote the single 66-bin ledger
   debit only after allocating both segments. The outside-warehouse allocator
   had the same defect.
2. Exact treatment-key matching excluded segments **148** and **154** when the
   current inventory snapshot had a blank status instead of `Conventional`.
   These statuses describe the same ledger position and production type. The
   missing 256 bins were consequently materialized again into shared segment
   160. The materialization path does not create a separate movement row, so the
   timing/quantity reconstruction comes from the code, immutable movements, and
   persisted balances rather than an invented historical movement.

Exact four-bin evidence:

- Dispatch movement **590**: 4 bins, segment **159**, Receipt **774**, transfer **22**.
- Dispatch movement **591**: 62 bins, segment **160**, same transfer.
- Inventory adjustment **3180**: **-66** bins from Room 66 for transfer 22.
- Receive movement **601**: the same 4 bins received into segment **617**, room **4**.
- Receive movement **602**: 62 bins received into segment **618**, room **4**.
- Transfer 22 is `Received`, with 66 loaded and 66 received. Those bins must not
  be recreated in Room 14.

Independent repair calculation for shared segment 160:

`280 + 194 + 64 + 66 - 62 = 542`

The first three additions are movements **96**, **97**, and **182**, from room
transfers **218**, **219**, and **281**. The 66-bin receiving addition is adjustment
**1298**, Receipt **763**. The final 62 is movement 591. Keeping the separate
184- and 72-bin segments gives `542 + 184 + 72 = 798`. Thus the required correction
to segment 160 is `802 - 260 = 542`, with the 260 explained as 256 double-counted
status-alias bins plus four rematerialized dispatch bins.

## Definitions and scope

- **Authoritative inventory**: `RoomInventoryLedgerQueryService` projects signed
  `RoomInventoryAdjustments`, applies existing baseline/receipt-date rules,
  resolves inventory identity, and returns current room-position balances.
- **Explicit bins**: sum of positive `TreatmentLineageSegment.CurrentBins` for
  the selected room and treatment identity. These are stored quantities, not a
  count of bin reference rows or application links.
- **Treatment lineage**: segment treatment state/signature, Receipt provenance,
  application links, and immutable movements linking source/destination segments
  to receiving, transfers, losses, or processing events. Historical Receipt
  location remains distinct from current inventory location.
- **Movement eligibility**: positive authoritative inventory with operable
  canonical identity and reconciled, exact treatment provenance. Write-time
  checks retain authorization, room sealing, destination/crew validation,
  superseded-identity rejection, quantity limits, and inventory invariants.

Blast radius: room-transfer projection and writes, treatment selection and
materialization, inter-crew/outside allocation, and the transfer form. Inventory
ledger arithmetic is unchanged. Existing receiving records, prior movements,
applications, and historical locations must remain intact.

## Implementation

- `RoomTreatmentService.cs`: decrement the working authoritative snapshot after
  each compound allocation, recognize only blank/redundant production-status
  aliases while keeping all other identity fields exact, select the correct
  materialized remainder, and include segment IDs/quantities in review errors.
- `DashboardDataService.cs`: add an all-eligible room transfer using one
  serializable transaction and the existing audited per-segment transfer path.
  A review fingerprint detects changed inventory/eligibility; deterministic
  child operation keys prevent duplicate retry movements. Reuse one source
  projection and update remaining quantities between allocations.
- `DashboardViewModels.cs` and `Views/BinsRun/Index.cshtml`: add the bulk option,
  show current/eligible/excluded totals and exception reasons, and review the
  destination and total before confirmation. Existing individual-position
  transfers remain supported.
- `BulkRoomTransferTests.cs`, `OutsideWarehouseTransferTests.cs`, and
  `BinsRunWorkflowTests.cs`: data-integrity and UI regression coverage.
- `scripts/postgresql/repair-mcd14-lineage-20260923.sql`: guarded, auditable,
  idempotent one-time correction; **not executed against production**.

The implementation performs work per lot/treatment segment, not per bin. An
800-bin segment produces one transfer, paired ledger adjustments, and one
lineage movement. It retains existing bounded per-segment database operations;
it does not claim one SQL statement for an arbitrarily large number of segments.

Unreconciled positions do not block other eligible positions. Where conflicting
quantities cannot be assigned to exact Receipt/treatment provenance, the
affected position remains unavailable rather than guessing which bins/history
to remove. For this incident the proven repair makes all 798 lot-1372 bins
available. Before repair, the corrected lookup exposes **1,058 vs 798** with
segments 148/154/160 identified; it does not silently cap or rewrite them.

## Validation

- `dotnet restore CropQc.sln`: passed.
- `dotnet build CropQc.sln --no-restore`: passed; existing nullable warnings remain.
- Focused test command: **206 passed, 1 skipped, 0 failed**. The filter is
  `BulkRoomTransferTests|RoomTreatmentTrackingTests|OutsideWarehouseTransferTests|CanonicalQcTransferTests|BinsRunWorkflowTests|ReceiptDateBaselineTests|TreatmentAwareTransferTests|TreatmentLineage144`
  (each term is a `FullyQualifiedName~` filter).
- `ROOM_TRANSFER_TEST_POSTGRES` set to a disposable localhost database, then
  `dotnet test tests/CropQc.Api.Tests/CropQc.Api.Tests.csproj --no-build --filter FullyQualifiedName~BulkRoomTransferTests`:
  **9 passed, 0 skipped, 0 failed**, including actual PostgreSQL rollback and repair tests.
- EF `has-pending-model-changes`: none.
- Formatting verification for changed C# files and `git diff --check`: passed.

The focused set covers bulk transfer, lineage tracking, outside/inter-crew
allocation, canonical transfer identity, Bins Run transfer behavior, receipt-date
baseline preservation, and existing lineage regression paths. No unrelated
full-product recertification was performed.

New PostgreSQL tests execute:

1. 800 bins transferred once; source zero, destination 800; retry is a no-op;
   old-source reuse rejected; Receipt, grower, profile, and application links retained.
2. 802 explicit / 798 authoritative stale lineage: other valid inventory moves;
   the unresolved position and its segment quantities/versions remain unchanged.
3. Duplicate explicit provenance: same exclusion/preservation behavior.
4. Repaired status-alias shape: 256 + 542 = 798, both segments transferred once.
5. Legitimate authoritative remainder: four real bins above 798 explicit bins
   can move before the legacy alias segment, then all 802 reconcile at destination.
6. Split dispatch: 4 + 62 consumes 66 and leaves 798, never 802.
7. Depletion and stale preview: no writes on stale submission; refreshed transfer
   moves only 798 and does not resurrect the four removed bins.
8. Failure during the second position rolls back every first-position write.
9. Repair script: changed-evidence refusal, 802 to 542 with audit, unchanged
   ledger/movement history, total 798, and idempotent rerun.

The repair SQL fixture uses session-local evidence tables with production-model
column types. Other PostgreSQL tests use the complete EF-created schema and real
constraints. This is synthetic, disposable PostgreSQL validation, not a restored
production database or a live browser test. Existing environment-gated restored
tests were not activated. No concurrent-user load/stress test was performed.

## Release and operational considerations

No EF migration, schema changes, or MSI is required. A **one-time data repair is
required** for the existing historical overcount. Ship the fixed application and
perform the reviewed repair only after authorization and the repository's fresh,
verified production-backup gate. The repair refuses altered fingerprints instead
of extrapolating the recorded analysis to new data.

The script updates only segment 160's current quantity, concurrency version, and
update timestamp, plus a new audit record containing the complete before/after
segment. It preserves segments 148/154, all movement/application history,
Receipts, and inventory adjustments. It does not move physical inventory.

After deployment and repair, verify the current lot total, room total (which may
have changed with legitimate operations), eligible counts, exception display,
and browser review flow. Perform mutation rehearsal on disposable data. Room
seals, crew restrictions, and real unresolved identity conflicts can still
legitimately prevent a transfer.

Rolling back to the previous application reintroduces both software defects;
suspend the affected movement paths or restore the fix before further use.
Do not reverse the evidence-backed repair or overwrite newer production activity
as a substitute for fixing the application.

Nothing was merged or deployed and production data was not modified during this
investigation and implementation.
