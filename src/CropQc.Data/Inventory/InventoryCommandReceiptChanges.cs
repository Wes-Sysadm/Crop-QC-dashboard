using System.Collections.Immutable;
using System.Text.Json;
using CropQc.Data.Entities;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

public sealed partial class InventoryCommandExecutor
{
    private async Task<ImmutableArray<InventoryCommandEffect>> ChangeReceiptQuantityAsync(CropQcDbContext db, CanonicalProjectionFactory factory,
        InventoryCommand c, DateTimeOffset now, int attempt, CancellationToken ct)
    {
        Require(c.Lines.IsEmpty && c.ReceiptChange is { ReceiptId: > 0, NewQuantity: >= 0 } && !c.ReceiptChange.Allocations.IsDefault,
            "Receipt correction needs a reviewed exact receipt intent.");
        var change = c.ReceiptChange!;
        var state = await new InventoryReceiptAvailability(db).ReadAsync(change.ReceiptId, ct);
        Require(state != null && state.Blocker == null, state?.Blocker ?? "Receipt was not found.");
        Require(state!.Fingerprint == change.ExpectedFingerprint && state.Receipt.ConcurrencyVersion == change.ExpectedVersion,
            "Receipt inventory changed; reload and review the correction.", InventoryCommandStatus.Stale);
        var receipt = await db.Receipts.SingleAsync(x => x.Id == change.ReceiptId, ct);
        Require(receipt.ReceiptType == "Truck receipt", "Only ordinary inventory receipts can change inventory quantity.");
        var isVoid = c.Kind == InventoryCommandKind.VoidReceipt;
        Require(!isVoid || change.VoidConfirmation == receipt.CompuTechReceiptId && state.CustodyQuantity == 0,
            "Voiding requires the exact receipt confirmation and no remaining external/transit custody.");
        var delta = isVoid ? -state.RoomQuantity : checked(change.NewQuantity - receipt.BinCount);
        Require(isVoid || delta != 0, "No quantity correction was requested.");
        var allocations = change.Allocations;
        if (isVoid) allocations = state.Allocations.Where(x => x.Position.Location.Custody == InventoryCustody.Room)
            .Select(x => new InventoryReceiptQuantityAllocation(x.Key, x.Slice.Quantity)).ToImmutableArray();
        Require(allocations.All(x => x.Quantity > 0) && allocations.Select(x => x.Key).Distinct().Count() == allocations.Length
            && allocations.Sum(x => x.Quantity) == Math.Abs(delta), "Correction allocations must total the exact receipt quantity change.");
        var selected = allocations.Select(x => (Input: x, Evidence: state.Allocations.SingleOrDefault(y => y.Key == x.Key))).ToArray();
        Require(selected.All(x => x.Evidence != null && x.Evidence.Position.Location.Custody == InventoryCustody.Room
            && (delta > 0 || x.Input.Quantity <= x.Evidence.Slice.Quantity)), "Only exact current receipt allocations may be corrected; custody cannot be rewritten.");
        foreach (var p in selected.Select(x => x.Evidence!.Position).DistinctBy(x => x.PositionKey))
        {
            Require(await db.Rooms.AnyAsync(x => x.Id == p.Location.RoomId && x.WarehouseId == p.Location.WarehouseId && x.IsActive && x.Warehouse.IsActive && !x.IsSealed, ct),
                "Correction room is unavailable or sealed.");
            var evidence = (await new InventoryEvidenceLoader(db).LoadAsync(new(p.Location.WarehouseId, [p.Location.RoomId!.Value]), now, ct)).Positions.Single(x => x.Identity.Key == p.Identity.Key);
            await NormalizePositionAsync(db, factory, c, evidence, p, now, attempt, ct);
        }
        var beforeJson = ReceiptValues(receipt);
        var operation = new ReceiptInventoryOverride
        {
            Id = Guid.NewGuid(),
            Receipt = receipt,
            ActionType = isVoid ? ReceiptInventoryOverrideActionTypes.VoidReceipt : ReceiptInventoryOverrideActionTypes.QuantityCorrection,
            OldReceiptBinCount = receipt.BinCount,
            NewReceiptBinCount = isVoid ? 0 : change.NewQuantity,
            InventoryDelta = delta,
            CurrentInventoryBefore = state.RoomQuantity + state.CustodyQuantity,
            CurrentInventoryAfter = state.RoomQuantity + state.CustodyQuantity + delta,
            AdministratorUserId = c.ActorId,
            Reason = c.Reason,
            OperationKey = c.OperationKey,
            CreatedAt = now,
            BeforeReceiptSnapshotJson = beforeJson,
            AfterReceiptSnapshotJson = "{}",
            AffectedInventorySnapshotJson = JsonSerializer.Serialize(state.Positions, Json),
            VoidConfirmationDetails = isVoid ? JsonSerializer.Serialize(new { change.VoidConfirmation }, Json) : null
        };
        db.ReceiptInventoryOverrides.Add(operation);
        var effects = ImmutableArray.CreateBuilder<InventoryCommandEffect>();
        for (var index = 0; index < selected.Length; index++)
        {
            var (input, allocation) = selected[index]; var p = allocation!.Position; var slice = allocation.Slice;
            var room = p.Location.RoomId!.Value; var warehouse = p.Location.WarehouseId;
            var before = await PhysicalAsync(db, p.Identity, warehouse, room, ct);
            var signed = delta > 0 ? input.Quantity : -input.Quantity;
            var rows = await db.TreatmentLineageSegments.Include(x => x.Applications).Where(x => x.RoomId == room && x.Disposition == "Current" && x.CurrentBins > 0
                && x.ReceiptId == receipt.Id && x.TreatmentSignature == slice.Signature).OrderBy(x => x.Id).ToListAsync(ct);
            rows = rows.Where(x => InventoryStatusIdentity.NormalizeLineageKey(x.IdentityKey) == p.Identity.Key).ToList();
            Require(rows.Count > 0 && rows.Sum(x => x.CurrentBins) == slice.Quantity, "Exact receipt allocation changed during correction.");
            var moves = new List<TreatmentLineageMovement>();
            if (delta > 0)
            {
                var target = await factory.CurrentAsync(p.Identity, warehouse, room, slice.Signature, slice.State, receipt.Id, slice.ApplicationIds, now, ct);
                Credit(target, input.Quantity, now);
                var movement = Move(c, p.Identity, new(target, input.Quantity), target, null, room, c.OperationKey + ":correct:" + index, now, "ReceiptQuantityCorrection");
                movement.SourceSegment = null; movement.SourceSegmentId = null; moves.Add(movement);
            }
            else
            {
                var remaining = input.Quantity;
                foreach (var row in rows)
                {
                    var quantity = Math.Min(remaining, row.CurrentBins); if (quantity == 0) break;
                    var movement = Move(c, p.Identity, new(row, quantity), null, room, null, c.OperationKey + ":correct:" + index + ":" + row.Id, now,
                        isVoid ? "ReceiptVoid" : "ReceiptQuantityCorrection");
                    moves.Add(movement); row.CurrentBins -= quantity; row.ConcurrencyVersion++; row.UpdatedAt = now;
                    if (row.CurrentBins == 0) CanonicalProjectionFactory.Retire(row, c.OperationKey, now);
                    remaining -= quantity;
                }
                Require(remaining == 0, "Receipt correction would consume unproven stock.");
            }
            var ledger = Ledger(c, p.Identity, warehouse, room, signed, before, c.OperationKey + ":correct:" + index, now);
            ledger.Receipt = receipt; ledger.ReceiptInventoryOverride = operation; ledger.AdjustmentType = "ReceiptAdminOverride";
            db.RoomInventoryAdjustments.Add(ledger); db.TreatmentLineageMovements.AddRange(moves);
            await Stage("Movement", db, attempt, ct); await db.SaveChangesAsync(ct);
            Require(await PhysicalAsync(db, p.Identity, warehouse, room, ct) == before + signed && before + signed >= 0, "Receipt correction failed quantity conservation.");
            effects.Add(new(p.PositionKey, before, before + signed, input.Quantity, receipt.Id, [ledger.Id], moves.Select(x => x.Id).ToImmutableArray()));
        }
        if (isVoid)
        {
            receipt.IsDeleted = true; receipt.DeletedAt = now; receipt.DeletedByUserId = c.ActorId; receipt.DeleteReason = c.Reason;
            var actor = await db.Users.SingleAsync(x => x.Id == c.ActorId, ct);
            db.ReceiptDeletionAudits.Add(new()
            {
                Id = Guid.NewGuid(),
                OperationId = operation.Id,
                DeletedReceiptId = receipt.Id,
                ReceiptNumber = receipt.CompuTechReceiptId,
                CropYear = receipt.CropYear,
                IdentifyingFieldsJson = beforeJson,
                DependencyCountsJson = operation.AffectedInventorySnapshotJson,
                DeletedByEmail = actor.Email,
                DeletedAt = now,
                Reason = c.Reason,
                Result = "AdminVoided"
            });
        }
        else receipt.BinCount = change.NewQuantity;
        receipt.ConcurrencyVersion++; receipt.UpdatedAt = now;
        operation.AfterReceiptSnapshotJson = ReceiptValues(receipt); operation.ExpectedAdjustmentCount = selected.Length; operation.IsComplete = true;
        AddAudit(db, c, isVoid ? "CanonicalReceiptVoided" : "CanonicalReceiptQuantityCorrected", operation.Id.ToString(), beforeJson,
            new { operation.AfterReceiptSnapshotJson, operation.InventoryDelta, receipt.Id, receipt.ConcurrencyVersion }, now);
        await db.SaveChangesAsync(ct);
        foreach (var p in selected.Select(x => x.Evidence!.Position).DistinctBy(x => x.PositionKey))
        {
            var after = (await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(p.Location.WarehouseId, [p.Location.RoomId!.Value]), new(), now, ct))
                .Positions.Single(x => x.PositionKey == p.PositionKey);
            Require(after.IsOperable && after.RawProjectionQuantity == after.AuthoritativeQuantity, "Receipt correction left inconsistent current projections.");
        }
        return effects.ToImmutable();
    }

    private static string ReceiptValues(Receipt r) => JsonSerializer.Serialize(new
    {
        r.Id,
        r.CompuTechReceiptId,
        r.BinCount,
        r.CropYear,
        r.GrowerLotId,
        r.FruitProfileId,
        r.GrowerNumber,
        r.GrowerName,
        r.LotCode,
        r.WarehouseId,
        r.RoomId,
        r.ReceivedAt,
        r.ReceiptType,
        r.IsDeleted,
        r.ConcurrencyVersion
    }, Json);
}
