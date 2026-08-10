using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryDiagnosticAcknowledgments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InventoryDiagnosticAcknowledgments",
                columns: table => new
                {
                    Id = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DiagnosticKey = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(64)", "character varying(64)"), maxLength: 64, nullable: false),
                    DiagnosticType = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: false),
                    DiagnosticCode = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: false),
                    DiagnosticMessage = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: false),
                    RoomInventoryAdjustmentId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false),
                    InvariantVersion = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    Reason = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(500)", "character varying(500)"), maxLength: 500, nullable: false),
                    DiagnosticSnapshotJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(4000)", "character varying(4000)"), maxLength: 4000, nullable: false),
                    DismissedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    DismissedByEmail = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(320)", "character varying(320)"), maxLength: 320, nullable: false),
                    DismissedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    IsActive = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    RestoredByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    RestoredByEmail = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(320)", "character varying(320)"), maxLength: 320, nullable: true),
                    RestoredAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryDiagnosticAcknowledgments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryDiagnosticAck_Adjustment",
                        column: x => x.RoomInventoryAdjustmentId,
                        principalTable: "RoomInventoryAdjustments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryDiagnosticAck_DismissedBy",
                        column: x => x.DismissedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_InventoryDiagnosticAck_RestoredBy",
                        column: x => x.RestoredByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryDiagnosticAck_Key",
                table: "InventoryDiagnosticAcknowledgments",
                column: "DiagnosticKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryDiagnosticAck_DismissedBy",
                table: "InventoryDiagnosticAcknowledgments",
                column: "DismissedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryDiagnosticAck_ActiveAdjustment",
                table: "InventoryDiagnosticAcknowledgments",
                columns: new[] { "IsActive", "RoomInventoryAdjustmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryDiagnosticAck_RestoredBy",
                table: "InventoryDiagnosticAcknowledgments",
                column: "RestoredByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryDiagnosticAck_Adjustment",
                table: "InventoryDiagnosticAcknowledgments",
                column: "RoomInventoryAdjustmentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryDiagnosticAcknowledgments");
        }
    }
}
