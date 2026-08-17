using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEndOfDayFillWarehouseScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WarehouseId",
                table: "EndOfDayFillReportGroups",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                nullable: true);

            migrationBuilder.Sql(MigrationProviderTypes.Sql(
                migrationBuilder,
                """
                IF NOT EXISTS (SELECT 1 FROM [Warehouses] WHERE [Id] = 4 AND [Code] = N'WP')
                    THROW 51000, 'Warehouse 4 / WP is required for the reviewed End of Day Fill backfill.', 1;
                IF NOT EXISTS (SELECT 1 FROM [Warehouses] WHERE [Id] = 1 AND [Code] = N'EBS')
                    THROW 51000, 'Warehouse 1 / EBS is required for the reviewed End of Day Fill backfill.', 1;
                IF EXISTS (SELECT 1 FROM [EndOfDayFillReportGroups] WHERE [Id] NOT IN (1, 2))
                    THROW 51000, 'Unexpected End of Day Fill groups require review before warehouse-scope migration.', 1;
                IF NOT EXISTS (SELECT 1 FROM [EndOfDayFillReportGroups] WHERE [Id] = 1 AND [Name] = N'WP End of Day Fill' AND [Facility] = N'WP')
                    THROW 51000, 'Reviewed group 1 / WP End of Day Fill was not found exactly.', 1;
                IF NOT EXISTS (SELECT 1 FROM [EndOfDayFillReportGroups] WHERE [Id] = 2 AND [Name] = N'EBS End of Day Fill' AND [Facility] = N'EBS')
                    THROW 51000, 'Reviewed group 2 / EBS End of Day Fill was not found exactly.', 1;
                UPDATE [EndOfDayFillReportGroups] SET [WarehouseId] = 4 WHERE [Id] = 1;
                UPDATE [EndOfDayFillReportGroups] SET [WarehouseId] = 1 WHERE [Id] = 2;
                """,
                """
                DO $warehouse_scope_backfill$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM "Warehouses" WHERE "Id" = 4 AND "Code" = 'WP') THEN
                        RAISE EXCEPTION 'Warehouse 4 / WP is required for the reviewed End of Day Fill backfill.';
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM "Warehouses" WHERE "Id" = 1 AND "Code" = 'EBS') THEN
                        RAISE EXCEPTION 'Warehouse 1 / EBS is required for the reviewed End of Day Fill backfill.';
                    END IF;
                    IF EXISTS (SELECT 1 FROM "EndOfDayFillReportGroups" WHERE "Id" NOT IN (1, 2)) THEN
                        RAISE EXCEPTION 'Unexpected End of Day Fill groups require review before warehouse-scope migration.';
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM "EndOfDayFillReportGroups" WHERE "Id" = 1 AND "Name" = 'WP End of Day Fill' AND "Facility" = 'WP') THEN
                        RAISE EXCEPTION 'Reviewed group 1 / WP End of Day Fill was not found exactly.';
                    END IF;
                    IF NOT EXISTS (SELECT 1 FROM "EndOfDayFillReportGroups" WHERE "Id" = 2 AND "Name" = 'EBS End of Day Fill' AND "Facility" = 'EBS') THEN
                        RAISE EXCEPTION 'Reviewed group 2 / EBS End of Day Fill was not found exactly.';
                    END IF;
                    UPDATE "EndOfDayFillReportGroups" SET "WarehouseId" = 4 WHERE "Id" = 1;
                    UPDATE "EndOfDayFillReportGroups" SET "WarehouseId" = 1 WHERE "Id" = 2;
                END $warehouse_scope_backfill$;
                """));

            migrationBuilder.AlterColumn<int>(
                name: "WarehouseId",
                table: "EndOfDayFillReportGroups",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                nullable: false,
                oldClrType: typeof(int),
                oldType: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_EndOfDayFillReportGroups_WarehouseId",
                table: "EndOfDayFillReportGroups",
                column: "WarehouseId");

            migrationBuilder.AddForeignKey(
                name: "FK_EndOfDayFillReportGroups_Warehouses_WarehouseId",
                table: "EndOfDayFillReportGroups",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EndOfDayFillReportGroups_Warehouses_WarehouseId",
                table: "EndOfDayFillReportGroups");

            migrationBuilder.DropIndex(
                name: "IX_EndOfDayFillReportGroups_WarehouseId",
                table: "EndOfDayFillReportGroups");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "EndOfDayFillReportGroups");
        }
    }
}
