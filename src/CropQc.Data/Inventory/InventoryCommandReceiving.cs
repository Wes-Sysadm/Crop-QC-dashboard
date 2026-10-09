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
        Receipt? receipt = null;
        object? beforeReceipt = null;
        if (command.Kind == InventoryCommandKind.ActivateReceiptInventory)
        {
            Require(command.PhysicalParentId > 0 && command.ExpectedParentVersion != null, "Receipt inventory activation requires its reviewed version.");
            receipt = await db.Receipts.SingleOrDefaultAsync(x => x.Id == command.PhysicalParentId && !x.IsDeleted && !x.IsTransferReceipt, ct);
            Require(receipt != null && receipt.ConcurrencyVersion == command.ExpectedParentVersion, "Receipt changed; reload before adding its inventory.", InventoryCommandStatus.Stale);
            Require(receipt!.BinCount == p.Quantity, "Saved receipt quantity changes require an administrator correction.");
            Require(!await db.RoomInventoryAdjustments.AnyAsync(x => x.ReceiptId == receipt.Id, ct)
                && !await db.TreatmentLineageSegments.AnyAsync(x => x.ReceiptId == receipt.Id, ct)
                && !await db.TreatmentLineageMovements.AnyAsync(x => x.ReceiptId == receipt.Id, ct)
                && !await db.RoomTreatmentApplications.AnyAsync(x => x.ReceiptId == receipt.Id, ct)
                && !await db.RoomTreatmentApplicationSources.AnyAsync(x => x.ReceiptId == receipt.Id, ct)
                && !await db.BinsRunEntries.AnyAsync(x => x.ReceiptId == receipt.Id, ct)
                && !await db.RoomDepletions.AnyAsync(x => x.ReceiptId == receipt.Id, ct),
                "Existing receipt inventory or treatment history prevents ordinary receiving activation.");
            beforeReceipt = ReceiptValues(receipt);
        }
        var room = await db.Rooms.Include(x => x.Warehouse).SingleOrDefaultAsync(x => x.Id == p.RoomId, ct);
        Require(room != null && room.IsActive && room.Warehouse.IsActive && room.WarehouseId == p.WarehouseId && !room.IsSealed,
            "Receiving room is unavailable, sealed or mismatched.");
        var grower = await db.GrowerLots.SingleOrDefaultAsync(x => x.Id == p.GrowerLotId && x.IsActive, ct);
        var profile = await db.FruitProfiles.SingleOrDefaultAsync(x => x.Id == p.FruitProfileId && x.IsActive, ct);
        Require(grower != null && profile != null, "Select active, reviewed grower and fruit identities.");
        Require(OrasProductDefinition.IsValid(profile!), OrasProductDefinition.Error);
        Require(!await db.InventoryIdentityCorrections.AnyAsync(x => x.IsActive && x.IsComplete && x.CorrectedReceiptId == null
            && x.SourceCropYear == p.CropYear && x.SourceGrowerLotId == p.GrowerLotId && x.SourceFruitProfileId == p.FruitProfileId, ct),
            "Receiving identity is superseded; select its reviewed replacement.");
        var existingReceiptId = receipt?.Id;
        Require(!await db.Receipts.AnyAsync(x => x.CompuTechReceiptId == p.ReceiptNumber && !x.IsDeleted && x.Id != existingReceiptId, ct), "This Receipt ID already exists.", InventoryCommandStatus.Conflict);
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
            await PrepareDestinationAsync(db, factory, command, existing, now, attempt, ct);
            before = resolved.AuthoritativeQuantity;
        }
        var creating = receipt == null;
        receipt ??= new Receipt
        {
            CompuTechReceiptId = p.ReceiptNumber,
            GrowerName = grower.Grower,
            LotCode = grower.LotNumber,
            CreatedAt = now,
            ConcurrencyVersion = 0
        };
        receipt.CropYear = p.CropYear; receipt.WarehouseId = p.WarehouseId; receipt.RoomId = p.RoomId;
        receipt.GrowerLotId = p.GrowerLotId; receipt.FruitProfileId = p.FruitProfileId; receipt.ReceivedAt = command.EffectiveAt;
        receipt.CompuTechReceiptId = p.ReceiptNumber; receipt.GrowerName = grower.Grower;
        receipt.GrowerNumber = grower.LotNumber; receipt.LotCode = grower.LotNumber; receipt.BinCount = p.Quantity;
        receipt.ReceiptType = "Truck receipt"; receipt.UpdatedAt = now; receipt.ConcurrencyVersion++;
        if (creating) db.Receipts.Add(receipt);
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
        Require(after.AuthoritativeQuantity == checked(before + p.Quantity)
            && (IsAdmittedDestination(factory, p.RoomId, identity.Key)
                || after.IsOperable && after.RawProjectionQuantity == after.AuthoritativeQuantity),
            "Received inventory, provenance and current projections do not reconcile.");
        AddAudit(db, command, creating ? "CanonicalReceiptCreated" : "CanonicalReceiptInventoryActivated", receipt.Id.ToString(),
            new { Before = before, Receipt = beforeReceipt }, new { Receipt = p, After = after.AuthoritativeQuantity }, now);
        return [new(after.PositionKey, before, after.AuthoritativeQuantity, p.Quantity, receipt.Id, [ledger.Id], [movement.Id])];
    }
}
