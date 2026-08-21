using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRoomSealing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSealed",
                table: "Rooms",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"),
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "SealedAt",
                table: "Rooms",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"),
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SealedByUserId",
                table: "Rooms",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RoomSealEvents",
                columns: table => new
                {
                    Id = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoomId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    Action = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(20)", "character varying(20)"), maxLength: 20, nullable: false),
                    ChangedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    ChangedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    WarehouseCodeSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"), maxLength: 25, nullable: false),
                    RoomCodeSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(150)", "character varying(150)"), maxLength: 150, nullable: false),
                    Note = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(500)", "character varying(500)"), maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoomSealEvents", x => x.Id);
                    table.CheckConstraint("CK_RoomSealEvents_Action", MigrationProviderTypes.Sql(migrationBuilder, "[Action] IN ('Seal', 'Unseal')", "\"Action\" IN ('Seal', 'Unseal')"));
                    table.ForeignKey(
                        name: "FK_RoomSealEvents_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RoomSealEvents_Users_ChangedByUserId",
                        column: x => x.ChangedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_SealedByUserId",
                table: "Rooms",
                column: "SealedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomSealEvents_ChangedByUserId",
                table: "RoomSealEvents",
                column: "ChangedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_RoomSealEvents_RoomId_ChangedAt",
                table: "RoomSealEvents",
                columns: new[] { "RoomId", "ChangedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_Rooms_Users_SealedByUserId",
                table: "Rooms",
                column: "SealedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Rooms_Users_SealedByUserId",
                table: "Rooms");

            migrationBuilder.DropTable(
                name: "RoomSealEvents");

            migrationBuilder.DropIndex(
                name: "IX_Rooms_SealedByUserId",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "IsSealed",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "SealedAt",
                table: "Rooms");

            migrationBuilder.DropColumn(
                name: "SealedByUserId",
                table: "Rooms");
        }
    }
}
