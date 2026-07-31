using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeparatePlanningProjectionsFromActualRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "RunProjectionId",
                table: "PackoutRuns",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<long>(
                name: "ActualRunId",
                table: "PackoutRuns",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RunExpectationId",
                table: "PackoutRuns",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RunExpectations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ActualRunId = table.Column<long>(type: "bigint", nullable: false),
                    ActualRunRevisionId = table.Column<long>(type: "bigint", nullable: false),
                    RevisionNumber = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    FacilityWarehouseId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    FacilitySnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    RunAtSnapshot = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    TotalBins = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    GrossPounds = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,4)", "numeric(18,4)"), precision: 18, scale: 4, nullable: false),
                    ExpectedPackoutPercent = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(8,4)", "numeric(8,4)"), precision: 8, scale: 4, nullable: false),
                    ExpectedPackedPounds = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,4)", "numeric(18,4)"), precision: 18, scale: 4, nullable: false),
                    ExpectedPackedBoxes = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,4)", "numeric(18,4)"), precision: 18, scale: 4, nullable: false),
                    ExpectedWholeBoxes = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    ExpectedCullPounds = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,4)", "numeric(18,4)"), precision: 18, scale: 4, nullable: false),
                    ExpectedJuicePounds = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,4)", "numeric(18,4)"), precision: 18, scale: 4, nullable: false),
                    ExpectedPeelerPounds = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,4)", "numeric(18,4)"), precision: 18, scale: 4, nullable: false),
                    ExpectedWastePounds = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,4)", "numeric(18,4)"), precision: 18, scale: 4, nullable: false),
                    ConfidencePercent = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(8,4)", "numeric(8,4)"), precision: 8, scale: 4, nullable: false),
                    SizeDistributionSnapshotJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), nullable: false),
                    GradeDistributionSnapshotJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), nullable: false),
                    ConfigurationSnapshotJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), nullable: false),
                    CalculationVersion = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(75)", "character varying(75)"), maxLength: 75, nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    CreatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunExpectations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunExpectations_ActualRunRevisions_ActualRunRevisionId",
                        column: x => x.ActualRunRevisionId,
                        principalTable: "ActualRunRevisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RunExpectations_ActualRuns_ActualRunId",
                        column: x => x.ActualRunId,
                        principalTable: "ActualRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RunExpectations_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RunExpectationSources",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RunExpectationId = table.Column<long>(type: "bigint", nullable: false),
                    BinsRunEntryId = table.Column<long>(type: "bigint", nullable: false),
                    WarehouseId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    RoomId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    FacilitySnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    RoomSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: false),
                    CropYearSnapshot = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    GrowerLotId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    FruitProfileId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    GrowerSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"), maxLength: 200, nullable: false),
                    LotSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: false),
                    VarietySnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: false),
                    ProductionTypeSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    IsOrganicSnapshot = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    BinsContributed = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    ContributionPercent = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(9,6)", "numeric(9,6)"), precision: 9, scale: 6, nullable: false),
                    QcSampleId = table.Column<long>(type: "bigint", nullable: true),
                    QcSampleTakenAtSnapshot = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true),
                    QcFruitCountSnapshot = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    QcMeasurementSnapshotJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), nullable: false),
                    SizeDistributionSnapshotJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), nullable: false),
                    GradeDistributionSnapshotJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), nullable: false),
                    GrossPounds = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,4)", "numeric(18,4)"), precision: 18, scale: 4, nullable: false),
                    ExpectedPackedPounds = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,4)", "numeric(18,4)"), precision: 18, scale: 4, nullable: false),
                    ExpectedWholeBoxes = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    ExpectedCullPounds = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,4)", "numeric(18,4)"), precision: 18, scale: 4, nullable: false),
                    ConfidencePercent = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(8,4)", "numeric(8,4)"), precision: 8, scale: 4, nullable: false),
                    WarningSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunExpectationSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunExpectationSources_BinsRunEntries_BinsRunEntryId",
                        column: x => x.BinsRunEntryId,
                        principalTable: "BinsRunEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RunExpectationSources_QcSamples_QcSampleId",
                        column: x => x.QcSampleId,
                        principalTable: "QcSamples",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RunExpectationSources_RunExpectations_RunExpectationId",
                        column: x => x.RunExpectationId,
                        principalTable: "RunExpectations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PackoutSourceAllocations",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PackoutRunId = table.Column<long>(type: "bigint", nullable: false),
                    RunExpectationSourceId = table.Column<long>(type: "bigint", nullable: false),
                    BinsContributed = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    ContributionPercent = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(9,6)", "numeric(9,6)"), precision: 9, scale: 6, nullable: false),
                    AllocatedPackedPounds = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,6)", "numeric(18,6)"), precision: 18, scale: 6, nullable: false),
                    AllocatedWholeBoxes = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    AllocatedResidualPounds = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,6)", "numeric(18,6)"), precision: 18, scale: 6, nullable: false),
                    AllocatedJuicePounds = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,6)", "numeric(18,6)"), precision: 18, scale: 6, nullable: false),
                    AllocatedPeelerPounds = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,6)", "numeric(18,6)"), precision: 18, scale: 6, nullable: false),
                    AllocatedWastePounds = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,6)", "numeric(18,6)"), precision: 18, scale: 6, nullable: false),
                    PackCodeAllocationJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), nullable: false),
                    SizeAllocationJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), nullable: false),
                    GradeAllocationJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), nullable: false),
                    AllocationVersion = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(75)", "character varying(75)"), maxLength: 75, nullable: false),
                    CalculatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackoutSourceAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackoutSourceAllocations_PackoutRuns_PackoutRunId",
                        column: x => x.PackoutRunId,
                        principalTable: "PackoutRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PackoutSourceAllocations_RunExpectationSources_RunExpectationSourceId",
                        column: x => x.RunExpectationSourceId,
                        principalTable: "RunExpectationSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PackoutRuns_RunExpectationId",
                table: "PackoutRuns",
                column: "RunExpectationId");

            migrationBuilder.CreateIndex(
                name: "UX_PackoutRuns_ActualRunId",
                table: "PackoutRuns",
                column: "ActualRunId",
                unique: true,
                filter: MigrationProviderTypes.Sql(
                    migrationBuilder,
                    "[ActualRunId] IS NOT NULL",
                    "\"ActualRunId\" IS NOT NULL"));

            migrationBuilder.CreateIndex(
                name: "IX_PackoutSourceAllocations_PackoutRunId_RunExpectationSourceId",
                table: "PackoutSourceAllocations",
                columns: new[] { "PackoutRunId", "RunExpectationSourceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackoutSourceAllocations_RunExpectationSourceId",
                table: "PackoutSourceAllocations",
                column: "RunExpectationSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_RunExpectations_ActualRunId_RevisionNumber",
                table: "RunExpectations",
                columns: new[] { "ActualRunId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunExpectations_ActualRunRevisionId",
                table: "RunExpectations",
                column: "ActualRunRevisionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunExpectations_CreatedByUserId",
                table: "RunExpectations",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RunExpectationSources_BinsRunEntryId",
                table: "RunExpectationSources",
                column: "BinsRunEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_RunExpectationSources_QcSampleId",
                table: "RunExpectationSources",
                column: "QcSampleId");

            migrationBuilder.CreateIndex(
                name: "IX_RunExpectationSources_RunExpectationId_BinsRunEntryId",
                table: "RunExpectationSources",
                columns: new[] { "RunExpectationId", "BinsRunEntryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunExpectationSources_WarehouseId_RoomId_CropYearSnapshot_LotSnapshot_VarietySnapshot",
                table: "RunExpectationSources",
                columns: new[] { "WarehouseId", "RoomId", "CropYearSnapshot", "LotSnapshot", "VarietySnapshot" });

            migrationBuilder.AddForeignKey(
                name: "FK_PackoutRuns_ActualRuns_ActualRunId",
                table: "PackoutRuns",
                column: "ActualRunId",
                principalTable: "ActualRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PackoutRuns_RunExpectations_RunExpectationId",
                table: "PackoutRuns",
                column: "RunExpectationId",
                principalTable: "RunExpectations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PackoutRuns_ActualRuns_ActualRunId",
                table: "PackoutRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_PackoutRuns_RunExpectations_RunExpectationId",
                table: "PackoutRuns");

            migrationBuilder.DropTable(
                name: "PackoutSourceAllocations");

            migrationBuilder.DropTable(
                name: "RunExpectationSources");

            migrationBuilder.DropTable(
                name: "RunExpectations");

            migrationBuilder.DropIndex(
                name: "IX_PackoutRuns_RunExpectationId",
                table: "PackoutRuns");

            migrationBuilder.DropIndex(
                name: "UX_PackoutRuns_ActualRunId",
                table: "PackoutRuns");

            migrationBuilder.DropColumn(
                name: "ActualRunId",
                table: "PackoutRuns");

            migrationBuilder.DropColumn(
                name: "RunExpectationId",
                table: "PackoutRuns");

            migrationBuilder.AlterColumn<long>(
                name: "RunProjectionId",
                table: "PackoutRuns",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);
        }
    }
}
