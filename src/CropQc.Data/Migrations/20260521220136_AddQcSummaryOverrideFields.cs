using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQcSummaryOverrideFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsOverride",
                table: "QcSummaryEmailLogs",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "MissingItemsSnapshot",
                table: "QcSummaryEmailLogs",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OverrideReason",
                table: "QcSummaryEmailLogs",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsOverride",
                table: "QcSummaryEmailLogs");

            migrationBuilder.DropColumn(
                name: "MissingItemsSnapshot",
                table: "QcSummaryEmailLogs");

            migrationBuilder.DropColumn(
                name: "OverrideReason",
                table: "QcSummaryEmailLogs");
        }
    }
}
