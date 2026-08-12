using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRoomInventoryLosses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "RoomInventoryLossId",
                table: "RoomInventoryAdjustments",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"),
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RoomInventoryLosses",
                columns: table => new
                {
                    Id = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OperationKey = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(150)", "character varying(150)"), maxLength: 150, nullable: false),
                    WarehouseId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    RoomId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    ReceiptId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: true),
                    CropYear = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    GrowerLotId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    FruitProfileId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    GrowerName = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"), maxLength: 200, nullable: false),
                    GrowerNumber = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: true),
                    LotNumber = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: false),
                    PoolStart = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(20)", "character varying(20)"), maxLength: 20, nullable: true),
                    VarietyCode = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    InventoryStatus = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: true),
                    LossType = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    BinCount = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    Reason = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(500)", "character varying(500)"), maxLength: 500, nullable: false),
                    Notes = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true),
                    CreatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    IsReversed = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    ReversedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true),
                    ReversedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    ReverseReason = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomInventoryLosses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoomInventoryLosses_FruitProfiles_FruitProfileId",
                        column: x => x.FruitProfileId,
                        principalTable: "FruitProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoomInventoryLosses_Receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "Receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoomInventoryLosses_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoomInventoryLosses_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoomInventoryLosses_Users_ReversedByUserId",
                        column: x => x.ReversedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoomInventoryLosses_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoomInventoryAdjustments_RoomInventoryLossId",
                table: "RoomInventoryAdjustments",
                column: "RoomInventoryLossId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomInventoryAdjustments_RoomInventoryLossId_AdjustmentType",
                table: "RoomInventoryAdjustments",
                columns: new[] { "RoomInventoryLossId", "AdjustmentType" },
                unique: true,
                filter: MigrationProviderTypes.Sql(
                    migrationBuilder,
                    "[RoomInventoryLossId] IS NOT NULL",
                    "\"RoomInventoryLossId\" IS NOT NULL"));

            migrationBuilder.CreateIndex(
                name: "IX_RoomInventoryLosses_CreatedByUserId",
                table: "RoomInventoryLosses",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomInventoryLosses_FruitProfileId",
                table: "RoomInventoryLosses",
                column: "FruitProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomInventoryLosses_OperationKey",
                table: "RoomInventoryLosses",
                column: "OperationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoomInventoryLosses_ReceiptId_CreatedAt",
                table: "RoomInventoryLosses",
                columns: new[] { "ReceiptId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RoomInventoryLosses_ReversedByUserId",
                table: "RoomInventoryLosses",
                column: "ReversedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomInventoryLosses_RoomId_CreatedAt",
                table: "RoomInventoryLosses",
                columns: new[] { "RoomId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RoomInventoryLosses_WarehouseId",
                table: "RoomInventoryLosses",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_RoomInventoryAdjustments_RoomInventoryLosses_RoomInventoryLossId",
                table: "RoomInventoryAdjustments",
                column: "RoomInventoryLossId",
                principalTable: "RoomInventoryLosses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RoomInventoryAdjustments_RoomInventoryLosses_RoomInventoryLossId",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropTable(
                name: "RoomInventoryLosses");

            migrationBuilder.DropIndex(
                name: "IX_RoomInventoryAdjustments_RoomInventoryLossId",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_RoomInventoryAdjustments_RoomInventoryLossId_AdjustmentType",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropColumn(
                name: "RoomInventoryLossId",
                table: "RoomInventoryAdjustments");
        }
    }
}
