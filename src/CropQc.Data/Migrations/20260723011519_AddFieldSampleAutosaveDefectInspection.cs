using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFieldSampleAutosaveDefectInspection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "FieldSampleAutosaveVersion",
                table: "QcSamples",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "DefectsInspected",
                table: "QcFruitReadings",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"),
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "FieldVersion",
                table: "QcFruitReadings",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FieldSampleAutosaveVersion",
                table: "QcSamples");

            migrationBuilder.DropColumn(
                name: "DefectsInspected",
                table: "QcFruitReadings");

            migrationBuilder.DropColumn(
                name: "FieldVersion",
                table: "QcFruitReadings");
        }
    }
}
