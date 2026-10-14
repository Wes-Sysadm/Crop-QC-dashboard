BEGIN ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT "Id", "Status", "BinsLoaded", "BinsReceived", "DestinationWarehouseId", "DestinationRoomId", "ReceivingReceiptId", "ConcurrencyVersion"
FROM "InterCrewTransfers"
WHERE "BinsLoaded" <= 0 OR ("Status"='InTransit' AND ("DestinationWarehouseId" IS NOT NULL OR "DestinationRoomId" IS NOT NULL OR "BinsReceived" IS NOT NULL)) OR ("Status" IN ('Received','ReceivedNeedsReview') AND ("DestinationWarehouseId" IS NULL OR "DestinationRoomId" IS NULL OR "BinsReceived" IS NULL));
SELECT a."Id", a."DispatchMovementId", a."Quantity", a."Quantity"-COALESCE((SELECT sum(r."Quantity") FROM "ReceiptCustodyReversals" r WHERE r."AcknowledgmentId"=a."Id" AND r."PlacementId" IS NULL),0) AS "NetAcknowledged",
COALESCE((SELECT sum(p."Quantity") FROM "ReceiptCustodyPlacements" p WHERE p."AcknowledgmentId"=a."Id"),0)-COALESCE((SELECT sum(r."Quantity") FROM "ReceiptCustodyReversals" r WHERE r."AcknowledgmentId"=a."Id" AND r."PlacementId" IS NOT NULL),0) AS "NetPlaced"
FROM "ReceiptCustodyAcknowledgments" a WHERE a."InterCrewTransferId"=43 ORDER BY a."Id";
SELECT "Id", "MovementType", "BinCount", "SourceSegmentId", "DestinationSegmentId", "ReceiptId", "TreatmentSignatureSnapshot", "TreatmentStateSnapshot", "ReversesTreatmentLineageMovementId", "OperationKey", "OccurredAt", "CreatedAt"
FROM "TreatmentLineageMovements" WHERE "InterCrewTransferId"=43 ORDER BY "Id";
SELECT "RoomId", sum("ChangeAmount") AS "AuthoritativeRoomBins"
FROM "RoomInventoryAdjustments" WHERE "GrowerLotId"=990001 GROUP BY "RoomId" ORDER BY "RoomId";
SELECT "ReceiptId", "TreatmentSignature", "RoomId", sum("CurrentBins") AS "ProjectionBins"
FROM "TreatmentLineageSegments" WHERE "GrowerLotId"=990001 AND "Disposition"='Current'
GROUP BY "ReceiptId", "TreatmentSignature", "RoomId" ORDER BY 1,3;
SELECT row_to_json(s) FROM "RoomTreatmentApplicationSources" s WHERE "RoomTreatmentApplicationId"=30;
ROLLBACK;
