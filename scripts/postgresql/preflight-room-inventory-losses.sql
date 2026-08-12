\set ON_ERROR_STOP on
BEGIN TRANSACTION READ ONLY;

DO $preflight$
DECLARE
    target_oid oid := to_regclass(format('%I.%I', current_schema(), 'RoomInventoryLosses'));
    adjustment_oid oid := to_regclass(format('%I.%I', current_schema(), 'RoomInventoryAdjustments'));
    mismatch text;
    has_column boolean;
BEGIN
    IF adjustment_oid IS NULL THEN
        RAISE EXCEPTION 'Missing prerequisite table RoomInventoryAdjustments.';
    END IF;
    SELECT EXISTS (
        SELECT 1 FROM information_schema.columns
        WHERE table_schema=current_schema() AND table_name='RoomInventoryAdjustments' AND column_name='RoomInventoryLossId')
    INTO has_column;

    IF target_oid IS NULL AND NOT has_column THEN
        IF EXISTS (
            SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE n.nspname=current_schema() AND c.relname LIKE '%RoomInventoryLoss%')
        OR EXISTS (
            SELECT 1 FROM pg_constraint c JOIN pg_namespace n ON n.oid=c.connamespace
            WHERE n.nspname=current_schema() AND c.conname LIKE '%RoomInventoryLoss%') THEN
            RAISE EXCEPTION 'STATE C: target table and column are absent but conflicting named RoomInventoryLoss objects exist.';
        END IF;
        RAISE NOTICE 'STATE A: Room Inventory Loss schema is fully absent and eligible for reviewed compatibility apply.';
        RETURN;
    END IF;

    IF target_oid IS NULL OR NOT has_column THEN
        RAISE EXCEPTION 'STATE C: Room Inventory Loss schema is partial (table present=%; adjustment column present=%).', target_oid IS NOT NULL, has_column;
    END IF;
    IF (SELECT relkind FROM pg_class WHERE oid=target_oid) NOT IN ('r','p') THEN
        RAISE EXCEPTION 'STATE C: RoomInventoryLosses exists but is not a table.';
    END IF;

    WITH expected(ordinal_position,column_name,data_type,max_length,is_nullable,is_identity) AS (VALUES
      (1,'Id','bigint',NULL::integer,'NO','YES'),
      (2,'OperationKey','character varying',150,'NO','NO'),
      (3,'WarehouseId','integer',NULL::integer,'NO','NO'),
      (4,'RoomId','integer',NULL::integer,'NO','NO'),
      (5,'ReceiptId','bigint',NULL::integer,'YES','NO'),
      (6,'CropYear','integer',NULL::integer,'YES','NO'),
      (7,'GrowerLotId','integer',NULL::integer,'YES','NO'),
      (8,'FruitProfileId','integer',NULL::integer,'YES','NO'),
      (9,'GrowerName','character varying',200,'NO','NO'),
      (10,'GrowerNumber','character varying',50,'YES','NO'),
      (11,'LotNumber','character varying',100,'NO','NO'),
      (12,'PoolStart','character varying',20,'YES','NO'),
      (13,'VarietyCode','character varying',50,'NO','NO'),
      (14,'InventoryStatus','character varying',100,'YES','NO'),
      (15,'LossType','character varying',50,'NO','NO'),
      (16,'BinCount','integer',NULL::integer,'NO','NO'),
      (17,'Reason','character varying',500,'NO','NO'),
      (18,'Notes','character varying',1000,'YES','NO'),
      (19,'OccurredAt','timestamp with time zone',NULL::integer,'YES','NO'),
      (20,'CreatedByUserId','integer',NULL::integer,'NO','NO'),
      (21,'CreatedAt','timestamp with time zone',NULL::integer,'NO','NO'),
      (22,'IsReversed','boolean',NULL::integer,'NO','NO'),
      (23,'ReversedAt','timestamp with time zone',NULL::integer,'YES','NO'),
      (24,'ReversedByUserId','integer',NULL::integer,'YES','NO'),
      (25,'ReverseReason','character varying',1000,'YES','NO')
    ), actual AS (
      SELECT ordinal_position,column_name,data_type,character_maximum_length AS max_length,is_nullable,is_identity
      FROM information_schema.columns
      WHERE table_schema=current_schema() AND table_name='RoomInventoryLosses'
    )
    SELECT string_agg(coalesce(e.column_name,a.column_name), ', ' ORDER BY coalesce(e.ordinal_position,a.ordinal_position))
    INTO mismatch
    FROM expected e FULL JOIN actual a USING (column_name)
    WHERE e.column_name IS NULL OR a.column_name IS NULL
       OR (e.ordinal_position,e.data_type,e.max_length,e.is_nullable,e.is_identity)
          IS DISTINCT FROM (a.ordinal_position,a.data_type,a.max_length,a.is_nullable,a.is_identity);
    IF mismatch IS NOT NULL THEN RAISE EXCEPTION 'STATE C: incompatible RoomInventoryLosses columns: %', mismatch; END IF;

    IF (SELECT data_type FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='RoomInventoryAdjustments' AND column_name='RoomInventoryLossId') <> 'bigint'
       OR (SELECT is_nullable FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='RoomInventoryAdjustments' AND column_name='RoomInventoryLossId') <> 'YES' THEN
        RAISE EXCEPTION 'STATE C: RoomInventoryAdjustments.RoomInventoryLossId is incompatible.';
    END IF;

    IF (SELECT count(*) FROM pg_constraint WHERE conrelid=target_oid AND contype IN ('p','f','u','c','x')) <> 7 THEN
        RAISE EXCEPTION 'STATE C: expected exactly PK plus six FKs on RoomInventoryLosses.';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid=target_oid AND conname='PK_RoomInventoryLosses' AND contype='p') THEN
        RAISE EXCEPTION 'STATE C: RoomInventoryLosses primary key is missing.';
    END IF;

    WITH expected(name,parent_table,local_column,delete_action) AS (VALUES
      ('FK_RoomInventoryLosses_Warehouses_WarehouseId','Warehouses','WarehouseId','r'::"char"),
      ('FK_RoomInventoryLosses_Rooms_RoomId','Rooms','RoomId','r'::"char"),
      ('FK_RoomInventoryLosses_Receipts_ReceiptId','Receipts','ReceiptId','r'::"char"),
      ('FK_RoomInventoryLosses_FruitProfiles_FruitProfileId','FruitProfiles','FruitProfileId','r'::"char"),
      ('FK_RoomInventoryLosses_Users_CreatedByUserId','Users','CreatedByUserId','r'::"char"),
      ('FK_RoomInventoryLosses_Users_ReversedByUserId','Users','ReversedByUserId','r'::"char")
    )
    SELECT string_agg(e.name, ', ' ORDER BY e.name) INTO mismatch
    FROM expected e
    WHERE NOT EXISTS (
      SELECT 1 FROM pg_constraint c
      JOIN pg_attribute a ON a.attrelid=c.conrelid AND a.attnum=c.conkey[1]
      WHERE c.conrelid=target_oid AND c.conname=e.name AND c.contype='f'
        AND c.confrelid=to_regclass(format('%I.%I',current_schema(),e.parent_table))
        AND a.attname=e.local_column AND c.confdeltype=e.delete_action);
    IF mismatch IS NOT NULL THEN RAISE EXCEPTION 'STATE C: missing or incompatible RoomInventoryLosses FKs: %', mismatch; END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint c WHERE c.conrelid=adjustment_oid
      AND c.conname='FK_RoomInventoryAdjustments_RoomInventoryLosses_RoomInventoryLossId'
      AND c.contype='f' AND c.confrelid=target_oid AND c.confdeltype='r') THEN
        RAISE EXCEPTION 'STATE C: adjustment-to-loss FK is missing or incompatible.';
    END IF;

    IF (SELECT count(*) FROM pg_index WHERE indrelid=target_oid) <> 8 THEN
        RAISE EXCEPTION 'STATE C: expected PK index plus seven secondary indexes on RoomInventoryLosses.';
    END IF;
    WITH expected(name,is_unique,columns) AS (VALUES
      ('IX_RoomInventoryLosses_CreatedByUserId',false,ARRAY['CreatedByUserId']::name[]),
      ('IX_RoomInventoryLosses_FruitProfileId',false,ARRAY['FruitProfileId']::name[]),
      ('IX_RoomInventoryLosses_OperationKey',true,ARRAY['OperationKey']::name[]),
      ('IX_RoomInventoryLosses_ReceiptId_CreatedAt',false,ARRAY['ReceiptId','CreatedAt']::name[]),
      ('IX_RoomInventoryLosses_ReversedByUserId',false,ARRAY['ReversedByUserId']::name[]),
      ('IX_RoomInventoryLosses_RoomId_CreatedAt',false,ARRAY['RoomId','CreatedAt']::name[]),
      ('IX_RoomInventoryLosses_WarehouseId',false,ARRAY['WarehouseId']::name[])
    )
    SELECT string_agg(e.name, ', ' ORDER BY e.name) INTO mismatch
    FROM expected e WHERE NOT EXISTS (
      SELECT 1 FROM pg_class ic JOIN pg_index i ON i.indexrelid=ic.oid
      WHERE i.indrelid=target_oid AND ic.relname=e.name AND i.indisunique=e.is_unique
        AND i.indisvalid AND i.indisready AND i.indpred IS NULL AND i.indexprs IS NULL
        AND (SELECT array_agg(a.attname ORDER BY k.ordinality) FROM unnest(i.indkey) WITH ORDINALITY k(attnum,ordinality) JOIN pg_attribute a ON a.attrelid=i.indrelid AND a.attnum=k.attnum)=e.columns);
    IF mismatch IS NOT NULL THEN RAISE EXCEPTION 'STATE C: missing or incompatible RoomInventoryLosses indexes: %', mismatch; END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_class ic JOIN pg_index i ON i.indexrelid=ic.oid
      WHERE i.indrelid=adjustment_oid AND ic.relname='IX_RoomInventoryAdjustments_RoomInventoryLossId'
        AND NOT i.indisunique AND i.indpred IS NULL)
    OR NOT EXISTS (SELECT 1 FROM pg_class ic JOIN pg_index i ON i.indexrelid=ic.oid
      WHERE i.indrelid=adjustment_oid AND ic.relname='IX_RoomInventoryAdjustments_RoomInventoryLossId_AdjustmentType'
        AND i.indisunique AND pg_get_expr(i.indpred,i.indrelid)='("RoomInventoryLossId" IS NOT NULL)') THEN
        RAISE EXCEPTION 'STATE C: adjustment loss indexes are missing or incompatible.';
    END IF;

    RAISE NOTICE 'STATE B: Room Inventory Loss schema is complete and exact; compatibility apply must be a no-op.';
END $preflight$;

SELECT 'room_inventory_losses_preflight_passed' AS status,
       CASE WHEN to_regclass(format('%I.%I',current_schema(),'RoomInventoryLosses')) IS NULL
            THEN 'state_a_absent' ELSE 'state_b_complete_exact' END AS compatibility_state;
ROLLBACK;
