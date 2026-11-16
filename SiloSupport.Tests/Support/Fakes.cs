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

    /// <summary>Files re-sent by file id (sendPhoto, sendVideo, ...), with the caption.</summary>
    public List<(long ChatId, AttachmentKind Kind, string FileId, string? Caption)> SentAttachments { get; } = new();

    public bool SendAttachmentResult { get; set; } = true;

    /// <summary>What <see cref="DownloadFileAsync"/> returns; null simulates an unavailable or too-large file.</summary>
    public BaleDownloadedFile? Download { get; set; } = new(new byte[] { 1, 2, 3 }, "photos/file_1.jpg");

    public List<string> Downloads { get; } = new();

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

    public Task<bool> SendAttachmentAsync(long chatId, AttachmentKind kind, string fileId, string? caption, CancellationToken cancellationToken)
    {
        if (ThrowOnSend)
            throw new HttpRequestException("boom");

        if (SendAttachmentResult)
            SentAttachments.Add((chatId, kind, fileId, caption));

        return Task.FromResult(SendAttachmentResult);
    }

    public Task<BaleDownloadedFile?> DownloadFileAsync(string fileId, long maxBytes, CancellationToken cancellationToken)
    {
        Downloads.Add(fileId);
        return Task.FromResult(Download);
    }
}

public sealed class FakeClassifier : IMessagePriorityClassifier
{
    public PriorityClassificationResult Result { get; set; } = PriorityClassificationResult.Success(GroupPriority.Low, "analysis");

    public bool Throw { get; set; }

    public List<string> Calls { get; } = new();

    /// <summary>The open issues offered to the AI on each call.</summary>
    public List<IReadOnlyList<ThreadContextDto>> ThreadContexts { get; } = new();

    public Task<PriorityClassificationResult> ClassifyAsync(string customerMessage, IReadOnlyList<ThreadContextDto> openThreads, CancellationToken cancellationToken)
    {
        Calls.Add(customerMessage);
        ThreadContexts.Add(openThreads);
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
    public List<SupportPerson> SupportPersons { get; } = new();
    public List<CustomerGroup> Groups { get; } = new();
    public List<CustomerGroupedMessage> Messages { get; } = new();
    public List<SupportThread> Threads { get; } = new();
    public List<MessageAttachment> Attachments { get; } = new();
    public bool FailMessageInsert { get; set; }
}

public sealed class FakeMemberRepository : ICustomerMemberRepository
{
    private readonly FakeStore _store;

    public FakeMemberRepository(FakeStore store) => _store = store;

