using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedMvp1Rooms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "Rooms",
                columns: new[] { "Id", "CapacityBins", "Code", "IsActive", "Name", "WarehouseId" },
                values: new object[,]
                {
                    { 1, 0, "WP-4", true, "Room 4", 4 },
                    { 2, 0, "WP-5", true, "Room 5", 4 },
                    { 3, 0, "WP-6", true, "Room 6", 4 },
                    { 4, 0, "WP-7", true, "Room 7", 4 },
                    { 5, 0, "WP-8", true, "Room 8", 4 },
                    { 6, 0, "LAMB-13", true, "Lamb Street 13", 1 },
                    { 7, 0, "LAMB-14", true, "Lamb Street 14", 1 },
                    { 8, 0, "LAMB-15", true, "Lamb Street 15", 1 },
                    { 9, 0, "LAMB-16", true, "Lamb Street 16", 1 },
                    { 10, 0, "LAMB-17", true, "Lamb Street 17", 1 },
                    { 11, 0, "EVANS-1", true, "Evans Street 1", 1 },
                    { 12, 0, "EVANS-2", true, "Evans Street 2", 1 },
                    { 13, 0, "EVANS-3", true, "Evans Street 3", 1 },
                    { 14, 0, "EVANS-4", true, "Evans Street 4", 1 },
                    { 15, 0, "EVANS-5", true, "Evans Street 5", 1 },
                    { 16, 0, "EVANS-6", true, "Evans Street 6", 1 },
                    { 17, 0, "EVANS-7", true, "Evans Street 7", 1 },
                    { 18, 0, "EVANS-8", true, "Evans Street 8", 1 },
                    { 19, 0, "EVANS-9", true, "Evans Street 9", 1 },
                    { 20, 0, "EVANS-10", true, "Evans Street 10", 1 },
                    { 21, 0, "EVANS-11", true, "Evans Street 11", 1 },
                    { 22, 0, "EVANS-12", true, "Evans Street 12", 1 },
                    { 23, 0, "EVANS-BKT", true, "Evans Street BKT", 1 },
                    { 24, 0, "EVANS-BACKSIDE", true, "Evans Street Backside", 1 },
                    { 25, 0, "EVANS-HALLWAY1", true, "Evans Street Hallway 1", 1 },
                    { 26, 0, "EVANS-HALLWAY2", true, "Evans Street Hallway 2", 1 },
                    { 27, 0, "BM-1", true, "Bluemountain 1", 1 },
                    { 28, 0, "BM-2", true, "Bluemountain 2", 1 },
                    { 29, 0, "BM-3", true, "Bluemountain 3", 1 },
                    { 30, 0, "BM-4", true, "Bluemountain 4", 1 },
                    { 31, 0, "BM-5", true, "Bluemountain 5", 1 },
                    { 32, 0, "BM-6", true, "Bluemountain 6", 1 },
                    { 33, 0, "DH-1", true, "Room 1", 2 },
                    { 34, 0, "DH-2", true, "Room 2", 2 },
                    { 35, 0, "DH-3", true, "Room 3", 2 },
                    { 36, 0, "DH-4", true, "Room 4", 2 },
                    { 37, 0, "DH-5", true, "Room 5", 2 },
                    { 38, 0, "DH-6", true, "Room 6", 2 },
                    { 39, 0, "DH-7", true, "Room 7", 2 },
                    { 40, 0, "DH-8", true, "Room 8", 2 },
                    { 41, 0, "DH-9", true, "Room 9", 2 },
                    { 42, 0, "DH-10", true, "Room 10", 2 },
                    { 43, 0, "DH-11", true, "Room 11", 2 },
                    { 44, 0, "DH-12", true, "Room 12", 2 },
                    { 45, 0, "DH-13", true, "Room 13", 2 },
                    { 46, 0, "DH-14", true, "Room 14", 2 },
                    { 47, 0, "DH-15", true, "Room 15", 2 },
                    { 48, 0, "DH-16", true, "Room 16", 2 },
                    { 49, 0, "DH-17", true, "Room 17", 2 },
                    { 50, 0, "DH-18", true, "Room 18", 2 },
                    { 51, 0, "DH-19", true, "Room 19", 2 },
                    { 52, 0, "DH-20", true, "Room 20", 2 },
                    { 53, 0, "DH-21", true, "Room 21", 2 },
                    { 54, 0, "DH-22", true, "Room 22", 2 },
                    { 55, 0, "MCD-3", true, "Room 3", 3 },
                    { 56, 0, "MCD-4", true, "Room 4", 3 },
                    { 57, 0, "MCD-5", true, "Room 5", 3 },
                    { 58, 0, "MCD-6", true, "Room 6", 3 },
                    { 59, 0, "MCD-7", true, "Room 7", 3 },
                    { 60, 0, "MCD-8", true, "Room 8", 3 },
                    { 61, 0, "MCD-9", true, "Room 9", 3 },
                    { 62, 0, "MCD-10", true, "Room 10", 3 },
                    { 63, 0, "MCD-11", true, "Room 11", 3 },
                    { 64, 0, "MCD-12", true, "Room 12", 3 },
                    { 65, 0, "MCD-13", true, "Room 13", 3 },
                    { 66, 0, "MCD-14", true, "Room 14", 3 },
                    { 67, 0, "MCD-15", true, "Room 15", 3 },
                    { 68, 0, "MCD-16", true, "Room 16", 3 }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 3);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 4);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 6);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 7);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 8);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 9);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 10);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 11);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 12);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 13);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 14);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 15);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 16);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 17);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 18);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 19);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 20);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 21);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 22);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 23);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 24);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 25);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 26);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 27);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 28);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 29);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 30);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 31);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 32);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 33);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 34);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 35);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 36);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 37);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 38);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 39);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 40);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 41);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 42);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 43);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 44);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 45);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 46);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 47);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 48);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 49);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 50);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 51);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 52);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 53);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 54);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 55);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 56);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 57);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 58);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 59);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 60);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 61);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 62);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 63);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 64);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 65);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 66);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 67);

            migrationBuilder.DeleteData(
                table: "Rooms",
                keyColumn: "Id",
                keyValue: 68);
        }
    }
}
