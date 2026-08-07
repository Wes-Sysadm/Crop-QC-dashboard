\set ON_ERROR_STOP on
BEGIN TRANSACTION READ ONLY;

DO $preflight$
DECLARE
    missing_base text;
    target_table_count integer;
    conflicting_name_count integer;
    warehouse_identity_count integer;
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
        ('EndOfDayFillReportGroups'), ('EndOfDayFillReportGroupRooms'),
        ('EndOfDayFillReportRecipients'), ('EndOfDayFillUserGroupAssignments'),
        ('EndOfDayFillReportSends'), ('EndOfDayFillSendReservations')) AS expected(name)
    WHERE to_regclass(format('%I.%I', current_schema(), expected.name)) IS NOT NULL;
    IF target_table_count NOT IN (0, 6) THEN
        RAISE EXCEPTION 'Unsupported partial End of Day Fill table state detected (% of 6 tables). No changes were made.', target_table_count;
    END IF;

    IF target_table_count = 0 THEN
        SELECT count(*) INTO conflicting_name_count
        FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
        WHERE n.nspname=current_schema()
          AND c.relname IN (
            'IX_EndOfDayFillReportGroups_Name',
            'IX_EndOfDayFillReportGroupRooms_ReportGroupId_RoomId',
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
    WHERE "IsActive" AND lower(btrim("Code")) IN ('dh', 'mcdougall', 'ebs');
    IF warehouse_identity_count <> 3
       OR EXISTS (
          SELECT lower(btrim("Code")) FROM "Warehouses"
          WHERE "IsActive" AND lower(btrim("Code")) IN ('dh', 'mcdougall', 'ebs')
          GROUP BY lower(btrim("Code")) HAVING count(*) <> 1) THEN
        RAISE EXCEPTION 'Expected exactly one active DH, McDougall, and EBS warehouse identity. No changes were made.';
    END IF;

    IF target_table_count = 6 THEN
        IF EXISTS (SELECT 1 FROM "EndOfDayFillReportSends")
           OR EXISTS (SELECT 1 FROM "EndOfDayFillSendReservations") THEN
            RAISE EXCEPTION 'Existing End of Day Fill send or reservation data is incompatible with an initial deployment apply.';
        END IF;
    END IF;
END $preflight$;

SELECT CASE WHEN lower(btrim(w."Code")) IN ('dh', 'mcdougall') THEN 'WP End of Day Fill' ELSE 'EBS End of Day Fill' END AS report_group,
       w."Id" AS warehouse_id, w."Code" AS warehouse_code, w."Name" AS warehouse_name,
       r."Id" AS room_id, r."Code" AS room_code, coalesce(r."DisplayName", r."Name") AS room_display_name,
       r."SubLocation" AS sub_location, r."CapacityBins" AS capacity_bins
FROM "Rooms" r JOIN "Warehouses" w ON w."Id"=r."WarehouseId"
WHERE r."IsActive" AND w."IsActive" AND lower(btrim(w."Code")) IN ('dh', 'mcdougall', 'ebs')
ORDER BY report_group, lower(w."Code"), r."SortOrder", r."Code";

SELECT CASE WHEN lower(btrim(w."Code")) IN ('dh', 'mcdougall') THEN 'WP End of Day Fill' ELSE 'EBS End of Day Fill' END AS report_group,
       count(*) AS candidate_room_count
FROM "Rooms" r JOIN "Warehouses" w ON w."Id"=r."WarehouseId"
WHERE r."IsActive" AND w."IsActive" AND lower(btrim(w."Code")) IN ('dh', 'mcdougall', 'ebs')
GROUP BY report_group ORDER BY report_group;

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
