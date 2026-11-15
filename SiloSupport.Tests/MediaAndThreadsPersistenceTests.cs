using Microsoft.EntityFrameworkCore;
using SiloSupport.Application.Dtos;
using SiloSupport.Application.Interfaces;
using SiloSupport.Application.Services;
using SiloSupport.Domain.Entities;
using SiloSupport.Domain.Enums;
using SiloSupport.Persistence;
using SiloSupport.Tests.Support;

namespace SiloSupport.Tests;

/// <summary>Threads and attachments on the real EF repositories (SQLite in-memory).</summary>
public class MediaAndThreadsPersistenceTests : TestBase
{
    private async Task<(Customer Customer, CustomerGroup Low, CustomerGroup Critical)> SetupAsync(string name = "Acme")
    {
        var customer = await AddCustomerAsync(name);
        var low = await AddGroupAsync(customer.Id, GroupPriority.Low, 1000 + customer.Id);
        var critical = await AddGroupAsync(customer.Id, GroupPriority.Critical, 2000 + customer.Id);
        return (customer, low, critical);
    }

    private Task AddWithThreadAsync(int groupId, int customerId, string title, string text, DateTimeOffset at, int? existingThreadId = null, MessageAttachment? attachment = null)
    {
        var message = new CustomerGroupedMessage
        {
            GroupId = groupId,
            Message = "forwarded: " + text,
            OriginalText = text,
            CreatedAtUtc = at.UtcDateTime,
            ThreadId = existingThreadId,
            Thread = existingThreadId is null
                ? new SupportThread { CustomerId = customerId, Title = title, CreatedAtUtc = at.UtcDateTime, LastActivityUtc = at.UtcDateTime }
                : null,
            Attachment = attachment
        };
        return Get<ICustomerGroupedMessageRepository>().AddAsync(message);
    }

    [Fact]
    public async Task NewMessage_CreatesItsThread_AndFollowUpsJoinIt_MovingLastActivityForward()
    {
        var (customer, low, critical) = await SetupAsync();
        var t0 = Clock.GetUtcNow();
        await AddWithThreadAsync(low.Id, customer.Id, "Login problem", "cannot log in", t0);
        var threadId = (await Db.SupportThreads.AsNoTracking().SingleAsync()).Id;

        await AddWithThreadAsync(critical.Id, customer.Id, "ignored", "still failing", t0.AddMinutes(10), existingThreadId: threadId);

        var thread = await Db.SupportThreads.AsNoTracking().SingleAsync();
        Assert.Equal("Login problem", thread.Title);
        Assert.Equal(t0.AddMinutes(10).UtcDateTime, thread.LastActivityUtc);
        Assert.Equal(2, await Db.CustomerGroupedMessages.CountAsync(m => m.ThreadId == threadId));
    }

    [Fact]
    public async Task LastActivity_NeverMovesBackwards()
    {
        var (customer, low, _) = await SetupAsync();
        var t0 = Clock.GetUtcNow();
        await AddWithThreadAsync(low.Id, customer.Id, "T", "first", t0);
        var threadId = (await Db.SupportThreads.AsNoTracking().SingleAsync()).Id;

        await AddWithThreadAsync(low.Id, customer.Id, "T", "older", t0.AddMinutes(-30), existingThreadId: threadId);

        Assert.Equal(t0.UtcDateTime, (await Db.SupportThreads.AsNoTracking().SingleAsync()).LastActivityUtc);
    }

    [Fact]
    public async Task OpenContext_ReturnsOnlyRecentOpenThreadsOfThatCustomer_WithTheLatestMessages()
    {
        var (acme, acmeLow, _) = await SetupAsync("Acme");
        var (beta, betaLow, _) = await SetupAsync("Beta");
        var now = Clock.GetUtcNow();

        await AddWithThreadAsync(acmeLow.Id, acme.Id, "Open", "m1", now.AddMinutes(-50));
        var openId = (await Db.SupportThreads.AsNoTracking().SingleAsync(t => t.Title == "Open")).Id;
        await AddWithThreadAsync(acmeLow.Id, acme.Id, "x", "m2", now.AddMinutes(-40), existingThreadId: openId);
        await AddWithThreadAsync(acmeLow.Id, acme.Id, "x", "m3", now.AddMinutes(-30), existingThreadId: openId);

        await AddWithThreadAsync(acmeLow.Id, acme.Id, "Stale", "old", now.AddHours(-48));
        await AddWithThreadAsync(acmeLow.Id, acme.Id, "Resolved", "done", now.AddMinutes(-20));
        var resolvedId = (await Db.SupportThreads.AsNoTracking().SingleAsync(t => t.Title == "Resolved")).Id;
        await Get<ICustomerGroupedMessageRepository>().SetThreadResolvedAsync(acme.Id, resolvedId, true);
        await AddWithThreadAsync(betaLow.Id, beta.Id, "Other customer", "secret", now.AddMinutes(-5));

        var context = await Get<ISupportThreadRepository>().GetOpenContextAsync(acme.Id, now.AddHours(-24).UtcDateTime, 5, 2);

        var thread = Assert.Single(context);
        Assert.Equal("Open", thread.Title);
        Assert.Equal(new[] { "m2", "m3" }, thread.Messages.Select(m => m.Text)); // the 2 latest, oldest first
        Assert.All(thread.Messages, m => Assert.Equal(GroupPriority.Low, m.Priority));
        Assert.Equal(DateTimeKind.Utc, thread.LastActivityUtc.Kind);
    }

