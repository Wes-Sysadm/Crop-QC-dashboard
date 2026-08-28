\set ON_ERROR_STOP on
\ir verify-grower-number-qc-recipients.sql

BEGIN TRANSACTION READ ONLY;
DO $preflight$
DECLARE
    existing_count integer;
    exact_count constant integer := 82;
BEGIN
    SELECT
        (CASE WHEN to_regclass(format('%I.%I', current_schema(), 'OutsideWarehouses')) IS NULL THEN 0 ELSE 1 END)
        + (CASE WHEN to_regclass(format('%I.%I', current_schema(), 'OutsideWarehouseTransfers')) IS NULL THEN 0 ELSE 1 END)
        + (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name IN ('OutsideWarehouses','OutsideWarehouseTransfers'))
        + (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND (table_name,column_name) IN (
            ('RoomInventoryAdjustments','OutsideWarehouseTransferId'),('TreatmentLineageMovements','OutsideWarehouseTransferId')))
        + (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
           WHERE n.nspname=current_schema() AND c.relkind='i' AND c.relname=ANY(ARRAY(SELECT left(x,63) FROM unnest(ARRAY[
            'IX_TreatmentLineageMovements_OutsideWarehouseTransferId','IX_RoomInventoryAdjustments_OutsideWarehouseTransferId',
            'IX_RoomInventoryAdjustments_OutsideWarehouseTransferId_AdjustmentType','IX_OutsideWarehouses_Code',
            'IX_OutsideWarehouses_CreatedByUserId','IX_OutsideWarehouses_IsActive_Name','IX_OutsideWarehouses_UpdatedByUserId',
            'IX_OutsideWarehouseTransfers_CreatedByUserId','IX_OutsideWarehouseTransfers_FruitProfileId',
            'IX_OutsideWarehouseTransfers_GrowerNumberSnapshot','IX_OutsideWarehouseTransfers_OperationKey',
            'IX_OutsideWarehouseTransfers_OutsideWarehouseId','IX_OutsideWarehouseTransfers_ReceiptId',
            'IX_OutsideWarehouseTransfers_ReversalOperationKey','IX_OutsideWarehouseTransfers_ReversedByUserId',
            'IX_OutsideWarehouseTransfers_SourceInventoryAdjustmentId','IX_OutsideWarehouseTransfers_SourceRoomId',
            'IX_OutsideWarehouseTransfers_SourceWarehouseId_SourceRoomId_TransferredAt',
            'IX_OutsideWarehouseTransfers_TransferredAt_OutsideWarehouseId']) x)))
        + (SELECT count(*) FROM pg_constraint WHERE connamespace=current_schema()::regnamespace
           AND conname=ANY(ARRAY(SELECT left(x,63) FROM unnest(ARRAY[
            'PK_OutsideWarehouses','PK_OutsideWarehouseTransfers','FK_OutsideWarehouses_Users_CreatedByUserId',
            'FK_OutsideWarehouses_Users_UpdatedByUserId','FK_OutsideWarehouseTransfers_FruitProfiles_FruitProfileId',
            'FK_OutsideWarehouseTransfers_OutsideWarehouses_OutsideWarehouseId','FK_OutsideWarehouseTransfers_Receipts_ReceiptId',
            'FK_OutsideWarehouseTransfers_RoomInventoryAdjustments_SourceInventoryAdjustmentId',
            'FK_OutsideWarehouseTransfers_Rooms_SourceRoomId','FK_OutsideWarehouseTransfers_Users_CreatedByUserId',
            'FK_OutsideWarehouseTransfers_Users_ReversedByUserId','FK_OutsideWarehouseTransfers_Warehouses_SourceWarehouseId',
            'FK_RoomInventoryAdjustments_OutsideWarehouseTransfers_OutsideWarehouseTransferId',
            'FK_TreatmentLineageMovements_OutsideWarehouseTransfers_OutsideWarehouseTransferId']) x)))
    INTO existing_count;

    IF existing_count NOT IN (0, exact_count) THEN
        RAISE EXCEPTION 'State C: partial/conflicting Outside Warehouse Transfer schema detected (% of % objects)', existing_count, exact_count;
    END IF;
    IF existing_count = exact_count THEN
        IF (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='OutsideWarehouses') <> 10
           OR (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='OutsideWarehouseTransfers') <> 35 THEN
            RAISE EXCEPTION 'State C: Outside Warehouse Transfer table column counts are incompatible';
        END IF;
        IF EXISTS (
            SELECT 1 FROM (VALUES
                ('OutsideWarehouses','Id','integer','NO',NULL::integer,'YES'),('OutsideWarehouses','Name','character varying','NO',200,'NO'),
                ('OutsideWarehouses','Code','character varying','NO',50,'NO'),('OutsideWarehouses','Address','character varying','YES',500,'NO'),
                ('OutsideWarehouses','Notes','character varying','YES',1000,'NO'),('OutsideWarehouses','IsActive','boolean','NO',NULL::integer,'NO'),
                ('OutsideWarehouses','CreatedAt','timestamp with time zone','NO',NULL::integer,'NO'),('OutsideWarehouses','CreatedByUserId','integer','YES',NULL::integer,'NO'),
                ('OutsideWarehouses','UpdatedAt','timestamp with time zone','NO',NULL::integer,'NO'),('OutsideWarehouses','UpdatedByUserId','integer','YES',NULL::integer,'NO'),
                ('OutsideWarehouseTransfers','Id','bigint','NO',NULL::integer,'YES'),('OutsideWarehouseTransfers','OperationKey','character varying','NO',150,'NO'),
                ('OutsideWarehouseTransfers','OutsideWarehouseId','integer','NO',NULL::integer,'NO'),('OutsideWarehouseTransfers','OutsideWarehouseCodeSnapshot','character varying','NO',50,'NO'),
                ('OutsideWarehouseTransfers','OutsideWarehouseNameSnapshot','character varying','NO',200,'NO'),('OutsideWarehouseTransfers','OutsideWarehouseAddressSnapshot','character varying','YES',500,'NO'),
                ('OutsideWarehouseTransfers','SourceWarehouseId','integer','NO',NULL::integer,'NO'),('OutsideWarehouseTransfers','SourceRoomId','integer','NO',NULL::integer,'NO'),
                ('OutsideWarehouseTransfers','ReceiptId','bigint','YES',NULL::integer,'NO'),('OutsideWarehouseTransfers','SourceInventoryAdjustmentId','bigint','YES',NULL::integer,'NO'),
                ('OutsideWarehouseTransfers','CropYear','integer','YES',NULL::integer,'NO'),('OutsideWarehouseTransfers','GrowerLotId','integer','YES',NULL::integer,'NO'),
                ('OutsideWarehouseTransfers','FruitProfileId','integer','YES',NULL::integer,'NO'),('OutsideWarehouseTransfers','GrowerNumberSnapshot','character varying','YES',50,'NO'),
                ('OutsideWarehouseTransfers','GrowerNameSnapshot','character varying','NO',200,'NO'),('OutsideWarehouseTransfers','LotNumberSnapshot','character varying','NO',100,'NO'),
                ('OutsideWarehouseTransfers','VarietyCodeSnapshot','character varying','NO',50,'NO'),('OutsideWarehouseTransfers','ProductionTypeSnapshot','character varying','NO',50,'NO'),
                ('OutsideWarehouseTransfers','IsOrganicSnapshot','boolean','YES',NULL::integer,'NO'),('OutsideWarehouseTransfers','InventoryStatusSnapshot','character varying','YES',100,'NO'),
                ('OutsideWarehouseTransfers','TreatmentStateSnapshot','character varying','NO',25,'NO'),('OutsideWarehouseTransfers','TreatmentSignatureSnapshot','character varying','NO',1000,'NO'),
                ('OutsideWarehouseTransfers','TreatmentSummarySnapshot','character varying','NO',2000,'NO'),('OutsideWarehouseTransfers','BinCount','integer','NO',NULL::integer,'NO'),
                ('OutsideWarehouseTransfers','TransferredAt','timestamp with time zone','NO',NULL::integer,'NO'),('OutsideWarehouseTransfers','TruckLoadBolNumber','character varying','YES',150,'NO'),
                ('OutsideWarehouseTransfers','Notes','character varying','YES',1000,'NO'),('OutsideWarehouseTransfers','CreatedByUserId','integer','NO',NULL::integer,'NO'),
                ('OutsideWarehouseTransfers','CreatedAt','timestamp with time zone','NO',NULL::integer,'NO'),('OutsideWarehouseTransfers','IsReversed','boolean','NO',NULL::integer,'NO'),
                ('OutsideWarehouseTransfers','ReversalOperationKey','character varying','YES',150,'NO'),('OutsideWarehouseTransfers','ReversedAt','timestamp with time zone','YES',NULL::integer,'NO'),
                ('OutsideWarehouseTransfers','ReversedByUserId','integer','YES',NULL::integer,'NO'),('OutsideWarehouseTransfers','ReverseReason','character varying','YES',1000,'NO'),
                ('OutsideWarehouseTransfers','ConcurrencyVersion','bigint','NO',NULL::integer,'NO'),
                ('RoomInventoryAdjustments','OutsideWarehouseTransferId','bigint','YES',NULL::integer,'NO'),
                ('TreatmentLineageMovements','OutsideWarehouseTransferId','bigint','YES',NULL::integer,'NO')
            ) e(table_name,column_name,data_type,is_nullable,maximum_length,is_identity)
            LEFT JOIN information_schema.columns c ON c.table_schema=current_schema() AND c.table_name=e.table_name
              AND c.column_name=e.column_name AND c.data_type=e.data_type AND c.is_nullable=e.is_nullable
              AND c.character_maximum_length IS NOT DISTINCT FROM e.maximum_length AND c.is_identity=e.is_identity
            WHERE c.column_name IS NULL
        ) THEN RAISE EXCEPTION 'State C: Outside Warehouse Transfer columns are incompatible'; END IF;
        IF EXISTS (
            SELECT 1 FROM (VALUES
                ('IX_OutsideWarehouses_Code',true),('IX_OutsideWarehouseTransfers_OperationKey',true),
                ('IX_OutsideWarehouseTransfers_ReversalOperationKey',true),
                ('IX_RoomInventoryAdjustments_OutsideWarehouseTransferId_AdjustmentType',true)
            ) e(name,is_unique)
            LEFT JOIN pg_class c ON c.relname=left(e.name,63)
            LEFT JOIN pg_namespace n ON n.oid=c.relnamespace AND n.nspname=current_schema()
            LEFT JOIN pg_index i ON i.indexrelid=c.oid AND i.indisvalid AND i.indisready
            WHERE n.oid IS NULL OR i.indisunique IS DISTINCT FROM e.is_unique
        ) THEN RAISE EXCEPTION 'State C: Outside Warehouse Transfer unique indexes are incompatible'; END IF;
    END IF;
END $preflight$;

SELECT CASE WHEN to_regclass(format('%I.%I', current_schema(), 'OutsideWarehouses')) IS NULL
       THEN 'state_a_absent' ELSE 'state_b_complete_exact' END AS compatibility_state;
ROLLBACK;
