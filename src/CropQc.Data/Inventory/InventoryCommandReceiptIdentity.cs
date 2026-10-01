using System.Collections.Immutable;
using System.Text.Json;
using CropQc.Data.Entities;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

public sealed partial class InventoryCommandExecutor
{
    private async Task<ImmutableArray<InventoryCommandEffect>> CorrectReceiptIdentityAsync(CropQcDbContext db, CanonicalProjectionFactory factory,
        InventoryCommand c, DateTimeOffset now, int attempt, CancellationToken ct)
    {
        Require(c.Lines.IsEmpty && c.ReceiptIdentity is { ReceiptId: > 0 }, "An exact receipt identity correction is required.");
        var change = c.ReceiptIdentity!;
        var state = await new InventoryReceiptAvailability(db).ReadAsync(change.ReceiptId, ct);
        Require(state != null && state.Blocker == null, state?.Blocker ?? "Receipt not found.");
        var receipt = await db.Receipts.SingleAsync(x => x.Id == change.ReceiptId, ct);
        Require(receipt.ConcurrencyVersion == change.ExpectedVersion && state!.Fingerprint == change.ExpectedFingerprint,
            "Receipt inventory changed after review.", InventoryCommandStatus.Stale);
        var expected = change.ExpectedReceipt;
        Require(receipt.ReceiptType == "Truck receipt" && receipt.GrowerLotId != null && expected.ReceiptType == receipt.ReceiptType && expected.Quantity == receipt.BinCount
            && expected.ReceiptNumber == receipt.CompuTechReceiptId && expected.CropYear == receipt.CropYear && expected.GrowerLotId == receipt.GrowerLotId
            && expected.FruitProfileId == receipt.FruitProfileId && expected.RoomId == receipt.RoomId && expected.WarehouseId == receipt.WarehouseId,
            "Identity correction requires structured source identity and cannot also change location or quantity.");
        var grower = await db.GrowerLots.AsNoTracking().SingleOrDefaultAsync(x => x.Id == change.Target.GrowerLotId && x.IsActive, ct);
        var profile = await db.FruitProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == change.Target.FruitProfileId && x.IsActive, ct);
        Require(grower != null && profile != null && change.Target.CropYear is >= 2000 and <= 2200 && change.Target.IsComplete
            && change.Target.Lot == grower.LotNumber && change.Target.GrowerNumber == grower.LotNumber && change.Target.Variety == profile.VarietyCode
            && change.Target.ProductionType == profile.ProductionType && change.Target.IsOrganic == profile.IsOrganic,
            "Select an exact active reviewed target identity.");
        Require((receipt.CropYear, receipt.GrowerLotId, receipt.FruitProfileId) != (change.Target.CropYear, change.Target.GrowerLotId, change.Target.FruitProfileId),
            "A different structured target identity is required.");
        var identities = await CanonicalIdentityMap.LoadAsync(db, [receipt.Id], ct);
        Require(identities.Resolve(change.Target, receipt.Id).Current.Key == change.Target.Key,
            "The selected target is superseded or would create a correction cycle.");
        Require(!identities.Corrections.Any(x => x.SourceCropYear == receipt.CropYear && x.SourceGrowerLotId == receipt.GrowerLotId
            && x.SourceFruitProfileId == receipt.FruitProfileId && x.CorrectedReceiptId == receipt.Id), "The source already has a recorded receipt correction.");
        Require(state!.Allocations.All(x => x.Position.Identity.CropYear == receipt.CropYear && x.Position.Identity.GrowerLotId == receipt.GrowerLotId
            && x.Position.Identity.FruitProfileId == receipt.FruitProfileId), "Receipt allocations do not match the reviewed source identity.");
        var beforeJson = ReceiptValues(receipt);
        var operation = new ReceiptInventoryOverride
        {
            Id = Guid.NewGuid(),
            Receipt = receipt,
            ActionType = ReceiptInventoryOverrideActionTypes.InventoryReclassification,
            OldReceiptBinCount = receipt.BinCount,
            NewReceiptBinCount = receipt.BinCount,
            InventoryDelta = 0,
            CurrentInventoryBefore = state.RoomQuantity + state.CustodyQuantity,
            CurrentInventoryAfter = state.RoomQuantity + state.CustodyQuantity,
            AdministratorUserId = c.ActorId,
            Reason = c.Reason,
            OperationKey = c.OperationKey,
            CreatedAt = now,
            BeforeReceiptSnapshotJson = beforeJson,
            AfterReceiptSnapshotJson = "{}",
            AffectedInventorySnapshotJson = JsonSerializer.Serialize(state.Allocations, Json)
        };
        var correction = new InventoryIdentityCorrection
        {
            Id = Guid.NewGuid(),
            OperationKey = c.OperationKey,
            SourceCropYear = receipt.CropYear,
            SourceGrowerLotId = receipt.GrowerLotId,
            SourceFruitProfileId = receipt.FruitProfileId,
            TargetCropYear = change.Target.CropYear!.Value,
            TargetGrowerLotId = grower!.Id,
            TargetFruitProfileId = profile!.Id,
            CorrectedReceipt = receipt,
            ReceiptInventoryOverride = operation,
            Reason = c.Reason,
            CreatedByUserId = c.ActorId,
            CreatedAt = now,
            SourceIdentitySnapshotJson = JsonSerializer.Serialize(new { Receipt = beforeJson, state.Positions, state.Allocations }, Json),
            TargetIdentitySnapshotJson = JsonSerializer.Serialize(change.Target, Json),
            IsActive = true
        };
        db.ReceiptInventoryOverrides.Add(operation); db.InventoryIdentityCorrections.Add(correction);
        var loader = new InventoryEvidenceLoader(db);
        var effects = ImmutableArray.CreateBuilder<InventoryCommandEffect>();
        foreach (var group in state.Allocations.Where(x => x.Position.Location.Custody == InventoryCustody.Room).GroupBy(x => x.Position.PositionKey))
        {
            var p = group.First().Position;
            var room = p.Location.RoomId!.Value; var warehouse = p.Location.WarehouseId;
            Require(await db.Rooms.AnyAsync(x => x.Id == room && x.WarehouseId == warehouse && x.IsActive && x.Warehouse.IsActive && !x.IsSealed, ct),
                "Physical identity correction requires an active unsealed room.");
            var targetIdentity = change.Target with { Status = InventoryStatusIdentity.Normalize(p.Identity.Status, p.Identity.ProductionType) };
            var batch = await loader.LoadAsync(new(warehouse, [room]), now, ct);
            var source = batch.Positions.Single(x => x.Identity.Key == p.Identity.Key);
            await NormalizePositionAsync(db, factory, c, source, p, now, attempt, ct);
            var targetEvidence = batch.Positions.SingleOrDefault(x => x.Identity.Key == targetIdentity.Key);
            var targetBefore = targetEvidence?.AuthoritativeQuantity ?? 0;
            if (targetEvidence != null)
            {
                var targetResult = InventoryAvailabilityResolver.Resolve(targetEvidence, new());
                Require(targetResult.IsOperable, "Target identity inventory requires review before merging receipt stock.");
                await NormalizePositionAsync(db, factory, c, targetEvidence, targetResult, now, attempt, ct);
            }
            var quantity = group.Sum(x => x.Slice.Quantity);
            var rows = (await db.TreatmentLineageSegments.Include(x => x.Applications).Where(x => x.Disposition == "Current" && x.CurrentBins > 0
                && x.RoomId == room && x.ReceiptId == receipt.Id).ToListAsync(ct)).Where(x => x.IdentityKey == p.Identity.Key).ToArray();
            Require(rows.Sum(x => x.CurrentBins) == quantity && group.All(g => rows.Where(x => x.TreatmentSignature == g.Slice.Signature).Sum(x => x.CurrentBins) == g.Slice.Quantity),
                "Current projections no longer prove the exact receipt allocation.", InventoryCommandStatus.Stale);
            var part = $"{c.OperationKey}:identity:{effects.Count}";
            var outgoing = Ledger(c, p.Identity, warehouse, room, -quantity, p.AuthoritativeQuantity, part + ":out", now);
            var incoming = Ledger(c, targetIdentity, warehouse, room, quantity, targetBefore, part + ":in", now);
            outgoing.Receipt = receipt; incoming.Receipt = receipt;
            outgoing.ReceiptInventoryOverride = operation; incoming.ReceiptInventoryOverride = operation;
            outgoing.InventoryIdentityCorrection = correction; incoming.InventoryIdentityCorrection = correction;
            outgoing.AdjustmentType = "InventoryIdentityCorrection"; incoming.AdjustmentType = "InventoryIdentityCorrection";
            incoming.GrowerName = grower.Grower;
            db.RoomInventoryAdjustments.AddRange(outgoing, incoming);
            var moves = new List<TreatmentLineageMovement>();
            foreach (var row in rows)
            {
                var bins = row.CurrentBins;
                var target = await factory.CurrentAsync(targetIdentity, warehouse, room, row.TreatmentSignature, row.TreatmentState,
                    receipt.Id, row.Applications.Select(x => x.RoomTreatmentApplicationId), now, ct);
                Credit(target, bins, now);
                var moveOut = Move(c, p.Identity, new(row, bins), null, room, null, part + ":out", now, "InventoryIdentityCorrectionOut");
                var moveIn = Move(c, targetIdentity, new(row, bins), target, null, room, part + ":in", now, "InventoryIdentityCorrectionIn");
                moveIn.SourceSegment = null; moveIn.SourceSegmentId = null;
                moveOut.InventoryIdentityCorrection = correction; moveIn.InventoryIdentityCorrection = correction;
                moves.Add(moveOut); moves.Add(moveIn);
                row.CurrentBins = 0; CanonicalProjectionFactory.Retire(row, c.OperationKey, now);
            }
            db.TreatmentLineageMovements.AddRange(moves);
            await Stage("Movement", db, attempt, ct); await db.SaveChangesAsync(ct);
            Require(await PhysicalAsync(db, p.Identity, warehouse, room, ct) == p.AuthoritativeQuantity - quantity
                && await PhysicalAsync(db, targetIdentity, warehouse, room, ct) == targetBefore + quantity,
                "Identity correction did not conserve physical inventory.");
            effects.Add(new(p.PositionKey, p.AuthoritativeQuantity, p.AuthoritativeQuantity - quantity, quantity, receipt.Id,
                [outgoing.Id, incoming.Id], moves.Select(x => x.Id).ToImmutableArray()));
        }
        receipt.CropYear = change.Target.CropYear!.Value; receipt.GrowerLotId = grower.Id; receipt.FruitProfileId = profile.Id;
        receipt.GrowerNumber = grower.LotNumber; receipt.LotCode = grower.LotNumber; receipt.GrowerName = grower.Grower;
        receipt.ConcurrencyVersion++; receipt.UpdatedAt = now;
        operation.AfterReceiptSnapshotJson = ReceiptValues(receipt); operation.ExpectedAdjustmentCount = effects.Sum(x => x.LedgerIds.Length); operation.IsComplete = true;
        correction.ExpectedAdjustmentCount = operation.ExpectedAdjustmentCount;
        correction.ExpectedTreatmentMovementCount = effects.Sum(x => x.MovementIds.Length); correction.IsComplete = true;
        AddAudit(db, c, "CanonicalReceiptIdentityCorrected", correction.Id.ToString(), beforeJson,
            new
            {
                operation.AfterReceiptSnapshotJson,
                correction.Id,
                CurrentRoomBins = state.RoomQuantity,
                ExternalCustodyBins = state.CustodyQuantity,
                HistoricalOnly = state.RoomQuantity + state.CustodyQuantity == 0,
                RetainedCustodyParents = state.Allocations.Where(x => x.Position.Location.Custody != InventoryCustody.Room).Select(x => x.Position.Location)
            }, now);
        await db.SaveChangesAsync(ct);
        var after = await new InventoryReceiptAvailability(db).ReadAsync(receipt.Id, ct);
        Require(after != null && after.Blocker == null && after.RoomQuantity == state.RoomQuantity && after.CustodyQuantity == state.CustodyQuantity,
            "Identity correction changed quantity or lost exact receipt ownership.");
        Require(after!.Allocations.All(x => x.Position.Identity.CropYear == change.Target.CropYear && x.Position.Identity.GrowerLotId == grower.Id
            && x.Position.Identity.FruitProfileId == profile.Id), "Current receipt stock retains an obsolete identity.");
        return effects.ToImmutable();
    }
}
