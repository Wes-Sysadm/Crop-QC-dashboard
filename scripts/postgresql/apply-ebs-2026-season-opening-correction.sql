\set ON_ERROR_STOP on
\if :{?correction_authorization}
\else
    \echo 'Missing correction_authorization. Required: APPLY_EBS_2026_SEASON_OPENING_CORRECTION'
    \quit 3
\endif
\if :{?expected_boundary_receipt_id}
\else
    \echo 'Missing expected_boundary_receipt_id. Use the receipt ID reported by preflight.'
    \quit 3
\endif
\if :{?operator_email}
\else
    \echo 'Missing operator_email.'
    \quit 3
\endif

BEGIN ISOLATION LEVEL SERIALIZABLE;
SET LOCAL lock_timeout = '15s';
SET LOCAL statement_timeout = '10min';
SELECT pg_advisory_xact_lock(hashtextextended('CropQc:Ebs2026SeasonOpeningCorrection', 0));

LOCK TABLE "Warehouses", "Rooms", "Receipts", "RoomInventoryAdjustments",
    "RoomDepletions", "BinsRunEntries", "RoomTransfers", "GrowerLots", "AuditLogs", "Users"
    IN SHARE ROW EXCLUSIVE MODE;

CREATE TEMP TABLE correction_parameters AS
SELECT :'correction_authorization'::text AS authorization_token,
       :'expected_boundary_receipt_id'::bigint AS expected_boundary_receipt_id,
       :'operator_email'::text AS operator_email;

DO $AUTHORIZATION$
DECLARE
    operator_count integer;
BEGIN
    IF (SELECT authorization_token FROM correction_parameters) <> 'APPLY_EBS_2026_SEASON_OPENING_CORRECTION' THEN
        RAISE EXCEPTION 'Explicit EBS season-opening correction authorization did not match.';
    END IF;
    SELECT count(*) INTO operator_count
    FROM "Users"
    WHERE lower("Email") = lower((SELECT operator_email FROM correction_parameters))
      AND "IsActive";
    IF operator_count <> 1 THEN
        RAISE EXCEPTION 'The supplied operator email does not identify exactly one active user.';
    END IF;
END $AUTHORIZATION$;

CREATE TEMP TABLE protected_room AS
SELECT room_row.*
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

CREATE TEMP TABLE season_boundary AS
SELECT receipt_row."Id" AS receipt_id,
       receipt_row."ReceivedAt" AS received_at_utc,
       receipt_row."RoomId" AS room_id,
       receipt_row."GrowerLotId" AS grower_lot_id,
       receipt_row."FruitProfileId" AS fruit_profile_id
FROM "Receipts" receipt_row
JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = receipt_row."WarehouseId"
WHERE upper(warehouse_row."Code") = 'EBS'
  AND receipt_row."CropYear" = 2026
  AND NOT receipt_row."IsDeleted"
  AND NOT receipt_row."IsTestData"
ORDER BY receipt_row."ReceivedAt", receipt_row."Id"
LIMIT 1;

CREATE TEMP TABLE expected_changes (
    adjustment_id bigint PRIMARY KEY,
    expected_change integer NOT NULL,
    corrected_change integer NOT NULL,
    corrected_old_bins integer,
    corrected_new_bins integer NOT NULL,
    correction_reason text NOT NULL
);
INSERT INTO expected_changes VALUES
    (1, 34, 0, 0, 0, 'EBS 2026 opening correction: prior-season carried balance retired; receipt 26 preserved.'),
    (8, 1039, 0, 0, 0, 'EBS 2026 opening correction: stale duplicate receipt-ledger carry retired; receipt 28 preserved.'),
    (22, 0, 144, 0, 144, 'EBS 2026 opening correction: restored the persisted 144-bin source for Bins Run 23.'),
    (23, 0, 144, 0, 144, 'EBS 2026 opening correction: restored the persisted 144-bin source for Bins Run 24.'),
    (25, 0, 101, 0, 101, 'EBS 2026 opening correction: restored the persisted 101-bin source for Bins Run 25.'),
    (26, 0, 101, 0, 101, 'EBS 2026 opening correction: restored the persisted 101-bin source for Bins Run 26.');

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
    WHERE transfer_row."SourceRoomId" = protected_room_id OR transfer_row."DestinationRoomId" = protected_room_id
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
        WHERE bins_row."RoomId" = protected_room_id AND bins_row."GrowerLotId" IS NOT NULL);
$$;

