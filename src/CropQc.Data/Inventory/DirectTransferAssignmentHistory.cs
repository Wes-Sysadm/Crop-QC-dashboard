using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

/// <summary>Advisory uncertainty from immutable movement ancestry, never an
/// inventory quantity, physical count or allocation of receipt ownership.</summary>
public static class DirectTransferAssignmentHistory
{
    public static async Task<IReadOnlyList<long>> ForRoomAsync(CropQcDbContext db, int? room, CancellationToken ct)
    {
        var pending = await db.RoomTransfers.AsNoTracking().Where(x => x.PhysicalLocationVerified == false
            && db.RoomTransfers.Where(c => c.CorrectsRoomTransferId == x.Id).Sum(c => c.BinCount) < x.BinCount)
            .Select(x => new { x.Id, x.DestinationRoomId }).ToListAsync(ct);
        if (room == null) return pending.Select(x => x.Id).ToArray();
        var matching = new HashSet<long>();
        foreach (var assignment in pending)
        {
            if (assignment.DestinationRoomId == room) { matching.Add(assignment.Id); continue; }
            var visited = (await db.TreatmentLineageMovements.AsNoTracking()
                .Where(x => x.RoomTransferId == assignment.Id && x.DestinationSegmentId != null)
                .Select(x => x.DestinationSegmentId!.Value).ToListAsync(ct)).ToHashSet();
            var frontier = visited.ToArray();
            for (var depth = 0; frontier.Length > 0; depth++)
            {
                // A warning may be conservative when a very old graph is too
                // large; it must never turn into a physical-write blocker or a
                // fabricated verified state. No projection quantity participates.
                if (depth >= 64 || visited.Count > 10000) { matching.Add(assignment.Id); break; }
                var edges = await db.TreatmentLineageMovements.AsNoTracking()
                    .Where(x => x.SourceSegmentId != null && frontier.Contains(x.SourceSegmentId.Value)
                        && x.DestinationSegmentId != null
                        && (x.RoomTransferId == null || x.RoomTransfer!.PhysicalLocationVerified != true))
                    .Select(x => new { x.DestinationRoomId, Segment = x.DestinationSegmentId!.Value }).Take(10001).ToListAsync(ct);
                if (edges.Count > 10000 || edges.Any(x => x.DestinationRoomId == room)) { matching.Add(assignment.Id); break; }
                frontier = edges.Where(x => visited.Add(x.Segment)).Select(x => x.Segment).ToArray();
            }
        }
        return matching.Order().ToArray();
    }
}
