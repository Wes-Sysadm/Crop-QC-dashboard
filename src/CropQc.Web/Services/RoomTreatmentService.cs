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
    string Label,
    long? ReceiptId = null,
    long? SegmentId = null,
    bool IsAvailable = true,
    string? UnavailableReason = null,
    int? ExplicitBins = null);

public sealed record ActualRunTreatmentRestorationSource(
    long BinsRunEntryId,
    RoomInventoryLedgerSnapshot Snapshot,
    string TreatmentSignature,
    string TreatmentState,
    string TreatmentSummary,
    int Bins);

public sealed record TreatmentLineageWriteResult(bool Success, string? Error, long? MovementId = null);

public interface IRoomTreatmentService
{
    Task<RoomTreatmentApplyPageViewModel> GetApplyPageAsync(RoomTreatmentApplyForm form, bool review, CancellationToken cancellationToken);
    Task<(string? Error, long? ApplicationId)> ApplyAsync(RoomTreatmentApplyForm form, CancellationToken cancellationToken);
    Task<string?> ReverseAsync(ReverseRoomTreatmentApplicationForm form, CancellationToken cancellationToken);
    Task<RoomTreatmentData> GetRoomDataAsync(int roomId, CancellationToken cancellationToken);
    Task<IReadOnlyList<TreatmentSegmentSelection>> GetSelectionsAsync(RoomInventoryLedgerSnapshot snapshot, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<string, IReadOnlyList<TreatmentSegmentSelection>>> GetSelectionsAsync(IReadOnlyList<RoomInventoryLedgerSnapshot> snapshots, CancellationToken cancellationToken);
    async Task<IReadOnlyDictionary<string, IReadOnlyList<TreatmentSegmentSelection>>> GetActualRunCorrectionSelectionsAsync(
        IReadOnlyList<RoomInventoryLedgerSnapshot> snapshots,
        IReadOnlyList<ActualRunTreatmentRestorationSource> restorationSources,
        CancellationToken cancellationToken)
    {
        var result = (await GetSelectionsAsync(snapshots, cancellationToken))
            .ToDictionary(x => x.Key, x => x.Value.ToList(), StringComparer.OrdinalIgnoreCase);
        foreach (var source in restorationSources)
        {
            var key = RoomTreatmentService.SelectionLookupKey(source.Snapshot);
            if (!result.TryGetValue(key, out var selections))
            {
                result[key] =
                [
                    new TreatmentSegmentSelection(
                        RoomTreatmentService.IdentityKey(source.Snapshot),
                        source.TreatmentSignature,
                        source.TreatmentState,
                        0,
                        "Needs Review — unavailable for Actual Run correction",
                        IsAvailable: false,
                        UnavailableReason: $"Treatment provenance for Actual Run entry #{source.BinsRunEntryId} could not be resolved exactly.")
                ];
                continue;
            }

            var matches = selections.Where(x =>
                    string.Equals(x.TreatmentSignature, source.TreatmentSignature, StringComparison.Ordinal))
                .ToList();
            if (matches.Count > 1)
            {
                var implicitMatch = matches.Where(x => x.SegmentId is null && x.ReceiptId is null).ToList();
                if (implicitMatch.Count == 1)
                {
                    matches = implicitMatch;
                }
            }
            if (matches.Count != 1)
            {
                selections.Add(new TreatmentSegmentSelection(
                    RoomTreatmentService.IdentityKey(source.Snapshot),
                    source.TreatmentSignature,
                    source.TreatmentState,
                    0,
                    "Needs Review — unavailable for Actual Run correction",
                    IsAvailable: false,
                    UnavailableReason: $"Treatment provenance for Actual Run entry #{source.BinsRunEntryId} could not be resolved exactly."));
                continue;
            }

            var match = matches[0];
            selections[selections.IndexOf(match)] = match with { CurrentBins = match.CurrentBins + source.Bins };
        }
        return result.ToDictionary(x => x.Key, x => (IReadOnlyList<TreatmentSegmentSelection>)x.Value, StringComparer.OrdinalIgnoreCase);
    }
    Task<TreatmentLineageWriteResult> MoveAsync(RoomInventoryLedgerSnapshot snapshot, string? treatmentSignature, int bins, int? destinationWarehouseId, int? destinationRoomId, string operationKey, string movementType, long? roomTransferId, long? roomInventoryLossId, long? binsRunEntryId, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken);
    Task<TreatmentLineageWriteResult> MoveSelectedAsync(RoomInventoryLedgerSnapshot snapshot, string? treatmentSignature, long? treatmentSegmentId, long? treatmentReceiptId, int bins, int? destinationWarehouseId, int? destinationRoomId, string operationKey, string movementType, long? roomTransferId, long? roomInventoryLossId, long? binsRunEntryId, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken) =>
        MoveAsync(snapshot, treatmentSignature, bins, destinationWarehouseId, destinationRoomId, operationKey, movementType, roomTransferId, roomInventoryLossId, binsRunEntryId, occurredAt, actorUserId, cancellationToken);
    Task<TreatmentLineageWriteResult> ReverseMovementsAsync(string operationKeyPrefix, string movementType, long? roomTransferId, long? roomInventoryLossId, long? binsRunEntryId, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken);
    Task<TreatmentLineageWriteResult> ReclassifyIdentityAsync(
        RoomInventoryLedgerSnapshot source,
        RoomInventoryLedgerSnapshot target,
        InventoryIdentityCorrection correction,
        DateTimeOffset occurredAt,
        int actorUserId,
        CancellationToken cancellationToken) =>
        Task.FromResult(new TreatmentLineageWriteResult(false, "Treatment identity reclassification is not supported by this implementation."));
    Task<TreatmentLineageWriteResult> CorrectReceiptLocationAsync(
        RoomInventoryLedgerSnapshot source,
        RoomInventoryLedgerSnapshot target,
        long receiptId,
        string operationKey,
        DateTimeOffset occurredAt,
        int actorUserId,
        CancellationToken cancellationToken) =>
        Task.FromResult(new TreatmentLineageWriteResult(false, "Receipt location correction is not supported by this implementation."));
    Task<TreatmentLineageWriteResult> AddUnknownAsync(RoomInventoryLedgerSnapshot snapshot, int bins, string operationKey, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken);
}

public interface IProcessorTreatmentLineageService
{
    Task<TreatmentLineageWriteResult> MoveToProcessorAsync(RoomInventoryLedgerSnapshot snapshot, string? treatmentSignature, int bins, string operationKey, long processorShipmentLineId, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken);
    Task<TreatmentLineageWriteResult> MoveSelectedToProcessorAsync(RoomInventoryLedgerSnapshot snapshot, string? treatmentSignature, long? treatmentSegmentId, long? treatmentReceiptId, int bins, string operationKey, long processorShipmentLineId, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken) =>
        MoveToProcessorAsync(snapshot, treatmentSignature, bins, operationKey, processorShipmentLineId, occurredAt, actorUserId, cancellationToken);
    Task<TreatmentLineageWriteResult> ReverseProcessorMovementAsync(string operationKeyPrefix, long processorShipmentLineId, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken);
}

public interface IOutsideWarehouseTreatmentLineageService
{
    Task<TreatmentLineageWriteResult> MoveToOutsideWarehouseAsync(
        RoomInventoryLedgerSnapshot snapshot,
        string treatmentSignature,
        int bins,
        string operationKey,
        long outsideWarehouseTransferId,
        DateTimeOffset occurredAt,
        int? actorUserId,
        CancellationToken cancellationToken);

