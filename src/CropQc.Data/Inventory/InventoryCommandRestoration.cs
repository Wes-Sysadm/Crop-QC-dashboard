using System.Collections.Immutable;
using CropQc.Data.Entities;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

public sealed partial class InventoryCommandExecutor
{
    private async Task<ImmutableArray<InventoryCommandEffect>> ReverseLossAsync(CropQcDbContext db, CanonicalProjectionFactory factory,
        InventoryCommand c, DateTimeOffset now, int attempt, CancellationToken ct)
    {
        Require(c.Lines.IsEmpty && c.PhysicalParentId > 0, "Loss restoration requires an exact original loss.", InventoryCommandStatus.InvalidIntent);
        var loss = await db.RoomInventoryLosses.Include(x => x.InventoryAdjustments).SingleOrDefaultAsync(x => x.Id == c.PhysicalParentId, ct);
        Require(loss != null && !loss.IsReversed, "Loss is missing or already reversed.");
        var old = loss!;
        Require(old.InventoryAdjustments.Count == 1 && old.InventoryAdjustments.Single().ChangeAmount == -old.BinCount,
            "Original loss ledger cannot be restored exactly.");
        var original = old.InventoryAdjustments.Single();
        var moves = await db.TreatmentLineageMovements.Include(x => x.SourceSegment).ThenInclude(x => x!.Applications)
            .Where(x => x.RoomInventoryLossId == old.Id).ToListAsync(ct);
        var result = await RestoreConsumptionAsync(db, factory, c, original, moves, old.BinCount, now, attempt, ct);
        result.Ledger.RoomInventoryLoss = old; result.Ledger.AdjustmentType = InventoryLedgerKinds.DroppedBinsReversal;
        foreach (var move in result.Movements) { move.RoomInventoryLoss = old; move.MovementType = "InventoryLossReversal"; }
        old.IsReversed = true; old.ReversedAt = now; old.ReversedByUserId = c.ActorId; old.ReverseReason = c.Reason;
        await db.SaveChangesAsync(ct);
        var after = (await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(old.WarehouseId, [old.RoomId]), new(), now, ct))
            .Positions.Single(x => x.PositionKey == result.Effect.PositionKey);
        Require(after.IsOperable && after.AuthoritativeQuantity == result.Effect.After && after.RawProjectionQuantity == after.AuthoritativeQuantity,
            "Loss restoration does not reconcile authoritative inventory and current treatment.");
        return [result.Effect with { ParentId = old.Id, LedgerIds = [result.Ledger.Id], MovementIds = result.Movements.Select(x => x.Id).ToImmutableArray() }];
    }

    private async Task<(InventoryCommandEffect Effect, RoomInventoryAdjustment Ledger, List<TreatmentLineageMovement> Movements)> RestoreConsumptionAsync(
        CropQcDbContext db, CanonicalProjectionFactory factory, InventoryCommand c, RoomInventoryAdjustment original,
        List<TreatmentLineageMovement> moves, int quantity, DateTimeOffset now, int attempt, CancellationToken ct)
    {
        Require(quantity > 0 && original.ChangeAmount == -quantity && moves.Count > 0 && moves.Sum(x => x.BinCount) == quantity
            && moves.All(x => x.BinCount > 0 && x.SourceSegment != null && x.SourceRoomId == original.RoomId
                && x.DestinationRoomId == null && x.DestinationSegmentId == null && x.ReversesTreatmentLineageMovementId == null),
            "Original consumption lacks exact immutable quantity and lineage evidence.");
        var ids = moves.Select(x => x.Id).ToArray();
        Require(!await db.TreatmentLineageMovements.AnyAsync(x => ids.Contains(x.ReversesTreatmentLineageMovementId ?? 0), ct),
            "Consumption was already restored.");
        var s = moves[0].SourceSegment!;
        var identity = new InventoryIdentity(s.CropYear, s.GrowerLotId, s.FruitProfileId, s.LotNumberSnapshot, s.GrowerNumberSnapshot,
            s.VarietyCodeSnapshot, s.ProductionTypeSnapshot, s.IsOrganicSnapshot, s.InventoryStatusSnapshot ?? "");
        Require(identity.IsComplete && original.CropYear == identity.CropYear && original.GrowerLotId == identity.GrowerLotId
            && original.FruitProfileId == identity.FruitProfileId && original.LotNumber == identity.Lot
            && moves.All(x => InventoryStatusIdentity.NormalizeLineageKey(x.IdentityKey) == identity.Key
                && InventoryStatusIdentity.NormalizeLineageKey(x.SourceSegment!.IdentityKey) == identity.Key
                && x.SourceSegment.TreatmentSignature == x.TreatmentSignatureSnapshot && x.SourceSegment.TreatmentState == x.TreatmentStateSnapshot),
            "Original consumed identity or treatment evidence conflicts.");
        Require(await db.Rooms.AnyAsync(x => x.Id == original.RoomId && x.WarehouseId == original.WarehouseId
            && x.IsActive && x.Warehouse.IsActive && !x.IsSealed, ct), "Restoration room is unavailable or sealed.");
        var loader = new InventoryEvidenceLoader(db);
        var evidence = (await loader.LoadAsync(new(original.WarehouseId, [original.RoomId]), now, ct)).Positions.SingleOrDefault(x => x.Identity.Key == identity.Key);
        Require(evidence != null, "Original room inventory position is unavailable.");
        var resolved = InventoryAvailabilityResolver.Resolve(evidence!, new());
        Require(resolved.IsOperable, "Current inventory requires review before restoration.");
        await NormalizePositionAsync(db, factory, c, evidence!, resolved, now, attempt, ct);
        var ledger = Ledger(c, identity, original.WarehouseId, original.RoomId, quantity, resolved.AuthoritativeQuantity, c.OperationKey + ":restore:" + original.Id, now);
        ledger.ReceiptId = original.ReceiptId;
        var reversals = new List<TreatmentLineageMovement>();
        var appIds = moves.SelectMany(x => x.SourceSegment!.Applications).Select(x => x.RoomTreatmentApplicationId).Distinct().ToArray();
        var applications = await db.RoomTreatmentApplications.AsNoTracking().Where(x => appIds.Contains(x.Id))
            .Select(x => new InventoryApplicationEvidence(x.Id, x.AppliedAt, x.ReversedAt, x.ReceiptId)).ToArrayAsync(ct);
        foreach (var move in moves)
        {
            var source = move.SourceSegment!;
            var treatment = InventoryEffectiveTreatment.Read(source.TreatmentSignature, source.TreatmentState,
                source.Applications.Select(x => x.RoomTreatmentApplicationId).ToImmutableArray(), applications);
            var target = await factory.CurrentAsync(identity, original.WarehouseId, original.RoomId, treatment.Signature,
                treatment.State, source.ReceiptId, treatment.ApplicationIds, now, ct);
            Credit(target, move.BinCount, now);
            var reversal = Move(c, identity, new(source, move.BinCount, treatment), target, null, original.RoomId,
                c.OperationKey + ":restore:" + move.Id, now, "ConsumptionReversal");
            reversal.ReversesTreatmentLineageMovementId = move.Id;
            reversals.Add(reversal);
        }
        db.RoomInventoryAdjustments.Add(ledger); db.TreatmentLineageMovements.AddRange(reversals);
        await Stage("Restoration", db, attempt, ct);
        // Parent links and reversal type are assigned by the typed owner before persistence.
        return (new(resolved.PositionKey, resolved.AuthoritativeQuantity, checked(resolved.AuthoritativeQuantity + quantity), quantity,
            null, [], []), ledger, reversals);
    }
}
