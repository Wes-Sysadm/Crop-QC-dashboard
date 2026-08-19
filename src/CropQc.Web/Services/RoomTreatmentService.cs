using System.Security.Claims;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

public sealed record RoomTreatmentData(
    IReadOnlyList<CurrentTreatmentSegmentViewModel> Current,
    IReadOnlyList<RoomTreatmentApplicationHistoryViewModel> History,
    bool CanApply,
    bool CanReverse);

public sealed record TreatmentSegmentSelection(
    string IdentityKey,
    string TreatmentSignature,
    string TreatmentState,
    int CurrentBins,
    string Label);

public sealed record TreatmentLineageWriteResult(bool Success, string? Error, long? MovementId = null);

public interface IRoomTreatmentService
{
    Task<RoomTreatmentApplyPageViewModel> GetApplyPageAsync(RoomTreatmentApplyForm form, bool review, CancellationToken cancellationToken);
    Task<(string? Error, long? ApplicationId)> ApplyAsync(RoomTreatmentApplyForm form, CancellationToken cancellationToken);
    Task<string?> ReverseAsync(ReverseRoomTreatmentApplicationForm form, CancellationToken cancellationToken);
    Task<RoomTreatmentData> GetRoomDataAsync(int roomId, CancellationToken cancellationToken);
    Task<IReadOnlyList<TreatmentSegmentSelection>> GetSelectionsAsync(RoomInventoryLedgerSnapshot snapshot, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, IReadOnlyList<TreatmentSegmentSelection>>> GetSelectionsAsync(IReadOnlyList<RoomInventoryLedgerSnapshot> snapshots, CancellationToken cancellationToken);
    Task<TreatmentLineageWriteResult> MoveAsync(RoomInventoryLedgerSnapshot snapshot, string? treatmentSignature, int bins, int? destinationWarehouseId, int? destinationRoomId, string operationKey, string movementType, long? roomTransferId, long? roomInventoryLossId, long? binsRunEntryId, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken);
    Task<TreatmentLineageWriteResult> ReverseMovementsAsync(string operationKeyPrefix, string movementType, long? roomTransferId, long? roomInventoryLossId, long? binsRunEntryId, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken);
    Task<TreatmentLineageWriteResult> AddUnknownAsync(RoomInventoryLedgerSnapshot snapshot, int bins, string operationKey, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken);
}

public sealed class RoomTreatmentService(
    CropQcDbContext dbContext,
    IRoomInventoryLedgerQueryService ledger,
    IUserAccessService access,
    IHttpContextAccessor httpContextAccessor,
    IBusinessTimeService businessTime,
    ILogger<RoomTreatmentService> logger) : IRoomTreatmentService
{
    private const string SourceApplication = "CropQc.Web room treatment workflow";
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    public async Task<RoomTreatmentApplyPageViewModel> GetApplyPageAsync(
        RoomTreatmentApplyForm form,
        bool review,
        CancellationToken cancellationToken)
    {
        var room = await dbContext.Rooms.AsNoTracking().Include(x => x.Warehouse)
            .SingleOrDefaultAsync(x => x.Id == form.RoomId && x.IsActive, cancellationToken);
        if (room is null)
        {
            return new() { Form = form, Error = "Room was not found." };
        }

        var snapshotResult = await ResolveApplicationSnapshotAsync(room.Id, form.AppliedAt, cancellationToken);
        var fruit = snapshotResult.Snapshots.Select(ToFruitView).ToList();
        var crop = ResolveWholeRoomCrop(snapshotResult.Snapshots);
        var chemicals = crop is null
            ? []
            : await dbContext.TreatmentChemicals.AsNoTracking()
                .Where(x => x.IsActive && x.Crop == crop)
                .OrderBy(x => x.CommonName ?? x.ProductName)
                .ThenBy(x => x.ProductName)
                .Select(x => new TreatmentChemicalOptionViewModel(x.Id, x.ProductName, x.CommonName, x.Crop, x.Volume, x.Unit, x.UnitPrice, x.Currency))
                .ToListAsync(cancellationToken);
        var selected = chemicals.SingleOrDefault(x => x.Id == form.TreatmentChemicalId);
        var error = snapshotResult.Error;
        if (error is null && snapshotResult.Snapshots.Count == 0)
        {
            error = "An empty room cannot receive a treatment application.";
        }
        else if (error is null && crop is null)
        {
            error = "This room contains mixed or unresolved crops. One treatment cannot be proven valid for every bin, so the application was not allowed.";
        }
        else if (review && error is null && selected is null)
        {
            error = "Select an active treatment that is valid for all fruit in the room.";
        }

        return new RoomTreatmentApplyPageViewModel
        {
            Form = form,
            Warehouse = room.Warehouse.Code,
            Room = room.CropQcRoomName ?? room.DisplayName ?? room.Code,
            TotalBins = snapshotResult.Snapshots.Sum(x => x.CurrentBins),
            Error = error,
            IsReview = review && error is null && selected is not null,
            SelectedTreatment = selected,
            EstimatedCost = selected is null ? 0 : decimal.Round(snapshotResult.Snapshots.Sum(x => x.CurrentBins) * selected.UnitPrice, 2),
            TreatmentOptions = chemicals,
            Fruit = fruit
        };
    }

    public async Task<(string? Error, long? ApplicationId)> ApplyAsync(
        RoomTreatmentApplyForm form,
        CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal is null || !await access.HasAccessAsync(principal, ApplicationAreas.RoomTransactions, PageAccessLevel.Edit, cancellationToken))
        {
            return ("Room Transactions Edit access is required to apply a treatment.", null);
        }
        if (!form.ConfirmedReview)
        {
            return ("Review the treatment and exact fruit snapshot before saving.", null);
        }
        if (string.IsNullOrWhiteSpace(form.OperationKey) || form.OperationKey.Trim().Length > 100)
        {
            return ("A valid treatment operation key is required.", null);
        }
        if (form.Notes?.Trim().Length > 1000)
        {
            return ("Notes cannot exceed 1000 characters.", null);
        }

        var actor = await CurrentUserAsync(cancellationToken);
        if (actor is null)
        {
            return ("The active user record could not be resolved.", null);
        }

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            var operationKey = form.OperationKey.Trim();
            var existing = await dbContext.RoomTreatmentApplications.AsNoTracking()
                .SingleOrDefaultAsync(x => x.OperationKey == operationKey, cancellationToken);
            if (existing is not null)
            {
                return existing.RoomId == form.RoomId
                    && existing.TreatmentChemicalId == form.TreatmentChemicalId
                    && existing.AppliedAt.ToUniversalTime() == form.AppliedAt.ToUniversalTime()
                    && existing.Notes == Normalize(form.Notes)
                    ? (null, existing.Id)
                    : ("The operation key already belongs to a different treatment application.", null);
            }

            var room = await dbContext.Rooms.Include(x => x.Warehouse).SingleOrDefaultAsync(x => x.Id == form.RoomId && x.IsActive, cancellationToken);
            var chemical = await dbContext.TreatmentChemicals.SingleOrDefaultAsync(x => x.Id == form.TreatmentChemicalId && x.IsActive, cancellationToken);
            if (room is null || chemical is null)
            {
                return ("The room or active treatment was not found.", null);
            }

            var snapshotResult = await ResolveApplicationSnapshotAsync(room.Id, form.AppliedAt, cancellationToken);
            if (snapshotResult.Error is not null)
            {
                return (snapshotResult.Error, null);
            }
            if (snapshotResult.Snapshots.Count == 0)
            {
                return ("An empty room cannot receive a treatment application.", null);
            }
            var crop = ResolveWholeRoomCrop(snapshotResult.Snapshots);
            if (crop is null || !string.Equals(crop, chemical.Crop, StringComparison.OrdinalIgnoreCase))
            {
                return ("The selected chemical is not valid for every bin in this room snapshot.", null);
            }

            var now = businessTime.UtcNow;
            var total = snapshotResult.Snapshots.Sum(x => x.CurrentBins);
            var application = new RoomTreatmentApplication
            {
                OperationKey = operationKey,
                TreatmentChemicalId = chemical.Id,
                WarehouseId = room.WarehouseId,
                RoomId = room.Id,
                AppliedAt = form.AppliedAt.ToUniversalTime(),
                AppliedByUserId = actor.Id,
                Notes = Normalize(form.Notes),
                TotalBinsSnapshot = total,
                ProductNameSnapshot = chemical.ProductName,
                CommonNameSnapshot = Normalize(chemical.CommonName),
                CropSnapshot = chemical.Crop,
                VolumeSnapshot = chemical.Volume,
                UnitSnapshot = chemical.Unit,
                UnitPriceSnapshot = chemical.UnitPrice,
                CurrencySnapshot = chemical.Currency,
                EstimatedCostSnapshot = decimal.Round(total * chemical.UnitPrice, 2),
                CreatedAt = now,
                CreatedByUserId = actor.Id
            };
            dbContext.RoomTreatmentApplications.Add(application);
            await dbContext.SaveChangesAsync(cancellationToken);

            foreach (var snapshot in snapshotResult.Snapshots)
            {
                var segments = await MaterializeAsync(snapshot, cancellationToken);
                foreach (var segment in segments.Where(x => x.CurrentBins > 0).ToList())
                {
                    var treatedBins = segment.CurrentBins;
                    var resultSignature = AppendApplication(segment.TreatmentSignature, application.Id);
                    var target = await GetOrCreateSegmentAsync(snapshot, segment.TreatmentState == TreatmentLineageStates.Unknown ? TreatmentLineageStates.Unknown : TreatmentLineageStates.Confirmed, resultSignature, now, cancellationToken);
                    await CopyApplicationLinksAsync(segment, target, cancellationToken);
                    EnsureApplicationLink(target, application.Id);
                    target.CurrentBins += treatedBins;
                    target.UpdatedAt = now;
                    target.ConcurrencyVersion++;
                    segment.CurrentBins = 0;
                    segment.UpdatedAt = now;
                    segment.ConcurrencyVersion++;
                    application.Sources.Add(new RoomTreatmentApplicationSource
                    {
                        CropYear = snapshot.CropYear,
                        GrowerLotId = snapshot.GrowerLotId,
                        FruitProfileId = snapshot.FruitProfileId,
                        IdentityKey = IdentityKey(snapshot),
                        GrowerNumberSnapshot = Normalize(snapshot.GrowerNumber),
                        GrowerNameSnapshot = snapshot.Grower,
                        LotNumberSnapshot = snapshot.Lot,
                        VarietyCodeSnapshot = snapshot.Variety,
                        ProductionTypeSnapshot = snapshot.ProductionType,
                        IsOrganicSnapshot = snapshot.IsOrganic,
                        InventoryStatusSnapshot = Normalize(snapshot.InventoryStatus),
                        BinsTreated = treatedBins,
                        PriorTreatmentSignature = segment.TreatmentSignature,
                        ResultTreatmentSignature = resultSignature
                    });
                }
            }

            dbContext.AuditLogs.Add(new AuditLog
            {
                UserId = actor.Id,
                Action = "ApplyTreatment",
                EntityName = nameof(RoomTreatmentApplication),
                EntityKey = application.Id.ToString(),
                AfterValuesJson = JsonSerializer.Serialize(new
                {
                    TreatmentApplicationId = application.Id,
                    Room = $"{room.Warehouse.Code}/{room.Code}",
                    application.AppliedAt,
                    Chemical = application.ProductNameSnapshot,
                    AffectedBins = total,
                    SourceCount = snapshotResult.Snapshots.Count,
                    SourceIdentityKeys = snapshotResult.Snapshots.Select(IdentityKey).Take(50),
                    Actor = actor.Email
                }, AuditJson),
                SourceApplication = SourceApplication,
                CreatedAt = now
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            await ValidateRoomsAsync([room.Id], cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return (null, application.Id);
        }
        catch (Exception exception)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            logger.LogError(exception, "Treatment application failed and was rolled back. RoomId={RoomId}", form.RoomId);
            return ("Treatment application failed and was rolled back. Review restricted logs.", null);
        }
    }

    public async Task<string?> ReverseAsync(ReverseRoomTreatmentApplicationForm form, CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal is null || !await access.HasAccessAsync(principal, ApplicationAreas.RoomTransactions, PageAccessLevel.Admin, cancellationToken))
        {
            return "Room Transactions Admin access is required to reverse a treatment application.";
        }
        if (string.IsNullOrWhiteSpace(form.Reason)) return "A reversal reason is required.";
        if (form.Reason.Trim().Length > 1000) return "Reversal reason cannot exceed 1000 characters.";
        var actor = await CurrentUserAsync(cancellationToken);
        if (actor is null) return "The active administrator could not be resolved.";

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        try
        {
            var application = await dbContext.RoomTreatmentApplications.SingleOrDefaultAsync(x => x.Id == form.Id, cancellationToken);
            if (application is null) return "Treatment application was not found.";
            if (application.ReversedAt is not null) return "Treatment application is already reversed.";

            var segments = await dbContext.TreatmentLineageSegments
                .Include(x => x.Applications)
                .Where(x => x.CurrentBins > 0 && x.Applications.Any(a => a.RoomTreatmentApplicationId == application.Id))
                .ToListAsync(cancellationToken);
            var affectedRooms = segments.Select(x => x.RoomId).Distinct().ToList();
            var now = businessTime.UtcNow;
            foreach (var segment in segments)
            {
                var remainingIds = segment.Applications
                    .Where(x => x.RoomTreatmentApplicationId != application.Id)
                    .OrderBy(x => x.Sequence)
                    .Select(x => x.RoomTreatmentApplicationId)
                    .ToList();
                var prefix = segment.TreatmentSignature.StartsWith("x|", StringComparison.Ordinal) ? "x" : "u";
                var signature = remainingIds.Count == 0 ? prefix : $"{prefix}|a:{string.Join(',', remainingIds)}";
                var snapshot = ToSnapshot(segment);
                var state = prefix == "x" ? TreatmentLineageStates.Unknown : remainingIds.Count == 0 ? TreatmentLineageStates.Untreated : TreatmentLineageStates.Confirmed;
                var target = await GetOrCreateSegmentAsync(snapshot, state, signature, now, cancellationToken);
                foreach (var id in remainingIds) EnsureApplicationLink(target, id);
                target.CurrentBins += segment.CurrentBins;
                target.UpdatedAt = now;
                target.ConcurrencyVersion++;
                segment.CurrentBins = 0;
                segment.UpdatedAt = now;
                segment.ConcurrencyVersion++;
            }

            application.ReversedAt = now;
            application.ReversedByUserId = actor.Id;
            application.ReversalReason = form.Reason.Trim();
            dbContext.AuditLogs.Add(new AuditLog
            {
                UserId = actor.Id,
                Action = "ReverseTreatment",
                EntityName = nameof(RoomTreatmentApplication),
                EntityKey = application.Id.ToString(),
                BeforeValuesJson = JsonSerializer.Serialize(new { application.RoomId, application.AppliedAt, application.ProductNameSnapshot, application.TotalBinsSnapshot }, AuditJson),
                AfterValuesJson = JsonSerializer.Serialize(new { ReversedAt = now, ReversedBy = actor.Email, Reason = form.Reason.Trim(), CurrentSegmentsChanged = segments.Count }, AuditJson),
                SourceApplication = SourceApplication,
                CreatedAt = now
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            await ValidateRoomsAsync(affectedRooms, cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return null;
        }
        catch (Exception exception)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            logger.LogError(exception, "Treatment reversal failed and was rolled back. ApplicationId={ApplicationId}", form.Id);
            return "Treatment reversal failed and was rolled back. Review restricted logs.";
        }
    }

    public async Task<RoomTreatmentData> GetRoomDataAsync(int roomId, CancellationToken cancellationToken)
    {
        var snapshots = await ledger.GetSnapshotsAsync(null, [roomId], cancellationToken);
        var activeSnapshots = snapshots.Where(x => x.CurrentBins > 0).ToList();
        var projected = await ProjectSelectionsBatchAsync(activeSnapshots, cancellationToken);
        var current = activeSnapshots.SelectMany(x => projected[SelectionLookupKey(x)]).ToList();

        var history = await dbContext.RoomTreatmentApplications.AsNoTracking()
            .Where(x => x.RoomId == roomId)
            .OrderByDescending(x => x.AppliedAt).ThenByDescending(x => x.Id)
            .Take(200)
            .Select(x => new RoomTreatmentApplicationHistoryViewModel(
                x.Id, x.AppliedAt, x.ProductNameSnapshot, x.CommonNameSnapshot, x.TotalBinsSnapshot,
                x.AppliedByUser.DisplayName ?? x.AppliedByUser.Email, x.EstimatedCostSnapshot, x.CurrencySnapshot,
                x.Notes, x.ReversedAt != null, x.ReversedAt, x.ReversalReason))
            .ToListAsync(cancellationToken);
        var principal = httpContextAccessor.HttpContext?.User;
        var canApply = principal is not null && await access.HasAccessAsync(principal, ApplicationAreas.RoomTransactions, PageAccessLevel.Edit, cancellationToken);
        var canReverse = principal is not null && await access.HasAccessAsync(principal, ApplicationAreas.RoomTransactions, PageAccessLevel.Admin, cancellationToken);
        return new(current, history, canApply, canReverse);
    }

    public async Task<IReadOnlyList<TreatmentSegmentSelection>> GetSelectionsAsync(RoomInventoryLedgerSnapshot snapshot, CancellationToken cancellationToken)
    {
        var projected = (await ProjectSelectionsBatchAsync([snapshot], cancellationToken))[SelectionLookupKey(snapshot)];
        return projected.Select(x => new TreatmentSegmentSelection(x.IdentityKey, x.TreatmentSignature, x.TreatmentState, x.Bins, SegmentLabel(x))).ToList();
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<TreatmentSegmentSelection>>> GetSelectionsAsync(
        IReadOnlyList<RoomInventoryLedgerSnapshot> snapshots,
        CancellationToken cancellationToken)
    {
        var active = snapshots.Where(x => x.CurrentBins > 0).ToList();
        var projected = await ProjectSelectionsBatchAsync(active, cancellationToken);
        return projected.ToDictionary(
            x => x.Key,
            x => (IReadOnlyList<TreatmentSegmentSelection>)x.Value.Select(y => new TreatmentSegmentSelection(
                y.IdentityKey, y.TreatmentSignature, y.TreatmentState, y.Bins, SegmentLabel(y))).ToList());
    }

    public async Task<TreatmentLineageWriteResult> MoveAsync(
        RoomInventoryLedgerSnapshot snapshot,
        string? treatmentSignature,
        int bins,
        int? destinationWarehouseId,
        int? destinationRoomId,
        string operationKey,
        string movementType,
        long? roomTransferId,
        long? roomInventoryLossId,
        long? binsRunEntryId,
        DateTimeOffset occurredAt,
        int? actorUserId,
        CancellationToken cancellationToken)
    {
        if (bins <= 0) return new(false, "Treatment lineage movement quantity must be positive.");
        if (roomTransferId is null && roomInventoryLossId is null && binsRunEntryId is null)
        {
            return new(false, "A specific parent movement is required for treatment lineage movement.");
        }
        if ((roomTransferId is not null ? 1 : 0) + (roomInventoryLossId is not null ? 1 : 0) + (binsRunEntryId is not null ? 1 : 0) != 1)
        {
            return new(false, "Treatment lineage movement must reference exactly one parent movement.");
        }
        var parentError = await ValidateMovementParentAsync(
            snapshot, bins, destinationWarehouseId, destinationRoomId,
            roomTransferId, roomInventoryLossId, binsRunEntryId, cancellationToken);
        if (parentError is not null) return new(false, parentError);
        var existingMovement = await dbContext.TreatmentLineageMovements.AsNoTracking()
            .SingleOrDefaultAsync(x => x.OperationKey == operationKey, cancellationToken);
        if (existingMovement is not null)
        {
            var sameRequest = existingMovement.MovementType == movementType
                && existingMovement.BinCount == bins
                && existingMovement.IdentityKey == IdentityKey(snapshot)
                && existingMovement.SourceRoomId == snapshot.RoomId
                && existingMovement.TreatmentSignatureSnapshot == (string.IsNullOrWhiteSpace(treatmentSignature) ? existingMovement.TreatmentSignatureSnapshot : treatmentSignature)
                && existingMovement.DestinationRoomId == destinationRoomId
                && existingMovement.RoomTransferId == roomTransferId
                && existingMovement.RoomInventoryLossId == roomInventoryLossId
                && existingMovement.BinsRunEntryId == binsRunEntryId;
            return sameRequest
                ? new(true, null, existingMovement.Id)
                : new(false, "The operation key already belongs to a different treatment lineage movement.");
        }
        var segments = await MaterializeAsync(snapshot, cancellationToken);
        var available = segments.Where(x => x.CurrentBins > 0).ToList();
        TreatmentLineageSegment? source;
        if (string.IsNullOrWhiteSpace(treatmentSignature))
        {
            if (available.Count != 1) return new(false, "This fruit identity has multiple treatment histories. Select the exact treated or untreated segment.");
            source = available[0];
        }
        else
        {
            source = available.SingleOrDefault(x => x.TreatmentSignature == treatmentSignature);
        }
        if (source is null) return new(false, "The selected treatment segment is no longer available. Refresh before retrying.");
        if (source.CurrentBins < bins) return new(false, $"Only {source.CurrentBins} bins remain in the selected treatment segment.");

        var now = businessTime.UtcNow;
        TreatmentLineageSegment? destination = null;
        if (destinationRoomId is not null && destinationWarehouseId is not null)
        {
            var destinationSnapshot = snapshot with { WarehouseId = destinationWarehouseId.Value, RoomId = destinationRoomId.Value };
            destination = await GetOrCreateSegmentAsync(destinationSnapshot, source.TreatmentState, source.TreatmentSignature, now, cancellationToken);
            await CopyApplicationLinksAsync(source, destination, cancellationToken);
            destination.CurrentBins += bins;
            destination.UpdatedAt = now;
            destination.ConcurrencyVersion++;
        }
        source.CurrentBins -= bins;
        source.UpdatedAt = now;
        source.ConcurrencyVersion++;
        var movement = new TreatmentLineageMovement
        {
            OperationKey = operationKey,
            MovementType = movementType,
            SourceSegment = source,
            DestinationSegment = destination,
            SourceRoomId = source.RoomId,
            DestinationRoomId = destinationRoomId,
            IdentityKey = source.IdentityKey,
            TreatmentStateSnapshot = source.TreatmentState,
            TreatmentSignatureSnapshot = source.TreatmentSignature,
            BinCount = bins,
            RoomTransferId = roomTransferId,
            RoomInventoryLossId = roomInventoryLossId,
            BinsRunEntryId = binsRunEntryId,
            OccurredAt = occurredAt,
            CreatedByUserId = actorUserId,
            CreatedAt = now
        };
        dbContext.TreatmentLineageMovements.Add(movement);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(true, null, movement.Id);
    }

    private async Task<string?> ValidateMovementParentAsync(
        RoomInventoryLedgerSnapshot snapshot,
        int bins,
        int? destinationWarehouseId,
        int? destinationRoomId,
        long? roomTransferId,
        long? roomInventoryLossId,
        long? binsRunEntryId,
        CancellationToken cancellationToken)
    {
        if (roomTransferId is not null)
        {
            var parent = await dbContext.RoomTransfers.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == roomTransferId.Value, cancellationToken);
            if (parent is null
                || parent.SourceWarehouseId != snapshot.WarehouseId
                || parent.SourceRoomId != snapshot.RoomId
                || parent.DestinationWarehouseId != destinationWarehouseId
                || parent.DestinationRoomId != destinationRoomId
                || parent.BinCount != bins
                || !SameIdentity(parent.CropYear, parent.GrowerLotId, parent.FruitProfileId, parent.LotNumber, parent.VarietyCode, snapshot))
            {
                return "The room transfer parent does not match the exact treatment lineage movement.";
            }
            return null;
        }

        if (roomInventoryLossId is not null)
        {
            var parent = await dbContext.RoomInventoryLosses.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == roomInventoryLossId.Value, cancellationToken);
            if (parent is null
                || parent.WarehouseId != snapshot.WarehouseId
                || parent.RoomId != snapshot.RoomId
                || destinationWarehouseId is not null
                || destinationRoomId is not null
                || parent.BinCount != bins
                || !SameIdentity(parent.CropYear, parent.GrowerLotId, parent.FruitProfileId, parent.LotNumber, parent.VarietyCode, snapshot))
            {
                return "The inventory-loss parent does not match the exact treatment lineage movement.";
            }
            return null;
        }

        var runParent = await dbContext.BinsRunEntries.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == binsRunEntryId!.Value, cancellationToken);
        if (runParent is null
            || runParent.WarehouseId != snapshot.WarehouseId
            || runParent.RoomId != snapshot.RoomId
            || destinationWarehouseId is not null
            || destinationRoomId is not null
            || runParent.BinsRun != bins
            || !SameIdentity(runParent.CropYear, runParent.GrowerLotId, runParent.FruitProfileId, runParent.LotNumber, runParent.VarietyCode, snapshot))
        {
            return "The Bins Run parent does not match the exact treatment lineage movement.";
        }
        return null;
    }

