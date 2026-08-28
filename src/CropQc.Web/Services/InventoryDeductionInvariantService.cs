using CropQc.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;
using System.Runtime.CompilerServices;
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
            .Where(x => x.State != EntityState.Deleted && x.Entity.InventoryInvariantVersion >= CurrentVersion)
            .Select(x => x.Entity)
            .Distinct()
            .ToList();
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
            .Where(x => x.ChangeAmount >= 0 && x.InventoryInvariantVersion >= CurrentVersion)
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

        var interCrewTransferIds = adjustments.Where(x => x.InterCrewTransferId is not null).Select(x => x.InterCrewTransferId!.Value).Distinct().ToList();
        var persistedInterCrewTransfers = interCrewTransferIds.Count == 0 ? [] : await dbContext.InterCrewTransfers.AsNoTracking()
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
            var parentCount = binsParents.Count + (transfer is null ? 0 : 1) + (receiptOverride is null ? 0 : 1) + (loss is null ? 0 : 1) + (processorLine is null ? 0 : 1) + (outsideTransfer is null ? 0 : 1) + (interCrewTransfer is null ? 0 : 1);
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
                ValidateReceiptOverride(adjustment, receiptOverride, overrideAdjustments, Add);
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
                ValidateOutsideWarehouseTransfer(adjustment, outsideTransfer, outsideAdjustments, Add);
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
                ValidateInterCrewTransfer(adjustment, interCrewTransfer, operationAdjustments, Add);
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
            if (side.WarehouseId != transfer.SourceWarehouseId
                || side.RoomId != transfer.SourceRoomId
                || side.ReceiptId != transfer.ReceiptId
                || side.CropYear != transfer.CropYear
                || side.GrowerLotId != transfer.GrowerLotId
                || side.FruitProfileId != transfer.FruitProfileId
                || !Same(side.GrowerName, transfer.GrowerNameSnapshot)
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
        if (adjustment.OutsideWarehouseTransferId is null && adjustment.OutsideWarehouseTransfer is null)
        {
            add("MissingOutsideWarehouseTransferLink", "Outside Warehouse Transfer adjustment is not linked by a persisted transfer ID.");
        }
    }

    private static void ValidateInterCrewTransfer(
        RoomInventoryAdjustment adjustment, InterCrewTransfer transfer,
        IReadOnlyCollection<RoomInventoryAdjustment> operationAdjustments, Action<string, string> add)
    {
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
            if (side.WarehouseId != expectedWarehouse || side.RoomId != expectedRoom || side.CropYear != transfer.CropYear
                || side.GrowerLotId != transfer.GrowerLotId || side.FruitProfileId != transfer.FruitProfileId
                || !Same(side.LotNumber, transfer.LotNumberSnapshot) || !Same(side.VarietyCode, transfer.VarietyCodeSnapshot)
                || !Same(side.InventoryStatus, transfer.InventoryStatusSnapshot))
                add("InterCrewIdentityMismatch", "Inter-crew ledger identity does not match its durable transfer snapshot.");
            if (side.OldBinCount is null || side.NewBinCount != side.OldBinCount + side.ChangeAmount)
                add("InterCrewBalanceMismatch", "Inter-crew before/after balance does not reconcile with its quantity.");
        }
        if (adjustment.InterCrewTransferId is null && adjustment.InterCrewTransfer is null)
            add("MissingInterCrewTransferLink", "Inter-crew adjustment is not linked by a persisted transfer ID.");
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
        if (operationAdjustments.Any(x => x.ReceiptId != receiptOverride.ReceiptId
            || x.CreatedByUserId != receiptOverride.AdministratorUserId
            || !string.Equals(x.AdjustmentType, ReceiptInventoryOverrideService.AdjustmentType, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(x.LotNumber)
            || x.FruitProfileId is null))
        {
            add("ReceiptOverrideIdentityMismatch", "Receipt administrator override adjustment receipt, administrator, or inventory identity does not match.");
        }
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

    private static void ValidateReceiptOverrideInventoryIdentity(
        ReceiptInventoryOverride receiptOverride,
        IReadOnlyCollection<RoomInventoryAdjustment> adjustments,
        Action<string, string> add)
    {
        try
        {
            using var affectedDocument = JsonDocument.Parse(receiptOverride.AffectedInventorySnapshotJson);
            using var afterDocument = JsonDocument.Parse(receiptOverride.AfterReceiptSnapshotJson);
            var affected = affectedDocument.RootElement.EnumerateArray().Select(x => new OverrideIdentity(
                x.GetProperty("warehouseId").GetInt32(),
                x.GetProperty("roomId").GetInt32(),
                NullableInt(x, "cropYear"),
                NullableInt(x, "growerLotId"),
                NullableInt(x, "fruitProfileId"),
                x.GetProperty("lot").GetString(),
                x.GetProperty("variety").GetString(),
                x.GetProperty("inventoryStatus").GetString())).ToList();
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
                var valid = receiptOverride.ActionType switch
                {
                    ReceiptInventoryOverrideActionTypes.InventoryReclassification when adjustment.ChangeAmount < 0 => matchesAffected,
                    ReceiptInventoryOverrideActionTypes.InventoryReclassification => matchesAfter,
                    ReceiptInventoryOverrideActionTypes.VoidReceipt => matchesAffected,
                    ReceiptInventoryOverrideActionTypes.QuantityCorrection when adjustment.ChangeAmount > 0 => matchesAfter,
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

    private static int? NullableInt(JsonElement element, string propertyName)
    {
        var property = element.GetProperty(propertyName);
        return property.ValueKind == JsonValueKind.Null ? null : property.GetInt32();
    }

    private static bool Matches(RoomInventoryAdjustment adjustment, OverrideIdentity identity, bool compareVarietyAndStatus) =>
        adjustment.WarehouseId == identity.WarehouseId
        && adjustment.RoomId == identity.RoomId
        && adjustment.CropYear == identity.CropYear
        && adjustment.GrowerLotId == identity.GrowerLotId
        && adjustment.FruitProfileId == identity.FruitProfileId
        && Same(adjustment.LotNumber, identity.Lot)
        && (!compareVarietyAndStatus
            || (Same(adjustment.VarietyCode, identity.Variety)
                && Same(adjustment.InventoryStatus, identity.InventoryStatus)));

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
