using System.Data;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CropQc.Web.Services;

public sealed record OutsideWarehouseTransferWriteResult(bool Success, bool AlreadyApplied, long? TransferId, string? Error);

public interface IOutsideWarehouseTransferService
{
    Task<OutsideWarehouseTransferPageViewModel> GetPageAsync(BinsRunFilterForm filter, CancellationToken cancellationToken);
    Task<OutsideWarehouseTransferWriteResult> CreateAsync(OutsideWarehouseTransferForm form, CancellationToken cancellationToken);
    Task<OutsideWarehouseTransferDetailViewModel?> GetDetailsAsync(long id, CancellationToken cancellationToken);
    Task<string?> ReverseAsync(OutsideWarehouseTransferReversalForm form, CancellationToken cancellationToken);
}

public sealed class OutsideWarehouseTransferService(
    CropQcDbContext dbContext,
    IRoomInventoryLedgerQueryService ledger,
    IRoomTreatmentService roomTreatments,
    IOutsideWarehouseTreatmentLineageService treatmentLineage,
    IInventoryDeductionInvariantService invariant,
    IUserAccessService access,
    IHttpContextAccessor httpContextAccessor,
    IBusinessTimeService businessTime) : IOutsideWarehouseTransferService
{
    private const string AuditSource = "CropQc.Web outside warehouse transfer workflow";
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    public async Task<OutsideWarehouseTransferPageViewModel> GetPageAsync(
        BinsRunFilterForm filter,
        CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();
        var inventory = await GetInventoryOptionsAsync(cancellationToken);
        if (filter.WarehouseId is int warehouseId)
            inventory = inventory.Where(x => x.WarehouseId == warehouseId).ToList();
        if (filter.RoomId is int roomId)
            inventory = inventory.Where(x => x.RoomId == roomId).ToList();
        var locations = await dbContext.OutsideWarehouses.AsNoTracking()
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Code)
            .Select(x => new OutsideWarehouseOptionViewModel(x.Id, x.Code, x.Name, x.IsActive))
            .ToListAsync(cancellationToken);
        var query = dbContext.OutsideWarehouseTransfers.AsNoTracking()
            .Include(x => x.SourceWarehouse)
            .Include(x => x.SourceRoom)
            .Include(x => x.CreatedByUser)
            .AsQueryable();
        if (filter.OutsideWarehouseId is int outsideWarehouseId) query = query.Where(x => x.OutsideWarehouseId == outsideWarehouseId);
        if (filter.OutsideSourceWarehouseId is int sourceWarehouseId) query = query.Where(x => x.SourceWarehouseId == sourceWarehouseId);
        if (filter.OutsideSourceRoomId is int sourceRoomId) query = query.Where(x => x.SourceRoomId == sourceRoomId);
        if (!string.IsNullOrWhiteSpace(filter.OutsideGrowerNumber)) query = query.Where(x => x.GrowerNumberSnapshot == filter.OutsideGrowerNumber.Trim());
        if (!string.IsNullOrWhiteSpace(filter.OutsideVariety)) query = query.Where(x => x.VarietyCodeSnapshot == filter.OutsideVariety.Trim());
        if (string.Equals(filter.OutsideStatus, "Active", StringComparison.OrdinalIgnoreCase)) query = query.Where(x => !x.IsReversed);
        if (string.Equals(filter.OutsideStatus, "Reversed", StringComparison.OrdinalIgnoreCase)) query = query.Where(x => x.IsReversed);
        if (DateTime.TryParse(filter.OutsideFrom, CultureInfo.InvariantCulture, DateTimeStyles.None, out var from))
            query = query.Where(x => x.TransferredAt >= businessTime.PacificLocalToUtc(from.Date));
        if (DateTime.TryParse(filter.OutsideTo, CultureInfo.InvariantCulture, DateTimeStyles.None, out var to))
            query = query.Where(x => x.TransferredAt < businessTime.PacificLocalToUtc(to.Date.AddDays(1)));

        var history = await query.OrderByDescending(x => x.TransferredAt).ThenByDescending(x => x.Id).Take(500)
            .Select(x => new OutsideWarehouseTransferHistoryViewModel(
                x.Id,
                x.TransferredAt,
                x.OutsideWarehouseNameSnapshot,
                x.OutsideWarehouseCodeSnapshot,
                x.SourceWarehouse.Code,
                x.SourceRoom.CropQcRoomName ?? x.SourceRoom.DisplayName ?? x.SourceRoom.Code,
                x.GrowerNumberSnapshot,
                x.GrowerNameSnapshot,
                x.LotNumberSnapshot,
                x.VarietyCodeSnapshot,
                x.ProductionTypeSnapshot,
                x.BinCount,
                x.TruckLoadBolNumber,
                x.CreatedByUser.DisplayName ?? x.CreatedByUser.Email,
                x.IsReversed))
            .ToListAsync(cancellationToken);

        var form = new OutsideWarehouseTransferForm
        {
            SourceKey = filter.SourceKey ?? "",
            TransferredAt = businessTime.NowPacific.DateTime
        };
        var selected = inventory.SingleOrDefault(x => x.SourceKey == form.SourceKey);
        if (selected is not null) form.ExpectedAvailableBins = selected.AvailableBins;
        return new OutsideWarehouseTransferPageViewModel
        {
            Form = form,
            OutsideWarehouses = locations.Where(x => x.IsActive).ToList(),
            ReportOutsideWarehouses = locations,
            Inventory = inventory,
            History = history,
            ReviewSource = selected,
            CanCreate = await access.HasAccessAsync(principal, ApplicationAreas.Transfers, PageAccessLevel.Create, cancellationToken),
            CanAdmin = await access.HasAccessAsync(principal, ApplicationAreas.Transfers, PageAccessLevel.Admin, cancellationToken)
                || await access.HasAccessAsync(principal, ApplicationAreas.RoomTransactions, PageAccessLevel.Admin, cancellationToken)
        };
    }

    public async Task<OutsideWarehouseTransferWriteResult> CreateAsync(
        OutsideWarehouseTransferForm form,
        CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();
        if (!await access.HasAccessAsync(principal, ApplicationAreas.Transfers, PageAccessLevel.Create, cancellationToken))
            return new(false, false, null, "Transfers Create access is required to record an Outside Warehouse Transfer.");
        if (!form.ConfirmedReview) return new(false, false, null, "Review the Outside Warehouse Transfer before confirming it.");
        if (form.OutsideWarehouseId is null) return new(false, false, null, "Select an active Outside Warehouse.");
        if (form.TransferredAt == default) return new(false, false, null, "Transfer date and time are required.");
        if (form.BinCount <= 0) return new(false, false, null, "Bins must be greater than zero.");
        if (Normalize(form.TruckLoadBolNumber)?.Length > 150) return new(false, false, null, "Truck / Load / BOL must be 150 characters or fewer.");
        if (Normalize(form.Notes)?.Length > 1000) return new(false, false, null, "Notes must be 1,000 characters or fewer.");
        var operationKey = Normalize(form.OperationKey);
        if (operationKey is null || operationKey.Length > 150) return new(false, false, null, "The transfer operation key is invalid. Refresh and retry.");
        var existing = await dbContext.OutsideWarehouseTransfers.AsNoTracking().SingleOrDefaultAsync(x => x.OperationKey == operationKey, cancellationToken);
        if (existing is not null) return new(true, true, existing.Id, null);
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return new(false, false, null, "The current active user could not be resolved.");

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            var outsideWarehouse = await dbContext.OutsideWarehouses.SingleOrDefaultAsync(
                x => x.Id == form.OutsideWarehouseId && x.IsActive,
                cancellationToken);
            if (outsideWarehouse is null) return new(false, false, null, "Select an active Outside Warehouse.");
            var options = await GetInventoryOptionsAsync(cancellationToken);
            var option = options.SingleOrDefault(x => x.SourceKey == form.SourceKey);
            if (option is null) return new(false, false, null, "The selected current inventory is no longer available. Refresh and retry.");
            if (form.ExpectedAvailableBins != option.AvailableBins)
                return new(false, false, null, "Source inventory changed after this page loaded. Refresh before retrying.");
            if (form.BinCount > option.AvailableBins)
                return new(false, false, null, $"Only {option.AvailableBins} bins remain in the selected treatment segment.");
            var sealError = await RoomMovementSealGuard.ValidateAsync(dbContext, [option.RoomId], [], businessTime, cancellationToken);
            if (sealError is not null) return new(false, false, null, sealError);

            var exactSnapshot = await FindSnapshotAsync(option, cancellationToken);
            if (exactSnapshot is null || exactSnapshot.CurrentBins < form.BinCount)
                return new(false, false, null, "The exact source inventory changed while this transfer was being saved. Refresh and retry.");
            var now = businessTime.UtcNow;
            var transferredAt = businessTime.PacificLocalToUtc(form.TransferredAt);
            var transfer = new OutsideWarehouseTransfer
            {
                OperationKey = operationKey,
                OutsideWarehouseId = outsideWarehouse.Id,
                OutsideWarehouseCodeSnapshot = outsideWarehouse.Code,
                OutsideWarehouseNameSnapshot = outsideWarehouse.Name,
                OutsideWarehouseAddressSnapshot = outsideWarehouse.Address,
                SourceWarehouseId = option.WarehouseId,
                SourceRoomId = option.RoomId,
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
                BinCount = form.BinCount,
                TransferredAt = transferredAt,
                TruckLoadBolNumber = Normalize(form.TruckLoadBolNumber),
                Notes = Normalize(form.Notes),
                CreatedByUserId = actor.Id,
                CreatedAt = now
            };
            dbContext.OutsideWarehouseTransfers.Add(transfer);
            await dbContext.SaveChangesAsync(cancellationToken);

            dbContext.RoomInventoryAdjustments.Add(new RoomInventoryAdjustment
            {
                CropYear = transfer.CropYear,
                ReceiptId = transfer.ReceiptId,
                WarehouseId = transfer.SourceWarehouseId,
                RoomId = transfer.SourceRoomId,
                GrowerLotId = transfer.GrowerLotId,
                FruitProfileId = transfer.FruitProfileId,
                GrowerName = transfer.GrowerNameSnapshot,
                LotNumber = transfer.LotNumberSnapshot,
                VarietyCode = transfer.VarietyCodeSnapshot,
                OldBinCount = exactSnapshot.CurrentBins,
                ChangeAmount = -transfer.BinCount,
                NewBinCount = exactSnapshot.CurrentBins - transfer.BinCount,
                AdjustmentType = OutsideWarehouseTransferAdjustmentTypes.Transfer,
                Source = "Outside Warehouse Transfer",
                InventoryStatus = transfer.InventoryStatusSnapshot,
                Reason = $"Sent to {transfer.OutsideWarehouseNameSnapshot}",
                Notes = transfer.TruckLoadBolNumber,
                AdjustmentAt = transfer.TransferredAt,
                CreatedByUserId = actor.Id,
                CreatedAt = now,
                InventoryInvariantVersion = InventoryDeductionInvariantService.CurrentVersion,
                InventoryOperationKey = $"outside-warehouse-transfer:{operationKey}",
                OutsideWarehouseTransfer = transfer
            });
            await invariant.ValidateBeforeCommitAsync(cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);

            var lineage = await treatmentLineage.MoveToOutsideWarehouseAsync(
                exactSnapshot,
                option.TreatmentSignature,
                transfer.BinCount,
                $"outside-warehouse-transfer:{operationKey}:treatment",
                transfer.Id,
                transfer.TransferredAt,
                actor.Id,
                cancellationToken);
            if (!lineage.Success || lineage.MovementId is null)
                throw new InvalidOperationException(lineage.Error ?? "The exact treatment lineage could not be preserved.");

            dbContext.AuditLogs.Add(new AuditLog
            {
                UserId = actor.Id,
                Action = "OutsideWarehouseTransferCreated",
                EntityName = nameof(OutsideWarehouseTransfer),
                EntityKey = transfer.Id.ToString(CultureInfo.InvariantCulture),
                AfterValuesJson = JsonSerializer.Serialize(new
                {
                    transfer.OutsideWarehouseId,
                    transfer.OutsideWarehouseCodeSnapshot,
                    transfer.OutsideWarehouseNameSnapshot,
                    transfer.SourceWarehouseId,
                    transfer.SourceRoomId,
                    transfer.ReceiptId,
                    transfer.GrowerLotId,
                    transfer.FruitProfileId,
                    transfer.GrowerNumberSnapshot,
                    transfer.LotNumberSnapshot,
                    transfer.VarietyCodeSnapshot,
                    transfer.TreatmentSignatureSnapshot,
                    transfer.BinCount,
                    transfer.TransferredAt,
                    transfer.TruckLoadBolNumber
                }, AuditJson),
                SourceApplication = AuditSource,
                CreatedAt = now
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new(true, false, transfer.Id, null);
        }
        catch (Exception exception) when (exception is InvalidOperationException or DbUpdateException)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            return new(false, false, null, exception.Message);
        }
    }

    public async Task<OutsideWarehouseTransferDetailViewModel?> GetDetailsAsync(long id, CancellationToken cancellationToken)
    {
        var transfer = await dbContext.OutsideWarehouseTransfers.AsNoTracking()
            .Include(x => x.SourceWarehouse)
            .Include(x => x.SourceRoom)
            .Include(x => x.CreatedByUser)
            .Include(x => x.ReversedByUser)
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (transfer is null) return null;
        var principal = httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();
        return new OutsideWarehouseTransferDetailViewModel
        {
            Id = transfer.Id,
            TransferredAt = transfer.TransferredAt,
            OutsideWarehouse = transfer.OutsideWarehouseNameSnapshot,
            OutsideWarehouseCode = transfer.OutsideWarehouseCodeSnapshot,
            OutsideWarehouseAddress = transfer.OutsideWarehouseAddressSnapshot,
            Facility = transfer.SourceWarehouse.Code,
            Room = transfer.SourceRoom.CropQcRoomName ?? transfer.SourceRoom.DisplayName ?? transfer.SourceRoom.Code,
            ReceiptId = transfer.ReceiptId,
            SourceInventoryAdjustmentId = transfer.SourceInventoryAdjustmentId,
            GrowerLotId = transfer.GrowerLotId,
            FruitProfileId = transfer.FruitProfileId,
            CropYear = transfer.CropYear,
            GrowerNumber = transfer.GrowerNumberSnapshot,
            GrowerName = transfer.GrowerNameSnapshot,
            Lot = transfer.LotNumberSnapshot,
            Variety = transfer.VarietyCodeSnapshot,
            ProductionType = transfer.ProductionTypeSnapshot,
            OrganicStatus = OrganicLabel(transfer.IsOrganicSnapshot, transfer.ProductionTypeSnapshot),
            InventoryStatus = transfer.InventoryStatusSnapshot ?? "",
            Treatment = transfer.TreatmentSummarySnapshot,
            Bins = transfer.BinCount,
            TruckLoadBolNumber = transfer.TruckLoadBolNumber,
            Notes = transfer.Notes,
            RecordedBy = transfer.CreatedByUser.DisplayName ?? transfer.CreatedByUser.Email,
            IsReversed = transfer.IsReversed,
            ReversedAt = transfer.ReversedAt,
            ReversedBy = transfer.ReversedByUser == null ? null : transfer.ReversedByUser.DisplayName ?? transfer.ReversedByUser.Email,
            ReverseReason = transfer.ReverseReason,
            CanAdmin = await access.HasAccessAsync(principal, ApplicationAreas.Transfers, PageAccessLevel.Admin, cancellationToken)
                || await access.HasAccessAsync(principal, ApplicationAreas.RoomTransactions, PageAccessLevel.Admin, cancellationToken)
        };
    }

    public async Task<string?> ReverseAsync(OutsideWarehouseTransferReversalForm form, CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User ?? new ClaimsPrincipal();
        var canAdmin = await access.HasAccessAsync(principal, ApplicationAreas.Transfers, PageAccessLevel.Admin, cancellationToken)
            || await access.HasAccessAsync(principal, ApplicationAreas.RoomTransactions, PageAccessLevel.Admin, cancellationToken);
        if (!canAdmin) return "Transfer Admin access is required to reverse an Outside Warehouse Transfer.";
        if (string.IsNullOrWhiteSpace(form.Reason)) return "A reversal reason is required.";
        var operationKey = Normalize(form.OperationKey);
        if (operationKey is null || operationKey.Length > 150) return "The reversal operation key is invalid.";
        var actor = await GetActorAsync(cancellationToken);
        if (actor is null) return "The current active user could not be resolved.";

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            var transfer = await dbContext.OutsideWarehouseTransfers
                .Include(x => x.InventoryAdjustments)
                .SingleOrDefaultAsync(x => x.Id == form.TransferId, cancellationToken);
            if (transfer is null) return "Outside Warehouse Transfer was not found.";
            if (transfer.IsReversed) return null;
            if (await dbContext.OutsideWarehouseTransfers.AsNoTracking().AnyAsync(x => x.ReversalOperationKey == operationKey, cancellationToken)) return null;
            var sealError = await RoomMovementSealGuard.ValidateAsync(dbContext, [], [transfer.SourceRoomId], businessTime, cancellationToken);
            if (sealError is not null) return sealError;
            var original = transfer.InventoryAdjustments.SingleOrDefault(x =>
                x.AdjustmentType == OutsideWarehouseTransferAdjustmentTypes.Transfer
                && x.ChangeAmount == -transfer.BinCount);
            if (original is null || transfer.InventoryAdjustments.Any(x => x.AdjustmentType == OutsideWarehouseTransferAdjustmentTypes.Reversal))
                throw new InvalidOperationException("The exact Outside Warehouse Transfer ledger history cannot be deterministically reversed.");
            var current = await FindSnapshotAsync(transfer, cancellationToken);
            var oldBalance = current?.CurrentBins ?? 0;
            var now = businessTime.UtcNow;
            dbContext.RoomInventoryAdjustments.Add(new RoomInventoryAdjustment
            {
                CropYear = transfer.CropYear,
                ReceiptId = transfer.ReceiptId,
                WarehouseId = transfer.SourceWarehouseId,
                RoomId = transfer.SourceRoomId,
                GrowerLotId = transfer.GrowerLotId,
                FruitProfileId = transfer.FruitProfileId,
                GrowerName = transfer.GrowerNameSnapshot,
                LotNumber = transfer.LotNumberSnapshot,
                VarietyCode = transfer.VarietyCodeSnapshot,
                OldBinCount = oldBalance,
                ChangeAmount = transfer.BinCount,
                NewBinCount = oldBalance + transfer.BinCount,
                AdjustmentType = OutsideWarehouseTransferAdjustmentTypes.Reversal,
                Source = "Outside Warehouse Transfer Reversal",
                InventoryStatus = transfer.InventoryStatusSnapshot,
                Reason = form.Reason.Trim(),
                AdjustmentAt = now,
                CreatedByUserId = actor.Id,
                CreatedAt = now,
                InventoryInvariantVersion = InventoryDeductionInvariantService.CurrentVersion,
                InventoryOperationKey = $"outside-warehouse-transfer-reversal:{operationKey}",
                OutsideWarehouseTransfer = transfer
            });
            transfer.IsReversed = true;
            transfer.ReversalOperationKey = operationKey;
            transfer.ReversedAt = now;
            transfer.ReversedByUserId = actor.Id;
            transfer.ReverseReason = form.Reason.Trim();
            transfer.ConcurrencyVersion++;
            await invariant.ValidateBeforeCommitAsync(cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            var lineage = await treatmentLineage.ReverseOutsideWarehouseMovementAsync(
                $"outside-warehouse-transfer-reversal:{operationKey}:treatment",
                transfer.Id,
                now,
                actor.Id,
                cancellationToken);
            if (!lineage.Success || lineage.MovementId is null)
                throw new InvalidOperationException(lineage.Error ?? "The exact treatment lineage could not be restored.");
            dbContext.AuditLogs.Add(new AuditLog
            {
                UserId = actor.Id,
                Action = "OutsideWarehouseTransferReversed",
                EntityName = nameof(OutsideWarehouseTransfer),
                EntityKey = transfer.Id.ToString(CultureInfo.InvariantCulture),
                BeforeValuesJson = JsonSerializer.Serialize(new { transfer.BinCount, IsReversed = false }, AuditJson),
                AfterValuesJson = JsonSerializer.Serialize(new { IsReversed = true, Reason = transfer.ReverseReason, RestoredBins = transfer.BinCount }, AuditJson),
                SourceApplication = AuditSource,
                CreatedAt = now
            });
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

    private async Task<List<OutsideWarehouseInventoryOptionViewModel>> GetInventoryOptionsAsync(CancellationToken cancellationToken)
    {
        var snapshots = (await ledger.GetSnapshotsAsync(null, null, cancellationToken))
            .Where(x => x.CurrentBins > 0)
            .GroupBy(RoomTreatmentService.SelectionLookupKey, StringComparer.Ordinal)
            .Select(x => ConsolidateSnapshots(x.ToList()))
            .ToList();
        var roomIds = snapshots.Select(x => x.RoomId).Distinct().ToList();
        var sealedRoomIds = await dbContext.Rooms.AsNoTracking()
            .Where(x => roomIds.Contains(x.Id) && x.IsSealed && (x.SealedAt == null || x.SealedAt <= businessTime.UtcNow))
            .Select(x => x.Id)
            .ToHashSetAsync(cancellationToken);
        var result = new List<OutsideWarehouseInventoryOptionViewModel>();
        foreach (var snapshot in snapshots)
        {
            var selections = (await roomTreatments.GetSelectionsAsync(snapshot, cancellationToken)).Where(x => x.CurrentBins > 0).ToList();
            foreach (var selectionGroup in selections.GroupBy(x => x.TreatmentSignature, StringComparer.Ordinal))
            {
                var grouped = selectionGroup.OrderBy(x => x.ReceiptId ?? long.MaxValue).ThenBy(x => x.SegmentId ?? long.MaxValue).ToList();
                var selection = grouped[0];
                var explicitReceipts = grouped.Select(x => x.ReceiptId).Where(x => x is not null).Distinct().ToList();
                // A pooled inventory identity can span Receipts. Only snapshot a Receipt when
                // treatment lineage resolves exactly one; never infer provenance from the latest
                // adjustment in an otherwise unassigned aggregate.
                var receiptId = explicitReceipts.Count == 1 ? explicitReceipts[0] : null;
                var segmentId = grouped.Count == 1 ? selection.SegmentId : null;
                result.Add(new OutsideWarehouseInventoryOptionViewModel(
                    SourceKey(snapshot, selection.IdentityKey, selection.TreatmentSignature),
                    snapshot.WarehouseId,
                    snapshot.Facility,
                    snapshot.RoomId,
                    snapshot.Room,
                    snapshot.CropYear,
                    snapshot.GrowerLotId,
                    snapshot.FruitProfileId,
                    snapshot.Grower,
                    snapshot.GrowerNumber,
                    snapshot.Lot,
                    snapshot.Variety,
                    snapshot.VarietyName,
                    snapshot.ProductionType,
                    snapshot.IsOrganic,
                    snapshot.InventoryStatus,
                    selection.TreatmentState,
                    selection.TreatmentSignature,
                    grouped.Select(x => x.Label).Distinct(StringComparer.Ordinal).Count() == 1
                        ? selection.Label
                        : $"{selection.TreatmentState} ({grouped.Count} provenance segments)",
                    grouped.Sum(x => x.CurrentBins),
                    snapshot.LatestAdjustmentId,
                    receiptId,
                    sealedRoomIds.Contains(snapshot.RoomId),
                    segmentId));
            }
        }
        return result.OrderBy(x => x.Facility).ThenBy(x => x.Room).ThenBy(x => x.GrowerNumber).ThenBy(x => x.VarietyName).ThenBy(x => x.TreatmentSummary).ToList();
    }

    private async Task<RoomInventoryLedgerSnapshot?> FindSnapshotAsync(OutsideWarehouseInventoryOptionViewModel option, CancellationToken cancellationToken)
    {
        var matches = (await ledger.GetSnapshotsAsync(option.WarehouseId, [option.RoomId], cancellationToken)).Where(x =>
            x.CropYear == option.CropYear && x.GrowerLotId == option.GrowerLotId && x.FruitProfileId == option.FruitProfileId
            && Same(x.GrowerNumber, option.GrowerNumber) && Same(x.Lot, option.LotNumber) && Same(x.Variety, option.VarietyCode)
            && Same(x.ProductionType, option.ProductionType) && x.IsOrganic == option.IsOrganic
            && Same(x.InventoryStatus, option.InventoryStatus)).ToList();
        return matches.Count == 0 ? null : ConsolidateSnapshots(matches);
    }

    private async Task<RoomInventoryLedgerSnapshot?> FindSnapshotAsync(OutsideWarehouseTransfer transfer, CancellationToken cancellationToken)
    {
        var matches = (await ledger.GetSnapshotsAsync(transfer.SourceWarehouseId, [transfer.SourceRoomId], cancellationToken)).Where(x =>
            x.CropYear == transfer.CropYear && x.GrowerLotId == transfer.GrowerLotId && x.FruitProfileId == transfer.FruitProfileId
            && Same(x.GrowerNumber, transfer.GrowerNumberSnapshot) && Same(x.Lot, transfer.LotNumberSnapshot) && Same(x.Variety, transfer.VarietyCodeSnapshot)
            && Same(x.ProductionType, transfer.ProductionTypeSnapshot) && x.IsOrganic == transfer.IsOrganicSnapshot
            && Same(x.InventoryStatus, transfer.InventoryStatusSnapshot)).ToList();
        return matches.Count == 0 ? null : ConsolidateSnapshots(matches);
    }

    private static string SourceKey(RoomInventoryLedgerSnapshot snapshot, string identityKey, string signature)
    {
        var raw = $"{snapshot.WarehouseId}|{snapshot.RoomId}|{identityKey}|{signature}|{snapshot.LatestAdjustmentId}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    private static RoomInventoryLedgerSnapshot ConsolidateSnapshots(IReadOnlyList<RoomInventoryLedgerSnapshot> snapshots)
    {
        var distinct = snapshots.Distinct().ToList();
        var latest = distinct.OrderByDescending(x => x.LastTransactionAt).ThenByDescending(x => x.LatestAdjustmentId).First();
        return latest with
        {
            PositiveBins = distinct.Sum(x => x.PositiveBins),
            NegativeBins = distinct.Sum(x => x.NegativeBins),
            ActualRunDepletionBins = distinct.Sum(x => x.ActualRunDepletionBins),
            ActualRunReversalBins = distinct.Sum(x => x.ActualRunReversalBins),
            LegacyBinsRunDepletionBins = distinct.Sum(x => x.LegacyBinsRunDepletionBins),
            TransferInBins = distinct.Sum(x => x.TransferInBins),
            TransferOutBins = distinct.Sum(x => x.TransferOutBins),
            TrueUpBins = distinct.Sum(x => x.TrueUpBins),
            OtherAdjustmentBins = distinct.Sum(x => x.OtherAdjustmentBins),
            CurrentBins = distinct.Sum(x => x.CurrentBins),
            TransactionCount = distinct.Sum(x => x.TransactionCount),
            FirstTransactionAt = distinct.Min(x => x.FirstTransactionAt),
            LastTransactionAt = distinct.Max(x => x.LastTransactionAt),
            LatestAdjustmentId = distinct.Max(x => x.LatestAdjustmentId),
            DroppedBins = distinct.Sum(x => x.DroppedBins),
            DroppedBinsRestored = distinct.Sum(x => x.DroppedBinsRestored)
        };
    }

    private async Task<User?> GetActorAsync(CancellationToken cancellationToken)
    {
        var email = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();
        return email is null ? null : await dbContext.Users.SingleOrDefaultAsync(x => x.Email == email && x.IsActive, cancellationToken);
    }

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is not null || (dbContext.Database.ProviderName ?? "").Contains("InMemory", StringComparison.OrdinalIgnoreCase)) return null;
        return await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
    }

    private static string OrganicLabel(bool? organic, string production) => organic switch { true => "Organic", false => "Conventional", _ => production };
    private static bool Same(string? left, string? right) => string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
