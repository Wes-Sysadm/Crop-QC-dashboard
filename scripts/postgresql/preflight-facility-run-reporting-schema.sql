\set ON_ERROR_STOP on
\ir verify-projection-actual-run-separation.sql
BEGIN TRANSACTION READ ONLY;

DO $preflight$
DECLARE
    existing_target_count integer;
BEGIN
    IF EXISTS (SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId"='20260804052104_AddFacilityRunReporting') THEN
        RAISE EXCEPTION 'Facility reporting migration is already recorded; use verify-facility-run-reporting.sql instead';
    END IF;

    SELECT
        (CASE WHEN to_regclass(format('%I.%I', current_schema(), 'UserEmploymentHistory')) IS NULL THEN 0 ELSE 1 END)
        + (SELECT COUNT(*) FROM information_schema.columns
           WHERE table_schema=current_schema()
             AND ((table_name='Users' AND column_name IN ('EmploymentEffectiveAt','EmploymentFacility','EmploymentUpdatedAt','EmploymentUpdatedByUserId'))
               OR (table_name='ActualRuns' AND column_name IN ('RunFacilityAssignedAt','RunFacilityAssignedByUserId','RunFacilityAssignmentSource','RunFacilityCodeSnapshot','RunFacilityWarehouseId'))
               OR (table_name='ActualRunOverrideRequests' AND column_name IN ('RunFacilityAssignmentSource','RunFacilityCodeSnapshot','RunFacilityWarehouseId'))
               OR (table_name='BinsRunEntries' AND column_name IN ('GrowerNumberSnapshot','IsOrganicSnapshot','ProductionTypeSnapshot','ReportingCropYearSnapshot','ReportingFacilityAssignedAt','ReportingFacilityAssignedByUserId','ReportingFacilityAssignmentSource','ReportingFacilityCodeSnapshot','ReportingFacilityWarehouseId','ReportingFruitProfileIdSnapshot','ReportingVarietyCodeSnapshot'))))
    INTO existing_target_count;

    IF existing_target_count <> 0 THEN
        RAISE EXCEPTION 'Unexpected partial facility-reporting schema detected (% of 24 target table/columns)', existing_target_count;
    END IF;
END $preflight$;

SELECT 'facility_reporting_schema_preflight_ready' AS status,
       '20260804052104_AddFacilityRunReporting' AS migration;
ROLLBACK;
