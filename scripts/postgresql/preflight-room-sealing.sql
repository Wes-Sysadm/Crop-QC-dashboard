\set ON_ERROR_STOP on
\ir verify-processor-shipments.sql

BEGIN TRANSACTION READ ONLY;
DO $preflight$
DECLARE
    existing_count integer;
    exact_count constant integer := 20;
BEGIN
    SELECT
        (SELECT count(*) FROM information_schema.tables WHERE table_schema=current_schema() AND table_name='RoomSealEvents')
      + (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND ((table_name='Rooms' AND column_name IN ('IsSealed','SealedAt','SealedByUserId')) OR table_name='RoomSealEvents'))
      + (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=current_schema() AND c.relkind='i' AND c.relname=ANY(ARRAY(SELECT left(x,63) FROM unnest(ARRAY[
          'IX_Rooms_SealedByUserId','IX_RoomSealEvents_ChangedByUserId','IX_RoomSealEvents_RoomId_ChangedAt']) x)))
      + (SELECT count(*) FROM pg_constraint WHERE connamespace=current_schema()::regnamespace AND conname=ANY(ARRAY(SELECT left(x,63) FROM unnest(ARRAY[
          'PK_RoomSealEvents','CK_RoomSealEvents_Action','FK_RoomSealEvents_Rooms_RoomId','FK_RoomSealEvents_Users_ChangedByUserId','FK_Rooms_Users_SealedByUserId']) x)))
    INTO existing_count;

    IF existing_count NOT IN (0, exact_count) THEN
        RAISE EXCEPTION 'State C: partial/conflicting Room Sealing schema detected (% of % objects)', existing_count, exact_count;
    END IF;
    IF existing_count = exact_count THEN
        IF (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='RoomSealEvents') <> 8 THEN
            RAISE EXCEPTION 'State C: RoomSealEvents columns are not exact';
        END IF;
        IF EXISTS (
            SELECT 1 FROM information_schema.columns
            WHERE table_schema=current_schema() AND table_name='Rooms' AND (
                (column_name='IsSealed' AND (data_type <> 'boolean' OR is_nullable <> 'NO' OR column_default NOT LIKE '%false%'))
                OR (column_name='SealedAt' AND (data_type <> 'timestamp with time zone' OR is_nullable <> 'YES'))
                OR (column_name='SealedByUserId' AND (data_type <> 'integer' OR is_nullable <> 'YES')))) THEN
            RAISE EXCEPTION 'State C: Rooms sealing columns are incompatible';
        END IF;
        IF EXISTS (SELECT 1 FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid JOIN pg_namespace n ON n.oid=c.relnamespace WHERE n.nspname=current_schema() AND c.relname=ANY(ARRAY(SELECT left(x,63) FROM unnest(ARRAY['IX_Rooms_SealedByUserId','IX_RoomSealEvents_ChangedByUserId','IX_RoomSealEvents_RoomId_ChangedAt']) x)) AND (NOT i.indisvalid OR NOT i.indisready)) THEN
            RAISE EXCEPTION 'State C: Room Sealing indexes are incompatible';
        END IF;
        IF EXISTS (SELECT 1 FROM pg_constraint WHERE connamespace=current_schema()::regnamespace AND conname=ANY(ARRAY(SELECT left(x,63) FROM unnest(ARRAY['PK_RoomSealEvents','CK_RoomSealEvents_Action','FK_RoomSealEvents_Rooms_RoomId','FK_RoomSealEvents_Users_ChangedByUserId','FK_Rooms_Users_SealedByUserId']) x)) AND NOT convalidated) THEN
            RAISE EXCEPTION 'State C: Room Sealing constraints are not validated';
        END IF;
    END IF;
END $preflight$;
SELECT CASE WHEN to_regclass(format('%I.%I',current_schema(),'RoomSealEvents')) IS NULL THEN 'state_a_absent' ELSE 'state_b_complete_exact' END AS compatibility_state;
ROLLBACK;
