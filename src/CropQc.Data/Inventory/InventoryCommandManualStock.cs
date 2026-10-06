using System.Collections.Immutable;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

public sealed partial class InventoryCommandExecutor
{
    private async Task<ImmutableArray<InventoryCommandEffect>> AddManualStockAsync(CropQcDbContext db, CanonicalProjectionFactory factory, InventoryCommand c,
        InventoryAvailabilityResult before, DateTimeOffset now, int attempt, CancellationToken ct)
    {
        var line = c.Lines.Single(); var room = before.Location.RoomId!.Value;
        var target = await factory.CurrentAsync(before.Identity, before.Location.WarehouseId, room, "x", "Unknown", null, [], now, ct);
        Credit(target, line.Quantity, now);
        var credit = Ledger(c, before.Identity, before.Location.WarehouseId, room, line.Quantity, before.AuthoritativeQuantity, c.OperationKey + ":manual", now);
        credit.AdjustmentType = "ManualTrueUp";
        var movement = Move(c, before.Identity, new(target, line.Quantity), target, null, room, c.OperationKey + ":manual", now, "ManualTrueUp");
        movement.SourceSegment = null; movement.SourceSegmentId = null;
        db.RoomInventoryAdjustments.Add(credit); db.TreatmentLineageMovements.Add(movement);
        AddAudit(db, c, "CanonicalManualStockAddition", before.PositionKey, new { before.AuthoritativeQuantity, before.RawProjectionQuantity },
            new { Added = line.Quantity, Quantity = checked(before.AuthoritativeQuantity + line.Quantity), Treatment = "Unknown", ReceiptId = (long?)null }, now);
        await Stage("Movement", db, attempt, ct); await db.SaveChangesAsync(ct);
        var after = (await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(before.Location.WarehouseId, [room]),
            new(RequireKnownTreatment: false), now, ct)).Positions.Single(x => x.PositionKey == before.PositionKey);
        Require(after.IsOperable && after.AuthoritativeQuantity == before.AuthoritativeQuantity + line.Quantity && after.RawProjectionQuantity == after.AuthoritativeQuantity,
            "Explicit manual addition did not conserve its declared quantity.");
        return [new(before.PositionKey, before.AuthoritativeQuantity, after.AuthoritativeQuantity, line.Quantity, credit.Id, [credit.Id], [movement.Id])];
    }
}
