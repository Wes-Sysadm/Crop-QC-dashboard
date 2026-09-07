using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHarvestWatchDeployments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HarvestWatchDeployments",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoomId = table.Column<int>(type: "int", nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    HarvestWatchCode = table.Column<string>(type: "nvarchar(5)", maxLength: 5, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DeployedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    DeployedByUserId = table.Column<int>(type: "int", nullable: false),
                    DeployerEmailSnapshot = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    WarehouseCodeSnapshot = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    RoomCodeSnapshot = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    VarietySnapshot = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CorrelationToken = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    VerificationEmailSentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VerificationEmailMessageId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    VerificationEmailError = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    VerifiedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    VerifiedByEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    LastReplyMessageId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ErrorNotificationSentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ErrorNotificationMessageId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    RemovedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    RemovedByUserId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HarvestWatchDeployments", x => x.Id);
                    table.CheckConstraint("CK_HarvestWatchDeployments_Code", "[HarvestWatchCode] NOT LIKE '%[^0-9]%' AND LEN([HarvestWatchCode]) = 5");
                    table.ForeignKey(
                        name: "FK_HarvestWatchDeployments_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HarvestWatchDeployments_Users_DeployedByUserId",
                        column: x => x.DeployedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HarvestWatchDeployments_Users_RemovedByUserId",
                        column: x => x.RemovedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HarvestWatchDeployments_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HarvestWatchMailboxCursors",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LastPolledAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HarvestWatchMailboxCursors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HarvestWatchInboundMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GmailMessageId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    HarvestWatchDeploymentId = table.Column<long>(type: "bigint", nullable: true),
                    SenderEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    Subject = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    BodyExcerpt = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Outcome = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HarvestWatchInboundMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HarvestWatchInboundMessages_HarvestWatchDeployments_HarvestWatchDeploymentId",
                        column: x => x.HarvestWatchDeploymentId,
                        principalTable: "HarvestWatchDeployments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "HarvestWatchStatusHistories",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HarvestWatchDeploymentId = table.Column<long>(type: "bigint", nullable: false),
                    PreviousStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    NewStatus = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Source = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    InboundMessageId = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ChangedByEmail = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HarvestWatchStatusHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HarvestWatchStatusHistories_HarvestWatchDeployments_HarvestWatchDeploymentId",
                        column: x => x.HarvestWatchDeploymentId,
                        principalTable: "HarvestWatchDeployments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HarvestWatchDeployments_CorrelationToken",
                table: "HarvestWatchDeployments",
                column: "CorrelationToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HarvestWatchDeployments_DeployedByUserId",
                table: "HarvestWatchDeployments",
                column: "DeployedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HarvestWatchDeployments_HarvestWatchCode_IsActive",
                table: "HarvestWatchDeployments",
                columns: new[] { "HarvestWatchCode", "IsActive" },
                unique: true,
                filter: "[IsActive] = 1");

            migrationBuilder.CreateIndex(
                name: "IX_HarvestWatchDeployments_RemovedByUserId",
                table: "HarvestWatchDeployments",
                column: "RemovedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HarvestWatchDeployments_RoomId_IsActive",
                table: "HarvestWatchDeployments",
                columns: new[] { "RoomId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_HarvestWatchDeployments_WarehouseId",
                table: "HarvestWatchDeployments",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_HarvestWatchInboundMessages_GmailMessageId",
                table: "HarvestWatchInboundMessages",
                column: "GmailMessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HarvestWatchInboundMessages_HarvestWatchDeploymentId",
                table: "HarvestWatchInboundMessages",
                column: "HarvestWatchDeploymentId");

            migrationBuilder.CreateIndex(
                name: "IX_HarvestWatchStatusHistories_HarvestWatchDeploymentId_ChangedAt",
                table: "HarvestWatchStatusHistories",
                columns: new[] { "HarvestWatchDeploymentId", "ChangedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HarvestWatchInboundMessages");

            migrationBuilder.DropTable(
                name: "HarvestWatchMailboxCursors");

            migrationBuilder.DropTable(
                name: "HarvestWatchStatusHistories");

            migrationBuilder.DropTable(
                name: "HarvestWatchDeployments");
        }
    }
}
