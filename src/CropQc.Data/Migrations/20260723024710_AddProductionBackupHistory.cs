using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductionBackupHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BackupOperationLeases",
                columns: table => new
                {
                    Id = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    LeaseId = table.Column<Guid>(type: MigrationProviderTypes.StoreType(migrationBuilder, "uniqueidentifier", "uuid"), nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupOperationLeases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BackupRunRecords",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BackupType = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(30)", "character varying(30)"), maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(30)", "character varying(30)"), maxLength: 30, nullable: false),
                    EnvironmentName = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: false),
                    DatabaseProvider = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: false),
                    DeployedCommit = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(64)", "character varying(64)"), maxLength: 64, nullable: true),
                    RequestedBy = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(320)", "character varying(320)"), maxLength: 320, nullable: true),
                    RetentionCategory = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(30)", "character varying(30)"), maxLength: 30, nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true),
                    DurationMilliseconds = table.Column<long>(type: "bigint", nullable: true),
                    PackageFileName = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(260)", "character varying(260)"), maxLength: 260, nullable: true),
                    PackageStorageKey = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(500)", "character varying(500)"), maxLength: 500, nullable: true),
                    PackageWebUrl = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(2000)", "character varying(2000)"), maxLength: 2000, nullable: true),
                    ManifestFileName = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(260)", "character varying(260)"), maxLength: 260, nullable: true),
                    ManifestStorageKey = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(500)", "character varying(500)"), maxLength: 500, nullable: true),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    Sha256 = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(64)", "character varying(64)"), maxLength: 64, nullable: true),
                    VerifiedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true),
                    ErrorSummary = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(2000)", "character varying(2000)"), maxLength: 2000, nullable: true),
                    PrunedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackupRunRecords", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "BackupOperationLeases",
                columns: new[] { "Id", "ExpiresAt", "LeaseId" },
                values: new object[] { 1, null, null });

            migrationBuilder.CreateIndex(
                name: "IX_BackupRunRecords_RetentionCategory_StartedAt",
                table: "BackupRunRecords",
                columns: new[] { "RetentionCategory", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BackupRunRecords_Status_StartedAt",
                table: "BackupRunRecords",
                columns: new[] { "Status", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackupOperationLeases");

            migrationBuilder.DropTable(
                name: "BackupRunRecords");
        }
    }
}
