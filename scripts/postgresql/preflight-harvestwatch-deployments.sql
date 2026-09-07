-- Read-only HarvestWatch compatibility preflight. State C must stop the release.
DO $$
DECLARE object_count integer;
BEGIN
    SELECT count(*) INTO object_count FROM information_schema.tables
    WHERE table_schema=current_schema() AND table_name IN ('HarvestWatchDeployments','HarvestWatchStatusHistories','HarvestWatchInboundMessages','HarvestWatchMailboxCursors');
    IF object_count = 0 THEN
        RAISE NOTICE 'state_a_absent';
        RETURN;
    END IF;
    IF object_count <> 4 THEN RAISE EXCEPTION 'State C: HarvestWatch table set is partial'; END IF;
    IF (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='HarvestWatchDeployments') <> 25
       OR (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='HarvestWatchStatusHistories') <> 9
       OR (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='HarvestWatchInboundMessages') <> 9
       OR (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='HarvestWatchMailboxCursors') <> 3 THEN
       RAISE EXCEPTION 'State C: HarvestWatch columns are not exact';
    END IF;
    IF (SELECT count(*) FROM pg_indexes WHERE schemaname=current_schema() AND indexname IN (
        'IX_HarvestWatchDeployments_CorrelationToken','IX_HarvestWatchDeployments_DeployedByUserId','IX_HarvestWatchDeployments_HarvestWatchCode_IsActive','IX_HarvestWatchDeployments_RemovedByUserId','IX_HarvestWatchDeployments_RoomId_IsActive','IX_HarvestWatchDeployments_WarehouseId','IX_HarvestWatchInboundMessages_GmailMessageId','IX_HarvestWatchInboundMessages_HarvestWatchDeploymentId','IX_HarvestWatchStatusHistories_HarvestWatchDeploymentId_ChangedAt')) <> 9 THEN
       RAISE EXCEPTION 'State C: HarvestWatch indexes are not exact';
    END IF;
    IF (SELECT count(*) FROM pg_constraint WHERE connamespace=current_schema()::regnamespace AND conname IN (
        'PK_HarvestWatchDeployments','PK_HarvestWatchStatusHistories','PK_HarvestWatchInboundMessages','PK_HarvestWatchMailboxCursors',
        'CK_HarvestWatchDeployments_Code','FK_HarvestWatchDeployments_Rooms_RoomId','FK_HarvestWatchDeployments_Warehouses_WarehouseId',
        'FK_HarvestWatchDeployments_Users_DeployedByUserId','FK_HarvestWatchDeployments_Users_RemovedByUserId',
        'FK_HarvestWatchStatusHistories_HarvestWatchDeployments_HarvestWatchDeploymentId',
        'FK_HarvestWatchInboundMessages_HarvestWatchDeployments_HarvestWatchDeploymentId')) <> 11 THEN
       RAISE EXCEPTION 'State C: HarvestWatch constraints are not exact';
    END IF;
    RAISE NOTICE 'state_b_complete_exact';
END $$;
