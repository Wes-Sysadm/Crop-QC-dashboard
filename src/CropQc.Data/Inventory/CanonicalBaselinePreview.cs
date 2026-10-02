using System.Collections.Immutable;
using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using CropQc.Data.Entities;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

public sealed record CanonicalBaselinePosition(InventoryIdentity Identity, int WarehouseId, int RoomId, int Before, int After, bool Imported);
public sealed record CanonicalBaselineReview(string Fingerprint, bool RequiresReplacement,
    ImmutableArray<CanonicalBaselinePosition> Positions);
internal sealed record CanonicalBaselinePlan(CanonicalBaselineReview Review, IReadOnlyList<RoomInventoryAdjustment> Rows,
    InventoryEvidenceBatch Evidence, IReadOnlyList<CanonicalBaselineRoomMetadata> Rooms);
internal sealed record CanonicalBaselineRoomMetadata(int RoomId, string? SubLocation, string? CropQcRoomName,
    string? CompuTechRoomCode, string? DisplayName, int SortOrder);

/// <summary>Read-only room-wide forecast. Commit repeats this under the executor's Serializable transaction.</summary>
public sealed class CanonicalBaselinePreview(CropQcDbContext db)
{
    public async Task<CanonicalBaselineReview> PreviewAsync(ImmutableArray<InventoryBaselineRow> rows, CancellationToken ct)
    {
        await using var tx = db.Database.CurrentTransaction == null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct) : null;
        if (tx != null) await db.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
        return (await PlanAsync(rows, DateTimeOffset.UtcNow, ct)).Review;
    }

    internal async Task<CanonicalBaselinePlan> PlanAsync(ImmutableArray<InventoryBaselineRow> rows, DateTimeOffset now, CancellationToken ct)
    {
        Check(!rows.IsDefaultOrEmpty && rows.Length <= 500, "Select between 1 and 500 baseline rows.");
        Check(rows.All(x => x.CropYear is >= 2000 and <= 2200 && x.Quantity >= 0 && x.EffectiveAt <= now
            && x.EffectiveAt.Offset == TimeSpan.Zero && !string.IsNullOrWhiteSpace(x.Source)), "Invalid baseline quantity, date or source.");
        var roomIds = rows.Select(x => x.RoomId).Distinct().Order().ToArray();
        var growerIds = rows.Select(x => x.GrowerLotId).Distinct().ToArray();
        var profileIds = rows.Select(x => x.FruitProfileId).Distinct().ToArray();
        Check(rows.GroupBy(x => x.RoomId).All(x => x.Select(y => y.EffectiveAt).Distinct().Count() == 1),
            "A baseline import must use one effective date per room.");
        Check(rows.GroupBy(x => x.RoomId).All(x => x.Select(y => (y.SourceRoomCode, y.SourceSubLocation, y.RoomDisplayName, y.RoomSortOrder)).Distinct().Count() == 1),
            "Use consistent room names and locations across baseline rows.");
        Check(rows.Select(x => (x.RoomId, x.CropYear, x.GrowerLotId, x.FruitProfileId)).Distinct().Count() == rows.Length,
            "Duplicate canonical baseline identity; status aliases cannot create separate physical stock.");
        var rooms = await db.Rooms.AsNoTracking().Include(x => x.Warehouse).Where(x => roomIds.Contains(x.Id)).ToListAsync(ct);
        var growers = await db.GrowerLots.AsNoTracking().Where(x => growerIds.Contains(x.Id)).ToListAsync(ct);
        var profiles = await db.FruitProfiles.AsNoTracking().Where(x => profileIds.Contains(x.Id)).ToListAsync(ct);
        var oldRows = await db.RoomInventoryAdjustments.AsNoTracking().Where(x => roomIds.Contains(x.RoomId))
            .OrderBy(x => x.Id).Take(InventoryEvidenceLoader.MaximumEvidenceRowsPerTable + 1).ToListAsync(ct);
        Check(oldRows.Count <= InventoryEvidenceLoader.MaximumEvidenceRowsPerTable && oldRows.All(x => x.CreatedAt < now),
            "Baseline evidence exceeds its safe bound or contains future creation timestamps.");
        var corrections = await db.InventoryIdentityCorrections.AsNoTracking().Where(x => x.IsActive && x.IsComplete
            && x.CorrectedReceiptId == null && growerIds.Contains(x.SourceGrowerLotId ?? -1)).ToListAsync(ct);
        var replacements = false;
        var proposed = new List<RoomInventoryAdjustment>();
        foreach (var row in rows)
        {
            var room = rooms.SingleOrDefault(x => x.Id == row.RoomId);
            var grower = growers.SingleOrDefault(x => x.Id == row.GrowerLotId && x.IsActive);
            var profile = profiles.SingleOrDefault(x => x.Id == row.FruitProfileId && x.IsActive);
            Check(room != null && room.WarehouseId == row.WarehouseId && room.IsActive && room.Warehouse.IsActive,
                "Select an active room and matching warehouse.");
            Check(grower != null && profile != null && grower.LotNumber == row.Lot && profile.VarietyCode == row.Variety,
                "Baseline needs an exact active Master Data lot and fruit profile.");
            Check(!corrections.Any(x => x.SourceCropYear == row.CropYear && x.SourceGrowerLotId == row.GrowerLotId && x.SourceFruitProfileId == row.FruitProfileId),
                "Baseline identity has a reviewed replacement; use its current identity.");
            var prior = oldRows.Where(x => x.ReceiptId == null && x.AdjustmentType == InventoryLedgerKinds.StartingInventoryImport
                && x.RoomId == row.RoomId && x.CropYear == row.CropYear && x.LotNumber == row.Lot && x.VarietyCode == row.Variety)
                .OrderByDescending(x => x.AdjustmentAt).ThenByDescending(x => x.CreatedAt).FirstOrDefault();
            Check(prior == null || prior.NewBinCount <= row.Quantity, "Baseline cannot lower established inventory; record its authorized movement or consumption.");
            Check(prior == null || prior.AdjustmentAt <= row.EffectiveAt, "A newer baseline already exists; reload and review the effective date.");
            replacements |= prior != null && prior.AdjustmentAt == row.EffectiveAt;
            proposed.Add(new()
            {
                // Preview-only ids sort after all persisted metadata. Never persisted.
                Id = long.MaxValue - rows.Length + proposed.Count,
                CropYear = row.CropYear,
                WarehouseId = row.WarehouseId,
                RoomId = row.RoomId,
                GrowerLotId = row.GrowerLotId,
                FruitProfileId = row.FruitProfileId,
                GrowerName = grower!.Grower,
                LotNumber = row.Lot,
                VarietyCode = row.Variety,
                PoolStart = grower.PoolStart,
                OldBinCount = prior?.NewBinCount,
                NewBinCount = row.Quantity,
                ChangeAmount = checked(row.Quantity - (prior?.NewBinCount ?? 0)),
                AdjustmentType = InventoryLedgerKinds.StartingInventoryImport,
                Source = row.Source,
                Reason = row.Source,
                Notes = row.Notes,
                SourceRoomCode = row.SourceRoomCode,
                SourceSubLocation = row.SourceSubLocation,
                InventoryStatus = InventoryStatusIdentity.Normalize(row.Status, profile!.ProductionType),
                AdjustmentAt = row.EffectiveAt,
                CreatedAt = now,
                InventoryInvariantVersion = InventoryLedgerKinds.CanonicalCommandInvariantVersion
            });
        }
        var evidence = await new InventoryEvidenceLoader(db).LoadAsync(new(null, roomIds.ToImmutableArray()), now, ct);
        foreach (var e in evidence.Positions.Where(e => rows.Any(r => r.RoomId == e.Location.RoomId && r.CropYear == e.Identity.CropYear
            && r.GrowerLotId == e.Identity.GrowerLotId && r.FruitProfileId == e.Identity.FruitProfileId)))
        {
            var resolved = InventoryAvailabilityResolver.Resolve(e, new(RequireKnownTreatment: false));
            Check(resolved.IsOperable, "Existing room identity, custody or treatment allocation requires review before importing a baseline.");
            if (resolved.RawProjectionQuantity != resolved.AuthoritativeQuantity)
                _ = InventoryNormalizationPlanner.Plan(e, InventoryAvailabilityResolver.Resolve(e, new()));
        }
        var forecast = await new RoomInventoryLedgerQueryService(db).PreviewBaselinesAsync(proposed, roomIds, now, ct);
        var after = forecast.GroupBy(x => (x.RoomId, Identity: InventoryEvidenceLoader.Identity(x).Key))
            .ToDictionary(x => x.Key, x => new { Identity = InventoryEvidenceLoader.Identity(x.First()), x.First().WarehouseId, Quantity = x.Sum(y => y.CurrentBins) });
        var positions = ImmutableArray.CreateBuilder<CanonicalBaselinePosition>();
        foreach (var key in evidence.Positions.Select(x => (x.Location.RoomId!.Value, x.Identity.Key)).Concat(after.Keys).Distinct().Order())
        {
            var before = evidence.Positions.SingleOrDefault(x => x.Location.RoomId == key.Item1 && x.Identity.Key == key.Item2);
            after.TryGetValue(key, out var projected);
            var identity = projected?.Identity ?? before!.Identity;
            var oldQuantity = before?.AuthoritativeQuantity ?? 0;
            var newQuantity = projected?.Quantity ?? 0;
            var imported = rows.Any(r => r.RoomId == key.Item1 && r.CropYear == identity.CropYear
                && r.GrowerLotId == identity.GrowerLotId && r.FruitProfileId == identity.FruitProfileId);
            Check(imported || newQuantity == oldQuantity, $"Baseline would change omitted lot {identity.Lot} in room {key.Item1}. Include every affected lot for review.");
            Check(identity.IsComplete && newQuantity >= oldQuantity && newQuantity >= 0,
                $"Baseline would hide, reduce or ambiguously identify existing lot {identity.Lot} in room {key.Item1}. Include every affected lot at its verified quantity.");
            positions.Add(new(identity, projected?.WarehouseId ?? before!.Location.WarehouseId, key.Item1, oldQuantity, newQuantity, imported));
        }
        var properties = db.Model.FindEntityType(typeof(RoomInventoryAdjustment))!.GetProperties().ToArray();
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            Rows = rows,
            Ledger = oldRows.Select(row => properties.ToDictionary(p => p.Name, p => p.PropertyInfo!.GetValue(row))),
            Evidence = evidence.Positions.OrderBy(x => x.Location.RoomId).ThenBy(x => x.Identity.Key).Select(x => x.Watermark.Fingerprint),
            Rooms = rooms.OrderBy(x => x.Id).Select(x => new
            {
                x.Id,
                x.WarehouseId,
                x.IsActive,
                x.IsSealed,
                x.SubLocation,
                x.CropQcRoomName,
                x.CompuTechRoomCode,
                x.DisplayName,
                x.SortOrder,
                WarehouseActive = x.Warehouse.IsActive
            }),
            Growers = growers.OrderBy(x => x.Id).Select(x => new { x.Id, x.IsActive, x.LotNumber, x.Grower, x.PoolStart }),
            Profiles = profiles.OrderBy(x => x.Id).Select(x => new { x.Id, x.IsActive, x.VarietyCode, x.ProductionType, x.IsOrganic }),
            Positions = positions.ToImmutable()
        })));
        return new(new(fingerprint, replacements, positions.ToImmutable()), proposed, evidence,
            rooms.Select(x => new CanonicalBaselineRoomMetadata(x.Id, x.SubLocation, x.CropQcRoomName, x.CompuTechRoomCode, x.DisplayName, x.SortOrder)).ToArray());
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
