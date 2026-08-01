\set ON_ERROR_STOP on

BEGIN TRANSACTION READ ONLY;

DO $VERIFY$
DECLARE
    audit_count integer;
BEGIN
    IF (SELECT count(*) FROM "RoomInventoryAdjustments"
        WHERE "Id" = 52 AND "ReceiptId" = 37 AND "WarehouseId" = 2 AND "RoomId" = 33
          AND "GrowerLotId" = 216 AND "FruitProfileId" = 22 AND "CropYear" = 2025
          AND "ChangeAmount" = 0 AND "NewBinCount" = 0 AND "AdjustmentType" = 'ReceiptAdd'
          AND "Reason" = 'Authorized correction: deleted fake receipt contributes no current DH Room 1 Autumn Glory inventory.'
          AND "Notes" LIKE '%Authorized correction: deleted fake receipt contributes no current DH Room 1 Autumn Glory inventory.') <> 1 THEN
        RAISE EXCEPTION 'The Autumn Glory DH Room 1 adjustment is not in the reviewed corrected state.';
    END IF;
    IF (SELECT count(*) FROM "Receipts" WHERE "Id" = 37 AND "IsDeleted" AND "DeleteReason" = 'Fake' AND "BinCount" = 1) <> 1 THEN
        RAISE EXCEPTION 'The preserved deleted receipt changed.';
    END IF;
    IF (SELECT coalesce(sum("ChangeAmount"), 0) FROM "RoomInventoryAdjustments"
        WHERE "RoomId" = 33 AND "FruitProfileId" = 22) <> 0 THEN
        RAISE EXCEPTION 'Autumn Glory DH Room 1 is not zero.';
    END IF;
    IF (SELECT coalesce(sum("ChangeAmount"), 0) FROM "RoomInventoryAdjustments"
        WHERE "Id" IN (35,36,37,54,55,66,67,68)) <> 0 THEN
        RAISE EXCEPTION 'Fuji Evans 12 physical ledger is not zero.';
    END IF;
    SELECT count(*) INTO audit_count FROM "AuditLogs"
    WHERE "Action" = 'ApplyFujiEvans12AutumnGloryDh1Correction'
      AND "EntityName" = 'RoomInventoryCorrection'
      AND "EntityKey" = 'FUJI-EVANS12-ATGL-DH1-20260731';
    IF audit_count <> 1 THEN
        RAISE EXCEPTION 'Expected exactly one correction audit marker; found %.', audit_count;
    END IF;
END $VERIFY$;

SELECT room_row."Id" AS room_id,
       warehouse_row."Code" AS facility,
       coalesce(room_row."CropQcRoomName", room_row."DisplayName", room_row."Name", room_row."Code") AS room,
       fruit_profile."Name" AS variety,
       coalesce(sum(adjustment_row."ChangeAmount"), 0) AS balance
FROM "RoomInventoryAdjustments" adjustment_row
JOIN "Rooms" room_row ON room_row."Id" = adjustment_row."RoomId"
JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = adjustment_row."WarehouseId"
JOIN "FruitProfiles" fruit_profile ON fruit_profile."Id" = adjustment_row."FruitProfileId"
WHERE (adjustment_row."RoomId" = 22 AND adjustment_row."FruitProfileId" = 1)
   OR (adjustment_row."RoomId" = 33 AND adjustment_row."FruitProfileId" = 22)
GROUP BY room_row."Id", warehouse_row."Code", room, fruit_profile."Name"
ORDER BY room_row."Id";

SELECT "Id", "UserId", "Action", "EntityKey", "BeforeValuesJson", "AfterValuesJson", "CreatedAt"
FROM "AuditLogs"
WHERE "Action" = 'ApplyFujiEvans12AutumnGloryDh1Correction'
  AND "EntityKey" = 'FUJI-EVANS12-ATGL-DH1-20260731';

ROLLBACK;
