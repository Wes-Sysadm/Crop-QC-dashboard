using System.Text.Json;
using CropQc.Data.Entities;
using CropQc.Shared.Inventory;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Data.Inventory;

public sealed partial class InventoryCommandExecutor
{
    private static async Task<long> DispatchAsync(CropQcDbContext db, InventoryCommand c, InventoryIdentity i, InventoryLocation loc,
        List<Allocation> allocations, RoomInventoryAdjustment debit, List<TreatmentLineageMovement> movements,
        string key, DateTimeOffset now, CancellationToken ct)
    {
        var quantity = allocations.Sum(x => x.Quantity); var row = allocations[0].Segment;
        Require(allocations.Select(x => x.Segment.TreatmentSignature).Distinct().Count() == 1, "Dispatch requires a selected treatment.");
        if (c.Kind == InventoryCommandKind.InterCompanyDispatch)
        {
            var sourceCode = await db.Warehouses.Where(x => x.Id == loc.WarehouseId).Select(x => x.Code).SingleAsync(ct);
            Require(TruckReceiptRoutes.RequiresReceiptForGroup(sourceCode, c.CustodyGroup!), "Inter-company dispatch requires a configured cross-company route.");
            var transfer = db.InterCrewTransfers.Local.SingleOrDefault(x => x.OperationKey == c.OperationKey + ":load");
            if (transfer == null)
            {
                transfer = new InterCrewTransfer
                {
                    OperationKey = c.OperationKey + ":load",
                    SourceWarehouseId = loc.WarehouseId,
                    SourceRoomId = loc.RoomId!.Value,
                    DestinationCustodyGroup = c.CustodyGroup!,
                    CropYear = i.CropYear,
                    GrowerLotId = i.GrowerLotId,
                    FruitProfileId = i.FruitProfileId,
                    GrowerNumberSnapshot = i.GrowerNumber,
                    GrowerNameSnapshot = row.GrowerNameSnapshot,
                    LotNumberSnapshot = i.Lot,
                    VarietyCodeSnapshot = i.Variety,
                    ProductionTypeSnapshot = i.ProductionType,
                    IsOrganicSnapshot = i.IsOrganic,
                    InventoryStatusSnapshot = i.Status,
                    TreatmentStateSnapshot = row.TreatmentState,
                    TreatmentSignatureSnapshot = row.TreatmentSignature,
                    TreatmentSummarySnapshot = row.TreatmentState,
                    BinsLoaded = c.Lines.Sum(x => x.Quantity),
                    LoadedAt = c.EffectiveAt,
                    LoadedByUserId = c.ActorId,
                    CreatedAt = now,
                    Status = InterCrewTransferStatuses.InTransit,
                    RequiresTruckReceipt = true,
                    Notes = c.Reason
                };
                db.InterCrewTransfers.Add(transfer);
            }
            debit.InterCrewTransfer = transfer; debit.AdjustmentType = InterCrewTransferAdjustmentTypes.Dispatch;
            foreach (var a in allocations) { var m = Move(c, i, a, null, loc.RoomId, null, key, now, "InterCrewDispatch"); m.InterCrewTransfer = transfer; movements.Add(m); }
            await db.SaveChangesAsync(ct); Require(transfer.BinsLoaded == c.Lines.Sum(x => x.Quantity), "Custody conservation failed."); return transfer.Id;
        }
        if (c.Kind == InventoryCommandKind.OutsideWarehouseTransfer)
        {
            var outside = await db.OutsideWarehouses.SingleOrDefaultAsync(x => x.Id == c.CounterpartyId && x.IsActive, ct);
            Require(outside != null, "Outside warehouse is unavailable.");
            var transfer = new OutsideWarehouseTransfer
            {
                OperationKey = key,
                OutsideWarehouse = outside!,
                OutsideWarehouseCodeSnapshot = outside!.Code,
                OutsideWarehouseNameSnapshot = outside.Name,
                SourceWarehouseId = loc.WarehouseId,
                SourceRoomId = loc.RoomId!.Value,
                CropYear = i.CropYear,
                GrowerLotId = i.GrowerLotId,
                FruitProfileId = i.FruitProfileId,
                GrowerNumberSnapshot = i.GrowerNumber,
                GrowerNameSnapshot = row.GrowerNameSnapshot,
                LotNumberSnapshot = i.Lot,
                VarietyCodeSnapshot = i.Variety,
                ProductionTypeSnapshot = i.ProductionType,
                IsOrganicSnapshot = i.IsOrganic,
                InventoryStatusSnapshot = i.Status,
                TreatmentStateSnapshot = row.TreatmentState,
                TreatmentSignatureSnapshot = row.TreatmentSignature,
                TreatmentSummarySnapshot = row.TreatmentState,
                BinCount = quantity,
                TransferredAt = c.EffectiveAt,
                CreatedByUserId = c.ActorId,
                CreatedAt = now,
                Notes = c.Reason
            };
            db.OutsideWarehouseTransfers.Add(transfer); debit.OutsideWarehouseTransfer = transfer; debit.AdjustmentType = OutsideWarehouseTransferAdjustmentTypes.Transfer;
            foreach (var a in allocations) { var m = Move(c, i, a, null, loc.RoomId, null, key, now, "OutsideWarehouseTransfer"); m.OutsideWarehouseTransfer = transfer; movements.Add(m); }
            await db.SaveChangesAsync(ct); Require(transfer.BinCount == quantity, "Custody conservation failed."); return transfer.Id;
        }
        var processor = await db.Processors.SingleOrDefaultAsync(x => x.Id == c.CounterpartyId && x.IsActive, ct);
        Require(processor != null, "Processor is unavailable.");
        var shipment = new ProcessorShipment
        {
            OperationKey = key,
            Processor = processor!,
            ProcessorNameSnapshot = processor!.Name,
            ShippedAt = c.EffectiveAt,
            OriginalPricingBasis = c.ProcessorTerms!.Basis,
            PricingBasis = c.ProcessorTerms.Basis,
            OriginalSaleRate = c.ProcessorTerms.Rate,
            SaleRate = c.ProcessorTerms.Rate,
            Currency = c.ProcessorTerms.Currency,
            CreatedByUserId = c.ActorId,
            CreatedAt = now,
            Notes = c.Reason
        };
        var line = new ProcessorShipmentLine
        {
            ProcessorShipment = shipment,
            WarehouseId = loc.WarehouseId,
            RoomId = loc.RoomId!.Value,
            CropYear = i.CropYear,
            GrowerLotId = i.GrowerLotId,
            FruitProfileId = i.FruitProfileId,
            GrowerNumberSnapshot = i.GrowerNumber,
            GrowerNameSnapshot = row.GrowerNameSnapshot,
            LotNumberSnapshot = i.Lot,
            VarietyCodeSnapshot = i.Variety,
            ProductionTypeSnapshot = i.ProductionType,
            IsOrganicSnapshot = i.IsOrganic,
            InventoryStatusSnapshot = i.Status,
            TreatmentStateSnapshot = row.TreatmentState,
            TreatmentSignatureSnapshot = row.TreatmentSignature,
            TreatmentSummarySnapshot = row.TreatmentState,
            BinsSent = quantity,
            PoundsPerBinSnapshot = c.ProcessorTerms!.PoundsPerBin
        };
        db.ProcessorShipmentLines.Add(line); debit.ProcessorShipmentLine = line; debit.AdjustmentType = ProcessorShipmentAdjustmentTypes.Shipment;
        foreach (var a in allocations) { var m = Move(c, i, a, null, loc.RoomId, null, key, now, "ProcessorShipment"); m.ProcessorShipmentLine = line; movements.Add(m); }
        await db.SaveChangesAsync(ct); Require(line.BinsSent == quantity, "Processor custody conservation failed."); return line.Id;
    }

