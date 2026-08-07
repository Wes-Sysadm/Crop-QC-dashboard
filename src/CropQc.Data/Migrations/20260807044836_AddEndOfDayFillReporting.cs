using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEndOfDayFillReporting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EndOfDayFillReportGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(150)", "character varying(150)"), maxLength: 150, nullable: false),
                    Facility = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(10)", "character varying(10)"), maxLength: 10, nullable: false),
                    IsActive = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EndOfDayFillReportGroups", x => x.Id);
                    table.CheckConstraint("CK_EndOfDayFillReportGroups_Facility", "\"Facility\" IN ('WP', 'EBS')");
                });

            migrationBuilder.CreateTable(
                name: "EndOfDayFillReportRecipients",
                columns: table => new
                {
                    Id = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    EmailAddress = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(320)", "character varying(320)"), maxLength: 320, nullable: false),
                    NormalizedEmailAddress = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(320)", "character varying(320)"), maxLength: 320, nullable: false),
                    IsActive = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    SortOrder = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    UpdatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EndOfDayFillReportRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EndOfDayFillReportRecipients_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "EndOfDayFillReportGroupRooms",
                columns: table => new
                {
                    Id = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReportGroupId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    RoomId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    CreatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EndOfDayFillReportGroupRooms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EndOfDayFillReportGroupRooms_EndOfDayFillReportGroups_ReportGroupId",
                        column: x => x.ReportGroupId,
                        principalTable: "EndOfDayFillReportGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EndOfDayFillReportGroupRooms_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EndOfDayFillReportGroupRooms_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "EndOfDayFillReportSends",
                columns: table => new
                {
                    Id = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ReportGroupId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    ReportGroupName = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(150)", "character varying(150)"), maxLength: 150, nullable: false),
                    Facility = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(10)", "character varying(10)"), maxLength: 10, nullable: false),
                    PacificReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RevisionNumber = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    SenderUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    SenderEmail = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(320)", "character varying(320)"), maxLength: 320, nullable: false),
                    SenderDisplayName = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"), maxLength: 200, nullable: false),
                    RecipientsJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), maxLength: 10000, nullable: false),
                    PhysicalCountConfirmed = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    SnapshotHash = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(64)", "character varying(64)"), maxLength: 64, nullable: false),
                    SnapshotJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), maxLength: 500000, nullable: false),
                    SuccessRevisionKey = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"), maxLength: 200, nullable: true),
                    SuccessSnapshotKey = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(250)", "character varying(250)"), maxLength: 250, nullable: true),
                    Subject = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(500)", "character varying(500)"), maxLength: 500, nullable: false),
                    HtmlBody = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), maxLength: 1000000, nullable: false),
                    TextBody = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), maxLength: 500000, nullable: false),
                    Status = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"), maxLength: 25, nullable: false),
                    FailureReason = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(2000)", "character varying(2000)"), maxLength: 2000, nullable: true),
                    GmailMessageId = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(500)", "character varying(500)"), maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    AttemptedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EndOfDayFillReportSends", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EndOfDayFillReportSends_EndOfDayFillReportGroups_ReportGroupId",
                        column: x => x.ReportGroupId,
                        principalTable: "EndOfDayFillReportGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_EndOfDayFillReportSends_Users_SenderUserId",
                        column: x => x.SenderUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "EndOfDayFillUserGroupAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    ReportGroupId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    CreatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EndOfDayFillUserGroupAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EndOfDayFillUserGroupAssignments_EndOfDayFillReportGroups_ReportGroupId",
                        column: x => x.ReportGroupId,
                        principalTable: "EndOfDayFillReportGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EndOfDayFillUserGroupAssignments_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_EndOfDayFillUserGroupAssignments_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EndOfDayFillSendReservations",
                columns: table => new
                {
                    ReportGroupId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    PacificReportDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RevisionNumber = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    SnapshotHash = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(64)", "character varying(64)"), maxLength: 64, nullable: false),
                    SendAttemptId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EndOfDayFillSendReservations", x => x.ReportGroupId);
                    table.ForeignKey(
                        name: "FK_EndOfDayFillSendReservations_EndOfDayFillReportGroups_ReportGroupId",
                        column: x => x.ReportGroupId,
                        principalTable: "EndOfDayFillReportGroups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EndOfDayFillSendReservations_EndOfDayFillReportSends_SendAttemptId",
                        column: x => x.SendAttemptId,
                        principalTable: "EndOfDayFillReportSends",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.Sql(MigrationProviderTypes.Sql(
                migrationBuilder,
                """
                SET IDENTITY_INSERT [EndOfDayFillReportGroups] ON;
                INSERT INTO [EndOfDayFillReportGroups] ([Id], [Name], [Facility], [IsActive], [CreatedAt], [UpdatedAt]) VALUES
                    (1, 'WP End of Day Fill', 'WP', 1, '1970-01-01T00:00:00+00:00', '1970-01-01T00:00:00+00:00'),
                    (2, 'EBS End of Day Fill', 'EBS', 1, '1970-01-01T00:00:00+00:00', '1970-01-01T00:00:00+00:00');
                SET IDENTITY_INSERT [EndOfDayFillReportGroups] OFF;
                SET IDENTITY_INSERT [EndOfDayFillReportRecipients] ON;
                INSERT INTO [EndOfDayFillReportRecipients] ([Id], [EmailAddress], [NormalizedEmailAddress], [IsActive], [SortOrder], [CreatedAt], [UpdatedAt], [UpdatedByUserId]) VALUES
                    (1, 'wes@fruitandland.com', 'WES@FRUITANDLAND.COM', 1, 10, '1970-01-01T00:00:00+00:00', '1970-01-01T00:00:00+00:00', NULL),
                    (2, 'jorge@wp-packing.com', 'JORGE@WP-PACKING.COM', 1, 20, '1970-01-01T00:00:00+00:00', '1970-01-01T00:00:00+00:00', NULL),
                    (3, 'rob@earlbrownandsons.com', 'ROB@EARLBROWNANDSONS.COM', 1, 30, '1970-01-01T00:00:00+00:00', '1970-01-01T00:00:00+00:00', NULL);
                SET IDENTITY_INSERT [EndOfDayFillReportRecipients] OFF;
                INSERT INTO [EndOfDayFillReportGroupRooms] ([ReportGroupId], [RoomId], [CreatedAt], [CreatedByUserId])
                SELECT 1, r.[Id], SYSUTCDATETIME(), NULL
                FROM [Rooms] r INNER JOIN [Warehouses] w ON w.[Id] = r.[WarehouseId]
                WHERE r.[IsActive] = 1 AND LOWER(LTRIM(RTRIM(w.[Code]))) IN ('dh', 'mcdougall');
                INSERT INTO [EndOfDayFillReportGroupRooms] ([ReportGroupId], [RoomId], [CreatedAt], [CreatedByUserId])
                SELECT 2, r.[Id], SYSUTCDATETIME(), NULL
                FROM [Rooms] r INNER JOIN [Warehouses] w ON w.[Id] = r.[WarehouseId]
                WHERE r.[IsActive] = 1 AND LOWER(LTRIM(RTRIM(w.[Code]))) = 'ebs';
                INSERT INTO [EndOfDayFillUserGroupAssignments] ([UserId], [ReportGroupId], [CreatedAt], [CreatedByUserId])
                SELECT [Id], 1, SYSUTCDATETIME(), NULL FROM [Users] WHERE LOWER(LTRIM(RTRIM([Email]))) IN ('jorge@wp-packing.com', 'wes@fruitandland.com');
                INSERT INTO [EndOfDayFillUserGroupAssignments] ([UserId], [ReportGroupId], [CreatedAt], [CreatedByUserId])
                SELECT [Id], 2, SYSUTCDATETIME(), NULL FROM [Users] WHERE LOWER(LTRIM(RTRIM([Email]))) IN ('rob@earlbrownandsons.com', 'wes@fruitandland.com');
                """,
                """
                INSERT INTO "EndOfDayFillReportGroups" ("Id", "Name", "Facility", "IsActive", "CreatedAt", "UpdatedAt") VALUES
                    (1, 'WP End of Day Fill', 'WP', TRUE, TIMESTAMPTZ '1970-01-01 00:00:00+00', TIMESTAMPTZ '1970-01-01 00:00:00+00'),
                    (2, 'EBS End of Day Fill', 'EBS', TRUE, TIMESTAMPTZ '1970-01-01 00:00:00+00', TIMESTAMPTZ '1970-01-01 00:00:00+00');
                INSERT INTO "EndOfDayFillReportRecipients" ("Id", "EmailAddress", "NormalizedEmailAddress", "IsActive", "SortOrder", "CreatedAt", "UpdatedAt", "UpdatedByUserId") VALUES
                    (1, 'wes@fruitandland.com', 'WES@FRUITANDLAND.COM', TRUE, 10, TIMESTAMPTZ '1970-01-01 00:00:00+00', TIMESTAMPTZ '1970-01-01 00:00:00+00', NULL),
                    (2, 'jorge@wp-packing.com', 'JORGE@WP-PACKING.COM', TRUE, 20, TIMESTAMPTZ '1970-01-01 00:00:00+00', TIMESTAMPTZ '1970-01-01 00:00:00+00', NULL),
                    (3, 'rob@earlbrownandsons.com', 'ROB@EARLBROWNANDSONS.COM', TRUE, 30, TIMESTAMPTZ '1970-01-01 00:00:00+00', TIMESTAMPTZ '1970-01-01 00:00:00+00', NULL);
                INSERT INTO "EndOfDayFillReportGroupRooms" ("ReportGroupId", "RoomId", "CreatedAt", "CreatedByUserId")
                SELECT 1, r."Id", CURRENT_TIMESTAMP, NULL
                FROM "Rooms" r INNER JOIN "Warehouses" w ON w."Id" = r."WarehouseId"
                WHERE r."IsActive" AND lower(btrim(w."Code")) IN ('dh', 'mcdougall');
                INSERT INTO "EndOfDayFillReportGroupRooms" ("ReportGroupId", "RoomId", "CreatedAt", "CreatedByUserId")
                SELECT 2, r."Id", CURRENT_TIMESTAMP, NULL
                FROM "Rooms" r INNER JOIN "Warehouses" w ON w."Id" = r."WarehouseId"
                WHERE r."IsActive" AND lower(btrim(w."Code")) = 'ebs';
                INSERT INTO "EndOfDayFillUserGroupAssignments" ("UserId", "ReportGroupId", "CreatedAt", "CreatedByUserId")
                SELECT "Id", 1, CURRENT_TIMESTAMP, NULL FROM "Users" WHERE lower(btrim("Email")) IN ('jorge@wp-packing.com', 'wes@fruitandland.com');
                INSERT INTO "EndOfDayFillUserGroupAssignments" ("UserId", "ReportGroupId", "CreatedAt", "CreatedByUserId")
                SELECT "Id", 2, CURRENT_TIMESTAMP, NULL FROM "Users" WHERE lower(btrim("Email")) IN ('rob@earlbrownandsons.com', 'wes@fruitandland.com');
                ALTER SEQUENCE "EndOfDayFillReportGroups_Id_seq" RESTART WITH 3;
                ALTER SEQUENCE "EndOfDayFillReportRecipients_Id_seq" RESTART WITH 4;
                """));

            migrationBuilder.CreateIndex(
                name: "IX_EndOfDayFillReportGroupRooms_CreatedByUserId",
                table: "EndOfDayFillReportGroupRooms",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EndOfDayFillReportGroupRooms_ReportGroupId_RoomId",
                table: "EndOfDayFillReportGroupRooms",
                columns: new[] { "ReportGroupId", "RoomId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EndOfDayFillReportGroupRooms_RoomId",
                table: "EndOfDayFillReportGroupRooms",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_EndOfDayFillReportGroups_Name",
                table: "EndOfDayFillReportGroups",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EndOfDayFillReportRecipients_NormalizedEmailAddress",
                table: "EndOfDayFillReportRecipients",
                column: "NormalizedEmailAddress",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EndOfDayFillReportRecipients_UpdatedByUserId",
                table: "EndOfDayFillReportRecipients",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EndOfDayFillReportSends_ReportGroupId_PacificReportDate_Status",
                table: "EndOfDayFillReportSends",
                columns: new[] { "ReportGroupId", "PacificReportDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_EndOfDayFillReportSends_SenderUserId",
                table: "EndOfDayFillReportSends",
                column: "SenderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EndOfDayFillReportSends_SuccessRevisionKey",
                table: "EndOfDayFillReportSends",
                column: "SuccessRevisionKey",
                unique: true,
                filter: MigrationProviderTypes.Sql(migrationBuilder, "[SuccessRevisionKey] IS NOT NULL", "\"SuccessRevisionKey\" IS NOT NULL"));

            migrationBuilder.CreateIndex(
                name: "IX_EndOfDayFillReportSends_SuccessSnapshotKey",
                table: "EndOfDayFillReportSends",
                column: "SuccessSnapshotKey");

            migrationBuilder.CreateIndex(
                name: "IX_EndOfDayFillSendReservations_SendAttemptId",
                table: "EndOfDayFillSendReservations",
                column: "SendAttemptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EndOfDayFillUserGroupAssignments_CreatedByUserId",
                table: "EndOfDayFillUserGroupAssignments",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_EndOfDayFillUserGroupAssignments_ReportGroupId",
                table: "EndOfDayFillUserGroupAssignments",
                column: "ReportGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_EndOfDayFillUserGroupAssignments_UserId_ReportGroupId",
                table: "EndOfDayFillUserGroupAssignments",
                columns: new[] { "UserId", "ReportGroupId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EndOfDayFillReportGroupRooms");

            migrationBuilder.DropTable(
                name: "EndOfDayFillReportRecipients");

            migrationBuilder.DropTable(
                name: "EndOfDayFillSendReservations");

            migrationBuilder.DropTable(
                name: "EndOfDayFillUserGroupAssignments");

            migrationBuilder.DropTable(
                name: "EndOfDayFillReportSends");

            migrationBuilder.DropTable(
                name: "EndOfDayFillReportGroups");
        }
    }
}
