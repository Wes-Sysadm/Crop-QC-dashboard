using System.Data;
using System.Security.Claims;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CropQc.Web.Services;

public interface IRoomInventoryLossService
{
    Task<RoomInventoryLossPageData> GetRoomDataAsync(int roomId, CancellationToken cancellationToken);
    Task<IReadOnlyList<RoomInventoryLossHistoryViewModel>> GetReceiptHistoryAsync(long receiptId, CancellationToken cancellationToken);
    Task<string?> CreateAsync(RoomInventoryLossForm form, CancellationToken cancellationToken);
    Task<string?> ReverseAsync(ReverseRoomInventoryLossForm form, CancellationToken cancellationToken);
    Task<RoomInventoryLossWriteResult> CreateReviewedCorrectionAsync(
        RoomInventoryLossCreateRequest request,
        int actorUserId,
        CancellationToken cancellationToken);
    Task<RoomInventoryLossWriteResult> NormalizeReviewedMalformedAdjustmentAsync(
        RoomInventoryLossNormalizationRequest request,
        int actorUserId,
        CancellationToken cancellationToken);
}

public sealed record RoomInventoryLossPageData(
    IReadOnlyList<RoomInventoryLossOptionViewModel> Options,
    IReadOnlyList<RoomInventoryLossHistoryViewModel> History,
    bool CanRecord,
    bool CanReverse);

public sealed record RoomInventoryLossCreateRequest(
    string OperationKey,
    int RoomId,
    long InventoryAdjustmentId,
    int ExpectedCurrentBins,
    int BinCount,
    DateTimeOffset? OccurredAt,
    string Reason,
    string? Notes,
    long? RequiredReceiptId,
    string AuditSource,
    string? TreatmentSignature = null,
    long? TreatmentSegmentId = null);

public sealed record RoomInventoryLossNormalizationRequest(
    string OperationKey,
    long ExistingAdjustmentId,
    int ExpectedPersistedCurrentBins,
    int CanonicalBalanceBeforeAdjustment,
    int BinCount,
    long RequiredReceiptId,
    string Reason,
    string? Notes,
    string AuditSource,
    RoomInventoryLossNormalizationAuditContext AuditContext);

public sealed record RoomInventoryLossNormalizationAuditContext(
    string ReceiptReference,
    long ReceiptId,
    int ReceivedBins,
    int DroppedBins,
    int CanonicalBalanceBeforeAdjustment,
    int CorrectedCurrentPackableBins,
    long MalformedAdjustmentId,
    string RootCause,
    string BusinessEvent);

public sealed record RoomInventoryLossNormalizationAuditEvidence(
    string ReceiptReference,
    long ReceiptId,
    int ReceivedBins,
    int DroppedBins,
    int CanonicalBalanceBeforeAdjustment,
    int CorrectedCurrentPackableBins,
    long MalformedAdjustmentId,
    string OriginalAdjustmentType,
    int? OriginalOldBinCount,
    int OriginalChangeAmount,
    int OriginalNewBinCount,
    string CorrectedAdjustmentType,
    int CorrectedOldBinCount,
    int CorrectedChangeAmount,
    int CorrectedNewBinCount,
    long LossParentId,
    string LossOperationKey,
    string RootCause,
    string BusinessEvent,
    bool ReceiptQuantityWasNotChanged,
    bool OriginalAuditWasRetained,
    DateTimeOffset CorrectedAt,
    string CorrectedBy);

public sealed record RoomInventoryLossWriteResult(
    bool Success,
    bool AlreadyApplied,
    long? LossId,
    string? Error);

public static class RoomInventoryLossAdjustmentTypes
{
    public const string DroppedBins = "DroppedBins";
    public const string DroppedBinsReversal = "DroppedBinsReversal";
}

