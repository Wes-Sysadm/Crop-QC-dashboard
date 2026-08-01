\set ON_ERROR_STOP on
\if :{?correction_authorization}
\else
    \echo 'Missing correction_authorization.'
    \quit 3
\endif
\if :{?operator_email}
\else
    \echo 'Missing operator_email.'
    \quit 3
\endif
\if :{?expected_gala_fingerprint}
\else
    \echo 'Missing expected_gala_fingerprint from immediate preflight.'
    \quit 3
\endif
\if :{?expected_wp_fingerprint}
\else
    \echo 'Missing expected_wp_fingerprint from immediate preflight.'
    \quit 3
\endif

BEGIN ISOLATION LEVEL SERIALIZABLE;
SET LOCAL lock_timeout = '15s';
SET LOCAL statement_timeout = '10min';
SELECT pg_advisory_xact_lock(hashtextextended('CropQc:FujiEvans12AutumnGloryDh1Correction', 0));

LOCK TABLE "Warehouses", "Rooms", "FruitProfiles", "Users", "Receipts",
    "RoomInventoryAdjustments", "BinsRunEntries", "RoomDepletions", "RoomTransfers", "AuditLogs"
    IN SHARE ROW EXCLUSIVE MODE;

CREATE TEMP TABLE correction_parameters AS
SELECT :'correction_authorization'::text AS authorization_token,
       :'operator_email'::text AS operator_email,
       :'expected_gala_fingerprint'::text AS expected_gala_fingerprint,
       :'expected_wp_fingerprint'::text AS expected_wp_fingerprint,
       'Authorized correction: deleted fake receipt contributes no current DH Room 1 Autumn Glory inventory.'::text AS correction_reason;

DO $FAIL_CLOSED$
DECLARE
    operator_count integer;
    audit_count integer;
    gala_fingerprint text;
    wp_fingerprint text;
    fuji_fingerprint text;
    fuji_bins_fingerprint text;
    autumn_fingerprint text;
    receipt_fingerprint text;
