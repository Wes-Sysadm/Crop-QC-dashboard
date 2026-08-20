using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTreatmentReportAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RoomTreatmentApplicationAttachments",
                columns: table => new
                {
                    Id = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoomTreatmentApplicationId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false),
                    OperationKey = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: false),
                    FileName = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(255)", "character varying(255)"), maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: false),
                    FileSizeBytes = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false),
                    StorageProvider = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    DriveId = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"), maxLength: 200, nullable: true),
                    FileId = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"), maxLength: 200, nullable: false),
                    FolderId = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"), maxLength: 200, nullable: true),
                    StoragePath = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    CreatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    IsDeleted = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    DeletedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true),
                    DeletedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    DeleteReason = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomTreatmentApplicationAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoomTreatmentApplicationAttachments_RoomTreatmentApplications_RoomTreatmentApplicationId",
                        column: x => x.RoomTreatmentApplicationId,
                        principalTable: "RoomTreatmentApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoomTreatmentApplicationAttachments_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoomTreatmentApplicationAttachments_Users_DeletedByUserId",
                        column: x => x.DeletedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoomTreatmentApplicationAttachments_CreatedByUserId",
                table: "RoomTreatmentApplicationAttachments",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomTreatmentApplicationAttachments_DeletedByUserId",
                table: "RoomTreatmentApplicationAttachments",
                column: "DeletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_TreatmentReportAttachments_Application_IsDeleted_CreatedAt",
                table: "RoomTreatmentApplicationAttachments",
                columns: new[] { "RoomTreatmentApplicationId", "IsDeleted", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_TreatmentReportAttachments_Application_OperationKey",
                table: "RoomTreatmentApplicationAttachments",
                columns: new[] { "RoomTreatmentApplicationId", "OperationKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoomTreatmentApplicationAttachments");
        }
    }
}