    private async Task<(long ParentId, InventoryCommandDestination? Destination, int DestinationBefore)> CompleteCustodyAsync(
        CropQcDbContext db, CanonicalProjectionFactory factory, InventoryCommand c, InventoryCommandLine line, InventoryAvailabilityResult r,
        List<Allocation> allocations, InventoryCommand? original, RoomInventoryAdjustment? debit, List<RoomInventoryAdjustment> ledger,
        List<TreatmentLineageMovement> movements, string key, DateTimeOffset now, HashSet<long> completedParents, CancellationToken ct)
    {
        var loc = r.Location; var i = r.Identity; var quantity = line.Quantity;
        if (loc.Custody != InventoryCustody.Room)
        {
            var id = loc.CustodyRecordId!.Value;
            var destination = line.Destination;
            InterCrewTransfer? crew = null; OutsideWarehouseTransfer? outside = null; ProcessorShipmentLine? processor = null;
            if (loc.Custody == InventoryCustody.InTransit)
            {
                crew = await db.InterCrewTransfers.Include(x => x.SourceWarehouse).SingleAsync(x => x.Id == id, ct);
                var first = completedParents.Add(crew.Id);
                var parentQuantity = c.Lines.Where(x => x.Source.Location.CustodyRecordId == crew.Id).Sum(x => x.Quantity);
                Require((!first || crew.Status == InterCrewTransferStatuses.InTransit) && crew.BinsLoaded == parentQuantity, "Transfer custody changed.", InventoryCommandStatus.Stale);
                if (c.Kind == InventoryCommandKind.ReceiveTransfer)
                {
                    if (first && crew.RequiresTruckReceipt)
                    {
                        Require(c.ReceivingEvidence != null && crew.ReceivingReceiptId == c.ReceivingEvidence.ReceiptId, "A matched Truck Receipt is required.");
                        var receipt = await db.Receipts.Include(x => x.VarietyLines).Include(x => x.Warehouse)
                            .SingleAsync(x => x.Id == c.ReceivingEvidence!.ReceiptId, ct);
                        Require(receipt.ConcurrencyVersion == c.ReceivingEvidence!.ExpectedVersion, "Truck Receipt version changed.", InventoryCommandStatus.Stale);
                        Require(receipt.IsTransferReceipt && !receipt.IsDeleted && receipt.TransferCompletedAt == null
                            && receipt.ReceiptType == "Truck receipt" && !string.IsNullOrWhiteSpace(receipt.CompuTechReceiptId)
                            && c.Lines.All(x => x.Source.Identity.CropYear == receipt.CropYear) && receipt.RoomId == destination!.RoomId && receipt.WarehouseId == destination.WarehouseId
                            && TruckReceiptRoutes.RequiresReceiptForGroup(crew.SourceWarehouse.Code, crew.DestinationCustodyGroup)
                            && TruckReceiptRoutes.Group(receipt.Warehouse.Code) == crew.DestinationCustodyGroup
                            && receipt.VarietyLines.Count > 0 && receipt.VarietyLines.All(x => x.BinCount > 0)
                            && receipt.VarietyLines.GroupBy(x => x.FruitProfileId).All(g => g.Sum(x => x.BinCount) == c.Lines.Where(x => x.Source.Identity.FruitProfileId == g.Key).Sum(x => x.Quantity))
                            && receipt.VarietyLines.Sum(x => x.BinCount) == parentQuantity && receipt.BinCount == parentQuantity
                            && !await db.RoomInventoryAdjustments.AnyAsync(x => x.ReceiptId == receipt.Id, ct), "Truck Receipt route, identity, quantity or existing inventory does not reconcile.");
                        AddAudit(db, c, "CanonicalTruckReceiptCompletion", receipt.Id.ToString(),
                            new { receipt.TransferCompletedAt, receipt.ConcurrencyVersion }, new { CompletedAt = now, Version = receipt.ConcurrencyVersion + 1 }, now);
                        receipt.TransferCompletedAt = now; receipt.UpdatedAt = now; receipt.ConcurrencyVersion++;
                    }
                    crew.Status = InterCrewTransferStatuses.Received; crew.BinsReceived = parentQuantity; crew.VarianceBins = 0;
                    crew.DestinationWarehouseId = destination!.WarehouseId; crew.DestinationRoomId = destination.RoomId;
                    crew.ReceivedAt = c.EffectiveAt; crew.ReceivedByUserId = c.ActorId; crew.ReceiveOperationKey = c.OperationKey;
                }
                else
                {
                    Require(original?.Kind == InventoryCommandKind.InterCompanyDispatch && crew.OperationKey.StartsWith(original.OperationKey + ":", StringComparison.Ordinal), "Return must refer to this original dispatch.");
                    if (first && crew.ReceivingReceiptId is long matchedId)
                    {
                        Require(c.ReceivingEvidence?.ReceiptId == matchedId, "A matched receipt must participate in dispatch return.");
                        var matched = await db.Receipts.SingleAsync(x => x.Id == matchedId, ct);
                        Require(matched.IsTransferReceipt && !matched.IsDeleted && matched.TransferCompletedAt == null
                            && matched.ConcurrencyVersion == c.ReceivingEvidence!.ExpectedVersion, "Matched receiving evidence changed.", InventoryCommandStatus.Stale);
                        AddAudit(db, c, "CanonicalTruckReceiptUnlink", matchedId.ToString(),
                            new { crew.ReceivingReceiptId, matched.ConcurrencyVersion }, new { ReceiptId = (long?)null, Version = matched.ConcurrencyVersion + 1 }, now);
                        crew.ReceivingReceiptId = null; matched.ConcurrencyVersion++; matched.UpdatedAt = now;
                    }
                    destination = new(crew.SourceWarehouseId, crew.SourceRoomId); crew.Status = InterCrewTransferStatuses.Reversed;
                    crew.ReversedAt = now; crew.ReversedByUserId = c.ActorId; crew.ReversalOperationKey = c.OperationKey; crew.ReversalReason = c.Reason;
                }
                if (first) crew.ConcurrencyVersion++;
            }
            else if (loc.Custody == InventoryCustody.OutsideWarehouse)
            {
                outside = await db.OutsideWarehouseTransfers.SingleAsync(x => x.Id == id, ct);
                Require(c.Kind == InventoryCommandKind.Return && original?.Kind == InventoryCommandKind.OutsideWarehouseTransfer
                    && outside.OperationKey.StartsWith(original.OperationKey + ":", StringComparison.Ordinal) && !outside.IsReversed && outside.BinCount == quantity, "Outside return does not match original active custody.");
                destination = new(outside.SourceWarehouseId, outside.SourceRoomId); outside.IsReversed = true; outside.ReversedAt = now;
                outside.ReversedByUserId = c.ActorId; outside.ReversalOperationKey = c.OperationKey; outside.ReverseReason = c.Reason; outside.ConcurrencyVersion++;
            }
            else
            {
                processor = await db.ProcessorShipmentLines.Include(x => x.ProcessorShipment).SingleAsync(x => x.Id == id, ct);
                var shipment = processor.ProcessorShipment;
                Require(c.Kind == InventoryCommandKind.Return && original?.Kind == InventoryCommandKind.ProcessorSale
                    && shipment.OperationKey.StartsWith(original.OperationKey + ":", StringComparison.Ordinal) && shipment.ReversedAt == null && processor.BinsSent == quantity
                    && await db.ProcessorShipmentLines.CountAsync(x => x.ProcessorShipmentId == shipment.Id, ct) == 1, "Processor return does not match exact whole shipment.");
                destination = new(processor.WarehouseId, processor.RoomId); shipment.ReversedAt = now; shipment.ReversedByUserId = c.ActorId;
                shipment.ReversalReason = c.Reason; shipment.ConcurrencyVersion++;
            }
            var before = await PhysicalAsync(db, i, destination!.WarehouseId, destination.RoomId, ct);
            Require(await db.Rooms.AnyAsync(x => x.Id == destination.RoomId && x.WarehouseId == destination.WarehouseId
                && x.IsActive && x.Warehouse.IsActive && !x.IsSealed, ct), "Return destination is unavailable or sealed.");
            Require(before >= 0, "Return/receive destination has negative legacy inventory.");
            var credit = Ledger(c, i, destination.WarehouseId, destination.RoomId, quantity, before, key + ":in", now);
            credit.InterCrewTransfer = crew; credit.OutsideWarehouseTransfer = outside; credit.ProcessorShipmentLine = processor;
            credit.AdjustmentType = crew != null ? c.Kind == InventoryCommandKind.ReceiveTransfer ? InterCrewTransferAdjustmentTypes.Receive : InterCrewTransferAdjustmentTypes.ReversalSource
                : outside != null ? OutsideWarehouseTransferAdjustmentTypes.Reversal : ProcessorShipmentAdjustmentTypes.Reversal;
            ledger.Add(credit);
            foreach (var a in allocations)
            {
                var target = await factory.CurrentAsync(i, destination.WarehouseId, destination.RoomId, a.Segment.TreatmentSignature,
                    a.Segment.TreatmentState, a.Segment.ReceiptId, a.Segment.Applications.Select(x => x.RoomTreatmentApplicationId), now, ct);
                Credit(target, a.Quantity, now);
                if (c.Kind == InventoryCommandKind.Return)
                {
                    var all = await db.TreatmentLineageMovements.Where(x => crew != null && x.InterCrewTransferId == id
                        || outside != null && x.OutsideWarehouseTransferId == id || processor != null && x.ProcessorShipmentLineId == id).ToListAsync(ct);
                    var originalMoves = all.Where(x => x.SourceSegmentId == a.Segment.Id && x.ReversesTreatmentLineageMovementId == null
                        && x.MovementType == (crew != null ? "InterCrewDispatch" : outside != null ? "OutsideWarehouseTransfer" : "ProcessorShipment"))
                        .Select(x => (Movement: x, Quantity: x.BinCount - all.Where(y => y.ReversesTreatmentLineageMovementId == x.Id).Sum(y => y.BinCount)))
                        .Where(x => x.Quantity > 0).ToArray();
                    Require(originalMoves.Sum(x => x.Quantity) == a.Quantity, "Original dispatch allocation no longer balances.");
                    foreach (var originalMove in originalMoves)
                    {
                        var reversal = Move(c, i, new(a.Segment, originalMove.Quantity), target, null, destination.RoomId, key + ":r" + originalMove.Movement.Id, now,
                            crew != null ? "InterCrewReversal" : outside != null ? "OutsideWarehouseTransferReversal" : "ProcessorShipmentReversal");
                        reversal.InterCrewTransfer = crew; reversal.OutsideWarehouseTransfer = outside; reversal.ProcessorShipmentLine = processor;
                        reversal.ReversesTreatmentLineageMovementId = originalMove.Movement.Id; movements.Add(reversal);
                    }
                }
                else
                {
                    var move = Move(c, i, a, target, null, destination.RoomId, key, now, "InterCrewReceive");
                    move.InterCrewTransfer = crew; movements.Add(move);
                }
            }
            return (id, destination, before);
        }
        Require(original?.Kind == InventoryCommandKind.ReceiveTransfer, "Reopen/received return needs original receive command.");
        var previous = original!.Lines.Single(x => x.Source.Identity.Key == i.Key && x.Destination?.RoomId == loc.RoomId);
        Require(previous.Quantity == quantity && previous.Destination?.RoomId == loc.RoomId && previous.Source.Identity.Key == i.Key, "Original received identity/quantity mismatch.");
        var transfer = await db.InterCrewTransfers.SingleAsync(x => x.Id == previous.Source.Location.CustodyRecordId, ct);
        var firstReversal = completedParents.Add(transfer.Id);
        var totalReceived = original.Lines.Sum(x => x.Quantity);
        Require(!firstReversal || transfer.Status == InterCrewTransferStatuses.Received && transfer.BinsReceived == totalReceived, "Transfer is not a complete received allocation.");
        var destinationRoom = c.Kind == InventoryCommandKind.Return ? new InventoryCommandDestination(transfer.SourceWarehouseId, transfer.SourceRoomId) : null;
        var destinationQuantity = destinationRoom == null ? 0 : await PhysicalAsync(db, i, destinationRoom.WarehouseId, destinationRoom.RoomId, ct);
        if (destinationRoom != null)
            Require(await db.Rooms.AnyAsync(x => x.Id == destinationRoom.RoomId && x.WarehouseId == destinationRoom.WarehouseId
                && x.IsActive && x.Warehouse.IsActive && !x.IsSealed, ct), "Return destination is unavailable or sealed.");
        Require(destinationQuantity >= 0, "Return destination is negative.");
        var receives = await db.TreatmentLineageMovements.Where(x => x.InterCrewTransferId == transfer.Id && x.MovementType == "InterCrewReceive"
            && x.OperationKey.StartsWith(original.OperationKey + ":")).ToListAsync(ct);
        Require(receives.Sum(x => x.BinCount) == totalReceived && receives.All(x => x.DestinationSegmentId != null), "Original receive movement evidence is incomplete.");
        var receiveIds = receives.Select(x => x.Id).ToArray();
        var segmentIds = receives.Select(x => x.DestinationSegmentId!.Value).Distinct().ToArray();
        Require(allocations.All(x => segmentIds.Contains(x.Segment.Id))
            && !await db.TreatmentLineageMovements.AnyAsync(x => (segmentIds.Contains(x.SourceSegmentId ?? 0) || segmentIds.Contains(x.DestinationSegmentId ?? 0))
                && x.Id > receiveIds.Min() && !receiveIds.Contains(x.Id) && !x.OperationKey.StartsWith(c.OperationKey + ":"), ct), "Subsequent movement prevents exact receive reversal.");
        if (firstReversal && transfer.RequiresTruckReceipt)
        {
            Require(c.ReceivingEvidence != null && transfer.ReceivingReceiptId == c.ReceivingEvidence.ReceiptId, "Matched receipt reversal evidence is required.");
            var receipt = await db.Receipts.SingleAsync(x => x.Id == c.ReceivingEvidence!.ReceiptId, ct);
            Require(receipt.ConcurrencyVersion == c.ReceivingEvidence!.ExpectedVersion && receipt.TransferCompletedAt != null
                && !receipt.IsDeleted && receipt.IsTransferReceipt, "Receiving evidence changed.", InventoryCommandStatus.Stale);
            AddAudit(db, c, "CanonicalTruckReceiptReopen", receipt.Id.ToString(), new { receipt.TransferCompletedAt, transfer.ReceivingReceiptId, receipt.ConcurrencyVersion },
                new { CompletedAt = (DateTimeOffset?)null, Version = receipt.ConcurrencyVersion + 1 }, now);
            receipt.TransferCompletedAt = null; receipt.ConcurrencyVersion++; receipt.UpdatedAt = now; transfer.ReceivingReceiptId = null;
        }
        if (c.Kind == InventoryCommandKind.Return)
        { transfer.ReversedAt = now; transfer.ReversedByUserId = c.ActorId; transfer.ReversalReason = c.Reason; transfer.ReversalOperationKey = c.OperationKey; }
        transfer.ReceivedAt = null; transfer.ReceivedByUserId = null; transfer.ReceiveOperationKey = null; transfer.VarianceBins = null;
        transfer.Status = c.Kind == InventoryCommandKind.Return ? InterCrewTransferStatuses.Reversed : InterCrewTransferStatuses.InTransit;
        transfer.BinsReceived = null; transfer.DestinationRoomId = null; transfer.DestinationWarehouseId = null; if (firstReversal) transfer.ConcurrencyVersion++;
        debit!.InterCrewTransfer = transfer; debit.AdjustmentType = InterCrewTransferAdjustmentTypes.ReversalDestination;
        if (destinationRoom != null)
        {
            var credit = Ledger(c, i, destinationRoom.WarehouseId, destinationRoom.RoomId, quantity, destinationQuantity, key + ":in", now);
            credit.InterCrewTransfer = transfer; credit.AdjustmentType = InterCrewTransferAdjustmentTypes.ReversalSource; ledger.Add(credit);
        }
        foreach (var a in allocations)
        {
            TreatmentLineageSegment? target = null;
            if (destinationRoom != null)
            {
                target = await factory.CurrentAsync(i, destinationRoom.WarehouseId, destinationRoom.RoomId, a.Segment.TreatmentSignature,
                    a.Segment.TreatmentState, a.Segment.ReceiptId, a.Segment.Applications.Select(x => x.RoomTreatmentApplicationId), now, ct);
                Credit(target, a.Quantity, now);
            }
            var originals = receives.Where(x => x.DestinationSegmentId == a.Segment.Id).ToArray();
            Require(originals.Sum(x => x.BinCount) == a.Quantity, "Received allocation changed before reversal.");
            foreach (var originalMove in originals)
            {
                var m = Move(c, i, new(a.Segment, originalMove.BinCount), target, loc.RoomId, destinationRoom?.RoomId,
                    key + ":r" + originalMove.Id, now, "InterCrewReversal");
                m.InterCrewTransfer = transfer; m.ReversesTreatmentLineageMovementId = originalMove.Id; movements.Add(m);
            }
        }
        return (transfer.Id, destinationRoom, destinationQuantity);
    }

