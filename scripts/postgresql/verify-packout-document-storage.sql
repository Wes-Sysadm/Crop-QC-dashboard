\set ON_ERROR_STOP on
BEGIN TRANSACTION READ ONLY;
DO $verify$
BEGIN
    IF (SELECT count(*) FROM information_schema.columns
        WHERE table_schema=current_schema() AND table_name='PackoutReportSources'
          AND column_name=ANY(ARRAY['StorageProvider','StorageKey','StoragePath','DriveId','FileId','FolderId','ParseStatus','UploadedAt','UploadedByUserId'])) <> 9 THEN
        RAISE EXCEPTION 'Packout document storage columns are incomplete';
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
        JOIN pg_index i ON i.indexrelid=c.oid
        WHERE n.nspname=current_schema() AND c.relname='IX_PackoutReportSources_UploadedByUserId'
          AND i.indisvalid AND i.indisready AND NOT i.indisunique
    ) THEN
        RAISE EXCEPTION 'Packout document uploader index is missing or invalid';
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE connamespace=current_schema()::regnamespace
          AND conname='FK_PackoutReportSources_Users_UploadedByUserId' AND convalidated
    ) THEN
        RAISE EXCEPTION 'Packout document uploader foreign key is missing or unvalidated';
    END IF;
    IF EXISTS (
        SELECT 1 FROM "PackoutReportSources"
        WHERE btrim("ParseStatus")=''
           OR ("StorageKey" IS NOT NULL AND ("StorageProvider" IS NULL OR "StoragePath" IS NULL OR "UploadedAt" IS NULL OR "UploadedByUserId" IS NULL))
    ) THEN
        RAISE EXCEPTION 'Packout document storage metadata is internally inconsistent';
    END IF;
END $verify$;
SELECT 'packout_document_storage_schema_verified' AS status,
       11 AS checked_target_objects,
       (SELECT count(*) FROM "PackoutReportSources") AS source_rows,
       (SELECT count(*) FROM "PackoutReportSources" WHERE "StorageKey" IS NOT NULL) AS stored_document_rows;
ROLLBACK;
