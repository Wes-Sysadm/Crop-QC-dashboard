using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddBackupNotificationsAndReceiptPurgeAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FailureStage",
                table: "BackupRunRecords",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"),
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IncompleteObjectCreated",
                table: "BackupRunRecords",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"),
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LeaseReleasedAt",
                table: "BackupRunRecords",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"),
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RetentionProcessedAt",
                table: "BackupRunRecords",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"),
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScheduledPacificDate",
                table: "BackupRunRecords",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(10)", "character varying(10)"),
                maxLength: 10,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BackupNightlyRunGuards",
                columns: table => new
                {
                    PacificDate = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(10)", "character varying(10)"), maxLength: 10, nullable: false),
                    BackupRunId = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true),
                    Result = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupNightlyRunGuards", x => x.PacificDate);
                });

            migrationBuilder.CreateTable(
                name: "BackupNotificationRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BackupRunId = table.Column<long>(type: "bigint", nullable: false),
                    NotificationType = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(20)", "character varying(20)"), maxLength: 20, nullable: false),
                    Recipient = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(320)", "character varying(320)"), maxLength: 320, nullable: false),
                    Status = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(20)", "character varying(20)"), maxLength: 20, nullable: false),
                    AttemptCount = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    LastAttemptedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true),
                    NextAttemptAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true),
                    MessageId = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(500)", "character varying(500)"), maxLength: 500, nullable: true),
                    ErrorSummary = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupNotificationRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BackupNotificationRecords_BackupRunRecords_BackupRunId",
                        column: x => x.BackupRunId,
                        principalTable: "BackupRunRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReceiptDeletionAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: MigrationProviderTypes.StoreType(migrationBuilder, "uniqueidentifier", "uuid"), nullable: false),
                    OperationId = table.Column<Guid>(type: MigrationProviderTypes.StoreType(migrationBuilder, "uniqueidentifier", "uuid"), nullable: false),
                    DeletedReceiptId = table.Column<long>(type: "bigint", nullable: false),
                    ReceiptNumber = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    CropYear = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    IdentifyingFieldsJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), nullable: false),
                    DependencyCountsJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), nullable: false),
                    DeletedByEmail = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(320)", "character varying(320)"), maxLength: 320, nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    Reason = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: false),
                    BackupRunId = table.Column<long>(type: "bigint", nullable: true),
                    Result = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptDeletionAudits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ReceiptPurgeOperations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: MigrationProviderTypes.StoreType(migrationBuilder, "uniqueidentifier", "uuid"), nullable: false),
                    TargetCropYear = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    BackupRunId = table.Column<long>(type: "bigint", nullable: false),
                    RequestedByEmail = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(320)", "character varying(320)"), maxLength: 320, nullable: false),
                    Reason = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true),
                    PreflightJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), nullable: false),
                    PreservationBaselineJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), nullable: false),
                    DeletedCountsJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), nullable: true),
                    ErrorSummary = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(2000)", "character varying(2000)"), maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReceiptPurgeOperations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackupRunRecords_ScheduledPacificDate",
                table: "BackupRunRecords",
                column: "ScheduledPacificDate");

            migrationBuilder.CreateIndex(
                name: "IX_BackupNightlyRunGuards_BackupRunId",
                table: "BackupNightlyRunGuards",
                column: "BackupRunId");

            migrationBuilder.CreateIndex(
                name: "IX_BackupNotificationRecords_BackupRunId_NotificationType",
                table: "BackupNotificationRecords",
                columns: new[] { "BackupRunId", "NotificationType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BackupNotificationRecords_Status_NextAttemptAt",
                table: "BackupNotificationRecords",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptDeletionAudits_CropYear_DeletedAt",
                table: "ReceiptDeletionAudits",
                columns: new[] { "CropYear", "DeletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptDeletionAudits_DeletedReceiptId",
                table: "ReceiptDeletionAudits",
                column: "DeletedReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptDeletionAudits_OperationId",
                table: "ReceiptDeletionAudits",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptPurgeOperations_BackupRunId",
                table: "ReceiptPurgeOperations",
                column: "BackupRunId");

            migrationBuilder.CreateIndex(
                name: "IX_ReceiptPurgeOperations_TargetCropYear_StartedAt",
                table: "ReceiptPurgeOperations",
                columns: new[] { "TargetCropYear", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackupNightlyRunGuards");

            migrationBuilder.DropTable(
                name: "BackupNotificationRecords");

            migrationBuilder.DropTable(
                name: "ReceiptDeletionAudits");

            migrationBuilder.DropTable(
                name: "ReceiptPurgeOperations");

            migrationBuilder.DropIndex(
                name: "IX_BackupRunRecords_ScheduledPacificDate",
                table: "BackupRunRecords");

            migrationBuilder.DropColumn(
                name: "FailureStage",
                table: "BackupRunRecords");

            migrationBuilder.DropColumn(
                name: "IncompleteObjectCreated",
                table: "BackupRunRecords");

            migrationBuilder.DropColumn(
                name: "LeaseReleasedAt",
                table: "BackupRunRecords");

            migrationBuilder.DropColumn(
                name: "RetentionProcessedAt",
                table: "BackupRunRecords");

            migrationBuilder.DropColumn(
                name: "ScheduledPacificDate",
                table: "BackupRunRecords");
        }
    }
}
