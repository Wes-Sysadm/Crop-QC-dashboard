\set ON_ERROR_STOP on
BEGIN TRANSACTION READ ONLY;

DO $preflight$
DECLARE
    missing_base text;
    target_table_count integer;
    conflicting_name_count integer;
    warehouse_identity_count integer;
    expected_room_count integer;
    wp_candidate_count integer;
    ebs_candidate_count integer;
    unresolved_room_count integer;
    duplicate_room_code_count integer;
BEGIN
    SELECT string_agg(expected.name, ', ' ORDER BY expected.name)
    INTO missing_base
    FROM (VALUES ('Users'), ('UserGoogleCredentials'), ('Warehouses'), ('Rooms'), ('AuditLogs')) AS expected(name)
    WHERE to_regclass(format('%I.%I', current_schema(), expected.name)) IS NULL;
    IF missing_base IS NOT NULL THEN
        RAISE EXCEPTION 'Required production prerequisite table(s) are missing: %', missing_base;
    END IF;

    SELECT count(*) INTO target_table_count
    FROM (VALUES
        ('EndOfDayFillReportGroups'),
        ('EndOfDayFillReportRecipients'), ('EndOfDayFillUserGroupAssignments'),
        ('EndOfDayFillReportSends'), ('EndOfDayFillSendReservations')) AS expected(name)
    WHERE to_regclass(format('%I.%I', current_schema(), expected.name)) IS NOT NULL;
    IF target_table_count NOT IN (0, 5) THEN
        RAISE EXCEPTION 'Unsupported partial End of Day Fill table state detected (% of 5 tables). No changes were made.', target_table_count;
    END IF;
    IF (target_table_count = 0 AND EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='Rooms' AND column_name='EndOfDayFillReportGroupId'))
       OR (target_table_count = 5 AND NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='Rooms' AND column_name='EndOfDayFillReportGroupId')) THEN
        RAISE EXCEPTION 'Unsupported partial Room End of Day Fill assignment state. No changes were made.';
    END IF;
    IF to_regclass(format('%I.%I', current_schema(), 'EndOfDayFillReportGroupRooms')) IS NOT NULL THEN
        RAISE EXCEPTION 'Obsolete draft EndOfDayFillReportGroupRooms table detected. No changes were made.';
    END IF;

    IF target_table_count = 0 THEN
        SELECT count(*) INTO conflicting_name_count
        FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
        WHERE n.nspname=current_schema()
          AND c.relname IN (
            'IX_EndOfDayFillReportGroups_Name',
            'IX_Rooms_EndOfDayFillReportGroupId',
            'IX_EndOfDayFillReportRecipients_NormalizedEmailAddress',
            'IX_EndOfDayFillUserGroupAssignments_UserId_ReportGroupId',
            'IX_EndOfDayFillReportSends_SuccessRevisionKey',
            'IX_EndOfDayFillSendReservations_SendAttemptId');
        IF conflicting_name_count <> 0 THEN
            RAISE EXCEPTION 'Conflicting End of Day Fill index names exist without the expected tables.';
        END IF;
    END IF;

    SELECT count(*) INTO warehouse_identity_count
    FROM "Warehouses"
    WHERE "IsActive" AND lower(btrim("Code")) IN ('dh', 'mcdougall', 'wp', 'ebs');
    IF warehouse_identity_count <> 4
       OR EXISTS (
          SELECT lower(btrim("Code")) FROM "Warehouses"
          WHERE "IsActive" AND lower(btrim("Code")) IN ('dh', 'mcdougall', 'wp', 'ebs')
          GROUP BY lower(btrim("Code")) HAVING count(*) <> 1) THEN
        RAISE EXCEPTION 'Expected exactly one active DH, McDougall, WP, and EBS warehouse identity. No changes were made.';
    END IF;

    SELECT count(*) INTO duplicate_room_code_count
    FROM (
        SELECT w."Id", lower(btrim(r."Code")) AS normalized_room_code
        FROM "Warehouses" w
        JOIN "Rooms" r ON r."WarehouseId"=w."Id"
        WHERE w."IsActive" AND lower(btrim(w."Code")) IN ('dh', 'mcdougall', 'wp', 'ebs')
        GROUP BY w."Id", lower(btrim(r."Code"))
        HAVING count(*) <> 1
    ) duplicates;
    IF duplicate_room_code_count <> 0 THEN
        RAISE EXCEPTION 'Duplicate normalized Room codes exist in the reviewed DH, McDougall, WP, or EBS warehouse scope. No changes were made.';
    END IF;

    WITH expected(facility, warehouse_code, room_code) AS (
        SELECT 'WP', 'dh', 'DH-' || n FROM generate_series(1, 22) AS n
        UNION ALL SELECT 'WP', 'mcdougall', 'MCD-01'
        UNION ALL SELECT 'WP', 'mcdougall', 'MCD-' || n FROM generate_series(3, 16) AS n
        UNION ALL SELECT 'WP', 'wp', 'WP-' || n FROM generate_series(4, 8) AS n
        UNION ALL SELECT 'EBS', 'ebs', 'LAMB-' || n FROM generate_series(13, 17) AS n
        UNION ALL SELECT 'EBS', 'ebs', 'EVANS-' || n FROM generate_series(1, 12) AS n
        UNION ALL SELECT 'EBS', 'ebs', room_code FROM (VALUES
            ('EVANS-BACKSIDE'), ('EVANS-BKT'), ('EVANS-HALLWAY1'), ('EVANS-HALLWAY2')) special(room_code)
        UNION ALL SELECT 'EBS', 'ebs', 'BM-' || n FROM generate_series(1, 6) AS n
    ), resolved AS (
        SELECT e.facility, e.warehouse_code, e.room_code, count(r."Id") AS match_count
        FROM expected e
        LEFT JOIN "Warehouses" w
          ON w."IsActive" AND lower(btrim(w."Code"))=e.warehouse_code
        LEFT JOIN "Rooms" r
          ON r."WarehouseId"=w."Id" AND r."IsActive"
         AND lower(btrim(r."Code"))=lower(e.room_code)
        GROUP BY e.facility, e.warehouse_code, e.room_code
    )
    SELECT count(*),
           count(*) FILTER (WHERE facility='WP' AND match_count=1),
           count(*) FILTER (WHERE facility='EBS' AND match_count=1),
           count(*) FILTER (WHERE match_count<>1)
    INTO expected_room_count, wp_candidate_count, ebs_candidate_count, unresolved_room_count
    FROM resolved;
    IF expected_room_count <> 69 OR wp_candidate_count <> 42 OR ebs_candidate_count <> 27 OR unresolved_room_count <> 0 THEN
        RAISE EXCEPTION 'Reviewed End of Day Fill Room scope did not resolve exactly. expected=69 wp=% ebs=% missing_or_ambiguous=%. No changes were made.',
            wp_candidate_count, ebs_candidate_count, unresolved_room_count;
    END IF;

    IF target_table_count = 5 THEN
        IF EXISTS (SELECT 1 FROM "EndOfDayFillReportSends")
           OR EXISTS (SELECT 1 FROM "EndOfDayFillSendReservations") THEN
            RAISE EXCEPTION 'Existing End of Day Fill send or reservation data is incompatible with an initial deployment apply.';
        END IF;
    END IF;
