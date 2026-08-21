using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProcessorShipments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ProcessorShipmentLineId",
                table: "TreatmentLineageMovements",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"),
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ProcessorShipmentLineId",
                table: "RoomInventoryAdjustments",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"),
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Processors",
                columns: table => new
                {
                    Id = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"), maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    Notes = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    CreatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    UpdatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Processors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Processors_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Processors_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ProcessorShipments",
                columns: table => new
                {
                    Id = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OperationKey = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(150)", "character varying(150)"), maxLength: 150, nullable: false),
                    ProcessorId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    ProcessorNameSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"), maxLength: 200, nullable: false),
                    ShippedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    OriginalSaleRate = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,4)", "numeric(18,4)"), precision: 18, scale: 4, nullable: false),
                    OriginalPricingBasis = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(20)", "character varying(20)"), maxLength: 20, nullable: false),
                    SaleRate = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,4)", "numeric(18,4)"), precision: 18, scale: 4, nullable: false),
                    PricingBasis = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(20)", "character varying(20)"), maxLength: 20, nullable: false),
                    Currency = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(3)", "character varying(3)"), maxLength: 3, nullable: false),
                    ReferenceNumber = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    ReversedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true),
                    ReversedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    ReversalReason = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true),
                    ConcurrencyVersion = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessorShipments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessorShipments_Processors_ProcessorId",
                        column: x => x.ProcessorId,
                        principalTable: "Processors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcessorShipments_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcessorShipments_Users_ReversedByUserId",
                        column: x => x.ReversedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcessorShipmentLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProcessorShipmentId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false),
                    WarehouseId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    RoomId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    CropYear = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    ReceiptId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: true),
                    SourceInventoryAdjustmentId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: true),
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
                    BinsSent = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    PoundsPerBinSnapshot = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,4)", "numeric(18,4)"), precision: 18, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessorShipmentLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessorShipmentLines_ProcessorShipments_ProcessorShipmentId",
                        column: x => x.ProcessorShipmentId,
                        principalTable: "ProcessorShipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcessorShipmentLines_Receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "Receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ProcessorShipmentLines_RoomInventoryAdjustments_SourceInventoryAdjustmentId",
                        column: x => x.SourceInventoryAdjustmentId,
                        principalTable: "RoomInventoryAdjustments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ProcessorShipmentLines_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcessorShipmentLines_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProcessorShipmentPriceCorrections",
                columns: table => new
                {
                    Id = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProcessorShipmentId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false),
                    OperationKey = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(150)", "character varying(150)"), maxLength: 150, nullable: false),
                    OriginalSaleRate = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,4)", "numeric(18,4)"), precision: 18, scale: 4, nullable: false),
                    OriginalPricingBasis = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(20)", "character varying(20)"), maxLength: 20, nullable: false),
                    CorrectedSaleRate = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,4)", "numeric(18,4)"), precision: 18, scale: 4, nullable: false),
                    CorrectedPricingBasis = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(20)", "character varying(20)"), maxLength: 20, nullable: false),
                    Reason = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: false),
                    CorrectedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    CorrectedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessorShipmentPriceCorrections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessorShipmentPriceCorrections_ProcessorShipments_ProcessorShipmentId",
                        column: x => x.ProcessorShipmentId,
                        principalTable: "ProcessorShipments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProcessorShipmentPriceCorrections_Users_CorrectedByUserId",
                        column: x => x.CorrectedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "RolePageAccesses",
                columns: new[] { "Id", "AccessLevel", "AreaKey", "RoleId", "UpdatedAt", "UpdatedByUserId" },
                values: new object[,]
                {
                    { -205, "View", "processor-shipments", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { -204, "View", "processor-shipments", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { -203, "View", "processor-shipments", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { -202, "Admin", "processor-shipments", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { -201, "Admin", "processor-shipments", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null }
                });

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentLineageMovements_ProcessorShipmentLineId",
                table: "TreatmentLineageMovements",
                column: "ProcessorShipmentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomInventoryAdjustments_ProcessorShipmentLineId",
                table: "RoomInventoryAdjustments",
                column: "ProcessorShipmentLineId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomInventoryAdjustments_ProcessorShipmentLineId_AdjustmentType",
                table: "RoomInventoryAdjustments",
                columns: new[] { "ProcessorShipmentLineId", "AdjustmentType" },
                unique: true,
                filter: MigrationProviderTypes.Sql(migrationBuilder, "[ProcessorShipmentLineId] IS NOT NULL", "\"ProcessorShipmentLineId\" IS NOT NULL"));

            migrationBuilder.CreateIndex(
                name: "IX_Processors_CreatedByUserId",
                table: "Processors",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Processors_IsActive_Name",
                table: "Processors",
                columns: new[] { "IsActive", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Processors_Name",
                table: "Processors",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Processors_UpdatedByUserId",
                table: "Processors",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessorShipmentLines_ProcessorShipmentId",
                table: "ProcessorShipmentLines",
                column: "ProcessorShipmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessorShipmentLines_ReceiptId",
                table: "ProcessorShipmentLines",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessorShipmentLines_RoomId",
                table: "ProcessorShipmentLines",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessorShipmentLines_SourceInventoryAdjustmentId",
                table: "ProcessorShipmentLines",
                column: "SourceInventoryAdjustmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessorShipmentLines_WarehouseId_RoomId",
                table: "ProcessorShipmentLines",
                columns: new[] { "WarehouseId", "RoomId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessorShipmentPriceCorrections_CorrectedByUserId",
                table: "ProcessorShipmentPriceCorrections",
                column: "CorrectedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessorShipmentPriceCorrections_OperationKey",
                table: "ProcessorShipmentPriceCorrections",
                column: "OperationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessorShipmentPriceCorrections_ProcessorShipmentId_CorrectedAt",
                table: "ProcessorShipmentPriceCorrections",
                columns: new[] { "ProcessorShipmentId", "CorrectedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProcessorShipments_CreatedByUserId",
                table: "ProcessorShipments",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessorShipments_OperationKey",
                table: "ProcessorShipments",
                column: "OperationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProcessorShipments_ProcessorId",
                table: "ProcessorShipments",
                column: "ProcessorId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessorShipments_ReversedByUserId",
                table: "ProcessorShipments",
                column: "ReversedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessorShipments_ShippedAt_ProcessorId",
                table: "ProcessorShipments",
                columns: new[] { "ShippedAt", "ProcessorId" });

            migrationBuilder.AddForeignKey(
                name: "FK_RoomInventoryAdjustments_ProcessorShipmentLines_ProcessorShipmentLineId",
                table: "RoomInventoryAdjustments",
                column: "ProcessorShipmentLineId",
                principalTable: "ProcessorShipmentLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TreatmentLineageMovements_ProcessorShipmentLines_ProcessorShipmentLineId",
                table: "TreatmentLineageMovements",
                column: "ProcessorShipmentLineId",
                principalTable: "ProcessorShipmentLines",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RoomInventoryAdjustments_ProcessorShipmentLines_ProcessorShipmentLineId",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropForeignKey(
                name: "FK_TreatmentLineageMovements_ProcessorShipmentLines_ProcessorShipmentLineId",
                table: "TreatmentLineageMovements");

            migrationBuilder.DropTable(
                name: "ProcessorShipmentLines");

            migrationBuilder.DropTable(
                name: "ProcessorShipmentPriceCorrections");

            migrationBuilder.DropTable(
                name: "ProcessorShipments");

            migrationBuilder.DropTable(
                name: "Processors");

            migrationBuilder.DropIndex(
                name: "IX_TreatmentLineageMovements_ProcessorShipmentLineId",
                table: "TreatmentLineageMovements");

            migrationBuilder.DropIndex(
                name: "IX_RoomInventoryAdjustments_ProcessorShipmentLineId",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_RoomInventoryAdjustments_ProcessorShipmentLineId_AdjustmentType",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DeleteData(
                table: "RolePageAccesses",
                keyColumn: "Id",
                keyValue: -205);

            migrationBuilder.DeleteData(
                table: "RolePageAccesses",
                keyColumn: "Id",
                keyValue: -204);

            migrationBuilder.DeleteData(
                table: "RolePageAccesses",
                keyColumn: "Id",
                keyValue: -203);

            migrationBuilder.DeleteData(
                table: "RolePageAccesses",
                keyColumn: "Id",
                keyValue: -202);

            migrationBuilder.DeleteData(
                table: "RolePageAccesses",
                keyColumn: "Id",
                keyValue: -201);

            migrationBuilder.DropColumn(
                name: "ProcessorShipmentLineId",
                table: "TreatmentLineageMovements");

            migrationBuilder.DropColumn(
                name: "ProcessorShipmentLineId",
                table: "RoomInventoryAdjustments");
        }
    }
}
