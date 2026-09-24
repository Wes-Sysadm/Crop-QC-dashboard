-- Reviewed one-time workflow adoption, never a migration/startup/user operation.
-- Invoke through the preflight/apply wrapper. Default is READ-ONLY validation.
-- Frozen production header + dispatch/ledger hashes identify each reviewed load.
-- All 15 must pass before ANY update; a failure aborts the entire transaction.
DO $adoption$
DECLARE
    apply_adoption boolean := coalesce(current_setting('cropqc.truck_adoption_apply',true),'false') = 'true';
    actor integer;
    expected record;
    transfer_row record;
    failures text[] := ARRAY[]::text[];
    reasons text[];
    before_row jsonb;
    after_row jsonb;
    protected_before jsonb;
    protected_after jsonb;
    affected integer;
    total_affected integer := 0;
    batch_key text := 'truck-receipt-initial-adoption-20260924';
BEGIN
    -- Stable timestamp rendering for reviewed JSON fingerprints across hosts.
    PERFORM set_config('TimeZone','UTC',true);
    IF apply_adoption THEN
        IF current_setting('transaction_isolation') <> 'serializable' THEN
            RAISE EXCEPTION 'Adoption requires a serializable transaction';
        END IF;
        LOCK TABLE "InterCrewTransfers", "RoomInventoryAdjustments", "TreatmentLineageMovements",
            "TreatmentLineageSegments", "TreatmentLineageSegmentApplications", "Receipts", "ReceiptVarietyLines",
            "InventoryIdentityCorrections", "FruitProfiles", "GrowerLots", "Warehouses", "Rooms", "Users", "AuditLogs"
            IN SHARE ROW EXCLUSIVE MODE;
        actor := current_setting('cropqc.repair_actor')::integer;
        IF NOT EXISTS (SELECT 1 FROM "Users" WHERE "Id"=actor AND "IsActive") THEN
            RAISE EXCEPTION 'An active authorized release operator ID is required';
        END IF;
    END IF;
    FOR expected IN SELECT * FROM (VALUES
        (1,64,'4d82813cf9653d605c0ce6b4cbe9e5a9','4707ef909288aa61b6815c1643581c8b','50bcae5469b8e1662de352a6b12db3b0'),
        (2,27,'af7571beb77130c0bd166ed7c83d2a9b','98c6c07d9576f678044106074a7a0034','cfae68b276a245339b32ac21fca79ea9'),
        (3,45,'a27b1548fcfef27f47bf686b26551a4c','38aa5cc2e7ba385c50d33e6d67732ca9','1aa97637b7add91fbd6464bc88ba4381'),
        (4,70,'143e265543ad99adbeb2eab68fef8f37','3141a8b49a58ce8673afe448583390f7','40577e4336352c93e6e2ec364b6f3483'),
        (5,70,'6afa32c21beeb3f25fbde9f24428615d','654436da396c9a7fc7fc4bab17908ff2','5ea0d430ded8eb2d72cc2d10e59ea61d'),
        (6,66,'114476e82aa370d928154ed5ff1fff4a','46a46491c3943fe1d0796c6dc9639bb8','b4598e759905c13f44523d536409fa52'),
        (7,29,'3eb20d177454dd2e427c6a6323d626bd','9117a2532ec06646e96686079b6c9217','1c67e11ff6afba17e4817ac50cec6336'),
        (8,41,'9d1f46a4c4d7241b5f26269e299fc528','8257546863aa91969e6960d3e6e28095','0c152c76045ef5d24e2d06c39925208b'),
        (9,59,'344227f5aceb9f5eb3d4aaabcf2e7aad','19a525e8e914b46eb73bb3cbc7a68a31','5ccf400c4d82a95a62cbb176d8cadbb0'),
        (10,11,'9ede329445f15e56701084c3df7f5a81','c0ea7d2ba2b4a64181743b16f203a7ee','909cf6e935382f54fcf57704b603cace'),
        (11,40,'e609bfcf2c869373b3300b915c4fcb00','ee87d13582a282012bca2c1f95f40a4a','9edb37b7725cbae712d9b933e8b74d92'),
        (12,34,'48cc51130a23f00e3c5b37827d964cdb','99f893a620b7f7d7430eac296c13dcaa','45775abbef342c1ecbcff5421ec38fa4'),
        (13,25,'09fa7c6d7f1398135f0823e3499d7661','5fb1f7ca947deff8a0d7695c05986208','ceef17596e515fc02976832f7db54de9'),
        (14,30,'b8412f5def44363960a12adac8f87b51','1d6b210478613cab6899d5254407188c','c083494f008b670bb21c3542969a1498'),
        (15,19,'51e20cda6a07a76a98bc8fe0a4d27793','130385b26f16654c1949ff196e4f04d1','761665245f5b828aeb6fc39540bc7b1b')
    ) AS reviewed(id,bins,header_hash,movement_hash,ledger_hash)
    LOOP
        reasons := ARRAY[]::text[];
        SELECT t.* INTO transfer_row FROM "InterCrewTransfers" t WHERE t."Id"=expected.id;
        IF NOT FOUND THEN
            failures := array_append(failures,format('Load %s: missing',expected.id));
            CONTINUE;
        END IF;
        IF transfer_row."BinsLoaded" <> expected.bins THEN reasons:=array_append(reasons,'count changed'); END IF;
        IF transfer_row."SourceWarehouseId" <> 1 OR transfer_row."SourceRoomId" <> 6
            OR NOT EXISTS (SELECT 1 FROM "Warehouses" WHERE "Id"=1 AND "Code"='EBS' AND "IsActive")
            OR NOT EXISTS (SELECT 1 FROM "Rooms" WHERE "Id"=6 AND "WarehouseId"=1 AND "Name"='Lamb Street 13' AND "IsActive")
            THEN reasons:=array_append(reasons,'source changed'); END IF;
        IF transfer_row."DestinationCustodyGroup" <> 'WP_DH' OR transfer_row."DestinationRoomId" IS NOT NULL
            OR transfer_row."DestinationWarehouseId" IS NOT NULL THEN reasons:=array_append(reasons,'destination assigned/changed'); END IF;
        IF transfer_row."Status" <> 'InTransit' OR transfer_row."BinsReceived" IS NOT NULL
            OR transfer_row."ReceivedAt" IS NOT NULL OR transfer_row."ReversedAt" IS NOT NULL
            THEN reasons:=array_append(reasons,'not untouched InTransit'); END IF;
        IF coalesce((to_jsonb(transfer_row)->>'RequiresTruckReceipt')::boolean,false)
            OR to_jsonb(transfer_row)->>'ReceivingReceiptId' IS NOT NULL THEN reasons:=array_append(reasons,'already adopted or matched'); END IF;
        IF transfer_row."ConcurrencyVersion" <> 1
            OR md5((to_jsonb(transfer_row)-'RequiresTruckReceipt'-'ReceivingReceiptId')::text) <> expected.header_hash
            THEN reasons:=array_append(reasons,'reviewed header/provenance/version changed'); END IF;
        IF expected.movement_hash IS DISTINCT FROM (SELECT md5(string_agg(to_jsonb(m)::text,',' ORDER BY m."Id"))
                FROM "TreatmentLineageMovements" m WHERE m."InterCrewTransferId"=expected.id)
            OR (SELECT coalesce(sum("BinCount"),0) FROM "TreatmentLineageMovements" WHERE "InterCrewTransferId"=expected.id AND "MovementType"='InterCrewDispatch') <> expected.bins
            OR EXISTS (SELECT 1 FROM "TreatmentLineageMovements" WHERE "InterCrewTransferId"=expected.id
                AND ("MovementType"<>'InterCrewDispatch' OR "DestinationSegmentId" IS NOT NULL OR "DestinationRoomId" IS NOT NULL OR "ReversesTreatmentLineageMovementId" IS NOT NULL))
            OR EXISTS (SELECT 1 FROM "TreatmentLineageMovements" m JOIN "TreatmentLineageMovements" reversal
                ON reversal."ReversesTreatmentLineageMovementId"=m."Id" WHERE m."InterCrewTransferId"=expected.id)
            THEN reasons:=array_append(reasons,'dispatch/downstream movement evidence changed'); END IF;
        IF expected.ledger_hash IS DISTINCT FROM (SELECT md5(string_agg(to_jsonb(a)::text,',' ORDER BY a."Id"))
                FROM "RoomInventoryAdjustments" a WHERE a."InterCrewTransferId"=expected.id)
            OR (SELECT coalesce(sum("ChangeAmount"),0) FROM "RoomInventoryAdjustments" WHERE "InterCrewTransferId"=expected.id) <> -expected.bins
            OR EXISTS (SELECT 1 FROM "RoomInventoryAdjustments" WHERE "InterCrewTransferId"=expected.id
                AND ("AdjustmentType"<>'InterCrewTransferDispatch' OR "WarehouseId"<>1 OR "RoomId"<>6))
            THEN reasons:=array_append(reasons,'source debit/destination ledger evidence changed'); END IF;
        IF NOT EXISTS (SELECT 1 FROM "FruitProfiles" WHERE "Id"=10 AND "FruitType"='Apple' AND "VarietyCode"='ORHC'
                AND "Name"='Organic Honey Crisp' AND "ProductionType"='Organic' AND "IsOrganic" AND "IsActive")
            OR EXISTS (SELECT 1 FROM "TreatmentLineageMovements" m LEFT JOIN "TreatmentLineageSegments" s ON s."Id"=m."SourceSegmentId"
                WHERE m."InterCrewTransferId"=expected.id AND (s."Id" IS NULL OR s."WarehouseId"<>1 OR s."RoomId"<>6
                    OR s."IdentityKey" IS DISTINCT FROM m."IdentityKey" OR s."TreatmentSignature" IS DISTINCT FROM m."TreatmentSignatureSnapshot"
                    OR s."CropYear" IS DISTINCT FROM transfer_row."CropYear" OR s."GrowerLotId" IS DISTINCT FROM transfer_row."GrowerLotId"
                    OR s."FruitProfileId" IS DISTINCT FROM 10 OR s."LotNumberSnapshot" IS DISTINCT FROM transfer_row."LotNumberSnapshot"))
            OR EXISTS (SELECT 1 FROM "InventoryIdentityCorrections" c WHERE c."IsActive" AND c."IsComplete" AND c."CorrectedReceiptId" IS NULL
                AND c."SourceCropYear"=transfer_row."CropYear" AND c."SourceGrowerLotId"=transfer_row."GrowerLotId" AND c."SourceFruitProfileId"=transfer_row."FruitProfileId")
            THEN reasons:=array_append(reasons,'canonical allocation identity changed/superseded'); END IF;
        IF EXISTS (SELECT 1 FROM "AuditLogs" WHERE "Action"='AdoptTruckReceiptWorkflow' AND "EntityKey"=batch_key||':load:'||expected.id)
            THEN reasons:=array_append(reasons,'adoption audit already exists; repeat refused'); END IF;
        IF cardinality(reasons)>0 THEN failures:=array_append(failures,format('Load %s: %s',expected.id,array_to_string(reasons,', '))); END IF;
    END LOOP;
    IF cardinality(failures)>0 THEN RAISE EXCEPTION 'Adoption rejected; NO loads adopted. %',array_to_string(failures,'; '); END IF;
    IF NOT apply_adoption THEN
        RAISE NOTICE 'Preflight passed: reviewed loads 1-15, 630 bins; no writes. Physical arrival requires operator verification.';
        RETURN;
    END IF;
    SELECT jsonb_build_object(
        'ledger',(SELECT md5(string_agg(to_jsonb(a)::text,',' ORDER BY "Id")) FROM "RoomInventoryAdjustments" a),
        'movements',(SELECT md5(string_agg(to_jsonb(m)::text,',' ORDER BY "Id")) FROM "TreatmentLineageMovements" m),
        'segments',(SELECT md5(string_agg(to_jsonb(s)::text,',' ORDER BY "Id")) FROM "TreatmentLineageSegments" s),
        'applications',(SELECT md5(string_agg(to_jsonb(s)::text,',' ORDER BY "TreatmentLineageSegmentId","RoomTreatmentApplicationId")) FROM "TreatmentLineageSegmentApplications" s),
        'receipts',(SELECT md5(string_agg(to_jsonb(r)::text,',' ORDER BY "Id")) FROM "Receipts" r),
        'lines',(SELECT md5(string_agg(to_jsonb(r)::text,',' ORDER BY "Id")) FROM "ReceiptVarietyLines" r),
        'transfers',(SELECT md5(string_agg((CASE WHEN "Id" BETWEEN 1 AND 15 THEN to_jsonb(t)-'RequiresTruckReceipt'-'ConcurrencyVersion' ELSE to_jsonb(t) END)::text,',' ORDER BY "Id")) FROM "InterCrewTransfers" t),
        'audit',(SELECT md5(string_agg(to_jsonb(a)::text,',' ORDER BY "Id")) FROM "AuditLogs" a WHERE "Action"<>'AdoptTruckReceiptWorkflow' OR "EntityKey" NOT LIKE batch_key||':load:%')
    ) INTO protected_before;
    FOR expected IN SELECT "Id" AS id FROM "InterCrewTransfers" WHERE "Id" IN (1,2,3,4,5,6,7,8,9,10,11,12,13,14,15) ORDER BY "Id"
    LOOP
        SELECT to_jsonb(t) INTO before_row FROM "InterCrewTransfers" t WHERE t."Id"=expected.id;
        UPDATE "InterCrewTransfers" SET "RequiresTruckReceipt"=true,"ConcurrencyVersion"="ConcurrencyVersion"+1
        WHERE "Id"=expected.id AND NOT "RequiresTruckReceipt" AND "ReceivingReceiptId" IS NULL AND "ConcurrencyVersion"=1
            AND "Status"='InTransit' AND "SourceWarehouseId"=1 AND "SourceRoomId"=6 AND "DestinationCustodyGroup"='WP_DH'
            AND "DestinationWarehouseId" IS NULL AND "DestinationRoomId" IS NULL;
        GET DIAGNOSTICS affected = ROW_COUNT;
        IF affected <> 1 THEN RAISE EXCEPTION 'Load %: expected one row, got %; entire adoption rolled back',expected.id,affected; END IF;
        total_affected := total_affected + affected;
        SELECT to_jsonb(t) INTO after_row FROM "InterCrewTransfers" t WHERE t."Id"=expected.id;
        INSERT INTO "AuditLogs" ("UserId","Action","EntityName","EntityKey","BeforeValuesJson","AfterValuesJson","SourceApplication","CreatedAt")
        VALUES (actor,'AdoptTruckReceiptWorkflow','InterCrewTransfer',batch_key||':load:'||expected.id,before_row::text,after_row::text,
            'Reviewed Truck Receipt release adoption; no inventory movement',CURRENT_TIMESTAMP);
    END LOOP;
    SELECT jsonb_build_object(
        'ledger',(SELECT md5(string_agg(to_jsonb(a)::text,',' ORDER BY "Id")) FROM "RoomInventoryAdjustments" a),
        'movements',(SELECT md5(string_agg(to_jsonb(m)::text,',' ORDER BY "Id")) FROM "TreatmentLineageMovements" m),
        'segments',(SELECT md5(string_agg(to_jsonb(s)::text,',' ORDER BY "Id")) FROM "TreatmentLineageSegments" s),
        'applications',(SELECT md5(string_agg(to_jsonb(s)::text,',' ORDER BY "TreatmentLineageSegmentId","RoomTreatmentApplicationId")) FROM "TreatmentLineageSegmentApplications" s),
        'receipts',(SELECT md5(string_agg(to_jsonb(r)::text,',' ORDER BY "Id")) FROM "Receipts" r),
        'lines',(SELECT md5(string_agg(to_jsonb(r)::text,',' ORDER BY "Id")) FROM "ReceiptVarietyLines" r),
        'transfers',(SELECT md5(string_agg((CASE WHEN "Id" BETWEEN 1 AND 15 THEN to_jsonb(t)-'RequiresTruckReceipt'-'ConcurrencyVersion' ELSE to_jsonb(t) END)::text,',' ORDER BY "Id")) FROM "InterCrewTransfers" t),
        'audit',(SELECT md5(string_agg(to_jsonb(a)::text,',' ORDER BY "Id")) FROM "AuditLogs" a WHERE "Action"<>'AdoptTruckReceiptWorkflow' OR "EntityKey" NOT LIKE batch_key||':load:%')
    ) INTO protected_after;
    IF total_affected<>15 OR protected_before IS DISTINCT FROM protected_after
        OR (SELECT count(*) FROM "InterCrewTransfers" WHERE "Id" BETWEEN 1 AND 15 AND "RequiresTruckReceipt" AND "ConcurrencyVersion"=2
            AND "Status"='InTransit' AND "DestinationRoomId" IS NULL AND "ReceivingReceiptId" IS NULL)<>15
        OR (SELECT sum("BinsLoaded") FROM "InterCrewTransfers" WHERE "Id" BETWEEN 1 AND 15)<>630
        OR (SELECT count(*) FROM "AuditLogs" WHERE "Action"='AdoptTruckReceiptWorkflow' AND "EntityKey" LIKE batch_key||':load:%')<>15
        THEN RAISE EXCEPTION 'Post-adoption validation failed; entire adoption rolled back'; END IF;
    RAISE NOTICE 'Adopted exactly 15 loads / 630 bins; only RequiresTruckReceipt false->true, version 1->2, and 15 audits changed. All inventory/receipt/history fingerprints unchanged.';
END $adoption$;
