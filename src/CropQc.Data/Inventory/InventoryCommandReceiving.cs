using System.Collections.Immutable;
using CropQc.Data.Entities;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

public sealed partial class InventoryCommandExecutor
{
    private async Task<ImmutableArray<InventoryCommandEffect>> ReceiveStockAsync(CropQcDbContext db,
        CanonicalProjectionFactory factory, InventoryCommand command, DateTimeOffset now, int attempt, CancellationToken ct)
    {
        var input = command.Receipt;
        Require(input is { Quantity: > 0, CropYear: >= 2000 and <= 2200 } && command.Lines.IsEmpty
            && input.ReceiptType == "Truck receipt" && !string.IsNullOrWhiteSpace(input.ReceiptNumber) && input.ReceiptNumber.Length <= 50,
            "Receiving requires a complete ordinary Truck receipt intent.", InventoryCommandStatus.InvalidIntent);
        var p = input!;
        var room = await db.Rooms.Include(x => x.Warehouse).SingleOrDefaultAsync(x => x.Id == p.RoomId, ct);
        Require(room != null && room.IsActive && room.Warehouse.IsActive && room.WarehouseId == p.WarehouseId && !room.IsSealed,
            "Receiving room is unavailable, sealed or mismatched.");
        var grower = await db.GrowerLots.SingleOrDefaultAsync(x => x.Id == p.GrowerLotId && x.IsActive, ct);
        var profile = await db.FruitProfiles.SingleOrDefaultAsync(x => x.Id == p.FruitProfileId && x.IsActive, ct);
        Require(grower != null && profile != null, "Select active, reviewed grower and fruit identities.");
        Require(!await db.InventoryIdentityCorrections.AnyAsync(x => x.IsActive && x.IsComplete && x.CorrectedReceiptId == null
            && x.SourceCropYear == p.CropYear && x.SourceGrowerLotId == p.GrowerLotId && x.SourceFruitProfileId == p.FruitProfileId, ct),
            "Receiving identity is superseded; select its reviewed replacement.");
        Require(!await db.Receipts.AnyAsync(x => x.CompuTechReceiptId == p.ReceiptNumber && !x.IsDeleted, ct), "This Receipt ID already exists.", InventoryCommandStatus.Conflict);
        var identity = new InventoryIdentity(p.CropYear, p.GrowerLotId, p.FruitProfileId, grower!.LotNumber, grower.LotNumber,
            profile!.VarietyCode, profile.ProductionType, profile.IsOrganic, "");
        Require(identity.IsComplete, "Receiving identity is incomplete.");
        var loader = new InventoryEvidenceLoader(db);
        var evidence = await loader.LoadAsync(new(p.WarehouseId, [p.RoomId]), now, ct);
        var existing = evidence.Positions.SingleOrDefault(x => x.Identity.Key == identity.Key);
        var before = 0;
        if (existing != null)
        {
            var resolved = InventoryAvailabilityResolver.Resolve(existing, new());
            Require(resolved.IsOperable, "Existing inventory identity or treatment requires review before receiving into this position.");
            before = resolved.AuthoritativeQuantity;
            await NormalizePositionAsync(db, factory, command, existing, resolved, now, attempt, ct);
        }
        var receipt = new Receipt
        {
            CropYear = p.CropYear,
            WarehouseId = p.WarehouseId,
            RoomId = p.RoomId,
            GrowerLotId = p.GrowerLotId,
            FruitProfileId = p.FruitProfileId,
            ReceivedAt = command.EffectiveAt,
            CompuTechReceiptId = p.ReceiptNumber,
            GrowerName = grower.Grower,
            GrowerNumber = grower.LotNumber,
            LotCode = grower.LotNumber,
            BinCount = p.Quantity,
            ReceiptType = "Truck receipt",
            CreatedAt = now,
            UpdatedAt = now,
            ConcurrencyVersion = 1
        };
        db.Receipts.Add(receipt);
        await db.SaveChangesAsync(ct);
        var projection = await factory.CurrentAsync(identity, p.WarehouseId, p.RoomId, "u", "Untreated", receipt.Id, [], now, ct);
        Credit(projection, p.Quantity, now);
        var ledger = Ledger(command, identity, p.WarehouseId, p.RoomId, p.Quantity, before, command.OperationKey + ":receipt", now);
        ledger.Receipt = receipt; ledger.AdjustmentType = "ReceiptAdd"; ledger.GrowerName = grower.Grower;
        var movement = new TreatmentLineageMovement
        {
            OperationKey = command.OperationKey + ":receipt",
            MovementType = "Receipt",
            DestinationSegment = projection,
            DestinationRoomId = p.RoomId,
            Receipt = receipt,
            IdentityKey = identity.Key,
            TreatmentStateSnapshot = "Untreated",
            TreatmentSignatureSnapshot = "u",
            BinCount = p.Quantity,
            OccurredAt = command.EffectiveAt,
            CreatedAt = now,
            CreatedByUserId = command.ActorId
        };
        db.RoomInventoryAdjustments.Add(ledger); db.TreatmentLineageMovements.Add(movement);
        await Stage("Movement", db, attempt, ct);
        await db.SaveChangesAsync(ct);
        var after = InventoryAvailabilityResolver.Resolve((await loader.LoadAsync(new(p.WarehouseId, [p.RoomId]), now, ct))
            .Positions.Single(x => x.Identity.Key == identity.Key), new());
        Require(after.IsOperable && after.AuthoritativeQuantity == checked(before + p.Quantity)
            && after.RawProjectionQuantity == after.AuthoritativeQuantity, "Received inventory, provenance and current projections do not reconcile.");
        AddAudit(db, command, "CanonicalReceiptCreated", receipt.Id.ToString(), new { Before = before }, new { Receipt = p, After = after.AuthoritativeQuantity }, now);
        return [new(after.PositionKey, before, after.AuthoritativeQuantity, p.Quantity, receipt.Id, [ledger.Id], [movement.Id])];
    }
}
