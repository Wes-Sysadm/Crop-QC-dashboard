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
    IRoomInventoryLedgerQueryService ledgerQuery,
    IInventoryIdentityService identityService,
    IRoomTreatmentService roomTreatmentService,
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
        var inventoryStateToken = await GetInventoryIdentityStateTokenAsync(receipt, cancellationToken);
        var trueUpState = await GetPositiveTrueUpStateAsync(receipt, cancellationToken);
        var counts = await GetOperationalCountsAsync(receipt, cancellationToken);
        var hasPriorOverride = await dbContext.ReceiptInventoryOverrides.AsNoTracking()
            .AnyAsync(x => x.ReceiptId == receipt.Id, cancellationToken);
        return new ReceiptInventoryOverridePreviewViewModel
        {
            ReceiptId = receipt.Id,
            ConcurrencyVersion = receipt.ConcurrencyVersion,
            InventoryStateToken = inventoryStateToken,
            PositiveTrueUpStateToken = trueUpState.StateToken,
            ReceiptBinCount = receipt.BinCount,
            CurrentInventory = state.Total,
            ConsumedBins = receipt.BinCount - state.Total,
            Balances = state.Balances.Select(ToViewModel).ToList(),
            BinsRunCount = counts.BinsRuns,
            ActualRunCount = counts.ActualRuns,
            TransferCount = counts.Transfers,
            HasPriorOverride = hasPriorOverride,
            CurrentCanonicalInventory = trueUpState.TotalBins,
            TrueUpPositions = trueUpState.Positions.Select(ToViewModel).ToList()
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
            var locationChanged = receipt.WarehouseId != form.WarehouseId || receipt.RoomId != form.RoomId;
            var identityChanged = InventoryIdentityChanged(receipt, form, growerLot, newLot);
            if (!quantityChanged && !identityChanged && !locationChanged)
            {
                return await RollbackAsync(transaction, Failed("No inventory-affecting receipt change was requested."), cancellationToken);
            }
            if (quantityChanged && (identityChanged || locationChanged))
            {
                return await RollbackAsync(transaction, Failed("Quantity correction and identity/location correction must be completed as separate administrator operations."), cancellationToken);
            }
            if (identityChanged && locationChanged)
                return await RollbackAsync(transaction, Failed("Inventory identity and receiving-room corrections must be completed as separate administrator operations."), cancellationToken);

            IReadOnlyList<RoomInventoryLedgerSnapshot> sourceIdentitySnapshots = [];
            RoomInventoryLedgerSnapshot? locationSource = null;
            var sourceCustodyBins = 0;
            object[] sourceCustody = [];
            InventoryIdentityCorrection? identityCorrection = null;
            IReadOnlyList<TrueUpAllocation> positiveAllocations = [];
            if (identityChanged)
            {
                if (receipt.GrowerLotId is null || form.GrowerLotId is null)
                    return await RollbackAsync(transaction, Failed("Inventory identity reclassification requires explicit source and target Grower Lots."), cancellationToken);
                var sourceKey = new InventoryIdentityKey(receipt.CropYear, receipt.GrowerLotId.Value, receipt.FruitProfileId);
                var targetKey = new InventoryIdentityKey(form.CropYear, form.GrowerLotId.Value, form.FruitProfileId);
                var correctionError = await identityService.ValidateCorrectionAsync(sourceKey, targetKey, cancellationToken);
                if (correctionError is not null)
                    return await RollbackAsync(transaction, Failed(correctionError), cancellationToken);
                sourceIdentitySnapshots = (await ledgerQuery.GetSnapshotsAsync(null, null, cancellationToken))
                    .Where(x => x.CropYear == sourceKey.CropYear
                        && x.GrowerLotId == sourceKey.GrowerLotId
                        && x.FruitProfileId == sourceKey.FruitProfileId
                        && x.CurrentBins != 0)
                    .OrderBy(x => x.WarehouseId).ThenBy(x => x.RoomId)
                    .ToList();
                var interCrewCustody = await dbContext.InterCrewTransfers.AsNoTracking()
                    .Where(x => x.Status == InterCrewTransferStatuses.InTransit
                        && x.CropYear == sourceKey.CropYear
                        && x.GrowerLotId == sourceKey.GrowerLotId
                        && x.FruitProfileId == sourceKey.FruitProfileId)
                    .Select(x => new { Kind = "InterCrew", x.Id, Bins = x.BinsLoaded })
                    .ToListAsync(cancellationToken);
                var outsideCustody = await dbContext.OutsideWarehouseTransfers.AsNoTracking()
                    .Where(x => !x.IsReversed
                        && x.CropYear == sourceKey.CropYear
                        && x.GrowerLotId == sourceKey.GrowerLotId
                        && x.FruitProfileId == sourceKey.FruitProfileId)
                    .Select(x => new { Kind = "OutsideWarehouse", x.Id, Bins = x.BinCount })
                    .ToListAsync(cancellationToken);
                sourceCustody = interCrewCustody.Cast<object>().Concat(outsideCustody).ToArray();
                sourceCustodyBins = interCrewCustody.Sum(x => x.Bins) + outsideCustody.Sum(x => x.Bins);
                var currentStateToken = CreateInventoryStateToken(sourceIdentitySnapshots,
                    interCrewCustody.Select(x => $"InterCrew:{x.Id}:{x.Bins}")
                        .Concat(outsideCustody.Select(x => $"OutsideWarehouse:{x.Id}:{x.Bins}")));
                if (string.IsNullOrWhiteSpace(form.ExpectedInventoryStateToken)
                    || !string.Equals(form.ExpectedInventoryStateToken, currentStateToken, StringComparison.Ordinal))
                    return await RollbackAsync(transaction, Conflict("Inventory moved or changed after this override was reviewed. Refresh the Receipt and review the correction again."), cancellationToken);
                if (sourceIdentitySnapshots.Count == 0 && sourceCustodyBins == 0)
                    return await RollbackAsync(transaction, Failed("No current inventory remains under the obsolete identity."), cancellationToken);
                if (sourceIdentitySnapshots.Any(x => x.CurrentBins < 0))
                    return await RollbackAsync(transaction, Failed("Negative or ambiguous source identity inventory must be reconciled before reclassification."), cancellationToken);
                state = new InventoryState(sourceIdentitySnapshots.Select(ToBalance).ToList(), HasExactReceiptProvenance: true);
            }
            else if (quantityChanged)
            {
                if (form.BinCount > receipt.BinCount)
                {
                    var trueUpState = await GetPositiveTrueUpStateAsync(receipt, cancellationToken);
                    if (string.IsNullOrWhiteSpace(form.ExpectedPositiveTrueUpStateToken)
                        || !string.Equals(form.ExpectedPositiveTrueUpStateToken, trueUpState.StateToken, StringComparison.Ordinal))
                        return await RollbackAsync(transaction, Conflict("Inventory custody or treatment changed after this true-up was reviewed. Refresh the Receipt and review the correction again."), cancellationToken);
                    var allocationResult = ResolvePositiveAllocations(form, trueUpState, form.BinCount - receipt.BinCount);
                    if (allocationResult.Error is not null)
                        return await RollbackAsync(transaction, Failed(allocationResult.Error), cancellationToken);
                    positiveAllocations = allocationResult.Allocations;
                    sourceCustodyBins = trueUpState.CustodyBins;
                    state = new InventoryState(trueUpState.RoomSnapshots.Select(ToBalance).ToList(), HasExactReceiptProvenance: true);
                }
                else
                {
                    var lineageError = await ValidateLineageAsync(receipt, state, cancellationToken);
                    if (lineageError is not null) return await RollbackAsync(transaction, Failed(lineageError), cancellationToken);
                }
            }
            else if (locationChanged)
            {
                var counts = await GetOperationalCountsAsync(receipt, cancellationToken);
                if (counts.Transfers == 0 && counts.BinsRuns == 0 && counts.ActualRuns == 0)
                {
                    if (state.Balances.Count != 1 || state.Balances[0].CurrentBins <= 0
                        || state.Balances[0].WarehouseId != receipt.WarehouseId || state.Balances[0].RoomId != receipt.RoomId)
                        return await RollbackAsync(transaction, Failed("The Receipt's current bins cannot be attributed exactly to its original room. No location correction was made."), cancellationToken);
                    locationSource = (await ledgerQuery.GetSnapshotsAsync(receipt.WarehouseId, [receipt.RoomId], cancellationToken))
                        .SingleOrDefault(x => x.CropYear == receipt.CropYear && x.GrowerLotId == receipt.GrowerLotId
                            && x.FruitProfileId == receipt.FruitProfileId && x.CurrentBins >= state.Balances[0].CurrentBins);
                    if (locationSource is null)
                        return await RollbackAsync(transaction, Failed("The Receipt location does not reconcile with authoritative current inventory."), cancellationToken);
                    locationSource = locationSource with { CurrentBins = state.Balances[0].CurrentBins };
                }
                // Once fruit has moved, Warehouse/Room on Receipt is receiving provenance only.
                // Correct that metadata without teleporting current inventory.
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
                    : identityChanged
                        ? ReceiptInventoryOverrideActionTypes.InventoryReclassification
                        : ReceiptInventoryOverrideActionTypes.LocationCorrection,
                OldReceiptBinCount = receipt.BinCount,
                NewReceiptBinCount = form.BinCount,
                InventoryDelta = quantityChanged ? form.BinCount - receipt.BinCount : 0,
                CurrentInventoryBefore = state.Total + sourceCustodyBins,
                CurrentInventoryAfter = quantityChanged
                    ? state.Total + sourceCustodyBins + form.BinCount - receipt.BinCount
                    : state.Total + sourceCustodyBins,
                AdministratorUser = administrator,
                AdministratorUserId = administrator.Id,
                Reason = form.Reason.Trim(),
                OperationKey = form.OperationKey.Trim(),
                CreatedAt = now,
                NegativeInventoryAcknowledged = form.AcknowledgeNegativeInventory,
                BeforeReceiptSnapshotJson = beforeJson,
                AfterReceiptSnapshotJson = "{}",
                AffectedInventorySnapshotJson = JsonSerializer.Serialize(
                    positiveAllocations.Count > 0
                        ? positiveAllocations.Select(ToAuditSnapshot)
                        : state.Balances.Select(x => (object)ToViewModel(x)), JsonOptions),
                IsComplete = false
            };
            dbContext.ReceiptInventoryOverrides.Add(operation);

            if (quantityChanged)
            {
                if (operation.CurrentInventoryAfter < 0 && !form.AcknowledgeNegativeInventory)
                {
                    return await RollbackAsync(transaction, Failed("This correction would create negative inventory. Select the separate negative-inventory acknowledgment before saving."), cancellationToken);
                }
                if (operation.InventoryDelta > 0)
                {
                    var lineageError = await AddPositiveTrueUpAsync(
                        operation, receipt, positiveAllocations, administrator, now, cancellationToken);
                    if (lineageError is not null)
                        return await RollbackAsync(transaction, Failed(lineageError), cancellationToken);
                }
                else
                {
                    AddQuantityAdjustments(operation, receipt, state, administrator, now);
                }
            }
            else if (identityChanged)
            {
                var sourceKey = new InventoryIdentityKey(receipt.CropYear, receipt.GrowerLotId!.Value, receipt.FruitProfileId);
                var targetKey = new InventoryIdentityKey(form.CropYear, form.GrowerLotId!.Value, form.FruitProfileId);
                identityCorrection = new InventoryIdentityCorrection
                {
                    Id = Guid.NewGuid(),
                    OperationKey = operation.OperationKey,
                    SourceCropYear = sourceKey.CropYear,
                    SourceGrowerLotId = sourceKey.GrowerLotId,
                    SourceFruitProfileId = sourceKey.FruitProfileId,
                    TargetCropYear = targetKey.CropYear,
                    TargetGrowerLotId = targetKey.GrowerLotId,
                    TargetFruitProfileId = targetKey.FruitProfileId,
                    CorrectedReceipt = receipt,
                    CorrectedReceiptId = receipt.Id,
                    ReceiptInventoryOverride = operation,
                    ReceiptInventoryOverrideId = operation.Id,
                    Reason = operation.Reason,
                    CreatedByUser = administrator,
                    CreatedByUserId = administrator.Id,
                    CreatedAt = now,
                    SourceIdentitySnapshotJson = JsonSerializer.Serialize(new
                    {
                        Identity = sourceKey,
                        Custody = sourceCustody
                    }, JsonOptions),
                    TargetIdentitySnapshotJson = JsonSerializer.Serialize(targetKey, JsonOptions),
                    IsComplete = false,
                    IsActive = true
                };
                dbContext.InventoryIdentityCorrections.Add(identityCorrection);
                await AddReclassificationAdjustmentsAsync(
                    operation, identityCorrection, state, administrator, now, form,
                    growerLot!, newGrower, newLot, fruit, cancellationToken);
                foreach (var sourceSnapshot in sourceIdentitySnapshots)
                {
                    var targetSnapshot = ToTargetSnapshot(sourceSnapshot, form, growerLot!, fruit);
                    var lineage = await roomTreatmentService.ReclassifyIdentityAsync(
                        sourceSnapshot,
                        targetSnapshot,
                        identityCorrection,
                        now,
                        administrator.Id,
                        cancellationToken);
                    if (!lineage.Success)
                        return await RollbackAsync(transaction, Failed(lineage.Error ?? "Treatment identity reclassification failed."), cancellationToken);
                }
                identityCorrection.ExpectedAdjustmentCount = identityCorrection.InventoryAdjustments.Count;
                identityCorrection.ExpectedTreatmentMovementCount = identityCorrection.TreatmentLineageMovements.Count;
                identityCorrection.IsComplete = true;
            }
            else if (locationSource is not null)
            {
                var old = ToBalance(locationSource);
                AddAdjustment(operation, old, -old.CurrentBins, 0, administrator, now, "CorrectOriginalRoomOut");
                var targetCurrent = (await ledgerQuery.GetSnapshotsAsync(form.WarehouseId, [form.RoomId], cancellationToken))
                    .Where(x => x.CropYear == locationSource.CropYear && x.GrowerLotId == locationSource.GrowerLotId
                        && x.FruitProfileId == locationSource.FruitProfileId)
                    .Sum(x => x.CurrentBins);
                var target = old with
                {
                    WarehouseId = form.WarehouseId,
                    Warehouse = warehouse.Code,
                    RoomId = room.Id,
                    Room = room.CropQcRoomName ?? room.DisplayName ?? room.Code,
                    CurrentBins = targetCurrent
                };
                AddAdjustment(operation, target, old.CurrentBins, targetCurrent + old.CurrentBins,
                    administrator, now, "CorrectOriginalRoomIn");
                var targetSnapshot = locationSource with
                {
                    WarehouseId = form.WarehouseId,
                    Facility = warehouse.Code,
                    RoomId = room.Id,
                    Room = room.CropQcRoomName ?? room.DisplayName ?? room.Code
                };
                var lineage = await roomTreatmentService.CorrectReceiptLocationAsync(locationSource, targetSnapshot,
                    receipt.Id, operation.OperationKey, now, administrator.Id, cancellationToken);
                if (!lineage.Success)
                    return await RollbackAsync(transaction, Failed(lineage.Error ?? "Treatment provenance location correction failed."), cancellationToken);
            }

            var receivingWarehouseId = receipt.WarehouseId;
            var receivingRoomId = receipt.RoomId;
            ApplyReceiptValues(receipt, form, growerLot, newGrower, newLot);
            if (identityChanged)
            {
                // Receipt location is immutable receiving provenance; current physical location comes from the ledger.
                receipt.WarehouseId = receivingWarehouseId;
                receipt.RoomId = receivingRoomId;
            }
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
            var lineageError = await ValidateLineageAsync(receipt, state, cancellationToken);
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
        var provenance = await dbContext.TreatmentLineageSegments.AsNoTracking()
            .Where(x => x.ReceiptId == receipt.Id && x.CurrentBins != 0)
            .Include(x => x.Warehouse)
            .Include(x => x.Room)
            .Include(x => x.FruitProfile)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        if (provenance.Count > 0)
        {
            var provenanceBalances = provenance
                .GroupBy(x => new InventoryIdentity(
                    x.WarehouseId,
                    x.Warehouse.Code,
                    x.RoomId,
                    x.Room.CropQcRoomName ?? x.Room.DisplayName ?? x.Room.Code,
                    x.CropYear,
                    x.GrowerLotId,
                    x.FruitProfileId,
                    x.GrowerNameSnapshot,
                    x.LotNumberSnapshot,
                    x.FruitProfile?.VarietyCode ?? x.VarietyCodeSnapshot,
                    x.InventoryStatusSnapshot ?? x.ProductionTypeSnapshot))
                .Select(x => new InventoryBalance(
                    x.Key.WarehouseId, x.Key.Warehouse, x.Key.RoomId, x.Key.Room, x.Key.CropYear,
                    x.Key.GrowerLotId, x.Key.FruitProfileId, x.Key.Grower, x.Key.Lot, x.Key.Variety,
                    x.Key.InventoryStatus, x.Sum(y => y.CurrentBins)))
                .Where(x => x.CurrentBins != 0)
                .OrderBy(x => x.WarehouseId).ThenBy(x => x.RoomId).ThenBy(x => x.Lot)
                .ToList();
            return new InventoryState(provenanceBalances, HasExactReceiptProvenance: true);
        }

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
            ], HasExactReceiptProvenance: false);
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
        return new InventoryState(balances, HasExactReceiptProvenance: false);
    }

    private async Task<string?> ValidateLineageAsync(Receipt receipt, InventoryState state, CancellationToken cancellationToken)
    {
        if (state.Balances.Any(x => string.IsNullOrWhiteSpace(x.Lot) || x.FruitProfileId is null))
        {
            return "The current receipt inventory lineage has an incomplete lot or fruit identity and must be reviewed before an administrator override.";
        }
        var counts = await GetOperationalCountsAsync(receipt, cancellationToken);
        if ((counts.Transfers > 0 || counts.BinsRuns > 0 || counts.ActualRuns > 0)
            && !state.HasExactReceiptProvenance)
        {
            return "Current inventory lineage can no longer be allocated to this Receipt exactly after operational movement. Reconcile Receipt provenance before changing quantity or voiding the Receipt. No inventory change was made.";
        }
        return null;
    }

    private async Task<PositiveTrueUpState> GetPositiveTrueUpStateAsync(
        Receipt receipt,
        CancellationToken cancellationToken)
    {
        if (receipt.GrowerLotId is null)
            return new([], [], [], 0, CreateInventoryStateToken([]),
                "This Receipt has no canonical Grower Lot identity. Reconcile its identity before adding inventory.");

        var resolution = await identityService.ResolveAsync(
            new InventoryIdentityKey(receipt.CropYear, receipt.GrowerLotId.Value, receipt.FruitProfileId),
            cancellationToken);
        var snapshots = (await ledgerQuery.GetSnapshotsAsync(null, null, cancellationToken))
            .Where(x => x.CurrentBins > 0
                && x.CropYear == resolution.Canonical.CropYear
                && x.GrowerLotId == resolution.Canonical.GrowerLotId
                && x.FruitProfileId == resolution.Canonical.FruitProfileId)
            .OrderBy(x => x.WarehouseId).ThenBy(x => x.RoomId)
            .ToList();

        var positions = new List<TrueUpPosition>();
        var treatmentByIdentity = await roomTreatmentService.GetSelectionsAsync(snapshots, cancellationToken);
        foreach (var snapshot in snapshots)
        {
            var lookupKey = RoomTreatmentService.SelectionLookupKey(snapshot);
            var selections = treatmentByIdentity.GetValueOrDefault(lookupKey) ?? [];
            if (selections.Count == 0)
            {
                positions.Add(TrueUpPosition.UnavailableRoom(snapshot,
                    "Treatment lineage could not be determined for this current room position."));
                continue;
            }
            foreach (var selection in selections.Where(x => x.CurrentBins > 0))
            {
                var unavailable = selection.IsAvailable
                    ? null
                    : selection.UnavailableReason ?? "Treatment lineage requires review.";
                positions.Add(new TrueUpPosition(
                    CreateTrueUpTargetKey(snapshot, selection),
                    "Room",
                    snapshot.Facility,
                    snapshot.Room,
                    selection.CurrentBins,
                    selection.Label,
                    selection.TreatmentSignature,
                    selection.TreatmentState,
                    selection.SegmentId,
                    selection.ReceiptId,
                    unavailable is null,
                    unavailable,
                    snapshot));
            }
        }

        var interCrewRows = await dbContext.InterCrewTransfers.AsNoTracking()
            .Where(x => x.Status == InterCrewTransferStatuses.InTransit
                && x.CropYear != null && x.GrowerLotId != null && x.FruitProfileId != null)
            .Select(x => new
            {
                x.Id,
                x.BinsLoaded,
                x.DestinationCustodyGroup,
                CropYear = x.CropYear!.Value,
                GrowerLotId = x.GrowerLotId!.Value,
                FruitProfileId = x.FruitProfileId!.Value
            })
            .ToListAsync(cancellationToken);
        var interCrew = new List<(long Id, int BinsLoaded, string DestinationCustodyGroup)>();
        foreach (var item in interCrewRows)
        {
            var custodyIdentity = await identityService.ResolveAsync(
                new InventoryIdentityKey(item.CropYear, item.GrowerLotId, item.FruitProfileId), cancellationToken);
            if (custodyIdentity.Canonical == resolution.Canonical)
                interCrew.Add((item.Id, item.BinsLoaded, item.DestinationCustodyGroup));
        }
        foreach (var item in interCrew)
        {
            positions.Add(TrueUpPosition.UnavailableCustody(
                $"C:InterCrew:{item.Id}", "Inter-Crew transit",
                TransferCustodyGroups.Label(item.DestinationCustodyGroup), item.BinsLoaded,
                "Inventory in transit cannot be increased without rewriting the historical dispatch. Use a separately reviewed custody reconciliation."));
        }

        var outsideRows = await dbContext.OutsideWarehouseTransfers.AsNoTracking()
            .Where(x => !x.IsReversed
                && x.CropYear != null && x.GrowerLotId != null && x.FruitProfileId != null)
            .Select(x => new
            {
                x.Id,
                x.BinCount,
                x.OutsideWarehouseCodeSnapshot,
                x.OutsideWarehouseNameSnapshot,
                CropYear = x.CropYear!.Value,
                GrowerLotId = x.GrowerLotId!.Value,
                FruitProfileId = x.FruitProfileId!.Value
            })
            .ToListAsync(cancellationToken);
        var outside = new List<(long Id, int BinCount, string OutsideWarehouseCodeSnapshot, string OutsideWarehouseNameSnapshot)>();
        foreach (var item in outsideRows)
        {
            var custodyIdentity = await identityService.ResolveAsync(
                new InventoryIdentityKey(item.CropYear, item.GrowerLotId, item.FruitProfileId), cancellationToken);
            if (custodyIdentity.Canonical == resolution.Canonical)
                outside.Add((item.Id, item.BinCount, item.OutsideWarehouseCodeSnapshot, item.OutsideWarehouseNameSnapshot));
        }
        foreach (var item in outside)
        {
            positions.Add(TrueUpPosition.UnavailableCustody(
                $"C:OutsideWarehouse:{item.Id}", "Outside Warehouse",
                $"{item.OutsideWarehouseCodeSnapshot} - {item.OutsideWarehouseNameSnapshot}", item.BinCount,
                "Outside-warehouse custody cannot be increased without rewriting the historical transfer. Use a separately reviewed custody reconciliation."));
        }

        var custodyTokens = interCrew.Select(x => $"InterCrew:{x.Id}:{x.BinsLoaded}:{x.DestinationCustodyGroup}")
            .Concat(outside.Select(x => $"OutsideWarehouse:{x.Id}:{x.BinCount}"));
        var treatmentTokens = positions.Where(x => x.PositionType == "Room")
            .Select(x => $"Treatment:{x.TargetKey}:{x.CurrentBins}:{x.IsEligible}");
        var stateToken = CreateInventoryStateToken(snapshots, custodyTokens.Concat(treatmentTokens)
            .Concat(resolution.CorrectionChain.Select(x => $"IdentityCorrection:{x:D}")));
        var custodyBins = interCrew.Sum(x => x.BinsLoaded) + outside.Sum(x => x.BinCount);
        var error = snapshots.Count == 0 && custodyBins == 0
            ? "No current matching inventory or custody position exists. The additional inventory cannot be placed in the original receiving room automatically."
            : null;
        return new(snapshots, positions, resolution.CorrectionChain, custodyBins, stateToken, error);
    }

    private static PositiveAllocationResult ResolvePositiveAllocations(
        AdminReceiptInventoryOverrideForm form,
        PositiveTrueUpState state,
        int delta)
    {
        if (state.Error is not null) return new([], state.Error);
        var eligible = state.Positions.Where(x => x.IsEligible && x.Snapshot is not null).ToList();
        if (eligible.Count == 0)
        {
            var custody = state.Positions.Any(x => x.PositionType != "Room")
                ? " Matching inventory is currently in transfer custody, which this override cannot safely rewrite."
                : "";
            return new([], "No eligible current room position has exact treatment lineage." + custody);
        }
        if (eligible.Count == 1 && state.Positions.Count == 1)
            return new([new TrueUpAllocation(eligible[0], delta)], null);

        var requested = form.TrueUpAllocations.Where(x => x.Bins > 0).ToList();
        if (requested.Count == 0)
            return new([], "Where is the additional inventory now? Allocate the positive correction across the current positions before applying the override.");
        if (requested.GroupBy(x => x.TargetKey, StringComparer.Ordinal).Any(x => x.Count() != 1))
            return new([], "Each current inventory position may be selected only once.");
        if (requested.Sum(x => x.Bins) != delta)
            return new([], $"Current-position allocations must total exactly {delta} bin(s).");

        var byKey = eligible.ToDictionary(x => x.TargetKey, StringComparer.Ordinal);
        var allocations = new List<TrueUpAllocation>();
        foreach (var item in requested)
        {
            if (!byKey.TryGetValue(item.TargetKey, out var position))
                return new([], "A selected current inventory position changed or is no longer eligible. Refresh and review the true-up again.");
            allocations.Add(new(position, item.Bins));
        }
        return new(allocations, null);
    }

    private async Task<string?> AddPositiveTrueUpAsync(
        ReceiptInventoryOverride operation,
        Receipt receipt,
        IReadOnlyList<TrueUpAllocation> allocations,
        User administrator,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var roomBalances = allocations
            .GroupBy(x => (x.Position.Snapshot!.WarehouseId, x.Position.Snapshot.RoomId))
            .ToDictionary(x => x.Key, x => x.First().Position.Snapshot!.CurrentBins);
        var sequence = 0;
        foreach (var allocation in allocations)
        {
            sequence++;
            var snapshot = allocation.Position.Snapshot!;
            var roomKey = (snapshot.WarehouseId, snapshot.RoomId);
            var oldBins = roomBalances[roomKey];
            var lineageSnapshot = snapshot with { CurrentBins = oldBins };
            var balance = ToBalance(lineageSnapshot);
            AddAdjustment(operation, balance, allocation.Bins, oldBins + allocation.Bins,
                administrator, now, "PositiveQuantityTrueUp");
            roomBalances[roomKey] = oldBins + allocation.Bins;

            var lineage = await roomTreatmentService.AddReceiptTrueUpAsync(
                lineageSnapshot,
                allocation.Position.TreatmentSegmentId,
                allocation.Position.TreatmentSignature,
                receipt.Id,
                allocation.Bins,
                $"receipt-override:{operation.OperationKey}:treatment:{sequence.ToString(CultureInfo.InvariantCulture)}",
                now,
                administrator.Id,
                cancellationToken);
            if (!lineage.Success)
                return lineage.Error ?? "Treatment lineage could not be assigned to the positive Receipt true-up.";
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
            throw new InvalidOperationException("Positive quantity corrections must use current-custody allocation.");
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

    private async Task AddReclassificationAdjustmentsAsync(
        ReceiptInventoryOverride operation,
        InventoryIdentityCorrection correction,
        InventoryState state,
        User administrator,
        DateTimeOffset now,
        AdminReceiptInventoryOverrideForm form,
        GrowerLot targetGrowerLot,
        string grower,
        string lot,
        FruitProfile fruit,
        CancellationToken cancellationToken)
    {
        var targetSnapshots = (await ledgerQuery.GetSnapshotsAsync(null, null, cancellationToken))
            .Where(x => x.CropYear == form.CropYear
                && x.GrowerLotId == form.GrowerLotId
                && x.FruitProfileId == form.FruitProfileId)
            .ToDictionary(x => (x.WarehouseId, x.RoomId), x => x.CurrentBins);
        foreach (var old in state.Balances.Where(x => x.CurrentBins > 0))
        {
            AddAdjustment(operation, old, -old.CurrentBins, 0, administrator, now, "ReclassifyOldIdentity", correction);
            var existingTarget = targetSnapshots.GetValueOrDefault((old.WarehouseId, old.RoomId));
            var target = new InventoryBalance(
                old.WarehouseId, old.Warehouse, old.RoomId, old.Room, form.CropYear, targetGrowerLot.Id,
                form.FruitProfileId, grower, lot, fruit.VarietyCode, fruit.ProductionType, existingTarget);
            AddAdjustment(operation, target, old.CurrentBins, existingTarget + old.CurrentBins,
                administrator, now, "ReclassifyNewIdentity", correction);
            targetSnapshots[(old.WarehouseId, old.RoomId)] = existingTarget + old.CurrentBins;
        }
    }

    private void AddAdjustment(
        ReceiptInventoryOverride operation,
        InventoryBalance balance,
        int change,
        int newBalance,
        User administrator,
        DateTimeOffset now,
        string side,
        InventoryIdentityCorrection? correction = null)
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
            ReceiptInventoryOverride = operation,
            InventoryIdentityCorrectionId = correction?.Id,
            InventoryIdentityCorrection = correction
        };
        operation.InventoryAdjustments.Add(adjustment);
        correction?.InventoryAdjustments.Add(adjustment);
        dbContext.RoomInventoryAdjustments.Add(adjustment);
    }

    private async Task<OperationalCounts> GetOperationalCountsAsync(Receipt receipt, CancellationToken cancellationToken)
    {
        var binsRuns = await dbContext.BinsRunEntries.AsNoTracking().CountAsync(x =>
            x.ReceiptId == receipt.Id
            || (x.CropYear == receipt.CropYear
                && x.GrowerLotId == receipt.GrowerLotId
                && x.FruitProfileId == receipt.FruitProfileId), cancellationToken);
        var actualRuns = await dbContext.BinsRunEntries.AsNoTracking()
            .Where(x => x.ActualRunId != null
                && (x.ReceiptId == receipt.Id
                    || (x.CropYear == receipt.CropYear
                        && x.GrowerLotId == receipt.GrowerLotId
                        && x.FruitProfileId == receipt.FruitProfileId)))
            .Select(x => x.ActualRunId)
            .Distinct()
            .CountAsync(cancellationToken);
        var transfers = await dbContext.RoomTransfers.AsNoTracking()
            .Where(x => x.CropYear == receipt.CropYear
                && x.GrowerLotId == receipt.GrowerLotId
                && x.FruitProfileId == receipt.FruitProfileId)
            .Select(x => x.Id)
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
        receipt.CropYear != form.CropYear
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
                operation.AffectedInventorySnapshotJson,
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

    private static InventoryBalance ToBalance(RoomInventoryLedgerSnapshot snapshot) => new(
        snapshot.WarehouseId, snapshot.Facility, snapshot.RoomId, snapshot.Room,
        snapshot.CropYear, snapshot.GrowerLotId, snapshot.FruitProfileId, snapshot.Grower,
        snapshot.Lot, snapshot.Variety, snapshot.InventoryStatus, snapshot.CurrentBins);

    private static RoomInventoryLedgerSnapshot ToTargetSnapshot(
        RoomInventoryLedgerSnapshot source,
        AdminReceiptInventoryOverrideForm form,
        GrowerLot growerLot,
        FruitProfile fruit) => source with
        {
            CropYear = form.CropYear,
            GrowerLotId = growerLot.Id,
            FruitProfileId = fruit.Id,
            Grower = growerLot.Grower,
            GrowerNumber = growerLot.LotNumber,
            Lot = growerLot.LotNumber,
            PoolStart = growerLot.PoolStart,
            StoredVarietyCode = fruit.VarietyCode,
            Variety = fruit.VarietyCode,
            VarietyName = fruit.Name,
            FruitType = fruit.FruitType,
            ProductionType = fruit.ProductionType,
            IsOrganic = fruit.IsOrganic,
            InventoryStatus = fruit.ProductionType
        };

    private static ReceiptInventoryBalanceViewModel ToViewModel(InventoryBalance x) => new(
        x.WarehouseId, x.Warehouse, x.RoomId, x.Room, x.CropYear, x.GrowerLotId, x.FruitProfileId,
        x.Grower, x.Lot, x.Variety, x.InventoryStatus, x.CurrentBins);

    private static ReceiptInventoryTrueUpPositionViewModel ToViewModel(TrueUpPosition x) => new(
        x.TargetKey, x.PositionType, x.Facility, x.Location, x.CurrentBins,
        x.Treatment, x.TreatmentSignature, x.TreatmentSegmentId, x.IsEligible, x.UnavailableReason);

    private static object ToAuditSnapshot(TrueUpAllocation allocation)
    {
        var snapshot = allocation.Position.Snapshot!;
        return new
        {
            allocation.Position.TargetKey,
            snapshot.WarehouseId,
            Warehouse = snapshot.Facility,
            snapshot.RoomId,
            Room = snapshot.Room,
            snapshot.CropYear,
            snapshot.GrowerLotId,
            snapshot.FruitProfileId,
            snapshot.Grower,
            Lot = snapshot.Lot,
            Variety = snapshot.Variety,
            InventoryStatus = snapshot.InventoryStatus,
            CurrentBins = snapshot.CurrentBins,
            AllocatedBins = allocation.Bins,
            allocation.Position.TreatmentSegmentId,
            allocation.Position.TreatmentState,
            allocation.Position.TreatmentSignature,
            allocation.Position.Treatment
        };
    }

    private async Task<string> GetInventoryIdentityStateTokenAsync(Receipt receipt, CancellationToken cancellationToken)
    {
        if (receipt.GrowerLotId is null) return "";
        var snapshots = (await ledgerQuery.GetSnapshotsAsync(null, null, cancellationToken))
            .Where(x => x.CropYear == receipt.CropYear && x.GrowerLotId == receipt.GrowerLotId
                && x.FruitProfileId == receipt.FruitProfileId && x.CurrentBins != 0)
            .ToList();
        var interCrew = await dbContext.InterCrewTransfers.AsNoTracking()
            .Where(x => x.Status == InterCrewTransferStatuses.InTransit && x.CropYear == receipt.CropYear
                && x.GrowerLotId == receipt.GrowerLotId && x.FruitProfileId == receipt.FruitProfileId)
            .Select(x => $"InterCrew:{x.Id}:{x.BinsLoaded}").ToListAsync(cancellationToken);
        var outside = await dbContext.OutsideWarehouseTransfers.AsNoTracking()
            .Where(x => !x.IsReversed && x.CropYear == receipt.CropYear
                && x.GrowerLotId == receipt.GrowerLotId && x.FruitProfileId == receipt.FruitProfileId)
            .Select(x => $"OutsideWarehouse:{x.Id}:{x.BinCount}").ToListAsync(cancellationToken);
        return CreateInventoryStateToken(snapshots, interCrew.Concat(outside));
    }

    private static string CreateTrueUpTargetKey(
        RoomInventoryLedgerSnapshot snapshot,
        TreatmentSegmentSelection selection)
    {
        var treatmentKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{selection.TreatmentSignature}|{selection.SegmentId}|{selection.ReceiptId}")))[..16];
        return $"R:{snapshot.WarehouseId}:{snapshot.RoomId}:{treatmentKey}";
    }

    public static string CreateInventoryStateToken(
        IEnumerable<RoomInventoryLedgerSnapshot> snapshots,
        IEnumerable<string>? custody = null)
    {
        var state = string.Join("|", snapshots
            .OrderBy(x => x.WarehouseId).ThenBy(x => x.RoomId)
            .Select(x => $"R:{x.WarehouseId}:{x.RoomId}:{x.CropYear}:{x.GrowerLotId}:{x.FruitProfileId}:{x.CurrentBins}:{x.LatestAdjustmentId}")
            .Concat((custody ?? []).OrderBy(x => x, StringComparer.Ordinal)));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(state)));
    }

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
            var receiptValuesMatch = root.GetProperty("warehouseId").GetInt32() == form.WarehouseId
                && root.GetProperty("roomId").GetInt32() == form.RoomId
                && root.GetProperty("cropYear").GetInt32() == form.CropYear
                && root.GetProperty("fruitProfileId").GetInt32() == form.FruitProfileId
                && string.Equals(root.GetProperty("growerNumber").GetString(), form.GrowerNumber.Trim(), StringComparison.OrdinalIgnoreCase);
            if (!receiptValuesMatch) return false;
            var requestedAllocations = form.TrueUpAllocations.Where(x => x.Bins > 0)
                .OrderBy(x => x.TargetKey, StringComparer.Ordinal)
                .Select(x => $"{x.TargetKey}:{x.Bins}")
                .ToList();
            if (requestedAllocations.Count == 0) return true;
            using var affected = JsonDocument.Parse(existing.AffectedInventorySnapshotJson);
            var persistedAllocations = affected.RootElement.EnumerateArray()
                .Where(x => x.TryGetProperty("targetKey", out _) && x.TryGetProperty("allocatedBins", out _))
                .OrderBy(x => x.GetProperty("targetKey").GetString(), StringComparer.Ordinal)
                .Select(x => $"{x.GetProperty("targetKey").GetString()}:{x.GetProperty("allocatedBins").GetInt32()}")
                .ToList();
            return requestedAllocations.SequenceEqual(persistedAllocations, StringComparer.Ordinal);
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
    private sealed record InventoryState(IReadOnlyList<InventoryBalance> Balances, bool HasExactReceiptProvenance)
    {
        public int Total => Balances.Sum(x => x.CurrentBins);
    }
    private sealed record TrueUpPosition(
        string TargetKey,
        string PositionType,
        string Facility,
        string Location,
        int CurrentBins,
        string Treatment,
        string TreatmentSignature,
        string TreatmentState,
        long? TreatmentSegmentId,
        long? TreatmentReceiptId,
        bool IsEligible,
        string? UnavailableReason,
        RoomInventoryLedgerSnapshot? Snapshot)
    {
        public static TrueUpPosition UnavailableRoom(RoomInventoryLedgerSnapshot snapshot, string reason) =>
            new($"R:{snapshot.WarehouseId}:{snapshot.RoomId}:unavailable", "Room", snapshot.Facility, snapshot.Room,
                snapshot.CurrentBins, "Needs Review", "needs-review", "NeedsReview", null, null, false, reason, snapshot);

        public static TrueUpPosition UnavailableCustody(
            string key, string kind, string location, int bins, string reason) =>
            new(key, kind, "Custody", location, bins, "Preserved custody treatment", "custody", "Custody",
                null, null, false, reason, null);
    }
    private sealed record TrueUpAllocation(TrueUpPosition Position, int Bins);
    private sealed record PositiveAllocationResult(IReadOnlyList<TrueUpAllocation> Allocations, string? Error);
    private sealed record PositiveTrueUpState(
        IReadOnlyList<RoomInventoryLedgerSnapshot> RoomSnapshots,
        IReadOnlyList<TrueUpPosition> Positions,
        IReadOnlyList<Guid> CorrectionChain,
        int CustodyBins,
        string StateToken,
        string? Error)
    {
        public int TotalBins => RoomSnapshots.Sum(x => x.CurrentBins) + CustodyBins;
    }
    private sealed record OperationalCounts(int BinsRuns, int ActualRuns, int Transfers);
}
