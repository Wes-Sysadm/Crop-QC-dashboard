using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleBasedUserAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(MigrationProviderTypes.Sql(
                migrationBuilder,
                "IF EXISTS (SELECT 1 FROM [UserRoles] GROUP BY [UserId] HAVING COUNT(*) > 1) THROW 51000, 'Multiple UserRole assignments must be resolved before role-based access can be enabled.', 1;",
                "DO $guard$ BEGIN IF EXISTS (SELECT 1 FROM \"UserRoles\" GROUP BY \"UserId\" HAVING count(*) > 1) THEN RAISE EXCEPTION 'Multiple UserRole assignments must be resolved before role-based access can be enabled.'; END IF; END $guard$;"));

            migrationBuilder.DropIndex(
                name: "IX_Roles_Name",
                table: "Roles");

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Roles",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"),
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                table: "Roles",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"),
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "RolePageAccesses",
                columns: table => new
                {
                    Id = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoleId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    AreaKey = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: false),
                    AccessLevel = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"), maxLength: 25, nullable: false),
                    UpdatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePageAccesses", x => x.Id);
                    table.CheckConstraint("CK_RolePageAccesses_AccessLevel", "\"AccessLevel\" IN ('None', 'View', 'Create', 'Admin')");
                    table.ForeignKey(
                        name: "FK_RolePageAccesses_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RolePageAccesses_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.Sql(MigrationProviderTypes.Sql(
                migrationBuilder,
                "UPDATE [Roles] SET [NormalizedName] = UPPER(LTRIM(RTRIM([Name]))), [IsActive] = 1; UPDATE [Roles] SET [Description] = N'Broad operational management without security administration.' WHERE [Id] = 2; UPDATE [Roles] SET [Name] = N'QC Tech', [NormalizedName] = N'QC TECH' WHERE [Id] = 3; UPDATE [Roles] SET [Description] = N'Read-only operational visibility.' WHERE [Id] = 4;",
                "UPDATE \"Roles\" SET \"NormalizedName\" = upper(btrim(\"Name\")), \"IsActive\" = TRUE; UPDATE \"Roles\" SET \"Description\" = 'Broad operational management without security administration.' WHERE \"Id\" = 2; UPDATE \"Roles\" SET \"Name\" = 'QC Tech', \"NormalizedName\" = 'QC TECH' WHERE \"Id\" = 3; UPDATE \"Roles\" SET \"Description\" = 'Read-only operational visibility.' WHERE \"Id\" = 4;"));

            migrationBuilder.InsertData(
                table: "RolePageAccesses",
                columns: new[] { "Id", "AccessLevel", "AreaKey", "RoleId", "UpdatedAt", "UpdatedByUserId" },
                columnTypes: new[]
                {
                    MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                    MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"),
                    MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"),
                    MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                    MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"),
                    MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer")
                },
                values: new object[,]
                {
                    { 1, "Admin", "dashboard", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 2, "Admin", "daily-qc", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 3, "Admin", "field-samples", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 4, "Admin", "qc-reports", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 5, "Admin", "receipts", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 6, "Admin", "current-lots", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 7, "Admin", "bins-run", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 8, "Admin", "projection-planner", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 9, "Admin", "projection-outcome", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 10, "Admin", "actual-runs", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 11, "Admin", "packout-results", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 12, "Admin", "historical-inventory-cleanup", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 13, "Admin", "rooms", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 14, "Admin", "room-transactions", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 15, "Admin", "transfers", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 16, "Admin", "true-up", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 17, "Admin", "inventory", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 18, "Admin", "grower-lots", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 19, "Admin", "crop-year-review", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 20, "Admin", "master-data", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 21, "Admin", "users", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 22, "Admin", "permission-matrix", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 23, "Admin", "qc-stations", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 24, "Admin", "downloads", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 25, "Admin", "configuration", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 26, "Admin", "variety-colors", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 27, "Admin", "backups", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 28, "Admin", "orchard-recipients", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 29, "Admin", "orchard-managers", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 30, "Admin", "facilities", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 31, "Admin", "varieties", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 32, "Admin", "grades", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 33, "Admin", "defects", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 34, "Admin", "size-configuration", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 35, "Admin", "email-configuration", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 36, "Admin", "backup-history", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 37, "Admin", "audit-history", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 38, "Admin", "import-tools", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 39, "Admin", "export-tools", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 40, "Admin", "data-cleanup", 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 41, "View", "dashboard", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 42, "Admin", "daily-qc", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 43, "Admin", "field-samples", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 44, "Admin", "qc-reports", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 45, "Admin", "receipts", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 46, "Admin", "current-lots", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 47, "Admin", "bins-run", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 48, "Admin", "projection-planner", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 49, "Admin", "projection-outcome", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 50, "Admin", "actual-runs", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 51, "Admin", "packout-results", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 52, "None", "historical-inventory-cleanup", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 53, "Admin", "rooms", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 54, "Admin", "room-transactions", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 55, "Admin", "transfers", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 56, "Admin", "true-up", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 57, "Admin", "inventory", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 58, "Admin", "grower-lots", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 59, "None", "crop-year-review", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 60, "Admin", "master-data", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 61, "None", "users", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 62, "None", "permission-matrix", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 63, "Admin", "qc-stations", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 64, "View", "downloads", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 65, "None", "configuration", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 66, "Admin", "variety-colors", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 67, "None", "backups", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 68, "Admin", "orchard-recipients", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 69, "Admin", "orchard-managers", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 70, "Admin", "facilities", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 71, "Admin", "varieties", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 72, "Admin", "grades", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 73, "Admin", "defects", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 74, "Admin", "size-configuration", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 75, "None", "email-configuration", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 76, "None", "backup-history", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 77, "View", "audit-history", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 78, "Admin", "import-tools", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 79, "Admin", "export-tools", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 80, "None", "data-cleanup", 2, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 81, "View", "dashboard", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 82, "Create", "daily-qc", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 83, "Create", "field-samples", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 84, "View", "qc-reports", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 85, "Create", "receipts", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 86, "View", "current-lots", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 87, "None", "bins-run", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 88, "None", "projection-planner", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 89, "None", "projection-outcome", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 90, "None", "actual-runs", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 91, "None", "packout-results", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 92, "None", "historical-inventory-cleanup", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 93, "View", "rooms", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 94, "None", "room-transactions", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 95, "None", "transfers", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 96, "None", "true-up", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 97, "View", "inventory", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 98, "View", "grower-lots", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 99, "None", "crop-year-review", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 100, "None", "master-data", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 101, "None", "users", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 102, "None", "permission-matrix", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 103, "None", "qc-stations", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 104, "None", "downloads", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 105, "None", "configuration", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 106, "None", "variety-colors", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 107, "None", "backups", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 108, "None", "orchard-recipients", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 109, "None", "orchard-managers", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 110, "None", "facilities", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 111, "None", "varieties", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 112, "None", "grades", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 113, "None", "defects", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 114, "None", "size-configuration", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 115, "None", "email-configuration", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 116, "None", "backup-history", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 117, "None", "audit-history", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 118, "None", "import-tools", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 119, "None", "export-tools", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 120, "None", "data-cleanup", 3, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 121, "View", "dashboard", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 122, "View", "daily-qc", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 123, "View", "field-samples", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 124, "View", "qc-reports", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 125, "View", "receipts", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 126, "View", "current-lots", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 127, "None", "bins-run", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 128, "None", "projection-planner", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 129, "None", "projection-outcome", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 130, "None", "actual-runs", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 131, "None", "packout-results", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 132, "None", "historical-inventory-cleanup", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 133, "View", "rooms", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 134, "None", "room-transactions", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 135, "None", "transfers", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 136, "None", "true-up", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 137, "View", "inventory", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 138, "View", "grower-lots", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 139, "None", "crop-year-review", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 140, "None", "master-data", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 141, "None", "users", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 142, "None", "permission-matrix", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 143, "None", "qc-stations", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 144, "None", "downloads", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 145, "None", "configuration", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 146, "None", "variety-colors", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 147, "None", "backups", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 148, "None", "orchard-recipients", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 149, "None", "orchard-managers", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 150, "None", "facilities", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 151, "None", "varieties", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 152, "None", "grades", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 153, "None", "defects", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 154, "None", "size-configuration", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 155, "None", "email-configuration", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 156, "None", "backup-history", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 157, "None", "audit-history", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 158, "None", "import-tools", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 159, "None", "export-tools", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 160, "None", "data-cleanup", 4, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null }
                });

            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "Id", "Description", "IsActive", "IsSystemRole", "Name", "NormalizedName" },
                columnTypes: new[]
                {
                    MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                    MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(500)", "character varying(500)"),
                    MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"),
                    MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"),
                    MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"),
                    MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)")
                },
                values: new object[] { 5, "QC workflow and QC configuration administration without system security access.", true, true, "QC Admin", "QC ADMIN" });

            migrationBuilder.InsertData(
                table: "RolePageAccesses",
                columns: new[] { "Id", "AccessLevel", "AreaKey", "RoleId", "UpdatedAt", "UpdatedByUserId" },
                columnTypes: new[]
                {
                    MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                    MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"),
                    MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"),
                    MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                    MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"),
                    MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer")
                },
                values: new object[,]
                {
                    { 161, "View", "dashboard", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 162, "Admin", "daily-qc", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 163, "Admin", "field-samples", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 164, "Admin", "qc-reports", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 165, "Create", "receipts", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 166, "View", "current-lots", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 167, "None", "bins-run", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 168, "None", "projection-planner", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 169, "None", "projection-outcome", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 170, "None", "actual-runs", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 171, "None", "packout-results", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 172, "None", "historical-inventory-cleanup", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 173, "View", "rooms", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 174, "None", "room-transactions", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 175, "None", "transfers", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 176, "None", "true-up", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 177, "View", "inventory", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 178, "View", "grower-lots", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 179, "None", "crop-year-review", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 180, "View", "master-data", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 181, "None", "users", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 182, "None", "permission-matrix", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 183, "Admin", "qc-stations", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 184, "None", "downloads", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 185, "None", "configuration", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 186, "Admin", "variety-colors", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 187, "None", "backups", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 188, "Admin", "orchard-recipients", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 189, "Admin", "orchard-managers", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 190, "None", "facilities", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 191, "Admin", "varieties", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 192, "Admin", "grades", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 193, "Admin", "defects", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 194, "Admin", "size-configuration", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 195, "None", "email-configuration", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 196, "None", "backup-history", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 197, "None", "audit-history", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 198, "None", "import-tools", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 199, "None", "export-tools", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null },
                    { 200, "None", "data-cleanup", 5, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null }
                });

            migrationBuilder.Sql(MigrationProviderTypes.Sql(
                migrationBuilder,
                "SELECT 1;",
                "SELECT setval(pg_get_serial_sequence('\"RolePageAccesses\"','Id'), (SELECT max(\"Id\") FROM \"RolePageAccesses\"));"));

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_UserId",
                table: "UserRoles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Roles_NormalizedName",
                table: "Roles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RolePageAccesses_RoleId_AreaKey",
                table: "RolePageAccesses",
                columns: new[] { "RoleId", "AreaKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RolePageAccesses_UpdatedByUserId",
                table: "RolePageAccesses",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RolePageAccesses");

            migrationBuilder.DropIndex(
                name: "IX_UserRoles_UserId",
                table: "UserRoles");

            migrationBuilder.DropIndex(
                name: "IX_Roles_NormalizedName",
                table: "Roles");

            migrationBuilder.DeleteData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 5);

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Roles");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                table: "Roles");

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 2,
                column: "Description",
                value: "Manage QC receiving workflows and resend summaries.");

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 3,
                column: "Name",
                value: "QC User");

            migrationBuilder.UpdateData(
                table: "Roles",
                keyColumn: "Id",
                keyValue: 4,
                column: "Description",
                value: "Read-only dashboard access.");

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true);
        }
    }
}
