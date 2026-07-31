\set ON_ERROR_STOP on

BEGIN TRANSACTION READ ONLY;

SELECT current_database() AS database_name, version() AS provider_version;

SELECT "MigrationId", "ProductVersion"
FROM "__EFMigrationsHistory"
ORDER BY "MigrationId" DESC
LIMIT 10;

SELECT
    (SELECT COUNT(*) FROM "RunProjections") AS planning_projections,
    (SELECT COUNT(*) FROM "ActualRuns") AS actual_runs,
    (SELECT COUNT(*) FROM "PackoutRuns") AS packout_results,
    (SELECT COUNT(*) FROM "BinsRunEntries") AS bins_run_entries,
    (SELECT COUNT(*) FROM "RoomInventoryAdjustments") AS room_adjustments;

SELECT
    COUNT(*) FILTER (WHERE b."ActualRunId" IS NOT NULL) AS packouts_with_actual_run_evidence,
    COUNT(*) FILTER (WHERE b."ActualRunId" IS NULL) AS legacy_packouts_without_actual_run_evidence
FROM "PackoutRuns" p
LEFT JOIN "BinsRunEntries" b ON b."Id" = p."BinsRunEntryId";

SELECT
    b."ActualRunId",
    COUNT(*) AS legacy_packout_count
FROM "PackoutRuns" p
JOIN "BinsRunEntries" b ON b."Id" = p."BinsRunEntryId"
WHERE b."ActualRunId" IS NOT NULL
GROUP BY b."ActualRunId"
HAVING COUNT(*) > 1
ORDER BY b."ActualRunId";

ROLLBACK;