public sealed class RoomInventoryLossService(
    CropQcDbContext dbContext,
    IRoomInventoryLedgerQueryService ledgerQuery,
    IInventoryDeductionInvariantService inventoryInvariant,
    IUserAccessService userAccessService,
    ICanonicalGrowerService canonicalGrowerService,
    IHttpContextAccessor httpContextAccessor,
    IBusinessTimeService businessTime,
    ILogger<RoomInventoryLossService> logger,
    IRoomTreatmentService? roomTreatmentService = null) : IRoomInventoryLossService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<RoomInventoryLossPageData> GetRoomDataAsync(int roomId, CancellationToken cancellationToken)
    {
        var snapshots = await ledgerQuery.GetSnapshotsAsync(null, [roomId], cancellationToken);
        var growerResolver = await canonicalGrowerService.LoadResolutionSetAsync(cancellationToken);
        var options = new List<RoomInventoryLossOptionViewModel>();
        var activeSnapshots = snapshots.Where(x => x.CurrentBins > 0).ToList();
        var treatmentSelections = roomTreatmentService is null
            ? null
            : await roomTreatmentService.GetSelectionsAsync(activeSnapshots, cancellationToken);
        foreach (var snapshot in activeSnapshots
                     .OrderBy(x => x.GrowerNumber ?? x.Grower).ThenBy(x => x.Lot).ThenBy(x => x.ProductionType).ThenBy(x => x.Variety))
        {
            var segments = roomTreatmentService is null
                ? [new TreatmentSegmentSelection(RoomTreatmentService.IdentityKey(snapshot), "", TreatmentLineageStates.Untreated, snapshot.CurrentBins, "Untreated")]
                : treatmentSelections![RoomTreatmentService.SelectionLookupKey(snapshot)];
            foreach (var segment in segments)
            {
                options.Add(new RoomInventoryLossOptionViewModel(
                    snapshot.LatestAdjustmentId,
                    $"{growerResolver.DisplayName(snapshot.Grower, snapshot.GrowerNumber)} / {snapshot.GrowerNumber ?? snapshot.Lot} / {snapshot.VarietyName} / {OrganicLabel(snapshot)} / {segment.Label} ({segment.CurrentBins} packable bins)",
                    snapshot.Facility,
                    snapshot.Room,
                    growerResolver.DisplayName(snapshot.Grower, snapshot.GrowerNumber),
                    snapshot.Lot,
                    snapshot.VarietyName,
                    snapshot.ProductionType,
                    snapshot.IsOrganic,
                    segment.CurrentBins,
                    segment.TreatmentSignature,
                    segment.Label,
                    segment.SegmentId));
            }
        }
        var principal = httpContextAccessor.HttpContext?.User;
        var canRecord = principal is not null
            && await userAccessService.HasAccessAsync(principal, ApplicationAreas.RoomTransactions, PageAccessLevel.Edit, cancellationToken);
        var canReverse = principal is not null
            && await userAccessService.HasAccessAsync(principal, ApplicationAreas.RoomTransactions, PageAccessLevel.Admin, cancellationToken);
        var history = await ProjectHistoryAsync(
            dbContext.RoomInventoryLosses.AsNoTracking().Where(x => x.RoomId == roomId),
            growerResolver,
            cancellationToken);
        return new(options, history, canRecord, canReverse);
    }

    public async Task<IReadOnlyList<RoomInventoryLossHistoryViewModel>> GetReceiptHistoryAsync(
        long receiptId,
        CancellationToken cancellationToken)
    {
        var growerResolver = await canonicalGrowerService.LoadResolutionSetAsync(cancellationToken);
        return await ProjectHistoryAsync(
            dbContext.RoomInventoryLosses.AsNoTracking().Where(x => x.ReceiptId == receiptId),
            growerResolver,
            cancellationToken);
    }

    public async Task<string?> CreateAsync(RoomInventoryLossForm form, CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal is null
            || !await userAccessService.HasAccessAsync(principal, ApplicationAreas.RoomTransactions, PageAccessLevel.Edit, cancellationToken))
        {
            return "Room Transactions Edit access is required to mark bins dropped.";
        }

        var actor = await GetCurrentUserAsync(cancellationToken);
        if (actor is null)
        {
            return "The active user record could not be resolved.";
        }

        var result = await CreateCoreAsync(
            new RoomInventoryLossCreateRequest(
                form.OperationKey,
                form.RoomId,
                form.InventoryAdjustmentId,
                form.ExpectedCurrentBins,
                form.BinCount,
                form.OccurredAt,
                "Bins became unavailable for packing because they were dropped.",
                form.Notes,
                null,
                "CropQc.Web room dropped-bin workflow",
                form.TreatmentSignature,
                form.TreatmentSegmentId),
            actor,
            cancellationToken);
        return result.Success ? null : result.Error;
    }

    public async Task<RoomInventoryLossWriteResult> CreateReviewedCorrectionAsync(
        RoomInventoryLossCreateRequest request,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        var actor = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == actorUserId && x.IsActive, cancellationToken);
        return actor is null
            ? new(false, false, null, "The active correction administrator could not be resolved.")
            : await CreateCoreAsync(request, actor, cancellationToken);
    }

    public async Task<RoomInventoryLossWriteResult> NormalizeReviewedMalformedAdjustmentAsync(
        RoomInventoryLossNormalizationRequest request,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        var operationKey = NormalizeOperationKey(request.OperationKey);
        if (operationKey is null) return Failed("A valid operation key is required.");
        if (request.BinCount <= 0) return Failed("Bins dropped must be greater than zero.");
        if (request.CanonicalBalanceBeforeAdjustment < request.BinCount)
            return Failed("The reviewed canonical balance cannot cover the dropped-bin quantity.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return Failed("A dropped-bin reason is required.");
        if (request.AuditContext.ReceiptId != request.RequiredReceiptId
            || request.AuditContext.DroppedBins != request.BinCount
            || request.AuditContext.CanonicalBalanceBeforeAdjustment != request.CanonicalBalanceBeforeAdjustment
            || request.AuditContext.CorrectedCurrentPackableBins != request.CanonicalBalanceBeforeAdjustment - request.BinCount
            || request.AuditContext.MalformedAdjustmentId != request.ExistingAdjustmentId
            || string.IsNullOrWhiteSpace(request.AuditContext.ReceiptReference)
            || string.IsNullOrWhiteSpace(request.AuditContext.RootCause)
            || string.IsNullOrWhiteSpace(request.AuditContext.BusinessEvent))
            return Failed("The reviewed normalization audit context is incomplete or contradicts the correction request.");

        var actor = await dbContext.Users.SingleOrDefaultAsync(x => x.Id == actorUserId && x.IsActive, cancellationToken);
        if (actor is null) return Failed("The active correction administrator could not be resolved.");

        var ownsTransaction = dbContext.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction ? await BeginTransactionIfNeededAsync(cancellationToken) : null;
        try
        {
            var existingLoss = await dbContext.RoomInventoryLosses.AsNoTracking()
                .Include(x => x.InventoryAdjustments)
                .SingleOrDefaultAsync(x => x.OperationKey == operationKey, cancellationToken);
            if (existingLoss is not null)
            {
                var existingAdjustment = existingLoss.InventoryAdjustments.SingleOrDefault();
                return existingLoss.ReceiptId == request.RequiredReceiptId
                    && existingLoss.BinCount == request.BinCount
                    && existingLoss.LossType == RoomInventoryLossTypes.Dropped
                    && !existingLoss.IsReversed
                    && existingAdjustment?.Id == request.ExistingAdjustmentId
                    && existingAdjustment.AdjustmentType == RoomInventoryLossAdjustmentTypes.DroppedBins
                    && existingAdjustment.OldBinCount == request.CanonicalBalanceBeforeAdjustment
                    && existingAdjustment.ChangeAmount == -request.BinCount
                    && existingAdjustment.NewBinCount == request.CanonicalBalanceBeforeAdjustment - request.BinCount
                    ? new(true, true, existingLoss.Id, null)
                    : Failed("The operation key already belongs to a different room inventory loss.");
            }

            var adjustment = await dbContext.RoomInventoryAdjustments
                .SingleOrDefaultAsync(x => x.Id == request.ExistingAdjustmentId, cancellationToken);
            if (adjustment is null) return Failed("The reviewed malformed adjustment no longer exists.");
            if (adjustment.ReceiptId != request.RequiredReceiptId
                || adjustment.AdjustmentType != "ManualTrueUp"
                || adjustment.OldBinCount != 28
                || adjustment.ChangeAmount != 218
                || adjustment.NewBinCount != 246
                || adjustment.InventoryInvariantVersion != 0
                || !string.IsNullOrWhiteSpace(adjustment.InventoryOperationKey)
                || adjustment.RoomDepletionId is not null
                || adjustment.RoomTransferId is not null
                || adjustment.ReceiptInventoryOverrideId is not null
                || adjustment.ActualRunId is not null
                || adjustment.ActualRunRevisionId is not null
                || adjustment.RoomInventoryLossId is not null)
            {
                return Failed("The reviewed malformed Manual True Up shape no longer matches; no correction was recorded.");
            }

            var auditReceiptMatches = await dbContext.Receipts.AsNoTracking().AnyAsync(x =>
                x.Id == request.RequiredReceiptId
                && x.CompuTechReceiptId == request.AuditContext.ReceiptReference
                && x.BinCount == request.AuditContext.ReceivedBins,
                cancellationToken);
            if (!auditReceiptMatches)
                return Failed("The reviewed normalization audit context contradicts the persisted receipt evidence.");

            var snapshots = await ledgerQuery.GetSnapshotsAsync(adjustment.WarehouseId, [adjustment.RoomId], adjustment.FruitProfileId, cancellationToken);
            var snapshot = snapshots.SingleOrDefault(x => x.LatestAdjustmentId == adjustment.Id);
            if (snapshot is null || snapshot.CurrentBins != request.ExpectedPersistedCurrentBins)
                return Failed("The exact reviewed inventory identity changed; no correction was recorded.");

            var original = new
            {
                adjustment.Id,
                adjustment.ReceiptId,
                adjustment.AdjustmentType,
                adjustment.OldBinCount,
                adjustment.ChangeAmount,
                adjustment.NewBinCount,
                adjustment.Source,
                adjustment.Reason,
                adjustment.Notes,
                adjustment.AdjustmentAt,
                adjustment.CreatedAt,
                adjustment.CreatedByUserId,
                adjustment.InventoryInvariantVersion,
                adjustment.InventoryOperationKey,
                adjustment.RoomInventoryLossId
            };
            var now = businessTime.UtcNow;
            var loss = new RoomInventoryLoss
            {
                OperationKey = operationKey,
                WarehouseId = adjustment.WarehouseId,
                RoomId = adjustment.RoomId,
                ReceiptId = request.RequiredReceiptId,
                CropYear = adjustment.CropYear,
                GrowerLotId = adjustment.GrowerLotId,
                FruitProfileId = adjustment.FruitProfileId,
                GrowerName = adjustment.GrowerName,
                GrowerNumber = Normalize(snapshot.GrowerNumber),
                LotNumber = adjustment.LotNumber,
                PoolStart = Normalize(adjustment.PoolStart),
                VarietyCode = adjustment.VarietyCode ?? snapshot.Variety,
                InventoryStatus = Normalize(adjustment.InventoryStatus),
                LossType = RoomInventoryLossTypes.Dropped,
                BinCount = request.BinCount,
                Reason = request.Reason.Trim(),
                Notes = Normalize(request.Notes) ?? Normalize(adjustment.Notes),
                OccurredAt = null,
                CreatedByUserId = actor.Id,
                CreatedAt = now
            };
            dbContext.RoomInventoryLosses.Add(loss);
            await dbContext.SaveChangesAsync(cancellationToken);

            adjustment.AdjustmentType = RoomInventoryLossAdjustmentTypes.DroppedBins;
            adjustment.OldBinCount = request.CanonicalBalanceBeforeAdjustment;
            adjustment.ChangeAmount = -request.BinCount;
            adjustment.NewBinCount = request.CanonicalBalanceBeforeAdjustment - request.BinCount;
            adjustment.Source = "Room Inventory Loss";
            adjustment.Reason = loss.Reason;
            adjustment.Notes = loss.Notes;
            adjustment.InventoryInvariantVersion = InventoryDeductionInvariantService.CurrentVersion;
            adjustment.InventoryOperationKey = $"room-inventory-loss:{operationKey}:dropped";
            adjustment.RoomInventoryLossId = loss.Id;
            adjustment.RoomInventoryLoss = loss;
            await dbContext.SaveChangesAsync(cancellationToken);

            dbContext.AuditLogs.Add(new AuditLog
            {
                UserId = actor.Id,
                Action = "NormalizeMalformedManualTrueUp",
                EntityName = nameof(RoomInventoryAdjustment),
                EntityKey = adjustment.Id.ToString(),
                BeforeValuesJson = JsonSerializer.Serialize(original, JsonOptions),
                AfterValuesJson = JsonSerializer.Serialize(new RoomInventoryLossNormalizationAuditEvidence(
                    request.AuditContext.ReceiptReference,
                    request.AuditContext.ReceiptId,
                    request.AuditContext.ReceivedBins,
                    request.AuditContext.DroppedBins,
                    request.AuditContext.CanonicalBalanceBeforeAdjustment,
                    request.AuditContext.CorrectedCurrentPackableBins,
                    request.AuditContext.MalformedAdjustmentId,
                    original.AdjustmentType,
                    original.OldBinCount,
                    original.ChangeAmount,
                    original.NewBinCount,
                    adjustment.AdjustmentType,
                    adjustment.OldBinCount!.Value,
                    adjustment.ChangeAmount,
                    adjustment.NewBinCount,
                    loss.Id,
                    loss.OperationKey,
                    request.AuditContext.RootCause,
                    request.AuditContext.BusinessEvent,
                    true,
                    true,
                    now,
                    actor.Email), JsonOptions),
                SourceApplication = request.AuditSource,
                CreatedAt = now
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            await inventoryInvariant.ValidateBeforeCommitAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new(true, false, loss.Id, null);
        }
        catch (Exception exception)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            logger.LogError(exception, "Reviewed malformed dropped-bin normalization failed and was rolled back. OperationKey={OperationKey}", operationKey);
            if (!ownsTransaction) throw;
            return Failed("Reviewed malformed dropped-bin normalization failed and was rolled back. Review restricted logs.");
        }

        static RoomInventoryLossWriteResult Failed(string error) => new(false, false, null, error);
    }

    public async Task<string?> ReverseAsync(ReverseRoomInventoryLossForm form, CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal is null
            || !await userAccessService.HasAccessAsync(principal, ApplicationAreas.RoomTransactions, PageAccessLevel.Admin, cancellationToken))
        {
            return "Room Transactions Admin access is required to restore dropped bins.";
        }
        if (string.IsNullOrWhiteSpace(form.Reason))
        {
            return "A reversal reason is required.";
        }

        var operationKey = NormalizeOperationKey(form.OperationKey);
        if (operationKey is null)
        {
            return "A valid reversal operation key is required.";
        }

        var actor = await GetCurrentUserAsync(cancellationToken);
        if (actor is null)
        {
            return "The active administrator record could not be resolved.";
        }

        await using var transaction = await BeginTransactionIfNeededAsync(cancellationToken);
        try
        {
            var loss = await dbContext.RoomInventoryLosses
                .Include(x => x.InventoryAdjustments)
                .SingleOrDefaultAsync(x => x.Id == form.Id, cancellationToken);
            if (loss is null)
            {
                return "Dropped-bin record was not found.";
            }
            if (loss.IsReversed || loss.InventoryAdjustments.Any(x => x.AdjustmentType == RoomInventoryLossAdjustmentTypes.DroppedBinsReversal))
            {
                return null;
            }
            if (await dbContext.RoomInventoryAdjustments.AsNoTracking()
                .AnyAsync(x => x.InventoryOperationKey == $"room-inventory-loss:{operationKey}:reversal", cancellationToken))
            {
                return null;
            }

            var snapshots = await ledgerQuery.GetSnapshotsAsync(loss.WarehouseId, [loss.RoomId], cancellationToken);
            var snapshot = snapshots.SingleOrDefault(x => Matches(x, loss));
            if (snapshot is null)
            {
                return "The exact dropped-bin inventory identity no longer exists; no reversal was recorded.";
            }

            var now = businessTime.UtcNow;
            var reason = form.Reason.Trim();
            var wasReversed = loss.IsReversed;
            if (roomTreatmentService is not null)
            {
                var lineage = await roomTreatmentService.ReverseMovementsAsync(
                    $"room-inventory-loss:{operationKey}:treatment-reversal",
                    TreatmentLineageMovementTypes.InventoryLossReversal,
                    null,
                    loss.Id,
                    null,
                    now,
                    actor.Id,
                    cancellationToken);
                if (!lineage.Success) return lineage.Error;
                if (lineage.MovementId is null)
                {
                    var unknown = await roomTreatmentService.AddUnknownAsync(snapshot, loss.BinCount, $"room-inventory-loss:{operationKey}:unknown-restoration", now, actor.Id, cancellationToken);
                    if (!unknown.Success) return unknown.Error;
                }
            }
            var adjustment = CreateAdjustment(
                loss,
                actor.Id,
                snapshot.CurrentBins,
                loss.BinCount,
                snapshot.CurrentBins + loss.BinCount,
                RoomInventoryLossAdjustmentTypes.DroppedBinsReversal,
                now,
                reason,
                $"Restored dropped-bin loss #{loss.Id}.",
                $"room-inventory-loss:{operationKey}:reversal");
            loss.IsReversed = true;
            loss.ReversedAt = now;
            loss.ReversedByUserId = actor.Id;
            loss.ReverseReason = reason;
            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.AuditLogs.Add(new AuditLog
            {
                UserId = actor.Id,
                Action = RoomInventoryLossAdjustmentTypes.DroppedBinsReversal,
                EntityName = nameof(RoomInventoryLoss),
                EntityKey = loss.Id.ToString(),
                BeforeValuesJson = JsonSerializer.Serialize(new
                {
                    loss.Id,
                    loss.OperationKey,
                    loss.BinCount,
                    CurrentPackableBins = snapshot.CurrentBins,
                    IsReversed = wasReversed
                }, JsonOptions),
                AfterValuesJson = JsonSerializer.Serialize(new
                {
                    loss.Id,
                    ReversalAdjustmentId = adjustment.Id,
                    RestoredBins = loss.BinCount,
                    CurrentPackableBins = snapshot.CurrentBins + loss.BinCount,
                    Actor = actor.Email,
                    ReversedAt = now,
                    Reason = reason,
                    ReversalOperationKey = operationKey
                }, JsonOptions),
                SourceApplication = "CropQc.Web room dropped-bin workflow",
                CreatedAt = now
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            await inventoryInvariant.ValidateBeforeCommitAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return null;
        }
        catch (Exception exception)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            logger.LogError(exception, "Dropped-bin reversal failed and was rolled back. LossId={LossId}", form.Id);
            return "Dropped-bin restoration failed and was rolled back. Review restricted logs.";
        }
    }

    private async Task<RoomInventoryLossWriteResult> CreateCoreAsync(
        RoomInventoryLossCreateRequest request,
        User actor,
        CancellationToken cancellationToken)
    {
        var operationKey = NormalizeOperationKey(request.OperationKey);
        if (operationKey is null) return Failed("A valid operation key is required.");
        if (request.BinCount <= 0) return Failed("Bins dropped must be greater than zero.");
        if (request.ExpectedCurrentBins < 0) return Failed("Expected current bins cannot be negative.");
        if (string.IsNullOrWhiteSpace(request.Reason)) return Failed("A dropped-bin reason is required.");
        if (request.Notes?.Trim().Length > 1000) return Failed("Notes cannot exceed 1000 characters.");
        if (request.OccurredAt > businessTime.UtcNow.AddMinutes(5)) return Failed("Loss time cannot be in the future.");

        var ownsTransaction = dbContext.Database.CurrentTransaction is null;
        await using var transaction = ownsTransaction ? await BeginTransactionIfNeededAsync(cancellationToken) : null;
        try
        {
            var existing = await dbContext.RoomInventoryLosses.AsNoTracking()
                .SingleOrDefaultAsync(x => x.OperationKey == operationKey, cancellationToken);
            if (existing is not null)
            {
                return existing.RoomId == request.RoomId
                    && existing.BinCount == request.BinCount
                    && (request.RequiredReceiptId is null || existing.ReceiptId == request.RequiredReceiptId)
                    && existing.LossType == RoomInventoryLossTypes.Dropped
                    ? new(true, true, existing.Id, null)
                    : Failed("The operation key already belongs to a different room inventory loss.");
            }

            var snapshots = await ledgerQuery.GetSnapshotsAsync(null, [request.RoomId], cancellationToken);
            var snapshot = snapshots.SingleOrDefault(x => x.LatestAdjustmentId == request.InventoryAdjustmentId);
            if (snapshot is null)
            {
                return Failed("The selected inventory identity changed or is no longer current. Refresh the room before retrying.");
            }
            if (snapshot.CurrentBins != request.ExpectedCurrentBins)
            {
                return Failed($"Current packable inventory changed from {request.ExpectedCurrentBins} to {snapshot.CurrentBins}. Refresh before retrying.");
            }
            if (request.BinCount > snapshot.CurrentBins)
            {
                return Failed($"Cannot mark {request.BinCount} bins dropped because only {snapshot.CurrentBins} packable bins remain for the exact selected identity.");
            }

            var latestAdjustment = await dbContext.RoomInventoryAdjustments.AsNoTracking()
                .SingleAsync(x => x.Id == snapshot.LatestAdjustmentId, cancellationToken);
            if (request.RequiredReceiptId is not null && latestAdjustment.ReceiptId != request.RequiredReceiptId)
            {
                return Failed("The reviewed receipt does not own the exact current inventory identity.");
            }

            var now = businessTime.UtcNow;
            // A room-level loss belongs to the canonical ledger identity, not to whichever
            // receipt happened to contribute the latest adjustment. Only reviewed workflows
            // with authoritative receipt evidence may attach a receipt.
            var receiptId = request.RequiredReceiptId;
            var loss = new RoomInventoryLoss
            {
                OperationKey = operationKey,
                WarehouseId = snapshot.WarehouseId,
                RoomId = snapshot.RoomId,
                ReceiptId = receiptId,
                CropYear = snapshot.CropYear,
                GrowerLotId = snapshot.GrowerLotId,
                FruitProfileId = snapshot.FruitProfileId,
                GrowerName = snapshot.Grower,
                GrowerNumber = Normalize(snapshot.GrowerNumber),
                LotNumber = snapshot.Lot,
                PoolStart = Normalize(snapshot.PoolStart),
                VarietyCode = snapshot.Variety,
                InventoryStatus = Normalize(snapshot.InventoryStatus),
                LossType = RoomInventoryLossTypes.Dropped,
                BinCount = request.BinCount,
                Reason = request.Reason.Trim(),
                Notes = Normalize(request.Notes),
                OccurredAt = request.OccurredAt,
                CreatedByUserId = actor.Id,
                CreatedAt = now
            };
            dbContext.RoomInventoryLosses.Add(loss);
            await dbContext.SaveChangesAsync(cancellationToken);

            if (roomTreatmentService is not null)
            {
                var lineage = await roomTreatmentService.MoveSelectedAsync(
                    snapshot,
                    request.TreatmentSignature,
                    request.TreatmentSegmentId,
                    loss.ReceiptId,
                    request.BinCount,
                    null,
                    null,
                    $"room-inventory-loss:{operationKey}:treatment",
                    TreatmentLineageMovementTypes.InventoryLoss,
                    null,
                    loss.Id,
                    null,
                    request.OccurredAt ?? now,
                    actor.Id,
                    cancellationToken);
                if (!lineage.Success) return Failed(lineage.Error ?? "Treatment lineage could not be resolved for the selected loss.");
            }

            var after = snapshot.CurrentBins - request.BinCount;
            var adjustment = CreateAdjustment(
                loss,
                actor.Id,
                snapshot.CurrentBins,
                -request.BinCount,
                after,
                RoomInventoryLossAdjustmentTypes.DroppedBins,
                request.OccurredAt ?? now,
                loss.Reason,
                loss.Notes,
                $"room-inventory-loss:{operationKey}:dropped");
            await dbContext.SaveChangesAsync(cancellationToken);
            dbContext.AuditLogs.Add(new AuditLog
            {
                UserId = actor.Id,
                Action = RoomInventoryLossAdjustmentTypes.DroppedBins,
                EntityName = nameof(RoomInventoryLoss),
                EntityKey = loss.Id.ToString(),
                BeforeValuesJson = JsonSerializer.Serialize(new
                {
                    loss.ReceiptId,
                    loss.WarehouseId,
                    loss.RoomId,
                    loss.CropYear,
                    loss.GrowerLotId,
                    loss.FruitProfileId,
                    loss.GrowerName,
                    loss.GrowerNumber,
                    loss.LotNumber,
                    loss.VarietyCode,
                    loss.InventoryStatus,
                    CurrentPackableBins = snapshot.CurrentBins,
                    ReceiptBinsRemainUnchanged = receiptId is null ? null : await dbContext.Receipts.Where(x => x.Id == receiptId).Select(x => (int?)x.BinCount).SingleAsync(cancellationToken)
                }, JsonOptions),
                AfterValuesJson = JsonSerializer.Serialize(new
                {
                    loss.Id,
                    loss.OperationKey,
                    loss.LossType,
                    DroppedBins = loss.BinCount,
                    CurrentPackableBins = after,
                    AdjustmentId = adjustment.Id,
                    Actor = actor.Email,
                    RecordedAt = now,
                    loss.OccurredAt,
                    loss.Reason,
                    loss.Notes,
                    ReceiptQuantityWasNotChanged = true
                }, JsonOptions),
                SourceApplication = request.AuditSource,
                CreatedAt = now
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            await inventoryInvariant.ValidateBeforeCommitAsync(cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return new(true, false, loss.Id, null);
        }
        catch (Exception exception)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            logger.LogError(exception, "Dropped-bin operation failed and was rolled back. OperationKey={OperationKey}", operationKey);
            if (!ownsTransaction) throw;
            return Failed("Dropped-bin operation failed and was rolled back. Review restricted logs.");
        }

        static RoomInventoryLossWriteResult Failed(string error) => new(false, false, null, error);
    }

    private RoomInventoryAdjustment CreateAdjustment(
        RoomInventoryLoss loss,
        int actorUserId,
        int oldBins,
        int change,
        int newBins,
        string adjustmentType,
        DateTimeOffset adjustmentAt,
        string reason,
        string? notes,
        string operationKey)
    {
        var adjustment = new RoomInventoryAdjustment
        {
            CropYear = loss.CropYear,
            ReceiptId = loss.ReceiptId,
            WarehouseId = loss.WarehouseId,
            RoomId = loss.RoomId,
            GrowerLotId = loss.GrowerLotId,
            FruitProfileId = loss.FruitProfileId,
            GrowerName = loss.GrowerName,
            LotNumber = loss.LotNumber,
            PoolStart = loss.PoolStart,
            VarietyCode = loss.VarietyCode,
            OldBinCount = oldBins,
            ChangeAmount = change,
            NewBinCount = newBins,
            AdjustmentType = adjustmentType,
            Source = "Room Inventory Loss",
            InventoryStatus = loss.InventoryStatus,
            Reason = reason,
            Notes = notes,
            AdjustmentAt = adjustmentAt,
            CreatedByUserId = actorUserId,
            CreatedAt = businessTime.UtcNow,
            InventoryInvariantVersion = InventoryDeductionInvariantService.CurrentVersion,
            InventoryOperationKey = operationKey,
            RoomInventoryLossId = loss.Id,
            RoomInventoryLoss = loss
        };
        dbContext.RoomInventoryAdjustments.Add(adjustment);
        return adjustment;
    }

    private static async Task<IReadOnlyList<RoomInventoryLossHistoryViewModel>> ProjectHistoryAsync(
        IQueryable<RoomInventoryLoss> query,
        CanonicalGrowerResolutionSet growerResolver,
        CancellationToken cancellationToken)
    {
        var rows = await query.OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.RoomId,
                x.ReceiptId,
                ReceiptReference = x.Receipt == null ? "" : x.Receipt.CompuTechReceiptId,
                x.LossType,
                x.BinCount,
                Facility = x.Warehouse.Code,
                Room = x.Room.CropQcRoomName ?? x.Room.DisplayName ?? x.Room.Code,
                x.GrowerName,
                GrowerNumber = x.GrowerNumber ?? (x.Receipt == null ? null : x.Receipt.GrowerNumber),
                x.LotNumber,
                Variety = x.FruitProfile == null ? x.VarietyCode : x.FruitProfile.Name,
                ProductionType = x.FruitProfile == null ? "" : x.FruitProfile.ProductionType,
                IsOrganic = x.FruitProfile == null ? null : (bool?)x.FruitProfile.IsOrganic,
                x.OccurredAt,
                x.CreatedAt,
                CreatedBy = x.CreatedByUser.DisplayName,
                x.Reason,
                x.Notes,
                x.IsReversed,
                x.ReversedAt,
                ReversedBy = x.ReversedByUser == null ? null : x.ReversedByUser.DisplayName,
                x.ReverseReason
            })
            .ToListAsync(cancellationToken);
        return rows.Select(x => new RoomInventoryLossHistoryViewModel(
            x.Id,
            x.RoomId,
            x.ReceiptId,
            x.ReceiptReference,
            x.LossType,
            x.BinCount,
            x.Facility,
            x.Room,
            growerResolver.DisplayName(x.GrowerName, x.GrowerNumber ?? x.LotNumber),
            x.LotNumber,
            x.Variety,
            x.ProductionType,
            x.IsOrganic,
            x.OccurredAt,
            x.CreatedAt,
            x.CreatedBy,
            x.Reason,
            x.Notes,
            x.IsReversed,
            x.ReversedAt,
            x.ReversedBy,
            x.ReverseReason)).ToList();
    }

    private async Task<User?> GetCurrentUserAsync(CancellationToken cancellationToken)
    {
        var email = httpContextAccessor.HttpContext?.User.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();
        return string.IsNullOrWhiteSpace(email)
            ? null
            : await dbContext.Users.SingleOrDefaultAsync(x => x.Email == email && x.IsActive, cancellationToken);
    }

    private async Task<IDbContextTransaction?> BeginTransactionIfNeededAsync(CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is not null
            || (dbContext.Database.ProviderName ?? "").Contains("InMemory", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
    }

    private static bool Matches(RoomInventoryLedgerSnapshot snapshot, RoomInventoryLoss loss) =>
        snapshot.WarehouseId == loss.WarehouseId
        && snapshot.RoomId == loss.RoomId
        && snapshot.CropYear == loss.CropYear
        && snapshot.GrowerLotId == loss.GrowerLotId
        && snapshot.FruitProfileId == loss.FruitProfileId
        && Same(snapshot.Lot, loss.LotNumber)
        && Same(snapshot.Variety, loss.VarietyCode)
        && Same(snapshot.InventoryStatus, loss.InventoryStatus);

    private static string OrganicLabel(RoomInventoryLedgerSnapshot snapshot) => snapshot.IsOrganic switch
    {
        true => "Organic",
        false => "Conventional",
        _ => string.IsNullOrWhiteSpace(snapshot.ProductionType) ? "Organic status unavailable" : snapshot.ProductionType
    };

    private static string? NormalizeOperationKey(string? value)
    {
        value = Normalize(value);
        return value is { Length: <= 150 } ? value : null;
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
