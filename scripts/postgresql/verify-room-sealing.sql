\set ON_ERROR_STOP on
BEGIN TRANSACTION READ ONLY;
DO $verify$ BEGIN
    IF to_regclass(format('%I.%I',current_schema(),'RoomSealEvents')) IS NULL THEN RAISE EXCEPTION 'RoomSealEvents table is missing'; END IF;
    IF (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='RoomSealEvents') <> 8 THEN RAISE EXCEPTION 'RoomSealEvents columns are incomplete'; END IF;
    IF (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='Rooms' AND column_name IN ('IsSealed','SealedAt','SealedByUserId')) <> 3 THEN RAISE EXCEPTION 'Rooms sealing columns are incomplete'; END IF;
    IF (SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
        WHERE n.nspname=current_schema() AND c.relkind='i' AND c.relname IN (
            'IX_Rooms_SealedByUserId','IX_RoomSealEvents_ChangedByUserId','IX_RoomSealEvents_RoomId_ChangedAt')) <> 3 THEN
        RAISE EXCEPTION 'Room Sealing indexes are incomplete';
    END IF;
    IF (SELECT count(*) FROM pg_constraint WHERE connamespace=current_schema()::regnamespace AND conname IN (
        'PK_RoomSealEvents','CK_RoomSealEvents_Action','FK_RoomSealEvents_Rooms_RoomId',
        'FK_RoomSealEvents_Users_ChangedByUserId','FK_Rooms_Users_SealedByUserId')) <> 5 THEN
        RAISE EXCEPTION 'Room Sealing constraints are incomplete';
    END IF;
    IF EXISTS (SELECT 1 FROM pg_constraint WHERE connamespace=current_schema()::regnamespace AND conname IN (
        'PK_RoomSealEvents','CK_RoomSealEvents_Action','FK_RoomSealEvents_Rooms_RoomId',
        'FK_RoomSealEvents_Users_ChangedByUserId','FK_Rooms_Users_SealedByUserId') AND NOT convalidated) THEN
        RAISE EXCEPTION 'Room Sealing constraints are not validated';
    END IF;
    IF EXISTS (SELECT 1 FROM "Rooms" WHERE NOT "IsSealed" AND ("SealedAt" IS NOT NULL OR "SealedByUserId" IS NOT NULL)) THEN RAISE EXCEPTION 'Open room has inconsistent current sealing metadata'; END IF;
    IF EXISTS (SELECT 1 FROM "RoomSealEvents" WHERE "Action" NOT IN ('Seal','Unseal')) THEN RAISE EXCEPTION 'RoomSealEvents contains an invalid action'; END IF;
END $verify$;
SELECT 'room_sealing_schema_verified' AS status,
       20 AS checked_target_objects,
       (SELECT count(*) FROM "RoomSealEvents") AS seal_event_rows,
       (SELECT count(*) FROM "Rooms" WHERE "IsSealed") AS sealed_room_rows;
ROLLBACK;
