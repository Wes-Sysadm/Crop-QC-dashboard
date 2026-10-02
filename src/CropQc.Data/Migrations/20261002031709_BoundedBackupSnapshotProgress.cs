using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class BoundedBackupSnapshotProgress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "BytesProcessed",
                table: "BackupRunRecords",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CurrentStage",
                table: "BackupRunRecords",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"),
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FrozenObjectCount",
                table: "BackupRunRecords",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "HeartbeatAt",
                table: "BackupRunRecords",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"),
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ObjectsCompleted",
                table: "BackupRunRecords",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SnapshotCapturedAt",
                table: "BackupRunRecords",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"),
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SnapshotRevision",
                table: "BackupRunRecords",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"),
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WorkerId",
                table: "BackupRunRecords",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "uniqueidentifier", "uuid"),
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MigrationProviderTypes.Sql(migrationBuilder,
                "IF EXISTS (SELECT 1 FROM [BackupRunRecords] WHERE [WorkerId] IS NOT NULL) THROW 51000, 'Backup worker evidence exists; preserve additive schema.', 1;",
                "DO $$ BEGIN IF EXISTS (SELECT 1 FROM \"BackupRunRecords\" WHERE \"WorkerId\" IS NOT NULL) THEN RAISE EXCEPTION 'Backup worker evidence exists; preserve additive schema.'; END IF; END $$;"));
            migrationBuilder.DropColumn(
                name: "BytesProcessed",
                table: "BackupRunRecords");

            migrationBuilder.DropColumn(
                name: "CurrentStage",
                table: "BackupRunRecords");

            migrationBuilder.DropColumn(
                name: "FrozenObjectCount",
                table: "BackupRunRecords");

            migrationBuilder.DropColumn(
                name: "HeartbeatAt",
                table: "BackupRunRecords");

            migrationBuilder.DropColumn(
                name: "ObjectsCompleted",
                table: "BackupRunRecords");

            migrationBuilder.DropColumn(
                name: "SnapshotCapturedAt",
                table: "BackupRunRecords");

            migrationBuilder.DropColumn(
                name: "SnapshotRevision",
                table: "BackupRunRecords");

            migrationBuilder.DropColumn(
                name: "WorkerId",
                table: "BackupRunRecords");
        }
    }
}
