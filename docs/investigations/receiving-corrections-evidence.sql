-- Read-only production evidence, captured 2026-10-13; workspace tea-d7uc4ippo60c73ebn4mg.

-- history

WITH receipts AS (SELECT * FROM "Receipts" WHERE ("GrowerLotId","FruitProfileId") IN ((615,12),(616,14),(575,4))), ledger AS (SELECT * FROM "RoomInventoryAdjustments" WHERE ("GrowerLotId","FruitProfileId") IN ((615,12),(616,14)) OR ("GrowerLotId"=575 AND "FruitProfileId"=4) OR "ReceiptId" IN (SELECT "Id" FROM receipts)), segments AS (SELECT * FROM "TreatmentLineageSegments" WHERE ("GrowerLotId","FruitProfileId") IN ((615,12),(616,14),(575,4)) OR "ReceiptId" IN (SELECT "Id" FROM receipts)) SELECT json_build_object(
'capturedAt',now(),
'receipts',(SELECT json_agg(r ORDER BY "CreatedAt","Id") FROM receipts r),
'ledger',(SELECT json_agg(l ORDER BY "CreatedAt","Id") FROM ledger l),
'overrides',(SELECT json_agg(o ORDER BY "CreatedAt") FROM "ReceiptInventoryOverrides" o WHERE "ReceiptId" IN (SELECT "Id" FROM receipts)),
'segments',(SELECT json_agg(s ORDER BY "CreatedAt","Id") FROM segments s),
'movements',(SELECT json_agg(m ORDER BY "CreatedAt","Id") FROM "TreatmentLineageMovements" m WHERE "SourceSegmentId" IN (SELECT "Id" FROM segments) OR "DestinationSegmentId" IN (SELECT "Id" FROM segments) OR "ReceiptId" IN (SELECT "Id" FROM receipts)),
'segmentLinks',(SELECT json_agg(a) FROM "TreatmentLineageSegmentApplications" a WHERE "TreatmentLineageSegmentId" IN (SELECT "Id" FROM segments)),
'applicationSources',(SELECT json_agg(a) FROM "RoomTreatmentApplicationSources" a WHERE ("GrowerLotId","FruitProfileId") IN ((615,12),(616,14),(575,4)) OR "ReceiptId" IN (SELECT "Id" FROM receipts)),
'applications',(SELECT json_agg(a ORDER BY "AppliedAt") FROM "RoomTreatmentApplications" a WHERE "Id" IN (SELECT "RoomTreatmentApplicationId" FROM "RoomTreatmentApplicationSources" WHERE ("GrowerLotId","FruitProfileId") IN ((615,12),(616,14),(575,4))) OR "RoomId" IN (7,30)),
'transfers',(SELECT json_agg(t ORDER BY "CreatedAt") FROM "RoomTransfers" t WHERE "Id" IN (SELECT "RoomTransferId" FROM ledger)),
'losses',(SELECT json_agg(l ORDER BY "CreatedAt") FROM "RoomInventoryLosses" l WHERE "Id" IN (SELECT "RoomInventoryLossId" FROM ledger)),
'binsRuns',(SELECT json_agg(b ORDER BY "CreatedAt") FROM "BinsRunEntries" b WHERE "ReceiptId" IN (SELECT "Id" FROM receipts) OR "InventoryAdjustmentId" IN (SELECT "Id" FROM ledger)),
'interCrew',(SELECT json_agg(t ORDER BY "CreatedAt") FROM "InterCrewTransfers" t WHERE "Id" IN (SELECT "InterCrewTransferId" FROM ledger)),
'outside',(SELECT json_agg(t ORDER BY "CreatedAt") FROM "OutsideWarehouseTransfers" t WHERE "Id" IN (SELECT "OutsideWarehouseTransferId" FROM ledger)),
'identityCorrections',(SELECT json_agg(c) FROM "InventoryIdentityCorrections" c WHERE "CorrectedReceiptId" IN (SELECT "Id" FROM receipts) OR "SourceGrowerLotId" IN (615,616,575) OR "TargetGrowerLotId" IN (615,616,575))
) evidence;

-- parents

SELECT json_build_object(
'rooms',(SELECT json_agg(to_jsonb(r)) FROM "Rooms" r WHERE "Id" IN (6,7,30)),
'profiles',(SELECT json_agg(to_jsonb(f)) FROM "FruitProfiles" f WHERE "Id" IN (4,12,14)),
'actualRun',(SELECT json_agg(to_jsonb(a)) FROM "ActualRuns" a WHERE "Id"=168),
'revisions',(SELECT json_agg(to_jsonb(a)) FROM "ActualRunRevisions" a WHERE "ActualRunId"=168),
'audits',(SELECT json_agg(to_jsonb(a) ORDER BY "CreatedAt","Id") FROM "AuditLogs" a WHERE ("EntityName" ILIKE '%Receipt%' AND "EntityKey" IN ('2475','2476','2490')) OR ("AfterValuesJson" LIKE '%TR110044%' OR "BeforeValuesJson" LIKE '%TR110044%' OR "AfterValuesJson" LIKE '%TR110045%' OR "BeforeValuesJson" LIKE '%TR110045%' OR "AfterValuesJson" LIKE '%TR110057%' OR "BeforeValuesJson" LIKE '%TR110057%')),
'commands',(SELECT json_agg(to_jsonb(c) ORDER BY "CommittedAt") FROM "InventoryCommands" c WHERE "OperationKey" IN ('2cb0ce0ee73e4c52af07833342d176b3','51eec487a6ba44789c8ed35275057fa7','78eef0840985408eb996245e05f65cab','a3bc2aa9f36d434fb17bab8408e67e2d') OR "IntentJson" LIKE '%TR110044%' OR "IntentJson" LIKE '%TR110045%' OR "IntentJson" LIKE '%TR110057%')
) evidence;

-- ancillary

SELECT jsonb_build_object(
'capturedAt',now(),
'depletions',(SELECT coalesce(jsonb_agg(to_jsonb(x)),'[]') FROM "RoomDepletions" x WHERE "ReceiptId" IN (2475,2476,2490) OR "LotCode" IN ('9501','9502','9333')),
'processorLines',(SELECT coalesce(jsonb_agg(to_jsonb(x)),'[]') FROM "ProcessorShipmentLines" x WHERE "ReceiptId" IN (2475,2476,2490) OR ("GrowerLotId","FruitProfileId") IN ((615,12),(616,14),(575,4))),
'deletionAudits',(SELECT coalesce(jsonb_agg(to_jsonb(x)-'DeletedByEmail'),'[]') FROM "ReceiptDeletionAudits" x WHERE "DeletedReceiptId" IN (2475,2476,2490)),
'runRevisions',(SELECT coalesce(jsonb_agg(to_jsonb(x)),'[]') FROM "ActualRunRevisions" x WHERE "ActualRunId"=168),
'runAudits',(SELECT coalesce(jsonb_agg(to_jsonb(x)),'[]') FROM "AuditLogs" x WHERE ("EntityName" LIKE '%Run%' AND "EntityKey"='168') OR concat("BeforeValuesJson","AfterValuesJson") LIKE '%a3bc2aa9f36d434fb17bab8408e67e2d%')
) AS evidence;
