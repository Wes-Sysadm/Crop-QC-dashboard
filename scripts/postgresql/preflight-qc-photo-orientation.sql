\set ON_ERROR_STOP on

BEGIN TRANSACTION READ ONLY;

DO $preflight$
DECLARE
    existing_count integer;
    exact_count integer;
    constraint_hash text;
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
        RAISE NOTICE 'State A: QC photo orientation schema is absent and safe to apply.';
        RETURN;
    END IF;

    IF existing_count <> 10 THEN
        RAISE EXCEPTION 'State C: partial QC photo orientation schema detected (% of 10 target objects).', existing_count;
    END IF;

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

    SELECT md5(string_agg(c.conname || '|' || pg_get_constraintdef(c.oid), ';' ORDER BY c.conname))
    INTO constraint_hash
    FROM pg_constraint c
    JOIN pg_class t ON t.oid = c.conrelid
    JOIN pg_namespace n ON n.oid = t.relnamespace
    WHERE n.nspname = current_schema() AND t.relname = 'QcPhotos'
      AND c.conname IN ('CK_QcPhotos_OrientationState', 'CK_QcPhotos_PresentationMetadata');

    IF exact_count <> 8 OR constraint_hash <> 'd3add83d7c9a978bb903e33e4c4ac4e5' OR EXISTS (
        SELECT 1 FROM pg_constraint c
        JOIN pg_class t ON t.oid = c.conrelid
        JOIN pg_namespace n ON n.oid = t.relnamespace
        WHERE n.nspname = current_schema() AND t.relname = 'QcPhotos'
          AND c.conname IN ('CK_QcPhotos_OrientationState', 'CK_QcPhotos_PresentationMetadata')
          AND (c.contype <> 'c' OR NOT c.convalidated)
    ) THEN
        RAISE EXCEPTION 'State C: QC photo orientation objects exist but are incompatible.';
    END IF;

    RAISE NOTICE 'State B: QC photo orientation schema is complete.';
END $preflight$;

SELECT 'qc_photo_orientation_preflight_pass' AS status;
ROLLBACK;
