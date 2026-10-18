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
-- These proofs deliberately use raw history, never the opening-filtered effective CTE.
, compensation_candidates AS (
 SELECT * FROM "ReceiptCustodyReversals" WHERE "PlacementId" IS NOT NULL OR "InventoryAdjustmentId" IS NOT NULL OR "MovementId" IS NOT NULL
), custody_totals AS (
 SELECT a.*,a."Quantity"::bigint-COALESCE((SELECT sum(r."Quantity") FROM "ReceiptCustodyReversals" r WHERE r."AcknowledgmentId"=a."Id" AND r."PlacementId" IS NULL),0) AS net,
 COALESCE((SELECT sum(p."Quantity") FROM "ReceiptCustodyPlacements" p WHERE p."AcknowledgmentId"=a."Id"),0)
 -COALESCE((SELECT sum(r."Quantity") FROM "ReceiptCustodyReversals" r WHERE r."AcknowledgmentId"=a."Id" AND r."PlacementId" IS NOT NULL),0) AS placed
 FROM "ReceiptCustodyAcknowledgments" a
), custody_checks AS (
 SELECT a."Id",COALESCE(a."Quantity">0 AND a."ReceiptId"=t."ReceivingReceiptId"
 AND NOT EXISTS (SELECT 1 FROM "ReceiptCustodyPlacements" p WHERE p."AcknowledgmentId"=a."Id" AND p."Quantity"<=0)
 AND NOT EXISTS (SELECT 1 FROM "ReceiptCustodyReversals" r WHERE r."AcknowledgmentId"=a."Id" AND (r."Quantity"<=0 OR (r."PlacementId" IS NULL AND (r."InventoryAdjustmentId" IS NOT NULL OR r."MovementId" IS NOT NULL))))
 AND NOT EXISTS (SELECT 1 FROM (
 SELECT a."AcknowledgedAt" AS at UNION SELECT p."PlacedAt" FROM "ReceiptCustodyPlacements" p WHERE p."AcknowledgmentId"=a."Id"
 UNION SELECT r."ReversedAt" FROM "ReceiptCustodyReversals" r WHERE r."AcknowledgmentId"=a."Id") times
 CROSS JOIN LATERAL (SELECT a."Quantity"::bigint-COALESCE((SELECT sum(r."Quantity") FROM "ReceiptCustodyReversals" r WHERE r."AcknowledgmentId"=a."Id" AND r."PlacementId" IS NULL AND r."ReversedAt"<=times.at),0) AS net,
 COALESCE((SELECT sum(p."Quantity") FROM "ReceiptCustodyPlacements" p WHERE p."AcknowledgmentId"=a."Id" AND p."PlacedAt"<=times.at),0)
 -COALESCE((SELECT sum(r."Quantity") FROM "ReceiptCustodyReversals" r WHERE r."AcknowledgmentId"=a."Id" AND r."PlacementId" IS NOT NULL AND r."ReversedAt"<=times.at),0) AS placed) q
 WHERE times.at<a."AcknowledgedAt" OR q.net<0 OR q.placed<0 OR q.placed>q.net),false) AS valid
 FROM custody_totals a LEFT JOIN "InterCrewTransfers" t ON t."Id"=a."InterCrewTransferId"
), transfer_custody_checks AS (
 SELECT t."Id",COALESCE(q.net<=t."BinsLoaded" AND t."BinsReceived"=q.net AND t."VarianceBins"=q.net-t."BinsLoaded" AND q.placed<=q.net
 AND CASE WHEN t."Status"='Received' THEN q.placed=t."BinsLoaded" AND q.net=t."BinsLoaded" ELSE t."Status"='InTransit' AND q.placed<t."BinsLoaded" END
 AND NOT EXISTS (SELECT 1 FROM custody_totals a JOIN custody_checks c ON c."Id"=a."Id" WHERE a."InterCrewTransferId"=t."Id" AND NOT c.valid)
 AND NOT EXISTS (SELECT 1 FROM custody_totals a WHERE a."InterCrewTransferId"=t."Id" GROUP BY a."DispatchMovementId",a."OperationKey" HAVING count(*)<>1)
 AND NOT EXISTS (SELECT 1 FROM custody_totals at_ack JOIN "TreatmentLineageMovements" d ON d."Id"=at_ack."DispatchMovementId" WHERE at_ack."InterCrewTransferId"=t."Id" AND
 (SELECT sum(a."Quantity"::bigint-COALESCE((SELECT sum(r."Quantity") FROM "ReceiptCustodyReversals" r WHERE r."AcknowledgmentId"=a."Id" AND r."PlacementId" IS NULL AND r."ReversedAt"<=at_ack."AcknowledgedAt"),0))
 FROM custody_totals a WHERE a."InterCrewTransferId"=t."Id" AND a."DispatchMovementId"=at_ack."DispatchMovementId" AND a."AcknowledgedAt"<=at_ack."AcknowledgedAt")>d."BinCount")
 AND NOT EXISTS (SELECT 1 FROM custody_totals a LEFT JOIN "TreatmentLineageMovements" d ON d."Id"=a."DispatchMovementId" WHERE a."InterCrewTransferId"=t."Id"
 GROUP BY a."DispatchMovementId",d."Id" HAVING NOT COALESCE(d."InterCrewTransferId"=t."Id" AND sum(a.net)<=d."BinCount",false)),false) AS valid
 FROM "InterCrewTransfers" t CROSS JOIN LATERAL (SELECT COALESCE(sum(a.net),0) AS net,COALESCE(sum(a.placed),0) AS placed FROM custody_totals a WHERE a."InterCrewTransferId"=t."Id") q
), compensation_operations AS (
 SELECT r."OperationKey" AS key,39 AS kind,r."ActorId" AS actor,r."ReversedAt" AS at,r."AcknowledgmentId" AS ack,r."PlacementId" AS selected,r."Quantity" AS quantity,r."InventoryAdjustmentId" AS ledger,r."MovementId" AS movement FROM compensation_candidates r
 UNION ALL SELECT p."OperationKey",37,l."CreatedByUserId",p."PlacedAt",p."AcknowledgmentId",p."AcknowledgmentId",p."Quantity",p."InventoryAdjustmentId",p."MovementId"
 FROM "ReceiptCustodyPlacements" p LEFT JOIN "RoomInventoryAdjustments" l ON l."Id"=p."InventoryAdjustmentId"
), operation_checks AS (
 SELECT op.*,COALESCE(j."ActorId"=op.actor AND j."CommittedAt"=op.at AND j."IntentHash"=upper(encode(sha256(convert_to(j."IntentJson",'UTF8')),'hex'))
 AND intent->>'kind'=op.kind::text AND intent->>'operationKey'=op.key AND intent->>'actorId'=op.actor::text
 -- Intent JSON retains 100ns ticks; Npgsql truncates at PostgreSQL microseconds.
 AND (CASE WHEN pg_input_is_valid(regexp_replace(intent->>'effectiveAt','(\.[0-9]{6})[0-9]+','\1'),'timestamp with time zone') THEN regexp_replace(intent->>'effectiveAt','(\.[0-9]{6})[0-9]+','\1')::timestamptz END)=l."AdjustmentAt"
 AND intent->>'reason'=l."Reason" AND intent#>>'{receiptCustody,transferId}'=a."InterCrewTransferId"::text AND intent#>>'{receiptCustody,receiptId}'=a."ReceiptId"::text
 AND jsonb_array_length(allocations)>0
 AND NOT EXISTS (SELECT 1 FROM jsonb_array_elements(allocations) x WHERE NOT COALESCE(jsonb_typeof(x->'quantity')='number' AND x->'quantity'>'0'::jsonb AND jsonb_typeof(x->'id')='number',false))
 AND (SELECT count(DISTINCT x->'id') FROM jsonb_array_elements(allocations) x)=jsonb_array_length(allocations)
 AND (SELECT count(*) FROM jsonb_array_elements(allocations) x WHERE x->>'id'=op.selected::text AND x->>'quantity'=op.quantity::text)=1
 AND CASE WHEN op.kind=39 THEN intent#>'{receiptCustody,destination}'='null'::jsonb
 ELSE intent#>>'{receiptCustody,destination,roomId}'=l."RoomId"::text AND intent#>>'{receiptCustody,destination,warehouseId}'=l."WarehouseId"::text END
 AND jsonb_array_length(effects)=jsonb_array_length(allocations)
 AND committed->>'status'='0' AND committed->>'operationKey'=op.key AND jsonb_typeof(committed->'effects')='array'
 AND (SELECT count(*) FROM jsonb_array_elements(effects) e WHERE e->>'quantity'=op.quantity::text AND e->>'parentId'=a."InterCrewTransferId"::text
 AND e->'ledgerIds'=jsonb_build_array(l."Id") AND e->'movementIds'=jsonb_build_array(op.movement))=1
 AND (SELECT count(*) FROM "AuditLogs" b WHERE b."Action"='CanonicalInventoryCommand' AND b."EntityKey"=op.key)=1
 AND audit."UserId"=op.actor AND audit."CreatedAt"=op.at AND audit."EntityName"='CanonicalInventory' AND audit."SourceApplication"='CanonicalInventory/v1'
 AND before_audit->>'intent'=j."IntentJson" AND before_audit->>'hash'=j."IntentHash" AND after_audit=committed->'effects'
 AND jsonb_array_length(allocations)=CASE WHEN op.kind=37 THEN (SELECT count(*) FROM "ReceiptCustodyPlacements" p WHERE p."OperationKey"=op.key)
 ELSE (SELECT count(*) FROM compensation_candidates r WHERE r."OperationKey"=op.key) END,false) AS valid
 FROM compensation_operations op
 LEFT JOIN "InventoryCommands" j ON j."OperationKey"=op.key
 LEFT JOIN "RoomInventoryAdjustments" l ON l."Id"=op.ledger
 LEFT JOIN "ReceiptCustodyAcknowledgments" a ON a."Id"=op.ack
 LEFT JOIN "AuditLogs" audit ON audit."Action"='CanonicalInventoryCommand' AND audit."EntityKey"=op.key
 CROSS JOIN LATERAL (SELECT CASE WHEN j."IntentJson" IS JSON OBJECT THEN j."IntentJson"::jsonb ELSE '{}'::jsonb END AS intent,
 CASE WHEN j."ResultJson" IS JSON OBJECT THEN j."ResultJson"::jsonb ELSE '{}'::jsonb END AS committed,
 CASE WHEN audit."BeforeValuesJson" IS JSON OBJECT THEN audit."BeforeValuesJson"::jsonb ELSE '{}'::jsonb END AS before_audit,
 CASE WHEN audit."AfterValuesJson" IS JSON THEN audit."AfterValuesJson"::jsonb ELSE 'null'::jsonb END AS after_audit) json
 CROSS JOIN LATERAL (SELECT CASE WHEN jsonb_typeof(intent#>'{receiptCustody,allocations}')='array' THEN intent#>'{receiptCustody,allocations}' ELSE '[]'::jsonb END AS allocations,
 CASE WHEN jsonb_typeof(committed->'effects')='array' THEN committed->'effects' ELSE '[]'::jsonb END AS effects) arrays
), compensation_checks AS (
 SELECT r."Id",COALESCE(r."PlacementId"=p."Id" AND r."AcknowledgmentId"=p."AcknowledgmentId" AND a."ReceiptId"=t."ReceivingReceiptId" AND receipt."IsTransferReceipt" AND receipt."ReceiptType"='Truck receipt'
 AND r."Quantity">0 AND p."Quantity">0 AND a."Quantity">0 AND r."ActorId">0 AND btrim(r."Reason")<>''
 AND r."ReversedAt">=p."PlacedAt" AND p."PlacedAt">=a."AcknowledgedAt" AND a."AcknowledgedAt">=d."CreatedAt"
 AND (SELECT sum(s."Quantity"::bigint) FROM "ReceiptCustodyReversals" s WHERE s."PlacementId"=p."Id")<=p."Quantity"
 AND NOT EXISTS (SELECT 1 FROM "ReceiptCustodyReversals" s WHERE s."PlacementId"=p."Id" AND s."Quantity"<=0)
 AND (SELECT count(*) FROM compensation_candidates s WHERE s."InventoryAdjustmentId"=l."Id")=1 AND (SELECT count(*) FROM compensation_candidates s WHERE s."MovementId"=m."Id")=1
 AND (SELECT count(*) FROM "ReceiptCustodyReversals" s WHERE s."PlacementId"=p."Id" AND s."OperationKey"=r."OperationKey")=1
 AND (SELECT count(*) FROM "ReceiptCustodyPlacements" s WHERE s."InventoryAdjustmentId"=credit."Id")=1 AND (SELECT count(*) FROM "ReceiptCustodyPlacements" s WHERE s."MovementId"=original."Id")=1
 AND (SELECT count(*) FROM "ReceiptCustodyPlacements" s WHERE s."AcknowledgmentId"=a."Id" AND s."OperationKey"=p."OperationKey")=1
 AND d."MovementType"='InterCrewDispatch' AND d."InterCrewTransferId"=t."Id" AND d."SourceRoomId"=t."SourceRoomId" AND source."RoomId"=t."SourceRoomId" AND source."WarehouseId"=t."SourceWarehouseId"
 AND d."DestinationRoomId" IS NULL AND d."DestinationSegmentId" IS NULL AND d."ReversesTreatmentLineageMovementId" IS NULL
 AND original."MovementType"='InterCrewReceive' AND original."InterCrewTransferId"=t."Id" AND original."SourceSegmentId"=source."Id" AND original."SourceRoomId" IS NULL AND original."ReversesTreatmentLineageMovementId" IS NULL
 AND original."DestinationRoomId"=target."RoomId" AND original."BinCount"=p."Quantity"
 AND credit."ChangeAmount"=p."Quantity" AND credit."AdjustmentType"='InterCrewTransferReceive' AND credit."InterCrewTransferId"=t."Id" AND credit."CreatedAt"=p."PlacedAt" AND original."CreatedAt"=p."PlacedAt" AND credit."CreatedByUserId"=original."CreatedByUserId"
 AND credit."InventoryOperationKey"=p."OperationKey"||':'||a."Id" AND original."OperationKey"=credit."InventoryOperationKey"||':s'||source."Id"
 AND target."WarehouseId"=receipt."WarehouseId" AND credit."WarehouseId"=target."WarehouseId" AND credit."RoomId"=target."RoomId"
 AND l."AdjustmentType"='ReceiptPlacementReversal' AND l."ChangeAmount"::bigint=-r."Quantity"::bigint AND l."InterCrewTransferId"=t."Id"
 AND l."InventoryOperationKey"=r."OperationKey"||':'||p."Id" AND l."CreatedAt"=r."ReversedAt" AND l."CreatedByUserId"=r."ActorId" AND btrim(l."Reason")=r."Reason"
 AND ROW(l."WarehouseId",l."RoomId",l."CropYear",l."GrowerLotId",l."FruitProfileId",l."LotNumber",l."VarietyCode",l."InventoryStatus") IS NOT DISTINCT FROM ROW(credit."WarehouseId",credit."RoomId",credit."CropYear",credit."GrowerLotId",credit."FruitProfileId",credit."LotNumber",credit."VarietyCode",credit."InventoryStatus")
 AND l."InventoryInvariantVersion"=3 AND credit."InventoryInvariantVersion"=3 AND l."NewBinCount"::bigint=l."OldBinCount"::bigint+l."ChangeAmount" AND credit."NewBinCount"::bigint=credit."OldBinCount"::bigint+credit."ChangeAmount"
 AND m."MovementType"='ReceiptPlacementReversal' AND m."InterCrewTransferId"=t."Id" AND m."BinCount"=r."Quantity" AND m."ReversesTreatmentLineageMovementId"=original."Id"
 AND m."SourceSegmentId"=target."Id" AND m."SourceRoomId"=target."RoomId" AND m."DestinationRoomId" IS NULL AND m."DestinationSegmentId" IS NULL
 AND m."CreatedAt"=r."ReversedAt" AND m."CreatedByUserId"=r."ActorId" AND m."OperationKey"=l."InventoryOperationKey"||':s'||target."Id" AND m."OccurredAt"=l."AdjustmentAt"
 AND m."ReceiptId" IS NOT DISTINCT FROM original."ReceiptId" AND original."ReceiptId" IS NOT DISTINCT FROM d."ReceiptId" AND target."ReceiptId" IS NOT DISTINCT FROM d."ReceiptId" AND source."ReceiptId" IS NOT DISTINCT FROM d."ReceiptId"
 AND m."IdentityKey"=original."IdentityKey" AND original."IdentityKey"=d."IdentityKey" AND source."IdentityKey"=d."IdentityKey" AND target."IdentityKey"=d."IdentityKey"
 AND ROW(credit."CropYear",credit."GrowerLotId",credit."FruitProfileId",credit."LotNumber",credit."VarietyCode") IS NOT DISTINCT FROM ROW(target."CropYear",target."GrowerLotId",target."FruitProfileId",target."LotNumberSnapshot",target."VarietyCodeSnapshot")
 AND m."TreatmentStateSnapshot"=original."TreatmentStateSnapshot" AND m."TreatmentSignatureSnapshot"=original."TreatmentSignatureSnapshot"
 AND source."TreatmentSignature"=d."TreatmentSignatureSnapshot" AND source."TreatmentState"=d."TreatmentStateSnapshot"
 AND NOT EXISTS (SELECT 1 FROM "TreatmentLineageSegmentApplications" sa LEFT JOIN "RoomTreatmentApplications" app ON app."Id"=sa."RoomTreatmentApplicationId"
 WHERE sa."TreatmentLineageSegmentId"=source."Id" AND NOT COALESCE(app."AppliedAt"<=d."OccurredAt" AND app."CreatedAt"<=d."CreatedAt",false))
 AND CASE WHEN d."TreatmentStateSnapshot"='Untreated' THEN cardinality(treatment.ids)=0 AND d."TreatmentSignatureSnapshot"='u'
 ELSE d."TreatmentStateSnapshot"='Confirmed' AND cardinality(treatment.ids)>0 AND d."TreatmentSignatureSnapshot"='u|a:'||array_to_string(treatment.ids,',') END
 AND NOT EXISTS (SELECT 1 FROM "RoomTreatmentApplications" app WHERE app."Id"=ANY(treatment.active) AND app."ReversedAt"<=r."ReversedAt")
 AND ARRAY(SELECT sa."RoomTreatmentApplicationId" FROM "TreatmentLineageSegmentApplications" sa WHERE sa."TreatmentLineageSegmentId"=target."Id" ORDER BY 1)=treatment.active
 AND original."TreatmentSignatureSnapshot"=CASE WHEN cardinality(treatment.active)=0 THEN 'u' ELSE 'u|a:'||array_to_string(treatment.active,',') END
 AND original."TreatmentStateSnapshot"=CASE WHEN cardinality(treatment.active)=0 THEN 'Untreated' ELSE 'Confirmed' END
 AND target."TreatmentSignature"=original."TreatmentSignatureSnapshot" AND target."TreatmentState"=original."TreatmentStateSnapshot"
 AND tc.valid AND EXISTS (SELECT 1 FROM operation_checks oc WHERE oc.key=r."OperationKey" AND oc.ledger=l."Id" AND oc.valid)
 AND EXISTS (SELECT 1 FROM operation_checks oc WHERE oc.key=p."OperationKey" AND oc.ledger=credit."Id" AND oc.valid),false) AS valid
 FROM compensation_candidates r
 LEFT JOIN "ReceiptCustodyPlacements" p ON p."Id"=r."PlacementId"
 LEFT JOIN "ReceiptCustodyAcknowledgments" a ON a."Id"=r."AcknowledgmentId"
 LEFT JOIN "InterCrewTransfers" t ON t."Id"=a."InterCrewTransferId"
 LEFT JOIN "Receipts" receipt ON receipt."Id"=t."ReceivingReceiptId"
 LEFT JOIN "RoomInventoryAdjustments" l ON l."Id"=r."InventoryAdjustmentId"
 LEFT JOIN "RoomInventoryAdjustments" credit ON credit."Id"=p."InventoryAdjustmentId"
 LEFT JOIN "TreatmentLineageMovements" m ON m."Id"=r."MovementId"
 LEFT JOIN "TreatmentLineageMovements" original ON original."Id"=p."MovementId"
 LEFT JOIN "TreatmentLineageMovements" d ON d."Id"=a."DispatchMovementId"
 LEFT JOIN "TreatmentLineageSegments" source ON source."Id"=d."SourceSegmentId"
 LEFT JOIN "TreatmentLineageSegments" target ON target."Id"=original."DestinationSegmentId"
 LEFT JOIN transfer_custody_checks tc ON tc."Id"=t."Id"
 CROSS JOIN LATERAL (SELECT ARRAY(SELECT sa."RoomTreatmentApplicationId" FROM "TreatmentLineageSegmentApplications" sa WHERE sa."TreatmentLineageSegmentId"=source."Id" ORDER BY 1) AS ids,
 ARRAY(SELECT sa."RoomTreatmentApplicationId" FROM "TreatmentLineageSegmentApplications" sa JOIN "RoomTreatmentApplications" app ON app."Id"=sa."RoomTreatmentApplicationId" WHERE sa."TreatmentLineageSegmentId"=source."Id" AND (app."ReversedAt" IS NULL OR app."ReversedAt">p."PlacedAt") ORDER BY 1) AS active) treatment
), orphan_compensation_rows AS (
 SELECT l."Id" FROM "RoomInventoryAdjustments" l WHERE l."AdjustmentType"='ReceiptPlacementReversal' AND (SELECT count(*) FROM compensation_candidates r WHERE r."InventoryAdjustmentId"=l."Id")<>1
), orphan_compensation_moves AS (
 SELECT m."Id" FROM "TreatmentLineageMovements" m WHERE m."MovementType"='ReceiptPlacementReversal' AND (SELECT count(*) FROM compensation_candidates r WHERE r."MovementId"=m."Id")<>1
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
 ('ReceiptAdd','ReceiptEdit','ReceiptAdminOverride','StartingInventoryImport','BinsRun','BinsRunReversal','Depletion','DepletionReversal','DroppedBins','DroppedBinsReversal','ProcessorShipment','ProcessorShipmentReversal','OutsideWarehouseTransfer','OutsideWarehouseTransferReversal','InterCrewTransferDispatch','InterCrewTransferReceive','InterCrewTransferReversalDestination','InterCrewTransferReversalSource','TransferIn','TransferOut','InventoryIdentityCorrection','CorrectOriginalRoomOut','CorrectOriginalRoomIn','ReceiptPlacementReversal')),
 'invalid_receipt_compensation_ids',(SELECT COALESCE(jsonb_agg("Id" ORDER BY "Id"),'[]'::jsonb) FROM compensation_checks WHERE NOT valid),
 'orphan_receipt_compensation_adjustment_ids',(SELECT COALESCE(jsonb_agg("Id" ORDER BY "Id"),'[]'::jsonb) FROM orphan_compensation_rows),
 'orphan_receipt_compensation_movement_ids',(SELECT COALESCE(jsonb_agg("Id" ORDER BY "Id"),'[]'::jsonb) FROM orphan_compensation_moves),
 'receipt_compensations_ready',NOT EXISTS (SELECT 1 FROM compensation_checks WHERE NOT valid) AND NOT EXISTS (SELECT 1 FROM orphan_compensation_rows) AND NOT EXISTS (SELECT 1 FROM orphan_compensation_moves),
 'receipt_held_bins',(SELECT COALESCE(sum(net-placed),0) FROM custody_totals),
 'invalid_location_correction_ids',(SELECT COALESCE(jsonb_agg(correction_id ORDER BY correction_id),'[]'::jsonb) FROM invalid_location_corrections),
 'orphan_location_adjustment_ids',(SELECT COALESCE(jsonb_agg("Id" ORDER BY "Id"),'[]'::jsonb) FROM orphan_location_rows),
 'location_corrections_ready',NOT EXISTS (SELECT 1 FROM invalid_location_corrections) AND NOT EXISTS (SELECT 1 FROM orphan_location_rows),
 'unbalanced_transfers',(SELECT COUNT(*) FROM transfers WHERE net<>0),
 'unbalanced_identity_corrections',(SELECT COUNT(*) FROM identities WHERE net<>0),
 'identity_net',(SELECT COALESCE(SUM(net),0) FROM identities),
 'internal_transfer_net',(SELECT COALESCE(SUM(net),0) FROM transfers),
 'treatment_identity',(SELECT to_jsonb(treatment) FROM treatment),
 'other_treatment_correction_types',(SELECT jsonb_agg(x) FROM (SELECT "MovementType",COUNT(*) AS rows,SUM("BinCount") AS bins FROM "TreatmentLineageMovements" WHERE "InventoryIdentityCorrectionId" IS NOT NULL AND "MovementType"<>'IdentityReclassification' GROUP BY 1) x),
 'intercrew_transit',(SELECT COALESCE(SUM("BinsLoaded"::bigint-COALESCE("BinsReceived",0)),0) FROM "InterCrewTransfers" WHERE "Status"='InTransit'),
 'outside_warehouse_custody',(SELECT COALESCE(SUM("BinCount"),0) FROM "OutsideWarehouseTransfers" WHERE NOT "IsReversed")
) AS report;
