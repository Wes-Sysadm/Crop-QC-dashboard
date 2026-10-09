using System.Collections.Immutable;
using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using CropQc.Data.Entities;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace CropQc.Data.Inventory;

public static class InventoryReadServices
{
    public static IServiceCollection AddCanonicalInventoryReads(this IServiceCollection services)
    {
        services.AddScoped<IInventoryEvidenceLoader, InventoryEvidenceLoader>();
        services.AddScoped<IInventoryAvailability, InventoryAvailabilityResolver>();
        return services;
    }
}

/// <summary>One bounded evidence load per scope, not per displayed position. No tracked queries or SaveChanges.</summary>
public sealed partial class InventoryEvidenceLoader(CropQcDbContext db) : IInventoryEvidenceLoader
{
    public const int MaximumEvidenceRowsPerTable = 100_000;

    public async Task<InventoryEvidenceBatch> LoadAsync(InventoryScope scope, DateTimeOffset asOf, CancellationToken cancellationToken)
    {
        if (scope.WarehouseId is null && scope.RoomIds.IsDefaultOrEmpty && (scope.Custody == InventoryCustody.Room || scope.CustodyRecordId is null))
            throw new ArgumentException("Filter by warehouse, room or custody record; unbounded all-history diagnostics are not supported.", nameof(scope));
        IDbContextTransaction? owned = null;
        var consistency = "InMemorySnapshot";
        if (db.Database.IsRelational())
        {
            if (db.Database.CurrentTransaction is null)
            {
                owned = await db.Database.BeginTransactionAsync((db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite") ? IsolationLevel.Serializable : IsolationLevel.RepeatableRead, cancellationToken);
                if (db.Database.IsNpgsql()) await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
            }
            var isolation = db.Database.CurrentTransaction!.GetDbTransaction().IsolationLevel;
            if (isolation is not (IsolationLevel.RepeatableRead or IsolationLevel.Serializable or IsolationLevel.Snapshot))
                throw new InvalidOperationException("Canonical evidence requires a consistent read transaction.");
            consistency = isolation.ToString();
        }
        try
        {
            return scope.Custody == InventoryCustody.Room
                ? await LoadRoomsAsync(scope, asOf, consistency, cancellationToken)
                : await LoadCustodyAsync(scope, asOf, consistency, cancellationToken);
        }
        finally
        {
            // Disposal rolls back the read transaction. Do not commit a caller's transaction.
            if (owned is not null) await owned.DisposeAsync();
        }
    }

    private async Task<InventoryEvidenceBatch> LoadRoomsAsync(InventoryScope scope, DateTimeOffset asOf, string consistency, CancellationToken ct)
    {
        var roomIds = scope.RoomIds.IsDefaultOrEmpty ? null : scope.RoomIds.ToArray();
        var snapshots = await new RoomInventoryLedgerQueryService(db).GetSnapshotsAsOfAsync(scope.WarehouseId, roomIds, asOf, ct);
        // Include rooms with orphan projections even if the ledger has no matching position.
        var rows = await Bounded(db.RoomInventoryAdjustments.AsNoTracking().Where(x =>
            (scope.WarehouseId == null || x.WarehouseId == scope.WarehouseId) && (roomIds == null || roomIds.Contains(x.RoomId))), ct);
        var segments = await Bounded(db.TreatmentLineageSegments.AsNoTracking().Include(x => x.Applications).AsSingleQuery().Where(x =>
            (scope.WarehouseId == null || x.WarehouseId == scope.WarehouseId) && (roomIds == null || roomIds.Contains(x.RoomId))), ct);
        var rooms = rows.Select(x => x.RoomId).Concat(segments.Select(x => x.RoomId)).Distinct().ToArray();
        var movements = await Bounded(db.TreatmentLineageMovements.AsNoTracking().Where(x =>
            rooms.Contains(x.SourceRoomId ?? -1) || rooms.Contains(x.DestinationRoomId ?? -1)), ct);
        var receiptIds = rows.Where(x => x.ReceiptId != null).Select(x => x.ReceiptId!.Value)
            .Concat(segments.Where(x => x.ReceiptId != null).Select(x => x.ReceiptId!.Value))
            .Concat(movements.Where(x => x.ReceiptId != null).Select(x => x.ReceiptId!.Value)).Distinct().ToArray();
        var receipts = await Bounded(db.Receipts.AsNoTracking().Where(x => receiptIds.Contains(x.Id)), ct);
        var correctedOriginIds = receipts.Where(r => rows.Any(l => l.AdjustmentType == "ReceiptAdd" && l.ReceiptId == r.Id
            && (l.ChangeAmount != r.BinCount || l.CropYear != r.CropYear || l.GrowerLotId != r.GrowerLotId || l.FruitProfileId != r.FruitProfileId)))
            .Select(r => r.Id).ToArray();
        var originRevisions = correctedOriginIds.Length == 0 ? [] : await Bounded(db.ReceiptInventoryOverrides.AsNoTracking()
            .Where(x => correctedOriginIds.Contains(x.ReceiptId)), ct);
        var appIds = segments.SelectMany(x => x.Applications).Select(x => x.RoomTreatmentApplicationId)
            .Concat(movements.SelectMany(x => InventoryEventReplay.ApplicationIds(x.TreatmentSignatureSnapshot))).Distinct().ToArray();
        var applications = await Bounded(db.RoomTreatmentApplications.AsNoTracking().Include(x => x.Sources).AsSingleQuery().Where(x =>
            appIds.Contains(x.Id) || (x.ReceiptId == null && rooms.Contains(x.RoomId)) || receiptIds.Contains(x.ReceiptId ?? -1)), ct);
        var profileIds = snapshots.Where(x => x.FruitProfileId != null).Select(x => x.FruitProfileId!.Value)
            .Concat(segments.Where(x => x.FruitProfileId != null).Select(x => x.FruitProfileId!.Value)).Distinct().ToArray();
        var profiles = await db.FruitProfiles.AsNoTracking().Where(x => profileIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var adjustmentIds = rows.Select(x => x.Id).ToArray();
        var runParents = await db.BinsRunEntries.AsNoTracking().Where(x => adjustmentIds.Contains(x.InventoryAdjustmentId))
            .Select(x => new { x.Id, x.InventoryAdjustmentId }).ToListAsync(ct);
        var entryByAdjustment = runParents.GroupBy(x => x.InventoryAdjustmentId).Where(x => x.Count() == 1)
            .ToDictionary(x => x.Key, x => x.Single().Id);

        // Index once. No full movement scan per row; aliases meet at canonical keys.
        var rowIndex = rows.ToLookup(x => (x.RoomId, Lot: N(x.LotNumber)));
        var segmentIndex = segments.ToLookup(x => (x.RoomId, Key: InventoryStatusIdentity.NormalizeLineageKey(x.IdentityKey)));
        var segmentPositionIndex = segments.ToLookup(x => (x.RoomId, x.CropYear, x.GrowerLotId, x.FruitProfileId));
        var movementIndex = movements.SelectMany(x => new[] { x.SourceRoomId, x.DestinationRoomId }.Where(y => y != null).Distinct()
            .Select(room => (Room: room!.Value, Movement: x))).ToLookup(x => (x.Room, Key: InventoryStatusIdentity.NormalizeLineageKey(x.Movement.IdentityKey)), x => x.Movement);
        var segmentMovementIndex = movements.SelectMany(x => new[] { x.SourceSegmentId, x.DestinationSegmentId }.Where(y => y != null).Distinct()
            .Select(id => (Id: id!.Value, Movement: x))).ToLookup(x => x.Id, x => x.Movement);
        var receiptIndex = receipts.ToDictionary(x => x.Id);
        var roomApps = applications.Where(x => x.ReceiptId == null).ToLookup(x => x.RoomId);
        var receiptApps = applications.Where(x => x.ReceiptId != null).ToLookup(x => x.ReceiptId!.Value);
        var appsById = applications.ToDictionary(x => x.Id);
        var result = ImmutableArray.CreateBuilder<InventoryPositionEvidence>();
        foreach (var group in snapshots.GroupBy(x => (x.RoomId, Identity: Identity(x).Key)))
        {
            var snapshot = group.OrderBy(x => x.LatestAdjustmentId).Last();
            var identity = Identity(snapshot);
            var matchingSegments = segmentIndex[(snapshot.RoomId, identity.Key)].Concat(segmentPositionIndex[(snapshot.RoomId,
                snapshot.CropYear, snapshot.GrowerLotId, snapshot.FruitProfileId)]).DistinctBy(x => x.Id).OrderBy(x => x.Id).ToArray();
            var matchingRows = rowIndex[(snapshot.RoomId, N(snapshot.Lot))].Where(x => (x.FruitProfileId == snapshot.FruitProfileId || x.FruitProfileId == null)
                    && (x.CropYear == snapshot.CropYear || x.CropYear == null) && (x.GrowerLotId == snapshot.GrowerLotId || x.GrowerLotId == null))
                .OrderBy(x => x.Id).ToArray();
            var matchingMovements = movementIndex[(snapshot.RoomId, identity.Key)]
                .Concat(matchingSegments.SelectMany(x => segmentMovementIndex[x.Id])).DistinctBy(x => x.Id).OrderBy(x => x.Id).ToArray();
            var ids = matchingRows.Where(x => x.ReceiptId != null).Select(x => x.ReceiptId!.Value)
                .Concat(matchingSegments.Where(x => x.ReceiptId != null).Select(x => x.ReceiptId!.Value))
                .Concat(matchingMovements.Where(x => x.ReceiptId != null).Select(x => x.ReceiptId!.Value)).Distinct().Order().ToArray();
            var matchingReceipts = ids.Where(receiptIndex.ContainsKey).Select(x => receiptIndex[x]).ToArray();
            var matchingApps = roomApps[snapshot.RoomId].Concat(ids.SelectMany(id => receiptApps[id]))
                .Concat(matchingMovements.SelectMany(x => InventoryEventReplay.ApplicationIds(x.TreatmentSignatureSnapshot))
                    .Where(appsById.ContainsKey).Select(x => appsById[x]))
                .Concat(matchingSegments.SelectMany(x => x.Applications).Select(x => x.RoomTreatmentApplicationId).Where(appsById.ContainsKey).Select(x => appsById[x]))
                .DistinctBy(x => x.Id).OrderBy(x => x.Id).ToArray();
            var ledgerEvidence = matchingRows.Where(x => x.AdjustmentAt <= asOf).Select(x => new InventoryLedgerEvidence(x.Id, x.ChangeAmount,
                x.AdjustmentType, x.AdjustmentAt, x.ReceiptId, entryByAdjustment.TryGetValue(x.Id, out var entry) ? $"entry:{entry}" : Parent(x), x.WarehouseId == snapshot.WarehouseId && x.CropYear == identity.CropYear
                    && x.GrowerLotId == identity.GrowerLotId && x.FruitProfileId == identity.FruitProfileId
                    && (string.IsNullOrWhiteSpace(x.VarietyCode) || N(x.VarietyCode) == N(identity.Variety))
                    && InventoryStatusIdentity.Normalize(x.InventoryStatus, identity.ProductionType) == InventoryStatusIdentity.Normalize(identity.Status, identity.ProductionType), x.CreatedAt)
            { OperationKey = x.InventoryOperationKey, InvariantVersion = x.InventoryInvariantVersion }).ToImmutableArray();
            var projectionEvidence = matchingSegments.Select(x => Projection(x, identity, snapshot.WarehouseId)).ToImmutableArray();
            var movementEvidence = matchingMovements.Select(x => Movement(x, identity, snapshot.RoomId)).ToImmutableArray();
            var receiptEvidence = matchingReceipts.Select(x => OriginalReceipt(Receipt(x, identity), identity,
                originRevisions.Where(r => r.ReceiptId == x.Id).OrderBy(r => r.CreatedAt).ThenBy(r => r.Id).FirstOrDefault())).ToImmutableArray();
            var appEvidence = matchingApps.Select(x => new InventoryApplicationEvidence(x.Id, x.AppliedAt, x.ReversedAt, x.ReceiptId, x.RoomId)
            {
                RecordedAt = x.CreatedAt,
                Allocations = x.Sources.Where(s => InventoryStatusIdentity.NormalizeLineageKey(s.IdentityKey) == identity.Key)
                    .Select(s => new InventoryApplicationAllocation(s.Id, s.BinsTreated, s.ReceiptId,
                        s.PriorTreatmentSignature, s.ResultTreatmentSignature,
                        s.CropYear == identity.CropYear && s.GrowerLotId == identity.GrowerLotId && s.FruitProfileId == identity.FruitProfileId))
                    .OrderBy(s => s.Id).ToImmutableArray()
            }).ToImmutableArray();
            profiles.TryGetValue(snapshot.FruitProfileId ?? -1, out var profile);
            var identityVerified = identity.IsComplete && profile != null && N(profile.VarietyCode) == N(identity.Variety)
                && N(profile.ProductionType) == N(identity.ProductionType) && profile.IsOrganic == identity.IsOrganic;
            var versions = projectionEvidence.Select(x => new InventoryVersion("TreatmentLineageSegment", x.Id.ToString(), x.Version, x.UpdatedAt))
                .Concat(receiptEvidence.Select(x => new InventoryVersion("Receipt", x.Id.ToString(), x.Version, x.UpdatedAt))).ToImmutableArray();
            var physical = group.Sum(x => x.CurrentBins);
            var watermark = Watermark(new
            {
                identity,
                physical,
                ledgerEvidence,
                projectionEvidence,
                movementEvidence,
                receiptEvidence,
                appEvidence,
                Profile = profile == null ? null : new { profile.Id, profile.VarietyCode, profile.ProductionType, profile.IsOrganic }
            }, consistency, versions);
            result.Add(new(identity, new(InventoryCustody.Room, snapshot.WarehouseId, snapshot.RoomId, snapshot.Facility, snapshot.Room),
                physical, 0, identityVerified, true, ledgerEvidence, projectionEvidence, movementEvidence, receiptEvidence, appEvidence, watermark,
                matchingRows.Any(x => x.CreatedAt > asOf) || matchingSegments.Any(x => x.UpdatedAt > asOf)
                    || matchingReceipts.Any(x => x.UpdatedAt > asOf) || matchingApps.Any(x => x.CreatedAt > asOf || x.ReversedAt > asOf))
            { ApplicationAllocationsLoaded = true });
        }
        // Orphan projections are never converted into physical stock or silently dropped.
        var present = result.Select(x => (x.Location.RoomId, x.Identity.Key)).ToHashSet();
        foreach (var group in segments.GroupBy(x => (RoomId: (int?)x.RoomId, Key: InventoryStatusIdentity.NormalizeLineageKey(x.IdentityKey))).Where(x => !present.Contains(x.Key)))
        {
            var first = group.First();
            var identity = Identity(first);
            var projections = group.OrderBy(x => x.Id).Select(x => Projection(x, identity, first.WarehouseId)).ToImmutableArray();
            result.Add(new(identity, new(InventoryCustody.Room, first.WarehouseId, first.RoomId, "", $"Room {first.RoomId}"),
                0, 0, false, true, [], projections, [], [], [], Watermark(projections, consistency, [])));
        }
        return new(result.ToImmutable(), rows.Count + segments.Count + movements.Count + receipts.Count + originRevisions.Count + applications.Count + profiles.Count + runParents.Count);
    }

    internal static InventoryIdentity Identity(RoomInventoryLedgerSnapshot x) => new(x.CropYear, x.GrowerLotId, x.FruitProfileId,
        x.Lot, x.GrowerNumber, x.Variety, x.ProductionType, x.IsOrganic, x.InventoryStatus);
    private static InventoryReceiptEvidence OriginalReceipt(InventoryReceiptEvidence receipt, InventoryIdentity identity,
        ReceiptInventoryOverride? revision)
    {
        if (revision == null) return receipt;
        try
        {
            using var json = JsonDocument.Parse(revision.BeforeReceiptSnapshotJson);
            var original = json.RootElement;
            return receipt with
            {
                OriginalQuantity = original.GetProperty("binCount").GetInt32(),
                OriginalIdentityVerified = revision.IsComplete && original.GetProperty("id").GetInt64() == receipt.Id
                    && original.GetProperty("cropYear").GetInt32() == identity.CropYear
                    && original.GetProperty("growerLotId").GetInt32() == identity.GrowerLotId
                    && original.GetProperty("fruitProfileId").GetInt32() == identity.FruitProfileId
            };
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
        { return receipt with { OriginalIdentityVerified = false }; }
    }
    private static InventoryIdentity Identity(TreatmentLineageSegment x) => new(x.CropYear, x.GrowerLotId, x.FruitProfileId,
        x.LotNumberSnapshot, x.GrowerNumberSnapshot, x.VarietyCodeSnapshot, x.ProductionTypeSnapshot, x.IsOrganicSnapshot, x.InventoryStatusSnapshot ?? "");
    private static InventoryProjectionEvidence Projection(TreatmentLineageSegment x, InventoryIdentity identity, int warehouse) =>
        new(x.Id, x.IdentityKey, x.CurrentBins, x.TreatmentState, x.TreatmentSignature, x.ReceiptId, x.CreatedAt, x.UpdatedAt,
            x.ConcurrencyVersion, Identity(x).Key == identity.Key && InventoryStatusIdentity.NormalizeLineageKey(x.IdentityKey) == identity.Key
                && x.WarehouseId == warehouse, x.Applications.OrderBy(a => a.Sequence).Select(a => a.RoomTreatmentApplicationId).ToImmutableArray(), x.Disposition, x.RetiredQuantity)
        { CohortKey = x.CohortKey };
    private static InventoryMovementEvidence Movement(TreatmentLineageMovement x, InventoryIdentity identity, int room) =>
        new(x.Id, x.MovementType, x.BinCount, x.OccurredAt, x.CreatedAt, x.TreatmentSignatureSnapshot, x.TreatmentStateSnapshot,
            x.ReceiptId, x.SourceSegmentId, x.DestinationSegmentId, x.DestinationRoomId == room, x.SourceRoomId == room,
            Parent(x), x.ReversesTreatmentLineageMovementId, InventoryStatusIdentity.NormalizeLineageKey(x.IdentityKey) == identity.Key)
        { OperationKey = x.OperationKey };
    private static InventoryReceiptEvidence Receipt(Receipt x, InventoryIdentity identity) => new(x.Id, x.BinCount,
        x.CropYear == identity.CropYear && x.GrowerLotId == identity.GrowerLotId && x.FruitProfileId == identity.FruitProfileId
            && N(x.GrowerNumber ?? x.LotCode) == N(identity.Lot), x.IsDeleted, x.IsTransferReceipt, x.UpdatedAt, x.ConcurrencyVersion);
    private static string? Parent(RoomInventoryAdjustment x) => x.RoomTransferId is long rt ? $"room:{rt}" : x.InterCrewTransferId is long it ? $"crew:{it}"
        : x.OutsideWarehouseTransferId is long ot ? $"outside:{ot}" : x.ProcessorShipmentLineId is long ps ? $"processor:{ps}"
        : x.RoomInventoryLossId is long loss ? $"loss:{loss}" : x.InventoryIdentityCorrectionId is Guid correction ? $"identity:{correction}"
        : x.ActualRunId is long ar ? $"run:{ar}" : null;
    private static string? Parent(TreatmentLineageMovement x) => x.RoomTransferId is long rt ? $"room:{rt}" : x.InterCrewTransferId is long it ? $"crew:{it}"
        : x.OutsideWarehouseTransferId is long ot ? $"outside:{ot}" : x.ProcessorShipmentLineId is long ps ? $"processor:{ps}"
        : x.RoomInventoryLossId is long loss ? $"loss:{loss}" : x.InventoryIdentityCorrectionId is Guid correction ? $"identity:{correction}"
        : x.BinsRunEntryId is long br ? $"entry:{br}" : null;
    private static string N(string? x) => (x ?? "").Trim().ToUpperInvariant();
    private static InventoryReadWatermark Watermark<T>(T evidence, string consistency, ImmutableArray<InventoryVersion> versions) =>
        new(Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(evidence))), consistency, versions);
    private static async Task<List<T>> Bounded<T>(IQueryable<T> query, CancellationToken ct)
    {
        var rows = await query.Take(MaximumEvidenceRowsPerTable + 1).ToListAsync(ct);
        return rows.Count <= MaximumEvidenceRowsPerTable ? rows : throw new InvalidOperationException("Evidence scope exceeds the safe row limit. Narrow the diagnostic scope.");
    }
}
