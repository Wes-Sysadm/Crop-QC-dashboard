\set ON_ERROR_STOP on

DO $PREFLIGHT$
DECLARE
    ebs_count integer;
    evans7_count integer;
BEGIN
    SELECT count(*) INTO ebs_count
    FROM "Warehouses"
    WHERE upper("Code") = 'EBS';

    IF ebs_count <> 1 THEN
        RAISE EXCEPTION 'Expected exactly one EBS warehouse; found %.', ebs_count;
    END IF;

    SELECT count(*) INTO evans7_count
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

    IF evans7_count <> 1 THEN
        RAISE EXCEPTION 'Expected exactly one EBS Evans 7 room identity; found %. Cleanup is disabled.', evans7_count;
    END IF;
END $PREFLIGHT$;

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

SELECT 'PROTECTED_EVANS_7' AS category, * FROM protected_room;

SELECT
    'EVANS_7_BEFORE' AS category,
    (SELECT count(*)
     FROM "Receipts" receipt_row
     WHERE receipt_row."RoomId" = protected."Id") AS receipt_count,
    (SELECT count(*)
     FROM "RoomInventoryAdjustments" adjustment_row
     WHERE adjustment_row."RoomId" = protected."Id") AS ledger_row_count,
    (SELECT coalesce(sum(adjustment_row."ChangeAmount"), 0)
     FROM "RoomInventoryAdjustments" adjustment_row
     WHERE adjustment_row."RoomId" = protected."Id") AS ledger_change_sum
FROM protected_room protected;

SELECT
    'CLEANUP_CANDIDATE' AS category,
    adjustment_row."Id" AS adjustment_id,
    room_row."Id" AS room_id,
    coalesce(room_row."CropQcRoomName", room_row."DisplayName", room_row."Name", room_row."Code") AS room,
    adjustment_row."CropYear" AS crop_year,
    adjustment_row."GrowerName" AS grower,
    adjustment_row."LotNumber" AS lot,
    adjustment_row."VarietyCode" AS variety,
    adjustment_row."AdjustmentType" AS adjustment_type,
    adjustment_row."ChangeAmount" AS change_amount,
    adjustment_row."NewBinCount" AS recorded_new_bins,
    adjustment_row."AdjustmentAt" AS adjustment_at
FROM "RoomInventoryAdjustments" adjustment_row
JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = adjustment_row."WarehouseId"
JOIN "Rooms" room_row ON room_row."Id" = adjustment_row."RoomId"
CROSS JOIN protected_room protected
WHERE upper(warehouse_row."Code") = 'EBS'
  AND adjustment_row."RoomId" <> protected."Id"
ORDER BY room, adjustment_row."LotNumber", adjustment_row."Id";

SELECT
    'CANDIDATE_TOTALS' AS category,
    count(*) AS ledger_rows,
    coalesce(sum(adjustment_row."ChangeAmount"), 0) AS current_balance_to_remove
FROM "RoomInventoryAdjustments" adjustment_row
JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = adjustment_row."WarehouseId"
CROSS JOIN protected_room protected
WHERE upper(warehouse_row."Code") = 'EBS'
  AND adjustment_row."RoomId" <> protected."Id";

SELECT
    'BLOCKING_OPERATIONAL_LINKS' AS category,
    count(DISTINCT bins_row."Id") AS bins_run_entries,
    count(DISTINCT adjustment_row."RoomTransferId") FILTER (WHERE adjustment_row."RoomTransferId" IS NOT NULL) AS transfers,
    count(DISTINCT adjustment_row."ActualRunId") FILTER (WHERE adjustment_row."ActualRunId" IS NOT NULL) AS actual_runs,
    count(DISTINCT adjustment_row."RoomDepletionId") FILTER (WHERE adjustment_row."RoomDepletionId" IS NOT NULL) AS room_depletions
FROM "RoomInventoryAdjustments" adjustment_row
JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = adjustment_row."WarehouseId"
CROSS JOIN protected_room protected
LEFT JOIN "BinsRunEntries" bins_row
    ON bins_row."InventoryAdjustmentId" = adjustment_row."Id"
    OR bins_row."SourceInventoryAdjustmentId" = adjustment_row."Id"
WHERE upper(warehouse_row."Code") = 'EBS'
  AND adjustment_row."RoomId" <> protected."Id";

SELECT
    'WP_UNTOUCHED_BASELINE' AS category,
    count(*) AS ledger_rows,
    coalesce(sum(adjustment_row."ChangeAmount"), 0) AS current_balance
FROM "RoomInventoryAdjustments" adjustment_row
JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = adjustment_row."WarehouseId"
WHERE upper(warehouse_row."Code") = 'WP';

ROLLBACK;
