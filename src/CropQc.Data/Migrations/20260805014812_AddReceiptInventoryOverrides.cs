using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReceiptInventoryOverrides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ReceiptInventoryOverrideId",
                table: "RoomInventoryAdjustments",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "uniqueidentifier", "uuid"),
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ConcurrencyVersion",
                table: "Receipts",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"),
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "ReceiptInventoryOverrides",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: MigrationProviderTypes.StoreType(migrationBuilder, "uniqueidentifier", "uuid"), nullable: false),
                    ReceiptId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false),
                    ActionType = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    OldReceiptBinCount = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    NewReceiptBinCount = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    InventoryDelta = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    CurrentInventoryBefore = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    CurrentInventoryAfter = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    AdministratorUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    Reason = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: false),
                    OperationKey = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(150)", "character varying(150)"), maxLength: 150, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    NegativeInventoryAcknowledged = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    VoidConfirmationDetails = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true),
                    BeforeReceiptSnapshotJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), nullable: false),
                    AfterReceiptSnapshotJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), nullable: false),
                    AffectedInventorySnapshotJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), nullable: false),
                    ExpectedAdjustmentCount = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    IsComplete = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptInventoryOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceiptInventoryOverrides_Receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "Receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptInventoryOverrides_Users_AdministratorUserId",
                        column: x => x.AdministratorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoomInventoryAdjustments_ReceiptInventoryOverrideId",
                table: "RoomInventoryAdjustments",
                column: "ReceiptInventoryOverrideId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptInventoryOverrides_AdministratorUserId",
                table: "ReceiptInventoryOverrides",
                column: "AdministratorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptInventoryOverrides_OperationKey",
                table: "ReceiptInventoryOverrides",
                column: "OperationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptInventoryOverrides_ReceiptId_CreatedAt",
                table: "ReceiptInventoryOverrides",
                columns: new[] { "ReceiptId", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_RoomInventoryAdjustments_ReceiptInventoryOverrides_ReceiptInventoryOverrideId",
                table: "RoomInventoryAdjustments",
                column: "ReceiptInventoryOverrideId",
                principalTable: "ReceiptInventoryOverrides",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RoomInventoryAdjustments_ReceiptInventoryOverrides_ReceiptInventoryOverrideId",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropTable(
                name: "ReceiptInventoryOverrides");

            migrationBuilder.DropIndex(
                name: "IX_RoomInventoryAdjustments_ReceiptInventoryOverrideId",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropColumn(
                name: "ReceiptInventoryOverrideId",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropColumn(
                name: "ConcurrencyVersion",
                table: "Receipts");
        }
    }
}
