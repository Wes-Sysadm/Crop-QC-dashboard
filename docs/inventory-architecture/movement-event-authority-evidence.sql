-- Read-only investigation queries. Observation date: 2026-10-09 UTC.
-- Render workspace: tea-d7uc4ippo60c73ebn4mg.
-- These select evidence; they are not reconstruction/repair commands.
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;

SELECT CURRENT_TIMESTAMP AS observed_at, current_setting('transaction_read_only') AS read_only;

SELECT "Id", "RoomId", "CropYear", "GrowerLotId", "FruitProfileId",
       "LotNumber", "ChangeAmount", "AdjustmentType", "ReceiptId",
       "RoomTransferId", "InterCrewTransferId", "ReceiptInventoryOverrideId",
       "InventoryIdentityCorrectionId", "AdjustmentAt", "CreatedAt"
FROM "RoomInventoryAdjustments"
WHERE ("RoomId" IN (55,73) AND "GrowerLotId" IN (430,187))
   OR "RoomTransferId" = 411 OR "ReceiptId" = 2262
ORDER BY "CreatedAt", "Id";

SELECT "Id", "CompuTechReceiptId", "RoomId", "WarehouseId", "GrowerLotId",
       "FruitProfileId", "BinCount", "IsDeleted", "IsTransferReceipt",
       "ReceivedAt", "CreatedAt", "UpdatedAt"
FROM "Receipts"
WHERE "Id" IN (2252,2262,2266,2294,2334,2343,2364,2378,2384,2386,2419,2439,2472,2533)
ORDER BY "Id";

SELECT "Id", "RoomId", "IdentityKey", "ReceiptId", "CurrentBins",
       "TreatmentSignature", "TreatmentState", "Disposition", "CreatedAt", "UpdatedAt"
FROM "TreatmentLineageSegments"
WHERE "RoomId" IN (55,73) AND "GrowerLotId" IN (430,187)
ORDER BY "Id";

SELECT "Id", "ReceiptId", "IdentityKey", "BinCount", "MovementType",
       "SourceRoomId", "DestinationRoomId", "SourceSegmentId", "DestinationSegmentId",
       "TreatmentSignatureSnapshot", "TreatmentStateSnapshot", "RoomTransferId",
       "ReversesTreatmentLineageMovementId", "OccurredAt", "CreatedAt"
FROM "TreatmentLineageMovements"
WHERE ("SourceRoomId" IN (55,73) OR "DestinationRoomId" IN (55,73))
  AND split_part("IdentityKey", '|', 2) IN ('430','187')
ORDER BY "CreatedAt", "Id";

SELECT a."Id" AS application_id, a."ReceiptId", a."RoomId", a."AppliedAt",
       a."CreatedAt", a."ReversedAt", s."Id" AS allocation_id,
       s."ReceiptId" AS allocation_receipt, s."IdentityKey", s."BinsTreated",
       s."PriorTreatmentSignature", s."ResultTreatmentSignature"
FROM "RoomTreatmentApplications" a
LEFT JOIN "RoomTreatmentApplicationSources" s ON s."RoomTreatmentApplicationId" = a."Id"
WHERE a."Id" IN (28,29)
ORDER BY a."Id", s."Id";

ROLLBACK;
