\set ON_ERROR_STOP on

CREATE TEMP TABLE protected_room AS
SELECT room_row."Id", room_row."Code", room_row."Name",
       room_row."CropQcRoomName", room_row."CompuTechRoomCode", room_row."DisplayName"
FROM "Rooms" room_row
JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = room_row."WarehouseId"
WHERE upper(warehouse_row."Code") = 'EBS'
  AND (
      upper(regexp_replace(coalesce(room_row."Code", ''), '[^A-Za-z0-9]', '', 'g'))
          IN ('EVANS7', 'EVANSSTREET7', 'EVANCA07', 'EVANCA7')
      OR upper(regexp_replace(coalesce(room_row."Name", ''), '[^A-Za-z0-9]', '', 'g'))
          IN ('EVANS7', 'EVANSSTREET7', 'EVANCA07', 'EVANCA7')
      OR upper(regexp_replace(coalesce(room_row."CropQcRoomName", ''), '[^A-Za-z0-9]', '', 'g'))
          IN ('EVANS7', 'EVANSSTREET7', 'EVANCA07', 'EVANCA7')
      OR upper(regexp_replace(coalesce(room_row."CompuTechRoomCode", ''), '[^A-Za-z0-9]', '', 'g'))
          IN ('EVANS7', 'EVANSSTREET7', 'EVANCA07', 'EVANCA7')
      OR upper(regexp_replace(coalesce(room_row."DisplayName", ''), '[^A-Za-z0-9]', '', 'g'))
          IN ('EVANS7', 'EVANSSTREET7', 'EVANCA07', 'EVANCA7')
  );

BEGIN TRANSACTION READ ONLY;

DO $VERIFY_ROOM$
BEGIN
    IF (SELECT count(*) FROM protected_room) <> 1 THEN
        RAISE EXCEPTION 'Evans 7 room identity is not unique.';
    END IF;
END $VERIFY_ROOM$;

SELECT
    (SELECT "Id" FROM protected_room) AS protected_room_id,
    coalesce("CropQcRoomName", "DisplayName", "Name", "Code") AS protected_room
FROM protected_room;

SELECT
    count(*) AS non_evans7_ebs_ledger_rows,
    coalesce(sum(adjustment_row."ChangeAmount"), 0) AS non_evans7_ebs_balance,
    count(*) = 0 AS cleanup_complete
FROM "RoomInventoryAdjustments" adjustment_row
JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = adjustment_row."WarehouseId"
CROSS JOIN protected_room protected
WHERE upper(warehouse_row."Code") = 'EBS'
  AND adjustment_row."RoomId" <> protected."Id";

SELECT
    (SELECT count(*)
     FROM "Receipts" receipt_row
     WHERE receipt_row."RoomId" = protected."Id") AS evans7_receipts,
    (SELECT count(*)
     FROM "RoomInventoryAdjustments" adjustment_row
     WHERE adjustment_row."RoomId" = protected."Id") AS evans7_ledger_rows,
    (SELECT coalesce(sum(adjustment_row."ChangeAmount"), 0)
     FROM "RoomInventoryAdjustments" adjustment_row
     WHERE adjustment_row."RoomId" = protected."Id") AS evans7_ledger_balance,
    (SELECT count(DISTINCT related_lot."GrowerLotId")
     FROM (
         SELECT receipt_row."GrowerLotId"
         FROM "Receipts" receipt_row
         WHERE receipt_row."RoomId" = protected."Id"
         UNION
         SELECT adjustment_row."GrowerLotId"
         FROM "RoomInventoryAdjustments" adjustment_row
         WHERE adjustment_row."RoomId" = protected."Id"
     ) related_lot
     WHERE related_lot."GrowerLotId" IS NOT NULL) AS evans7_grower_lots
FROM protected_room protected;

SELECT
    count(*) AS wp_ledger_rows,
    coalesce(sum(adjustment_row."ChangeAmount"), 0) AS wp_ledger_balance
FROM "RoomInventoryAdjustments" adjustment_row
JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = adjustment_row."WarehouseId"
WHERE upper(warehouse_row."Code") = 'WP';

SELECT "Id", "UserId", "BeforeValuesJson", "AfterValuesJson", "CreatedAt"
FROM "AuditLogs"
WHERE "Action" = 'RemoveEbsTestInventory'
  AND "EntityKey" = 'EBS-outside-Evans-7'
ORDER BY "Id" DESC
LIMIT 1;

ROLLBACK;
