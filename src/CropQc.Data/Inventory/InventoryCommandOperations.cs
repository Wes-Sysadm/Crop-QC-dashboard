using System.Collections.Immutable;
using System.Text.Json;
using CropQc.Data.Entities;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

public sealed partial class InventoryCommandExecutor
{
    private static void ValidateShape(InventoryCommand c)
    {
        Require(c.Kind != InventoryCommandKind.ReverseRoomMove || c.PhysicalParentId > 0 && c.Lines.Length == 1,
            "Room reversal requires the exact original transfer.");
        Require(c.Lines.All(x => Enum.IsDefined(x.AdjustmentDirection) && (x.AdjustmentDirection == InventoryAdjustmentDirection.Decrease
            || c.Kind is InventoryCommandKind.ReceiptCorrection or InventoryCommandKind.BaselineAdjustment or InventoryCommandKind.ManualStockAddition)), "Only explicit corrections may add authoritative inventory.");
        Require(c.Kind != InventoryCommandKind.ManualStockAddition || c.Lines.Length == 1 && c.Lines[0].AdjustmentDirection == InventoryAdjustmentDirection.Increase
            && c.Lines[0].TreatmentSignature == "x" && c.Lines[0].ReceiptId == null, "Manual stock creation must explicitly retain Unknown treatment and unattributed receipt provenance.");
        Require(c.Lines.All(x => x.Source.Location.Custody == InventoryCustody.Room
            || c.Kind is InventoryCommandKind.ReceiveTransfer or InventoryCommandKind.Return or InventoryCommandKind.ReturnTransitAllocation), "Operation requires room inventory.");
        Require(c.Kind != InventoryCommandKind.ReceiptCorrection || c.Lines.All(x => x.ReceiptId != null), "Receipt correction requires exact receipt scope.");
        Require(c.Kind != InventoryCommandKind.ReceiptDepletion || c.Lines.Length == 1 && c.Lines[0].ReceiptId > 0, "Receipt depletion requires one exact receipt and treatment selection.");
        Require(!InventoryCommandPolicy.IsRoomMove(c.Kind) || c.Lines.All(x => x.Destination != null
            && x.Destination.RoomId != x.Source.Location.RoomId), "Room movement requires a different destination.");
        Require(c.Kind != InventoryCommandKind.ReceiveTransfer || c.Lines.All(x => x.Source.Location.Custody == InventoryCustody.InTransit && x.Destination != null), "Receive requires in-transit custody and a destination.");
        Require(c.Kind is not (InventoryCommandKind.Return or InventoryCommandKind.ReopenTransfer)
            || !string.IsNullOrWhiteSpace(c.OriginalOperationKey) || c.PhysicalParentId > 0, "Reversal requires the original committed operation or exact historical custody parent.");
        Require(c.Kind is not (InventoryCommandKind.TreatmentAssignment or InventoryCommandKind.ReceiptTreatmentAssignment) || c.TreatmentChemicalId != null, "Treatment chemical is required.");
        Require(c.Kind != InventoryCommandKind.ReceiptTreatmentAssignment || c.Lines.All(x => x.ReceiptId > 0)
            && c.Lines.Select(x => x.ReceiptId).Distinct().Count() == 1 && c.Lines.Select(x => x.Source.Location.RoomId).Distinct().Count() == 1,
            "Receiving treatment requires one exact receipt in one current room.");
        Require(c.Kind != InventoryCommandKind.TreatmentReversal || c.TreatmentApplicationId != null, "Original application is required.");
        Require(c.Kind is not (InventoryCommandKind.ProcessorSale or InventoryCommandKind.OutsideWarehouseTransfer) || c.CounterpartyId != null, "Counterparty is required.");
        Require(c.Kind != InventoryCommandKind.ProcessorSale || c.ProcessorTerms is { Rate: >= 0 } terms
            && ProcessorPricingBases.IsValid(terms.Basis) && terms.Currency.Length == 3
            && (terms.Basis != ProcessorPricingBases.PerTon || c.Lines.All(x => (x.PoundsPerBin ?? terms.PoundsPerBin) > 0)), "Explicit valid processor pricing terms are required.");
        Require(c.Kind != InventoryCommandKind.TransferEdit || (c.OriginalOperationKey != null || c.PhysicalParentId > 0) && c.ExpectedTransferVersion != null && c.Lines.Length == 1,
            "Transfer edit requires the original dispatch and expected parent version.");
        Require(c.Kind != InventoryCommandKind.ReturnTransitAllocation || c.PhysicalParentId > 0 && c.ExpectedTransferVersion != null
            && c.DispatchMovementId > 0 && c.Lines.Length == 1 && c.Lines[0].Source.Location.Custody == InventoryCustody.InTransit,
            "Partial return requires one exact dispatch allocation and parent version.");
        Require(c.Kind != InventoryCommandKind.InterCompanyDispatch || c.Lines.Select(x => (x.Source.Location.WarehouseId, x.Source.Location.RoomId)).Distinct().Count() == 1, "One load must have one source room.");
        Require(c.Kind != InventoryCommandKind.InterCompanyDispatch || TransferCustodyGroups.IsValid(c.CustodyGroup), "Valid destination custody group is required.");
        Require(c.Kind != InventoryCommandKind.ReceiveTransfer || c.Lines.Select(x => x.Source.Location.CustodyRecordId).Distinct().Count() == 1
            && c.Lines.Select(x => x.Destination).Distinct().Count() == 1, "Receive requires one complete parent and destination, including every identity.");
    }

