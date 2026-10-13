-- Read-only investigation; workspace tea-d7uc4ippo60c73ebn4mg,
-- database dpg-d8cru6reo5us73a2bgt0-a. Execute each SELECT through the
-- read-only Render query tool, or within BEGIN TRANSACTION READ ONLY.
-- Snapshot/results/limitations: lamb15-authoritative-admission.md.

SELECT r."Id",r."Code",r."Name",r."CompuTechRoomCode",r."WarehouseId",
       w."Code" AS "Warehouse",r."IsActive",r."IsSealed",r."CapacityBins"
FROM "Rooms" r JOIN "Warehouses" w ON w."Id"=r."WarehouseId"
WHERE r."Id" IN (8,23) ORDER BY r."Id";

SELECT "LotNumber","GrowerLotId","AdjustmentType",COUNT(*) AS "Rows",
       SUM("ChangeAmount") AS "Quantity",MIN("AdjustmentAt") AS "FirstEffective",
       MAX("CreatedAt") AS "LastRecorded"
FROM "RoomInventoryAdjustments"
WHERE "RoomId"=8 AND "FruitProfileId"=1
GROUP BY "LotNumber","GrowerLotId","AdjustmentType"
ORDER BY "LotNumber","AdjustmentType";

SELECT "Id","GrowerLotId","LotNumber","ChangeAmount","AdjustmentType",
       "ReceiptId","RoomTransferId","RoomInventoryLossId","ReceiptInventoryOverrideId",
       "AdjustmentAt","CreatedAt","InventoryOperationKey"
FROM "RoomInventoryAdjustments"
WHERE "RoomId"=8 AND "FruitProfileId"=1
ORDER BY "CreatedAt","Id";

-- Shape census only. This does not establish complete origin/treatment/custody
-- evidence, room eligibility, or repair authorization for any returned row.
SELECT r."Code",s."RoomId",s."Id",s."FruitProfileId",s."LotNumberSnapshot",
       s."VarietyCodeSnapshot",s."IdentityKey",s."CohortKey",s."ReceiptId",
       s."CurrentBins",s."TreatmentSignature",s."TreatmentState"
FROM "TreatmentLineageSegments" s JOIN "Rooms" r ON r."Id"=s."RoomId"
WHERE s."Disposition"='Current' AND s."CurrentBins"=0
  AND split_part(s."IdentityKey",'|',9)<>''
  AND upper(split_part(s."IdentityKey",'|',9))=upper(s."ProductionTypeSnapshot")
ORDER BY s."RoomId",s."Id";

SELECT "Id","BackupType","Status","DeployedCommit","StartedAt","CompletedAt",
       "PackageFileName","FileSizeBytes","Sha256","VerifiedAt","LeaseReleasedAt","FailureStage"
FROM "BackupRunRecords" ORDER BY "Id" DESC LIMIT 3;

SELECT "Id","LeaseId","ExpiresAt" FROM "BackupOperationLeases";
