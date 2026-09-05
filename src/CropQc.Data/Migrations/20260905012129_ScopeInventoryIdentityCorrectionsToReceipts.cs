using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class ScopeInventoryIdentityCorrectionsToReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "SourceGrowerLotId",
                table: "InventoryIdentityCorrections",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                nullable: true,
                oldClrType: typeof(int),
                oldType: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"));

            migrationBuilder.DropIndex(
                name: "IX_InventoryIdentityCorrections_CorrectedReceiptId",
                table: "InventoryIdentityCorrections");

            migrationBuilder.DropIndex(
                name: "IX_InventoryIdentityCorrections_SourceCropYear_SourceGrowerLotId_SourceFruitProfileId",
                table: "InventoryIdentityCorrections");

            migrationBuilder.CreateIndex(
                name: "UX_InventoryIdentityCorrections_GlobalSource",
                table: "InventoryIdentityCorrections",
                columns: new[] { "SourceCropYear", "SourceGrowerLotId", "SourceFruitProfileId" },
                unique: true,
                filter: "\"CorrectedReceiptId\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "UX_InventoryIdentityCorrections_ReceiptSource",
                table: "InventoryIdentityCorrections",
                columns: new[] { "CorrectedReceiptId", "SourceCropYear", "SourceGrowerLotId", "SourceFruitProfileId" },
                unique: true,
                filter: "\"CorrectedReceiptId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_InventoryIdentityCorrections_GlobalSource",
                table: "InventoryIdentityCorrections");

            migrationBuilder.DropIndex(
                name: "UX_InventoryIdentityCorrections_ReceiptSource",
                table: "InventoryIdentityCorrections");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryIdentityCorrections_CorrectedReceiptId",
                table: "InventoryIdentityCorrections",
                column: "CorrectedReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryIdentityCorrections_SourceCropYear_SourceGrowerLotId_SourceFruitProfileId",
                table: "InventoryIdentityCorrections",
                columns: new[] { "SourceCropYear", "SourceGrowerLotId", "SourceFruitProfileId" },
                unique: true);

            migrationBuilder.AlterColumn<int>(
                name: "SourceGrowerLotId",
                table: "InventoryIdentityCorrections",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                oldNullable: true);
        }
    }
}
