using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class EnforceRoomInventoryDeductionParents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "InventoryInvariantVersion",
                table: "RoomInventoryAdjustments",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "InventoryOperationKey",
                table: "RoomInventoryAdjustments",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"),
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RoomTransferId",
                table: "RoomInventoryAdjustments",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"),
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RoomTransfers",
                columns: table => new
                {
                    Id = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OperationKey = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(150)", "character varying(150)"), maxLength: 150, nullable: false),
                    SourceWarehouseId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    SourceRoomId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    DestinationWarehouseId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    DestinationRoomId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    CropYear = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    GrowerLotId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    FruitProfileId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    GrowerName = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"), maxLength: 200, nullable: false),
                    LotNumber = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: false),
                    PoolStart = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(20)", "character varying(20)"), maxLength: 20, nullable: true),
                    VarietyCode = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: true),
                    InventoryStatus = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: true),
                    BinCount = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    Reason = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(500)", "character varying(500)"), maxLength: 500, nullable: false),
                    Notes = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true),
                    TransferredAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    CreatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    IsReversed = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    ReversedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true),
                    ReversedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    ReverseReason = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true),
                    ReversesRoomTransferId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoomTransfers_FruitProfiles_FruitProfileId",
                        column: x => x.FruitProfileId,
                        principalTable: "FruitProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RoomTransfers_Rooms_DestinationRoomId",
                        column: x => x.DestinationRoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoomTransfers_Rooms_SourceRoomId",
                        column: x => x.SourceRoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoomTransfers_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RoomTransfers_Users_ReversedByUserId",
                        column: x => x.ReversedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RoomTransfers_RoomTransfers_ReversesRoomTransferId",
                        column: x => x.ReversesRoomTransferId,
                        principalTable: "RoomTransfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoomTransfers_Warehouses_DestinationWarehouseId",
                        column: x => x.DestinationWarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoomTransfers_Warehouses_SourceWarehouseId",
                        column: x => x.SourceWarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoomInventoryAdjustments_InventoryOperationKey",
                table: "RoomInventoryAdjustments",
                column: "InventoryOperationKey",
                unique: true,
                filter: MigrationProviderTypes.Sql(
                    migrationBuilder,
                    "[InventoryOperationKey] IS NOT NULL",
                    "\"InventoryOperationKey\" IS NOT NULL"));

            migrationBuilder.CreateIndex(
                name: "IX_RoomInventoryAdjustments_RoomTransferId_AdjustmentType",
                table: "RoomInventoryAdjustments",
                columns: new[] { "RoomTransferId", "AdjustmentType" },
                unique: true,
                filter: MigrationProviderTypes.Sql(
                    migrationBuilder,
                    "[RoomTransferId] IS NOT NULL",
                    "\"RoomTransferId\" IS NOT NULL"));

            migrationBuilder.CreateIndex(
                name: "UX_BinsRunEntries_InventoryAdjustmentId_Invariant",
                table: "BinsRunEntries",
                column: "InventoryAdjustmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoomTransfers_CreatedByUserId",
                table: "RoomTransfers",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomTransfers_DestinationRoomId_TransferredAt",
                table: "RoomTransfers",
                columns: new[] { "DestinationRoomId", "TransferredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RoomTransfers_DestinationWarehouseId",
                table: "RoomTransfers",
                column: "DestinationWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomTransfers_FruitProfileId",
                table: "RoomTransfers",
                column: "FruitProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomTransfers_GrowerLotId",
                table: "RoomTransfers",
                column: "GrowerLotId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomTransfers_OperationKey",
                table: "RoomTransfers",
                column: "OperationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoomTransfers_ReversedByUserId",
                table: "RoomTransfers",
                column: "ReversedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomTransfers_ReversesRoomTransferId",
                table: "RoomTransfers",
                column: "ReversesRoomTransferId",
                unique: true,
                filter: MigrationProviderTypes.Sql(
                    migrationBuilder,
                    "[ReversesRoomTransferId] IS NOT NULL",
                    "\"ReversesRoomTransferId\" IS NOT NULL"));

            migrationBuilder.CreateIndex(
                name: "IX_RoomTransfers_SourceRoomId_TransferredAt",
                table: "RoomTransfers",
                columns: new[] { "SourceRoomId", "TransferredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RoomTransfers_SourceWarehouseId",
                table: "RoomTransfers",
                column: "SourceWarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_RoomInventoryAdjustments_RoomTransfers_RoomTransferId",
                table: "RoomInventoryAdjustments",
                column: "RoomTransferId",
                principalTable: "RoomTransfers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RoomInventoryAdjustments_RoomTransfers_RoomTransferId",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropTable(
                name: "RoomTransfers");

            migrationBuilder.DropIndex(
                name: "IX_RoomInventoryAdjustments_InventoryOperationKey",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_RoomInventoryAdjustments_RoomTransferId_AdjustmentType",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropIndex(
                name: "UX_BinsRunEntries_InventoryAdjustmentId_Invariant",
                table: "BinsRunEntries");

            migrationBuilder.DropColumn(
                name: "InventoryInvariantVersion",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropColumn(
                name: "InventoryOperationKey",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropColumn(
                name: "RoomTransferId",
                table: "RoomInventoryAdjustments");

        }
    }
}
