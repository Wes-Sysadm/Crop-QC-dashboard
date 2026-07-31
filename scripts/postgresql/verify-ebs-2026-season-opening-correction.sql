\set ON_ERROR_STOP on

BEGIN TRANSACTION READ ONLY;

DO $VERIFY$
DECLARE
    protected_count integer;
    protected_room_id integer;
    boundary_count integer;
    boundary_receipt_id bigint;
    boundary_room_id integer;
    boundary_variety text;
    protected_balance integer;
    nonzero_room_count integer;
    corrected_count integer;
    audit_count integer;
BEGIN
    WITH protected_room AS (
        SELECT room_row."Id"
        FROM "Rooms" room_row
        JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = room_row."WarehouseId"
        WHERE upper(warehouse_row."Code") = 'EBS'
          AND (
              upper(regexp_replace(coalesce(room_row."Code", ''), '[^A-Za-z0-9]', '', 'g')) IN ('EVANS7','EVANSSTREET7','EVANCA07','EVANCA7')
              OR upper(regexp_replace(coalesce(room_row."Name", ''), '[^A-Za-z0-9]', '', 'g')) IN ('EVANS7','EVANSSTREET7','EVANCA07','EVANCA7')
              OR upper(regexp_replace(coalesce(room_row."CropQcRoomName", ''), '[^A-Za-z0-9]', '', 'g')) IN ('EVANS7','EVANSSTREET7','EVANCA07','EVANCA7')
              OR upper(regexp_replace(coalesce(room_row."CompuTechRoomCode", ''), '[^A-Za-z0-9]', '', 'g')) IN ('EVANS7','EVANSSTREET7','EVANCA07','EVANCA7')
              OR upper(regexp_replace(coalesce(room_row."DisplayName", ''), '[^A-Za-z0-9]', '', 'g')) IN ('EVANS7','EVANSSTREET7','EVANCA07','EVANCA7'))
    ),
    season_boundary AS (
        SELECT receipt_row."Id" AS receipt_id, receipt_row."RoomId" AS room_id,
               receipt_row."FruitProfileId" AS fruit_profile_id
        FROM "Receipts" receipt_row
        JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = receipt_row."WarehouseId"
        WHERE upper(warehouse_row."Code") = 'EBS' AND receipt_row."CropYear" = 2026
          AND NOT receipt_row."IsDeleted" AND NOT receipt_row."IsTestData"
        ORDER BY receipt_row."ReceivedAt", receipt_row."Id" LIMIT 1
    )
    SELECT (SELECT count(*) FROM protected_room), (SELECT "Id" FROM protected_room),
           (SELECT count(*) FROM season_boundary), (SELECT receipt_id FROM season_boundary),
           (SELECT room_id FROM season_boundary),
           (SELECT fruit_profile."VarietyCode" FROM season_boundary boundary
            JOIN "FruitProfiles" fruit_profile ON fruit_profile."Id" = boundary.fruit_profile_id),
           (SELECT coalesce(sum("ChangeAmount"), 0) FROM "RoomInventoryAdjustments"
            WHERE "RoomId" = (SELECT "Id" FROM protected_room)),
           (SELECT count(*) FROM (
                SELECT adjustment_row."RoomId"
                FROM "RoomInventoryAdjustments" adjustment_row
                JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = adjustment_row."WarehouseId"
                WHERE upper(warehouse_row."Code") = 'EBS'
                  AND adjustment_row."RoomId" <> (SELECT "Id" FROM protected_room)
                GROUP BY adjustment_row."RoomId"
                HAVING sum(adjustment_row."ChangeAmount") <> 0) nonzero_rooms),
           (SELECT count(*) FROM "RoomInventoryAdjustments"
            WHERE ("Id", "ChangeAmount", coalesce("OldBinCount", -1), "NewBinCount") IN (
                (1,0,0,0),(8,0,0,0),(22,144,0,144),(23,144,0,144),(25,101,0,101),(26,101,0,101))),
           (SELECT count(*) FROM "AuditLogs"
            WHERE "Action" = 'ApplyEbs2026SeasonOpeningCorrection'
              AND "EntityName" = 'EbsSeasonOpeningCorrection'
              AND "EntityKey" = 'EBS-2026-boundary-receipt-' || (SELECT receipt_id FROM season_boundary)::text)
    INTO protected_count, protected_room_id, boundary_count, boundary_receipt_id, boundary_room_id,
         boundary_variety, protected_balance, nonzero_room_count, corrected_count, audit_count;

    IF protected_count <> 1 OR boundary_count <> 1 THEN
        RAISE EXCEPTION 'Evans 7 or the EBS 2026 season boundary is not uniquely resolved.';
    END IF;
    IF boundary_room_id <> protected_room_id OR upper(boundary_variety) <> 'GALA' THEN
        RAISE EXCEPTION 'The verified EBS 2026 boundary is not the protected Evans 7 Gala receipt.';
    END IF;
    IF protected_balance <> 388 THEN
        RAISE EXCEPTION 'Evans 7 is not the protected 388-bin balance.';
    END IF;
    IF nonzero_room_count <> 0 THEN
        RAISE EXCEPTION 'At least one non-Evans 7 EBS room still has a nonzero balance.';
    END IF;
    IF corrected_count <> 6 THEN
        RAISE EXCEPTION 'The six reviewed correction rows do not match the expected state.';
    END IF;
    IF audit_count <> 1 THEN
        RAISE EXCEPTION 'Expected exactly one correction audit record; found %.', audit_count;
    END IF;