CREATE TEMP TABLE evans7_before AS
SELECT * FROM pg_temp.evans7_guard_rows((SELECT "Id" FROM protected_room));

CREATE TEMP TABLE non_ebs_ledger_before AS
SELECT adjustment_row."Id", to_jsonb(adjustment_row) AS row_value
FROM "RoomInventoryAdjustments" adjustment_row
JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = adjustment_row."WarehouseId"
WHERE upper(warehouse_row."Code") <> 'EBS';

CREATE TEMP TABLE preserved_ledger_before AS
SELECT adjustment_row."Id", to_jsonb(adjustment_row) AS row_value
FROM "RoomInventoryAdjustments" adjustment_row
WHERE adjustment_row."Id" NOT IN (SELECT adjustment_id FROM expected_changes);

CREATE TEMP TABLE target_before AS
SELECT adjustment_row."Id", to_jsonb(adjustment_row) AS row_value
FROM "RoomInventoryAdjustments" adjustment_row
JOIN expected_changes expected ON expected.adjustment_id = adjustment_row."Id";

DO $FAIL_CLOSED$
DECLARE
    audit_exists boolean;
    candidate_count integer;
    candidate_balance integer;
    exact_target_count integer;
    boundary_room integer;
    boundary_variety text;
BEGIN
    IF (SELECT count(*) FROM protected_room) <> 1 OR (SELECT count(*) FROM season_boundary) <> 1 THEN
        RAISE EXCEPTION 'EBS Evans 7 or the 2026 season boundary is not uniquely resolved.';
    END IF;
    IF (SELECT receipt_id FROM season_boundary) <> (SELECT expected_boundary_receipt_id FROM correction_parameters) THEN
        RAISE EXCEPTION 'The first legitimate 2026 EBS receipt changed. Run preflight again.';
    END IF;
    SELECT boundary.room_id, fruit_profile."VarietyCode" INTO boundary_room, boundary_variety
    FROM season_boundary boundary
    JOIN "FruitProfiles" fruit_profile ON fruit_profile."Id" = boundary.fruit_profile_id;
    IF boundary_room <> (SELECT "Id" FROM protected_room) OR upper(boundary_variety) <> 'GALA' THEN
        RAISE EXCEPTION 'The verified boundary is not the protected Evans 7 Gala receipt.';
    END IF;

    SELECT EXISTS(
        SELECT 1 FROM "AuditLogs"
        WHERE "Action" = 'ApplyEbs2026SeasonOpeningCorrection'
          AND "EntityName" = 'EbsSeasonOpeningCorrection'
          AND "EntityKey" = 'EBS-2026-boundary-receipt-' || (SELECT receipt_id FROM season_boundary)::text)
    INTO audit_exists;

    IF audit_exists THEN
        SELECT count(*) INTO exact_target_count
        FROM "RoomInventoryAdjustments" adjustment_row
        JOIN expected_changes expected ON expected.adjustment_id = adjustment_row."Id"
        WHERE adjustment_row."ChangeAmount" = expected.corrected_change
          AND adjustment_row."OldBinCount" IS NOT DISTINCT FROM expected.corrected_old_bins
          AND adjustment_row."NewBinCount" = expected.corrected_new_bins;
        IF exact_target_count <> 6 THEN
            RAISE EXCEPTION 'Correction audit exists but the six target rows do not match the reviewed corrected state.';
        END IF;
        RETURN;
    END IF;

    SELECT count(*), coalesce(sum(adjustment_row."ChangeAmount"), 0)
    INTO candidate_count, candidate_balance
    FROM "RoomInventoryAdjustments" adjustment_row
    JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = adjustment_row."WarehouseId"
    CROSS JOIN protected_room protected
    WHERE upper(warehouse_row."Code") = 'EBS' AND adjustment_row."RoomId" <> protected."Id";
    IF candidate_count <> 79 OR candidate_balance <> 583 THEN
        RAISE EXCEPTION 'Production fingerprint drifted: expected 79 rows / 583 bins, found % / %.', candidate_count, candidate_balance;
    END IF;

    SELECT count(*) INTO exact_target_count
    FROM "RoomInventoryAdjustments" adjustment_row
    JOIN expected_changes expected ON expected.adjustment_id = adjustment_row."Id"
    WHERE adjustment_row."ChangeAmount" = expected.expected_change
      AND adjustment_row."AdjustmentAt" < (SELECT received_at_utc FROM season_boundary);
    IF exact_target_count <> 6 OR (SELECT count(*) FROM target_before) <> 6 THEN
        RAISE EXCEPTION 'The six reviewed correction rows no longer match their preflight state.';
    END IF;

    IF EXISTS (
        SELECT 1
        FROM "RoomInventoryAdjustments" adjustment_row
        JOIN "Rooms" room_row ON room_row."Id" = adjustment_row."RoomId"
        WHERE room_row."Id" IN (27, 32, 22, 15, 10)
        GROUP BY room_row."Id"
        HAVING sum(adjustment_row."ChangeAmount") <> 0) THEN
        RAISE EXCEPTION 'A reviewed zero-net room no longer nets to zero. Stop for review.';
    END IF;
