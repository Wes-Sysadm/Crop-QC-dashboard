\set ON_ERROR_STOP on

BEGIN TRANSACTION READ ONLY;
DO $preflight$
DECLARE
    old_receipt_index boolean := to_regclass(format('%I.%I', current_schema(), 'IX_InventoryIdentityCorrections_CorrectedReceiptId')) IS NOT NULL;
    scoped_index boolean := to_regclass(format('%I.%I', current_schema(), 'UX_InventoryIdentityCorrections_ReceiptSource')) IS NOT NULL;
    source_definition text;
    scoped_definition text;
BEGIN
    IF to_regclass(format('%I.%I', current_schema(), 'InventoryIdentityCorrections')) IS NULL THEN
        RAISE EXCEPTION 'State C: InventoryIdentityCorrections predecessor table is missing';
    END IF;

    SELECT lower(indexdef) INTO source_definition
    FROM pg_indexes
    WHERE schemaname = current_schema() AND tablename = 'InventoryIdentityCorrections'
      AND lower(indexdef) LIKE '%("sourcecropyear", "sourcegrowerlotid", "sourcefruitprofileid")%';
    IF source_definition IS NULL OR source_definition NOT LIKE 'create unique index%'
       OR source_definition NOT LIKE '%("sourcecropyear", "sourcegrowerlotid", "sourcefruitprofileid")%' THEN
        RAISE EXCEPTION 'State C: source identity index is missing or incompatible';
    END IF;

    IF scoped_index THEN
        SELECT lower(pg_get_indexdef(c.oid)) INTO scoped_definition
        FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = current_schema()
          AND c.relname = 'UX_InventoryIdentityCorrections_ReceiptSource';
    END IF;

    IF old_receipt_index AND NOT scoped_index AND source_definition NOT LIKE '% where %' THEN
        RETURN;
    END IF;
    IF NOT old_receipt_index AND scoped_index
       AND source_definition LIKE '%where ("correctedreceiptid" is null)%'
       AND scoped_definition LIKE 'create unique index%'
       AND scoped_definition LIKE '%("correctedreceiptid", "sourcecropyear", "sourcegrowerlotid", "sourcefruitprofileid")%'
       AND scoped_definition LIKE '%where ("correctedreceiptid" is not null)%' THEN
        RETURN;
    END IF;
    RAISE EXCEPTION 'State C: partial or conflicting receipt-scoped inventory identity index contract';
END $preflight$;

SELECT CASE
    WHEN to_regclass(format('%I.%I', current_schema(), 'UX_InventoryIdentityCorrections_ReceiptSource')) IS NULL
    THEN 'state_a_predecessor_safe_to_apply'
    ELSE 'state_b_complete_exact'
END AS compatibility_state;
ROLLBACK;
