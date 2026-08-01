\set ON_ERROR_STOP on

BEGIN TRANSACTION READ ONLY;

DO $PREFLIGHT$
DECLARE
    fuji_adjustment_fingerprint text;
    fuji_bins_fingerprint text;
    autumn_adjustment_fingerprint text;
    autumn_receipt_fingerprint text;
    audit_count integer;
BEGIN
    IF (SELECT count(*) FROM "Warehouses" WHERE "Id" = 1 AND upper("Code") = 'EBS') <> 1
       OR (SELECT count(*) FROM "Warehouses" WHERE "Id" = 2 AND upper("Code") = 'DH') <> 1 THEN
        RAISE EXCEPTION 'The reviewed EBS/DH warehouse identity changed.';
    END IF;
    IF (SELECT count(*) FROM "Rooms" WHERE "Id" = 22 AND "WarehouseId" = 1 AND upper("Code") = 'EVANS-12') <> 1
       OR (SELECT count(*) FROM "Rooms" WHERE "Id" = 33 AND "WarehouseId" = 2 AND upper("Code") = 'DH-1') <> 1
       OR (SELECT count(*) FROM "Rooms" WHERE "Id" = 17 AND "WarehouseId" = 1 AND upper("Code") = 'EVANS-7') <> 1 THEN
        RAISE EXCEPTION 'A reviewed room identity changed.';
    END IF;
    IF (SELECT count(*) FROM "FruitProfiles" WHERE "Id" = 1 AND upper("VarietyCode") = 'FUJI' AND NOT "IsOrganic") <> 1
       OR (SELECT count(*) FROM "FruitProfiles" WHERE "Id" = 22 AND upper("VarietyCode") = 'ATGL' AND NOT "IsOrganic") <> 1
       OR (SELECT count(*) FROM "FruitProfiles" WHERE "Id" = 2 AND upper("VarietyCode") = 'GALA' AND NOT "IsOrganic") <> 1 THEN
        RAISE EXCEPTION 'A reviewed fruit-profile identity changed.';
    END IF;

    SELECT md5(string_agg("Id"::text || ':' || md5(to_jsonb(a)::text), ',' ORDER BY "Id"))
    INTO fuji_adjustment_fingerprint
    FROM "RoomInventoryAdjustments" a WHERE "Id" IN (35,36,37,54,55,66,67,68);
    SELECT md5(string_agg("Id"::text || ':' || md5(to_jsonb(b)::text), ',' ORDER BY "Id"))
    INTO fuji_bins_fingerprint
    FROM "BinsRunEntries" b WHERE "Id" IN (1,2,13,14,15);
    SELECT md5(string_agg("Id"::text || ':' || md5(to_jsonb(a)::text), ',' ORDER BY "Id"))
    INTO autumn_adjustment_fingerprint
    FROM "RoomInventoryAdjustments" a WHERE "Id" = 52;
    SELECT md5(string_agg("Id"::text || ':' || md5(to_jsonb(r)::text), ',' ORDER BY "Id"))
    INTO autumn_receipt_fingerprint
    FROM "Receipts" r WHERE "Id" = 37;

    IF fuji_adjustment_fingerprint <> '2b35e5a3ba2a0618dc721dc853e8608e'
       OR fuji_bins_fingerprint <> '9566a296087e3b03ac84cfcc34775eac' THEN
        RAISE EXCEPTION 'The reviewed Fuji Evans 12 ledger/Bins Run fingerprint changed.';
    END IF;
    IF autumn_adjustment_fingerprint <> '837f988f4045030f80139b6f45f1755d'
       OR autumn_receipt_fingerprint <> '2e08da66ddaeab9f940c4dc84eb2c36c' THEN
        RAISE EXCEPTION 'The reviewed Autumn Glory DH Room 1 source fingerprint changed.';
    END IF;
    IF (SELECT count(*) FROM "RoomInventoryAdjustments" WHERE "Id" IN (35,36,37,54,55,66,67,68)) <> 8
       OR (SELECT coalesce(sum("ChangeAmount"), 0) FROM "RoomInventoryAdjustments" WHERE "Id" IN (35,36,37,54,55,66,67,68)) <> 0
       OR (SELECT count(*) FROM "BinsRunEntries" WHERE "Id" IN (1,2,13,14,15) AND NOT "IsReversed") <> 5 THEN
        RAISE EXCEPTION 'Fuji Evans 12 no longer matches the reviewed zero-net physical history.';
    END IF;
    IF (SELECT count(*) FROM "RoomInventoryAdjustments"
        WHERE "Id" = 52 AND "ReceiptId" = 37 AND "WarehouseId" = 2 AND "RoomId" = 33
          AND "GrowerLotId" = 216 AND "FruitProfileId" = 22 AND "CropYear" = 2025
          AND "ChangeAmount" = 1 AND "NewBinCount" = 1 AND "AdjustmentType" = 'ReceiptAdd') <> 1
       OR (SELECT count(*) FROM "Receipts"
           WHERE "Id" = 37 AND "WarehouseId" = 2 AND "RoomId" = 33 AND "FruitProfileId" = 22
             AND "CropYear" = 2025 AND "BinCount" = 1 AND "IsDeleted" AND NOT "IsTestData"
             AND "DeleteReason" = 'Fake') <> 1 THEN
        RAISE EXCEPTION 'Autumn Glory DH Room 1 is no longer the reviewed deleted-fake-receipt contribution.';
    END IF;
    IF EXISTS (SELECT 1 FROM "BinsRunEntries" WHERE "RoomId" = 33 AND "FruitProfileId" = 22)
       OR EXISTS (SELECT 1 FROM "RoomDepletions" WHERE "RoomId" = 33 AND "FruitProfileId" = 22)
       OR EXISTS (SELECT 1 FROM "RoomTransfers" WHERE ("SourceRoomId" = 33 OR "DestinationRoomId" = 33) AND "FruitProfileId" = 22)
       OR EXISTS (SELECT 1 FROM "RoomInventoryAdjustments" WHERE "Id" = 52 AND ("ActualRunId" IS NOT NULL OR "RoomDepletionId" IS NOT NULL OR "RoomTransferId" IS NOT NULL)) THEN
        RAISE EXCEPTION 'Unexpected operational history is linked to the Autumn Glory target.';
    END IF;
    SELECT count(*) INTO audit_count FROM "AuditLogs"
    WHERE "Action" = 'ApplyFujiEvans12AutumnGloryDh1Correction'
      AND "EntityName" = 'RoomInventoryCorrection'
      AND "EntityKey" = 'FUJI-EVANS12-ATGL-DH1-20260731';
    IF audit_count <> 0 THEN
        RAISE EXCEPTION 'Correction audit already exists. Use verify; do not run first-use preflight.';
    END IF;