    public Task<IReadOnlyList<CustomerMember>> FindBySenderAsync(long? senderId, string? username, CancellationToken cancellationToken = default)
    {
        var lowered = SiloSupport.Application.Helpers.BaleIdentity.CandidatesFor(senderId, username)
            .Select(c => c.ToLowerInvariant())
            .ToHashSet();

        IReadOnlyList<CustomerMember> found = _store.Members
            .Where(m => m.BaleId is not null && lowered.Contains(m.BaleId.ToLowerInvariant()))
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

public sealed class FakeCustomerRepository : ICustomerRepository
{
    private readonly FakeStore _store;

    public FakeCustomerRepository(FakeStore store) => _store = store;

    public Task<Customer?> GetByBaleChatIdAsync(long baleChatId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_store.Customers
            .Where(c => c.BaleChatId == baleChatId)
            .Select(c => new Customer { Id = c.Id, Name = c.Name, Description = c.Description, BaleChatId = c.BaleChatId })
            .FirstOrDefault());

    public Task<IReadOnlyList<CustomerSummary>> GetSummariesAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<Customer?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task AddAsync(Customer customer, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> BaleChatIdExistsAsync(long baleChatId, int? excludeCustomerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> UpdateAsync(Customer customer, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> HasMessageHistoryAsync(int customerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

public sealed class FakeSupportPersonRepository : ISupportPersonRepository
{
    private readonly FakeStore _store;

    public FakeSupportPersonRepository(FakeStore store) => _store = store;

    public Task<SupportPerson?> FindByUsernameAsync(string? username, CancellationToken cancellationToken = default)
    {
        var normalized = SiloSupport.Application.Helpers.BaleIdentity.Normalize(username);
        return Task.FromResult(normalized.Length == 0
            ? null
            : _store.SupportPersons.FirstOrDefault(s => s.BaleUsername == normalized));
    }

    public Task<IReadOnlyList<SupportPerson>> GetAllAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<SupportPerson?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> UsernameExistsAsync(string username, int? excludeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task AddAsync(SupportPerson person, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> UpdateAsync(SupportPerson person, CancellationToken cancellationToken = default) => throw new NotSupportedException();
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

        var id = _store.Messages.Count + 1;
        var threadId = message.ThreadId;
        if (message.Thread is not null)
        {
            var thread = new SupportThread
            {
                Id = _store.Threads.Count + 1,
                CustomerId = message.Thread.CustomerId,
                Title = message.Thread.Title,
                Summary = message.Thread.Summary,
                CreatedAtUtc = message.Thread.CreatedAtUtc,
                LastActivityUtc = message.Thread.LastActivityUtc
            };
            _store.Threads.Add(thread);
            threadId = thread.Id;
        }
        else if (threadId is not null)
        {
            var thread = _store.Threads.First(t => t.Id == threadId);
            thread.LastActivityUtc = message.CreatedAtUtc;
        }

        _store.Messages.Add(new CustomerGroupedMessage
        {
            Id = id,
            GroupId = message.GroupId,
            Message = message.Message,
            OriginalText = message.OriginalText,
            ThreadId = threadId,
            IsResolved = message.IsResolved,
            CreatedAtUtc = message.CreatedAtUtc
        });

        if (message.Attachment is { } a)
        {
            _store.Attachments.Add(new MessageAttachment
            {
                Id = _store.Attachments.Count + 1,
                GroupedMessageId = id,
                Kind = a.Kind,
                FileName = a.FileName,
                MimeType = a.MimeType,
                SizeBytes = a.SizeBytes,
                BaleFileId = a.BaleFileId,
                BaleFileUniqueId = a.BaleFileUniqueId,
                Content = a.Content
            });
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<GroupedMessageDto>> GetByCustomerAsync(int customerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<MessageCountRow>> GetCountsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<IReadOnlyList<MessageTimeRow>> GetSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> SetResolvedAsync(int customerId, int messageId, bool isResolved, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<bool> SetThreadResolvedAsync(int customerId, int threadId, bool isResolved, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    public Task<AttachmentContentDto?> GetAttachmentAsync(int attachmentId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}

/// <summary>In-memory thread repository mirroring the EF implementation: every open thread (unresolved message), no age or count limit.</summary>
public sealed class FakeThreadRepository : ISupportThreadRepository
{
    private readonly FakeStore _store;

    public FakeThreadRepository(FakeStore store) => _store = store;

    public Task<IReadOnlyList<ThreadContextDto>> GetOpenContextAsync(int customerId, int recentMessagesPerThread, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ThreadContextDto> result = _store.Threads
            .Where(t => t.CustomerId == customerId && _store.Messages.Any(m => m.ThreadId == t.Id && !m.IsResolved))
            .OrderByDescending(t => t.LastActivityUtc).ThenByDescending(t => t.Id)
            .Select(t =>
            {
                var own = _store.Messages.Where(m => m.ThreadId == t.Id).ToList();
                return new ThreadContextDto(
                    t.Id,
                    t.Title,
                    t.Summary,
                    t.LastActivityUtc,
                    own.Count,
                    own.Max(m => _store.Groups.First(g => g.Id == m.GroupId).Priority),
                    own.Where(m => !string.IsNullOrWhiteSpace(m.OriginalText))
                        .OrderByDescending(m => m.CreatedAtUtc).ThenByDescending(m => m.Id)
                        .Take(recentMessagesPerThread)
                        .OrderBy(m => m.CreatedAtUtc).ThenBy(m => m.Id)
                        .Select(m => new ThreadContextMessageDto(m.OriginalText!, _store.Groups.First(g => g.Id == m.GroupId).Priority, m.CreatedAtUtc))
                        .ToList());
            })
            .ToList();
        return Task.FromResult(result);
    }

    public Task UpdateSummaryAsync(int threadId, string summary, CancellationToken cancellationToken = default)
    {
        _store.Threads.First(t => t.Id == threadId).Summary = summary;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SupportThread>> GetByCustomerAsync(int customerId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
