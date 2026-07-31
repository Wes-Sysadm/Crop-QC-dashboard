\set ON_ERROR_STOP on
\if :{?cleanup_authorization}
\else
    \echo 'Missing cleanup_authorization. Required: REMOVE_NON_EVANS7_EBS_TEST_INVENTORY'
    \quit 3
\endif
\if :{?operator_email}
\else
    \echo 'Missing operator_email.'
    \quit 3
\endif

BEGIN;
SET LOCAL lock_timeout = '15s';
SET LOCAL statement_timeout = '10min';
SELECT pg_advisory_xact_lock(hashtextextended('CropQc:EbsTestInventoryCleanup:Evans7Protected', 0));

LOCK TABLE "Warehouses", "Rooms", "Receipts", "RoomInventoryAdjustments",
    "RoomDepletions", "BinsRunEntries", "RoomTransfers", "GrowerLots", "AuditLogs"
    IN SHARE ROW EXCLUSIVE MODE;

CREATE TEMP TABLE cleanup_parameters AS
SELECT :'cleanup_authorization'::text AS authorization,
       :'operator_email'::text AS operator_email;

DO $AUTHORIZATION$
DECLARE
    operator_count integer;
    authorization_value text;
    operator_email_value text;
BEGIN
    SELECT parameters.authorization, parameters.operator_email
    INTO authorization_value, operator_email_value
    FROM cleanup_parameters parameters;

    IF authorization_value <> 'REMOVE_NON_EVANS7_EBS_TEST_INVENTORY' THEN
        RAISE EXCEPTION 'Explicit EBS cleanup authorization did not match the required phrase.';
    END IF;

    SELECT count(*) INTO operator_count
    FROM "Users"
    WHERE lower("Email") = lower(operator_email_value)
      AND "IsActive";
    IF operator_count <> 1 THEN
        RAISE EXCEPTION 'The supplied operator email does not identify exactly one active user.';
    END IF;
END $AUTHORIZATION$;

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

DO $ROOM_GUARD$
BEGIN
    IF (SELECT count(*) FROM protected_room) <> 1 THEN
        RAISE EXCEPTION 'Expected exactly one EBS Evans 7 room identity. Cleanup rolled back.';
    END IF;
END $ROOM_GUARD$;

CREATE OR REPLACE FUNCTION pg_temp.evans7_guard_rows(protected_room_id integer)
RETURNS TABLE(table_name text, row_key text, row_value jsonb)
LANGUAGE sql
AS $$
    SELECT 'Rooms', room_row."Id"::text, to_jsonb(room_row)
    FROM "Rooms" room_row WHERE room_row."Id" = protected_room_id
    UNION ALL
    SELECT 'Receipts', receipt_row."Id"::text, to_jsonb(receipt_row)
    FROM "Receipts" receipt_row WHERE receipt_row."RoomId" = protected_room_id
    UNION ALL
    SELECT 'RoomInventoryAdjustments', adjustment_row."Id"::text, to_jsonb(adjustment_row)
    FROM "RoomInventoryAdjustments" adjustment_row WHERE adjustment_row."RoomId" = protected_room_id
    UNION ALL
    SELECT 'RoomDepletions', depletion_row."Id"::text, to_jsonb(depletion_row)
    FROM "RoomDepletions" depletion_row WHERE depletion_row."RoomId" = protected_room_id
    UNION ALL
    SELECT 'BinsRunEntries', bins_row."Id"::text, to_jsonb(bins_row)
    FROM "BinsRunEntries" bins_row WHERE bins_row."RoomId" = protected_room_id
    UNION ALL
    SELECT 'RoomTransfers', transfer_row."Id"::text, to_jsonb(transfer_row)
    FROM "RoomTransfers" transfer_row
    WHERE transfer_row."SourceRoomId" = protected_room_id
       OR transfer_row."DestinationRoomId" = protected_room_id
    UNION ALL
    SELECT 'GrowerLots', lot_row."Id"::text, to_jsonb(lot_row)
    FROM "GrowerLots" lot_row
    WHERE lot_row."Id" IN (
        SELECT receipt_row."GrowerLotId" FROM "Receipts" receipt_row
        WHERE receipt_row."RoomId" = protected_room_id AND receipt_row."GrowerLotId" IS NOT NULL
        UNION
        SELECT adjustment_row."GrowerLotId" FROM "RoomInventoryAdjustments" adjustment_row
        WHERE adjustment_row."RoomId" = protected_room_id AND adjustment_row."GrowerLotId" IS NOT NULL
        UNION
        SELECT bins_row."GrowerLotId" FROM "BinsRunEntries" bins_row
        WHERE bins_row."RoomId" = protected_room_id AND bins_row."GrowerLotId" IS NOT NULL
    );
$$;

CREATE TEMP TABLE evans7_before AS
SELECT * FROM pg_temp.evans7_guard_rows((SELECT "Id" FROM protected_room));

CREATE TEMP TABLE wp_ledger_before AS
SELECT adjustment_row."Id", to_jsonb(adjustment_row) AS row_value
FROM "RoomInventoryAdjustments" adjustment_row
JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = adjustment_row."WarehouseId"
WHERE upper(warehouse_row."Code") = 'WP';

CREATE TEMP TABLE cleanup_candidates AS
SELECT adjustment_row."Id", adjustment_row."ChangeAmount"
FROM "RoomInventoryAdjustments" adjustment_row
JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = adjustment_row."WarehouseId"
CROSS JOIN protected_room protected
WHERE upper(warehouse_row."Code") = 'EBS'
  AND adjustment_row."RoomId" <> protected."Id";