    [Fact]
    public async Task OpenContext_RespectsTheThreadLimit_MostRecentFirst()
    {
        var (customer, low, _) = await SetupAsync();
        var now = Clock.GetUtcNow();
        for (var i = 1; i <= 4; i++)
            await AddWithThreadAsync(low.Id, customer.Id, $"T{i}", $"text{i}", now.AddMinutes(-60 + i));

        var context = await Get<ISupportThreadRepository>().GetOpenContextAsync(customer.Id, now.AddHours(-24).UtcDateTime, 2, 4);

        Assert.Equal(new[] { "T4", "T3" }, context.Select(t => t.Title));
        Assert.Empty(await Get<ISupportThreadRepository>().GetOpenContextAsync(customer.Id, now.AddHours(-24).UtcDateTime, 0, 4));
    }

    [Fact]
    public async Task Attachment_IsStoredWithTheMessage_AndListedWithoutItsBytes()
    {
        var (customer, low, _) = await SetupAsync();
        var attachment = new MessageAttachment
        {
            Kind = AttachmentKind.Photo,
            FileName = "shot.jpg",
            MimeType = "image/jpeg",
            SizeBytes = 3,
            BaleFileId = "FILE",
            Content = new byte[] { 1, 2, 3 }
        };
        await AddWithThreadAsync(low.Id, customer.Id, "Photo", "look", Clock.GetUtcNow(), attachment: attachment);

        var conversation = await Get<ConversationService>().GetAsync(customer.Id);

        var thread = Assert.Single(conversation!.Threads);
        var message = Assert.Single(thread.Messages);
        Assert.NotNull(message.Attachment);
        Assert.True(message.Attachment!.HasContent);
        Assert.Equal(AttachmentKind.Photo, message.Attachment.Kind);
        Assert.Equal("shot.jpg", message.Attachment.FileName);
        Assert.Empty(conversation.LegacyMessages);
        // the group view carries the same metadata
        Assert.NotNull(conversation.Groups.Single(g => g.GroupId == low.Id).Messages.Single().Attachment);

        var content = await Get<ConversationService>().GetAttachmentAsync(message.Attachment.Id);
        Assert.Equal(new byte[] { 1, 2, 3 }, content!.Content);
        Assert.Equal("image/jpeg", content.MimeType);
    }

    [Fact]
    public async Task Attachment_WithoutBytes_ReportsNoContent_AndIsNotServed()
    {
        var (customer, low, _) = await SetupAsync();
        await AddWithThreadAsync(low.Id, customer.Id, "Big", "video", Clock.GetUtcNow(), attachment: new MessageAttachment
        {
            Kind = AttachmentKind.Video, BaleFileId = "BIG", SizeBytes = 30_000_000, Content = null
        });

        var conversation = await Get<ConversationService>().GetAsync(customer.Id);
        var info = conversation!.Threads.Single().Messages.Single().Attachment!;

        Assert.False(info.HasContent);
        Assert.Null(await Get<ConversationService>().GetAttachmentAsync(info.Id));
        Assert.Null(await Get<ConversationService>().GetAttachmentAsync(9999));
    }

    [Fact]
    public async Task Messages_WithoutAThread_AreShownAsLegacyMessages()
    {
        var (customer, low, _) = await SetupAsync();
        await AddMessageAsync(low.Id, "old message from before threads");

        var conversation = await Get<ConversationService>().GetAsync(customer.Id);

        Assert.Empty(conversation!.Threads);
        Assert.Equal("old message from before threads", Assert.Single(conversation.LegacyMessages).Message);
        Assert.Single(conversation.Groups.Single(g => g.GroupId == low.Id).Messages);
    }

    [Fact]
    public async Task ThreadsOfOtherCustomers_AreNeverShown()
    {
        var (acme, acmeLow, _) = await SetupAsync("Acme");
        var (beta, betaLow, _) = await SetupAsync("Beta");
        await AddWithThreadAsync(acmeLow.Id, acme.Id, "Acme issue", "a", Clock.GetUtcNow());
        await AddWithThreadAsync(betaLow.Id, beta.Id, "Beta issue", "b", Clock.GetUtcNow());

        var conversation = await Get<ConversationService>().GetAsync(acme.Id);

        Assert.Equal("Acme issue", Assert.Single(conversation!.Threads).Title);
    }

