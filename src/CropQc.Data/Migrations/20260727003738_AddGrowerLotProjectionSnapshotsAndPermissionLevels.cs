using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGrowerLotProjectionSnapshotsAndPermissionLevels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AdditionalExpectedBinsSnapshot",
                table: "RunProjectionSources",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContributingReceiptIdsJson",
                table: "RunProjectionSources",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"),
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContributingSampleIdsJson",
                table: "RunProjectionSources",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"),
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GrowerLotKeySnapshot",
                table: "RunProjectionSources",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(300)", "character varying(300)"),
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastRefreshedAt",
                table: "RunProjectionSources",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "datetimeoffset", "timestamp with time zone"),
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptWeightingSnapshotJson",
                table: "RunProjectionSources",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"),
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReceivedBinsSnapshot",
                table: "RunProjectionSources",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefreshHistoryJson",
                table: "RunProjectionSources",
                type: MigrationProviderTypes.StoreType(migrationBuilder, "nvarchar(max)", "text"),
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RunProjectionSources_GrowerLotKeySnapshot",
                table: "RunProjectionSources",
                column: "GrowerLotKeySnapshot");

            migrationBuilder.Sql(MigrationProviderTypes.Sql(
                migrationBuilder,
                """
                IF OBJECT_ID(N'[UserPageAccesses]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [UserPageAccesses] (
                        [Id] int IDENTITY(1,1) NOT NULL,
                        [UserId] int NOT NULL,
                        [AreaKey] nvarchar(100) NOT NULL,
                        [AccessLevel] nvarchar(25) NOT NULL,
                        [UpdatedByUserId] int NULL,
                        [UpdatedAt] datetimeoffset NOT NULL CONSTRAINT [DF_UserPageAccesses_UpdatedAt] DEFAULT SYSDATETIMEOFFSET(),
                        CONSTRAINT [PK_UserPageAccesses] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_UserPageAccesses_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE CASCADE,
                        CONSTRAINT [FK_UserPageAccesses_Users_UpdatedByUserId] FOREIGN KEY ([UpdatedByUserId]) REFERENCES [Users] ([Id]) ON DELETE SET NULL
                    );
                    CREATE UNIQUE INDEX [IX_UserPageAccesses_UserId_AreaKey] ON [UserPageAccesses] ([UserId], [AreaKey]);
                    CREATE INDEX [IX_UserPageAccesses_UpdatedByUserId] ON [UserPageAccesses] ([UpdatedByUserId]);
                END;

                UPDATE [UserPageAccesses]
                SET [AccessLevel] = 'Create'
                WHERE LOWER([AccessLevel]) = 'edit';

                INSERT INTO [UserPageAccesses] ([UserId], [AreaKey], [AccessLevel], [UpdatedByUserId], [UpdatedAt])
                SELECT legacy.[UserId], 'receipts', 'Admin', NULL, SYSDATETIMEOFFSET()
                FROM [UserPageAccesses] legacy
                WHERE legacy.[AreaKey] = 'receipt-delete'
                  AND LOWER(legacy.[AccessLevel]) = 'admin'
                  AND NOT EXISTS (
                      SELECT 1 FROM [UserPageAccesses] existing
                      WHERE existing.[UserId] = legacy.[UserId] AND existing.[AreaKey] = 'receipts'
                  );

                INSERT INTO [UserPageAccesses] ([UserId], [AreaKey], [AccessLevel], [UpdatedByUserId], [UpdatedAt])
                SELECT legacy.[UserId], 'receipts', 'Create', NULL, SYSDATETIMEOFFSET()
                FROM [UserPageAccesses] legacy
                WHERE legacy.[AreaKey] = 'receipt-edit'
                  AND LOWER(legacy.[AccessLevel]) IN ('create', 'admin')
                  AND NOT EXISTS (
                      SELECT 1 FROM [UserPageAccesses] existing
                      WHERE existing.[UserId] = legacy.[UserId] AND existing.[AreaKey] = 'receipts'
                  );

                UPDATE receipts
                SET receipts.[AccessLevel] = 'Admin'
                FROM [UserPageAccesses] receipts
                WHERE receipts.[AreaKey] = 'receipts'
                  AND EXISTS (
                      SELECT 1 FROM [UserPageAccesses] legacy
                      WHERE legacy.[UserId] = receipts.[UserId]
                        AND legacy.[AreaKey] = 'receipt-delete'
                        AND LOWER(legacy.[AccessLevel]) = 'admin'
                  );

                UPDATE receipts
                SET receipts.[AccessLevel] = 'Create'
                FROM [UserPageAccesses] receipts
                WHERE receipts.[AreaKey] = 'receipts'
                  AND LOWER(receipts.[AccessLevel]) IN ('none', 'view')
                  AND EXISTS (
                      SELECT 1 FROM [UserPageAccesses] legacy
                      WHERE legacy.[UserId] = receipts.[UserId]
                        AND legacy.[AreaKey] = 'receipt-edit'
                        AND LOWER(legacy.[AccessLevel]) IN ('create', 'admin')
                  );

                INSERT INTO [UserPageAccesses] ([UserId], [AreaKey], [AccessLevel], [UpdatedByUserId], [UpdatedAt])
                SELECT source.[UserId], mapping.[NewArea], source.[AccessLevel], NULL, SYSDATETIMEOFFSET()
                FROM (VALUES
                    ('qc-reports', 'daily-qc'),
                    ('projection-planner', 'bins-run'),
                    ('projection-outcome', 'bins-run'),
                    ('transfers', 'room-transactions'),
                    ('true-up', 'room-transactions'),
                    ('inventory', 'current-lots'),
                    ('orchard-recipients', 'configuration'),
                    ('orchard-managers', 'configuration'),
                    ('permission-matrix', 'users'),
                    ('facilities', 'master-data'),
                    ('varieties', 'master-data'),
                    ('grades', 'master-data'),
                    ('defects', 'master-data'),
                    ('size-configuration', 'master-data'),
                    ('email-configuration', 'configuration'),
                    ('backup-history', 'backups'),
                    ('audit-history', 'master-data'),
                    ('import-tools', 'master-data'),
                    ('export-tools', 'master-data')
                ) mapping([NewArea], [LegacyArea])
                INNER JOIN [UserPageAccesses] source ON source.[AreaKey] = mapping.[LegacyArea]
                WHERE NOT EXISTS (
                    SELECT 1 FROM [UserPageAccesses] existing
                    WHERE existing.[UserId] = source.[UserId] AND existing.[AreaKey] = mapping.[NewArea]
                );
                """,
                """
                CREATE TABLE IF NOT EXISTS "UserPageAccesses" (
                    "Id" integer GENERATED BY DEFAULT AS IDENTITY,
                    "UserId" integer NOT NULL,
                    "AreaKey" character varying(100) NOT NULL,
                    "AccessLevel" character varying(25) NOT NULL,
                    "UpdatedByUserId" integer NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL DEFAULT now(),
                    CONSTRAINT "PK_UserPageAccesses" PRIMARY KEY ("Id"),
                    CONSTRAINT "FK_UserPageAccesses_Users_UserId" FOREIGN KEY ("UserId") REFERENCES "Users" ("Id") ON DELETE CASCADE,
                    CONSTRAINT "FK_UserPageAccesses_Users_UpdatedByUserId" FOREIGN KEY ("UpdatedByUserId") REFERENCES "Users" ("Id") ON DELETE SET NULL
                );
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_UserPageAccesses_UserId_AreaKey" ON "UserPageAccesses" ("UserId", "AreaKey");
                CREATE INDEX IF NOT EXISTS "IX_UserPageAccesses_UpdatedByUserId" ON "UserPageAccesses" ("UpdatedByUserId");

                UPDATE "UserPageAccesses"
                SET "AccessLevel" = 'Create'
                WHERE lower("AccessLevel") = 'edit';

                INSERT INTO "UserPageAccesses" ("UserId", "AreaKey", "AccessLevel", "UpdatedByUserId", "UpdatedAt")
                SELECT legacy."UserId", 'receipts', 'Admin', NULL, now()
                FROM "UserPageAccesses" legacy
                WHERE legacy."AreaKey" = 'receipt-delete'
                  AND lower(legacy."AccessLevel") = 'admin'
                  AND NOT EXISTS (
                      SELECT 1 FROM "UserPageAccesses" existing
                      WHERE existing."UserId" = legacy."UserId" AND existing."AreaKey" = 'receipts'
                  );

                INSERT INTO "UserPageAccesses" ("UserId", "AreaKey", "AccessLevel", "UpdatedByUserId", "UpdatedAt")
                SELECT legacy."UserId", 'receipts', 'Create', NULL, now()
                FROM "UserPageAccesses" legacy
                WHERE legacy."AreaKey" = 'receipt-edit'
                  AND lower(legacy."AccessLevel") IN ('create', 'admin')
                  AND NOT EXISTS (
                      SELECT 1 FROM "UserPageAccesses" existing
                      WHERE existing."UserId" = legacy."UserId" AND existing."AreaKey" = 'receipts'
                  );

                UPDATE "UserPageAccesses" AS receipts
                SET "AccessLevel" = 'Admin'
                WHERE receipts."AreaKey" = 'receipts'
                  AND EXISTS (
                      SELECT 1 FROM "UserPageAccesses" legacy
                      WHERE legacy."UserId" = receipts."UserId"
                        AND legacy."AreaKey" = 'receipt-delete'
                        AND lower(legacy."AccessLevel") = 'admin'
                  );

                UPDATE "UserPageAccesses" AS receipts
                SET "AccessLevel" = 'Create'
                WHERE receipts."AreaKey" = 'receipts'
                  AND lower(receipts."AccessLevel") IN ('none', 'view')
                  AND EXISTS (
                      SELECT 1 FROM "UserPageAccesses" legacy
                      WHERE legacy."UserId" = receipts."UserId"
                        AND legacy."AreaKey" = 'receipt-edit'
                        AND lower(legacy."AccessLevel") IN ('create', 'admin')
                  );

                INSERT INTO "UserPageAccesses" ("UserId", "AreaKey", "AccessLevel", "UpdatedByUserId", "UpdatedAt")
                SELECT source."UserId", mapping."NewArea", source."AccessLevel", NULL, now()
                FROM (VALUES
                    ('qc-reports', 'daily-qc'),
                    ('projection-planner', 'bins-run'),
                    ('projection-outcome', 'bins-run'),
                    ('transfers', 'room-transactions'),
                    ('true-up', 'room-transactions'),
                    ('inventory', 'current-lots'),
                    ('orchard-recipients', 'configuration'),
                    ('orchard-managers', 'configuration'),
                    ('permission-matrix', 'users'),
                    ('facilities', 'master-data'),
                    ('varieties', 'master-data'),
                    ('grades', 'master-data'),
                    ('defects', 'master-data'),
                    ('size-configuration', 'master-data'),
                    ('email-configuration', 'configuration'),
                    ('backup-history', 'backups'),
                    ('audit-history', 'master-data'),
                    ('import-tools', 'master-data'),
                    ('export-tools', 'master-data')
                ) AS mapping("NewArea", "LegacyArea")
                INNER JOIN "UserPageAccesses" source ON source."AreaKey" = mapping."LegacyArea"
                WHERE NOT EXISTS (
                    SELECT 1 FROM "UserPageAccesses" existing
                    WHERE existing."UserId" = source."UserId" AND existing."AreaKey" = mapping."NewArea"
                );
                """));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_RunProjectionSources_GrowerLotKeySnapshot",
                table: "RunProjectionSources");

            migrationBuilder.DropColumn(
                name: "AdditionalExpectedBinsSnapshot",
                table: "RunProjectionSources");

            migrationBuilder.DropColumn(
                name: "ContributingReceiptIdsJson",
                table: "RunProjectionSources");

            migrationBuilder.DropColumn(
                name: "ContributingSampleIdsJson",
                table: "RunProjectionSources");

            migrationBuilder.DropColumn(
                name: "GrowerLotKeySnapshot",
                table: "RunProjectionSources");

            migrationBuilder.DropColumn(
                name: "LastRefreshedAt",
                table: "RunProjectionSources");

            migrationBuilder.DropColumn(
                name: "ReceiptWeightingSnapshotJson",
                table: "RunProjectionSources");

            migrationBuilder.DropColumn(
                name: "ReceivedBinsSnapshot",
                table: "RunProjectionSources");

            migrationBuilder.DropColumn(
                name: "RefreshHistoryJson",
                table: "RunProjectionSources");
        }
    }
}
