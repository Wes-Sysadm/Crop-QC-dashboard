\set ON_ERROR_STOP on
BEGIN TRANSACTION READ ONLY;
DO $verify$
DECLARE missing_tables text; missing_columns text; missing_indexes text; missing_constraints text;
BEGIN
    SELECT string_agg(name, ', ' ORDER BY name) INTO missing_tables
    FROM (VALUES ('EndOfDayFillReportGroups'),('EndOfDayFillReportGroupRooms'),('EndOfDayFillReportRecipients'),('EndOfDayFillUserGroupAssignments'),('EndOfDayFillReportSends'),('EndOfDayFillSendReservations')) expected(name)
    WHERE to_regclass(format('%I.%I', current_schema(), name)) IS NULL;

    SELECT string_agg(table_name||'.'||column_name, ', ' ORDER BY table_name,column_name) INTO missing_columns
    FROM (VALUES
      ('EndOfDayFillReportGroups','Id','NO'),('EndOfDayFillReportGroups','Name','NO'),('EndOfDayFillReportGroups','Facility','NO'),('EndOfDayFillReportGroups','IsActive','NO'),('EndOfDayFillReportGroups','CreatedAt','NO'),('EndOfDayFillReportGroups','UpdatedAt','NO'),
      ('EndOfDayFillReportGroupRooms','Id','NO'),('EndOfDayFillReportGroupRooms','ReportGroupId','NO'),('EndOfDayFillReportGroupRooms','RoomId','NO'),('EndOfDayFillReportGroupRooms','CreatedAt','NO'),('EndOfDayFillReportGroupRooms','CreatedByUserId','YES'),
      ('EndOfDayFillReportRecipients','Id','NO'),('EndOfDayFillReportRecipients','EmailAddress','NO'),('EndOfDayFillReportRecipients','NormalizedEmailAddress','NO'),('EndOfDayFillReportRecipients','IsActive','NO'),('EndOfDayFillReportRecipients','SortOrder','NO'),('EndOfDayFillReportRecipients','CreatedAt','NO'),('EndOfDayFillReportRecipients','UpdatedAt','NO'),('EndOfDayFillReportRecipients','UpdatedByUserId','YES'),
      ('EndOfDayFillUserGroupAssignments','Id','NO'),('EndOfDayFillUserGroupAssignments','UserId','NO'),('EndOfDayFillUserGroupAssignments','ReportGroupId','NO'),('EndOfDayFillUserGroupAssignments','CreatedAt','NO'),('EndOfDayFillUserGroupAssignments','CreatedByUserId','YES'),
      ('EndOfDayFillReportSends','Id','NO'),('EndOfDayFillReportSends','ReportGroupId','NO'),('EndOfDayFillReportSends','ReportGroupName','NO'),('EndOfDayFillReportSends','Facility','NO'),('EndOfDayFillReportSends','PacificReportDate','NO'),('EndOfDayFillReportSends','RevisionNumber','NO'),('EndOfDayFillReportSends','SenderUserId','YES'),('EndOfDayFillReportSends','SenderEmail','NO'),('EndOfDayFillReportSends','SenderDisplayName','NO'),('EndOfDayFillReportSends','RecipientsJson','NO'),('EndOfDayFillReportSends','PhysicalCountConfirmed','NO'),('EndOfDayFillReportSends','SnapshotHash','NO'),('EndOfDayFillReportSends','SnapshotJson','NO'),('EndOfDayFillReportSends','SuccessRevisionKey','YES'),('EndOfDayFillReportSends','SuccessSnapshotKey','YES'),('EndOfDayFillReportSends','Subject','NO'),('EndOfDayFillReportSends','HtmlBody','NO'),('EndOfDayFillReportSends','TextBody','NO'),('EndOfDayFillReportSends','Status','NO'),('EndOfDayFillReportSends','FailureReason','YES'),('EndOfDayFillReportSends','GmailMessageId','YES'),('EndOfDayFillReportSends','CreatedAt','NO'),('EndOfDayFillReportSends','AttemptedAt','NO'),('EndOfDayFillReportSends','SentAt','YES'),
      ('EndOfDayFillSendReservations','ReportGroupId','NO'),('EndOfDayFillSendReservations','PacificReportDate','NO'),('EndOfDayFillSendReservations','RevisionNumber','NO'),('EndOfDayFillSendReservations','SnapshotHash','NO'),('EndOfDayFillSendReservations','SendAttemptId','NO'),('EndOfDayFillSendReservations','CreatedAt','NO')) expected(table_name,column_name,is_nullable)
    WHERE NOT EXISTS (SELECT 1 FROM information_schema.columns c WHERE c.table_schema=current_schema() AND c.table_name=expected.table_name AND c.column_name=expected.column_name AND c.is_nullable=expected.is_nullable);

    SELECT string_agg(index_name, ', ' ORDER BY index_name) INTO missing_indexes
    FROM (VALUES
      ('IX_EndOfDayFillReportGroups_Name',TRUE),('IX_EndOfDayFillReportGroupRooms_ReportGroupId_RoomId',TRUE),
      ('IX_EndOfDayFillReportRecipients_NormalizedEmailAddress',TRUE),('IX_EndOfDayFillUserGroupAssignments_UserId_ReportGroupId',TRUE),
      ('IX_EndOfDayFillReportSends_SuccessRevisionKey',TRUE),('IX_EndOfDayFillSendReservations_SendAttemptId',TRUE),
      ('IX_EndOfDayFillReportGroupRooms_CreatedByUserId',FALSE),('IX_EndOfDayFillReportGroupRooms_RoomId',FALSE),
      ('IX_EndOfDayFillReportRecipients_UpdatedByUserId',FALSE),('IX_EndOfDayFillReportSends_ReportGroupId_PacificReportDate_Status',FALSE),
      ('IX_EndOfDayFillReportSends_SenderUserId',FALSE),('IX_EndOfDayFillReportSends_SuccessSnapshotKey',FALSE),
      ('IX_EndOfDayFillUserGroupAssignments_CreatedByUserId',FALSE),('IX_EndOfDayFillUserGroupAssignments_ReportGroupId',FALSE)) expected(index_name,is_unique)
    WHERE NOT EXISTS (SELECT 1 FROM pg_class i JOIN pg_index ix ON ix.indexrelid=i.oid JOIN pg_namespace n ON n.oid=i.relnamespace WHERE n.nspname=current_schema() AND i.relname=left(expected.index_name,63) AND ix.indisunique=expected.is_unique);

    SELECT string_agg(constraint_name, ', ' ORDER BY constraint_name) INTO missing_constraints
    FROM (VALUES
      ('PK_EndOfDayFillReportGroups'),('PK_EndOfDayFillReportGroupRooms'),('PK_EndOfDayFillReportRecipients'),('PK_EndOfDayFillUserGroupAssignments'),('PK_EndOfDayFillReportSends'),('PK_EndOfDayFillSendReservations'),
      ('CK_EndOfDayFillReportGroups_Facility'),
      ('FK_EndOfDayFillReportRecipients_Users_UpdatedByUserId'),
      ('FK_EndOfDayFillReportGroupRooms_EndOfDayFillReportGroups_ReportGroupId'),('FK_EndOfDayFillReportGroupRooms_Rooms_RoomId'),('FK_EndOfDayFillReportGroupRooms_Users_CreatedByUserId'),
      ('FK_EndOfDayFillUserGroupAssignments_EndOfDayFillReportGroups_ReportGroupId'),('FK_EndOfDayFillUserGroupAssignments_Users_CreatedByUserId'),('FK_EndOfDayFillUserGroupAssignments_Users_UserId'),
      ('FK_EndOfDayFillReportSends_EndOfDayFillReportGroups_ReportGroupId'),('FK_EndOfDayFillReportSends_Users_SenderUserId'),
      ('FK_EndOfDayFillSendReservations_EndOfDayFillReportGroups_ReportGroupId'),('FK_EndOfDayFillSendReservations_EndOfDayFillReportSends_SendAttemptId')) expected(constraint_name)
    WHERE NOT EXISTS (SELECT 1 FROM pg_constraint c JOIN pg_namespace n ON n.oid=c.connamespace WHERE n.nspname=current_schema() AND c.conname=left(expected.constraint_name,63));

    IF missing_tables IS NOT NULL OR missing_columns IS NOT NULL OR missing_indexes IS NOT NULL OR missing_constraints IS NOT NULL THEN
      RAISE EXCEPTION 'End of Day Fill schema incomplete. tables=% columns=% indexes=% constraints=%', coalesce(missing_tables,'none'),coalesce(missing_columns,'none'),coalesce(missing_indexes,'none'),coalesce(missing_constraints,'none');
    END IF;
    IF (SELECT count(*) FROM "EndOfDayFillReportGroups" WHERE ("Name"='WP End of Day Fill' AND "Facility"='WP' AND "IsActive") OR ("Name"='EBS End of Day Fill' AND "Facility"='EBS' AND "IsActive")) <> 2 THEN RAISE EXCEPTION 'Initial report-group configuration is incorrect'; END IF;
    IF (SELECT count(*) FROM "EndOfDayFillReportRecipients" WHERE "IsActive" AND "NormalizedEmailAddress" IN ('WES@FRUITANDLAND.COM','JORGE@WP-PACKING.COM','ROB@EARLBROWNANDSONS.COM')) <> 3 THEN RAISE EXCEPTION 'Initial recipient configuration is incorrect'; END IF;
    IF EXISTS (SELECT 1 FROM "EndOfDayFillReportGroupRooms" gr JOIN "EndOfDayFillReportGroups" g ON g."Id"=gr."ReportGroupId" JOIN "Rooms" r ON r."Id"=gr."RoomId" JOIN "Warehouses" w ON w."Id"=r."WarehouseId" WHERE (g."Facility"='WP' AND lower(btrim(w."Code")) NOT IN ('dh','mcdougall')) OR (g."Facility"='EBS' AND lower(btrim(w."Code"))<>'ebs')) THEN RAISE EXCEPTION 'Cross-facility initial room membership detected'; END IF;
    IF EXISTS (SELECT gr."RoomId" FROM "EndOfDayFillReportGroupRooms" gr JOIN "EndOfDayFillReportGroups" g ON g."Id"=gr."ReportGroupId" WHERE g."IsActive" GROUP BY gr."RoomId" HAVING count(*)>1) THEN RAISE EXCEPTION 'Duplicate active room membership detected'; END IF;
    IF (SELECT count(*) FROM "EndOfDayFillReportSends") <> 0 OR (SELECT count(*) FROM "EndOfDayFillSendReservations") <> 0 THEN RAISE EXCEPTION 'Initial send/reservation tables must be empty'; END IF;