    [Fact]
    public async Task SetThreadResolved_ResolvesAndReopensEveryMessage_ButOnlyForTheOwningCustomer()
    {
        var (acme, acmeLow, acmeCritical) = await SetupAsync("Acme");
        var (beta, _, _) = await SetupAsync("Beta");
        var now = Clock.GetUtcNow();
        await AddWithThreadAsync(acmeLow.Id, acme.Id, "Issue", "one", now);
        var threadId = (await Db.SupportThreads.AsNoTracking().SingleAsync()).Id;
        await AddWithThreadAsync(acmeCritical.Id, acme.Id, "x", "two", now.AddMinutes(1), existingThreadId: threadId);
        var service = Get<ConversationService>();

        var wrongCustomer = await service.SetThreadResolvedAsync(beta.Id, threadId, true);
        Assert.False(wrongCustomer.Succeeded);
        Assert.False(await Db.CustomerGroupedMessages.AsNoTracking().AnyAsync(m => m.IsResolved));

        Assert.True((await service.SetThreadResolvedAsync(acme.Id, threadId, true)).Succeeded);
        var resolved = (await service.GetAsync(acme.Id))!.Threads.Single();
        Assert.False(resolved.IsOpen);

        Assert.True((await service.SetThreadResolvedAsync(acme.Id, threadId, false)).Succeeded);
        var reopened = (await service.GetAsync(acme.Id))!.Threads.Single();
        Assert.True(reopened.IsOpen);
        Assert.Equal(GroupPriority.Critical, reopened.HighestPriority);

        Assert.False((await service.SetThreadResolvedAsync(acme.Id, 9999, true)).Succeeded);
    }

    [Fact]
    public async Task DeletingAThread_KeepsItsMessages_DeletingAMessage_RemovesItsAttachment()
    {
        var (customer, low, _) = await SetupAsync();
        await AddWithThreadAsync(low.Id, customer.Id, "T", "text", Clock.GetUtcNow(),
            attachment: new MessageAttachment { Kind = AttachmentKind.Voice, BaleFileId = "V", Content = new byte[] { 1 } });

        await Db.SupportThreads.ExecuteDeleteAsync();
        var message = await Db.CustomerGroupedMessages.AsNoTracking().SingleAsync();
        Assert.Null(message.ThreadId); // ON DELETE SET NULL

        await Db.CustomerGroupedMessages.ExecuteDeleteAsync();
        Assert.Equal(0, await Db.MessageAttachments.CountAsync()); // cascade
    }

    [Fact]
    public async Task OneMessage_CanHaveOnlyOneAttachment()
    {
        var (customer, low, _) = await SetupAsync();
        await AddWithThreadAsync(low.Id, customer.Id, "T", "x", Clock.GetUtcNow(),
            attachment: new MessageAttachment { Kind = AttachmentKind.Photo, BaleFileId = "A" });
        var messageId = (await Db.CustomerGroupedMessages.AsNoTracking().SingleAsync()).Id;

        Db.MessageAttachments.Add(new MessageAttachment { GroupedMessageId = messageId, Kind = AttachmentKind.Photo, BaleFileId = "B" });

        await Assert.ThrowsAsync<DbUpdateException>(() => Db.SaveChangesAsync());
    }

    [Fact]
    public async Task AttachmentKind_IsStoredAsText()
    {
        var (customer, low, _) = await SetupAsync();
        await AddWithThreadAsync(low.Id, customer.Id, "T", "x", Clock.GetUtcNow(),
            attachment: new MessageAttachment { Kind = AttachmentKind.Animation, BaleFileId = "A" });

        var stored = await Db.Database.SqlQueryRaw<string>("SELECT Kind AS Value FROM MessageAttachments").SingleAsync();

        Assert.Equal("Animation", stored);
    }
}

/// <summary>The new migration on top of an existing database that already holds messages.</summary>
public class MediaAndThreadsMigrationTests : IDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection = new("DataSource=:memory:");

    public MediaAndThreadsMigrationTests() => _connection.Open();

    [Fact]
    public async Task Migration_KeepsExistingMessages_AndCreatesThreadAndAttachmentTables()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options;
        await using var db = new ApplicationDbContext(options);
        var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(db.Database);

        await migrator.MigrateAsync("20261103074459_Remove_Unique_BaleId");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Customers (Name, Description) VALUES ('A', '')");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO CustomerGroups (Name, BaleId, Priority, CustomerId) VALUES ('g', 5, 'Low', 1)");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO CustomerGroupedMessages (GroupId, Message, IsResolved, CreatedAtUtc) VALUES (1, 'before', 0, '2026-10-01 00:00:00')");

        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var tables = await db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table'").ToListAsync();
        Assert.Contains("SupportThreads", tables);
        Assert.Contains("MessageAttachments", tables);

        var old = await db.CustomerGroupedMessages.AsNoTracking().SingleAsync();
        Assert.Equal("before", old.Message);
        Assert.Null(old.ThreadId);
        Assert.Null(old.OriginalText);

        // The new foreign key works on the migrated schema (SET NULL when a thread is deleted).
        db.SupportThreads.Add(new SupportThread { CustomerId = 1, Title = "T", CreatedAtUtc = DateTime.UtcNow, LastActivityUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("UPDATE CustomerGroupedMessages SET ThreadId = 1");
        await db.Database.ExecuteSqlRawAsync("DELETE FROM SupportThreads");
        Assert.Null((await db.CustomerGroupedMessages.AsNoTracking().SingleAsync()).ThreadId);
    }

    public void Dispose() => _connection.Dispose();
}
