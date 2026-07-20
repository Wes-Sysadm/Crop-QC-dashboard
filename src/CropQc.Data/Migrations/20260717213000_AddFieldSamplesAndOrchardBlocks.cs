using System;
using CropQc.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(CropQcDbContext))]
    [Migration("20260717213000_AddFieldSamplesAndOrchardBlocks")]
    public partial class AddFieldSamplesAndOrchardBlocks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QcSamples_Receipts_ReceiptId",
                table: "QcSamples");

            migrationBuilder.DropIndex(
                name: "IX_QcSamples_ReceiptId_SampleSequenceNumber",
                table: "QcSamples");

            migrationBuilder.AlterColumn<long>(
                name: "ReceiptId",
                table: "QcSamples",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<int>(
                name: "CanonicalOrchardBlockId",
                table: "QcSamples",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FieldSampleFruitProfileId",
                table: "QcSamples",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FieldSampleGrowerName",
                table: "QcSamples",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FieldSampleGrowerNumber",
                table: "QcSamples",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FieldSampleOriginalBlockName",
                table: "QcSamples",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FieldSampleBlockResolution",
                table: "QcSamples",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CanonicalOrchardBlocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CanonicalGrowerId = table.Column<int>(type: "int", nullable: true),
                    OrchardName = table.Column<string>(maxLength: 200, nullable: false),
                    CanonicalBlockName = table.Column<string>(maxLength: 150, nullable: false),
                    NormalizedOrchardKey = table.Column<string>(maxLength: 200, nullable: false),
                    NormalizedBlockKey = table.Column<string>(maxLength: 150, nullable: false),
                    IsActive = table.Column<bool>(nullable: false),
                    Notes = table.Column<string>(maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CanonicalOrchardBlocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CanonicalOrchardBlocks_CanonicalGrowers_CanonicalGrowerId",
                        column: x => x.CanonicalGrowerId,
                        principalTable: "CanonicalGrowers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OrchardBlockAliases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", Npgsql.EntityFrameworkCore.PostgreSQL.Metadata.NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CanonicalOrchardBlockId = table.Column<int>(type: "int", nullable: false),
                    AliasName = table.Column<string>(maxLength: 150, nullable: false),
                    NormalizedAliasKey = table.Column<string>(maxLength: 150, nullable: false),
                    IsActive = table.Column<bool>(nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrchardBlockAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrchardBlockAliases_CanonicalOrchardBlocks_CanonicalOrchardBlockId",
                        column: x => x.CanonicalOrchardBlockId,
                        principalTable: "CanonicalOrchardBlocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            if (ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                migrationBuilder.Sql("""
                    INSERT INTO "SampleTypes" ("Id", "Name", "IsActive")
                    VALUES (5, 'Field Sample', TRUE)
                    ON CONFLICT ("Id") DO NOTHING;
                    """);
            }
            else
            {
                migrationBuilder.Sql("""
                    IF NOT EXISTS (SELECT 1 FROM [SampleTypes] WHERE [Id] = 5)
                    BEGIN
                        SET IDENTITY_INSERT [SampleTypes] ON;
                        INSERT INTO [SampleTypes] ([Id], [Name], [IsActive]) VALUES (5, N'Field Sample', 1);
                        SET IDENTITY_INSERT [SampleTypes] OFF;
                    END
                    """);
            }

            migrationBuilder.CreateIndex(
                name: "IX_QcSamples_CanonicalOrchardBlockId_SampleTypeId_SampleTakenAt",
                table: "QcSamples",
                columns: new[] { "CanonicalOrchardBlockId", "SampleTypeId", "SampleTakenAt" });

            migrationBuilder.CreateIndex(
                name: "IX_QcSamples_FieldSampleFruitProfileId",
                table: "QcSamples",
                column: "FieldSampleFruitProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_QcSamples_ReceiptId_SampleSequenceNumber",
                table: "QcSamples",
                columns: new[] { "ReceiptId", "SampleSequenceNumber" },
                unique: true,
                filter: ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
                    ? "\"ReceiptId\" IS NOT NULL"
                    : "[ReceiptId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalOrchardBlocks_CanonicalGrowerId",
                table: "CanonicalOrchardBlocks",
                column: "CanonicalGrowerId");

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalOrchardBlocks_NormalizedOrchardKey_NormalizedBlockKey",
                table: "CanonicalOrchardBlocks",
                columns: new[] { "NormalizedOrchardKey", "NormalizedBlockKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrchardBlockAliases_CanonicalOrchardBlockId_NormalizedAliasKey",
                table: "OrchardBlockAliases",
                columns: new[] { "CanonicalOrchardBlockId", "NormalizedAliasKey" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_QcSamples_CanonicalOrchardBlocks_CanonicalOrchardBlockId",
                table: "QcSamples",
                column: "CanonicalOrchardBlockId",
                principalTable: "CanonicalOrchardBlocks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_QcSamples_FruitProfiles_FieldSampleFruitProfileId",
                table: "QcSamples",
                column: "FieldSampleFruitProfileId",
                principalTable: "FruitProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_QcSamples_Receipts_ReceiptId",
                table: "QcSamples",
                column: "ReceiptId",
                principalTable: "Receipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QcSamples_CanonicalOrchardBlocks_CanonicalOrchardBlockId",
                table: "QcSamples");

            migrationBuilder.DropForeignKey(
                name: "FK_QcSamples_FruitProfiles_FieldSampleFruitProfileId",
                table: "QcSamples");

            migrationBuilder.DropForeignKey(
                name: "FK_QcSamples_Receipts_ReceiptId",
                table: "QcSamples");

            migrationBuilder.DropTable(name: "OrchardBlockAliases");

            migrationBuilder.DropTable(name: "CanonicalOrchardBlocks");

            migrationBuilder.DropIndex(
                name: "IX_QcSamples_CanonicalOrchardBlockId_SampleTypeId_SampleTakenAt",
                table: "QcSamples");

            migrationBuilder.DropIndex(
                name: "IX_QcSamples_FieldSampleFruitProfileId",
                table: "QcSamples");

            migrationBuilder.DropIndex(
                name: "IX_QcSamples_ReceiptId_SampleSequenceNumber",
                table: "QcSamples");

            if (ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                migrationBuilder.Sql("""DELETE FROM "SampleTypes" WHERE "Id" = 5;""");
            }
            else
            {
                migrationBuilder.Sql("""DELETE FROM [SampleTypes] WHERE [Id] = 5;""");
            }

            migrationBuilder.DropColumn(name: "CanonicalOrchardBlockId", table: "QcSamples");
            migrationBuilder.DropColumn(name: "FieldSampleFruitProfileId", table: "QcSamples");
            migrationBuilder.DropColumn(name: "FieldSampleGrowerName", table: "QcSamples");
            migrationBuilder.DropColumn(name: "FieldSampleGrowerNumber", table: "QcSamples");
            migrationBuilder.DropColumn(name: "FieldSampleOriginalBlockName", table: "QcSamples");
            migrationBuilder.DropColumn(name: "FieldSampleBlockResolution", table: "QcSamples");

            migrationBuilder.AlterColumn<long>(
                name: "ReceiptId",
                table: "QcSamples",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_QcSamples_ReceiptId_SampleSequenceNumber",
                table: "QcSamples",
                columns: new[] { "ReceiptId", "SampleSequenceNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_QcSamples_Receipts_ReceiptId",
                table: "QcSamples",
                column: "ReceiptId",
                principalTable: "Receipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
