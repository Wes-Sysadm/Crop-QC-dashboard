using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddOrchardManagerContactImports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CanonicalOrchardAliases",
                columns: table => new
                {
                    Id = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CanonicalOrchardId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    AliasText = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"), maxLength: 200, nullable: false),
                    NormalizedAlias = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"), maxLength: 200, nullable: false),
                    Source = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"), maxLength: 200, nullable: false),
                    ReviewNote = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    CreatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    UpdatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CanonicalOrchardAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CanonicalOrchardAliases_CanonicalOrchards_CanonicalOrchardId",
                        column: x => x.CanonicalOrchardId,
                        principalTable: "CanonicalOrchards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CanonicalOrchardAliases_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_CanonicalOrchardAliases_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "OrchardContactImportBatches",
                columns: table => new
                {
                    Id = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OriginalFileName = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(260)", "character varying(260)"), maxLength: 260, nullable: false),
                    WorkbookSha256 = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(64)", "character varying(64)"), maxLength: 64, nullable: false),
                    WorksheetName = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(30)", "character varying(30)"), maxLength: 30, nullable: false),
                    OrchardManagerSourceRowCount = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    ParsedOrchardTokenCount = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    UploadedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    AppliedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true),
                    AppliedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    VerifiedBackupRunId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: true),
                    ImportReason = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true),
                    ApplySummaryJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrchardContactImportBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrchardContactImportBatches_BackupRunRecords_VerifiedBackupRunId",
                        column: x => x.VerifiedBackupRunId,
                        principalTable: "BackupRunRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrchardContactImportBatches_Users_AppliedByUserId",
                        column: x => x.AppliedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OrchardContactImportBatches_Users_UploadedByUserId",
                        column: x => x.UploadedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "OrchardManagerContacts",
                columns: table => new
                {
                    Id = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    DisplayName = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"), maxLength: 200, nullable: false),
                    NormalizedDisplayName = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"), maxLength: 200, nullable: false),
                    EmailAddress = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(320)", "character varying(320)"), maxLength: 320, nullable: true),
                    NormalizedEmailAddress = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(320)", "character varying(320)"), maxLength: 320, nullable: true),
                    Phone = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: true),
                    NormalizedPhone = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"), maxLength: 25, nullable: true),
                    CommunicationNote = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true),
                    SourceWorkbook = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(260)", "character varying(260)"), maxLength: 260, nullable: false),
                    SourceWorksheet = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: false),
                    SourceRowNumber = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    ImportedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    IsActive = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    CreatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    UpdatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrchardManagerContacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrchardManagerContacts_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OrchardManagerContacts_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "OrchardContactImportRows",
                columns: table => new
                {
                    Id = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    OrchardContactImportBatchId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false),
                    WorkbookRowNumber = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    OriginalOrchardCell = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: false),
                    ParsedOrchardToken = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"), maxLength: 200, nullable: false),
                    ManagerDisplayName = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"), maxLength: 200, nullable: false),
                    NormalizedManagerName = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(200)", "character varying(200)"), maxLength: 200, nullable: false),
                    EmailAddress = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(320)", "character varying(320)"), maxLength: 320, nullable: true),
                    NormalizedEmailAddress = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(320)", "character varying(320)"), maxLength: 320, nullable: true),
                    EmailIsValid = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    Phone = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: true),
                    NormalizedPhone = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(25)", "character varying(25)"), maxLength: 25, nullable: true),
                    PhysicalAddress = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true),
                    CommunicationNote = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true),
                    SourceStatusNote = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(2000)", "character varying(2000)"), maxLength: 2000, nullable: true),
                    MatchMethod = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    MatchScore = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(6,4)", "numeric(6,4)"), precision: 6, scale: 4, nullable: true),
                    SuggestedCanonicalOrchardId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    CandidateMatchesJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), nullable: true),
                    Warning = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(2000)", "character varying(2000)"), maxLength: 2000, nullable: true),
                    ReviewDecision = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(30)", "character varying(30)"), maxLength: 30, nullable: false),
                    ApprovedCanonicalOrchardId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    CreateAlias = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    CreateRecipient = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    ReactivateDeletedRecipient = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    ReviewNote = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(2000)", "character varying(2000)"), maxLength: 2000, nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true),
                    ReviewedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    AppliedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true),
                    AppliedAction = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true),
                    OrchardManagerContactId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: true),
                    OrchardReportRecipientId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrchardContactImportRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrchardContactImportRows_CanonicalOrchards_ApprovedCanonicalOrchardId",
                        column: x => x.ApprovedCanonicalOrchardId,
                        principalTable: "CanonicalOrchards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrchardContactImportRows_CanonicalOrchards_SuggestedCanonicalOrchardId",
                        column: x => x.SuggestedCanonicalOrchardId,
                        principalTable: "CanonicalOrchards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrchardContactImportRows_OrchardContactImportBatches_OrchardContactImportBatchId",
                        column: x => x.OrchardContactImportBatchId,
                        principalTable: "OrchardContactImportBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrchardContactImportRows_OrchardManagerContacts_OrchardManagerContactId",
                        column: x => x.OrchardManagerContactId,
                        principalTable: "OrchardManagerContacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OrchardContactImportRows_OrchardReportRecipients_OrchardReportRecipientId",
                        column: x => x.OrchardReportRecipientId,
                        principalTable: "OrchardReportRecipients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OrchardContactImportRows_Users_ReviewedByUserId",
                        column: x => x.ReviewedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "OrchardManagerAssignments",
                columns: table => new
                {
                    Id = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CanonicalOrchardId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    OrchardManagerContactId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false),
                    OrchardReportRecipientId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    SourceImportRowId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: true),
                    IsActive = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    CreatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    UpdatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrchardManagerAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrchardManagerAssignments_CanonicalOrchards_CanonicalOrchardId",
                        column: x => x.CanonicalOrchardId,
                        principalTable: "CanonicalOrchards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrchardManagerAssignments_OrchardContactImportRows_SourceImportRowId",
                        column: x => x.SourceImportRowId,
                        principalTable: "OrchardContactImportRows",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OrchardManagerAssignments_OrchardManagerContacts_OrchardManagerContactId",
                        column: x => x.OrchardManagerContactId,
                        principalTable: "OrchardManagerContacts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OrchardManagerAssignments_OrchardReportRecipients_OrchardReportRecipientId",
                        column: x => x.OrchardReportRecipientId,
                        principalTable: "OrchardReportRecipients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OrchardManagerAssignments_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_OrchardManagerAssignments_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalOrchardAliases_CanonicalOrchardId_NormalizedAlias",
                table: "CanonicalOrchardAliases",
                columns: new[] { "CanonicalOrchardId", "NormalizedAlias" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalOrchardAliases_CreatedByUserId",
                table: "CanonicalOrchardAliases",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalOrchardAliases_NormalizedAlias",
                table: "CanonicalOrchardAliases",
                column: "NormalizedAlias");

            migrationBuilder.CreateIndex(
                name: "IX_CanonicalOrchardAliases_UpdatedByUserId",
                table: "CanonicalOrchardAliases",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrchardContactImportBatches_AppliedByUserId",
                table: "OrchardContactImportBatches",
                column: "AppliedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrchardContactImportBatches_UploadedByUserId",
                table: "OrchardContactImportBatches",
                column: "UploadedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrchardContactImportBatches_VerifiedBackupRunId",
                table: "OrchardContactImportBatches",
                column: "VerifiedBackupRunId");

            migrationBuilder.CreateIndex(
                name: "IX_OrchardContactImportBatches_WorkbookSha256_WorksheetName",
                table: "OrchardContactImportBatches",
                columns: new[] { "WorkbookSha256", "WorksheetName" });

            migrationBuilder.CreateIndex(
                name: "IX_OrchardContactImportRows_ApprovedCanonicalOrchardId",
                table: "OrchardContactImportRows",
                column: "ApprovedCanonicalOrchardId");

            migrationBuilder.CreateIndex(
                name: "IX_OrchardContactImportRows_OrchardContactImportBatchId_ReviewDecision",
                table: "OrchardContactImportRows",
                columns: new[] { "OrchardContactImportBatchId", "ReviewDecision" });

            migrationBuilder.CreateIndex(
                name: "IX_OrchardContactImportRows_OrchardContactImportBatchId_WorkbookRowNumber",
                table: "OrchardContactImportRows",
                columns: new[] { "OrchardContactImportBatchId", "WorkbookRowNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_OrchardContactImportRows_OrchardManagerContactId",
                table: "OrchardContactImportRows",
                column: "OrchardManagerContactId");

            migrationBuilder.CreateIndex(
                name: "IX_OrchardContactImportRows_OrchardReportRecipientId",
                table: "OrchardContactImportRows",
                column: "OrchardReportRecipientId");

            migrationBuilder.CreateIndex(
                name: "IX_OrchardContactImportRows_ReviewedByUserId",
                table: "OrchardContactImportRows",
                column: "ReviewedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrchardContactImportRows_SuggestedCanonicalOrchardId",
                table: "OrchardContactImportRows",
                column: "SuggestedCanonicalOrchardId");

            migrationBuilder.CreateIndex(
                name: "IX_OrchardManagerAssignments_CanonicalOrchardId_OrchardManagerContactId",
                table: "OrchardManagerAssignments",
                columns: new[] { "CanonicalOrchardId", "OrchardManagerContactId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrchardManagerAssignments_CreatedByUserId",
                table: "OrchardManagerAssignments",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrchardManagerAssignments_OrchardManagerContactId",
                table: "OrchardManagerAssignments",
                column: "OrchardManagerContactId");

            migrationBuilder.CreateIndex(
                name: "IX_OrchardManagerAssignments_OrchardReportRecipientId",
                table: "OrchardManagerAssignments",
                column: "OrchardReportRecipientId");

            migrationBuilder.CreateIndex(
                name: "IX_OrchardManagerAssignments_SourceImportRowId",
                table: "OrchardManagerAssignments",
                column: "SourceImportRowId");

            migrationBuilder.CreateIndex(
                name: "IX_OrchardManagerAssignments_UpdatedByUserId",
                table: "OrchardManagerAssignments",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrchardManagerContacts_CreatedByUserId",
                table: "OrchardManagerContacts",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_OrchardManagerContacts_NormalizedDisplayName_NormalizedPhone",
                table: "OrchardManagerContacts",
                columns: new[] { "NormalizedDisplayName", "NormalizedPhone" });

            migrationBuilder.CreateIndex(
                name: "IX_OrchardManagerContacts_NormalizedEmailAddress",
                table: "OrchardManagerContacts",
                column: "NormalizedEmailAddress");

            migrationBuilder.CreateIndex(
                name: "IX_OrchardManagerContacts_UpdatedByUserId",
                table: "OrchardManagerContacts",
                column: "UpdatedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CanonicalOrchardAliases");

            migrationBuilder.DropTable(
                name: "OrchardManagerAssignments");

            migrationBuilder.DropTable(
                name: "OrchardContactImportRows");

            migrationBuilder.DropTable(
                name: "OrchardContactImportBatches");

            migrationBuilder.DropTable(
                name: "OrchardManagerContacts");
        }
    }
}
