\set ON_ERROR_STOP on
BEGIN TRANSACTION READ ONLY;
DO $verify$
BEGIN
    IF to_regclass(format('%I.%I', current_schema(), 'RoomTreatmentApplicationAttachments')) IS NULL THEN
        RAISE EXCEPTION 'RoomTreatmentApplicationAttachments is missing';
    END IF;
    IF (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='RoomTreatmentApplicationAttachments') <> 17 THEN
        RAISE EXCEPTION 'Treatment report attachment column count is not exact';
    END IF;
    IF (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=current_schema() AND c.relkind='i' AND c.relname IN (
        'IX_RoomTreatmentApplicationAttachments_CreatedByUserId','IX_RoomTreatmentApplicationAttachments_DeletedByUserId',
        'IX_TreatmentReportAttachments_Application_IsDeleted_CreatedAt',
        'UX_TreatmentReportAttachments_Application_OperationKey')) <> 4 THEN
        RAISE EXCEPTION 'Treatment report attachment indexes are incomplete';
    END IF;
    IF (SELECT count(*) FROM pg_constraint WHERE connamespace=current_schema()::regnamespace AND conname=ANY(ARRAY(SELECT left(x,63) FROM unnest(ARRAY[
        'PK_RoomTreatmentApplicationAttachments','FK_RoomTreatmentApplicationAttachments_RoomTreatmentApplications_RoomTreatmentApplicationId',
        'FK_RoomTreatmentApplicationAttachments_Users_CreatedByUserId','FK_RoomTreatmentApplicationAttachments_Users_DeletedByUserId']) x))) <> 4 THEN
        RAISE EXCEPTION 'Treatment report attachment constraints are incomplete';
    END IF;
END $verify$;
SELECT 'treatment_report_attachment_schema_verified' AS status,
       26 AS checked_target_objects,
       (SELECT count(*) FROM "RoomTreatmentApplicationAttachments") AS attachment_rows;
ROLLBACK;
