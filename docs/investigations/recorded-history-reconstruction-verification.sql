-- Read-only evidence for the three remaining pools plus original source DH-4C.
-- Execute against a verified isolated restore or an explicitly authorized read-only connection.
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SELECT current_database() AS database, current_timestamp AS observed_at,
       current_setting('transaction_read_only') AS read_only;

WITH targets(room, lot, profile) AS (VALUES (47,495,18),(2,398,2),(5,495,18),(36,495,18))
SELECT a."Id", a."RoomId", a."CreatedAt" AS recorded_at, a."AdjustmentAt" AS effective_at,
       a."AdjustmentType", a."ChangeAmount", a."ReceiptId", a."RoomTransferId",
       a."ReceiptInventoryOverrideId", a."ActualRunId", a."ActualRunRevisionId",
       a."RoomInventoryLossId", a."RoomDepletionId",
       sum(a."ChangeAmount") OVER (PARTITION BY a."RoomId" ORDER BY a."CreatedAt", a."Id") AS recorded_balance,
       sum(a."ChangeAmount") OVER (PARTITION BY a."RoomId" ORDER BY a."AdjustmentAt", a."CreatedAt", a."Id") AS effective_balance
FROM "RoomInventoryAdjustments" a JOIN targets t ON a."RoomId"=t.room AND a."GrowerLotId"=t.lot AND a."FruitProfileId"=t.profile
WHERE a."CropYear"=2026 ORDER BY a."RoomId",a."CreatedAt",a."Id";

WITH targets(room, lot, profile) AS (VALUES (47,495,18),(2,398,2),(5,495,18),(36,495,18))
SELECT s."Id",s."RoomId",s."GrowerLotId",s."CurrentBins",s."ConcurrencyVersion",s."IdentityKey",
       s."ReceiptId",s."TreatmentState",s."TreatmentSignature",s."Disposition",s."RetiredQuantity",
       s."CreatedAt",s."UpdatedAt"
FROM "TreatmentLineageSegments" s JOIN targets t ON s."RoomId"=t.room AND s."GrowerLotId"=t.lot AND s."FruitProfileId"=t.profile
WHERE s."CropYear"=2026 ORDER BY s."RoomId",s."Id";

SELECT "Id","ReceiptId","ActionType","IsComplete","OperationKey","OldReceiptBinCount","NewReceiptBinCount",
       "InventoryDelta","ExpectedAdjustmentCount","BeforeReceiptSnapshotJson","AfterReceiptSnapshotJson","CreatedAt"
FROM "ReceiptInventoryOverrides" WHERE "ReceiptId" IN (1373,1339,1754) ORDER BY "CreatedAt","Id";
SELECT "Id","EntityKey","Action","CreatedAt","BeforeValuesJson","AfterValuesJson"
FROM "AuditLogs" WHERE "Id" IN (123203,120803,126585) ORDER BY "Id";

SELECT "Id","CompuTechReceiptId","RoomId","GrowerLotId","FruitProfileId","ReceivedAt","CreatedAt","UpdatedAt",
       "BinCount","IsDeleted","ConcurrencyVersion"
FROM "Receipts" WHERE "Id" IN (
 SELECT "ReceiptId" FROM "RoomInventoryAdjustments" WHERE "CropYear"=2026
 AND (("RoomId" IN (36,47,5) AND "GrowerLotId"=495 AND "FruitProfileId"=18)
 OR ("RoomId"=2 AND "GrowerLotId"=398 AND "FruitProfileId"=2))) ORDER BY "Id";

-- Independent parent search: these exact pools have no such events, including
-- no orphan parent hidden by a missing ledger link. A nonzero result is a blocker.
WITH targets(room,lot,profile) AS (VALUES (47,495,18),(2,398,2),(5,495,18),(36,495,18))
SELECT 'ReceiptWithoutArrival' AS kind, r."Id" FROM "Receipts" r JOIN targets t
 ON r."RoomId"=t.room AND r."GrowerLotId"=t.lot AND r."FruitProfileId"=t.profile
 WHERE r."CropYear"=2026 AND NOT r."IsTransferReceipt" AND NOT EXISTS
 (SELECT 1 FROM "RoomInventoryAdjustments" a WHERE a."ReceiptId"=r."Id" AND a."AdjustmentType"='ReceiptAdd')
UNION ALL
SELECT 'RoomInventoryLoss', x."Id" FROM "RoomInventoryLosses" x JOIN targets t
 ON x."RoomId"=t.room AND x."GrowerLotId"=t.lot AND x."FruitProfileId"=t.profile WHERE x."CropYear"=2026
UNION ALL
SELECT 'RoomDepletion', x."Id" FROM "RoomDepletions" x JOIN "Receipts" r ON x."ReceiptId"=r."Id" JOIN targets t
 ON r."RoomId"=t.room AND r."GrowerLotId"=t.lot AND r."FruitProfileId"=t.profile WHERE r."CropYear"=2026
UNION ALL
SELECT 'InterCrewTransfer', x."Id" FROM "InterCrewTransfers" x JOIN targets t
 ON (x."SourceRoomId"=t.room OR x."DestinationRoomId"=t.room) AND x."GrowerLotId"=t.lot AND x."FruitProfileId"=t.profile WHERE x."CropYear"=2026
UNION ALL
SELECT 'OutsideWarehouseTransfer', x."Id" FROM "OutsideWarehouseTransfers" x JOIN targets t
 ON x."SourceRoomId"=t.room AND x."GrowerLotId"=t.lot AND x."FruitProfileId"=t.profile WHERE x."CropYear"=2026
UNION ALL
SELECT 'ProcessorShipmentLine', x."Id" FROM "ProcessorShipmentLines" x JOIN targets t
 ON x."RoomId"=t.room AND x."GrowerLotId"=t.lot AND x."FruitProfileId"=t.profile WHERE x."CropYear"=2026;

SELECT * FROM "RoomTransfers" WHERE "Id" IN (335,369,370) ORDER BY "Id";
SELECT * FROM "BinsRunEntries" WHERE "Id" IN (39,53,125,221,357,313,376,382) ORDER BY "Id";
SELECT * FROM "ActualRunRevisions" WHERE "ActualRunId" IN (7,14,38,67,102,91,110,115) ORDER BY "ActualRunId","RevisionNumber";
SELECT "Id","Status","CurrentRevisionNumber","RunAt","CreatedAt" FROM "ActualRuns"
WHERE "Id" IN (7,14,38,67,102,91,110,115) ORDER BY "Id";
SELECT * FROM "TreatmentLineageMovements" WHERE "Id" IN (31,299,573,342,506,516,507,622,628) ORDER BY "Id";

SELECT "Id","RoomId","ReceiptId","AppliedAt","CreatedAt","ReversedAt" FROM "RoomTreatmentApplications"
WHERE "RoomId" IN (2,5,36,47) OR "ReceiptId" IN (
 SELECT "ReceiptId" FROM "RoomInventoryAdjustments" WHERE "CropYear"=2026
 AND (("RoomId" IN (36,47,5) AND "GrowerLotId"=495 AND "FruitProfileId"=18)
 OR ("RoomId"=2 AND "GrowerLotId"=398 AND "FruitProfileId"=2))) ORDER BY "Id";
ROLLBACK;
