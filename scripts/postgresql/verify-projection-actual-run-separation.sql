\set ON_ERROR_STOP on

BEGIN TRANSACTION READ ONLY;

SELECT EXISTS
(
    SELECT 1
    FROM "__EFMigrationsHistory"
    WHERE "MigrationId" = '20260731014107_SeparatePlanningProjectionsFromActualRuns'
) AS migration_applied;

SELECT
    to_regclass('"RunExpectations"') IS NOT NULL AS run_expectations_exists,
    to_regclass('"RunExpectationSources"') IS NOT NULL AS run_expectation_sources_exists,
    to_regclass('"PackoutSourceAllocations"') IS NOT NULL AS packout_allocations_exists;

SELECT column_name, is_nullable, data_type
FROM information_schema.columns
WHERE table_schema = current_schema()
  AND table_name = 'PackoutRuns'
  AND column_name IN ('RunProjectionId', 'ActualRunId', 'RunExpectationId')
ORDER BY column_name;

SELECT "ActualRunId", COUNT(*) AS duplicate_count
FROM "PackoutRuns"
WHERE "ActualRunId" IS NOT NULL
GROUP BY "ActualRunId"
HAVING COUNT(*) > 1;

SELECT "ActualRunId", "RevisionNumber", COUNT(*) AS duplicate_count
FROM "RunExpectations"
GROUP BY "ActualRunId", "RevisionNumber"
HAVING COUNT(*) > 1;

SELECT COUNT(*) AS orphan_expectation_sources
FROM "RunExpectationSources" s
LEFT JOIN "RunExpectations" e ON e."Id" = s."RunExpectationId"
LEFT JOIN "BinsRunEntries" b ON b."Id" = s."BinsRunEntryId"
WHERE e."Id" IS NULL OR b."Id" IS NULL;

SELECT COUNT(*) AS orphan_packout_allocations
FROM "PackoutSourceAllocations" a
LEFT JOIN "PackoutRuns" p ON p."Id" = a."PackoutRunId"
LEFT JOIN "RunExpectationSources" s ON s."Id" = a."RunExpectationSourceId"
WHERE p."Id" IS NULL OR s."Id" IS NULL;

ROLLBACK;
