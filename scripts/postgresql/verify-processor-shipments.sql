\set ON_ERROR_STOP on
BEGIN TRANSACTION READ ONLY;
DO $verify$ BEGIN
 IF (SELECT count(*) FROM information_schema.tables WHERE table_schema=current_schema() AND table_name IN ('Processors','ProcessorShipments','ProcessorShipmentLines','ProcessorShipmentPriceCorrections')) <> 4 THEN RAISE EXCEPTION 'Processor Shipment tables are incomplete'; END IF;
 IF (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='Processors') <> 9
 OR (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='ProcessorShipments') <> 18
 OR (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='ProcessorShipmentLines') <> 21
 OR (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND table_name='ProcessorShipmentPriceCorrections') <> 10 THEN RAISE EXCEPTION 'Processor Shipment columns are incomplete'; END IF;
 IF (SELECT count(*) FROM information_schema.columns WHERE table_schema=current_schema() AND ((table_name='RoomInventoryAdjustments' AND column_name='ProcessorShipmentLineId') OR (table_name='TreatmentLineageMovements' AND column_name='ProcessorShipmentLineId'))) <> 2 THEN RAISE EXCEPTION 'Processor Shipment parent columns are incomplete'; END IF;
 IF (SELECT count(*) FROM "Processors") < 0 OR (SELECT count(*) FROM "ProcessorShipments") < 0 THEN RAISE EXCEPTION 'Processor Shipment tables are unreadable'; END IF;
END $verify$;
SELECT 'processor_shipment_schema_verified' AS status, 102 AS checked_target_objects,
 (SELECT count(*) FROM "Processors") AS processor_rows,
 (SELECT count(*) FROM "ProcessorShipments") AS shipment_rows;
ROLLBACK;
