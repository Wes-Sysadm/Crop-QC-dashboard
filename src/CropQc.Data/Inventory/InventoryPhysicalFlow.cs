using CropQc.Data.Entities;
using CropQc.Shared.Inventory;

namespace CropQc.Data.Inventory;

/// <summary>Exact parent-ledger/immutable-movement conservation by historical identity
/// and location. Current identity overlays do not change this historical accounting.</summary>
internal static class InventoryPhysicalFlow
{
    private sealed record Key(int Warehouse, int Room, int? Crop, int? GrowerLot, int? Profile, string Lot);
    internal static bool Matches(IEnumerable<RoomInventoryAdjustment> ledger, IEnumerable<TreatmentLineageMovement> movements,
        IReadOnlyDictionary<long, TreatmentLineageSegment> segments)
    {
        var expected = new Dictionary<Key, int>();
        foreach (var move in movements)
        {
            if (move.BinCount <= 0) return false;
            if (!Add(move.SourceSegmentId, move.SourceRoomId, -move.BinCount, move.IdentityKey)
                || !Add(move.DestinationSegmentId, move.DestinationRoomId, move.BinCount, move.IdentityKey)) return false;
        }
        var actual = ledger.GroupBy(x => new Key(x.WarehouseId, x.RoomId, x.CropYear, x.GrowerLotId, x.FruitProfileId, x.LotNumber.Trim().ToUpperInvariant()))
            .ToDictionary(x => x.Key, x => x.Sum(y => y.ChangeAmount));
        return actual.Keys.Concat(expected.Keys).Distinct().All(key => actual.GetValueOrDefault(key) == expected.GetValueOrDefault(key));

        bool Add(long? segmentId, int? room, int quantity, string identityKey)
        {
            if (room == null) return true;
            if (segmentId == null || !segments.TryGetValue(segmentId.Value, out var segment) || segment.RoomId != room
                || InventoryStatusIdentity.NormalizeLineageKey(segment.IdentityKey) != InventoryStatusIdentity.NormalizeLineageKey(identityKey)) return false;
            var key = new Key(segment.WarehouseId, room.Value, segment.CropYear, segment.GrowerLotId, segment.FruitProfileId,
                segment.LotNumberSnapshot.Trim().ToUpperInvariant());
            expected[key] = checked(expected.GetValueOrDefault(key) + quantity);
            return true;
        }
    }
}
