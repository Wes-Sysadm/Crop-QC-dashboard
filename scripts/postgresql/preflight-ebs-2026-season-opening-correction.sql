\set ON_ERROR_STOP on

BEGIN TRANSACTION READ ONLY;

DO $PREFLIGHT$
DECLARE
    ebs_count integer;
    protected_count integer;
    boundary_count integer;
    boundary_room_id integer;
    protected_room_id integer;
    boundary_variety text;
    candidate_count integer;
    candidate_balance integer;
    unclear_count integer;
    unexpected_ids integer;
    missing_ids integer;
BEGIN
    WITH protected_room AS (
        SELECT room_row."Id"
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
                  IN ('EVANS7', 'EVANSSTREET7', 'EVANCA07', 'EVANCA7'))
    ),
    season_boundary AS (
        SELECT receipt_row."Id" AS receipt_id,
               receipt_row."ReceivedAt" AS received_at_utc,
               receipt_row."RoomId" AS room_id,
               receipt_row."FruitProfileId" AS fruit_profile_id
        FROM "Receipts" receipt_row
        JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = receipt_row."WarehouseId"
        WHERE upper(warehouse_row."Code") = 'EBS'
          AND receipt_row."CropYear" = 2026
          AND NOT receipt_row."IsDeleted"
          AND NOT receipt_row."IsTestData"
        ORDER BY receipt_row."ReceivedAt", receipt_row."Id"
        LIMIT 1
    ),
    classified_rows AS (
        SELECT adjustment_row."Id" AS adjustment_id,
               adjustment_row."ChangeAmount" AS quantity,
               CASE
                   WHEN adjustment_row."AdjustmentAt" >= boundary.received_at_utc THEN 5
                   WHEN room_row."Id" IN (27, 32, 22, 15, 10) THEN 2
                   WHEN adjustment_row."Id" = 1 THEN 1
                   WHEN adjustment_row."Id" = 8 THEN 3
                   WHEN room_row."Id" = 11 THEN 2
                   WHEN adjustment_row."Id" IN (22, 23, 25, 26, 76, 77, 78, 79) THEN 4
                   WHEN room_row."Id" = 7 THEN 2
                   ELSE 6
               END AS classification_number
        FROM "RoomInventoryAdjustments" adjustment_row
        JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = adjustment_row."WarehouseId"
        JOIN "Rooms" room_row ON room_row."Id" = adjustment_row."RoomId"
        CROSS JOIN protected_room protected
        CROSS JOIN season_boundary boundary
        WHERE upper(warehouse_row."Code") = 'EBS'
          AND adjustment_row."RoomId" <> protected."Id"
    )
    SELECT (SELECT count(*) FROM "Warehouses" WHERE upper("Code") = 'EBS'),
           (SELECT count(*) FROM protected_room),
           (SELECT count(*) FROM season_boundary),
           (SELECT room_id FROM season_boundary),
           (SELECT "Id" FROM protected_room),
           (SELECT fruit_profile."VarietyCode"
            FROM season_boundary boundary
            JOIN "FruitProfiles" fruit_profile ON fruit_profile."Id" = boundary.fruit_profile_id),
           (SELECT count(*) FROM classified_rows),
           (SELECT coalesce(sum(quantity), 0) FROM classified_rows),
           (SELECT count(*) FROM classified_rows WHERE classification_number = 6),
           (SELECT count(*) FROM classified_rows WHERE adjustment_id <> ALL (ARRAY[
                1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,
                31,32,33,34,35,36,37,38,39,40,41,42,43,44,45,46,47,48,49,50,51,53,54,55,56,57,58,59,
                60,61,62,63,64,65,66,67,68,69,70,71,72,73,74,75,76,77,78,79,80]::bigint[])),
           (SELECT count(*)
            FROM unnest(ARRAY[
                1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,
                31,32,33,34,35,36,37,38,39,40,41,42,43,44,45,46,47,48,49,50,51,53,54,55,56,57,58,59,
                60,61,62,63,64,65,66,67,68,69,70,71,72,73,74,75,76,77,78,79,80]::bigint[]) expected(id)
            WHERE NOT EXISTS (SELECT 1 FROM classified_rows classified WHERE classified.adjustment_id = expected.id))
    INTO ebs_count, protected_count, boundary_count, boundary_room_id, protected_room_id,
         boundary_variety, candidate_count, candidate_balance, unclear_count, unexpected_ids, missing_ids;

    IF ebs_count <> 1 OR protected_count <> 1 THEN
        RAISE EXCEPTION 'Expected exactly one EBS warehouse and one persisted Evans 7 room; found % and %.', ebs_count, protected_count;
    END IF;
    IF boundary_count <> 1 THEN
        RAISE EXCEPTION 'Could not resolve exactly one first legitimate 2026 EBS receipt.';
    END IF;
    IF boundary_room_id <> protected_room_id OR upper(boundary_variety) <> 'GALA' THEN
        RAISE EXCEPTION 'The first legitimate 2026 EBS receipt is not the protected Evans 7 Gala receipt. Stop for review.';
    END IF;
    IF candidate_count <> 79 OR candidate_balance <> 583 THEN
        RAISE EXCEPTION 'Production evidence drifted: expected 79 rows / 583 bins, found % rows / % bins.', candidate_count, candidate_balance;
    END IF;
    IF unclear_count <> 0 THEN
        RAISE EXCEPTION 'Classification contains % unclear rows. Stop and ask Wes.', unclear_count;
    END IF;
    IF unexpected_ids <> 0 OR missing_ids <> 0 THEN
        RAISE EXCEPTION 'The reviewed 79-row production fingerprint changed (unexpected %, missing %).', unexpected_ids, missing_ids;
    END IF;
