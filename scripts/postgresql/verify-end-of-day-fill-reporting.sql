\set ON_ERROR_STOP on
BEGIN TRANSACTION READ ONLY;
DO $verify$
DECLARE
    missing_tables text;
    missing_columns text;
    missing_indexes text;
    missing_constraints text;
    wp_assignment_count integer;
    ebs_assignment_count integer;
BEGIN
    SELECT string_agg(name, ', ' ORDER BY name) INTO missing_tables
    FROM (VALUES ('EndOfDayFillReportGroups'),('EndOfDayFillReportRecipients'),('EndOfDayFillUserGroupAssignments'),('EndOfDayFillReportSends'),('EndOfDayFillSendReservations')) expected(name)
    WHERE to_regclass(format('%I.%I', current_schema(), name)) IS NULL;

    SELECT string_agg(table_name||'.'||column_name, ', ' ORDER BY table_name,column_name) INTO missing_columns
    FROM (VALUES
      ('EndOfDayFillReportGroups','Id','NO'),('EndOfDayFillReportGroups','Name','NO'),('EndOfDayFillReportGroups','Facility','NO'),('EndOfDayFillReportGroups','IsActive','NO'),('EndOfDayFillReportGroups','CreatedAt','NO'),('EndOfDayFillReportGroups','UpdatedAt','NO'),
      ('Rooms','EndOfDayFillReportGroupId','YES'),
      ('EndOfDayFillReportRecipients','Id','NO'),('EndOfDayFillReportRecipients','EmailAddress','NO'),('EndOfDayFillReportRecipients','NormalizedEmailAddress','NO'),('EndOfDayFillReportRecipients','IsActive','NO'),('EndOfDayFillReportRecipients','SortOrder','NO'),('EndOfDayFillReportRecipients','CreatedAt','NO'),('EndOfDayFillReportRecipients','UpdatedAt','NO'),('EndOfDayFillReportRecipients','UpdatedByUserId','YES'),
      ('EndOfDayFillUserGroupAssignments','Id','NO'),('EndOfDayFillUserGroupAssignments','UserId','NO'),('EndOfDayFillUserGroupAssignments','ReportGroupId','NO'),('EndOfDayFillUserGroupAssignments','CreatedAt','NO'),('EndOfDayFillUserGroupAssignments','CreatedByUserId','YES'),
      ('EndOfDayFillReportSends','Id','NO'),('EndOfDayFillReportSends','ReportGroupId','NO'),('EndOfDayFillReportSends','ReportGroupName','NO'),('EndOfDayFillReportSends','Facility','NO'),('EndOfDayFillReportSends','PacificReportDate','NO'),('EndOfDayFillReportSends','RevisionNumber','NO'),('EndOfDayFillReportSends','SenderUserId','YES'),('EndOfDayFillReportSends','SenderEmail','NO'),('EndOfDayFillReportSends','SenderDisplayName','NO'),('EndOfDayFillReportSends','RecipientsJson','NO'),('EndOfDayFillReportSends','PhysicalCountConfirmed','NO'),('EndOfDayFillReportSends','SnapshotHash','NO'),('EndOfDayFillReportSends','SnapshotJson','NO'),('EndOfDayFillReportSends','SuccessRevisionKey','YES'),('EndOfDayFillReportSends','SuccessSnapshotKey','YES'),('EndOfDayFillReportSends','Subject','NO'),('EndOfDayFillReportSends','HtmlBody','NO'),('EndOfDayFillReportSends','TextBody','NO'),('EndOfDayFillReportSends','Status','NO'),('EndOfDayFillReportSends','FailureReason','YES'),('EndOfDayFillReportSends','GmailMessageId','YES'),('EndOfDayFillReportSends','CreatedAt','NO'),('EndOfDayFillReportSends','AttemptedAt','NO'),('EndOfDayFillReportSends','SentAt','YES'),
      ('EndOfDayFillSendReservations','ReportGroupId','NO'),('EndOfDayFillSendReservations','PacificReportDate','NO'),('EndOfDayFillSendReservations','RevisionNumber','NO'),('EndOfDayFillSendReservations','SnapshotHash','NO'),('EndOfDayFillSendReservations','SendAttemptId','NO'),('EndOfDayFillSendReservations','CreatedAt','NO')) expected(table_name,column_name,is_nullable)
    WHERE NOT EXISTS (SELECT 1 FROM information_schema.columns c WHERE c.table_schema=current_schema() AND c.table_name=expected.table_name AND c.column_name=expected.column_name AND c.is_nullable=expected.is_nullable);

    SELECT string_agg(index_name, ', ' ORDER BY index_name) INTO missing_indexes
    FROM (VALUES
      ('IX_EndOfDayFillReportGroups_Name',TRUE),
      ('IX_EndOfDayFillReportRecipients_NormalizedEmailAddress',TRUE),('IX_EndOfDayFillUserGroupAssignments_UserId_ReportGroupId',TRUE),
      ('IX_EndOfDayFillReportSends_SuccessRevisionKey',TRUE),('IX_EndOfDayFillSendReservations_SendAttemptId',TRUE),
      ('IX_Rooms_EndOfDayFillReportGroupId',FALSE),
      ('IX_EndOfDayFillReportRecipients_UpdatedByUserId',FALSE),('IX_EndOfDayFillReportSends_ReportGroupId_PacificReportDate_Status',FALSE),
      ('IX_EndOfDayFillReportSends_SenderUserId',FALSE),('IX_EndOfDayFillReportSends_SuccessSnapshotKey',FALSE),
      ('IX_EndOfDayFillUserGroupAssignments_CreatedByUserId',FALSE),('IX_EndOfDayFillUserGroupAssignments_ReportGroupId',FALSE)) expected(index_name,is_unique)
    WHERE NOT EXISTS (SELECT 1 FROM pg_class i JOIN pg_index ix ON ix.indexrelid=i.oid JOIN pg_namespace n ON n.oid=i.relnamespace WHERE n.nspname=current_schema() AND i.relname=left(expected.index_name,63) AND ix.indisunique=expected.is_unique);

    SELECT string_agg(constraint_name, ', ' ORDER BY constraint_name) INTO missing_constraints
    FROM (VALUES
      ('PK_EndOfDayFillReportGroups'),('PK_EndOfDayFillReportRecipients'),('PK_EndOfDayFillUserGroupAssignments'),('PK_EndOfDayFillReportSends'),('PK_EndOfDayFillSendReservations'),
      ('CK_EndOfDayFillReportGroups_Facility'),
      ('FK_EndOfDayFillReportRecipients_Users_UpdatedByUserId'),
      ('FK_Rooms_EndOfDayFillReportGroups_EndOfDayFillReportGroupId'),
      ('FK_EndOfDayFillUserGroupAssignments_EndOfDayFillReportGroups_ReportGroupId'),('FK_EndOfDayFillUserGroupAssignments_Users_CreatedByUserId'),('FK_EndOfDayFillUserGroupAssignments_Users_UserId'),
      ('FK_EndOfDayFillReportSends_EndOfDayFillReportGroups_ReportGroupId'),('FK_EndOfDayFillReportSends_Users_SenderUserId'),
      ('FK_EndOfDayFillSendReservations_EndOfDayFillReportGroups_ReportGroupId'),('FK_EndOfDayFillSendReservations_EndOfDayFillReportSends_SendAttemptId')) expected(constraint_name)
    WHERE NOT EXISTS (SELECT 1 FROM pg_constraint c JOIN pg_namespace n ON n.oid=c.connamespace WHERE n.nspname=current_schema() AND c.conname=left(expected.constraint_name,63));

    IF missing_tables IS NOT NULL OR missing_columns IS NOT NULL OR missing_indexes IS NOT NULL OR missing_constraints IS NOT NULL THEN
      RAISE EXCEPTION 'End of Day Fill schema incomplete. tables=% columns=% indexes=% constraints=%', coalesce(missing_tables,'none'),coalesce(missing_columns,'none'),coalesce(missing_indexes,'none'),coalesce(missing_constraints,'none');
    END IF;
    IF (SELECT count(*) FROM "EndOfDayFillReportGroups" WHERE ("Name"='WP End of Day Fill' AND "Facility"='WP' AND "IsActive") OR ("Name"='EBS End of Day Fill' AND "Facility"='EBS' AND "IsActive")) <> 2 THEN RAISE EXCEPTION 'Initial report-group configuration is incorrect'; END IF;
    IF (SELECT count(*) FROM "EndOfDayFillReportRecipients" WHERE "IsActive" AND "NormalizedEmailAddress" IN ('WES@FRUITANDLAND.COM','JORGE@WP-PACKING.COM','ROB@EARLBROWNANDSONS.COM')) <> 3 THEN RAISE EXCEPTION 'Initial recipient configuration is incorrect'; END IF;
    IF to_regclass(format('%I.%I', current_schema(), 'EndOfDayFillReportGroupRooms')) IS NOT NULL THEN RAISE EXCEPTION 'Obsolete room-membership join table must not exist'; END IF;
    IF EXISTS (SELECT 1 FROM "Rooms" r JOIN "Warehouses" w ON w."Id"=r."WarehouseId" JOIN "EndOfDayFillReportGroups" g ON g."Id"=r."EndOfDayFillReportGroupId" WHERE g."Facility" <> CASE WHEN lower(btrim(w."Code")) IN ('dh','mcdougall','wp') THEN 'WP' WHEN lower(btrim(w."Code"))='ebs' THEN 'EBS' ELSE '' END) THEN RAISE EXCEPTION 'Cross-facility Room report assignment detected'; END IF;

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
    SELECT count(*) FILTER (WHERE e.facility='WP'),
           count(*) FILTER (WHERE e.facility='EBS')
    INTO wp_assignment_count, ebs_assignment_count
    FROM expected e
    JOIN "Warehouses" w
      ON w."IsActive" AND lower(btrim(w."Code"))=e.warehouse_code
    JOIN "Rooms" r
      ON r."WarehouseId"=w."Id" AND r."IsActive"
     AND lower(btrim(r."Code"))=lower(e.room_code)
    JOIN "EndOfDayFillReportGroups" g
      ON g."Facility"=e.facility
     AND g."Name"=CASE e.facility WHEN 'WP' THEN 'WP End of Day Fill' ELSE 'EBS End of Day Fill' END
     AND r."EndOfDayFillReportGroupId"=g."Id";
    IF wp_assignment_count <> 42 OR ebs_assignment_count <> 27 THEN
        RAISE EXCEPTION 'Initial Room assignments are incomplete or incorrect. wp=% ebs=%', wp_assignment_count, ebs_assignment_count;
    END IF;

    IF EXISTS (
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
        SELECT 1
        FROM "Rooms" r
        JOIN "Warehouses" w ON w."Id"=r."WarehouseId"
        WHERE r."EndOfDayFillReportGroupId" IS NOT NULL
          AND NOT EXISTS (
              SELECT 1 FROM expected e
              WHERE e.warehouse_code=lower(btrim(w."Code"))
                AND lower(e.room_code)=lower(btrim(r."Code")))
    ) THEN RAISE EXCEPTION 'An unexpected Room is assigned to an initial End of Day Fill report'; END IF;

    IF NOT EXISTS (
        SELECT 1 FROM "Rooms" r
        JOIN "Warehouses" w ON w."Id"=r."WarehouseId"
        JOIN "EndOfDayFillReportGroups" g ON g."Id"=r."EndOfDayFillReportGroupId"
        WHERE lower(btrim(w."Code"))='mcdougall'
          AND lower(btrim(r."Code"))='mcd-01'
          AND g."Name"='WP End of Day Fill' AND g."Facility"='WP'
    ) THEN RAISE EXCEPTION 'MCD-01 must be included in WP End of Day Fill reporting'; END IF;
    IF EXISTS (
         (SELECT lower(btrim(u."Email")) AS email, required.report_group
          FROM "Users" u
          JOIN (VALUES ('jorge@wp-packing.com','WP End of Day Fill'),('rob@earlbrownandsons.com','EBS End of Day Fill'),('wes@fruitandland.com','WP End of Day Fill'),('wes@fruitandland.com','EBS End of Day Fill')) required(email,report_group)
            ON required.email=lower(btrim(u."Email")))
         EXCEPT
         (SELECT lower(btrim(u."Email")), g."Name" FROM "EndOfDayFillUserGroupAssignments" a JOIN "Users" u ON u."Id"=a."UserId" JOIN "EndOfDayFillReportGroups" g ON g."Id"=a."ReportGroupId")
       ) OR EXISTS (
         (SELECT lower(btrim(u."Email")), g."Name" FROM "EndOfDayFillUserGroupAssignments" a JOIN "Users" u ON u."Id"=a."UserId" JOIN "EndOfDayFillReportGroups" g ON g."Id"=a."ReportGroupId")
         EXCEPT
         (VALUES ('jorge@wp-packing.com','WP End of Day Fill'),('rob@earlbrownandsons.com','EBS End of Day Fill'),('wes@fruitandland.com','WP End of Day Fill'),('wes@fruitandland.com','EBS End of Day Fill'))
       ) THEN RAISE EXCEPTION 'Initial user report assignments are incorrect'; END IF;
    IF (SELECT count(*) FROM "EndOfDayFillReportSends") <> 0 OR (SELECT count(*) FROM "EndOfDayFillSendReservations") <> 0 THEN RAISE EXCEPTION 'Initial send/reservation tables must be empty'; END IF;
