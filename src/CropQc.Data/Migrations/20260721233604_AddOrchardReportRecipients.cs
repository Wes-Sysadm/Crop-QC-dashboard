using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CropQc.Data.Migrations
{
    public partial class AddOrchardReportRecipients : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CanonicalOrchardBlockId",
                table: "Receipts",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CanonicalOrchardId",
                table: "CanonicalOrchardBlocks",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ToAddress",
                table: "QcSummaryEmailLogs",
                maxLength: 2000,
                nullable: false,
                oldClrType: typeof(string),
                oldMaxLength: 320);

            migrationBuilder.CreateTable(
                name: "CanonicalOrchards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrchardName = table.Column<string>(maxLength: 200, nullable: false),
                    NormalizedOrchardKey = table.Column<string>(maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(nullable: false)
                },
                constraints: table => table.PrimaryKey("PK_CanonicalOrchards", x => x.Id));

            if (ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                migrationBuilder.Sql("""
                    INSERT INTO "CanonicalOrchards" ("OrchardName", "NormalizedOrchardKey", "IsActive", "CreatedAt", "UpdatedAt")
                    SELECT MIN("OrchardName"), "NormalizedOrchardKey", BOOL_OR("IsActive"), MIN("CreatedAt"), MAX("UpdatedAt")
                    FROM "CanonicalOrchardBlocks"
                    GROUP BY "NormalizedOrchardKey";

                    UPDATE "CanonicalOrchardBlocks" AS block
                    SET "CanonicalOrchardId" = orchard."Id"
                    FROM "CanonicalOrchards" AS orchard
                    WHERE orchard."NormalizedOrchardKey" = block."NormalizedOrchardKey";
                    """);
            }
            else
            {
                migrationBuilder.Sql("""
                    INSERT INTO [CanonicalOrchards] ([OrchardName], [NormalizedOrchardKey], [IsActive], [CreatedAt], [UpdatedAt])
                    SELECT MIN([OrchardName]), [NormalizedOrchardKey], CONVERT(bit, MAX(CONVERT(int, [IsActive]))), MIN([CreatedAt]), MAX([UpdatedAt])
                    FROM [CanonicalOrchardBlocks]
                    GROUP BY [NormalizedOrchardKey];

                    UPDATE block
                    SET [CanonicalOrchardId] = orchard.[Id]
                    FROM [CanonicalOrchardBlocks] AS block
                    INNER JOIN [CanonicalOrchards] AS orchard ON orchard.[NormalizedOrchardKey] = block.[NormalizedOrchardKey];
                    """);
            }

            migrationBuilder.AlterColumn<int>(
                name: "CanonicalOrchardId",
                table: "CanonicalOrchardBlocks",
                type: "int",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "OrchardReportRecipients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CanonicalOrchardId = table.Column<int>(type: "int", nullable: false),
                    EmailAddress = table.Column<string>(maxLength: 320, nullable: false),
                    NormalizedEmailAddress = table.Column<string>(maxLength: 320, nullable: false),
                    IsActive = table.Column<bool>(nullable: false),
                    IsDeleted = table.Column<bool>(nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "int", nullable: true),
                    DeletedAt = table.Column<DateTimeOffset>(nullable: true),
                    DeletedByUserId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrchardReportRecipients", x => x.Id);
                    table.ForeignKey("FK_OrchardReportRecipients_CanonicalOrchards_CanonicalOrchardId", x => x.CanonicalOrchardId, "CanonicalOrchards", "Id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey("FK_OrchardReportRecipients_Users_CreatedByUserId", x => x.CreatedByUserId, "Users", "Id", onDelete: ReferentialAction.SetNull);
                    table.ForeignKey("FK_OrchardReportRecipients_Users_DeletedByUserId", x => x.DeletedByUserId, "Users", "Id", onDelete: ReferentialAction.SetNull);
                    table.ForeignKey("FK_OrchardReportRecipients_Users_UpdatedByUserId", x => x.UpdatedByUserId, "Users", "Id", onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex("IX_Receipts_CanonicalOrchardBlockId", "Receipts", "CanonicalOrchardBlockId");
            migrationBuilder.CreateIndex("IX_CanonicalOrchardBlocks_CanonicalOrchardId", "CanonicalOrchardBlocks", "CanonicalOrchardId");
            migrationBuilder.CreateIndex("IX_CanonicalOrchards_NormalizedOrchardKey", "CanonicalOrchards", "NormalizedOrchardKey", unique: true);
            migrationBuilder.CreateIndex("IX_OrchardReportRecipients_CanonicalOrchardId_IsActive_IsDeleted", "OrchardReportRecipients", new[] { "CanonicalOrchardId", "IsActive", "IsDeleted" });
            migrationBuilder.CreateIndex(
                "IX_OrchardReportRecipients_CanonicalOrchardId_NormalizedEmailAddress",
                "OrchardReportRecipients",
                new[] { "CanonicalOrchardId", "NormalizedEmailAddress" },
                unique: true,
                filter: ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) ? "\"IsDeleted\" = FALSE" : "[IsDeleted] = 0");
            migrationBuilder.CreateIndex("IX_OrchardReportRecipients_CreatedByUserId", "OrchardReportRecipients", "CreatedByUserId");
            migrationBuilder.CreateIndex("IX_OrchardReportRecipients_DeletedByUserId", "OrchardReportRecipients", "DeletedByUserId");
            migrationBuilder.CreateIndex("IX_OrchardReportRecipients_UpdatedByUserId", "OrchardReportRecipients", "UpdatedByUserId");

            migrationBuilder.AddForeignKey("FK_CanonicalOrchardBlocks_CanonicalOrchards_CanonicalOrchardId", "CanonicalOrchardBlocks", "CanonicalOrchardId", "CanonicalOrchards", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey("FK_Receipts_CanonicalOrchardBlocks_CanonicalOrchardBlockId", "Receipts", "CanonicalOrchardBlockId", "CanonicalOrchardBlocks", principalColumn: "Id", onDelete: ReferentialAction.SetNull);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey("FK_CanonicalOrchardBlocks_CanonicalOrchards_CanonicalOrchardId", "CanonicalOrchardBlocks");
            migrationBuilder.DropForeignKey("FK_Receipts_CanonicalOrchardBlocks_CanonicalOrchardBlockId", "Receipts");
            migrationBuilder.DropTable("OrchardReportRecipients");
            migrationBuilder.DropTable("CanonicalOrchards");
            migrationBuilder.DropIndex("IX_Receipts_CanonicalOrchardBlockId", "Receipts");
            migrationBuilder.DropIndex("IX_CanonicalOrchardBlocks_CanonicalOrchardId", "CanonicalOrchardBlocks");
            migrationBuilder.DropColumn("CanonicalOrchardBlockId", "Receipts");
            migrationBuilder.DropColumn("CanonicalOrchardId", "CanonicalOrchardBlocks");
            migrationBuilder.AlterColumn<string>(
                name: "ToAddress",
                table: "QcSummaryEmailLogs",
                maxLength: 320,
                nullable: false,
                oldClrType: typeof(string),
                oldMaxLength: 2000);
        }
    }
}
