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
    string AuditSource);

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
    IHttpContextAccessor httpContextAccessor,
    IBusinessTimeService businessTime,
    ILogger<RoomInventoryLossService> logger) : IRoomInventoryLossService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public async Task<RoomInventoryLossPageData> GetRoomDataAsync(int roomId, CancellationToken cancellationToken)
    {
        var snapshots = await ledgerQuery.GetSnapshotsAsync(null, [roomId], cancellationToken);
        var options = snapshots
            .Where(x => x.CurrentBins > 0)
            .OrderBy(x => x.GrowerNumber ?? x.Grower)
            .ThenBy(x => x.Lot)
            .ThenBy(x => x.ProductionType)
            .ThenBy(x => x.Variety)
            .Select(x => new RoomInventoryLossOptionViewModel(
                x.LatestAdjustmentId,
                $"{x.GrowerNumber ?? x.Grower} / {x.Lot} / {x.VarietyName} / {OrganicLabel(x)} ({x.CurrentBins} packable bins)",
                x.Facility,
                x.Room,
                x.Grower,
                x.Lot,
                x.VarietyName,
                x.ProductionType,
                x.IsOrganic,
                x.CurrentBins))
            .ToList();
        var principal = httpContextAccessor.HttpContext?.User;
        var canRecord = principal is not null
            && await userAccessService.HasAccessAsync(principal, ApplicationAreas.RoomTransactions, PageAccessLevel.Edit, cancellationToken);
        var canReverse = principal is not null
            && await userAccessService.HasAccessAsync(principal, ApplicationAreas.RoomTransactions, PageAccessLevel.Admin, cancellationToken);
        return new(options, await GetHistoryQuery().Where(x => x.RoomId == roomId).ToListAsync(cancellationToken), canRecord, canReverse);
    }

    public async Task<IReadOnlyList<RoomInventoryLossHistoryViewModel>> GetReceiptHistoryAsync(
        long receiptId,
        CancellationToken cancellationToken) =>
        await GetHistoryQuery().Where(x => x.ReceiptId == receiptId).ToListAsync(cancellationToken);

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
                "CropQc.Web room dropped-bin workflow"),
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
            var receiptId = request.RequiredReceiptId ?? latestAdjustment.ReceiptId;
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

    private IQueryable<RoomInventoryLossHistoryViewModel> GetHistoryQuery() =>
        dbContext.RoomInventoryLosses.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt)
            .ThenByDescending(x => x.Id)
            .Select(x => new RoomInventoryLossHistoryViewModel(
                x.Id,
                x.RoomId,
                x.ReceiptId,
                x.Receipt == null ? "" : x.Receipt.CompuTechReceiptId,
                x.LossType,
                x.BinCount,
                x.Warehouse.Code,
                x.Room.CropQcRoomName ?? x.Room.DisplayName ?? x.Room.Code,
                x.GrowerName,
                x.LotNumber,
                x.FruitProfile == null ? x.VarietyCode : x.FruitProfile.Name,
                x.FruitProfile == null ? "" : x.FruitProfile.ProductionType,
                x.FruitProfile == null ? null : (bool?)x.FruitProfile.IsOrganic,
                x.OccurredAt,
                x.CreatedAt,
                x.CreatedByUser.DisplayName,
                x.Reason,
                x.Notes,
                x.IsReversed,
                x.ReversedAt,
                x.ReversedByUser == null ? null : x.ReversedByUser.DisplayName,
                x.ReverseReason));

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