END $PREFLIGHT$;

SELECT 'Fuji Evans 12 reviewed physical ledger' AS scope,
       count(*) AS rows, sum("ChangeAmount") AS balance,
       md5(string_agg("Id"::text || ':' || md5(to_jsonb(a)::text), ',' ORDER BY "Id")) AS fingerprint
FROM "RoomInventoryAdjustments" a WHERE "Id" IN (35,36,37,54,55,66,67,68)
UNION ALL
SELECT 'Autumn Glory DH Room 1', count(*), sum("ChangeAmount"),
       md5(string_agg("Id"::text || ':' || md5(to_jsonb(a)::text), ',' ORDER BY "Id"))
FROM "RoomInventoryAdjustments" a WHERE "Id" = 52;

SELECT 'GALA_LEDGER_GUARD' AS guard_name, count(*) AS rows, sum(a."ChangeAmount") AS balance,
       md5(string_agg(a."Id"::text || ':' || md5(to_jsonb(a)::text), ',' ORDER BY a."Id")) AS fingerprint
FROM "RoomInventoryAdjustments" a WHERE a."RoomId" = 17;

SELECT 'WP_LEDGER_GUARD' AS guard_name, count(*) AS rows, sum(a."ChangeAmount") AS balance,
       md5(string_agg(a."Id"::text || ':' || md5(to_jsonb(a)::text), ',' ORDER BY a."Id")) AS fingerprint
FROM "RoomInventoryAdjustments" a WHERE a."WarehouseId" = 4;

ROLLBACK;
