\set ON_ERROR_STOP on
\ir preflight.sql
\ir expected_lines.psql
BEGIN TRANSACTION READ ONLY;

DO $verify$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM "Users" WHERE "Id"=8 AND "EmploymentFacility"='WP' AND "EmploymentEffectiveAt"='2026-07-28 05:11:00+00')
       OR NOT EXISTS (SELECT 1 FROM "Users" WHERE "Id"=2 AND "EmploymentFacility"='EBS' AND "EmploymentEffectiveAt"='2026-08-01 01:07:00+00') THEN
        RAISE EXCEPTION 'Employment assignments were not applied exactly';
    END IF;
    IF (SELECT COUNT(*) FROM "UserEmploymentHistory") <> 2
       OR (SELECT COUNT(*) FROM "UserEmploymentHistory" WHERE "UserId"=8 AND "PreviousEmploymentFacility"='Unassigned' AND "EmploymentFacility"='WP' AND "EffectiveAt"='2026-07-28 05:11:00+00') <> 1
       OR (SELECT COUNT(*) FROM "UserEmploymentHistory" WHERE "UserId"=2 AND "PreviousEmploymentFacility"='Unassigned' AND "EmploymentFacility"='EBS' AND "EffectiveAt"='2026-08-01 01:07:00+00') <> 1 THEN
        RAISE EXCEPTION 'Employment history was not applied exactly once';
    END IF;

    IF EXISTS (
        SELECT 1 FROM expected_run_reporting_lines AS e
        JOIN "BinsRunEntries" AS b ON b."Id"=e.entry_id
        WHERE b."ReportingFacilityWarehouseId" IS DISTINCT FROM CASE e.facility_code WHEN 'WP' THEN 4 WHEN 'EBS' THEN 1 END
           OR b."ReportingFacilityCodeSnapshot" IS DISTINCT FROM e.facility_code
           OR b."ReportingFacilityAssignmentSource" IS DISTINCT FROM 'ReviewedProductionBackfill:20260804-run40'
           OR b."ReportingCropYearSnapshot" IS DISTINCT FROM e.crop_year
           OR b."ReportingFruitProfileIdSnapshot" IS DISTINCT FROM e.fruit_profile_id
           OR b."ReportingVarietyCodeSnapshot" IS DISTINCT FROM e.variety_code
           OR b."ProductionTypeSnapshot" IS DISTINCT FROM e.production_type
           OR b."IsOrganicSnapshot" IS DISTINCT FROM e.is_organic
           OR b."GrowerNumberSnapshot" IS DISTINCT FROM e.grower_number
    ) THEN
        RAISE EXCEPTION 'A reporting line does not match the run-40 classification';
    END IF;

    IF EXISTS (
        SELECT 1 FROM expected_actual_run_facilities AS e
        JOIN "ActualRuns" AS a ON a."Id"=e.actual_run_id
        WHERE a."RunFacilityWarehouseId" IS DISTINCT FROM e.warehouse_id
           OR a."RunFacilityCodeSnapshot" IS DISTINCT FROM e.facility_code
           OR a."RunFacilityAssignmentSource" IS DISTINCT FROM 'ReviewedProductionBackfill:20260804-run40'
    ) THEN
        RAISE EXCEPTION 'An Actual Run facility does not match the run-40 classification';
    END IF;

    IF (SELECT COUNT(*) FROM "AuditLogs" WHERE "Action"='ReviewedEmploymentBackfill' AND "SourceApplication"='ops/run-reporting-backfill' AND "EntityKey" IN ('2','8')) <> 2
       OR (SELECT COUNT(*) FROM "AuditLogs" WHERE "Action"='ReviewedRunFacilityBackfill' AND "SourceApplication"='ops/run-reporting-backfill' AND "EntityKey"::bigint IN (1,2,3,4,5,6,7)) <> 7
       OR (SELECT COUNT(*) FROM "AuditLogs" WHERE "Action"='ReviewedRunReportingBackfill' AND "SourceApplication"='ops/run-reporting-backfill' AND "EntityKey"::bigint IN (28,29,30,31,32,34,35,36,37,38,39)) <> 11 THEN
        RAISE EXCEPTION 'Required attribution audits are missing or duplicated';
    END IF;

    IF EXISTS (
        SELECT 1 FROM "BinsRunEntries" WHERE "Id" BETWEEN 1 AND 27
          AND ("ReportingFacilityWarehouseId" IS NOT NULL
            OR "ReportingFacilityCodeSnapshot" IS NOT NULL
            OR "ReportingCropYearSnapshot" IS NOT NULL
            OR "ReportingFruitProfileIdSnapshot" IS NOT NULL
            OR "ReportingVarietyCodeSnapshot" IS NOT NULL
            OR "ProductionTypeSnapshot" IS NOT NULL
            OR "IsOrganicSnapshot" IS NOT NULL
            OR "GrowerNumberSnapshot" IS NOT NULL)
    ) THEN
        RAISE EXCEPTION 'Pre-2026 records were changed';
    END IF;
    IF EXISTS (
        SELECT 1 FROM "BinsRunEntries" WHERE "Id"=33
          AND ("ReportingFacilityWarehouseId" IS NOT NULL
            OR "ReportingFacilityCodeSnapshot" IS NOT NULL
            OR "ReportingCropYearSnapshot" IS NOT NULL
            OR "ReportingFruitProfileIdSnapshot" IS NOT NULL
            OR "ReportingVarietyCodeSnapshot" IS NOT NULL
            OR "ProductionTypeSnapshot" IS NOT NULL
            OR "IsOrganicSnapshot" IS NOT NULL
            OR "GrowerNumberSnapshot" IS NOT NULL)
    ) THEN
        RAISE EXCEPTION 'Invalid authoritative line 33 must remain excluded for Needs Review';
    END IF;
END $verify$;

SELECT facility_code, crop_year, COUNT(*) AS included_lines, SUM(b."BinsRun") AS included_bins
FROM expected_run_reporting_lines AS e
JOIN "BinsRunEntries" AS b ON b."Id"=e.entry_id
GROUP BY facility_code, crop_year
ORDER BY crop_year, facility_code;

SELECT 'Missing authoritative grower number' AS issue_type, COUNT(*) AS records, SUM("BinsRun") AS excluded_bins
FROM "BinsRunEntries"
WHERE "Id"=33
  AND "ReportingCropYearSnapshot" IS NULL
  AND "GrowerNumberSnapshot" IS NULL;
ROLLBACK;
