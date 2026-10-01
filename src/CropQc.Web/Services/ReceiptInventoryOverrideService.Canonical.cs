using System.Collections.Immutable;
using System.Security.Claims;
using System.Text.Json;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public sealed partial class ReceiptInventoryOverrideService
{
    private async Task<ReceiptInventoryOverridePreviewViewModel?> CanonicalPreviewAsync(long id, CancellationToken ct)
    {
        var state = await new InventoryReceiptAvailability(dbContext).ReadAsync(id, ct);
        if (state == null || state.Receipt.IsDeleted) return null;
        var receipt = state.Receipt;
        var roomIds = state.Allocations.Select(x => x.Position.Location.RoomId).Distinct().ToArray();
        var rooms = await dbContext.Rooms.AsNoTracking().Include(x => x.Warehouse).Where(x => roomIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id, ct);
        var counts = await GetOperationalCountsAsync(receipt, ct);
        return new()
        {
            ReceiptId = id,
            ConcurrencyVersion = receipt.ConcurrencyVersion,
            InventoryStateToken = state.Fingerprint,
            PositiveTrueUpStateToken = state.Fingerprint,
            ReceiptBinCount = receipt.BinCount,
            CurrentInventory = state.RoomQuantity + state.CustodyQuantity,
            CurrentCanonicalInventory = state.RoomQuantity + state.CustodyQuantity,
            ConsumedBins = receipt.BinCount - state.RoomQuantity - state.CustodyQuantity,
            CanonicalBlocker = state.Blocker,
            BinsRunCount = counts.BinsRuns,
            ActualRunCount = counts.ActualRuns,
            TransferCount = counts.Transfers,
            HasPriorOverride = await dbContext.ReceiptInventoryOverrides.AnyAsync(x => x.ReceiptId == id, ct),
            Balances = state.Allocations.Select(x =>
            {
                var room = x.Position.Location.RoomId is int r ? rooms.GetValueOrDefault(r) : null;
                return new ReceiptInventoryBalanceViewModel(x.Position.Location.WarehouseId, room?.Warehouse.Name ?? x.Position.Location.Custody.ToString(),
                    room?.Id ?? 0, room?.Name ?? x.Position.Location.Custody.ToString(), receipt.CropYear, receipt.GrowerLotId, receipt.FruitProfileId,
                    receipt.GrowerName, receipt.LotCode, receipt.FruitProfile.VarietyCode, x.Slice.State, x.Slice.Quantity);
            }).ToArray(),
            TrueUpPositions = state.Allocations.Select(x =>
            {
                var room = x.Position.Location.RoomId is int r ? rooms.GetValueOrDefault(r) : null;
                return new ReceiptInventoryTrueUpPositionViewModel(x.Key, x.Position.Location.Custody.ToString(), room?.Warehouse.Name ?? "External custody",
                    room?.Name ?? x.Position.Location.Custody.ToString(), x.Slice.Quantity, x.Slice.State, x.Slice.Signature,
                    x.Slice.ProjectionIds.Length == 1 ? x.Slice.ProjectionIds[0] : null, state.Blocker == null && x.Position.Location.Custody == InventoryCustody.Room,
                    state.Blocker ?? (x.Position.Location.Custody == InventoryCustody.Room ? null : "External custody cannot be rewritten by a receipt correction."));
            }).ToArray()
        };
    }

    private async Task<ReceiptInventoryOverrideResult> CanonicalEditAsync(AdminReceiptInventoryOverrideForm form, ClaimsPrincipal principal, CancellationToken ct)
    {
        var actor = await ResolveAdministratorAsync(principal, ct);
        if (actor == null || canonicalCommands == null) return Failed("Canonical administrator correction is unavailable.");
        var submission = JsonSerializer.Serialize(form, JsonOptions);
        var replay = await CanonicalApplicationReplay.TryAnyAsync(dbContext, canonicalCommands, form.OperationKey, actor.Id,
            [InventoryCommandKind.CorrectReceiptQuantity, InventoryCommandKind.CorrectReceiptLocation], submission, ct);
        if (replay != null) return await CanonicalResultAsync(replay, ct);
        var state = await new InventoryReceiptAvailability(dbContext).ReadAsync(form.Id, ct);
        if (state == null || state.Blocker != null) return Failed(state?.Blocker ?? "Receipt not found.");
        var r = state.Receipt;
        if (!form.ConfirmCropYear || form.BinCount < 0 || form.CropYear != r.CropYear || form.GrowerLotId != r.GrowerLotId
            || form.FruitProfileId != r.FruitProfileId
            || form.ReceivedAt != r.ReceivedAt || form.CompuTechReceiptId.Trim() != r.CompuTechReceiptId || form.ReceiptType != r.ReceiptType
            || form.GrowerName.Trim() != r.GrowerName.Trim())
            return Failed("Submit quantity correction separately from receiving identity, location or metadata changes.");
        if (form.GrowerNumber.Trim() != (r.GrowerNumber ?? r.LotCode) || form.LotCode.Trim() != r.LotCode)
            return Failed("A receipt identity change requires an explicit identity correction.");
        if (form.WarehouseId != r.WarehouseId || form.RoomId != r.RoomId)
        {
            if (form.BinCount != r.BinCount || form.TrueUpAllocations.Any(x => x.Bins != 0))
                return Failed("Submit quantity and location corrections as separate administrator operations.");
            var locationCommand = new InventoryCommand(form.OperationKey, InventoryCommandKind.CorrectReceiptLocation, actor.Id, DateTimeOffset.UtcNow,
                form.Reason.Trim(), [], ApplicationIntent: submission,
                ReceiptLocation: new(form.Id, form.ExpectedConcurrencyVersion, form.ExpectedInventoryStateToken,
                    new(r.CropYear, r.WarehouseId, r.RoomId, r.GrowerLotId ?? 0, r.FruitProfileId, r.CompuTechReceiptId, r.BinCount, r.ReceiptType),
                    new(form.WarehouseId, form.RoomId)));
            return await CanonicalResultAsync(await canonicalCommands.ExecuteAsync(locationCommand, ct), ct);
        }
        var allocations = form.TrueUpAllocations.Where(x => x.Bins != 0).Select(x => new InventoryReceiptQuantityAllocation(x.TargetKey, x.Bins)).ToImmutableArray();
        var available = state.Allocations.Where(x => x.Position.Location.Custody == InventoryCustody.Room).ToArray();
        if (allocations.IsEmpty && available.Length == 1 && form.BinCount != r.BinCount)
            allocations = [new(available[0].Key, Math.Abs(form.BinCount - r.BinCount))];
        var command = new InventoryCommand(form.OperationKey, InventoryCommandKind.CorrectReceiptQuantity, actor.Id, DateTimeOffset.UtcNow,
            form.Reason.Trim(), [], ApplicationIntent: submission, ReceiptChange: new(form.Id, form.ExpectedConcurrencyVersion,
                form.ExpectedInventoryStateToken, form.BinCount, allocations));
        return await CanonicalResultAsync(await canonicalCommands.ExecuteAsync(command, ct), ct);
    }

    private async Task<ReceiptInventoryOverrideResult> CanonicalVoidAsync(DeleteReceiptForm form, ClaimsPrincipal principal, CancellationToken ct)
    {
        var actor = await ResolveAdministratorAsync(principal, ct);
        if (actor == null || canonicalCommands == null) return Failed("Canonical administrator correction is unavailable.");
        var submission = JsonSerializer.Serialize(form, JsonOptions);
        var replay = await CanonicalApplicationReplay.TryAsync(dbContext, canonicalCommands, form.OperationToken, actor.Id,
            InventoryCommandKind.VoidReceipt, submission, ct);
        if (replay != null) return await CanonicalResultAsync(replay, ct);
        return await CanonicalResultAsync(await canonicalCommands.ExecuteAsync(new(form.OperationToken, InventoryCommandKind.VoidReceipt,
            actor.Id, DateTimeOffset.UtcNow, form.Reason.Trim(), [], ApplicationIntent: submission,
            ReceiptChange: new(form.Id, form.ExpectedConcurrencyVersion, form.ExpectedInventoryStateToken, 0, [], form.ConfirmationValue)), ct), ct);
    }

    private async Task<ReceiptInventoryOverrideResult> CanonicalResultAsync(InventoryCommandResult result, CancellationToken ct)
    {
        var error = CanonicalInventoryMessages.Result(result);
        var id = error == null ? await dbContext.ReceiptInventoryOverrides.Where(x => x.OperationKey == result.OperationKey).Select(x => (Guid?)x.Id).SingleAsync(ct) : null;
        return new(id, error, result.Status is InventoryCommandStatus.Stale or InventoryCommandStatus.Conflict, result.Status == InventoryCommandStatus.Replayed);
    }
}
