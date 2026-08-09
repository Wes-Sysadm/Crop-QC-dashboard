\set ON_ERROR_STOP on
BEGIN TRANSACTION READ ONLY;

DO $preflight$
DECLARE
    target_oid oid;
    mismatch text;
    migration_recorded boolean;
BEGIN
    SELECT string_agg(name, ', ' ORDER BY name) INTO mismatch
    FROM (VALUES ('RoomInventoryAdjustments'),('Users'),('Roles'),('UserRoles'),('RolePageAccesses')) required(name)
    WHERE to_regclass(format('%I.%I',current_schema(),name)) IS NULL;
    IF mismatch IS NOT NULL THEN
        RAISE EXCEPTION 'Missing prerequisite tables: %.', mismatch;
    END IF;

    SELECT string_agg(table_name||'.'||column_name, ', ' ORDER BY table_name,column_name) INTO mismatch
    FROM (VALUES
      ('Roles','Id'),('Roles','IsActive'),('Roles','NormalizedName'),
      ('UserRoles','UserId'),('UserRoles','RoleId'),
      ('RolePageAccesses','Id'),('RolePageAccesses','RoleId'),('RolePageAccesses','AreaKey'),('RolePageAccesses','AccessLevel')) required(table_name,column_name)
    WHERE NOT EXISTS (SELECT 1 FROM information_schema.columns c WHERE c.table_schema=current_schema() AND c.table_name=required.table_name AND c.column_name=required.column_name);
    IF mismatch IS NOT NULL THEN
        RAISE EXCEPTION 'Role-based schema prerequisites are incomplete: %.', mismatch;
    END IF;

    IF NOT EXISTS (
      SELECT 1 FROM pg_constraint c
      WHERE c.conrelid=to_regclass(format('%I.%I',current_schema(),'RoomInventoryAdjustments'))
        AND c.conname='PK_RoomInventoryAdjustments' AND c.contype='p'
        AND (SELECT array_agg(a.attname ORDER BY k.ordinality) FROM unnest(c.conkey) WITH ORDINALITY k(attnum,ordinality) JOIN pg_attribute a ON a.attrelid=c.conrelid AND a.attnum=k.attnum)=ARRAY['Id']::name[])
    OR NOT EXISTS (
      SELECT 1 FROM pg_constraint c
      WHERE c.conrelid=to_regclass(format('%I.%I',current_schema(),'Users'))
        AND c.conname='PK_Users' AND c.contype='p'
        AND (SELECT array_agg(a.attname ORDER BY k.ordinality) FROM unnest(c.conkey) WITH ORDINALITY k(attnum,ordinality) JOIN pg_attribute a ON a.attrelid=c.conrelid AND a.attnum=k.attnum)=ARRAY['Id']::name[]) THEN
        RAISE EXCEPTION 'Required parent primary keys PK_RoomInventoryAdjustments(Id) and PK_Users(Id) must be exact.';
    END IF;

    IF EXISTS (
      SELECT 1 FROM "Users" u
      LEFT JOIN "UserRoles" ur ON ur."UserId"=u."Id"
      WHERE u."IsActive"
      GROUP BY u."Id"
      HAVING count(ur."RoleId") <> 1) THEN
        RAISE EXCEPTION 'Role-based authorization prerequisite failed: every active user must have exactly one role.';
    END IF;

    SELECT EXISTS (
        SELECT 1 FROM "__EFMigrationsHistory"
        WHERE "MigrationId" = '20260809151943_AddInventoryDiagnosticAcknowledgments')
    INTO migration_recorded;

    SELECT c.oid INTO target_oid
    FROM pg_class c
    JOIN pg_namespace n ON n.oid = c.relnamespace
    WHERE n.nspname = current_schema()
      AND c.relname = 'InventoryDiagnosticAcknowledgments';

    IF target_oid IS NULL THEN
        IF migration_recorded THEN
            RAISE EXCEPTION 'STATE C: migration history records 20260809151943_AddInventoryDiagnosticAcknowledgments but the table is absent.';
        END IF;
        IF EXISTS (
            SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE n.nspname=current_schema()
              AND c.relname IN ('PK_InventoryDiagnosticAcknowledgments','IX_InventoryDiagnosticAck_Key','IX_InventoryDiagnosticAck_DismissedBy','IX_InventoryDiagnosticAck_ActiveAdjustment','IX_InventoryDiagnosticAck_RestoredBy','IX_InventoryDiagnosticAck_Adjustment'))
        OR EXISTS (
            SELECT 1 FROM pg_constraint c JOIN pg_namespace n ON n.oid=c.connamespace
            WHERE n.nspname=current_schema()
              AND c.conname IN ('PK_InventoryDiagnosticAcknowledgments','FK_InventoryDiagnosticAck_Adjustment','FK_InventoryDiagnosticAck_DismissedBy','FK_InventoryDiagnosticAck_RestoredBy')) THEN
            RAISE EXCEPTION 'STATE C: target table is absent but conflicting named objects exist.';
        END IF;
        RAISE NOTICE 'STATE A: InventoryDiagnosticAcknowledgments is fully absent and eligible for the reviewed compatibility apply.';
        RETURN;
    END IF;

    IF (SELECT relkind FROM pg_class WHERE oid=target_oid) NOT IN ('r','p') THEN
        RAISE EXCEPTION 'STATE C: InventoryDiagnosticAcknowledgments exists but is not a table.';
    END IF;

    WITH expected(ordinal_position,column_name,data_type,max_length,is_nullable,is_identity,identity_generation) AS (VALUES
      (1,'Id','bigint',NULL::integer,'NO','YES','BY DEFAULT'),
      (2,'DiagnosticKey','character varying',64,'NO','NO',NULL),
      (3,'DiagnosticType','character varying',100,'NO','NO',NULL),
      (4,'DiagnosticCode','character varying',100,'NO','NO',NULL),
      (5,'DiagnosticMessage','character varying',1000,'NO','NO',NULL),
      (6,'RoomInventoryAdjustmentId','bigint',NULL::integer,'NO','NO',NULL),
      (7,'InvariantVersion','integer',NULL::integer,'NO','NO',NULL),
      (8,'Reason','character varying',500,'NO','NO',NULL),
      (9,'DiagnosticSnapshotJson','character varying',4000,'NO','NO',NULL),
      (10,'DismissedByUserId','integer',NULL::integer,'YES','NO',NULL),
      (11,'DismissedByEmail','character varying',320,'NO','NO',NULL),
      (12,'DismissedAt','timestamp with time zone',NULL::integer,'NO','NO',NULL),
      (13,'IsActive','boolean',NULL::integer,'NO','NO',NULL),
      (14,'RestoredByUserId','integer',NULL::integer,'YES','NO',NULL),
      (15,'RestoredByEmail','character varying',320,'YES','NO',NULL),
      (16,'RestoredAt','timestamp with time zone',NULL::integer,'YES','NO',NULL)
    ), actual AS (
      SELECT ordinal_position,column_name,data_type,character_maximum_length AS max_length,is_nullable,is_identity,identity_generation
      FROM information_schema.columns
      WHERE table_schema=current_schema() AND table_name='InventoryDiagnosticAcknowledgments'
    )
    SELECT string_agg(format('%s expected=(%s,%s,%s,%s,%s,%s) actual=(%s,%s,%s,%s,%s,%s)',
      coalesce(e.column_name,a.column_name),e.ordinal_position,e.data_type,e.max_length,e.is_nullable,e.is_identity,e.identity_generation,
      a.ordinal_position,a.data_type,a.max_length,a.is_nullable,a.is_identity,a.identity_generation),'; ' ORDER BY coalesce(e.ordinal_position,a.ordinal_position))
    INTO mismatch
    FROM expected e FULL JOIN actual a USING (column_name)
    WHERE e.column_name IS NULL OR a.column_name IS NULL
       OR (e.ordinal_position,e.data_type,e.max_length,e.is_nullable,e.is_identity,e.identity_generation)
          IS DISTINCT FROM (a.ordinal_position,a.data_type,a.max_length,a.is_nullable,a.is_identity,a.identity_generation);
    IF mismatch IS NOT NULL THEN RAISE EXCEPTION 'STATE C: column mismatch: %', mismatch; END IF;

    IF NOT EXISTS (
      SELECT 1 FROM pg_constraint c
      WHERE c.conrelid=target_oid AND c.conname='PK_InventoryDiagnosticAcknowledgments' AND c.contype='p'
        AND (SELECT array_agg(a.attname ORDER BY k.ordinality) FROM unnest(c.conkey) WITH ORDINALITY k(attnum,ordinality) JOIN pg_attribute a ON a.attrelid=c.conrelid AND a.attnum=k.attnum)=ARRAY['Id']::name[]
    ) THEN RAISE EXCEPTION 'STATE C: primary key is missing or incompatible.'; END IF;

    IF (SELECT count(*) FROM pg_constraint WHERE conrelid=target_oid AND contype IN ('p','f','u','c','x')) <> 4 THEN
        RAISE EXCEPTION 'STATE C: expected exactly four constraints on InventoryDiagnosticAcknowledgments.';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM pg_constraint c WHERE c.conrelid=target_oid AND c.conname='FK_InventoryDiagnosticAck_Adjustment' AND c.contype='f' AND c.confrelid=to_regclass(format('%I.%I',current_schema(),'RoomInventoryAdjustments')) AND c.confdeltype='r' AND (SELECT array_agg(a.attname ORDER BY k.ordinality) FROM unnest(c.conkey) WITH ORDINALITY k(attnum,ordinality) JOIN pg_attribute a ON a.attrelid=c.conrelid AND a.attnum=k.attnum)=ARRAY['RoomInventoryAdjustmentId']::name[] AND (SELECT array_agg(a.attname ORDER BY k.ordinality) FROM unnest(c.confkey) WITH ORDINALITY k(attnum,ordinality) JOIN pg_attribute a ON a.attrelid=c.confrelid AND a.attnum=k.attnum)=ARRAY['Id']::name[])
    THEN RAISE EXCEPTION 'STATE C: adjustment FK target, columns, or RESTRICT action is incompatible.'; END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint c WHERE c.conrelid=target_oid AND c.conname='FK_InventoryDiagnosticAck_DismissedBy' AND c.contype='f' AND c.confrelid=to_regclass(format('%I.%I',current_schema(),'Users')) AND c.confdeltype='n' AND (SELECT array_agg(a.attname ORDER BY k.ordinality) FROM unnest(c.conkey) WITH ORDINALITY k(attnum,ordinality) JOIN pg_attribute a ON a.attrelid=c.conrelid AND a.attnum=k.attnum)=ARRAY['DismissedByUserId']::name[] AND (SELECT array_agg(a.attname ORDER BY k.ordinality) FROM unnest(c.confkey) WITH ORDINALITY k(attnum,ordinality) JOIN pg_attribute a ON a.attrelid=c.confrelid AND a.attnum=k.attnum)=ARRAY['Id']::name[])
    THEN RAISE EXCEPTION 'STATE C: dismissed-by FK target, columns, or SET NULL action is incompatible.'; END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint c WHERE c.conrelid=target_oid AND c.conname='FK_InventoryDiagnosticAck_RestoredBy' AND c.contype='f' AND c.confrelid=to_regclass(format('%I.%I',current_schema(),'Users')) AND c.confdeltype='n' AND (SELECT array_agg(a.attname ORDER BY k.ordinality) FROM unnest(c.conkey) WITH ORDINALITY k(attnum,ordinality) JOIN pg_attribute a ON a.attrelid=c.conrelid AND a.attnum=k.attnum)=ARRAY['RestoredByUserId']::name[] AND (SELECT array_agg(a.attname ORDER BY k.ordinality) FROM unnest(c.confkey) WITH ORDINALITY k(attnum,ordinality) JOIN pg_attribute a ON a.attrelid=c.confrelid AND a.attnum=k.attnum)=ARRAY['Id']::name[])
    THEN RAISE EXCEPTION 'STATE C: restored-by FK target, columns, or SET NULL action is incompatible.'; END IF;

    IF (SELECT count(*) FROM pg_index WHERE indrelid=target_oid) <> 6 THEN
        RAISE EXCEPTION 'STATE C: expected exactly the PK index and five secondary indexes.';
    END IF;
    WITH expected(index_name,is_unique,columns) AS (VALUES
      ('IX_InventoryDiagnosticAck_Key',true,ARRAY['DiagnosticKey']::name[]),
      ('IX_InventoryDiagnosticAck_DismissedBy',false,ARRAY['DismissedByUserId']::name[]),
      ('IX_InventoryDiagnosticAck_ActiveAdjustment',false,ARRAY['IsActive','RoomInventoryAdjustmentId']::name[]),
      ('IX_InventoryDiagnosticAck_RestoredBy',false,ARRAY['RestoredByUserId']::name[]),
      ('IX_InventoryDiagnosticAck_Adjustment',false,ARRAY['RoomInventoryAdjustmentId']::name[])
    )
    SELECT string_agg(e.index_name,', ' ORDER BY e.index_name) INTO mismatch
    FROM expected e
    WHERE NOT EXISTS (
      SELECT 1 FROM pg_class ic JOIN pg_index i ON i.indexrelid=ic.oid
      WHERE i.indrelid=target_oid AND ic.relname=e.index_name AND i.indisunique=e.is_unique
        AND i.indisvalid AND i.indisready AND i.indpred IS NULL AND i.indexprs IS NULL
        AND i.indnkeyatts=array_length(e.columns,1) AND i.indnatts=array_length(e.columns,1)
        AND (SELECT array_agg(a.attname ORDER BY k.ordinality) FROM unnest(i.indkey) WITH ORDINALITY k(attnum,ordinality) JOIN pg_attribute a ON a.attrelid=i.indrelid AND a.attnum=k.attnum)=e.columns
    );
    IF mismatch IS NOT NULL THEN RAISE EXCEPTION 'STATE C: missing or incompatible indexes: %', mismatch; END IF;

    RAISE NOTICE 'STATE B: InventoryDiagnosticAcknowledgments is complete and exact; compatibility apply must be a no-op.';
END $preflight$;

SELECT "MigrationId", "ProductVersion"
FROM "__EFMigrationsHistory"
ORDER BY "MigrationId";
SELECT 'inventory_diagnostic_acknowledgments_preflight_passed' AS status,
       CASE WHEN to_regclass(format('%I.%I',current_schema(),'InventoryDiagnosticAcknowledgments')) IS NULL
            THEN 'state_a_absent' ELSE 'state_b_complete_exact' END AS compatibility_state;
ROLLBACK;
