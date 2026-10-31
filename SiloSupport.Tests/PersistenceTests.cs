using SiloSupport.Application.Helpers;
using Microsoft.EntityFrameworkCore;
using SiloSupport.Application.Dtos;
using SiloSupport.Application.Interfaces;
using SiloSupport.Application.Services;
using SiloSupport.Domain.Entities;
using SiloSupport.Domain.Enums;
using SiloSupport.Persistence;
using SiloSupport.Tests.Support;

namespace SiloSupport.Tests;

/// <summary>EF Core mappings, constraints and repositories on SQLite in-memory.</summary>
public class PersistenceTests : TestBase
{
    // 17
    [Theory]
    [InlineData("@Ali", "ali")]
    [InlineData("ali", "@ALI")]
    [InlineData("@ali", "@ali")]
    public async Task MemberBaleUserName_IsFoundIgnoringCaseAndAtSign(string stored, string incoming)
    {
        var customer = await AddCustomerAsync();
        await AddMemberAsync(customer.Id, stored);

        var matches = await Get<ICustomerMemberRepository>()
            .FindByBaleIdsAsync(BaleIdentity.CandidatesFor(null, incoming));

        Assert.Equal(stored, Assert.Single(matches).BaleId);
        Assert.True(await Get<ICustomerMemberRepository>().BaleIdExistsAsync(incoming, null));
    }

    [Fact]
    public async Task Database_RejectsTwoGroupsWithTheSamePriorityForOneCustomer()
    {
        var customer = await AddCustomerAsync();
        await AddGroupAsync(customer.Id, GroupPriority.Low, 100);

        await Assert.ThrowsAsync<DbUpdateException>(() => AddGroupAsync(customer.Id, GroupPriority.Low, 101));
    }

    [Fact]
    public async Task Database_AllowsTheSamePriorityForDifferentCustomers()
    {
        var a = await AddCustomerAsync("A");
        var b = await AddCustomerAsync("B");

        await AddGroupAsync(a.Id, GroupPriority.Critical, 100);
        await AddGroupAsync(b.Id, GroupPriority.Critical, 101);

        Assert.Equal(2, await Db.CustomerGroups.CountAsync());
    }

    [Fact]
    public async Task Database_RejectsDuplicateGroupBaleId()
    {
        var a = await AddCustomerAsync("A");
        var b = await AddCustomerAsync("B");
        await AddGroupAsync(a.Id, GroupPriority.Low, 100);

        await Assert.ThrowsAsync<DbUpdateException>(() => AddGroupAsync(b.Id, GroupPriority.Low, 100));
    }

    [Fact]
    public async Task Database_RejectsDuplicateMemberBaleId()
    {
        var a = await AddCustomerAsync("A");
        var b = await AddCustomerAsync("B");
        await AddMemberAsync(a.Id, "500");

        await Assert.ThrowsAsync<DbUpdateException>(() => AddMemberAsync(b.Id, "500"));
    }

    [Fact]
    public async Task Database_RestrictsDeletingAGroupThatHasMessages()
    {
        var customer = await AddCustomerAsync();
        var group = await AddGroupAsync(customer.Id, GroupPriority.Low, 100);
        await AddMessageAsync(group.Id, "history");

        await Assert.ThrowsAnyAsync<Exception>(() => Get<ICustomerGroupRepository>().DeleteAsync(group.Id));

        Assert.Equal(1, await Db.CustomerGroupedMessages.CountAsync());
    }

