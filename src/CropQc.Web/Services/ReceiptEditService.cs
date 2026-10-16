using System.Security.Claims;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

/// <summary>Classifies a single Edit submission; never chains independently committed commands.</summary>
public sealed class ReceiptEditService(CropQcDbContext db, IUserAccessService access,
    IDashboardDataService dashboard, IReceiptInventoryOverrideService corrections,
    IInventoryCommandExecutor commands, IBusinessTimeService time)
{
    public async Task<ReceiptInventoryOverrideResult> SaveAsync(AdminReceiptInventoryOverrideForm form, ClaimsPrincipal principal, CancellationToken ct)
    {
        if (!await access.HasAccessAsync(principal, ApplicationAreas.Receipts, PageAccessLevel.Admin, ct))
            return new(null, "Receipts Admin permission is required to edit an existing receipt.");
        if (string.IsNullOrWhiteSpace(form.Reason)) return new(null, "Enter a reason for this receipt edit.");
        if (form.Reason.Length > 1000) return new(null, "The reason must be at most 1000 characters.");
        var saved = await db.Receipts.AsNoTracking().SingleOrDefaultAsync(x => x.Id == form.Id && !x.IsDeleted, ct);
        if (saved == null) return new(null, "Receipt not found.");
        var email = principal.FindFirstValue(ClaimTypes.Email);
        var actor = await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Email == email && x.IsActive, ct);
        if (actor == null) return new(null, "The active administrator could not be resolved.");
        form.ConfirmInventoryChange = true;
        var submission = JsonSerializer.Serialize(form, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        // Resolve retries before comparing with the now-corrected receipt. Complete
        // submitted intent (including reason/evidence) remains bound to its operation key.
        if (db.CanonicalInventoryEnabled)
        {
            var replay = await CanonicalApplicationReplay.TryAnyAsync(db, commands, form.OperationKey, actor.Id,
                [InventoryCommandKind.UpdateReceiptMetadata, InventoryCommandKind.CorrectReceiptQuantity,
                    InventoryCommandKind.CorrectReceiptIdentity, InventoryCommandKind.CorrectReceiptLocation], submission, ct);
            if (replay != null) return Result(replay);
        }
        var identity = form.CropYear != saved.CropYear || form.GrowerLotId != saved.GrowerLotId || form.FruitProfileId != saved.FruitProfileId;
        var quantity = form.BinCount != saved.BinCount;
        var location = form.WarehouseId != saved.WarehouseId || form.RoomId != saved.RoomId || form.CorrectionSourceRoomId != null;
        var unchangedTime = ReceiptInventoryOverrideService.MatchesCorrectionTime(time, form.ReceivedAt, saved.ReceivedAt);
        var metadata = !unchangedTime || form.CompuTechReceiptId?.Trim() != saved.CompuTechReceiptId.Trim()
            || form.ReceiptType?.Trim() != saved.ReceiptType.Trim() || !identity && form.GrowerName?.Trim() != saved.GrowerName.Trim();
        if ((identity ? 1 : 0) + (quantity ? 1 : 0) + (location ? 1 : 0) + (metadata ? 1 : 0) > 1)
            return new(null, "No changes were saved. Quantity, identity, location and metadata use separate atomic commands. Save one correction type at a time; combining them requires a reviewed composite command.");
        if (!identity && (form.GrowerNumber?.Trim() != (saved.GrowerNumber ?? saved.LotCode).Trim()
            || !form.LotCodeOmitted && form.LotCode?.Trim() != saved.LotCode.Trim()))
            return new(null, "Select the exact Grower Lot for an identity correction. A changed lot label alone cannot reclassify inventory.");
        if (identity)
        {
            var target = await db.GrowerLots.AsNoTracking().SingleOrDefaultAsync(x => x.Id == form.GrowerLotId && x.IsActive, ct);
            if (target == null || form.GrowerNumber?.Trim() != target.LotNumber
                || !form.LotCodeOmitted && form.LotCode?.Trim() != target.LotNumber
                || form.GrowerName?.Trim() != target.Grower && form.GrowerName?.Trim() != saved.GrowerName.Trim())
                return new(null, "Select consistent reviewed Grower Lot values. Save an independent grower-name change separately.");
        }
        if (identity || quantity || location)
        {
            // Saving with the reason is the operator's confirmation; all specific
            // custody/treatment attestations still come from submitted evidence.
            return await corrections.ApplyEditAsync(form, principal, ct);
        }
        if (form.TrueUpAllocations.Any(x => x.Bins != 0) || !string.IsNullOrWhiteSpace(form.AdditionalStockTarget))
            return new(null, "Allocation evidence requires a quantity correction. No changes were saved.");
        if (!form.ConfirmCropYear) return new(null, "Confirm the crop year.");
        if (!db.CanonicalInventoryEnabled || saved.IsTransferReceipt || saved.ReceiptType != "Truck receipt")
        {
            // Existing non-inventory/transfer adapter retains its specialized guards.
            if (unchangedTime) form.ReceivedAt = saved.ReceivedAt;
            return new(null, await dashboard.UpdateReceiptAsync(form, ct));
        }
        var input = new InventoryReceiptMetadata(saved.Id, form.ReceiptVersion,
            unchangedTime ? saved.ReceivedAt : form.ReceivedAt, form.CompuTechReceiptId?.Trim() ?? "", form.GrowerName?.Trim() ?? "",
            new(saved.CropYear, saved.WarehouseId, saved.RoomId, saved.GrowerLotId ?? 0, saved.FruitProfileId,
                saved.CompuTechReceiptId, saved.BinCount, form.ReceiptType));
        return Result(await new CanonicalReceiptMetadataService(db, commands)
            .UpdateAsync(form.OperationKey, actor.Id, input, form.Reason.Trim(), submission, ct));
    }

    private static ReceiptInventoryOverrideResult Result(InventoryCommandResult result) =>
        new(null, CanonicalInventoryMessages.Result(result), result.Status is InventoryCommandStatus.Stale or InventoryCommandStatus.Conflict,
            result.Status == InventoryCommandStatus.Replayed);
}
