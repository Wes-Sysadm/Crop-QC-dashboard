using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFieldSampleDeletionAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FieldSampleDeletionAudits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: MigrationProviderTypes.StoreType(migrationBuilder, "uniqueidentifier", "uuid"), nullable: false),
                    OperationId = table.Column<Guid>(type: MigrationProviderTypes.StoreType(migrationBuilder, "uniqueidentifier", "uuid"), nullable: false),
                    DeletedFieldSampleId = table.Column<long>(type: "bigint", nullable: false),
                    IdentifyingFieldsJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), nullable: false),
                    DependencyCountsJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), nullable: false),
                    DeletedByEmail = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(320)", "character varying(320)"), maxLength: 320, nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    DeletedAtPacific = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    Reason = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: false),
                    BackupRunId = table.Column<long>(type: "bigint", nullable: false),
                    Result = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FieldSampleDeletionAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FieldSampleDeletionAudits_BackupRunRecords_BackupRunId",
                        column: x => x.BackupRunId,
                        principalTable: "BackupRunRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FieldSampleDeletionAudits_BackupRunId",
                table: "FieldSampleDeletionAudits",
                column: "BackupRunId");

            migrationBuilder.CreateIndex(
                name: "IX_FieldSampleDeletionAudits_DeletedFieldSampleId_DeletedAt",
                table: "FieldSampleDeletionAudits",
                columns: new[] { "DeletedFieldSampleId", "DeletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FieldSampleDeletionAudits_OperationId",
                table: "FieldSampleDeletionAudits",
                column: "OperationId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FieldSampleDeletionAudits");
        }
    }
}
