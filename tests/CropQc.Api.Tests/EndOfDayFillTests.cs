using CropQc.Data;
using CropQc.Data.Entities;
using CropQc.Shared.Time;
using CropQc.Web.Models;
using CropQc.Web.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace CropQc.Api.Tests;

public sealed class EndOfDayFillTests
{
    [Fact]
    public async Task Preview_UsesOnlyConfiguredRooms_AndReconcilesRoomVarietyGrowerTotals()
    {
        await using var fixture = await Fixture.CreateAsync();
        var preview = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);

        Assert.True(preview.CanSend);
        var room = Assert.Single(preview.Rooms);
        Assert.Equal(145, room.CurrentBins);
        var variety = Assert.Single(room.Varieties);
        Assert.Equal("Gala", variety.Name);
        Assert.Equal(145, variety.Bins);
        Assert.Equal(145, variety.Growers.Sum(x => x.Bins));
        Assert.Equal([Fixture.RoomId], fixture.Inventory.RequestedRoomIds);
        Assert.DoesNotContain(preview.Rooms, x => x.RoomId == Fixture.UnconfiguredRoomId);
        Assert.DoesNotContain(fixture.Db.ChangeTracker.Entries(), x => x.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
    }

    [Fact]
    public async Task Send_RequiresConfirmation_AndRejectsAStalePreview()
    {
        await using var fixture = await Fixture.CreateAsync();
        var preview = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);

        var uncheckedResult = await fixture.Service.SendAsync(Fixture.SenderEmail,
            new EndOfDayFillSendForm { GroupId = 1, PreviewToken = preview.PreviewToken!, PhysicalCountConfirmed = false }, default);
        Assert.False(uncheckedResult.Success);
        Assert.Empty(fixture.Sender.Messages);

