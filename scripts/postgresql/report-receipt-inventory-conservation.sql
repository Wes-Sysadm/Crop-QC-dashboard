-- Read-only release gate. Execute as one statement in a read-only transaction.
-- Receiving boundary: ALL active non-test Truck receipts in the current crop.
-- Set crop_year to the application's resolved ICropYearService current year.
-- Inventory boundary: current room ledger after opening-import supersession.
-- Outside/transit custody is displayed separately; it is not a physical fruit exit.
WITH parameters AS (SELECT 2026::integer AS crop_year), receiving AS (
 SELECT r."Id",r."WarehouseId",r."BinCount",COALESCE(SUM(a."ChangeAmount"),0) AS ledger
 FROM "Receipts" r
 LEFT JOIN "RoomInventoryAdjustments" a ON a."ReceiptId"=r."Id"
 AND a."InventoryIdentityCorrectionId" IS NULL
 AND (a."AdjustmentType" IN ('ReceiptAdd','ReceiptEdit')
 OR (a."AdjustmentType"='ReceiptAdminOverride'
 AND EXISTS (SELECT 1 FROM "ReceiptInventoryOverrides" o WHERE o."Id"=a."ReceiptInventoryOverrideId" AND o."ActionType"='QuantityCorrection')))
 WHERE NOT r."IsDeleted" AND NOT r."IsTestData" AND r."ReceiptType"='Truck receipt'
 AND r."CropYear"=(SELECT crop_year FROM parameters)
 GROUP BY r."Id"
), effective AS (
 SELECT a.*,CASE WHEN a."ReceiptId" IS NULL AND a."AdjustmentType"='StartingInventoryImport'
 THEN a."NewBinCount" ELSE a."ChangeAmount" END::bigint AS bins,
 COALESCE(NULLIF(a."LotNumber",''),r."GrowerNumber",r."LotCode",'') AS resolved_lot,
 COALESCE(NULLIF(a."VarietyCode",''),f."VarietyCode",rf."VarietyCode",'') AS resolved_variety
 FROM "RoomInventoryAdjustments" a
 LEFT JOIN "Receipts" r ON r."Id"=a."ReceiptId"
 LEFT JOIN "FruitProfiles" f ON f."Id"=a."FruitProfileId"
 LEFT JOIN "FruitProfiles" rf ON rf."Id"=r."FruitProfileId"
 WHERE NOT EXISTS (SELECT 1 FROM "RoomInventoryAdjustments" b WHERE b."RoomId"=a."RoomId"
 AND b."ReceiptId" IS NULL AND b."AdjustmentType"='StartingInventoryImport'
 AND CASE WHEN a."ReceiptId" IS NOT NULL THEN b."AdjustmentAt">=r."ReceivedAt" ELSE b."AdjustmentAt">a."AdjustmentAt" END)
 AND (a."ReceiptId" IS NOT NULL OR a."AdjustmentType"<>'StartingInventoryImport' OR NOT EXISTS
 (SELECT 1 FROM "RoomInventoryAdjustments" b WHERE b."RoomId"=a."RoomId" AND b."ReceiptId" IS NULL
 AND b."AdjustmentType"='StartingInventoryImport' AND b."AdjustmentAt"=a."AdjustmentAt"
 AND b."CropYear" IS NOT DISTINCT FROM a."CropYear" AND b."GrowerLotId" IS NOT DISTINCT FROM a."GrowerLotId"
 AND b."FruitProfileId" IS NOT DISTINCT FROM a."FruitProfileId" AND b."LotNumber"=a."LotNumber"
 AND b."VarietyCode" IS NOT DISTINCT FROM a."VarietyCode" AND b."CreatedAt">a."CreatedAt"))
), categories AS (
 SELECT "WarehouseId","AdjustmentType",SUM(bins) AS bins,COUNT(*) AS rows FROM effective GROUP BY 1,2
), inventory AS (
 SELECT w."Code" AS facility,COALESCE(SUM(e.bins),0) AS accounted,
 COALESCE(SUM(e.bins) FILTER (WHERE e.resolved_lot<>'' AND e.resolved_variety<>''),0) AS current_room_bins,
 COALESCE(SUM(e.bins) FILTER (WHERE e.resolved_lot='' OR e.resolved_variety=''),0) AS difference
 FROM "Warehouses" w LEFT JOIN effective e ON e."WarehouseId"=w."Id" GROUP BY w."Code"
), transfers AS (
 SELECT "RoomTransferId",SUM("ChangeAmount") AS net FROM "RoomInventoryAdjustments"
 WHERE "RoomTransferId" IS NOT NULL GROUP BY 1
), identities AS (
 SELECT "InventoryIdentityCorrectionId",SUM("ChangeAmount") AS net FROM "RoomInventoryAdjustments"
 WHERE "InventoryIdentityCorrectionId" IS NOT NULL GROUP BY 1
), treatment AS (
 SELECT COALESCE(SUM("BinCount") FILTER (WHERE "SourceSegmentId" IS NOT NULL),0) AS debit,
 COALESCE(SUM("BinCount") FILTER (WHERE "DestinationSegmentId" IS NOT NULL),0) AS credit,
 COUNT(*) FILTER (WHERE "SourceSegmentId" IS NULL OR "DestinationSegmentId" IS NULL OR "BinCount"<=0) AS invalid_rows
 FROM "TreatmentLineageMovements" WHERE "InventoryIdentityCorrectionId" IS NOT NULL AND "MovementType"='IdentityReclassification'
)
SELECT jsonb_build_object(
 'crop_year',(SELECT crop_year FROM parameters),
 'receiving',(SELECT jsonb_build_object('count',COUNT(*),'receipt_total',SUM("BinCount"),'ledger_total',SUM(ledger),
 'mismatch_count',COUNT(*) FILTER (WHERE "BinCount"<>ledger),'difference',SUM("BinCount"-ledger),
 'mismatch_receipt_ids',COALESCE(jsonb_agg("Id") FILTER (WHERE "BinCount"<>ledger),'[]'::jsonb)) FROM receiving),
 'receiving_by_facility',(SELECT jsonb_agg(x) FROM (SELECT w."Code",COUNT(*) AS receipts,SUM(r."BinCount") AS receipt_bins,SUM(r.ledger) AS ledger_bins FROM receiving r JOIN "Warehouses" w ON w."Id"=r."WarehouseId" GROUP BY 1 ORDER BY 1) x),
 'inventory_by_facility',(SELECT jsonb_agg(inventory ORDER BY facility) FROM inventory),
 'global',(SELECT jsonb_build_object('current_room_bins',SUM(current_room_bins),'accounted',SUM(accounted),'difference',SUM(difference)) FROM inventory),
 'categories',(SELECT jsonb_agg(x) FROM (SELECT w."Code" AS facility,c."AdjustmentType",c.bins,c.rows FROM categories c JOIN "Warehouses" w ON w."Id"=c."WarehouseId" ORDER BY 1,2) x),
 'unclassified_types',(SELECT COALESCE(jsonb_agg(DISTINCT "AdjustmentType"),'[]'::jsonb) FROM effective WHERE "AdjustmentType" NOT IN
 ('ReceiptAdd','ReceiptEdit','ReceiptAdminOverride','StartingInventoryImport','BinsRun','BinsRunReversal','Depletion','DepletionReversal','DroppedBins','DroppedBinsReversal','ProcessorShipment','ProcessorShipmentReversal','OutsideWarehouseTransfer','OutsideWarehouseTransferReversal','InterCrewTransferDispatch','InterCrewTransferReceive','InterCrewTransferReversalDestination','InterCrewTransferReversalSource','TransferIn','TransferOut','InventoryIdentityCorrection')),
 'unbalanced_transfers',(SELECT COUNT(*) FROM transfers WHERE net<>0),
 'unbalanced_identity_corrections',(SELECT COUNT(*) FROM identities WHERE net<>0),
 'identity_net',(SELECT COALESCE(SUM(net),0) FROM identities),
 'internal_transfer_net',(SELECT COALESCE(SUM(net),0) FROM transfers),
 'treatment_identity',(SELECT to_jsonb(treatment) FROM treatment),
 'other_treatment_correction_types',(SELECT jsonb_agg(x) FROM (SELECT "MovementType",COUNT(*) AS rows,SUM("BinCount") AS bins FROM "TreatmentLineageMovements" WHERE "InventoryIdentityCorrectionId" IS NOT NULL AND "MovementType"<>'IdentityReclassification' GROUP BY 1) x),
 'intercrew_transit',(SELECT COALESCE(SUM("BinsLoaded"),0) FROM "InterCrewTransfers" WHERE "Status"='InTransit'),
 'outside_warehouse_custody',(SELECT COALESCE(SUM("BinCount"),0) FROM "OutsideWarehouseTransfers" WHERE NOT "IsReversed")
) AS report;