    private static bool SameIdentity(
        int? cropYear,
        int? growerLotId,
        int? fruitProfileId,
        string? lotNumber,
        string? varietyCode,
        RoomInventoryLedgerSnapshot snapshot) =>
        cropYear == snapshot.CropYear
        && growerLotId == snapshot.GrowerLotId
        && fruitProfileId == snapshot.FruitProfileId
        && string.Equals(lotNumber?.Trim(), snapshot.Lot?.Trim(), StringComparison.OrdinalIgnoreCase)
        && string.Equals(varietyCode?.Trim(), snapshot.Variety?.Trim(), StringComparison.OrdinalIgnoreCase);

    public async Task<TreatmentLineageWriteResult> ReverseMovementsAsync(
        string operationKeyPrefix,
        string movementType,
        long? roomTransferId,
        long? roomInventoryLossId,
        long? binsRunEntryId,
        DateTimeOffset occurredAt,
        int? actorUserId,
        CancellationToken cancellationToken)
    {
        if (roomTransferId is null && roomInventoryLossId is null && binsRunEntryId is null)
        {
            return new(false, "A specific parent movement is required for treatment lineage reversal.");
        }
        var originals = await dbContext.TreatmentLineageMovements
            .Where(x => (roomTransferId == null || x.RoomTransferId == roomTransferId)
                && (roomInventoryLossId == null || x.RoomInventoryLossId == roomInventoryLossId)
                && (binsRunEntryId == null || x.BinsRunEntryId == binsRunEntryId)
                && x.ReversesTreatmentLineageMovementId == null)
            .ToListAsync(cancellationToken);
        long? lastMovementId = null;
        foreach (var original in originals)
        {
            var key = $"{operationKeyPrefix}:{original.Id}";
            if (await dbContext.TreatmentLineageMovements.AsNoTracking().AnyAsync(x => x.OperationKey == key, cancellationToken)) continue;
            var destination = original.DestinationSegmentId is null
                ? null
                : await dbContext.TreatmentLineageSegments.Include(x => x.Applications).SingleAsync(x => x.Id == original.DestinationSegmentId, cancellationToken);
            var source = await dbContext.TreatmentLineageSegments.Include(x => x.Applications).SingleAsync(x => x.Id == original.SourceSegmentId, cancellationToken);
            if (destination is not null && destination.CurrentBins < original.BinCount)
            {
                return new(false, "The exact transferred treatment segment no longer contains enough bins to reverse this movement.");
            }
            var now = businessTime.UtcNow;
            source.CurrentBins += original.BinCount;
            source.UpdatedAt = now;
            source.ConcurrencyVersion++;
            if (destination is not null)
            {
                destination.CurrentBins -= original.BinCount;
                destination.UpdatedAt = now;
                destination.ConcurrencyVersion++;
            }
            var reversal = new TreatmentLineageMovement
            {
                OperationKey = key,
                MovementType = movementType,
                SourceSegment = destination,
                DestinationSegment = source,
                SourceRoomId = destination?.RoomId,
                DestinationRoomId = source.RoomId,
                IdentityKey = original.IdentityKey,
                TreatmentStateSnapshot = original.TreatmentStateSnapshot,
                TreatmentSignatureSnapshot = original.TreatmentSignatureSnapshot,
                BinCount = original.BinCount,
                RoomTransferId = roomTransferId,
                RoomInventoryLossId = roomInventoryLossId,
                BinsRunEntryId = binsRunEntryId,
                ReversesTreatmentLineageMovementId = original.Id,
                OccurredAt = occurredAt,
                CreatedByUserId = actorUserId,
                CreatedAt = now
            };
            dbContext.TreatmentLineageMovements.Add(reversal);
            await dbContext.SaveChangesAsync(cancellationToken);
            lastMovementId = reversal.Id;
        }
        return new(true, null, lastMovementId);
    }