BEGIN
    IF (SELECT authorization_token FROM correction_parameters) <> 'APPLY_FUJI_EVANS12_AUTUMN_GLORY_DH1_CORRECTION' THEN
        RAISE EXCEPTION 'Explicit correction authorization did not match.';
    END IF;
    SELECT count(*) INTO operator_count FROM "Users"
    WHERE lower("Email") = lower((SELECT operator_email FROM correction_parameters)) AND "IsActive";
    IF operator_count <> 1 THEN
        RAISE EXCEPTION 'The operator email does not identify exactly one active user.';
    END IF;
    IF (SELECT count(*) FROM "Rooms" WHERE "Id" = 22 AND "WarehouseId" = 1 AND upper("Code") = 'EVANS-12') <> 1
       OR (SELECT count(*) FROM "Rooms" WHERE "Id" = 33 AND "WarehouseId" = 2 AND upper("Code") = 'DH-1') <> 1
       OR (SELECT count(*) FROM "Rooms" WHERE "Id" = 17 AND "WarehouseId" = 1 AND upper("Code") = 'EVANS-7') <> 1 THEN
        RAISE EXCEPTION 'A reviewed room identity changed.';
    END IF;

    SELECT count(*) INTO audit_count FROM "AuditLogs"
    WHERE "Action" = 'ApplyFujiEvans12AutumnGloryDh1Correction'
      AND "EntityName" = 'RoomInventoryCorrection'
      AND "EntityKey" = 'FUJI-EVANS12-ATGL-DH1-20260731';
    IF audit_count > 1 THEN
        RAISE EXCEPTION 'More than one correction audit marker exists.';
    END IF;

    SELECT md5(string_agg("Id"::text || ':' || md5(to_jsonb(a)::text), ',' ORDER BY "Id"))
    INTO gala_fingerprint FROM "RoomInventoryAdjustments" a WHERE "RoomId" = 17;
    SELECT md5(string_agg("Id"::text || ':' || md5(to_jsonb(a)::text), ',' ORDER BY "Id"))
    INTO wp_fingerprint FROM "RoomInventoryAdjustments" a WHERE "WarehouseId" = 4;
    IF gala_fingerprint <> (SELECT expected_gala_fingerprint FROM correction_parameters)
       OR wp_fingerprint <> (SELECT expected_wp_fingerprint FROM correction_parameters) THEN
        RAISE EXCEPTION 'Gala Evans 7 or WP changed after preflight. Stop and repeat read-only inspection.';
    END IF;

    IF audit_count = 1 THEN
        IF (SELECT count(*) FROM "RoomInventoryAdjustments"
            WHERE "Id" = 52 AND "ReceiptId" = 37 AND "WarehouseId" = 2 AND "RoomId" = 33
              AND "GrowerLotId" = 216 AND "FruitProfileId" = 22 AND "CropYear" = 2025
              AND "ChangeAmount" = 0 AND "NewBinCount" = 0 AND "AdjustmentType" = 'ReceiptAdd'
              AND "Reason" = (SELECT correction_reason FROM correction_parameters)
              AND "Notes" LIKE '%' || (SELECT correction_reason FROM correction_parameters)) <> 1 THEN
            RAISE EXCEPTION 'Audit exists but the target row is not in the corrected state.';
        END IF;
        RETURN;
    END IF;

    SELECT md5(string_agg("Id"::text || ':' || md5(to_jsonb(a)::text), ',' ORDER BY "Id"))
    INTO fuji_fingerprint FROM "RoomInventoryAdjustments" a WHERE "Id" IN (35,36,37,54,55,66,67,68);
    SELECT md5(string_agg("Id"::text || ':' || md5(to_jsonb(b)::text), ',' ORDER BY "Id"))
    INTO fuji_bins_fingerprint FROM "BinsRunEntries" b WHERE "Id" IN (1,2,13,14,15);
    SELECT md5(string_agg("Id"::text || ':' || md5(to_jsonb(a)::text), ',' ORDER BY "Id"))
    INTO autumn_fingerprint FROM "RoomInventoryAdjustments" a WHERE "Id" = 52;
    SELECT md5(string_agg("Id"::text || ':' || md5(to_jsonb(r)::text), ',' ORDER BY "Id"))
    INTO receipt_fingerprint FROM "Receipts" r WHERE "Id" = 37;
    IF fuji_fingerprint <> '2b35e5a3ba2a0618dc721dc853e8608e'
       OR fuji_bins_fingerprint <> '9566a296087e3b03ac84cfcc34775eac'
       OR autumn_fingerprint <> '837f988f4045030f80139b6f45f1755d'
       OR receipt_fingerprint <> '2e08da66ddaeab9f940c4dc84eb2c36c' THEN
        RAISE EXCEPTION 'A reviewed target fingerprint changed.';
    END IF;
    IF (SELECT coalesce(sum("ChangeAmount"), 0) FROM "RoomInventoryAdjustments"
        WHERE "Id" IN (35,36,37,54,55,66,67,68)) <> 0 THEN
        RAISE EXCEPTION 'Fuji Evans 12 physical ledger is no longer zero.';
    END IF;
    IF EXISTS (SELECT 1 FROM "BinsRunEntries" WHERE "RoomId" = 33 AND "FruitProfileId" = 22)
       OR EXISTS (SELECT 1 FROM "RoomDepletions" WHERE "RoomId" = 33 AND "FruitProfileId" = 22)
       OR EXISTS (SELECT 1 FROM "RoomTransfers" WHERE ("SourceRoomId" = 33 OR "DestinationRoomId" = 33) AND "FruitProfileId" = 22) THEN
        RAISE EXCEPTION 'Unexpected operational history is linked to the Autumn Glory target.';
    END IF;
END $FAIL_CLOSED$;

