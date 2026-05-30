using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddQcStationEnrollmentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApiKeyCreatedAt",
                table: "QcStations",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApiKeyHash",
                table: "QcStations",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApiKeyLastFour",
                table: "QcStations",
                type: "nvarchar(12)",
                maxLength: 12,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApiKeyRotatedAt",
                table: "QcStations",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                table: "QcStations",
                type: "datetimeoffset",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<int>(
                name: "CreatedByUserId",
                table: "QcStations",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "QcStations",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSeenAt",
                table: "QcStations",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastSeenIp",
                table: "QcStations",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastSyncAt",
                table: "QcStations",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "QcStations",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StationName",
                table: "QcStations",
                type: "nvarchar(150)",
                maxLength: 150,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "UpdatedAt",
                table: "QcStations",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WarehouseCode",
                table: "QcStations",
                type: "nvarchar(25)",
                maxLength: 25,
                nullable: true);

            migrationBuilder.Sql("UPDATE [QcStations] SET [StationName] = [Name] WHERE [StationName] = N'';");

            migrationBuilder.CreateIndex(
                name: "IX_QcStations_CreatedByUserId",
                table: "QcStations",
                column: "CreatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_QcStations_Users_CreatedByUserId",
                table: "QcStations",
                column: "CreatedByUserId",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_QcStations_Users_CreatedByUserId",
                table: "QcStations");

            migrationBuilder.DropIndex(
                name: "IX_QcStations_CreatedByUserId",
                table: "QcStations");

            migrationBuilder.DropColumn(
                name: "ApiKeyCreatedAt",
                table: "QcStations");

            migrationBuilder.DropColumn(
                name: "ApiKeyHash",
                table: "QcStations");

            migrationBuilder.DropColumn(
                name: "ApiKeyLastFour",
                table: "QcStations");

            migrationBuilder.DropColumn(
                name: "ApiKeyRotatedAt",
                table: "QcStations");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "QcStations");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "QcStations");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "QcStations");

            migrationBuilder.DropColumn(
                name: "LastSeenAt",
                table: "QcStations");

            migrationBuilder.DropColumn(
                name: "LastSeenIp",
                table: "QcStations");

            migrationBuilder.DropColumn(
                name: "LastSyncAt",
                table: "QcStations");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "QcStations");

            migrationBuilder.DropColumn(
                name: "StationName",
                table: "QcStations");

            migrationBuilder.DropColumn(
                name: "UpdatedAt",
                table: "QcStations");

            migrationBuilder.DropColumn(
                name: "WarehouseCode",
                table: "QcStations");
        }
    }
}
