\set ON_ERROR_STOP on
\ir preflight-packout-document-storage.sql

BEGIN;
SET LOCAL lock_timeout='15s';
SET LOCAL statement_timeout='10min';
SELECT pg_advisory_xact_lock(hashtextextended('CropQc:20260824233548_AddPackoutDocumentStorageMetadata', 0));
SELECT NOT EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_schema=current_schema() AND table_name='PackoutReportSources' AND column_name='StorageKey') AS create_target \gset

\if :create_target
ALTER TABLE "PackoutReportSources" ADD "DriveId" character varying(250);
ALTER TABLE "PackoutReportSources" ADD "FileId" character varying(250);
ALTER TABLE "PackoutReportSources" ADD "FolderId" character varying(250);
ALTER TABLE "PackoutReportSources" ADD "ParseStatus" character varying(50) NOT NULL DEFAULT 'Legacy metadata only';
ALTER TABLE "PackoutReportSources" ADD "StorageKey" character varying(500);
ALTER TABLE "PackoutReportSources" ADD "StoragePath" character varying(1000);
ALTER TABLE "PackoutReportSources" ADD "StorageProvider" character varying(50);
ALTER TABLE "PackoutReportSources" ADD "UploadedAt" timestamp with time zone;
ALTER TABLE "PackoutReportSources" ADD "UploadedByUserId" integer;
CREATE INDEX "IX_PackoutReportSources_UploadedByUserId" ON "PackoutReportSources" ("UploadedByUserId");
ALTER TABLE "PackoutReportSources" ADD CONSTRAINT "FK_PackoutReportSources_Users_UploadedByUserId"
    FOREIGN KEY ("UploadedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL;
\else
\echo 'Packout document storage schema is already complete and exact; no DDL will be applied.'
\endif

DO $postcheck$
BEGIN
    IF current_setting('cropqc.test_force_packout_document_failure', true)='on' THEN
        RAISE EXCEPTION 'Forced Packout document compatibility failure for rollback regression';
    END IF;
END $postcheck$;
COMMIT;
\ir verify-packout-document-storage.sql
