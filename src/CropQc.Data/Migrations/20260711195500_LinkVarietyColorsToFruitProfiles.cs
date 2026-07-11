using CropQc.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(CropQcDbContext))]
    [Migration("20260711195500_LinkVarietyColorsToFruitProfiles")]
    public partial class LinkVarietyColorsToFruitProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[VarietyColorConfigurations]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [VarietyColorConfigurations] (
                        [Id] int IDENTITY(1,1) NOT NULL,
                        [FruitProfileId] int NULL,
                        [VarietyKey] nvarchar(100) NOT NULL,
                        [VarietyName] nvarchar(150) NOT NULL,
                        [HexColor] nvarchar(7) NOT NULL,
                        [CreatedAt] datetimeoffset NOT NULL,
                        [UpdatedAt] datetimeoffset NOT NULL,
                        [UpdatedByUserId] int NULL,
                        CONSTRAINT [PK_VarietyColorConfigurations] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_VarietyColorConfigurations_FruitProfiles_FruitProfileId] FOREIGN KEY ([FruitProfileId]) REFERENCES [FruitProfiles] ([Id]) ON DELETE SET NULL,
                        CONSTRAINT [FK_VarietyColorConfigurations_Users_UpdatedByUserId] FOREIGN KEY ([UpdatedByUserId]) REFERENCES [Users] ([Id]) ON DELETE SET NULL
                    );
                    CREATE UNIQUE INDEX [IX_VarietyColorConfigurations_VarietyKey] ON [VarietyColorConfigurations] ([VarietyKey]);
                    CREATE INDEX [IX_VarietyColorConfigurations_FruitProfileId] ON [VarietyColorConfigurations] ([FruitProfileId]);
                END

                IF COL_LENGTH(N'[VarietyColorConfigurations]', N'FruitProfileId') IS NULL
                BEGIN
                    ALTER TABLE [VarietyColorConfigurations] ADD [FruitProfileId] int NULL;
                    CREATE INDEX [IX_VarietyColorConfigurations_FruitProfileId] ON [VarietyColorConfigurations] ([FruitProfileId]);
                    ALTER TABLE [VarietyColorConfigurations] WITH CHECK ADD CONSTRAINT [FK_VarietyColorConfigurations_FruitProfiles_FruitProfileId] FOREIGN KEY([FruitProfileId]) REFERENCES [FruitProfiles] ([Id]) ON DELETE SET NULL;
                END

                UPDATE vc
                SET [FruitProfileId] = fp.[Id]
                FROM [VarietyColorConfigurations] vc
                CROSS APPLY (
                    SELECT TOP (1) fp2.[Id]
                    FROM [FruitProfiles] fp2
                    WHERE
                        UPPER(REPLACE(REPLACE(CASE WHEN fp2.[IsOrganic] = 1 AND fp2.[Name] LIKE N'Organic %' THEN SUBSTRING(fp2.[Name], 9, LEN(fp2.[Name])) ELSE fp2.[Name] END, N' ', N''), N'_', N'')) =
                        UPPER(REPLACE(REPLACE(vc.[VarietyName], N' ', N''), N'_', N''))
                        OR UPPER(REPLACE(REPLACE(fp2.[VarietyCode], N' ', N''), N'_', N'')) = UPPER(REPLACE(REPLACE(vc.[VarietyKey], N' ', N''), N'_', N''))
                    ORDER BY fp2.[IsOrganic], CASE WHEN fp2.[Name] = vc.[VarietyName] THEN 0 ELSE 1 END, fp2.[Id]
                ) fp
                WHERE vc.[FruitProfileId] IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF COL_LENGTH(N'[VarietyColorConfigurations]', N'FruitProfileId') IS NOT NULL
                BEGIN
                    IF OBJECT_ID(N'[FK_VarietyColorConfigurations_FruitProfiles_FruitProfileId]', N'F') IS NOT NULL
                    BEGIN
                        ALTER TABLE [VarietyColorConfigurations] DROP CONSTRAINT [FK_VarietyColorConfigurations_FruitProfiles_FruitProfileId];
                    END
                    IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_VarietyColorConfigurations_FruitProfileId' AND object_id = OBJECT_ID(N'[VarietyColorConfigurations]'))
                    BEGIN
                        DROP INDEX [IX_VarietyColorConfigurations_FruitProfileId] ON [VarietyColorConfigurations];
                    END
                    ALTER TABLE [VarietyColorConfigurations] DROP COLUMN [FruitProfileId];
                END
                """);
        }
    }
}
