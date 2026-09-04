using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReintroduceQcPhotoOrientation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ManualRotationQuarterTurns",
                table: "QcPhotos",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OriginalExifOrientation",
                table: "QcPhotos",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PresentationContentType",
                table: "QcPhotos",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"),
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PresentationFileName",
                table: "QcPhotos",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(260)", "character varying(260)"),
                maxLength: 260,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PresentationFileSizeBytes",
                table: "QcPhotos",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"),
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PresentationRevision",
                table: "QcPhotos",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "PresentationStorageKey",
                table: "QcPhotos",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"),
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PresentationUpdatedAt",
                table: "QcPhotos",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"),
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_QcPhotos_OrientationState",
                table: "QcPhotos",
                sql: MigrationProviderTypes.Sql(migrationBuilder,
                    "[ManualRotationQuarterTurns] BETWEEN 0 AND 3 AND [PresentationRevision] >= 0 AND ([OriginalExifOrientation] IS NULL OR [OriginalExifOrientation] BETWEEN 1 AND 8)",
                    "\"ManualRotationQuarterTurns\" BETWEEN 0 AND 3 AND \"PresentationRevision\" >= 0 AND (\"OriginalExifOrientation\" IS NULL OR \"OriginalExifOrientation\" BETWEEN 1 AND 8)"));

            migrationBuilder.AddCheckConstraint(
                name: "CK_QcPhotos_PresentationMetadata",
                table: "QcPhotos",
                sql: MigrationProviderTypes.Sql(migrationBuilder,
                    "([PresentationStorageKey] IS NULL AND [PresentationFileName] IS NULL AND [PresentationContentType] IS NULL AND [PresentationFileSizeBytes] IS NULL AND [PresentationUpdatedAt] IS NULL) OR ([PresentationStorageKey] IS NOT NULL AND [PresentationFileName] IS NOT NULL AND [PresentationContentType] IS NOT NULL AND [PresentationFileSizeBytes] >= 0 AND [PresentationUpdatedAt] IS NOT NULL AND [PresentationRevision] > 0)",
                    "(\"PresentationStorageKey\" IS NULL AND \"PresentationFileName\" IS NULL AND \"PresentationContentType\" IS NULL AND \"PresentationFileSizeBytes\" IS NULL AND \"PresentationUpdatedAt\" IS NULL) OR (\"PresentationStorageKey\" IS NOT NULL AND \"PresentationFileName\" IS NOT NULL AND \"PresentationContentType\" IS NOT NULL AND \"PresentationFileSizeBytes\" >= 0 AND \"PresentationUpdatedAt\" IS NOT NULL AND \"PresentationRevision\" > 0)"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_QcPhotos_OrientationState",
                table: "QcPhotos");

            migrationBuilder.DropCheckConstraint(
                name: "CK_QcPhotos_PresentationMetadata",
                table: "QcPhotos");

            migrationBuilder.DropColumn(
                name: "ManualRotationQuarterTurns",
                table: "QcPhotos");

            migrationBuilder.DropColumn(
                name: "OriginalExifOrientation",
                table: "QcPhotos");

            migrationBuilder.DropColumn(
                name: "PresentationContentType",
                table: "QcPhotos");

            migrationBuilder.DropColumn(
                name: "PresentationFileName",
                table: "QcPhotos");

            migrationBuilder.DropColumn(
                name: "PresentationFileSizeBytes",
                table: "QcPhotos");

            migrationBuilder.DropColumn(
                name: "PresentationRevision",
                table: "QcPhotos");

            migrationBuilder.DropColumn(
                name: "PresentationStorageKey",
                table: "QcPhotos");

            migrationBuilder.DropColumn(
                name: "PresentationUpdatedAt",
                table: "QcPhotos");
        }
    }
}
