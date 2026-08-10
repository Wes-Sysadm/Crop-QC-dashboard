\set ON_ERROR_STOP on
\ir preflight-inventory-diagnostic-acknowledgments.sql

BEGIN TRANSACTION READ ONLY;
DO $verify$
BEGIN
    IF to_regclass(format('%I.%I',current_schema(),'InventoryDiagnosticAcknowledgments')) IS NULL THEN
        RAISE EXCEPTION 'InventoryDiagnosticAcknowledgments is absent; verification requires the complete exact table.';
    END IF;
END $verify$;

SELECT count(*) AS acknowledgment_row_count
FROM "InventoryDiagnosticAcknowledgments";
SELECT 'inventory_diagnostic_acknowledgments_schema_verified' AS status,
       'exact_16_columns_5_indexes_pk_3_fks' AS object_state,
       'migration_history_intentionally_unchanged' AS migration_history;
ROLLBACK;
