\set ON_ERROR_STOP on
\ir preflight-room-seal-effective-time.sql

BEGIN;
SET LOCAL lock_timeout='15s';
SET LOCAL statement_timeout='10min';
SELECT pg_advisory_xact_lock(hashtextextended('CropQc:20260822152806_AddRoomSealEffectiveTime',0));
SELECT NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='Rooms' AND column_name='SealRecordedAt') AS create_target \gset
\if :create_target
ALTER TABLE "Rooms" ADD COLUMN "SealRecordedAt" timestamp with time zone;
ALTER TABLE "RoomSealEvents" ADD COLUMN "EffectiveAt" timestamp with time zone;
ALTER TABLE "RoomSealEvents" ADD COLUMN "PreviousEffectiveAt" timestamp with time zone;
UPDATE "RoomSealEvents" SET "EffectiveAt"="ChangedAt" WHERE "EffectiveAt" IS NULL;
UPDATE "Rooms" SET "SealRecordedAt"="SealedAt" WHERE "IsSealed" AND "SealRecordedAt" IS NULL;
ALTER TABLE "RoomSealEvents" ALTER COLUMN "EffectiveAt" SET NOT NULL;
ALTER TABLE "RoomSealEvents" ALTER COLUMN "Action" TYPE character varying(30);
ALTER TABLE "RoomSealEvents" DROP CONSTRAINT "CK_RoomSealEvents_Action";
ALTER TABLE "RoomSealEvents" ADD CONSTRAINT "CK_RoomSealEvents_Action"
    CHECK ("Action" IN ('Seal','SealScheduled','ScheduleChanged','ScheduleCanceled','Unseal'));
\else
\echo 'Room Seal effective-time schema is already complete and exact; no DDL will be applied.'
\endif
DO $postcheck$ BEGIN
    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='RoomSealEvents' AND column_name='EffectiveAt') THEN
        RAISE EXCEPTION 'Room Seal effective-time schema missing after apply';
    END IF;
    IF current_setting('cropqc.test_force_room_seal_effective_time_failure',true)='on' THEN
        RAISE EXCEPTION 'Forced Room Seal effective-time compatibility failure for rollback regression';
    END IF;
END $postcheck$;
COMMIT;
\ir verify-room-seal-effective-time.sql