    Task<TreatmentLineageWriteResult> ReverseOutsideWarehouseMovementAsync(
        string operationKeyPrefix,
        long outsideWarehouseTransferId,
        DateTimeOffset occurredAt,
        int? actorUserId,
        CancellationToken cancellationToken);
}

public interface IInterCrewTreatmentLineageService
{
    Task<TreatmentLineageWriteResult> DispatchAsync(RoomInventoryLedgerSnapshot snapshot, string treatmentSignature, int bins, string operationKey, long transferId, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken);
    Task<TreatmentLineageWriteResult> ReceiveAsync(long transferId, int destinationWarehouseId, int destinationRoomId, int binsReceived, string operationKey, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken);
    Task<TreatmentLineageWriteResult> ReverseAsync(long transferId, bool wasReceived, string operationKey, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken);
}

public interface IReceivingTreatmentService
{
    Task<ReceiptTreatmentApplyPageViewModel> GetReceiptApplyPageAsync(ReceiptTreatmentApplyForm form, bool review, CancellationToken cancellationToken);
    Task<(string? Error, long? ApplicationId)> ApplyReceiptAsync(ReceiptTreatmentApplyForm form, CancellationToken cancellationToken);
    Task<string?> ReverseReceiptAsync(ReverseRoomTreatmentApplicationForm form, CancellationToken cancellationToken);
}

public sealed class RoomTreatmentService(
    CropQcDbContext dbContext,
    IRoomInventoryLedgerQueryService ledger,
    IUserAccessService access,
    IHttpContextAccessor httpContextAccessor,
    IBusinessTimeService businessTime,
    ILogger<RoomTreatmentService> logger) : IRoomTreatmentService, IReceivingTreatmentService, IProcessorTreatmentLineageService, IOutsideWarehouseTreatmentLineageService, IInterCrewTreatmentLineageService
{
    private const string SourceApplication = "CropQc.Web room treatment workflow";
    private static readonly JsonSerializerOptions AuditJson = new(JsonSerializerDefaults.Web);

    private sealed record ReceiptApplicationSnapshot(
        Receipt? Receipt,
        RoomInventoryLedgerSnapshot? Inventory,
        int CurrentBins,
        string? Error);

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
                .Where(x => x.IsActive && x.ApplicationLevel == TreatmentApplicationLevels.Room && x.Crop == crop)
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

    public async Task<ReceiptTreatmentApplyPageViewModel> GetReceiptApplyPageAsync(
        ReceiptTreatmentApplyForm form,
        bool review,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveReceiptApplicationSnapshotAsync(form.ReceiptId, form.AppliedAt, cancellationToken);
        if (resolved.Receipt is null)
        {
            return new() { Form = form, Error = resolved.Error ?? "Receipt was not found." };
        }

        var receipt = resolved.Receipt;
        var crop = NormalizeCrop(receipt.FruitProfile.FruitType);
        var chemicals = crop is null
            ? []
            : await dbContext.TreatmentChemicals.AsNoTracking()
                .Where(x => x.IsActive && x.ApplicationLevel == TreatmentApplicationLevels.Receiving && x.Crop == crop)
                .OrderBy(x => x.CommonName ?? x.ProductName)
                .ThenBy(x => x.ProductName)
                .Select(x => new TreatmentChemicalOptionViewModel(x.Id, x.ProductName, x.CommonName, x.Crop, x.Volume, x.Unit, x.UnitPrice, x.Currency))
                .ToListAsync(cancellationToken);
        var selected = chemicals.SingleOrDefault(x => x.Id == form.TreatmentChemicalId);
        var error = resolved.Error;
        if (error is null && crop is null)
        {
            error = "The Receipt crop could not be resolved to Apples or Pears.";
        }
        else if (review && error is null && selected is null)
        {
            error = "Select an active Receiving treatment that is valid for this Receipt crop.";
        }

        return new ReceiptTreatmentApplyPageViewModel
        {
            Form = form,
            ReceiptNumber = receipt.CompuTechReceiptId,
            Grower = receipt.GrowerName,
            GrowerNumber = receipt.GrowerNumber ?? receipt.LotCode,
            Warehouse = resolved.Inventory?.Facility ?? receipt.Warehouse.Code,
            Room = resolved.Inventory?.Room ?? receipt.Room.CropQcRoomName ?? receipt.Room.DisplayName ?? receipt.Room.Code,
            Variety = receipt.FruitProfile.Name,
            ProductionType = receipt.FruitProfile.ProductionType,
            TotalBins = resolved.CurrentBins,
            Error = error,
            IsReview = review && error is null && selected is not null,
            SelectedTreatment = selected,
            EstimatedCost = selected is null ? 0 : decimal.Round(resolved.CurrentBins * selected.UnitPrice, 2),
            TreatmentOptions = chemicals
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
            var chemical = await dbContext.TreatmentChemicals.SingleOrDefaultAsync(x => x.Id == form.TreatmentChemicalId
                && x.IsActive && x.ApplicationLevel == TreatmentApplicationLevels.Room, cancellationToken);
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
            foreach (var inventory in snapshotResult.Snapshots)
            {
                var identityError = await InventoryIdentityWriteGuard.RejectSupersededAsync(
                    dbContext, inventory.CropYear, inventory.GrowerLotId, inventory.FruitProfileId,
                    "Room treatment application", cancellationToken);
                if (identityError is not null) return (identityError, null);
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
                ApplicationLevel = TreatmentApplicationLevels.Room,
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
                    var target = await GetOrCreateSegmentAsync(snapshot, segment.TreatmentState == TreatmentLineageStates.Unknown ? TreatmentLineageStates.Unknown : TreatmentLineageStates.Confirmed, resultSignature, now, cancellationToken, segment.ReceiptId);
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
                        ReceiptId = segment.ReceiptId,
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

    public async Task<(string? Error, long? ApplicationId)> ApplyReceiptAsync(
        ReceiptTreatmentApplyForm form,
        CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal is null || !await access.HasAccessAsync(principal, ApplicationAreas.Receipts, PageAccessLevel.Edit, cancellationToken))
        {
            return ("Receipts Edit access is required to apply a Receiving treatment.", null);
        }
        if (!form.ConfirmedReview) return ("Review the treatment and exact Receipt snapshot before saving.", null);
        if (string.IsNullOrWhiteSpace(form.OperationKey) || form.OperationKey.Trim().Length > 100)
            return ("A valid treatment operation key is required.", null);
        if (form.Notes?.Trim().Length > 1000) return ("Notes cannot exceed 1000 characters.", null);
        var actor = await CurrentUserAsync(cancellationToken);
        if (actor is null) return ("The active user record could not be resolved.", null);

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            : null;
        try
        {
            var operationKey = form.OperationKey.Trim();
            var existing = await dbContext.RoomTreatmentApplications.AsNoTracking()
                .SingleOrDefaultAsync(x => x.OperationKey == operationKey, cancellationToken);
            if (existing is not null)
            {
                return existing.ApplicationLevel == TreatmentApplicationLevels.Receiving
                    && existing.ReceiptId == form.ReceiptId
                    && existing.TreatmentChemicalId == form.TreatmentChemicalId
                    && existing.AppliedAt.ToUniversalTime() == form.AppliedAt.ToUniversalTime()
                    && existing.Notes == Normalize(form.Notes)
                    ? (null, existing.Id)
                    : ("The operation key already belongs to a different treatment application.", null);
            }

            var resolved = await ResolveReceiptApplicationSnapshotAsync(form.ReceiptId, form.AppliedAt, cancellationToken);
            if (resolved.Error is not null || resolved.Receipt is null || resolved.Inventory is null)
                return (resolved.Error ?? "The exact Receipt inventory could not be resolved.", null);
            var receipt = resolved.Receipt;
            var snapshot = resolved.Inventory;
            var identityError = await InventoryIdentityWriteGuard.RejectSupersededAsync(
                dbContext, snapshot.CropYear, snapshot.GrowerLotId, snapshot.FruitProfileId,
                "Receipt treatment application", cancellationToken);
            if (identityError is not null) return (identityError, null);
            var crop = NormalizeCrop(receipt.FruitProfile.FruitType);
            var chemical = await dbContext.TreatmentChemicals.SingleOrDefaultAsync(x => x.Id == form.TreatmentChemicalId
                && x.IsActive && x.ApplicationLevel == TreatmentApplicationLevels.Receiving, cancellationToken);
            if (chemical is null || crop is null || !string.Equals(chemical.Crop, crop, StringComparison.OrdinalIgnoreCase))
                return ("The selected chemical is not an active Receiving treatment for this Receipt crop.", null);

            var segments = await MaterializeAsync(snapshot, cancellationToken);
            var exactSegments = segments.Where(x => x.ReceiptId == receipt.Id && x.CurrentBins > 0).ToList();
            var exactBins = exactSegments.Sum(x => x.CurrentBins);
            if (exactBins > resolved.CurrentBins)
                return ("Treatment lineage for this Receipt exceeds its authoritative current inventory. No changes were made.", null);
            var missingBins = resolved.CurrentBins - exactBins;
            TreatmentLineageSegment? unassignedSource = null;
            if (missingBins > 0)
            {
                var unassigned = segments.Where(x => x.ReceiptId == null && x.CurrentBins > 0).ToList();
                var signatures = unassigned.Select(x => x.TreatmentSignature).Distinct(StringComparer.Ordinal).ToList();
                if (signatures.Count != 1)
                    return ("The Receipt shares this room identity across multiple historical treatment states. Crop QC cannot guess which bins belong to the Receipt.", null);
                unassignedSource = unassigned.Single();
                if (unassignedSource.CurrentBins < missingBins)
                    return ("The room treatment lineage does not contain enough unassigned bins to prove this exact Receipt quantity.", null);
            }

            var now = businessTime.UtcNow;
            var application = new RoomTreatmentApplication
            {
                OperationKey = operationKey,
                TreatmentChemicalId = chemical.Id,
                ApplicationLevel = TreatmentApplicationLevels.Receiving,
                ReceiptId = receipt.Id,
                WarehouseId = snapshot.WarehouseId,
                RoomId = snapshot.RoomId,
                AppliedAt = form.AppliedAt.ToUniversalTime(),
                AppliedByUserId = actor.Id,
                Notes = Normalize(form.Notes),
                TotalBinsSnapshot = resolved.CurrentBins,
                ProductNameSnapshot = chemical.ProductName,
                CommonNameSnapshot = Normalize(chemical.CommonName),
                CropSnapshot = chemical.Crop,
                VolumeSnapshot = chemical.Volume,
                UnitSnapshot = chemical.Unit,
                UnitPriceSnapshot = chemical.UnitPrice,
                CurrencySnapshot = chemical.Currency,
                EstimatedCostSnapshot = decimal.Round(resolved.CurrentBins * chemical.UnitPrice, 2),
                CreatedAt = now,
                CreatedByUserId = actor.Id
            };
            dbContext.RoomTreatmentApplications.Add(application);
            await dbContext.SaveChangesAsync(cancellationToken);

            var sources = exactSegments.Select(x => (Segment: x, Bins: x.CurrentBins)).ToList();
            if (unassignedSource is not null) sources.Add((unassignedSource, missingBins));
            foreach (var source in sources.Where(x => x.Bins > 0))
            {
                var resultSignature = AppendApplication(source.Segment.TreatmentSignature, application.Id);
                var state = source.Segment.TreatmentState == TreatmentLineageStates.Unknown
                    ? TreatmentLineageStates.Unknown
                    : TreatmentLineageStates.Confirmed;
                var target = await GetOrCreateSegmentAsync(snapshot, state, resultSignature, now, cancellationToken, receipt.Id);
                await CopyApplicationLinksAsync(source.Segment, target, cancellationToken);
                EnsureApplicationLink(target, application.Id);
                target.CurrentBins += source.Bins;
                target.UpdatedAt = now;
                target.ConcurrencyVersion++;
                source.Segment.CurrentBins -= source.Bins;
                source.Segment.UpdatedAt = now;
                source.Segment.ConcurrencyVersion++;
                application.Sources.Add(new RoomTreatmentApplicationSource
                {
                    ReceiptId = receipt.Id,
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
                    BinsTreated = source.Bins,
                    PriorTreatmentSignature = source.Segment.TreatmentSignature,
                    ResultTreatmentSignature = resultSignature
                });
            }

            dbContext.AuditLogs.Add(new AuditLog
            {
                UserId = actor.Id,
                Action = "ApplyReceivingTreatment",
                EntityName = nameof(RoomTreatmentApplication),
                EntityKey = application.Id.ToString(),
                AfterValuesJson = JsonSerializer.Serialize(new
                {
                    TreatmentApplicationId = application.Id,
                    ReceiptId = receipt.Id,
                    Receipt = receipt.CompuTechReceiptId,
                    Room = $"{snapshot.Facility}/{snapshot.Room}",
                    application.AppliedAt,
                    Chemical = application.ProductNameSnapshot,
                    AffectedBins = resolved.CurrentBins,
                    InventoryDelta = 0,
                    Actor = actor.Email
                }, AuditJson),
                SourceApplication = SourceApplication,
                CreatedAt = now
            });
            await dbContext.SaveChangesAsync(cancellationToken);
            await ValidateRoomsAsync([snapshot.RoomId], cancellationToken);
            if (transaction is not null) await transaction.CommitAsync(cancellationToken);
            return (null, application.Id);
        }
        catch (Exception exception)
        {
            if (transaction is not null) await transaction.RollbackAsync(cancellationToken);
            dbContext.ChangeTracker.Clear();
            logger.LogError(exception, "Receiving treatment application failed and was rolled back. ReceiptId={ReceiptId}", form.ReceiptId);
            return ("Receiving treatment application failed and was rolled back. Review restricted logs.", null);
        }
    }

    public Task<string?> ReverseAsync(ReverseRoomTreatmentApplicationForm form, CancellationToken cancellationToken) =>
        ReverseCoreAsync(form, TreatmentApplicationLevels.Room, ApplicationAreas.RoomTransactions, "Room Transactions", cancellationToken);

    public Task<string?> ReverseReceiptAsync(ReverseRoomTreatmentApplicationForm form, CancellationToken cancellationToken) =>
        ReverseCoreAsync(form, TreatmentApplicationLevels.Receiving, ApplicationAreas.Receipts, "Receipts", cancellationToken);

    private async Task<string?> ReverseCoreAsync(
        ReverseRoomTreatmentApplicationForm form,
        string applicationLevel,
        string permissionArea,
        string permissionLabel,
        CancellationToken cancellationToken)
    {
        var principal = httpContextAccessor.HttpContext?.User;
        if (principal is null || !await access.HasAccessAsync(principal, permissionArea, PageAccessLevel.Admin, cancellationToken))
        {
            return $"{permissionLabel} Admin access is required to reverse this treatment application.";
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
            if (application.ApplicationLevel != applicationLevel)
                return $"This is not a {applicationLevel} treatment application.";
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
                var target = await GetOrCreateSegmentAsync(snapshot, state, signature, now, cancellationToken, segment.ReceiptId);
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
        var current = projected.Values.SelectMany(x => x).ToList();

        var historyRows = await dbContext.RoomTreatmentApplications.AsNoTracking()
            .Include(x => x.AppliedByUser)
            .Include(x => x.Attachments)
            .Where(x => x.RoomId == roomId && x.ApplicationLevel == TreatmentApplicationLevels.Room)
            .OrderByDescending(x => x.AppliedAt).ThenByDescending(x => x.Id)
            .Take(200)
            .ToListAsync(cancellationToken);
        var history = historyRows.Select(x => new RoomTreatmentApplicationHistoryViewModel(
                x.Id, x.AppliedAt, x.ProductNameSnapshot, x.CommonNameSnapshot, x.TotalBinsSnapshot,
                x.AppliedByUser.DisplayName ?? x.AppliedByUser.Email, x.EstimatedCostSnapshot, x.CurrencySnapshot,
                x.Notes, x.ReversedAt != null, x.ReversedAt, x.ReversalReason,
                x.Attachments.Where(a => !a.IsDeleted).OrderBy(a => a.CreatedAt).ThenBy(a => a.Id)
                    .Select(a => new TreatmentReportAttachmentViewModel(a.Id, a.FileName, a.ContentType, a.FileSizeBytes, a.CreatedAt)).ToList()))
            .ToList();
        var principal = httpContextAccessor.HttpContext?.User;
        var canApply = principal is not null && await access.HasAccessAsync(principal, ApplicationAreas.RoomTransactions, PageAccessLevel.Edit, cancellationToken);
        var canReverse = principal is not null && await access.HasAccessAsync(principal, ApplicationAreas.RoomTransactions, PageAccessLevel.Admin, cancellationToken);
        return new(current, history, canApply, canReverse);
    }

    public async Task<IReadOnlyList<TreatmentSegmentSelection>> GetSelectionsAsync(RoomInventoryLedgerSnapshot snapshot, CancellationToken cancellationToken)
    {
        var projected = (await ProjectSelectionsBatchAsync([snapshot], cancellationToken))[SelectionLookupKey(snapshot)];
        return projected.Select(ToSelection).ToList();
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<TreatmentSegmentSelection>>> GetSelectionsAsync(
        IReadOnlyList<RoomInventoryLedgerSnapshot> snapshots,
        CancellationToken cancellationToken)
    {
        var active = snapshots.Where(x => x.CurrentBins > 0).ToList();
        var projected = await ProjectSelectionsBatchAsync(active, cancellationToken);
        return projected.ToDictionary(
            x => x.Key,
            x => (IReadOnlyList<TreatmentSegmentSelection>)x.Value.Select(ToSelection).ToList());
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<TreatmentSegmentSelection>>> GetActualRunCorrectionSelectionsAsync(
        IReadOnlyList<RoomInventoryLedgerSnapshot> snapshots,
        IReadOnlyList<ActualRunTreatmentRestorationSource> restorationSources,
        CancellationToken cancellationToken)
    {
        var result = (await GetSelectionsAsync(snapshots, cancellationToken))
            .ToDictionary(x => x.Key, x => x.Value.ToList(), StringComparer.OrdinalIgnoreCase);
        if (restorationSources.Count == 0)
        {
            return result.ToDictionary(x => x.Key, x => (IReadOnlyList<TreatmentSegmentSelection>)x.Value, StringComparer.OrdinalIgnoreCase);
        }

        var entryIds = restorationSources.Select(x => x.BinsRunEntryId).Distinct().ToList();
        var movements = await dbContext.TreatmentLineageMovements.AsNoTracking()
            .Include(x => x.SourceSegment)
            .Where(x => x.BinsRunEntryId != null
                && entryIds.Contains(x.BinsRunEntryId.Value)
                && x.MovementType == TreatmentLineageMovementTypes.BinsRun
                && x.ReversesTreatmentLineageMovementId == null)
            .ToListAsync(cancellationToken);

        foreach (var source in restorationSources)
        {
            var selectionKey = SelectionLookupKey(source.Snapshot);
            if (!result.TryGetValue(selectionKey, out var selections))
            {
                selections = [];
                result[selectionKey] = selections;
            }

            var sourceMovements = movements.Where(x => x.BinsRunEntryId == source.BinsRunEntryId).ToList();
            var movement = sourceMovements.Count == 1 ? sourceMovements[0] : null;
            var segment = movement?.SourceSegment;
            var identityKey = IdentityKey(source.Snapshot);
            var exact = movement is not null
                && segment is not null
                && movement.SourceSegmentId is not null
                && movement.BinCount == source.Bins
                && movement.SourceRoomId == source.Snapshot.RoomId
                && movement.IdentityKey == identityKey
                && movement.TreatmentSignatureSnapshot == source.TreatmentSignature
                && movement.TreatmentStateSnapshot == source.TreatmentState
                && segment.WarehouseId == source.Snapshot.WarehouseId
                && segment.RoomId == source.Snapshot.RoomId
                && segment.IdentityKey == identityKey
                && segment.TreatmentSignature == source.TreatmentSignature
                && segment.TreatmentState == source.TreatmentState
                && segment.ReceiptId == movement.ReceiptId;
            if (!exact)
            {
                selections.Add(new TreatmentSegmentSelection(
                    identityKey,
                    source.TreatmentSignature,
                    source.TreatmentState,
                    0,
                    "Needs Review — unavailable for Actual Run correction",
                    movement?.ReceiptId,
                    movement?.SourceSegmentId,
                    false,
                    $"Treatment provenance for Actual Run entry #{source.BinsRunEntryId} could not be resolved exactly."));
                continue;
            }

            var exactMovement = movement!;
            var existing = selections.SingleOrDefault(x =>
                x.SegmentId == exactMovement.SourceSegmentId
                && x.ReceiptId == exactMovement.ReceiptId
                && string.Equals(x.TreatmentSignature, exactMovement.TreatmentSignatureSnapshot, StringComparison.Ordinal));
            if (existing is null)
            {
                selections.Add(new TreatmentSegmentSelection(
                    identityKey,
                    exactMovement.TreatmentSignatureSnapshot,
                    exactMovement.TreatmentStateSnapshot,
                    exactMovement.BinCount,
                    source.TreatmentSummary,
                    exactMovement.ReceiptId,
                    exactMovement.SourceSegmentId));
            }
            else
            {
                selections[selections.IndexOf(existing)] = existing with { CurrentBins = existing.CurrentBins + exactMovement.BinCount };
            }
        }

        return result.ToDictionary(x => x.Key, x => (IReadOnlyList<TreatmentSegmentSelection>)x.Value, StringComparer.OrdinalIgnoreCase);
    }

    public Task<TreatmentLineageWriteResult> MoveAsync(
        RoomInventoryLedgerSnapshot snapshot, string? treatmentSignature, int bins,
        int? destinationWarehouseId, int? destinationRoomId, string operationKey, string movementType,
        long? roomTransferId, long? roomInventoryLossId, long? binsRunEntryId,
        DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken) =>
        MoveCoreAsync(snapshot, treatmentSignature, bins, destinationWarehouseId, destinationRoomId, operationKey,
            movementType, roomTransferId, roomInventoryLossId, binsRunEntryId, occurredAt, actorUserId,
            cancellationToken, null);

    public Task<TreatmentLineageWriteResult> MoveSelectedAsync(
        RoomInventoryLedgerSnapshot snapshot, string? treatmentSignature, long? treatmentSegmentId, long? treatmentReceiptId, int bins,
        int? destinationWarehouseId, int? destinationRoomId, string operationKey, string movementType,
        long? roomTransferId, long? roomInventoryLossId, long? binsRunEntryId,
        DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken) =>
        MoveCoreAsync(snapshot, treatmentSignature, bins, destinationWarehouseId, destinationRoomId, operationKey,
            movementType, roomTransferId, roomInventoryLossId, binsRunEntryId, occurredAt, actorUserId,
            treatmentReceiptId, cancellationToken, null, treatmentSegmentId, exactSelection: true);

    public async Task<TreatmentLineageWriteResult> ReclassifyIdentityAsync(
        RoomInventoryLedgerSnapshot source,
        RoomInventoryLedgerSnapshot target,
        InventoryIdentityCorrection correction,
        DateTimeOffset occurredAt,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        if (source.RoomId != target.RoomId || source.WarehouseId != target.WarehouseId)
            return new(false, "Inventory identity correction cannot move fruit between rooms or facilities.");
        if (source.CurrentBins <= 0 || target.CurrentBins != source.CurrentBins)
            return new(false, "Inventory identity correction requires an exact positive conserved quantity.");
        var prefix = $"identity-correction:{correction.OperationKey}:room:{source.RoomId}:";
        var existing = await dbContext.TreatmentLineageMovements.AsNoTracking()
            .Where(x => x.InventoryIdentityCorrectionId == correction.Id && x.OperationKey.StartsWith(prefix))
            .SumAsync(x => (int?)x.BinCount, cancellationToken) ?? 0;
        if (existing == source.CurrentBins) return new(true, null);
        if (existing != 0) return new(false, "The treatment identity correction is partially applied and requires review.");

        var segments = (await MaterializeAsync(source, cancellationToken))
            .Where(x => x.CurrentBins > 0)
            .OrderBy(x => x.ReceiptId ?? long.MaxValue)
            .ThenBy(x => x.Id)
            .ToList();
        if (segments.Sum(x => x.CurrentBins) != source.CurrentBins)
            return new(false, "Treatment provenance does not exactly reconcile with the authoritative source identity.");

        // A correction is one durable operation. Keep its treatment rows and
        // segment updates on the correction's authoritative operation time so
        // exact State B verification is deterministic.
        var now = occurredAt;
        foreach (var segment in segments)
        {
            var bins = segment.CurrentBins;
            var destination = await GetOrCreateSegmentAsync(
                target,
                segment.TreatmentState,
                segment.TreatmentSignature,
                now,
                cancellationToken,
                segment.ReceiptId);
            await CopyApplicationLinksAsync(segment, destination, cancellationToken);
            segment.CurrentBins = 0;
            segment.UpdatedAt = now;
            segment.ConcurrencyVersion++;
            destination.CurrentBins += bins;
            destination.UpdatedAt = now;
            destination.ConcurrencyVersion++;
            var movement = new TreatmentLineageMovement
            {
                OperationKey = $"{prefix}{segment.Id}",
                MovementType = TreatmentLineageMovementTypes.IdentityReclassification,
                SourceSegment = segment,
                DestinationSegment = destination,
                SourceRoomId = source.RoomId,
                DestinationRoomId = target.RoomId,
                IdentityKey = IdentityKey(target),
                TreatmentStateSnapshot = segment.TreatmentState,
                TreatmentSignatureSnapshot = segment.TreatmentSignature,
                ReceiptId = segment.ReceiptId,
                BinCount = bins,
                InventoryIdentityCorrection = correction,
                InventoryIdentityCorrectionId = correction.Id,
                OccurredAt = occurredAt,
                CreatedByUserId = actorUserId,
                CreatedAt = now
            };
            correction.TreatmentLineageMovements.Add(movement);
            dbContext.TreatmentLineageMovements.Add(movement);
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(true, null, correction.TreatmentLineageMovements.LastOrDefault()?.Id);
    }

    public async Task<TreatmentLineageWriteResult> CorrectReceiptLocationAsync(
        RoomInventoryLedgerSnapshot source,
        RoomInventoryLedgerSnapshot target,
        long receiptId,
        string operationKey,
        DateTimeOffset occurredAt,
        int actorUserId,
        CancellationToken cancellationToken)
    {
        if (source.WarehouseId == target.WarehouseId && source.RoomId == target.RoomId)
            return new(false, "Receipt location correction requires a different destination room.");
        if (IdentityKey(source) != IdentityKey(target) || source.CurrentBins <= 0 || target.CurrentBins != source.CurrentBins)
            return new(false, "Receipt location correction requires an exact conserved inventory identity and quantity.");
        var prefix = $"receipt-location-correction:{operationKey}:";
        var existing = await dbContext.TreatmentLineageMovements.AsNoTracking()
            .Where(x => x.ReceiptId == receiptId && x.MovementType == TreatmentLineageMovementTypes.ReceiptLocationCorrection
                && x.OperationKey.StartsWith(prefix))
            .SumAsync(x => (int?)x.BinCount, cancellationToken) ?? 0;
        if (existing == source.CurrentBins) return new(true, null);
        if (existing != 0) return new(false, "The Receipt location treatment movement is partially applied and requires review.");

        var materialized = await MaterializeAsync(source, cancellationToken);
        var segments = materialized
            .Where(x => x.ReceiptId == receiptId && x.CurrentBins > 0)
            .OrderBy(x => x.Id).ToList();
        var allocations = segments.Select(x => (Segment: x, Bins: x.CurrentBins)).ToList();
        if (allocations.Sum(x => x.Bins) == 0)
        {
            var unassigned = materialized.Where(x => x.ReceiptId is null && x.CurrentBins > 0).OrderBy(x => x.Id).ToList();
            if (unassigned.Select(x => x.TreatmentSignature).Distinct(StringComparer.Ordinal).Count() != 1
                || unassigned.Sum(x => x.CurrentBins) < source.CurrentBins)
                return new(false, "Treatment provenance cannot allocate the exact Receipt bins at the original room. No location correction was made.");
            var remaining = source.CurrentBins;
            allocations = [];
            foreach (var segment in unassigned)
            {
                var bins = Math.Min(remaining, segment.CurrentBins);
                if (bins > 0) allocations.Add((segment, bins));
                remaining -= bins;
                if (remaining == 0) break;
            }
        }
        if (allocations.Sum(x => x.Bins) != source.CurrentBins)
            return new(false, "Treatment provenance cannot allocate the exact Receipt bins at the original room. No location correction was made.");

        var now = businessTime.UtcNow;
        foreach (var allocation in allocations)
        {
            var segment = allocation.Segment;
            var bins = allocation.Bins;
            var destination = await GetOrCreateSegmentAsync(target, segment.TreatmentState, segment.TreatmentSignature,
                now, cancellationToken, receiptId);
            await CopyApplicationLinksAsync(segment, destination, cancellationToken);
            segment.CurrentBins -= bins;
            segment.UpdatedAt = now;
            segment.ConcurrencyVersion++;
            destination.CurrentBins += bins;
            destination.UpdatedAt = now;
            destination.ConcurrencyVersion++;
            dbContext.TreatmentLineageMovements.Add(new TreatmentLineageMovement
            {
                OperationKey = $"{prefix}{segment.Id}",
                MovementType = TreatmentLineageMovementTypes.ReceiptLocationCorrection,
                SourceSegment = segment,
                DestinationSegment = destination,
                SourceRoomId = source.RoomId,
                DestinationRoomId = target.RoomId,
                IdentityKey = IdentityKey(source),
                TreatmentStateSnapshot = segment.TreatmentState,
                TreatmentSignatureSnapshot = segment.TreatmentSignature,
                ReceiptId = receiptId,
                BinCount = bins,
                OccurredAt = occurredAt,
                CreatedByUserId = actorUserId,
                CreatedAt = now
            });
        }
        await dbContext.SaveChangesAsync(cancellationToken);
        return new(true, null);
    }

    public Task<TreatmentLineageWriteResult> MoveToProcessorAsync(
        RoomInventoryLedgerSnapshot snapshot, string? treatmentSignature, int bins, string operationKey,
        long processorShipmentLineId, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken) =>
        MoveCoreAsync(snapshot, treatmentSignature, bins, null, null, operationKey,
            TreatmentLineageMovementTypes.ProcessorShipment, null, null, null, occurredAt, actorUserId,
            cancellationToken, processorShipmentLineId);

    public Task<TreatmentLineageWriteResult> MoveSelectedToProcessorAsync(
        RoomInventoryLedgerSnapshot snapshot, string? treatmentSignature, long? treatmentSegmentId, long? treatmentReceiptId,
        int bins, string operationKey, long processorShipmentLineId, DateTimeOffset occurredAt,
        int? actorUserId, CancellationToken cancellationToken) =>
        MoveCoreAsync(snapshot, treatmentSignature, bins, null, null, operationKey,
            TreatmentLineageMovementTypes.ProcessorShipment, null, null, null, occurredAt, actorUserId,
            treatmentReceiptId, cancellationToken, processorShipmentLineId, treatmentSegmentId, exactSelection: true);

    public async Task<TreatmentLineageWriteResult> MoveToOutsideWarehouseAsync(
        RoomInventoryLedgerSnapshot snapshot, string treatmentSignature, int bins,
        string operationKey, long outsideWarehouseTransferId, DateTimeOffset occurredAt,
        int? actorUserId, CancellationToken cancellationToken)
    {
        if (bins <= 0) return new(false, "Treatment lineage movement quantity must be positive.");
        var segments = (await MaterializeAsync(snapshot, cancellationToken))
            .Where(x => x.CurrentBins > 0 && x.TreatmentSignature == treatmentSignature)
            .OrderBy(x => x.ReceiptId ?? long.MaxValue)
            .ThenBy(x => x.Id)
            .ToList();
        if (segments.Sum(x => x.CurrentBins) < bins)
            return new(false, "The exact treatment provenance no longer contains enough bins. Refresh before retrying.");

        var remaining = bins;
        long? lastMovementId = null;
        foreach (var segment in segments)
        {
            var allocated = Math.Min(remaining, segment.CurrentBins);
            if (allocated == 0) continue;
            var result = await MoveCoreAsync(snapshot, treatmentSignature, allocated, null, null,
                $"{operationKey}:{segment.Id}", TreatmentLineageMovementTypes.OutsideWarehouseTransfer,
                null, null, null, occurredAt, actorUserId, segment.ReceiptId, cancellationToken,
                null, segment.Id, exactSelection: true, outsideWarehouseTransferId: outsideWarehouseTransferId);
            if (!result.Success) return result;
            lastMovementId = result.MovementId;
            remaining -= allocated;
            if (remaining == 0) break;
        }
        if (remaining != 0) return new(false, "The exact treatment provenance allocation did not balance.");
        var moved = await dbContext.TreatmentLineageMovements
            .Where(x => x.OutsideWarehouseTransferId == outsideWarehouseTransferId && x.ReversesTreatmentLineageMovementId == null)
            .SumAsync(x => x.BinCount, cancellationToken);
        return moved == bins
            ? new(true, null, lastMovementId)
            : new(false, "The Outside Warehouse Transfer treatment lineage does not equal its bin count.");
    }

    public async Task<TreatmentLineageWriteResult> DispatchAsync(
        RoomInventoryLedgerSnapshot snapshot, string treatmentSignature, int bins, string operationKey,
        long transferId, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken)
    {
        if (bins <= 0) return new(false, "Treatment lineage movement quantity must be positive.");
        var segments = (await MaterializeAsync(snapshot, cancellationToken))
            .Where(x => x.CurrentBins > 0 && x.TreatmentSignature == treatmentSignature)
            .OrderBy(x => x.ReceiptId ?? long.MaxValue).ThenBy(x => x.Id).ToList();
        if (segments.Sum(x => x.CurrentBins) < bins)
            return new(false, "The exact treatment provenance no longer contains enough bins. Refresh before retrying.");
        var remaining = bins;
        long? last = null;
        foreach (var segment in segments)
        {
            var allocated = Math.Min(remaining, segment.CurrentBins);
            if (allocated == 0) continue;
            var result = await MoveCoreAsync(snapshot, treatmentSignature, allocated, null, null,
                $"{operationKey}:{segment.Id}", TreatmentLineageMovementTypes.InterCrewDispatch,
                null, null, null, occurredAt, actorUserId, segment.ReceiptId, cancellationToken,
                null, segment.Id, exactSelection: true, interCrewTransferId: transferId);
            if (!result.Success) return result;
            last = result.MovementId;
            remaining -= allocated;
            if (remaining == 0) break;
        }
        return remaining == 0 ? new(true, null, last) : new(false, "The inter-crew treatment allocation did not balance.");
    }

    public async Task<TreatmentLineageWriteResult> ReceiveAsync(
        long transferId, int destinationWarehouseId, int destinationRoomId, int binsReceived,
        string operationKey, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken)
    {
        if (binsReceived <= 0) return new(false, "Received bins must be positive.");
        var transfer = await dbContext.InterCrewTransfers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == transferId, cancellationToken);
        if (transfer is null) return new(false, "The inter-crew transfer was not found.");
        InventoryIdentityResolution? resolvedIdentity = null;
        if (transfer.CropYear is not null && transfer.GrowerLotId is not null && transfer.FruitProfileId is not null)
        {
            resolvedIdentity = await new InventoryIdentityService(dbContext).ResolveAsync(new InventoryIdentityKey(
                transfer.CropYear.Value, transfer.GrowerLotId.Value, transfer.FruitProfileId.Value), cancellationToken);
        }
        var originals = await dbContext.TreatmentLineageMovements.Include(x => x.SourceSegment).ThenInclude(x => x!.Applications)
            .Where(x => x.InterCrewTransferId == transferId && x.MovementType == TreatmentLineageMovementTypes.InterCrewDispatch && x.ReversesTreatmentLineageMovementId == null)
            .OrderBy(x => x.Id).ToListAsync(cancellationToken);
        if (originals.Count == 0) return new(false, "The dispatch treatment lineage was not found.");
        if (await dbContext.TreatmentLineageMovements.AsNoTracking().AnyAsync(x => x.OperationKey.StartsWith(operationKey + ":"), cancellationToken))
            return new(true, null);
        var remaining = binsReceived;
        long? last = null;
        foreach (var original in originals)
        {
            if (remaining == 0) break;
            var allocated = Math.Min(remaining, original.BinCount);
            var source = original.SourceSegment!;
            var snapshot = new RoomInventoryLedgerSnapshot(
                transfer.SourceWarehouseId, "", transfer.SourceRoomId, "", "", transfer.CropYear,
                transfer.GrowerLotId, transfer.FruitProfileId, transfer.GrowerNameSnapshot,
                transfer.GrowerNumberSnapshot, transfer.LotNumberSnapshot, null, transfer.VarietyCodeSnapshot,
                transfer.VarietyCodeSnapshot, transfer.VarietyCodeSnapshot, "", transfer.ProductionTypeSnapshot,
                transfer.IsOrganicSnapshot, transfer.InventoryStatusSnapshot ?? "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                occurredAt, occurredAt, transfer.SourceInventoryAdjustmentId ?? 0);
            snapshot = Canonicalize(snapshot, resolvedIdentity);
            var destinationSnapshot = snapshot with { WarehouseId = destinationWarehouseId, RoomId = destinationRoomId };
            var destination = await GetOrCreateSegmentAsync(destinationSnapshot, source.TreatmentState, source.TreatmentSignature, businessTime.UtcNow, cancellationToken, source.ReceiptId);
            await CopyApplicationLinksAsync(source, destination, cancellationToken);
            destination.CurrentBins += allocated;
            destination.UpdatedAt = businessTime.UtcNow;
            destination.ConcurrencyVersion++;
            var movement = new TreatmentLineageMovement
            {
                OperationKey = $"{operationKey}:{original.Id}",
                MovementType = TreatmentLineageMovementTypes.InterCrewReceive,
                SourceSegmentId = source.Id,
                DestinationSegment = destination,
                SourceRoomId = null,
                DestinationRoomId = destinationRoomId,
                IdentityKey = IdentityKey(destinationSnapshot),
                TreatmentStateSnapshot = original.TreatmentStateSnapshot,
                TreatmentSignatureSnapshot = original.TreatmentSignatureSnapshot,
                ReceiptId = original.ReceiptId,
                BinCount = allocated,
                InterCrewTransferId = transferId,
                OccurredAt = occurredAt,
                CreatedByUserId = actorUserId,
                CreatedAt = businessTime.UtcNow
            };
            dbContext.TreatmentLineageMovements.Add(movement);
            await dbContext.SaveChangesAsync(cancellationToken);
            last = movement.Id;
            remaining -= allocated;
        }
        if (remaining > 0)
        {
            var template = originals[0];
            var source = template.SourceSegment!;
            var identitySnapshot = new RoomInventoryLedgerSnapshot(
                destinationWarehouseId, "", destinationRoomId, "", "", transfer.CropYear, transfer.GrowerLotId,
                transfer.FruitProfileId, transfer.GrowerNameSnapshot, transfer.GrowerNumberSnapshot,
                transfer.LotNumberSnapshot, null, transfer.VarietyCodeSnapshot, transfer.VarietyCodeSnapshot,
                transfer.VarietyCodeSnapshot, "", transfer.ProductionTypeSnapshot, transfer.IsOrganicSnapshot,
                transfer.InventoryStatusSnapshot ?? "", 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
                occurredAt, occurredAt, transfer.SourceInventoryAdjustmentId ?? 0);
            identitySnapshot = Canonicalize(identitySnapshot, resolvedIdentity);
            var destination = await GetOrCreateSegmentAsync(identitySnapshot, source.TreatmentState, source.TreatmentSignature, businessTime.UtcNow, cancellationToken, null);
            await CopyApplicationLinksAsync(source, destination, cancellationToken);
            destination.CurrentBins += remaining;
            destination.UpdatedAt = businessTime.UtcNow;
            destination.ConcurrencyVersion++;
            var movement = new TreatmentLineageMovement
            {
                OperationKey = $"{operationKey}:variance",
                MovementType = TreatmentLineageMovementTypes.InterCrewReceive,
                DestinationSegment = destination,
                DestinationRoomId = destinationRoomId,
                IdentityKey = IdentityKey(identitySnapshot),
                TreatmentStateSnapshot = template.TreatmentStateSnapshot,
                TreatmentSignatureSnapshot = template.TreatmentSignatureSnapshot,
                BinCount = remaining,
                InterCrewTransferId = transferId,
                OccurredAt = occurredAt,
                CreatedByUserId = actorUserId,
                CreatedAt = businessTime.UtcNow
            };
            dbContext.TreatmentLineageMovements.Add(movement);
            await dbContext.SaveChangesAsync(cancellationToken);
            last = movement.Id;
        }
        return new(true, null, last);
    }

    public async Task<TreatmentLineageWriteResult> ReverseAsync(
        long transferId, bool wasReceived, string operationKey, DateTimeOffset occurredAt,
        int? actorUserId, CancellationToken cancellationToken)
    {
        if (wasReceived)
        {
            var receives = await dbContext.TreatmentLineageMovements
                .Where(x => x.InterCrewTransferId == transferId && x.MovementType == TreatmentLineageMovementTypes.InterCrewReceive && x.ReversesTreatmentLineageMovementId == null)
                .OrderBy(x => x.Id).ToListAsync(cancellationToken);
            foreach (var receive in receives)
            {
                var key = $"{operationKey}:receive:{receive.Id}";
                if (await dbContext.TreatmentLineageMovements.AsNoTracking().AnyAsync(x => x.OperationKey == key, cancellationToken)) continue;
                var destination = await dbContext.TreatmentLineageSegments.SingleAsync(x => x.Id == receive.DestinationSegmentId, cancellationToken);
                if (destination.CurrentBins < receive.BinCount) return new(false, "The exact received treatment segment no longer contains enough bins to reverse.");
                destination.CurrentBins -= receive.BinCount;
                destination.UpdatedAt = businessTime.UtcNow;
                destination.ConcurrencyVersion++;
                dbContext.TreatmentLineageMovements.Add(new TreatmentLineageMovement
                {
                    OperationKey = key,
                    MovementType = TreatmentLineageMovementTypes.InterCrewReversal,
                    SourceSegment = destination,
                    SourceRoomId = destination.RoomId,
                    IdentityKey = receive.IdentityKey,
                    TreatmentStateSnapshot = receive.TreatmentStateSnapshot,
                    TreatmentSignatureSnapshot = receive.TreatmentSignatureSnapshot,
                    ReceiptId = receive.ReceiptId,
                    BinCount = receive.BinCount,
                    InterCrewTransferId = transferId,
                    ReversesTreatmentLineageMovementId = receive.Id,
                    OccurredAt = occurredAt,
                    CreatedByUserId = actorUserId,
                    CreatedAt = businessTime.UtcNow
                });
                await dbContext.SaveChangesAsync(cancellationToken);
            }
        }
        return await ReverseMovementsCoreAsync(operationKey + ":dispatch", TreatmentLineageMovementTypes.InterCrewReversal,
            null, null, null, occurredAt, actorUserId, cancellationToken, interCrewTransferId: transferId,
            originalMovementType: TreatmentLineageMovementTypes.InterCrewDispatch);
    }

    private async Task<TreatmentLineageWriteResult> MoveCoreAsync(
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
        CancellationToken cancellationToken,
        long? processorShipmentLineId = null,
        long? outsideWarehouseTransferId = null,
        long? interCrewTransferId = null)
    {
        long? receiptId = null;
        if (roomInventoryLossId is not null)
        {
            receiptId = await dbContext.RoomInventoryLosses.AsNoTracking()
                .Where(x => x.Id == roomInventoryLossId.Value)
                .Select(x => x.ReceiptId)
                .SingleOrDefaultAsync(cancellationToken);
        }
        else if (binsRunEntryId is not null)
        {
            receiptId = await dbContext.BinsRunEntries.AsNoTracking()
                .Where(x => x.Id == binsRunEntryId.Value)
                .Select(x => x.ReceiptId)
                .SingleOrDefaultAsync(cancellationToken);
        }
        else if (roomTransferId is not null && snapshot.LatestAdjustmentId > 0)
        {
            receiptId = await dbContext.RoomInventoryAdjustments.AsNoTracking()
                .Where(x => x.Id == snapshot.LatestAdjustmentId)
                .Select(x => x.ReceiptId)
                .SingleOrDefaultAsync(cancellationToken);
        }
        else if (processorShipmentLineId is not null)
        {
            receiptId = await dbContext.ProcessorShipmentLines.AsNoTracking()
                .Where(x => x.Id == processorShipmentLineId.Value)
                .Select(x => x.ReceiptId)
                .SingleOrDefaultAsync(cancellationToken);
        }
        else if (outsideWarehouseTransferId is not null)
        {
            receiptId = await dbContext.OutsideWarehouseTransfers.AsNoTracking()
                .Where(x => x.Id == outsideWarehouseTransferId.Value)
                .Select(x => x.ReceiptId)
                .SingleOrDefaultAsync(cancellationToken);
        }
        return await MoveCoreAsync(snapshot, treatmentSignature, bins, destinationWarehouseId, destinationRoomId, operationKey,
            movementType, roomTransferId, roomInventoryLossId, binsRunEntryId, occurredAt, actorUserId, receiptId,
            cancellationToken, processorShipmentLineId, outsideWarehouseTransferId: outsideWarehouseTransferId,
            interCrewTransferId: interCrewTransferId);
    }

    private async Task<TreatmentLineageWriteResult> MoveCoreAsync(
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
        long? receiptId,
        CancellationToken cancellationToken,
        long? processorShipmentLineId,
        long? treatmentSegmentId = null,
        bool exactSelection = false,
        long? outsideWarehouseTransferId = null,
        long? interCrewTransferId = null)
    {
        if (bins <= 0) return new(false, "Treatment lineage movement quantity must be positive.");
        if (roomTransferId is null && roomInventoryLossId is null && binsRunEntryId is null && processorShipmentLineId is null && outsideWarehouseTransferId is null && interCrewTransferId is null)
        {
            return new(false, "A specific parent movement is required for treatment lineage movement.");
        }
        if ((roomTransferId is not null ? 1 : 0) + (roomInventoryLossId is not null ? 1 : 0) + (binsRunEntryId is not null ? 1 : 0) + (processorShipmentLineId is not null ? 1 : 0) + (outsideWarehouseTransferId is not null ? 1 : 0) + (interCrewTransferId is not null ? 1 : 0) != 1)
        {
            return new(false, "Treatment lineage movement must reference exactly one parent movement.");
        }
        var parentError = await ValidateMovementParentAsync(
            snapshot, bins, destinationWarehouseId, destinationRoomId,
            roomTransferId, roomInventoryLossId, binsRunEntryId, cancellationToken, processorShipmentLineId, outsideWarehouseTransferId, interCrewTransferId);
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
            sameRequest = sameRequest
                && existingMovement.ProcessorShipmentLineId == processorShipmentLineId
                && existingMovement.OutsideWarehouseTransferId == outsideWarehouseTransferId
                && existingMovement.InterCrewTransferId == interCrewTransferId
                && (!exactSelection || existingMovement.ReceiptId == receiptId)
                && (treatmentSegmentId is null || existingMovement.SourceSegmentId == treatmentSegmentId);
            return sameRequest
                ? new(true, null, existingMovement.Id)
                : new(false, "The operation key already belongs to a different treatment lineage movement.");
        }
        var segments = await MaterializeAsync(snapshot, cancellationToken);
        if (exactSelection && treatmentSegmentId is not null)
        {
            var selected = segments.SingleOrDefault(x => x.Id == treatmentSegmentId
                && (string.IsNullOrWhiteSpace(treatmentSignature) || x.TreatmentSignature == treatmentSignature));
            if (selected is not null && selected.CurrentBins < bins)
            {
                return new(false, $"Only {selected.CurrentBins} bins remain in the selected treatment segment. Refresh before retrying.");
            }
        }
        var available = segments.Where(x => x.CurrentBins > 0
            && (exactSelection || receiptId == null || x.ReceiptId == receiptId || x.ReceiptId == null)).ToList();
        TreatmentLineageSegment? source;
        if (exactSelection)
        {
            var signatureCandidates = available.Where(x =>
                string.IsNullOrWhiteSpace(treatmentSignature) || x.TreatmentSignature == treatmentSignature).ToList();
            List<TreatmentLineageSegment> candidates;
            if (treatmentSegmentId is not null)
            {
                candidates = signatureCandidates.Where(x => x.Id == treatmentSegmentId).ToList();
            }
            else
            {
                var receiptCandidates = signatureCandidates.Where(x => x.ReceiptId == receiptId).ToList();
                candidates = receiptCandidates.Count > 0
                    ? receiptCandidates
                    : signatureCandidates.Count == 1
                        ? signatureCandidates
                        : [];
            }
            if (candidates.Count > 1)
            {
                return new(false, "This room-lot has multiple treatment histories. Select the exact treatment segment being packed.");
            }
            source = candidates.Count == 1 ? candidates[0] : null;
        }
        else if (string.IsNullOrWhiteSpace(treatmentSignature))
        {
            if (available.Count != 1) return new(false, "This fruit identity has multiple treatment histories. Select the exact treated or untreated segment.");
            source = available[0];
        }
        else
        {
            var candidates = available.Where(x => x.TreatmentSignature == treatmentSignature).ToList();
            source = receiptId is null
                ? candidates.SingleOrDefault()
                : candidates.SingleOrDefault(x => x.ReceiptId == receiptId && x.CurrentBins >= bins)
                    ?? (candidates.All(x => x.ReceiptId != receiptId)
                        ? candidates.SingleOrDefault(x => x.ReceiptId == null && x.CurrentBins >= bins)
                        : null);
        }
        if (source is null) return new(false, "The selected treatment segment is no longer available. Refresh before retrying.");
        if (source.CurrentBins < bins) return new(false, $"Only {source.CurrentBins} bins remain in the selected treatment segment.");

        var now = businessTime.UtcNow;
        TreatmentLineageSegment? destination = null;
        if (destinationRoomId is not null && destinationWarehouseId is not null)
        {
            var destinationSnapshot = snapshot with { WarehouseId = destinationWarehouseId.Value, RoomId = destinationRoomId.Value };
            destination = await GetOrCreateSegmentAsync(destinationSnapshot, source.TreatmentState, source.TreatmentSignature, now, cancellationToken, exactSelection ? source.ReceiptId : receiptId ?? source.ReceiptId);
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
            ReceiptId = exactSelection ? source.ReceiptId ?? receiptId : receiptId ?? source.ReceiptId,
            BinCount = bins,
            RoomTransferId = roomTransferId,
            RoomInventoryLossId = roomInventoryLossId,
            BinsRunEntryId = binsRunEntryId,
            ProcessorShipmentLineId = processorShipmentLineId,
            OutsideWarehouseTransferId = outsideWarehouseTransferId,
            InterCrewTransferId = interCrewTransferId,
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
        CancellationToken cancellationToken,
        long? processorShipmentLineId,
        long? outsideWarehouseTransferId,
        long? interCrewTransferId)
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

        if (processorShipmentLineId is not null)
        {
            var parent = await dbContext.ProcessorShipmentLines.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == processorShipmentLineId.Value, cancellationToken);
            if (parent is null
                || parent.WarehouseId != snapshot.WarehouseId
                || parent.RoomId != snapshot.RoomId
                || destinationWarehouseId is not null
                || destinationRoomId is not null
                || parent.BinsSent != bins
                || !SameIdentity(parent.CropYear, parent.GrowerLotId, parent.FruitProfileId, parent.LotNumberSnapshot, parent.VarietyCodeSnapshot, snapshot))
            {
                return "The Processor Shipment line parent does not match the exact treatment lineage movement.";
            }
            return null;
        }

        if (outsideWarehouseTransferId is not null)
        {
            var parent = await dbContext.OutsideWarehouseTransfers.AsNoTracking()
                .SingleOrDefaultAsync(x => x.Id == outsideWarehouseTransferId.Value, cancellationToken);
            var alreadyMoved = await dbContext.TreatmentLineageMovements.AsNoTracking()
                .Where(x => x.OutsideWarehouseTransferId == outsideWarehouseTransferId && x.ReversesTreatmentLineageMovementId == null)
                .SumAsync(x => x.BinCount, cancellationToken);
            if (parent is null
                || parent.SourceWarehouseId != snapshot.WarehouseId
                || parent.SourceRoomId != snapshot.RoomId
                || destinationWarehouseId is not null
                || destinationRoomId is not null
                || bins <= 0
                || alreadyMoved + bins > parent.BinCount
                || !SameIdentity(parent.CropYear, parent.GrowerLotId, parent.FruitProfileId, parent.LotNumberSnapshot, parent.VarietyCodeSnapshot, snapshot))
            {
                return "The Outside Warehouse Transfer parent does not match the exact treatment lineage movement.";
            }
            return null;
        }

        if (interCrewTransferId is not null)
        {
            var parent = await dbContext.InterCrewTransfers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == interCrewTransferId.Value, cancellationToken);
            var moved = await dbContext.TreatmentLineageMovements.AsNoTracking()
                .Where(x => x.InterCrewTransferId == interCrewTransferId && x.MovementType == TreatmentLineageMovementTypes.InterCrewDispatch && x.ReversesTreatmentLineageMovementId == null)
                .SumAsync(x => x.BinCount, cancellationToken);
            if (parent is null || parent.SourceWarehouseId != snapshot.WarehouseId || parent.SourceRoomId != snapshot.RoomId
                || destinationWarehouseId is not null || destinationRoomId is not null || moved + bins > parent.BinsLoaded
                || !SameIdentity(parent.CropYear, parent.GrowerLotId, parent.FruitProfileId, parent.LotNumberSnapshot, parent.VarietyCodeSnapshot, snapshot))
                return "The inter-crew transfer parent does not match the exact treatment lineage movement.";
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

    public Task<TreatmentLineageWriteResult> ReverseMovementsAsync(
        string operationKeyPrefix, string movementType, long? roomTransferId, long? roomInventoryLossId,
        long? binsRunEntryId, DateTimeOffset occurredAt, int? actorUserId, CancellationToken cancellationToken) =>
        ReverseMovementsCoreAsync(operationKeyPrefix, movementType, roomTransferId, roomInventoryLossId,
            binsRunEntryId, occurredAt, actorUserId, cancellationToken, null);

    public Task<TreatmentLineageWriteResult> ReverseProcessorMovementAsync(
        string operationKeyPrefix, long processorShipmentLineId, DateTimeOffset occurredAt,
        int? actorUserId, CancellationToken cancellationToken) =>
        ReverseMovementsCoreAsync(operationKeyPrefix, TreatmentLineageMovementTypes.ProcessorShipmentReversal,
            null, null, null, occurredAt, actorUserId, cancellationToken, processorShipmentLineId);

    public Task<TreatmentLineageWriteResult> ReverseOutsideWarehouseMovementAsync(
        string operationKeyPrefix, long outsideWarehouseTransferId, DateTimeOffset occurredAt,
        int? actorUserId, CancellationToken cancellationToken) =>
        ReverseMovementsCoreAsync(operationKeyPrefix, TreatmentLineageMovementTypes.OutsideWarehouseTransferReversal,
            null, null, null, occurredAt, actorUserId, cancellationToken, null, outsideWarehouseTransferId);

    private async Task<TreatmentLineageWriteResult> ReverseMovementsCoreAsync(
        string operationKeyPrefix,
        string movementType,
        long? roomTransferId,
        long? roomInventoryLossId,
        long? binsRunEntryId,
        DateTimeOffset occurredAt,
        int? actorUserId,
        CancellationToken cancellationToken,
        long? processorShipmentLineId = null,
        long? outsideWarehouseTransferId = null,
        long? interCrewTransferId = null,
        string? originalMovementType = null)
    {
        if (roomTransferId is null && roomInventoryLossId is null && binsRunEntryId is null && processorShipmentLineId is null && outsideWarehouseTransferId is null && interCrewTransferId is null)
        {
            return new(false, "A specific parent movement is required for treatment lineage reversal.");
        }
        var originals = await dbContext.TreatmentLineageMovements
            .Where(x => (roomTransferId == null || x.RoomTransferId == roomTransferId)
                && (roomInventoryLossId == null || x.RoomInventoryLossId == roomInventoryLossId)
                && (binsRunEntryId == null || x.BinsRunEntryId == binsRunEntryId)
                && (processorShipmentLineId == null || x.ProcessorShipmentLineId == processorShipmentLineId)
                && (outsideWarehouseTransferId == null || x.OutsideWarehouseTransferId == outsideWarehouseTransferId)
                && (interCrewTransferId == null || x.InterCrewTransferId == interCrewTransferId)
                && (originalMovementType == null || x.MovementType == originalMovementType)
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
                ReceiptId = original.ReceiptId,
                BinCount = original.BinCount,
                RoomTransferId = roomTransferId,
                RoomInventoryLossId = roomInventoryLossId,
                BinsRunEntryId = binsRunEntryId,
                ProcessorShipmentLineId = processorShipmentLineId,
                OutsideWarehouseTransferId = outsideWarehouseTransferId,
                InterCrewTransferId = interCrewTransferId,
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
        var identityError = await InventoryIdentityWriteGuard.RejectSupersededAsync(
            dbContext, snapshot.CropYear, snapshot.GrowerLotId, snapshot.FruitProfileId,
            "Treatment lineage true-up", cancellationToken);
        if (identityError is not null) return new(false, identityError);
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

    private async Task<ReceiptApplicationSnapshot> ResolveReceiptApplicationSnapshotAsync(
        long receiptId,
        DateTimeOffset appliedAt,
        CancellationToken cancellationToken)
    {
        var receipt = await dbContext.Receipts.AsNoTracking()
            .Include(x => x.Warehouse)
            .Include(x => x.Room)
            .Include(x => x.FruitProfile)
            .SingleOrDefaultAsync(x => x.Id == receiptId && !x.IsDeleted, cancellationToken);
        if (receipt is null) return new(null, null, 0, "Receipt was not found.");
        var appliedAtUtc = appliedAt.ToUniversalTime();
        if (appliedAtUtc > businessTime.UtcNow.AddMinutes(5))
            return new(receipt, null, 0, "Application date/time cannot be in the future.");

        var exactSegments = await dbContext.TreatmentLineageSegments.AsNoTracking()
            .Where(x => x.ReceiptId == receipt.Id && x.CurrentBins > 0)
            .Select(x => new { x.WarehouseId, x.RoomId, x.IdentityKey, x.CurrentBins, x.UpdatedAt })
            .ToListAsync(cancellationToken);
        if (exactSegments.Count > 0)
        {
            if (exactSegments.Count != 1 || exactSegments[0].UpdatedAt > appliedAtUtc)
                return new(receipt, null, exactSegments.Sum(x => x.CurrentBins), "The Receipt is split, moved, or changed after this application time. Crop QC cannot safely identify the exact treated bins.");
            var exact = exactSegments[0];
            var exactSnapshots = await ledger.GetSnapshotsAsOfAsync(exact.WarehouseId, [exact.RoomId], appliedAtUtc, cancellationToken);
            var match = exactSnapshots.SingleOrDefault(x => x.CurrentBins >= exact.CurrentBins && IdentityKey(x) == exact.IdentityKey);
            return match is null
                ? new(receipt, null, exact.CurrentBins, "The exact Receipt treatment lineage does not reconcile with authoritative room inventory.")
                : new(receipt, match, exact.CurrentBins, null);
        }

        var hasIdentityMovement = await dbContext.TreatmentLineageMovements.AsNoTracking()
            .AnyAsync(x => x.OccurredAt <= businessTime.UtcNow
                && x.SourceSegment.CropYear == receipt.CropYear
                && x.SourceSegment.GrowerLotId == receipt.GrowerLotId
                && x.SourceSegment.FruitProfileId == receipt.FruitProfileId, cancellationToken);
        if (hasIdentityMovement)
            return new(receipt, null, 0, "Current inventory can no longer be allocated to this Receipt exactly after movement. Reconcile Receipt provenance before applying a Receipt-level treatment.");

        var rows = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .Where(x => x.ReceiptId == receipt.Id && x.AdjustmentAt <= appliedAtUtc)
            .Select(x => new
            {
                x.WarehouseId,
                x.RoomId,
                CropYear = x.CropYear ?? receipt.CropYear,
                GrowerLotId = x.GrowerLotId ?? receipt.GrowerLotId,
                FruitProfileId = x.FruitProfileId ?? receipt.FruitProfileId,
                Lot = x.LotNumber,
                x.ChangeAmount
            })
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return new(receipt, null, 0, "This Receipt has no authoritative inventory ledger rows.");
        var balances = rows
            .GroupBy(x => new { x.WarehouseId, x.RoomId, x.CropYear, x.GrowerLotId, x.FruitProfileId, x.Lot })
            .Select(x => new
            {
                x.Key.WarehouseId,
                x.Key.RoomId,
                x.Key.CropYear,
                x.Key.GrowerLotId,
                x.Key.FruitProfileId,
                x.Key.Lot,
                Bins = x.Sum(y => y.ChangeAmount)
            })
            .Where(x => x.Bins != 0)
            .ToList();
        if (balances.Any(x => x.Bins < 0) || balances.Count(x => x.Bins > 0) != 1)
            return new(receipt, null, 0, "The Receipt is split across rooms or has conflicting inventory provenance. No treatment was allowed.");
        var balance = balances.Single(x => x.Bins > 0);
        var laterReceiptMovement = await dbContext.RoomInventoryAdjustments.AsNoTracking()
            .AnyAsync(x => x.ReceiptId == receipt.Id && x.AdjustmentAt > appliedAtUtc && x.AdjustmentAt <= businessTime.UtcNow, cancellationToken);
        if (laterReceiptMovement)
            return new(receipt, null, balance.Bins, "The Receipt moved or changed after this application time. Crop QC cannot safely reconstruct the exact treated bins.");

        var snapshots = await ledger.GetSnapshotsAsOfAsync(null, [balance.RoomId], appliedAtUtc, cancellationToken);
        var receiptLot = string.IsNullOrWhiteSpace(receipt.GrowerNumber) ? receipt.LotCode : receipt.GrowerNumber;
        var matches = snapshots.Where(x => x.CurrentBins > 0
                && x.WarehouseId == balance.WarehouseId
                && x.RoomId == balance.RoomId
                && x.CropYear == balance.CropYear
                && x.GrowerLotId == balance.GrowerLotId
                && x.FruitProfileId == balance.FruitProfileId
                && string.Equals(x.Lot, receiptLot, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count != 1 || matches[0].CurrentBins < balance.Bins)
            return new(receipt, null, balance.Bins, "The exact Receipt quantity does not reconcile with authoritative room inventory.");
        return new(receipt, matches[0], balance.Bins, null);
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
            var untreated = segments.SingleOrDefault(x => x.TreatmentSignature == "u" && x.ReceiptId == null)
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
        var authoritative = snapshots
            .Where(x => x.CurrentBins > 0)
            .GroupBy(SelectionLookupKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                x => x.Key,
                x => ConsolidateSelectionSnapshots(x.ToList()),
                StringComparer.OrdinalIgnoreCase);
        var result = authoritative.Keys.ToDictionary(x => x, _ => new List<CurrentTreatmentSegmentViewModel>(), StringComparer.OrdinalIgnoreCase);
        if (authoritative.Count == 0) return result;
        var roomIds = authoritative.Values.Select(x => x.RoomId).Distinct().ToList();
        var identityKeys = authoritative.Values.Select(IdentityKey).Distinct().ToList();
        var segments = await dbContext.TreatmentLineageSegments.AsNoTracking()
            .Include(x => x.Applications)
            .ThenInclude(x => x.RoomTreatmentApplication)
            .Where(x => roomIds.Contains(x.RoomId) && identityKeys.Contains(x.IdentityKey) && x.CurrentBins > 0)
            .ToListAsync(cancellationToken);

        foreach (var snapshot in authoritative.Values)
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
                    segment.TreatmentSignature, applications, segment.ReceiptId));
            }
            var explicitBins = output.Sum(x => x.Bins);
            var implicitBins = snapshot.CurrentBins - explicitBins;
            if (implicitBins < 0)
            {
                output.Clear();
                output.Add(new CurrentTreatmentSegmentViewModel(
                    null, key, snapshot.GrowerNumber ?? snapshot.Lot, snapshot.Grower, snapshot.VarietyName,
                    snapshot.ProductionType, snapshot.IsOrganic, snapshot.CurrentBins, "NeedsReview",
                    "needs-review", [], null, false,
                    $"Treatment lineage requires review: {explicitBins} explicit bins exceed {snapshot.CurrentBins} authoritative bins.",
                    explicitBins));
                continue;
            }
            if (implicitBins > 0)
            {
                output.Add(new CurrentTreatmentSegmentViewModel(null, key, snapshot.GrowerNumber ?? snapshot.Lot, snapshot.Grower,
                    snapshot.VarietyName, snapshot.ProductionType, snapshot.IsOrganic, implicitBins,
                    TreatmentLineageStates.Untreated, "u", []));
            }
        }
        return result;
    }

    private static RoomInventoryLedgerSnapshot ConsolidateSelectionSnapshots(
        IReadOnlyList<RoomInventoryLedgerSnapshot> snapshots)
    {
        var distinct = snapshots.Distinct().ToList();
        var latest = distinct
            .OrderByDescending(x => x.LastTransactionAt)
            .ThenByDescending(x => x.LatestAdjustmentId)
            .First();
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

    private static TreatmentSegmentSelection ToSelection(CurrentTreatmentSegmentViewModel value) =>
        new(value.IdentityKey, value.TreatmentSignature, value.TreatmentState, value.Bins, SegmentLabel(value),
            value.ReceiptId, value.SegmentId, value.IsAvailable, value.UnavailableReason, value.ExplicitBins);

    private async Task<TreatmentLineageSegment> GetOrCreateSegmentAsync(RoomInventoryLedgerSnapshot snapshot, string state, string signature, DateTimeOffset now, CancellationToken cancellationToken, long? receiptId = null)
    {
        var key = IdentityKey(snapshot);
        var existing = await dbContext.TreatmentLineageSegments.Include(x => x.Applications)
            .SingleOrDefaultAsync(x => x.RoomId == snapshot.RoomId && x.IdentityKey == key && x.TreatmentSignature == signature && x.ReceiptId == receiptId, cancellationToken);
        if (existing is not null) return existing;
        var segment = new TreatmentLineageSegment
        {
            WarehouseId = snapshot.WarehouseId,
            RoomId = snapshot.RoomId,
            ReceiptId = receiptId,
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

    private static RoomInventoryLedgerSnapshot Canonicalize(
        RoomInventoryLedgerSnapshot snapshot,
        InventoryIdentityResolution? resolved) => resolved is null || !resolved.IsSuperseded
            ? snapshot
            : snapshot with
            {
                CropYear = resolved.Canonical.CropYear,
                GrowerLotId = resolved.Canonical.GrowerLotId,
                FruitProfileId = resolved.Canonical.FruitProfileId,
                Grower = resolved.GrowerLot.Grower,
                GrowerNumber = resolved.GrowerLot.LotNumber,
                Lot = resolved.GrowerLot.LotNumber,
                PoolStart = resolved.GrowerLot.PoolStart,
                StoredVarietyCode = resolved.FruitProfile.VarietyCode,
                Variety = resolved.FruitProfile.VarietyCode,
                VarietyName = resolved.FruitProfile.Name,
                FruitType = resolved.FruitProfile.FruitType,
                ProductionType = resolved.FruitProfile.ProductionType,
                IsOrganic = resolved.FruitProfile.IsOrganic,
                InventoryStatus = resolved.FruitProfile.ProductionType
            };

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
    private static string? NormalizeCrop(string? crop) => crop?.Trim().ToLowerInvariant() switch
    {
        "apple" or "apples" => "Apples",
        "pear" or "pears" => "Pears",
        _ => null
    };
    private static string? ResolveWholeRoomCrop(IReadOnlyList<RoomInventoryLedgerSnapshot> snapshots)
    {
        var crops = snapshots.Select(x => NormalizeCrop(x.FruitType) ?? "")
            .Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return crops.Count == 1 && snapshots.All(x => !string.IsNullOrWhiteSpace(x.FruitType)) ? crops[0] : null;
    }
    private static string SegmentLabel(CurrentTreatmentSegmentViewModel x) => !x.IsAvailable
        ? "Needs Review — unavailable for inventory movement"
        : x.TreatmentState == TreatmentLineageStates.Untreated
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
