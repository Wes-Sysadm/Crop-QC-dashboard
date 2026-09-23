using System.Data;
using System.Security.Claims;
using System.Text.Json;
using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Web.Services;

/// <summary>One receiving receipt confirms one existing load. Dispatch movements are its immutable manifest.</summary>
public sealed class TruckReceiptReconciliationService(
    CropQcDbContext db,
    IOutsideWarehouseTransferService inventory,
    IInterCrewTreatmentLineageService lineage,
    IRoomInventoryLedgerQueryService ledger,
    IInventoryDeductionInvariantService invariant,
    IUserAccessService access,
    IHttpContextAccessor context,
    IBusinessTimeService time)
{
    public const string ReturnToSource = "TransitAllocationReturn";
    public const string ReopenDestination = "TransferReceiptReopen";
    private ClaimsPrincipal Principal => context.HttpContext?.User ?? new ClaimsPrincipal();

    public async Task<TruckReceiptPage> GetAsync(long? receiptId, long? transferId, CancellationToken ct)
    {
        var receipt = receiptId is null ? null : await db.Receipts.AsNoTracking().Include(x => x.VarietyLines)
            .Include(x => x.Warehouse).SingleOrDefaultAsync(x => x.Id == receiptId && !x.IsDeleted, ct);
        var transfer = transferId is null
            ? await Transfers().SingleOrDefaultAsync(x => receiptId != null && x.ReceivingReceiptId == receiptId, ct)
            : await Transfers().SingleOrDefaultAsync(x => x.Id == transferId, ct);
        if (receipt is null && transfer?.ReceivingReceiptId is long linked)
            receipt = await db.Receipts.AsNoTracking().Include(x => x.VarietyLines).Include(x => x.Warehouse).SingleAsync(x => x.Id == linked, ct);
        var page = new TruckReceiptPage
        {
            Receipt = receipt,
            Transfer = transfer,
            Profiles = await db.FruitProfiles.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.VarietyCode).ToListAsync(ct),
            CanAdmin = await IsAdminAsync(ct),
            CanEditReceipt = await access.HasAccessAsync(Principal, ApplicationAreas.Receipts, PageAccessLevel.Edit, ct),
            CanEditTransfer = await access.HasAccessAsync(Principal, ApplicationAreas.Transfers, PageAccessLevel.Edit, ct)
        };
        if (transfer is not null)
        {
            page.Allocations = await ActiveAllocationsAsync(transfer.Id, ct);
            page.Comparison = Compare(page.Allocations, receipt?.VarietyLines ?? [], page.Profiles);
            if (transfer.Status == InterCrewTransferStatuses.InTransit)
                page.Available = (await inventory.GetInventoryAsync(ct)).Where(x => x.RoomId == transfer.SourceRoomId && x.IsAvailable).ToList();
        }
        if (receipt is { IsTransferReceipt: true, TransferCompletedAt: null } && transfer is null)
        {
            var pending = await Transfers().Where(x => x.Status == InterCrewTransferStatuses.InTransit && x.ReceivingReceiptId == null).OrderBy(x => x.LoadedAt).ToListAsync(ct);
            var candidates = new List<InterCrewTransfer>();
            foreach (var candidate in pending)
            {
                var allocations = await ActiveAllocationsAsync(candidate.Id, ct);
                if (!RouteMatches(candidate, receipt) || !Compatible(allocations, receipt)) continue;
                candidates.Add(candidate);
                page.CandidateVarieties[candidate.Id] = string.Join(", ", Compare(allocations, [], page.Profiles).Select(x => $"{x.Variety}: {x.Transfer}"));
            }
            page.Candidates = candidates; // Deliberately unranked and unselected. Quantities do not filter candidates.
        }
        if (receiptId is not null && receipt is null) page.Error = "Receipt not found.";
        else if (receipt is { IsTransferReceipt: false }) page.Error = "This normal receipt already posts its own inventory. Create a Truck Receipt with Await transfer reconciliation selected to receive a transfer.";
        return page;
    }

    public static IReadOnlyList<VarietyReconciliation> Compare(IEnumerable<TransitAllocation> allocations,
        IEnumerable<ReceiptVarietyLine> lines, IEnumerable<FruitProfile> profiles)
    {
        var shipped = allocations.GroupBy(x => x.Movement.SourceSegment!.FruitProfileId ?? 0).ToDictionary(x => x.Key, x => x.Sum(y => y.Bins));
        var received = lines.GroupBy(x => x.FruitProfileId).ToDictionary(x => x.Key, x => x.Sum(y => y.BinCount));
        var names = profiles.ToDictionary(x => x.Id, x => $"{x.VarietyCode} — {x.Name} ({x.ProductionType})");
        return shipped.Keys.Union(received.Keys).Order().Select(id => new VarietyReconciliation(id,
            names.GetValueOrDefault(id, $"Variety #{id}"), shipped.GetValueOrDefault(id), received.GetValueOrDefault(id))).ToList();
    }

    public Task<string?> MatchAsync(TruckReceiptActionForm form, CancellationToken ct) => WriteAsync(async actor =>
    {
        await RequireAccessAsync(ApplicationAreas.Receipts, PageAccessLevel.Edit, ct);
        var receipt = await ReceiptAsync(form, ct);
        var transfer = await TransferAsync(form.TransferId, form.TransferVersion, ct);
        Require(receipt.TransferCompletedAt is null && transfer.Status == InterCrewTransferStatuses.InTransit, "Only open receipts and In Transit transfers may be matched.");
        Require(RouteMatches(transfer, receipt), "The transfer does not belong to this receiving location/company.");
        await RequireCrewAsync(actor, transfer.DestinationCustodyGroup, ct);
        Require(transfer.ReceivingReceiptId is null && !await db.InterCrewTransfers.AnyAsync(x => x.ReceivingReceiptId == receipt.Id, ct), "The transfer or receipt is already linked. Reload the current match.");
        var allocations = await ValidateTransitAsync(transfer, ct);
        Require(Compatible(allocations, receipt), "The receipt and transfer must have compatible canonical varieties and crop year.");
        transfer.RequiresTruckReceipt = true;
        transfer.ReceivingReceiptId = receipt.Id;
        transfer.ConcurrencyVersion++;
        receipt.ConcurrencyVersion++;
        Audit(actor, "MatchTransferReceipt", transfer, receipt, null, new { transfer.ReceivingReceiptId });
    }, ct);

    public Task<string?> EditReceiptAsync(TruckReceiptActionForm form, CancellationToken ct) => WriteAsync(async actor =>
    {
        await RequireAccessAsync(ApplicationAreas.Receipts, PageAccessLevel.Edit, ct);
        var receipt = await ReceiptAsync(form, ct);
        Require(receipt.TransferCompletedAt is null, "Reopen the completed match before changing its receiving quantities.");
        await RequireCrewAsync(actor, TruckReceiptRoutes.Group(receipt.Warehouse.Code) ?? "", ct);
        var lines = form.Lines.Where(x => x.FruitProfileId != 0 || x.BinCount != 0).ToList();
        Require(lines.Count > 0 && lines.Count <= 100 && lines.All(x => x.BinCount > 0)
            && lines.Sum(x => (long)x.BinCount) <= int.MaxValue
            && lines.Select(x => x.FruitProfileId).Distinct().Count() == lines.Count, "Enter each canonical variety once with a positive bin count.");
        var ids = lines.Select(x => x.FruitProfileId).ToList();
        Require(await db.FruitProfiles.CountAsync(x => ids.Contains(x.Id) && x.IsActive, ct) == ids.Count, "Select active canonical varieties.");
        var before = receipt.VarietyLines.Select(x => new { x.FruitProfileId, x.BinCount }).ToArray();
        foreach (var old in receipt.VarietyLines.ToList())
        {
            var replacement = lines.SingleOrDefault(x => x.FruitProfileId == old.FruitProfileId);
            if (replacement is null) db.ReceiptVarietyLines.Remove(old);
            else old.BinCount = replacement.BinCount;
        }
        foreach (var line in lines.Where(x => !receipt.VarietyLines.Any(y => y.FruitProfileId == x.FruitProfileId)))
            receipt.VarietyLines.Add(new() { FruitProfileId = line.FruitProfileId, BinCount = line.BinCount });
        receipt.BinCount = checked(lines.Sum(x => x.BinCount));
        receipt.FruitProfileId = lines[0].FruitProfileId;
        receipt.ConcurrencyVersion++;
        receipt.UpdatedAt = time.UtcNow;
        Audit(actor, "EditTransferReceiptVarieties", null, receipt, before, lines);
    }, ct);

    public Task<string?> CompleteAsync(TruckReceiptActionForm form, CancellationToken ct) => WriteAsync(async actor =>
    {
        await RequireAccessAsync(ApplicationAreas.Receipts, PageAccessLevel.Edit, ct);
        var receipt = await db.Receipts.Include(x => x.VarietyLines).Include(x => x.Warehouse).SingleOrDefaultAsync(x => x.Id == form.ReceiptId && !x.IsDeleted, ct);
        var transfer = await db.InterCrewTransfers.Include(x => x.SourceWarehouse).SingleOrDefaultAsync(x => x.Id == form.TransferId, ct);
        Require(receipt is { IsTransferReceipt: true } && transfer is not null, "The transfer receipt was not found.");
        await RequireCrewAsync(actor, transfer!.DestinationCustodyGroup, ct);
        Require(transfer.ReceivingReceiptId == receipt!.Id && RouteMatches(transfer, receipt), "The receipt relationship or destination changed. Reload the match.");
        if (receipt.TransferCompletedAt is not null && transfer.Status == InterCrewTransferStatuses.Received) return; // Safe retry, no writes.
        Require(receipt.ConcurrencyVersion == form.ReceiptVersion && transfer.ConcurrencyVersion == form.TransferVersion, "The receipt or transfer changed. Reload and reconcile the current quantities.");
        Require(receipt.TransferCompletedAt is null && transfer.Status == InterCrewTransferStatuses.InTransit, "Only an open matched receipt can complete.");
        Require(!string.IsNullOrWhiteSpace(receipt.CompuTechReceiptId) && receipt.ReceiptType == "Truck receipt", "A Computech Truck Receipt number is required.");
        var allocations = await ValidateTransitAsync(transfer, ct);
        Require(Compatible(allocations, receipt), "The canonical variety or crop year changed.");
        var comparison = Compare(allocations, receipt.VarietyLines, []);
        Require(receipt.VarietyLines.All(x => x.BinCount > 0) && receipt.VarietyLines.Sum(x => x.BinCount) == receipt.BinCount
            && comparison.Count > 0 && comparison.All(x => x.Difference == 0), "Reconciliation Required: transfer and receipt quantities must match exactly for every variety. There is no override.");
        var room = await db.Rooms.Include(x => x.Warehouse).SingleOrDefaultAsync(x => x.Id == receipt.RoomId && x.IsActive && x.Warehouse.IsActive, ct);
        Require(room is not null && room.WarehouseId == receipt.WarehouseId, "Select an active destination room belonging to the receiving warehouse.");
        Require(await RoomMovementSealGuard.ValidateAsync(db, [], [receipt.RoomId], time, ct) is null, "The destination room is sealed.");
        Require(!await db.RoomInventoryAdjustments.AnyAsync(x => x.ReceiptId == receipt.Id, ct), "Receiving evidence must not already own inventory.");
        var key = $"truck-receipt:{transfer.Id}:v{transfer.ConcurrencyVersion}";
        foreach (var allocation in allocations)
        {
            var source = allocation.Movement.SourceSegment!;
            await RejectChangedIdentityAsync(source, ct);
            var destination = await DestinationSegmentAsync(source, receipt.WarehouseId, receipt.RoomId, ct);
            destination.CurrentBins += allocation.Bins;
            destination.ConcurrencyVersion++;
            destination.UpdatedAt = time.UtcNow;
            var movement = Movement(transfer, source, allocation.Bins, TreatmentLineageMovementTypes.InterCrewReceive, key + ":" + allocation.Movement.Id, actor);
            movement.SourceRoomId = null;
            movement.DestinationSegment = destination;
            movement.DestinationRoomId = receipt.RoomId;
            db.TreatmentLineageMovements.Add(movement);
            await AddLedgerAsync(transfer, source, receipt.WarehouseId, receipt.RoomId, allocation.Bins, InterCrewTransferAdjustmentTypes.Receive, movement.OperationKey, actor, ct);
            await db.SaveChangesAsync(ct);
        }
        transfer.RequiresTruckReceipt = true;
        transfer.DestinationWarehouseId = receipt.WarehouseId;
        transfer.DestinationRoomId = receipt.RoomId;
        transfer.BinsReceived = transfer.BinsLoaded;
        transfer.VarianceBins = 0;
        transfer.ReceiveOperationKey = key;
        transfer.ReceivedAt = time.UtcNow;
        transfer.ReceivedByUserId = actor.Id;
        transfer.Status = InterCrewTransferStatuses.Received;
        transfer.ConcurrencyVersion++;
        receipt.TransferCompletedAt = time.UtcNow;
        receipt.UpdatedAt = time.UtcNow;
        receipt.ConcurrencyVersion++;
        Audit(actor, "CompleteTransferReceipt", transfer, receipt, null, new { receipt.CompuTechReceiptId, receipt.BinCount, Comparison = comparison });
        await invariant.ValidateBeforeCommitAsync(ct);
    }, ct);

    public Task<string?> EditTransferAsync(TransitEditForm form, CancellationToken ct) => WriteAsync(async actor =>
    {
        await RequireAccessAsync(ApplicationAreas.Transfers, PageAccessLevel.Edit, ct);
        var transfer = await TransferAsync(form.TransferId, form.TransferVersion, ct);
        Require(transfer.Status == InterCrewTransferStatuses.InTransit, "Only an In Transit load may be edited.");
        Require(TruckReceiptRoutes.RequiresReceiptForGroup(transfer.SourceWarehouse.Code, transfer.DestinationCustodyGroup), "This transfer uses the existing internal workflow.");
        await RequireCrewAsync(actor, TruckReceiptRoutes.Group(transfer.SourceWarehouse.Code) ?? "", ct);
        Require(form.Bins > 0 && !string.IsNullOrWhiteSpace(form.Reason), "A positive quantity and edit reason are required.");
        Require(await RoomMovementSealGuard.ValidateAsync(db, [transfer.SourceRoomId], [], time, ct) is null, "The source room is sealed.");
        var allocations = await ValidateTransitAsync(transfer, ct);
        var key = $"transit-edit:{transfer.Id}:v{transfer.ConcurrencyVersion}";
        var before = transfer.BinsLoaded;
        transfer.RequiresTruckReceipt = true;
        if (form.DispatchMovementId is long movementId)
        {
            var allocation = allocations.SingleOrDefault(x => x.Movement.Id == movementId);
            Require(allocation is not null && form.Bins <= allocation.Bins && form.Bins <= transfer.BinsLoaded, "Only bins still controlled by this load can return to source.");
            var source = allocation!.Movement.SourceSegment!;
            await RejectChangedIdentityAsync(source, ct);
            source.CurrentBins += form.Bins;
            source.ConcurrencyVersion++;
            source.UpdatedAt = time.UtcNow;
            var movement = Movement(transfer, source, form.Bins, TreatmentLineageMovementTypes.InterCrewReversal, key, actor);
            movement.SourceSegmentId = null;
            movement.SourceRoomId = null;
            movement.DestinationSegment = source;
            movement.DestinationRoomId = source.RoomId;
            movement.ReversesTreatmentLineageMovementId = movementId;
            db.TreatmentLineageMovements.Add(movement);
            await AddLedgerAsync(transfer, source, source.WarehouseId, source.RoomId, form.Bins, ReturnToSource, key, actor, ct);
            transfer.BinsLoaded -= form.Bins;
        }
        else
        {
            var option = (await inventory.GetInventoryAsync(ct)).SingleOrDefault(x => x.SourceKey == form.SourceKey && x.RoomId == transfer.SourceRoomId && x.WarehouseId == transfer.SourceWarehouseId);
            Require(option is { IsAvailable: true } && option.AvailableBins == form.ExpectedAvailableBins && form.Bins <= option.AvailableBins, "Source inventory changed or is unavailable. Reload before adding bins.");
            var snapshot = await inventory.ResolveInventoryAsync(option!, ct);
            Require(snapshot is not null && snapshot.CurrentBins >= form.Bins, "The exact source inventory is no longer available.");
            Require(snapshot!.CropYear == transfer.CropYear, "All varieties in this load must belong to the same crop year.");
            transfer.BinsLoaded = checked(transfer.BinsLoaded + form.Bins);
            await db.SaveChangesAsync(ct);
            var result = await lineage.DispatchAsync(snapshot!, option!.TreatmentSignature, form.Bins, key, transfer.Id, time.UtcNow, actor.Id, ct);
            Require(result.Success, result.Error ?? "Cannot dispatch treatment lineage.");
            var added = await db.TreatmentLineageMovements.Include(x => x.SourceSegment).Where(x => x.OperationKey.StartsWith(key + ":")).ToListAsync(ct);
            foreach (var movement in added)
            {
                await AddLedgerAsync(transfer, movement.SourceSegment!, transfer.SourceWarehouseId, transfer.SourceRoomId, -movement.BinCount,
                    InterCrewTransferAdjustmentTypes.Dispatch, movement.OperationKey, actor, ct);
                await db.SaveChangesAsync(ct);
            }
        }
        if (transfer.BinsLoaded == 0)
        {
            transfer.Status = InterCrewTransferStatuses.Reversed;
            transfer.ReversedAt = time.UtcNow;
            transfer.ReversedByUserId = actor.Id;
            transfer.ReversalReason = form.Reason;
            transfer.ReversalOperationKey = key;
            if (transfer.ReceivingReceiptId is long linked)
            {
                var receipt = await db.Receipts.SingleAsync(x => x.Id == linked, ct);
                Require(receipt.TransferCompletedAt is null, "A completed receipt cannot be cancelled.");
                receipt.ConcurrencyVersion++;
                transfer.ReceivingReceiptId = null;
            }
        }
        transfer.ConcurrencyVersion++;
        Audit(actor, "EditInTransitLoad", transfer, null, new { BinsLoaded = before }, new { transfer.BinsLoaded, form.Reason });
        await db.SaveChangesAsync(ct);
        if (transfer.BinsLoaded > 0) await ValidateTransitAsync(transfer, ct);
        else Require((await ActiveAllocationsAsync(transfer.Id, ct)).Count == 0, "Cancellation allocations do not balance.");
        await invariant.ValidateBeforeCommitAsync(ct);
    }, ct);

    public Task<string?> ReopenAsync(TruckReceiptActionForm form, CancellationToken ct) => WriteAsync(async actor =>
    {
        Require(await IsAdminAsync(ct), "Only Admin users may reopen or unlink a transfer receipt.");
        Require(!string.IsNullOrWhiteSpace(form.Reason), "An audit reason is required.");
        var receipt = await ReceiptAsync(form, ct);
        var transfer = await TransferAsync(form.TransferId, form.TransferVersion, ct);
        Require(transfer.ReceivingReceiptId == receipt.Id, "This receipt is not linked to this transfer.");
        var key = $"truck-reopen:{transfer.Id}:v{transfer.ConcurrencyVersion}";
        if (receipt.TransferCompletedAt is not null)
        {
            Require(transfer.Status == InterCrewTransferStatuses.Received && transfer.ReceiveOperationKey is not null, "Completed transfer state is inconsistent.");
            var receives = await db.TreatmentLineageMovements.Include(x => x.DestinationSegment)
                .Where(x => x.InterCrewTransferId == transfer.Id && x.OperationKey.StartsWith(transfer.ReceiveOperationKey + ":")).ToListAsync(ct);
            Require(receives.Count > 0 && receives.Sum(x => x.BinCount) == transfer.BinsLoaded, "The exact receiving evidence is missing.");
            Require(await RoomMovementSealGuard.ValidateAsync(db, [receipt.RoomId], [], time, ct) is null, "The receiving room is sealed.");
            var receiveLedgerIds = await db.RoomInventoryAdjustments.Where(x => x.InterCrewTransferId == transfer.Id
                && x.InventoryOperationKey != null && x.InventoryOperationKey.StartsWith(transfer.ReceiveOperationKey + ":")).Select(x => x.Id).ToListAsync(ct);
            Require(receiveLedgerIds.Count > 0, "The receiving ledger evidence is missing.");
            var firstReceiveLedgerId = receiveLedgerIds.Min();
            foreach (var group in receives.GroupBy(x => x.DestinationSegmentId))
            {
                var segment = group.First().DestinationSegment!;
                await RejectChangedIdentityAsync(segment, ct);
                var bins = group.Sum(x => x.BinCount);
                var receiveIds = receives.Select(x => x.Id).ToList();
                Require(segment.CurrentBins >= bins
                    && !await db.TreatmentLineageMovements.AnyAsync(x => (x.SourceSegmentId == segment.Id || x.DestinationSegmentId == segment.Id)
                        && x.Id > receiveIds.Min() && !receiveIds.Contains(x.Id), ct)
                    && !await db.RoomInventoryAdjustments.AnyAsync(x => x.RoomId == segment.RoomId && x.FruitProfileId == segment.FruitProfileId
                        && x.GrowerLotId == segment.GrowerLotId && x.CropYear == segment.CropYear && x.Id > firstReceiveLedgerId
                        && x.InterCrewTransferId != transfer.Id, ct), "Cannot reopen: subsequent inventory or treatment activity exists. History was preserved.");
                segment.CurrentBins -= bins;
                segment.ConcurrencyVersion++;
                segment.UpdatedAt = time.UtcNow;
                foreach (var receive in group)
                {
                    var reversal = Movement(transfer, segment, receive.BinCount, TreatmentLineageMovementTypes.InterCrewReversal, key + ":" + receive.Id, actor);
                    reversal.ReversesTreatmentLineageMovementId = receive.Id;
                    db.TreatmentLineageMovements.Add(reversal);
                    await AddLedgerAsync(transfer, segment, segment.WarehouseId, segment.RoomId, -receive.BinCount, ReopenDestination, reversal.OperationKey, actor, ct);
                    await db.SaveChangesAsync(ct);
                }
            }
            transfer.Status = InterCrewTransferStatuses.InTransit;
            transfer.BinsReceived = null; transfer.VarianceBins = null;
            transfer.ReceivedAt = null; transfer.ReceivedByUserId = null; transfer.ReceiveOperationKey = null;
            receipt.TransferCompletedAt = null;
        }
        else Require(transfer.Status == InterCrewTransferStatuses.InTransit, "Only a pending or completed matched receipt may be unlinked.");
        transfer.ReceivingReceiptId = null;
        transfer.ConcurrencyVersion++;
        receipt.ConcurrencyVersion++;
        receipt.UpdatedAt = time.UtcNow;
        Audit(actor, "ReopenUnlinkTransferReceipt", transfer, receipt, new { ReceivingReceiptId = receipt.Id }, new { form.Reason, Status = transfer.Status });
        await db.SaveChangesAsync(ct);
        await ValidateTransitAsync(transfer, ct);
        await invariant.ValidateBeforeCommitAsync(ct);
    }, ct);

    public async Task<IReadOnlyList<TransitAllocation>> ActiveAllocationsAsync(long transferId, CancellationToken ct)
    {
        var movements = await db.TreatmentLineageMovements.Include(x => x.SourceSegment).ThenInclude(x => x!.Applications)
            .Where(x => x.InterCrewTransferId == transferId).OrderBy(x => x.Id).ToListAsync(ct);
        return movements.Where(x => x.MovementType == TreatmentLineageMovementTypes.InterCrewDispatch && x.ReversesTreatmentLineageMovementId == null)
            .Select(x => new TransitAllocation(x, x.BinCount - movements.Where(y => y.ReversesTreatmentLineageMovementId == x.Id).Sum(y => y.BinCount)))
            .Where(x => x.Bins != 0).ToList();
    }

    private async Task<IReadOnlyList<TransitAllocation>> ValidateTransitAsync(InterCrewTransfer transfer, CancellationToken ct)
    {
        var allocations = await ActiveAllocationsAsync(transfer.Id, ct);
        Require(transfer.Status == InterCrewTransferStatuses.InTransit && transfer.BinsReceived is null && transfer.BinsLoaded > 0
            && allocations.Count > 0 && allocations.All(x => x.Bins > 0 && x.Movement.SourceSegment is not null
                && x.Movement.SourceSegment.RoomId == transfer.SourceRoomId
                && x.Movement.SourceSegment.WarehouseId == transfer.SourceWarehouseId
                && x.Movement.SourceSegment.IdentityKey == x.Movement.IdentityKey
                && x.Movement.SourceSegment.TreatmentSignature == x.Movement.TreatmentSignatureSnapshot)
            && allocations.Sum(x => x.Bins) == transfer.BinsLoaded, "In Transit evidence changed or does not balance. No receipt completion is allowed.");
        var rows = await db.RoomInventoryAdjustments.Where(x => x.InterCrewTransferId == transfer.Id).ToListAsync(ct);
        var source = rows.Where(x => x.AdjustmentType is InterCrewTransferAdjustmentTypes.Dispatch or ReturnToSource).ToList();
        Require(source.Sum(x => x.ChangeAmount) == -transfer.BinsLoaded
            && rows.Except(source).Sum(x => x.ChangeAmount) == 0, "The transit ledger is inconsistent or inventory is already at destination.");
        foreach (var group in allocations.GroupBy(x => new { x.Movement.SourceSegment!.CropYear, x.Movement.SourceSegment.GrowerLotId, x.Movement.SourceSegment.FruitProfileId, x.Movement.SourceSegment.LotNumberSnapshot }))
            Require(source.Where(x => x.CropYear == group.Key.CropYear && x.GrowerLotId == group.Key.GrowerLotId
                && x.FruitProfileId == group.Key.FruitProfileId && x.LotNumber == group.Key.LotNumberSnapshot).Sum(x => x.ChangeAmount) == -group.Sum(x => x.Bins), "Transit identity does not balance with its ledger.");
        return allocations;
    }

    private static bool RouteMatches(InterCrewTransfer transfer, Receipt receipt) =>
        TruckReceiptRoutes.RequiresReceiptForGroup(transfer.SourceWarehouse.Code, transfer.DestinationCustodyGroup)
        && TruckReceiptRoutes.Group(receipt.Warehouse.Code) == transfer.DestinationCustodyGroup;
    private static bool Compatible(IReadOnlyList<TransitAllocation> allocations, Receipt receipt) => allocations.Count > 0
        && allocations.All(x => x.Movement.SourceSegment?.CropYear == receipt.CropYear)
        && allocations.Any(x => receipt.VarietyLines.Any(y => y.FruitProfileId == x.Movement.SourceSegment!.FruitProfileId));
    private IQueryable<InterCrewTransfer> Transfers() => db.InterCrewTransfers.AsNoTracking().Include(x => x.SourceWarehouse).Include(x => x.SourceRoom);

    private async Task<Receipt> ReceiptAsync(TruckReceiptActionForm form, CancellationToken ct)
    {
        var receipt = await db.Receipts.Include(x => x.VarietyLines).Include(x => x.Warehouse).SingleOrDefaultAsync(x => x.Id == form.ReceiptId && !x.IsDeleted, ct);
        Require(receipt is { IsTransferReceipt: true } && receipt.ConcurrencyVersion == form.ReceiptVersion, "Transfer receipt changed or was not found. Reload before saving.");
        Require(!await db.RoomInventoryAdjustments.AnyAsync(x => x.ReceiptId == receipt!.Id, ct), "This receipt already owns inventory. It cannot also receive transferred inventory.");
        return receipt!;
    }
    private async Task<InterCrewTransfer> TransferAsync(long id, long version, CancellationToken ct)
    {
        var transfer = await db.InterCrewTransfers.Include(x => x.SourceWarehouse).SingleOrDefaultAsync(x => x.Id == id, ct);
        Require(transfer is not null && transfer.ConcurrencyVersion == version, "Transfer changed or was not found. Reload before saving.");
        return transfer!;
    }
    private async Task RejectChangedIdentityAsync(TreatmentLineageSegment segment, CancellationToken ct)
    {
        var error = await InventoryIdentityWriteGuard.RejectSupersededAsync(db, segment.CropYear, segment.GrowerLotId, segment.FruitProfileId, "Truck Receipt reconciliation", ct);
        Require(error is null, error ?? "Inventory identity changed.");
    }

    private async Task<TreatmentLineageSegment> DestinationSegmentAsync(TreatmentLineageSegment source, int warehouseId, int roomId, CancellationToken ct)
    {
        var destination = await db.TreatmentLineageSegments.Include(x => x.Applications).SingleOrDefaultAsync(x => x.WarehouseId == warehouseId && x.RoomId == roomId
            && x.IdentityKey == source.IdentityKey && x.TreatmentSignature == source.TreatmentSignature && x.ReceiptId == source.ReceiptId, ct);
        if (destination is not null) return destination;
        destination = new TreatmentLineageSegment
        {
            WarehouseId = warehouseId,
            RoomId = roomId,
            ReceiptId = source.ReceiptId,
            CropYear = source.CropYear,
            GrowerLotId = source.GrowerLotId,
            FruitProfileId = source.FruitProfileId,
            IdentityKey = source.IdentityKey,
            GrowerNumberSnapshot = source.GrowerNumberSnapshot,
            GrowerNameSnapshot = source.GrowerNameSnapshot,
            LotNumberSnapshot = source.LotNumberSnapshot,
            VarietyCodeSnapshot = source.VarietyCodeSnapshot,
            ProductionTypeSnapshot = source.ProductionTypeSnapshot,
            IsOrganicSnapshot = source.IsOrganicSnapshot,
            InventoryStatusSnapshot = source.InventoryStatusSnapshot,
            TreatmentState = source.TreatmentState,
            TreatmentSignature = source.TreatmentSignature,
            CreatedAt = time.UtcNow,
            UpdatedAt = time.UtcNow
        };
        foreach (var application in source.Applications)
            destination.Applications.Add(new() { RoomTreatmentApplicationId = application.RoomTreatmentApplicationId, Sequence = application.Sequence });
        db.TreatmentLineageSegments.Add(destination);
        return destination;
    }

    private TreatmentLineageMovement Movement(InterCrewTransfer transfer, TreatmentLineageSegment source, int bins, string type, string key, User actor) => new()
    {
        OperationKey = key,
        MovementType = type,
        SourceSegmentId = source.Id,
        SourceRoomId = source.RoomId,
        IdentityKey = source.IdentityKey,
        TreatmentStateSnapshot = source.TreatmentState,
        TreatmentSignatureSnapshot = source.TreatmentSignature,
        ReceiptId = source.ReceiptId,
        BinCount = bins,
        InterCrewTransferId = transfer.Id,
        OccurredAt = time.UtcNow,
        CreatedAt = time.UtcNow,
        CreatedByUserId = actor.Id
    };

    private async Task AddLedgerAsync(InterCrewTransfer transfer, TreatmentLineageSegment segment, int warehouseId, int roomId,
        int delta, string type, string key, User actor, CancellationToken ct)
    {
        var current = (await ledger.GetSnapshotsAsync(warehouseId, [roomId], ct)).Where(x => x.CropYear == segment.CropYear
            && x.GrowerLotId == segment.GrowerLotId && x.FruitProfileId == segment.FruitProfileId && x.Lot == segment.LotNumberSnapshot).Sum(x => x.CurrentBins);
        db.RoomInventoryAdjustments.Add(new()
        {
            WarehouseId = warehouseId,
            RoomId = roomId,
            // Movement time controls opening-baseline accounting; original receipt provenance lives on the lineage movement.
            ReceiptId = null,
            CropYear = segment.CropYear,
            GrowerLotId = segment.GrowerLotId,
            FruitProfileId = segment.FruitProfileId,
            GrowerName = segment.GrowerNameSnapshot,
            LotNumber = segment.LotNumberSnapshot,
            VarietyCode = segment.VarietyCodeSnapshot,
            InventoryStatus = segment.InventoryStatusSnapshot,
            OldBinCount = current,
            ChangeAmount = delta,
            NewBinCount = checked(current + delta),
            AdjustmentType = type,
            Source = "Truck Receipt reconciliation",
            Reason = type,
            AdjustmentAt = time.UtcNow,
            CreatedAt = time.UtcNow,
            CreatedByUserId = actor.Id,
            InterCrewTransferId = transfer.Id,
            InventoryOperationKey = key,
            InventoryInvariantVersion = InventoryDeductionInvariantService.CurrentVersion
        });
    }

    private async Task<string?> WriteAsync(Func<User, Task> write, CancellationToken ct)
    {
        await using var transaction = db.Database.IsRelational() && db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var outer = transaction is null ? db.Database.CurrentTransaction : null;
        var savepoint = "truck_" + Guid.NewGuid().ToString("N");
        if (outer is not null) await outer.CreateSavepointAsync(savepoint, ct);
        try
        {
            var email = Principal.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant();
            var actor = await db.Users.SingleOrDefaultAsync(x => x.Email == email && x.IsActive, ct);
            Require(actor is not null, "An active signed-in user is required.");
            await write(actor!);
            await db.SaveChangesAsync(ct);
            if (transaction is not null) await transaction.CommitAsync(ct);
            if (outer is not null) await outer.ReleaseSavepointAsync(savepoint, ct);
            return null;
        }
        catch (Exception ex) when (ex is InvalidOperationException or DbUpdateException || InventoryMovementConcurrency.IsConflict(ex))
        {
            if (transaction is not null) await transaction.RollbackAsync(ct);
            if (outer is not null) await outer.RollbackToSavepointAsync(savepoint, ct);
            db.ChangeTracker.Clear();
            return InventoryMovementConcurrency.IsConflict(ex) ? InventoryMovementConcurrency.RefreshMessage : ex.Message;
        }
    }
    private async Task RequireAccessAsync(string area, PageAccessLevel level, CancellationToken ct) =>
        Require(await access.HasAccessAsync(Principal, area, level, ct), "You do not have permission to change this record.");
    private Task<bool> IsAdminAsync(CancellationToken ct) => access.HasAccessAsync(Principal, ApplicationAreas.Transfers, PageAccessLevel.Admin, ct);
    private async Task RequireCrewAsync(User actor, string group, CancellationToken ct) => Require(await IsAdminAsync(ct)
        || actor.EmploymentFacility == EmploymentFacilities.Shared
        || actor.EmploymentFacility == EmploymentFacilities.Ebs && group == TransferCustodyGroups.Ebs
        || actor.EmploymentFacility == EmploymentFacilities.Wp && group == TransferCustodyGroups.WpDh, "This record belongs to another receiving or dispatch crew.");
    private static void Require(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }
    private void Audit(User actor, string action, InterCrewTransfer? transfer, Receipt? receipt, object? before, object after) => db.AuditLogs.Add(new()
    {
        UserId = actor.Id,
        Action = action,
        EntityName = "TransferReceipt",
        EntityKey = $"{transfer?.Id}/{receipt?.Id}",
        BeforeValuesJson = before is null ? null : JsonSerializer.Serialize(before),
        AfterValuesJson = JsonSerializer.Serialize(after),
        SourceApplication = "CropQc.Web Truck Receipt reconciliation",
        CreatedAt = time.UtcNow
    });
}
