\set ON_ERROR_STOP on
\ir verify-processor-shipments.sql

BEGIN TRANSACTION READ ONLY;
DO $preflight$
DECLARE
    added_columns integer;
    action_definition text;
BEGIN
    IF to_regclass(format('%I.%I',current_schema(),'RoomSealEvents')) IS NULL THEN
        RAISE EXCEPTION 'State C: predecessor Room Sealing schema is missing';
    END IF;
    IF (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='Rooms' AND column_name IN ('IsSealed','SealedAt','SealedByUserId')) <> 3
       OR (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='RoomSealEvents' AND column_name IN ('Id','RoomId','Action','ChangedAt','ChangedByUserId','WarehouseCodeSnapshot','RoomCodeSnapshot','Note')) <> 8 THEN
        RAISE EXCEPTION 'State C: predecessor Room Sealing columns are incomplete';
    END IF;
    IF (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
        WHERE n.nspname=current_schema() AND c.relkind='i' AND c.relname IN ('IX_Rooms_SealedByUserId','IX_RoomSealEvents_ChangedByUserId','IX_RoomSealEvents_RoomId_ChangedAt')) <> 3 THEN
        RAISE EXCEPTION 'State C: predecessor Room Sealing indexes are incomplete';
    END IF;
    IF (SELECT count(*) FROM pg_constraint WHERE connamespace=current_schema()::regnamespace AND conname IN (
        'PK_RoomSealEvents','CK_RoomSealEvents_Action','FK_RoomSealEvents_Rooms_RoomId','FK_RoomSealEvents_Users_ChangedByUserId','FK_Rooms_Users_SealedByUserId')) <> 5 THEN
        RAISE EXCEPTION 'State C: predecessor Room Sealing constraints are incomplete';
    END IF;

    SELECT count(*) INTO added_columns
    FROM information_schema.columns
    WHERE table_schema=current_schema() AND (
        (table_name='Rooms' AND column_name='SealRecordedAt') OR
        (table_name='RoomSealEvents' AND column_name IN ('EffectiveAt','PreviousEffectiveAt')));
    SELECT lower(replace(pg_get_constraintdef(oid),' ','')) INTO action_definition
    FROM pg_constraint
    WHERE connamespace=current_schema()::regnamespace AND conname='CK_RoomSealEvents_Action';

    IF added_columns = 0 THEN
        IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='RoomSealEvents' AND column_name='Action' AND (data_type <> 'character varying' OR character_maximum_length <> 20))
           OR action_definition NOT LIKE '%''seal''%'
           OR action_definition NOT LIKE '%''unseal''%'
           OR action_definition LIKE '%sealscheduled%' THEN
            RAISE EXCEPTION 'State C: predecessor Room Sealing action contract is incompatible';
        END IF;
    ELSIF added_columns = 3 THEN
        IF (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='RoomSealEvents') <> 10 THEN
            RAISE EXCEPTION 'State C: RoomSealEvents columns are not exact';
        END IF;
        IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema=current_schema() AND (
            (table_name='Rooms' AND column_name='SealRecordedAt' AND (data_type <> 'timestamp with time zone' OR is_nullable <> 'YES')) OR
            (table_name='RoomSealEvents' AND column_name='EffectiveAt' AND (data_type <> 'timestamp with time zone' OR is_nullable <> 'NO')) OR
            (table_name='RoomSealEvents' AND column_name='PreviousEffectiveAt' AND (data_type <> 'timestamp with time zone' OR is_nullable <> 'YES')) OR
            (table_name='RoomSealEvents' AND column_name='Action' AND (data_type <> 'character varying' OR character_maximum_length <> 30)))) THEN
            RAISE EXCEPTION 'State C: Room Seal effective-time columns are incompatible';
        END IF;
        IF action_definition NOT LIKE '%sealscheduled%' OR action_definition NOT LIKE '%schedulechanged%'
           OR action_definition NOT LIKE '%schedulecanceled%' THEN
            RAISE EXCEPTION 'State C: Room Seal effective-time action constraint is incompatible';
        END IF;
    ELSE
        RAISE EXCEPTION 'State C: partial Room Seal effective-time schema detected (% of 3 columns)', added_columns;
    END IF;
END $preflight$;
SELECT CASE WHEN EXISTS (
    SELECT 1 FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='Rooms' AND column_name='SealRecordedAt')
    THEN 'state_b_complete_exact' ELSE 'state_a_absent_safe_to_apply' END AS compatibility_state;
ROLLBACK;
