\set ON_ERROR_STOP on

BEGIN TRANSACTION READ ONLY;
DO $verify$
DECLARE
    source_definition text;
    scoped_definition text;
BEGIN
    SELECT lower(pg_get_indexdef(c.oid)) INTO source_definition
    FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname = current_schema()
      AND c.relname = 'UX_InventoryIdentityCorrections_GlobalSource';
    SELECT lower(pg_get_indexdef(c.oid)) INTO scoped_definition
    FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname = current_schema()
      AND c.relname = 'UX_InventoryIdentityCorrections_ReceiptSource';

    IF to_regclass(format('%I.%I', current_schema(), 'IX_InventoryIdentityCorrections_CorrectedReceiptId')) IS NOT NULL
       OR source_definition NOT LIKE 'create unique index%'
       OR source_definition NOT LIKE '%where ("correctedreceiptid" is null)%'
       OR scoped_definition NOT LIKE 'create unique index%'
       OR scoped_definition NOT LIKE '%where ("correctedreceiptid" is not null)%' THEN
        RAISE EXCEPTION 'Receipt-scoped inventory identity index contract is not exact';
    END IF;
END $verify$;

SELECT 'receipt_scoped_inventory_identity_indexes_verified' AS status,
       2 AS checked_target_objects,
       (SELECT count(*) FROM "InventoryIdentityCorrections" WHERE "CorrectedReceiptId" IS NULL) AS global_corrections,
       (SELECT count(*) FROM "InventoryIdentityCorrections" WHERE "CorrectedReceiptId" IS NOT NULL) AS receipt_scoped_corrections;
ROLLBACK;
