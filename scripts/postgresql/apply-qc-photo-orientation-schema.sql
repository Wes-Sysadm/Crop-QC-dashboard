\set ON_ERROR_STOP on
\ir preflight-qc-photo-orientation.sql

BEGIN;
SET LOCAL lock_timeout = '15s';
SET LOCAL statement_timeout = '10min';
SELECT pg_advisory_xact_lock(hashtextextended('CropQc:20260904030132_ReintroduceQcPhotoOrientation', 0));

DO $apply$
DECLARE
    existing_count integer;
BEGIN
    IF to_regclass(format('%I.%I', current_schema(), 'QcPhotos')) IS NULL THEN
        RAISE EXCEPTION 'State C: required QcPhotos table is absent.';
    END IF;

    SELECT
        (SELECT count(*) FROM information_schema.columns
         WHERE table_schema = current_schema() AND table_name = 'QcPhotos'
           AND column_name IN ('OriginalExifOrientation', 'ManualRotationQuarterTurns',
                               'PresentationRevision', 'PresentationStorageKey',
                               'PresentationFileName', 'PresentationContentType',
                               'PresentationFileSizeBytes', 'PresentationUpdatedAt'))
        +
        (SELECT count(*) FROM pg_constraint c
         JOIN pg_class t ON t.oid = c.conrelid
         JOIN pg_namespace n ON n.oid = t.relnamespace
         WHERE n.nspname = current_schema() AND t.relname = 'QcPhotos'
           AND c.conname IN ('CK_QcPhotos_OrientationState', 'CK_QcPhotos_PresentationMetadata'))
    INTO existing_count;

    IF existing_count = 0 THEN
        ALTER TABLE "QcPhotos" ADD COLUMN "ManualRotationQuarterTurns" integer NOT NULL DEFAULT 0;
        ALTER TABLE "QcPhotos" ADD COLUMN "OriginalExifOrientation" integer;
        ALTER TABLE "QcPhotos" ADD COLUMN "PresentationContentType" character varying(100);
        ALTER TABLE "QcPhotos" ADD COLUMN "PresentationFileName" character varying(260);
        ALTER TABLE "QcPhotos" ADD COLUMN "PresentationFileSizeBytes" bigint;
        ALTER TABLE "QcPhotos" ADD COLUMN "PresentationRevision" integer NOT NULL DEFAULT 0;
        ALTER TABLE "QcPhotos" ADD COLUMN "PresentationStorageKey" character varying(1000);
        ALTER TABLE "QcPhotos" ADD COLUMN "PresentationUpdatedAt" timestamp with time zone;

        ALTER TABLE "QcPhotos" ADD CONSTRAINT "CK_QcPhotos_OrientationState"
            CHECK ("ManualRotationQuarterTurns" BETWEEN 0 AND 3
               AND "PresentationRevision" >= 0
               AND ("OriginalExifOrientation" IS NULL OR "OriginalExifOrientation" BETWEEN 1 AND 8));

        ALTER TABLE "QcPhotos" ADD CONSTRAINT "CK_QcPhotos_PresentationMetadata"
            CHECK (("PresentationStorageKey" IS NULL
                    AND "PresentationFileName" IS NULL
                    AND "PresentationContentType" IS NULL
                    AND "PresentationFileSizeBytes" IS NULL
                    AND "PresentationUpdatedAt" IS NULL)
                OR ("PresentationStorageKey" IS NOT NULL
                    AND "PresentationFileName" IS NOT NULL
                    AND "PresentationContentType" IS NOT NULL
                    AND "PresentationFileSizeBytes" >= 0
                    AND "PresentationUpdatedAt" IS NOT NULL
                    AND "PresentationRevision" > 0));
    ELSIF existing_count <> 10 THEN
        RAISE EXCEPTION 'State C: partial QC photo orientation schema detected (% of 10 target objects). Transaction rolled back.', existing_count;
    END IF;

    IF current_setting('cropqc.test_force_qc_photo_orientation_failure', true) = 'on' THEN
        RAISE EXCEPTION 'Forced QC photo orientation compatibility failure for rollback validation.';
    END IF;
END $apply$;

COMMIT;
\ir verify-qc-photo-orientation.sql
