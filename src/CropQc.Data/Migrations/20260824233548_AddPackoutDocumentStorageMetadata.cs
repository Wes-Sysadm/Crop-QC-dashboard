using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPackoutDocumentStorageMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DriveId",
                table: "PackoutReportSources",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(250)", "character varying(250)"),
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileId",
                table: "PackoutReportSources",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(250)", "character varying(250)"),
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FolderId",
                table: "PackoutReportSources",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(250)", "character varying(250)"),
                maxLength: 250,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ParseStatus",
                table: "PackoutReportSources",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"),
                maxLength: 50,
                nullable: false,
                defaultValue: "Legacy metadata only");

            migrationBuilder.AddColumn<string>(
                name: "StorageKey",
                table: "PackoutReportSources",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(500)", "character varying(500)"),
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StoragePath",
                table: "PackoutReportSources",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"),
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageProvider",
                table: "PackoutReportSources",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"),
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UploadedAt",
                table: "PackoutReportSources",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"),
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UploadedByUserId",
                table: "PackoutReportSources",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackoutReportSources_UploadedByUserId",
                table: "PackoutReportSources",
                column: "UploadedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_PackoutReportSources_Users_UploadedByUserId",
                table: "PackoutReportSources",
                column: "UploadedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PackoutReportSources_Users_UploadedByUserId",
                table: "PackoutReportSources");

            migrationBuilder.DropIndex(
                name: "IX_PackoutReportSources_UploadedByUserId",
                table: "PackoutReportSources");

            migrationBuilder.DropColumn(
                name: "DriveId",
                table: "PackoutReportSources");

            migrationBuilder.DropColumn(
                name: "FileId",
                table: "PackoutReportSources");

            migrationBuilder.DropColumn(
                name: "FolderId",
                table: "PackoutReportSources");

            migrationBuilder.DropColumn(
                name: "ParseStatus",
                table: "PackoutReportSources");

            migrationBuilder.DropColumn(
                name: "StorageKey",
                table: "PackoutReportSources");

            migrationBuilder.DropColumn(
                name: "StoragePath",
                table: "PackoutReportSources");

            migrationBuilder.DropColumn(
                name: "StorageProvider",
                table: "PackoutReportSources");

            migrationBuilder.DropColumn(
                name: "UploadedAt",
                table: "PackoutReportSources");

            migrationBuilder.DropColumn(
                name: "UploadedByUserId",
                table: "PackoutReportSources");
        }
    }
}