END $PREFLIGHT$;

WITH protected_room AS (
    SELECT room_row."Id"
    FROM "Rooms" room_row
    JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = room_row."WarehouseId"
    WHERE upper(warehouse_row."Code") = 'EBS'
      AND upper(regexp_replace(concat_ws(' ', room_row."Code", room_row."Name", room_row."CropQcRoomName",
          room_row."CompuTechRoomCode", room_row."DisplayName"), '[^A-Za-z0-9]', '', 'g'))
          LIKE ANY (ARRAY['%EVANS7%', '%EVANSSTREET7%', '%EVANCA07%', '%EVANCA7%'])
),
season_boundary AS (
    SELECT receipt_row.*
    FROM "Receipts" receipt_row
    JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = receipt_row."WarehouseId"
    WHERE upper(warehouse_row."Code") = 'EBS' AND receipt_row."CropYear" = 2026
      AND NOT receipt_row."IsDeleted" AND NOT receipt_row."IsTestData"
    ORDER BY receipt_row."ReceivedAt", receipt_row."Id" LIMIT 1
)
SELECT boundary."Id" AS boundary_receipt_id,
       boundary."ReceivedAt" AS boundary_utc,
       timezone('America/Los_Angeles', boundary."ReceivedAt") AS boundary_pacific,
       boundary."RoomId", fruit_profile."VarietyCode", boundary."GrowerLotId", boundary."BinCount"
FROM season_boundary boundary
JOIN protected_room protected ON protected."Id" = boundary."RoomId"
JOIN "FruitProfiles" fruit_profile ON fruit_profile."Id" = boundary."FruitProfileId";

