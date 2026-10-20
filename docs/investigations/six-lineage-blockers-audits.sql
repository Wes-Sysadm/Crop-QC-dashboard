SELECT jsonb_build_object('observed_at',now(),'read_only',current_setting('transaction_read_only'),'AuditLogs',(SELECT jsonb_agg(to_jsonb(a) ORDER BY a."Id") FROM "AuditLogs" a WHERE ("EntityName"='Receipt' AND "EntityKey" IN ('132','169','189','201','206','318','378','444','626','633','660','709','720','767','774','1069','1338','1339','1358','1373','1389','1403','1532','1539','1555','1568','1569','1572','1587','1594','1626','1754')) OR ("EntityName"='RoomTransfer' AND "EntityKey" IN ('149','164','198','211','214','215','217','218','219','281','335','337','341','346','349','350','351','369','370')) OR ("EntityName"='InterCrewTransfer' AND "EntityKey" IN ('16','17','18','19','20','21','22','34')) OR ("EntityName"='BinsRunEntry' AND "EntityKey" IN ('125','221','313','357','376','377','382')) OR "EntityName" IN ('ReceiptInventoryOverride','RoomTreatmentApplication','TreatmentLineageSegment')));
-- Supplemental exact run/identity-correction audit query used in the investigation.
SELECT "Id","EntityName","EntityKey","Action","CreatedAt"
FROM "AuditLogs"
WHERE ("EntityName" IN ('ActualRun','ActualRunRevision','BinsRunEntry')
 AND "EntityKey" IN ('38','43','67','73','91','110','102','125','111','134','133','115','138','313','357','376','377','382','221'))
 OR ("EntityName"='InventoryIdentityCorrection'
 AND "EntityKey"='b94b079d-8e24-474d-a11b-1fdc79e39880')
ORDER BY "Id";

-- Verify appended backup audits separately; never call legitimate appends historical rewrites.
SELECT "Id","Action","EntityName","EntityKey","CreatedAt","SourceApplication"
FROM "AuditLogs" WHERE "Id">175137 ORDER BY "Id";
SELECT count(*) AS prior_audit_count,
 md5(string_agg(to_jsonb(t)::text,'' ORDER BY "Id")) AS prior_audit_hash
FROM "AuditLogs" t WHERE "Id"<=175137;
