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
        var currentRooms = state!.Allocations.Where(x => x.Position.Location.Custody == InventoryCustody.Room && x.Slice.Quantity > 0).ToArray();
        var sourceRooms = currentRooms.Select(x => x.Position.Location.RoomId!.Value).Distinct().ToArray();
        Require(sourceRooms.Length > 0, "No current receipt inventory remains in a room. A location correction cannot create or recall inventory.");
        Require(change.SourceRoomId != null || sourceRooms.Length == 1,
            "Receipt inventory is currently split across multiple rooms. Select the current inventory location to correct.");
        var sourceRoom = change.SourceRoomId ?? sourceRooms.Single();
        var allocations = currentRooms.Where(x => x.Position.Location.RoomId == sourceRoom).ToArray();
        Require(allocations.Length > 0, "The selected receipt inventory location is no longer available. Refresh and review the allocations.", InventoryCommandStatus.Stale);
        Require(allocations.Select(x => x.Position.PositionKey).Distinct().Count() == 1,
            "Selected receipt inventory contains conflicting identities. Review its allocations before correcting the location.");
        var p = allocations[0].Position;
        var sourceWarehouse = p.Location.WarehouseId;
        var quantity = allocations.Sum(x => x.Slice.Quantity);
        Require(quantity > 0 && quantity <= p.AuthoritativeQuantity, "Exact surviving receipt quantity cannot be proven.");
        Require(sourceRoom != destination.RoomId, "Select a different destination room.");
        Require(await db.Rooms.AnyAsync(x => x.Id == sourceRoom && x.WarehouseId == sourceWarehouse
            && x.IsActive && x.Warehouse.IsActive && !x.IsSealed, ct), "Source room is unavailable or sealed.");
        Require(await db.Rooms.AnyAsync(x => x.Id == destination.RoomId && x.WarehouseId == destination.WarehouseId
            && x.IsActive && x.Warehouse.IsActive && !x.IsSealed, ct), "Select an active, unsealed destination room.");
        var loader = new InventoryEvidenceLoader(db);
        var sourceEvidence = (await loader.LoadAsync(new(sourceWarehouse, [sourceRoom]), now, ct)).Positions.Single(x => x.Identity.Key == p.Identity.Key);
        var exact = InventoryAvailabilityResolver.Resolve(sourceEvidence, new(RequireExactReceipt: true, ReceiptId: receipt.Id));
        Require(exact.IsOperable, "Exact receipt inventory or treatment ownership cannot be proven. Review the receipt allocations.");
        await NormalizePositionAsync(db, factory, c, sourceEvidence, p, now, attempt, ct);
        var targetEvidence = (await loader.LoadAsync(new(destination.WarehouseId, [destination.RoomId]), now, ct)).Positions.SingleOrDefault(x => x.Identity.Key == p.Identity.Key);
        if (targetEvidence != null)
        {
            var resolved = InventoryAvailabilityResolver.Resolve(targetEvidence, new());
            Require(resolved.IsOperable, CanonicalInventoryMessages.PlacementBlocker($"Receipt {receipt.CompuTechReceiptId}", targetEvidence.Location.Name, resolved));
            await NormalizePositionAsync(db, factory, c, targetEvidence, resolved, now, attempt, ct);
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
            AffectedInventorySnapshotJson = JsonSerializer.Serialize(new { SourceRoomId = sourceRoom, SourceWarehouseId = sourceWarehouse, Destination = destination, RelocatedBins = quantity, state.Positions, state.Allocations }, Json)
        };
        db.ReceiptInventoryOverrides.Add(operation);
        // One conserved ledger pair for this selected room, with separate treatment
        // movements. Receipt rows are bounded by receipt + room + current disposition;
        // identity normalization is deliberately performed only after that SQL filter.
        var sourceBefore = p.AuthoritativeQuantity;
        var targetBefore = targetEvidence?.AuthoritativeQuantity ?? 0;
        var rows = (await db.TreatmentLineageSegments.Include(x => x.Applications).Where(x => x.Disposition == "Current"
            && x.CurrentBins > 0 && x.RoomId == sourceRoom && x.ReceiptId == receipt.Id)
            .Take(InventoryEvidenceLoader.MaximumEvidenceRowsPerTable + 1).ToListAsync(ct));
        Require(rows.Count <= InventoryEvidenceLoader.MaximumEvidenceRowsPerTable, "Receipt allocation exceeds the safe evidence limit.");
        rows = rows.Where(x => x.IdentityKey == p.Identity.Key).ToList();
        Require(rows.Sum(x => x.CurrentBins) == quantity && allocations.All(a => rows.Where(x => x.TreatmentSignature == a.Slice.Signature
            && x.TreatmentState == a.Slice.State).Sum(x => x.CurrentBins) == a.Slice.Quantity),
            "Exact receipt allocation changed. Refresh and try again.", InventoryCommandStatus.Stale);
        var part = $"{c.OperationKey}:location";
        var outgoing = Ledger(c, p.Identity, sourceWarehouse, sourceRoom, -quantity, sourceBefore, part + ":out", now);
        var incoming = Ledger(c, p.Identity, destination.WarehouseId, destination.RoomId, quantity, targetBefore, part + ":in", now);
        outgoing.Receipt = receipt; incoming.Receipt = receipt;
        outgoing.ReceiptInventoryOverride = operation; incoming.ReceiptInventoryOverride = operation;
        outgoing.AdjustmentType = "CorrectOriginalRoomOut"; incoming.AdjustmentType = "CorrectOriginalRoomIn";
        db.RoomInventoryAdjustments.AddRange(outgoing, incoming);
        var moves = new List<TreatmentLineageMovement>();
        await factory.PrepareReceiptDestinationAsync(p.Identity, destination.RoomId, receipt.Id, ct);
        foreach (var row in rows)
        {
            var bins = row.CurrentBins;
            var target = await factory.CurrentAsync(p.Identity, destination.WarehouseId, destination.RoomId, row.TreatmentSignature,
                row.TreatmentState, receipt.Id, row.Applications.Select(x => x.RoomTreatmentApplicationId), now, ct);
            Credit(target, bins, now);
            moves.Add(Move(c, p.Identity, new(row, bins), target, sourceRoom, destination.RoomId, part, now, "ReceiptLocationCorrection"));
            row.CurrentBins = 0;
            CanonicalProjectionFactory.Retire(row, c.OperationKey, now);
        }
        db.TreatmentLineageMovements.AddRange(moves);
        await Stage("Movement", db, attempt, ct); await db.SaveChangesAsync(ct);
        Require(await PhysicalAsync(db, p.Identity, sourceWarehouse, sourceRoom, ct) == sourceBefore - quantity
            && await PhysicalAsync(db, p.Identity, destination.WarehouseId, destination.RoomId, ct) == targetBefore + quantity,
            "Receipt location correction did not conserve physical inventory.");
        ImmutableArray<InventoryCommandEffect> effects = [new(p.PositionKey, sourceBefore, sourceBefore - quantity, quantity, receipt.Id,
            [outgoing.Id, incoming.Id], moves.Select(x => x.Id).ToImmutableArray())];
        receipt.WarehouseId = destination.WarehouseId; receipt.RoomId = destination.RoomId; receipt.ConcurrencyVersion++; receipt.UpdatedAt = now;
        operation.AfterReceiptSnapshotJson = ReceiptValues(receipt); operation.ExpectedAdjustmentCount = 2; operation.IsComplete = true;
        AddAudit(db, c, "CanonicalReceiptLocationCorrected", operation.Id.ToString(), beforeJson,
            new { operation.AfterReceiptSnapshotJson, SourceRoomId = sourceRoom, SourceWarehouseId = sourceWarehouse, Destination = destination, RelocatedBins = quantity, c.ActorId, c.Reason, CorrectedAt = now }, now);
        await db.SaveChangesAsync(ct);
        var after = await new InventoryReceiptAvailability(db).ReadAsync(receipt.Id, ct);
        Require(after != null && after.Blocker == null && after.RoomQuantity == state.RoomQuantity && after.CustodyQuantity == state.CustodyQuantity,
            "Receipt location correction changed quantity or receipt ownership.");
        // External custody and every unselected room retain exactly the same receipt slices.
        static object[] Unselected(InventoryReceiptAvailabilityResult value, int source, int target) => value.Allocations
            .Where(x => x.Position.Location.Custody != InventoryCustody.Room || x.Position.Location.RoomId != source && x.Position.Location.RoomId != target)
            .OrderBy(x => x.Key).Select(x => (object)new { x.Key, x.Slice.State, x.Slice.Quantity, x.Slice.ApplicationIds }).ToArray();
        Require(JsonSerializer.Serialize(Unselected(state, sourceRoom, destination.RoomId), Json)
            == JsonSerializer.Serialize(Unselected(after!, sourceRoom, destination.RoomId), Json), "Unselected receipt allocations changed.");
        return effects;
    }
}
