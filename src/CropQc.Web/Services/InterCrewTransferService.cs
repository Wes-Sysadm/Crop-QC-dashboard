using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CropQc.Web.Services;

public sealed record InterCrewWriteResult(bool Success, bool AlreadyApplied, long? TransferId, string? Error);

public interface IInterCrewTransferService
{
    Task<InterCrewTransferPageViewModel> GetPageAsync(BinsRunFilterForm filter, CancellationToken cancellationToken);
    Task<InterCrewWriteResult> DispatchAsync(InterCrewDispatchForm form, CancellationToken cancellationToken);
    Task<InterCrewWriteResult> ReceiveAsync(InterCrewReceiveForm form, CancellationToken cancellationToken);
    Task<string?> ReviewAsync(InterCrewReviewForm form, CancellationToken cancellationToken);
    Task<string?> ReverseAsync(InterCrewReversalForm form, CancellationToken cancellationToken);
    Task<InterCrewTransferDetailViewModel?> GetDetailsAsync(long id, CancellationToken cancellationToken);
}

public sealed class InterCrewTransferService(
    CropQcDbContext dbContext,
    IOutsideWarehouseTransferService inventoryProvider,
    IRoomInventoryLedgerQueryService ledger,
    IInterCrewTreatmentLineageService treatmentLineage,
    IInventoryIdentityService identityService,
    IInventoryDeductionInvariantService invariant,
    IUserAccessService access,
    IHttpContextAccessor httpContextAccessor,
    IBusinessTimeService businessTime) : IInterCrewTransferService
{
    private const string AuditSource = "CropQc.Web inter-crew transfer workflow";
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    public async Task<InterCrewTransferPageViewModel> GetPageAsync(
        BinsRunFilterForm filter,
        CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();
        var actor = await GetActorAsync(cancellationToken);
        var group = CustodyGroupForUser(actor);
        var canCreate = await access.HasAccessAsync(principal, ApplicationAreas.Transfers, PageAccessLevel.Create, cancellationToken);
        var canAdmin = await CanAdminAsync(principal, cancellationToken);
        var all = await dbContext.InterCrewTransfers.AsNoTracking()
            .Include(x => x.SourceWarehouse).Include(x => x.SourceRoom)
            .OrderByDescending(x => x.LoadedAt).Take(500).ToListAsync(cancellationToken);
        var queue = all.Where(x => x.Status == InterCrewTransferStatuses.InTransit && CanAccessGroup(group, canAdmin, x.DestinationCustodyGroup))
            .Select(x => ListItem(x, true)).ToList();
        var inventory = new List<OutsideWarehouseInventoryOptionViewModel>();
        Warehouse? sourceWarehouse = null;
        Room? sourceRoom = null;
        string? sourceSelectionMessage = null;
        if (filter.RoomId is not int sourceRoomId)
        {
            sourceSelectionMessage = "Select a source Facility and Room to view inventory for an inter-crew transfer.";
        }
        else
        {
            sourceRoom = await dbContext.Rooms.AsNoTracking()
                .Include(x => x.Warehouse)
                .SingleOrDefaultAsync(x => x.Id == sourceRoomId && x.IsActive && x.Warehouse.IsActive, cancellationToken);
            if (sourceRoom is null)
            {
                sourceSelectionMessage = "The selected source Room is no longer active. Select another source Room.";
            }
            else if (filter.WarehouseId is int sourceWarehouseId && sourceRoom.WarehouseId != sourceWarehouseId)
            {
                sourceSelectionMessage = "The selected source Room does not belong to the selected Facility. Select the source Room again.";
                sourceRoom = null;
            }
            else
            {
                sourceWarehouse = sourceRoom.Warehouse;
                filter.WarehouseId = sourceRoom.WarehouseId;
                inventory = (await inventoryProvider.GetInventoryAsync(cancellationToken))
                    .Where(x => x.WarehouseId == sourceRoom.WarehouseId
                        && x.RoomId == sourceRoom.Id
                        && AllowedDestinationGroups(x.Facility).Count > 0)
                    .ToList();
            }
        }
        return new InterCrewTransferPageViewModel
        {
            Form = new()
            {
                SourceWarehouseId = sourceWarehouse?.Id,
                SourceRoomId = sourceRoom?.Id,
                LoadedAt = businessTime.NowPacific.DateTime
            },
            Inventory = inventory,
            Queue = queue,
            History = all.Select(x => ListItem(x, false)).ToList(),
            CanCreate = canCreate,
            CanAdmin = canAdmin,
            CurrentCustodyGroup = group switch { SharedCustodyGroup => "Shared", null => "No receiving crew", _ => TransferCustodyGroups.Label(group) },
            SourceFacility = sourceWarehouse?.Code,
            SourceRoom = sourceRoom is null ? null : RoomLabel(sourceRoom),
            SourceSelectionMessage = sourceSelectionMessage,
            InTransitLoads = all.Count(x => x.Status == InterCrewTransferStatuses.InTransit),
            InTransitBins = all.Where(x => x.Status == InterCrewTransferStatuses.InTransit).Sum(x => x.BinsLoaded)
        };
    }

    public async Task<InterCrewWriteResult> DispatchAsync(InterCrewDispatchForm form, CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();
        if (!await access.HasAccessAsync(principal, ApplicationAreas.Transfers, PageAccessLevel.Create, cancellationToken))
            return Fail("Transfers Create access is required to dispatch an inter-crew transfer.");
        if (!form.ConfirmedReview) return Fail("Review the inter-crew transfer before dispatching it.");
        if (form.SourceWarehouseId is null || form.SourceRoomId is null)
            return Fail("Select the source Facility and Room again before dispatching this transfer.");
        if (!TransferCustodyGroups.IsValid(form.DestinationCustodyGroup)) return Fail("Select WP / DH or EBS as the destination crew.");
        if (form.BinsLoaded <= 0) return Fail("Bins loaded must be greater than zero.");
        if (form.LoadedAt == default) return Fail("Loaded date and time are required.");
        var key = Normalize(form.OperationKey);
        if (key is null || key.Length > 150) return Fail("The transfer operation key is invalid. Refresh and retry.");
        var existing = await dbContext.InterCrewTransfers.AsNoTracking().SingleOrDefaultAsync(x => x.OperationKey == key, cancellationToken);
        if (existing is not null) return new(true, true, existing.Id, null);
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return Fail("The current active user could not be resolved.");

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            var option = (await inventoryProvider.GetInventoryAsync(cancellationToken)).SingleOrDefault(x =>
                x.SourceKey == form.SourceKey
                && x.WarehouseId == form.SourceWarehouseId
                && x.RoomId == form.SourceRoomId);
            if (option is null) return Fail("The selected current inventory is no longer available. Refresh and retry.");
            if (!option.IsAvailable) return Fail(option.UnavailableReason ?? "The selected treatment lineage requires review and cannot be dispatched.");
            var allowed = AllowedDestinationGroups(option.Facility);
            if (!allowed.Contains(form.DestinationCustodyGroup, StringComparer.Ordinal))
                return Fail("That source and destination must use the existing immediate internal transfer workflow.");
            if (form.ExpectedAvailableBins != option.AvailableBins) return Fail("Source inventory changed after this page loaded. Refresh before retrying.");
            if (form.BinsLoaded > option.AvailableBins) return Fail($"Only {option.AvailableBins} bins remain in the selected inventory position.");
            var sealError = await RoomMovementSealGuard.ValidateAsync(dbContext, [option.RoomId], [], businessTime, cancellationToken);
            if (sealError is not null) return Fail(sealError);
            var snapshot = await inventoryProvider.ResolveInventoryAsync(option, cancellationToken);
            if (snapshot is null || snapshot.CurrentBins < form.BinsLoaded) return Fail("The exact source inventory changed while the transfer was being saved.");
            var now = businessTime.UtcNow;
            var transfer = new InterCrewTransfer
            {
                OperationKey = key,
                SourceWarehouseId = option.WarehouseId,
                SourceRoomId = option.RoomId,
                DestinationCustodyGroup = form.DestinationCustodyGroup,
                ReceiptId = option.ReceiptId,
                SourceInventoryAdjustmentId = option.SourceInventoryAdjustmentId,
                CropYear = option.CropYear,
                GrowerLotId = option.GrowerLotId,
                FruitProfileId = option.FruitProfileId,
                GrowerNumberSnapshot = option.GrowerNumber,
                GrowerNameSnapshot = option.GrowerName,
                LotNumberSnapshot = option.LotNumber,
                VarietyCodeSnapshot = option.VarietyCode,
                ProductionTypeSnapshot = option.ProductionType,
                IsOrganicSnapshot = option.IsOrganic,
                InventoryStatusSnapshot = option.InventoryStatus,
                TreatmentStateSnapshot = option.TreatmentState,
                TreatmentSignatureSnapshot = option.TreatmentSignature,
                TreatmentSummarySnapshot = option.TreatmentSummary,
                BinsLoaded = form.BinsLoaded,
                LoadedAt = businessTime.PacificLocalToUtc(form.LoadedAt),
                TruckLoadBolNumber = Normalize(form.TruckLoadBolNumber),
                Notes = Normalize(form.Notes),
                LoadedByUserId = actor.Id,
                CreatedAt = now,
                Status = InterCrewTransferStatuses.InTransit
            };
            dbContext.InterCrewTransfers.Add(transfer);
            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.RoomInventoryAdjustments.Add(Adjustment(transfer, option.WarehouseId, option.RoomId,
                -transfer.BinsLoaded, snapshot.CurrentBins, InterCrewTransferAdjustmentTypes.Dispatch,
                "Inter-crew transfer dispatch", $"Dispatched to {TransferCustodyGroups.Label(transfer.DestinationCustodyGroup)} crew",
                transfer.LoadedAt, actor.Id, $"inter-crew-dispatch:{key}"));
            await invariant.ValidateBeforeCommitAsync(cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            var lineageResult = await treatmentLineage.DispatchAsync(snapshot, option.TreatmentSignature, transfer.BinsLoaded,
                $"inter-crew-dispatch:{key}:treatment", transfer.Id, transfer.LoadedAt, actor.Id, cancellationToken);
            if (!lineageResult.Success) throw new InvalidOperationException(lineageResult.Error);
            AddAudit(actor.Id, "InterCrewTransferDispatched", transfer, new { transfer.BinsLoaded, transfer.DestinationCustodyGroup, transfer.TruckLoadBolNumber });
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new(true, false, transfer.Id, null);
        }
        catch (Exception exception) when (exception is InvalidOperationException or DbUpdateException)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            return Fail(exception.Message);
        }
    }

    public async Task<InterCrewWriteResult> ReceiveAsync(InterCrewReceiveForm form, CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();
        if (!await access.HasAccessAsync(principal, ApplicationAreas.Transfers, PageAccessLevel.Create, cancellationToken))
            return Fail("Transfers Create access is required to receive an inter-crew transfer.");
        if (form.DestinationRoomId is null || form.BinsReceived <= 0 || form.ReceivedAt == default)
            return Fail("Destination room, received bins, and received date/time are required.");
        var key = Normalize(form.OperationKey);
        if (key is null || key.Length > 150) return Fail("The receive operation key is invalid.");
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return Fail("The current active user could not be resolved.");
        var canAdmin = await CanAdminAsync(principal, cancellationToken);

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            var transfer = await dbContext.InterCrewTransfers.SingleOrDefaultAsync(x => x.Id == form.TransferId, cancellationToken);
            if (transfer is null) return Fail("Inter-crew transfer was not found.");
            if (transfer.ReceiveOperationKey == key) return new(true, true, transfer.Id, null);
            if (transfer.Status != InterCrewTransferStatuses.InTransit) return Fail("Only an in-transit load can be received.");
            if (!CanAccessGroup(CustodyGroupForUser(actor), canAdmin, transfer.DestinationCustodyGroup))
                return Fail("This load belongs to another receiving crew.");
            var room = await dbContext.Rooms.Include(x => x.Warehouse).SingleOrDefaultAsync(x => x.Id == form.DestinationRoomId && x.IsActive, cancellationToken);
            if (room is null || !TransferCustodyGroups.ContainsWarehouse(transfer.DestinationCustodyGroup, room.Warehouse.Code)
                || room.Warehouse.Code.Equals("McDougall", StringComparison.OrdinalIgnoreCase))
                return Fail("Select a destination room belonging to the receiving crew. McDougall cannot be a destination.");
            var sealError = await RoomMovementSealGuard.ValidateAsync(dbContext, [room.Id], [], businessTime, cancellationToken);
            if (sealError is not null) return Fail(sealError);
            InventoryIdentityResolution? resolvedIdentity = null;
            if (transfer.CropYear is not null && transfer.GrowerLotId is not null && transfer.FruitProfileId is not null)
            {
                resolvedIdentity = await identityService.ResolveAsync(new InventoryIdentityKey(
                    transfer.CropYear.Value, transfer.GrowerLotId.Value, transfer.FruitProfileId.Value), cancellationToken);
            }
            var current = await FindSnapshotAsync(
                transfer, room.WarehouseId, room.Id, cancellationToken, resolvedIdentity?.Canonical);
            var oldBalance = current?.CurrentBins ?? 0;
            var now = businessTime.UtcNow;
            var receivedAt = businessTime.PacificLocalToUtc(form.ReceivedAt);
            transfer.DestinationWarehouseId = room.WarehouseId;
            transfer.DestinationRoomId = room.Id;
            transfer.BinsReceived = form.BinsReceived;
            transfer.VarianceBins = form.BinsReceived - transfer.BinsLoaded;
            transfer.ReceivedAt = receivedAt;
            transfer.ReceivedByUserId = actor.Id;
            transfer.ReceivingNote = Normalize(form.Note);
            transfer.ReceiveOperationKey = key;
            transfer.Status = transfer.VarianceBins == 0 ? InterCrewTransferStatuses.Received : InterCrewTransferStatuses.ReceivedNeedsReview;
            transfer.ConcurrencyVersion++;
            dbContext.RoomInventoryAdjustments.Add(Adjustment(transfer, room.WarehouseId, room.Id, form.BinsReceived,
                oldBalance, InterCrewTransferAdjustmentTypes.Receive, "Inter-crew transfer receive",
                $"Received from {transfer.SourceWarehouseId}/{transfer.SourceRoomId}; loaded {transfer.BinsLoaded}, received {form.BinsReceived}",
                receivedAt, actor.Id, $"inter-crew-receive:{key}", resolvedIdentity));
            await invariant.ValidateBeforeCommitAsync(cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            var lineageResult = await treatmentLineage.ReceiveAsync(transfer.Id, room.WarehouseId, room.Id, form.BinsReceived,
                $"inter-crew-receive:{key}:treatment", receivedAt, actor.Id, cancellationToken);
            if (!lineageResult.Success) throw new InvalidOperationException(lineageResult.Error);
            AddAudit(actor.Id, "InterCrewTransferReceived", transfer, new { transfer.BinsLoaded, transfer.BinsReceived, transfer.VarianceBins, transfer.Status });
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new(true, false, transfer.Id, null);
        }
        catch (Exception exception) when (exception is InvalidOperationException or DbUpdateException)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            return Fail(exception.Message);
        }
    }

    public async Task<string?> ReviewAsync(InterCrewReviewForm form, CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();
        if (!await CanAdminAsync(principal, cancellationToken)) return "Transfer Admin access is required to review a variance.";
        if (string.IsNullOrWhiteSpace(form.Note)) return "A review note is required.";
        var actor = await GetActorAsync(cancellationToken);
        var key = Normalize(form.OperationKey);
        if (actor is null || key is null) return "The review request is invalid.";
        var transfer = await dbContext.InterCrewTransfers.SingleOrDefaultAsync(x => x.Id == form.TransferId, cancellationToken);
        if (transfer is null) return "Inter-crew transfer was not found.";
        if (transfer.ReviewOperationKey == key || transfer.Status == InterCrewTransferStatuses.Received) return null;
        if (transfer.Status != InterCrewTransferStatuses.ReceivedNeedsReview) return "Only a received variance can be reviewed.";
        transfer.ReviewOperationKey = key; transfer.ReviewNote = form.Note.Trim(); transfer.ReviewedAt = businessTime.UtcNow;
        transfer.ReviewedByUserId = actor.Id; transfer.Status = InterCrewTransferStatuses.Received; transfer.ConcurrencyVersion++;
        AddAudit(actor.Id, "InterCrewTransferVarianceReviewed", transfer, new { transfer.BinsLoaded, transfer.BinsReceived, transfer.VarianceBins, transfer.ReviewNote });
        await dbContext.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task<string?> ReverseAsync(InterCrewReversalForm form, CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();
        if (!await CanAdminAsync(principal, cancellationToken)) return "Transfer Admin access is required to reverse an inter-crew transfer.";
        if (string.IsNullOrWhiteSpace(form.Reason)) return "A reversal reason is required.";
        var actor = await GetActorAsync(cancellationToken);
        var key = Normalize(form.OperationKey);
        if (actor is null || key is null) return "The reversal request is invalid.";
        await using var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            var transfer = await dbContext.InterCrewTransfers.Include(x => x.InventoryAdjustments).SingleOrDefaultAsync(x => x.Id == form.TransferId, cancellationToken);
            if (transfer is null) return "Inter-crew transfer was not found.";
            if (transfer.ReversalOperationKey == key || transfer.Status == InterCrewTransferStatuses.Reversed) return null;
            var identityError = await InventoryIdentityWriteGuard.RejectSupersededAsync(
                dbContext, transfer.CropYear, transfer.GrowerLotId, transfer.FruitProfileId,
                $"Inter-crew transfer #{transfer.Id} reversal", cancellationToken);
            if (identityError is not null) return identityError;
            var wasReceived = transfer.BinsReceived is not null && transfer.DestinationRoomId is not null && transfer.DestinationWarehouseId is not null;
            var roomIds = wasReceived ? new[] { transfer.SourceRoomId, transfer.DestinationRoomId!.Value } : new[] { transfer.SourceRoomId };
            var sealError = await RoomMovementSealGuard.ValidateAsync(dbContext, roomIds, [], businessTime, cancellationToken);
            if (sealError is not null) return sealError;
            var now = businessTime.UtcNow;
            if (wasReceived)
            {
                var destination = await FindSnapshotAsync(transfer, transfer.DestinationWarehouseId!.Value, transfer.DestinationRoomId!.Value, cancellationToken);
                if (destination is null || destination.CurrentBins < transfer.BinsReceived!.Value)
                    return "The destination no longer contains enough of the exact received inventory to reverse this transfer.";
                dbContext.RoomInventoryAdjustments.Add(Adjustment(transfer, transfer.DestinationWarehouseId.Value, transfer.DestinationRoomId.Value,
                    -transfer.BinsReceived.Value, destination.CurrentBins, InterCrewTransferAdjustmentTypes.ReversalDestination,
                    "Inter-crew transfer reversal", form.Reason.Trim(), now, actor.Id, $"inter-crew-reversal-destination:{key}"));
            }
            var source = await FindSnapshotAsync(transfer, transfer.SourceWarehouseId, transfer.SourceRoomId, cancellationToken);
            var sourceBalance = source?.CurrentBins ?? 0;
            dbContext.RoomInventoryAdjustments.Add(Adjustment(transfer, transfer.SourceWarehouseId, transfer.SourceRoomId,
                transfer.BinsLoaded, sourceBalance, InterCrewTransferAdjustmentTypes.ReversalSource,
                "Inter-crew transfer reversal", form.Reason.Trim(), now, actor.Id, $"inter-crew-reversal-source:{key}"));
            transfer.Status = InterCrewTransferStatuses.Reversed; transfer.ReversalOperationKey = key;
            transfer.ReversalReason = form.Reason.Trim(); transfer.ReversedAt = now; transfer.ReversedByUserId = actor.Id; transfer.ConcurrencyVersion++;
            await invariant.ValidateBeforeCommitAsync(cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            var lineageResult = await treatmentLineage.ReverseAsync(transfer.Id, wasReceived,
                $"inter-crew-reversal:{key}:treatment", now, actor.Id, cancellationToken);
            if (!lineageResult.Success) throw new InvalidOperationException(lineageResult.Error);
            AddAudit(actor.Id, "InterCrewTransferReversed", transfer, new { transfer.BinsLoaded, transfer.BinsReceived, Reason = transfer.ReversalReason });
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or DbUpdateException)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            return exception.Message;
        }
    }

    public async Task<InterCrewTransferDetailViewModel?> GetDetailsAsync(long id, CancellationToken cancellationToken)
    {
        var x = await dbContext.InterCrewTransfers.AsNoTracking()
            .Include(t => t.SourceWarehouse).Include(t => t.SourceRoom).Include(t => t.DestinationWarehouse).Include(t => t.DestinationRoom)
            .Include(t => t.LoadedByUser).Include(t => t.ReceivedByUser).Include(t => t.ReviewedByUser)
            .SingleOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (x is null) return null;
        var principal = httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();
        var actor = await GetActorAsync(cancellationToken);
        var canAdmin = await CanAdminAsync(principal, cancellationToken);
        var canReceive = x.Status == InterCrewTransferStatuses.InTransit
            && await access.HasAccessAsync(principal, ApplicationAreas.Transfers, PageAccessLevel.Create, cancellationToken)
            && CanAccessGroup(CustodyGroupForUser(actor), canAdmin, x.DestinationCustodyGroup);
        var rooms = await dbContext.Rooms.AsNoTracking().Include(r => r.Warehouse)
            .Where(r => r.IsActive && !r.Warehouse.Code.ToUpper().Contains("MCD") && r.Warehouse.Code != "McDougall")
            .OrderBy(r => r.Warehouse.Code).ThenBy(r => r.SortOrder).ToListAsync(cancellationToken);
        return new InterCrewTransferDetailViewModel
        {
            Id = x.Id,
            Status = x.Status,
            Source = $"{x.SourceWarehouse.Code} / {RoomLabel(x.SourceRoom)}",
            DestinationGroup = TransferCustodyGroups.Label(x.DestinationCustodyGroup),
            Destination = x.DestinationRoom is null ? null : $"{x.DestinationWarehouse!.Code} / {RoomLabel(x.DestinationRoom)}",
            Grower = $"{x.GrowerNumberSnapshot} - {x.GrowerNameSnapshot}",
            Lot = x.LotNumberSnapshot,
            Variety = $"{x.VarietyCodeSnapshot} ({x.ProductionTypeSnapshot})",
            Treatment = x.TreatmentSummarySnapshot,
            BinsLoaded = x.BinsLoaded,
            BinsReceived = x.BinsReceived,
            Variance = x.VarianceBins,
            LoadedAt = x.LoadedAt,
            ReceivedAt = x.ReceivedAt,
            Bol = x.TruckLoadBolNumber,
            Notes = x.Notes,
            LoadedBy = UserLabel(x.LoadedByUser),
            ReceivedBy = x.ReceivedByUser is null ? null : UserLabel(x.ReceivedByUser),
            ReviewNote = x.ReviewNote,
            ReviewedBy = x.ReviewedByUser is null ? null : UserLabel(x.ReviewedByUser),
            ReviewedAt = x.ReviewedAt,
            ReversalReason = x.ReversalReason,
            CanReceive = canReceive,
            CanAdmin = canAdmin,
            DestinationRooms = rooms.Where(r => TransferCustodyGroups.ContainsWarehouse(x.DestinationCustodyGroup, r.Warehouse.Code))
                .Select(r => new InterCrewDestinationRoomViewModel(r.Id, r.Warehouse.Code, RoomLabel(r))).ToList()
        };
    }

    private async Task<RoomInventoryLedgerSnapshot?> FindSnapshotAsync(
        InterCrewTransfer transfer,
        int warehouseId,
        int roomId,
        CancellationToken cancellationToken,
        InventoryIdentityKey? identity = null)
    {
        var cropYear = identity?.CropYear ?? transfer.CropYear;
        var growerLotId = identity?.GrowerLotId ?? transfer.GrowerLotId;
        var fruitProfileId = identity?.FruitProfileId ?? transfer.FruitProfileId;
        var matches = (await ledger.GetSnapshotsAsync(warehouseId, [roomId], cancellationToken)).Where(x =>
            x.CropYear == cropYear && x.GrowerLotId == growerLotId && x.FruitProfileId == fruitProfileId
            && (identity is not null || Same(x.Lot, transfer.LotNumberSnapshot) && Same(x.InventoryStatus, transfer.InventoryStatusSnapshot))).ToList();
        if (matches.Count == 0) return null;
        var latest = matches.OrderByDescending(x => x.LastTransactionAt).ThenByDescending(x => x.LatestAdjustmentId).First();
        return latest with { CurrentBins = matches.Sum(x => x.CurrentBins), LatestAdjustmentId = matches.Max(x => x.LatestAdjustmentId) };
    }

    private static RoomInventoryAdjustment Adjustment(InterCrewTransfer transfer, int warehouseId, int roomId, int delta,
        int oldBalance, string type, string source, string reason, DateTimeOffset at, int actorId, string operationKey,
        InventoryIdentityResolution? resolvedIdentity = null) => new()
        {
            CropYear = resolvedIdentity?.Canonical.CropYear ?? transfer.CropYear,
            ReceiptId = transfer.ReceiptId,
            WarehouseId = warehouseId,
            RoomId = roomId,
            GrowerLotId = resolvedIdentity?.Canonical.GrowerLotId ?? transfer.GrowerLotId,
            FruitProfileId = resolvedIdentity?.Canonical.FruitProfileId ?? transfer.FruitProfileId,
            GrowerName = resolvedIdentity?.GrowerLot.Grower ?? transfer.GrowerNameSnapshot,
            LotNumber = resolvedIdentity?.GrowerLot.LotNumber ?? transfer.LotNumberSnapshot,
            VarietyCode = resolvedIdentity?.FruitProfile.VarietyCode ?? transfer.VarietyCodeSnapshot,
            OldBinCount = oldBalance,
            ChangeAmount = delta,
            NewBinCount = oldBalance + delta,
            AdjustmentType = type,
            Source = source,
            InventoryStatus = resolvedIdentity?.FruitProfile.ProductionType ?? transfer.InventoryStatusSnapshot,
            Reason = reason,
            Notes = transfer.TruckLoadBolNumber,
            AdjustmentAt = at,
            CreatedByUserId = actorId,
            CreatedAt = at,
            InventoryInvariantVersion = InventoryDeductionInvariantService.CurrentVersion,
            InventoryOperationKey = operationKey,
            InterCrewTransfer = transfer
        };

    private void AddAudit(int actorId, string action, InterCrewTransfer transfer, object values) => dbContext.AuditLogs.Add(new AuditLog
    {
        UserId = actorId,
        Action = action,
        EntityName = nameof(InterCrewTransfer),
        EntityKey = transfer.Id.ToString(CultureInfo.InvariantCulture),
        AfterValuesJson = JsonSerializer.Serialize(values, AuditJson),
        SourceApplication = AuditSource,
        CreatedAt = businessTime.UtcNow
    });

    private async Task<User?> GetActorAsync(CancellationToken cancellationToken)
    {
        var email = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();
        return email is null ? null : await dbContext.Users.SingleOrDefaultAsync(x => x.Email == email && x.IsActive, cancellationToken);
    }

    private async Task<bool> CanAdminAsync(ClaimsPrincipal principal, CancellationToken cancellationToken) =>
        await access.HasAccessAsync(principal, ApplicationAreas.Transfers, PageAccessLevel.Admin, cancellationToken)
        || await access.HasAccessAsync(principal, ApplicationAreas.RoomTransactions, PageAccessLevel.Admin, cancellationToken);

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is not null || (dbContext.Database.ProviderName ?? "").Contains("InMemory", StringComparison.OrdinalIgnoreCase)) return null;
        return await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
    }

    internal static IReadOnlyList<string> AllowedDestinationGroups(string? sourceWarehouse) => sourceWarehouse?.Trim().ToUpperInvariant() switch
    {
        "WP" or "DH" => [TransferCustodyGroups.Ebs],
        "EBS" => [TransferCustodyGroups.WpDh],
        "MCD" or "MCDOUGALL" => [TransferCustodyGroups.WpDh, TransferCustodyGroups.Ebs],
        _ => []
    };

    private const string SharedCustodyGroup = "SHARED";

    private static string? CustodyGroupForUser(User? user) => user?.EmploymentFacility switch
    {
        EmploymentFacilities.Wp => TransferCustodyGroups.WpDh,
        EmploymentFacilities.Ebs => TransferCustodyGroups.Ebs,
        EmploymentFacilities.Shared => SharedCustodyGroup,
        _ => null
    };
    private static bool CanAccessGroup(string? userGroup, bool canAdmin, string destinationGroup) =>
        canAdmin || userGroup == SharedCustodyGroup || userGroup == destinationGroup;
    private static InterCrewWriteResult Fail(string? error) => new(false, false, null, error);
    private static InterCrewTransferListItemViewModel ListItem(InterCrewTransfer x, bool canReceive) => new(
        x.Id, x.LoadedAt, $"{x.SourceWarehouse.Code} / {RoomLabel(x.SourceRoom)}", TransferCustodyGroups.Label(x.DestinationCustodyGroup),
        $"{x.GrowerNumberSnapshot} - {x.GrowerNameSnapshot}", x.LotNumberSnapshot, x.VarietyCodeSnapshot,
        x.TreatmentSummarySnapshot, x.BinsLoaded, x.BinsReceived, x.VarianceBins, x.TruckLoadBolNumber, x.Status, canReceive);
    private static string RoomLabel(Room room) => room.CropQcRoomName ?? room.DisplayName ?? room.Code;
    private static string UserLabel(User user) => user.DisplayName ?? user.Email;
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool Same(string? left, string? right) => string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
}
