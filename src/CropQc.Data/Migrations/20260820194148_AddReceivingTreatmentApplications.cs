using Microsoft.EntityFrameworkCore.Migrations;

using System;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReceivingTreatmentApplications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TreatmentLineageSegments_RoomId_IdentityKey_TreatmentSignature",
                table: "TreatmentLineageSegments");

            migrationBuilder.DropIndex(
                name: "IX_TreatmentChemicals_Crop_IsActive_ProductName",
                table: "TreatmentChemicals");

            migrationBuilder.AddColumn<long>(
                name: "ReceiptId",
                table: "TreatmentLineageSegments",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReceiptId",
                table: "TreatmentLineageMovements",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApplicationLevel",
                table: "TreatmentChemicals",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"),
                maxLength: 25,
                nullable: false,
                defaultValue: "Room");

            migrationBuilder.AddColumn<long>(
                name: "ReceiptId",
                table: "RoomTreatmentApplicationSources",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApplicationLevel",
                table: "RoomTreatmentApplications",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"),
                maxLength: 25,
                nullable: false,
                defaultValue: "Room");

            migrationBuilder.AddColumn<long>(
                name: "ReceiptId",
                table: "RoomTreatmentApplications",
                type: "bigint",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "TreatmentChemicals",
                keyColumn: "Id",
                keyValue: 1,
                column: "ApplicationLevel",
                value: "Room");

            migrationBuilder.UpdateData(
                table: "TreatmentChemicals",
                keyColumn: "Id",
                keyValue: 2,
                column: "ApplicationLevel",
                value: "Room");

            migrationBuilder.UpdateData(
                table: "TreatmentChemicals",
                keyColumn: "Id",
                keyValue: 3,
                column: "ApplicationLevel",
                value: "Room");

            migrationBuilder.UpdateData(
                table: "TreatmentChemicals",
                keyColumn: "Id",
                keyValue: 4,
                column: "ApplicationLevel",
                value: "Room");

            migrationBuilder.UpdateData(
                table: "TreatmentChemicals",
                keyColumn: "Id",
                keyValue: 5,
                column: "ApplicationLevel",
                value: "Room");

            migrationBuilder.UpdateData(
                table: "TreatmentChemicals",
                keyColumn: "Id",
                keyValue: 6,
                column: "ApplicationLevel",
                value: "Room");

            migrationBuilder.UpdateData(
                table: "TreatmentChemicals",
                keyColumn: "Id",
                keyValue: 7,
                column: "ApplicationLevel",
                value: "Room");

            migrationBuilder.UpdateData(
                table: "TreatmentChemicals",
                keyColumn: "Id",
                keyValue: 8,
                column: "ApplicationLevel",
                value: "Room");

            migrationBuilder.UpdateData(
                table: "TreatmentChemicals",
                keyColumn: "Id",
                keyValue: 9,
                column: "ApplicationLevel",
                value: "Room");

            migrationBuilder.UpdateData(
                table: "TreatmentChemicals",
                keyColumn: "Id",
                keyValue: 10,
                column: "ApplicationLevel",
                value: "Room");

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentLineageSegments_ReceiptId",
                table: "TreatmentLineageSegments",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "UX_TreatmentLineageSegments_Receipt",
                table: "TreatmentLineageSegments",
                columns: new[] { "RoomId", "IdentityKey", "TreatmentSignature", "ReceiptId" },
                unique: true,
                filter: migrationBuilder.ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
                    ? "\"ReceiptId\" IS NOT NULL"
                    : "[ReceiptId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "UX_TreatmentLineageSegments_Unassigned",
                table: "TreatmentLineageSegments",
                columns: new[] { "RoomId", "IdentityKey", "TreatmentSignature" },
                unique: true,
                filter: migrationBuilder.ActiveProvider.Contains("Npgsql", StringComparison.OrdinalIgnoreCase)
                    ? "\"ReceiptId\" IS NULL"
                    : "[ReceiptId] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentLineageMovements_ReceiptId",
                table: "TreatmentLineageMovements",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentChemicals_ApplicationLevel_Crop_IsActive_ProductName",
                table: "TreatmentChemicals",
                columns: new[] { "ApplicationLevel", "Crop", "IsActive", "ProductName" });

            migrationBuilder.CreateIndex(
                name: "IX_RoomTreatmentApplicationSources_ReceiptId",
                table: "RoomTreatmentApplicationSources",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomTreatmentApplications_ReceiptId_AppliedAt",
                table: "RoomTreatmentApplications",
                columns: new[] { "ReceiptId", "AppliedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_RoomTreatmentApplications_Receipts_ReceiptId",
                table: "RoomTreatmentApplications",
                column: "ReceiptId",
                principalTable: "Receipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RoomTreatmentApplicationSources_Receipts_ReceiptId",
                table: "RoomTreatmentApplicationSources",
                column: "ReceiptId",
                principalTable: "Receipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TreatmentLineageMovements_Receipts_ReceiptId",
                table: "TreatmentLineageMovements",
                column: "ReceiptId",
                principalTable: "Receipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TreatmentLineageSegments_Receipts_ReceiptId",
                table: "TreatmentLineageSegments",
                column: "ReceiptId",
                principalTable: "Receipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RoomTreatmentApplications_Receipts_ReceiptId",
                table: "RoomTreatmentApplications");

            migrationBuilder.DropForeignKey(
                name: "FK_RoomTreatmentApplicationSources_Receipts_ReceiptId",
                table: "RoomTreatmentApplicationSources");

            migrationBuilder.DropForeignKey(
                name: "FK_TreatmentLineageMovements_Receipts_ReceiptId",
                table: "TreatmentLineageMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_TreatmentLineageSegments_Receipts_ReceiptId",
                table: "TreatmentLineageSegments");

            migrationBuilder.DropIndex(
                name: "IX_TreatmentLineageSegments_ReceiptId",
                table: "TreatmentLineageSegments");

            migrationBuilder.DropIndex(
                name: "UX_TreatmentLineageSegments_Receipt",
                table: "TreatmentLineageSegments");

            migrationBuilder.DropIndex(
                name: "UX_TreatmentLineageSegments_Unassigned",
                table: "TreatmentLineageSegments");

            migrationBuilder.DropIndex(
                name: "IX_TreatmentLineageMovements_ReceiptId",
                table: "TreatmentLineageMovements");

            migrationBuilder.DropIndex(
                name: "IX_TreatmentChemicals_ApplicationLevel_Crop_IsActive_ProductName",
                table: "TreatmentChemicals");

            migrationBuilder.DropIndex(
                name: "IX_RoomTreatmentApplicationSources_ReceiptId",
                table: "RoomTreatmentApplicationSources");

            migrationBuilder.DropIndex(
                name: "IX_RoomTreatmentApplications_ReceiptId_AppliedAt",
                table: "RoomTreatmentApplications");

            migrationBuilder.DropColumn(
                name: "ReceiptId",
                table: "TreatmentLineageSegments");

            migrationBuilder.DropColumn(
                name: "ReceiptId",
                table: "TreatmentLineageMovements");

            migrationBuilder.DropColumn(
                name: "ApplicationLevel",
                table: "TreatmentChemicals");

            migrationBuilder.DropColumn(
                name: "ReceiptId",
                table: "RoomTreatmentApplicationSources");

            migrationBuilder.DropColumn(
                name: "ApplicationLevel",
                table: "RoomTreatmentApplications");

            migrationBuilder.DropColumn(
                name: "ReceiptId",
                table: "RoomTreatmentApplications");

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentLineageSegments_RoomId_IdentityKey_TreatmentSignature",
                table: "TreatmentLineageSegments",
                columns: new[] { "RoomId", "IdentityKey", "TreatmentSignature" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentChemicals_Crop_IsActive_ProductName",
                table: "TreatmentChemicals",
                columns: new[] { "Crop", "IsActive", "ProductName" });
        }
    }
}
