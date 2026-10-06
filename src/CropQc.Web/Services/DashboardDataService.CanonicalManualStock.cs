using System.Text.Json;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public sealed partial class DashboardDataService
{
    private async Task<IReadOnlyList<RoomReceiptOptionViewModel>> CanonicalManualStockOptionsAsync(int room, CancellationToken ct)
    {
        var batch = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(dbContext)).ResolveAsync(new(null, [room]), new(RequireKnownTreatment: false), BusinessTime.UtcNow, ct);
        var receipts = await dbContext.Receipts.AsNoTracking().Where(x => x.RoomId == room && !x.IsDeleted && !x.IsTransferReceipt).ToListAsync(ct);
        return batch.Positions.Where(x => x.IsOperable).Select(p => new
        {
            Position = p,
            Receipt = receipts.FirstOrDefault(r => r.CropYear == p.Identity.CropYear
            && r.FruitProfileId == p.Identity.FruitProfileId && r.GrowerLotId == p.Identity.GrowerLotId && (r.GrowerNumber ?? r.LotCode) == p.Identity.Lot)
        })
            .Where(x => x.Receipt != null).Select(x => new RoomReceiptOptionViewModel(x.Receipt!.Id,
                $"{x.Position.Identity.Lot} {x.Position.Identity.Variety} ({x.Position.AuthoritativeQuantity} current bins)", x.Position.AuthoritativeQuantity,
                CanonicalFingerprint: x.Position.Watermark.Fingerprint)).ToArray();
    }

    private async Task<string?> CreateCanonicalManualStockAsync(RoomInventoryTrueUpForm form, CancellationToken ct)
    {
        if (canonicalCommands == null) return "Canonical inventory command execution is not configured.";
        var actor = await GetCurrentUserAsync(ct);
        if (actor == null) return "The active administrator could not be resolved.";
        var submission = JsonSerializer.Serialize(form);
        var replay = await CanonicalApplicationReplay.TryAsync(dbContext, canonicalCommands, form.OperationKey, actor.Id, InventoryCommandKind.ManualStockAddition, submission, ct);
        if (replay != null) return CanonicalInventoryMessages.Result(replay);
        var receipt = await dbContext.Receipts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == form.ReceiptId && x.RoomId == form.RoomId && !x.IsDeleted && !x.IsTransferReceipt, ct);
        if (receipt == null) return "The reference receipt was not found in this room.";
        var batch = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(dbContext)).ResolveAsync(new(receipt.WarehouseId, [form.RoomId]), new(RequireKnownTreatment: false), BusinessTime.UtcNow, ct);
        var p = batch.Positions.SingleOrDefault(x => x.Identity.CropYear == receipt.CropYear && x.Identity.GrowerLotId == receipt.GrowerLotId
            && x.Identity.FruitProfileId == receipt.FruitProfileId && x.Identity.Lot == (receipt.GrowerNumber ?? receipt.LotCode));
        if (p == null || !p.IsOperable) return "Current physical identity or custody cannot be proven.";
        if (p.Watermark.Fingerprint != form.CanonicalFingerprint) return "Inventory changed; reload and review the verified count.";
        if (form.NewBinCount <= p.AuthoritativeQuantity) return "Positive true-up must add bins. Use a movement or loss workflow for reductions.";
        return CanonicalInventoryMessages.Result(await canonicalCommands.ExecuteAsync(new(form.OperationKey, InventoryCommandKind.ManualStockAddition,
            actor.Id, form.AdjustmentAt, form.Reason, [new(new(p.Identity, p.Location, p.Watermark.Fingerprint, p.Watermark.Versions),
                checked(form.NewBinCount - p.AuthoritativeQuantity), "x", AdjustmentDirection: InventoryAdjustmentDirection.Increase)],
            ApplicationIntent: submission, Dispatch: new(null, form.Notes)), ct));
    }
}
