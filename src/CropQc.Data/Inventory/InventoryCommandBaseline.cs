using System.Collections.Immutable;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

public sealed partial class InventoryCommandExecutor
{
    private async Task<ImmutableArray<InventoryCommandEffect>> ImportBaselineAsync(CropQcDbContext db, CanonicalProjectionFactory factory,
        InventoryCommand c, DateTimeOffset now, int attempt, CancellationToken ct)
    {
        Require(c.Lines.IsEmpty && c.Baseline != null && !string.IsNullOrWhiteSpace(c.Baseline.ExpectedFingerprint), "Baseline requires its reviewed complete import intent.");
        var plan = await new CanonicalBaselinePreview(db).PlanAsync(c.Baseline!.Rows, now, ct);
        Require(plan.Review.Fingerprint == c.Baseline.ExpectedFingerprint, "Inventory or baseline input changed; preview the import again.", InventoryCommandStatus.Stale);
        Require(!plan.Review.RequiresReplacement || c.Baseline.ConfirmReplacement, "Confirm replacement of the existing baseline batch.");
        await Stage("BaselineResolved", db, attempt, ct);
        foreach (var e in plan.Evidence.Positions.Where(e => plan.Review.Positions.Any(p => p.Imported && p.RoomId == e.Location.RoomId && p.Identity.Key == e.Identity.Key)))
        {
            var result = InventoryAvailabilityResolver.Resolve(e, new(RequireKnownTreatment: false));
            if (result.RawProjectionQuantity != result.AuthoritativeQuantity)
                await NormalizePositionAsync(db, factory, c, e, InventoryAvailabilityResolver.Resolve(e, new()), now, attempt, ct);
        }
        var rowIndex = 0;
        foreach (var row in plan.Rows)
        {
            row.Id = 0; row.CreatedByUserId = c.ActorId; row.InventoryOperationKey = $"{c.OperationKey}:baseline:{rowIndex++}";
            db.RoomInventoryAdjustments.Add(row);
        }
        var importedRoomIds = plan.Rows.Select(x => x.RoomId).Distinct().ToArray();
        var importedRooms = await db.Rooms.Where(x => importedRoomIds.Contains(x.Id)).ToListAsync(ct);
        foreach (var room in importedRooms)
        {
            var input = c.Baseline.Rows.First(x => x.RoomId == room.Id);
            room.SubLocation = input.SourceSubLocation; room.CompuTechRoomCode = input.SourceRoomCode;
            room.DisplayName = input.RoomDisplayName; room.CropQcRoomName = input.RoomDisplayName; room.SortOrder = input.RoomSortOrder;
        }
        await db.SaveChangesAsync(ct);
        await Stage("BaselineLedger", db, attempt, ct);
        var effects = ImmutableArray.CreateBuilder<InventoryCommandEffect>();
        foreach (var p in plan.Review.Positions.Where(x => x.Imported))
        {
            var delta = checked(p.After - p.Before);
            var movements = ImmutableArray<long>.Empty;
            if (delta > 0)
            {
                var target = await factory.CurrentAsync(p.Identity, p.WarehouseId, p.RoomId, "x", "Unknown", null, [], now, ct);
                Credit(target, delta, now);
                var movement = Move(c, p.Identity, new(target, delta), target, null, p.RoomId, $"{c.OperationKey}:baseline:{effects.Count}", now, "BaselineImport");
                movement.SourceSegment = null; movement.SourceSegmentId = null;
                db.TreatmentLineageMovements.Add(movement);
                await db.SaveChangesAsync(ct);
                movements = [movement.Id];
            }
            effects.Add(new($"Room:{p.WarehouseId}:{p.RoomId}::{p.Identity.Key}", p.Before, p.After, delta, null,
                plan.Rows.Where(x => x.RoomId == p.RoomId && x.CropYear == p.Identity.CropYear && x.GrowerLotId == p.Identity.GrowerLotId && x.FruitProfileId == p.Identity.FruitProfileId)
                    .Select(x => x.Id).ToImmutableArray(), movements));
        }
        await Stage("Movement", db, attempt, ct);
        var rooms = c.Baseline.Rows.Select(x => x.RoomId).Distinct().ToImmutableArray();
        var after = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(null, rooms), new(RequireKnownTreatment: false), now, ct);
        Require(after.Positions.Length == plan.Review.Positions.Length && plan.Review.Positions.All(p => after.Positions.Any(x => x.Location.RoomId == p.RoomId
            && x.Identity.Key == p.Identity.Key && x.AuthoritativeQuantity == p.After
            && (p.Imported ? x.RawProjectionQuantity == p.After && x.IsOperable
                : x.Watermark.Fingerprint == plan.Evidence.Positions.Single(e => e.Location.RoomId == p.RoomId && e.Identity.Key == p.Identity.Key).Watermark.Fingerprint))),
            "Committed baseline differs from its complete room forecast or current projections.");
        AddAudit(db, c, "CanonicalBaselineImport", c.OperationKey, new { Inventory = plan.Review.Positions.Select(x => new { x.RoomId, x.Identity, Quantity = x.Before }), plan.Rooms },
            new
            {
                plan.Review,
                LedgerIds = plan.Rows.Select(x => x.Id),
                Rooms = importedRooms.Select(x => new { x.Id, x.SubLocation, x.CompuTechRoomCode, x.CropQcRoomName, x.DisplayName, x.SortOrder }),
                AddedTreatment = "Unknown",
                ReceiptProvenance = "Unattributed"
            }, now);
        return effects.ToImmutable();
    }
}
