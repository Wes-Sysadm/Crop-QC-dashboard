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
    public static InventoryIdentity ReceiptIdentity(Receipt r) => new(r.CropYear, r.GrowerLotId, r.FruitProfileId,
        r.LotCode, r.GrowerNumber ?? r.LotCode, r.FruitProfile.VarietyCode, r.FruitProfile.ProductionType, r.FruitProfile.IsOrganic, "");

    // Choices for an explicit new correction origin, never surviving allocations.
    public async Task<IReadOnlyList<InventoryReceiptAdditionTarget>> AdditionTargetsAsync(InventoryReceiptAvailabilityResult state, CancellationToken ct)
    {
        if (state.Blocker != null || !state.Allocations.IsEmpty) return [];
        var receipt = state.Receipt;
        var identity = ReceiptIdentity(receipt);
        if (!identity.IsComplete) return [];
        var rooms = await db.Rooms.AsNoTracking().Where(x => x.WarehouseId == receipt.WarehouseId && x.IsActive && x.Warehouse.IsActive && !x.IsSealed)
            .OrderBy(x => x.Name).ToListAsync(ct);
        var evidence = await new InventoryEvidenceLoader(db).LoadAsync(new(receipt.WarehouseId, rooms.Select(x => x.Id).ToImmutableArray()), DateTimeOffset.UtcNow, ct);
        return rooms.Select(room =>
        {
            var p = evidence.Positions.SingleOrDefault(x => x.Location.RoomId == room.Id && x.Identity.Key == identity.Key);
            return new InventoryReceiptAdditionTarget(room.Id, room.Name,
                p == null ? "empty" : InventoryAvailabilityResolver.Resolve(p, new()).Watermark.Fingerprint);
        }).ToArray();
    }

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
        var loader = new InventoryEvidenceLoader(db);
        var positions = ImmutableArray.CreateBuilder<InventoryAvailabilityResult>();
        var historicalOnly = new HashSet<string>();
        var asOf = DateTimeOffset.UtcNow;
        var rooms = roomIds.Distinct().ToImmutableArray();
        foreach (var custody in new[] { InventoryCustody.Room, InventoryCustody.InTransit, InventoryCustody.OutsideWarehouse, InventoryCustody.Processor, InventoryCustody.ReceiptHeld })
        {
            var batch = await loader.LoadAsync(new(null, rooms, custody), asOf, ct);
            // Same-lot inventory elsewhere does not belong to this receipt merely
            // because identity matches. Include every position retaining its evidence.
            foreach (var evidence in batch.Positions.Where(x => x.Receipts.Any(r => r.Id == receiptId)
                || x.Projections.Any(p => p.ReceiptId == receiptId || p.CustodyTreatment?.SourceReceiptId == receiptId) || x.Movements.Any(m => m.ReceiptId == receiptId)))
            {
                var absent = InventoryCohortEvidence.ProvesNoCurrentReceipt(evidence, receiptId);
                var position = InventoryAvailabilityResolver.Resolve(evidence,
                    new(AllowedCustody: custody, ReceiptId: receiptId, RequireExactReceipt: !absent && evidence.AuthoritativeQuantity != 0)
                    { AllowIndependentCohorts = true });
                positions.Add(position);
                // Retain history and its fingerprint for concurrency, but do not
                // reinterpret a fully withdrawn receipt as current shared stock.
                if (absent) historicalOnly.Add(position.PositionKey);
            }
        }
        var allocation = ImmutableArray.CreateBuilder<InventoryReceiptAllocation>();
        string? blocker = receipt.IsDeleted || receipt.IsTransferReceipt ? "An active ordinary receipt is required." : null;
        foreach (var p in positions)
        {
            if (p.AuthoritativeQuantity < 0) blocker ??= "Negative authoritative inventory requires a separately reviewed correction.";
            if (historicalOnly.Contains(p.PositionKey)) continue;
            if (p.AuthoritativeQuantity == 0 && p.IsOperable) continue;
            if (!p.IsOperable)
            { blocker ??= "Exact current receipt inventory and treatment ownership cannot be proven. Review the receipt allocations and unassigned movements; same-lot stock alone is not receipt attribution."; continue; }
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

public sealed record InventoryReceiptAdditionTarget(int RoomId, string RoomName, string Fingerprint)
{
    public string Key => $"{RoomId}:{Fingerprint}";
}