        fixture.Inventory.Bins = 146;
        var staleResult = await fixture.Service.SendAsync(Fixture.SenderEmail,
            new EndOfDayFillSendForm { GroupId = 1, PreviewToken = preview.PreviewToken!, PhysicalCountConfirmed = true }, default);
        Assert.False(staleResult.Success);
        Assert.True(staleResult.StalePreview);
        Assert.Empty(fixture.Sender.Messages);
        Assert.Empty(await fixture.Db.EndOfDayFillReportSends.ToListAsync());
    }

    [Fact]
    public async Task SuccessfulSend_UsesLoggedInGmailBoundary_StoresExactHistory_AndBlocksDuplicate()
    {
        await using var fixture = await Fixture.CreateAsync();
        var preview = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);
        var form = new EndOfDayFillSendForm { GroupId = 1, PreviewToken = preview.PreviewToken!, PhysicalCountConfirmed = true };

        var result = await fixture.Service.SendAsync(Fixture.SenderEmail, form, default);

        Assert.True(result.Success);
        var message = Assert.Single(fixture.Sender.Messages);
        Assert.Equal(Fixture.SenderEmail, message.Message.From);
        Assert.Equal("wes@fruitandland.com, jorge@wp-packing.com, rob@earlbrownandsons.com", message.Message.To);
        Assert.Equal("End of Day Fill Report — WP — August 6, 2026", message.Message.Subject);
        Assert.Contains("End of Day Fill Report as of August 6, 2026 — 9:22 PM Pacific", message.Message.HtmlBody);
        Assert.Contains("Grower 1084 — Smith Orchards — 145 bins", message.Message.TextBody);
        Assert.DoesNotContain("pressure", message.Message.TextBody, StringComparison.OrdinalIgnoreCase);

        var stored = Assert.Single(await fixture.Db.EndOfDayFillReportSends.AsNoTracking().ToListAsync());
        Assert.Equal(EndOfDayFillSendStatuses.Succeeded, stored.Status);
        Assert.True(stored.PhysicalCountConfirmed);
        Assert.Equal("fake-gmail-id", stored.GmailMessageId);
        Assert.Equal(message.Message.Subject, stored.Subject);
        Assert.Equal(message.Message.HtmlBody, stored.HtmlBody);
        Assert.Contains("\"currentBins\":145", stored.SnapshotJson);
        Assert.Empty(await fixture.Db.EndOfDayFillSendReservations.ToListAsync());

        var duplicate = await fixture.Service.SendAsync(Fixture.SenderEmail, form, default);
        Assert.False(duplicate.Success);
        Assert.Contains("No report data has changed", duplicate.Message);
        Assert.Single(fixture.Sender.Messages);
    }

    [Fact]
    public async Task MeaningfulInventoryChange_SendsRevision_ButClockChangeAloneDoesNot()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);
        Assert.True((await fixture.Service.SendAsync(Fixture.SenderEmail,
            new EndOfDayFillSendForm { GroupId = 1, PreviewToken = first.PreviewToken!, PhysicalCountConfirmed = true }, default)).Success);

        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddMinutes(20);
        var clockOnly = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);
        var blocked = await fixture.Service.SendAsync(Fixture.SenderEmail,
            new EndOfDayFillSendForm { GroupId = 1, PreviewToken = clockOnly.PreviewToken!, PhysicalCountConfirmed = true }, default);
        Assert.False(blocked.Success);

        fixture.Inventory.Bins = 146;
        var revisionPreview = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);
        var revision = await fixture.Service.SendAsync(Fixture.SenderEmail,
            new EndOfDayFillSendForm { GroupId = 1, PreviewToken = revisionPreview.PreviewToken!, PhysicalCountConfirmed = true }, default);
        Assert.True(revision.Success);
        Assert.StartsWith("REVISION 1 — End of Day Fill Report", fixture.Sender.Messages.Last().Message.Subject);
        Assert.Contains("REVISION 1", fixture.Sender.Messages.Last().Message.HtmlBody);
        Assert.Equal([0, 1], await fixture.Db.EndOfDayFillReportSends.Where(x => x.Status == EndOfDayFillSendStatuses.Succeeded).OrderBy(x => x.RevisionNumber).Select(x => x.RevisionNumber).ToListAsync());
    }

    [Fact]
    public async Task ActiveRecipientChange_IsMeaningfulAndProducesRevision()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);
        Assert.True((await fixture.Service.SendAsync(Fixture.SenderEmail,
            new EndOfDayFillSendForm { GroupId = 1, PreviewToken = first.PreviewToken!, PhysicalCountConfirmed = true }, default)).Success);

        (await fixture.Db.EndOfDayFillReportRecipients.SingleAsync(x => x.EmailAddress == "rob@earlbrownandsons.com")).IsActive = false;
        await fixture.Db.SaveChangesAsync();
        var changed = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);
        var revision = await fixture.Service.SendAsync(Fixture.SenderEmail,
            new EndOfDayFillSendForm { GroupId = 1, PreviewToken = changed.PreviewToken!, PhysicalCountConfirmed = true }, default);

        Assert.True(revision.Success);
        Assert.StartsWith("REVISION 1", fixture.Sender.Messages.Last().Message.Subject);
        Assert.DoesNotContain("rob@earlbrownandsons.com", fixture.Sender.Messages.Last().Message.To);
    }

    [Fact]
    public async Task FailedSend_IsAudited_AndDoesNotAdvanceSuccessfulRevision()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Sender.FailNext = true;
        var preview = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);
        var failed = await fixture.Service.SendAsync(Fixture.SenderEmail,
            new EndOfDayFillSendForm { GroupId = 1, PreviewToken = preview.PreviewToken!, PhysicalCountConfirmed = true }, default);
        Assert.False(failed.Success);
        Assert.Equal(EndOfDayFillSendStatuses.Failed, (await fixture.Db.EndOfDayFillReportSends.SingleAsync()).Status);

        var retryPreview = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);
        var retry = await fixture.Service.SendAsync(Fixture.SenderEmail,
            new EndOfDayFillSendForm { GroupId = 1, PreviewToken = retryPreview.PreviewToken!, PhysicalCountConfirmed = true }, default);
        Assert.True(retry.Success);
        Assert.Equal(0, (await fixture.Db.EndOfDayFillReportSends.SingleAsync(x => x.Status == EndOfDayFillSendStatuses.Succeeded)).RevisionNumber);
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), x => x.Action == "send-failure");
    }

    [Fact]
    public async Task ChangedThenReturnedSnapshot_IsAllowedRelativeToImmediatelyPreviousSuccess()
    {
        await using var fixture = await Fixture.CreateAsync();
        async Task<EndOfDayFillSendResult> SendCurrentAsync()
        {
            var preview = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);
            return await fixture.Service.SendAsync(Fixture.SenderEmail,
                new EndOfDayFillSendForm { GroupId = 1, PreviewToken = preview.PreviewToken!, PhysicalCountConfirmed = true }, default);
        }

        Assert.True((await SendCurrentAsync()).Success);
        fixture.Inventory.Bins = 146;
        Assert.True((await SendCurrentAsync()).Success);
        fixture.Inventory.Bins = 145;
        Assert.True((await SendCurrentAsync()).Success);

        Assert.Equal([0, 1, 2], await fixture.Db.EndOfDayFillReportSends.OrderBy(x => x.RevisionNumber).Select(x => x.RevisionNumber).ToListAsync());
    }

    [Fact]
    public async Task ActiveReservation_BlocksConcurrentDuplicateBeforeGmail()
    {
        await using var fixture = await Fixture.CreateAsync();
        var preview = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);
        var user = await fixture.Db.Users.SingleAsync(x => x.Email == Fixture.SenderEmail);
        var attempt = new EndOfDayFillReportSend
        {
            ReportGroupId = 1,
            ReportGroupName = "WP End of Day Fill",
            Facility = "WP",
            PacificReportDate = new DateOnly(2026, 8, 6),
            RevisionNumber = 0,
            SenderUserId = user.Id,
            SenderEmail = user.Email,
            SenderDisplayName = user.DisplayName,
            RecipientsJson = "[]",
            PhysicalCountConfirmed = true,
            SnapshotHash = new string('a', 64),
            SnapshotJson = "{}",
            Subject = "reserved",
            HtmlBody = "reserved",
            TextBody = "reserved",
            Status = EndOfDayFillSendStatuses.Pending,
            CreatedAt = fixture.Clock.UtcNow,
            AttemptedAt = fixture.Clock.UtcNow
        };
        fixture.Db.EndOfDayFillReportSends.Add(attempt);
        await fixture.Db.SaveChangesAsync();
        fixture.Db.EndOfDayFillSendReservations.Add(new EndOfDayFillSendReservation { ReportGroupId = 1, PacificReportDate = new DateOnly(2026, 8, 6), RevisionNumber = 0, SnapshotHash = attempt.SnapshotHash, SendAttemptId = attempt.Id, CreatedAt = fixture.Clock.UtcNow });
        await fixture.Db.SaveChangesAsync();

        var blocked = await fixture.Service.SendAsync(Fixture.SenderEmail,
            new EndOfDayFillSendForm { GroupId = 1, PreviewToken = preview.PreviewToken!, PhysicalCountConfirmed = true }, default);
        Assert.False(blocked.Success);
        Assert.Contains("already in progress", blocked.Message);
        Assert.Empty(fixture.Sender.Messages);
    }

    [Fact]
    public async Task MissingIdentityCapacityRecipientsOrGmail_FailsClosed()
    {
        await using var fixture = await Fixture.CreateAsync();
        fixture.Inventory.GrowerNumber = "";
        fixture.Inventory.CanonicalName = "";
        (await fixture.Db.Rooms.SingleAsync(x => x.Id == Fixture.RoomId)).CapacityBins = 0;
        foreach (var recipient in await fixture.Db.EndOfDayFillReportRecipients.ToListAsync()) recipient.IsActive = false;
        foreach (var credential in await fixture.Db.UserGoogleCredentials.ToListAsync()) credential.Scope = "openid email";
        await fixture.Db.SaveChangesAsync();

        var preview = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);
        Assert.False(preview.CanSend);
        Assert.Contains(preview.Issues, x => x.Code == "missing-grower");
        Assert.Contains(preview.Issues, x => x.Code == "missing-variety");
        Assert.Contains(preview.Issues, x => x.Code == "invalid-capacity");
        Assert.Contains(preview.Issues, x => x.Code == "no-recipients");
        Assert.Contains(preview.Issues, x => x.Code == "gmail");
    }

    [Fact]
    public async Task GroupConfiguration_DoesNotOwnRoomMembership_AndAuditsUserAssignments()
    {
        await using var fixture = await Fixture.CreateAsync();
        var admin = fixture.CreateAdminService();
        Assert.Null(await admin.SaveGroupAsync(new EndOfDayFillGroupForm { Name = "Other WP", Facility = "WP", IsActive = true }, Fixture.SenderEmail, default));
        Assert.Equal(1, (await admin.GetPageAsync(default)).Groups.Single(x => x.Id == 1).AssignedRoomCount);

        var user = await fixture.Db.Users.SingleAsync(x => x.Email == Fixture.UnassignedEmail);
        Assert.Null(await admin.SaveUserAssignmentsAsync(new EndOfDayFillUserAssignmentsForm { UserId = user.Id, GroupIds = [1] }, Fixture.SenderEmail, default));
        Assert.True(await fixture.Db.EndOfDayFillUserGroupAssignments.AnyAsync(x => x.UserId == user.Id && x.ReportGroupId == 1));
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), x => x.Action == "update-assignments" && x.EntityKey == user.Id.ToString());
    }

    [Fact]
    public async Task AdminConfiguration_CreatesAndEditsGroupsAndRecipientsWithAuditHistory()
    {
        await using var fixture = await Fixture.CreateAsync();
        var admin = fixture.CreateAdminService();

        Assert.Null(await admin.SaveGroupAsync(new EndOfDayFillGroupForm
        {
            Name = "Second WP scope",
            Facility = "WP",
            IsActive = true
        }, Fixture.SenderEmail, default));
        var group = await fixture.Db.EndOfDayFillReportGroups.Include(x => x.Rooms).SingleAsync(x => x.Name == "Second WP scope");
        Assert.Empty(group.Rooms);

        Assert.Null(await admin.SaveGroupAsync(new EndOfDayFillGroupForm
        {
            Id = group.Id,
            Name = "Renamed WP scope",
            Facility = "WP",
            IsActive = false
        }, Fixture.SenderEmail, default));
        Assert.False((await fixture.Db.EndOfDayFillReportGroups.SingleAsync(x => x.Id == group.Id)).IsActive);

        Assert.Null(await admin.SaveRecipientAsync(new EndOfDayFillRecipientForm
        {
            EmailAddress = "QC@FruitAndLand.com",
            IsActive = true,
            SortOrder = 5
        }, Fixture.SenderEmail, default));
        var recipient = await fixture.Db.EndOfDayFillReportRecipients.SingleAsync(x => x.NormalizedEmailAddress == "QC@FRUITANDLAND.COM");
        Assert.Equal("qc@fruitandland.com", recipient.EmailAddress);
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), x => x.EntityName == "end-of-day-fill-report-group" && x.Action == "create");
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), x => x.EntityName == "end-of-day-fill-report-group" && x.Action == "update");
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), x => x.EntityName == "end-of-day-fill-report-recipient" && x.Action == "create");
    }

    [Fact]
    public async Task RoomMasterData_OwnsAssignment_ValidatesFacility_PreservesAndAuditsChanges()
    {
        await using var fixture = await Fixture.CreateAsync();
        var masterData = fixture.CreateMasterDataService();
        var wp = await fixture.Db.Warehouses.SingleAsync(x => x.Code == "WP");
        var ebs = await fixture.Db.Warehouses.SingleAsync(x => x.Code == "EBS");

        Assert.Null(await masterData.SaveMasterDataAsync(new MasterDataEditForm { Type = "rooms", WarehouseId = wp.Id, Code = "WP-NONE", Name = "No report", CapacityBins = 10, IsActive = true }, Fixture.SenderEmail, default));
        var futureRoom = await fixture.Db.Rooms.SingleAsync(x => x.Code == "WP-NONE");
        Assert.Null(futureRoom.EndOfDayFillReportGroupId);
        var futureRoomEdit = (await masterData.GetEditFormAsync("rooms", futureRoom.Id, default))!;
        futureRoomEdit.EndOfDayFillReportGroupId = 1;
        Assert.Null(await masterData.SaveMasterDataAsync(futureRoomEdit, Fixture.SenderEmail, default));
        Assert.Equal(1, (await fixture.Db.Rooms.AsNoTracking().SingleAsync(x => x.Id == futureRoom.Id)).EndOfDayFillReportGroupId);

        Assert.Null(await masterData.SaveMasterDataAsync(new MasterDataEditForm { Type = "rooms", WarehouseId = ebs.Id, Code = "EBS-ASSIGNED", Name = "EBS assigned", CapacityBins = 20, IsActive = true, EndOfDayFillReportGroupId = 2 }, Fixture.SenderEmail, default));
        var assigned = await fixture.Db.Rooms.SingleAsync(x => x.Code == "EBS-ASSIGNED");
        Assert.Equal(2, assigned.EndOfDayFillReportGroupId);

        var invalid = await masterData.SaveMasterDataAsync(new MasterDataEditForm { Type = "rooms", Id = assigned.Id, WarehouseId = ebs.Id, Code = assigned.Code, Name = assigned.Name, CapacityBins = 20, IsActive = true, EndOfDayFillReportGroupId = 1 }, Fixture.SenderEmail, default);
        Assert.Contains("EBS", invalid);
        Assert.Equal(2, (await fixture.Db.Rooms.AsNoTracking().SingleAsync(x => x.Id == assigned.Id)).EndOfDayFillReportGroupId);

        var edit = (await masterData.GetEditFormAsync("rooms", assigned.Id, default))!;
        edit.Name = "Unrelated rename";
        Assert.Null(await masterData.SaveMasterDataAsync(edit, Fixture.SenderEmail, default));
        Assert.Equal(2, (await fixture.Db.Rooms.AsNoTracking().SingleAsync(x => x.Id == assigned.Id)).EndOfDayFillReportGroupId);
        edit.EndOfDayFillReportGroupId = null;
        Assert.Null(await masterData.SaveMasterDataAsync(edit, Fixture.SenderEmail, default));
        Assert.Null((await fixture.Db.Rooms.AsNoTracking().SingleAsync(x => x.Id == assigned.Id)).EndOfDayFillReportGroupId);

        (await fixture.Db.EndOfDayFillReportGroups.SingleAsync(x => x.Id == 2)).IsActive = false;
        await fixture.Db.SaveChangesAsync();
        Assert.Contains("inactive", await masterData.SaveMasterDataAsync(new MasterDataEditForm { Type = "rooms", WarehouseId = ebs.Id, Code = "EBS-INACTIVE", Name = "Inactive", CapacityBins = 10, IsActive = true, EndOfDayFillReportGroupId = 2 }, Fixture.SenderEmail, default));

        var page = await masterData.GetMasterDataAsync("rooms", true, default);
        Assert.Contains("End of Day Fill Report", page.Columns);
        Assert.Contains(page.Items, x => x.Cells.Contains("WP End of Day Fill"));
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), x => x.EntityName == "rooms" && x.AfterValuesJson!.Contains("PreviousEndOfDayFillReportGroup"));
    }

    [Fact]
    public async Task RoomMasterData_PreservesCurrentInactiveAssignment_ButRejectsNewOrIncompatibleUse()
    {
        await using var fixture = await Fixture.CreateAsync();
        var masterData = fixture.CreateMasterDataService();
        var wp = await fixture.Db.Warehouses.SingleAsync(x => x.Code == "WP");
        var ebs = await fixture.Db.Warehouses.SingleAsync(x => x.Code == "EBS");
        var currentGroup = await fixture.Db.EndOfDayFillReportGroups.SingleAsync(x => x.Id == 1);
        var alternateGroup = new EndOfDayFillReportGroup
        {
            Name = "Alternate WP End of Day Fill",
            Facility = "WP",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        fixture.Db.EndOfDayFillReportGroups.Add(alternateGroup);
        await fixture.Db.SaveChangesAsync();

        Assert.Null(await masterData.SaveMasterDataAsync(new MasterDataEditForm
        {
            Type = "rooms",
            WarehouseId = wp.Id,
            Code = "DH-3",
            Name = "Move candidate",
            CapacityBins = 300,
            IsActive = true,
            EndOfDayFillReportGroupId = currentGroup.Id
        }, Fixture.SenderEmail, default));
        var moveCandidate = await fixture.Db.Rooms.SingleAsync(x => x.Code == "DH-3");

        currentGroup.IsActive = false;
        await fixture.Db.SaveChangesAsync();

        var currentRoomForm = (await masterData.GetEditFormAsync("rooms", Fixture.RoomId, default))!;
        var inactiveCurrentOption = Assert.Single(currentRoomForm.EndOfDayFillReportGroups, x => x.Id == currentGroup.Id);
        Assert.False(inactiveCurrentOption.IsActive);
        Assert.True(inactiveCurrentOption.IsCurrentAssignment);
        var addRoomForm = (await masterData.GetMasterDataAsync("rooms", true, default)).EditForm!;
        Assert.DoesNotContain(addRoomForm.EndOfDayFillReportGroups, x => x.Id == currentGroup.Id);
        Assert.Contains(addRoomForm.EndOfDayFillReportGroups, x => x.Id == alternateGroup.Id && x.IsActive);

        currentRoomForm.CapacityBins = 925;
        Assert.Null(await masterData.SaveMasterDataAsync(currentRoomForm, Fixture.SenderEmail, default));
        var afterCapacityEdit = await fixture.Db.Rooms.AsNoTracking().SingleAsync(x => x.Id == Fixture.RoomId);
        Assert.Equal(925, afterCapacityEdit.CapacityBins);
        Assert.Equal(currentGroup.Id, afterCapacityEdit.EndOfDayFillReportGroupId);

        currentRoomForm = (await masterData.GetEditFormAsync("rooms", Fixture.RoomId, default))!;
        currentRoomForm.Name = "Renamed while report inactive";
        Assert.Null(await masterData.SaveMasterDataAsync(currentRoomForm, Fixture.SenderEmail, default));
        var afterNameEdit = await fixture.Db.Rooms.AsNoTracking().SingleAsync(x => x.Id == Fixture.RoomId);
        Assert.Equal("Renamed while report inactive", afterNameEdit.Name);
        Assert.Equal(currentGroup.Id, afterNameEdit.EndOfDayFillReportGroupId);

        var newRoomError = await masterData.SaveMasterDataAsync(new MasterDataEditForm
        {
            Type = "rooms",
            WarehouseId = wp.Id,
            Code = "DH-INACTIVE-NEW",
            Name = "Invalid new assignment",
            CapacityBins = 10,
            IsActive = true,
            EndOfDayFillReportGroupId = currentGroup.Id
        }, Fixture.SenderEmail, default);
        Assert.Contains("inactive", newRoomError);
        Assert.DoesNotContain(await fixture.Db.Rooms.AsNoTracking().ToListAsync(), x => x.Code == "DH-INACTIVE-NEW");

        var differentRoomForm = (await masterData.GetEditFormAsync("rooms", Fixture.UnconfiguredRoomId, default))!;
        differentRoomForm.EndOfDayFillReportGroupId = currentGroup.Id;
        Assert.Contains("inactive", await masterData.SaveMasterDataAsync(differentRoomForm, Fixture.SenderEmail, default));
        Assert.Null((await fixture.Db.Rooms.AsNoTracking().SingleAsync(x => x.Id == Fixture.UnconfiguredRoomId)).EndOfDayFillReportGroupId);

        var incompatibleMove = (await masterData.GetEditFormAsync("rooms", moveCandidate.Id, default))!;
        incompatibleMove.WarehouseId = ebs.Id;
        Assert.Contains("EBS", await masterData.SaveMasterDataAsync(incompatibleMove, Fixture.SenderEmail, default));
        var rejectedMove = await fixture.Db.Rooms.AsNoTracking().SingleAsync(x => x.Id == moveCandidate.Id);
        Assert.Equal(wp.Id, rejectedMove.WarehouseId);
        Assert.Equal(currentGroup.Id, rejectedMove.EndOfDayFillReportGroupId);

        var compatibleMove = (await masterData.GetEditFormAsync("rooms", moveCandidate.Id, default))!;
        compatibleMove.EndOfDayFillReportGroupId = alternateGroup.Id;
        Assert.Null(await masterData.SaveMasterDataAsync(compatibleMove, Fixture.SenderEmail, default));
        Assert.Equal(alternateGroup.Id, (await fixture.Db.Rooms.AsNoTracking().SingleAsync(x => x.Id == moveCandidate.Id)).EndOfDayFillReportGroupId);

        var clearCurrent = (await masterData.GetEditFormAsync("rooms", Fixture.RoomId, default))!;
        clearCurrent.EndOfDayFillReportGroupId = null;
        Assert.Null(await masterData.SaveMasterDataAsync(clearCurrent, Fixture.SenderEmail, default));
        Assert.Null((await fixture.Db.Rooms.AsNoTracking().SingleAsync(x => x.Id == Fixture.RoomId)).EndOfDayFillReportGroupId);

        var currentRoomAudits = await fixture.Db.AuditLogs.AsNoTracking()
            .Where(x => x.EntityName == "rooms" && x.EntityKey == Fixture.RoomId.ToString())
            .OrderBy(x => x.Id)
            .ToListAsync();
        Assert.True(currentRoomAudits.Count >= 3);
        Assert.All(currentRoomAudits.Take(2), audit =>
        {
            Assert.Contains($"\"PreviousEndOfDayFillReportGroupId\":{currentGroup.Id}", audit.AfterValuesJson);
            Assert.Contains($"\"EndOfDayFillReportGroupId\":{currentGroup.Id}", audit.AfterValuesJson);
        });
        Assert.Contains(currentRoomAudits, audit =>
            audit.AfterValuesJson!.Contains($"\"PreviousEndOfDayFillReportGroupId\":{currentGroup.Id}")
            && audit.AfterValuesJson.Contains("\"EndOfDayFillReportGroupId\":null"));
        Assert.Contains(await fixture.Db.AuditLogs.AsNoTracking().ToListAsync(), audit =>
            audit.EntityName == "rooms"
            && audit.EntityKey == moveCandidate.Id.ToString()
            && audit.AfterValuesJson!.Contains($"\"PreviousEndOfDayFillReportGroupId\":{currentGroup.Id}")
            && audit.AfterValuesJson.Contains($"\"EndOfDayFillReportGroupId\":{alternateGroup.Id}"));
    }

    [Fact]
    public async Task EmptyRoomAssignment_ChangesSnapshot_InvalidatesPreview_AndPermitsRevision()
    {
        await using var fixture = await Fixture.CreateAsync();
        var first = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);
        Assert.True((await fixture.Service.SendAsync(Fixture.SenderEmail, new EndOfDayFillSendForm { GroupId = 1, PreviewToken = first.PreviewToken!, PhysicalCountConfirmed = true }, default)).Success);

        var stale = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);
        var emptyRoom = await fixture.Db.Rooms.SingleAsync(x => x.Id == Fixture.UnconfiguredRoomId);
        emptyRoom.EndOfDayFillReportGroupId = 1;
        fixture.Inventory.IncludeUnconfiguredRoom = false;
        await fixture.Db.SaveChangesAsync();

        var staleResult = await fixture.Service.SendAsync(Fixture.SenderEmail, new EndOfDayFillSendForm { GroupId = 1, PreviewToken = stale.PreviewToken!, PhysicalCountConfirmed = true }, default);
        Assert.True(staleResult.StalePreview);
        var changed = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);
        Assert.Single(changed.Rooms);
        Assert.NotEqual(stale.PreviewToken, changed.PreviewToken);
        Assert.Equal([Fixture.RoomId, Fixture.UnconfiguredRoomId], fixture.Inventory.RequestedRoomIds);
        Assert.True((await fixture.Service.SendAsync(Fixture.SenderEmail, new EndOfDayFillSendForm { GroupId = 1, PreviewToken = changed.PreviewToken, PhysicalCountConfirmed = true }, default)).Success);
        Assert.Equal(1, (await fixture.Db.EndOfDayFillReportSends.SingleAsync(x => x.RevisionNumber == 1)).RevisionNumber);
    }

    [Fact]
    public async Task AssignmentChecks_FailClosedForUnassignedUsersAndUnassignedGroups()
    {
        await using var fixture = await Fixture.CreateAsync();

        Assert.True(await fixture.Service.HasActiveAssignmentAsync(Fixture.SenderEmail, default));
        Assert.True(await fixture.Service.HasGroupAssignmentAsync(Fixture.SenderEmail, 1, default));
        Assert.False(await fixture.Service.HasActiveAssignmentAsync(Fixture.UnassignedEmail, default));
        Assert.False(await fixture.Service.HasGroupAssignmentAsync(Fixture.SenderEmail, 2, default));
        Assert.False(await fixture.Service.HasGroupAssignmentAsync(Fixture.UnassignedEmail, 1, default));
    }

    [Fact]
    public async Task RequestCancellationAfterReservation_DoesNotAbandonCriticalSendOrFinalization()
    {
        await using var fixture = await Fixture.CreateAsync();
        using var request = new CancellationTokenSource();
        fixture.Sender.OnSend = token =>
        {
            request.Cancel();
            Assert.False(token.IsCancellationRequested);
        };
        var preview = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, request.Token);

        var result = await fixture.Service.SendAsync(Fixture.SenderEmail,
            new EndOfDayFillSendForm { GroupId = 1, PreviewToken = preview.PreviewToken!, PhysicalCountConfirmed = true }, request.Token);

        Assert.True(result.Success);
        Assert.True(request.IsCancellationRequested);
        Assert.Equal(EndOfDayFillSendStatuses.Succeeded, (await fixture.Db.EndOfDayFillReportSends.SingleAsync()).Status);
        Assert.Empty(await fixture.Db.EndOfDayFillSendReservations.ToListAsync());
    }

    [Fact]
    public async Task StaleReservation_IsVisibleAndBlocksNormalSend()
    {
        await using var fixture = await Fixture.CreateAsync();
        var originalPreview = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);
        var pending = await fixture.CreateUncertainPendingAsync();
        fixture.Clock.UtcNow = fixture.Clock.UtcNow.Add(EndOfDayFillRecoveryPolicy.StaleAfter).AddSeconds(1);

        var preview = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);
        var blocked = await fixture.Service.SendAsync(Fixture.SenderEmail,
            new EndOfDayFillSendForm { GroupId = 1, PreviewToken = originalPreview.PreviewToken!, PhysicalCountConfirmed = true }, default);
        var adminPage = await fixture.CreateAdminService().GetPageAsync(default);

        Assert.False(preview.CanSend);
        Assert.True(preview.PendingAttempt?.IsStale);
        Assert.Equal(pending.Id, preview.PendingAttempt?.SendAttemptId);
        Assert.Contains("uncertain outcome", blocked.Message);
        Assert.Equal(pending.Id, Assert.Single(adminPage.StaleAttempts).SendAttemptId);
        Assert.NotNull(await fixture.CreateAdminService().GetPendingDetailAsync(pending.Id, default));
    }

    [Fact]
    public async Task ConfirmedSent_PromotesPending_ReleasesReservation_AndAdvancesRevisionSafely()
    {
        await using var fixture = await Fixture.CreateAsync();
        var pending = await fixture.CreateUncertainPendingAsync();
        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddMinutes(16);
        var admin = fixture.CreateAdminService();

        var error = await admin.ResolvePendingSendAsync(new EndOfDayFillRecoveryForm
        {
            SendAttemptId = pending.Id,
            Resolution = "confirmed-sent",
            Reason = "Verified in sender Gmail Sent folder.",
            GmailMessageId = "manually-recorded-id",
            Confirmed = true
        }, Fixture.SenderEmail, default);

        Assert.Null(error);
        var stored = await fixture.Db.EndOfDayFillReportSends.SingleAsync(x => x.Id == pending.Id);
        Assert.Equal(EndOfDayFillSendStatuses.Succeeded, stored.Status);
        Assert.Equal(stored.AttemptedAt, stored.SentAt);
        Assert.Equal("manually-recorded-id", stored.GmailMessageId);
        Assert.Empty(await fixture.Db.EndOfDayFillSendReservations.ToListAsync());
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), x => x.Action == "manual-confirmed-sent" && x.EntityKey == pending.Id.ToString());

        var identical = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);
        var duplicate = await fixture.Service.SendAsync(Fixture.SenderEmail,
            new EndOfDayFillSendForm { GroupId = 1, PreviewToken = identical.PreviewToken!, PhysicalCountConfirmed = true }, default);
        Assert.False(duplicate.Success);
        Assert.Contains("No report data has changed", duplicate.Message);

        fixture.Inventory.Bins++;
        var changed = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);
        var revision = await fixture.Service.SendAsync(Fixture.SenderEmail,
            new EndOfDayFillSendForm { GroupId = 1, PreviewToken = changed.PreviewToken!, PhysicalCountConfirmed = true }, default);
        Assert.True(revision.Success);
        Assert.Equal(1, (await fixture.Db.EndOfDayFillReportSends.SingleAsync(x => x.Status == EndOfDayFillSendStatuses.Succeeded && x.Id != pending.Id)).RevisionNumber);
    }

    [Fact]
    public async Task ConfirmedNotSent_FailsPending_DoesNotAdvanceRevision_AndResolutionIsFailClosed()
    {
        await using var fixture = await Fixture.CreateAsync();
        var pending = await fixture.CreateUncertainPendingAsync();
        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddMinutes(16);
        var admin = fixture.CreateAdminService();
        var form = new EndOfDayFillRecoveryForm
        {
            SendAttemptId = pending.Id,
            Resolution = "confirmed-not-sent",
            Reason = "Verified no matching message in sender Gmail Sent folder.",
            Confirmed = true
        };

        Assert.Null(await admin.ResolvePendingSendAsync(form, Fixture.SenderEmail, default));
        var secondResolution = await admin.ResolvePendingSendAsync(new EndOfDayFillRecoveryForm
        {
            SendAttemptId = pending.Id,
            Resolution = "confirmed-sent",
            Reason = "A second administrator disagreed.",
            Confirmed = true
        }, Fixture.SenderEmail, default);
        Assert.Contains("already been resolved", secondResolution);
        Assert.Equal(EndOfDayFillSendStatuses.Failed, (await fixture.Db.EndOfDayFillReportSends.SingleAsync(x => x.Id == pending.Id)).Status);
        Assert.Contains(await fixture.Db.AuditLogs.ToListAsync(), x => x.Action == "manual-confirmed-not-sent");

        var retryPreview = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);
        var retry = await fixture.Service.SendAsync(Fixture.SenderEmail,
            new EndOfDayFillSendForm { GroupId = 1, PreviewToken = retryPreview.PreviewToken!, PhysicalCountConfirmed = true }, default);
        Assert.True(retry.Success);
        Assert.Equal(0, (await fixture.Db.EndOfDayFillReportSends.SingleAsync(x => x.Status == EndOfDayFillSendStatuses.Succeeded)).RevisionNumber);
    }

    [Fact]
    public async Task RecoveryRejectsActiveReservationMissingConfirmationAndUnknownAdministrator()
    {
        await using var fixture = await Fixture.CreateAsync();
        var pending = await fixture.CreateUncertainPendingAsync();
        var admin = fixture.CreateAdminService();
        var form = new EndOfDayFillRecoveryForm { SendAttemptId = pending.Id, Resolution = "confirmed-not-sent", Reason = "Checked sender account carefully.", Confirmed = true };

        Assert.Contains("processing window", await admin.ResolvePendingSendAsync(form, Fixture.SenderEmail, default));
        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddMinutes(16);
        form.Confirmed = false;
        Assert.Contains("Explicitly confirm", await admin.ResolvePendingSendAsync(form, Fixture.SenderEmail, default));
        form.Confirmed = true;
        Assert.Contains("Admin access", await admin.ResolvePendingSendAsync(form, Fixture.UnassignedEmail, default));
        Assert.True(await fixture.Db.EndOfDayFillSendReservations.AnyAsync());
    }

    [Fact]
    public async Task GmailSuccessWithRepeatedDatabaseFinalizationFailure_RemainsPendingAndCannotDuplicate()
    {
        var interceptor = new FailSendFinalizationInterceptor(EndOfDayFillRecoveryPolicy.FinalizationAttempts);
        await using var fixture = await Fixture.CreateAsync(interceptor);
        var preview = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);

        var result = await fixture.Service.SendAsync(Fixture.SenderEmail,
            new EndOfDayFillSendForm { GroupId = 1, PreviewToken = preview.PreviewToken!, PhysicalCountConfirmed = true }, default);

        Assert.False(result.Success);
        Assert.Contains("could not be safely finalized", result.Message);
        Assert.Single(fixture.Sender.Messages);
        Assert.Equal(EndOfDayFillSendStatuses.Pending, (await fixture.Db.EndOfDayFillReportSends.SingleAsync()).Status);
        Assert.True(await fixture.Db.EndOfDayFillSendReservations.AnyAsync());
        var blocked = await fixture.Service.SendAsync(Fixture.SenderEmail,
            new EndOfDayFillSendForm { GroupId = 1, PreviewToken = preview.PreviewToken!, PhysicalCountConfirmed = true }, default);
        Assert.False(blocked.Success);
        Assert.Single(fixture.Sender.Messages);
    }

    [Fact]
    public async Task FutureWpRoomUsesCentralFacilityIdentityWithoutLocationNameRecognition()
    {
        await using var fixture = await Fixture.CreateAsync();
        var room = await fixture.Db.Rooms.SingleAsync(x => x.Id == Fixture.RoomId);
        room.Code = "FUTURE-42";
        room.Name = "Future expansion room";
        room.DisplayName = "Future expansion room";
        await fixture.Db.SaveChangesAsync();

        var preview = await fixture.Service.GetPreviewAsync(Fixture.SenderEmail, 1, default);

        Assert.True(preview.CanSend);
        Assert.DoesNotContain(preview.Issues, x => x.Code == "cross-facility-room");
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public const string SenderEmail = "sender@fruitandland.com";
        public const string UnassignedEmail = "unassigned@fruitandland.com";
        public const int RoomId = 910;
        public const int UnconfiguredRoomId = 911;
        private readonly SqliteConnection connection;
        public CropQcDbContext Db { get; }
        public MutableInventorySource Inventory { get; }
        public FakeEmailSender Sender { get; }
        public MutableClock Clock { get; }
        public EndOfDayFillService Service { get; }

        private Fixture(SqliteConnection connection, CropQcDbContext db, MutableInventorySource inventory, FakeEmailSender sender, MutableClock clock)
        {
            this.connection = connection;
            Db = db;
            Inventory = inventory;
            Sender = sender;
            Clock = clock;
            Service = new EndOfDayFillService(db, inventory, sender, new EmailOptions { Provider = EmailProviders.GmailUser }, new PacificBusinessTimeService(clock), new EphemeralDataProtectionProvider(), new FacilityContextService(db), NullLogger<EndOfDayFillService>.Instance);
        }

        public static async Task<Fixture> CreateAsync(IInterceptor? interceptor = null)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<CropQcDbContext>().UseSqlite(connection);
            if (interceptor is not null) options.AddInterceptors(interceptor);
            var db = new CropQcDbContext(options.Options);
            await db.Database.EnsureCreatedAsync();
            var sender = new User { Email = SenderEmail, DisplayName = "Current Sender", Domain = "fruitandland.com", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
            var unassigned = new User { Email = UnassignedEmail, DisplayName = "No Access", Domain = "fruitandland.com", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
            var warehouse = await db.Warehouses.SingleAsync(x => x.Code == "WP");
            db.AddRange(sender, unassigned);
            await db.SaveChangesAsync();
            var room = new Room { Id = RoomId, WarehouseId = warehouse.Id, Code = "DH-1", Name = "DH Room 1", DisplayName = "DH-1", CapacityBins = 900, IsActive = true, EndOfDayFillReportGroupId = 1 };
            var unconfigured = new Room { Id = UnconfiguredRoomId, WarehouseId = warehouse.Id, Code = "DH-2", Name = "DH Room 2", CapacityBins = 900, IsActive = true };
            db.AddRange(room, unconfigured);
            db.EndOfDayFillUserGroupAssignments.Add(new EndOfDayFillUserGroupAssignment { UserId = sender.Id, ReportGroupId = 1, CreatedAt = DateTimeOffset.UtcNow, CreatedByUserId = sender.Id });
            db.UserGoogleCredentials.Add(new UserGoogleCredential { UserId = sender.Id, Provider = "Google", RefreshTokenEncrypted = "test-only", Scope = GmailScopes.Send, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow });
            await db.SaveChangesAsync();
            var inventory = new MutableInventorySource();
            var fakeSender = new FakeEmailSender();
            var clock = new MutableClock { UtcNow = new DateTimeOffset(2026, 8, 7, 4, 22, 0, TimeSpan.Zero) };
            return new Fixture(connection, db, inventory, fakeSender, clock);
        }

        public EndOfDayFillAdminService CreateAdminService() => new(Db, new FacilityContextService(Db), new PacificBusinessTimeService(Clock), new FakeUserAccessService());
        public AdminManagementService CreateMasterDataService() => new(Db, new VarietyColorService(Db), new CanonicalGrowerService(Db), new FacilityContextService(Db));

        public async Task<EndOfDayFillReportSend> CreateUncertainPendingAsync()
        {
            Sender.ThrowAfterAccept = true;
            var preview = await Service.GetPreviewAsync(SenderEmail, 1, default);
            var result = await Service.SendAsync(SenderEmail,
                new EndOfDayFillSendForm { GroupId = 1, PreviewToken = preview.PreviewToken!, PhysicalCountConfirmed = true }, default);
            Sender.ThrowAfterAccept = false;
            Assert.False(result.Success);
            Assert.Contains("could not be safely finalized", result.Message);
            return await Db.EndOfDayFillReportSends.SingleAsync(x => x.Status == EndOfDayFillSendStatuses.Pending);
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    private sealed class MutableInventorySource : IEndOfDayFillInventorySource
    {
        public int Bins { get; set; } = 145;
        public string GrowerNumber { get; set; } = "1084";
        public string CanonicalName { get; set; } = "Gala";
        public bool IncludeUnconfiguredRoom { get; set; } = true;
        public IReadOnlyList<int> RequestedRoomIds { get; private set; } = [];
        public Task<IReadOnlyList<RoomLotSummaryViewModel>> GetCurrentLotsAsync(IReadOnlyCollection<int> roomIds, CancellationToken cancellationToken)
        {
            RequestedRoomIds = roomIds.Order().ToList();
            IReadOnlyList<RoomLotSummaryViewModel> result =
            [
                new() { RoomId = Fixture.RoomId, RoomCode = "DH-1", CurrentBins = Bins, GrowerNumber = GrowerNumber, GrowerName = "Smith Orchards", CanonicalVarietyKey = CanonicalName.Length == 0 ? "" : "gala", CanonicalVarietyName = CanonicalName, ProductionType = "Fresh", IsOrganic = false, VarietyHexColor = "#c62828", InventoryKey = "canonical-398", GrowerLotId = 398 },
                new() { RoomId = Fixture.UnconfiguredRoomId, RoomCode = "DH-2", CurrentBins = 999, GrowerNumber = "9999", GrowerName = "Excluded", CanonicalVarietyKey = "fuji", CanonicalVarietyName = "Fuji", ProductionType = "Fresh", IsOrganic = false, InventoryKey = "excluded" }
            ];
            return Task.FromResult<IReadOnlyList<RoomLotSummaryViewModel>>(result.Where(x => roomIds.Contains(x.RoomId) && (IncludeUnconfiguredRoom || x.RoomId != Fixture.UnconfiguredRoomId)).ToList());
        }
    }

    private sealed class FakeEmailSender : IQcEmailSender
    {
        public bool FailNext { get; set; }
        public bool ThrowAfterAccept { get; set; }
        public Action<CancellationToken>? OnSend { get; set; }
        public List<(User Sender, QcEmailMessage Message)> Messages { get; } = [];
        public Task<QcEmailSendResult> SendAsync(User sender, QcEmailMessage message, CancellationToken cancellationToken)
        {
            OnSend?.Invoke(cancellationToken);
            Messages.Add((sender, message));
            if (ThrowAfterAccept) throw new InvalidOperationException("Simulated process failure after fake Gmail acceptance.");
            if (FailNext) { FailNext = false; return Task.FromResult(QcEmailSendResult.Failed("fake failure")); }
            return Task.FromResult(QcEmailSendResult.Sent("fake-gmail-id"));
        }
    }

    private sealed class FailSendFinalizationInterceptor(int failures) : SaveChangesInterceptor
    {
        private int remainingFailures = failures;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (remainingFailures > 0
                && eventData.Context?.ChangeTracker.Entries<EndOfDayFillReportSend>()
                    .Any(x => x.State == EntityState.Modified && x.Entity.Status != EndOfDayFillSendStatuses.Pending) == true)
            {
                remainingFailures--;
                throw new DbUpdateException("Simulated finalization failure.");
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class FakeUserAccessService : IUserAccessService
    {
        public Task<bool> HasAccessAsync(System.Security.Claims.ClaimsPrincipal principal, string areaKey, PageAccessLevel minimumLevel, CancellationToken cancellationToken) =>
            Task.FromResult(false);
        public Task<PageAccessLevel> GetAccessLevelAsync(string? email, string areaKey, CancellationToken cancellationToken) =>
            Task.FromResult(email == Fixture.SenderEmail ? PageAccessLevel.Admin : PageAccessLevel.None);
        public Task<IReadOnlyList<UserAccessMatrixRow>> GetMatrixAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<UserAccessMatrixRow>>([]);
        public Task EnsureAccessMatrixAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<string?> SaveMatrixAsync(UserAccessMatrixForm form, string changedByEmail, CancellationToken cancellationToken) =>
            Task.FromResult<string?>(null);
    }

    private sealed class MutableClock : IClock { public DateTimeOffset UtcNow { get; set; } }
}
