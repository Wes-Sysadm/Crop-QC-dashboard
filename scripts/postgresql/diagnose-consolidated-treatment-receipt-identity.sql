-- Read-only candidate scan for Receipt identity corrections whose exact current
-- Receipt balance is represented by a larger consolidated treatment segment.
-- Review each row with ReceiptInventoryProvenanceResolver before any correction.
WITH receipt_balances AS (
    SELECT r."Id" AS "ReceiptId",
           r."CompuTechReceiptId",
           r."WarehouseId",
           r."RoomId",
           r."CropYear",
           r."GrowerLotId",
           r."FruitProfileId",
           SUM(a."ChangeAmount") AS "ReceiptCurrentBins"
    FROM "Receipts" r
    JOIN "RoomInventoryAdjustments" a ON a."ReceiptId" = r."Id"
    WHERE NOT r."IsDeleted"
    GROUP BY r."Id", r."CompuTechReceiptId", r."WarehouseId", r."RoomId",
             r."CropYear", r."GrowerLotId", r."FruitProfileId"
    HAVING SUM(a."ChangeAmount") > 0
)
SELECT rb."ReceiptId",
       rb."CompuTechReceiptId",
       w."Code" AS "Facility",
       rm."Code" AS "Room",
       rb."ReceiptCurrentBins",
       s."Id" AS "ConsolidatedSegmentId",
       s."CurrentBins" AS "ConsolidatedSegmentBins",
       s."TreatmentState",
       s."TreatmentSignature"
FROM receipt_balances rb
JOIN "TreatmentLineageSegments" s
  ON s."WarehouseId" = rb."WarehouseId"
 AND s."RoomId" = rb."RoomId"
 AND s."CropYear" = rb."CropYear"
 AND s."GrowerLotId" = rb."GrowerLotId"
 AND s."FruitProfileId" = rb."FruitProfileId"
 AND s."ReceiptId" IS NULL
 AND s."CurrentBins" > rb."ReceiptCurrentBins"
JOIN "Warehouses" w ON w."Id" = rb."WarehouseId"
JOIN "Rooms" rm ON rm."Id" = rb."RoomId"
ORDER BY w."Code", rm."Code", rb."ReceiptId", s."Id";
