\set ON_ERROR_STOP on
\ir preflight.sql

BEGIN;
\ir expected_lines.psql

DO $conflicts$
BEGIN
    IF EXISTS (
        SELECT 1 FROM expected_actual_run_facilities e JOIN "ActualRuns" a ON a."Id"=e.actual_run_id
        WHERE (a."RunFacilityWarehouseId" IS NOT NULL AND a."RunFacilityWarehouseId"<>e.warehouse_id)
           OR (a."RunFacilityCodeSnapshot" IS NOT NULL AND a."RunFacilityCodeSnapshot"<>e.facility_code)
           OR (a."RunFacilityAssignmentSource" IS NOT NULL AND a."RunFacilityAssignmentSource"<>'ReviewedProductionBackfill:20260804-run40')
    ) THEN RAISE EXCEPTION 'Conflicting existing Actual Run facility attribution'; END IF;

    IF EXISTS (
        SELECT 1 FROM expected_run_reporting_lines e JOIN "BinsRunEntries" b ON b."Id"=e.entry_id
        WHERE (b."ReportingFacilityWarehouseId" IS NOT NULL AND b."ReportingFacilityWarehouseId"<>CASE e.facility_code WHEN 'WP' THEN 4 WHEN 'EBS' THEN 1 END)
           OR (b."ReportingFacilityCodeSnapshot" IS NOT NULL AND b."ReportingFacilityCodeSnapshot" IS DISTINCT FROM e.facility_code)
           OR (b."ReportingFacilityAssignmentSource" IS NOT NULL AND b."ReportingFacilityAssignmentSource"<>'ReviewedProductionBackfill:20260804-run40')
           OR (b."ReportingCropYearSnapshot" IS NOT NULL AND b."ReportingCropYearSnapshot"<>e.crop_year)
           OR (b."ReportingFruitProfileIdSnapshot" IS NOT NULL AND b."ReportingFruitProfileIdSnapshot"<>e.fruit_profile_id)
           OR (b."ReportingVarietyCodeSnapshot" IS NOT NULL AND b."ReportingVarietyCodeSnapshot"<>e.variety_code)
           OR (b."ProductionTypeSnapshot" IS NOT NULL AND b."ProductionTypeSnapshot"<>e.production_type)
           OR (b."IsOrganicSnapshot" IS NOT NULL AND b."IsOrganicSnapshot"<>e.is_organic)
           OR (b."GrowerNumberSnapshot" IS NOT NULL AND b."GrowerNumberSnapshot"<>e.grower_number)
    ) THEN RAISE EXCEPTION 'Conflicting existing Bins Run reporting snapshot'; END IF;

    IF EXISTS (
        SELECT 1 FROM expected_attribution_users AS expected
        JOIN "Users" AS actual ON actual."Id"=expected.user_id
        WHERE actual."EmploymentFacility" IS DISTINCT FROM 'Unassigned'
          AND actual."EmploymentFacility" IS DISTINCT FROM expected.facility_code
    ) THEN
        RAISE EXCEPTION 'Conflicting existing employment assignment';
    END IF;
END $conflicts$;

CREATE TEMP TABLE pending_users ON COMMIT DROP AS
SELECT u."Id", u."EmploymentFacility" AS before_facility,
       expected.facility_code AS after_facility,
       expected.effective_at
FROM "Users" AS u
JOIN expected_attribution_users AS expected ON expected.user_id=u."Id"
WHERE u."EmploymentFacility" IS DISTINCT FROM expected.facility_code;

UPDATE "Users" u SET
    "EmploymentFacility"=p.after_facility,
    "EmploymentEffectiveAt"=p.effective_at,
    "EmploymentUpdatedByUserId"=NULL,
    "EmploymentUpdatedAt"=transaction_timestamp()
FROM pending_users p WHERE u."Id"=p."Id";

INSERT INTO "UserEmploymentHistory" ("UserId","PreviousEmploymentFacility","EmploymentFacility","EffectiveAt","ChangedByUserId","ChangedAt")
SELECT "Id", before_facility, after_facility, effective_at, NULL, transaction_timestamp()
FROM pending_users;

INSERT INTO "AuditLogs" ("UserId","Action","EntityName","EntityKey","BeforeValuesJson","AfterValuesJson","SourceApplication","CreatedAt")
SELECT NULL, 'ReviewedEmploymentBackfill', 'User', "Id"::text,
       jsonb_build_object('EmploymentFacility',before_facility)::text,
       jsonb_build_object('EmploymentFacility',after_facility,'reviewedBackup','20260804-run40')::text,
       'ops/run-reporting-backfill', transaction_timestamp()
FROM pending_users;

CREATE TEMP TABLE pending_actual_runs ON COMMIT DROP AS
SELECT a."Id", a."RunFacilityWarehouseId" AS before_warehouse_id,
       a."RunFacilityCodeSnapshot" AS before_code, a."RunFacilityAssignmentSource" AS before_source,
       e.warehouse_id, e.facility_code
FROM "ActualRuns" a JOIN expected_actual_run_facilities e ON e.actual_run_id=a."Id"
WHERE a."RunFacilityWarehouseId" IS DISTINCT FROM e.warehouse_id
   OR a."RunFacilityCodeSnapshot" IS DISTINCT FROM e.facility_code
   OR a."RunFacilityAssignmentSource" IS DISTINCT FROM 'ReviewedProductionBackfill:20260804-run40';

