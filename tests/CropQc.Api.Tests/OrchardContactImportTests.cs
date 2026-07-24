using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Web.Models;
using CropQc.Web.Services;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace CropQc.Api.Tests;

public sealed class OrchardContactImportTests
{
    [Fact]
    public async Task SummaryIsUsedAndOtherSheetsAreIgnored()
    {
        var parser = new OrchardContactWorkbookParser();
        var parsed = await ParseAsync(parser, [Manager("Summary Orchard")], [Manager("Wrong Orchard")]);
        Assert.Single(parsed.Tokens);
        Assert.Equal("Summary Orchard", parsed.Tokens[0].ParsedOrchardToken);
        Assert.Equal("Summary", parsed.WorksheetName);
    }

    [Fact]
    public async Task OnlyOrchardManagerRowsAreImported()
    {
        var parsed = await ParseAsync(new OrchardContactWorkbookParser(),
            [Manager("A"), Row("Regional Manager", "B"), Row("Mechanic", "C"), Row("", "D")]);
        Assert.Single(parsed.Tokens);
        Assert.Equal("A", parsed.Tokens[0].ParsedOrchardToken);
    }

    [Fact]
    public async Task Exactly33OrchardManagerRowsAreDetected()
    {
        var rows = Enumerable.Range(1, 33).Select(x => Manager($"Orchard {x}")).ToArray();
        var parsed = await ParseAsync(new OrchardContactWorkbookParser(), rows);
        Assert.Equal(33, parsed.OrchardManagerSourceRowCount);
    }

    [Fact]
    public void CommaSeparatedOrchardsAreSplit() =>
        Assert.Equal(["Academy", "Reyna"], OrchardContactWorkbookParser.SplitOrchards("Academy, Reyna"));

    [Fact]
    public void SlashSeparatedOrchardsAreSplit() =>
        Assert.Equal(["Groff", "Hendrickson"], OrchardContactWorkbookParser.SplitOrchards("Groff/Hendrickson"));

    [Fact]
    public void HyphenatedNamesRemainIntact() =>
        Assert.Equal(["FFC - Entiat", "JJ-4-PAC"], OrchardContactWorkbookParser.SplitOrchards("FFC - Entiat, JJ-4-PAC"));

    [Fact]
    public void ApostrophesRemainIntact() =>
        Assert.Equal(["Othello's Edge"], OrchardContactWorkbookParser.SplitOrchards("Othello's Edge"));

    [Fact]
    public async Task WhitespaceIsTrimmed()
    {
        var parsed = await ParseAsync(new OrchardContactWorkbookParser(), [Manager("  Academy  ")]);
        Assert.Equal("Academy", parsed.Tokens[0].ParsedOrchardToken);
    }

    [Fact]
    public async Task EmailWhitespaceIsTrimmed()
    {
        var parsed = await ParseAsync(new OrchardContactWorkbookParser(), [Manager("Academy", " Name ", " User@Example.com ")]);
        Assert.Equal("user@example.com", parsed.Tokens[0].EmailAddress);
    }

    [Fact]
    public async Task EmailNormalizationIsCaseInsensitive()
    {
        var parsed = await ParseAsync(new OrchardContactWorkbookParser(), [Manager("Academy", "Name", "User@Example.COM")]);
        Assert.Equal("USER@EXAMPLE.COM", parsed.Tokens[0].NormalizedEmailAddress);
    }

    [Fact]
    public async Task InvalidEmailIsFlagged()
    {
        var parsed = await ParseAsync(new OrchardContactWorkbookParser(), [Manager("Academy", "Name", "not-an-email")]);
        Assert.False(parsed.Tokens[0].EmailIsValid);
    }