END $preflight$;

WITH expected(facility, warehouse_code, room_code) AS (
    SELECT 'WP', 'dh', 'DH-' || n FROM generate_series(1, 22) AS n
    UNION ALL SELECT 'WP', 'mcdougall', 'MCD-01'
    UNION ALL SELECT 'WP', 'mcdougall', 'MCD-' || n FROM generate_series(3, 16) AS n
    UNION ALL SELECT 'WP', 'wp', 'WP-' || n FROM generate_series(4, 8) AS n
    UNION ALL SELECT 'EBS', 'ebs', 'LAMB-' || n FROM generate_series(13, 17) AS n
    UNION ALL SELECT 'EBS', 'ebs', 'EVANS-' || n FROM generate_series(1, 12) AS n
    UNION ALL SELECT 'EBS', 'ebs', room_code FROM (VALUES
        ('EVANS-BACKSIDE'), ('EVANS-BKT'), ('EVANS-HALLWAY1'), ('EVANS-HALLWAY2')) special(room_code)
    UNION ALL SELECT 'EBS', 'ebs', 'BM-' || n FROM generate_series(1, 6) AS n
)
SELECT CASE e.facility WHEN 'WP' THEN 'WP End of Day Fill' ELSE 'EBS End of Day Fill' END AS report_group,
       w."Id" AS warehouse_id, w."Code" AS warehouse_code, w."Name" AS warehouse_name,
       r."Id" AS room_id, r."Code" AS room_code, coalesce(r."DisplayName", r."Name") AS room_display_name,
       r."SubLocation" AS sub_location, r."CapacityBins" AS capacity_bins