CREATE TEMP TABLE protected_state_before AS
SELECT 'Receipts'::text AS table_name, "Id"::text AS row_id, to_jsonb(r) AS row_value FROM "Receipts" r
UNION ALL SELECT 'RoomInventoryAdjustments', "Id"::text, to_jsonb(a) FROM "RoomInventoryAdjustments" a WHERE "Id" <> 52
UNION ALL SELECT 'BinsRunEntries', "Id"::text, to_jsonb(b) FROM "BinsRunEntries" b
UNION ALL SELECT 'RoomDepletions', "Id"::text, to_jsonb(d) FROM "RoomDepletions" d
UNION ALL SELECT 'RoomTransfers', "Id"::text, to_jsonb(t) FROM "RoomTransfers" t
UNION ALL SELECT 'AuditLogs', "Id"::text, to_jsonb(l) FROM "AuditLogs" l
WHERE NOT ("Action" = 'ApplyFujiEvans12AutumnGloryDh1Correction'
  AND "EntityName" = 'RoomInventoryCorrection' AND "EntityKey" = 'FUJI-EVANS12-ATGL-DH1-20260731');

CREATE TEMP TABLE target_before AS
SELECT to_jsonb(a) AS row_value FROM "RoomInventoryAdjustments" a WHERE "Id" = 52;

CREATE TEMP TABLE updated_rows AS
WITH corrected AS (
    UPDATE "RoomInventoryAdjustments" a
    SET "ChangeAmount" = 0,
        "NewBinCount" = 0,
        "Reason" = (SELECT correction_reason FROM correction_parameters),
        "Notes" = concat_ws(E'\n', nullif(a."Notes", ''), (SELECT correction_reason FROM correction_parameters))
    WHERE a."Id" = 52 AND a."ChangeAmount" = 1 AND a."NewBinCount" = 1
      AND NOT EXISTS (SELECT 1 FROM "AuditLogs"
          WHERE "Action" = 'ApplyFujiEvans12AutumnGloryDh1Correction'
            AND "EntityName" = 'RoomInventoryCorrection'
            AND "EntityKey" = 'FUJI-EVANS12-ATGL-DH1-20260731')
    RETURNING a."Id")
SELECT "Id" FROM corrected;

DO $UPDATE_COUNT$
DECLARE
    audit_exists boolean;
    updated_count integer;
BEGIN
    SELECT EXISTS(SELECT 1 FROM "AuditLogs"
        WHERE "Action" = 'ApplyFujiEvans12AutumnGloryDh1Correction'
          AND "EntityName" = 'RoomInventoryCorrection'
          AND "EntityKey" = 'FUJI-EVANS12-ATGL-DH1-20260731') INTO audit_exists;
    SELECT count(*) INTO updated_count FROM updated_rows;
    IF (audit_exists AND updated_count <> 0) OR (NOT audit_exists AND updated_count <> 1) THEN
        RAISE EXCEPTION 'Expected zero idempotent updates or one first-run update; found %.', updated_count;
    END IF;
END $UPDATE_COUNT$;

INSERT INTO "AuditLogs" ("Id", "UserId", "Action", "EntityName", "EntityKey",
    "BeforeValuesJson", "AfterValuesJson", "SourceApplication", "CreatedAt")
SELECT audit_key.audit_id, user_row."Id",
       'ApplyFujiEvans12AutumnGloryDh1Correction', 'RoomInventoryCorrection',
       'FUJI-EVANS12-ATGL-DH1-20260731',
       jsonb_build_object(
           'target', (SELECT row_value FROM target_before),
           'fujiEvans12PhysicalBalance', 0,
           'protectedStateRows', (SELECT count(*) FROM protected_state_before),
           'protectedStateHash', (SELECT md5(string_agg(table_name || ':' || row_id || ':' || md5(row_value::text), ',' ORDER BY table_name, row_id)) FROM protected_state_before),
           'galaEvans7Fingerprint', (SELECT expected_gala_fingerprint FROM correction_parameters),
           'wpFingerprint', (SELECT expected_wp_fingerprint FROM correction_parameters))::text,
       jsonb_build_object(
           'target', to_jsonb(target_row),
           'fujiEvans12PhysicalBalance', 0,
           'autumnGloryDh1Balance', 0,
           'historyPreserved', true)::text,
       'PostgreSQL operational correction', statement_timestamp()
