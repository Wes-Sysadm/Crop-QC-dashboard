\set ON_ERROR_STOP on

BEGIN TRANSACTION READ ONLY;

DO $verify$
DECLARE
    exact_count integer;
    constraint_count integer;
    constraint_hash text;
BEGIN
    SELECT count(*) INTO exact_count
    FROM information_schema.columns
    WHERE table_schema = current_schema() AND table_name = 'QcPhotos'
      AND (
        (column_name = 'OriginalExifOrientation' AND data_type = 'integer' AND is_nullable = 'YES') OR
        (column_name = 'ManualRotationQuarterTurns' AND data_type = 'integer' AND is_nullable = 'NO' AND column_default = '0') OR
        (column_name = 'PresentationRevision' AND data_type = 'integer' AND is_nullable = 'NO' AND column_default = '0') OR
        (column_name = 'PresentationStorageKey' AND data_type = 'character varying' AND character_maximum_length = 1000 AND is_nullable = 'YES') OR
        (column_name = 'PresentationFileName' AND data_type = 'character varying' AND character_maximum_length = 260 AND is_nullable = 'YES') OR
        (column_name = 'PresentationContentType' AND data_type = 'character varying' AND character_maximum_length = 100 AND is_nullable = 'YES') OR
        (column_name = 'PresentationFileSizeBytes' AND data_type = 'bigint' AND is_nullable = 'YES') OR
        (column_name = 'PresentationUpdatedAt' AND data_type = 'timestamp with time zone' AND is_nullable = 'YES')
      );

    SELECT count(*) INTO constraint_count
    FROM pg_constraint c
    JOIN pg_class t ON t.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = t.relnamespace
    WHERE n.nspname = current_schema() AND t.relname = 'QcPhotos'
      AND c.conname IN ('CK_QcPhotos_OrientationState', 'CK_QcPhotos_PresentationMetadata')
      AND c.contype = 'c' AND c.convalidated;

    SELECT md5(string_agg(c.conname || '|' || pg_get_constraintdef(c.oid), ';' ORDER BY c.conname))
    INTO constraint_hash
    FROM pg_constraint c
    JOIN pg_class t ON t.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = t.relnamespace
    WHERE n.nspname = current_schema() AND t.relname = 'QcPhotos'
      AND c.conname IN ('CK_QcPhotos_OrientationState', 'CK_QcPhotos_PresentationMetadata');

    IF exact_count <> 8 OR constraint_count <> 2 OR constraint_hash <> 'd3add83d7c9a978bb903e33e4c4ac4e5' THEN
        RAISE EXCEPTION 'QC photo orientation verification failed: % of 8 exact columns, % of 2 checks.', exact_count, constraint_count;
    END IF;
END $verify$;

SELECT 'qc_photo_orientation_schema_pass' AS status,
       (SELECT count(*) FROM "QcPhotos" WHERE "PresentationStorageKey" IS NOT NULL) AS presentation_rows,
       (SELECT count(*) FROM "__EFMigrationsHistory") AS migration_history_rows;
ROLLBACK;
