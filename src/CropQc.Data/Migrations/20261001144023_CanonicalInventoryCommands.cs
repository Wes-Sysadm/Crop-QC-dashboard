using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class CanonicalInventoryCommands : Migration
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
                name: "Disposition",
                table: "TreatmentLineageSegments",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(20)", "character varying(20)"),
                maxLength: 20,
                nullable: false,
                defaultValue: "Current");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RetiredAt",
                table: "TreatmentLineageSegments",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"),
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RetiredByCommandKey",
                table: "TreatmentLineageSegments",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"),
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RetiredQuantity",
                table: "TreatmentLineageSegments",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InventoryCommands",
                columns: table => new
                {
                    OperationKey = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: false),
                    IntentHash = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(64)", "character varying(64)"), maxLength: 64, nullable: false),
                    IntentJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), nullable: false),
                    ResultJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), nullable: false),
                    ReversesOperationKey = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: true),
                    ActorId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    CommittedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryCommands", x => x.OperationKey);
                });

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

            migrationBuilder.CreateIndex(
                name: "IX_InventoryCommands_ReversesOperationKey",
                table: "InventoryCommands",
                column: "ReversesOperationKey",
                unique: true,
                filter: MigrationProviderTypes.Sql(migrationBuilder, "[ReversesOperationKey] IS NOT NULL", "\"ReversesOperationKey\" IS NOT NULL"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MigrationProviderTypes.Sql(migrationBuilder,
                "IF EXISTS (SELECT 1 FROM [InventoryCommands]) OR EXISTS (SELECT 1 FROM [TreatmentLineageSegments] WHERE [Disposition] <> 'Current') THROW 51000, 'Canonical command evidence exists; preserve additive schema.', 1;",
                "DO $$ BEGIN IF EXISTS (SELECT 1 FROM \"InventoryCommands\") OR EXISTS (SELECT 1 FROM \"TreatmentLineageSegments\" WHERE \"Disposition\" <> 'Current') THEN RAISE EXCEPTION 'Canonical command evidence exists; preserve additive schema.'; END IF; END $$;"));

            migrationBuilder.DropTable(
                name: "InventoryCommands");

            migrationBuilder.DropIndex(
                name: "UX_TreatmentLineageSegments_Receipt",
                table: "TreatmentLineageSegments");

            migrationBuilder.DropIndex(
                name: "UX_TreatmentLineageSegments_Unassigned",
                table: "TreatmentLineageSegments");

            migrationBuilder.DropColumn(
                name: "Disposition",
                table: "TreatmentLineageSegments");

            migrationBuilder.DropColumn(
                name: "RetiredAt",
                table: "TreatmentLineageSegments");

            migrationBuilder.DropColumn(
                name: "RetiredByCommandKey",
                table: "TreatmentLineageSegments");

            migrationBuilder.DropColumn(
                name: "RetiredQuantity",
                table: "TreatmentLineageSegments");

            migrationBuilder.CreateIndex(
                name: "UX_TreatmentLineageSegments_Receipt",
                table: "TreatmentLineageSegments",
                columns: new[] { "RoomId", "IdentityKey", "TreatmentSignature", "ReceiptId" },
                unique: true,
                filter: MigrationProviderTypes.Sql(migrationBuilder, "[ReceiptId] IS NOT NULL", "\"ReceiptId\" IS NOT NULL"));

            migrationBuilder.CreateIndex(
                name: "UX_TreatmentLineageSegments_Unassigned",
                table: "TreatmentLineageSegments",
                columns: new[] { "RoomId", "IdentityKey", "TreatmentSignature" },
                unique: true,
                filter: MigrationProviderTypes.Sql(migrationBuilder, "[ReceiptId] IS NULL", "\"ReceiptId\" IS NULL"));
        }
    }
}
