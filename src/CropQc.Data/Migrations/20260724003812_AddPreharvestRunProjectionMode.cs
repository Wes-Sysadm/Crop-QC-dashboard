using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPreharvestRunProjectionMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ExpectedPackoutUsedDefault",
                table: "RunProjectionSources",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"),
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "SourceProjectionSourceId",
                table: "RunProjectionSources",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProjectionMode",
                table: "RunProjections",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"),
                maxLength: 25,
                nullable: false,
                defaultValue: "Inventory");

            migrationBuilder.AddColumn<long>(
                name: "SourceProjectionId",
                table: "RunProjections",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunProjectionSources_SourceProjectionSourceId",
                table: "RunProjectionSources",
                column: "SourceProjectionSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_RunProjections_SourceProjectionId",
                table: "RunProjections",
                column: "SourceProjectionId");

            migrationBuilder.AddForeignKey(
                name: "FK_RunProjections_RunProjections_SourceProjectionId",
                table: "RunProjections",
                column: "SourceProjectionId",
                principalTable: "RunProjections",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RunProjectionSources_RunProjectionSources_SourceProjectionSourceId",
                table: "RunProjectionSources",
                column: "SourceProjectionSourceId",
                principalTable: "RunProjectionSources",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RunProjections_RunProjections_SourceProjectionId",
                table: "RunProjections");

            migrationBuilder.DropForeignKey(
                name: "FK_RunProjectionSources_RunProjectionSources_SourceProjectionSourceId",
                table: "RunProjectionSources");

            migrationBuilder.DropIndex(
                name: "IX_RunProjectionSources_SourceProjectionSourceId",
                table: "RunProjectionSources");

            migrationBuilder.DropIndex(
                name: "IX_RunProjections_SourceProjectionId",
                table: "RunProjections");

            migrationBuilder.DropColumn(
                name: "ExpectedPackoutUsedDefault",
                table: "RunProjectionSources");

            migrationBuilder.DropColumn(
                name: "SourceProjectionSourceId",
                table: "RunProjectionSources");

            migrationBuilder.DropColumn(
                name: "ProjectionMode",
                table: "RunProjections");

            migrationBuilder.DropColumn(
                name: "SourceProjectionId",
                table: "RunProjections");
        }
    }
}
