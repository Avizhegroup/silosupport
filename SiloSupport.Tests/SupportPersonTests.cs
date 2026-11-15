using System.ComponentModel.DataAnnotations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SiloSupport.Application.Dtos;
using SiloSupport.Application.Helpers;
using SiloSupport.Application.Interfaces;
using SiloSupport.Application.Services;
using SiloSupport.Domain.Entities;
using SiloSupport.Domain.Enums;
using SiloSupport.Persistence;
using SiloSupport.Tests.Support;

namespace SiloSupport.Tests;

/// <summary>Support persons (our own team) and the customer Bale chat id on SQLite in-memory.</summary>
public class SupportPersonTests : TestBase
{
    private const string Phone = "09121234567";

    private SupportPersonService Service => Get<SupportPersonService>();

    [Fact]
    public async Task Create_StoresTheUserNameNormalized_AndListsOrderedByName()
    {
        Assert.True((await Service.CreateAsync(new SupportPersonInput { Name = "Zahra", PhoneNumber = Phone, BaleUsername = "@Zahra_S" })).Succeeded);
        Assert.True((await Service.CreateAsync(new SupportPersonInput { Name = "Ali", PhoneNumber = "09351234567", BaleUsername = "ali" })).Succeeded);

        var list = await Service.GetListAsync();

        Assert.Equal(new[] { "Ali", "Zahra" }, list.Select(p => p.Name));
        Assert.Equal(new[] { "ali", "zahra_s" }, list.Select(p => p.BaleUsername));
    }

    [Theory]
    [InlineData("@ALI")]
    [InlineData("ali")]
    [InlineData(" @Ali ")]
    public async Task Create_RejectsADuplicateUserName_IgnoringCaseAndAtSign(string duplicate)
    {
        await Service.CreateAsync(new SupportPersonInput { Name = "Ali", PhoneNumber = Phone, BaleUsername = "ali" });

        var result = await Service.CreateAsync(new SupportPersonInput { Name = "Other", PhoneNumber = "09351234567", BaleUsername = duplicate });

        Assert.False(result.Succeeded);
        Assert.Contains("نام کاربری بله", result.FirstError);
    }

    [Fact]
    public async Task Create_RequiresNamePhoneAndUserName()
    {
        Assert.False((await Service.CreateAsync(new SupportPersonInput { Name = "", PhoneNumber = Phone, BaleUsername = "a" })).Succeeded);
        Assert.False((await Service.CreateAsync(new SupportPersonInput { Name = "x", PhoneNumber = "", BaleUsername = "a" })).Succeeded);
        Assert.False((await Service.CreateAsync(new SupportPersonInput { Name = "x", PhoneNumber = "12", BaleUsername = "a" })).Succeeded);
        Assert.False((await Service.CreateAsync(new SupportPersonInput { Name = "x", PhoneNumber = Phone, BaleUsername = "" })).Succeeded);
        Assert.False((await Service.CreateAsync(new SupportPersonInput { Name = "x", PhoneNumber = Phone, BaleUsername = "@" })).Succeeded);
        Assert.False((await Service.CreateAsync(new SupportPersonInput { Name = "x", PhoneNumber = Phone, BaleUsername = "a b" })).Succeeded);
        Assert.Empty(await Service.GetListAsync());
    }

    [Fact]
    public async Task Update_ChangesTheFields_AndMayKeepItsOwnUserName()
    {
        var id = (await Service.CreateAsync(new SupportPersonInput { Name = "Ali", PhoneNumber = Phone, BaleUsername = "ali" })).Value;

        var result = await Service.UpdateAsync(id, new SupportPersonInput { Name = "Ali R.", PhoneNumber = "09351234567", BaleUsername = "@Ali" });

        Assert.True(result.Succeeded);
        var loaded = await Service.GetAsync(id);
        Assert.Equal("Ali R.", loaded!.Name);
        Assert.Equal("09351234567", loaded.PhoneNumber);
        Assert.Equal("ali", loaded.BaleUsername);
    }

