using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRunProjectionCullSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CullCalculationVersion",
                table: "RunProjections",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"),
                maxLength: 25,
                nullable: false,
                defaultValue: "1.0");

            migrationBuilder.AddColumn<decimal>(
                name: "JuiceCullShare",
                table: "RunProjections",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(5,4)", "numeric(5,4)"),
                precision: 5,
                scale: 4,
                nullable: false,
                defaultValue: 0.40m);

            migrationBuilder.AddColumn<decimal>(
                name: "PeelerCullShare",
                table: "RunProjections",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(5,4)", "numeric(5,4)"),
                precision: 5,
                scale: 4,
                nullable: false,
                defaultValue: 0.35m);

            migrationBuilder.AddColumn<decimal>(
                name: "WasteCullShare",
                table: "RunProjections",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(5,4)", "numeric(5,4)"),
                precision: 5,
                scale: 4,
                nullable: false,
                defaultValue: 0.25m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CullCalculationVersion",
                table: "RunProjections");

            migrationBuilder.DropColumn(
                name: "JuiceCullShare",
                table: "RunProjections");

            migrationBuilder.DropColumn(
                name: "PeelerCullShare",
                table: "RunProjections");

            migrationBuilder.DropColumn(
                name: "WasteCullShare",
                table: "RunProjections");
        }
    }
}
