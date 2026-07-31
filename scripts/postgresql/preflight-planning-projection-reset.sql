\set ON_ERROR_STOP on

-- Read-only. Run only after a verified production backup has been captured.
BEGIN TRANSACTION READ ONLY;

SELECT
    COUNT(*) FILTER (WHERE NOT "IsDeleted") AS active_projection_count,
    COUNT(*) FILTER (WHERE "IsDeleted") AS already_archived_projection_count,
    COUNT(*) AS total_projection_count
FROM "RunProjections";

SELECT
    p."Id",
    p."Name",
    p."Status",
    p."ProjectionMode",
    p."PlannedRunDate",
    COUNT(DISTINCT s."Id") AS source_count,
    COUNT(DISTINCT a."Id") AS linked_actual_run_count,
    COUNT(DISTINCT k."Id") AS legacy_packout_count
FROM "RunProjections" p
LEFT JOIN "RunProjectionSources" s ON s."RunProjectionId" = p."Id"
LEFT JOIN "ActualRuns" a ON a."RunProjectionId" = p."Id"
LEFT JOIN "PackoutRuns" k ON k."RunProjectionId" = p."Id"
WHERE NOT p."IsDeleted"
GROUP BY p."Id", p."Name", p."Status", p."ProjectionMode", p."PlannedRunDate"
ORDER BY p."Id";

SELECT
    (SELECT COUNT(*) FROM "ActualRuns") AS actual_runs,
    (SELECT COUNT(*) FROM "BinsRunEntries") AS bins_run_entries,
    (SELECT COUNT(*) FROM "RoomInventoryAdjustments") AS room_adjustments,
    (SELECT COUNT(*) FROM "QcSamples") AS qc_samples,
    (SELECT COUNT(*) FROM "Receipts") AS receipts;

ROLLBACK;
