using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddActualRunRoomInventoryLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "ActualRunId",
                table: "RoomInventoryAdjustments",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"),
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ActualRunRevisionId",
                table: "RoomInventoryAdjustments",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"),
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ActualRunId",
                table: "BinsRunEntries",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"),
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ActualRunRevisionId",
                table: "BinsRunEntries",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"),
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CropYear",
                table: "BinsRunEntries",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsOverdrawOverride",
                table: "BinsRunEntries",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"),
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OverrideApprovedAt",
                table: "BinsRunEntries",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"),
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OverrideApprovedByUserId",
                table: "BinsRunEntries",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OverrideAvailableBins",
                table: "BinsRunEntries",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OverrideReason",
                table: "BinsRunEntries",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"),
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OverrideRequestedBins",
                table: "BinsRunEntries",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OverrideShortageBins",
                table: "BinsRunEntries",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReversesBinsRunEntryId",
                table: "BinsRunEntries",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"),
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TransactionType",
                table: "BinsRunEntries",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"),
                maxLength: 25,
                nullable: false,
                defaultValue: "Legacy");

            migrationBuilder.CreateTable(
                name: "ActualRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RunProjectionId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: true),
                    Status = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"), maxLength: 25, nullable: false),
                    CurrentRevisionNumber = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    ConcurrencyVersion = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false),
                    RunAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    Notes = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    UpdatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true),
                    CanceledByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    CanceledAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true),
                    CancellationReason = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActualRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActualRuns_RunProjections_RunProjectionId",
                        column: x => x.RunProjectionId,
                        principalTable: "RunProjections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ActualRuns_Users_CanceledByUserId",
                        column: x => x.CanceledByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ActualRuns_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ActualRuns_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ActualRunOverrideRequests",
                columns: table => new
                {
                    Id = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ActualRunId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: true),
                    RunProjectionId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: true),
                    OperationType = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"), maxLength: 25, nullable: false),
                    OperationKey = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(64)", "character varying(64)"), maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"), maxLength: 25, nullable: false),
                    ExpectedConcurrencyVersion = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: true),
                    RunAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    Notes = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true),
                    RequestedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    RequestedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    ApprovedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true),
                    ApprovalReason = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActualRunOverrideRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActualRunOverrideRequests_ActualRuns_ActualRunId",
                        column: x => x.ActualRunId,
                        principalTable: "ActualRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ActualRunOverrideRequests_RunProjections_RunProjectionId",
                        column: x => x.RunProjectionId,
                        principalTable: "RunProjections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ActualRunOverrideRequests_Users_ApprovedByUserId",
                        column: x => x.ApprovedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ActualRunOverrideRequests_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ActualRunRevisions",
                columns: table => new
                {
                    Id = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ActualRunId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false),
                    RevisionNumber = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    OperationType = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"), maxLength: 25, nullable: false),
                    OperationKey = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(64)", "character varying(64)"), maxLength: 64, nullable: false),
                    IsCurrent = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    Reason = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActualRunRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActualRunRevisions_ActualRuns_ActualRunId",
                        column: x => x.ActualRunId,
                        principalTable: "ActualRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActualRunRevisions_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ActualRunOverrideRequestLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ActualRunOverrideRequestId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false),
                    WarehouseId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    RoomId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    CropYear = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    GrowerLotId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    FruitProfileId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    GrowerName = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"), maxLength: 200, nullable: false),
                    LotNumber = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: false),
                    PoolStart = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(20)", "character varying(20)"), maxLength: 20, nullable: true),
                    VarietyCode = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    InventoryStatus = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: true),
                    AvailableBins = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    RequestedBins = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    ShortageBins = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    RunProjectionSourceId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActualRunOverrideRequestLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActualRunOverrideRequestLines_ActualRunOverrideRequests_ActualRunOverrideRequestId",
                        column: x => x.ActualRunOverrideRequestId,
                        principalTable: "ActualRunOverrideRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActualRunOverrideRequestLines_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ActualRunOverrideRequestLines_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoomInventoryAdjustments_ActualRunId_ActualRunRevisionId",
                table: "RoomInventoryAdjustments",
                columns: new[] { "ActualRunId", "ActualRunRevisionId" });

            migrationBuilder.CreateIndex(
                name: "IX_RoomInventoryAdjustments_ActualRunRevisionId",
                table: "RoomInventoryAdjustments",
                column: "ActualRunRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomInventoryAdjustments_WarehouseId_RoomId_CropYear_LotNumber_VarietyCode_AdjustmentAt",
                table: "RoomInventoryAdjustments",
                columns: new[] { "WarehouseId", "RoomId", "CropYear", "LotNumber", "VarietyCode", "AdjustmentAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BinsRunEntries_ActualRunId_ActualRunRevisionId_TransactionType",
                table: "BinsRunEntries",
                columns: new[] { "ActualRunId", "ActualRunRevisionId", "TransactionType" });

            migrationBuilder.CreateIndex(
                name: "IX_BinsRunEntries_ActualRunRevisionId",
                table: "BinsRunEntries",
                column: "ActualRunRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_BinsRunEntries_OverrideApprovedByUserId",
                table: "BinsRunEntries",
                column: "OverrideApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BinsRunEntries_ReversesBinsRunEntryId",
                table: "BinsRunEntries",
                column: "ReversesBinsRunEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_ActualRunOverrideRequestLines_ActualRunOverrideRequestId_RoomId_LotNumber_VarietyCode",
                table: "ActualRunOverrideRequestLines",
                columns: new[] { "ActualRunOverrideRequestId", "RoomId", "LotNumber", "VarietyCode" });

            migrationBuilder.CreateIndex(
                name: "IX_ActualRunOverrideRequestLines_RoomId",
                table: "ActualRunOverrideRequestLines",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_ActualRunOverrideRequestLines_WarehouseId",
                table: "ActualRunOverrideRequestLines",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_ActualRunOverrideRequests_ActualRunId",
                table: "ActualRunOverrideRequests",
                column: "ActualRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ActualRunOverrideRequests_ApprovedByUserId",
                table: "ActualRunOverrideRequests",
                column: "ApprovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ActualRunOverrideRequests_OperationKey",
                table: "ActualRunOverrideRequests",
                column: "OperationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActualRunOverrideRequests_RequestedByUserId",
                table: "ActualRunOverrideRequests",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ActualRunOverrideRequests_RunProjectionId",
                table: "ActualRunOverrideRequests",
                column: "RunProjectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ActualRunOverrideRequests_Status_RequestedAt",
                table: "ActualRunOverrideRequests",
                columns: new[] { "Status", "RequestedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ActualRunRevisions_ActualRunId_IsCurrent",
                table: "ActualRunRevisions",
                columns: new[] { "ActualRunId", "IsCurrent" });

            migrationBuilder.CreateIndex(
                name: "IX_ActualRunRevisions_ActualRunId_RevisionNumber",
                table: "ActualRunRevisions",
                columns: new[] { "ActualRunId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActualRunRevisions_CreatedByUserId",
                table: "ActualRunRevisions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ActualRunRevisions_OperationKey",
                table: "ActualRunRevisions",
                column: "OperationKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActualRuns_CanceledByUserId",
                table: "ActualRuns",
                column: "CanceledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ActualRuns_CreatedByUserId",
                table: "ActualRuns",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ActualRuns_RunProjectionId",
                table: "ActualRuns",
                column: "RunProjectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ActualRuns_Status_RunAt",
                table: "ActualRuns",
                columns: new[] { "Status", "RunAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ActualRuns_UpdatedByUserId",
                table: "ActualRuns",
                column: "UpdatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_BinsRunEntries_ActualRunRevisions_ActualRunRevisionId",
                table: "BinsRunEntries",
                column: "ActualRunRevisionId",
                principalTable: "ActualRunRevisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_BinsRunEntries_ActualRuns_ActualRunId",
                table: "BinsRunEntries",
                column: "ActualRunId",
                principalTable: "ActualRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_BinsRunEntries_BinsRunEntries_ReversesBinsRunEntryId",
                table: "BinsRunEntries",
                column: "ReversesBinsRunEntryId",
                principalTable: "BinsRunEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BinsRunEntries_Users_OverrideApprovedByUserId",
                table: "BinsRunEntries",
                column: "OverrideApprovedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_RoomInventoryAdjustments_ActualRunRevisions_ActualRunRevisionId",
                table: "RoomInventoryAdjustments",
                column: "ActualRunRevisionId",
                principalTable: "ActualRunRevisions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_RoomInventoryAdjustments_ActualRuns_ActualRunId",
                table: "RoomInventoryAdjustments",
                column: "ActualRunId",
                principalTable: "ActualRuns",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BinsRunEntries_ActualRunRevisions_ActualRunRevisionId",
                table: "BinsRunEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_BinsRunEntries_ActualRuns_ActualRunId",
                table: "BinsRunEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_BinsRunEntries_BinsRunEntries_ReversesBinsRunEntryId",
                table: "BinsRunEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_BinsRunEntries_Users_OverrideApprovedByUserId",
                table: "BinsRunEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_RoomInventoryAdjustments_ActualRunRevisions_ActualRunRevisionId",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropForeignKey(
                name: "FK_RoomInventoryAdjustments_ActualRuns_ActualRunId",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropTable(
                name: "ActualRunOverrideRequestLines");

            migrationBuilder.DropTable(
                name: "ActualRunRevisions");

            migrationBuilder.DropTable(
                name: "ActualRunOverrideRequests");

            migrationBuilder.DropTable(
                name: "ActualRuns");

            migrationBuilder.DropIndex(
                name: "IX_RoomInventoryAdjustments_ActualRunId_ActualRunRevisionId",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_RoomInventoryAdjustments_ActualRunRevisionId",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_RoomInventoryAdjustments_WarehouseId_RoomId_CropYear_LotNumber_VarietyCode_AdjustmentAt",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropIndex(
                name: "IX_BinsRunEntries_ActualRunId_ActualRunRevisionId_TransactionType",
                table: "BinsRunEntries");

            migrationBuilder.DropIndex(
                name: "IX_BinsRunEntries_ActualRunRevisionId",
                table: "BinsRunEntries");

            migrationBuilder.DropIndex(
                name: "IX_BinsRunEntries_OverrideApprovedByUserId",
                table: "BinsRunEntries");

            migrationBuilder.DropIndex(
                name: "IX_BinsRunEntries_ReversesBinsRunEntryId",
                table: "BinsRunEntries");

            migrationBuilder.DropColumn(
                name: "ActualRunId",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropColumn(
                name: "ActualRunRevisionId",
                table: "RoomInventoryAdjustments");

            migrationBuilder.DropColumn(
                name: "ActualRunId",
                table: "BinsRunEntries");

            migrationBuilder.DropColumn(
                name: "ActualRunRevisionId",
                table: "BinsRunEntries");

            migrationBuilder.DropColumn(
                name: "CropYear",
                table: "BinsRunEntries");

            migrationBuilder.DropColumn(
                name: "IsOverdrawOverride",
                table: "BinsRunEntries");

            migrationBuilder.DropColumn(
                name: "OverrideApprovedAt",
                table: "BinsRunEntries");

            migrationBuilder.DropColumn(
                name: "OverrideApprovedByUserId",
                table: "BinsRunEntries");

            migrationBuilder.DropColumn(
                name: "OverrideAvailableBins",
                table: "BinsRunEntries");

            migrationBuilder.DropColumn(
                name: "OverrideReason",
                table: "BinsRunEntries");

            migrationBuilder.DropColumn(
                name: "OverrideRequestedBins",
                table: "BinsRunEntries");

            migrationBuilder.DropColumn(
                name: "OverrideShortageBins",
                table: "BinsRunEntries");

            migrationBuilder.DropColumn(
                name: "ReversesBinsRunEntryId",
                table: "BinsRunEntries");

            migrationBuilder.DropColumn(
                name: "TransactionType",
                table: "BinsRunEntries");
        }
    }
}
