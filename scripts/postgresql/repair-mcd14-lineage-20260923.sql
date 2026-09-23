-- ONE-TIME DATA REPAIR, NOT A SCHEMA MIGRATION. Do not run automatically.
-- Requires authorized operator, fresh verified production backup, and fixed app.
-- psql -X -v ON_ERROR_STOP=1 -v actor_user_id=<authorized user ID> -f this-file.sql
-- Stop application writes during the bounded repair window. All guards must pass.
\set ON_ERROR_STOP on
BEGIN ISOLATION LEVEL SERIALIZABLE;
SET LOCAL lock_timeout = '15s';
SET LOCAL statement_timeout = '60s';
SELECT set_config('cropqc.repair_actor', :'actor_user_id', true);
LOCK TABLE "TreatmentLineageSegments", "TreatmentLineageMovements", "RoomInventoryAdjustments", "AuditLogs" IN SHARE ROW EXCLUSIVE MODE;
DO $repair$
DECLARE
    before_row jsonb;
    after_row jsonb;
    other_segments_hash text;
    ledger_hash text;
    movement_hash text;
    actor integer := current_setting('cropqc.repair_actor')::integer;
    repair_key text := 'mcd14-lineage-20260923-segment160';
BEGIN
    IF EXISTS (SELECT 1 FROM "AuditLogs" WHERE "Action"='ReconcileLineage' AND "EntityKey"=repair_key) THEN
        RAISE NOTICE 'Repair already recorded; no changes.';
        RETURN;
    END IF;
    IF NOT EXISTS (SELECT 1 FROM "Users" WHERE "Id"=actor AND "IsActive") THEN
        RAISE EXCEPTION 'An active authorized operator ID is required';
    END IF;
    IF (SELECT COUNT(*) FROM "TreatmentLineageSegments" WHERE "Id" IN (148,154,160)
        AND "WarehouseId"=3 AND "RoomId"=66 AND "CropYear"=2026 AND "GrowerLotId"=448
        AND "FruitProfileId"=17 AND "TreatmentState"='Untreated' AND "TreatmentSignature"='u') <> 3
        OR NOT EXISTS (SELECT 1 FROM "TreatmentLineageSegments" WHERE "Id"=148 AND "CurrentBins"=184 AND "ConcurrencyVersion"=2 AND "ReceiptId"=720
            AND "IdentityKey"='2026|448|17|1372|1372|BART|CONVENTIONAL|False|CONVENTIONAL')
        OR NOT EXISTS (SELECT 1 FROM "TreatmentLineageSegments" WHERE "Id"=154 AND "CurrentBins"=72 AND "ConcurrencyVersion"=2 AND "ReceiptId"=626
            AND "IdentityKey"='2026|448|17|1372|1372|BART|CONVENTIONAL|False|CONVENTIONAL')
        OR NOT EXISTS (SELECT 1 FROM "TreatmentLineageSegments" WHERE "Id"=160 AND "CurrentBins"=802 AND "ConcurrencyVersion"=7 AND "ReceiptId" IS NULL
            AND "UpdatedAt"='2026-09-16T13:46:23.520179Z'
            AND "IdentityKey"='2026|448|17|1372|1372|BART|CONVENTIONAL|False|')
        OR EXISTS (SELECT 1 FROM "TreatmentLineageSegmentApplications" WHERE "TreatmentLineageSegmentId" IN (148,154,160)) THEN
        RAISE EXCEPTION 'Reviewed segment fingerprints changed; investigate instead of applying this repair';
    END IF;
    IF (SELECT COALESCE(SUM("CurrentBins"),0) FROM "TreatmentLineageSegments"
        WHERE "RoomId"=66 AND "GrowerLotId"=448 AND "FruitProfileId"=17 AND "CropYear"=2026) <> 1058
        OR (SELECT COALESCE(SUM("ChangeAmount"),0) FROM "RoomInventoryAdjustments"
            WHERE "RoomId"=66 AND "LotNumber"='1372' AND "FruitProfileId"=17) <> 798
        OR (SELECT COALESCE(SUM("ChangeAmount"),0) FROM "RoomInventoryAdjustments" WHERE "RoomId"=66) <> 2520
        OR EXISTS (SELECT 1 FROM "RoomInventoryAdjustments" WHERE "RoomId"=66 AND "AdjustmentType"='StartingInventoryImport') THEN
        RAISE EXCEPTION 'Current inventory evidence changed; do not extrapolate this historical repair';
    END IF;
    -- Independent evidence: 538 shared transfer-ins plus 66 received bins,
    -- minus the 62 actually dispatched from segment 160 = 542 shared bins.
    IF (SELECT COUNT(*) FROM "TreatmentLineageMovements" WHERE
        ("Id"=96 AND "DestinationSegmentId"=160 AND "RoomTransferId"=218 AND "BinCount"=280) OR
        ("Id"=97 AND "DestinationSegmentId"=160 AND "RoomTransferId"=219 AND "BinCount"=194) OR
        ("Id"=182 AND "DestinationSegmentId"=160 AND "RoomTransferId"=281 AND "BinCount"=64) OR
        ("Id"=590 AND "SourceSegmentId"=159 AND "InterCrewTransferId"=22 AND "BinCount"=4 AND "MovementType"='InterCrewDispatch') OR
        ("Id"=591 AND "SourceSegmentId"=160 AND "InterCrewTransferId"=22 AND "BinCount"=62 AND "MovementType"='InterCrewDispatch')) <> 5
        OR NOT EXISTS (SELECT 1 FROM "RoomInventoryAdjustments" WHERE "Id"=1298 AND "ReceiptId"=763 AND "RoomId"=66 AND "ChangeAmount"=66)
        OR NOT EXISTS (SELECT 1 FROM "RoomInventoryAdjustments" WHERE "Id"=3180 AND "InterCrewTransferId"=22 AND "RoomId"=66 AND "ChangeAmount"=-66) THEN
        RAISE EXCEPTION 'Required receiving/transfer evidence is missing';
    END IF;
    SELECT md5(string_agg(to_jsonb(s)::text, '' ORDER BY "Id")) INTO other_segments_hash FROM "TreatmentLineageSegments" s WHERE "Id"<>160;
    SELECT md5(string_agg(to_jsonb(a)::text, '' ORDER BY "Id")) INTO ledger_hash FROM "RoomInventoryAdjustments" a;
    SELECT md5(string_agg(to_jsonb(m)::text, '' ORDER BY "Id")) INTO movement_hash FROM "TreatmentLineageMovements" m;
    SELECT to_jsonb(s) INTO before_row FROM "TreatmentLineageSegments" s WHERE "Id"=160;
    UPDATE "TreatmentLineageSegments" SET "CurrentBins"=542, "ConcurrencyVersion"="ConcurrencyVersion"+1,
        "UpdatedAt"=CURRENT_TIMESTAMP WHERE "Id"=160;
    SELECT to_jsonb(s) INTO after_row FROM "TreatmentLineageSegments" s WHERE "Id"=160;
    INSERT INTO "AuditLogs" ("UserId","Action","EntityName","EntityKey","BeforeValuesJson","AfterValuesJson","SourceApplication","CreatedAt")
    VALUES (actor,'ReconcileLineage','TreatmentLineageSegment',repair_key,before_row::text,
        jsonb_build_object('Segment',after_row,'Reason','Remove 256 status-alias double counts plus 4 bins rematerialized during split dispatch 22',
            'PreservedSegments',jsonb_build_array(148,154),'EvidenceMovements',jsonb_build_array(89,92,96,97,182,590,591),
            'AuthoritativeLotBins',798,'InventoryLedgerChanged',false)::text,'Reviewed SQL repair',CURRENT_TIMESTAMP);
    IF (SELECT SUM("CurrentBins") FROM "TreatmentLineageSegments"
        WHERE "RoomId"=66 AND "GrowerLotId"=448 AND "FruitProfileId"=17 AND "CropYear"=2026) <> 798 THEN
        RAISE EXCEPTION 'Post-repair lineage failed to reconcile';
    END IF;
    IF other_segments_hash IS DISTINCT FROM (SELECT md5(string_agg(to_jsonb(s)::text, '' ORDER BY "Id")) FROM "TreatmentLineageSegments" s WHERE "Id"<>160)
        OR ledger_hash IS DISTINCT FROM (SELECT md5(string_agg(to_jsonb(a)::text, '' ORDER BY "Id")) FROM "RoomInventoryAdjustments" a)
        OR movement_hash IS DISTINCT FROM (SELECT md5(string_agg(to_jsonb(m)::text, '' ORDER BY "Id")) FROM "TreatmentLineageMovements" m) THEN
        RAISE EXCEPTION 'Protected historical evidence changed; rolling back';
    END IF;
    RAISE NOTICE 'Repair complete: segment160=542; segments148/154=256; lot1372=798; room66=2520. Other segments, ledger and movement fingerprints unchanged.';
END $repair$;
COMMIT;
