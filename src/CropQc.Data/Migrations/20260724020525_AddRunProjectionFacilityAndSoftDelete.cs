using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRunProjectionFacilityAndSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            var postgres = migrationBuilder.ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase);
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DeletedAt",
                table: "RunProjections",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"),
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DeletedByUserId",
                table: "RunProjections",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletedFromStatus",
                table: "RunProjections",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"),
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "DeletionOperationId",
                table: "RunProjections",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "uniqueidentifier", "uuid"),
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeletionReason",
                table: "RunProjections",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"),
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FacilityCodeSnapshot",
                table: "RunProjections",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"),
                maxLength: 25,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FacilityWarehouseId",
                table: "RunProjections",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsDeleted",
                table: "RunProjections",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"),
                nullable: false,
                defaultValue: false);

            if (postgres)
            {
                migrationBuilder.Sql(
                    """
                    UPDATE "RunProjections" AS p
                    SET "FacilityWarehouseId" = candidate."WarehouseId",
                        "FacilityCodeSnapshot" = w."Code"
                    FROM (
                        SELECT s."RunProjectionId", MIN(s."WarehouseId") AS "WarehouseId"
                        FROM "RunProjectionSources" AS s
                        WHERE s."SourceType" = 'Inventory'
                        GROUP BY s."RunProjectionId"
                        HAVING COUNT(*) > 0
                           AND COUNT(s."WarehouseId") = COUNT(*)
                           AND COUNT(DISTINCT s."WarehouseId") = 1
                    ) AS candidate
                    INNER JOIN "Warehouses" AS w ON w."Id" = candidate."WarehouseId"
                    WHERE p."Id" = candidate."RunProjectionId"
                      AND w."IsActive" = TRUE
                      AND w."Code" IN ('WP', 'EBS');
                    """);
            }
            else
            {
                migrationBuilder.Sql(
                    """
                    UPDATE p
                    SET p.[FacilityWarehouseId] = candidate.[WarehouseId],
                        p.[FacilityCodeSnapshot] = w.[Code]
                    FROM [RunProjections] AS p
                    INNER JOIN (
                        SELECT s.[RunProjectionId], MIN(s.[WarehouseId]) AS [WarehouseId]
                        FROM [RunProjectionSources] AS s
                        WHERE s.[SourceType] = 'Inventory'
                        GROUP BY s.[RunProjectionId]
                        HAVING COUNT(*) > 0
                           AND COUNT(s.[WarehouseId]) = COUNT(*)
                           AND COUNT(DISTINCT s.[WarehouseId]) = 1
                    ) AS candidate ON candidate.[RunProjectionId] = p.[Id]
                    INNER JOIN [Warehouses] AS w ON w.[Id] = candidate.[WarehouseId]
                    WHERE w.[IsActive] = 1
                      AND w.[Code] IN ('WP', 'EBS');
                    """);
            }

            migrationBuilder.CreateIndex(
                name: "IX_RunProjections_CropYear_FacilityWarehouseId_IsDeleted",
                table: "RunProjections",
                columns: new[] { "CropYear", "FacilityWarehouseId", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_RunProjections_DeletedByUserId",
                table: "RunProjections",
                column: "DeletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RunProjections_DeletionOperationId",
                table: "RunProjections",
                column: "DeletionOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_RunProjections_FacilityWarehouseId_PlannedRunDate_IsDeleted_Status",
                table: "RunProjections",
                columns: new[] { "FacilityWarehouseId", "PlannedRunDate", "IsDeleted", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_RunProjections_Users_DeletedByUserId",
                table: "RunProjections",
                column: "DeletedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_RunProjections_Warehouses_FacilityWarehouseId",
                table: "RunProjections",
                column: "FacilityWarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RunProjections_Users_DeletedByUserId",
                table: "RunProjections");

            migrationBuilder.DropForeignKey(
                name: "FK_RunProjections_Warehouses_FacilityWarehouseId",
                table: "RunProjections");

            migrationBuilder.DropIndex(
                name: "IX_RunProjections_CropYear_FacilityWarehouseId_IsDeleted",
                table: "RunProjections");

            migrationBuilder.DropIndex(
                name: "IX_RunProjections_DeletedByUserId",
                table: "RunProjections");

            migrationBuilder.DropIndex(
                name: "IX_RunProjections_DeletionOperationId",
                table: "RunProjections");

            migrationBuilder.DropIndex(
                name: "IX_RunProjections_FacilityWarehouseId_PlannedRunDate_IsDeleted_Status",
                table: "RunProjections");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "RunProjections");

            migrationBuilder.DropColumn(
                name: "DeletedByUserId",
                table: "RunProjections");

            migrationBuilder.DropColumn(
                name: "DeletedFromStatus",
                table: "RunProjections");

            migrationBuilder.DropColumn(
                name: "DeletionOperationId",
                table: "RunProjections");

            migrationBuilder.DropColumn(
                name: "DeletionReason",
                table: "RunProjections");

            migrationBuilder.DropColumn(
                name: "FacilityCodeSnapshot",
                table: "RunProjections");

            migrationBuilder.DropColumn(
                name: "FacilityWarehouseId",
                table: "RunProjections");

            migrationBuilder.DropColumn(
                name: "IsDeleted",
                table: "RunProjections");
        }
    }
}
