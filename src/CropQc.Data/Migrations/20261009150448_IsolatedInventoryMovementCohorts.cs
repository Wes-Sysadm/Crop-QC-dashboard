using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class IsolatedInventoryMovementCohorts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_TreatmentLineageSegments_Receipt",
                table: "TreatmentLineageSegments");

            migrationBuilder.DropIndex(
                name: "UX_TreatmentLineageSegments_Unassigned",
                table: "TreatmentLineageSegments");

            migrationBuilder.AddColumn<string>(
                name: "CohortKey",
                table: "TreatmentLineageSegments",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(60)", "character varying(60)"),
                maxLength: 60,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "UX_TreatmentLineageSegments_Receipt",
                table: "TreatmentLineageSegments",
                columns: new[] { "RoomId", "IdentityKey", "TreatmentSignature", "ReceiptId", "CohortKey" },
                unique: true,
                filter: MigrationProviderTypes.Sql(migrationBuilder, "[ReceiptId] IS NOT NULL AND [Disposition] = 'Current'", "\"ReceiptId\" IS NOT NULL AND \"Disposition\" = 'Current'"));

            migrationBuilder.CreateIndex(
                name: "UX_TreatmentLineageSegments_Unassigned",
                table: "TreatmentLineageSegments",
                columns: new[] { "RoomId", "IdentityKey", "TreatmentSignature", "CohortKey" },
                unique: true,
                filter: MigrationProviderTypes.Sql(migrationBuilder, "[ReceiptId] IS NULL AND [Disposition] = 'Current'", "\"ReceiptId\" IS NULL AND \"Disposition\" = 'Current'"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MigrationProviderTypes.Sql(migrationBuilder,
                "IF EXISTS (SELECT 1 FROM [TreatmentLineageSegments] WHERE [CohortKey] <> '') THROW 51000, 'Cannot remove cohort evidence after use. Preserve the additive schema and use a compatible application rollback.', 1;",
                "DO $$ BEGIN IF EXISTS (SELECT 1 FROM \"TreatmentLineageSegments\" WHERE \"CohortKey\" <> '') THEN RAISE EXCEPTION 'Cannot remove cohort evidence after use. Preserve the additive schema and use a compatible application rollback.'; END IF; END $$;"));
            migrationBuilder.DropIndex(
                name: "UX_TreatmentLineageSegments_Receipt",
                table: "TreatmentLineageSegments");

            migrationBuilder.DropIndex(
                name: "UX_TreatmentLineageSegments_Unassigned",
                table: "TreatmentLineageSegments");

            migrationBuilder.DropColumn(
                name: "CohortKey",
                table: "TreatmentLineageSegments");

            migrationBuilder.CreateIndex(
                name: "UX_TreatmentLineageSegments_Receipt",
                table: "TreatmentLineageSegments",
                columns: new[] { "RoomId", "IdentityKey", "TreatmentSignature", "ReceiptId" },
                unique: true,
                filter: MigrationProviderTypes.Sql(migrationBuilder, "[ReceiptId] IS NOT NULL AND [Disposition] = 'Current'", "\"ReceiptId\" IS NOT NULL AND \"Disposition\" = 'Current'"));

            migrationBuilder.CreateIndex(
                name: "UX_TreatmentLineageSegments_Unassigned",
                table: "TreatmentLineageSegments",
                columns: new[] { "RoomId", "IdentityKey", "TreatmentSignature" },
                unique: true,
                filter: MigrationProviderTypes.Sql(migrationBuilder, "[ReceiptId] IS NULL AND [Disposition] = 'Current'", "\"ReceiptId\" IS NULL AND \"Disposition\" = 'Current'"));
        }
    }
}