END $VERIFY$;

WITH season_boundary AS (
    SELECT receipt_row.* FROM "Receipts" receipt_row
    JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = receipt_row."WarehouseId"
    WHERE upper(warehouse_row."Code") = 'EBS' AND receipt_row."CropYear" = 2026
      AND NOT receipt_row."IsDeleted" AND NOT receipt_row."IsTestData"
    ORDER BY receipt_row."ReceivedAt", receipt_row."Id" LIMIT 1
)
SELECT boundary."Id" AS boundary_receipt_id, boundary."ReceivedAt" AS boundary_utc,
       timezone('America/Los_Angeles', boundary."ReceivedAt") AS boundary_pacific,
       boundary."RoomId", fruit_profile."VarietyCode" AS boundary_variety
FROM season_boundary boundary
JOIN "FruitProfiles" fruit_profile ON fruit_profile."Id" = boundary."FruitProfileId";

SELECT room_row."Id" AS room_id,
       coalesce(room_row."CropQcRoomName", room_row."DisplayName", room_row."Name", room_row."Code") AS room,
       count(adjustment_row."Id") AS ledger_rows,
       coalesce(sum(adjustment_row."ChangeAmount"), 0) AS current_balance
FROM "Rooms" room_row
JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = room_row."WarehouseId"
LEFT JOIN "RoomInventoryAdjustments" adjustment_row ON adjustment_row."RoomId" = room_row."Id"
WHERE upper(warehouse_row."Code") = 'EBS'
GROUP BY room_row."Id", room ORDER BY room;

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
       (SELECT coalesce(sum("ChangeAmount"), 0) FROM "RoomInventoryAdjustments" WHERE "RoomId" = protected."Id") AS evans7_bins,
       (SELECT count(DISTINCT related."GrowerLotId") FROM (
            SELECT "GrowerLotId" FROM "Receipts" WHERE "RoomId" = protected."Id"
            UNION SELECT "GrowerLotId" FROM "RoomInventoryAdjustments" WHERE "RoomId" = protected."Id") related
        WHERE related."GrowerLotId" IS NOT NULL) AS evans7_grower_lots
FROM protected_room protected;

SELECT upper(warehouse_row."Code") AS facility, count(*) AS ledger_rows,
       coalesce(sum(adjustment_row."ChangeAmount"), 0) AS ledger_balance
FROM "RoomInventoryAdjustments" adjustment_row
JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = adjustment_row."WarehouseId"
WHERE upper(warehouse_row."Code") <> 'EBS'
GROUP BY upper(warehouse_row."Code") ORDER BY facility;

SELECT "Id", "UserId", "EntityKey", "BeforeValuesJson", "AfterValuesJson", "CreatedAt"
FROM "AuditLogs"
WHERE "Action" = 'ApplyEbs2026SeasonOpeningCorrection'
  AND "EntityName" = 'EbsSeasonOpeningCorrection'
ORDER BY "Id";

ROLLBACK;