WITH protected_room AS (
    SELECT room_row."Id" FROM "Rooms" room_row
    JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = room_row."WarehouseId"
    WHERE upper(warehouse_row."Code") = 'EBS'
      AND (upper(regexp_replace(coalesce(room_row."Code", ''), '[^A-Za-z0-9]', '', 'g')) IN ('EVANS7','EVANSSTREET7','EVANCA07','EVANCA7')
        OR upper(regexp_replace(coalesce(room_row."Name", ''), '[^A-Za-z0-9]', '', 'g')) IN ('EVANS7','EVANSSTREET7','EVANCA07','EVANCA7')
        OR upper(regexp_replace(coalesce(room_row."CropQcRoomName", ''), '[^A-Za-z0-9]', '', 'g')) IN ('EVANS7','EVANSSTREET7','EVANCA07','EVANCA7')
        OR upper(regexp_replace(coalesce(room_row."CompuTechRoomCode", ''), '[^A-Za-z0-9]', '', 'g')) IN ('EVANS7','EVANSSTREET7','EVANCA07','EVANCA7')
        OR upper(regexp_replace(coalesce(room_row."DisplayName", ''), '[^A-Za-z0-9]', '', 'g')) IN ('EVANS7','EVANSSTREET7','EVANCA07','EVANCA7'))
),
season_boundary AS (
    SELECT receipt_row."Id", receipt_row."ReceivedAt" FROM "Receipts" receipt_row
    JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = receipt_row."WarehouseId"
    WHERE upper(warehouse_row."Code") = 'EBS' AND receipt_row."CropYear" = 2026
      AND NOT receipt_row."IsDeleted" AND NOT receipt_row."IsTestData"
    ORDER BY receipt_row."ReceivedAt", receipt_row."Id" LIMIT 1
)
SELECT adjustment_row."Id" AS adjustment_id,
       adjustment_row."RoomId" AS room_id,
       coalesce(room_row."CropQcRoomName", room_row."DisplayName", room_row."Name", room_row."Code") AS room,
       adjustment_row."ReceiptId", adjustment_row."GrowerLotId",
       coalesce(grower_lot."LotNumber", adjustment_row."LotNumber") AS grower_lot,
       coalesce(fruit_profile."VarietyCode", adjustment_row."VarietyCode") AS variety,
       coalesce(adjustment_row."CropYear", receipt_row."CropYear") AS crop_year,
       adjustment_row."ChangeAmount" AS quantity,
       adjustment_row."AdjustmentType", adjustment_row."Source",
       adjustment_row."RoomDepletionId", adjustment_row."RoomTransferId", adjustment_row."ActualRunId",
       adjustment_row."CreatedAt", adjustment_row."AdjustmentAt",
       boundary."Id" AS boundary_receipt_id,
       boundary."ReceivedAt" AS boundary_utc,
       timezone('America/Los_Angeles', boundary."ReceivedAt") AS boundary_pacific,
       adjustment_row."AdjustmentAt" < boundary."ReceivedAt" AS before_boundary,
       (SELECT string_agg(bins_row."Id"::text, ',' ORDER BY bins_row."Id")
        FROM "BinsRunEntries" bins_row
        WHERE bins_row."InventoryAdjustmentId" = adjustment_row."Id"
           OR bins_row."SourceInventoryAdjustmentId" = adjustment_row."Id") AS bins_run_ids,
       CASE
           WHEN adjustment_row."AdjustmentAt" >= boundary."ReceivedAt" THEN '5. Valid 2026 activity that must remain'
           WHEN room_row."Id" IN (27,32,22,15,10) THEN '2. Valid prior-season history already netting to zero and requiring no change'
           WHEN adjustment_row."Id" = 1 THEN '1. Valid prior-season historical activity with a carried balance requiring a season-opening zero'
           WHEN adjustment_row."Id" = 8 THEN '3. Invalid test or duplicate data safe to remove'
           WHEN room_row."Id" = 11 THEN '2. Valid prior-season history already netting to zero and requiring no change'
           WHEN adjustment_row."Id" IN (22,23,25,26,76,77,78,79) THEN '4. Invalid negative-balance data requiring direct cleanup'
           WHEN room_row."Id" = 7 THEN '2. Valid prior-season history already netting to zero and requiring no change'
           ELSE '6. Unclear - stop and ask Wes'
       END AS classification,
       CASE
           WHEN adjustment_row."Id" = 1 THEN 'Neutralize carried impact; preserve receipt 26.'
           WHEN adjustment_row."Id" = 8 THEN 'Neutralize duplicate carry; preserve receipt 28 and Bins Run history.'
           WHEN adjustment_row."Id" IN (22,23,25,26) THEN 'Restore persisted source quantity.'
           WHEN adjustment_row."Id" IN (76,77,78,79) THEN 'Preserve linked Bins Run deduction.'
           ELSE 'Preserve unchanged.'
       END AS proposed_treatment
FROM "RoomInventoryAdjustments" adjustment_row
JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = adjustment_row."WarehouseId"
JOIN "Rooms" room_row ON room_row."Id" = adjustment_row."RoomId"
CROSS JOIN protected_room protected
CROSS JOIN season_boundary boundary
LEFT JOIN "Receipts" receipt_row ON receipt_row."Id" = adjustment_row."ReceiptId"
LEFT JOIN "GrowerLots" grower_lot ON grower_lot."Id" = adjustment_row."GrowerLotId"
LEFT JOIN "FruitProfiles" fruit_profile ON fruit_profile."Id" = adjustment_row."FruitProfileId"
WHERE upper(warehouse_row."Code") = 'EBS' AND adjustment_row."RoomId" <> protected."Id"
ORDER BY room, grower_lot, adjustment_row."CreatedAt", adjustment_row."Id";

