using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class CanonicalIdentityRestorationSides : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RoomInventoryAdjustments_OutsideWarehouseTransferId_AdjustmentType",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_RoomInventoryAdjustments_ProcessorShipmentLineId_AdjustmentType",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_RoomInventoryAdjustments_RoomInventoryLossId_AdjustmentType",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_RoomInventoryAdjustments_RoomTransferId_AdjustmentType",
                table: "RoomInventoryAdjustments");

            migrationBuilder.CreateIndex(
                name: "IX_RoomInventoryAdjustments_OutsideWarehouseTransferId_AdjustmentType",
                table: "RoomInventoryAdjustments",
                columns: new[] { "OutsideWarehouseTransferId", "AdjustmentType" },
                unique: true,
                filter: MigrationProviderTypes.Sql(migrationBuilder, "[OutsideWarehouseTransferId] IS NOT NULL AND [InventoryInvariantVersion] < 3", "\"OutsideWarehouseTransferId\" IS NOT NULL AND \"InventoryInvariantVersion\" < 3"));

            migrationBuilder.CreateIndex(
                name: "IX_RoomInventoryAdjustments_ProcessorShipmentLineId_AdjustmentType",
                table: "RoomInventoryAdjustments",
                columns: new[] { "ProcessorShipmentLineId", "AdjustmentType" },
                unique: true,
                filter: MigrationProviderTypes.Sql(migrationBuilder, "[ProcessorShipmentLineId] IS NOT NULL AND [InventoryInvariantVersion] < 3", "\"ProcessorShipmentLineId\" IS NOT NULL AND \"InventoryInvariantVersion\" < 3"));

            migrationBuilder.CreateIndex(
                name: "IX_RoomInventoryAdjustments_RoomInventoryLossId_AdjustmentType",
                table: "RoomInventoryAdjustments",
                columns: new[] { "RoomInventoryLossId", "AdjustmentType" },
                unique: true,
                filter: MigrationProviderTypes.Sql(migrationBuilder, "[RoomInventoryLossId] IS NOT NULL AND [InventoryInvariantVersion] < 3", "\"RoomInventoryLossId\" IS NOT NULL AND \"InventoryInvariantVersion\" < 3"));

            migrationBuilder.CreateIndex(
                name: "IX_RoomInventoryAdjustments_RoomTransferId_AdjustmentType",
                table: "RoomInventoryAdjustments",
                columns: new[] { "RoomTransferId", "AdjustmentType" },
                unique: true,
                filter: MigrationProviderTypes.Sql(migrationBuilder, "[RoomTransferId] IS NOT NULL AND [InventoryInvariantVersion] < 3", "\"RoomTransferId\" IS NOT NULL AND \"InventoryInvariantVersion\" < 3"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MigrationProviderTypes.Sql(migrationBuilder,
                "IF EXISTS (SELECT 1 FROM [InventoryCommands]) THROW 51000, 'Canonical command evidence exists; retain identity-restoration schema.', 1;",
                "DO $$ BEGIN IF EXISTS (SELECT 1 FROM \"InventoryCommands\") THEN RAISE EXCEPTION 'Canonical command evidence exists; retain identity-restoration schema.'; END IF; END $$;"));
            migrationBuilder.DropIndex(
                name: "IX_RoomInventoryAdjustments_OutsideWarehouseTransferId_AdjustmentType",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_RoomInventoryAdjustments_ProcessorShipmentLineId_AdjustmentType",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_RoomInventoryAdjustments_RoomInventoryLossId_AdjustmentType",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_RoomInventoryAdjustments_RoomTransferId_AdjustmentType",
                table: "RoomInventoryAdjustments");

            migrationBuilder.CreateIndex(
                name: "IX_RoomInventoryAdjustments_OutsideWarehouseTransferId_AdjustmentType",
                table: "RoomInventoryAdjustments",
                columns: new[] { "OutsideWarehouseTransferId", "AdjustmentType" },
                unique: true,
                filter: MigrationProviderTypes.Sql(migrationBuilder, "[OutsideWarehouseTransferId] IS NOT NULL", "\"OutsideWarehouseTransferId\" IS NOT NULL"));

            migrationBuilder.CreateIndex(
                name: "IX_RoomInventoryAdjustments_ProcessorShipmentLineId_AdjustmentType",
                table: "RoomInventoryAdjustments",
                columns: new[] { "ProcessorShipmentLineId", "AdjustmentType" },
                unique: true,
                filter: MigrationProviderTypes.Sql(migrationBuilder, "[ProcessorShipmentLineId] IS NOT NULL", "\"ProcessorShipmentLineId\" IS NOT NULL"));

            migrationBuilder.CreateIndex(
                name: "IX_RoomInventoryAdjustments_RoomInventoryLossId_AdjustmentType",
                table: "RoomInventoryAdjustments",
                columns: new[] { "RoomInventoryLossId", "AdjustmentType" },
                unique: true,
                filter: MigrationProviderTypes.Sql(migrationBuilder, "[RoomInventoryLossId] IS NOT NULL", "\"RoomInventoryLossId\" IS NOT NULL"));

            migrationBuilder.CreateIndex(
                name: "IX_RoomInventoryAdjustments_RoomTransferId_AdjustmentType",
                table: "RoomInventoryAdjustments",
                columns: new[] { "RoomTransferId", "AdjustmentType" },
                unique: true,
                filter: MigrationProviderTypes.Sql(migrationBuilder, "[RoomTransferId] IS NOT NULL", "\"RoomTransferId\" IS NOT NULL"));
        }
    }
}
