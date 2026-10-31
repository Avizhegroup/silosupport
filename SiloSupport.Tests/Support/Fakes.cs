using Microsoft.Extensions.Logging;
using SiloSupport.Application.Dtos;
using SiloSupport.Application.Interfaces;
using SiloSupport.Domain.Entities;
using SiloSupport.Domain.Enums;

namespace SiloSupport.Tests.Support;

public sealed class FakeClock : TimeProvider
{
    private DateTimeOffset _now;

    public FakeClock(DateTimeOffset now) => _now = now;

    public void Advance(TimeSpan by) => _now += by;

    public override DateTimeOffset GetUtcNow() => _now;
}

/// <summary>Captures log entries so tests can assert that something was logged.</summary>
public sealed class ListLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        => Entries.Add((logLevel, formatter(state, exception)));

    public bool Has(LogLevel level, string contains) =>
        Entries.Any(e => e.Level == level && e.Message.Contains(contains, StringComparison.OrdinalIgnoreCase));
}

public sealed class FakeBaleClient : IBaleBotClient
{
    public long? BotUserId { get; set; } = 999;

    public bool SendResult { get; set; } = true;

    public bool ThrowOnSend { get; set; }

    public List<(long ChatId, string Text)> Sent { get; } = new();

    public Task<IReadOnlyList<BalePolledUpdate>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<BalePolledUpdate>>(Array.Empty<BalePolledUpdate>());

    public Task<long?> GetBotUserIdAsync(CancellationToken cancellationToken) => Task.FromResult(BotUserId);

    public Task<bool> SendMessageAsync(long chatId, string text, CancellationToken cancellationToken)
    {
        if (ThrowOnSend)
            throw new HttpRequestException("boom");

        if (SendResult)
            Sent.Add((chatId, text));

        return Task.FromResult(SendResult);
    }
}

public sealed class FakeClassifier : IMessagePriorityClassifier
{
    public PriorityClassificationResult Result { get; set; } = PriorityClassificationResult.Success(GroupPriority.Low, "analysis");

    public bool Throw { get; set; }

    public List<string> Calls { get; } = new();

    public Task<PriorityClassificationResult> ClassifyAsync(string customerMessage, CancellationToken cancellationToken)
    {
        Calls.Add(customerMessage);
        if (Throw)
            throw new InvalidOperationException("AI down");

        return Task.FromResult(Result);
    }
}

/// <summary>In-memory repositories that clone objects to mimic persistence boundaries.</summary>
public sealed class FakeStore
{
    public List<Customer> Customers { get; } = new();
    public List<CustomerMember> Members { get; } = new();
    public List<CustomerGroup> Groups { get; } = new();
    public List<CustomerGroupedMessage> Messages { get; } = new();
    public bool FailMessageInsert { get; set; }
}

public sealed class FakeMemberRepository : ICustomerMemberRepository
{
    private readonly FakeStore _store;

    public FakeMemberRepository(FakeStore store) => _store = store;

    public Task<IReadOnlyList<CustomerMember>> FindByBaleIdsAsync(IReadOnlyCollection<string> candidates, CancellationToken cancellationToken = default)
    {
        var lowered = candidates.Select(c => c.ToLowerInvariant()).ToHashSet();

        IReadOnlyList<CustomerMember> found = _store.Members
            .Where(m => lowered.Contains(m.BaleId.ToLowerInvariant()))
            .Select(member => new CustomerMember
            {
                Id = member.Id,
                Name = member.Name,
                Description = member.Description,
                PhoneNumber = member.PhoneNumber,
                BaleId = member.BaleId,
                CustomerId = member.CustomerId,
                Customer = _store.Customers.FirstOrDefault(c => c.Id == member.CustomerId)
            })
            .ToList();

        return Task.FromResult(found);
    }

    public Task<IReadOnlyList<CustomerMember>> GetByCustomerAsync(int customerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<CustomerMember?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> BaleIdExistsAsync(string baleId, int? excludeMemberId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task AddAsync(CustomerMember member, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> UpdateAsync(CustomerMember member, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

public sealed class FakeGroupRepository : ICustomerGroupRepository
{
    private readonly FakeStore _store;

    public FakeGroupRepository(FakeStore store) => _store = store;

    public Task<bool> BaleIdExistsAsync(long baleId, int? excludeGroupId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_store.Groups.Any(g => g.BaleId == baleId && (excludeGroupId is null || g.Id != excludeGroupId)));

    public Task<CustomerGroup?> GetByCustomerAndPriorityAsync(int customerId, GroupPriority priority, CancellationToken cancellationToken = default) =>
        Task.FromResult(_store.Groups
            .Where(g => g.CustomerId == customerId && g.Priority == priority)
            .Select(g => new CustomerGroup { Id = g.Id, Name = g.Name, BaleId = g.BaleId, CustomerId = g.CustomerId, Priority = g.Priority })
            .FirstOrDefault());

    public Task<IReadOnlyList<CustomerGroup>> GetByCustomerAsync(int customerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<CustomerGroup?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> PriorityTakenAsync(int customerId, GroupPriority priority, int? excludeGroupId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> HasMessagesAsync(int groupId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task AddAsync(CustomerGroup group, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> UpdateAsync(CustomerGroup group, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

public sealed class FakeGroupedMessageRepository : ICustomerGroupedMessageRepository
{
    private readonly FakeStore _store;

    public FakeGroupedMessageRepository(FakeStore store) => _store = store;

    public Task AddAsync(CustomerGroupedMessage message, CancellationToken cancellationToken = default)
    {
        if (_store.FailMessageInsert)
            throw new InvalidOperationException("db down");

        _store.Messages.Add(new CustomerGroupedMessage
        {
            Id = _store.Messages.Count + 1,
            GroupId = message.GroupId,
            Message = message.Message,
            IsResolved = message.IsResolved,
            CreatedAtUtc = message.CreatedAtUtc
        });
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GroupedMessageDto>> GetByCustomerAsync(int customerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<MessageCountRow>> GetCountsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<MessageTimeRow>> GetSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> SetResolvedAsync(int customerId, int messageId, bool isResolved, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