    [Fact]
    public async Task Update_CannotTakeAnotherSupportPersonsUserName()
    {
        await Service.CreateAsync(new SupportPersonInput { Name = "Ali", PhoneNumber = Phone, BaleUsername = "ali" });
        var otherId = (await Service.CreateAsync(new SupportPersonInput { Name = "Sara", PhoneNumber = "09351234567", BaleUsername = "sara" })).Value;

        var result = await Service.UpdateAsync(otherId, new SupportPersonInput { Name = "Sara", PhoneNumber = "09351234567", BaleUsername = "@ALI" });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task UpdateAndDelete_OfAMissingSupportPerson_Fail()
    {
        Assert.False((await Service.UpdateAsync(99, new SupportPersonInput { Name = "x", PhoneNumber = Phone, BaleUsername = "x" })).Succeeded);
        Assert.False((await Service.DeleteAsync(99)).Succeeded);
        Assert.Null(await Service.GetAsync(99));
    }

    [Fact]
    public async Task Delete_RemovesTheSupportPerson()
    {
        var id = (await Service.CreateAsync(new SupportPersonInput { Name = "Ali", PhoneNumber = Phone, BaleUsername = "ali" })).Value;

        Assert.True((await Service.DeleteAsync(id)).Succeeded);

        Assert.Empty(await Service.GetListAsync());
    }

    [Theory]
    [InlineData("ali", "@ALI")]
    [InlineData("ali", "ali")]
    public async Task Repository_FindsASupportPersonByUserName_IgnoringCaseAndAtSign(string stored, string incoming)
    {
        await Service.CreateAsync(new SupportPersonInput { Name = "Ali", PhoneNumber = Phone, BaleUsername = stored });

        var found = await Get<ISupportPersonRepository>().FindByUsernameAsync(incoming);

        Assert.Equal("Ali", found!.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("@")]
    [InlineData("nobody")]
    public async Task Repository_FindByUserName_ReturnsNullWhenThereIsNothingToMatch(string? incoming)
    {
        await Service.CreateAsync(new SupportPersonInput { Name = "Ali", PhoneNumber = Phone, BaleUsername = "ali" });

        Assert.Null(await Get<ISupportPersonRepository>().FindByUsernameAsync(incoming));
    }

    [Fact]
    public async Task Database_RejectsTwoSupportPersonsWithTheSameUserName()
    {
        Db.SupportPersons.Add(new SupportPerson { Name = "a", PhoneNumber = Phone, BaleUsername = "ali" });
        await Db.SaveChangesAsync();

        Db.SupportPersons.Add(new SupportPerson { Name = "b", PhoneNumber = Phone, BaleUsername = "ali" });
        await Assert.ThrowsAsync<DbUpdateException>(() => Db.SaveChangesAsync());
    }

    // ---- customer Bale chat id ----

    [Fact]
    public async Task Customer_BaleChatId_IsStoredAndFoundByChat()
    {
        var customers = Get<CustomerService>();
        var id = (await customers.CreateAsync(new CustomerInput { Name = "Acme", BaleChatId = 8888 })).Value;

        Assert.Equal(8888, (await customers.GetAsync(id))!.BaleChatId);
        Assert.Equal(id, (await Get<ICustomerRepository>().GetByBaleChatIdAsync(8888))!.Id);
        Assert.Null(await Get<ICustomerRepository>().GetByBaleChatIdAsync(1));
    }

    [Fact]
    public async Task Customer_BaleChatId_IsOptional_AndSeveralCustomersMayHaveNone()
    {
        var customers = Get<CustomerService>();

        Assert.True((await customers.CreateAsync(new CustomerInput { Name = "A" })).Succeeded);
        Assert.True((await customers.CreateAsync(new CustomerInput { Name = "B" })).Succeeded);
    }

    [Fact]
    public async Task Customer_BaleChatId_MustBeUnique()
    {
        var customers = Get<CustomerService>();
        await customers.CreateAsync(new CustomerInput { Name = "A", BaleChatId = 8888 });

        var duplicate = await customers.CreateAsync(new CustomerInput { Name = "B", BaleChatId = 8888 });

        Assert.False(duplicate.Succeeded);
        Assert.Contains("شناسه چت", duplicate.FirstError);
    }

    [Fact]
    public async Task Customer_BaleChatId_CannotBeAPriorityGroupChat()
    {
        var other = await AddCustomerAsync("Other");
        await AddGroupAsync(other.Id, GroupPriority.Low, 5001);

        var result = await Get<CustomerService>().CreateAsync(new CustomerInput { Name = "A", BaleChatId = 5001 });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Customer_Update_KeepsItsOwnChat_AndCanClearIt()
    {
        var customers = Get<CustomerService>();
        var id = (await customers.CreateAsync(new CustomerInput { Name = "A", BaleChatId = 8888 })).Value;

        Assert.True((await customers.UpdateAsync(id, new CustomerInput { Name = "A2", BaleChatId = 8888 })).Succeeded);
        Assert.True((await customers.UpdateAsync(id, new CustomerInput { Name = "A2", BaleChatId = null })).Succeeded);

        Assert.Null((await customers.GetAsync(id))!.BaleChatId);
    }

    [Fact]
    public async Task Customer_DetailsExposeTheChatId()
    {
        var id = (await Get<CustomerService>().CreateAsync(new CustomerInput { Name = "A", BaleChatId = 8888 })).Value;

        var details = await Get<CustomerService>().GetDetailsAsync(id);

        Assert.Equal(8888, details!.Customer.BaleChatId);
    }
}

public class SupportPersonInputValidationTests
{
    private static IList<ValidationResult> Validate(SupportPersonInput input)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(input, new ValidationContext(input), results, validateAllProperties: true);
        return results;
    }

    [Theory]
    [InlineData("ali")]
    [InlineData("@ali")]
    [InlineData("Ali_Support")]
    public void UserName_AcceptsNamesWithOrWithoutAtSign(string username)
    {
        Assert.Empty(Validate(new SupportPersonInput { Name = "Ali", PhoneNumber = "09121234567", BaleUsername = username }));
    }

    [Theory]
    [InlineData("@")]
    [InlineData("@@ali")]
    [InlineData("a li")]
    public void UserName_RejectsBadShapes(string username)
    {
        Assert.NotEmpty(Validate(new SupportPersonInput { Name = "Ali", PhoneNumber = "09121234567", BaleUsername = username }));
    }

    [Fact]
    public void RequiredFields_HavePersianMessages()
    {
        var errors = Validate(new SupportPersonInput());

        Assert.Contains(errors, e => e.ErrorMessage == "نام الزامی است.");
        Assert.Contains(errors, e => e.ErrorMessage == "شماره تلفن الزامی است.");
        Assert.Contains(errors, e => e.ErrorMessage == "نام کاربری بله الزامی است.");
    }
}

public class PhoneNumberNormalizerTests
{
    [Theory]
    [InlineData("09121234567", "9121234567")]
    [InlineData("+98 912 123 4567", "9121234567")]
    [InlineData("989121234567", "9121234567")]
    [InlineData("۰۹۱۲۱۲۳۴۵۶۷", "9121234567")]
    [InlineData("0912", "")]
    [InlineData(null, "")]
    public void Key_IsTheLastTenDigits(string? phone, string expected) =>
        Assert.Equal(expected, PhoneNumberNormalizer.Key(phone));

    [Fact]
    public void AreSame_ComparesAcrossFormats()
    {
        Assert.True(PhoneNumberNormalizer.AreSame("0912 123 4567", "+989121234567"));
        Assert.False(PhoneNumberNormalizer.AreSame("09121234567", "09121234568"));
        Assert.False(PhoneNumberNormalizer.AreSame("0912", "0912"));
    }
}

public class SupportPersonMigrationTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public SupportPersonMigrationTests() => _connection.Open();

    private ApplicationDbContext NewDb() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlite(_connection).Options);

