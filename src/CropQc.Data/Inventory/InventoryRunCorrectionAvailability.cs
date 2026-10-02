using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
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
    public async Task<IReadOnlyDictionary<string, InventoryRunCorrectionPosition>> ReadLegacyAsync(InventoryAvailabilityBatch current, long entryId, CancellationToken ct)
    {
        var entries = await db.BinsRunEntries.AsNoTracking().Include(x => x.InventoryAdjustment).Where(x => x.Id == entryId).ToListAsync(ct);
        return await ReadEntriesAsync(current, entries, entries.Count != 1 || entries.Any(x => x.ActualRunId != null || x.IsReversed || x.IsReconciled
            || x.TransactionType != ActualRunTransactionTypes.Legacy), ct);
    }

    public async Task<IReadOnlyDictionary<string, InventoryRunCorrectionPosition>> ReadAsync(InventoryAvailabilityBatch current, long runId, CancellationToken ct)
    {
        var run = await db.ActualRuns.AsNoTracking().Include(x => x.Revisions).SingleAsync(x => x.Id == runId, ct);
        var revision = run.Revisions.SingleOrDefault(x => x.IsCurrent);
        var entries = await db.BinsRunEntries.AsNoTracking().Include(x => x.InventoryAdjustment)
            .Where(x => x.ActualRunId == runId && !x.IsReversed && x.TransactionType == ActualRunTransactionTypes.Depletion).ToListAsync(ct);
        return await ReadEntriesAsync(current, entries, run.Status != ActualRunStatuses.Active || revision == null
            || entries.Any(x => x.ActualRunRevisionId != revision.Id || x.IsReconciled), ct);
    }

    private async Task<IReadOnlyDictionary<string, InventoryRunCorrectionPosition>> ReadEntriesAsync(InventoryAvailabilityBatch current,
        List<BinsRunEntry> entries, bool invalid, CancellationToken ct)
    {
        var ids = entries.Select(x => x.Id).ToArray();
        var movements = await db.TreatmentLineageMovements.AsNoTracking().Include(x => x.SourceSegment).ThenInclude(x => x!.Applications)
            .Where(x => ids.Contains(x.BinsRunEntryId ?? 0)).ToListAsync(ct);
        var movementIds = movements.Select(x => x.Id).ToArray();
        var alreadyRestored = await db.TreatmentLineageMovements.AsNoTracking().AnyAsync(x => movementIds.Contains(x.ReversesTreatmentLineageMovementId ?? 0), ct)
            || await db.BinsRunEntries.AsNoTracking().AnyAsync(x => ids.Contains(x.ReversesBinsRunEntryId ?? 0), ct);
        invalid |= alreadyRestored;
        var identities = await CanonicalIdentityMap.LoadAsync(db, movements.Where(x => x.ReceiptId != null).Select(x => x.ReceiptId!.Value).Distinct().ToArray(), ct);
        var appIds = movements.Where(x => x.SourceSegment != null).SelectMany(x => x.SourceSegment!.Applications).Select(x => x.RoomTreatmentApplicationId).Distinct().ToArray();
        var applications = await db.RoomTreatmentApplications.AsNoTracking().Where(x => appIds.Contains(x.Id))
            .Select(x => new InventoryApplicationEvidence(x.Id, x.AppliedAt, x.ReversedAt, x.ReceiptId, x.RoomId)).ToArrayAsync(ct);
        foreach (var entry in entries)
        {
            var moves = movements.Where(x => x.BinsRunEntryId == entry.Id).ToArray();
            invalid |= entry.BinsRun <= 0 || entry.InventoryAdjustment.ChangeAmount != -entry.BinsRun
                || moves.Length == 0 || moves.Sum(x => x.BinCount) != entry.BinsRun
                || moves.Any(x => x.SourceSegment == null || x.SourceRoomId != entry.RoomId || x.DestinationRoomId != null || x.BinCount <= 0
                    || x.ReversesTreatmentLineageMovementId != null || x.ReceiptId != x.SourceSegment.ReceiptId
                    || InventoryStatusIdentity.NormalizeLineageKey(x.IdentityKey) != CanonicalIdentityMap.Historical(x.SourceSegment).Key
                    || x.SourceSegment.CropYear != entry.InventoryAdjustment.CropYear || x.SourceSegment.GrowerLotId != entry.InventoryAdjustment.GrowerLotId
                    || x.SourceSegment.FruitProfileId != entry.InventoryAdjustment.FruitProfileId || x.SourceSegment.LotNumberSnapshot != entry.InventoryAdjustment.LotNumber
                    || x.TreatmentSignatureSnapshot != x.SourceSegment.TreatmentSignature || x.TreatmentStateSnapshot != x.SourceSegment.TreatmentState);
        }
        var result = new Dictionary<string, InventoryRunCorrectionPosition>();
        var positions = current.Positions.ToList();
        // A fully consumed receipt can be corrected to a lot with no ledger rows.
        // Expose zero physical stock plus only this run's proved restoration credit.
        // This preview is not available to unrelated inventory consumers.
        foreach (var group in movements.Where(x => x.SourceSegment != null && x.SourceRoomId != null).GroupBy(x => new
        {
            x.SourceSegment!.WarehouseId,
            RoomId = x.SourceRoomId!.Value,
            Identity = identities.Resolve(CanonicalIdentityMap.Historical(x.SourceSegment), x.ReceiptId).Current
        }))
        {
            var key = group.Key;
            var location = current.Positions.FirstOrDefault(x => x.Location.WarehouseId == key.WarehouseId && x.Location.RoomId == key.RoomId)?.Location;
            if (location == null || positions.Any(x => x.Location == location && x.Identity.Key == key.Identity.Key)) continue;
            var watermark = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                key.Identity,
                Room = current.Positions.Where(x => x.Location == location).OrderBy(x => x.PositionKey).Select(x => x.Watermark.Fingerprint).ToArray(),
                Movements = group.OrderBy(x => x.Id).Select(x => new { x.Id, x.BinCount, x.ReceiptId }).ToArray(),
                Corrections = identities.Corrections.Select(x => new { x.Id, x.IsComplete, x.IsActive, x.CreatedAt }).ToArray()
            })));
            positions.Add(InventoryAvailabilityResolver.Resolve(new(key.Identity, location, 0, 0, key.Identity.IsComplete, true,
                [], [], [], [], [], new(watermark, "ExactRunRestorationPreview", [])), new()));
        }
        foreach (var p in positions)
        {
            var slices = p.TreatmentSlices.ToBuilder();
            var blocked = invalid || !p.IsOperable;
            foreach (var move in movements.Where(x => x.SourceSegment != null && x.SourceRoomId == p.Location.RoomId
                && x.SourceSegment.WarehouseId == p.Location.WarehouseId
                && identities.Resolve(CanonicalIdentityMap.Historical(x.SourceSegment), x.ReceiptId).Current.Key == p.Identity.Key))
            {
                var source = move.SourceSegment!;
                var effective = InventoryEffectiveTreatment.Read(source.TreatmentSignature, source.TreatmentState,
                    source.Applications.Select(x => x.RoomTreatmentApplicationId).ToImmutableArray(), applications);
                slices.Add(new(effective.Signature, effective.State, move.BinCount, InventoryConfidence.Proven,
                    [], effective.ApplicationIds, source.ReceiptId is long receipt ? [receipt] : []));
            }
            result[p.PositionKey] = new(p, blocked ? [] : slices.GroupBy(x => new { x.Signature, x.State }).Select(g => new InventoryTreatmentSlice(
                g.Key.Signature, g.Key.State, g.Sum(x => x.Quantity), InventoryConfidence.Proven, g.SelectMany(x => x.ProjectionIds).Distinct().ToImmutableArray(),
                g.SelectMany(x => x.ApplicationIds).Distinct().ToImmutableArray(), g.SelectMany(x => x.ReceiptEvidenceIds).Distinct().ToImmutableArray())).ToImmutableArray(),
                blocked ? "The exact current run revision or its restoration evidence requires review." : null);
        }
        return result;
    }
}
