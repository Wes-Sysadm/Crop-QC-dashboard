using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFacilityRunReporting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmploymentEffectiveAt",
                table: "Users",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"),
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmploymentFacility",
                table: "Users",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"),
                maxLength: 25,
                nullable: false,
                defaultValue: "Unassigned");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EmploymentUpdatedAt",
                table: "Users",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"),
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "EmploymentUpdatedByUserId",
                table: "Users",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GrowerNumberSnapshot",
                table: "BinsRunEntries",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"),
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsOrganicSnapshot",
                table: "BinsRunEntries",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"),
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProductionTypeSnapshot",
                table: "BinsRunEntries",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"),
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReportingCropYearSnapshot",
                table: "BinsRunEntries",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReportingFacilityAssignedAt",
                table: "BinsRunEntries",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"),
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReportingFacilityAssignedByUserId",
                table: "BinsRunEntries",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportingFacilityAssignmentSource",
                table: "BinsRunEntries",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"),
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportingFacilityCodeSnapshot",
                table: "BinsRunEntries",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"),
                maxLength: 25,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReportingFacilityWarehouseId",
                table: "BinsRunEntries",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReportingFruitProfileIdSnapshot",
                table: "BinsRunEntries",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportingVarietyCodeSnapshot",
                table: "BinsRunEntries",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"),
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RunFacilityAssignedAt",
                table: "ActualRuns",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"),
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RunFacilityAssignedByUserId",
                table: "ActualRuns",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RunFacilityAssignmentSource",
                table: "ActualRuns",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"),
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RunFacilityCodeSnapshot",
                table: "ActualRuns",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"),
                maxLength: 25,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RunFacilityWarehouseId",
                table: "ActualRuns",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RunFacilityAssignmentSource",
                table: "ActualRunOverrideRequests",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"),
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RunFacilityCodeSnapshot",
                table: "ActualRunOverrideRequests",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"),
                maxLength: 25,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RunFacilityWarehouseId",
                table: "ActualRunOverrideRequests",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserEmploymentHistory",
                columns: table => new
                {
                    Id = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    PreviousEmploymentFacility = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"), maxLength: 25, nullable: false),
                    EmploymentFacility = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"), maxLength: 25, nullable: false),
                    EffectiveAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    ChangedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    ChangedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserEmploymentHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserEmploymentHistory_Users_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_UserEmploymentHistory_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_EmploymentFacility",
                table: "Users",
                column: "EmploymentFacility");

            migrationBuilder.CreateIndex(
                name: "IX_Users_EmploymentUpdatedByUserId",
                table: "Users",
                column: "EmploymentUpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BinsRunEntries_ReportingFacilityAssignedByUserId",
                table: "BinsRunEntries",
                column: "ReportingFacilityAssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BinsRunEntries_ReportingFacilityWarehouseId_ReportingCropYearSnapshot_RunAt",
                table: "BinsRunEntries",
                columns: new[] { "ReportingFacilityWarehouseId", "ReportingCropYearSnapshot", "RunAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ActualRuns_RunFacilityAssignedByUserId",
                table: "ActualRuns",
                column: "RunFacilityAssignedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ActualRuns_RunFacilityWarehouseId_Status_RunAt",
                table: "ActualRuns",
                columns: new[] { "RunFacilityWarehouseId", "Status", "RunAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ActualRunOverrideRequests_RunFacilityWarehouseId",
                table: "ActualRunOverrideRequests",
                column: "RunFacilityWarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_UserEmploymentHistory_ChangedByUserId",
                table: "UserEmploymentHistory",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserEmploymentHistory_UserId_ChangedAt",
                table: "UserEmploymentHistory",
                columns: new[] { "UserId", "ChangedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_ActualRunOverrideRequests_Warehouses_RunFacilityWarehouseId",
                table: "ActualRunOverrideRequests",
                column: "RunFacilityWarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ActualRuns_Users_RunFacilityAssignedByUserId",
                table: "ActualRuns",
                column: "RunFacilityAssignedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_ActualRuns_Warehouses_RunFacilityWarehouseId",
                table: "ActualRuns",
                column: "RunFacilityWarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_BinsRunEntries_Users_ReportingFacilityAssignedByUserId",
                table: "BinsRunEntries",
                column: "ReportingFacilityAssignedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_BinsRunEntries_Warehouses_ReportingFacilityWarehouseId",
                table: "BinsRunEntries",
                column: "ReportingFacilityWarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Users_Users_EmploymentUpdatedByUserId",
                table: "Users",
                column: "EmploymentUpdatedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ActualRunOverrideRequests_Warehouses_RunFacilityWarehouseId",
                table: "ActualRunOverrideRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_ActualRuns_Users_RunFacilityAssignedByUserId",
                table: "ActualRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_ActualRuns_Warehouses_RunFacilityWarehouseId",
                table: "ActualRuns");

            migrationBuilder.DropForeignKey(
                name: "FK_BinsRunEntries_Users_ReportingFacilityAssignedByUserId",
                table: "BinsRunEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_BinsRunEntries_Warehouses_ReportingFacilityWarehouseId",
                table: "BinsRunEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_Users_Users_EmploymentUpdatedByUserId",
                table: "Users");

            migrationBuilder.DropTable(
                name: "UserEmploymentHistory");

            migrationBuilder.DropIndex(
                name: "IX_Users_EmploymentFacility",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_EmploymentUpdatedByUserId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_BinsRunEntries_ReportingFacilityAssignedByUserId",
                table: "BinsRunEntries");

            migrationBuilder.DropIndex(
                name: "IX_BinsRunEntries_ReportingFacilityWarehouseId_ReportingCropYearSnapshot_RunAt",
                table: "BinsRunEntries");

            migrationBuilder.DropIndex(
                name: "IX_ActualRuns_RunFacilityAssignedByUserId",
                table: "ActualRuns");

            migrationBuilder.DropIndex(
                name: "IX_ActualRuns_RunFacilityWarehouseId_Status_RunAt",
                table: "ActualRuns");

            migrationBuilder.DropIndex(
                name: "IX_ActualRunOverrideRequests_RunFacilityWarehouseId",
                table: "ActualRunOverrideRequests");

            migrationBuilder.DropColumn(
                name: "EmploymentEffectiveAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmploymentFacility",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmploymentUpdatedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmploymentUpdatedByUserId",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "GrowerNumberSnapshot",
                table: "BinsRunEntries");

            migrationBuilder.DropColumn(
                name: "IsOrganicSnapshot",
                table: "BinsRunEntries");

            migrationBuilder.DropColumn(
                name: "ProductionTypeSnapshot",
                table: "BinsRunEntries");

            migrationBuilder.DropColumn(
                name: "ReportingCropYearSnapshot",
                table: "BinsRunEntries");

            migrationBuilder.DropColumn(
                name: "ReportingFacilityAssignedAt",
                table: "BinsRunEntries");

            migrationBuilder.DropColumn(
                name: "ReportingFacilityAssignedByUserId",
                table: "BinsRunEntries");

            migrationBuilder.DropColumn(
                name: "ReportingFacilityAssignmentSource",
                table: "BinsRunEntries");

            migrationBuilder.DropColumn(
                name: "ReportingFacilityCodeSnapshot",
                table: "BinsRunEntries");

            migrationBuilder.DropColumn(
                name: "ReportingFacilityWarehouseId",
                table: "BinsRunEntries");

            migrationBuilder.DropColumn(
                name: "ReportingFruitProfileIdSnapshot",
                table: "BinsRunEntries");

            migrationBuilder.DropColumn(
                name: "ReportingVarietyCodeSnapshot",
                table: "BinsRunEntries");

            migrationBuilder.DropColumn(
                name: "RunFacilityAssignedAt",
                table: "ActualRuns");

            migrationBuilder.DropColumn(
                name: "RunFacilityAssignedByUserId",
                table: "ActualRuns");

            migrationBuilder.DropColumn(
                name: "RunFacilityAssignmentSource",
                table: "ActualRuns");

            migrationBuilder.DropColumn(
                name: "RunFacilityCodeSnapshot",
                table: "ActualRuns");

            migrationBuilder.DropColumn(
                name: "RunFacilityWarehouseId",
                table: "ActualRuns");

            migrationBuilder.DropColumn(
                name: "RunFacilityAssignmentSource",
                table: "ActualRunOverrideRequests");

            migrationBuilder.DropColumn(
                name: "RunFacilityCodeSnapshot",
                table: "ActualRunOverrideRequests");

            migrationBuilder.DropColumn(
                name: "RunFacilityWarehouseId",
                table: "ActualRunOverrideRequests");
        }
    }
}