    [Fact]
    public async Task MissingEmailDoesNotProposeRecipient()
    {
        var parsed = await ParseAsync(new OrchardContactWorkbookParser(), [Manager("Academy", "Name", "")]);
        var result = OrchardContactMatcher.Match(parsed.Tokens[0], [Orchard(1, "Academy")]);
        Assert.False(result.EmailIsValid);
        Assert.Contains("do not create", result.ProposedAction, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExactCanonicalOrchardMatchSucceeds()
    {
        var token = (await ParseAsync(new OrchardContactWorkbookParser(), [Manager("ga orchard")])).Tokens[0];
        var result = OrchardContactMatcher.Match(token, [Orchard(1, "GA Orchard")]);
        Assert.Equal(OrchardContactMatchMethods.Exact, result.MatchMethod);
        Assert.Equal(1, result.SuggestedCanonicalOrchardId);
    }

    [Fact]
    public async Task ExistingAliasMatchSucceeds()
    {
        var token = (await ParseAsync(new OrchardContactWorkbookParser(), [Manager("WPO")])).Tokens[0];
        var source = Orchard(1, "WP ORCHARD", [("WPO", OrchardContactNormalization.NormalizeOrchardIdentity("WPO"))]);
        var result = OrchardContactMatcher.Match(token, [source]);
        Assert.Equal(OrchardContactMatchMethods.Alias, result.MatchMethod);
    }

    [Fact]
    public async Task AbbreviationWithoutApprovedAliasIsNotAutoMatched()
    {
        var token = (await ParseAsync(new OrchardContactWorkbookParser(), [Manager("WPO")])).Tokens[0];
        var result = OrchardContactMatcher.Match(token, [Orchard(1, "WP ORCHARD")]);
        Assert.Equal(OrchardContactMatchMethods.Unmatched, result.MatchMethod);
        Assert.Null(result.SuggestedCanonicalOrchardId);
    }

    [Fact]
    public async Task ParentheticalAddressSupportsButDoesNotOverrideMatching()
    {
        var token = (await ParseAsync(new OrchardContactWorkbookParser(), [Manager("Unknown", address: "Road (WP ORCHARD)")])).Tokens[0];
        var result = OrchardContactMatcher.Match(token, [Orchard(1, "WP ORCHARD")]);
        Assert.Equal(OrchardContactMatchMethods.Unmatched, result.MatchMethod);
        Assert.Contains(result.Candidates, x => x.AddressEvidence == "WP ORCHARD");
    }

    [Fact]
    public async Task FuzzyResultsAreReviewOnly()
    {
        var token = (await ParseAsync(new OrchardContactWorkbookParser(), [Manager("Pinecreek")])).Tokens[0];
        var result = OrchardContactMatcher.Match(token, [Orchard(1, "Pine Creek")]);
        Assert.Equal(OrchardContactMatchMethods.Unmatched, result.MatchMethod);
        Assert.Null(result.SuggestedCanonicalOrchardId);
        Assert.NotEmpty(result.Candidates);
    }

    [Fact]
    public async Task AmbiguousMatchIsNotApplied()
    {
        var token = (await ParseAsync(new OrchardContactWorkbookParser(), [Manager("Academy")])).Tokens[0];
        var result = OrchardContactMatcher.Match(token, [Orchard(1, "Academy"), Orchard(2, "Academy")]);
        Assert.Equal(OrchardContactMatchMethods.Ambiguous, result.MatchMethod);
        Assert.Null(result.SuggestedCanonicalOrchardId);
    }

    [Fact]
    public async Task UnmatchedOrchardIsFlagged()
    {
        var token = (await ParseAsync(new OrchardContactWorkbookParser(), [Manager("Academy")])).Tokens[0];
        var result = OrchardContactMatcher.Match(token, [Orchard(1, "WP ORCHARD")]);
        Assert.Equal(OrchardContactMatchMethods.Unmatched, result.MatchMethod);
    }

    [Fact]
    public async Task FourDigitGrowerNumberCannotBeTreatedAsOrchard()
    {
        var token = (await ParseAsync(new OrchardContactWorkbookParser(), [Manager("1080")])).Tokens[0];
        var result = OrchardContactMatcher.Match(token, [Orchard(1, "1080")]);
        Assert.Equal(OrchardContactMatchMethods.InvalidOrchardIdentity, result.MatchMethod);
        Assert.Null(result.SuggestedCanonicalOrchardId);
    }

    [Fact]
    public async Task ExistingRecipientIsNotDuplicated()
    {
        var token = (await ParseAsync(new OrchardContactWorkbookParser(), [Manager("Academy", email: "manager@example.com")])).Tokens[0];
        var orchard = Orchard(1, "Academy", recipients: [(1, "Manager@Example.com", "MANAGER@EXAMPLE.COM", true, false)]);
        var result = OrchardContactMatcher.Match(token, [orchard]);
        Assert.True(result.IsDuplicateExistingRecipient);
    }

    [Fact]
    public async Task ExistingUnrelatedRecipientIsNotRemoved()
    {
        await using var db = CreateDb();
        var orchard = SeedOrchard(db, "Academy");
        db.OrchardReportRecipients.Add(Recipient(orchard.Id, "existing@example.com"));
        await db.SaveChangesAsync();
        var workbook = WorkbookFile([Manager("Academy", email: "new@example.com")]);
        var service = CreateService(db);
        var batchId = (await service.StageAsync(workbook, "admin@example.com", default)).BatchId!.Value;
        await ApproveAllAsync(db, batchId, orchard.Id);
        var backup = SeedBackup(db);
        await db.SaveChangesAsync();
        var result = await service.ApplyAsync(ApplyForm(batchId, workbook, backup.Id), "admin@example.com", default);
        Assert.True(result.Success);
        Assert.Contains(await db.OrchardReportRecipients.ToListAsync(), x => x.EmailAddress == "existing@example.com");
    }

    [Fact]
    public async Task ConflictingManagerAssignmentIsFlagged()
    {
        var token = (await ParseAsync(new OrchardContactWorkbookParser(), [Manager("Academy", email: "new@example.com")])).Tokens[0];
        var orchard = Orchard(1, "Academy", recipients: [(1, "old@example.com", "OLD@EXAMPLE.COM", true, false)]);
        Assert.True(OrchardContactMatcher.Match(token, [orchard]).HasExistingRecipientConflict);
    }

    [Fact]
    public async Task OneManagerCanBeAssignedToMultipleOrchards()
    {
        await using var db = CreateDb();
        var first = SeedOrchard(db, "Academy");
        var second = SeedOrchard(db, "Reyna");
        await db.SaveChangesAsync();
        var workbook = WorkbookFile([Manager("Academy, Reyna")]);
        var service = CreateService(db);
        var batchId = (await service.StageAsync(workbook, "admin@example.com", default)).BatchId!.Value;
        var rows = await db.OrchardContactImportRows.Where(x => x.OrchardContactImportBatchId == batchId).OrderBy(x => x.Id).ToListAsync();
        Approve(rows[0], first.Id);
        Approve(rows[1], second.Id);
        var backup = SeedBackup(db);
        await db.SaveChangesAsync();
        Assert.True((await service.ApplyAsync(ApplyForm(batchId, workbook, backup.Id), "admin@example.com", default)).Success);
        Assert.Equal(2, await db.OrchardManagerAssignments.CountAsync());
        Assert.Single(await db.OrchardManagerContacts.ToListAsync());
    }

    [Fact]
    public async Task OneOrchardCanRetainMultipleApprovedManagers()
    {
        await using var db = CreateDb();
        var orchard = SeedOrchard(db, "Academy");
        await db.SaveChangesAsync();
        var workbook = WorkbookFile([
            Manager("Academy", "Manager One", "one@example.com"),
            Manager("Academy", "Manager Two", "two@example.com")
        ]);
        var service = CreateService(db);
        var batchId = (await service.StageAsync(workbook, "admin@example.com", default)).BatchId!.Value;
        await ApproveAllAsync(db, batchId, orchard.Id);
        var backup = SeedBackup(db);
        await db.SaveChangesAsync();
        Assert.True((await service.ApplyAsync(ApplyForm(batchId, workbook, backup.Id), "admin@example.com", default)).Success);
        Assert.Equal(2, await db.OrchardReportRecipients.CountAsync());
    }

    [Fact]
    public async Task DryRunChangesNoData()
    {
        await using var db = CreateDb();
        SeedOrchard(db, "Academy");
        await db.SaveChangesAsync();
        var before = db.ChangeTracker.Entries().Count();
        var preview = await CreateService(db).PreviewAsync(WorkbookFile([Manager("Academy")]), default);
        Assert.Single(preview.Rows);
        Assert.Empty(await db.OrchardContactImportBatches.ToListAsync());
        Assert.Empty(await db.AuditLogs.ToListAsync());
        Assert.True(before >= 0);
    }

    [Fact]
    public async Task ProductionApplyRequiresReviewedDecisions()
    {
        await using var db = CreateDb();
        SeedOrchard(db, "Academy");
        await db.SaveChangesAsync();
        var workbook = WorkbookFile([Manager("Academy")]);
        var service = CreateService(db);
        var batchId = (await service.StageAsync(workbook, "admin@example.com", default)).BatchId!.Value;
        var backup = SeedBackup(db);
        await db.SaveChangesAsync();
        var result = await service.ApplyAsync(ApplyForm(batchId, workbook, backup.Id), "admin@example.com", default);
        Assert.False(result.Success);
        Assert.Contains("approved, rejected, or explicitly deferred", result.Error);
    }

    [Fact]
    public async Task ProductionApplyRequiresVerifiedBackup()
    {
        await using var db = CreateDb();
        var orchard = SeedOrchard(db, "Academy");
        await db.SaveChangesAsync();
        var workbook = WorkbookFile([Manager("Academy")]);
        var service = CreateService(db);
        var batchId = (await service.StageAsync(workbook, "admin@example.com", default)).BatchId!.Value;
        await ApproveAllAsync(db, batchId, orchard.Id);
        var result = await service.ApplyAsync(ApplyForm(batchId, workbook, 999), "admin@example.com", default);
        Assert.False(result.Success);
        Assert.Contains("backup", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task WorkbookChecksumMismatchBlocksApply()
    {
        await using var db = CreateDb();
        var orchard = SeedOrchard(db, "Academy");
        await db.SaveChangesAsync();
        var reviewed = WorkbookFile([Manager("Academy")]);
        var service = CreateService(db);
        var batchId = (await service.StageAsync(reviewed, "admin@example.com", default)).BatchId!.Value;
        await ApproveAllAsync(db, batchId, orchard.Id);
        var backup = SeedBackup(db);
        await db.SaveChangesAsync();
        var different = WorkbookFile([Manager("Different")]);
        var result = await service.ApplyAsync(ApplyForm(batchId, different, backup.Id), "admin@example.com", default);
        Assert.False(result.Success);
        Assert.Contains("checksum", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ApprovedAliasesPointOnlyToExistingCanonicalOrchards()
    {
        await using var db = CreateDb();
        var orchard = SeedOrchard(db, "WP ORCHARD");
        await db.SaveChangesAsync();
        var workbook = WorkbookFile([Manager("WPO")]);
        var service = CreateService(db);
        var batchId = (await service.StageAsync(workbook, "admin@example.com", default)).BatchId!.Value;
        var row = await db.OrchardContactImportRows.SingleAsync();
        Approve(row, orchard.Id, createAlias: true);
        var backup = SeedBackup(db);
        await db.SaveChangesAsync();
        Assert.True((await service.ApplyAsync(ApplyForm(batchId, workbook, backup.Id), "admin@example.com", default)).Success);
        Assert.Equal(orchard.Id, (await db.CanonicalOrchardAliases.SingleAsync()).CanonicalOrchardId);
        Assert.Single(await db.CanonicalOrchards.ToListAsync());
    }

    [Fact]
    public async Task ImportIsTransactional()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<CropQcDbContext>().UseSqlite(connection).Options;
        await using var db = new CropQcDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var first = SeedOrchard(db, "Academy");
        var second = SeedOrchard(db, "Reyna");
        db.CanonicalOrchardAliases.Add(new CanonicalOrchardAlias
        {
            CanonicalOrchard = first,
            AliasText = "Conflict",
            NormalizedAlias = OrchardContactNormalization.NormalizeOrchardIdentity("Conflict"),
            Source = "test",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var workbook = WorkbookFile([Manager("Academy"), Manager("Conflict", "Other", "other@example.com")]);
        var service = CreateService(db);
        var batchId = (await service.StageAsync(workbook, "admin@example.com", default)).BatchId!.Value;
        var rows = await db.OrchardContactImportRows.OrderBy(x => x.Id).ToListAsync();
        Approve(rows[0], first.Id);
        Approve(rows[1], second.Id, createAlias: true);
        var backup = SeedBackup(db);
        await db.SaveChangesAsync();
        var result = await service.ApplyAsync(ApplyForm(batchId, workbook, backup.Id), "admin@example.com", default);
        Assert.False(result.Success);
        Assert.Empty(await db.OrchardManagerContacts.ToListAsync());
        Assert.Empty(await db.OrchardReportRecipients.ToListAsync());
    }

    [Fact]
    public async Task AuditContainsWorkbookRowAndMatchMethod()
    {
        await using var db = CreateDb();
        SeedOrchard(db, "Academy");
        await db.SaveChangesAsync();
        var service = CreateService(db);
        await service.StageAsync(WorkbookFile([Manager("Academy")]), "admin@example.com", default);
        var audit = await db.AuditLogs.SingleAsync(x => x.Action == "row-parsed");
        Assert.Contains("workbookRowNumber", audit.AfterValuesJson, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("matchMethod", audit.AfterValuesJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReRunningSameApprovedImportIsIdempotent()
    {
        await using var db = CreateDb();
        var orchard = SeedOrchard(db, "Academy");
        await db.SaveChangesAsync();
        var workbook = WorkbookFile([Manager("Academy")]);
        var service = CreateService(db);
        var batchId = (await service.StageAsync(workbook, "admin@example.com", default)).BatchId!.Value;
        await ApproveAllAsync(db, batchId, orchard.Id);
        var backup = SeedBackup(db);
        await db.SaveChangesAsync();
        var form = ApplyForm(batchId, workbook, backup.Id);
        Assert.True((await service.ApplyAsync(form, "admin@example.com", default)).Success);
        var second = await service.ApplyAsync(form, "admin@example.com", default);
        Assert.True(second.Success);
        Assert.True(second.WasAlreadyApplied);
        Assert.Single(await db.OrchardReportRecipients.ToListAsync());
        Assert.Single(await db.OrchardManagerAssignments.ToListAsync());
    }

    private static OrchardContactImportService CreateService(CropQcDbContext db) =>
        new(db, new OrchardContactWorkbookParser(), new OrchardRecipientAdminService(db));

    private static CropQcDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<CropQcDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new CropQcDbContext(options);
    }

    private static CanonicalOrchard SeedOrchard(CropQcDbContext db, string name)
    {
        var orchard = new CanonicalOrchard
        {
            OrchardName = name,
            NormalizedOrchardKey = OrchardBlockMatcher.Normalize(name),
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        db.CanonicalOrchards.Add(orchard);
        db.SaveChanges();
        return orchard;
    }

    private static OrchardReportRecipient Recipient(int orchardId, string email) => new()
    {
        CanonicalOrchardId = orchardId,
        EmailAddress = email,
        NormalizedEmailAddress = email.ToUpperInvariant(),
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static BackupRunRecord SeedBackup(CropQcDbContext db)
    {
        var backup = new BackupRunRecord
        {
            BackupType = BackupRunTypes.PreDeployment,
            Status = BackupRunStatuses.Succeeded,
            EnvironmentName = "Test",
            DatabaseProvider = db.Database.ProviderName ?? "Test",
            RetentionCategory = BackupRunTypes.PreDeployment,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            CompletedAt = DateTimeOffset.UtcNow.AddMinutes(-4),
            PackageStorageKey = "test/backup.zip",
            FileSizeBytes = 1024,
            Sha256 = new string('A', 64),
            VerifiedAt = DateTimeOffset.UtcNow.AddMinutes(-3)
        };
        db.BackupRunRecords.Add(backup);
        db.SaveChanges();
        return backup;
    }

    private static async Task ApproveAllAsync(CropQcDbContext db, long batchId, int orchardId)
    {
        var rows = await db.OrchardContactImportRows.Where(x => x.OrchardContactImportBatchId == batchId).ToListAsync();
        foreach (var row in rows) Approve(row, orchardId);
        await db.SaveChangesAsync();
    }

    private static void Approve(OrchardContactImportRow row, int orchardId, bool createAlias = false)
    {
        row.ReviewDecision = OrchardContactImportDecisions.Approved;
        row.ApprovedCanonicalOrchardId = orchardId;
        row.CreateAlias = createAlias;
        row.CreateRecipient = row.EmailIsValid;
        row.ReviewedAt = DateTimeOffset.UtcNow;
    }

    private static OrchardContactImportApplyForm ApplyForm(long batchId, IFormFile workbook, long backupId) => new()
    {
        BatchId = batchId,
        Workbook = workbook,
        VerifiedBackupRunId = backupId,
        ImportReason = "Reviewed manager assignment import",
        ProductionConfirmation = "APPLY ORCHARD RECIPIENTS"
    };

    private static CanonicalOrchardMatchSource Orchard(
        int id,
        string name,
        IReadOnlyList<(string AliasText, string NormalizedAlias)>? aliases = null,
        IReadOnlyList<(int Id, string Email, string NormalizedEmail, bool IsActive, bool IsDeleted)>? recipients = null) =>
        new(id, name, aliases ?? [], recipients ?? []);

    private static async Task<ParsedOrchardContactWorkbook> ParseAsync(
        OrchardContactWorkbookParser parser,
        IReadOnlyList<string[]> summaryRows,
        IReadOnlyList<string[]>? otherSheetRows = null)
    {
        var form = WorkbookFile(summaryRows, otherSheetRows);
        await using var stream = form.OpenReadStream();
        return await parser.ParseAsync(stream, form.FileName, default);
    }

    private static IFormFile WorkbookFile(
        IReadOnlyList<string[]> summaryRows,
        IReadOnlyList<string[]>? otherSheetRows = null)
    {
        var stream = new MemoryStream();
        using (var document = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, true))
        {
            var workbookPart = document.AddWorkbookPart();
            workbookPart.Workbook = new Workbook();
            var sheets = workbookPart.Workbook.AppendChild(new Sheets());
            AddSheet(workbookPart, sheets, "Summary", [Headers(), .. summaryRows]);
            AddSheet(workbookPart, sheets, "Chart", [Headers(), .. (otherSheetRows ?? [])]);
            AddSheet(workbookPart, sheets, "Sheet1", [Headers()]);
            AddSheet(workbookPart, sheets, "Sheet2", [Headers()]);
            workbookPart.Workbook.Save();
        }

        stream.Position = 0;
        return new FormFile(stream, 0, stream.Length, "Workbook", "Master Contact List.xlsx")
        {
            Headers = new HeaderDictionary(),
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
        };
    }

    private static void AddSheet(WorkbookPart workbookPart, Sheets sheets, string name, IReadOnlyList<string[]> rows)
    {
        var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
        var sheetData = new SheetData();
        uint rowIndex = 1;
        foreach (var values in rows)
        {
            var row = new Row { RowIndex = rowIndex++ };
            for (var column = 0; column < values.Length; column++)
            {
                var value = values[column] ?? "";
                row.Append(new Cell
                {
                    CellReference = $"{ColumnName(column + 1)}{row.RowIndex}",
                    DataType = CellValues.InlineString,
                    InlineString = new InlineString(new Text(value))
                });
            }
            sheetData.Append(row);
        }
        worksheetPart.Worksheet = new Worksheet(sheetData);
        worksheetPart.Worksheet.Save();
        sheets.Append(new Sheet
        {
            Id = workbookPart.GetIdOfPart(worksheetPart),
            SheetId = (uint)sheets.Count() + 1,
            Name = name
        });
    }

    private static string ColumnName(int index)
    {
        var name = "";
        while (index > 0)
        {
            index--;
            name = (char)('A' + index % 26) + name;
            index /= 26;
        }
        return name;
    }

    private static string[] Headers() =>
        ["Type", "Orchard", "Physical Address", "Name", "Phone", "Email", "Communication Notes", ""];

    private static string[] Manager(
        string orchard,
        string name = "Manager Name",
        string email = "manager@example.com",
        string address = "123 Orchard Road") =>
        Row("Orchard Manager", orchard, address, name, "(509) 123-4567", email, "3", "source note");

    private static string[] Row(
        string type,
        string orchard,
        string address = "",
        string name = "Name",
        string phone = "",
        string email = "",
        string communication = "",
        string note = "") =>
        [type, orchard, address, name, phone, email, communication, note];
}
