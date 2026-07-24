using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCommercialPackProjectionConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "JointSizeGradeSnapshotJson",
                table: "RunProjectionSources",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"),
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CommercialPackPlanId",
                table: "RunProjections",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PackAllocationSnapshotJson",
                table: "RunProjections",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"),
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PackCalculatedAt",
                table: "RunProjections",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"),
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PackCalculationVersion",
                table: "RunProjections",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"),
                maxLength: 25,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PackConfigurationSnapshotJson",
                table: "RunProjections",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"),
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PackPlanCodeSnapshot",
                table: "RunProjections",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"),
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PackPlanNameSnapshot",
                table: "RunProjections",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(150)", "character varying(150)"),
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PackPlanTypeSnapshot",
                table: "RunProjections",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"),
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CommercialPackDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(150)", "character varying(150)"), maxLength: 150, nullable: false),
                    Commodity = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    PackType = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    PackageWeightPounds = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(10,4)", "numeric(10,4)"), precision: 10, scale: 4, nullable: false),
                    AllowsMixedSizes = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    MixRule = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    EffectiveCropYearStart = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    EffectiveCropYearEnd = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    UpdatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialPackDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommercialPackDefinitions_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CommercialPackPlans",
                columns: table => new
                {
                    Id = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(150)", "character varying(150)"), maxLength: 150, nullable: false),
                    Commodity = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    PlanType = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    IsActive = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    EffectiveCropYearStart = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    EffectiveCropYearEnd = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    UpdatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialPackPlans", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommercialPackPlans_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "CommercialPackEligibleSizes",
                columns: table => new
                {
                    Id = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CommercialPackDefinitionId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    SizeCategory = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    Priority = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    TargetPercent = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(7,4)", "numeric(7,4)"), precision: 7, scale: 4, nullable: true),
                    MinimumPercent = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(7,4)", "numeric(7,4)"), precision: 7, scale: 4, nullable: true),
                    MaximumPercent = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(7,4)", "numeric(7,4)"), precision: 7, scale: 4, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialPackEligibleSizes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommercialPackEligibleSizes_CommercialPackDefinitions_CommercialPackDefinitionId",
                        column: x => x.CommercialPackDefinitionId,
                        principalTable: "CommercialPackDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CommercialPackFruitProfileRestrictions",
                columns: table => new
                {
                    CommercialPackDefinitionId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    FruitProfileId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialPackFruitProfileRestrictions", x => new { x.CommercialPackDefinitionId, x.FruitProfileId });
                    table.ForeignKey(
                        name: "FK_CommercialPackFruitProfileRestrictions_CommercialPackDefinitions_CommercialPackDefinitionId",
                        column: x => x.CommercialPackDefinitionId,
                        principalTable: "CommercialPackDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CommercialPackFruitProfileRestrictions_FruitProfiles_FruitProfileId",
                        column: x => x.FruitProfileId,
                        principalTable: "FruitProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CommercialPackPlanItems",
                columns: table => new
                {
                    CommercialPackPlanId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    CommercialPackDefinitionId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    Priority = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercialPackPlanItems", x => new { x.CommercialPackPlanId, x.CommercialPackDefinitionId });
                    table.ForeignKey(
                        name: "FK_CommercialPackPlanItems_CommercialPackDefinitions_CommercialPackDefinitionId",
                        column: x => x.CommercialPackDefinitionId,
                        principalTable: "CommercialPackDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CommercialPackPlanItems_CommercialPackPlans_CommercialPackPlanId",
                        column: x => x.CommercialPackPlanId,
                        principalTable: "CommercialPackPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RunProjections_CommercialPackPlanId",
                table: "RunProjections",
                column: "CommercialPackPlanId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialPackDefinitions_Code",
                table: "CommercialPackDefinitions",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommercialPackDefinitions_Commodity_IsActive",
                table: "CommercialPackDefinitions",
                columns: new[] { "Commodity", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialPackDefinitions_UpdatedByUserId",
                table: "CommercialPackDefinitions",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialPackEligibleSizes_CommercialPackDefinitionId_SizeCategory",
                table: "CommercialPackEligibleSizes",
                columns: new[] { "CommercialPackDefinitionId", "SizeCategory" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommercialPackFruitProfileRestrictions_FruitProfileId",
                table: "CommercialPackFruitProfileRestrictions",
                column: "FruitProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialPackPlanItems_CommercialPackDefinitionId",
                table: "CommercialPackPlanItems",
                column: "CommercialPackDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercialPackPlanItems_CommercialPackPlanId_Priority",
                table: "CommercialPackPlanItems",
                columns: new[] { "CommercialPackPlanId", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialPackPlans_Code",
                table: "CommercialPackPlans",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommercialPackPlans_Commodity_IsActive",
                table: "CommercialPackPlans",
                columns: new[] { "Commodity", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_CommercialPackPlans_UpdatedByUserId",
                table: "CommercialPackPlans",
                column: "UpdatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_RunProjections_CommercialPackPlans_CommercialPackPlanId",
                table: "RunProjections",
                column: "CommercialPackPlanId",
                principalTable: "CommercialPackPlans",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RunProjections_CommercialPackPlans_CommercialPackPlanId",
                table: "RunProjections");

            migrationBuilder.DropTable(
                name: "CommercialPackEligibleSizes");

            migrationBuilder.DropTable(
                name: "CommercialPackFruitProfileRestrictions");

            migrationBuilder.DropTable(
                name: "CommercialPackPlanItems");

            migrationBuilder.DropTable(
                name: "CommercialPackDefinitions");

            migrationBuilder.DropTable(
                name: "CommercialPackPlans");

            migrationBuilder.DropIndex(
                name: "IX_RunProjections_CommercialPackPlanId",
                table: "RunProjections");

            migrationBuilder.DropColumn(
                name: "JointSizeGradeSnapshotJson",
                table: "RunProjectionSources");

            migrationBuilder.DropColumn(
                name: "CommercialPackPlanId",
                table: "RunProjections");

            migrationBuilder.DropColumn(
                name: "PackAllocationSnapshotJson",
                table: "RunProjections");

            migrationBuilder.DropColumn(
                name: "PackCalculatedAt",
                table: "RunProjections");

            migrationBuilder.DropColumn(
                name: "PackCalculationVersion",
                table: "RunProjections");

            migrationBuilder.DropColumn(
                name: "PackConfigurationSnapshotJson",
                table: "RunProjections");

            migrationBuilder.DropColumn(
                name: "PackPlanCodeSnapshot",
                table: "RunProjections");

            migrationBuilder.DropColumn(
                name: "PackPlanNameSnapshot",
                table: "RunProjections");

            migrationBuilder.DropColumn(
                name: "PackPlanTypeSnapshot",
                table: "RunProjections");
        }
    }
}