    [Fact]
    public async Task Database_RejectsMessageForUnknownGroup()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => AddMessageAsync(groupId: 12345, "orphan"));
    }

    [Fact]
    public async Task GroupedMessage_RoundTrips_WithIsResolvedDefaultFalse()
    {
        var customer = await AddCustomerAsync();
        var group = await AddGroupAsync(customer.Id, GroupPriority.Medium, 100);

        var saved = await AddMessageAsync(group.Id, "the exact text");

        var loaded = await Db.CustomerGroupedMessages.AsNoTracking().SingleAsync(m => m.Id == saved.Id);
        Assert.Equal("the exact text", loaded.Message);
        Assert.False(loaded.IsResolved);
        Assert.Equal(Clock.GetUtcNow().UtcDateTime, loaded.CreatedAtUtc);
        Assert.Equal(group.Id, loaded.GroupId);
    }

    [Fact]
    public async Task Priority_IsStoredAsItsName()
    {
        var customer = await AddCustomerAsync();
        await AddGroupAsync(customer.Id, GroupPriority.Critical, 100);

        var stored = await Db.Database
            .SqlQueryRaw<string>("SELECT Priority AS Value FROM CustomerGroups")
            .SingleAsync();

        Assert.Equal("Critical", stored);
    }

    [Theory]
    [InlineData("4503599627370495")] // 2^52 - 1: the documented maximum of significant bits of a Bale id
    [InlineData("123")]
    [InlineData("user_42")]
    public async Task MemberBaleId_IsStoredAndFoundAsText(string baleId)
    {
        var customer = await AddCustomerAsync();
        await AddMemberAsync(customer.Id, baleId);

        var matches = await Get<ICustomerMemberRepository>().FindByBaleIdsAsync(BaleIdentity.Variants(baleId).ToList());

        var found = Assert.Single(matches);
        Assert.Equal(baleId, found.BaleId);
        Assert.Equal(customer.Id, found.CustomerId);
        Assert.Equal("Acme", found.Customer!.Name);
    }

    [Fact]
    public async Task MemberBaleId_IsStoredAsATextColumn()
    {
        var customer = await AddCustomerAsync();
        await AddMemberAsync(customer.Id, "00123");

        var stored = await Db.Database
            .SqlQueryRaw<string>("SELECT BaleId AS Value FROM CustomerMembers")
            .SingleAsync();

        Assert.Equal("00123", stored); // text keeps leading zeros; a number column would not
    }

    [Fact]
    public async Task GroupRepository_GetByCustomerAndPriority_NeverReturnsAnotherCustomersGroup()
    {
        var a = await AddCustomerAsync("A");
        var b = await AddCustomerAsync("B");
        await AddGroupAsync(a.Id, GroupPriority.Critical, 100);

        var forB = await Get<ICustomerGroupRepository>().GetByCustomerAndPriorityAsync(b.Id, GroupPriority.Critical);

        Assert.Null(forB);
    }

    [Fact]
    public async Task TransactionRunner_RollsBackOnException()
    {
        var runner = Get<ITransactionRunner>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => runner.ExecuteAsync<int>(async () =>
        {
            await AddCustomerAsync("rolled back");
            throw new InvalidOperationException("fail");
        }));

        Assert.False(await Db.Customers.AnyAsync());
    }

    [Fact]
    public async Task TransactionRunner_JoinsAnOpenTransaction()
    {
        var runner = Get<ITransactionRunner>();

        await runner.ExecuteAsync(async () =>
        {
            await runner.ExecuteAsync(async () =>
            {
                await AddCustomerAsync("nested");
                return 1;
            });
            return 1;
        });

        Assert.True(await Db.Customers.AnyAsync());
    }
}

/// <summary>Application services running on the real repositories.</summary>
public class SupportServicesTests : TestBase
{
    [Fact]
    public async Task GroupService_RejectsSecondGroupWithSamePriority()
    {
        var customer = await AddCustomerAsync();
        var service = Get<CustomerGroupService>();
        Assert.True((await service.CreateAsync(customer.Id, new GroupInput { Name = "L1", BaleId = 10, Priority = GroupPriority.Low })).Succeeded);

        var second = await service.CreateAsync(customer.Id, new GroupInput { Name = "L2", BaleId = 11, Priority = GroupPriority.Low });

        Assert.False(second.Succeeded);
        Assert.Contains("«کم»", second.FirstError);
    }

    [Fact]
    public async Task GroupService_RejectsDuplicateBaleIdAcrossCustomers()
    {
        var a = await AddCustomerAsync("A");
        var b = await AddCustomerAsync("B");
        var service = Get<CustomerGroupService>();
        await service.CreateAsync(a.Id, new GroupInput { Name = "g", BaleId = 10, Priority = GroupPriority.Low });

        var result = await service.CreateAsync(b.Id, new GroupInput { Name = "g", BaleId = 10, Priority = GroupPriority.Low });

        Assert.False(result.Succeeded);
        Assert.Contains("گروه بله", result.FirstError);
    }