END $FAIL_CLOSED$;

CREATE TEMP TABLE updated_rows AS
WITH corrected AS (
    UPDATE "RoomInventoryAdjustments" adjustment_row
    SET "ChangeAmount" = expected.corrected_change,
        "OldBinCount" = expected.corrected_old_bins,
        "NewBinCount" = expected.corrected_new_bins,
        "Reason" = expected.correction_reason,
        "Notes" = concat_ws(E'\n', nullif(adjustment_row."Notes", ''), expected.correction_reason)
    FROM expected_changes expected
    WHERE adjustment_row."Id" = expected.adjustment_id
      AND adjustment_row."ChangeAmount" = expected.expected_change
      AND NOT EXISTS (
          SELECT 1 FROM "AuditLogs"
          WHERE "Action" = 'ApplyEbs2026SeasonOpeningCorrection'
            AND "EntityName" = 'EbsSeasonOpeningCorrection'
            AND "EntityKey" = 'EBS-2026-boundary-receipt-' || (SELECT receipt_id FROM season_boundary)::text)
    RETURNING adjustment_row."Id")
SELECT "Id" FROM corrected;

DO $UPDATE_COUNT$
DECLARE
    audit_exists boolean;
    updated_count integer;
BEGIN
    SELECT EXISTS(
        SELECT 1 FROM "AuditLogs"
        WHERE "Action" = 'ApplyEbs2026SeasonOpeningCorrection'
          AND "EntityName" = 'EbsSeasonOpeningCorrection'
          AND "EntityKey" = 'EBS-2026-boundary-receipt-' || (SELECT receipt_id FROM season_boundary)::text)
    INTO audit_exists;
    SELECT count(*) INTO updated_count FROM updated_rows;
    IF (audit_exists AND updated_count <> 0) OR (NOT audit_exists AND updated_count <> 6) THEN
        RAISE EXCEPTION 'Expected either zero idempotent updates or exactly six first-run updates; found %.', updated_count;
    END IF;
END $UPDATE_COUNT$;

INSERT INTO "AuditLogs" (
    "Id", "UserId", "Action", "EntityName", "EntityKey",
    "BeforeValuesJson", "AfterValuesJson", "SourceApplication", "CreatedAt")
SELECT audit_key.audit_id,
       user_row."Id",
       'ApplyEbs2026SeasonOpeningCorrection',
       'EbsSeasonOpeningCorrection',
       'EBS-2026-boundary-receipt-' || boundary.receipt_id::text,
       jsonb_build_object(
           'boundaryReceiptId', boundary.receipt_id,
           'boundaryUtc', boundary.received_at_utc,
           'targetRows', (SELECT jsonb_agg(row_value ORDER BY "Id") FROM target_before),
           'nonEvans7Balance', 583)::text,
       jsonb_build_object(
           'boundaryReceiptId', boundary.receipt_id,
           'targetRows', (SELECT jsonb_agg(to_jsonb(adjustment_row) ORDER BY adjustment_row."Id")
                          FROM "RoomInventoryAdjustments" adjustment_row
                          WHERE adjustment_row."Id" IN (SELECT adjustment_id FROM expected_changes)),
           'nonEvans7Balance', 0,
           'evans7Protected', true,
           'nonEbsProtected', true)::text,
       'PostgreSQL operational correction',
       now()
FROM season_boundary boundary
JOIN "Users" user_row ON lower(user_row."Email") = lower((SELECT operator_email FROM correction_parameters))
CROSS JOIN LATERAL (SELECT coalesce(max(audit_row."Id"), 0) + 1 AS audit_id FROM "AuditLogs" audit_row) audit_key
WHERE (SELECT count(*) FROM updated_rows) = 6
  AND NOT EXISTS (
      SELECT 1 FROM "AuditLogs"
      WHERE "Action" = 'ApplyEbs2026SeasonOpeningCorrection'
        AND "EntityName" = 'EbsSeasonOpeningCorrection'
        AND "EntityKey" = 'EBS-2026-boundary-receipt-' || boundary.receipt_id::text);

