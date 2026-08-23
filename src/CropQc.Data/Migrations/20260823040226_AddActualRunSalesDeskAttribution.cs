using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddActualRunSalesDeskAttribution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SalesDeskId",
                table: "ActualRuns",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SalesDeskNameSnapshot",
                table: "ActualRuns",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"),
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SalesDeskId",
                table: "ActualRunOverrideRequests",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SalesDeskNameSnapshot",
                table: "ActualRunOverrideRequests",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"),
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SalesDesks",
                columns: table => new
                {
                    Id = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    DisplayOrder = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    CreatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    UpdatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SalesDesks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SalesDesks_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_SalesDesks_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ActualRunSalesDeskCorrections",
                columns: table => new
                {
                    Id = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ActualRunId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false),
                    OperationKey = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(64)", "character varying(64)"), maxLength: 64, nullable: false),
                    ExpectedConcurrencyVersion = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false),
                    PreviousSalesDeskId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    PreviousSalesDeskNameSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: true),
                    NewSalesDeskId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    NewSalesDeskNameSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: false),
                    Reason = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: false),
                    CorrectedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    CorrectedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActualRunSalesDeskCorrections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActualRunSalesDeskCorrections_ActualRuns_ActualRunId",
                        column: x => x.ActualRunId,
                        principalTable: "ActualRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActualRunSalesDeskCorrections_SalesDesks_NewSalesDeskId",
                        column: x => x.NewSalesDeskId,
                        principalTable: "SalesDesks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActualRunSalesDeskCorrections_SalesDesks_PreviousSalesDeskId",
                        column: x => x.PreviousSalesDeskId,
                        principalTable: "SalesDesks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActualRunSalesDeskCorrections_Users_CorrectedByUserId",
                        column: x => x.CorrectedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            if (ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase))
            {
                migrationBuilder.Sql(
                    """
                    INSERT INTO "SalesDesks" ("Id", "CreatedAt", "CreatedByUserId", "DisplayOrder", "IsActive", "Name", "UpdatedAt", "UpdatedByUserId")
                    OVERRIDING SYSTEM VALUE
                    VALUES
                        (1, TIMESTAMPTZ '2026-05-21 00:00:00+00', NULL, 10, TRUE, 'Domex', TIMESTAMPTZ '2026-05-21 00:00:00+00', NULL),
                        (2, TIMESTAMPTZ '2026-05-21 00:00:00+00', NULL, 20, TRUE, 'Honey Bear', TIMESTAMPTZ '2026-05-21 00:00:00+00', NULL),
                        (3, TIMESTAMPTZ '2026-05-21 00:00:00+00', NULL, 30, TRUE, 'Viva Tierra', TIMESTAMPTZ '2026-05-21 00:00:00+00', NULL);
                    SELECT setval(pg_get_serial_sequence('"SalesDesks"', 'Id'), 3, true);
                    """);
            }
            else
            {
                migrationBuilder.InsertData(
                    table: "SalesDesks",
                    columns: new[] { "Id", "CreatedAt", "CreatedByUserId", "DisplayOrder", "IsActive", "Name", "UpdatedAt", "UpdatedByUserId" },
                    values: new object[,]
                    {
                        { 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 10, true, "Domex", new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                        { 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 20, true, "Honey Bear", new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                        { 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 30, true, "Viva Tierra", new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null }
                    });
            }

            migrationBuilder.CreateIndex(
                name: "IX_ActualRuns_SalesDeskId_Status_RunAt",
                table: "ActualRuns",
                columns: new[] { "SalesDeskId", "Status", "RunAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ActualRunOverrideRequests_SalesDeskId",
                table: "ActualRunOverrideRequests",
                column: "SalesDeskId");

            migrationBuilder.CreateIndex(
                name: "IX_ActualRunSalesDeskCorrections_ActualRunId_CorrectedAt",
                table: "ActualRunSalesDeskCorrections",
                columns: new[] { "ActualRunId", "CorrectedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ActualRunSalesDeskCorrections_CorrectedByUserId",
                table: "ActualRunSalesDeskCorrections",
                column: "CorrectedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ActualRunSalesDeskCorrections_NewSalesDeskId",
                table: "ActualRunSalesDeskCorrections",
                column: "NewSalesDeskId");

            migrationBuilder.CreateIndex(
                name: "IX_ActualRunSalesDeskCorrections_OperationKey",
                table: "ActualRunSalesDeskCorrections",
                column: "OperationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActualRunSalesDeskCorrections_PreviousSalesDeskId",
                table: "ActualRunSalesDeskCorrections",
                column: "PreviousSalesDeskId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesDesks_CreatedByUserId",
                table: "SalesDesks",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_SalesDesks_IsActive_DisplayOrder_Name",
                table: "SalesDesks",
                columns: new[] { "IsActive", "DisplayOrder", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_SalesDesks_Name",
                table: "SalesDesks",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SalesDesks_UpdatedByUserId",
                table: "SalesDesks",
                column: "UpdatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_ActualRunOverrideRequests_SalesDesks_SalesDeskId",
                table: "ActualRunOverrideRequests",
                column: "SalesDeskId",
                principalTable: "SalesDesks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ActualRuns_SalesDesks_SalesDeskId",
                table: "ActualRuns",
                column: "SalesDeskId",
                principalTable: "SalesDesks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ActualRunOverrideRequests_SalesDesks_SalesDeskId",
                table: "ActualRunOverrideRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_ActualRuns_SalesDesks_SalesDeskId",
                table: "ActualRuns");

            migrationBuilder.DropTable(
                name: "ActualRunSalesDeskCorrections");

            migrationBuilder.DropTable(
                name: "SalesDesks");

            migrationBuilder.DropIndex(
                name: "IX_ActualRuns_SalesDeskId_Status_RunAt",
                table: "ActualRuns");

            migrationBuilder.DropIndex(
                name: "IX_ActualRunOverrideRequests_SalesDeskId",
                table: "ActualRunOverrideRequests");

            migrationBuilder.DropColumn(
                name: "SalesDeskId",
                table: "ActualRuns");

            migrationBuilder.DropColumn(
                name: "SalesDeskNameSnapshot",
                table: "ActualRuns");

            migrationBuilder.DropColumn(
                name: "SalesDeskId",
                table: "ActualRunOverrideRequests");

            migrationBuilder.DropColumn(
                name: "SalesDeskNameSnapshot",
                table: "ActualRunOverrideRequests");
        }
    }
}