    [Fact]
    public async Task MigrationChain_CreatesSupportPersonsAndTheCustomerChatColumn_WithUniqueIndexes()
    {
        await using var db = NewDb();
        await db.Database.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        var tables = await db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'table'").ToListAsync();
        Assert.Contains("SupportPersons", tables);

        var uniqueIndexes = await db.Database
            .SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'index' AND sql LIKE 'CREATE UNIQUE%'")
            .ToListAsync();
        Assert.Contains("IX_SupportPersons_BaleUsername", uniqueIndexes);
        Assert.Contains("IX_Customers_BaleChatId", uniqueIndexes);
        Assert.Contains("IX_CustomerMembers_BaleId", uniqueIndexes);
    }

    [Fact]
    public async Task MigratedSchema_AllowsManyCustomersWithoutChat_ButNotTwoWithTheSameChat()
    {
        await using var db = NewDb();
        await db.Database.MigrateAsync();

        db.Customers.Add(new Customer { Name = "A", Description = "" });
        db.Customers.Add(new Customer { Name = "B", Description = "" });
        await db.SaveChangesAsync();

        db.Customers.Add(new Customer { Name = "C", Description = "", BaleChatId = 5 });
        await db.SaveChangesAsync();
        db.Customers.Add(new Customer { Name = "D", Description = "", BaleChatId = 5 });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task MigratedSchema_AllowsManyMembersWithoutBaleId_ButNotTwoWithTheSameOne()
    {
        await using var db = NewDb();
        await db.Database.MigrateAsync();
        db.Customers.Add(new Customer { Name = "A", Description = "" });
        await db.SaveChangesAsync();

        db.CustomerMembers.Add(new CustomerMember { CustomerId = 1, Name = "a", Description = "", PhoneNumber = "09121234567", BaleId = null });
        db.CustomerMembers.Add(new CustomerMember { CustomerId = 1, Name = "b", Description = "", PhoneNumber = "09121234568", BaleId = null });
        await db.SaveChangesAsync();

        db.CustomerMembers.Add(new CustomerMember { CustomerId = 1, Name = "c", Description = "", PhoneNumber = "1", BaleId = "@x" });
        await db.SaveChangesAsync();
        db.CustomerMembers.Add(new CustomerMember { CustomerId = 1, Name = "d", Description = "", PhoneNumber = "1", BaleId = "@x" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task MigrationChain_KeepsExistingMembers_AndTurnsEmptyBaleIdsIntoNull()
    {
        await using var db = NewDb();
        var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(db.Database);

        await migrator.MigrateAsync("20261028100000_MemberBaleIdAsString");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Customers (Name, Description) VALUES ('A', '')");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO CustomerMembers (Name, Description, PhoneNumber, BaleId, CustomerId) VALUES ('Sara', '', '0912', '@sara', 1)");
        await db.Database.ExecuteSqlRawAsync("INSERT INTO CustomerMembers (Name, Description, PhoneNumber, BaleId, CustomerId) VALUES ('Empty', '', '0935', '', 1)");

        await db.Database.MigrateAsync();

        var rows = await db.CustomerMembers.AsNoTracking().OrderBy(m => m.Id).ToListAsync();
        Assert.Equal("@sara", rows[0].BaleId);
        Assert.Null(rows[1].BaleId);
        Assert.All(await db.Customers.ToListAsync(), c => Assert.Null(c.BaleChatId));
    }

    public void Dispose() => _connection.Dispose();
}
