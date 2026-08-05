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

public interface IReceiptInventoryOverrideService
{
    Task<ReceiptInventoryOverridePreviewViewModel?> GetPreviewAsync(long receiptId, CancellationToken cancellationToken);
    Task<ReceiptInventoryOverrideResult> ApplyEditAsync(
        AdminReceiptInventoryOverrideForm form,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken);
    Task<ReceiptInventoryOverrideResult> VoidAsync(
        DeleteReceiptForm form,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken);
    Task<ReceiptInventoryOverrideAuditViewModel?> GetAuditDetailAsync(Guid overrideId, CancellationToken cancellationToken);
    Task<IReadOnlyList<VoidedReceiptAdminViewModel>> GetVoidedReceiptsAsync(CancellationToken cancellationToken);
}

public sealed class ReceiptInventoryOverrideService(
    CropQcDbContext dbContext,
    IUserAccessService userAccessService,
    IInventoryDeductionInvariantService inventoryInvariantService,
    IBusinessTimeService businessTime,
    ILogger<ReceiptInventoryOverrideService> logger) : IReceiptInventoryOverrideService
{
    public const string AdjustmentType = "ReceiptAdminOverride";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ReceiptInventoryOverridePreviewViewModel?> GetPreviewAsync(
        long receiptId,
        CancellationToken cancellationToken)
    {
        var receipt = await ReceiptQuery(asTracking: false)
            .SingleOrDefaultAsync(x => x.Id == receiptId && !x.IsDeleted, cancellationToken);
        if (receipt is null) return null;

        var state = await GetInventoryStateAsync(receipt, cancellationToken);
        var counts = await GetOperationalCountsAsync(receipt.Id, cancellationToken);
        var hasPriorOverride = await dbContext.ReceiptInventoryOverrides.AsNoTracking()
            .AnyAsync(x => x.ReceiptId == receipt.Id, cancellationToken);
        return new ReceiptInventoryOverridePreviewViewModel
        {
            ReceiptId = receipt.Id,
            ConcurrencyVersion = receipt.ConcurrencyVersion,
            ReceiptBinCount = receipt.BinCount,
            CurrentInventory = state.Total,
            ConsumedBins = receipt.BinCount - state.Total,
            Balances = state.Balances.Select(ToViewModel).ToList(),
            BinsRunCount = counts.BinsRuns,
            ActualRunCount = counts.ActualRuns,
            TransferCount = counts.Transfers,
            HasPriorOverride = hasPriorOverride
        };
    }

    public async Task<ReceiptInventoryOverrideAuditViewModel?> GetAuditDetailAsync(Guid overrideId, CancellationToken cancellationToken) =>
        await dbContext.ReceiptInventoryOverrides.AsNoTracking()
            .Where(x => x.Id == overrideId)
            .Select(x => new ReceiptInventoryOverrideAuditViewModel
            {
                Id = x.Id,
                ReceiptId = x.ReceiptId,
                ReceiptNumber = x.Receipt.CompuTechReceiptId,
                ReceiptIsVoided = x.Receipt.IsDeleted,
                Action = x.ActionType,
                Administrator = x.AdministratorUser.DisplayName,
                CreatedAt = x.CreatedAt,
                Reason = x.Reason,
                OldReceiptBins = x.OldReceiptBinCount,
                NewReceiptBins = x.NewReceiptBinCount,
                InventoryDelta = x.InventoryDelta,
                CurrentInventoryBefore = x.CurrentInventoryBefore,
                CurrentInventoryAfter = x.CurrentInventoryAfter,
                NegativeInventoryAcknowledged = x.NegativeInventoryAcknowledged,
                BeforeSnapshotJson = x.BeforeReceiptSnapshotJson,
                AfterSnapshotJson = x.AfterReceiptSnapshotJson,
                Adjustments = x.InventoryAdjustments.OrderBy(y => y.Id).Select(y => new ReceiptInventoryOverrideAdjustmentViewModel(
                    y.Id,
                    y.Warehouse.Code,
                    y.Room.CropQcRoomName ?? y.Room.DisplayName ?? y.Room.Code,
                    y.CropYear,
                    y.LotNumber,
                    y.FruitProfile != null ? y.FruitProfile.VarietyCode : y.VarietyCode ?? "",
                    y.OldBinCount ?? 0,
                    y.ChangeAmount,
                    y.NewBinCount)).ToList()
            })
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<VoidedReceiptAdminViewModel>> GetVoidedReceiptsAsync(CancellationToken cancellationToken) =>
        await dbContext.ReceiptInventoryOverrides.AsNoTracking()
            .Where(x => x.ActionType == ReceiptInventoryOverrideActionTypes.VoidReceipt && x.Receipt.IsDeleted)
            .OrderByDescending(x => x.ReceiptId)
            .Select(x => new VoidedReceiptAdminViewModel(
                x.ReceiptId,
                x.Receipt.CompuTechReceiptId,
                x.Receipt.CropYear,
                x.Receipt.DeletedAt,
                x.Reason,
                x.Id,
                x.AdministratorUser.DisplayName,
                -x.InventoryDelta))
            .ToListAsync(cancellationToken);

    public async Task<ReceiptInventoryOverrideResult> ApplyEditAsync(
        AdminReceiptInventoryOverrideForm form,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var authorizationError = await AuthorizeAsync(principal, cancellationToken);
        if (authorizationError is not null) return authorizationError;
        var inputError = ValidateCommon(form.OperationKey, form.Reason, form.ConfirmInventoryChange);
        if (inputError is not null) return Failed(inputError);

        var duplicate = await FindDuplicateAsync(form.OperationKey, cancellationToken);
        if (duplicate is not null)
        {
            return IsSameEditRequest(duplicate, form)
                ? new(duplicate.Id, null, WasIdempotent: true)
                : Failed("The operation key was already used for a different receipt override.");
        }

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            var receipt = await ReceiptQuery(asTracking: true)
                .SingleOrDefaultAsync(x => x.Id == form.Id && !x.IsDeleted, cancellationToken);
            if (receipt is null) return await RollbackAsync(transaction, Failed("Receipt not found or already voided."), cancellationToken);
            if (receipt.ConcurrencyVersion != form.ExpectedConcurrencyVersion)
            {
                return await RollbackAsync(transaction, Conflict("The receipt changed after this override was previewed. Reload and review the current inventory before trying again."), cancellationToken);
            }

            var administrator = await ResolveAdministratorAsync(principal, cancellationToken);
            if (administrator is null) return await RollbackAsync(transaction, Failed("The active administrator account could not be resolved."), cancellationToken);

            var state = await GetInventoryStateAsync(receipt, cancellationToken);
            var lineageError = await ValidateLineageAsync(receipt.Id, state, cancellationToken);
            if (lineageError is not null) return await RollbackAsync(transaction, Failed(lineageError), cancellationToken);

            var growerLot = form.GrowerLotId is null
                ? null
                : await dbContext.GrowerLots.AsNoTracking().SingleOrDefaultAsync(x => x.Id == form.GrowerLotId && x.IsActive, cancellationToken);
            if (form.GrowerLotId is not null && growerLot is null)
            {
                return await RollbackAsync(transaction, Failed("The selected Grower Lot is unavailable."), cancellationToken);
            }
            var warehouse = await dbContext.Warehouses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == form.WarehouseId && x.IsActive, cancellationToken);
            var room = await dbContext.Rooms.AsNoTracking().SingleOrDefaultAsync(x => x.Id == form.RoomId && x.WarehouseId == form.WarehouseId && x.IsActive, cancellationToken);
            var fruit = await dbContext.FruitProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == form.FruitProfileId && x.IsActive, cancellationToken);
            if (warehouse is null || room is null || fruit is null)
            {
                return await RollbackAsync(transaction, Failed("The selected facility, room, or fruit profile is unavailable."), cancellationToken);
            }
            if (form.BinCount < 0 || form.CropYear <= 0 || !form.ConfirmCropYear
                || form.ReceivedAt == default || string.IsNullOrWhiteSpace(form.CompuTechReceiptId)
                || string.IsNullOrWhiteSpace(form.GrowerName))
            {
                return await RollbackAsync(transaction, Failed("The corrected receipt values are incomplete or invalid."), cancellationToken);
            }
            if (!string.Equals(receipt.ReceiptType, "Truck receipt", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(form.ReceiptType.Trim(), "Truck receipt", StringComparison.OrdinalIgnoreCase))
            {
                return await RollbackAsync(transaction, Failed("Receipt inventory overrides are limited to truck receipts. Review non-inventory receipt-type changes separately."), cancellationToken);
            }

            var newLot = growerLot?.LotNumber ?? form.GrowerNumber.Trim();
            var newGrower = growerLot?.Grower ?? form.GrowerName.Trim();
            if (string.IsNullOrWhiteSpace(newLot))
            {
                return await RollbackAsync(transaction, Failed("A grower number or lot identity is required."), cancellationToken);
            }

            var quantityChanged = form.BinCount != receipt.BinCount;
            var identityChanged = InventoryIdentityChanged(receipt, form, growerLot, newLot);
            if (!quantityChanged && !identityChanged)
            {
                return await RollbackAsync(transaction, Failed("No inventory-affecting receipt change was requested."), cancellationToken);
            }
            if (quantityChanged && identityChanged)
            {
                return await RollbackAsync(transaction, Failed("Quantity correction and inventory reclassification must be completed as separate administrator operations."), cancellationToken);
            }

            var beforeJson = ReceiptSnapshot(receipt);
            var now = businessTime.UtcNow;
            var operation = new ReceiptInventoryOverride
            {
                Id = Guid.NewGuid(),
                Receipt = receipt,
                ReceiptId = receipt.Id,
                ActionType = quantityChanged
                    ? ReceiptInventoryOverrideActionTypes.QuantityCorrection
                    : ReceiptInventoryOverrideActionTypes.InventoryReclassification,
                OldReceiptBinCount = receipt.BinCount,
                NewReceiptBinCount = form.BinCount,
                InventoryDelta = quantityChanged ? form.BinCount - receipt.BinCount : 0,
                CurrentInventoryBefore = state.Total,
                CurrentInventoryAfter = quantityChanged ? state.Total + form.BinCount - receipt.BinCount : state.Total,
                AdministratorUser = administrator,
                AdministratorUserId = administrator.Id,
                Reason = form.Reason.Trim(),
                OperationKey = form.OperationKey.Trim(),
                CreatedAt = now,
                NegativeInventoryAcknowledged = form.AcknowledgeNegativeInventory,
                BeforeReceiptSnapshotJson = beforeJson,
                AfterReceiptSnapshotJson = "{}",
                AffectedInventorySnapshotJson = JsonSerializer.Serialize(state.Balances.Select(ToViewModel), JsonOptions),
                IsComplete = false
            };
            dbContext.ReceiptInventoryOverrides.Add(operation);

            if (quantityChanged)
            {
                if (operation.CurrentInventoryAfter < 0 && !form.AcknowledgeNegativeInventory)
                {
                    return await RollbackAsync(transaction, Failed("This correction would create negative inventory. Select the separate negative-inventory acknowledgment before saving."), cancellationToken);
                }
                AddQuantityAdjustments(operation, receipt, state, administrator, now);
            }
            else
            {
                var counts = await GetOperationalCountsAsync(receipt.Id, cancellationToken);
                if (counts.BinsRuns > 0 || counts.ActualRuns > 0 || counts.Transfers > 0)
                {
                    return await RollbackAsync(transaction, Failed("The receipt identity conflicts with linked Bins Run, Actual Run, or transfer evidence and cannot be reclassified accurately."), cancellationToken);
                }
                if (state.Balances.Any(x => x.CurrentBins < 0))
                {
                    return await RollbackAsync(transaction, Failed("Negative or ambiguous receipt inventory must be reconciled before identity reclassification."), cancellationToken);
                }
                AddReclassificationAdjustments(operation, receipt, state, administrator, now, form, newGrower, newLot, fruit);
            }

            ApplyReceiptValues(receipt, form, growerLot, newGrower, newLot);
            receipt.ConcurrencyVersion++;
            receipt.UpdatedAt = now;
            operation.ExpectedAdjustmentCount = operation.InventoryAdjustments.Count;
            operation.AfterReceiptSnapshotJson = ReceiptSnapshot(receipt);
            operation.IsComplete = true;
            AddAudit(operation, administrator, now);

            await inventoryInvariantService.ValidateBeforeCommitAsync(cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new(operation.Id, null);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return Conflict("The receipt or inventory changed while the override was being saved. No changes were committed.");
        }
        catch (DbUpdateException ex) when (IsDuplicateOperation(ex))
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            var existing = await FindDuplicateAsync(form.OperationKey, cancellationToken);
            return existing is not null && IsSameEditRequest(existing, form)
                ? new(existing.Id, null, WasIdempotent: true)
                : Failed("The operation key was already used for a different receipt override.");
        }
        catch (Exception ex)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            logger.LogError(ex, "Receipt inventory override {OperationKey} failed.", form.OperationKey);
            return Failed("The receipt inventory override failed and no changes were committed.");
        }
    }

    public async Task<ReceiptInventoryOverrideResult> VoidAsync(
        DeleteReceiptForm form,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        var authorizationError = await AuthorizeAsync(principal, cancellationToken);
        if (authorizationError is not null) return authorizationError;
        var inputError = ValidateCommon(form.OperationToken, form.Reason, form.ConfirmDeletion && form.ConfirmInventoryChange);
        if (inputError is not null) return Failed(inputError);
        var duplicate = await FindDuplicateAsync(form.OperationToken, cancellationToken);
        if (duplicate is not null)
        {
            return duplicate.ReceiptId == form.Id
                && string.Equals(duplicate.ActionType, ReceiptInventoryOverrideActionTypes.VoidReceipt, StringComparison.Ordinal)
                && string.Equals(duplicate.Reason, form.Reason.Trim(), StringComparison.Ordinal)
                    ? new(duplicate.Id, null, WasIdempotent: true)
                    : Failed("The operation key was already used for a different receipt override.");
        }

        await using var transaction = await BeginTransactionAsync(cancellationToken);
        try
        {
            var receipt = await ReceiptQuery(asTracking: true)
                .SingleOrDefaultAsync(x => x.Id == form.Id && !x.IsDeleted, cancellationToken);
            if (receipt is null) return await RollbackAsync(transaction, Failed("Receipt not found or already voided."), cancellationToken);
            if (receipt.ConcurrencyVersion != form.ExpectedConcurrencyVersion)
            {
                return await RollbackAsync(transaction, Conflict("The receipt changed after the void preview. Reload and review it again."), cancellationToken);
            }
            if (!string.Equals(form.ConfirmationValue.Trim(), receipt.CompuTechReceiptId, StringComparison.Ordinal))
            {
                return await RollbackAsync(transaction, Failed("Type the exact receipt number to confirm the void."), cancellationToken);
            }

            var administrator = await ResolveAdministratorAsync(principal, cancellationToken);
            if (administrator is null) return await RollbackAsync(transaction, Failed("The active administrator account could not be resolved."), cancellationToken);
            var state = await GetInventoryStateAsync(receipt, cancellationToken);
            var lineageError = await ValidateLineageAsync(receipt.Id, state, cancellationToken);
            if (lineageError is not null) return await RollbackAsync(transaction, Failed(lineageError), cancellationToken);
            var remaining = state.Balances.Where(x => x.CurrentBins > 0).Sum(x => x.CurrentBins);
            var after = state.Total - remaining;
            if (after < 0 && !form.AcknowledgeNegativeInventory)
            {
                return await RollbackAsync(transaction, Failed("This receipt already has a negative attributable balance. Acknowledge the negative inventory before voiding it."), cancellationToken);
            }

            var now = businessTime.UtcNow;
            var operation = new ReceiptInventoryOverride
            {
                Id = Guid.NewGuid(),
                Receipt = receipt,
                ReceiptId = receipt.Id,
                ActionType = ReceiptInventoryOverrideActionTypes.VoidReceipt,
                OldReceiptBinCount = receipt.BinCount,
                NewReceiptBinCount = 0,
                InventoryDelta = -remaining,
                CurrentInventoryBefore = state.Total,
                CurrentInventoryAfter = after,
                AdministratorUser = administrator,
                AdministratorUserId = administrator.Id,
                Reason = form.Reason.Trim(),
                OperationKey = form.OperationToken.Trim(),
                CreatedAt = now,
                NegativeInventoryAcknowledged = form.AcknowledgeNegativeInventory,
                VoidConfirmationDetails = JsonSerializer.Serialize(new { TypedReceiptNumber = form.ConfirmationValue.Trim(), form.ConfirmDeletion, form.ConfirmInventoryChange }, JsonOptions),
                BeforeReceiptSnapshotJson = ReceiptSnapshot(receipt),
                AfterReceiptSnapshotJson = "{}",
                AffectedInventorySnapshotJson = JsonSerializer.Serialize(state.Balances.Select(ToViewModel), JsonOptions),
                IsComplete = false
            };
            dbContext.ReceiptInventoryOverrides.Add(operation);
            foreach (var balance in state.Balances.Where(x => x.CurrentBins > 0))
            {
                AddAdjustment(operation, balance, -balance.CurrentBins, 0, administrator, now, "VoidRemainingInventory");
            }

            receipt.IsDeleted = true;
            receipt.DeletedAt = now;
            receipt.DeletedByUserId = administrator.Id;
            receipt.DeleteReason = operation.Reason;
            receipt.UpdatedAt = now;
            receipt.ConcurrencyVersion++;
            operation.ExpectedAdjustmentCount = operation.InventoryAdjustments.Count;
            operation.AfterReceiptSnapshotJson = ReceiptSnapshot(receipt);
            operation.IsComplete = true;
            AddAudit(operation, administrator, now);
            dbContext.ReceiptDeletionAudits.Add(new ReceiptDeletionAudit
            {
                Id = Guid.NewGuid(),
                OperationId = operation.Id,
                DeletedReceiptId = receipt.Id,
                ReceiptNumber = receipt.CompuTechReceiptId,
                CropYear = receipt.CropYear,
                IdentifyingFieldsJson = operation.BeforeReceiptSnapshotJson,
                DependencyCountsJson = operation.AffectedInventorySnapshotJson,
                DeletedByEmail = administrator.Email,
                DeletedAt = now,
                Reason = operation.Reason,
                Result = "AdminVoided"
            });

            await inventoryInvariantService.ValidateBeforeCommitAsync(cancellationToken);
            await dbContext.SaveChangesAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new(operation.Id, null);
        }
        catch (DbUpdateConcurrencyException)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            return Conflict("The receipt or inventory changed while the void was being saved. No changes were committed.");
        }
        catch (Exception ex)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            logger.LogError(ex, "Receipt void {OperationKey} failed.", form.OperationToken);
            return Failed("The receipt void failed and no changes were committed.");
        }
    }

    private IQueryable<Receipt> ReceiptQuery(bool asTracking)
    {
        var query = dbContext.Receipts
            .Include(x => x.Warehouse)
            .Include(x => x.Room)
            .Include(x => x.FruitProfile);
        return asTracking ? query : query.AsNoTracking();
    }

    private async Task<InventoryState> GetInventoryStateAsync(Receipt receipt, CancellationToken cancellationToken)
    {
        var rows = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => x.ReceiptId == receipt.Id)
            .Include(x => x.Warehouse)
            .Include(x => x.Room)
            .Include(x => x.FruitProfile)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return new InventoryState([
                new InventoryBalance(
                    receipt.WarehouseId, receipt.Warehouse.Code, receipt.RoomId,
                    receipt.Room.CropQcRoomName ?? receipt.Room.DisplayName ?? receipt.Room.Code,
                    receipt.CropYear, receipt.GrowerLotId, receipt.FruitProfileId, receipt.GrowerName,
                    receipt.GrowerNumber ?? receipt.LotCode, receipt.FruitProfile.VarietyCode,
                    receipt.FruitProfile.ProductionType, receipt.BinCount)
            ]);
        }

        var balances = rows.GroupBy(x => new InventoryIdentity(
                x.WarehouseId,
                x.Warehouse.Code,
                x.RoomId,
                x.Room.CropQcRoomName ?? x.Room.DisplayName ?? x.Room.Code,
                x.CropYear ?? receipt.CropYear,
                x.GrowerLotId,
                x.FruitProfileId ?? receipt.FruitProfileId,
                x.GrowerName,
                x.LotNumber,
                x.FruitProfile?.VarietyCode ?? x.VarietyCode ?? receipt.FruitProfile.VarietyCode,
                x.InventoryStatus ?? x.FruitProfile?.ProductionType ?? receipt.FruitProfile.ProductionType))
            .Select(x => new InventoryBalance(
                x.Key.WarehouseId, x.Key.Warehouse, x.Key.RoomId, x.Key.Room, x.Key.CropYear,
                x.Key.GrowerLotId, x.Key.FruitProfileId, x.Key.Grower, x.Key.Lot, x.Key.Variety,
                x.Key.InventoryStatus, x.Sum(y => y.ChangeAmount)))
            .Where(x => x.CurrentBins != 0)
            .OrderBy(x => x.WarehouseId).ThenBy(x => x.RoomId).ThenBy(x => x.Lot)
            .ToList();
        return new InventoryState(balances);
    }

    private async Task<string?> ValidateLineageAsync(long receiptId, InventoryState state, CancellationToken cancellationToken)
    {
        if (state.Balances.Any(x => string.IsNullOrWhiteSpace(x.Lot) || x.FruitProfileId is null))
        {
            return "The current receipt inventory lineage has an incomplete lot or fruit identity and must be reviewed before an administrator override.";
        }
        var transferIds = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => x.ReceiptId == receiptId && x.RoomTransferId != null)
            .Select(x => x.RoomTransferId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        foreach (var transferId in transferIds)
        {
            var pair = await dbContext.RoomInventoryAdjustments.AsNoTracking()
                .Where(x => x.RoomTransferId == transferId)
                .Select(x => new { x.ReceiptId, x.ChangeAmount, x.AdjustmentType })
                .ToListAsync(cancellationToken);
            if (pair.Count != 2 || pair.Sum(x => x.ChangeAmount) != 0
                || pair.Any(x => x.ReceiptId != receiptId)
                || pair.Count(x => x.AdjustmentType == "TransferOut") != 1
                || pair.Count(x => x.AdjustmentType == "TransferIn") != 1)
            {
                return $"Transfer {transferId} does not provide exact receipt inventory lineage. No changes were made.";
            }
        }
        return null;
    }

    private void AddQuantityAdjustments(
        ReceiptInventoryOverride operation,
        Receipt receipt,
        InventoryState state,
        User administrator,
        DateTimeOffset now)
    {
        var delta = operation.InventoryDelta;
        if (delta > 0)
        {
            var target = BalanceFromReceipt(receipt, state.Balances.FirstOrDefault(x => SameReceiptIdentity(x, receipt))?.CurrentBins ?? 0);
            AddAdjustment(operation, target, delta, target.CurrentBins + delta, administrator, now, "QuantityCorrection");
            return;
        }

        var remaining = -delta;
        foreach (var balance in state.Balances.Where(x => x.CurrentBins > 0).OrderByDescending(x => x.CurrentBins))
        {
            if (remaining == 0) break;
            var reduction = Math.Min(remaining, balance.CurrentBins);
            AddAdjustment(operation, balance, -reduction, balance.CurrentBins - reduction, administrator, now, "QuantityCorrection");
            remaining -= reduction;
        }
        if (remaining > 0)
        {
            var target = BalanceFromReceipt(receipt, 0);
            AddAdjustment(operation, target, -remaining, target.CurrentBins - remaining, administrator, now, "NegativeQuantityCorrection");
        }
    }

    private void AddReclassificationAdjustments(
        ReceiptInventoryOverride operation,
        Receipt receipt,
        InventoryState state,
        User administrator,
        DateTimeOffset now,
        AdminReceiptInventoryOverrideForm form,
        string grower,
        string lot,
        FruitProfile fruit)
    {
        var targetCurrent = 0;
        foreach (var old in state.Balances.Where(x => x.CurrentBins > 0))
        {
            AddAdjustment(operation, old, -old.CurrentBins, 0, administrator, now, "ReclassifyOldIdentity");
            var target = new InventoryBalance(
                form.WarehouseId, "", form.RoomId, "", form.CropYear, form.GrowerLotId,
                form.FruitProfileId, grower, lot, fruit.VarietyCode, fruit.ProductionType, targetCurrent);
            targetCurrent += old.CurrentBins;
            AddAdjustment(operation, target, old.CurrentBins, targetCurrent, administrator, now, "ReclassifyNewIdentity");
        }
    }

    private void AddAdjustment(
        ReceiptInventoryOverride operation,
        InventoryBalance balance,
        int change,
        int newBalance,
        User administrator,
        DateTimeOffset now,
        string side)
    {
        var sequence = operation.InventoryAdjustments.Count + 1;
        var adjustment = new RoomInventoryAdjustment
        {
            CropYear = balance.CropYear,
            ReceiptId = operation.ReceiptId,
            Receipt = operation.Receipt,
            WarehouseId = balance.WarehouseId,
            RoomId = balance.RoomId,
            GrowerLotId = balance.GrowerLotId,
            FruitProfileId = balance.FruitProfileId,
            GrowerName = balance.Grower,
            LotNumber = balance.Lot,
            VarietyCode = balance.Variety,
            InventoryStatus = balance.InventoryStatus,
            OldBinCount = balance.CurrentBins,
            ChangeAmount = change,
            NewBinCount = newBalance,
            AdjustmentType = AdjustmentType,
            Source = "Receipt Admin Override",
            Reason = operation.Reason,
            Notes = $"{side}; override {operation.Id:D}.",
            AdjustmentAt = now,
            CreatedByUserId = administrator.Id,
            CreatedByUser = administrator,
            CreatedAt = now,
            InventoryInvariantVersion = InventoryDeductionInvariantService.CurrentVersion,
            InventoryOperationKey = $"receipt-override:{operation.OperationKey}:{sequence.ToString(CultureInfo.InvariantCulture)}",
            ReceiptInventoryOverrideId = operation.Id,
            ReceiptInventoryOverride = operation
        };
        operation.InventoryAdjustments.Add(adjustment);
        dbContext.RoomInventoryAdjustments.Add(adjustment);
    }

    private async Task<OperationalCounts> GetOperationalCountsAsync(long receiptId, CancellationToken cancellationToken)
    {
        var binsRuns = await dbContext.BinsRunEntries.AsNoTracking().CountAsync(x => x.ReceiptId == receiptId, cancellationToken);
        var actualRuns = await dbContext.BinsRunEntries.AsNoTracking()
            .Where(x => x.ReceiptId == receiptId && x.ActualRunId != null)
            .Select(x => x.ActualRunId)
            .Distinct()
            .CountAsync(cancellationToken);
        var transfers = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => x.ReceiptId == receiptId && x.RoomTransferId != null)
            .Select(x => x.RoomTransferId)
            .Distinct()
            .CountAsync(cancellationToken);
        return new OperationalCounts(binsRuns, actualRuns, transfers);
    }

    private async Task<ReceiptInventoryOverrideResult?> AuthorizeAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        return await userAccessService.HasAccessAsync(principal, ApplicationAreas.Receipts, PageAccessLevel.Admin, cancellationToken)
            ? null
            : Failed("Receipts Admin permission is required for an inventory override.");
    }

    private async Task<User?> ResolveAdministratorAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var email = principal.FindFirstValue(ClaimTypes.Email)?.Trim();
        return string.IsNullOrWhiteSpace(email)
            ? null
            : await dbContext.Users.SingleOrDefaultAsync(x => x.Email == email && x.IsActive, cancellationToken);
    }

    private async Task<ReceiptInventoryOverride?> FindDuplicateAsync(string operationKey, CancellationToken cancellationToken)
    {
        var normalized = operationKey.Trim();
        return await dbContext.ReceiptInventoryOverrides.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OperationKey == normalized, cancellationToken);
    }

    private static string? ValidateCommon(string operationKey, string reason, bool confirmed)
    {
        if (!Guid.TryParse(operationKey, out _)) return "The operation key is invalid. Reload the page before retrying.";
        if (string.IsNullOrWhiteSpace(reason)) return "An administrator override reason is required.";
        if (!confirmed) return "Explicit confirmation that inventory will change is required.";
        return null;
    }

    private static bool InventoryIdentityChanged(Receipt receipt, AdminReceiptInventoryOverrideForm form, GrowerLot? growerLot, string lot) =>
        receipt.WarehouseId != form.WarehouseId
        || receipt.RoomId != form.RoomId
        || receipt.CropYear != form.CropYear
        || receipt.GrowerLotId != growerLot?.Id
        || receipt.FruitProfileId != form.FruitProfileId
        || !Same(receipt.GrowerNumber ?? receipt.LotCode, lot);

    private static void ApplyReceiptValues(Receipt receipt, UpdateReceiptForm form, GrowerLot? growerLot, string grower, string lot)
    {
        receipt.CropYear = form.CropYear;
        receipt.ReceivedAt = form.ReceivedAt;
        receipt.CompuTechReceiptId = form.CompuTechReceiptId.Trim();
        receipt.ReceiptType = form.ReceiptType.Trim();
        receipt.WarehouseId = form.WarehouseId;
        receipt.RoomId = form.RoomId;
        receipt.FruitProfileId = form.FruitProfileId;
        receipt.GrowerLotId = growerLot?.Id;
        receipt.GrowerNumber = lot;
        receipt.GrowerName = grower;
        receipt.LotCode = lot;
        receipt.BinCount = form.BinCount;
    }

    private void AddAudit(ReceiptInventoryOverride operation, User administrator, DateTimeOffset now)
    {
        dbContext.AuditLogs.Add(new AuditLog
        {
            Action = operation.ActionType,
            EntityName = nameof(ReceiptInventoryOverride),
            EntityKey = operation.Id.ToString("D", CultureInfo.InvariantCulture),
            UserId = administrator.Id,
            BeforeValuesJson = operation.BeforeReceiptSnapshotJson,
            AfterValuesJson = JsonSerializer.Serialize(new
            {
                operation.AfterReceiptSnapshotJson,
                operation.InventoryDelta,
                operation.CurrentInventoryBefore,
                operation.CurrentInventoryAfter,
                operation.NegativeInventoryAcknowledged,
                operation.Reason,
                LedgerAdjustments = operation.InventoryAdjustments.Select(x => new { x.WarehouseId, x.RoomId, x.ChangeAmount, x.NewBinCount })
            }, JsonOptions),
            SourceApplication = "CropQc.Web",
            CreatedAt = now
        });
    }

    private static string ReceiptSnapshot(Receipt receipt) => JsonSerializer.Serialize(new
    {
        receipt.Id,
        receipt.CompuTechReceiptId,
        receipt.CropYear,
        receipt.ReceivedAt,
        receipt.ReceiptType,
        receipt.WarehouseId,
        receipt.RoomId,
        receipt.FruitProfileId,
        receipt.GrowerLotId,
        receipt.GrowerNumber,
        receipt.GrowerName,
        receipt.LotCode,
        receipt.BinCount,
        receipt.IsDeleted,
        receipt.ConcurrencyVersion
    }, JsonOptions);

    private static InventoryBalance BalanceFromReceipt(Receipt receipt, int current) => new(
        receipt.WarehouseId, receipt.Warehouse.Code, receipt.RoomId,
        receipt.Room.CropQcRoomName ?? receipt.Room.DisplayName ?? receipt.Room.Code,
        receipt.CropYear, receipt.GrowerLotId, receipt.FruitProfileId, receipt.GrowerName,
        receipt.GrowerNumber ?? receipt.LotCode, receipt.FruitProfile.VarietyCode,
        receipt.FruitProfile.ProductionType, current);

    private static ReceiptInventoryBalanceViewModel ToViewModel(InventoryBalance x) => new(
        x.WarehouseId, x.Warehouse, x.RoomId, x.Room, x.CropYear, x.GrowerLotId, x.FruitProfileId,
        x.Grower, x.Lot, x.Variety, x.InventoryStatus, x.CurrentBins);

    private static bool SameReceiptIdentity(InventoryBalance balance, Receipt receipt) =>
        balance.WarehouseId == receipt.WarehouseId && balance.RoomId == receipt.RoomId
        && balance.CropYear == receipt.CropYear && balance.FruitProfileId == receipt.FruitProfileId
        && Same(balance.Lot, receipt.GrowerNumber ?? receipt.LotCode);

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private async Task<IDbContextTransaction?> BeginTransactionAsync(CancellationToken cancellationToken) =>
        (dbContext.Database.ProviderName ?? "").Contains("InMemory", StringComparison.OrdinalIgnoreCase)
            ? null
            : await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);

    private async Task<ReceiptInventoryOverrideResult> RollbackAsync(
        IDbContextTransaction? transaction,
        ReceiptInventoryOverrideResult result,
        CancellationToken cancellationToken)
    {
        if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
        dbContext.ChangeTracker.Clear();
        return result;
    }

    private static bool IsDuplicateOperation(DbUpdateException exception) =>
        exception.InnerException?.Message.Contains("OperationKey", StringComparison.OrdinalIgnoreCase) == true
        || exception.Message.Contains("OperationKey", StringComparison.OrdinalIgnoreCase);

    private static bool IsSameEditRequest(ReceiptInventoryOverride existing, AdminReceiptInventoryOverrideForm form)
    {
        if (existing.ReceiptId != form.Id
            || existing.NewReceiptBinCount != form.BinCount
            || !string.Equals(existing.Reason, form.Reason.Trim(), StringComparison.Ordinal))
        {
            return false;
        }
        try
        {
            using var snapshot = JsonDocument.Parse(existing.AfterReceiptSnapshotJson);
            var root = snapshot.RootElement;
            return root.GetProperty("warehouseId").GetInt32() == form.WarehouseId
                && root.GetProperty("roomId").GetInt32() == form.RoomId
                && root.GetProperty("cropYear").GetInt32() == form.CropYear
                && root.GetProperty("fruitProfileId").GetInt32() == form.FruitProfileId
                && string.Equals(root.GetProperty("growerNumber").GetString(), form.GrowerNumber.Trim(), StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static ReceiptInventoryOverrideResult Failed(string error) => new(null, error);
    private static ReceiptInventoryOverrideResult Conflict(string error) => new(null, error, IsConflict: true);

    private sealed record InventoryIdentity(
        int WarehouseId, string Warehouse, int RoomId, string Room, int? CropYear,
        int? GrowerLotId, int? FruitProfileId, string Grower, string Lot, string Variety, string InventoryStatus);
    private sealed record InventoryBalance(
        int WarehouseId, string Warehouse, int RoomId, string Room, int? CropYear,
        int? GrowerLotId, int? FruitProfileId, string Grower, string Lot, string Variety,
        string InventoryStatus, int CurrentBins);
    private sealed record InventoryState(IReadOnlyList<InventoryBalance> Balances)
    {
        public int Total => Balances.Sum(x => x.CurrentBins);
    }
    private sealed record OperationalCounts(int BinsRuns, int ActualRuns, int Transfers);
}
