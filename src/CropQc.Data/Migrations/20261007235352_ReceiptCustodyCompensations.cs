using System;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReceiptCustodyCompensations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReceiptCustodyReversals",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1").Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AcknowledgmentId = table.Column<long>(type: "bigint", nullable: false),
                    PlacementId = table.Column<long>(type: "bigint", nullable: true),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    OperationKey = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(60)", "character varying(60)"), maxLength: 60, nullable: false),
                    Reason = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: false),
                    ActorId = table.Column<int>(type: "int", nullable: false),
                    ReversedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    InventoryAdjustmentId = table.Column<long>(type: "bigint", nullable: true),
                    MovementId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptCustodyReversals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReceiptCustodyReversals_ReceiptCustodyAcknowledgments_AcknowledgmentId",
                        column: x => x.AcknowledgmentId,
                        principalTable: "ReceiptCustodyAcknowledgments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptCustodyReversals_ReceiptCustodyPlacements_PlacementId",
                        column: x => x.PlacementId,
                        principalTable: "ReceiptCustodyPlacements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptCustodyReversals_RoomInventoryAdjustments_InventoryAdjustmentId",
                        column: x => x.InventoryAdjustmentId,
                        principalTable: "RoomInventoryAdjustments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReceiptCustodyReversals_TreatmentLineageMovements_MovementId",
                        column: x => x.MovementId,
                        principalTable: "TreatmentLineageMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptCustodyReversals_AcknowledgmentId",
                table: "ReceiptCustodyReversals",
                column: "AcknowledgmentId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptCustodyReversals_InventoryAdjustmentId",
                table: "ReceiptCustodyReversals",
                column: "InventoryAdjustmentId",
                unique: true,
                filter: MigrationProviderTypes.Sql(migrationBuilder, "[InventoryAdjustmentId] IS NOT NULL", "\"InventoryAdjustmentId\" IS NOT NULL"));

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptCustodyReversals_MovementId",
                table: "ReceiptCustodyReversals",
                column: "MovementId",
                unique: true,
                filter: MigrationProviderTypes.Sql(migrationBuilder, "[MovementId] IS NOT NULL", "\"MovementId\" IS NOT NULL"));

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptCustodyReversals_OperationKey_AcknowledgmentId_PlacementId",
                table: "ReceiptCustodyReversals",
                columns: new[] { "OperationKey", "AcknowledgmentId", "PlacementId" },
                unique: true,
                filter: MigrationProviderTypes.Sql(migrationBuilder, "[PlacementId] IS NOT NULL", "\"PlacementId\" IS NOT NULL"));

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptCustodyReversals_PlacementId",
                table: "ReceiptCustodyReversals",
                column: "PlacementId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MigrationProviderTypes.Sql(migrationBuilder,
                "IF EXISTS (SELECT 1 FROM [ReceiptCustodyReversals]) THROW 51000, 'Custody compensations exist. A compensation-aware application is required.', 1;",
                "DO $$ BEGIN IF EXISTS (SELECT 1 FROM \"ReceiptCustodyReversals\") THEN RAISE EXCEPTION 'Custody compensations exist. A compensation-aware application is required.'; END IF; END $$;"));
            migrationBuilder.DropTable(
                name: "ReceiptCustodyReversals");
        }
    }
}
