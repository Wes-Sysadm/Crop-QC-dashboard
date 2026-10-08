using System;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReceiptHeldTransferCustody : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReceiptCustodyAcknowledgments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1").Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReceiptId = table.Column<long>(type: "bigint", nullable: false),
                    InterCrewTransferId = table.Column<long>(type: "bigint", nullable: false),
                    DispatchMovementId = table.Column<long>(type: "bigint", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    OperationKey = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(60)", "character varying(60)"), maxLength: 60, nullable: false),
                    ActorId = table.Column<int>(type: "int", nullable: false),
                    AcknowledgedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptCustodyAcknowledgments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceiptCustodyAcknowledgments_InterCrewTransfers_InterCrewTransferId",
                        column: x => x.InterCrewTransferId,
                        principalTable: "InterCrewTransfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptCustodyAcknowledgments_Receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "Receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptCustodyAcknowledgments_TreatmentLineageMovements_DispatchMovementId",
                        column: x => x.DispatchMovementId,
                        principalTable: "TreatmentLineageMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceiptCustodyPlacements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1").Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AcknowledgmentId = table.Column<long>(type: "bigint", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    OperationKey = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(60)", "character varying(60)"), maxLength: 60, nullable: false),
                    InventoryAdjustmentId = table.Column<long>(type: "bigint", nullable: false),
                    MovementId = table.Column<long>(type: "bigint", nullable: false),
                    PlacedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptCustodyPlacements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceiptCustodyPlacements_ReceiptCustodyAcknowledgments_AcknowledgmentId",
                        column: x => x.AcknowledgmentId,
                        principalTable: "ReceiptCustodyAcknowledgments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptCustodyPlacements_RoomInventoryAdjustments_InventoryAdjustmentId",
                        column: x => x.InventoryAdjustmentId,
                        principalTable: "RoomInventoryAdjustments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptCustodyPlacements_TreatmentLineageMovements_MovementId",
                        column: x => x.MovementId,
                        principalTable: "TreatmentLineageMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptCustodyAcknowledgments_DispatchMovementId",
                table: "ReceiptCustodyAcknowledgments",
                column: "DispatchMovementId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptCustodyAcknowledgments_InterCrewTransferId",
                table: "ReceiptCustodyAcknowledgments",
                column: "InterCrewTransferId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptCustodyAcknowledgments_OperationKey_DispatchMovementId",
                table: "ReceiptCustodyAcknowledgments",
                columns: new[] { "OperationKey", "DispatchMovementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptCustodyAcknowledgments_ReceiptId",
                table: "ReceiptCustodyAcknowledgments",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptCustodyPlacements_AcknowledgmentId",
                table: "ReceiptCustodyPlacements",
                column: "AcknowledgmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptCustodyPlacements_InventoryAdjustmentId",
                table: "ReceiptCustodyPlacements",
                column: "InventoryAdjustmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptCustodyPlacements_MovementId",
                table: "ReceiptCustodyPlacements",
                column: "MovementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptCustodyPlacements_OperationKey_AcknowledgmentId",
                table: "ReceiptCustodyPlacements",
                columns: new[] { "OperationKey", "AcknowledgmentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MigrationProviderTypes.Sql(migrationBuilder,
                "IF EXISTS (SELECT 1 FROM [ReceiptCustodyAcknowledgments]) THROW 51000, 'Receipt custody evidence exists. Preserve the additive schema and use a custody-aware application rollback.', 1;",
                "DO $$ BEGIN IF EXISTS (SELECT 1 FROM \"ReceiptCustodyAcknowledgments\") THEN RAISE EXCEPTION 'Receipt custody evidence exists. Preserve the additive schema and use a custody-aware application rollback.'; END IF; END $$;"));
            migrationBuilder.DropTable(
                name: "ReceiptCustodyPlacements");

            migrationBuilder.DropTable(
                name: "ReceiptCustodyAcknowledgments");
        }
    }
}
