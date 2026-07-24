using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRunProjectionOutcomeTrendSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FieldSampleTrendSnapshotJson",
                table: "RunProjectionSources",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"),
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FieldSampleTrendSnapshotJson",
                table: "RunProjectionSources");
        }
    }
}
