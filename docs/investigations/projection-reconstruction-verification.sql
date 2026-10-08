-- Read-only evidence inspection. Never a repair script.
-- Quantities in the report come from InventoryEvidenceLoader/canonical ledger
-- semantics; do not substitute a raw row sum or a displayed room balance.
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;

SELECT "Id", "WarehouseId", "RoomId", "CropYear", "GrowerLotId", "FruitProfileId",
       "IdentityKey", "CurrentBins", "ConcurrencyVersion", "ReceiptId",
       "TreatmentState", "TreatmentSignature", "Disposition", "RetiredQuantity",
       "RetiredAt", "RetiredByCommandKey", "CreatedAt", "UpdatedAt"
FROM "TreatmentLineageSegments"
WHERE "Id" IN (414,426,419,428,617,618,619,630,669,670,407,557,65,597,552,553,503)
ORDER BY "Id";

WITH targets(room_id, lot_id, profile_id) AS (
 VALUES (15,511,2),(15,642,2),(4,448,17),(47,495,18),(2,398,2),(5,495,18)
)
SELECT a."Id", a."RoomId", a."GrowerLotId", a."FruitProfileId", a."ChangeAmount",
       a."NewBinCount", a."AdjustmentType", a."AdjustmentAt", a."CreatedAt",
       a."ReceiptId", a."ReceiptInventoryOverrideId", a."RoomTransferId", a."InterCrewTransferId"
FROM "RoomInventoryAdjustments" a JOIN targets t
 ON (a."RoomId",a."GrowerLotId",a."FruitProfileId")=(t.room_id,t.lot_id,t.profile_id)
ORDER BY a."RoomId",a."GrowerLotId",a."AdjustmentAt",a."Id";

SELECT "Id", "ReceiptId", "ActionType", "OldReceiptBinCount", "NewReceiptBinCount",
       "InventoryDelta", "ExpectedAdjustmentCount", "IsComplete", "CreatedAt",
       "OperationKey", "BeforeReceiptSnapshotJson", "AfterReceiptSnapshotJson"
FROM "ReceiptInventoryOverrides" WHERE "ReceiptId" IN (1373,1339,1754,626,720)
ORDER BY "ReceiptId","CreatedAt","Id";

SELECT "Id", "MovementType", "BinCount", "SourceSegmentId", "DestinationSegmentId",
       "SourceRoomId", "DestinationRoomId", "IdentityKey", "ReceiptId",
       "TreatmentSignatureSnapshot", "TreatmentStateSnapshot", "OccurredAt", "CreatedAt"
FROM "TreatmentLineageMovements"
WHERE "SourceSegmentId" IN (414,426,419,428,617,618,619,630,669,670,407,557,65,597,552,553,503)
   OR "DestinationSegmentId" IN (414,426,419,428,617,618,619,630,669,670,407,557,65,597,552,553,503)
ORDER BY "Id";

SELECT "Id", "Action", "EntityName", "EntityKey", "CreatedAt",
       md5(coalesce("BeforeValuesJson",'')) AS before_hash,
       md5(coalesce("AfterValuesJson",'')) AS after_hash
FROM "AuditLogs"
WHERE "Id" IN (123203,120803,126585,55448,145038,57218,112768)
   OR ("EntityName"='ProjectionReconstruction' AND "SourceApplication"='CanonicalProjectionReconstruction/v1')
ORDER BY "Id";

SELECT "Id", "RoomId", "AppliedAt", "ReversedAt" FROM "RoomTreatmentApplications" WHERE "Id"=1;
SELECT * FROM "RoomTreatmentApplicationSources" WHERE "Id"=4;
SELECT * FROM "TreatmentLineageSegmentApplications" WHERE "TreatmentLineageSegmentId"=503;
ROLLBACK;