UPDATE "ActualRuns" a SET
    "RunFacilityWarehouseId"=p.warehouse_id,
    "RunFacilityCodeSnapshot"=p.facility_code,
    "RunFacilityAssignmentSource"='ReviewedProductionBackfill:20260804-run40',
    "RunFacilityAssignedByUserId"=NULL,
    "RunFacilityAssignedAt"=COALESCE(a."RunFacilityAssignedAt",transaction_timestamp())
FROM pending_actual_runs p WHERE a."Id"=p."Id";

INSERT INTO "AuditLogs" ("UserId","Action","EntityName","EntityKey","BeforeValuesJson","AfterValuesJson","SourceApplication","CreatedAt")
SELECT NULL, 'ReviewedRunFacilityBackfill', 'ActualRun', "Id"::text,
       jsonb_build_object('warehouseId',before_warehouse_id,'facility',before_code,'source',before_source)::text,
       jsonb_build_object('warehouseId',warehouse_id,'facility',facility_code,'source','ReviewedProductionBackfill:20260804-run40')::text,
       'ops/run-reporting-backfill', transaction_timestamp()
FROM pending_actual_runs;

CREATE TEMP TABLE pending_entries ON COMMIT DROP AS
SELECT b."Id", to_jsonb(b) AS before_values
FROM "BinsRunEntries" b JOIN expected_run_reporting_lines e ON e.entry_id=b."Id"
WHERE b."ReportingFacilityWarehouseId" IS DISTINCT FROM CASE e.facility_code WHEN 'WP' THEN 4 WHEN 'EBS' THEN 1 END
   OR b."ReportingFacilityCodeSnapshot" IS DISTINCT FROM e.facility_code
   OR b."ReportingFacilityAssignmentSource" IS DISTINCT FROM 'ReviewedProductionBackfill:20260804-run40'
   OR b."ReportingCropYearSnapshot" IS DISTINCT FROM e.crop_year
   OR b."ReportingFruitProfileIdSnapshot" IS DISTINCT FROM e.fruit_profile_id
   OR b."ReportingVarietyCodeSnapshot" IS DISTINCT FROM e.variety_code
   OR b."ProductionTypeSnapshot" IS DISTINCT FROM e.production_type
   OR b."IsOrganicSnapshot" IS DISTINCT FROM e.is_organic
   OR b."GrowerNumberSnapshot" IS DISTINCT FROM e.grower_number;

UPDATE "BinsRunEntries" b SET
    "ReportingFacilityWarehouseId"=CASE e.facility_code WHEN 'WP' THEN 4 WHEN 'EBS' THEN 1 END,
    "ReportingFacilityCodeSnapshot"=e.facility_code,
    "ReportingFacilityAssignmentSource"='ReviewedProductionBackfill:20260804-run40',
    "ReportingFacilityAssignedByUserId"=NULL,
    "ReportingFacilityAssignedAt"=COALESCE(b."ReportingFacilityAssignedAt",transaction_timestamp()),
    "ReportingCropYearSnapshot"=e.crop_year,
    "ReportingFruitProfileIdSnapshot"=e.fruit_profile_id,
    "ReportingVarietyCodeSnapshot"=e.variety_code,
    "ProductionTypeSnapshot"=e.production_type,
    "IsOrganicSnapshot"=e.is_organic,
    "GrowerNumberSnapshot"=e.grower_number
FROM expected_run_reporting_lines e
JOIN pending_entries p ON p."Id"=e.entry_id
WHERE b."Id"=e.entry_id;

INSERT INTO "AuditLogs" ("UserId","Action","EntityName","EntityKey","BeforeValuesJson","AfterValuesJson","SourceApplication","CreatedAt")
SELECT NULL, 'ReviewedRunReportingBackfill', 'BinsRunEntry', p."Id"::text,
       jsonb_build_object(
         'facility',p.before_values->'ReportingFacilityCodeSnapshot',
         'cropYear',p.before_values->'ReportingCropYearSnapshot',
         'fruitProfileId',p.before_values->'ReportingFruitProfileIdSnapshot',
         'variety',p.before_values->'ReportingVarietyCodeSnapshot',
         'productionType',p.before_values->'ProductionTypeSnapshot',
         'isOrganic',p.before_values->'IsOrganicSnapshot',
         'growerNumber',p.before_values->'GrowerNumberSnapshot')::text,
       jsonb_build_object('facility',e.facility_code,'cropYear',e.crop_year,'fruitProfileId',e.fruit_profile_id,
         'variety',e.variety_code,'productionType',e.production_type,'isOrganic',e.is_organic,
         'growerNumber',e.grower_number,'source','ReviewedProductionBackfill:20260804-run40')::text,
       'ops/run-reporting-backfill', transaction_timestamp()
FROM pending_entries p JOIN expected_run_reporting_lines e ON e.entry_id=p."Id";

SELECT (SELECT COUNT(*) FROM pending_users) AS users_changed,
       (SELECT COUNT(*) FROM pending_actual_runs) AS actual_runs_changed,
       (SELECT COUNT(*) FROM pending_entries) AS entries_changed;
COMMIT;