FROM "RoomInventoryAdjustments" target_row
JOIN "Users" user_row ON lower(user_row."Email") = lower((SELECT operator_email FROM correction_parameters))
CROSS JOIN LATERAL (SELECT coalesce(max("Id"), 0) + 1 AS audit_id FROM "AuditLogs") audit_key
WHERE target_row."Id" = 52 AND (SELECT count(*) FROM updated_rows) = 1
  AND NOT EXISTS (SELECT 1 FROM "AuditLogs"
      WHERE "Action" = 'ApplyFujiEvans12AutumnGloryDh1Correction'
        AND "EntityName" = 'RoomInventoryCorrection'
        AND "EntityKey" = 'FUJI-EVANS12-ATGL-DH1-20260731');

CREATE TEMP TABLE protected_state_after AS
SELECT 'Receipts'::text AS table_name, "Id"::text AS row_id, to_jsonb(r) AS row_value FROM "Receipts" r
UNION ALL SELECT 'RoomInventoryAdjustments', "Id"::text, to_jsonb(a) FROM "RoomInventoryAdjustments" a WHERE "Id" <> 52
UNION ALL SELECT 'BinsRunEntries', "Id"::text, to_jsonb(b) FROM "BinsRunEntries" b
UNION ALL SELECT 'RoomDepletions', "Id"::text, to_jsonb(d) FROM "RoomDepletions" d
UNION ALL SELECT 'RoomTransfers', "Id"::text, to_jsonb(t) FROM "RoomTransfers" t
UNION ALL SELECT 'AuditLogs', "Id"::text, to_jsonb(l) FROM "AuditLogs" l
WHERE NOT ("Action" = 'ApplyFujiEvans12AutumnGloryDh1Correction'
  AND "EntityName" = 'RoomInventoryCorrection' AND "EntityKey" = 'FUJI-EVANS12-ATGL-DH1-20260731');

DO $VERIFY_BEFORE_COMMIT$
DECLARE
    protected_differences integer;
    audit_count integer;
BEGIN
    SELECT count(*) INTO protected_differences FROM (
        (SELECT * FROM protected_state_before EXCEPT SELECT * FROM protected_state_after)
        UNION ALL
        (SELECT * FROM protected_state_after EXCEPT SELECT * FROM protected_state_before)) differences;
    IF protected_differences <> 0 THEN
        RAISE EXCEPTION 'Protected inventory/history changed in the correction transaction (% differences).', protected_differences;
    END IF;
    IF (SELECT count(*) FROM "RoomInventoryAdjustments"
        WHERE "Id" = 52 AND "ChangeAmount" = 0 AND "NewBinCount" = 0
          AND "Reason" = (SELECT correction_reason FROM correction_parameters)
          AND "Notes" LIKE '%' || (SELECT correction_reason FROM correction_parameters)) <> 1 THEN
        RAISE EXCEPTION 'Target adjustment is not in the corrected state.';
    END IF;
    IF (SELECT coalesce(sum("ChangeAmount"), 0) FROM "RoomInventoryAdjustments"
        WHERE "RoomId" = 33 AND "FruitProfileId" = 22) <> 0 THEN
        RAISE EXCEPTION 'Autumn Glory DH Room 1 is not zero.';
    END IF;
    SELECT count(*) INTO audit_count FROM "AuditLogs"
    WHERE "Action" = 'ApplyFujiEvans12AutumnGloryDh1Correction'
      AND "EntityName" = 'RoomInventoryCorrection'
      AND "EntityKey" = 'FUJI-EVANS12-ATGL-DH1-20260731';
    IF audit_count <> 1 THEN
        RAISE EXCEPTION 'Expected exactly one correction audit marker; found %.', audit_count;
    END IF;
END $VERIFY_BEFORE_COMMIT$;

SELECT (SELECT count(*) FROM updated_rows) AS rows_updated_this_run,
       (SELECT count(*) FROM protected_state_before) AS protected_rows_before,
       (SELECT md5(string_agg(table_name || ':' || row_id || ':' || md5(row_value::text), ',' ORDER BY table_name, row_id)) FROM protected_state_before) AS protected_hash_before,
       (SELECT count(*) FROM protected_state_after) AS protected_rows_after,
       (SELECT md5(string_agg(table_name || ':' || row_id || ':' || md5(row_value::text), ',' ORDER BY table_name, row_id)) FROM protected_state_after) AS protected_hash_after;

COMMIT;
