using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRunProjections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RunProjections",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlannedRunDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Name = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    CropYear = table.Column<int>(type: "int", nullable: false),
                    ApplePoundsPerBin = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    PearPoundsPerBin = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    StandardBoxWeightPounds = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    TotalPlannedBins = table.Column<int>(type: "int", nullable: false),
                    TotalProjectedPounds = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalProjectedBoxes = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    TotalRoundedProjectedBoxes = table.Column<int>(type: "int", nullable: false),
                    ConcurrencyVersion = table.Column<long>(type: "bigint", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    CreatedByUserId = table.Column<int>(type: "int", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    UpdatedByUserId = table.Column<int>(type: "int", nullable: true),
                    CancelledAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true),
                    CancelledByUserId = table.Column<int>(type: "int", nullable: true),
                    CancelReason = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunProjections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunProjections_Users_CancelledByUserId",
                        column: x => x.CancelledByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RunProjections_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RunProjections_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RunProjectionSources",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RunProjectionId = table.Column<long>(type: "bigint", nullable: false),
                    SourceType = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"), maxLength: 25, nullable: false),
                    InventoryKey = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(250)", "character varying(250)"), maxLength: 250, nullable: true),
                    ReceiptId = table.Column<long>(type: "bigint", nullable: true),
                    SourceInventoryAdjustmentId = table.Column<long>(type: "bigint", nullable: true),
                    WarehouseId = table.Column<int>(type: "int", nullable: true),
                    RoomId = table.Column<int>(type: "int", nullable: true),
                    CanonicalOrchardBlockId = table.Column<int>(type: "int", nullable: true),
                    FruitProfileId = table.Column<int>(type: "int", nullable: false),
                    FieldSampleId = table.Column<long>(type: "bigint", nullable: true),
                    SelectedQcSourceType = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"), maxLength: 25, nullable: false),
                    SelectedQcSampleId = table.Column<long>(type: "bigint", nullable: true),
                    PlannedBins = table.Column<int>(type: "int", nullable: false),
                    AvailableBinsSnapshot = table.Column<int>(type: "int", nullable: true),
                    AvailabilityOverrideAcknowledged = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true),
                    Commodity = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"), maxLength: 25, nullable: false),
                    PoundsPerBinUsed = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    ProjectedPounds = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ProjectedBoxes = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    RoundedProjectedBoxes = table.Column<int>(type: "int", nullable: false),
                    SourceLabelSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(500)", "character varying(500)"), maxLength: 500, nullable: false),
                    FacilitySnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: true),
                    RoomSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: true),
                    LotSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: true),
                    OrchardSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"), maxLength: 200, nullable: true),
                    GrowerSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"), maxLength: 200, nullable: true),
                    GrowerNumberSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: true),
                    BlockSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(150)", "character varying(150)"), maxLength: 150, nullable: true),
                    VarietySnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(150)", "character varying(150)"), maxLength: 150, nullable: false),
                    QcSampleDateSnapshot = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true),
                    QcFruitCountSnapshot = table.Column<int>(type: "int", nullable: true),
                    AverageWeightGramsSnapshot = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    AveragePressureLbsSnapshot = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    GradeSummarySnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true),
                    DefectSummarySnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true),
                    ProjectionWarning = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true),
                    ActualBinsRunEntryId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunProjectionSources", x => x.Id);
                    // RoomInventoryAdjustments and BinsRunEntries are provisioned by the
                    // existing startup compatibility path rather than the historical EF
                    // migration chain. Their optional logical links intentionally have no
                    // physical FK in this migration so a fresh migration chain remains valid.
                    table.ForeignKey(
                        name: "FK_RunProjectionSources_CanonicalOrchardBlocks_CanonicalOrchardBlockId",
                        column: x => x.CanonicalOrchardBlockId,
                        principalTable: "CanonicalOrchardBlocks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RunProjectionSources_FruitProfiles_FruitProfileId",
                        column: x => x.FruitProfileId,
                        principalTable: "FruitProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RunProjectionSources_QcSamples_FieldSampleId",
                        column: x => x.FieldSampleId,
                        principalTable: "QcSamples",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RunProjectionSources_QcSamples_SelectedQcSampleId",
                        column: x => x.SelectedQcSampleId,
                        principalTable: "QcSamples",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RunProjectionSources_Receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "Receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RunProjectionSources_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RunProjectionSources_RunProjections_RunProjectionId",
                        column: x => x.RunProjectionId,
                        principalTable: "RunProjections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RunProjectionSources_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RunProjectionSizeResults",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RunProjectionSourceId = table.Column<long>(type: "bigint", nullable: false),
                    Commodity = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"), maxLength: 25, nullable: false),
                    SizeCategory = table.Column<int>(type: "int", nullable: false),
                    SampleCount = table.Column<int>(type: "int", nullable: false),
                    Percentage = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    UnroundedProjectedBoxes = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    RoundedProjectedBoxes = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunProjectionSizeResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunProjectionSizeResults_RunProjectionSources_RunProjectionSourceId",
                        column: x => x.RunProjectionSourceId,
                        principalTable: "RunProjectionSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RunProjections_CancelledByUserId",
                table: "RunProjections",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RunProjections_CreatedByUserId",
                table: "RunProjections",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RunProjections_CropYear_PlannedRunDate",
                table: "RunProjections",
                columns: new[] { "CropYear", "PlannedRunDate" });

            migrationBuilder.CreateIndex(
                name: "IX_RunProjections_PlannedRunDate_Status",
                table: "RunProjections",
                columns: new[] { "PlannedRunDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_RunProjections_UpdatedByUserId",
                table: "RunProjections",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RunProjectionSizeResults_RunProjectionSourceId_Commodity_SizeCategory",
                table: "RunProjectionSizeResults",
                columns: new[] { "RunProjectionSourceId", "Commodity", "SizeCategory" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunProjectionSources_ActualBinsRunEntryId",
                table: "RunProjectionSources",
                column: "ActualBinsRunEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_RunProjectionSources_CanonicalOrchardBlockId_FruitProfileId",
                table: "RunProjectionSources",
                columns: new[] { "CanonicalOrchardBlockId", "FruitProfileId" });

            migrationBuilder.CreateIndex(
                name: "IX_RunProjectionSources_FieldSampleId",
                table: "RunProjectionSources",
                column: "FieldSampleId");

            migrationBuilder.CreateIndex(
                name: "IX_RunProjectionSources_FruitProfileId",
                table: "RunProjectionSources",
                column: "FruitProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_RunProjectionSources_InventoryKey",
                table: "RunProjectionSources",
                column: "InventoryKey");

            migrationBuilder.CreateIndex(
                name: "IX_RunProjectionSources_ReceiptId",
                table: "RunProjectionSources",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_RunProjectionSources_RoomId",
                table: "RunProjectionSources",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_RunProjectionSources_RunProjectionId_SortOrder",
                table: "RunProjectionSources",
                columns: new[] { "RunProjectionId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_RunProjectionSources_SelectedQcSampleId",
                table: "RunProjectionSources",
                column: "SelectedQcSampleId");

            migrationBuilder.CreateIndex(
                name: "IX_RunProjectionSources_SourceInventoryAdjustmentId",
                table: "RunProjectionSources",
                column: "SourceInventoryAdjustmentId");

            migrationBuilder.CreateIndex(
                name: "IX_RunProjectionSources_WarehouseId",
                table: "RunProjectionSources",
                column: "WarehouseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RunProjectionSizeResults");

            migrationBuilder.DropTable(
                name: "RunProjectionSources");

            migrationBuilder.DropTable(
                name: "RunProjections");
        }
    }
}
