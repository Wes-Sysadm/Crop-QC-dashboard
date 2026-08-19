using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRoomTreatmentTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TreatmentSignatureSnapshot",
                table: "BinsRunEntries",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"),
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TreatmentStateSnapshot",
                table: "BinsRunEntries",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"),
                maxLength: 25,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TreatmentSummarySnapshot",
                table: "BinsRunEntries",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(2000)", "character varying(2000)"),
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TreatmentSignature",
                table: "ActualRunOverrideRequestLines",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"),
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TreatmentChemicals",
                columns: table => new
                {
                    Id = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductName = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"), maxLength: 200, nullable: false),
                    CommonName = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"), maxLength: 200, nullable: true),
                    Crop = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"), maxLength: 25, nullable: false),
                    Volume = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(12,2)", "numeric(12,2)"), precision: 12, scale: 2, nullable: false),
                    Unit = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"), maxLength: 25, nullable: false),
                    UnitPrice = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(12,2)", "numeric(12,2)"), precision: 12, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(3)", "character varying(3)"), maxLength: 3, nullable: false),
                    IsActive = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    CreatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    UpdatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreatmentChemicals", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TreatmentChemicals_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TreatmentChemicals_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TreatmentLineageSegments",
                columns: table => new
                {
                    Id = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    WarehouseId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    RoomId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    CropYear = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    GrowerLotId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    FruitProfileId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    IdentityKey = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(500)", "character varying(500)"), maxLength: 500, nullable: false),
                    GrowerNumberSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: true),
                    GrowerNameSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"), maxLength: 200, nullable: false),
                    LotNumberSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: false),
                    VarietyCodeSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    ProductionTypeSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    IsOrganicSnapshot = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: true),
                    InventoryStatusSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: true),
                    TreatmentState = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"), maxLength: 25, nullable: false),
                    TreatmentSignature = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: false),
                    CurrentBins = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    ConcurrencyVersion = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreatmentLineageSegments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TreatmentLineageSegments_FruitProfiles_FruitProfileId",
                        column: x => x.FruitProfileId,
                        principalTable: "FruitProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TreatmentLineageSegments_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TreatmentLineageSegments_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RoomTreatmentApplications",
                columns: table => new
                {
                    Id = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OperationKey = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: false),
                    TreatmentChemicalId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    WarehouseId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    RoomId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    AppliedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    AppliedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    Notes = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true),
                    TotalBinsSnapshot = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    ProductNameSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"), maxLength: 200, nullable: false),
                    CommonNameSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"), maxLength: 200, nullable: true),
                    CropSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"), maxLength: 25, nullable: false),
                    VolumeSnapshot = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(12,2)", "numeric(12,2)"), precision: 12, scale: 2, nullable: false),
                    UnitSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"), maxLength: 25, nullable: false),
                    UnitPriceSnapshot = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(12,2)", "numeric(12,2)"), precision: 12, scale: 2, nullable: false),
                    CurrencySnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(3)", "character varying(3)"), maxLength: 3, nullable: false),
                    EstimatedCostSnapshot = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(14,2)", "numeric(14,2)"), precision: 14, scale: 2, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    CreatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    ReversedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true),
                    ReversedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    ReversalReason = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomTreatmentApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoomTreatmentApplications_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoomTreatmentApplications_TreatmentChemicals_TreatmentChemicalId",
                        column: x => x.TreatmentChemicalId,
                        principalTable: "TreatmentChemicals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoomTreatmentApplications_Users_AppliedByUserId",
                        column: x => x.AppliedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoomTreatmentApplications_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoomTreatmentApplications_Users_ReversedByUserId",
                        column: x => x.ReversedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RoomTreatmentApplications_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "TreatmentLineageMovements",
                columns: table => new
                {
                    Id = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OperationKey = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"), maxLength: 200, nullable: false),
                    MovementType = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    SourceSegmentId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: true),
                    DestinationSegmentId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: true),
                    SourceRoomId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    DestinationRoomId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    IdentityKey = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(500)", "character varying(500)"), maxLength: 500, nullable: false),
                    TreatmentStateSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"), maxLength: 25, nullable: false),
                    TreatmentSignatureSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: false),
                    BinCount = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    RoomTransferId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: true),
                    RoomInventoryLossId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: true),
                    BinsRunEntryId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: true),
                    ReversesTreatmentLineageMovementId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    CreatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreatmentLineageMovements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TreatmentLineageMovements_BinsRunEntries_BinsRunEntryId",
                        column: x => x.BinsRunEntryId,
                        principalTable: "BinsRunEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TreatmentLineageMovements_RoomInventoryLosses_RoomInventoryLossId",
                        column: x => x.RoomInventoryLossId,
                        principalTable: "RoomInventoryLosses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TreatmentLineageMovements_RoomTransfers_RoomTransferId",
                        column: x => x.RoomTransferId,
                        principalTable: "RoomTransfers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TreatmentLineageMovements_Rooms_DestinationRoomId",
                        column: x => x.DestinationRoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TreatmentLineageMovements_Rooms_SourceRoomId",
                        column: x => x.SourceRoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TreatmentLineageMovements_TreatmentLineageMovements_ReversesTreatmentLineageMovementId",
                        column: x => x.ReversesTreatmentLineageMovementId,
                        principalTable: "TreatmentLineageMovements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TreatmentLineageMovements_TreatmentLineageSegments_DestinationSegmentId",
                        column: x => x.DestinationSegmentId,
                        principalTable: "TreatmentLineageSegments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TreatmentLineageMovements_TreatmentLineageSegments_SourceSegmentId",
                        column: x => x.SourceSegmentId,
                        principalTable: "TreatmentLineageSegments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TreatmentLineageMovements_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "RoomTreatmentApplicationSources",
                columns: table => new
                {
                    Id = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoomTreatmentApplicationId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false),
                    CropYear = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    GrowerLotId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    FruitProfileId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    IdentityKey = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(500)", "character varying(500)"), maxLength: 500, nullable: false),
                    GrowerNumberSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: true),
                    GrowerNameSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"), maxLength: 200, nullable: false),
                    LotNumberSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: false),
                    VarietyCodeSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    ProductionTypeSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    IsOrganicSnapshot = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: true),
                    InventoryStatusSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: true),
                    BinsTreated = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    PriorTreatmentSignature = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: false),
                    ResultTreatmentSignature = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomTreatmentApplicationSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoomTreatmentApplicationSources_FruitProfiles_FruitProfileId",
                        column: x => x.FruitProfileId,
                        principalTable: "FruitProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_RoomTreatmentApplicationSources_RoomTreatmentApplications_RoomTreatmentApplicationId",
                        column: x => x.RoomTreatmentApplicationId,
                        principalTable: "RoomTreatmentApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TreatmentLineageSegmentApplications",
                columns: table => new
                {
                    TreatmentLineageSegmentId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false),
                    RoomTreatmentApplicationId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false),
                    Sequence = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TreatmentLineageSegmentApplications", x => new { x.TreatmentLineageSegmentId, x.RoomTreatmentApplicationId });
                    table.ForeignKey(
                        name: "FK_TreatmentLineageSegmentApplications_RoomTreatmentApplications_RoomTreatmentApplicationId",
                        column: x => x.RoomTreatmentApplicationId,
                        principalTable: "RoomTreatmentApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TreatmentLineageSegmentApplications_TreatmentLineageSegments_TreatmentLineageSegmentId",
                        column: x => x.TreatmentLineageSegmentId,
                        principalTable: "TreatmentLineageSegments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "TreatmentChemicals",
                columns: new[] { "Id", "CommonName", "CreatedAt", "CreatedByUserId", "Crop", "Currency", "IsActive", "ProductName", "Unit", "UnitPrice", "UpdatedAt", "UpdatedByUserId", "Volume" },
                columnTypes: new[]
                {
                    MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                    MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"),
                    MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"),
                    MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                    MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"),
                    MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(3)", "character varying(3)"),
                    MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"),
                    MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"),
                    MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"),
                    MigrationProviderTypes.StoreType(migrationBuilder, "decimal(12,2)", "numeric(12,2)"),
                    MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"),
                    MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                    MigrationProviderTypes.StoreType(migrationBuilder, "decimal(12,2)", "numeric(12,2)")
                },
                values: new object[,]
                {
                    { 1, null, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Apples", "USD", true, "eFOG-160 PYR FOGGING", "BIN", 5.25m, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1.00m },
                    { 2, null, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Apples", "USD", true, "FOGGING EF 170,SB TBZ 99, EF80", "BIN", 5.67m, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1.00m },
                    { 3, null, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Pears", "USD", true, "FOGGING EF 180, TBZ 99, EF 80", "BIN", 9.58m, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1.00m },
                    { 4, null, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Pears", "USD", true, "eFOG-80 FDL FOGGING", "BIN", 5.25m, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1.00m },
                    { 5, null, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Apples", "USD", true, "FOGGING EF 170, EF 160", "BIN", 5.67m, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1.00m },
                    { 6, null, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Pears", "USD", true, "eFOG-180 FOGGING", "BIN", 4.95m, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1.00m },
                    { 7, null, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Apples", "USD", true, "FOGGING EF 170, EF 80", "BIN", 5.67m, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1.00m },
                    { 8, null, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Pears", "USD", true, "FOGGING EF 180, EF 160", "BIN", 9.27m, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1.00m },
                    { 9, null, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Apples", "USD", true, "FOGGING EF 170, SB TBZ 99", "BIN", 5.25m, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1.00m },
                    { 10, null, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Apples", "USD", true, "eFOG-170 DPA FOGGING", "BIN", 2.80m, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 1.00m }
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoomTreatmentApplications_AppliedByUserId",
                table: "RoomTreatmentApplications",
                column: "AppliedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomTreatmentApplications_CreatedByUserId",
                table: "RoomTreatmentApplications",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomTreatmentApplications_OperationKey",
                table: "RoomTreatmentApplications",
                column: "OperationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoomTreatmentApplications_ReversedByUserId",
                table: "RoomTreatmentApplications",
                column: "ReversedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomTreatmentApplications_RoomId_AppliedAt",
                table: "RoomTreatmentApplications",
                columns: new[] { "RoomId", "AppliedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RoomTreatmentApplications_TreatmentChemicalId",
                table: "RoomTreatmentApplications",
                column: "TreatmentChemicalId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomTreatmentApplications_WarehouseId",
                table: "RoomTreatmentApplications",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomTreatmentApplicationSources_FruitProfileId",
                table: "RoomTreatmentApplicationSources",
                column: "FruitProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomTreatmentApplicationSources_GrowerLotId",
                table: "RoomTreatmentApplicationSources",
                column: "GrowerLotId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomTreatmentApplicationSources_RoomTreatmentApplicationId_IdentityKey",
                table: "RoomTreatmentApplicationSources",
                columns: new[] { "RoomTreatmentApplicationId", "IdentityKey" });

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentChemicals_CreatedByUserId",
                table: "TreatmentChemicals",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentChemicals_Crop_IsActive_ProductName",
                table: "TreatmentChemicals",
                columns: new[] { "Crop", "IsActive", "ProductName" });

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentChemicals_ProductName",
                table: "TreatmentChemicals",
                column: "ProductName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentChemicals_UpdatedByUserId",
                table: "TreatmentChemicals",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentLineageMovements_BinsRunEntryId",
                table: "TreatmentLineageMovements",
                column: "BinsRunEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentLineageMovements_CreatedByUserId",
                table: "TreatmentLineageMovements",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentLineageMovements_DestinationRoomId_OccurredAt",
                table: "TreatmentLineageMovements",
                columns: new[] { "DestinationRoomId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentLineageMovements_DestinationSegmentId",
                table: "TreatmentLineageMovements",
                column: "DestinationSegmentId");

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentLineageMovements_OperationKey",
                table: "TreatmentLineageMovements",
                column: "OperationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentLineageMovements_ReversesTreatmentLineageMovementId",
                table: "TreatmentLineageMovements",
                column: "ReversesTreatmentLineageMovementId");

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentLineageMovements_RoomInventoryLossId",
                table: "TreatmentLineageMovements",
                column: "RoomInventoryLossId");

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentLineageMovements_RoomTransferId",
                table: "TreatmentLineageMovements",
                column: "RoomTransferId");

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentLineageMovements_SourceRoomId_OccurredAt",
                table: "TreatmentLineageMovements",
                columns: new[] { "SourceRoomId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentLineageMovements_SourceSegmentId",
                table: "TreatmentLineageMovements",
                column: "SourceSegmentId");

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentLineageSegmentApplications_RoomTreatmentApplicationId_TreatmentLineageSegmentId",
                table: "TreatmentLineageSegmentApplications",
                columns: new[] { "RoomTreatmentApplicationId", "TreatmentLineageSegmentId" });

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentLineageSegments_FruitProfileId",
                table: "TreatmentLineageSegments",
                column: "FruitProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentLineageSegments_GrowerLotId",
                table: "TreatmentLineageSegments",
                column: "GrowerLotId");

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentLineageSegments_RoomId_CurrentBins",
                table: "TreatmentLineageSegments",
                columns: new[] { "RoomId", "CurrentBins" });

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentLineageSegments_RoomId_IdentityKey_TreatmentSignature",
                table: "TreatmentLineageSegments",
                columns: new[] { "RoomId", "IdentityKey", "TreatmentSignature" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentLineageSegments_WarehouseId",
                table: "TreatmentLineageSegments",
                column: "WarehouseId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoomTreatmentApplicationSources");

            migrationBuilder.DropTable(
                name: "TreatmentLineageMovements");

            migrationBuilder.DropTable(
                name: "TreatmentLineageSegmentApplications");

            migrationBuilder.DropTable(
                name: "RoomTreatmentApplications");

            migrationBuilder.DropTable(
                name: "TreatmentLineageSegments");

            migrationBuilder.DropTable(
                name: "TreatmentChemicals");

            migrationBuilder.DropColumn(
                name: "TreatmentSignatureSnapshot",
                table: "BinsRunEntries");

            migrationBuilder.DropColumn(
                name: "TreatmentStateSnapshot",
                table: "BinsRunEntries");

            migrationBuilder.DropColumn(
                name: "TreatmentSummarySnapshot",
                table: "BinsRunEntries");

            migrationBuilder.DropColumn(
                name: "TreatmentSignature",
                table: "ActualRunOverrideRequestLines");
        }
    }
}
