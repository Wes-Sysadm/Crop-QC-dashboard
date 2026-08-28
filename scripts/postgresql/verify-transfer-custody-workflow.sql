\set ON_ERROR_STOP on
BEGIN TRANSACTION READ ONLY;
DO $verify$
BEGIN
    IF to_regclass(format('%I.%I', current_schema(), 'OutsideWarehouses')) IS NULL
       OR to_regclass(format('%I.%I', current_schema(), 'OutsideWarehouseTransfers')) IS NULL
       OR to_regclass(format('%I.%I', current_schema(), 'InterCrewTransfers')) IS NULL THEN
        RAISE EXCEPTION 'Transfer Custody Workflow tables are missing';
    END IF;
    IF (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='OutsideWarehouses') <> 10
       OR (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='OutsideWarehouseTransfers') <> 35
       OR (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='InterCrewTransfers') <> 44 THEN
        RAISE EXCEPTION 'Transfer Custody Workflow table columns are not exact';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='RoomInventoryAdjustments' AND column_name='OutsideWarehouseTransferId' AND data_type='bigint' AND is_nullable='YES')
       OR NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='TreatmentLineageMovements' AND column_name='OutsideWarehouseTransferId' AND data_type='bigint' AND is_nullable='YES')
       OR NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='RoomInventoryAdjustments' AND column_name='InterCrewTransferId' AND data_type='bigint' AND is_nullable='YES')
       OR NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='TreatmentLineageMovements' AND column_name='InterCrewTransferId' AND data_type='bigint' AND is_nullable='YES') THEN
        RAISE EXCEPTION 'Transfer Custody Workflow parent columns are incomplete';
    END IF;
    IF (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=current_schema() AND c.relkind='i'
        AND c.relname=ANY(ARRAY(SELECT left(x,63) FROM unnest(ARRAY[
            'IX_TreatmentLineageMovements_OutsideWarehouseTransferId','IX_RoomInventoryAdjustments_OutsideWarehouseTransferId',
            'IX_RoomInventoryAdjustments_OutsideWarehouseTransferId_AdjustmentType','IX_OutsideWarehouses_Code',
            'IX_OutsideWarehouses_CreatedByUserId','IX_OutsideWarehouses_IsActive_Name','IX_OutsideWarehouses_UpdatedByUserId',
            'IX_OutsideWarehouseTransfers_CreatedByUserId','IX_OutsideWarehouseTransfers_FruitProfileId','IX_OutsideWarehouseTransfers_GrowerNumberSnapshot',
            'IX_OutsideWarehouseTransfers_OperationKey','IX_OutsideWarehouseTransfers_OutsideWarehouseId','IX_OutsideWarehouseTransfers_ReceiptId',
            'IX_OutsideWarehouseTransfers_ReversalOperationKey','IX_OutsideWarehouseTransfers_ReversedByUserId',
            'IX_OutsideWarehouseTransfers_SourceInventoryAdjustmentId','IX_OutsideWarehouseTransfers_SourceRoomId',
            'IX_OutsideWarehouseTransfers_SourceWarehouseId_SourceRoomId_TransferredAt','IX_OutsideWarehouseTransfers_TransferredAt_OutsideWarehouseId',
            'IX_TreatmentLineageMovements_InterCrewTransferId','IX_RoomInventoryAdjustments_InterCrewTransferId',
            'IX_RoomInventoryAdjustments_InterCrewTransferId_AdjustmentType','IX_InterCrewTransfers_DestinationCustodyGroup_Status_LoadedAt',
            'IX_InterCrewTransfers_DestinationRoomId','IX_InterCrewTransfers_DestinationWarehouseId','IX_InterCrewTransfers_FruitProfileId',
            'IX_InterCrewTransfers_LoadedByUserId','IX_InterCrewTransfers_OperationKey','IX_InterCrewTransfers_ReceiptId',
            'IX_InterCrewTransfers_ReceivedByUserId','IX_InterCrewTransfers_ReceiveOperationKey','IX_InterCrewTransfers_ReversalOperationKey',
            'IX_InterCrewTransfers_ReversedByUserId','IX_InterCrewTransfers_ReviewedByUserId','IX_InterCrewTransfers_ReviewOperationKey',
            'IX_InterCrewTransfers_SourceInventoryAdjustmentId','IX_InterCrewTransfers_SourceRoomId_LoadedAt','IX_InterCrewTransfers_SourceWarehouseId']) x))) <> 38 THEN
        RAISE EXCEPTION 'Transfer Custody Workflow indexes are incomplete';
    END IF;
    IF (SELECT count(*) FROM pg_constraint WHERE connamespace=current_schema()::regnamespace
        AND conname=ANY(ARRAY(SELECT left(x,63) FROM unnest(ARRAY[
            'PK_OutsideWarehouses','PK_OutsideWarehouseTransfers','FK_OutsideWarehouses_Users_CreatedByUserId',
            'FK_OutsideWarehouses_Users_UpdatedByUserId','FK_OutsideWarehouseTransfers_FruitProfiles_FruitProfileId',
            'FK_OutsideWarehouseTransfers_OutsideWarehouses_OutsideWarehouseId','FK_OutsideWarehouseTransfers_Receipts_ReceiptId',
            'FK_OutsideWarehouseTransfers_RoomInventoryAdjustments_SourceInventoryAdjustmentId','FK_OutsideWarehouseTransfers_Rooms_SourceRoomId',
            'FK_OutsideWarehouseTransfers_Users_CreatedByUserId','FK_OutsideWarehouseTransfers_Users_ReversedByUserId',
            'FK_OutsideWarehouseTransfers_Warehouses_SourceWarehouseId','FK_RoomInventoryAdjustments_OutsideWarehouseTransfers_OutsideWarehouseTransferId',
            'FK_TreatmentLineageMovements_OutsideWarehouseTransfers_OutsideWarehouseTransferId','PK_InterCrewTransfers',
            'FK_InterCrewTransfers_FruitProfiles_FruitProfileId','FK_InterCrewTransfers_Receipts_ReceiptId',
            'FK_InterCrewTransfers_RoomInventoryAdjustments_SourceInventoryAdjustmentId','FK_InterCrewTransfers_Rooms_DestinationRoomId',
            'FK_InterCrewTransfers_Rooms_SourceRoomId','FK_InterCrewTransfers_Users_LoadedByUserId',
            'FK_InterCrewTransfers_Users_ReceivedByUserId','FK_InterCrewTransfers_Users_ReversedByUserId',
            'FK_InterCrewTransfers_Users_ReviewedByUserId','FK_InterCrewTransfers_Warehouses_DestinationWarehouseId',
            'FK_InterCrewTransfers_Warehouses_SourceWarehouseId','FK_RoomInventoryAdjustments_InterCrewTransfers_InterCrewTransferId',
            'FK_TreatmentLineageMovements_InterCrewTransfers_InterCrewTransferId']) x))) <> 28 THEN
        RAISE EXCEPTION 'Transfer Custody Workflow constraints are incomplete';
    END IF;
END $verify$;
SELECT 'transfer_custody_workflow_schema_verified' AS status,
       162 AS checked_target_objects,
       (SELECT count(*) FROM "OutsideWarehouses") AS outside_warehouse_rows,
       (SELECT count(*) FROM "OutsideWarehouseTransfers") AS outside_transfer_rows,
       (SELECT count(*) FROM "InterCrewTransfers") AS inter_crew_transfer_rows;
ROLLBACK;
