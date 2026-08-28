\set ON_ERROR_STOP on
BEGIN TRANSACTION READ ONLY;
DO $verify$
BEGIN
    IF to_regclass(format('%I.%I', current_schema(), 'OutsideWarehouses')) IS NULL
       OR to_regclass(format('%I.%I', current_schema(), 'OutsideWarehouseTransfers')) IS NULL THEN
        RAISE EXCEPTION 'Outside Warehouse Transfer tables are missing';
    END IF;
    IF (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='OutsideWarehouses') <> 10
       OR (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='OutsideWarehouseTransfers') <> 35 THEN
        RAISE EXCEPTION 'Outside Warehouse Transfer table columns are not exact';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='RoomInventoryAdjustments' AND column_name='OutsideWarehouseTransferId' AND data_type='bigint' AND is_nullable='YES')
       OR NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='TreatmentLineageMovements' AND column_name='OutsideWarehouseTransferId' AND data_type='bigint' AND is_nullable='YES') THEN
        RAISE EXCEPTION 'Outside Warehouse Transfer parent columns are incomplete';
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
            'IX_OutsideWarehouseTransfers_SourceWarehouseId_SourceRoomId_TransferredAt','IX_OutsideWarehouseTransfers_TransferredAt_OutsideWarehouseId']) x))) <> 19 THEN
        RAISE EXCEPTION 'Outside Warehouse Transfer indexes are incomplete';
    END IF;
    IF (SELECT count(*) FROM pg_constraint WHERE connamespace=current_schema()::regnamespace
        AND conname=ANY(ARRAY(SELECT left(x,63) FROM unnest(ARRAY[
            'PK_OutsideWarehouses','PK_OutsideWarehouseTransfers','FK_OutsideWarehouses_Users_CreatedByUserId',
            'FK_OutsideWarehouses_Users_UpdatedByUserId','FK_OutsideWarehouseTransfers_FruitProfiles_FruitProfileId',
            'FK_OutsideWarehouseTransfers_OutsideWarehouses_OutsideWarehouseId','FK_OutsideWarehouseTransfers_Receipts_ReceiptId',
            'FK_OutsideWarehouseTransfers_RoomInventoryAdjustments_SourceInventoryAdjustmentId','FK_OutsideWarehouseTransfers_Rooms_SourceRoomId',
            'FK_OutsideWarehouseTransfers_Users_CreatedByUserId','FK_OutsideWarehouseTransfers_Users_ReversedByUserId',
            'FK_OutsideWarehouseTransfers_Warehouses_SourceWarehouseId','FK_RoomInventoryAdjustments_OutsideWarehouseTransfers_OutsideWarehouseTransferId',
            'FK_TreatmentLineageMovements_OutsideWarehouseTransfers_OutsideWarehouseTransferId']) x))) <> 14 THEN
        RAISE EXCEPTION 'Outside Warehouse Transfer constraints are incomplete';
    END IF;
END $verify$;
SELECT 'outside_warehouse_transfer_schema_verified' AS status,
       82 AS checked_target_objects,
       (SELECT count(*) FROM "OutsideWarehouses") AS outside_warehouse_rows,
       (SELECT count(*) FROM "OutsideWarehouseTransfers") AS outside_transfer_rows;
ROLLBACK;
