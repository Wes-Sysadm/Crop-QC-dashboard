\set ON_ERROR_STOP on
BEGIN TRANSACTION READ ONLY;
DO $verify$
BEGIN
    IF to_regclass(format('%I.%I',current_schema(),'SalesDesks')) IS NULL OR to_regclass(format('%I.%I',current_schema(),'ActualRunSalesDeskCorrections')) IS NULL THEN RAISE EXCEPTION 'Sales Desk tables are missing'; END IF;
    IF (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='SalesDesks') <> 8 THEN RAISE EXCEPTION 'SalesDesks columns are incomplete'; END IF;
    IF (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='ActualRunSalesDeskCorrections') <> 11 THEN RAISE EXCEPTION 'ActualRunSalesDeskCorrections columns are incomplete'; END IF;
    IF (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='ActualRuns' AND column_name IN ('SalesDeskId','SalesDeskNameSnapshot')) <> 2 THEN RAISE EXCEPTION 'ActualRuns Sales Desk columns are incomplete'; END IF;
    IF (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='ActualRunOverrideRequests' AND column_name IN ('SalesDeskId','SalesDeskNameSnapshot')) <> 2 THEN RAISE EXCEPTION 'ActualRunOverrideRequests Sales Desk columns are incomplete'; END IF;
    IF EXISTS (SELECT 1 FROM "ActualRuns" WHERE "RunFacilityCodeSnapshot"='EBS' AND ("SalesDeskId" IS NOT NULL OR "SalesDeskNameSnapshot" IS NOT NULL)) THEN RAISE EXCEPTION 'EBS Actual Run has invalid Sales Desk attribution'; END IF;
    IF EXISTS (SELECT 1 FROM "ActualRuns" WHERE "SalesDeskId" IS NULL AND "SalesDeskNameSnapshot" IS NOT NULL) THEN RAISE EXCEPTION 'Actual Run Sales Desk snapshot has no parent'; END IF;
    IF EXISTS (SELECT 1 FROM "ActualRuns" r JOIN "SalesDesks" s ON s."Id"=r."SalesDeskId" WHERE r."SalesDeskNameSnapshot" IS DISTINCT FROM s."Name") THEN RAISE EXCEPTION 'Actual Run Sales Desk snapshot does not match its assigned master row'; END IF;
    IF (SELECT count(*) FROM "SalesDesks" WHERE ("Id","Name") IN ((1,'Domex'),(2,'Honey Bear'),(3,'Viva Tierra'))) <> 3 THEN RAISE EXCEPTION 'Reviewed initial Sales Desks are not exact'; END IF;
    IF EXISTS (SELECT 1 FROM pg_constraint WHERE connamespace=current_schema()::regnamespace AND conname LIKE '%SalesDesk%' AND NOT convalidated) THEN RAISE EXCEPTION 'Sales Desk constraint is not validated'; END IF;
END $verify$;
SELECT 'actual_run_sales_desk_schema_verified' AS status,
       46 AS checked_target_objects,
       (SELECT count(*) FROM "SalesDesks") AS sales_desk_rows,
       (SELECT count(*) FROM "ActualRunSalesDeskCorrections") AS correction_rows,
       (SELECT count(*) FROM "ActualRuns" WHERE "RunFacilityCodeSnapshot"='WP' AND "SalesDeskId" IS NULL) AS unassigned_wp_runs,
       (SELECT count(*) FROM "__EFMigrationsHistory") AS migration_history_rows;
ROLLBACK;
