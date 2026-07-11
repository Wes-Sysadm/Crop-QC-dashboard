using CropQc.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CropQc.Data.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(CropQcDbContext))]
    [Migration("20260711213000_AddCanonicalGrowers")]
    public partial class AddCanonicalGrowers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[CanonicalGrowers]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [CanonicalGrowers] (
                        [Id] int IDENTITY(1,1) NOT NULL,
                        [DisplayName] nvarchar(200) NOT NULL,
                        [NormalizedKey] nvarchar(200) NOT NULL,
                        [IsActive] bit NOT NULL,
                        [MergedIntoCanonicalGrowerId] int NULL,
                        [CreatedAt] datetimeoffset NOT NULL,
                        [UpdatedAt] datetimeoffset NOT NULL,
                        CONSTRAINT [PK_CanonicalGrowers] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_CanonicalGrowers_CanonicalGrowers_MergedIntoCanonicalGrowerId] FOREIGN KEY ([MergedIntoCanonicalGrowerId]) REFERENCES [CanonicalGrowers] ([Id]) ON DELETE NO ACTION
                    );
                    CREATE INDEX [IX_CanonicalGrowers_NormalizedKey] ON [CanonicalGrowers] ([NormalizedKey]);
                    CREATE INDEX [IX_CanonicalGrowers_MergedIntoCanonicalGrowerId] ON [CanonicalGrowers] ([MergedIntoCanonicalGrowerId]);
                END

                IF OBJECT_ID(N'[CanonicalGrowerAliases]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [CanonicalGrowerAliases] (
                        [Id] int IDENTITY(1,1) NOT NULL,
                        [CanonicalGrowerId] int NOT NULL,
                        [AliasName] nvarchar(200) NOT NULL,
                        [NormalizedAliasKey] nvarchar(200) NOT NULL,
                        [SourceSystem] nvarchar(100) NULL,
                        [IsActive] bit NOT NULL,
                        [CreatedAt] datetimeoffset NOT NULL,
                        [UpdatedAt] datetimeoffset NOT NULL,
                        CONSTRAINT [PK_CanonicalGrowerAliases] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_CanonicalGrowerAliases_CanonicalGrowers_CanonicalGrowerId] FOREIGN KEY ([CanonicalGrowerId]) REFERENCES [CanonicalGrowers] ([Id]) ON DELETE CASCADE
                    );
                    CREATE INDEX [IX_CanonicalGrowerAliases_NormalizedAliasKey] ON [CanonicalGrowerAliases] ([NormalizedAliasKey]);
                    CREATE INDEX [IX_CanonicalGrowerAliases_CanonicalGrowerId_NormalizedAliasKey] ON [CanonicalGrowerAliases] ([CanonicalGrowerId], [NormalizedAliasKey]);
                END

                IF OBJECT_ID(N'[CanonicalGrowerNumbers]', N'U') IS NULL
                BEGIN
                    CREATE TABLE [CanonicalGrowerNumbers] (
                        [Id] int IDENTITY(1,1) NOT NULL,
                        [CanonicalGrowerId] int NOT NULL,
                        [GrowerNumber] nvarchar(50) NOT NULL,
                        [NormalizedGrowerNumber] nvarchar(50) NOT NULL,
                        [SourceSystem] nvarchar(100) NULL,
                        [Facility] nvarchar(100) NULL,
                        [CropYear] int NULL,
                        [IsActive] bit NOT NULL,
                        [CreatedAt] datetimeoffset NOT NULL,
                        [UpdatedAt] datetimeoffset NOT NULL,
                        CONSTRAINT [PK_CanonicalGrowerNumbers] PRIMARY KEY ([Id]),
                        CONSTRAINT [FK_CanonicalGrowerNumbers_CanonicalGrowers_CanonicalGrowerId] FOREIGN KEY ([CanonicalGrowerId]) REFERENCES [CanonicalGrowers] ([Id]) ON DELETE CASCADE
                    );
                    CREATE INDEX [IX_CanonicalGrowerNumbers_NormalizedGrowerNumber] ON [CanonicalGrowerNumbers] ([NormalizedGrowerNumber]);
                    CREATE INDEX [IX_CanonicalGrowerNumbers_CanonicalGrowerId_NormalizedGrowerNumber] ON [CanonicalGrowerNumbers] ([CanonicalGrowerId], [NormalizedGrowerNumber]);
                END

                DECLARE @now datetimeoffset = SYSDATETIMEOFFSET();

                IF NOT EXISTS (SELECT 1 FROM [CanonicalGrowers] WHERE [NormalizedKey] = N'VANTAGE_ORCHARD')
                BEGIN
                    INSERT INTO [CanonicalGrowers] ([DisplayName], [NormalizedKey], [IsActive], [MergedIntoCanonicalGrowerId], [CreatedAt], [UpdatedAt])
                    VALUES (N'Vantage Orchard', N'VANTAGE_ORCHARD', 1, NULL, @now, @now);
                END

                IF NOT EXISTS (SELECT 1 FROM [CanonicalGrowers] WHERE [NormalizedKey] = N'STAYMAN_FLATS')
                BEGIN
                    INSERT INTO [CanonicalGrowers] ([DisplayName], [NormalizedKey], [IsActive], [MergedIntoCanonicalGrowerId], [CreatedAt], [UpdatedAt])
                    VALUES (N'Stayman Flats', N'STAYMAN_FLATS', 1, NULL, @now, @now);
                END

                DECLARE @vantageId int = (SELECT TOP (1) [Id] FROM [CanonicalGrowers] WHERE [NormalizedKey] = N'VANTAGE_ORCHARD' ORDER BY [Id]);
                DECLARE @staymanId int = (SELECT TOP (1) [Id] FROM [CanonicalGrowers] WHERE [NormalizedKey] = N'STAYMAN_FLATS' ORDER BY [Id]);

                IF @vantageId IS NOT NULL
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM [CanonicalGrowerAliases] WHERE [CanonicalGrowerId] = @vantageId AND [NormalizedAliasKey] = N'VANTAGE_ORCHARD')
                        INSERT INTO [CanonicalGrowerAliases] ([CanonicalGrowerId], [AliasName], [NormalizedAliasKey], [SourceSystem], [IsActive], [CreatedAt], [UpdatedAt]) VALUES (@vantageId, N'Vantage Orchard', N'VANTAGE_ORCHARD', NULL, 1, @now, @now);
                    IF NOT EXISTS (SELECT 1 FROM [CanonicalGrowerAliases] WHERE [CanonicalGrowerId] = @vantageId AND [NormalizedAliasKey] = N'VANTAGE_ORCHARD_NON_CHILEAN')
                        INSERT INTO [CanonicalGrowerAliases] ([CanonicalGrowerId], [AliasName], [NormalizedAliasKey], [SourceSystem], [IsActive], [CreatedAt], [UpdatedAt]) VALUES (@vantageId, N'Vantage Orchard Non Chilean', N'VANTAGE_ORCHARD_NON_CHILEAN', NULL, 1, @now, @now);
                END

                IF @staymanId IS NOT NULL
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM [CanonicalGrowerAliases] WHERE [CanonicalGrowerId] = @staymanId AND [NormalizedAliasKey] = N'STAYMAN_FLATS')
                        INSERT INTO [CanonicalGrowerAliases] ([CanonicalGrowerId], [AliasName], [NormalizedAliasKey], [SourceSystem], [IsActive], [CreatedAt], [UpdatedAt]) VALUES (@staymanId, N'Stayman Flats', N'STAYMAN_FLATS', NULL, 1, @now, @now);
                    IF NOT EXISTS (SELECT 1 FROM [CanonicalGrowerAliases] WHERE [CanonicalGrowerId] = @staymanId AND [NormalizedAliasKey] = N'STAYMAN')
                        INSERT INTO [CanonicalGrowerAliases] ([CanonicalGrowerId], [AliasName], [NormalizedAliasKey], [SourceSystem], [IsActive], [CreatedAt], [UpdatedAt]) VALUES (@staymanId, N'Stayman', N'STAYMAN', NULL, 1, @now, @now);
                    IF NOT EXISTS (SELECT 1 FROM [CanonicalGrowerAliases] WHERE [CanonicalGrowerId] = @staymanId AND [NormalizedAliasKey] = N'STAYMAN_FLATS_NON_CHILEAN')
                        INSERT INTO [CanonicalGrowerAliases] ([CanonicalGrowerId], [AliasName], [NormalizedAliasKey], [SourceSystem], [IsActive], [CreatedAt], [UpdatedAt]) VALUES (@staymanId, N'Stayman Flats Non Chilean', N'STAYMAN_FLATS_NON_CHILEAN', NULL, 1, @now, @now);
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF OBJECT_ID(N'[CanonicalGrowerNumbers]', N'U') IS NOT NULL DROP TABLE [CanonicalGrowerNumbers];
                IF OBJECT_ID(N'[CanonicalGrowerAliases]', N'U') IS NOT NULL DROP TABLE [CanonicalGrowerAliases];
                IF OBJECT_ID(N'[CanonicalGrowers]', N'U') IS NOT NULL DROP TABLE [CanonicalGrowers];
                """);
        }
    }
}
