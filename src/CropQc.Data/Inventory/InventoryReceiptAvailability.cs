using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using CropQc.Data.Entities;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

public sealed record InventoryReceiptAllocation(InventoryAvailabilityResult Position, InventoryTreatmentSlice Slice)
{
    public string Key => $"{Position.PositionKey}:{Slice.Signature}";
}

public sealed record InventoryReceiptAvailabilityResult(Receipt Receipt, string Fingerprint,
    ImmutableArray<InventoryReceiptAllocation> Allocations, ImmutableArray<InventoryAvailabilityResult> Positions, string? Blocker)
{
    public int RoomQuantity => Allocations.Where(x => x.Position.Location.Custody == InventoryCustody.Room).Sum(x => x.Slice.Quantity);
    public int CustodyQuantity => Allocations.Where(x => x.Position.Location.Custody != InventoryCustody.Room).Sum(x => x.Slice.Quantity);
}

/// <summary>Receipt-scoped canonical evidence shared by preview and commit. A same-lot
/// treatment pool cannot substitute for exact surviving receipt ownership.</summary>
public sealed class InventoryReceiptAvailability(CropQcDbContext db)
{
    public async Task<InventoryReceiptAvailabilityResult?> ReadAsync(long receiptId, CancellationToken ct)
    {
        var receipt = await db.Receipts.AsNoTracking().Include(x => x.FruitProfile).Include(x => x.GrowerLot).Include(x => x.Warehouse).Include(x => x.Room)
            .SingleOrDefaultAsync(x => x.Id == receiptId, ct);
        if (receipt == null) return null;
        var roomIds = await db.RoomInventoryAdjustments.Where(x => x.ReceiptId == receiptId
            || x.CropYear == receipt.CropYear && x.GrowerLotId == receipt.GrowerLotId && x.FruitProfileId == receipt.FruitProfileId)
            .Select(x => x.RoomId).Distinct().ToListAsync(ct);
        roomIds.AddRange(await db.TreatmentLineageSegments.Where(x => x.ReceiptId == receiptId).Select(x => x.RoomId).Distinct().ToListAsync(ct));
        roomIds.Add(receipt.RoomId);
        var resolver = new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db));
        var positions = ImmutableArray.CreateBuilder<InventoryAvailabilityResult>();
        var asOf = DateTimeOffset.UtcNow;
        var rooms = roomIds.Distinct().ToImmutableArray();
        foreach (var custody in new[] { InventoryCustody.Room, InventoryCustody.InTransit, InventoryCustody.OutsideWarehouse, InventoryCustody.Processor })
        {
            var batch = await resolver.ResolveAsync(new(null, rooms, custody), new(AllowedCustody: custody), asOf, ct);
            positions.AddRange(batch.Positions.Where(x => x.Identity.CropYear == receipt.CropYear && x.Identity.GrowerLotId == receipt.GrowerLotId
                && x.Identity.FruitProfileId == receipt.FruitProfileId || x.ReceiptProvenance.ReceiptIds.Contains(receiptId)));
        }
        var allocation = ImmutableArray.CreateBuilder<InventoryReceiptAllocation>();
        string? blocker = receipt.IsDeleted || receipt.IsTransferReceipt ? "An active ordinary receipt is required." : null;
        foreach (var p in positions)
        {
            if (p.AuthoritativeQuantity < 0) blocker ??= "Negative authoritative inventory requires a separately reviewed correction.";
            if (p.AuthoritativeQuantity == 0 && p.IsOperable) continue;
            if (!p.IsOperable || p.ReceiptProvenance.Confidence != InventoryConfidence.Proven)
            { blocker ??= "Exact receipt inventory or treatment ownership cannot be proven."; continue; }
            foreach (var group in p.TreatmentSlices.Where(x => x.ReceiptEvidenceIds.Length == 1 && x.ReceiptEvidenceIds[0] == receiptId).GroupBy(x => new { x.Signature, x.State }))
            {
                var first = group.First();
                allocation.Add(new(p, first with { Quantity = group.Sum(x => x.Quantity), ProjectionIds = group.SelectMany(x => x.ProjectionIds).Distinct().ToImmutableArray() }));
            }
        }
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            receipt.Id,
            receipt.ConcurrencyVersion,
            receipt.BinCount,
            receipt.CropYear,
            receipt.GrowerLotId,
            receipt.FruitProfileId,
            receipt.WarehouseId,
            receipt.RoomId,
            receipt.ReceivedAt,
            receipt.UpdatedAt,
            receipt.IsDeleted,
            receipt.IsTransferReceipt,
            Positions = positions.OrderBy(x => x.PositionKey).Select(x => new { x.PositionKey, x.Watermark.Fingerprint }).ToArray()
        })));
        return new(receipt, fingerprint, allocation.ToImmutable(), positions.ToImmutable(), blocker);
    }
}
