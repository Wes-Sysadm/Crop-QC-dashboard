using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDashboardConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DashboardConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(150)", "character varying(150)"), maxLength: 150, nullable: false),
                    Value = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: false),
                    Description = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(500)", "character varying(500)"), maxLength: 500, nullable: false),
                    ValueType = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardConfigurations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DashboardConfigurations_Key",
                table: "DashboardConfigurations",
                column: "Key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DashboardConfigurations");
        }
    }
}
