using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReceiptCustodyGuardrails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql("""
                    CREATE FUNCTION cropqc_require_custody_writer() RETURNS trigger LANGUAGE plpgsql AS $$
                    BEGIN
                      IF EXISTS (SELECT 1 FROM "ReceiptCustodyAcknowledgments")
                         AND current_setting('cropqc.receipt_custody_writer', true) IS DISTINCT FROM 'compensations-v1' THEN
                        RAISE EXCEPTION 'Receipt custody exists: use a compensation-aware canonical writer. Unsafe application downgrade blocked.';
                      END IF;
                      IF TG_OP = 'DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
                    END $$;
                    """);
                foreach (var table in new[] { "RoomInventoryAdjustments", "TreatmentLineageSegments", "TreatmentLineageMovements", "RoomTreatmentApplications" })
                    migrationBuilder.Sql($"CREATE TRIGGER custody_writer_fence BEFORE INSERT OR UPDATE OR DELETE ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION cropqc_require_custody_writer();");
                migrationBuilder.Sql("CREATE UNIQUE INDEX \"IX_ReceiptCustodyReversals_HeldOperation\" ON \"ReceiptCustodyReversals\" (\"OperationKey\", \"AcknowledgmentId\") WHERE \"PlacementId\" IS NULL;");
            }
            migrationBuilder.AddCheckConstraint(
                name: "CK_ReceiptCustodyReversals_Evidence",
                table: "ReceiptCustodyReversals",
                sql: "(\"PlacementId\" IS NULL AND \"InventoryAdjustmentId\" IS NULL AND \"MovementId\" IS NULL) OR (\"PlacementId\" IS NOT NULL AND \"InventoryAdjustmentId\" IS NOT NULL AND \"MovementId\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ReceiptCustodyReversals_Quantity",
                table: "ReceiptCustodyReversals",
                sql: "\"Quantity\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ReceiptCustodyPlacements_Quantity",
                table: "ReceiptCustodyPlacements",
                sql: "\"Quantity\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ReceiptCustodyAcknowledgments_Quantity",
                table: "ReceiptCustodyAcknowledgments",
                sql: "\"Quantity\" > 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MigrationProviderTypes.Sql(migrationBuilder,
                "IF EXISTS (SELECT 1 FROM [ReceiptCustodyAcknowledgments]) THROW 51000, 'Preserve custody schema and writer fence after first use.', 1;",
                "DO $$ BEGIN IF EXISTS (SELECT 1 FROM \"ReceiptCustodyAcknowledgments\") THEN RAISE EXCEPTION 'Preserve custody schema and writer fence after first use.'; END IF; END $$;"));
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                foreach (var table in new[] { "RoomInventoryAdjustments", "TreatmentLineageSegments", "TreatmentLineageMovements", "RoomTreatmentApplications" })
                    migrationBuilder.Sql($"DROP TRIGGER custody_writer_fence ON \"{table}\";");
                migrationBuilder.Sql("DROP FUNCTION cropqc_require_custody_writer(); DROP INDEX \"IX_ReceiptCustodyReversals_HeldOperation\";");
            }
            migrationBuilder.DropCheckConstraint(
                name: "CK_ReceiptCustodyReversals_Evidence",
                table: "ReceiptCustodyReversals");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ReceiptCustodyReversals_Quantity",
                table: "ReceiptCustodyReversals");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ReceiptCustodyPlacements_Quantity",
                table: "ReceiptCustodyPlacements");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ReceiptCustodyAcknowledgments_Quantity",
                table: "ReceiptCustodyAcknowledgments");
        }
    }
}
