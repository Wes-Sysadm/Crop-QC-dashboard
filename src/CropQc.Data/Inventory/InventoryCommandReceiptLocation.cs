using System.Collections.Immutable;
using System.Text.Json;
using CropQc.Data.Entities;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

public sealed partial class InventoryCommandExecutor
{
    private async Task<ImmutableArray<InventoryCommandEffect>> CorrectReceiptLocationAsync(CropQcDbContext db, CanonicalProjectionFactory factory,
        InventoryCommand c, DateTimeOffset now, int attempt, CancellationToken ct)
    {
        Require(c.Lines.IsEmpty && c.ReceiptLocation is { ReceiptId: > 0 }, "An exact receipt location correction is required.");
        var change = c.ReceiptLocation!;
        var state = await new InventoryReceiptAvailability(db).ReadAsync(change.ReceiptId, ct);
        Require(state != null && state.Blocker == null, state?.Blocker ?? "Receipt not found.");
        var receipt = await db.Receipts.SingleAsync(x => x.Id == change.ReceiptId, ct);
        Require(receipt.ConcurrencyVersion == change.ExpectedVersion && state!.Fingerprint == change.ExpectedFingerprint,
            "Receipt or inventory changed after review.", InventoryCommandStatus.Stale);
        var expected = change.ExpectedReceipt;
        Require(receipt.ReceiptType == "Truck receipt" && expected.ReceiptType == receipt.ReceiptType && expected.Quantity == receipt.BinCount
            && expected.ReceiptNumber == receipt.CompuTechReceiptId && expected.CropYear == receipt.CropYear && expected.GrowerLotId == receipt.GrowerLotId
            && expected.FruitProfileId == receipt.FruitProfileId && expected.RoomId == receipt.RoomId && expected.WarehouseId == receipt.WarehouseId,
            "Location correction cannot also change receipt identity or quantity.");
        var destination = change.Destination;
        Require(destination.RoomId != receipt.RoomId && await db.Rooms.AnyAsync(x => x.Id == destination.RoomId
            && x.WarehouseId == destination.WarehouseId && x.IsActive && x.Warehouse.IsActive, ct), "Select a different active receiving room.");

        // After business movement/consumption, the receipt location is receiving
        // provenance only. A correction must never teleport current or external stock.
        var subsequentActivity = await db.TreatmentLineageMovements.AnyAsync(x => (x.ReceiptId == receipt.Id || x.SourceSegment != null && x.SourceSegment.ReceiptId == receipt.Id)
            && x.SourceRoomId != null
            && x.SourceRoomId != x.DestinationRoomId && x.MovementType != "ReceiptLocationCorrection" && x.MovementType != "ReceiptQuantityCorrection", ct)
            || await db.BinsRunEntries.AnyAsync(x => x.ReceiptId == receipt.Id || x.SourceInventoryAdjustment != null && x.SourceInventoryAdjustment.ReceiptId == receipt.Id, ct)
            || await db.RoomDepletions.AnyAsync(x => x.ReceiptId == receipt.Id, ct);
        var relocate = !subsequentActivity && state!.RoomQuantity > 0 && state.CustodyQuantity == 0;
        var loader = new InventoryEvidenceLoader(db);
        if (relocate)
        {
            Require(state!.Allocations.All(x => x.Position.Location.Custody == InventoryCustody.Room && x.Position.Location.RoomId == receipt.RoomId
                && x.Position.Location.WarehouseId == receipt.WarehouseId), "Current receipt stock is not wholly attributable to its original receiving room.");
            Require(await db.Rooms.AnyAsync(x => x.Id == receipt.RoomId && x.IsActive && !x.IsSealed, ct)
                && await db.Rooms.AnyAsync(x => x.Id == destination.RoomId && !x.IsSealed, ct), "Physical correction requires unsealed rooms.");
            foreach (var p in state.Allocations.Select(x => x.Position).DistinctBy(x => x.PositionKey))
            {
                var evidence = (await loader.LoadAsync(new(p.Location.WarehouseId, [p.Location.RoomId!.Value]), now, ct)).Positions.Single(x => x.Identity.Key == p.Identity.Key);
                await NormalizePositionAsync(db, factory, c, evidence, p, now, attempt, ct);
                var target = (await loader.LoadAsync(new(destination.WarehouseId, [destination.RoomId]), now, ct)).Positions.SingleOrDefault(x => x.Identity.Key == p.Identity.Key);
                if (target != null)
                {
                    var resolved = InventoryAvailabilityResolver.Resolve(target, new());
                    Require(resolved.IsOperable, "Destination inventory needs review before receiving the correction.");
                    await NormalizePositionAsync(db, factory, c, target, resolved, now, attempt, ct);
                }
            }
        }
        var beforeJson = ReceiptValues(receipt);
        var operation = new ReceiptInventoryOverride
        {
            Id = Guid.NewGuid(),
            Receipt = receipt,
            ActionType = ReceiptInventoryOverrideActionTypes.LocationCorrection,
            OldReceiptBinCount = receipt.BinCount,
            NewReceiptBinCount = receipt.BinCount,
            InventoryDelta = 0,
            CurrentInventoryBefore = state!.RoomQuantity + state.CustodyQuantity,
            CurrentInventoryAfter = state.RoomQuantity + state.CustodyQuantity,
            AdministratorUserId = c.ActorId,
            Reason = c.Reason,
            OperationKey = c.OperationKey,
            CreatedAt = now,
            BeforeReceiptSnapshotJson = beforeJson,
            AfterReceiptSnapshotJson = "{}",
            AffectedInventorySnapshotJson = JsonSerializer.Serialize(new { RelocateCurrentStock = relocate, state.Positions }, Json)
        };
        db.ReceiptInventoryOverrides.Add(operation);
        var effects = ImmutableArray.CreateBuilder<InventoryCommandEffect>();
        if (relocate)
        {
            foreach (var allocation in state.Allocations)
            {
                var p = allocation.Position;
                var sourceBefore = await PhysicalAsync(db, p.Identity, p.Location.WarehouseId, p.Location.RoomId!.Value, ct);
                var targetBefore = await PhysicalAsync(db, p.Identity, destination.WarehouseId, destination.RoomId, ct);
                var rows = (await db.TreatmentLineageSegments.Include(x => x.Applications).Where(x => x.Disposition == "Current"
                    && x.CurrentBins > 0 && x.RoomId == receipt.RoomId && x.ReceiptId == receipt.Id && x.TreatmentSignature == allocation.Slice.Signature).ToListAsync(ct))
                    .Where(x => x.IdentityKey == p.Identity.Key).ToArray();
                Require(rows.Sum(x => x.CurrentBins) == allocation.Slice.Quantity, "Exact receipt allocation changed.", InventoryCommandStatus.Stale);
                var part = $"{c.OperationKey}:location:{effects.Count}";
                var outgoing = Ledger(c, p.Identity, p.Location.WarehouseId, p.Location.RoomId.Value, -allocation.Slice.Quantity, sourceBefore, part + ":out", now);
                var incoming = Ledger(c, p.Identity, destination.WarehouseId, destination.RoomId, allocation.Slice.Quantity, targetBefore, part + ":in", now);
                outgoing.Receipt = receipt; incoming.Receipt = receipt;
                outgoing.ReceiptInventoryOverride = operation; incoming.ReceiptInventoryOverride = operation;
                outgoing.AdjustmentType = "CorrectOriginalRoomOut"; incoming.AdjustmentType = "CorrectOriginalRoomIn";
                db.RoomInventoryAdjustments.AddRange(outgoing, incoming);
                var moves = new List<TreatmentLineageMovement>();
                foreach (var row in rows)
                {
                    var quantity = row.CurrentBins;
                    var target = await factory.CurrentAsync(p.Identity, destination.WarehouseId, destination.RoomId, row.TreatmentSignature,
                        row.TreatmentState, receipt.Id, row.Applications.Select(x => x.RoomTreatmentApplicationId), now, ct);
                    Credit(target, quantity, now);
                    moves.Add(Move(c, p.Identity, new(row, quantity), target, receipt.RoomId, destination.RoomId, part, now, "ReceiptLocationCorrection"));
                    row.CurrentBins = 0;
                    CanonicalProjectionFactory.Retire(row, c.OperationKey, now);
                }
                db.TreatmentLineageMovements.AddRange(moves);
                await Stage("Movement", db, attempt, ct); await db.SaveChangesAsync(ct);
                Require(await PhysicalAsync(db, p.Identity, p.Location.WarehouseId, p.Location.RoomId.Value, ct) == sourceBefore - allocation.Slice.Quantity
                    && await PhysicalAsync(db, p.Identity, destination.WarehouseId, destination.RoomId, ct) == targetBefore + allocation.Slice.Quantity,
                    "Receipt location correction did not conserve physical inventory.");
                effects.Add(new(p.PositionKey, sourceBefore, sourceBefore - allocation.Slice.Quantity, allocation.Slice.Quantity, receipt.Id,
                    [outgoing.Id, incoming.Id], moves.Select(x => x.Id).ToImmutableArray()));
            }
        }
        receipt.WarehouseId = destination.WarehouseId; receipt.RoomId = destination.RoomId; receipt.ConcurrencyVersion++; receipt.UpdatedAt = now;
        operation.AfterReceiptSnapshotJson = ReceiptValues(receipt); operation.ExpectedAdjustmentCount = effects.Count * 2; operation.IsComplete = true;
        AddAudit(db, c, "CanonicalReceiptLocationCorrected", operation.Id.ToString(), beforeJson,
            new { operation.AfterReceiptSnapshotJson, RelocatedBins = relocate ? state.RoomQuantity : 0, ReceivingProvenanceOnly = !relocate }, now);
        await db.SaveChangesAsync(ct);
        var after = await new InventoryReceiptAvailability(db).ReadAsync(receipt.Id, ct);
        Require(after != null && after.Blocker == null && after.RoomQuantity == state.RoomQuantity && after.CustodyQuantity == state.CustodyQuantity,
            "Receipt location correction changed quantity or receipt ownership.");
        if (!relocate)
            Require(state.Positions.OrderBy(x => x.PositionKey).Select(x => (x.PositionKey, x.AuthoritativeQuantity, x.RawProjectionQuantity))
                .SequenceEqual(after!.Positions.OrderBy(x => x.PositionKey).Select(x => (x.PositionKey, x.AuthoritativeQuantity, x.RawProjectionQuantity))),
                "Receiving provenance correction changed current inventory.");
        return effects.ToImmutable();
    }
}
