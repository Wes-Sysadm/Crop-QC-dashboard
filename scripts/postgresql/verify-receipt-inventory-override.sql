\set ON_ERROR_STOP on

BEGIN TRANSACTION READ ONLY;

DO $verify$
DECLARE
    missing_columns text;
    missing_indexes text;
    missing_constraints text;
BEGIN
    IF to_regclass(format('%I.%I', current_schema(), 'ReceiptInventoryOverrides')) IS NULL THEN
        RAISE EXCEPTION 'ReceiptInventoryOverrides is missing';
    END IF;

    SELECT string_agg(expected.table_name || '.' || expected.column_name, ', ' ORDER BY expected.table_name, expected.column_name)
    INTO missing_columns
    FROM (VALUES
        ('Receipts', 'ConcurrencyVersion', 'bigint', 'NO'),
        ('RoomInventoryAdjustments', 'ReceiptInventoryOverrideId', 'uuid', 'YES'),
        ('ReceiptInventoryOverrides', 'Id', 'uuid', 'NO'),
        ('ReceiptInventoryOverrides', 'ReceiptId', 'bigint', 'NO'),
        ('ReceiptInventoryOverrides', 'ActionType', 'character varying', 'NO'),
        ('ReceiptInventoryOverrides', 'OldReceiptBinCount', 'integer', 'NO'),
        ('ReceiptInventoryOverrides', 'NewReceiptBinCount', 'integer', 'NO'),
        ('ReceiptInventoryOverrides', 'InventoryDelta', 'integer', 'NO'),
        ('ReceiptInventoryOverrides', 'CurrentInventoryBefore', 'integer', 'NO'),
        ('ReceiptInventoryOverrides', 'CurrentInventoryAfter', 'integer', 'NO'),
        ('ReceiptInventoryOverrides', 'AdministratorUserId', 'integer', 'NO'),
        ('ReceiptInventoryOverrides', 'Reason', 'character varying', 'NO'),
        ('ReceiptInventoryOverrides', 'OperationKey', 'character varying', 'NO'),
        ('ReceiptInventoryOverrides', 'CreatedAt', 'timestamp with time zone', 'NO'),
        ('ReceiptInventoryOverrides', 'NegativeInventoryAcknowledged', 'boolean', 'NO'),
        ('ReceiptInventoryOverrides', 'VoidConfirmationDetails', 'character varying', 'YES'),
        ('ReceiptInventoryOverrides', 'BeforeReceiptSnapshotJson', 'text', 'NO'),
        ('ReceiptInventoryOverrides', 'AfterReceiptSnapshotJson', 'text', 'NO'),
        ('ReceiptInventoryOverrides', 'AffectedInventorySnapshotJson', 'text', 'NO'),
        ('ReceiptInventoryOverrides', 'ExpectedAdjustmentCount', 'integer', 'NO'),
        ('ReceiptInventoryOverrides', 'IsComplete', 'boolean', 'NO')
    ) AS expected(table_name, column_name, data_type, is_nullable)
    LEFT JOIN information_schema.columns AS actual
      ON actual.table_schema=current_schema()
     AND actual.table_name=expected.table_name
     AND actual.column_name=expected.column_name
     AND actual.data_type=expected.data_type
     AND actual.is_nullable=expected.is_nullable
    WHERE actual.column_name IS NULL;
    IF missing_columns IS NOT NULL THEN
        RAISE EXCEPTION 'Receipt inventory override columns are missing or incompatible: %', missing_columns;
    END IF;

    IF COALESCE((SELECT column_default FROM information_schema.columns
                 WHERE table_schema=current_schema() AND table_name='Receipts' AND column_name='ConcurrencyVersion'), '')
       NOT IN ('0', '0::bigint') THEN
        RAISE EXCEPTION 'Receipts.ConcurrencyVersion does not have the required zero default';
    END IF;

    SELECT string_agg(expected.name, ', ' ORDER BY expected.name)
    INTO missing_indexes
    FROM (VALUES
        ('IX_RoomInventoryAdjustments_ReceiptInventoryOverrideId'),
        ('IX_ReceiptInventoryOverrides_AdministratorUserId'),
        ('IX_ReceiptInventoryOverrides_OperationKey'),
        ('IX_ReceiptInventoryOverrides_ReceiptId_CreatedAt')
    ) AS expected(name)
    WHERE to_regclass(format('%I.%I', current_schema(), expected.name)) IS NULL;
    IF missing_indexes IS NOT NULL THEN
        RAISE EXCEPTION 'Receipt inventory override indexes are missing: %', missing_indexes;
    END IF;

    IF NOT EXISTS (
        SELECT 1 FROM pg_index AS i
        JOIN pg_class AS c ON c.oid=i.indexrelid
        JOIN pg_namespace AS n ON n.oid=c.relnamespace
        WHERE n.nspname=current_schema()
          AND c.relname='IX_ReceiptInventoryOverrides_OperationKey'
          AND i.indisunique
    ) THEN
        RAISE EXCEPTION 'Receipt inventory override operation-key index is not unique';
    END IF;

    SELECT string_agg(expected.name, ', ' ORDER BY expected.name)
    INTO missing_constraints
    FROM (VALUES
        ('PK_ReceiptInventoryOverrides'),
        ('FK_ReceiptInventoryOverrides_Receipts_ReceiptId'),
        ('FK_ReceiptInventoryOverrides_Users_AdministratorUserId'),
        ('FK_RoomInventoryAdjustments_ReceiptOverrides_OverrideId')
    ) AS expected(name)
    WHERE NOT EXISTS (
        SELECT 1 FROM pg_constraint AS actual
        WHERE actual.connamespace=current_schema()::regnamespace
          AND actual.conname=expected.name
    );
    IF missing_constraints IS NOT NULL THEN
        RAISE EXCEPTION 'Receipt inventory override constraints are missing: %', missing_constraints;
    END IF;

    IF (SELECT COUNT(*) FROM "__EFMigrationsHistory"
        WHERE "MigrationId"='20260805014812_AddReceiptInventoryOverrides' AND "ProductVersion"='9.0.9') <> 1 THEN
        RAISE EXCEPTION 'Receipt inventory override migration-history row is missing or duplicated';
    END IF;
END $verify$;

SELECT 'receipt_inventory_override_schema_verified' AS status,
       '20260805014812_AddReceiptInventoryOverrides' AS migration,
       10 AS checked_target_objects;
ROLLBACK;
