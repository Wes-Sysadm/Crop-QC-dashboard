using System.Collections.Immutable;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

public sealed partial class InventoryCommandExecutor
{
    private static async Task<ImmutableArray<InventoryCommandEffect>> ReverseCurrentTreatmentAsync(CropQcDbContext db, CanonicalProjectionFactory factory,
        InventoryCommand c, InventoryAvailabilityResult[] reviewed, DateTimeOffset now, CancellationToken ct)
    {
        var app = await db.RoomTreatmentApplications.SingleOrDefaultAsync(x => x.Id == c.TreatmentApplicationId, ct);
        Require(app != null && app.ReversedAt == null, "The active treatment application was not found.");
        var current = await db.TreatmentLineageSegments.Include(x => x.Applications).Where(x => x.Disposition == "Current" && x.CurrentBins > 0
            && x.Applications.Any(a => a.RoomTreatmentApplicationId == app!.Id)).ToListAsync(ct);
        var historicalRooms = await db.TreatmentLineageSegments.Where(x => x.Applications.Any(a => a.RoomTreatmentApplicationId == app!.Id))
            .Select(x => x.RoomId).Distinct().ToArrayAsync(ct);
        var custody = new List<InventoryAvailabilityResult>();
        if (historicalRooms.Length > 0)
            foreach (var kind in new[] { InventoryCustody.InTransit, InventoryCustody.OutsideWarehouse, InventoryCustody.Processor })
            {
                var batch = await new InventoryEvidenceLoader(db).LoadAsync(new(null, historicalRooms.ToImmutableArray(), kind), now, ct);
                foreach (var evidence in batch.Positions.Where(x => x.Applications.Any(a => a.Id == app!.Id)))
                {
                    var result = InventoryAvailabilityResolver.Resolve(evidence, new(AllowedCustody: kind));
                    Require(result.IsOperable, "Current external custody treatment could not be proven for this reversal.");
                    custody.Add(result);
                }
            }
        Require(current.All(row => c.Lines.Any(line => line.Source.Location.RoomId == row.RoomId
            && line.Source.Identity.Key == InventoryStatusIdentity.NormalizeLineageKey(row.IdentityKey) && line.TreatmentSignature == row.TreatmentSignature)),
            "Treatment inventory moved or changed; reload the reversal.", InventoryCommandStatus.Stale);
        Require(c.Lines.All(line => current.Where(row => row.RoomId == line.Source.Location.RoomId
            && InventoryStatusIdentity.NormalizeLineageKey(row.IdentityKey) == line.Source.Identity.Key && row.TreatmentSignature == line.TreatmentSignature)
            .Sum(row => row.CurrentBins) == line.Quantity), "Reversal must include every surviving treatment allocation.", InventoryCommandStatus.Stale);
        var before = current.Select(x => new { x.Id, x.CurrentBins, x.ConcurrencyVersion, x.TreatmentSignature, x.Disposition }).ToArray();
        foreach (var row in current)
        {
            var p = reviewed.First(x => x.Location.RoomId == row.RoomId && x.Identity.Key == InventoryStatusIdentity.NormalizeLineageKey(row.IdentityKey));
            var ids = row.Applications.Select(x => x.RoomTreatmentApplicationId).Where(x => x != app!.Id).Order().ToArray();
            var signature = ids.Length == 0 ? "u" : "u|a:" + string.Join(',', ids);
            var target = await factory.CurrentAsync(p.Identity, row.WarehouseId, row.RoomId, signature,
                ids.Length == 0 ? "Untreated" : "Confirmed", row.ReceiptId, ids, now, ct);
            var quantity = row.CurrentBins;
            CanonicalProjectionFactory.Retire(row, c.OperationKey, now);
            Credit(target, quantity, now);
        }
        app!.ReversedAt = now; app.ReversedByUserId = c.ActorId; app.ReversalReason = c.Reason;
        AddAudit(db, c, "CanonicalTreatmentReversed", app.Id.ToString(), new { app.TotalBinsSnapshot, app.AppliedAt, Segments = before },
            new { app.ReversedAt, app.ReversedByUserId, app.ReversalReason, SurvivingBins = before.Sum(x => x.CurrentBins) }, now);
        await db.SaveChangesAsync(ct);
        var effects = ImmutableArray.CreateBuilder<InventoryCommandEffect>();
        foreach (var p in reviewed.DistinctBy(x => x.PositionKey))
        {
            var after = (await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(new(p.Location.WarehouseId,
                [p.Location.RoomId!.Value]), new(), now, ct)).Positions.Single(x => x.PositionKey == p.PositionKey);
            Require(after.IsOperable && after.AuthoritativeQuantity == p.AuthoritativeQuantity && after.RawProjectionQuantity == after.AuthoritativeQuantity,
                "Treatment reversal did not preserve current quantity and treatment proof.");
            effects.Add(new(p.PositionKey, p.AuthoritativeQuantity, after.AuthoritativeQuantity, c.Lines.Where(x => x.Source.Location == p.Location
                && x.Source.Identity.Key == p.Identity.Key).Sum(x => x.Quantity), app.Id, [], []));
        }
        foreach (var p in custody)
        {
            var after = (await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(
                new(p.Location.WarehouseId, [], p.Location.Custody, p.Location.CustodyRecordId),
                new(AllowedCustody: p.Location.Custody), now, ct)).Positions.Single(x => x.Identity.Key == p.Identity.Key);
            Require(after.IsOperable && after.AuthoritativeQuantity == p.AuthoritativeQuantity && after.TreatmentSlices.All(x => !x.ApplicationIds.Contains(app.Id)),
                "Audited treatment reversal did not preserve current custody proof.");
            effects.Add(new(p.PositionKey, p.AuthoritativeQuantity, after.AuthoritativeQuantity, 0, app.Id, [], []));
        }
        return effects.ToImmutable();
    }
}
