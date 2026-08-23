\set ON_ERROR_STOP on
BEGIN TRANSACTION READ ONLY;
DO $verify$ BEGIN
    IF to_regclass(format('%I.%I',current_schema(),'RoomSealEvents')) IS NULL THEN RAISE EXCEPTION 'RoomSealEvents table is missing'; END IF;
    IF (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='RoomSealEvents') <> 10 THEN RAISE EXCEPTION 'RoomSealEvents columns are not exact'; END IF;
    IF (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='Rooms' AND column_name IN ('IsSealed','SealedAt','SealRecordedAt','SealedByUserId')) <> 4 THEN RAISE EXCEPTION 'Rooms sealing columns are incomplete'; END IF;
    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema=current_schema() AND (
        (table_name='RoomSealEvents' AND column_name='EffectiveAt' AND (data_type <> 'timestamp with time zone' OR is_nullable <> 'NO')) OR
        (table_name='RoomSealEvents' AND column_name='PreviousEffectiveAt' AND (data_type <> 'timestamp with time zone' OR is_nullable <> 'YES')) OR
        (table_name='RoomSealEvents' AND column_name='Action' AND (data_type <> 'character varying' OR character_maximum_length <> 30)) OR
        (table_name='Rooms' AND column_name='SealRecordedAt' AND (data_type <> 'timestamp with time zone' OR is_nullable <> 'YES')))) THEN
        RAISE EXCEPTION 'Room Seal effective-time column contract is incompatible';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM pg_constraint WHERE connamespace=current_schema()::regnamespace
        AND conname='CK_RoomSealEvents_Action' AND convalidated
        AND lower(pg_get_constraintdef(oid)) LIKE '%sealscheduled%'
        AND lower(pg_get_constraintdef(oid)) LIKE '%schedulechanged%'
        AND lower(pg_get_constraintdef(oid)) LIKE '%schedulecanceled%') THEN
        RAISE EXCEPTION 'Room Seal effective-time action constraint is incompatible';
    END IF;
    IF EXISTS (SELECT 1 FROM "RoomSealEvents" WHERE "EffectiveAt" IS NULL OR "Action" NOT IN ('Seal','SealScheduled','ScheduleChanged','ScheduleCanceled','Unseal')) THEN RAISE EXCEPTION 'RoomSealEvents contains invalid effective-time history'; END IF;
    IF EXISTS (SELECT 1 FROM "Rooms" WHERE NOT "IsSealed" AND ("SealedAt" IS NOT NULL OR "SealRecordedAt" IS NOT NULL OR "SealedByUserId" IS NOT NULL)) THEN RAISE EXCEPTION 'Open Room has inconsistent sealing metadata'; END IF;
    IF EXISTS (SELECT 1 FROM "Rooms" WHERE "IsSealed" AND ("SealedAt" IS NULL OR "SealRecordedAt" IS NULL OR "SealedByUserId" IS NULL)) THEN RAISE EXCEPTION 'Active or scheduled Room seal has incomplete metadata'; END IF;
END $verify$;
SELECT 'room_seal_effective_time_schema_verified' AS status,
       23 AS checked_target_objects,
       (SELECT count(*) FROM "RoomSealEvents") AS seal_event_rows,
       (SELECT count(*) FROM "Rooms" WHERE "IsSealed" AND "SealedAt" > statement_timestamp()) AS scheduled_room_rows,
       (SELECT count(*) FROM "Rooms" WHERE "IsSealed" AND "SealedAt" <= statement_timestamp()) AS effectively_sealed_room_rows;
ROLLBACK;