    private sealed record Allocation(TreatmentLineageSegment Segment, int Quantity, InventoryEffectiveTreatment? Treatment = null)
    {
        public string Signature => Treatment?.Signature ?? Segment.TreatmentSignature;
        public string State => Treatment?.State ?? Segment.TreatmentState;
        public IEnumerable<long> ApplicationIds => Treatment?.ApplicationIds ?? Segment.Applications.Select(x => x.RoomTreatmentApplicationId).ToImmutableArray();
    }
    private async Task<ImmutableArray<InventoryCommandEffect>> ApplyAsync(CropQcDbContext db, CanonicalProjectionFactory factory,
        InventoryCommand c, List<(InventoryCommandLine Line, InventoryPositionEvidence Evidence, InventoryAvailabilityResult Result)> inputs,
        DateTimeOffset now, int attempt, CancellationToken ct, ActualRun? existingRun = null, ActualRunRevision? existingRevision = null)
    {
        ActualRun? run = null;
        ActualRunRevision? revision = null;
        if (c.Kind == InventoryCommandKind.Dump)
        {
            run = existingRun ?? new()
            {
                Status = ActualRunStatuses.Active,
                RunAt = c.EffectiveAt,
                CreatedAt = now,
                CreatedByUserId = c.ActorId,
                CurrentRevisionNumber = 1,
                Notes = c.Reason
            };
            revision = existingRevision ?? new()
            {
                ActualRun = run,
                RevisionNumber = 1,
                OperationType = ActualRunRevisionTypes.Create,
                OperationKey = c.OperationKey,
                IsCurrent = true,
                CreatedAt = now,
                CreatedByUserId = c.ActorId,
                Reason = c.Reason
            };
            if (existingRevision == null) db.ActualRunRevisions.Add(revision);
            run.RunAt = c.EffectiveAt;
            if (c.Run is { } details)
            {
                Require(runExpectations != null, "Actual Run expectation writer is not configured.");
                var facility = await db.Warehouses.SingleOrDefaultAsync(x => x.Id == details.FacilityWarehouseId && x.IsActive, ct);
                Require(facility != null && facility.Code is "WP" or "EBS", "Select an active Actual Run facility.");
                SalesDesk? desk = details.SalesDeskId is int deskId ? await db.SalesDesks.SingleOrDefaultAsync(x => x.Id == deskId && x.IsActive, ct) : null;
                Require(facility!.Code == "WP" ? desk != null : details.SalesDeskId == null, "Select the Sales Desk appropriate to the run facility.");
                run.RunFacilityWarehouseId = facility.Id; run.RunFacilityCodeSnapshot = facility.Code;
                run.RunFacilityAssignmentSource = details.AssignmentSource;
                run.RunFacilityAssignedByUserId = c.ActorId; run.RunFacilityAssignedAt = now;
                run.SalesDeskId = desk?.Id; run.SalesDeskNameSnapshot = desk?.Name; run.Notes = details.Notes;
            }
        }
        InventoryCommand? original = null;
        if (c.OriginalOperationKey != null)
        {
            var record = await db.InventoryCommands.SingleOrDefaultAsync(x => x.OperationKey == c.OriginalOperationKey, ct);
            Require(record != null && !await db.InventoryCommands.AnyAsync(x => x.ReversesOperationKey == c.OriginalOperationKey, ct), "Original command missing or already reversed.", InventoryCommandStatus.Conflict);
            original = JsonSerializer.Deserialize<InventoryCommand>(record!.IntentJson, Json)!;
            Require(c.Kind is not (InventoryCommandKind.Return or InventoryCommandKind.ReopenTransfer) || c.Lines.Length == original.Lines.Length,
                "Reversal must include every original command allocation.");
        }
        await Stage("Parent", db, attempt, ct);
        await db.SaveChangesAsync(ct);
        var effects = ImmutableArray.CreateBuilder<InventoryCommandEffect>();
        RoomTransfer? originalRoomTransfer = null;
        List<TreatmentLineageMovement> originalRoomMoves = [];
        if (c.Kind == InventoryCommandKind.ReverseRoomMove)
        {
            originalRoomTransfer = await db.RoomTransfers.Include(x => x.InventoryAdjustments).SingleOrDefaultAsync(x => x.Id == c.PhysicalParentId, ct);
            Require(originalRoomTransfer != null && !originalRoomTransfer.IsReversed && originalRoomTransfer.ReversesRoomTransferId == null,
                "Transfer is missing, already reversed, or is itself a reversal.");
            var old = originalRoomTransfer!;
            var selected = c.Lines[0];
            Require(selected.Source.Location.WarehouseId == old.DestinationWarehouseId && selected.Source.Location.RoomId == old.DestinationRoomId
                && selected.Destination == new InventoryCommandDestination(old.SourceWarehouseId, old.SourceRoomId) && selected.Quantity == old.BinCount,
                "Reversal route or quantity differs from original transfer.");
            Require(old.InventoryAdjustments.Count == 2
                && old.InventoryAdjustments.SingleOrDefault(x => x.RoomId == old.SourceRoomId)?.ChangeAmount == -old.BinCount
                && old.InventoryAdjustments.SingleOrDefault(x => x.RoomId == old.DestinationRoomId)?.ChangeAmount == old.BinCount,
                "Original transfer ledger does not prove an exact reversible pair.");
            originalRoomMoves = await db.TreatmentLineageMovements.Where(x => x.RoomTransferId == old.Id).ToListAsync(ct);
            Require(originalRoomMoves.Count > 0 && originalRoomMoves.Sum(x => x.BinCount) == old.BinCount
                && originalRoomMoves.All(x => x.SourceRoomId == old.SourceRoomId && x.DestinationRoomId == old.DestinationRoomId
                    && x.SourceSegmentId != null && x.DestinationSegmentId != null && x.ReversesTreatmentLineageMovementId == null
                    && InventoryStatusIdentity.NormalizeLineageKey(x.IdentityKey) == selected.Source.Identity.Key
                    && x.TreatmentSignatureSnapshot == selected.TreatmentSignature), "Original transfer lineage cannot be reversed exactly.");
            var ids = originalRoomMoves.Select(x => x.Id).ToArray();
            Require(!await db.TreatmentLineageMovements.AnyAsync(x => ids.Contains(x.ReversesTreatmentLineageMovementId ?? 0), ct), "Transfer movement has already been reversed.");
        }
        var reversedApplications = new HashSet<long>();
        var completedParents = new HashSet<long>();
        var affectedRooms = new HashSet<(InventoryIdentity Identity, int Warehouse, int Room)>();
        for (var index = 0; index < inputs.Count; index++)
        {
            var (line, evidence, r) = inputs[index];
            var i = r.Identity; var loc = r.Location; var qty = line.Quantity;
            var partKey = $"{c.OperationKey}:{index}";
            var before = loc.Custody == InventoryCustody.Room ? await PhysicalAsync(db, i, loc.WarehouseId, loc.RoomId!.Value, ct)
                : r.AuthoritativeQuantity - inputs.Take(index).Where(x => x.Result.PositionKey == r.PositionKey).Sum(x => x.Line.Quantity);
            var increase = line.AdjustmentDirection == InventoryAdjustmentDirection.Increase;
            var delta = increase ? qty : -qty;
            var allocations = new List<Allocation>();
            if (loc.Custody == InventoryCustody.Room)
            {
                Require(await db.Rooms.AnyAsync(x => x.Id == loc.RoomId && x.WarehouseId == loc.WarehouseId && x.IsActive && x.Warehouse.IsActive && !x.IsSealed, ct),
                    "Source is unavailable, sealed or mismatched.");
                var rows = await db.TreatmentLineageSegments.Include(x => x.Applications).Where(x => x.RoomId == loc.RoomId
                    && x.Disposition == "Current" && x.CurrentBins > 0 && x.TreatmentSignature == line.TreatmentSignature).OrderBy(x => x.Id).ToListAsync(ct);
                var remaining = qty;
                foreach (var row in rows.Where(x => InventoryStatusIdentity.NormalizeLineageKey(x.IdentityKey) == i.Key
                    && (c.Kind is not (InventoryCommandKind.ReceiptCorrection or InventoryCommandKind.ReceiptTreatmentAssignment or InventoryCommandKind.ReceiptDepletion) || x.ReceiptId == line.ReceiptId)
                    && (originalRoomTransfer == null || originalRoomMoves.Any(m => m.DestinationSegmentId == x.Id))))
                {
                    Require(row.IdentityKey == i.Key, "Status alias requires an exact normalization plan before consumption.");
                    var amount = originalRoomTransfer != null ? originalRoomMoves.Where(m => m.DestinationSegmentId == row.Id).Sum(m => m.BinCount)
                        : increase ? remaining : Math.Min(row.CurrentBins, remaining);
                    Require(increase || row.CurrentBins >= amount, "Original destination allocation has been consumed or changed.");
                    if (amount > 0) allocations.Add(new(row, amount));
                    remaining -= amount;
                }
                Require(remaining == 0, "Proven current projections cannot cover this selected treatment/receipt quantity.");
            }
            else
            {
                // Custody slices originate from immutable dispatch movements, not current source stock.
                foreach (var allocation in evidence.Projections.Where(x => x.Signature == line.TreatmentSignature).GroupBy(x => x.Id))
                {
                    var amount = allocation.Sum(x => x.Quantity);
                    if (amount == 0) continue;
                    Require(amount > 0 && allocation.All(x => x.Signature == line.TreatmentSignature), "Custody allocation is not exact.");
                    var row = await db.TreatmentLineageSegments.Include(x => x.Applications).SingleAsync(x => x.Id == allocation.Key, ct);
                    var effective = InventoryEffectiveTreatment.Read(row.TreatmentSignature, row.TreatmentState,
                        row.Applications.Select(x => x.RoomTreatmentApplicationId).ToImmutableArray(), evidence.Applications);
                    Require(allocation.All(x => effective.Signature == x.Signature && effective.State == x.State && row.ReceiptId == x.ReceiptId), "Dispatch treatment/provenance changed.");
                    allocations.Add(new(row, amount, effective));
                }
                Require(allocations.Sum(x => x.Quantity) == qty && qty == r.AvailableQuantity, "Only exact complete custody treatment allocations may complete/return.");
            }
            var segmentBefore = allocations.Select(x => new
            {
                x.Segment.Id,
                x.Segment.CurrentBins,
                x.Segment.ConcurrencyVersion,
                x.Segment.Disposition,
                x.Segment.TreatmentSignature,
                x.Segment.ReceiptId
            }).ToArray();
            var entries = new List<RoomInventoryAdjustment>();
            var movements = new List<TreatmentLineageMovement>();
            long? parentId = null;
            BinsRunEntry? legacyEntry = null;
            RoomDepletion? receiptDepletion = null;
            var destination = line.Destination;
            int destinationBefore = 0;
            if (destination != null)
            {
                Require(await db.Rooms.AnyAsync(x => x.Id == destination.RoomId && x.WarehouseId == destination.WarehouseId
                    && x.IsActive && x.Warehouse.IsActive && !x.IsSealed, ct), "Destination is unavailable, sealed or mismatched.");
                destinationBefore = await PhysicalAsync(db, i, destination.WarehouseId, destination.RoomId, ct);
                Require(destinationBefore >= 0, "Destination has negative legacy authority.");
            }
            RoomInventoryAdjustment? debit = null;
            if (loc.Custody == InventoryCustody.Room && !InventoryCommandPolicy.IsTreatment(c.Kind))
            {
                debit = Ledger(c, i, loc.WarehouseId, loc.RoomId!.Value, delta, before, partKey + ":out", now);
                entries.Add(debit);
            }
            if (InventoryCommandPolicy.IsRoomMove(c.Kind))
            {
                var sourceCode = await db.Warehouses.Where(x => x.Id == loc.WarehouseId).Select(x => x.Code).SingleAsync(ct);
                var destinationCode = await db.Warehouses.Where(x => x.Id == destination!.WarehouseId).Select(x => x.Code).SingleAsync(ct);
                Require(!TruckReceiptRoutes.RequiresReceipt(sourceCode, destinationCode),
                    "This route requires an inter-company dispatch and matched Truck Receipt; a direct move cannot bypass reconciliation.");
                var transfer = new RoomTransfer
                {
                    OperationKey = partKey,
                    SourceWarehouseId = loc.WarehouseId,
                    SourceRoomId = loc.RoomId!.Value,
                    DestinationWarehouseId = destination!.WarehouseId,
                    DestinationRoomId = destination.RoomId,
                    CropYear = i.CropYear,
                    GrowerLotId = i.GrowerLotId,
                    FruitProfileId = i.FruitProfileId,
                    GrowerName = allocations[0].Segment.GrowerNameSnapshot,
                    LotNumber = i.Lot,
                    VarietyCode = i.Variety,
                    InventoryStatus = i.Status,
                    BinCount = qty,
                    Reason = c.Reason,
                    Notes = c.Dispatch?.Notes,
                    TransferredAt = c.EffectiveAt,
                    CreatedByUserId = c.ActorId,
                    CreatedAt = now
                };
                db.RoomTransfers.Add(transfer);
                if (originalRoomTransfer != null)
                {
                    transfer.ReversesRoomTransfer = originalRoomTransfer;
                    originalRoomTransfer.IsReversed = true; originalRoomTransfer.ReversedAt = now;
                    originalRoomTransfer.ReversedByUserId = c.ActorId; originalRoomTransfer.ReverseReason = c.Reason;
                }
                debit!.RoomTransfer = transfer; debit.AdjustmentType = "TransferOut";
                var credit = Ledger(c, i, destination.WarehouseId, destination.RoomId, qty, destinationBefore, partKey + ":in", now);
                credit.RoomTransfer = transfer; credit.AdjustmentType = "TransferIn"; entries.Add(credit);
                foreach (var a in allocations)
                {
                    var target = await factory.CurrentAsync(i, destination.WarehouseId, destination.RoomId,
                        a.Segment.TreatmentSignature, a.Segment.TreatmentState, a.Segment.ReceiptId,
                        a.Segment.Applications.Select(x => x.RoomTreatmentApplicationId), now, ct);
                    Credit(target, a.Quantity, now);
                    if (originalRoomTransfer == null)
                    {
                        var movement = Move(c, i, a, target, loc.RoomId, destination.RoomId, partKey, now, "Transfer");
                        movement.RoomTransfer = transfer; movements.Add(movement);
                    }
                    else foreach (var oldMove in originalRoomMoves.Where(x => x.DestinationSegmentId == a.Segment.Id))
                    {
                        var movement = Move(c, i, new(a.Segment, oldMove.BinCount), target, loc.RoomId, destination.RoomId,
                            partKey + ":r" + oldMove.Id, now, "TransferReversal");
                        movement.RoomTransfer = transfer; movement.ReversesTreatmentLineageMovementId = oldMove.Id; movements.Add(movement);
                    }
                }
                await db.SaveChangesAsync(ct); parentId = transfer.Id;
            }
            else if (c.Kind == InventoryCommandKind.TransferEdit)
            {
                Require(original == null || original.Kind == InventoryCommandKind.InterCompanyDispatch, "Original command is not a dispatch.");
                var transfer = original == null ? await db.InterCrewTransfers.SingleAsync(x => x.Id == c.PhysicalParentId, ct)
                    : await db.InterCrewTransfers.SingleAsync(x => x.OperationKey == original.OperationKey + ":load", ct);
                Require(transfer.Status == InterCrewTransferStatuses.InTransit && transfer.BinsReceived == null
                    && transfer.ConcurrencyVersion == c.ExpectedTransferVersion, "Transfer is received or stale.", InventoryCommandStatus.Stale);
                Require(transfer.SourceRoomId == loc.RoomId && transfer.SourceWarehouseId == loc.WarehouseId && transfer.CropYear == i.CropYear,
                    "Added allocation must belong to the load's source room and crop.");
                var transitBefore = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db)).ResolveAsync(
                    new(transfer.SourceWarehouseId, [], InventoryCustody.InTransit, transfer.Id), new(AllowedCustody: InventoryCustody.InTransit), now, ct);
                Require(transitBefore.Positions.Length > 0 && transitBefore.Positions.All(x => x.IsOperable)
                    && transitBefore.Positions.Sum(x => x.AuthoritativeQuantity) == transfer.BinsLoaded, "Existing transit custody cannot be proven.");
                var oldParent = new { transfer.BinsLoaded, transfer.ConcurrencyVersion };
                transfer.BinsLoaded = checked(transfer.BinsLoaded + qty); transfer.ConcurrencyVersion++;
                debit!.InterCrewTransfer = transfer; debit.AdjustmentType = InterCrewTransferAdjustmentTypes.Dispatch;
                foreach (var a in allocations) { var m = Move(c, i, a, null, loc.RoomId, null, partKey, now, "InterCrewDispatch"); m.InterCrewTransfer = transfer; movements.Add(m); }
                AddAudit(db, c, "CanonicalTransferAllocationEdit", transfer.Id.ToString(), oldParent, new { transfer.BinsLoaded, transfer.ConcurrencyVersion }, now);
                parentId = transfer.Id;
            }
            else if (c.Kind is InventoryCommandKind.Dump or InventoryCommandKind.LegacyDump or InventoryCommandKind.ReceiptDepletion)
            {
                debit!.AdjustmentType = "BinsRun"; debit.ActualRun = run; debit.ActualRunRevision = revision;
                RoomDepletion? depletion = null;
                if (c.Kind == InventoryCommandKind.ReceiptDepletion)
                {
                    var receipt = await db.Receipts.SingleAsync(x => x.Id == line.ReceiptId, ct);
                    Require(!receipt.IsDeleted && !receipt.IsTransferReceipt && receipt.RoomId == loc.RoomId,
                        "Receipt depletion requires exact inventory in the original receiving room.");
                    depletion = new()
                    {
                        Receipt = receipt,
                        WarehouseId = loc.WarehouseId,
                        RoomId = loc.RoomId!.Value,
                        FruitProfileId = i.FruitProfileId!.Value,
                        GrowerName = receipt.GrowerName,
                        LotCode = receipt.LotCode,
                        BinCountDepleted = qty,
                        Destination = c.Dispatch?.Reference,
                        Notes = c.Dispatch?.Notes,
                        DepletedAt = c.EffectiveAt,
                        CreatedByUserId = c.ActorId,
                        CreatedAt = now
                    };
                    db.RoomDepletions.Add(depletion); debit.RoomDepletion = depletion; debit.ReceiptId = receipt.Id; debit.AdjustmentType = "Depletion";
                }
                var entry = new BinsRunEntry
                {
                    ReceiptId = line.ReceiptId,
                    InventoryAdjustment = debit,
                    WarehouseId = loc.WarehouseId,
                    RoomId = loc.RoomId!.Value,
                    CropYear = i.CropYear,
                    GrowerLotId = i.GrowerLotId,
                    FruitProfileId = i.FruitProfileId,
                    GrowerName = allocations[0].Segment.GrowerNameSnapshot,
                    GrowerNumberSnapshot = i.GrowerNumber,
                    LotNumber = i.Lot,
                    VarietyCode = i.Variety,
                    InventoryStatus = i.Status,
                    PreviousAvailableBins = before,
                    BinsRun = qty,
                    NewAvailableBins = before - qty,
                    RunAt = c.EffectiveAt,
                    CreatedAt = now,
                    CreatedByUserId = c.ActorId,
                    ActualRun = run,
                    ActualRunRevision = revision,
                    TransactionType = run != null ? ActualRunTransactionTypes.Depletion : ActualRunTransactionTypes.Legacy,
                    TreatmentStateSnapshot = allocations[0].Segment.TreatmentState,
                    TreatmentSignatureSnapshot = line.TreatmentSignature,
                    ProductionTypeSnapshot = i.ProductionType,
                    IsOrganicSnapshot = i.IsOrganic,
                    Notes = c.Dispatch?.Notes ?? c.Reason,
                    ReportingFacilityWarehouseId = c.LegacyRun?.FacilityWarehouseId,
                    ReportingFacilityCodeSnapshot = c.LegacyRun?.FacilityCode,
                    ReportingFacilityAssignmentSource = c.LegacyRun?.AssignmentSource,
                    ReportingFacilityAssignedByUserId = c.LegacyRun?.FacilityWarehouseId != null ? c.ActorId : null,
                    ReportingFacilityAssignedAt = c.LegacyRun?.FacilityWarehouseId != null ? now : null,
                    ReportingCropYearSnapshot = i.CropYear,
                    ReportingFruitProfileIdSnapshot = i.FruitProfileId,
                    ReportingVarietyCodeSnapshot = i.Variety
                };
                db.BinsRunEntries.Add(entry);
                foreach (var a in allocations) { var m = Move(c, i, a, null, loc.RoomId, null, partKey, now, "BinsRun"); m.BinsRunEntry = entry; movements.Add(m); }
                legacyEntry = entry; receiptDepletion = depletion;
                parentId = run?.Id;
            }
            else if (c.Kind is InventoryCommandKind.OutsideWarehouseTransfer or InventoryCommandKind.InterCompanyDispatch or InventoryCommandKind.ProcessorSale)
                parentId = await DispatchAsync(db, c, i, loc, allocations, debit!, movements, partKey, now, ct);
            else if (c.Kind is InventoryCommandKind.ReceiveTransfer or InventoryCommandKind.Return or InventoryCommandKind.ReopenTransfer)
            {
                var receipt = await CompleteCustodyAsync(db, factory, c, line, r, allocations, original, debit, entries, movements, partKey, now, completedParents, ct);
                parentId = receipt.ParentId; destination = receipt.Destination; destinationBefore = receipt.DestinationBefore;
            }
            else if (InventoryCommandPolicy.IsTreatment(c.Kind))
                parentId = await TreatAsync(db, factory, c, i, loc, allocations, before, qty, partKey, now, reversedApplications, ct);
            else if (c.Kind == InventoryCommandKind.Loss)
            {
                var loss = new RoomInventoryLoss
                {
                    OperationKey = partKey,
                    WarehouseId = loc.WarehouseId,
                    RoomId = loc.RoomId!.Value,
                    CropYear = i.CropYear,
                    GrowerLotId = i.GrowerLotId,
                    FruitProfileId = i.FruitProfileId,
                    GrowerName = allocations[0].Segment.GrowerNameSnapshot,
                    GrowerNumber = i.GrowerNumber,
                    LotNumber = i.Lot,
                    VarietyCode = i.Variety,
                    InventoryStatus = i.Status,
                    LossType = RoomInventoryLossTypes.Dropped,
                    BinCount = qty,
                    Reason = c.Reason,
                    Notes = c.Dispatch?.Notes,
                    CreatedAt = now,
                    OccurredAt = c.EffectiveAt,
                    CreatedByUserId = c.ActorId
                };
                db.RoomInventoryLosses.Add(loss); debit!.RoomInventoryLoss = loss; debit.AdjustmentType = InventoryLedgerKinds.DroppedBins;
                foreach (var a in allocations) { var m = Move(c, i, a, null, loc.RoomId, null, partKey, now, "InventoryLoss"); m.RoomInventoryLoss = loss; movements.Add(m); }
                await db.SaveChangesAsync(ct); parentId = loss.Id;
            }
            else if (c.Kind == InventoryCommandKind.ReceiptCorrection)
            {
                var receipt = await db.Receipts.SingleAsync(x => x.Id == line.ReceiptId, ct);
                Require(!receipt.IsDeleted && !receipt.IsTransferReceipt && (increase || receipt.BinCount >= qty), "Receipt correction would make its quantity negative.");
                var old = JsonSerializer.Serialize(receipt.BinCount); var oldBins = receipt.BinCount;
                receipt.BinCount = checked(receipt.BinCount + delta); receipt.ConcurrencyVersion++; receipt.UpdatedAt = now;
                var correction = new ReceiptInventoryOverride
                {
                    Id = Guid.NewGuid(),
                    Receipt = receipt,
                    ActionType = ReceiptInventoryOverrideActionTypes.QuantityCorrection,
                    OldReceiptBinCount = oldBins,
                    NewReceiptBinCount = receipt.BinCount,
                    InventoryDelta = delta,
                    CurrentInventoryBefore = before,
                    CurrentInventoryAfter = checked(before + delta),
                    AdministratorUserId = c.ActorId,
                    Reason = c.Reason,
                    OperationKey = partKey,
                    CreatedAt = now,
                    BeforeReceiptSnapshotJson = old,
                    AfterReceiptSnapshotJson = JsonSerializer.Serialize(receipt.BinCount),
                    AffectedInventorySnapshotJson = JsonSerializer.Serialize(r, Json),
                    ExpectedAdjustmentCount = 1,
                    IsComplete = true
                };
                db.ReceiptInventoryOverrides.Add(correction); debit!.ReceiptInventoryOverride = correction;
                debit.ReceiptId = receipt.Id; debit.AdjustmentType = "ReceiptQuantityCorrection";
                foreach (var a in allocations) movements.Add(Move(c, i, a, null, loc.RoomId, null, partKey, now, "ManualTrueUp"));
                parentId = receipt.Id;
            }
            else if (c.Kind == InventoryCommandKind.BaselineAdjustment)
            {
                // Explicit signed adjustment, never a baseline replacement or inferred projection gap.
                debit!.AdjustmentType = "ManualTrueUp";
                foreach (var a in allocations) movements.Add(Move(c, i, a, null, loc.RoomId, null, partKey, now, "ManualTrueUp"));
            }
            else throw new Rejection(InventoryCommandStatus.InvalidIntent, "Unsupported operation shape.");

