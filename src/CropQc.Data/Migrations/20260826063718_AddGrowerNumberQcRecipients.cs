using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGrowerNumberQcRecipients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GrowerReportRecipients",
                columns: table => new
                {
                    Id = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CanonicalGrowerNumberId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    EmailAddress = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(320)", "character varying(320)"), maxLength: 320, nullable: false),
                    NormalizedEmailAddress = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(320)", "character varying(320)"), maxLength: 320, nullable: false),
                    IsActive = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    IsDeleted = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    CreatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    UpdatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true),
                    DeletedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GrowerReportRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GrowerReportRecipients_CanonicalGrowerNumbers_CanonicalGrowerNumberId",
                        column: x => x.CanonicalGrowerNumberId,
                        principalTable: "CanonicalGrowerNumbers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GrowerReportRecipients_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_GrowerReportRecipients_Users_DeletedByUserId",
                        column: x => x.DeletedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_GrowerReportRecipients_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GrowerReportRecipients_CanonicalGrowerNumberId_IsActive_IsDeleted",
                table: "GrowerReportRecipients",
                columns: new[] { "CanonicalGrowerNumberId", "IsActive", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_GrowerReportRecipients_CanonicalGrowerNumberId_NormalizedEmailAddress",
                table: "GrowerReportRecipients",
                columns: new[] { "CanonicalGrowerNumberId", "NormalizedEmailAddress" },
                unique: true,
                filter: MigrationProviderTypes.Sql(migrationBuilder, "[IsDeleted] = 0", "\"IsDeleted\" = FALSE"));

            migrationBuilder.CreateIndex(
                name: "IX_GrowerReportRecipients_CreatedByUserId",
                table: "GrowerReportRecipients",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GrowerReportRecipients_DeletedByUserId",
                table: "GrowerReportRecipients",
                column: "DeletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GrowerReportRecipients_UpdatedByUserId",
                table: "GrowerReportRecipients",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GrowerReportRecipients");
        }
    }
}
