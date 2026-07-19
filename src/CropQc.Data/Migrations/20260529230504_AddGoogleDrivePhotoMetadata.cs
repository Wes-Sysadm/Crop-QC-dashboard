using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleDrivePhotoMetadata : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DriveId",
                table: "QcPhotos",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"),
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileId",
                table: "QcPhotos",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"),
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FolderId",
                table: "QcPhotos",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"),
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageProvider",
                table: "QcPhotos",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"),
                maxLength: 50,
                nullable: false,
                defaultValue: "Legacy");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UploadedAt",
                table: "QcPhotos",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"),
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_QcPhotos_StorageProvider_FileId",
                table: "QcPhotos",
                columns: new[] { "StorageProvider", "FileId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_QcPhotos_StorageProvider_FileId",
                table: "QcPhotos");

            migrationBuilder.DropColumn(
                name: "DriveId",
                table: "QcPhotos");

            migrationBuilder.DropColumn(
                name: "FileId",
                table: "QcPhotos");

            migrationBuilder.DropColumn(
                name: "FolderId",
                table: "QcPhotos");

            migrationBuilder.DropColumn(
                name: "StorageProvider",
                table: "QcPhotos");

            migrationBuilder.DropColumn(
                name: "UploadedAt",
                table: "QcPhotos");
        }
    }
}
