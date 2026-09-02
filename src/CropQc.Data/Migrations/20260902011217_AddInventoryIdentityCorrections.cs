using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryIdentityCorrections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "InventoryIdentityCorrectionId",
                table: "TreatmentLineageMovements",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "uniqueidentifier", "uuid"),
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InventoryIdentityCorrectionId",
                table: "RoomInventoryAdjustments",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "uniqueidentifier", "uuid"),
                nullable: true);

            migrationBuilder.CreateTable(
                name: "InventoryIdentityCorrections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: MigrationProviderTypes.StoreType(migrationBuilder, "uniqueidentifier", "uuid"), nullable: false),
                    OperationKey = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(150)", "character varying(150)"), maxLength: 150, nullable: false),
                    SourceCropYear = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    SourceGrowerLotId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    SourceFruitProfileId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    TargetCropYear = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    TargetGrowerLotId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    TargetFruitProfileId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    CorrectedReceiptId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: true),
                    ReceiptInventoryOverrideId = table.Column<Guid>(type: MigrationProviderTypes.StoreType(migrationBuilder, "uniqueidentifier", "uuid"), nullable: true),
                    Reason = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: false),
                    CreatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    SourceIdentitySnapshotJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), nullable: false),
                    TargetIdentitySnapshotJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), nullable: false),
                    ExpectedAdjustmentCount = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    ExpectedTreatmentMovementCount = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    IsComplete = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    IsActive = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryIdentityCorrections", x => x.Id);
                    table.CheckConstraint("CK_InventoryIdentityCorrections_NonSelf", "\"SourceCropYear\" <> \"TargetCropYear\" OR \"SourceGrowerLotId\" <> \"TargetGrowerLotId\" OR \"SourceFruitProfileId\" <> \"TargetFruitProfileId\"");
                    table.CheckConstraint("CK_InventoryIdentityCorrections_PositiveCropYears", "\"SourceCropYear\" > 0 AND \"TargetCropYear\" > 0");
                    table.ForeignKey(
                        name: "FK_InventoryIdentityCorrections_FruitProfiles_SourceFruitProfileId",
                        column: x => x.SourceFruitProfileId,
                        principalTable: "FruitProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryIdentityCorrections_FruitProfiles_TargetFruitProfileId",
                        column: x => x.TargetFruitProfileId,
                        principalTable: "FruitProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryIdentityCorrections_GrowerLots_SourceGrowerLotId",
                        column: x => x.SourceGrowerLotId,
                        principalTable: "GrowerLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryIdentityCorrections_GrowerLots_TargetGrowerLotId",
                        column: x => x.TargetGrowerLotId,
                        principalTable: "GrowerLots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryIdentityCorrections_ReceiptInventoryOverrides_ReceiptInventoryOverrideId",
                        column: x => x.ReceiptInventoryOverrideId,
                        principalTable: "ReceiptInventoryOverrides",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryIdentityCorrections_Receipts_CorrectedReceiptId",
                        column: x => x.CorrectedReceiptId,
                        principalTable: "Receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryIdentityCorrections_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentLineageMovements_InventoryIdentityCorrectionId",
                table: "TreatmentLineageMovements",
                column: "InventoryIdentityCorrectionId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomInventoryAdjustments_InventoryIdentityCorrectionId",
                table: "RoomInventoryAdjustments",
                column: "InventoryIdentityCorrectionId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryIdentityCorrections_CorrectedReceiptId",
                table: "InventoryIdentityCorrections",
                column: "CorrectedReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryIdentityCorrections_CreatedByUserId",
                table: "InventoryIdentityCorrections",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryIdentityCorrections_IsActive_CreatedAt",
                table: "InventoryIdentityCorrections",
                columns: new[] { "IsActive", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryIdentityCorrections_OperationKey",
                table: "InventoryIdentityCorrections",
                column: "OperationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryIdentityCorrections_ReceiptInventoryOverrideId",
                table: "InventoryIdentityCorrections",
                column: "ReceiptInventoryOverrideId",
                unique: true,
                filter: MigrationProviderTypes.Sql(
                    migrationBuilder,
                    "[ReceiptInventoryOverrideId] IS NOT NULL",
                    "\"ReceiptInventoryOverrideId\" IS NOT NULL"));

            migrationBuilder.CreateIndex(
                name: "IX_InventoryIdentityCorrections_SourceCropYear_SourceGrowerLotId_SourceFruitProfileId",
                table: "InventoryIdentityCorrections",
                columns: new[] { "SourceCropYear", "SourceGrowerLotId", "SourceFruitProfileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryIdentityCorrections_SourceFruitProfileId",
                table: "InventoryIdentityCorrections",
                column: "SourceFruitProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryIdentityCorrections_SourceGrowerLotId",
                table: "InventoryIdentityCorrections",
                column: "SourceGrowerLotId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryIdentityCorrections_TargetCropYear_TargetGrowerLotId_TargetFruitProfileId",
                table: "InventoryIdentityCorrections",
                columns: new[] { "TargetCropYear", "TargetGrowerLotId", "TargetFruitProfileId" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryIdentityCorrections_TargetFruitProfileId",
                table: "InventoryIdentityCorrections",
                column: "TargetFruitProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryIdentityCorrections_TargetGrowerLotId",
                table: "InventoryIdentityCorrections",
                column: "TargetGrowerLotId");

            migrationBuilder.AddForeignKey(
                name: "FK_RoomInventoryAdjustments_InventoryIdentityCorrections_InventoryIdentityCorrectionId",
                table: "RoomInventoryAdjustments",
                column: "InventoryIdentityCorrectionId",
                principalTable: "InventoryIdentityCorrections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TreatmentLineageMovements_InventoryIdentityCorrections_InventoryIdentityCorrectionId",
                table: "TreatmentLineageMovements",
                column: "InventoryIdentityCorrectionId",
                principalTable: "InventoryIdentityCorrections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RoomInventoryAdjustments_InventoryIdentityCorrections_InventoryIdentityCorrectionId",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropForeignKey(
                name: "FK_TreatmentLineageMovements_InventoryIdentityCorrections_InventoryIdentityCorrectionId",
                table: "TreatmentLineageMovements");

            migrationBuilder.DropTable(
                name: "InventoryIdentityCorrections");

            migrationBuilder.DropIndex(
                name: "IX_TreatmentLineageMovements_InventoryIdentityCorrectionId",
                table: "TreatmentLineageMovements");

            migrationBuilder.DropIndex(
                name: "IX_RoomInventoryAdjustments_InventoryIdentityCorrectionId",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropColumn(
                name: "InventoryIdentityCorrectionId",
                table: "TreatmentLineageMovements");

            migrationBuilder.DropColumn(
                name: "InventoryIdentityCorrectionId",
                table: "RoomInventoryAdjustments");
        }
    }
}
