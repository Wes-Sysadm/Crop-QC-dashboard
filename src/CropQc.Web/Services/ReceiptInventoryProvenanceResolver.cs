using CropQc.Data;
using CropQc.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public sealed record ReceiptOperationalCounts(
    int BinsRuns,
    int ActualRuns,
    int Transfers,
    int InterCrewCustody = 0,
    int OutsideWarehouseCustody = 0);
public static class ReceiptInventoryProvenanceClassifications
{
    public const string ExactCurrent = "ExactCurrent";
    public const string ExactMoved = "ExactMoved";
    public const string BlendedCurrent = "BlendedCurrent";
    public const string FullyConsumed = "FullyConsumed";
    public const string SupersededByCorrection = "SupersededByCorrection";
    public const string HistoricalOnly = "HistoricalOnly";
    public const string TransferCustody = "TransferCustody";
    public const string NeedsReconciliation = "NeedsReconciliation";
}

public sealed record ReceiptInventoryProvenance(
    string Classification,
    ReceiptOperationalCounts Counts,
    bool IsExact,
    string? UnavailableReason = null);

public interface IReceiptInventoryProvenanceResolver
{
    Task<ReceiptOperationalCounts> GetOperationalCountsAsync(long receiptId, CancellationToken cancellationToken);
    Task<ReceiptInventoryProvenance> ResolveAsync(long receiptId, CancellationToken cancellationToken);
}

/// <summary>
/// Resolves activity attributable to one Receipt from durable parent/source links.
/// Canonical identity equality alone is deliberately insufficient: fruit from a
/// different Receipt may share the same Grower Lot and Fruit Profile.
/// </summary>
public sealed class ReceiptInventoryProvenanceResolver(CropQcDbContext dbContext)
    : IReceiptInventoryProvenanceResolver
{
    public async Task<ReceiptOperationalCounts> GetOperationalCountsAsync(
        long receiptId,
        CancellationToken cancellationToken) =>
        (await ResolveAsync(receiptId, cancellationToken)).Counts;

    public async Task<ReceiptInventoryProvenance> ResolveAsync(
        long receiptId,
        CancellationToken cancellationToken)
    {
        var binsRunRows = dbContext.BinsRunEntries.AsNoTracking().Where(x =>
            x.ReceiptId == receiptId
            || (x.SourceInventoryAdjustment != null && x.SourceInventoryAdjustment.ReceiptId == receiptId)
            || (x.InventoryAdjustment != null && x.InventoryAdjustment.ReceiptId == receiptId));
        var binsRuns = await binsRunRows.CountAsync(cancellationToken);
        var actualRuns = await binsRunRows
            .Where(x => x.ActualRunId != null)
            .Select(x => x.ActualRunId)
            .Distinct()
            .CountAsync(cancellationToken);

        var treatmentTransfers = await dbContext.TreatmentLineageMovements.AsNoTracking()
            .Where(x => x.RoomTransferId != null
                && (x.ReceiptId == receiptId
                    || (x.SourceSegment != null && x.SourceSegment.ReceiptId == receiptId)
                    || (x.DestinationSegment != null && x.DestinationSegment.ReceiptId == receiptId)))
            .Select(x => x.RoomTransferId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken);
        var transferAdjustments = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => x.ReceiptId == receiptId && x.RoomTransferId != null)
            .Select(x => new { TransferId = x.RoomTransferId!.Value, x.AdjustmentType, x.ChangeAmount })
            .ToListAsync(cancellationToken);
        var adjustmentTransfers = transferAdjustments.Select(x => x.TransferId).Distinct().ToList();
        var transfers = treatmentTransfers.Concat(adjustmentTransfers).Distinct().Count();
        var interCrewCustody = await dbContext.InterCrewTransfers.AsNoTracking().CountAsync(x =>
            x.Status == InterCrewTransferStatuses.InTransit
            && (x.ReceiptId == receiptId
                || (x.SourceInventoryAdjustment != null && x.SourceInventoryAdjustment.ReceiptId == receiptId)),
            cancellationToken);
        var outsideWarehouseCustody = await dbContext.OutsideWarehouseTransfers.AsNoTracking().CountAsync(x =>
            !x.IsReversed
            && (x.ReceiptId == receiptId
                || (x.SourceInventoryAdjustment != null && x.SourceInventoryAdjustment.ReceiptId == receiptId)),
            cancellationToken);
        var counts = new ReceiptOperationalCounts(
            binsRuns, actualRuns, transfers, interCrewCustody, outsideWarehouseCustody);

        var incompleteTransfer = transferAdjustments
            .GroupBy(x => x.TransferId)
            .Any(x => x.Count(y => y.AdjustmentType == "TransferOut" && y.ChangeAmount < 0) != 1
                || x.Count(y => y.AdjustmentType == "TransferIn" && y.ChangeAmount > 0) != 1
                || x.Sum(y => y.ChangeAmount) != 0);
        if (incompleteTransfer)
            return new(ReceiptInventoryProvenanceClassifications.NeedsReconciliation, counts, false,
                "Receipt transfer lineage is incomplete and must be reconciled before changing quantity or voiding the Receipt.");
        if (interCrewCustody > 0 || outsideWarehouseCustody > 0)
            return new(ReceiptInventoryProvenanceClassifications.TransferCustody, counts, false,
                $"Receipt inventory is in unsupported transfer custody ({interCrewCustody} inter-crew, {outsideWarehouseCustody} outside-warehouse). Reconcile or complete custody before changing quantity or voiding the Receipt.");

        var current = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => x.ReceiptId == receiptId)
            .SumAsync(x => (int?)x.ChangeAmount, cancellationToken) ?? 0;
        var hasHistory = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .AnyAsync(x => x.ReceiptId == receiptId, cancellationToken);
        if (!hasHistory)
            return new(ReceiptInventoryProvenanceClassifications.HistoricalOnly, counts, false,
                "No receipt-linked inventory history exists.");
        if (current <= 0)
            return new(ReceiptInventoryProvenanceClassifications.FullyConsumed, counts, true);
        if (await dbContext.InventoryIdentityCorrections.AsNoTracking().AnyAsync(x =>
            x.CorrectedReceiptId == receiptId && x.IsActive && x.IsComplete, cancellationToken))
            return new(ReceiptInventoryProvenanceClassifications.SupersededByCorrection, counts, true);
        if (transfers > 0 || binsRuns > 0 || actualRuns > 0)
            return new(ReceiptInventoryProvenanceClassifications.ExactMoved, counts, true);

        return new(ReceiptInventoryProvenanceClassifications.ExactCurrent, counts, true);
    }
}