            if (loc.Custody == InventoryCustody.Room && !InventoryCommandPolicy.IsTreatment(c.Kind))
                foreach (var a in allocations)
                {
                    a.Segment.CurrentBins = checked(a.Segment.CurrentBins + (increase ? a.Quantity : -a.Quantity)); a.Segment.ConcurrencyVersion++; a.Segment.UpdatedAt = now;
                    Require(a.Segment.CurrentBins >= 0, "Projection consumption exceeded its proven current quantity.");
                    if (a.Segment.CurrentBins == 0) CanonicalProjectionFactory.Retire(a.Segment, c.OperationKey, now);
                }
            if (increase)
                foreach (var movement in movements)
                {
                    movement.DestinationSegment = movement.SourceSegment; movement.SourceSegment = null;
                    movement.DestinationRoomId = loc.RoomId; movement.SourceRoomId = null;
                }
            db.RoomInventoryAdjustments.AddRange(entries);
            await Stage($"Source{index + 1}", db, attempt, ct);
            await Stage("Movement", db, attempt, ct);
            db.TreatmentLineageMovements.AddRange(movements);
            AddAudit(db, c, "CanonicalProjectionOperation", r.PositionKey, segmentBefore,
                new { c.Kind, Quantity = qty, Segments = allocations.Select(x => new { x.Segment.Id, x.Segment.CurrentBins, x.Segment.ConcurrencyVersion, x.Segment.Disposition }) }, now);
            await db.SaveChangesAsync(ct);
            if (legacyEntry != null && run == null) parentId = receiptDepletion?.Id ?? legacyEntry.Id;
            await Stage($"PersistedSource{index + 1}", db, attempt, ct);
            var after = loc.Custody == InventoryCustody.Room ? await PhysicalAsync(db, i, loc.WarehouseId, loc.RoomId!.Value, ct) : checked(before - qty);
            var expected = InventoryCommandPolicy.IsTreatment(c.Kind) ? before : checked(before + delta);
            Require(after == expected && after >= 0, "Authoritative source conservation failed.");
            if (destination != null)
                Require(await PhysicalAsync(db, i, destination.WarehouseId, destination.RoomId, ct) == destinationBefore + qty,
                    "Destination conservation failed.");
            Require(entries.Sum(x => x.ChangeAmount) == (InventoryCommandPolicy.IsTreatment(c.Kind) ? 0
                : destination != null ? loc.Custody == InventoryCustody.Room ? 0 : qty : delta), "Ledger quantity conservation failed.");
            if (c.Kind == InventoryCommandKind.Dump)
                Require(await db.BinsRunEntries.Where(x => x.ActualRunRevisionId == revision!.Id && x.TransactionType == ActualRunTransactionTypes.Depletion).SumAsync(x => x.BinsRun, ct) == inputs.Take(index + 1).Sum(x => x.Line.Quantity), "Run consumption conservation failed.");
            if (c.Kind is InventoryCommandKind.OutsideWarehouseTransfer or InventoryCommandKind.ProcessorSale or InventoryCommandKind.TransferEdit
                || c.Kind == InventoryCommandKind.InterCompanyDispatch && index == inputs.Count - 1)
            {
                var custody = c.Kind == InventoryCommandKind.OutsideWarehouseTransfer ? InventoryCustody.OutsideWarehouse
                    : c.Kind == InventoryCommandKind.ProcessorSale ? InventoryCustody.Processor : InventoryCustody.InTransit;
                var check = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db))
                    .ResolveAsync(new(loc.WarehouseId, [], custody, parentId), new(AllowedCustody: custody), DateTimeOffset.UtcNow, ct);
                Require(check.Positions.Length > 0 && check.Positions.All(x => x.IsOperable)
                    && (c.Kind == InventoryCommandKind.TransferEdit ? check.Positions.Sum(x => x.AuthoritativeQuantity) >= qty
                        : check.Positions.Sum(x => x.AuthoritativeQuantity) == (c.Kind == InventoryCommandKind.InterCompanyDispatch ? c.Lines.Sum(x => x.Quantity) : qty)),
                    "Dispatch ledger, immutable allocations and custody parent do not conserve quantity.");
            }
            if (loc.Custody == InventoryCustody.Room) affectedRooms.Add((i, loc.WarehouseId, loc.RoomId!.Value));
            if (destination != null) affectedRooms.Add((i, destination.WarehouseId, destination.RoomId));
            effects.Add(new(r.PositionKey, before, after, qty, parentId, entries.Select(x => x.Id).ToImmutableArray(), movements.Select(x => x.Id).ToImmutableArray()));
        }
        if (c.Kind == InventoryCommandKind.ReceiveTransfer)
        {
            var parent = c.Lines[0].Source.Location.CustodyRecordId;
            Require(await db.RoomInventoryAdjustments.Where(x => x.InterCrewTransferId == parent).SumAsync(x => x.ChangeAmount, ct) == 0,
                "Complete transfer did not conserve all parent allocations.");
        }
        foreach (var affected in affectedRooms)
        {
            var check = await new InventoryAvailabilityResolver(new InventoryEvidenceLoader(db))
                .ResolveAsync(new(affected.Warehouse, [affected.Room]), new(), DateTimeOffset.UtcNow, ct);
            var position = check.Positions.SingleOrDefault(x => x.Identity.Key == affected.Identity.Key);
            Require(position != null && position.IsOperable && position.RawProjectionQuantity == position.AuthoritativeQuantity,
                "Final current projection, treatment and authoritative inventory do not reconcile.");
        }
        if (run != null && c.Run != null)
        {
            // Reporting participant cannot use the physical write capability.
            db.CanonicalCommandTransaction = false;
            try
            {
                await runExpectations!.WriteAsync(db, run, revision!, await db.BinsRunEntries.Where(x => x.ActualRunRevisionId == revision!.Id && x.TransactionType == ActualRunTransactionTypes.Depletion).ToListAsync(ct), c.ActorId, now, ct);
                await db.SaveChangesAsync(ct);
            }
            finally { db.CanonicalCommandTransaction = true; }
        }
        return effects.ToImmutable();
    }
    private static void Credit(TreatmentLineageSegment row, int quantity, DateTimeOffset now)
    { Require(row.Disposition == "Current", "Cannot credit historical projection."); row.CurrentBins = checked(row.CurrentBins + quantity); row.UpdatedAt = now; row.ConcurrencyVersion++; }
    private static RoomInventoryAdjustment Ledger(InventoryCommand c, InventoryIdentity i, int warehouse, int room, int delta, int before, string key, DateTimeOffset now) => new()
    {
        WarehouseId = warehouse,
        RoomId = room,
        CropYear = i.CropYear,
        GrowerLotId = i.GrowerLotId,
        FruitProfileId = i.FruitProfileId,
        GrowerName = i.GrowerNumber!,
        LotNumber = i.Lot,
        VarietyCode = i.Variety,
        InventoryStatus = i.Status,
        ChangeAmount = delta,
        OldBinCount = before,
        NewBinCount = checked(before + delta),
        AdjustmentType = "CanonicalInventory",
        Source = "CanonicalInventory/v1",
        Reason = c.Reason,
        AdjustmentAt = c.EffectiveAt,
        CreatedAt = now,
        CreatedByUserId = c.ActorId,
        InventoryOperationKey = key,
        InventoryInvariantVersion = InventoryLedgerKinds.CanonicalCommandInvariantVersion
    };
    private static TreatmentLineageMovement Move(InventoryCommand c, InventoryIdentity i, Allocation a, TreatmentLineageSegment? target,
        int? sourceRoom, int? targetRoom, string key, DateTimeOffset now, string kind) => new()
        {
            OperationKey = $"{key}:s{a.Segment.Id}",
            MovementType = kind,
            SourceSegment = a.Segment,
            DestinationSegment = target,
            SourceRoomId = sourceRoom,
            DestinationRoomId = targetRoom,
            IdentityKey = i.Key,
            TreatmentStateSnapshot = a.State,
            TreatmentSignatureSnapshot = a.Signature,
            ReceiptId = a.Segment.ReceiptId,
            BinCount = a.Quantity,
            OccurredAt = c.EffectiveAt,
            CreatedAt = now,
            CreatedByUserId = c.ActorId
        };
}