DO $SAFETY_CHECKS$
DECLARE
    linked_count integer;
BEGIN
    SELECT count(*) INTO linked_count
    FROM "RoomInventoryAdjustments" adjustment_row
    JOIN cleanup_candidates candidate ON candidate."Id" = adjustment_row."Id"
    WHERE adjustment_row."RoomTransferId" IS NOT NULL
       OR adjustment_row."ActualRunId" IS NOT NULL
       OR adjustment_row."ActualRunRevisionId" IS NOT NULL
       OR adjustment_row."RoomDepletionId" IS NOT NULL
       OR EXISTS (
           SELECT 1 FROM "BinsRunEntries" bins_row
           WHERE bins_row."InventoryAdjustmentId" = adjustment_row."Id"
              OR bins_row."SourceInventoryAdjustmentId" = adjustment_row."Id");

    IF linked_count > 0 THEN
        RAISE EXCEPTION
            'Found % non-Evans 7 EBS ledger rows linked to operational records. Cleanup rolled back for manual review.',
            linked_count;
    END IF;
END $SAFETY_CHECKS$;

CREATE TEMP TABLE cleanup_summary AS
SELECT count(*)::integer AS before_rows,
       coalesce(sum("ChangeAmount"), 0)::integer AS before_balance
FROM cleanup_candidates;

DELETE FROM "RoomInventoryAdjustments" adjustment_row
USING cleanup_candidates candidate
WHERE adjustment_row."Id" = candidate."Id";

INSERT INTO "AuditLogs" (
    "Id", "UserId", "Action", "EntityName", "EntityKey",
    "BeforeValuesJson", "AfterValuesJson", "SourceApplication", "CreatedAt")
SELECT
    audit_key.audit_id,
    user_row."Id",
    'RemoveEbsTestInventory',
    'EbsTestInventoryCleanup',
    'EBS-outside-Evans-7',
    jsonb_build_object(
        'protectedRoomId', (SELECT "Id" FROM protected_room),
        'removedLedgerRows', summary.before_rows,
        'removedBalance', summary.before_balance)::text,
    jsonb_build_object(
        'remainingNonEvans7LedgerRows', 0,
        'remainingNonEvans7Balance', 0,
        'authorization', 'explicit-production-authorization-required')::text,
    'PostgreSQL operational cleanup',
    now()
FROM cleanup_summary summary
JOIN "Users" user_row
  ON lower(user_row."Email") = lower((SELECT operator_email FROM cleanup_parameters))
CROSS JOIN LATERAL (
    SELECT coalesce(max(audit_row."Id"), 0) + 1 AS audit_id
    FROM "AuditLogs" audit_row
) audit_key
WHERE summary.before_rows > 0;

CREATE TEMP TABLE evans7_after AS
SELECT * FROM pg_temp.evans7_guard_rows((SELECT "Id" FROM protected_room));

CREATE TEMP TABLE wp_ledger_after AS
SELECT adjustment_row."Id", to_jsonb(adjustment_row) AS row_value
FROM "RoomInventoryAdjustments" adjustment_row
JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = adjustment_row."WarehouseId"
WHERE upper(warehouse_row."Code") = 'WP';

DO $VERIFY_BEFORE_COMMIT$
DECLARE
    remaining_rows integer;
    protected_differences integer;
    wp_differences integer;
BEGIN
    SELECT count(*) INTO remaining_rows
    FROM "RoomInventoryAdjustments" adjustment_row
    JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = adjustment_row."WarehouseId"
    CROSS JOIN protected_room protected
    WHERE upper(warehouse_row."Code") = 'EBS'
      AND adjustment_row."RoomId" <> protected."Id";
    IF remaining_rows <> 0 THEN
        RAISE EXCEPTION 'Non-Evans 7 EBS ledger rows remain after cleanup. Transaction rolled back.';
    END IF;

    SELECT count(*) INTO protected_differences
    FROM (
        (SELECT * FROM evans7_before EXCEPT SELECT * FROM evans7_after)
        UNION ALL
        (SELECT * FROM evans7_after EXCEPT SELECT * FROM evans7_before)
    ) differences;
    IF protected_differences <> 0 THEN
        RAISE EXCEPTION 'Evans 7 rows changed during cleanup. Transaction rolled back.';
    END IF;

    SELECT count(*) INTO wp_differences
    FROM (
        (SELECT * FROM wp_ledger_before EXCEPT SELECT * FROM wp_ledger_after)
        UNION ALL
        (SELECT * FROM wp_ledger_after EXCEPT SELECT * FROM wp_ledger_before)
    ) differences;
    IF wp_differences <> 0 THEN
        RAISE EXCEPTION 'WP room ledger rows changed during EBS cleanup. Transaction rolled back.';
    END IF;
END $VERIFY_BEFORE_COMMIT$;

SELECT
    before_rows,
    before_balance,
    0 AS after_rows,
    0 AS after_balance,
    (SELECT count(*) FROM evans7_before) AS evans7_rows_before,
    (SELECT count(*) FROM evans7_after) AS evans7_rows_after,
    (SELECT count(*) FROM wp_ledger_before) AS wp_rows_before,
    (SELECT count(*) FROM wp_ledger_after) AS wp_rows_after
FROM cleanup_summary;

COMMIT;
