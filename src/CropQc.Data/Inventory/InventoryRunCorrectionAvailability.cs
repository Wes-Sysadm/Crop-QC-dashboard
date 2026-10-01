using System.Collections.Immutable;
using CropQc.Data.Entities;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

public sealed record InventoryRunCorrectionPosition(InventoryAvailabilityResult Current,
    ImmutableArray<InventoryTreatmentSlice> AvailableAfterOwnReversal, string? Blocker);

/// <summary>Read-only, operation-specific credit from the exact CURRENT run revision.
/// Does not add the credit to physical authority or authorize any other operation.</summary>
public sealed class InventoryRunCorrectionAvailability(CropQcDbContext db)
{
    public async Task<IReadOnlyDictionary<string, InventoryRunCorrectionPosition>> ReadAsync(InventoryAvailabilityBatch current, long runId, CancellationToken ct)
    {
        var run = await db.ActualRuns.AsNoTracking().Include(x => x.Revisions).SingleAsync(x => x.Id == runId, ct);
        var revision = run.Revisions.SingleOrDefault(x => x.IsCurrent);
        var entries = await db.BinsRunEntries.AsNoTracking().Include(x => x.InventoryAdjustment)
            .Where(x => x.ActualRunId == runId && !x.IsReversed && x.TransactionType == ActualRunTransactionTypes.Depletion).ToListAsync(ct);
        var ids = entries.Select(x => x.Id).ToArray();
        var movements = await db.TreatmentLineageMovements.AsNoTracking().Include(x => x.SourceSegment).ThenInclude(x => x!.Applications)
            .Where(x => ids.Contains(x.BinsRunEntryId ?? 0)).ToListAsync(ct);
        var movementIds = movements.Select(x => x.Id).ToArray();
        var alreadyRestored = await db.TreatmentLineageMovements.AsNoTracking().AnyAsync(x => movementIds.Contains(x.ReversesTreatmentLineageMovementId ?? 0), ct)
            || await db.BinsRunEntries.AsNoTracking().AnyAsync(x => ids.Contains(x.ReversesBinsRunEntryId ?? 0), ct);
        var invalid = run.Status != ActualRunStatuses.Active || revision == null || alreadyRestored
            || entries.Any(x => x.ActualRunRevisionId != revision.Id || x.IsReconciled);
        var result = new Dictionary<string, InventoryRunCorrectionPosition>();
        foreach (var p in current.Positions)
        {
            var own = entries.Where(x => x.RoomId == p.Location.RoomId && x.WarehouseId == p.Location.WarehouseId
                && x.CropYear == p.Identity.CropYear && x.GrowerLotId == p.Identity.GrowerLotId && x.FruitProfileId == p.Identity.FruitProfileId
                && x.LotNumber == p.Identity.Lot).ToArray();
            var slices = p.TreatmentSlices.ToBuilder();
            var blocked = invalid || !p.IsOperable;
            foreach (var entry in own)
            {
                var moves = movements.Where(x => x.BinsRunEntryId == entry.Id).ToArray();
                if (entry.BinsRun <= 0 || entry.InventoryAdjustment.ChangeAmount != -entry.BinsRun
                    || moves.Length == 0 || moves.Sum(x => x.BinCount) != entry.BinsRun
                    || moves.Any(x => x.SourceSegment == null || x.SourceRoomId != entry.RoomId || x.DestinationRoomId != null || x.BinCount <= 0
                        || x.ReversesTreatmentLineageMovementId != null || InventoryStatusIdentity.NormalizeLineageKey(x.IdentityKey) != p.Identity.Key
                        || InventoryStatusIdentity.NormalizeLineageKey(x.SourceSegment!.IdentityKey) != p.Identity.Key
                        || x.TreatmentSignatureSnapshot != x.SourceSegment.TreatmentSignature || x.TreatmentStateSnapshot != x.SourceSegment.TreatmentState))
                { blocked = true; continue; }
                foreach (var move in moves)
                {
                    var source = move.SourceSegment!;
                    slices.Add(new(source.TreatmentSignature, source.TreatmentState, move.BinCount, InventoryConfidence.Proven,
                        [], source.Applications.Select(x => x.RoomTreatmentApplicationId).ToImmutableArray(), source.ReceiptId is long receipt ? [receipt] : []));
                }
            }
            result[p.PositionKey] = new(p, blocked ? [] : slices.GroupBy(x => new { x.Signature, x.State }).Select(g => new InventoryTreatmentSlice(
                g.Key.Signature, g.Key.State, g.Sum(x => x.Quantity), InventoryConfidence.Proven, g.SelectMany(x => x.ProjectionIds).Distinct().ToImmutableArray(),
                g.SelectMany(x => x.ApplicationIds).Distinct().ToImmutableArray(), g.SelectMany(x => x.ReceiptEvidenceIds).Distinct().ToImmutableArray())).ToImmutableArray(),
                blocked ? "The exact current run revision or its restoration evidence requires review." : null);
        }
        return result;
    }
}
