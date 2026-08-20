\set ON_ERROR_STOP on
BEGIN TRANSACTION READ ONLY;

DO $preflight$
DECLARE
    target_tables text[] := ARRAY['TreatmentChemicals','RoomTreatmentApplications','RoomTreatmentApplicationSources','TreatmentLineageSegments','TreatmentLineageSegmentApplications','TreatmentLineageMovements'];
    extension_columns text[] := ARRAY['BinsRunEntries.TreatmentSignatureSnapshot','BinsRunEntries.TreatmentStateSnapshot','BinsRunEntries.TreatmentSummarySnapshot','ActualRunOverrideRequestLines.TreatmentSignature'];
    table_count integer;
    column_count integer;
    mismatch text;
BEGIN
    IF to_regclass(format('%I.%I', current_schema(), 'BinsRunEntries')) IS NULL
       OR to_regclass(format('%I.%I', current_schema(), 'ActualRunOverrideRequestLines')) IS NULL
       OR to_regclass(format('%I.%I', current_schema(), 'RoomInventoryLosses')) IS NULL
       OR to_regclass(format('%I.%I', current_schema(), 'RoomTransfers')) IS NULL THEN
        RAISE EXCEPTION 'Missing prerequisite schema for room treatment tracking.';
    END IF;

    SELECT count(*) INTO table_count
    FROM unnest(target_tables) t(name)
    WHERE to_regclass(format('%I.%I', current_schema(), t.name)) IS NOT NULL;

    SELECT count(*) INTO column_count
    FROM unnest(extension_columns) e(spec)
    WHERE EXISTS (
        SELECT 1 FROM information_schema.columns c
        WHERE c.table_schema=current_schema()
          AND c.table_name=split_part(e.spec,'.',1)
          AND c.column_name=split_part(e.spec,'.',2));

    IF table_count=0 AND column_count=0 THEN
        IF EXISTS (
            SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
            WHERE n.nspname=current_schema()
              AND (c.relname LIKE 'TreatmentLineage%' OR c.relname LIKE 'RoomTreatment%' OR c.relname LIKE 'TreatmentChemicals%'))
        OR EXISTS (
            SELECT 1 FROM pg_constraint c JOIN pg_namespace n ON n.oid=c.connamespace
            WHERE n.nspname=current_schema()
              AND (c.conname LIKE '%TreatmentLineage%' OR c.conname LIKE '%RoomTreatment%' OR c.conname LIKE '%TreatmentChemicals%')) THEN
            RAISE EXCEPTION 'STATE C: target tables and columns are absent but conflicting named room-treatment objects exist.';
        END IF;
        RAISE NOTICE 'STATE A: room treatment tracking schema is fully absent and eligible for reviewed compatibility apply.';
        RETURN;
    END IF;

    IF table_count<>6 OR column_count<>4 THEN
        RAISE EXCEPTION 'STATE C: room treatment schema is partial (tables %/6; extension columns %/4).', table_count, column_count;
    END IF;

    WITH expected(table_name, specs) AS (VALUES
      ('TreatmentChemicals', ARRAY['Id:integer:t:d','ProductName:character varying(200):t:','CommonName:character varying(200):f:','Crop:character varying(25):t:','Volume:numeric(12,2):t:','Unit:character varying(25):t:','UnitPrice:numeric(12,2):t:','Currency:character varying(3):t:','IsActive:boolean:t:','CreatedAt:timestamp with time zone:t:','CreatedByUserId:integer:f:','UpdatedAt:timestamp with time zone:t:','UpdatedByUserId:integer:f:']),
      ('RoomTreatmentApplications', ARRAY['Id:bigint:t:d','OperationKey:character varying(100):t:','TreatmentChemicalId:integer:t:','WarehouseId:integer:t:','RoomId:integer:t:','AppliedAt:timestamp with time zone:t:','AppliedByUserId:integer:t:','Notes:character varying(1000):f:','TotalBinsSnapshot:integer:t:','ProductNameSnapshot:character varying(200):t:','CommonNameSnapshot:character varying(200):f:','CropSnapshot:character varying(25):t:','VolumeSnapshot:numeric(12,2):t:','UnitSnapshot:character varying(25):t:','UnitPriceSnapshot:numeric(12,2):t:','CurrencySnapshot:character varying(3):t:','EstimatedCostSnapshot:numeric(14,2):t:','CreatedAt:timestamp with time zone:t:','CreatedByUserId:integer:t:','ReversedAt:timestamp with time zone:f:','ReversedByUserId:integer:f:','ReversalReason:character varying(1000):f:']),
      ('RoomTreatmentApplicationSources', ARRAY['Id:bigint:t:d','RoomTreatmentApplicationId:bigint:t:','CropYear:integer:f:','GrowerLotId:integer:f:','FruitProfileId:integer:f:','IdentityKey:character varying(500):t:','GrowerNumberSnapshot:character varying(50):f:','GrowerNameSnapshot:character varying(200):t:','LotNumberSnapshot:character varying(100):t:','VarietyCodeSnapshot:character varying(50):t:','ProductionTypeSnapshot:character varying(50):t:','IsOrganicSnapshot:boolean:f:','InventoryStatusSnapshot:character varying(100):f:','BinsTreated:integer:t:','PriorTreatmentSignature:character varying(1000):t:','ResultTreatmentSignature:character varying(1000):t:']),
      ('TreatmentLineageSegments', ARRAY['Id:bigint:t:d','WarehouseId:integer:t:','RoomId:integer:t:','CropYear:integer:f:','GrowerLotId:integer:f:','FruitProfileId:integer:f:','IdentityKey:character varying(500):t:','GrowerNumberSnapshot:character varying(50):f:','GrowerNameSnapshot:character varying(200):t:','LotNumberSnapshot:character varying(100):t:','VarietyCodeSnapshot:character varying(50):t:','ProductionTypeSnapshot:character varying(50):t:','IsOrganicSnapshot:boolean:f:','InventoryStatusSnapshot:character varying(100):f:','TreatmentState:character varying(25):t:','TreatmentSignature:character varying(1000):t:','CurrentBins:integer:t:','CreatedAt:timestamp with time zone:t:','UpdatedAt:timestamp with time zone:t:','ConcurrencyVersion:bigint:t:']),
      ('TreatmentLineageSegmentApplications', ARRAY['TreatmentLineageSegmentId:bigint:t:','RoomTreatmentApplicationId:bigint:t:','Sequence:integer:t:']),
      ('TreatmentLineageMovements', ARRAY['Id:bigint:t:d','OperationKey:character varying(200):t:','MovementType:character varying(50):t:','SourceSegmentId:bigint:f:','DestinationSegmentId:bigint:f:','SourceRoomId:integer:f:','DestinationRoomId:integer:f:','IdentityKey:character varying(500):t:','TreatmentStateSnapshot:character varying(25):t:','TreatmentSignatureSnapshot:character varying(1000):t:','BinCount:integer:t:','RoomTransferId:bigint:f:','RoomInventoryLossId:bigint:f:','BinsRunEntryId:bigint:f:','ReversesTreatmentLineageMovementId:bigint:f:','OccurredAt:timestamp with time zone:t:','CreatedByUserId:integer:f:','CreatedAt:timestamp with time zone:t:'])
    ), actual AS (
      SELECT c.relname AS table_name,
             array_agg(a.attname || ':' || format_type(a.atttypid,a.atttypmod) || ':' || CASE WHEN a.attnotnull THEN 't' ELSE 'f' END || ':' || a.attidentity::text ORDER BY a.attnum) AS specs
      FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
      JOIN pg_attribute a ON a.attrelid=c.oid AND a.attnum>0 AND NOT a.attisdropped
      WHERE n.nspname=current_schema() AND c.relname=ANY(target_tables)
      GROUP BY c.relname
    )
    SELECT string_agg(e.table_name, ', ' ORDER BY e.table_name) INTO mismatch
    FROM expected e LEFT JOIN actual a USING(table_name)
    WHERE a.specs IS DISTINCT FROM e.specs;
    IF mismatch IS NOT NULL THEN RAISE EXCEPTION 'STATE C: incompatible room treatment table definitions: %', mismatch; END IF;

    WITH expected(table_name,column_name,data_type,max_length,is_nullable) AS (VALUES
      ('BinsRunEntries','TreatmentSignatureSnapshot','character varying',1000,'YES'),
      ('BinsRunEntries','TreatmentStateSnapshot','character varying',25,'YES'),
      ('BinsRunEntries','TreatmentSummarySnapshot','character varying',2000,'YES'),
      ('ActualRunOverrideRequestLines','TreatmentSignature','character varying',1000,'YES')
    )
    SELECT string_agg(e.table_name||'.'||e.column_name, ', ' ORDER BY e.table_name,e.column_name) INTO mismatch
    FROM expected e LEFT JOIN information_schema.columns c
      ON c.table_schema=current_schema() AND c.table_name=e.table_name AND c.column_name=e.column_name
    WHERE (c.data_type,c.character_maximum_length,c.is_nullable) IS DISTINCT FROM (e.data_type,e.max_length,e.is_nullable);
    IF mismatch IS NOT NULL THEN RAISE EXCEPTION 'STATE C: incompatible treatment snapshot columns: %', mismatch; END IF;

    WITH expected(name) AS (SELECT unnest(ARRAY[
      'PK_TreatmentChemicals','PK_RoomTreatmentApplications','PK_RoomTreatmentApplicationSources','PK_TreatmentLineageSegments','PK_TreatmentLineageSegmentApplications','PK_TreatmentLineageMovements',
      'FK_TreatmentChemicals_Users_CreatedByUserId','FK_TreatmentChemicals_Users_UpdatedByUserId','FK_TreatmentLineageSegments_FruitProfiles_FruitProfileId','FK_TreatmentLineageSegments_Rooms_RoomId','FK_TreatmentLineageSegments_Warehouses_WarehouseId','FK_RoomTreatmentApplications_Rooms_RoomId','FK_RoomTreatmentApplications_TreatmentChemicals_TreatmentChemicalId','FK_RoomTreatmentApplications_Users_AppliedByUserId','FK_RoomTreatmentApplications_Users_CreatedByUserId','FK_RoomTreatmentApplications_Users_ReversedByUserId','FK_RoomTreatmentApplications_Warehouses_WarehouseId','FK_TreatmentLineageMovements_BinsRunEntries_BinsRunEntryId','FK_TreatmentLineageMovements_RoomInventoryLosses_RoomInventoryLossId','FK_TreatmentLineageMovements_RoomTransfers_RoomTransferId','FK_TreatmentLineageMovements_Rooms_DestinationRoomId','FK_TreatmentLineageMovements_Rooms_SourceRoomId','FK_TreatmentLineageMovements_TreatmentLineageMovements_ReversesTreatmentLineageMovementId','FK_TreatmentLineageMovements_TreatmentLineageSegments_DestinationSegmentId','FK_TreatmentLineageMovements_TreatmentLineageSegments_SourceSegmentId','FK_TreatmentLineageMovements_Users_CreatedByUserId','FK_RoomTreatmentApplicationSources_FruitProfiles_FruitProfileId','FK_RoomTreatmentApplicationSources_RoomTreatmentApplications_RoomTreatmentApplicationId','FK_TreatmentLineageSegmentApplications_RoomTreatmentApplications_RoomTreatmentApplicationId','FK_TreatmentLineageSegmentApplications_TreatmentLineageSegments_TreatmentLineageSegmentId']))
    SELECT string_agg(e.name, ', ' ORDER BY e.name) INTO mismatch FROM expected e
    WHERE NOT EXISTS (SELECT 1 FROM pg_constraint c JOIN pg_namespace n ON n.oid=c.connamespace WHERE n.nspname=current_schema() AND c.conname=left(e.name,63));
    IF mismatch IS NOT NULL THEN RAISE EXCEPTION 'STATE C: missing treatment constraints: %', mismatch; END IF;

    IF (SELECT count(*) FROM pg_constraint c JOIN pg_class t ON t.oid=c.conrelid JOIN pg_namespace n ON n.oid=t.relnamespace
        WHERE n.nspname=current_schema() AND t.relname=ANY(target_tables) AND c.contype IN ('p','f','u','c','x'))<>30 THEN
        RAISE EXCEPTION 'STATE C: expected exactly six primary keys and 24 foreign keys on room treatment tables.';
    END IF;

    IF EXISTS (SELECT 1 FROM pg_constraint WHERE conname=ANY(ARRAY(SELECT left(x,63) FROM unnest(ARRAY['FK_TreatmentChemicals_Users_CreatedByUserId','FK_TreatmentChemicals_Users_UpdatedByUserId','FK_TreatmentLineageSegments_FruitProfiles_FruitProfileId','FK_RoomTreatmentApplications_Users_ReversedByUserId','FK_TreatmentLineageMovements_Users_CreatedByUserId','FK_RoomTreatmentApplicationSources_FruitProfiles_FruitProfileId']) x)) AND confdeltype<>'n')
       OR EXISTS (SELECT 1 FROM pg_constraint WHERE conname=ANY(ARRAY(SELECT left(x,63) FROM unnest(ARRAY['FK_RoomTreatmentApplicationSources_RoomTreatmentApplications_RoomTreatmentApplicationId','FK_TreatmentLineageSegmentApplications_TreatmentLineageSegments_TreatmentLineageSegmentId']) x)) AND confdeltype<>'c')
       OR EXISTS (SELECT 1 FROM pg_constraint c JOIN pg_class t ON t.oid=c.conrelid JOIN pg_namespace n ON n.oid=t.relnamespace WHERE n.nspname=current_schema() AND t.relname=ANY(target_tables) AND c.contype='f' AND c.conname<>ALL(ARRAY(SELECT left(x,63) FROM unnest(ARRAY['FK_TreatmentChemicals_Users_CreatedByUserId','FK_TreatmentChemicals_Users_UpdatedByUserId','FK_TreatmentLineageSegments_FruitProfiles_FruitProfileId','FK_RoomTreatmentApplications_Users_ReversedByUserId','FK_TreatmentLineageMovements_Users_CreatedByUserId','FK_RoomTreatmentApplicationSources_FruitProfiles_FruitProfileId','FK_RoomTreatmentApplicationSources_RoomTreatmentApplications_RoomTreatmentApplicationId','FK_TreatmentLineageSegmentApplications_TreatmentLineageSegments_TreatmentLineageSegmentId']) x)) AND c.confdeltype<>'r') THEN
        RAISE EXCEPTION 'STATE C: one or more treatment foreign-key delete actions are incompatible.';
    END IF;

    WITH expected(name) AS (SELECT unnest(ARRAY[
      'IX_RoomTreatmentApplications_AppliedByUserId','IX_RoomTreatmentApplications_CreatedByUserId','IX_RoomTreatmentApplications_OperationKey','IX_RoomTreatmentApplications_ReversedByUserId','IX_RoomTreatmentApplications_RoomId_AppliedAt','IX_RoomTreatmentApplications_TreatmentChemicalId','IX_RoomTreatmentApplications_WarehouseId','IX_RoomTreatmentApplicationSources_FruitProfileId','IX_RoomTreatmentApplicationSources_GrowerLotId','IX_RoomTreatmentApplicationSources_RoomTreatmentApplicationId_IdentityKey','IX_TreatmentChemicals_CreatedByUserId','IX_TreatmentChemicals_Crop_IsActive_ProductName','IX_TreatmentChemicals_ProductName','IX_TreatmentChemicals_UpdatedByUserId','IX_TreatmentLineageMovements_BinsRunEntryId','IX_TreatmentLineageMovements_CreatedByUserId','IX_TreatmentLineageMovements_DestinationRoomId_OccurredAt','IX_TreatmentLineageMovements_DestinationSegmentId','IX_TreatmentLineageMovements_OperationKey','IX_TreatmentLineageMovements_ReversesTreatmentLineageMovementId','IX_TreatmentLineageMovements_RoomInventoryLossId','IX_TreatmentLineageMovements_RoomTransferId','IX_TreatmentLineageMovements_SourceRoomId_OccurredAt','IX_TreatmentLineageMovements_SourceSegmentId','IX_TreatmentLineageSegmentApplications_RoomTreatmentApplicationId_TreatmentLineageSegmentId','IX_TreatmentLineageSegments_FruitProfileId','IX_TreatmentLineageSegments_GrowerLotId','IX_TreatmentLineageSegments_RoomId_CurrentBins','IX_TreatmentLineageSegments_RoomId_IdentityKey_TreatmentSignature','IX_TreatmentLineageSegments_WarehouseId']))
    SELECT string_agg(e.name, ', ' ORDER BY e.name) INTO mismatch FROM expected e
    WHERE NOT EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace JOIN pg_index i ON i.indexrelid=c.oid WHERE n.nspname=current_schema() AND c.relname=left(e.name,63) AND i.indisvalid AND i.indisready);
    IF mismatch IS NOT NULL THEN RAISE EXCEPTION 'STATE C: missing treatment indexes: %', mismatch; END IF;

    IF (SELECT count(*) FROM pg_index i JOIN pg_class t ON t.oid=i.indrelid JOIN pg_namespace n ON n.oid=t.relnamespace
        WHERE n.nspname=current_schema() AND t.relname=ANY(target_tables))<>36 THEN
        RAISE EXCEPTION 'STATE C: expected exactly six primary-key indexes and 30 secondary indexes on room treatment tables.';
    END IF;

    IF EXISTS (SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace JOIN pg_index i ON i.indexrelid=c.oid WHERE n.nspname=current_schema() AND c.relname=ANY(ARRAY(SELECT left(x,63) FROM unnest(ARRAY['IX_RoomTreatmentApplications_OperationKey','IX_TreatmentChemicals_ProductName','IX_TreatmentLineageMovements_OperationKey','IX_TreatmentLineageSegments_RoomId_IdentityKey_TreatmentSignature']) x)) AND NOT i.indisunique) THEN
        RAISE EXCEPTION 'STATE C: one or more required treatment indexes are not unique.';
    END IF;

    IF EXISTS (
      WITH expected(id,created_at) AS (VALUES
        (1,'2026-05-21T00:00:00Z'::timestamptz),(2,'2026-05-21T00:00:00Z'::timestamptz),
        (3,'2026-05-21T00:00:00Z'::timestamptz),(4,'2026-05-21T00:00:00Z'::timestamptz),
        (5,'2026-05-21T00:00:00Z'::timestamptz),(6,'2026-05-21T00:00:00Z'::timestamptz),
        (7,'2026-05-21T00:00:00Z'::timestamptz),(8,'2026-05-21T00:00:00Z'::timestamptz),
        (9,'2026-05-21T00:00:00Z'::timestamptz),(10,'2026-05-21T00:00:00Z'::timestamptz)
      ) SELECT 1 FROM expected e LEFT JOIN "TreatmentChemicals" c ON c."Id"=e.id
        WHERE c."Id" IS NULL
           OR c."CreatedAt" IS DISTINCT FROM e.created_at
           OR c."CreatedByUserId" IS NOT NULL
    ) THEN
        RAISE EXCEPTION 'STATE C: one or more durable reviewed Treatment Chemical seed identities is missing or replaced.';
    END IF;

    RAISE NOTICE 'STATE B: room treatment tracking schema is complete and exact; all ten durable reviewed seed identities are present; additional or maintained Treatment Chemical master data is allowed; compatibility apply must be a no-op.';
END $preflight$;

SELECT 'room_treatment_tracking_preflight_passed' AS status,
       CASE WHEN to_regclass(format('%I.%I',current_schema(),'TreatmentChemicals')) IS NULL THEN 'state_a_absent' ELSE 'state_b_complete_exact' END AS compatibility_state;
ROLLBACK;