END $verify$;

SELECT g."Name" AS report_group, w."Code" AS warehouse_code, r."Id" AS room_id, r."Code" AS room_code, coalesce(r."DisplayName",r."Name") AS room_display_name, r."SubLocation", r."CapacityBins"
FROM "EndOfDayFillReportGroupRooms" gr JOIN "EndOfDayFillReportGroups" g ON g."Id"=gr."ReportGroupId" JOIN "Rooms" r ON r."Id"=gr."RoomId" JOIN "Warehouses" w ON w."Id"=r."WarehouseId"
ORDER BY g."Name", w."Code", r."SortOrder", r."Code";
SELECT g."Name", count(*) AS room_count FROM "EndOfDayFillReportGroupRooms" gr JOIN "EndOfDayFillReportGroups" g ON g."Id"=gr."ReportGroupId" GROUP BY g."Name" ORDER BY g."Name";
SELECT u."Id", lower(btrim(u."Email")) AS email, g."Name" AS report_group FROM "EndOfDayFillUserGroupAssignments" a JOIN "Users" u ON u."Id"=a."UserId" JOIN "EndOfDayFillReportGroups" g ON g."Id"=a."ReportGroupId" ORDER BY email,report_group;
SELECT count(*) AS initial_send_count FROM "EndOfDayFillReportSends";
SELECT count(*) AS initial_reservation_count FROM "EndOfDayFillSendReservations";
SELECT 'end_of_day_fill_schema_verified' AS status, 'migration_history_intentionally_unchanged' AS migration_history;
ROLLBACK;
