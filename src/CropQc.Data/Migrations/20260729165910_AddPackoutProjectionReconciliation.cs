using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPackoutProjectionReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // These two operational tables predate the EF migration chain and are
            // normally provisioned by the established startup compatibility path.
            // Provision them only when absent so this additive migration also works
            // on a clean database and at the pre-PR checkpoint.
            migrationBuilder.Sql(MigrationProviderTypes.Sql(
                migrationBuilder,
                """
                IF OBJECT_ID(N'[RoomInventoryAdjustments]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [RoomInventoryAdjustments] (
                        [Id] bigint NOT NULL IDENTITY,
                        [ReceiptId] bigint NULL,
                        [CropYear] int NULL,
                        [RoomDepletionId] bigint NULL,
                        [WarehouseId] int NOT NULL,
                        [RoomId] int NOT NULL,
                        [GrowerLotId] int NULL,
                        [FruitProfileId] int NULL,
                        [GrowerName] nvarchar(200) NOT NULL,
                        [LotNumber] nvarchar(100) NOT NULL,
                        [PoolStart] nvarchar(20) NULL,
                        [VarietyCode] nvarchar(50) NULL,
                        [OldBinCount] int NULL,
                        [ChangeAmount] int NOT NULL,
                        [NewBinCount] int NOT NULL,
                        [AdjustmentType] nvarchar(50) NOT NULL,
                        [Source] nvarchar(150) NULL,
                        [SourceRoomCode] nvarchar(100) NULL,
                        [SourceSubLocation] nvarchar(100) NULL,
                        [InventoryStatus] nvarchar(100) NULL,
                        [Reason] nvarchar(500) NULL,
                        [Notes] nvarchar(1000) NULL,
                        [AdjustmentAt] datetimeoffset NOT NULL,
                        [CreatedByUserId] int NULL,
                        [CreatedAt] datetimeoffset NOT NULL,
                        CONSTRAINT [PK_RoomInventoryAdjustments] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_RoomInventoryAdjustments_Receipts_ReceiptId] FOREIGN KEY ([ReceiptId]) REFERENCES [Receipts] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_RoomInventoryAdjustments_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_RoomInventoryAdjustments_Rooms_RoomId] FOREIGN KEY ([RoomId]) REFERENCES [Rooms] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_RoomInventoryAdjustments_FruitProfiles_FruitProfileId] FOREIGN KEY ([FruitProfileId]) REFERENCES [FruitProfiles] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_RoomInventoryAdjustments_Users_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
                    );
                    CREATE INDEX [IX_RoomInventoryAdjustments_RoomId_AdjustmentAt] ON [RoomInventoryAdjustments] ([RoomId], [AdjustmentAt]);
                    CREATE INDEX [IX_RoomInventoryAdjustments_ReceiptId_AdjustmentAt] ON [RoomInventoryAdjustments] ([ReceiptId], [AdjustmentAt]);
                END;

                IF OBJECT_ID(N'[BinsRunEntries]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [BinsRunEntries] (
                        [Id] bigint NOT NULL IDENTITY,
                        [ReceiptId] bigint NULL,
                        [SourceInventoryAdjustmentId] bigint NULL,
                        [InventoryAdjustmentId] bigint NOT NULL,
                        [WarehouseId] int NOT NULL,
                        [RoomId] int NOT NULL,
                        [GrowerLotId] int NULL,
                        [FruitProfileId] int NULL,
                        [GrowerName] nvarchar(200) NOT NULL,
                        [LotNumber] nvarchar(100) NOT NULL,
                        [PoolStart] nvarchar(20) NULL,
                        [VarietyCode] nvarchar(50) NULL,
                        [InventoryStatus] nvarchar(100) NULL,
                        [PreviousAvailableBins] int NOT NULL,
                        [BinsRun] int NOT NULL,
                        [NewAvailableBins] int NOT NULL,
                        [Notes] nvarchar(1000) NULL,
                        [RunAt] datetimeoffset NOT NULL,
                        [CreatedByUserId] int NULL,
                        [CreatedAt] datetimeoffset NOT NULL,
                        [UpdatedAt] datetimeoffset NULL,
                        [IsReversed] bit NOT NULL CONSTRAINT [DF_BinsRunEntries_IsReversed] DEFAULT 0,
                        [ReversedAt] datetimeoffset NULL,
                        [ReversedByUserId] int NULL,
                        [ReverseReason] nvarchar(1000) NULL,
                        CONSTRAINT [PK_BinsRunEntries] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_BinsRunEntries_Receipts_ReceiptId] FOREIGN KEY ([ReceiptId]) REFERENCES [Receipts] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_BinsRunEntries_SourceInventoryAdjustmentId] FOREIGN KEY ([SourceInventoryAdjustmentId]) REFERENCES [RoomInventoryAdjustments] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_BinsRunEntries_InventoryAdjustmentId] FOREIGN KEY ([InventoryAdjustmentId]) REFERENCES [RoomInventoryAdjustments] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_BinsRunEntries_Warehouses_WarehouseId] FOREIGN KEY ([WarehouseId]) REFERENCES [Warehouses] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_BinsRunEntries_Rooms_RoomId] FOREIGN KEY ([RoomId]) REFERENCES [Rooms] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_BinsRunEntries_FruitProfiles_FruitProfileId] FOREIGN KEY ([FruitProfileId]) REFERENCES [FruitProfiles] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_BinsRunEntries_Users_CreatedByUserId] FOREIGN KEY ([CreatedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_BinsRunEntries_Users_ReversedByUserId] FOREIGN KEY ([ReversedByUserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
                    );
                    CREATE INDEX [IX_BinsRunEntries_RoomId_RunAt] ON [BinsRunEntries] ([RoomId], [RunAt]);
                    CREATE INDEX [IX_BinsRunEntries_ReceiptId_IsReversed] ON [BinsRunEntries] ([ReceiptId], [IsReversed]);
                END;
                """,
                """
                CREATE TABLE IF NOT EXISTS "RoomInventoryAdjustments" (
                    "Id" bigint GENERATED BY DEFAULT AS IDENTITY,
                    "ReceiptId" bigint NULL,
                    "CropYear" integer NULL,
                    "RoomDepletionId" bigint NULL,
                    "WarehouseId" integer NOT NULL,
                    "RoomId" integer NOT NULL,
                    "GrowerLotId" integer NULL,
                    "FruitProfileId" integer NULL,
                    "GrowerName" character varying(200) NOT NULL,
                    "LotNumber" character varying(100) NOT NULL,
                    "PoolStart" character varying(20) NULL,
                    "VarietyCode" character varying(50) NULL,
                    "OldBinCount" integer NULL,
                    "ChangeAmount" integer NOT NULL,
                    "NewBinCount" integer NOT NULL,
                    "AdjustmentType" character varying(50) NOT NULL,
                    "Source" character varying(150) NULL,
                    "SourceRoomCode" character varying(100) NULL,
                    "SourceSubLocation" character varying(100) NULL,
                    "InventoryStatus" character varying(100) NULL,
                    "Reason" character varying(500) NULL,
                    "Notes" character varying(1000) NULL,
                    "AdjustmentAt" timestamp with time zone NOT NULL,
                    "CreatedByUserId" integer NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_RoomInventoryAdjustments" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_RoomInventoryAdjustments_Receipts_ReceiptId" FOREIGN KEY ("ReceiptId") REFERENCES "Receipts" ("Id") ON DELETE SET NULL,
                    CONSTRAINT "FK_RoomInventoryAdjustments_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES "Warehouses" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_RoomInventoryAdjustments_Rooms_RoomId" FOREIGN KEY ("RoomId") REFERENCES "Rooms" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_RoomInventoryAdjustments_FruitProfiles_FruitProfileId" FOREIGN KEY ("FruitProfileId") REFERENCES "FruitProfiles" ("Id") ON DELETE SET NULL,
                    CONSTRAINT "FK_RoomInventoryAdjustments_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
                );
                CREATE INDEX IF NOT EXISTS "IX_RoomInventoryAdjustments_RoomId_AdjustmentAt" ON "RoomInventoryAdjustments" ("RoomId", "AdjustmentAt");
                CREATE INDEX IF NOT EXISTS "IX_RoomInventoryAdjustments_ReceiptId_AdjustmentAt" ON "RoomInventoryAdjustments" ("ReceiptId", "AdjustmentAt");

                CREATE TABLE IF NOT EXISTS "BinsRunEntries" (
                    "Id" bigint GENERATED BY DEFAULT AS IDENTITY,
                    "ReceiptId" bigint NULL,
                    "SourceInventoryAdjustmentId" bigint NULL,
                    "InventoryAdjustmentId" bigint NOT NULL,
                    "WarehouseId" integer NOT NULL,
                    "RoomId" integer NOT NULL,
                    "GrowerLotId" integer NULL,
                    "FruitProfileId" integer NULL,
                    "GrowerName" character varying(200) NOT NULL,
                    "LotNumber" character varying(100) NOT NULL,
                    "PoolStart" character varying(20) NULL,
                    "VarietyCode" character varying(50) NULL,
                    "InventoryStatus" character varying(100) NULL,
                    "PreviousAvailableBins" integer NOT NULL,
                    "BinsRun" integer NOT NULL,
                    "NewAvailableBins" integer NOT NULL,
                    "Notes" character varying(1000) NULL,
                    "RunAt" timestamp with time zone NOT NULL,
                    "CreatedByUserId" integer NULL,
                    "CreatedAt" timestamp with time zone NOT NULL,
                    "UpdatedAt" timestamp with time zone NULL,
                    "IsReversed" boolean NOT NULL DEFAULT FALSE,
                    "ReversedAt" timestamp with time zone NULL,
                    "ReversedByUserId" integer NULL,
                    "ReverseReason" character varying(1000) NULL,
                    CONSTRAINT "PK_BinsRunEntries" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_BinsRunEntries_Receipts_ReceiptId" FOREIGN KEY ("ReceiptId") REFERENCES "Receipts" ("Id") ON DELETE SET NULL,
                    CONSTRAINT "FK_BinsRunEntries_SourceInventoryAdjustmentId" FOREIGN KEY ("SourceInventoryAdjustmentId") REFERENCES "RoomInventoryAdjustments" ("Id") ON DELETE SET NULL,
                    CONSTRAINT "FK_BinsRunEntries_InventoryAdjustmentId" FOREIGN KEY ("InventoryAdjustmentId") REFERENCES "RoomInventoryAdjustments" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_BinsRunEntries_Warehouses_WarehouseId" FOREIGN KEY ("WarehouseId") REFERENCES "Warehouses" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_BinsRunEntries_Rooms_RoomId" FOREIGN KEY ("RoomId") REFERENCES "Rooms" ("Id") ON DELETE RESTRICT,
                    CONSTRAINT "FK_BinsRunEntries_FruitProfiles_FruitProfileId" FOREIGN KEY ("FruitProfileId") REFERENCES "FruitProfiles" ("Id") ON DELETE SET NULL,
                    CONSTRAINT "FK_BinsRunEntries_Users_CreatedByUserId" FOREIGN KEY ("CreatedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL,
                    CONSTRAINT "FK_BinsRunEntries_Users_ReversedByUserId" FOREIGN KEY ("ReversedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
                );
                CREATE INDEX IF NOT EXISTS "IX_BinsRunEntries_RoomId_RunAt" ON "BinsRunEntries" ("RoomId", "RunAt");
                CREATE INDEX IF NOT EXISTS "IX_BinsRunEntries_ReceiptId_IsReversed" ON "BinsRunEntries" ("ReceiptId", "IsReversed");
                """));

            migrationBuilder.AddColumn<decimal>(
                name: "TotalDefectPercentageSnapshot",
                table: "RunProjectionSources",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(8,4)", "numeric(8,4)"),
                precision: 8,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsLocked",
                table: "RunProjections",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"),
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LockedAt",
                table: "RunProjections",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"),
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LockedByUserId",
                table: "RunProjections",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefectInspectionStatus",
                table: "QcSamples",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"),
                maxLength: 50,
                nullable: false,
                defaultValue: "No defects found");

            migrationBuilder.Sql(MigrationProviderTypes.Sql(
                migrationBuilder,
                """
                UPDATE samples
                SET samples.[DefectInspectionStatus] =
                    CASE WHEN EXISTS (
                        SELECT 1
                        FROM [QcFruitReadings] readings
                        INNER JOIN [QcFruitDefects] defects ON defects.[QcFruitReadingId] = readings.[Id]
                        WHERE readings.[QcSampleId] = samples.[Id]
                    ) THEN 'Defects found' ELSE 'No defects found' END
                FROM [QcSamples] samples;
                """,
                """
                UPDATE "QcSamples" AS samples
                SET "DefectInspectionStatus" =
                    CASE WHEN EXISTS (
                        SELECT 1
                        FROM "QcFruitReadings" AS readings
                        INNER JOIN "QcFruitDefects" AS defects ON defects."QcFruitReadingId" = readings."Id"
                        WHERE readings."QcSampleId" = samples."Id"
                    ) THEN 'Defects found' ELSE 'No defects found' END;
                """));

            migrationBuilder.AddColumn<bool>(
                name: "IsReconciled",
                table: "BinsRunEntries",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"),
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReconciledAt",
                table: "BinsRunEntries",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"),
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReconciledByUserId",
                table: "BinsRunEntries",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"),
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PackCodeDefinitions",
                columns: table => new
                {
                    Id = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(75)", "character varying(75)"), maxLength: 75, nullable: false),
                    NormalizedCode = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(75)", "character varying(75)"), maxLength: 75, nullable: false),
                    DisplayName = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(150)", "character varying(150)"), maxLength: 150, nullable: false),
                    ProductCategory = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    NetWeightPounds = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(10,4)", "numeric(10,4)"), precision: 10, scale: 4, nullable: true),
                    SizeCategory = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    GradeId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    IsActive = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    CreatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true),
                    UpdatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackCodeDefinitions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackCodeDefinitions_Grades_GradeId",
                        column: x => x.GradeId,
                        principalTable: "Grades",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PackCodeDefinitions_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PackCodeDefinitions_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PackoutAnalysisConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    AppleBinWeightPounds = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(10,2)", "numeric(10,2)"), precision: 10, scale: 2, nullable: false, defaultValue: 880m),
                    PearBinWeightPounds = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(10,2)", "numeric(10,2)"), precision: 10, scale: 2, nullable: false, defaultValue: 920m),
                    SizeScoreWeight = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(8,4)", "numeric(8,4)"), precision: 8, scale: 4, nullable: false, defaultValue: 35m),
                    GradeScoreWeight = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(8,4)", "numeric(8,4)"), precision: 8, scale: 4, nullable: false, defaultValue: 35m),
                    PackoutScoreWeight = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(8,4)", "numeric(8,4)"), precision: 8, scale: 4, nullable: false, defaultValue: 21m),
                    JuiceScoreWeight = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(8,4)", "numeric(8,4)"), precision: 8, scale: 4, nullable: false, defaultValue: 3m),
                    PeelerSlicerScoreWeight = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(8,4)", "numeric(8,4)"), precision: 8, scale: 4, nullable: false, defaultValue: 3m),
                    WasteScoreWeight = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(8,4)", "numeric(8,4)"), precision: 8, scale: 4, nullable: false, defaultValue: 3m),
                    CurrentCropYearHistoryWeight = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(8,4)", "numeric(8,4)"), precision: 8, scale: 4, nullable: false, defaultValue: 80m),
                    PriorCropYearHistoryWeight = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(8,4)", "numeric(8,4)"), precision: 8, scale: 4, nullable: false, defaultValue: 20m),
                    UpdatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    UpdatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackoutAnalysisConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackoutAnalysisConfigurations_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PackoutRuns",
                columns: table => new
                {
                    Id = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RunProjectionId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false),
                    BinsRunEntryId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: true),
                    Status = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    FacilitySnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: false),
                    PackingDate = table.Column<DateOnly>(type: MigrationProviderTypes.StoreType(migrationBuilder, "date", "date"), nullable: false),
                    RunNumber = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    LotNumberSnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: false),
                    VarietySnapshot = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: false),
                    IsOrganicSnapshot = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    CropYearSnapshot = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    DumpedBins = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,4)", "numeric(18,4)"), precision: 18, scale: 4, nullable: false),
                    PoundsPerBin = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(10,2)", "numeric(10,2)"), precision: 10, scale: 2, nullable: false),
                    DumpedPounds = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,4)", "numeric(18,4)"), precision: 18, scale: 4, nullable: false),
                    PackedProductPounds = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,4)", "numeric(18,4)"), precision: 18, scale: 4, nullable: false),
                    JuicePounds = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,4)", "numeric(18,4)"), precision: 18, scale: 4, nullable: false),
                    PeelerSlicerPounds = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,4)", "numeric(18,4)"), precision: 18, scale: 4, nullable: false),
                    WastePounds = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,4)", "numeric(18,4)"), precision: 18, scale: 4, nullable: false),
                    SupplementalJuicePounds = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,4)", "numeric(18,4)"), precision: 18, scale: 4, nullable: true),
                    SupplementalPeelerSlicerPounds = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,4)", "numeric(18,4)"), precision: 18, scale: 4, nullable: true),
                    SupplementalWastePounds = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,4)", "numeric(18,4)"), precision: 18, scale: 4, nullable: true),
                    ActualPackoutPercent = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(8,4)", "numeric(8,4)"), precision: 8, scale: 4, nullable: true),
                    ActualJuicePercent = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(8,4)", "numeric(8,4)"), precision: 8, scale: 4, nullable: true),
                    ActualPeelerSlicerPercent = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(8,4)", "numeric(8,4)"), precision: 8, scale: 4, nullable: true),
                    ActualWastePercent = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(8,4)", "numeric(8,4)"), precision: 8, scale: 4, nullable: true),
                    SizeAccuracyScore = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(8,4)", "numeric(8,4)"), precision: 8, scale: 4, nullable: true),
                    GradeAccuracyScore = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(8,4)", "numeric(8,4)"), precision: 8, scale: 4, nullable: true),
                    PackoutAccuracyScore = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(8,4)", "numeric(8,4)"), precision: 8, scale: 4, nullable: true),
                    JuiceAccuracyScore = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(8,4)", "numeric(8,4)"), precision: 8, scale: 4, nullable: true),
                    PeelerSlicerAccuracyScore = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(8,4)", "numeric(8,4)"), precision: 8, scale: 4, nullable: true),
                    WasteAccuracyScore = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(8,4)", "numeric(8,4)"), precision: 8, scale: 4, nullable: true),
                    OverallAccuracyScore = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(8,4)", "numeric(8,4)"), precision: 8, scale: 4, nullable: true),
                    ReconciliationDifferencePounds = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,4)", "numeric(18,4)"), precision: 18, scale: 4, nullable: false),
                    HasReconciliationWarning = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    ReviewNotes = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(2000)", "character varying(2000)"), maxLength: 2000, nullable: true),
                    ProjectionSnapshotJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), nullable: true),
                    ActualDistributionSnapshotJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), nullable: true),
                    AccuracySnapshotJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), nullable: true),
                    ConfigurationSnapshotJson = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"), nullable: true),
                    CalculationVersion = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: true),
                    ConcurrencyVersion = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    CreatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    UpdatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    FinalizedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true),
                    FinalizedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    FinalReportFileName = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(255)", "character varying(255)"), maxLength: 255, nullable: true),
                    FinalReportSha256 = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(64)", "character varying(64)"), maxLength: 64, nullable: true),
                    FinalEmailMessageId = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(250)", "character varying(250)"), maxLength: 250, nullable: true),
                    ReopenedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true),
                    ReopenedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    ReopenReason = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackoutRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackoutRuns_BinsRunEntries_BinsRunEntryId",
                        column: x => x.BinsRunEntryId,
                        principalTable: "BinsRunEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PackoutRuns_RunProjections_RunProjectionId",
                        column: x => x.RunProjectionId,
                        principalTable: "RunProjections",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PackoutRuns_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PackoutRuns_Users_FinalizedByUserId",
                        column: x => x.FinalizedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PackoutRuns_Users_ReopenedByUserId",
                        column: x => x.ReopenedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PackoutRuns_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "PackoutEmailAttempts",
                columns: table => new
                {
                    Id = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PackoutRunId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false),
                    Recipient = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(320)", "character varying(320)"), maxLength: 320, nullable: false),
                    SenderUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    AttemptedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    Succeeded = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    MessageId = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(250)", "character varying(250)"), maxLength: 250, nullable: true),
                    SafeError = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true),
                    IsUpdatedAnalysis = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackoutEmailAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackoutEmailAttempts_PackoutRuns_PackoutRunId",
                        column: x => x.PackoutRunId,
                        principalTable: "PackoutRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PackoutEmailAttempts_Users_SenderUserId",
                        column: x => x.SenderUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PackoutReportSources",
                columns: table => new
                {
                    Id = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PackoutRunId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false),
                    OriginalFileName = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(255)", "character varying(255)"), maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(150)", "character varying(150)"), maxLength: 150, nullable: false),
                    FileSizeBytes = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false),
                    Sha256 = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(64)", "character varying(64)"), maxLength: 64, nullable: false),
                    ParserName = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: false),
                    ParserVersion = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: true),
                    Confidence = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(6,5)", "numeric(6,5)"), precision: 6, scale: 5, nullable: true),
                    SafeDiagnostic = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true),
                    ParsedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackoutReportSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackoutReportSources_PackoutRuns_PackoutRunId",
                        column: x => x.PackoutRunId,
                        principalTable: "PackoutRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PackoutReportLines",
                columns: table => new
                {
                    Id = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1")
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PackoutRunId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: false),
                    PackoutReportSourceId = table.Column<long>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bigint", "bigint"), nullable: true),
                    SourceLineNumber = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: false),
                    RawText = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(2000)", "character varying(2000)"), maxLength: 2000, nullable: false),
                    RawPackCode = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: true),
                    NormalizedPackCode = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(100)", "character varying(100)"), maxLength: 100, nullable: true),
                    PackCodeDefinitionId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    Quantity = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,4)", "numeric(18,4)"), precision: 18, scale: 4, nullable: true),
                    NetWeightPounds = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(10,4)", "numeric(10,4)"), precision: 10, scale: 4, nullable: true),
                    ExtendedWeightPounds = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(18,4)", "numeric(18,4)"), precision: 18, scale: 4, nullable: true),
                    SizeCategory = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    GradeId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true),
                    ProductCategory = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(50)", "character varying(50)"), maxLength: 50, nullable: true),
                    Confidence = table.Column<decimal>(type: MigrationProviderTypes.StoreType(migrationBuilder, "decimal(6,5)", "numeric(6,5)"), precision: 6, scale: 5, nullable: false),
                    RequiresReview = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    NegativeQuantityConfirmed = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    WasCorrected = table.Column<bool>(type: MigrationProviderTypes.StoreType(migrationBuilder, "bit", "boolean"), nullable: false),
                    CorrectionReason = table.Column<string>(type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(1000)", "character varying(1000)"), maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"), nullable: true),
                    UpdatedByUserId = table.Column<int>(type: MigrationProviderTypes.StoreType(migrationBuilder, "int", "integer"), nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackoutReportLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PackoutReportLines_Grades_GradeId",
                        column: x => x.GradeId,
                        principalTable: "Grades",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PackoutReportLines_PackCodeDefinitions_PackCodeDefinitionId",
                        column: x => x.PackCodeDefinitionId,
                        principalTable: "PackCodeDefinitions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_PackoutReportLines_PackoutReportSources_PackoutReportSourceId",
                        column: x => x.PackoutReportSourceId,
                        principalTable: "PackoutReportSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_PackoutReportLines_PackoutRuns_PackoutRunId",
                        column: x => x.PackoutRunId,
                        principalTable: "PackoutRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PackoutReportLines_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_RunProjections_LockedByUserId",
                table: "RunProjections",
                column: "LockedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_BinsRunEntries_ReconciledByUserId",
                table: "BinsRunEntries",
                column: "ReconciledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PackCodeDefinitions_CreatedByUserId",
                table: "PackCodeDefinitions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PackCodeDefinitions_GradeId",
                table: "PackCodeDefinitions",
                column: "GradeId");

            migrationBuilder.CreateIndex(
                name: "IX_PackCodeDefinitions_IsActive_ProductCategory",
                table: "PackCodeDefinitions",
                columns: new[] { "IsActive", "ProductCategory" });

            migrationBuilder.CreateIndex(
                name: "IX_PackCodeDefinitions_NormalizedCode",
                table: "PackCodeDefinitions",
                column: "NormalizedCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackCodeDefinitions_UpdatedByUserId",
                table: "PackCodeDefinitions",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PackoutAnalysisConfigurations_UpdatedByUserId",
                table: "PackoutAnalysisConfigurations",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PackoutEmailAttempts_PackoutRunId_AttemptedAt",
                table: "PackoutEmailAttempts",
                columns: new[] { "PackoutRunId", "AttemptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PackoutEmailAttempts_SenderUserId",
                table: "PackoutEmailAttempts",
                column: "SenderUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PackoutReportLines_GradeId",
                table: "PackoutReportLines",
                column: "GradeId");

            migrationBuilder.CreateIndex(
                name: "IX_PackoutReportLines_NormalizedPackCode",
                table: "PackoutReportLines",
                column: "NormalizedPackCode");

            migrationBuilder.CreateIndex(
                name: "IX_PackoutReportLines_PackCodeDefinitionId",
                table: "PackoutReportLines",
                column: "PackCodeDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_PackoutReportLines_PackoutReportSourceId",
                table: "PackoutReportLines",
                column: "PackoutReportSourceId");

            migrationBuilder.CreateIndex(
                name: "IX_PackoutReportLines_PackoutRunId_ProductCategory",
                table: "PackoutReportLines",
                columns: new[] { "PackoutRunId", "ProductCategory" });

            migrationBuilder.CreateIndex(
                name: "IX_PackoutReportLines_UpdatedByUserId",
                table: "PackoutReportLines",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PackoutReportSources_PackoutRunId_Sha256",
                table: "PackoutReportSources",
                columns: new[] { "PackoutRunId", "Sha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackoutRuns_BinsRunEntryId",
                table: "PackoutRuns",
                column: "BinsRunEntryId",
                unique: true,
                filter: MigrationProviderTypes.Sql(migrationBuilder, "[BinsRunEntryId] IS NOT NULL", "\"BinsRunEntryId\" IS NOT NULL"));

            migrationBuilder.CreateIndex(
                name: "IX_PackoutRuns_CreatedByUserId",
                table: "PackoutRuns",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PackoutRuns_FacilitySnapshot_PackingDate_RunNumber",
                table: "PackoutRuns",
                columns: new[] { "FacilitySnapshot", "PackingDate", "RunNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PackoutRuns_FinalizedByUserId",
                table: "PackoutRuns",
                column: "FinalizedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PackoutRuns_ReopenedByUserId",
                table: "PackoutRuns",
                column: "ReopenedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PackoutRuns_RunProjectionId_Status",
                table: "PackoutRuns",
                columns: new[] { "RunProjectionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PackoutRuns_UpdatedByUserId",
                table: "PackoutRuns",
                column: "UpdatedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_BinsRunEntries_Users_ReconciledByUserId",
                table: "BinsRunEntries",
                column: "ReconciledByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_RunProjections_Users_LockedByUserId",
                table: "RunProjections",
                column: "LockedByUserId",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BinsRunEntries_Users_ReconciledByUserId",
                table: "BinsRunEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_RunProjections_Users_LockedByUserId",
                table: "RunProjections");

            migrationBuilder.DropTable(
                name: "PackoutAnalysisConfigurations");

            migrationBuilder.DropTable(
                name: "PackoutEmailAttempts");

            migrationBuilder.DropTable(
                name: "PackoutReportLines");

            migrationBuilder.DropTable(
                name: "PackCodeDefinitions");

            migrationBuilder.DropTable(
                name: "PackoutReportSources");

            migrationBuilder.DropTable(
                name: "PackoutRuns");

            migrationBuilder.DropIndex(
                name: "IX_RunProjections_LockedByUserId",
                table: "RunProjections");

            migrationBuilder.DropIndex(
                name: "IX_BinsRunEntries_ReconciledByUserId",
                table: "BinsRunEntries");

            migrationBuilder.DropColumn(
                name: "TotalDefectPercentageSnapshot",
                table: "RunProjectionSources");

            migrationBuilder.DropColumn(
                name: "IsLocked",
                table: "RunProjections");

            migrationBuilder.DropColumn(
                name: "LockedAt",
                table: "RunProjections");

            migrationBuilder.DropColumn(
                name: "LockedByUserId",
                table: "RunProjections");

            migrationBuilder.DropColumn(
                name: "DefectInspectionStatus",
                table: "QcSamples");

            migrationBuilder.DropColumn(
                name: "IsReconciled",
                table: "BinsRunEntries");

            migrationBuilder.DropColumn(
                name: "ReconciledAt",
                table: "BinsRunEntries");

            migrationBuilder.DropColumn(
                name: "ReconciledByUserId",
                table: "BinsRunEntries");
        }
    }
}


