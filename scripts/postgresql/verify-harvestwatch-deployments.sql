DO $$
BEGIN
    IF (SELECT count(*) FROM information_schema.tables WHERE table_schema=current_schema() AND table_name IN ('HarvestWatchDeployments','HarvestWatchStatusHistories','HarvestWatchInboundMessages','HarvestWatchMailboxCursors')) <> 4 THEN RAISE EXCEPTION 'HarvestWatch tables missing'; END IF;
    IF (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='HarvestWatchDeployments') <> 25 THEN RAISE EXCEPTION 'HarvestWatchDeployments columns are not exact'; END IF;
    IF (SELECT count(*) FROM pg_constraint WHERE connamespace=current_schema()::regnamespace AND conname IN ('CK_HarvestWatchDeployments_Code','FK_HarvestWatchDeployments_Rooms_RoomId','FK_HarvestWatchDeployments_Warehouses_WarehouseId','FK_HarvestWatchDeployments_Users_DeployedByUserId','FK_HarvestWatchDeployments_Users_RemovedByUserId','FK_HarvestWatchStatusHistories_HarvestWatchDeployments_HarvestWatchDeploymentId','FK_HarvestWatchInboundMessages_HarvestWatchDeployments_HarvestWatchDeploymentId')) <> 7 THEN RAISE EXCEPTION 'HarvestWatch constraints missing'; END IF;
END $$;
SELECT 'PASS' AS harvestwatch_schema_verification, (SELECT count(*) FROM "HarvestWatchDeployments") AS deployment_rows;
