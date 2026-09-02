\set ON_ERROR_STOP on

BEGIN TRANSACTION READ ONLY;

DO $preflight$
DECLARE
    existing_count integer;
    exact_count constant integer := 18;
    exact_columns integer;
BEGIN
    IF to_regclass(format('%I.%I', current_schema(), 'ActualRuns')) IS NULL
       OR to_regclass(format('%I.%I', current_schema(), 'Users')) IS NULL THEN
        RAISE EXCEPTION 'State C: required Actual Run detail correction parent schema is missing';
    END IF;

    SELECT
        (SELECT count(*) FROM information_schema.tables WHERE table_schema=current_schema() AND table_name='ActualRunDetailCorrections')
      + (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='ActualRunDetailCorrections')
      + (SELECT count(*) FROM pg_indexes WHERE schemaname=current_schema() AND indexname IN (
            'IX_ActualRunDetailCorrections_ActualRunId_CorrectedAt',
            'IX_ActualRunDetailCorrections_CorrectedByUserId',
            'IX_ActualRunDetailCorrections_OperationKey'))
      + (SELECT count(*) FROM pg_constraint WHERE connamespace=current_schema()::regnamespace AND conname IN (
            'PK_ActualRunDetailCorrections',
            'FK_ActualRunDetailCorrections_ActualRuns_ActualRunId',
            'FK_ActualRunDetailCorrections_Users_CorrectedByUserId'))
    INTO existing_count;

    IF existing_count = 0 THEN
        RAISE NOTICE 'State A: Actual Run detail correction schema is absent and safe to apply.';
        RETURN;
    END IF;
    IF existing_count <> exact_count THEN
        RAISE EXCEPTION 'State C: partial/conflicting Actual Run detail correction schema detected (% of % objects)', existing_count, exact_count;
    END IF;

    SELECT count(*) INTO exact_columns
    FROM information_schema.columns
    WHERE table_schema=current_schema() AND table_name='ActualRunDetailCorrections'
      AND ((column_name IN ('Id','ActualRunId','ExpectedConcurrencyVersion') AND data_type='bigint' AND is_nullable='NO')
        OR (column_name='OperationKey' AND data_type='character varying' AND character_maximum_length=64 AND is_nullable='NO')
        OR (column_name IN ('PreviousRunAt','NewRunAt','CorrectedAt') AND data_type='timestamp with time zone' AND is_nullable='NO')
        OR (column_name IN ('PreviousNotes','NewNotes') AND data_type='character varying' AND character_maximum_length=1000 AND is_nullable='YES')
        OR (column_name='Reason' AND data_type='character varying' AND character_maximum_length=1000 AND is_nullable='NO')
        OR (column_name='CorrectedByUserId' AND data_type='integer' AND is_nullable='NO'));
    IF exact_columns <> 11 THEN
        RAISE EXCEPTION 'State C: Actual Run detail correction columns are incompatible (% of 11 exact)', exact_columns;
    END IF;
    IF NOT EXISTS (
        SELECT 1 FROM pg_index i
        JOIN pg_class c ON c.oid=i.indexrelid
        JOIN pg_namespace n ON n.oid=c.relnamespace
        WHERE n.nspname=current_schema()
          AND c.relname='IX_ActualRunDetailCorrections_OperationKey'
          AND i.indisunique AND i.indisvalid AND i.indisready) THEN
        RAISE EXCEPTION 'State C: Actual Run detail correction operation-key index is incompatible';
    END IF;
    IF EXISTS (
        SELECT 1 FROM pg_constraint
        WHERE connamespace=current_schema()::regnamespace
          AND conname IN ('PK_ActualRunDetailCorrections','FK_ActualRunDetailCorrections_ActualRuns_ActualRunId','FK_ActualRunDetailCorrections_Users_CorrectedByUserId')
          AND NOT convalidated) THEN
        RAISE EXCEPTION 'State C: Actual Run detail correction constraints are not validated';
    END IF;
    RAISE NOTICE 'State B: Actual Run detail correction schema is complete and exact.';
END $preflight$;

SELECT CASE WHEN to_regclass(format('%I.%I',current_schema(),'ActualRunDetailCorrections')) IS NULL
    THEN 'state_a_absent' ELSE 'state_b_complete_exact' END AS compatibility_state;
ROLLBACK;
