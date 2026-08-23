\set ON_ERROR_STOP on
BEGIN TRANSACTION READ ONLY;
DO $preflight$
DECLARE
    existing_count integer;
    exact_count constant integer := 46;
BEGIN
    IF to_regclass(format('%I.%I', current_schema(), 'ActualRuns')) IS NULL
       OR to_regclass(format('%I.%I', current_schema(), 'ActualRunOverrideRequests')) IS NULL
       OR to_regclass(format('%I.%I', current_schema(), 'Users')) IS NULL THEN
        RAISE EXCEPTION 'State C: required parent schema is missing';
    END IF;

    SELECT
        (SELECT count(*) FROM information_schema.tables WHERE table_schema=current_schema() AND table_name IN ('SalesDesks','ActualRunSalesDeskCorrections'))
      + (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND (
            (table_name='ActualRuns' AND column_name IN ('SalesDeskId','SalesDeskNameSnapshot'))
         OR (table_name='ActualRunOverrideRequests' AND column_name IN ('SalesDeskId','SalesDeskNameSnapshot'))
         OR table_name IN ('SalesDesks','ActualRunSalesDeskCorrections')))
      + (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=current_schema() AND c.relkind='i' AND c.relname=ANY(ARRAY(SELECT left(x,63) FROM unnest(ARRAY[
            'IX_ActualRuns_SalesDeskId_Status_RunAt','IX_ActualRunOverrideRequests_SalesDeskId',
            'IX_ActualRunSalesDeskCorrections_ActualRunId_CorrectedAt','IX_ActualRunSalesDeskCorrections_CorrectedByUserId',
            'IX_ActualRunSalesDeskCorrections_NewSalesDeskId','IX_ActualRunSalesDeskCorrections_OperationKey',
            'IX_ActualRunSalesDeskCorrections_PreviousSalesDeskId','IX_SalesDesks_CreatedByUserId',
            'IX_SalesDesks_IsActive_DisplayOrder_Name','IX_SalesDesks_Name','IX_SalesDesks_UpdatedByUserId']) x)))
      + (SELECT count(*) FROM pg_constraint WHERE connamespace=current_schema()::regnamespace AND conname=ANY(ARRAY(SELECT left(x,63) FROM unnest(ARRAY[
            'PK_SalesDesks','PK_ActualRunSalesDeskCorrections','FK_ActualRuns_SalesDesks_SalesDeskId',
            'FK_ActualRunOverrideRequests_SalesDesks_SalesDeskId','FK_SalesDesks_Users_CreatedByUserId',
            'FK_SalesDesks_Users_UpdatedByUserId','FK_ActualRunSalesDeskCorrections_ActualRuns_ActualRunId',
            'FK_ActualRunSalesDeskCorrections_SalesDesks_NewSalesDeskId','FK_ActualRunSalesDeskCorrections_SalesDesks_PreviousSalesDeskId',
            'FK_ActualRunSalesDeskCorrections_Users_CorrectedByUserId']) x)))
    INTO existing_count;

    IF existing_count NOT IN (0, exact_count) THEN
        RAISE EXCEPTION 'State C: partial/conflicting Actual Run Sales Desk schema detected (% of % objects)', existing_count, exact_count;
    END IF;
    IF existing_count = exact_count THEN
        IF (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='SalesDesks') <> 8
           OR (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='ActualRunSalesDeskCorrections') <> 11 THEN
            RAISE EXCEPTION 'State C: Sales Desk table columns are not exact';
        END IF;
        IF EXISTS (SELECT 1 FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid JOIN pg_namespace n ON n.oid=c.relnamespace
                   WHERE n.nspname=current_schema() AND c.relname=ANY(ARRAY(SELECT left(x,63) FROM unnest(ARRAY[
                       'IX_ActualRuns_SalesDeskId_Status_RunAt','IX_ActualRunOverrideRequests_SalesDeskId',
                       'IX_ActualRunSalesDeskCorrections_ActualRunId_CorrectedAt','IX_ActualRunSalesDeskCorrections_CorrectedByUserId',
                       'IX_ActualRunSalesDeskCorrections_NewSalesDeskId','IX_ActualRunSalesDeskCorrections_OperationKey',
                       'IX_ActualRunSalesDeskCorrections_PreviousSalesDeskId','IX_SalesDesks_CreatedByUserId',
                       'IX_SalesDesks_IsActive_DisplayOrder_Name','IX_SalesDesks_Name','IX_SalesDesks_UpdatedByUserId']) x))
                   AND (NOT i.indisvalid OR NOT i.indisready)) THEN
            RAISE EXCEPTION 'State C: Sales Desk indexes are incompatible';
        END IF;
        IF EXISTS (SELECT 1 FROM pg_constraint WHERE connamespace=current_schema()::regnamespace AND conname LIKE '%SalesDesk%' AND NOT convalidated) THEN
            RAISE EXCEPTION 'State C: Sales Desk constraints are not validated';
        END IF;
        IF NOT EXISTS (SELECT 1 FROM "SalesDesks" WHERE "Id"=1 AND "Name"='Domex')
           OR NOT EXISTS (SELECT 1 FROM "SalesDesks" WHERE "Id"=2 AND "Name"='Honey Bear')
           OR NOT EXISTS (SELECT 1 FROM "SalesDesks" WHERE "Id"=3 AND "Name"='Viva Tierra') THEN
            RAISE EXCEPTION 'State C: reviewed initial Sales Desk configuration is missing or conflicting';
        END IF;
    END IF;
END $preflight$;
SELECT CASE WHEN to_regclass(format('%I.%I',current_schema(),'SalesDesks')) IS NULL THEN 'state_a_absent' ELSE 'state_b_complete_exact' END AS compatibility_state;
ROLLBACK;