FROM expected e
JOIN "Warehouses" w ON w."IsActive" AND lower(btrim(w."Code"))=e.warehouse_code
JOIN "Rooms" r ON r."WarehouseId"=w."Id" AND r."IsActive" AND lower(btrim(r."Code"))=lower(e.room_code)
ORDER BY report_group, lower(w."Code"), r."SortOrder", r."Code";

WITH expected(facility, warehouse_code, room_code) AS (
    SELECT 'WP', 'dh', 'DH-' || n FROM generate_series(1, 22) AS n
    UNION ALL SELECT 'WP', 'mcdougall', 'MCD-01'
    UNION ALL SELECT 'WP', 'mcdougall', 'MCD-' || n FROM generate_series(3, 16) AS n
    UNION ALL SELECT 'WP', 'wp', 'WP-' || n FROM generate_series(4, 8) AS n
    UNION ALL SELECT 'EBS', 'ebs', 'LAMB-' || n FROM generate_series(13, 17) AS n
    UNION ALL SELECT 'EBS', 'ebs', 'EVANS-' || n FROM generate_series(1, 12) AS n
    UNION ALL SELECT 'EBS', 'ebs', room_code FROM (VALUES
        ('EVANS-BACKSIDE'), ('EVANS-BKT'), ('EVANS-HALLWAY1'), ('EVANS-HALLWAY2')) special(room_code)
    UNION ALL SELECT 'EBS', 'ebs', 'BM-' || n FROM generate_series(1, 6) AS n
)
SELECT CASE e.facility WHEN 'WP' THEN 'WP End of Day Fill' ELSE 'EBS End of Day Fill' END AS report_group,
       count(*) AS candidate_room_count
FROM expected e
JOIN "Warehouses" w ON w."IsActive" AND lower(btrim(w."Code"))=e.warehouse_code
JOIN "Rooms" r ON r."WarehouseId"=w."Id" AND r."IsActive" AND lower(btrim(r."Code"))=lower(e.room_code)
GROUP BY e.facility ORDER BY report_group;

SELECT w."Code" AS warehouse_code, r."Id" AS room_id, r."Code" AS room_code,
       r."CapacityBins" AS capacity_bins, 'included_approved_scope' AS seed_status
FROM "Rooms" r
JOIN "Warehouses" w ON w."Id"=r."WarehouseId"
WHERE lower(btrim(w."Code"))='mcdougall' AND lower(btrim(r."Code"))='mcd-01';

SELECT u."Id" AS user_id, lower(btrim(u."Email")) AS normalized_email, u."DisplayName", u."IsActive",
       EXISTS (SELECT 1 FROM "UserGoogleCredentials" g WHERE g."UserId"=u."Id" AND lower(g."Provider")='google'
           AND (g."AccessTokenEncrypted" IS NOT NULL OR g."RefreshTokenEncrypted" IS NOT NULL)) AS gmail_credential_present,
       EXISTS (SELECT 1 FROM "UserGoogleCredentials" g WHERE g."UserId"=u."Id" AND lower(g."Provider")='google'
           AND g."Scope" ILIKE '%gmail.send%') AS gmail_send_scope_present
FROM "Users" u
WHERE lower(btrim(u."Email")) IN ('jorge@wp-packing.com', 'rob@earlbrownandsons.com', 'wes@fruitandland.com')
ORDER BY normalized_email;

SELECT unnest(ARRAY['wes@fruitandland.com','jorge@wp-packing.com','rob@earlbrownandsons.com']) AS intended_recipient;
SELECT 'end_of_day_fill_preflight_ready' AS status, target_state.state
FROM (SELECT CASE WHEN to_regclass(format('%I.%I', current_schema(), 'EndOfDayFillReportGroups')) IS NULL THEN 'absent' ELSE 'complete-existing' END AS state) target_state;
ROLLBACK;
