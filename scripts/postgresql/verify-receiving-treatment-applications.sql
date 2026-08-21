\set ON_ERROR_STOP on
BEGIN TRANSACTION READ ONLY;
DO $verify$
BEGIN
    IF (SELECT count(*) FROM information_schema.columns
        WHERE table_schema=current_schema() AND (table_name,column_name) IN (
            ('TreatmentChemicals','ApplicationLevel'),('RoomTreatmentApplications','ApplicationLevel'),
            ('RoomTreatmentApplications','ReceiptId'),('RoomTreatmentApplicationSources','ReceiptId'),
            ('TreatmentLineageSegments','ReceiptId'),('TreatmentLineageMovements','ReceiptId'))) <> 6 THEN
        RAISE EXCEPTION 'Receiving treatment columns are incomplete';
    END IF;
    IF (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
        WHERE n.nspname=current_schema() AND c.relkind='i' AND c.relname IN (
            'IX_TreatmentChemicals_ApplicationLevel_Crop_IsActive_ProductNam','IX_RoomTreatmentApplications_ReceiptId_AppliedAt',
            'IX_RoomTreatmentApplicationSources_ReceiptId','IX_TreatmentLineageSegments_ReceiptId',
            'UX_TreatmentLineageSegments_Receipt','UX_TreatmentLineageSegments_Unassigned','IX_TreatmentLineageMovements_ReceiptId')) <> 7 THEN
        RAISE EXCEPTION 'Receiving treatment indexes are incomplete';
    END IF;
    IF (SELECT count(*) FROM pg_constraint WHERE connamespace=current_schema()::regnamespace AND conname IN (
        'FK_RoomTreatmentApplications_Receipts_ReceiptId','FK_RoomTreatmentApplicationSources_Receipts_ReceiptId',
        'FK_TreatmentLineageSegments_Receipts_ReceiptId','FK_TreatmentLineageMovements_Receipts_ReceiptId')) <> 4 THEN
        RAISE EXCEPTION 'Receiving treatment foreign keys are incomplete';
    END IF;
    IF to_regclass(format('%I.%I', current_schema(), 'IX_TreatmentChemicals_Crop_IsActive_ProductName')) IS NOT NULL
       OR to_regclass(format('%I.%I', current_schema(), 'IX_TreatmentLineageSegments_RoomId_IdentityKey_TreatmentSignature')) IS NOT NULL THEN
        RAISE EXCEPTION 'Legacy indexes remain after Receiving treatment schema apply';
    END IF;
    IF EXISTS (SELECT 1 FROM "TreatmentChemicals" WHERE "ApplicationLevel" NOT IN ('Room','Receiving'))
       OR EXISTS (SELECT 1 FROM "RoomTreatmentApplications" WHERE "ApplicationLevel" NOT IN ('Room','Receiving')) THEN
        RAISE EXCEPTION 'Invalid ApplicationLevel data exists';
    END IF;
END $verify$;
SELECT 'receiving_treatment_application_schema_verified' AS status,
       17 AS checked_target_objects,
       (SELECT count(*) FROM "RoomTreatmentApplications" WHERE "ApplicationLevel"='Receiving') AS receiving_application_rows;
ROLLBACK;
