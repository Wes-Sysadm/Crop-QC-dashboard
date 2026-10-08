using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Data.Inventory;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CropQc.Web.Services;

public interface IInventoryDeductionInvariantService
{
    Task ValidateBeforeCommitAsync(CancellationToken cancellationToken);
    Task<InventoryDeductionReadinessResult> VerifyReadinessAsync(CancellationToken cancellationToken);
}

public sealed record InventoryDeductionIssue(
    long AdjustmentId,
    int InvariantVersion,
    string Code,
    string Message,
    bool BlocksDeployment);

public sealed record InventoryDeductionReadinessResult(
    int NegativeAdjustmentCount,
    int HistoricalNegativeCount,
    int NewFormatNegativeCount,
    IReadOnlyList<InventoryDeductionIssue> Issues)
{
    public bool IsReady => Issues.All(x => !x.BlocksDeployment);
}

public sealed class InventoryDeductionInvariantException(string message) : InvalidOperationException(message);

public sealed class InventoryDeductionInvariantService(
    CropQcDbContext dbContext,
    ILogger<InventoryDeductionInvariantService> logger) : IInventoryDeductionInvariantService
{
    public const int CurrentVersion = 1;

    public async Task ValidateBeforeCommitAsync(CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.DetectChanges();
        var adjustments = dbContext.ChangeTracker.Entries<RoomInventoryAdjustment>()
            .Where(x => x.State != EntityState.Deleted
                && (x.Entity.InventoryInvariantVersion >= CurrentVersion
                    || x.Entity.InventoryIdentityCorrectionId is not null
                    || x.Entity.InventoryIdentityCorrection is not null))
            .Select(x => x.Entity)
            .Distinct()
            .ToList();
        await ValidateSupersededWritesAsync(cancellationToken);
        if (adjustments.Count == 0)
        {
            return;
        }

        var issues = await AnalyzeAsync(adjustments, cancellationToken);
        var blocking = issues.FirstOrDefault();
        if (blocking is null)
        {
            return;
        }

        logger.LogWarning(
            "Room inventory deduction rejected. Code {Code}; adjustment {AdjustmentId}; invariant version {InvariantVersion}.",
            blocking.Code,
            blocking.AdjustmentId,
            blocking.InvariantVersion);
        throw new InventoryDeductionInvariantException(
            $"Inventory was not changed because its required Bins Run, Transfer, Receipt Admin Override, Room Inventory Loss, Processor Shipment, or Outside Warehouse Transfer relationship is invalid ({blocking.Code}).");
    }

    public async Task<InventoryDeductionReadinessResult> VerifyReadinessAsync(CancellationToken cancellationToken)
    {
        var negativeAdjustments = await dbContext.RoomInventoryAdjustments
            .AsNoTracking()
            .Where(x => x.ChangeAmount < 0)
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var positiveNewFormatAdjustments = await dbContext.RoomInventoryAdjustments
            .AsNoTracking()
            .Where(x => x.ChangeAmount >= 0
                && (x.InventoryInvariantVersion >= CurrentVersion
                    || x.InventoryIdentityCorrectionId != null))
            .OrderBy(x => x.Id)
            .ToListAsync(cancellationToken);
        var reviewedAdjustments = negativeAdjustments
            .Concat(positiveNewFormatAdjustments)
            .DistinctBy(x => x.Id)
            .ToList();
        var issues = await AnalyzeAsync(reviewedAdjustments, cancellationToken);
        return new InventoryDeductionReadinessResult(
            negativeAdjustments.Count,
            negativeAdjustments.Count(x => x.InventoryInvariantVersion < CurrentVersion),
            negativeAdjustments.Count(x => x.InventoryInvariantVersion >= CurrentVersion),
            issues);
    }

    private async Task<List<InventoryDeductionIssue>> AnalyzeAsync(
        IReadOnlyCollection<RoomInventoryAdjustment> adjustments,
        CancellationToken cancellationToken)
    {
        var issues = new List<InventoryDeductionIssue>();
        var adjustmentIds = adjustments.Where(x => x.Id > 0).Select(x => x.Id).ToList();
        var persistedEntries = adjustmentIds.Count == 0
            ? []
            : await dbContext.BinsRunEntries.AsNoTracking()
                .Where(x => adjustmentIds.Contains(x.InventoryAdjustmentId))
                .ToListAsync(cancellationToken);
        var trackedEntries = dbContext.ChangeTracker.Entries<BinsRunEntry>()
            .Where(x => x.State != EntityState.Deleted)
            .Select(x => x.Entity)
            .ToList();
        var entryLookup = persistedEntries
            .Concat(trackedEntries)
            .DistinctBy(x => x.Id == 0 ? RuntimeHelpers.GetHashCode(x) : x.Id)
            .ToList();

        var transferIds = adjustments
            .Where(x => x.RoomTransferId is not null)
            .Select(x => x.RoomTransferId!.Value)
            .Distinct()
            .ToList();
        var persistedTransfers = transferIds.Count == 0
            ? []
            : await dbContext.RoomTransfers.AsNoTracking()
                .Include(x => x.InventoryAdjustments)
                .Where(x => transferIds.Contains(x.Id))
                .ToListAsync(cancellationToken);
        var trackedTransfers = dbContext.ChangeTracker.Entries<RoomTransfer>()
            .Where(x => x.State != EntityState.Deleted)
            .Select(x => x.Entity)
            .ToList();

        var overrideIds = adjustments
            .Where(x => x.ReceiptInventoryOverrideId is not null)
            .Select(x => x.ReceiptInventoryOverrideId!.Value)
            .Distinct()
            .ToList();
        var persistedOverrides = overrideIds.Count == 0
            ? []
            : await dbContext.ReceiptInventoryOverrides.AsNoTracking()
                .Include(x => x.InventoryAdjustments)
                .Where(x => overrideIds.Contains(x.Id))
                .ToListAsync(cancellationToken);
        var trackedOverrides = dbContext.ChangeTracker.Entries<ReceiptInventoryOverride>()
            .Where(x => x.State != EntityState.Deleted)
            .Select(x => x.Entity)
            .ToList();

        var correctionIds = adjustments.Where(x => x.InventoryIdentityCorrectionId is not null)
            .Select(x => x.InventoryIdentityCorrectionId!.Value).Distinct().ToList();
        var persistedCorrections = correctionIds.Count == 0
            ? []
            : await dbContext.InventoryIdentityCorrections.AsNoTracking()
                .Include(x => x.InventoryAdjustments)
                .Where(x => correctionIds.Contains(x.Id))
                .ToListAsync(cancellationToken);
        var trackedCorrections = dbContext.ChangeTracker.Entries<InventoryIdentityCorrection>()
            .Where(x => x.State != EntityState.Deleted).Select(x => x.Entity).ToList();
        var overrideKeys = persistedOverrides.Select(x => x.OperationKey).Distinct().ToArray();
        var overrideCommands = await dbContext.InventoryCommands.AsNoTracking()
            .Where(x => overrideKeys.Contains(x.OperationKey)).ToDictionaryAsync(x => x.OperationKey, cancellationToken);
        var correctionMovements = await dbContext.TreatmentLineageMovements.AsNoTracking()
            .Where(x => x.InventoryIdentityCorrectionId != null && correctionIds.Contains(x.InventoryIdentityCorrectionId.Value))
            .ToListAsync(cancellationToken);
        var canonicalCorrections = (await dbContext.InventoryIdentityCorrections.AsNoTracking()
                .Where(x => x.IsActive && x.IsComplete)
                .ToListAsync(cancellationToken))
            .Concat(trackedCorrections.Where(x => x.IsActive && x.IsComplete))
            .DistinctBy(x => x.Id)
            .ToList();

        var lossIds = adjustments
            .Where(x => x.RoomInventoryLossId is not null)
            .Select(x => x.RoomInventoryLossId!.Value)
            .Distinct()
            .ToList();
        var persistedLosses = lossIds.Count == 0
            ? []
            : await dbContext.RoomInventoryLosses.AsNoTracking()
                .Include(x => x.InventoryAdjustments)
                .Where(x => lossIds.Contains(x.Id))
                .ToListAsync(cancellationToken);
        var trackedLosses = dbContext.ChangeTracker.Entries<RoomInventoryLoss>()
            .Where(x => x.State != EntityState.Deleted)
            .Select(x => x.Entity)
            .ToList();

        var processorLineIds = adjustments
            .Where(x => x.ProcessorShipmentLineId is not null)
            .Select(x => x.ProcessorShipmentLineId!.Value)
            .Distinct()
            .ToList();
        var persistedProcessorLines = processorLineIds.Count == 0
            ? []
            : await dbContext.ProcessorShipmentLines.AsNoTracking()
                .Include(x => x.InventoryAdjustments)
                .Where(x => processorLineIds.Contains(x.Id))
                .ToListAsync(cancellationToken);
        var trackedProcessorLines = dbContext.ChangeTracker.Entries<ProcessorShipmentLine>()
            .Where(x => x.State != EntityState.Deleted)
            .Select(x => x.Entity)
            .ToList();

        var outsideTransferIds = adjustments
            .Where(x => x.OutsideWarehouseTransferId is not null)
            .Select(x => x.OutsideWarehouseTransferId!.Value)
            .Distinct()
            .ToList();
        var persistedOutsideTransfers = outsideTransferIds.Count == 0
            ? []
            : await dbContext.OutsideWarehouseTransfers.AsNoTracking()
                .Include(x => x.InventoryAdjustments)
                .Where(x => outsideTransferIds.Contains(x.Id))
                .ToListAsync(cancellationToken);
        var trackedOutsideTransfers = dbContext.ChangeTracker.Entries<OutsideWarehouseTransfer>()
            .Where(x => x.State != EntityState.Deleted)
            .Select(x => x.Entity)
            .ToList();

        var outsideMovements = outsideTransferIds.Count == 0 ? []
            : await dbContext.TreatmentLineageMovements.AsNoTracking()
                .Where(x => x.OutsideWarehouseTransferId != null && outsideTransferIds.Contains(x.OutsideWarehouseTransferId.Value))
                .ToListAsync(cancellationToken);
        var trackedOutsideMovements = dbContext.ChangeTracker.Entries<TreatmentLineageMovement>()
            .Where(x => x.State != EntityState.Deleted).Select(x => x.Entity).ToList();

        var interCrewTransferIds = adjustments.Where(x => x.InterCrewTransferId is not null).Select(x => x.InterCrewTransferId!.Value).Distinct().ToList();
        var persistedInterCrewTransfers = interCrewTransferIds.Count == 0 ? [] : await dbContext.InterCrewTransfers.AsNoTracking()
            .Include(x => x.CustodyAcknowledgments).ThenInclude(x => x.Placements).ThenInclude(x => x.Movement)
            .Include(x => x.CustodyAcknowledgments).ThenInclude(x => x.Placements).ThenInclude(x => x.InventoryAdjustment)
            .Include(x => x.CustodyAcknowledgments).ThenInclude(x => x.Reversals).ThenInclude(x => x.Movement)
            .Include(x => x.CustodyAcknowledgments).ThenInclude(x => x.Reversals).ThenInclude(x => x.InventoryAdjustment)
            .Include(x => x.CustodyAcknowledgments).ThenInclude(x => x.DispatchMovement)
            .Include(x => x.InventoryAdjustments).Where(x => interCrewTransferIds.Contains(x.Id)).ToListAsync(cancellationToken);
        var trackedInterCrewTransfers = dbContext.ChangeTracker.Entries<InterCrewTransfer>()
            .Where(x => x.State != EntityState.Deleted).Select(x => x.Entity).ToList();

        foreach (var adjustment in adjustments)
        {
            var binsParents = entryLookup
                .Where(x => ReferenceEquals(x.InventoryAdjustment, adjustment)
                    || (adjustment.Id > 0 && x.InventoryAdjustmentId == adjustment.Id))
                .ToList();
            var transfer = adjustment.RoomTransfer
                ?? trackedTransfers.SingleOrDefault(x => adjustment.RoomTransferId == x.Id || ReferenceEquals(x, adjustment.RoomTransfer))
                ?? persistedTransfers.SingleOrDefault(x => x.Id == adjustment.RoomTransferId);
            var receiptOverride = adjustment.ReceiptInventoryOverride
                ?? trackedOverrides.SingleOrDefault(x => adjustment.ReceiptInventoryOverrideId == x.Id || ReferenceEquals(x, adjustment.ReceiptInventoryOverride))
                ?? persistedOverrides.SingleOrDefault(x => x.Id == adjustment.ReceiptInventoryOverrideId);
            var identityCorrection = adjustment.InventoryIdentityCorrection
                ?? trackedCorrections.SingleOrDefault(x => adjustment.InventoryIdentityCorrectionId == x.Id || ReferenceEquals(x, adjustment.InventoryIdentityCorrection))
                ?? persistedCorrections.SingleOrDefault(x => x.Id == adjustment.InventoryIdentityCorrectionId);
            var loss = adjustment.RoomInventoryLoss
                ?? trackedLosses.SingleOrDefault(x => adjustment.RoomInventoryLossId == x.Id || ReferenceEquals(x, adjustment.RoomInventoryLoss))
                ?? persistedLosses.SingleOrDefault(x => x.Id == adjustment.RoomInventoryLossId);
            var processorLine = adjustment.ProcessorShipmentLine
                ?? trackedProcessorLines.SingleOrDefault(x => adjustment.ProcessorShipmentLineId == x.Id || ReferenceEquals(x, adjustment.ProcessorShipmentLine))
                ?? persistedProcessorLines.SingleOrDefault(x => x.Id == adjustment.ProcessorShipmentLineId);
            var outsideTransfer = adjustment.OutsideWarehouseTransfer
                ?? trackedOutsideTransfers.SingleOrDefault(x => adjustment.OutsideWarehouseTransferId == x.Id || ReferenceEquals(x, adjustment.OutsideWarehouseTransfer))
                ?? persistedOutsideTransfers.SingleOrDefault(x => x.Id == adjustment.OutsideWarehouseTransferId);
            var interCrewTransfer = adjustment.InterCrewTransfer
                ?? trackedInterCrewTransfers.SingleOrDefault(x => adjustment.InterCrewTransferId == x.Id || ReferenceEquals(x, adjustment.InterCrewTransfer))
                ?? persistedInterCrewTransfers.SingleOrDefault(x => x.Id == adjustment.InterCrewTransferId);
            var parentCount = binsParents.Count + (transfer is null ? 0 : 1)
                + (receiptOverride is null && identityCorrection is null ? 0 : 1)
                + (loss is null ? 0 : 1) + (processorLine is null ? 0 : 1)
                + (outsideTransfer is null ? 0 : 1) + (interCrewTransfer is null ? 0 : 1);
            var blocks = adjustment.InventoryInvariantVersion >= CurrentVersion;

            if (adjustment.ChangeAmount < 0 && parentCount == 0)
            {
                Add("NoParent", "Negative adjustment has no persisted Bins Run, Transfer, Receipt Admin Override, Room Inventory Loss, Processor Shipment, Outside Warehouse Transfer, or Inter-Crew Transfer parent.");
                continue;
            }
            if (parentCount > 1)
            {
                Add("MultipleParents", "Adjustment is related to more than one parent transaction.");
                continue;
            }

            if (binsParents.Count == 1)
            {
                ValidateBinsRun(adjustment, binsParents[0], Add);
            }
            else if (transfer is not null)
            {
                var transferAdjustments = transfer.InventoryAdjustments
                    .Concat(dbContext.ChangeTracker.Entries<RoomInventoryAdjustment>()
                        .Where(x => x.State != EntityState.Deleted && (ReferenceEquals(x.Entity.RoomTransfer, transfer) || x.Entity.RoomTransferId == transfer.Id))
                        .Select(x => x.Entity))
                    .DistinctBy(x => x.Id == 0 ? RuntimeHelpers.GetHashCode(x) : x.Id)
                    .ToList();
                ValidateTransfer(adjustment, transfer, transferAdjustments, Add);
            }
            else if (receiptOverride is not null)
            {
                var overrideAdjustments = receiptOverride.InventoryAdjustments
                    .Concat(dbContext.ChangeTracker.Entries<RoomInventoryAdjustment>()
                        .Where(x => x.State != EntityState.Deleted
                            && (ReferenceEquals(x.Entity.ReceiptInventoryOverride, receiptOverride)
                                || x.Entity.ReceiptInventoryOverrideId == receiptOverride.Id))
                        .Select(x => x.Entity))
                    .DistinctBy(x => x.Id == 0 ? RuntimeHelpers.GetHashCode(x) : x.Id)
                    .ToList();
                overrideCommands.TryGetValue(receiptOverride.OperationKey, out var overrideCommand);
                ValidateReceiptOverride(adjustment, receiptOverride, overrideAdjustments, identityCorrection, overrideCommand,
                    correctionMovements.Where(x => x.InventoryIdentityCorrectionId == identityCorrection?.Id).ToArray(), Add);
            }
            else if (loss is not null)
            {
                var lossAdjustments = loss.InventoryAdjustments
                    .Concat(dbContext.ChangeTracker.Entries<RoomInventoryAdjustment>()
                        .Where(x => x.State != EntityState.Deleted
                            && (ReferenceEquals(x.Entity.RoomInventoryLoss, loss)
                                || x.Entity.RoomInventoryLossId == loss.Id))
                        .Select(x => x.Entity))
                    .DistinctBy(x => x.Id == 0 ? RuntimeHelpers.GetHashCode(x) : x.Id)
                    .ToList();
                ValidateRoomInventoryLoss(adjustment, loss, lossAdjustments, Add);
            }
            else if (processorLine is not null)
            {
                var processorAdjustments = processorLine.InventoryAdjustments
                    .Concat(dbContext.ChangeTracker.Entries<RoomInventoryAdjustment>()
                        .Where(x => x.State != EntityState.Deleted
                            && (ReferenceEquals(x.Entity.ProcessorShipmentLine, processorLine)
                                || x.Entity.ProcessorShipmentLineId == processorLine.Id))
                        .Select(x => x.Entity))
                    .DistinctBy(x => x.Id == 0 ? RuntimeHelpers.GetHashCode(x) : x.Id)
                    .ToList();
                ValidateProcessorShipment(adjustment, processorLine, processorAdjustments, Add);
            }
            else if (outsideTransfer is not null)
            {
                var outsideAdjustments = outsideTransfer.InventoryAdjustments
                    .Concat(dbContext.ChangeTracker.Entries<RoomInventoryAdjustment>()
                        .Where(x => x.State != EntityState.Deleted
                            && (ReferenceEquals(x.Entity.OutsideWarehouseTransfer, outsideTransfer)
                                || x.Entity.OutsideWarehouseTransferId == outsideTransfer.Id))
                        .Select(x => x.Entity))
                    .DistinctBy(x => x.Id == 0 ? RuntimeHelpers.GetHashCode(x) : x.Id)
                    .ToList();
                var movements = trackedOutsideMovements
                    .Where(x => ReferenceEquals(x.OutsideWarehouseTransfer, outsideTransfer)
                        || outsideTransfer.Id > 0 && x.OutsideWarehouseTransferId == outsideTransfer.Id)
                    .Concat(outsideMovements.Where(x => x.OutsideWarehouseTransferId == outsideTransfer.Id))
                    .DistinctBy(x => x.Id == 0 ? RuntimeHelpers.GetHashCode(x) : x.Id).ToList();
                ValidateOutsideWarehouseTransfer(adjustment, outsideTransfer, outsideAdjustments, movements, Add);
            }
            else if (interCrewTransfer is not null)
            {
                var persistedInterCrewTransfer = persistedInterCrewTransfers
                    .SingleOrDefault(x => x.Id == interCrewTransfer.Id);
                var operationAdjustments = interCrewTransfer.InventoryAdjustments
                    .Concat(persistedInterCrewTransfer?.InventoryAdjustments ?? [])
                    .Concat(dbContext.ChangeTracker.Entries<RoomInventoryAdjustment>()
                        .Where(x => x.State != EntityState.Deleted && (ReferenceEquals(x.Entity.InterCrewTransfer, interCrewTransfer) || x.Entity.InterCrewTransferId == interCrewTransfer.Id))
                        .Select(x => x.Entity))
                    .DistinctBy(x => x.Id == 0 ? RuntimeHelpers.GetHashCode(x) : x.Id).ToList();
                if (persistedInterCrewTransfer?.CustodyAcknowledgments.Count > 0)
                    ValidateReceiptCustodyLedger(persistedInterCrewTransfer, operationAdjustments, Add);
                else ValidateInterCrewTransfer(adjustment, interCrewTransfer, operationAdjustments, canonicalCorrections, Add);
            }
            if (identityCorrection is not null)
            {
                var correctionAdjustments = identityCorrection.InventoryAdjustments
                    .Concat(dbContext.ChangeTracker.Entries<RoomInventoryAdjustment>()
                        .Where(x => x.State != EntityState.Deleted
                            && (ReferenceEquals(x.Entity.InventoryIdentityCorrection, identityCorrection)
                                || x.Entity.InventoryIdentityCorrectionId == identityCorrection.Id))
                        .Select(x => x.Entity))
                    .DistinctBy(x => x.Id == 0 ? RuntimeHelpers.GetHashCode(x) : x.Id)
                    .ToList();
                ValidateIdentityCorrection(adjustment, identityCorrection, correctionAdjustments, Add);
            }

            void Add(string code, string message)
            {
                issues.Add(new InventoryDeductionIssue(
                    adjustment.Id,
                    adjustment.InventoryInvariantVersion,
                    code,
                    message,
                    blocks));
            }
        }

        return issues;
    }

    private async Task ValidateSupersededWritesAsync(CancellationToken cancellationToken)
    {
        var added = dbContext.ChangeTracker.Entries<RoomInventoryAdjustment>()
            .Where(x => x.State == EntityState.Added && x.Entity.InventoryInvariantVersion >= CurrentVersion)
            .Select(x => x.Entity)
            .ToList();
        if (added.Count == 0) return;

        var trackedCorrections = dbContext.ChangeTracker.Entries<InventoryIdentityCorrection>()
            .Where(x => x.State != EntityState.Deleted && x.Entity.IsActive && x.Entity.IsComplete)
            .Select(x => x.Entity);
        var corrections = (await dbContext.InventoryIdentityCorrections.AsNoTracking()
                .Where(x => x.IsActive && x.IsComplete)
                .ToListAsync(cancellationToken))
            .Concat(trackedCorrections)
            .DistinctBy(x => x.Id)
            .ToList();
        if (corrections.Count == 0) return;

        foreach (var adjustment in added)
        {
            var isAuthorizedSourceRemoval = adjustment.InventoryIdentityCorrectionId is not null
                && adjustment.ChangeAmount < 0;
            if (isAuthorizedSourceRemoval) continue;

            if (adjustment.CropYear is null || adjustment.GrowerLotId is null || adjustment.FruitProfileId is null)
            {
                var intersects = corrections.Any(x =>
                    (adjustment.CropYear is null || x.SourceCropYear == adjustment.CropYear)
                    && (adjustment.GrowerLotId is null || x.SourceGrowerLotId == adjustment.GrowerLotId)
                    && (adjustment.FruitProfileId is null || x.SourceFruitProfileId == adjustment.FruitProfileId));
                if (intersects)
                    throw new InventoryDeductionInvariantException(
                        "A new inventory transaction has incomplete identity fields that intersect a superseded identity.");
                continue;
            }

            var source = new InventoryIdentityKey(
                adjustment.CropYear.Value,
                adjustment.GrowerLotId.Value,
                adjustment.FruitProfileId.Value);
            var canonical = InventoryIdentityWriteGuard.ResolveCanonical(source, corrections);
            if (canonical != source)
                throw new InventoryDeductionInvariantException(
                    $"A new inventory transaction cannot use superseded identity {source}; use canonical identity {canonical}.");
        }
    }

    private static void ValidateIdentityCorrection(
        RoomInventoryAdjustment adjustment,
        InventoryIdentityCorrection correction,
        IReadOnlyCollection<RoomInventoryAdjustment> operationAdjustments,
        Action<string, string> add)
    {
        if (!correction.IsComplete || !correction.IsActive || correction.SourceCropYear == correction.TargetCropYear
            && correction.SourceGrowerLotId == correction.TargetGrowerLotId
            && correction.SourceFruitProfileId == correction.TargetFruitProfileId)
            add("InvalidIdentityCorrection", "Inventory identity correction is incomplete, inactive, or self-referential.");
        if (operationAdjustments.Count != correction.ExpectedAdjustmentCount || operationAdjustments.Sum(x => x.ChangeAmount) != 0)
            add("IdentityCorrectionQuantityMismatch", "Inventory identity correction must conserve quantity and match its expected ledger rows.");
        if (operationAdjustments.Any(x =>
            {
                var isSource = x.CropYear == correction.SourceCropYear
                    && x.GrowerLotId == correction.SourceGrowerLotId
                    && x.FruitProfileId == correction.SourceFruitProfileId;
                var isTarget = x.CropYear == correction.TargetCropYear
                    && x.GrowerLotId == correction.TargetGrowerLotId
                    && x.FruitProfileId == correction.TargetFruitProfileId;
                return !isSource && !isTarget;
            }))
            add("IdentityCorrectionIdentityMismatch", "Inventory identity correction rows must use only the correction parent's source or target identity.");
        foreach (var roomGroup in operationAdjustments.GroupBy(x => new { x.WarehouseId, x.RoomId }))
        {
            var source = roomGroup.Where(x => x.CropYear == correction.SourceCropYear
                && x.GrowerLotId == correction.SourceGrowerLotId && x.FruitProfileId == correction.SourceFruitProfileId).Sum(x => x.ChangeAmount);
            var target = roomGroup.Where(x => x.CropYear == correction.TargetCropYear
                && x.GrowerLotId == correction.TargetGrowerLotId && x.FruitProfileId == correction.TargetFruitProfileId).Sum(x => x.ChangeAmount);
            if (source + target != 0 || source == 0)
                add("IdentityCorrectionRoomMismatch", "Each corrected room must contain exact, quantity-conserving source and target entries.");
        }
        if (adjustment.InventoryIdentityCorrectionId is null && adjustment.InventoryIdentityCorrection is null)
            add("MissingIdentityCorrectionLink", "Identity correction adjustment is not linked to its durable correction parent.");
    }

    private static void ValidateProcessorShipment(
        RoomInventoryAdjustment adjustment,
        ProcessorShipmentLine line,
        IReadOnlyCollection<RoomInventoryAdjustment> operationAdjustments,
        Action<string, string> add)
    {
        var shipment = operationAdjustments.Where(x => string.Equals(x.AdjustmentType, ProcessorShipmentAdjustmentTypes.Shipment, StringComparison.Ordinal)).ToList();
        var reversal = operationAdjustments.Where(x => string.Equals(x.AdjustmentType, ProcessorShipmentAdjustmentTypes.Reversal, StringComparison.Ordinal)).ToList();
        if (line.BinsSent <= 0 || shipment.Count != 1 || shipment[0].ChangeAmount != -line.BinsSent)
        {
            add("ProcessorShipmentAmountMismatch", "Processor Shipment must have exactly one deduction equal to its persisted source-line quantity.");
        }
        if (reversal.Count > 1 || (reversal.Count == 1 && reversal[0].ChangeAmount != line.BinsSent))
        {
            add("ProcessorShipmentReversalMismatch", "Processor Shipment reversal does not match its persisted source-line quantity.");
        }
        if (operationAdjustments.Count != shipment.Count + reversal.Count)
        {
            add("ProcessorShipmentAdjustmentTypeMismatch", "Processor Shipment contains an unsupported ledger adjustment type.");
        }
        foreach (var side in operationAdjustments)
        {
            if (side.WarehouseId != line.WarehouseId
                || side.RoomId != line.RoomId
                || side.CropYear != line.CropYear
                || side.GrowerLotId != line.GrowerLotId
                || side.FruitProfileId != line.FruitProfileId
                || !Same(side.LotNumber, line.LotNumberSnapshot)
                || !Same(side.VarietyCode, line.VarietyCodeSnapshot)
                || !Same(side.InventoryStatus, line.InventoryStatusSnapshot))
            {
                add("ProcessorShipmentIdentityMismatch", "Processor Shipment ledger identity does not match its durable source line.");
                break;
            }
            if (side.OldBinCount is null || side.NewBinCount != side.OldBinCount + side.ChangeAmount)
            {
                add("ProcessorShipmentBalanceMismatch", "Processor Shipment before/after balance does not reconcile with its quantity.");
                break;
            }
        }
        if (adjustment.ProcessorShipmentLineId is null && adjustment.ProcessorShipmentLine is null)
        {
            add("MissingProcessorShipmentLink", "Processor Shipment adjustment is not linked by a persisted source line ID.");
        }
    }

    private static void ValidateOutsideWarehouseTransfer(
        RoomInventoryAdjustment adjustment,
        OutsideWarehouseTransfer transfer,
        IReadOnlyCollection<RoomInventoryAdjustment> operationAdjustments,
        IReadOnlyCollection<TreatmentLineageMovement> movements,
        Action<string, string> add)
    {
        var outbound = operationAdjustments.Where(x => string.Equals(x.AdjustmentType, OutsideWarehouseTransferAdjustmentTypes.Transfer, StringComparison.Ordinal)).ToList();
        var reversal = operationAdjustments.Where(x => string.Equals(x.AdjustmentType, OutsideWarehouseTransferAdjustmentTypes.Reversal, StringComparison.Ordinal)).ToList();
        if (transfer.BinCount <= 0 || outbound.Count != 1 || outbound[0].ChangeAmount != -transfer.BinCount)
        {
            add("OutsideWarehouseTransferAmountMismatch", "Outside Warehouse Transfer must have exactly one deduction equal to its persisted quantity.");
        }
        if ((!transfer.IsReversed && reversal.Count != 0)
            || (transfer.IsReversed && (reversal.Count != 1 || reversal[0].ChangeAmount != transfer.BinCount)))
        {
            add("OutsideWarehouseTransferReversalMismatch", "Outside Warehouse Transfer reversal state does not match its positive ledger adjustment.");
        }
        if (operationAdjustments.Count != outbound.Count + reversal.Count)
        {
            add("OutsideWarehouseTransferAdjustmentTypeMismatch", "Outside Warehouse Transfer contains an unsupported ledger adjustment type.");
        }
        foreach (var side in operationAdjustments)
        {
            // Names are historical display evidence, not identity: canonical Ledger stores
            // the grower number here, while the transfer retains the source segment's name.
            // Keep the legacy name guard when stable identity is incomplete. Never rewrite
            // either snapshot to follow a subsequent master-data rename.
            var hasStableIdentity = side.GrowerLotId is > 0 && side.FruitProfileId is > 0
                && side.CropYear is > 0 && !string.IsNullOrWhiteSpace(side.LotNumber);
            if (side.WarehouseId != transfer.SourceWarehouseId
                || side.RoomId != transfer.SourceRoomId
                || side.ReceiptId != transfer.ReceiptId
                || side.CropYear != transfer.CropYear
                || side.GrowerLotId != transfer.GrowerLotId
                || side.FruitProfileId != transfer.FruitProfileId
                || !hasStableIdentity && !Same(side.GrowerName, transfer.GrowerNameSnapshot)
                || !Same(side.LotNumber, transfer.LotNumberSnapshot)
                || !Same(side.VarietyCode, transfer.VarietyCodeSnapshot)
                || !Same(side.InventoryStatus, transfer.InventoryStatusSnapshot))
            {
                add("OutsideWarehouseTransferIdentityMismatch", "Outside Warehouse Transfer ledger identity does not match its durable snapshot.");
                break;
            }
            if (side.OldBinCount is null || side.NewBinCount != side.OldBinCount + side.ChangeAmount)
            {
                add("OutsideWarehouseTransferBalanceMismatch", "Outside Warehouse Transfer before/after balance does not reconcile with its quantity.");
                break;
            }
            if (side.RoomTransferId is not null || side.ReceiptInventoryOverrideId is not null
                || side.RoomInventoryLossId is not null || side.ProcessorShipmentLineId is not null)
            {
                add("MultipleParents", "Outside Warehouse Transfer adjustment also references another operational parent.");
                break;
            }
        }
        if (outbound.Any(x => x.InventoryInvariantVersion >= InventoryLedgerKinds.CanonicalCommandInvariantVersion))
        {
            // Canonical movements retain the immutable grower number and complete identity,
            // unlike the ledger's display field. Require that independent evidence so a real
            // grower-number, organic, treatment or operation mismatch still blocks deployment.
            var identity = new InventoryIdentity(transfer.CropYear, transfer.GrowerLotId, transfer.FruitProfileId,
                transfer.LotNumberSnapshot, transfer.GrowerNumberSnapshot, transfer.VarietyCodeSnapshot,
                transfer.ProductionTypeSnapshot, transfer.IsOrganicSnapshot, transfer.InventoryStatusSnapshot ?? "");
            var dispatch = movements.Where(x => x.MovementType == OutsideWarehouseTransferAdjustmentTypes.Transfer
                && x.ReversesTreatmentLineageMovementId == null).ToList();
            if (!identity.IsComplete || dispatch.Count == 0 || dispatch.Sum(x => (long)x.BinCount) != transfer.BinCount
                || dispatch.Any(x => x.BinCount <= 0 || x.IdentityKey != identity.Key
                    || x.SourceRoomId != transfer.SourceRoomId || x.DestinationRoomId != null
                    || x.SourceSegmentId == null || x.DestinationSegmentId != null
                    || x.OperationKey != transfer.OperationKey + ":s" + x.SourceSegmentId
                    || transfer.ReceiptId != null && x.ReceiptId != transfer.ReceiptId
                    || !Same(x.TreatmentSignatureSnapshot, transfer.TreatmentSignatureSnapshot)
                    || !Same(x.TreatmentStateSnapshot, transfer.TreatmentStateSnapshot)))
                add("OutsideWarehouseTransferMovementMismatch", "Canonical Outside Warehouse Transfer identity, quantity or provenance does not match its immutable dispatch movements.");
        }
        if (adjustment.OutsideWarehouseTransferId is null && adjustment.OutsideWarehouseTransfer is null)
        {
            add("MissingOutsideWarehouseTransferLink", "Outside Warehouse Transfer adjustment is not linked by a persisted transfer ID.");
        }
    }

    private static void ValidateInterCrewTransfer(
        RoomInventoryAdjustment adjustment, InterCrewTransfer transfer,
        IReadOnlyCollection<RoomInventoryAdjustment> operationAdjustments,
        IReadOnlyCollection<InventoryIdentityCorrection> canonicalCorrections,
        Action<string, string> add)
    {
        if (transfer.RequiresTruckReceipt)
        {
            ValidateTruckReceiptLedger(transfer, operationAdjustments, add);
            return;
        }
        var dispatch = operationAdjustments.Where(x => x.AdjustmentType == InterCrewTransferAdjustmentTypes.Dispatch).ToList();
        var receive = operationAdjustments.Where(x => x.AdjustmentType == InterCrewTransferAdjustmentTypes.Receive).ToList();
        var reverseDestination = operationAdjustments.Where(x => x.AdjustmentType == InterCrewTransferAdjustmentTypes.ReversalDestination).ToList();
        var reverseSource = operationAdjustments.Where(x => x.AdjustmentType == InterCrewTransferAdjustmentTypes.ReversalSource).ToList();
        if (transfer.BinsLoaded <= 0 || dispatch.Count != 1 || dispatch[0].ChangeAmount != -transfer.BinsLoaded)
            add("InterCrewDispatchAmountMismatch", "Inter-crew dispatch must have exactly one source deduction equal to Bins Loaded.");
        var received = transfer.BinsReceived is not null;
        if ((!received && receive.Count != 0) || (received && (receive.Count != 1 || receive[0].ChangeAmount != transfer.BinsReceived)))
            add("InterCrewReceiveAmountMismatch", "Inter-crew receiving ledger does not equal the immutable Bins Received count.");
        var reversed = transfer.Status == InterCrewTransferStatuses.Reversed;
        if ((!reversed && (reverseDestination.Count != 0 || reverseSource.Count != 0))
            || (reversed && (reverseSource.Count != 1 || reverseSource[0].ChangeAmount != transfer.BinsLoaded))
            || (reversed && received && (reverseDestination.Count != 1 || reverseDestination[0].ChangeAmount != -transfer.BinsReceived)))
            add("InterCrewReversalMismatch", "Inter-crew reversal does not restore loaded bins and remove received bins exactly.");
        if (operationAdjustments.Count != dispatch.Count + receive.Count + reverseDestination.Count + reverseSource.Count)
            add("InterCrewAdjustmentTypeMismatch", "Inter-crew transfer contains an unsupported ledger adjustment type.");
        foreach (var side in operationAdjustments)
        {
            var isSource = side.AdjustmentType is InterCrewTransferAdjustmentTypes.Dispatch or InterCrewTransferAdjustmentTypes.ReversalSource;
            var expectedWarehouse = isSource ? transfer.SourceWarehouseId : transfer.DestinationWarehouseId;
            var expectedRoom = isSource ? transfer.SourceRoomId : transfer.DestinationRoomId;
            var exactHistoricalIdentity = side.CropYear == transfer.CropYear
                && side.GrowerLotId == transfer.GrowerLotId && side.FruitProfileId == transfer.FruitProfileId
                && Same(side.LotNumber, transfer.LotNumberSnapshot) && Same(side.VarietyCode, transfer.VarietyCodeSnapshot)
                && Same(side.InventoryStatus, transfer.InventoryStatusSnapshot);
            var canonicalReceiveIdentity = false;
            if (!isSource && transfer.CropYear is not null && transfer.GrowerLotId is not null && transfer.FruitProfileId is not null
                && side.CropYear is not null && side.GrowerLotId is not null && side.FruitProfileId is not null)
            {
                var historical = new InventoryIdentityKey(transfer.CropYear.Value, transfer.GrowerLotId.Value, transfer.FruitProfileId.Value);
                var finalCanonical = InventoryIdentityWriteGuard.ResolveCanonical(historical, canonicalCorrections);
                canonicalReceiveIdentity = side.CropYear == finalCanonical.CropYear
                    && side.GrowerLotId == finalCanonical.GrowerLotId
                    && side.FruitProfileId == finalCanonical.FruitProfileId;
            }
            if (side.WarehouseId != expectedWarehouse || side.RoomId != expectedRoom
                || !exactHistoricalIdentity && !canonicalReceiveIdentity)
                add("InterCrewIdentityMismatch", "Inter-crew ledger identity does not match its durable transfer snapshot.");
            if (side.OldBinCount is null || side.NewBinCount != side.OldBinCount + side.ChangeAmount)
                add("InterCrewBalanceMismatch", "Inter-crew before/after balance does not reconcile with its quantity.");
        }
        if (adjustment.InterCrewTransferId is null && adjustment.InterCrewTransfer is null)
            add("MissingInterCrewTransferLink", "Inter-crew adjustment is not linked by a persisted transfer ID.");
    }

    private static void ValidateTruckReceiptLedger(InterCrewTransfer transfer,
        IReadOnlyCollection<RoomInventoryAdjustment> rows, Action<string, string> add)
    {
        if (transfer.CustodyAcknowledgments.Count > 0)
        {
            ValidateReceiptCustodyLedger(transfer, rows, add);
            return;
        }
        var source = rows.Where(x => x.AdjustmentType is InterCrewTransferAdjustmentTypes.Dispatch or TruckReceiptReconciliationService.ReturnToSource).ToList();
        var destination = rows.Where(x => x.AdjustmentType is InterCrewTransferAdjustmentTypes.Receive or TruckReceiptReconciliationService.ReopenDestination).ToList();
        if (source.Count + destination.Count != rows.Count || (transfer.BinsLoaded < 0 || transfer.BinsLoaded == 0 && transfer.Status != InterCrewTransferStatuses.Reversed) || source.Sum(x => x.ChangeAmount) != -transfer.BinsLoaded)
            add("TruckReceiptDispatchMismatch", "The active dispatch ledger must equal the In Transit manifest.");
        var received = transfer.Status == InterCrewTransferStatuses.Received;
        if (destination.Sum(x => x.ChangeAmount) != (received ? transfer.BinsLoaded : 0)
            || received && (transfer.BinsReceived != transfer.BinsLoaded || transfer.VarianceBins != 0 || transfer.ReceivingReceiptId == null))
            add("TruckReceiptReceiveMismatch", "Receiving must exactly balance the active load and have a linked receipt.");
        foreach (var row in rows)
        {
            var isSource = source.Contains(row);
            if (isSource && (row.WarehouseId != transfer.SourceWarehouseId || row.RoomId != transfer.SourceRoomId)
                || row.OldBinCount is null || row.NewBinCount != row.OldBinCount + row.ChangeAmount
                || row.CropYear is null || row.FruitProfileId is null)
                add("TruckReceiptIdentityMismatch", "The transfer ledger location, canonical identity or before/after balance is invalid.");
        }
        foreach (var location in destination.GroupBy(x => new { x.WarehouseId, x.RoomId }))
        {
            var net = location.Sum(x => x.ChangeAmount);
            if (net < 0 || net != 0 && (!received || location.Key.WarehouseId != transfer.DestinationWarehouseId || location.Key.RoomId != transfer.DestinationRoomId))
                add("TruckReceiptDestinationMismatch", "Only the current receiving destination may retain transfer inventory.");
        }
        foreach (var group in rows.GroupBy(x => new { x.CropYear, x.GrowerLotId, x.FruitProfileId, x.LotNumber, x.VarietyCode }))
        {
            var debit = group.Where(source.Contains).Sum(x => x.ChangeAmount);
            var credit = group.Where(destination.Contains).Sum(x => x.ChangeAmount);
            if (debit > 0 || credit != (received ? -debit : 0))
                add("TruckReceiptVarietyMismatch", "Each original inventory identity must be conserved independently.");
        }
    }

    private static void ValidateBinsRun(
        RoomInventoryAdjustment adjustment,
        BinsRunEntry entry,
        Action<string, string> add)
    {
        var isReversal = string.Equals(entry.TransactionType, ActualRunTransactionTypes.Reversal, StringComparison.OrdinalIgnoreCase);
        var expectedChange = isReversal ? entry.BinsRun : -entry.BinsRun;
        if (adjustment.ChangeAmount != expectedChange)
        {
            add("AmountMismatch", "Bins Run and room-ledger quantities do not match.");
        }
        if (adjustment.WarehouseId != entry.WarehouseId)
        {
            add("FacilityMismatch", "Bins Run and room-ledger facilities do not match.");
        }
        if (adjustment.RoomId != entry.RoomId)
        {
            add("RoomMismatch", "Bins Run and room-ledger rooms do not match.");
        }
        if (adjustment.CropYear != entry.CropYear)
        {
            add("CropYearMismatch", "Bins Run and room-ledger crop years do not match.");
        }
        if (!Same(adjustment.LotNumber, entry.LotNumber))
        {
            add("LotMismatch", "Bins Run and room-ledger lots do not match.");
        }
        if (adjustment.FruitProfileId != entry.FruitProfileId)
        {
            add("FruitProfileMismatch", "Bins Run and room-ledger fruit profiles do not match.");
        }
        if (!Same(adjustment.VarietyCode, entry.VarietyCode))
        {
            add("VarietyMismatch", "Bins Run and room-ledger varieties do not match.");
        }
        if (!Same(adjustment.InventoryStatus, entry.InventoryStatus))
        {
            add("OrganicStatusMismatch", "Bins Run and room-ledger organic/conventional identities do not match.");
        }
        if (adjustment.OldBinCount != entry.PreviousAvailableBins || adjustment.NewBinCount != entry.NewAvailableBins)
        {
            add("BalanceMismatch", "Bins Run and room-ledger before/after balances do not match.");
        }
    }

    private static void ValidateTransfer(
        RoomInventoryAdjustment adjustment,
        RoomTransfer transfer,
        IReadOnlyCollection<RoomInventoryAdjustment> pair,
        Action<string, string> add)
    {
        var outs = pair.Where(x => string.Equals(x.AdjustmentType, "TransferOut", StringComparison.OrdinalIgnoreCase)).ToList();
        var ins = pair.Where(x => string.Equals(x.AdjustmentType, "TransferIn", StringComparison.OrdinalIgnoreCase)).ToList();
        if (outs.Count != 1 || ins.Count != 1)
        {
            add("IncompleteTransferPair", "Transfer must have exactly one Transfer Out and one Transfer In adjustment.");
            return;
        }

        var outgoing = outs[0];
        var incoming = ins[0];
        if (outgoing.ChangeAmount != -transfer.BinCount || incoming.ChangeAmount != transfer.BinCount)
        {
            add("TransferAmountMismatch", "Transfer In and Transfer Out must be equal to the persisted transfer quantity.");
        }
        if (transfer.SourceRoomId == transfer.DestinationRoomId)
        {
            add("TransferRoomMismatch", "Transfer source and destination rooms must be different.");
        }
        if (outgoing.WarehouseId != transfer.SourceWarehouseId || outgoing.RoomId != transfer.SourceRoomId
            || incoming.WarehouseId != transfer.DestinationWarehouseId || incoming.RoomId != transfer.DestinationRoomId)
        {
            add("TransferLocationMismatch", "Transfer adjustment rooms or facilities do not match the persisted transfer.");
        }
        foreach (var side in pair)
        {
            if (side.CropYear != transfer.CropYear
                || !Same(side.LotNumber, transfer.LotNumber)
                || side.FruitProfileId != transfer.FruitProfileId
                || !Same(side.VarietyCode, transfer.VarietyCode)
                || !Same(side.InventoryStatus, transfer.InventoryStatus))
            {
                add("TransferIdentityMismatch", "Transfer adjustment inventory identity does not match the persisted transfer.");
                break;
            }
        }
        if (adjustment.RoomTransferId is null && adjustment.RoomTransfer is null)
        {
            add("MissingTransferLink", "Transfer adjustment is not linked by a persisted transfer ID.");
        }
    }

    private static void ValidateRoomInventoryLoss(
        RoomInventoryAdjustment adjustment,
        RoomInventoryLoss loss,
        IReadOnlyCollection<RoomInventoryAdjustment> operationAdjustments,
        Action<string, string> add)
    {
        if (!string.Equals(loss.LossType, RoomInventoryLossTypes.Dropped, StringComparison.Ordinal)
            || loss.BinCount <= 0
            || string.IsNullOrWhiteSpace(loss.OperationKey)
            || string.IsNullOrWhiteSpace(loss.Reason))
        {
            add("InvalidRoomInventoryLoss", "Room Inventory Loss is incomplete or uses an unsupported loss type.");
        }

        var dropped = operationAdjustments
            .Where(x => string.Equals(x.AdjustmentType, RoomInventoryLossAdjustmentTypes.DroppedBins, StringComparison.Ordinal))
            .ToList();
        var restored = operationAdjustments
            .Where(x => string.Equals(x.AdjustmentType, RoomInventoryLossAdjustmentTypes.DroppedBinsReversal, StringComparison.Ordinal))
            .ToList();
        if (dropped.Count != 1 || dropped[0].ChangeAmount != -loss.BinCount)
        {
            add("RoomInventoryLossAmountMismatch", "Room Inventory Loss must have exactly one DroppedBins adjustment equal to the persisted loss quantity.");
        }
        if ((!loss.IsReversed && restored.Count != 0)
            || (loss.IsReversed && (restored.Count != 1 || restored[0].ChangeAmount != loss.BinCount)))
        {
            add("RoomInventoryLossReversalMismatch", "Room Inventory Loss reversal state and positive ledger adjustment do not match.");
        }
        if (operationAdjustments.Count != dropped.Count + restored.Count)
        {
            add("RoomInventoryLossAdjustmentTypeMismatch", "Room Inventory Loss contains an unsupported adjustment type.");
        }
        foreach (var side in operationAdjustments)
        {
            if (side.WarehouseId != loss.WarehouseId
                || side.RoomId != loss.RoomId
                || side.ReceiptId != loss.ReceiptId
                || side.CropYear != loss.CropYear
                || side.GrowerLotId != loss.GrowerLotId
                || side.FruitProfileId != loss.FruitProfileId
                || !Same(side.GrowerName, loss.GrowerName)
                || !Same(side.LotNumber, loss.LotNumber)
                || !Same(side.VarietyCode, loss.VarietyCode)
                || !Same(side.InventoryStatus, loss.InventoryStatus))
            {
                add("RoomInventoryLossIdentityMismatch", "Room Inventory Loss adjustment inventory identity does not match its persisted parent.");
                break;
            }
            if (side.OldBinCount is null || side.NewBinCount != side.OldBinCount + side.ChangeAmount)
            {
                add("RoomInventoryLossBalanceMismatch", "Room Inventory Loss before/after balance does not reconcile with its quantity.");
                break;
            }
            if (side.RoomTransferId is not null || side.ReceiptInventoryOverrideId is not null)
            {
                add("MultipleParents", "Room Inventory Loss adjustment also references another operational parent.");
                break;
            }
        }
        if (adjustment.RoomInventoryLossId is null && adjustment.RoomInventoryLoss is null)
        {
            add("MissingRoomInventoryLossLink", "Dropped-bin adjustment is not linked by a persisted Room Inventory Loss ID.");
        }
    }

    private static void ValidateReceiptOverride(
        RoomInventoryAdjustment adjustment,
        ReceiptInventoryOverride receiptOverride,
        IReadOnlyCollection<RoomInventoryAdjustment> operationAdjustments,
        InventoryIdentityCorrection? correction,
        InventoryCommandRecord? command,
        IReadOnlyCollection<TreatmentLineageMovement> movements,
        Action<string, string> add)
    {
        if (!receiptOverride.IsComplete
            || string.IsNullOrWhiteSpace(receiptOverride.OperationKey)
            || string.IsNullOrWhiteSpace(receiptOverride.Reason)
            || string.IsNullOrWhiteSpace(receiptOverride.BeforeReceiptSnapshotJson)
            || string.IsNullOrWhiteSpace(receiptOverride.AfterReceiptSnapshotJson))
        {
            add("IncompleteReceiptOverride", "Receipt administrator override is incomplete.");
        }
        if (operationAdjustments.Count != receiptOverride.ExpectedAdjustmentCount)
        {
            add("ReceiptOverrideAdjustmentCountMismatch", "Receipt administrator override adjustment count does not match its persisted operation.");
        }
        if (operationAdjustments.Sum(x => x.ChangeAmount) != receiptOverride.InventoryDelta)
        {
            add("ReceiptOverrideAmountMismatch", "Receipt administrator override and room-ledger quantities do not match.");
        }
        // Dispatch by the versioned writer contract, not a receipt/adjustment exception.
        // The canonical branch must prove its journal, parent and snapshots below.
        var canonicalIdentity = operationAdjustments.Any(x => x.InventoryInvariantVersion == InventoryLedgerKinds.CanonicalCommandInvariantVersion
            && x.AdjustmentType == "InventoryIdentityCorrection");
        if (operationAdjustments.Any(x => x.ReceiptId != receiptOverride.ReceiptId
            || x.CreatedByUserId != receiptOverride.AdministratorUserId
            || (canonicalIdentity
                ? x.AdjustmentType != "InventoryIdentityCorrection"
                : !ValidOverrideAdjustmentType(x, receiptOverride.ActionType))
            || string.IsNullOrWhiteSpace(x.LotNumber)
            || x.FruitProfileId is null))
        {
            add("ReceiptOverrideIdentityMismatch", "Receipt administrator override adjustment receipt, administrator, or inventory identity does not match.");
        }
        if (canonicalIdentity)
            ValidateCanonicalReceiptIdentity(receiptOverride, operationAdjustments, correction, command, movements, add);
        else
            ValidateReceiptOverrideInventoryIdentity(receiptOverride, operationAdjustments, add);
        if (adjustment.ReceiptInventoryOverrideId is null && adjustment.ReceiptInventoryOverride is null)
        {
            add("MissingReceiptOverrideLink", "Receipt administrator adjustment is not linked by a persisted override ID.");
        }

        if (string.Equals(receiptOverride.ActionType, ReceiptInventoryOverrideActionTypes.QuantityCorrection, StringComparison.Ordinal))
        {
            if (receiptOverride.InventoryDelta != receiptOverride.NewReceiptBinCount - receiptOverride.OldReceiptBinCount
                || receiptOverride.CurrentInventoryAfter != receiptOverride.CurrentInventoryBefore + receiptOverride.InventoryDelta)
            {
                add("ReceiptOverrideQuantityMismatch", "Receipt quantity correction before, after, and inventory delta do not reconcile.");
            }
            if (receiptOverride.CurrentInventoryAfter < 0 && !receiptOverride.NegativeInventoryAcknowledged)
            {
                add("MissingNegativeInventoryAcknowledgment", "Negative inventory requires an explicit administrator acknowledgment.");
            }
        }
        else if (string.Equals(receiptOverride.ActionType, ReceiptInventoryOverrideActionTypes.InventoryReclassification, StringComparison.Ordinal))
        {
            var negative = operationAdjustments.Where(x => x.ChangeAmount < 0).Sum(x => -x.ChangeAmount);
            var positive = operationAdjustments.Where(x => x.ChangeAmount > 0).Sum(x => x.ChangeAmount);
            if (receiptOverride.InventoryDelta != 0 || negative == 0 || negative != positive
                || receiptOverride.CurrentInventoryBefore != receiptOverride.CurrentInventoryAfter)
            {
                add("ReceiptOverrideReclassificationMismatch", "Inventory reclassification must use exact paired old/new adjustments and preserve total inventory.");
            }
        }
        else if (string.Equals(receiptOverride.ActionType, ReceiptInventoryOverrideActionTypes.LocationCorrection, StringComparison.Ordinal))
        {
            var negative = operationAdjustments.Where(x => x.ChangeAmount < 0).Sum(x => -x.ChangeAmount);
            var positive = operationAdjustments.Where(x => x.ChangeAmount > 0).Sum(x => x.ChangeAmount);
            if (receiptOverride.InventoryDelta != 0 || negative != positive
                || (operationAdjustments.Count != 0 && (negative == 0 || operationAdjustments.Count != 2))
                || receiptOverride.CurrentInventoryBefore != receiptOverride.CurrentInventoryAfter)
                add("ReceiptOverrideLocationMismatch", "Receipt location correction must either preserve current custody or use one exact conserved room pair.");
        }
        else if (string.Equals(receiptOverride.ActionType, ReceiptInventoryOverrideActionTypes.VoidReceipt, StringComparison.Ordinal))
        {
            if (operationAdjustments.Any(x => x.ChangeAmount > 0)
                || receiptOverride.NewReceiptBinCount != 0
                || receiptOverride.CurrentInventoryAfter != receiptOverride.CurrentInventoryBefore + receiptOverride.InventoryDelta
                || string.IsNullOrWhiteSpace(receiptOverride.VoidConfirmationDetails))
            {
                add("ReceiptOverrideVoidMismatch", "Receipt void adjustments or confirmation do not match the persisted override.");
            }
        }
        else
        {
            add("UnknownReceiptOverrideAction", "Receipt administrator override action type is not recognized.");
        }
    }

    private static void ValidateCanonicalReceiptIdentity(
        ReceiptInventoryOverride operation, IReadOnlyCollection<RoomInventoryAdjustment> adjustments,
        InventoryIdentityCorrection? correction, InventoryCommandRecord? record,
        IReadOnlyCollection<TreatmentLineageMovement> movements, Action<string, string> add)
    {
        try
        {
            // Required constructor parameters prevent missing nested identity/quantity fields
            // from silently becoming CLR defaults. Never fall back to a legacy interpretation.
            var json = new JsonSerializerOptions(JsonSerializerDefaults.Web) { RespectRequiredConstructorParameters = true };
            if (correction is null || record is null
                || operation.ActionType != ReceiptInventoryOverrideActionTypes.InventoryReclassification
                || correction.ReceiptInventoryOverrideId != operation.Id || correction.CorrectedReceiptId != operation.ReceiptId
                || correction.OperationKey != operation.OperationKey || record.OperationKey != operation.OperationKey
                || correction.CreatedByUserId != operation.AdministratorUserId || record.ActorId != operation.AdministratorUserId
                || correction.CreatedAt != operation.CreatedAt || record.CommittedAt != operation.CreatedAt
                || correction.Reason != operation.Reason || record.ReversesOperationKey != null
                || adjustments.Any(x => x.InventoryInvariantVersion != InventoryLedgerKinds.CanonicalCommandInvariantVersion
                    || x.Source != "CanonicalInventory/v1" || x.InventoryIdentityCorrectionId != correction.Id
                    || x.ReceiptInventoryOverrideId != operation.Id || x.CreatedAt != operation.CreatedAt
                    || x.OldBinCount is null || x.NewBinCount != x.OldBinCount + x.ChangeAmount)
                || Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(record.IntentJson))) != record.IntentHash)
            {
                add("ReceiptOverrideIdentityMismatch", "Canonical receipt identity correction lacks matching durable parent and command evidence.");
                return;
            }
            var intent = JsonSerializer.Deserialize<InventoryCommand>(record.IntentJson, json)!;
            var result = JsonSerializer.Deserialize<InventoryCommandResult>(record.ResultJson, json)!;
            var change = intent.ReceiptIdentity;
            if (intent.Kind != InventoryCommandKind.CorrectReceiptIdentity || change is null
                || intent.OperationKey != operation.OperationKey || intent.ActorId != operation.AdministratorUserId
                || intent.Reason != operation.Reason || !intent.Lines.IsEmpty || change.ReceiptId != operation.ReceiptId
                // PostgreSQL stores microseconds; command JSON retains .NET's 100ns ticks.
                || adjustments.Any(x => x.AdjustmentAt.UtcTicks / 10 != intent.EffectiveAt.UtcTicks / 10 || x.Reason != intent.Reason)
                || result.Status != InventoryCommandStatus.Committed || result.OperationKey != operation.OperationKey
                || !result.Effects.SelectMany(x => x.LedgerIds).Order().SequenceEqual(adjustments.Select(x => x.Id).Order())
                || !result.Effects.SelectMany(x => x.MovementIds).Order().SequenceEqual(movements.Select(x => x.Id).Order())
                || movements.Count != correction.ExpectedTreatmentMovementCount)
            {
                add("ReceiptOverrideIdentityMismatch", "Canonical receipt identity correction intent/result does not identify its exact committed ledger and movement rows.");
                return;
            }
            using var beforeDoc = JsonDocument.Parse(operation.BeforeReceiptSnapshotJson);
            using var afterDoc = JsonDocument.Parse(operation.AfterReceiptSnapshotJson);
            using var sourceDoc = JsonDocument.Parse(correction.SourceIdentitySnapshotJson);
            var before = beforeDoc.RootElement;
            var after = afterDoc.RootElement;
            var target = JsonSerializer.Deserialize<InventoryIdentity>(correction.TargetIdentitySnapshotJson, json)!;
            var allocations = JsonSerializer.Deserialize<InventoryReceiptAllocation[]>(operation.AffectedInventorySnapshotJson, json)!;
            var sourceAllocations = sourceDoc.RootElement.GetProperty("allocations").Deserialize<InventoryReceiptAllocation[]>(json)!;
            var sourcePositions = sourceDoc.RootElement.GetProperty("positions").Deserialize<InventoryAvailabilityResult[]>(json)!;
            var expected = change.ExpectedReceipt;
            var valid = target.IsComplete && target == change.Target
                && target.CropYear == correction.TargetCropYear && target.GrowerLotId == correction.TargetGrowerLotId
                && target.FruitProfileId == correction.TargetFruitProfileId
                && sourceDoc.RootElement.GetProperty("receipt").GetString() == operation.BeforeReceiptSnapshotJson
                && JsonSerializer.Serialize(allocations, json) == JsonSerializer.Serialize(sourceAllocations, json)
                && before.GetProperty("id").GetInt64() == operation.ReceiptId && after.GetProperty("id").GetInt64() == operation.ReceiptId
                && before.GetProperty("cropYear").GetInt32() == correction.SourceCropYear && correction.SourceCropYear == expected.CropYear
                && before.GetProperty("growerLotId").GetInt32() == correction.SourceGrowerLotId && correction.SourceGrowerLotId == expected.GrowerLotId
                && before.GetProperty("fruitProfileId").GetInt32() == correction.SourceFruitProfileId && correction.SourceFruitProfileId == expected.FruitProfileId
                && after.GetProperty("cropYear").GetInt32() == target.CropYear && after.GetProperty("growerLotId").GetInt32() == target.GrowerLotId
                && after.GetProperty("fruitProfileId").GetInt32() == target.FruitProfileId && after.GetProperty("growerNumber").GetString() == target.GrowerNumber
                && before.GetProperty("binCount").GetInt32() == expected.Quantity && expected.Quantity == operation.OldReceiptBinCount
                && after.GetProperty("binCount").GetInt32() == expected.Quantity && expected.Quantity == operation.NewReceiptBinCount
                && before.GetProperty("concurrencyVersion").GetInt64() == change.ExpectedVersion
                && after.GetProperty("concurrencyVersion").GetInt64() == change.ExpectedVersion + 1
                && before.GetProperty("warehouseId").GetInt32() == expected.WarehouseId && after.GetProperty("warehouseId").GetInt32() == expected.WarehouseId
                && before.GetProperty("roomId").GetInt32() == expected.RoomId && after.GetProperty("roomId").GetInt32() == expected.RoomId
                && before.GetProperty("compuTechReceiptId").GetString() == expected.ReceiptNumber && after.GetProperty("compuTechReceiptId").GetString() == expected.ReceiptNumber
                && expected.ReceiptType == "Truck receipt" && before.GetProperty("receiptType").GetString() == expected.ReceiptType
                && after.GetProperty("receiptType").GetString() == expected.ReceiptType
                && !before.GetProperty("isDeleted").GetBoolean() && !after.GetProperty("isDeleted").GetBoolean()
                && after.GetProperty("lotCode").GetString() == target.Lot
                && sourcePositions.Select(x => x.PositionKey).Distinct().Count() == sourcePositions.Length
                && allocations.Select(x => x.Key).Distinct().Count() == allocations.Length
                && allocations.Sum(x => x.Slice.Quantity) == operation.CurrentInventoryBefore;
            foreach (var allocation in allocations)
            {
                var p = allocation.Position;
                var positionEvidence = sourcePositions.Where(x => x.PositionKey == p.PositionKey).ToArray();
                var treatmentEvidence = p.TreatmentSlices.Where(x => x.Signature == allocation.Slice.Signature
                    && x.State == allocation.Slice.State && x.ReceiptEvidenceIds.SequenceEqual(new[] { operation.ReceiptId })).ToArray();
                valid &= p.Identity.IsComplete && p.Identity.CropYear == correction.SourceCropYear
                    && p.Identity.GrowerLotId == correction.SourceGrowerLotId && p.Identity.FruitProfileId == correction.SourceFruitProfileId
                    && p.Identity.Lot == before.GetProperty("growerNumber").GetString()
                    && p.IsOperable && p.QuantityConfidence == InventoryConfidence.Proven && p.TreatmentConfidence == InventoryConfidence.Proven
                    && p.ReceiptProvenance.Confidence == InventoryConfidence.Proven
                    && positionEvidence.Length == 1 && JsonSerializer.Serialize(positionEvidence[0], json) == JsonSerializer.Serialize(p, json)
                    && treatmentEvidence.Sum(x => x.Quantity) == allocation.Slice.Quantity
                    && treatmentEvidence.All(x => x.ApplicationIds.Order().SequenceEqual(allocation.Slice.ApplicationIds.Order()))
                    && treatmentEvidence.SelectMany(x => x.ProjectionIds).Distinct().Order().SequenceEqual(allocation.Slice.ProjectionIds.Order())
                    && allocation.Slice.Quantity > 0 && allocation.Slice.Quantity <= p.AuthoritativeQuantity
                    && allocation.Slice.Confidence == InventoryConfidence.Proven
                    && allocation.Slice.ReceiptEvidenceIds.SequenceEqual(new[] { operation.ReceiptId });
            }
            var groups = allocations.Where(x => x.Position.Location.Custody == InventoryCustody.Room).GroupBy(x => x.Position.PositionKey).ToArray();
            valid &= adjustments.Count == groups.Length * 2 && result.Effects.Length == groups.Length;
            foreach (var group in groups)
            {
                var p = group.First().Position;
                var quantity = group.Sum(x => x.Slice.Quantity);
                var source = new OverrideIdentity(p.Location.WarehouseId, p.Location.RoomId!.Value, p.Identity.CropYear,
                    p.Identity.GrowerLotId, p.Identity.FruitProfileId, p.Identity.Lot, p.Identity.Variety, p.Identity.Status);
                var destination = new OverrideIdentity(source.WarehouseId, source.RoomId, target.CropYear,
                    target.GrowerLotId, target.FruitProfileId, target.Lot, target.Variety,
                    InventoryStatusIdentity.Normalize(p.Identity.Status, p.Identity.ProductionType));
                var outgoing = adjustments.Where(x => x.ChangeAmount == -quantity && Matches(x, source, true)).ToArray();
                var incoming = adjustments.Where(x => x.ChangeAmount == quantity && Matches(x, destination, true)).ToArray();
                var effects = result.Effects.Where(x => x.PositionKey == p.PositionKey).ToArray();
                if (outgoing.Length != 1 || incoming.Length != 1 || effects.Length != 1) { valid = false; continue; }
                var effect = effects[0];
                var part = $"{operation.OperationKey}:identity:{result.Effects.IndexOf(effect)}";
                valid &= quantity <= p.AuthoritativeQuantity && effect.ParentId == operation.ReceiptId && effect.Quantity == quantity
                    && effect.Before == p.AuthoritativeQuantity && effect.After == p.AuthoritativeQuantity - quantity
                    && outgoing[0].OldBinCount == effect.Before && outgoing[0].NewBinCount == effect.After
                    && outgoing[0].InventoryOperationKey == part + ":out" && incoming[0].InventoryOperationKey == part + ":in"
                    && effect.LedgerIds.SequenceEqual(new[] { outgoing[0].Id, incoming[0].Id });
                var moves = movements.Where(x => effect.MovementIds.Contains(x.Id)).ToArray();
                valid &= moves.All(x => x.ReceiptId == operation.ReceiptId && x.CreatedByUserId == operation.AdministratorUserId
                    && x.CreatedAt == operation.CreatedAt && x.OccurredAt.UtcTicks / 10 == intent.EffectiveAt.UtcTicks / 10 && x.BinCount > 0
                    && x.ReversesTreatmentLineageMovementId == null);
                foreach (var slice in group)
                {
                    var outs = moves.Where(x => x.MovementType == "InventoryIdentityCorrectionOut" && x.TreatmentSignatureSnapshot == slice.Slice.Signature).ToArray();
                    var ins = moves.Where(x => x.MovementType == "InventoryIdentityCorrectionIn" && x.TreatmentSignatureSnapshot == slice.Slice.Signature).ToArray();
                    valid &= outs.Sum(x => x.BinCount) == slice.Slice.Quantity && ins.Sum(x => x.BinCount) == slice.Slice.Quantity
                        && outs.All(x => x.SourceRoomId == source.RoomId && x.DestinationRoomId == null && x.TreatmentStateSnapshot == slice.Slice.State
                            && x.IdentityKey == p.Identity.Key && x.SourceSegmentId != null && x.DestinationSegmentId == null)
                        && ins.All(x => x.DestinationRoomId == source.RoomId && x.SourceRoomId == null && x.TreatmentStateSnapshot == slice.Slice.State
                            && x.IdentityKey == (target with { Status = destination.InventoryStatus! }).Key && x.DestinationSegmentId != null && x.SourceSegmentId == null);
                }
                valid &= moves.Sum(x => x.BinCount) == quantity * 2;
            }
            if (!valid) add("ReceiptOverrideRoomLotMismatch", "Canonical receipt identity correction does not reconcile its exact receipt allocations, target, ledger and movement evidence.");
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentException or NullReferenceException or OverflowException)
        {
            add("ReceiptOverrideSnapshotInvalid", "Canonical receipt identity correction evidence is unreadable or incomplete.");
        }
    }

    private static void ValidateReceiptOverrideInventoryIdentity(
        ReceiptInventoryOverride receiptOverride,
        IReadOnlyCollection<RoomInventoryAdjustment> adjustments,
        Action<string, string> add)
    {
        try
        {
            using var affectedDocument = JsonDocument.Parse(receiptOverride.AffectedInventorySnapshotJson);
            using var afterDocument = JsonDocument.Parse(receiptOverride.AfterReceiptSnapshotJson);
            var canonical = adjustments.Count > 0 && adjustments.All(x => x.InventoryInvariantVersion == 3);
            var root = affectedDocument.RootElement;
            var entries = canonical && root.ValueKind == JsonValueKind.Object ? root.GetProperty("allocations") : root;
            var affected = entries.EnumerateArray().Select(x => ReadOverrideIdentity(x, canonical))
                .Where(x => x != null).Select(x => x!).ToList();
            var after = afterDocument.RootElement;
            var afterIdentity = new OverrideIdentity(
                after.GetProperty("warehouseId").GetInt32(),
                after.GetProperty("roomId").GetInt32(),
                after.GetProperty("cropYear").GetInt32(),
                NullableInt(after, "growerLotId"),
                after.GetProperty("fruitProfileId").GetInt32(),
                after.GetProperty("growerNumber").GetString(),
                null,
                null);

            foreach (var adjustment in adjustments)
            {
                var matchesAffected = affected.Any(x => Matches(adjustment, x, compareVarietyAndStatus: true));
                var matchesAfter = Matches(adjustment, afterIdentity, compareVarietyAndStatus: false);
                var matchesReclassificationTarget = affected.Any(x =>
                    x.WarehouseId == adjustment.WarehouseId && x.RoomId == adjustment.RoomId)
                    && MatchesIdentity(adjustment, afterIdentity);
                var valid = receiptOverride.ActionType switch
                {
                    ReceiptInventoryOverrideActionTypes.InventoryReclassification when adjustment.ChangeAmount < 0 => matchesAffected,
                    ReceiptInventoryOverrideActionTypes.InventoryReclassification =>
                        adjustment.InventoryIdentityCorrectionId is not null || adjustment.InventoryIdentityCorrection is not null
                            ? matchesReclassificationTarget
                            : matchesAfter,
                    ReceiptInventoryOverrideActionTypes.LocationCorrection when adjustment.ChangeAmount < 0 => matchesAffected,
                    ReceiptInventoryOverrideActionTypes.LocationCorrection => matchesAfter,
                    ReceiptInventoryOverrideActionTypes.VoidReceipt => matchesAffected,
                    ReceiptInventoryOverrideActionTypes.QuantityCorrection when adjustment.ChangeAmount > 0 => matchesAffected || matchesAfter,
                    ReceiptInventoryOverrideActionTypes.QuantityCorrection => matchesAffected || matchesAfter,
                    _ => false
                };
                if (!valid)
                {
                    add("ReceiptOverrideRoomLotMismatch", "Receipt administrator override adjustment room, lot, or receipt identity does not match its reviewed before/after inventory snapshot.");
                    return;
                }
            }
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or KeyNotFoundException)
        {
            add("ReceiptOverrideSnapshotInvalid", "Receipt administrator override inventory identity snapshot is unreadable or incomplete.");
        }
    }

    private static void ValidateReceiptCustodyLedger(InterCrewTransfer transfer,
        IReadOnlyCollection<RoomInventoryAdjustment> rows, Action<string, string> add)
    {
        var acks = transfer.CustodyAcknowledgments;
        var placements = acks.SelectMany(x => x.Placements).ToArray();
        var acknowledged = acks.Sum(x => x.NetQuantity);
        var placed = acks.Sum(x => x.PlacedQuantity);
        var reversals = acks.SelectMany(x => x.Reversals).Where(x => x.PlacementId != null).ToArray();
        var source = rows.Where(x => x.AdjustmentType is InterCrewTransferAdjustmentTypes.Dispatch or TruckReceiptReconciliationService.ReturnToSource).ToArray();
        var destination = rows.Except(source).ToArray();
        var valid = acknowledged >= 0 && acknowledged <= transfer.BinsLoaded && transfer.BinsReceived == acknowledged
            && transfer.VarianceBins == acknowledged - transfer.BinsLoaded && placed <= acknowledged
            && source.Sum(x => x.ChangeAmount) == -transfer.BinsLoaded
            && source.All(x => x.WarehouseId == transfer.SourceWarehouseId && x.RoomId == transfer.SourceRoomId)
            && acks.All(x => x.Quantity > 0 && x.ReceiptId == transfer.ReceivingReceiptId && x.InterCrewTransferId == transfer.Id
                && x.DispatchMovement.InterCrewTransferId == transfer.Id && x.DispatchMovement.MovementType == "InterCrewDispatch"
                && ReceiptCustodyProof.Valid(x))
            && acks.GroupBy(x => x.DispatchMovementId).All(g => g.Sum(x => x.NetQuantity) <= g.First().DispatchMovement.BinCount)
            && destination.All(x => x.AdjustmentType == InterCrewTransferAdjustmentTypes.Receive
                ? placements.Count(p => p.InventoryAdjustmentId == x.Id && p.Quantity == x.ChangeAmount) == 1
                : x.AdjustmentType == "ReceiptPlacementReversal" && reversals.Count(r => r.InventoryAdjustmentId == x.Id && -r.Quantity == x.ChangeAmount) == 1)
            && destination.Sum(x => x.ChangeAmount) == placed
            && placements.All(p => p.Quantity > 0 && destination.Any(x => x.Id == p.InventoryAdjustmentId && x.ChangeAmount == p.Quantity)
                && p.Movement.InterCrewTransferId == transfer.Id && p.Movement.MovementType == "InterCrewReceive"
                && p.Movement.BinCount == p.Quantity && p.Movement.SourceSegmentId == p.Acknowledgment.DispatchMovement.SourceSegmentId)
            && rows.All(x => x.OldBinCount != null && x.NewBinCount == x.OldBinCount + x.ChangeAmount)
            && (transfer.Status == InterCrewTransferStatuses.Received
                ? placed == transfer.BinsLoaded && acknowledged == transfer.BinsLoaded
                : transfer.Status == InterCrewTransferStatuses.InTransit && placed < transfer.BinsLoaded);
        if (!valid) add("ReceiptCustodyConservationMismatch", "Dispatch, receipt-held, placed and unresolved allocations do not conserve the original load.");
    }

    private static bool ValidOverrideAdjustmentType(RoomInventoryAdjustment row, string action) =>
        row.AdjustmentType == ReceiptInventoryOverrideService.AdjustmentType
        || row.InventoryInvariantVersion == 3 && (action switch
        {
            ReceiptInventoryOverrideActionTypes.InventoryReclassification => row.AdjustmentType == "InventoryIdentityCorrection"
                && (row.InventoryIdentityCorrectionId != null || row.InventoryIdentityCorrection != null),
            ReceiptInventoryOverrideActionTypes.LocationCorrection => row.ChangeAmount < 0
                ? row.AdjustmentType == "CorrectOriginalRoomOut" : row.AdjustmentType == "CorrectOriginalRoomIn",
            _ => false
        });

    private static OverrideIdentity? ReadOverrideIdentity(JsonElement entry, bool canonical)
    {
        if (canonical && entry.TryGetProperty("position", out var position)) entry = position;
        if (canonical && entry.TryGetProperty("location", out var location))
        {
            // External custody is retained in the audit but does not authorize a room ledger side.
            if (location.GetProperty("roomId").ValueKind == JsonValueKind.Null) return null;
            var identity = entry.GetProperty("identity");
            return new(location.GetProperty("warehouseId").GetInt32(), location.GetProperty("roomId").GetInt32(),
                NullableInt(identity, "cropYear"), NullableInt(identity, "growerLotId"), NullableInt(identity, "fruitProfileId"),
                identity.GetProperty("lot").GetString(), identity.GetProperty("variety").GetString(), identity.GetProperty("status").GetString());
        }
        return new(entry.GetProperty("warehouseId").GetInt32(), entry.GetProperty("roomId").GetInt32(),
            NullableInt(entry, "cropYear"), NullableInt(entry, "growerLotId"), NullableInt(entry, "fruitProfileId"),
            entry.GetProperty("lot").GetString(), entry.GetProperty("variety").GetString(), entry.GetProperty("inventoryStatus").GetString());
    }

    private static int? NullableInt(JsonElement element, string propertyName)
    {
        var property = element.GetProperty(propertyName);
        return property.ValueKind == JsonValueKind.Null ? null : property.GetInt32();
    }

    private static bool Matches(RoomInventoryAdjustment adjustment, OverrideIdentity identity, bool compareVarietyAndStatus) =>
        adjustment.WarehouseId == identity.WarehouseId
        && adjustment.RoomId == identity.RoomId
        && MatchesIdentity(adjustment, identity)
        && (!compareVarietyAndStatus
            || (Same(adjustment.VarietyCode, identity.Variety)
                && Same(adjustment.InventoryStatus, identity.InventoryStatus)));

    private static bool MatchesIdentity(RoomInventoryAdjustment adjustment, OverrideIdentity identity) =>
        adjustment.CropYear == identity.CropYear
        && adjustment.GrowerLotId == identity.GrowerLotId
        && adjustment.FruitProfileId == identity.FruitProfileId
        && Same(adjustment.LotNumber, identity.Lot);

    private sealed record OverrideIdentity(
        int WarehouseId,
        int RoomId,
        int? CropYear,
        int? GrowerLotId,
        int? FruitProfileId,
        string? Lot,
        string? Variety,
        string? InventoryStatus);

    private static bool Same(string? left, string? right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