    private static async Task<long> TreatAsync(CropQcDbContext db, CanonicalProjectionFactory factory, InventoryCommand c,
        InventoryIdentity identity, InventoryLocation loc, List<Allocation> allocations, int physical, int quantity,
        string key, DateTimeOffset now, HashSet<long> reversedApplications, CancellationToken ct)
    {
        Require(quantity == physical, "Treatment requires the entire selected physical position.");
        RoomTreatmentApplication app;
        if (c.Kind == InventoryCommandKind.TreatmentAssignment)
        {
            var chemical = await db.TreatmentChemicals.SingleOrDefaultAsync(x => x.Id == c.TreatmentChemicalId && x.IsActive, ct);
            Require(chemical != null, "Treatment chemical is unavailable.");
            var applicationKey = $"{c.OperationKey}:t:{loc.RoomId}";
            app = db.RoomTreatmentApplications.Local.SingleOrDefault(x => x.OperationKey == applicationKey)!;
            if (app == null)
            {
                app = new()
                {
                    OperationKey = applicationKey,
                    TreatmentChemical = chemical!,
                    WarehouseId = loc.WarehouseId,
                    RoomId = loc.RoomId!.Value,
                    AppliedAt = c.EffectiveAt,
                    AppliedByUserId = c.ActorId,
                    CreatedAt = now,
                    CreatedByUserId = c.ActorId,
                    TotalBinsSnapshot = c.Lines.Where(x => x.Source.Location.RoomId == loc.RoomId).Sum(x => x.Quantity),
                    ProductNameSnapshot = chemical!.ProductName,
                    CommonNameSnapshot = chemical.CommonName,
                    CropSnapshot = chemical.Crop,
                    VolumeSnapshot = chemical.Volume,
                    UnitSnapshot = chemical.Unit,
                    UnitPriceSnapshot = chemical.UnitPrice,
                    CurrencySnapshot = chemical.Currency,
                    Notes = c.Reason
                };
                db.RoomTreatmentApplications.Add(app); await db.SaveChangesAsync(ct);
            }
        }
        else
        {
            app = await db.RoomTreatmentApplications.SingleAsync(x => x.Id == c.TreatmentApplicationId, ct);
            Require(app.RoomId == loc.RoomId && app.WarehouseId == loc.WarehouseId && (app.ReversedAt == null || reversedApplications.Contains(app.Id))
                && allocations.All(x => x.Segment.Applications.Any(a => a.RoomTreatmentApplicationId == app.Id)), "Treatment reversal does not match active application.");
            var commandKeys = c.Lines.Where(x => x.Source.Location.RoomId == loc.RoomId).Select(x => x.Source.Identity.Key).ToArray();
            var selected = (await db.TreatmentLineageSegments.Where(x => x.RoomId == loc.RoomId && x.Disposition == "Current" && x.CurrentBins > 0
                && x.Applications.Any(a => a.RoomTreatmentApplicationId == app.Id)).ToListAsync(ct))
                .Where(x => commandKeys.Contains(InventoryStatusIdentity.NormalizeLineageKey(x.IdentityKey))).Select(x => x.Id).ToArray();
            Require(!await db.TreatmentLineageSegments.AnyAsync(x => x.Disposition == "Current" && x.CurrentBins > 0
                && x.Applications.Any(a => a.RoomTreatmentApplicationId == app.Id) && !selected.Contains(x.Id), ct)
                && !await db.TreatmentLineageMovements.AnyAsync(x => x.SourceSegment != null
                    && x.SourceSegment.Applications.Any(a => a.RoomTreatmentApplicationId == app.Id) && x.CreatedAt >= app.CreatedAt, ct),
                "Treatment has downstream stock/history outside this exact reversal.");
            reversedApplications.Add(app.Id);
            app.ReversedAt = now; app.ReversedByUserId = c.ActorId; app.ReversalReason = c.Reason;
        }
        foreach (var a in allocations)
        {
            var ids = a.Segment.Applications.Select(x => x.RoomTreatmentApplicationId).ToHashSet();
            if (c.Kind == InventoryCommandKind.TreatmentAssignment) ids.Add(app.Id); else ids.Remove(app.Id);
            var signature = ids.Count == 0 ? "u" : "u|a:" + string.Join(',', ids.Order());
            var target = await factory.CurrentAsync(identity, loc.WarehouseId, loc.RoomId!.Value, signature,
                ids.Count == 0 ? "Untreated" : "Confirmed", a.Segment.ReceiptId, ids, now, ct);
            if (c.Kind == InventoryCommandKind.TreatmentAssignment)
                app.Sources.Add(new()
                {
                    ReceiptId = a.Segment.ReceiptId,
                    CropYear = identity.CropYear,
                    GrowerLotId = identity.GrowerLotId,
                    FruitProfileId = identity.FruitProfileId,
                    IdentityKey = identity.Key,
                    GrowerNumberSnapshot = identity.GrowerNumber,
                    GrowerNameSnapshot = a.Segment.GrowerNameSnapshot,
                    LotNumberSnapshot = identity.Lot,
                    VarietyCodeSnapshot = identity.Variety,
                    ProductionTypeSnapshot = identity.ProductionType,
                    IsOrganicSnapshot = identity.IsOrganic,
                    InventoryStatusSnapshot = identity.Status,
                    BinsTreated = a.Quantity,
                    PriorTreatmentSignature = a.Segment.TreatmentSignature,
                    ResultTreatmentSignature = signature
                });
            CanonicalProjectionFactory.Retire(a.Segment, c.OperationKey, now);
            Credit(target, a.Quantity, now);
        }
        return app.Id;
    }
}