WITH protected_room AS (
    SELECT room_row."Id" FROM "Rooms" room_row
    JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = room_row."WarehouseId"
    WHERE upper(warehouse_row."Code") = 'EBS'
      AND (upper(regexp_replace(coalesce(room_row."Code", ''), '[^A-Za-z0-9]', '', 'g')) IN ('EVANS7','EVANSSTREET7','EVANCA07','EVANCA7')
        OR upper(regexp_replace(coalesce(room_row."Name", ''), '[^A-Za-z0-9]', '', 'g')) IN ('EVANS7','EVANSSTREET7','EVANCA07','EVANCA7')
        OR upper(regexp_replace(coalesce(room_row."CropQcRoomName", ''), '[^A-Za-z0-9]', '', 'g')) IN ('EVANS7','EVANSSTREET7','EVANCA07','EVANCA7')
        OR upper(regexp_replace(coalesce(room_row."CompuTechRoomCode", ''), '[^A-Za-z0-9]', '', 'g')) IN ('EVANS7','EVANSSTREET7','EVANCA07','EVANCA7')
        OR upper(regexp_replace(coalesce(room_row."DisplayName", ''), '[^A-Za-z0-9]', '', 'g')) IN ('EVANS7','EVANSSTREET7','EVANCA07','EVANCA7'))
)
SELECT coalesce(room_row."CropQcRoomName", room_row."DisplayName", room_row."Name", room_row."Code") AS room,
       count(*) AS rows, sum(adjustment_row."ChangeAmount") AS current_balance,
       min(adjustment_row."CreatedAt") AS first_created_at, max(adjustment_row."CreatedAt") AS last_created_at
FROM "RoomInventoryAdjustments" adjustment_row
JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = adjustment_row."WarehouseId"
JOIN "Rooms" room_row ON room_row."Id" = adjustment_row."RoomId"
WHERE upper(warehouse_row."Code") = 'EBS' AND room_row."Id" <> (SELECT "Id" FROM protected_room)
GROUP BY room ORDER BY room;

WITH protected_room AS (
    SELECT room_row."Id" FROM "Rooms" room_row
    JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = room_row."WarehouseId"
    WHERE upper(warehouse_row."Code") = 'EBS'
      AND (upper(regexp_replace(coalesce(room_row."Code", ''), '[^A-Za-z0-9]', '', 'g')) IN ('EVANS7','EVANSSTREET7','EVANCA07','EVANCA7')
        OR upper(regexp_replace(coalesce(room_row."Name", ''), '[^A-Za-z0-9]', '', 'g')) IN ('EVANS7','EVANSSTREET7','EVANCA07','EVANCA7')
        OR upper(regexp_replace(coalesce(room_row."CropQcRoomName", ''), '[^A-Za-z0-9]', '', 'g')) IN ('EVANS7','EVANSSTREET7','EVANCA07','EVANCA7')
        OR upper(regexp_replace(coalesce(room_row."CompuTechRoomCode", ''), '[^A-Za-z0-9]', '', 'g')) IN ('EVANS7','EVANSSTREET7','EVANCA07','EVANCA7')
        OR upper(regexp_replace(coalesce(room_row."DisplayName", ''), '[^A-Za-z0-9]', '', 'g')) IN ('EVANS7','EVANSSTREET7','EVANCA07','EVANCA7'))
)
SELECT (SELECT count(*) FROM "Receipts" WHERE "RoomId" = protected."Id") AS evans7_receipts,
       (SELECT count(*) FROM "RoomInventoryAdjustments" WHERE "RoomId" = protected."Id") AS evans7_ledger_rows,
       (SELECT coalesce(sum("ChangeAmount"), 0) FROM "RoomInventoryAdjustments" WHERE "RoomId" = protected."Id") AS evans7_bins
FROM protected_room protected;

SELECT upper(warehouse_row."Code") AS facility, count(*) AS ledger_rows,
       coalesce(sum(adjustment_row."ChangeAmount"), 0) AS ledger_balance
FROM "RoomInventoryAdjustments" adjustment_row
JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = adjustment_row."WarehouseId"
WHERE upper(warehouse_row."Code") <> 'EBS'
GROUP BY upper(warehouse_row."Code") ORDER BY facility;

ROLLBACK;
