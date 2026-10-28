using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SiloSupport.Application.Interfaces;
using SiloSupport.Application.Services;
using SiloSupport.Domain.Entities;
using SiloSupport.Domain.Enums;
using SiloSupport.Persistence;
using SiloSupport.Persistence.Repositories;

namespace SiloSupport.Tests.Support;

/// <summary>
/// Builds the real repositories/services on SQLite in-memory (fresh database per test instance). One scope is shared
/// so every repository uses the same DbContext, exactly as in a request/circuit scope.
/// </summary>
public abstract class TestBase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    protected FakeClock Clock { get; } = new(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));

    protected TestBase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlite(_connection));
        services.AddSingleton<TimeProvider>(Clock);
        services.AddScoped<ITransactionRunner, EfTransactionRunner>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<ICustomerMemberRepository, CustomerMemberRepository>();
        services.AddScoped<ICustomerGroupRepository, CustomerGroupRepository>();
        services.AddScoped<ICustomerGroupedMessageRepository, CustomerGroupedMessageRepository>();
        services.AddScoped<CustomerService>();
        services.AddScoped<CustomerMemberService>();
        services.AddScoped<CustomerGroupService>();
        services.AddScoped<ConversationService>();

        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateScope();

        Db.Database.EnsureCreated();
    }

    protected IServiceProvider Services => _scope.ServiceProvider;

    protected ApplicationDbContext Db => Services.GetRequiredService<ApplicationDbContext>();

    protected T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    protected async Task<Customer> AddCustomerAsync(string name = "Acme")
    {
        var customer = new Customer { Name = name, Description = "test" };
        await Get<ICustomerRepository>().AddAsync(customer);
        return customer;
    }

    protected async Task<CustomerGroup> AddGroupAsync(int customerId, GroupPriority priority, long baleId, string? name = null)
    {
        var group = new CustomerGroup
        {
            CustomerId = customerId,
            Priority = priority,
            BaleId = baleId,
            Name = name ?? $"{priority} group"
        };
        await Get<ICustomerGroupRepository>().AddAsync(group);
        return group;
    }

    protected async Task<CustomerMember> AddMemberAsync(int customerId, long baleId, string name = "Member")
    {
        var member = new CustomerMember { CustomerId = customerId, BaleId = baleId, Name = name, Description = "", PhoneNumber = "0912" };
        await Get<ICustomerMemberRepository>().AddAsync(member);
        return member;
    }

    protected async Task<CustomerGroupedMessage> AddMessageAsync(int groupId, string text, DateTimeOffset? at = null)
    {
        var message = new CustomerGroupedMessage
        {
            GroupId = groupId,
            Message = text,
            CreatedAtUtc = (at ?? Clock.GetUtcNow()).UtcDateTime
        };
        await Get<ICustomerGroupedMessageRepository>().AddAsync(message);
        return message;
    }

    public virtual void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
        _connection.Dispose();
    }
}