END $verify$;

SELECT g."Name" AS report_group, w."Code" AS warehouse_code, r."Id" AS room_id, r."Code" AS room_code, r."Name" AS room_name, r."CapacityBins"
FROM "Rooms" r JOIN "EndOfDayFillReportGroups" g ON g."Id"=r."EndOfDayFillReportGroupId" JOIN "Warehouses" w ON w."Id"=r."WarehouseId"
ORDER BY g."Name", w."Code", r."Code";
SELECT g."Name", count(*) AS room_count FROM "Rooms" r JOIN "EndOfDayFillReportGroups" g ON g."Id"=r."EndOfDayFillReportGroupId" GROUP BY g."Name" ORDER BY g."Name";
SELECT w."Code" AS warehouse_code, r."Id" AS room_id, r."Code" AS room_code,
       r."CapacityBins" AS capacity_bins, r."EndOfDayFillReportGroupId", 'included_approved_scope' AS seed_status
FROM "Rooms" r JOIN "Warehouses" w ON w."Id"=r."WarehouseId"
WHERE lower(btrim(w."Code"))='mcdougall' AND lower(btrim(r."Code"))='mcd-01';
SELECT u."Id", lower(btrim(u."Email")) AS email, g."Name" AS report_group FROM "EndOfDayFillUserGroupAssignments" a JOIN "Users" u ON u."Id"=a."UserId" JOIN "EndOfDayFillReportGroups" g ON g."Id"=a."ReportGroupId" ORDER BY email,report_group;
SELECT count(*) AS initial_send_count FROM "EndOfDayFillReportSends";
SELECT count(*) AS initial_reservation_count FROM "EndOfDayFillSendReservations";
SELECT 'end_of_day_fill_schema_verified' AS status, 'migration_history_intentionally_unchanged' AS migration_history;
ROLLBACK;