    [Theory]
    [InlineData(0)]
    public async Task GroupService_RejectsInvalidBaleId(long baleId)
    {
        var customer = await AddCustomerAsync();

        var result = await Get<CustomerGroupService>().CreateAsync(customer.Id, new GroupInput { Name = "g", BaleId = baleId, Priority = GroupPriority.Low });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task GroupService_RejectsUndefinedPriority()
    {
        var customer = await AddCustomerAsync();

        var result = await Get<CustomerGroupService>().CreateAsync(customer.Id, new GroupInput { Name = "g", BaleId = 5, Priority = (GroupPriority)42 });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task GroupService_UpdateCanKeepItsOwnPriorityAndBaleId()
    {
        var customer = await AddCustomerAsync();
        var service = Get<CustomerGroupService>();
        var id = (await service.CreateAsync(customer.Id, new GroupInput { Name = "g", BaleId = 10, Priority = GroupPriority.Low })).Value;

        var result = await service.UpdateAsync(id, new GroupInput { Name = "renamed", BaleId = 10, Priority = GroupPriority.Low });

        Assert.True(result.Succeeded);
        Assert.Equal("renamed", (await service.GetAsync(id))!.Name);
    }

    [Fact]
    public async Task GroupService_StatusListsAllThreePrioritiesAndShowsMissingOnes()
    {
        var customer = await AddCustomerAsync();
        await AddGroupAsync(customer.Id, GroupPriority.Medium, 10);

        var status = await Get<CustomerGroupService>().GetPriorityStatusAsync(customer.Id);

        Assert.Equal(3, status.Count);
        Assert.True(status.Single(s => s.Priority == GroupPriority.Medium).IsConfigured);
        Assert.False(status.Single(s => s.Priority == GroupPriority.Low).IsConfigured);
        Assert.False(status.Single(s => s.Priority == GroupPriority.Critical).IsConfigured);
    }

    [Fact]
    public async Task GroupService_CannotDeleteGroupWithHistory()
    {
        var customer = await AddCustomerAsync();
        var group = await AddGroupAsync(customer.Id, GroupPriority.Low, 10);
        await AddMessageAsync(group.Id, "history");

        var result = await Get<CustomerGroupService>().DeleteAsync(group.Id);

        Assert.False(result.Succeeded);
        Assert.NotNull(await Get<ICustomerGroupRepository>().GetByIdAsync(group.Id));
    }

    [Fact]
    public async Task MemberService_RejectsDuplicateBaleId()
    {
        var a = await AddCustomerAsync("A");
        var b = await AddCustomerAsync("B");
        var service = Get<CustomerMemberService>();
        Assert.True((await service.CreateAsync(a.Id, new MemberInput { Name = "x", BaleId = "77" })).Succeeded);

        var duplicate = await service.CreateAsync(b.Id, new MemberInput { Name = "y", BaleId = " 77 " });

        Assert.False(duplicate.Succeeded);
        Assert.Contains("شناسه بله", duplicate.FirstError);
    }

    [Fact]
    public async Task MemberService_RequiresNameAndBaleId()
    {
        var customer = await AddCustomerAsync();
        var service = Get<CustomerMemberService>();

        Assert.False((await service.CreateAsync(customer.Id, new MemberInput { Name = "", BaleId = "1" })).Succeeded);
        Assert.False((await service.CreateAsync(customer.Id, new MemberInput { Name = "x", BaleId = "0" })).Succeeded);
        Assert.False((await service.CreateAsync(customer.Id, new MemberInput { Name = "x", BaleId = "" })).Succeeded);
        Assert.False((await service.CreateAsync(customer.Id, new MemberInput { Name = "x", BaleId = "   " })).Succeeded);
        Assert.False((await service.CreateAsync(customer.Id, new MemberInput { Name = "x", BaleId = "12 34" })).Succeeded);
        Assert.False((await service.CreateAsync(customer.Id, new MemberInput { Name = "x", BaleId = new string('9', 65) })).Succeeded);
    }

    [Fact]
    public async Task MemberService_MemberStaysWithItsCustomerWhenEdited()
    {
        var a = await AddCustomerAsync("A");
        var member = await AddMemberAsync(a.Id, "5", "old");

        await Get<CustomerMemberService>().UpdateAsync(member.Id, new MemberInput { Name = "new", BaleId = "5", PhoneNumber = "1" });

        var loaded = await Get<CustomerMemberService>().GetAsync(member.Id);
        Assert.Equal(a.Id, loaded!.CustomerId);
        Assert.Equal("new", loaded.Name);
    }

    [Fact]
    public async Task CustomerService_CannotDeleteCustomerWithMessageHistory()
    {
        var customer = await AddCustomerAsync();
        var group = await AddGroupAsync(customer.Id, GroupPriority.Low, 10);
        await AddMessageAsync(group.Id, "history");

        var result = await Get<CustomerService>().DeleteAsync(customer.Id);

        Assert.False(result.Succeeded);
        Assert.NotNull(await Get<CustomerService>().GetAsync(customer.Id));
        Assert.Equal(1, await Db.CustomerGroupedMessages.CountAsync());
    }

    [Fact]
    public async Task CustomerService_DeletingCustomerWithoutHistoryRemovesItsMembersAndGroups()
    {
        var customer = await AddCustomerAsync();
        await AddMemberAsync(customer.Id, "5");
        await AddGroupAsync(customer.Id, GroupPriority.Low, 10);

        var result = await Get<CustomerService>().DeleteAsync(customer.Id);

        Assert.True(result.Succeeded);
        Assert.False(await Db.Customers.AnyAsync());
        Assert.False(await Db.CustomerMembers.AnyAsync());
        Assert.False(await Db.CustomerGroups.AnyAsync());
    }

    [Fact]
    public async Task CustomerService_ListShowsMissingPriorities()
    {
        var customer = await AddCustomerAsync();
        await AddGroupAsync(customer.Id, GroupPriority.Low, 10);
        await AddMemberAsync(customer.Id, "5");

        var item = Assert.Single(await Get<CustomerService>().GetListAsync());

        Assert.Equal(1, item.MemberCount);
        Assert.Equal(1, item.GroupCount);
        Assert.Equal(new[] { GroupPriority.Critical, GroupPriority.Medium }, item.MissingPriorities);
    }

    // 15
    [Fact]
    public async Task ConversationService_ReturnsOnlyTheSelectedCustomersMessagesGroupedByGroup()
    {
        var a = await AddCustomerAsync("A");
        var b = await AddCustomerAsync("B");
        var aLow = await AddGroupAsync(a.Id, GroupPriority.Low, 10, "A low");
        var aCritical = await AddGroupAsync(a.Id, GroupPriority.Critical, 11, "A critical");
        var bLow = await AddGroupAsync(b.Id, GroupPriority.Low, 20, "B low");

        var t0 = Clock.GetUtcNow();
        await AddMessageAsync(aLow.Id, "A low 2", t0.AddMinutes(2));
        await AddMessageAsync(aLow.Id, "A low 1", t0.AddMinutes(1));
        await AddMessageAsync(aCritical.Id, "A critical 1", t0);
        await AddMessageAsync(bLow.Id, "B secret", t0);

        var conversation = await Get<ConversationService>().GetAsync(a.Id);

        Assert.NotNull(conversation);
        Assert.Equal(2, conversation!.Groups.Count);
        Assert.Equal(GroupPriority.Critical, conversation.Groups[0].Priority); // most urgent group first
        Assert.Equal(new[] { "A critical 1" }, conversation.Groups[0].Messages.Select(m => m.Message));
        Assert.Equal(new[] { "A low 1", "A low 2" }, conversation.Groups[1].Messages.Select(m => m.Message)); // chronological
        Assert.DoesNotContain(conversation.Groups.SelectMany(g => g.Messages), m => m.Message == "B secret");
        Assert.Equal(new[] { GroupPriority.Medium }, conversation.MissingPriorities);
    }

    [Fact]
    public async Task ConversationService_UnknownCustomer_ReturnsNull()
    {
        Assert.Null(await Get<ConversationService>().GetAsync(999));
    }

    // 16
    [Fact]
    public async Task ConversationService_MarkingResolvedAndUnresolvedPersists()
    {
        var customer = await AddCustomerAsync();
        var group = await AddGroupAsync(customer.Id, GroupPriority.Low, 10);
        var message = await AddMessageAsync(group.Id, "help");
        var service = Get<ConversationService>();

        Assert.True((await service.SetResolvedAsync(customer.Id, message.Id, true)).Succeeded);
        Assert.True(await Db.CustomerGroupedMessages.AsNoTracking().Where(m => m.Id == message.Id).Select(m => m.IsResolved).SingleAsync());

        Assert.True((await service.SetResolvedAsync(customer.Id, message.Id, false)).Succeeded);
        Assert.False(await Db.CustomerGroupedMessages.AsNoTracking().Where(m => m.Id == message.Id).Select(m => m.IsResolved).SingleAsync());
    }

    [Fact]
    public async Task ConversationService_CannotResolveAnotherCustomersMessage()
    {
        var a = await AddCustomerAsync("A");
        var b = await AddCustomerAsync("B");
        var bGroup = await AddGroupAsync(b.Id, GroupPriority.Low, 10);
        var bMessage = await AddMessageAsync(bGroup.Id, "B message");

        var result = await Get<ConversationService>().SetResolvedAsync(a.Id, bMessage.Id, true);

        Assert.False(result.Succeeded);
        Assert.False(await Db.CustomerGroupedMessages.AsNoTracking().Where(m => m.Id == bMessage.Id).Select(m => m.IsResolved).SingleAsync());
    }
}

/// <summary>Applies the whole migration chain on SQLite, like the real startup does with SQL Server.</summary>
public class MigrationTests : IDisposable
{
    private readonly Microsoft.Data.Sqlite.SqliteConnection _connection = new("DataSource=:memory:");

    public MigrationTests() => _connection.Open();

    [Fact]
    public async Task MigrationChain_AppliesAndCreatesTheSupportTablesWithUniqueIndexes()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options;
        await using var db = new ApplicationDbContext(options);

        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        var tables = await db.Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table'")
            .ToListAsync();
        Assert.Contains("Customers", tables);
        Assert.Contains("CustomerMembers", tables);
        Assert.Contains("CustomerGroups", tables);
        Assert.Contains("CustomerGroupedMessages", tables);
        Assert.Contains("AspNetUsers", tables);

        var uniqueIndexes = await db.Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'index' AND sql LIKE 'CREATE UNIQUE%'")
            .ToListAsync();
        Assert.Contains("IX_CustomerGroups_CustomerId_Priority", uniqueIndexes);
        Assert.Contains("IX_CustomerGroups_BaleId", uniqueIndexes);
        Assert.Contains("IX_CustomerMembers_BaleId", uniqueIndexes);
    }

    [Fact]
    public async Task MigratedSchema_EnforcesOneGroupPerPriorityAndCustomer()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options;
        await using var db = new ApplicationDbContext(options);
        await db.Database.MigrateAsync();

        db.Customers.Add(new Customer { Name = "A", Description = "" });
        await db.SaveChangesAsync();
        db.CustomerGroups.Add(new CustomerGroup { CustomerId = 1, Name = "g1", BaleId = 1, Priority = GroupPriority.Low });
        await db.SaveChangesAsync();

        db.CustomerGroups.Add(new CustomerGroup { CustomerId = 1, Name = "g2", BaleId = 2, Priority = GroupPriority.Low });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task MigrationChain_ConvertsExistingNumericMemberBaleIdsToText_AndKeepsTheUniqueIndex()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options;
        await using var db = new ApplicationDbContext(options);
        var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(db.Database);

        // The schema as it was before this change: BaleId is a number.
        await migrator.MigrateAsync("20261027090000_InitialCreate");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Customers (Name, Description) VALUES ('A', '')");
        await db.Database.ExecuteSqlRawAsync(
            "INSERT INTO CustomerMembers (Name, Description, PhoneNumber, BaleId, CustomerId) VALUES ('Sara', '', '0912', 4503599627370495, 1)");

        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var stored = await db.Database
            .SqlQueryRaw<string>("SELECT BaleId AS Value FROM CustomerMembers")
            .SingleAsync();
        Assert.Equal("4503599627370495", stored);

        var columnType = await db.Database
            .SqlQueryRaw<string>("SELECT type AS Value FROM pragma_table_info('CustomerMembers') WHERE name = 'BaleId'")
            .SingleAsync();
        Assert.Contains("TEXT", columnType, StringComparison.OrdinalIgnoreCase);

        // Text ids are accepted now, and the unique index still protects them.
        db.CustomerMembers.Add(new CustomerMember { CustomerId = 1, Name = "Text", Description = "", PhoneNumber = "", BaleId = "user_1" });
        await db.SaveChangesAsync();
        db.CustomerMembers.Add(new CustomerMember { CustomerId = 1, Name = "Dup", Description = "", PhoneNumber = "", BaleId = "user_1" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    public void Dispose() => _connection.Dispose();
}
