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
 WHERE NOT r."IsDeleted" AND NOT r."IsTestData" AND NOT r."IsTransferReceipt" AND r."ReceiptType"='Truck receipt'
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
), location_ids AS (
 SELECT DISTINCT "ReceiptInventoryOverrideId" AS id FROM "RoomInventoryAdjustments"
 WHERE "AdjustmentType" IN ('CorrectOriginalRoomOut','CorrectOriginalRoomIn') AND "ReceiptInventoryOverrideId" IS NOT NULL
 UNION SELECT o."Id" FROM "ReceiptInventoryOverrides" o WHERE o."ActionType"='LocationCorrection' AND o."ExpectedAdjustmentCount"=2
 AND NOT EXISTS (SELECT 1 FROM "RoomInventoryAdjustments" r WHERE r."ReceiptInventoryOverrideId"=o."Id" AND r."AdjustmentType"='ReceiptAdminOverride')
 UNION SELECT CASE WHEN a."EntityKey" ~ '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$'
 THEN a."EntityKey"::uuid END FROM "AuditLogs" a
 WHERE a."Action"='CanonicalReceiptLocationCorrected'
 AND a."EntityKey" ~ '^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$'
), location_parents AS (
 SELECT ids.id,o.*,
 CASE WHEN o."BeforeReceiptSnapshotJson" IS JSON OBJECT THEN o."BeforeReceiptSnapshotJson"::jsonb ELSE '{}'::jsonb END AS before_snapshot,
 CASE WHEN o."AfterReceiptSnapshotJson" IS JSON OBJECT THEN o."AfterReceiptSnapshotJson"::jsonb ELSE '{}'::jsonb END AS after_snapshot,
 CASE WHEN o."AffectedInventorySnapshotJson" IS JSON OBJECT THEN o."AffectedInventorySnapshotJson"::jsonb ELSE '{}'::jsonb END AS affected
 FROM location_ids ids LEFT JOIN "ReceiptInventoryOverrides" o ON o."Id"=ids.id
), location_checks AS (
 SELECT p.id,COALESCE(
 p."IsComplete" AND p."ActionType"='LocationCorrection' AND p."ExpectedAdjustmentCount"=2
 AND p."InventoryDelta"=0 AND p."OldReceiptBinCount"=p."NewReceiptBinCount"
 AND p."CurrentInventoryBefore"=p."CurrentInventoryAfter" AND btrim(p."OperationKey")<>'' AND btrim(p."Reason")<>''
 AND (SELECT count(*) FROM "RoomInventoryAdjustments" r WHERE r."ReceiptInventoryOverrideId"=p.id)=2
 AND (SELECT count(*) FROM "RoomInventoryAdjustments" r WHERE r."ReceiptInventoryOverrideId"=p.id AND r."AdjustmentType"='CorrectOriginalRoomOut')=1
 AND (SELECT count(*) FROM "RoomInventoryAdjustments" r WHERE r."ReceiptInventoryOverrideId"=p.id AND r."AdjustmentType"='CorrectOriginalRoomIn')=1
 AND (SELECT count(*) FROM "AuditLogs" a WHERE a."Action"='CanonicalReceiptLocationCorrected' AND a."EntityKey"=p.id::text)=1
 AND outgoing."ChangeAmount"<0 AND outgoing."ChangeAmount"::bigint+incoming."ChangeAmount"=0
 AND outgoing."RoomId"<>incoming."RoomId" AND outgoing."AdjustmentAt"=incoming."AdjustmentAt"
 AND outgoing."InventoryOperationKey"=p."OperationKey"||':location:out' AND incoming."InventoryOperationKey"=p."OperationKey"||':location:in'
 AND outgoing."CropYear" IS NOT DISTINCT FROM incoming."CropYear" AND outgoing."GrowerLotId" IS NOT DISTINCT FROM incoming."GrowerLotId"
 AND outgoing."FruitProfileId" IS NOT DISTINCT FROM incoming."FruitProfileId" AND outgoing."LotNumber" IS NOT DISTINCT FROM incoming."LotNumber"
 AND outgoing."VarietyCode" IS NOT DISTINCT FROM incoming."VarietyCode" AND outgoing."InventoryStatus" IS NOT DISTINCT FROM incoming."InventoryStatus"
 AND NOT EXISTS (SELECT 1 FROM "RoomInventoryAdjustments" r LEFT JOIN "Rooms" room ON room."Id"=r."RoomId"
   WHERE r."ReceiptInventoryOverrideId"=p.id AND NOT COALESCE(r."ReceiptId"=p."ReceiptId" AND r."CreatedByUserId"=p."AdministratorUserId"
    AND r."InventoryInvariantVersion"=3 AND r."CreatedAt"=p."CreatedAt" AND r."Reason"=p."Reason"
    AND r."OldBinCount" IS NOT NULL AND r."NewBinCount"::bigint=r."OldBinCount"::bigint+r."ChangeAmount"
    AND room."WarehouseId"=r."WarehouseId",false))
 AND p.before_snapshot->>'id'=p."ReceiptId"::text AND p.after_snapshot->>'id'=p."ReceiptId"::text
 AND NOT EXISTS (SELECT 1 FROM (VALUES
   (p.before_snapshot->'id'),(p.after_snapshot->'id'),
   (p.before_snapshot->'binCount'),(p.after_snapshot->'binCount'),
   (p.affected->'sourceRoomId'),(p.affected->'sourceWarehouseId'),(p.affected->'relocatedBins'),
   (p.affected#>'{destination,roomId}'),(p.affected#>'{destination,warehouseId}'),
   (p.after_snapshot->'roomId'),(p.after_snapshot->'warehouseId'),
   (history.value->'sourceRoomId'),(history.value->'sourceWarehouseId'),(history.value->'relocatedBins'),
   (history.value#>'{destination,roomId}'),(history.value#>'{destination,warehouseId}')) numbers(value)
   WHERE jsonb_typeof(numbers.value) IS DISTINCT FROM 'number')
 AND p.before_snapshot->>'binCount'=p."OldReceiptBinCount"::text AND p.after_snapshot->>'binCount'=p."NewReceiptBinCount"::text
 AND p.affected->>'sourceRoomId'=outgoing."RoomId"::text AND p.affected->>'sourceWarehouseId'=outgoing."WarehouseId"::text
 AND p.affected->>'relocatedBins'=incoming."ChangeAmount"::text
 AND p.affected#>>'{destination,roomId}'=incoming."RoomId"::text AND p.affected#>>'{destination,warehouseId}'=incoming."WarehouseId"::text
 AND p.after_snapshot->>'roomId'=incoming."RoomId"::text AND p.after_snapshot->>'warehouseId'=incoming."WarehouseId"::text
 AND audit."UserId"=p."AdministratorUserId" AND audit."CreatedAt"=p."CreatedAt"
 AND audit."EntityName"='CanonicalInventory' AND audit."SourceApplication"='CanonicalInventory/v1'
 AND (CASE WHEN audit."BeforeValuesJson" IS JSON THEN audit."BeforeValuesJson"::jsonb ELSE 'null'::jsonb END)#>>'{}'=p."BeforeReceiptSnapshotJson"
 AND history.value->>'afterReceiptSnapshotJson'=p."AfterReceiptSnapshotJson"
 AND history.value->>'sourceRoomId'=outgoing."RoomId"::text AND history.value->>'sourceWarehouseId'=outgoing."WarehouseId"::text
 AND history.value#>>'{destination,roomId}'=incoming."RoomId"::text AND history.value#>>'{destination,warehouseId}'=incoming."WarehouseId"::text
 AND history.value->>'relocatedBins'=incoming."ChangeAmount"::text,false) AS valid
 FROM location_parents p
 LEFT JOIN "RoomInventoryAdjustments" outgoing ON outgoing."ReceiptInventoryOverrideId"=p.id AND outgoing."AdjustmentType"='CorrectOriginalRoomOut'
 LEFT JOIN "RoomInventoryAdjustments" incoming ON incoming."ReceiptInventoryOverrideId"=p.id AND incoming."AdjustmentType"='CorrectOriginalRoomIn'
 LEFT JOIN "AuditLogs" audit ON audit."Action"='CanonicalReceiptLocationCorrected' AND audit."EntityKey"=p.id::text
 CROSS JOIN LATERAL (SELECT CASE WHEN audit."AfterValuesJson" IS JSON OBJECT THEN audit."AfterValuesJson"::jsonb ELSE '{}'::jsonb END AS value) history
), invalid_location_corrections AS (
 SELECT DISTINCT id AS correction_id FROM location_checks WHERE NOT valid
), orphan_location_rows AS (
 SELECT "Id" FROM "RoomInventoryAdjustments" WHERE "AdjustmentType" IN ('CorrectOriginalRoomOut','CorrectOriginalRoomIn') AND "ReceiptInventoryOverrideId" IS NULL
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
 ('ReceiptAdd','ReceiptEdit','ReceiptAdminOverride','StartingInventoryImport','BinsRun','BinsRunReversal','Depletion','DepletionReversal','DroppedBins','DroppedBinsReversal','ProcessorShipment','ProcessorShipmentReversal','OutsideWarehouseTransfer','OutsideWarehouseTransferReversal','InterCrewTransferDispatch','InterCrewTransferReceive','InterCrewTransferReversalDestination','InterCrewTransferReversalSource','TransferIn','TransferOut','InventoryIdentityCorrection','CorrectOriginalRoomOut','CorrectOriginalRoomIn')),
 'invalid_location_correction_ids',(SELECT COALESCE(jsonb_agg(correction_id ORDER BY correction_id),'[]'::jsonb) FROM invalid_location_corrections),
 'orphan_location_adjustment_ids',(SELECT COALESCE(jsonb_agg("Id" ORDER BY "Id"),'[]'::jsonb) FROM orphan_location_rows),
 'location_corrections_ready',NOT EXISTS (SELECT 1 FROM invalid_location_corrections) AND NOT EXISTS (SELECT 1 FROM orphan_location_rows),
 'unbalanced_transfers',(SELECT COUNT(*) FROM transfers WHERE net<>0),
 'unbalanced_identity_corrections',(SELECT COUNT(*) FROM identities WHERE net<>0),
 'identity_net',(SELECT COALESCE(SUM(net),0) FROM identities),
 'internal_transfer_net',(SELECT COALESCE(SUM(net),0) FROM transfers),
 'treatment_identity',(SELECT to_jsonb(treatment) FROM treatment),
 'other_treatment_correction_types',(SELECT jsonb_agg(x) FROM (SELECT "MovementType",COUNT(*) AS rows,SUM("BinCount") AS bins FROM "TreatmentLineageMovements" WHERE "InventoryIdentityCorrectionId" IS NOT NULL AND "MovementType"<>'IdentityReclassification' GROUP BY 1) x),
 'intercrew_transit',(SELECT COALESCE(SUM("BinsLoaded"),0) FROM "InterCrewTransfers" WHERE "Status"='InTransit'),
 'outside_warehouse_custody',(SELECT COALESCE(SUM("BinCount"),0) FROM "OutsideWarehouseTransfers" WHERE NOT "IsReversed")
) AS report;
