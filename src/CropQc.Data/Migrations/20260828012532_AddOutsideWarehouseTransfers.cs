using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOutsideWarehouseTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "OutsideWarehouseTransferId",
                table: "TreatmentLineageMovements",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "OutsideWarehouseTransferId",
                table: "RoomInventoryAdjustments",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "OutsideWarehouses",
                columns: table => new
                {
                    Id = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"), maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    Address = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(500)", "character varying(500)"), maxLength: 500, nullable: true),
                    Notes = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    CreatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    UpdatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutsideWarehouses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutsideWarehouses_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OutsideWarehouses_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "OutsideWarehouseTransfers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OperationKey = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(150)", "character varying(150)"), maxLength: 150, nullable: false),
                    OutsideWarehouseId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    OutsideWarehouseCodeSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    OutsideWarehouseNameSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"), maxLength: 200, nullable: false),
                    OutsideWarehouseAddressSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(500)", "character varying(500)"), maxLength: 500, nullable: true),
                    SourceWarehouseId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    SourceRoomId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    ReceiptId = table.Column<long>(type: "bigint", nullable: true),
                    SourceInventoryAdjustmentId = table.Column<long>(type: "bigint", nullable: true),
                    CropYear = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    GrowerLotId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    FruitProfileId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    GrowerNumberSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: true),
                    GrowerNameSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"), maxLength: 200, nullable: false),
                    LotNumberSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: false),
                    VarietyCodeSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    ProductionTypeSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    IsOrganicSnapshot = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: true),
                    InventoryStatusSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: true),
                    TreatmentStateSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"), maxLength: 25, nullable: false),
                    TreatmentSignatureSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: false),
                    TreatmentSummarySnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(2000)", "character varying(2000)"), maxLength: 2000, nullable: false),
                    BinCount = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    TransferredAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    TruckLoadBolNumber = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(150)", "character varying(150)"), maxLength: 150, nullable: true),
                    Notes = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    IsReversed = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    ReversalOperationKey = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(150)", "character varying(150)"), maxLength: 150, nullable: true),
                    ReversedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true),
                    ReversedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    ReverseReason = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true),
                    ConcurrencyVersion = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutsideWarehouseTransfers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OutsideWarehouseTransfers_FruitProfiles_FruitProfileId",
                        column: x => x.FruitProfileId,
                        principalTable: "FruitProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OutsideWarehouseTransfers_OutsideWarehouses_OutsideWarehouseId",
                        column: x => x.OutsideWarehouseId,
                        principalTable: "OutsideWarehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutsideWarehouseTransfers_Receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "Receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OutsideWarehouseTransfers_RoomInventoryAdjustments_SourceInventoryAdjustmentId",
                        column: x => x.SourceInventoryAdjustmentId,
                        principalTable: "RoomInventoryAdjustments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OutsideWarehouseTransfers_Rooms_SourceRoomId",
                        column: x => x.SourceRoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutsideWarehouseTransfers_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutsideWarehouseTransfers_Users_ReversedByUserId",
                        column: x => x.ReversedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OutsideWarehouseTransfers_Warehouses_SourceWarehouseId",
                        column: x => x.SourceWarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentLineageMovements_OutsideWarehouseTransferId",
                table: "TreatmentLineageMovements",
                column: "OutsideWarehouseTransferId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomInventoryAdjustments_OutsideWarehouseTransferId",
                table: "RoomInventoryAdjustments",
                column: "OutsideWarehouseTransferId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomInventoryAdjustments_OutsideWarehouseTransferId_AdjustmentType",
                table: "RoomInventoryAdjustments",
                columns: new[] { "OutsideWarehouseTransferId", "AdjustmentType" },
                unique: true,
                filter: MigrationProviderTypes.Sql(migrationBuilder, "[OutsideWarehouseTransferId] IS NOT NULL", "\"OutsideWarehouseTransferId\" IS NOT NULL"));

            migrationBuilder.CreateIndex(
                name: "IX_OutsideWarehouses_Code",
                table: "OutsideWarehouses",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutsideWarehouses_CreatedByUserId",
                table: "OutsideWarehouses",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OutsideWarehouses_IsActive_Name",
                table: "OutsideWarehouses",
                columns: new[] { "IsActive", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_OutsideWarehouses_UpdatedByUserId",
                table: "OutsideWarehouses",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OutsideWarehouseTransfers_CreatedByUserId",
                table: "OutsideWarehouseTransfers",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OutsideWarehouseTransfers_FruitProfileId",
                table: "OutsideWarehouseTransfers",
                column: "FruitProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_OutsideWarehouseTransfers_GrowerNumberSnapshot",
                table: "OutsideWarehouseTransfers",
                column: "GrowerNumberSnapshot");

            migrationBuilder.CreateIndex(
                name: "IX_OutsideWarehouseTransfers_OperationKey",
                table: "OutsideWarehouseTransfers",
                column: "OperationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutsideWarehouseTransfers_OutsideWarehouseId",
                table: "OutsideWarehouseTransfers",
                column: "OutsideWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_OutsideWarehouseTransfers_ReceiptId",
                table: "OutsideWarehouseTransfers",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_OutsideWarehouseTransfers_ReversalOperationKey",
                table: "OutsideWarehouseTransfers",
                column: "ReversalOperationKey",
                unique: true,
                filter: MigrationProviderTypes.Sql(migrationBuilder, "[ReversalOperationKey] IS NOT NULL", "\"ReversalOperationKey\" IS NOT NULL"));

            migrationBuilder.CreateIndex(
                name: "IX_OutsideWarehouseTransfers_ReversedByUserId",
                table: "OutsideWarehouseTransfers",
                column: "ReversedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OutsideWarehouseTransfers_SourceInventoryAdjustmentId",
                table: "OutsideWarehouseTransfers",
                column: "SourceInventoryAdjustmentId");

            migrationBuilder.CreateIndex(
                name: "IX_OutsideWarehouseTransfers_SourceRoomId",
                table: "OutsideWarehouseTransfers",
                column: "SourceRoomId");

            migrationBuilder.CreateIndex(
                name: "IX_OutsideWarehouseTransfers_SourceWarehouseId_SourceRoomId_TransferredAt",
                table: "OutsideWarehouseTransfers",
                columns: new[] { "SourceWarehouseId", "SourceRoomId", "TransferredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_OutsideWarehouseTransfers_TransferredAt_OutsideWarehouseId",
                table: "OutsideWarehouseTransfers",
                columns: new[] { "TransferredAt", "OutsideWarehouseId" });

            migrationBuilder.AddForeignKey(
                name: "FK_RoomInventoryAdjustments_OutsideWarehouseTransfers_OutsideWarehouseTransferId",
                table: "RoomInventoryAdjustments",
                column: "OutsideWarehouseTransferId",
                principalTable: "OutsideWarehouseTransfers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TreatmentLineageMovements_OutsideWarehouseTransfers_OutsideWarehouseTransferId",
                table: "TreatmentLineageMovements",
                column: "OutsideWarehouseTransferId",
                principalTable: "OutsideWarehouseTransfers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RoomInventoryAdjustments_OutsideWarehouseTransfers_OutsideWarehouseTransferId",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropForeignKey(
                name: "FK_TreatmentLineageMovements_OutsideWarehouseTransfers_OutsideWarehouseTransferId",
                table: "TreatmentLineageMovements");

            migrationBuilder.DropTable(
                name: "OutsideWarehouseTransfers");

            migrationBuilder.DropTable(
                name: "OutsideWarehouses");

            migrationBuilder.DropIndex(
                name: "IX_TreatmentLineageMovements_OutsideWarehouseTransferId",
                table: "TreatmentLineageMovements");

            migrationBuilder.DropIndex(
                name: "IX_RoomInventoryAdjustments_OutsideWarehouseTransferId",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_RoomInventoryAdjustments_OutsideWarehouseTransferId_AdjustmentType",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropColumn(
                name: "OutsideWarehouseTransferId",
                table: "TreatmentLineageMovements");

            migrationBuilder.DropColumn(
                name: "OutsideWarehouseTransferId",
                table: "RoomInventoryAdjustments");
        }
    }
}
