using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialMvp1ReceivingQcModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DefectTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DefectTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FruitProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    VarietyCode = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    FruitType = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    ProductionType = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    IsOrganic = table.Column<bool>(type: "bit", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FruitProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FruitSizeConversionThresholds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FruitType = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    SizeCategory = table.Column<int>(type: "int", nullable: false),
                    MinimumWeightGrams = table.Column<decimal>(type: "decimal(8,4)", precision: 8, scale: 4, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FruitSizeConversionThresholds", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Grades",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Grades", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PasswordPolicies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MinimumLength = table.Column<int>(type: "int", nullable: false),
                    RequireUppercase = table.Column<bool>(type: "bit", nullable: false),
                    RequireLowercase = table.Column<bool>(type: "bit", nullable: false),
                    RequireNumber = table.Column<bool>(type: "bit", nullable: false),
                    RequireSymbol = table.Column<bool>(type: "bit", nullable: false),
                    PasswordExpirationDays = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsSystemRole = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SampleTypes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SampleTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Email = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PasswordLastChangedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Warehouses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Code = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Warehouses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StarchScales",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FruitType = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: true),
                    FruitProfileId = table.Column<int>(type: "int", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StarchScales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StarchScales_FruitProfiles_FruitProfileId",
                        column: x => x.FruitProfileId,
                        principalTable: "FruitProfiles",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RoleId = table.Column<int>(type: "int", nullable: false),
                    PermissionKey = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RolePermissions_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AuditLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: true),
                    Action = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EntityName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    EntityKey = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    BeforeValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AfterValuesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SourceApplication = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditLogs_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "int", nullable: false),
                    RoleId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QcStations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StationCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: true),
                    DeviceIdentifier = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QcStations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QcStations_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Rooms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    CapacityBins = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rooms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Rooms_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StarchScaleValues",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StarchScaleId = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<decimal>(type: "decimal(4,1)", precision: 4, scale: 1, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StarchScaleValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StarchScaleValues_StarchScales_StarchScaleId",
                        column: x => x.StarchScaleId,
                        principalTable: "StarchScales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfflineSyncItems",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QcStationId = table.Column<int>(type: "int", nullable: true),
                    EntityName = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    LocalEntityId = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ServerEntityId = table.Column<long>(type: "bigint", nullable: true),
                    SyncStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastAttemptedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SyncedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfflineSyncItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OfflineSyncItems_QcStations_QcStationId",
                        column: x => x.QcStationId,
                        principalTable: "QcStations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "Receipts",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CropYear = table.Column<int>(type: "int", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CompuTechReceiptId = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    WarehouseId = table.Column<int>(type: "int", nullable: false),
                    RoomId = table.Column<int>(type: "int", nullable: false),
                    FruitProfileId = table.Column<int>(type: "int", nullable: false),
                    GrowerName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LotCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    BinCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Receipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Receipts_FruitProfiles_FruitProfileId",
                        column: x => x.FruitProfileId,
                        principalTable: "FruitProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Receipts_Rooms_RoomId",
                        column: x => x.RoomId,
                        principalTable: "Rooms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Receipts_Warehouses_WarehouseId",
                        column: x => x.WarehouseId,
                        principalTable: "Warehouses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QcSamples",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReceiptId = table.Column<long>(type: "bigint", nullable: false),
                    SampleTypeId = table.Column<int>(type: "int", nullable: false),
                    SampleSequenceNumber = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    StarchStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PhotoStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    EmailStatus = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TakenByUserId = table.Column<int>(type: "int", nullable: true),
                    QcStationId = table.Column<int>(type: "int", nullable: true),
                    ActualSampleSize = table.Column<int>(type: "int", nullable: true),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    SampleTakenAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QcSamples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QcSamples_QcStations_QcStationId",
                        column: x => x.QcStationId,
                        principalTable: "QcStations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QcSamples_Receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "Receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QcSamples_SampleTypes_SampleTypeId",
                        column: x => x.SampleTypeId,
                        principalTable: "SampleTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QcSamples_Users_TakenByUserId",
                        column: x => x.TakenByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "QcFruitReadings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QcSampleId = table.Column<long>(type: "bigint", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    Pressure1Lbs = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    Pressure1Source = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    Pressure2Lbs = table.Column<decimal>(type: "decimal(6,2)", precision: 6, scale: 2, nullable: true),
                    Pressure2Source = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    WeightGrams = table.Column<decimal>(type: "decimal(8,4)", precision: 8, scale: 4, nullable: true),
                    GradeId = table.Column<int>(type: "int", nullable: true),
                    StarchScaleValueId = table.Column<int>(type: "int", nullable: true),
                    SizeCategory = table.Column<int>(type: "int", nullable: true),
                    SizeStatus = table.Column<string>(type: "nvarchar(25)", maxLength: 25, nullable: false, defaultValue: "NotCalculated"),
                    IsCompleted = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QcFruitReadings", x => x.Id);
                    table.CheckConstraint("CK_QcFruitReadings_CompletedRequiresCoreFields", "([IsCompleted] = 0) OR ([Pressure1Lbs] IS NOT NULL AND [Pressure2Lbs] IS NOT NULL AND [WeightGrams] IS NOT NULL AND [GradeId] IS NOT NULL)");
                    table.CheckConstraint("CK_QcFruitReadings_RowNumber_1_25", "[RowNumber] >= 1 AND [RowNumber] <= 25");
                    table.ForeignKey(
                        name: "FK_QcFruitReadings_Grades_GradeId",
                        column: x => x.GradeId,
                        principalTable: "Grades",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QcFruitReadings_QcSamples_QcSampleId",
                        column: x => x.QcSampleId,
                        principalTable: "QcSamples",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QcFruitReadings_StarchScaleValues_StarchScaleValueId",
                        column: x => x.StarchScaleValueId,
                        principalTable: "StarchScaleValues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "QcPhotos",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReceiptId = table.Column<long>(type: "bigint", nullable: true),
                    QcSampleId = table.Column<long>(type: "bigint", nullable: true),
                    PhotoType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PhotoSource = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    FileSizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    SharePointDriveId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SharePointItemId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    WebUrl = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CapturedByUserId = table.Column<int>(type: "int", nullable: true),
                    CapturedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QcPhotos", x => x.Id);
                    table.CheckConstraint("CK_QcPhotos_ReceiptOrSample", "([ReceiptId] IS NOT NULL AND [QcSampleId] IS NULL) OR ([ReceiptId] IS NULL AND [QcSampleId] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_QcPhotos_QcSamples_QcSampleId",
                        column: x => x.QcSampleId,
                        principalTable: "QcSamples",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QcPhotos_Receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "Receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QcPhotos_Users_CapturedByUserId",
                        column: x => x.CapturedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "QcSummaryEmailLogs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ReceiptId = table.Column<long>(type: "bigint", nullable: false),
                    QcSampleId = table.Column<long>(type: "bigint", nullable: true),
                    FromAddress = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    ToAddress = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: false),
                    ReplyToAddress = table.Column<string>(type: "nvarchar(320)", maxLength: 320, nullable: true),
                    Subject = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    MessageId = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SentByUserId = table.Column<int>(type: "int", nullable: true),
                    SentAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    IsResend = table.Column<bool>(type: "bit", nullable: false),
                    ResendReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EmailBodySnapshot = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReportSnapshotReference = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QcSummaryEmailLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QcSummaryEmailLogs_QcSamples_QcSampleId",
                        column: x => x.QcSampleId,
                        principalTable: "QcSamples",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_QcSummaryEmailLogs_Receipts_ReceiptId",
                        column: x => x.ReceiptId,
                        principalTable: "Receipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QcSummaryEmailLogs_Users_SentByUserId",
                        column: x => x.SentByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "QcFruitDefects",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    QcFruitReadingId = table.Column<long>(type: "bigint", nullable: false),
                    DefectTypeId = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QcFruitDefects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QcFruitDefects_DefectTypes_DefectTypeId",
                        column: x => x.DefectTypeId,
                        principalTable: "DefectTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_QcFruitDefects_QcFruitReadings_QcFruitReadingId",
                        column: x => x.QcFruitReadingId,
                        principalTable: "QcFruitReadings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "DefectTypes",
                columns: new[] { "Id", "IsActive", "Name" },
                values: new object[,]
                {
                    { 1, true, "Bruise" },
                    { 2, true, "Sunburn" },
                    { 3, true, "Bitter Pit" },
                    { 4, true, "Scald" },
                    { 5, true, "Decay" },
                    { 6, true, "Puncture" },
                    { 7, true, "Watercore" },
                    { 8, true, "Limb Rub" },
                    { 9, true, "Stem Bowl Crack" },
                    { 10, true, "Internal Browning" },
                    { 11, true, "Other" }
                });

            migrationBuilder.InsertData(
                table: "FruitProfiles",
                columns: new[] { "Id", "Description", "FruitType", "IsActive", "IsOrganic", "Name", "ProductionType", "VarietyCode" },
                values: new object[,]
                {
                    { 1, "Fuji", "Apple", true, false, "Fuji", "Conventional", "FUJI" },
                    { 2, "Gala", "Apple", true, false, "Gala", "Conventional", "GALA" },
                    { 3, "Golden Delicious", "Apple", true, false, "Golden Delicious", "Conventional", "GOLD" },
                    { 4, "Granny Smith", "Apple", true, false, "Granny Smith", "Conventional", "GSMT" },
                    { 5, "Honey Crisp", "Apple", true, false, "Honey Crisp", "Conventional", "HONY" },
                    { 6, "Organic Fuji", "Apple", true, true, "Organic Fuji", "Organic", "ORFU" },
                    { 7, "Organic Gala", "Apple", true, true, "Organic Gala", "Organic", "ORGA" },
                    { 8, "Organic Golden Delicious", "Apple", true, true, "Organic Golden Delicious", "Organic", "ORGD" },
                    { 9, "Organic Granny Smith", "Apple", true, true, "Organic Granny Smith", "Organic", "ORGS" },
                    { 10, "Organic Honey Crisp", "Apple", true, true, "Organic Honey Crisp", "Organic", "ORHC" },
                    { 11, "Organic Pink Lady", "Apple", true, true, "Organic Pink Lady", "Organic", "ORPL" },
                    { 12, "Organic Red Delicious", "Apple", true, true, "Organic Red Delicious", "Organic", "ORRD" },
                    { 13, "Pink Lady", "Apple", true, false, "Pink Lady", "Conventional", "PINK" },
                    { 14, "Red Delicious", "Apple", true, false, "Red Delicious", "Conventional", "RED" },
                    { 15, "Mardi Gras", "Pear", true, false, "Mardi Gras", "Conventional", "MDGS" },
                    { 16, "Bosc", "Pear", true, false, "Bosc", "Conventional", "BOSC" },
                    { 17, "Bartlett", "Pear", true, false, "Bartlett", "Conventional", "BART" },
                    { 18, "D'Anjou", "Pear", true, false, "D'Anjou", "Conventional", "DANJ" },
                    { 19, "Organic Bartlett", "Pear", true, true, "Organic Bartlett", "Organic", "ORBA" },
                    { 20, "Organic Bosc", "Pear", true, true, "Organic Bosc", "Organic", "ORBO" },
                    { 21, "Organic D'anjou", "Pear", true, true, "Organic D'anjou", "Organic", "ORDA" },
                    { 22, "Autumn Glory", "Apple", true, false, "Autumn Glory", "Conventional", "ATGL" }
                });

            migrationBuilder.InsertData(
                table: "FruitSizeConversionThresholds",
                columns: new[] { "Id", "FruitType", "IsActive", "MinimumWeightGrams", "SizeCategory" },
                values: new object[,]
                {
                    { 1, "Apple", true, 405.0000m, 48 },
                    { 2, "Apple", true, 354.0000m, 56 },
                    { 3, "Apple", true, 298.0000m, 64 },
                    { 4, "Apple", true, 264.0000m, 72 },
                    { 5, "Apple", true, 238.0000m, 80 },
                    { 6, "Apple", true, 215.0000m, 88 },
                    { 7, "Apple", true, 190.0000m, 100 },
                    { 8, "Apple", true, 167.0000m, 113 },
                    { 9, "Apple", true, 153.0000m, 125 },
                    { 10, "Apple", true, 136.0000m, 138 },
                    { 11, "Apple", true, 128.0000m, 150 },
                    { 12, "Apple", true, 116.0000m, 163 },
                    { 13, "Apple", true, 108.0000m, 175 },
                    { 14, "Apple", true, 96.0000m, 198 },
                    { 15, "Apple", true, 88.0000m, 216 },
                    { 16, "Pear", true, 360.0000m, 50 },
                    { 17, "Pear", true, 303.0000m, 60 },
                    { 18, "Pear", true, 260.0000m, 70 },
                    { 19, "Pear", true, 227.0000m, 80 },
                    { 20, "Pear", true, 203.0000m, 90 },
                    { 21, "Pear", true, 182.0000m, 100 },
                    { 22, "Pear", true, 165.0000m, 110 },
                    { 23, "Pear", true, 151.0000m, 120 },
                    { 24, "Pear", true, 135.0000m, 135 },
                    { 25, "Pear", true, 121.0000m, 150 },
                    { 26, "Pear", true, 110.0000m, 165 },
                    { 27, "Pear", true, 101.0000m, 180 },
                    { 28, "Pear", true, 94.0000m, 193 },
                    { 29, "Pear", true, 87.0000m, 210 },
                    { 30, "Pear", true, 81.0000m, 225 }
                });

            migrationBuilder.InsertData(
                table: "Grades",
                columns: new[] { "Id", "Code", "IsActive", "Name" },
                values: new object[,]
                {
                    { 1, "W1", true, "W1" },
                    { 2, "W2", true, "W2" },
                    { 3, "W3", true, "W3" },
                    { 4, "W4", true, "W4" },
                    { 5, "WF", true, "WF" },
                    { 6, "US1", true, "US1" },
                    { 7, "US2", true, "US2" },
                    { 8, "USF", true, "USF" }
                });

            migrationBuilder.InsertData(
                table: "PasswordPolicies",
                columns: new[] { "Id", "CreatedAt", "MinimumLength", "PasswordExpirationDays", "RequireLowercase", "RequireNumber", "RequireSymbol", "RequireUppercase", "UpdatedAt" },
                values: new object[] { 1, new DateTimeOffset(new DateTime(2026, 5, 21, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), 8, 365, true, true, true, true, null });

            migrationBuilder.InsertData(
                table: "Roles",
                columns: new[] { "Id", "Description", "IsSystemRole", "Name" },
                values: new object[,]
                {
                    { 1, "Full dashboard and configuration access.", true, "Admin" },
                    { 2, "Manage QC receiving workflows and resend summaries.", true, "Manager" },
                    { 3, "Capture receiving samples and QC readings.", true, "QC User" },
                    { 4, "Read-only dashboard access.", true, "Viewer" }
                });

            migrationBuilder.InsertData(
                table: "SampleTypes",
                columns: new[] { "Id", "IsActive", "Name" },
                values: new object[,]
                {
                    { 1, true, "Receiving Sample" },
                    { 2, true, "Door Sample" },
                    { 3, true, "Line QC Sample" }
                });

            migrationBuilder.InsertData(
                table: "StarchScales",
                columns: new[] { "Id", "FruitProfileId", "FruitType", "IsActive", "Name" },
                values: new object[] { 1, null, null, true, "6-point starch scale" });

            migrationBuilder.InsertData(
                table: "Warehouses",
                columns: new[] { "Id", "Code", "IsActive", "Name" },
                values: new object[,]
                {
                    { 1, "EBS", true, "EBS" },
                    { 2, "DH", true, "DH" },
                    { 3, "McDougall", true, "McDougall" },
                    { 4, "WP", true, "WP" }
                });

            migrationBuilder.InsertData(
                table: "StarchScaleValues",
                columns: new[] { "Id", "IsActive", "SortOrder", "StarchScaleId", "Value" },
                values: new object[,]
                {
                    { 1, true, 10, 1, 1.0m },
                    { 2, true, 20, 1, 1.2m },
                    { 3, true, 30, 1, 1.5m },
                    { 4, true, 40, 1, 1.8m },
                    { 5, true, 50, 1, 2.0m },
                    { 6, true, 60, 1, 2.5m },
                    { 7, true, 70, 1, 3.0m },
                    { 8, true, 80, 1, 3.5m },
                    { 9, true, 90, 1, 4.0m },
                    { 10, true, 100, 1, 4.5m },
                    { 11, true, 110, 1, 5.0m },
                    { 12, true, 120, 1, 6.0m }
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_CreatedAt",
                table: "AuditLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_EntityName_EntityKey",
                table: "AuditLogs",
                columns: new[] { "EntityName", "EntityKey" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditLogs_UserId",
                table: "AuditLogs",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_DefectTypes_Name",
                table: "DefectTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FruitProfiles_VarietyCode",
                table: "FruitProfiles",
                column: "VarietyCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FruitSizeConversionThresholds_FruitType_MinimumWeightGrams",
                table: "FruitSizeConversionThresholds",
                columns: new[] { "FruitType", "MinimumWeightGrams" });

            migrationBuilder.CreateIndex(
                name: "IX_FruitSizeConversionThresholds_FruitType_SizeCategory",
                table: "FruitSizeConversionThresholds",
                columns: new[] { "FruitType", "SizeCategory" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Grades_Code",
                table: "Grades",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfflineSyncItems_QcStationId_EntityName_LocalEntityId",
                table: "OfflineSyncItems",
                columns: new[] { "QcStationId", "EntityName", "LocalEntityId" },
                unique: true,
                filter: "[QcStationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_QcFruitDefects_DefectTypeId",
                table: "QcFruitDefects",
                column: "DefectTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_QcFruitDefects_QcFruitReadingId_DefectTypeId",
                table: "QcFruitDefects",
                columns: new[] { "QcFruitReadingId", "DefectTypeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QcFruitReadings_GradeId",
                table: "QcFruitReadings",
                column: "GradeId");

            migrationBuilder.CreateIndex(
                name: "IX_QcFruitReadings_QcSampleId_RowNumber",
                table: "QcFruitReadings",
                columns: new[] { "QcSampleId", "RowNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QcFruitReadings_StarchScaleValueId",
                table: "QcFruitReadings",
                column: "StarchScaleValueId");

            migrationBuilder.CreateIndex(
                name: "IX_QcPhotos_CapturedByUserId",
                table: "QcPhotos",
                column: "CapturedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_QcPhotos_QcSampleId",
                table: "QcPhotos",
                column: "QcSampleId");

            migrationBuilder.CreateIndex(
                name: "IX_QcPhotos_ReceiptId",
                table: "QcPhotos",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_QcPhotos_SharePointDriveId_SharePointItemId",
                table: "QcPhotos",
                columns: new[] { "SharePointDriveId", "SharePointItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QcSamples_QcStationId",
                table: "QcSamples",
                column: "QcStationId");

            migrationBuilder.CreateIndex(
                name: "IX_QcSamples_ReceiptId_SampleSequenceNumber",
                table: "QcSamples",
                columns: new[] { "ReceiptId", "SampleSequenceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QcSamples_SampleTypeId",
                table: "QcSamples",
                column: "SampleTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_QcSamples_TakenByUserId",
                table: "QcSamples",
                column: "TakenByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_QcStations_StationCode",
                table: "QcStations",
                column: "StationCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QcStations_WarehouseId",
                table: "QcStations",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_QcSummaryEmailLogs_QcSampleId",
                table: "QcSummaryEmailLogs",
                column: "QcSampleId");

            migrationBuilder.CreateIndex(
                name: "IX_QcSummaryEmailLogs_ReceiptId",
                table: "QcSummaryEmailLogs",
                column: "ReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_QcSummaryEmailLogs_SentByUserId",
                table: "QcSummaryEmailLogs",
                column: "SentByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_CompuTechReceiptId",
                table: "Receipts",
                column: "CompuTechReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_FruitProfileId",
                table: "Receipts",
                column: "FruitProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_RoomId",
                table: "Receipts",
                column: "RoomId");

            migrationBuilder.CreateIndex(
                name: "IX_Receipts_WarehouseId",
                table: "Receipts",
                column: "WarehouseId");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_RoleId_PermissionKey",
                table: "RolePermissions",
                columns: new[] { "RoleId", "PermissionKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Roles_Name",
                table: "Roles",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_WarehouseId_Code",
                table: "Rooms",
                columns: new[] { "WarehouseId", "Code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Rooms_WarehouseId_Name",
                table: "Rooms",
                columns: new[] { "WarehouseId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SampleTypes_Name",
                table: "SampleTypes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StarchScales_FruitProfileId",
                table: "StarchScales",
                column: "FruitProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_StarchScales_Name_FruitType_FruitProfileId",
                table: "StarchScales",
                columns: new[] { "Name", "FruitType", "FruitProfileId" },
                unique: true,
                filter: "[FruitType] IS NOT NULL AND [FruitProfileId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_StarchScaleValues_StarchScaleId_Value",
                table: "StarchScaleValues",
                columns: new[] { "StarchScaleId", "Value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId",
                table: "UserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Warehouses_Code",
                table: "Warehouses",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditLogs");

            migrationBuilder.DropTable(
                name: "FruitSizeConversionThresholds");

            migrationBuilder.DropTable(
                name: "OfflineSyncItems");

            migrationBuilder.DropTable(
                name: "PasswordPolicies");

            migrationBuilder.DropTable(
                name: "QcFruitDefects");

            migrationBuilder.DropTable(
                name: "QcPhotos");

            migrationBuilder.DropTable(
                name: "QcSummaryEmailLogs");

            migrationBuilder.DropTable(
                name: "RolePermissions");

            migrationBuilder.DropTable(
                name: "UserRoles");

            migrationBuilder.DropTable(
                name: "DefectTypes");

            migrationBuilder.DropTable(
                name: "QcFruitReadings");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "Grades");

            migrationBuilder.DropTable(
                name: "QcSamples");

            migrationBuilder.DropTable(
                name: "StarchScaleValues");

            migrationBuilder.DropTable(
                name: "QcStations");

            migrationBuilder.DropTable(
                name: "Receipts");

            migrationBuilder.DropTable(
                name: "SampleTypes");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "StarchScales");

            migrationBuilder.DropTable(
                name: "Rooms");

            migrationBuilder.DropTable(
                name: "FruitProfiles");

            migrationBuilder.DropTable(
                name: "Warehouses");
        }
    }
}
