using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRunProjectionPackoutAndGradeSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CalculationVersion",
                table: "RunProjectionSources",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"),
                maxLength: 25,
                nullable: false,
                defaultValue: "1.0");

            migrationBuilder.AddColumn<decimal>(
                name: "CullProjectedBoxes",
                table: "RunProjectionSources",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "CullProjectedPounds",
                table: "RunProjectionSources",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "ExpectedCullPercent",
                table: "RunProjectionSources",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExpectedPackoutPercent",
                table: "RunProjectionSources",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GradeBasisFruitCount",
                table: "RunProjectionSources",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "JointSizeGradeBasisFruitCount",
                table: "RunProjectionSources",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "PackedProjectedBoxes",
                table: "RunProjectionSources",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PackedProjectedPounds",
                table: "RunProjectionSources",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "QcSampleStatusSnapshot",
                table: "RunProjectionSources",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"),
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QcSampleTypeSnapshot",
                table: "RunProjectionSources",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"),
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RoundedCullProjectedBoxes",
                table: "RunProjectionSources",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RoundedPackedProjectedBoxes",
                table: "RunProjectionSources",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SizeBasisFruitCount",
                table: "RunProjectionSources",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "CullProjectedBoxes",
                table: "RunProjectionSizeResults",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PackedProjectedBoxes",
                table: "RunProjectionSizeResults",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "RoundedCullProjectedBoxes",
                table: "RunProjectionSizeResults",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RoundedPackedProjectedBoxes",
                table: "RunProjectionSizeResults",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalCullProjectedBoxes",
                table: "RunProjections",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalCullProjectedPounds",
                table: "RunProjections",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalPackedProjectedBoxes",
                table: "RunProjections",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalPackedProjectedPounds",
                table: "RunProjections",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "TotalRoundedCullProjectedBoxes",
                table: "RunProjections",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalRoundedPackedProjectedBoxes",
                table: "RunProjections",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "RunProjectionGradeResults",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RunProjectionSourceId = table.Column<long>(type: "bigint", nullable: false),
                    GradeCode = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    SampleCount = table.Column<int>(type: "int", nullable: false),
                    Percentage = table.Column<decimal>(type: "decimal(9,6)", precision: 9, scale: 6, nullable: false),
                    GrossProjectedBoxes = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    RoundedGrossProjectedBoxes = table.Column<int>(type: "int", nullable: false),
                    PackedProjectedBoxes = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    RoundedPackedProjectedBoxes = table.Column<int>(type: "int", nullable: false),
                    CullProjectedBoxes = table.Column<decimal>(type: "decimal(18,6)", precision: 18, scale: 6, nullable: false),
                    RoundedCullProjectedBoxes = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RunProjectionGradeResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RunProjectionGradeResults_RunProjectionSources_RunProjectionSourceId",
                        column: x => x.RunProjectionSourceId,
                        principalTable: "RunProjectionSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RunProjectionGradeResults_RunProjectionSourceId_GradeCode",
                table: "RunProjectionGradeResults",
                columns: new[] { "RunProjectionSourceId", "GradeCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RunProjectionGradeResults");

            migrationBuilder.DropColumn(
                name: "CalculationVersion",
                table: "RunProjectionSources");

            migrationBuilder.DropColumn(
                name: "CullProjectedBoxes",
                table: "RunProjectionSources");

            migrationBuilder.DropColumn(
                name: "CullProjectedPounds",
                table: "RunProjectionSources");

            migrationBuilder.DropColumn(
                name: "ExpectedCullPercent",
                table: "RunProjectionSources");

            migrationBuilder.DropColumn(
                name: "ExpectedPackoutPercent",
                table: "RunProjectionSources");

            migrationBuilder.DropColumn(
                name: "GradeBasisFruitCount",
                table: "RunProjectionSources");

            migrationBuilder.DropColumn(
                name: "JointSizeGradeBasisFruitCount",
                table: "RunProjectionSources");

            migrationBuilder.DropColumn(
                name: "PackedProjectedBoxes",
                table: "RunProjectionSources");

            migrationBuilder.DropColumn(
                name: "PackedProjectedPounds",
                table: "RunProjectionSources");

            migrationBuilder.DropColumn(
                name: "QcSampleStatusSnapshot",
                table: "RunProjectionSources");

            migrationBuilder.DropColumn(
                name: "QcSampleTypeSnapshot",
                table: "RunProjectionSources");

            migrationBuilder.DropColumn(
                name: "RoundedCullProjectedBoxes",
                table: "RunProjectionSources");

            migrationBuilder.DropColumn(
                name: "RoundedPackedProjectedBoxes",
                table: "RunProjectionSources");

            migrationBuilder.DropColumn(
                name: "SizeBasisFruitCount",
                table: "RunProjectionSources");

            migrationBuilder.DropColumn(
                name: "CullProjectedBoxes",
                table: "RunProjectionSizeResults");

            migrationBuilder.DropColumn(
                name: "PackedProjectedBoxes",
                table: "RunProjectionSizeResults");

            migrationBuilder.DropColumn(
                name: "RoundedCullProjectedBoxes",
                table: "RunProjectionSizeResults");

            migrationBuilder.DropColumn(
                name: "RoundedPackedProjectedBoxes",
                table: "RunProjectionSizeResults");

            migrationBuilder.DropColumn(
                name: "TotalCullProjectedBoxes",
                table: "RunProjections");

            migrationBuilder.DropColumn(
                name: "TotalCullProjectedPounds",
                table: "RunProjections");

            migrationBuilder.DropColumn(
                name: "TotalPackedProjectedBoxes",
                table: "RunProjections");

            migrationBuilder.DropColumn(
                name: "TotalPackedProjectedPounds",
                table: "RunProjections");

            migrationBuilder.DropColumn(
                name: "TotalRoundedCullProjectedBoxes",
                table: "RunProjections");

            migrationBuilder.DropColumn(
                name: "TotalRoundedPackedProjectedBoxes",
                table: "RunProjections");
        }
    }
}
