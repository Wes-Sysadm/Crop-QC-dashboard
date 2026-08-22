using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRoomSealEffectiveTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_RoomSealEvents_Action",
                table: "RoomSealEvents");

            migrationBuilder.AlterColumn<string>(
                name: "Action",
                table: "RoomSealEvents",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(30)", "character varying(30)"),
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(20)", "character varying(20)"),
                oldMaxLength: 20);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "EffectiveAt",
                table: "RoomSealEvents",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"),
                nullable: true);

            migrationBuilder.Sql(MigrationProviderTypes.Sql(
                migrationBuilder,
                "UPDATE [RoomSealEvents] SET [EffectiveAt] = [ChangedAt] WHERE [EffectiveAt] IS NULL;",
                "UPDATE \"RoomSealEvents\" SET \"EffectiveAt\" = \"ChangedAt\" WHERE \"EffectiveAt\" IS NULL;"));

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "EffectiveAt",
                table: "RoomSealEvents",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"),
                nullable: false,
                oldClrType: typeof(DateTimeOffset),
                oldType: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"),
                oldNullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PreviousEffectiveAt",
                table: "RoomSealEvents",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"),
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SealRecordedAt",
                table: "Rooms",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"),
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_RoomSealEvents_Action",
                table: "RoomSealEvents",
                sql: MigrationProviderTypes.Sql(migrationBuilder,
                    "[Action] IN ('Seal', 'SealScheduled', 'ScheduleChanged', 'ScheduleCanceled', 'Unseal')",
                    "\"Action\" IN ('Seal', 'SealScheduled', 'ScheduleChanged', 'ScheduleCanceled', 'Unseal')"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_RoomSealEvents_Action",
                table: "RoomSealEvents");

            migrationBuilder.DropColumn(
                name: "EffectiveAt",
                table: "RoomSealEvents");

            migrationBuilder.DropColumn(
                name: "PreviousEffectiveAt",
                table: "RoomSealEvents");

            migrationBuilder.DropColumn(
                name: "SealRecordedAt",
                table: "Rooms");

            migrationBuilder.AlterColumn<string>(
                name: "Action",
                table: "RoomSealEvents",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(20)", "character varying(20)"),
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(30)", "character varying(30)"),
                oldMaxLength: 30);

            migrationBuilder.AddCheckConstraint(
                name: "CK_RoomSealEvents_Action",
                table: "RoomSealEvents",
                sql: MigrationProviderTypes.Sql(migrationBuilder,
                    "[Action] IN ('Seal', 'Unseal')",
                    "\"Action\" IN ('Seal', 'Unseal')"));
        }
    }
}