    public async Task<TreatmentLineageWriteResult> AddUnknownAsync(RoomInventoryLedgerSnapshot snapshot, int bins, string operationKey, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken)
    {
        if (bins <= 0) return new(true, null);
        if (await dbContext.TreatmentLineageMovements.AsNoTracking().AnyAsync(x => x.OperationKey == operationKey, cancellationToken)) return new(true, null);
        var segment = await GetOrCreateSegmentAsync(snapshot, TreatmentLineageStates.Unknown, "x", businessTime.UtcNow, cancellationToken);
        segment.CurrentBins += bins;
        segment.UpdatedAt = businessTime.UtcNow;
        segment.ConcurrencyVersion++;
        dbContext.TreatmentLineageMovements.Add(new TreatmentLineageMovement
        {
            OperationKey = operationKey,
            MovementType = TreatmentLineageMovementTypes.ManualTrueUp,
            DestinationSegment = segment,
            DestinationRoomId = segment.RoomId,
            IdentityKey = segment.IdentityKey,
            TreatmentStateSnapshot = segment.TreatmentState,
            TreatmentSignatureSnapshot = segment.TreatmentSignature,
            BinCount = bins,
            OccurredAt = occurredAt,
            CreatedByUserId = actorUserId,
            CreatedAt = businessTime.UtcNow
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(true, null);
    }

    private async Task<(IReadOnlyList<RoomInventoryLedgerSnapshot> Snapshots, string? Error)> ResolveApplicationSnapshotAsync(int roomId, DateTimeOffset appliedAt, CancellationToken cancellationToken)
    {
        var appliedAtUtc = appliedAt.ToUniversalTime();
        if (appliedAtUtc > businessTime.UtcNow.AddMinutes(5)) return ([], "Application date/time cannot be in the future.");
        var snapshots = (await ledger.GetSnapshotsAsOfAsync(null, [roomId], appliedAtUtc, cancellationToken)).Where(x => x.CurrentBins > 0).ToList();
        var laterAmbiguousEvents = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .AnyAsync(x => x.RoomId == roomId
                && x.AdjustmentAt > appliedAtUtc
                && x.AdjustmentAt <= businessTime.UtcNow
                && !(x.ChangeAmount > 0 && x.ReceiptId != null), cancellationToken);
        var laterTreatment = await dbContext.RoomTreatmentApplications.AsNoTracking()
            .AnyAsync(x => x.RoomId == roomId && x.AppliedAt > appliedAtUtc && x.AppliedAt <= businessTime.UtcNow, cancellationToken);
        if (laterAmbiguousEvents || laterTreatment)
        {
            return (snapshots, "Crop QC cannot determine the exact room contents at this application time. Review the room transaction history before recording the treatment.");
        }
        return (snapshots, null);
    }

    private async Task<List<TreatmentLineageSegment>> MaterializeAsync(RoomInventoryLedgerSnapshot snapshot, CancellationToken cancellationToken)
    {
        var key = IdentityKey(snapshot);
        var segments = await dbContext.TreatmentLineageSegments.Include(x => x.Applications)
            .Where(x => x.RoomId == snapshot.RoomId && x.IdentityKey == key)
            .ToListAsync(cancellationToken);
        var explicitBins = segments.Sum(x => x.CurrentBins);
        if (explicitBins > snapshot.CurrentBins) throw new InvalidOperationException($"Treatment lineage exceeds authoritative inventory for room {snapshot.RoomId}, identity {key}.");
        var missing = snapshot.CurrentBins - explicitBins;
        if (missing > 0)
        {
            var untreated = segments.SingleOrDefault(x => x.TreatmentSignature == "u")
                ?? await GetOrCreateSegmentAsync(snapshot, TreatmentLineageStates.Untreated, "u", businessTime.UtcNow, cancellationToken);
            untreated.CurrentBins += missing;
            untreated.UpdatedAt = businessTime.UtcNow;
            untreated.ConcurrencyVersion++;
            if (!segments.Contains(untreated)) segments.Add(untreated);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        return segments;
    }

    private async Task<Dictionary<string, List<CurrentTreatmentSegmentViewModel>>> ProjectSelectionsBatchAsync(
        IReadOnlyList<RoomInventoryLedgerSnapshot> snapshots,
        CancellationToken cancellationToken)
    {
        var result = snapshots.ToDictionary(x => SelectionLookupKey(x), _ => new List<CurrentTreatmentSegmentViewModel>());
        if (snapshots.Count == 0) return result;
        var roomIds = snapshots.Select(x => x.RoomId).Distinct().ToList();
        var identityKeys = snapshots.Select(IdentityKey).Distinct().ToList();
        var segments = await dbContext.TreatmentLineageSegments.AsNoTracking()
            .Include(x => x.Applications)
            .ThenInclude(x => x.RoomTreatmentApplication)
            .Where(x => roomIds.Contains(x.RoomId) && identityKeys.Contains(x.IdentityKey) && x.CurrentBins > 0)
            .ToListAsync(cancellationToken);

        foreach (var snapshot in snapshots)
        {
            var key = IdentityKey(snapshot);
            var output = result[SelectionLookupKey(snapshot)];
            foreach (var segment in segments.Where(x => x.RoomId == snapshot.RoomId && x.IdentityKey == key))
            {
                var applications = segment.Applications.OrderBy(x => x.Sequence).Select(x => new TreatmentApplicationSummaryViewModel(
                    x.RoomTreatmentApplicationId,
                    x.RoomTreatmentApplication.AppliedAt,
                    x.RoomTreatmentApplication.ProductNameSnapshot,
                    x.RoomTreatmentApplication.CommonNameSnapshot,
                    x.RoomTreatmentApplication.ReversedAt is not null)).ToList();
                output.Add(new CurrentTreatmentSegmentViewModel(
                    segment.Id, key, snapshot.GrowerNumber ?? snapshot.Lot, snapshot.Grower, snapshot.VarietyName,
                    snapshot.ProductionType, snapshot.IsOrganic, segment.CurrentBins, segment.TreatmentState,
                    segment.TreatmentSignature, applications));
            }
            var implicitBins = snapshot.CurrentBins - output.Sum(x => x.Bins);
            if (implicitBins < 0) throw new InvalidOperationException($"Treatment lineage exceeds authoritative inventory for room {snapshot.RoomId}, identity {key}.");
            if (implicitBins > 0)
            {
                output.Add(new CurrentTreatmentSegmentViewModel(null, key, snapshot.GrowerNumber ?? snapshot.Lot, snapshot.Grower,
                    snapshot.VarietyName, snapshot.ProductionType, snapshot.IsOrganic, implicitBins,
                    TreatmentLineageStates.Untreated, "u", []));
            }
        }
        return result;
    }

    private async Task<TreatmentLineageSegment> GetOrCreateSegmentAsync(RoomInventoryLedgerSnapshot snapshot, string state, string signature, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var key = IdentityKey(snapshot);
        var existing = await dbContext.TreatmentLineageSegments.Include(x => x.Applications)
            .SingleOrDefaultAsync(x => x.RoomId == snapshot.RoomId && x.IdentityKey == key && x.TreatmentSignature == signature, cancellationToken);
        if (existing is not null) return existing;
        var segment = new TreatmentLineageSegment
        {
            WarehouseId = snapshot.WarehouseId,
            RoomId = snapshot.RoomId,
            CropYear = snapshot.CropYear,
            GrowerLotId = snapshot.GrowerLotId,
            FruitProfileId = snapshot.FruitProfileId,
            IdentityKey = key,
            GrowerNumberSnapshot = Normalize(snapshot.GrowerNumber),
            GrowerNameSnapshot = snapshot.Grower,
            LotNumberSnapshot = snapshot.Lot,
            VarietyCodeSnapshot = snapshot.Variety,
            ProductionTypeSnapshot = snapshot.ProductionType,
            IsOrganicSnapshot = snapshot.IsOrganic,
            InventoryStatusSnapshot = Normalize(snapshot.InventoryStatus),
            TreatmentState = state,
            TreatmentSignature = signature,
            CurrentBins = 0,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.TreatmentLineageSegments.Add(segment);
        await dbContext.SaveChangesAsync(cancellationToken);
        return segment;
    }

    private async Task CopyApplicationLinksAsync(TreatmentLineageSegment source, TreatmentLineageSegment destination, CancellationToken cancellationToken)
    {
        if (source.Applications.Count == 0)
        {
            await dbContext.Entry(source).Collection(x => x.Applications).LoadAsync(cancellationToken);
        }
        foreach (var link in source.Applications.OrderBy(x => x.Sequence)) EnsureApplicationLink(destination, link.RoomTreatmentApplicationId);
    }

    private static void EnsureApplicationLink(TreatmentLineageSegment segment, long applicationId)
    {
        if (segment.Applications.Any(x => x.RoomTreatmentApplicationId == applicationId)) return;
        segment.Applications.Add(new TreatmentLineageSegmentApplication
        {
            RoomTreatmentApplicationId = applicationId,
            Sequence = segment.Applications.Count == 0 ? 1 : segment.Applications.Max(x => x.Sequence) + 1
        });
    }

    private async Task ValidateRoomsAsync(IReadOnlyCollection<int> roomIds, CancellationToken cancellationToken)
    {
        if (roomIds.Count == 0) return;
        var snapshots = await ledger.GetSnapshotsAsync(null, roomIds, cancellationToken);
        var authoritative = snapshots.Where(x => x.CurrentBins > 0).ToDictionary(x => (x.RoomId, IdentityKey(x)), x => x.CurrentBins);
        var explicitRows = await dbContext.TreatmentLineageSegments.AsNoTracking()
            .Where(x => roomIds.Contains(x.RoomId) && x.CurrentBins > 0)
            .GroupBy(x => new { x.RoomId, x.IdentityKey })
            .Select(x => new { x.Key.RoomId, x.Key.IdentityKey, Bins = x.Sum(y => y.CurrentBins) })
            .ToListAsync(cancellationToken);
        foreach (var row in explicitRows)
        {
            if (!authoritative.TryGetValue((row.RoomId, row.IdentityKey), out var bins) || row.Bins > bins)
            {
                throw new InvalidOperationException($"Treatment lineage does not reconcile with authoritative inventory for room {row.RoomId}, identity {row.IdentityKey}.");
            }
        }
    }

    public static string IdentityKey(RoomInventoryLedgerSnapshot snapshot) =>
        string.Join('|', snapshot.CropYear?.ToString() ?? "-", snapshot.GrowerLotId?.ToString() ?? "-",
            snapshot.FruitProfileId?.ToString() ?? "-", NormalizeKey(snapshot.GrowerNumber ?? snapshot.Lot),
            NormalizeKey(snapshot.Lot), NormalizeKey(snapshot.Variety), NormalizeKey(snapshot.ProductionType),
            snapshot.IsOrganic?.ToString() ?? "-", NormalizeKey(snapshot.InventoryStatus));

    public static string SelectionLookupKey(RoomInventoryLedgerSnapshot snapshot) => $"{snapshot.RoomId}:{IdentityKey(snapshot)}";

    private static string AppendApplication(string signature, long id) =>
        signature.Contains("|a:", StringComparison.Ordinal)
            ? $"{signature},{id}"
            : $"{(signature.StartsWith('x') ? "x" : "u")}|a:{id}";

    private static RoomInventoryLedgerSnapshot ToSnapshot(TreatmentLineageSegment segment) => new(
        segment.WarehouseId, "", segment.RoomId, "", "", segment.CropYear, segment.GrowerLotId,
        segment.FruitProfileId, segment.GrowerNameSnapshot, segment.GrowerNumberSnapshot,
        segment.LotNumberSnapshot, null, segment.VarietyCodeSnapshot, segment.VarietyCodeSnapshot,
        segment.VarietyCodeSnapshot, CropFromProduction(segment.ProductionTypeSnapshot), segment.ProductionTypeSnapshot,
        segment.IsOrganicSnapshot, segment.InventoryStatusSnapshot ?? "", 0, 0, 0, 0, 0, 0, 0, 0, 0,
        segment.CurrentBins, 0, segment.CreatedAt, segment.UpdatedAt, 0);

    private static string CropFromProduction(string productionType) => productionType.Contains("Pear", StringComparison.OrdinalIgnoreCase) ? "Pear" : "Apple";
    private static RoomTreatmentFruitSnapshotViewModel ToFruitView(RoomInventoryLedgerSnapshot x) =>
        new(IdentityKey(x), x.GrowerNumber ?? x.Lot, x.Grower, x.VarietyName, x.ProductionType, x.IsOrganic, x.CurrentBins);
    private static string? ResolveWholeRoomCrop(IReadOnlyList<RoomInventoryLedgerSnapshot> snapshots)
    {
        var crops = snapshots.Select(x => x.FruitType.Trim().ToLowerInvariant() switch { "apple" or "apples" => "Apples", "pear" or "pears" => "Pears", _ => "" })
            .Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return crops.Count == 1 && snapshots.All(x => !string.IsNullOrWhiteSpace(x.FruitType)) ? crops[0] : null;
    }
    private static string SegmentLabel(CurrentTreatmentSegmentViewModel x) => x.TreatmentState == TreatmentLineageStates.Untreated
        ? "Untreated"
        : x.TreatmentState == TreatmentLineageStates.Unknown
            ? x.Treatments.Count == 0 ? "Treatment status unknown / unconfirmed" : $"Unconfirmed prior status; {string.Join(" + ", x.Treatments.Select(t => t.CommonName ?? t.ProductName))}"
            : string.Join(" + ", x.Treatments.Select(t => t.CommonName ?? t.ProductName));
    private async Task<User?> CurrentUserAsync(CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        var email = principal?.FindFirstValue(ClaimTypes.Email) ?? principal?.Identity?.Name;
        return string.IsNullOrWhiteSpace(email) ? null : await dbContext.Users.SingleOrDefaultAsync(x => x.Email == email && x.IsActive, cancellationToken);
    }
    private static string NormalizeKey(string? value) => (value ?? "").Trim().ToUpperInvariant();
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
