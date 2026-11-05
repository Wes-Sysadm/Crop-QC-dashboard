using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class DirectLocationTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CorrectsRoomTransferId",
                table: "RoomTransfers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDirectTransfer",
                table: "RoomTransfers",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"),
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LocationEvidence",
                table: "RoomTransfers",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(2000)", "character varying(2000)"),
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "PhysicalLocationVerified",
                table: "RoomTransfers",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"),
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoomTransfers_CorrectsRoomTransferId",
                table: "RoomTransfers",
                column: "CorrectsRoomTransferId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_RoomTransfers_DirectEvidence",
                table: "RoomTransfers",
                sql: "(\"IsDirectTransfer\" = 0 AND \"CorrectsRoomTransferId\" IS NULL AND \"PhysicalLocationVerified\" IS NULL AND \"LocationEvidence\" IS NULL) OR (\"IsDirectTransfer\" = 1 AND \"BinCount\" > 0 AND ((\"CorrectsRoomTransferId\" IS NULL AND \"PhysicalLocationVerified\" IS NULL AND \"LocationEvidence\" IS NULL) OR (\"CorrectsRoomTransferId\" IS NOT NULL AND \"CorrectsRoomTransferId\" <> \"Id\" AND \"PhysicalLocationVerified\" IS NOT NULL AND \"LocationEvidence\" IS NOT NULL)))"
                    .Replace("= 0", migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "= false" : "= 0")
                    .Replace("= 1", migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL" ? "= true" : "= 1"));

            migrationBuilder.AddForeignKey(
                name: "FK_RoomTransfers_RoomTransfers_CorrectsRoomTransferId",
                table: "RoomTransfers",
                column: "CorrectsRoomTransferId",
                principalTable: "RoomTransfers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                migrationBuilder.Sql("""
                    CREATE INDEX "IX_RoomTransfers_DirectCapability" ON "RoomTransfers" ("Id") WHERE "IsDirectTransfer";
                    CREATE INDEX "IX_InventoryCommands_ConversionCapability" ON "InventoryCommands" ("OperationKey") WHERE ("IntentJson"::jsonb ->> 'kind') = '42';
                    CREATE FUNCTION cropqc_require_direct_transfer_writer() RETURNS trigger LANGUAGE plpgsql AS $$
                    BEGIN
                      -- Kind 42 is the frozen persisted ConvertLegacyTransfer enum value.
                      IF (EXISTS (SELECT 1 FROM "RoomTransfers" WHERE "IsDirectTransfer")
                          OR EXISTS (SELECT 1 FROM "InventoryCommands" WHERE ("IntentJson"::jsonb ->> 'kind') = '42'))
                         AND current_setting('cropqc.direct_transfer_writer', true) IS DISTINCT FROM 'assignments-v1' THEN
                        RAISE EXCEPTION 'Direct transfers exist: use an assignment-aware canonical writer. Unsafe application downgrade blocked.';
                      END IF;
                      IF TG_TABLE_NAME = 'RoomTransfers' AND TG_OP IN ('UPDATE', 'DELETE') THEN
                        IF OLD."IsDirectTransfer" THEN
                          RAISE EXCEPTION 'Direct transfer history is immutable. Append a compensating correction.';
                        END IF;
                      END IF;
                      IF TG_OP = 'DELETE' THEN RETURN OLD; ELSE RETURN NEW; END IF;
                    END $$;
                    """);
                foreach (var table in new[] { "RoomTransfers", "RoomInventoryAdjustments", "TreatmentLineageSegments", "TreatmentLineageMovements", "RoomTreatmentApplications" })
                    migrationBuilder.Sql($"CREATE TRIGGER direct_transfer_writer_fence BEFORE INSERT OR UPDATE OR DELETE ON \"{table}\" FOR EACH ROW EXECUTE FUNCTION cropqc_require_direct_transfer_writer();");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MigrationProviderTypes.Sql(migrationBuilder,
                "IF EXISTS (SELECT 1 FROM [RoomTransfers] WHERE [IsDirectTransfer] = 1) OR EXISTS (SELECT 1 FROM [InventoryCommands] WHERE JSON_VALUE([IntentJson], '$.kind') = '42') THROW 51000, 'Preserve direct transfer schema after first use.', 1;",
                "DO $$ BEGIN IF EXISTS (SELECT 1 FROM \"RoomTransfers\" WHERE \"IsDirectTransfer\") OR EXISTS (SELECT 1 FROM \"InventoryCommands\" WHERE (\"IntentJson\"::jsonb ->> 'kind') = '42') THEN RAISE EXCEPTION 'Preserve direct transfer schema after first use.'; END IF; END $$;"));
            if (migrationBuilder.ActiveProvider == "Npgsql.EntityFrameworkCore.PostgreSQL")
            {
                foreach (var table in new[] { "RoomTransfers", "RoomInventoryAdjustments", "TreatmentLineageSegments", "TreatmentLineageMovements", "RoomTreatmentApplications" })
                    migrationBuilder.Sql($"DROP TRIGGER direct_transfer_writer_fence ON \"{table}\";");
                migrationBuilder.Sql("DROP FUNCTION cropqc_require_direct_transfer_writer();");
                migrationBuilder.Sql("DROP INDEX \"IX_RoomTransfers_DirectCapability\"; DROP INDEX \"IX_InventoryCommands_ConversionCapability\";");
            }
            migrationBuilder.DropForeignKey(
                name: "FK_RoomTransfers_RoomTransfers_CorrectsRoomTransferId",
                table: "RoomTransfers");

            migrationBuilder.DropIndex(
                name: "IX_RoomTransfers_CorrectsRoomTransferId",
                table: "RoomTransfers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_RoomTransfers_DirectEvidence",
                table: "RoomTransfers");

            migrationBuilder.DropColumn(
                name: "CorrectsRoomTransferId",
                table: "RoomTransfers");

            migrationBuilder.DropColumn(
                name: "IsDirectTransfer",
                table: "RoomTransfers");

            migrationBuilder.DropColumn(
                name: "LocationEvidence",
                table: "RoomTransfers");

            migrationBuilder.DropColumn(
                name: "PhysicalLocationVerified",
                table: "RoomTransfers");
        }
    }
}
