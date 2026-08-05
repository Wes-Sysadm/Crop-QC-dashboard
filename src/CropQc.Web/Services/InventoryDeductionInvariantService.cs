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
            $"Inventory was not changed because its required Bins Run, Transfer, or Receipt Admin Override relationship is invalid ({blocking.Code}).");
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
            var parentCount = binsParents.Count + (transfer is null ? 0 : 1) + (receiptOverride is null ? 0 : 1);
            var blocks = adjustment.InventoryInvariantVersion >= CurrentVersion;

            if (adjustment.ChangeAmount < 0 && parentCount == 0)
            {
                Add("NoParent", "Negative adjustment has no persisted Bins Run, Transfer, or Receipt Admin Override parent.");
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