CREATE TEMP TABLE evans7_after AS
SELECT * FROM pg_temp.evans7_guard_rows((SELECT "Id" FROM protected_room));
CREATE TEMP TABLE non_ebs_ledger_after AS
SELECT adjustment_row."Id", to_jsonb(adjustment_row) AS row_value
FROM "RoomInventoryAdjustments" adjustment_row
JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = adjustment_row."WarehouseId"
WHERE upper(warehouse_row."Code") <> 'EBS';
CREATE TEMP TABLE preserved_ledger_after AS
SELECT adjustment_row."Id", to_jsonb(adjustment_row) AS row_value
FROM "RoomInventoryAdjustments" adjustment_row
WHERE adjustment_row."Id" NOT IN (SELECT adjustment_id FROM expected_changes);

DO $VERIFY_BEFORE_COMMIT$
DECLARE
    evans7_differences integer;
    non_ebs_differences integer;
    preserved_differences integer;
    audit_count integer;
    protected_balance integer;
BEGIN
    SELECT count(*) INTO evans7_differences FROM (
        (SELECT * FROM evans7_before EXCEPT SELECT * FROM evans7_after)
        UNION ALL (SELECT * FROM evans7_after EXCEPT SELECT * FROM evans7_before)) differences;
    SELECT count(*) INTO non_ebs_differences FROM (
        (SELECT * FROM non_ebs_ledger_before EXCEPT SELECT * FROM non_ebs_ledger_after)
        UNION ALL (SELECT * FROM non_ebs_ledger_after EXCEPT SELECT * FROM non_ebs_ledger_before)) differences;
    SELECT count(*) INTO preserved_differences FROM (
        (SELECT * FROM preserved_ledger_before EXCEPT SELECT * FROM preserved_ledger_after)
        UNION ALL (SELECT * FROM preserved_ledger_after EXCEPT SELECT * FROM preserved_ledger_before)) differences;
    IF evans7_differences <> 0 OR non_ebs_differences <> 0 OR preserved_differences <> 0 THEN
        RAISE EXCEPTION 'Protected rows changed (Evans 7 %, non-EBS %, preserved ledger %).', evans7_differences, non_ebs_differences, preserved_differences;
    END IF;

    SELECT coalesce(sum("ChangeAmount"), 0) INTO protected_balance
    FROM "RoomInventoryAdjustments" WHERE "RoomId" = (SELECT "Id" FROM protected_room);
    IF protected_balance <> 388 THEN
        RAISE EXCEPTION 'Evans 7 balance changed from the protected 388 bins.';
    END IF;
    IF EXISTS (
        SELECT 1 FROM "RoomInventoryAdjustments" adjustment_row
        JOIN "Warehouses" warehouse_row ON warehouse_row."Id" = adjustment_row."WarehouseId"
        CROSS JOIN protected_room protected
        WHERE upper(warehouse_row."Code") = 'EBS' AND adjustment_row."RoomId" <> protected."Id"
        GROUP BY adjustment_row."RoomId"
        HAVING sum(adjustment_row."ChangeAmount") <> 0) THEN
        RAISE EXCEPTION 'At least one non-Evans 7 EBS room is not zero after correction.';
    END IF;

    SELECT count(*) INTO audit_count
    FROM "AuditLogs"
    WHERE "Action" = 'ApplyEbs2026SeasonOpeningCorrection'
      AND "EntityName" = 'EbsSeasonOpeningCorrection'
      AND "EntityKey" = 'EBS-2026-boundary-receipt-' || (SELECT receipt_id FROM season_boundary)::text;
    IF audit_count <> 1 THEN
        RAISE EXCEPTION 'Expected exactly one EBS correction audit record; found %.', audit_count;
    END IF;
END $VERIFY_BEFORE_COMMIT$;

SELECT (SELECT receipt_id FROM season_boundary) AS boundary_receipt_id,
       (SELECT received_at_utc FROM season_boundary) AS boundary_utc,
       (SELECT count(*) FROM updated_rows) AS rows_updated_this_run,
       (SELECT count(*) FROM evans7_before) AS evans7_guard_rows_before,
       (SELECT count(*) FROM evans7_after) AS evans7_guard_rows_after,
       (SELECT count(*) FROM non_ebs_ledger_before) AS non_ebs_rows_before,
       (SELECT count(*) FROM non_ebs_ledger_after) AS non_ebs_rows_after;

COMMIT;
