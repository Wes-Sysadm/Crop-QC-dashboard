using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTruckReceiptReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RoomInventoryAdjustments_InterCrewTransferId_AdjustmentType",
                table: "RoomInventoryAdjustments");

            migrationBuilder.AddColumn<bool>(
                name: "IsTransferReceipt",
                table: "Receipts",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"),
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TransferCompletedAt",
                table: "Receipts",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"),
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReceivingReceiptId",
                table: "InterCrewTransfers",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresTruckReceipt",
                table: "InterCrewTransfers",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"),
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "ReceiptVarietyLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReceiptId = table.Column<long>(type: "bigint", nullable: false),
                    FruitProfileId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    BinCount = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptVarietyLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceiptVarietyLines_FruitProfiles_FruitProfileId",
                        column: x => x.FruitProfileId,
                        principalTable: "FruitProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptVarietyLines_Receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "Receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoomInventoryAdjustments_InterCrewTransferId_AdjustmentType",
                table: "RoomInventoryAdjustments",
                columns: new[] { "InterCrewTransferId", "AdjustmentType" },
                filter: MigrationProviderTypes.Sql(migrationBuilder, "[InterCrewTransferId] IS NOT NULL", "\"InterCrewTransferId\" IS NOT NULL"));

            migrationBuilder.CreateIndex(
                name: "IX_InterCrewTransfers_ReceivingReceiptId",
                table: "InterCrewTransfers",
                column: "ReceivingReceiptId",
                unique: true,
                filter: MigrationProviderTypes.Sql(migrationBuilder, "[ReceivingReceiptId] IS NOT NULL", "\"ReceivingReceiptId\" IS NOT NULL"));

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptVarietyLines_FruitProfileId",
                table: "ReceiptVarietyLines",
                column: "FruitProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptVarietyLines_ReceiptId_FruitProfileId",
                table: "ReceiptVarietyLines",
                columns: new[] { "ReceiptId", "FruitProfileId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_InterCrewTransfers_Receipts_ReceivingReceiptId",
                table: "InterCrewTransfers",
                column: "ReceivingReceiptId",
                principalTable: "Receipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MigrationProviderTypes.Sql(migrationBuilder,
                "IF EXISTS (SELECT 1 FROM [Receipts] WHERE [IsTransferReceipt] = 1 OR [TransferCompletedAt] IS NOT NULL) OR EXISTS (SELECT 1 FROM [InterCrewTransfers] WHERE [RequiresTruckReceipt] = 1 OR [ReceivingReceiptId] IS NOT NULL) OR EXISTS (SELECT 1 FROM [ReceiptVarietyLines]) THROW 51000, 'Truck Receipt evidence exists. Preserve schema and use a feature-aware application rollback.', 1;",
                "DO $$ BEGIN IF EXISTS (SELECT 1 FROM \"Receipts\" WHERE \"IsTransferReceipt\" OR \"TransferCompletedAt\" IS NOT NULL) OR EXISTS (SELECT 1 FROM \"InterCrewTransfers\" WHERE \"RequiresTruckReceipt\" OR \"ReceivingReceiptId\" IS NOT NULL) OR EXISTS (SELECT 1 FROM \"ReceiptVarietyLines\") THEN RAISE EXCEPTION 'Truck Receipt evidence exists. Preserve schema and use a feature-aware application rollback.'; END IF; END $$;"));
            migrationBuilder.DropForeignKey(
                name: "FK_InterCrewTransfers_Receipts_ReceivingReceiptId",
                table: "InterCrewTransfers");

            migrationBuilder.DropTable(
                name: "ReceiptVarietyLines");

            migrationBuilder.DropIndex(
                name: "IX_RoomInventoryAdjustments_InterCrewTransferId_AdjustmentType",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_InterCrewTransfers_ReceivingReceiptId",
                table: "InterCrewTransfers");

            migrationBuilder.DropColumn(
                name: "IsTransferReceipt",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "TransferCompletedAt",
                table: "Receipts");

            migrationBuilder.DropColumn(
                name: "ReceivingReceiptId",
                table: "InterCrewTransfers");

            migrationBuilder.DropColumn(
                name: "RequiresTruckReceipt",
                table: "InterCrewTransfers");

            migrationBuilder.CreateIndex(
                name: "IX_RoomInventoryAdjustments_InterCrewTransferId_AdjustmentType",
                table: "RoomInventoryAdjustments",
                columns: new[] { "InterCrewTransferId", "AdjustmentType" },
                unique: true,
                filter: MigrationProviderTypes.Sql(migrationBuilder, "[InterCrewTransferId] IS NOT NULL", "\"InterCrewTransferId\" IS NOT NULL"));
        }
    }
}
